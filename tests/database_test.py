"""PostgreSQL invariants and concurrent-write tests; disposable local DB only."""
import os
import uuid
from concurrent.futures import ThreadPoolExecutor
from threading import Barrier

import psycopg
from psycopg.conninfo import conninfo_to_dict

DSN = os.environ["PILLAR_TEST_DATABASE"]
assert conninfo_to_dict(DSN).get("host") in ("127.0.0.1", "localhost"), "Use a disposable local database."


def expected(c, sql, args, state):
    try:
        with c.transaction():
            c.execute(sql, args)
    except psycopg.Error as error:
        assert error.sqlstate == state, f"Expected {state}, received {error.sqlstate}"
        return
    raise AssertionError(f"Expected PostgreSQL rejection {state}")


def person(c):
    return c.execute("INSERT INTO pillar.people(first_name,surname) VALUES('Constraint','Synthetic') RETURNING id", ()).fetchone()[0]


def household(c):
    return c.execute("INSERT INTO pillar.households(display_name) VALUES('Synthetic test household') RETURNING id").fetchone()[0]


def main():
    with psycopg.connect(DSN) as c:
        a, b, child = person(c), person(c), person(c)
        h1, h2 = household(c), household(c)
        c.execute("INSERT INTO pillar.household_memberships(person_id,household_id,valid_from) VALUES(%s,%s,'2020-01-01')", (child, h1))
        expected(c, "INSERT INTO pillar.household_memberships(person_id,household_id,valid_from) VALUES(%s,%s,'2021-01-01')", (child, h2), "23P01")
        c.execute("UPDATE pillar.household_memberships SET valid_to='2022-01-01' WHERE person_id=%s", (child,))
        c.execute("INSERT INTO pillar.household_memberships(person_id,household_id,valid_from) VALUES(%s,%s,'2022-01-01')", (child, h2))
        c.execute("INSERT INTO pillar.parent_child_relationships(parent_id,child_id,kind) VALUES(%s,%s,'biological')", (a, b))
        c.execute("INSERT INTO pillar.parent_child_relationships(parent_id,child_id,kind) VALUES(%s,%s,'biological')", (b, child))
        expected(c, "INSERT INTO pillar.parent_child_relationships(parent_id,child_id,kind) VALUES(%s,%s,'adoptive')", (child, a), "23514")
        expected(c, "INSERT INTO pillar.parent_child_relationships(parent_id,child_id,kind) VALUES(%s,%s,'biological')", (a, a), "23514")
        c.execute("INSERT INTO pillar.marriages(spouse_a_id,spouse_b_id,married_on) VALUES(LEAST(%s::uuid,%s::uuid),GREATEST(%s::uuid,%s::uuid),'2020-01-01')", (a, b, a, b))
        expected(c, "INSERT INTO pillar.marriages(spouse_a_id,spouse_b_id,married_on) VALUES(LEAST(%s::uuid,%s::uuid),GREATEST(%s::uuid,%s::uuid),'2021-01-01')", (b, child, b, child), "23514")
        ministry = c.execute("SELECT id FROM pillar.ministries ORDER BY name LIMIT 2").fetchall()
        c.execute("INSERT INTO pillar.ministry_assignments(person_id,ministry_id,is_main) VALUES(%s,%s,true)", (a, ministry[0][0]))
        expected(c, "INSERT INTO pillar.ministry_assignments(person_id,ministry_id,is_main) VALUES(%s,%s,true)", (a, ministry[1][0]), "23P01")
        actor = c.execute("SELECT id FROM pillar.app_users LIMIT 1").fetchone()[0]
        card = c.execute("INSERT INTO pillar.member_cards(person_id,token_digest,issued_by_user_id,expires_at) VALUES(%s,%s,%s,now()+interval '1 day') RETURNING id", (a, os.urandom(32), actor)).fetchone()[0]
        expected(c, "INSERT INTO pillar.member_cards(person_id,token_digest,issued_by_user_id,expires_at) VALUES(%s,%s,%s,now()+interval '1 day')", (a, os.urandom(32), actor), "23505")
        event = c.execute("""INSERT INTO pillar.event_occurrences(title,starts_at,ends_at,time_zone,check_in_opens_at,check_in_closes_at,created_by_user_id)
            VALUES('Constraint test',now(),now()+interval '1 hour','UTC',now()-interval '1 hour',now()+interval '2 hours',%s) RETURNING id""", (actor,)).fetchone()[0]
        expected(c, "INSERT INTO pillar.attendance_records(event_occurrence_id,person_id,member_card_id,recorded_by_user_id,method) VALUES(%s,%s,%s,%s,'qr_scan')", (event, b, card, actor), "23503")
        expected(c, "INSERT INTO pillar.attendance_records(event_occurrence_id,person_id,recorded_by_user_id,method) VALUES(%s,%s,%s,'manual')", (event, b, actor), "23514")
        c.execute("INSERT INTO pillar.attendance_records(event_occurrence_id,person_id,recorded_by_user_id,method,manual_reason) VALUES(%s,%s,%s,'manual','Synthetic')", (event, b, actor))
        expected(c, "INSERT INTO pillar.attendance_records(event_occurrence_id,person_id,recorded_by_user_id,method,manual_reason) VALUES(%s,%s,%s,'manual','Synthetic')", (event, b, actor), "23505")
        audit = c.execute("INSERT INTO pillar.audit_events(system_actor,action,entity_type,entity_id,correlation_id) VALUES('test','test','people',%s,%s) RETURNING id", (a, uuid.uuid4())).fetchone()[0]
        expected(c, "DELETE FROM pillar.audit_events WHERE id=%s", (audit,), "42501")
        expected(c, "UPDATE pillar.audit_events SET action='tamper' WHERE id=%s", (audit,), "42501")
        expected(c, "CREATE TABLE pillar.should_be_denied(id int)", (), "42501")
        c.rollback()
    print("PASS temporal periods, ancestry cycles, spouse overlap, card ownership, duplicates, and restricted DB grants", flush=True)

    # Test concurrent graph writes directly, exercising the trigger's locking too.
    with psycopg.connect(DSN) as c:
        a, b, d = person(c), person(c), person(c)
    barrier = Barrier(2)

    def add_parent(pair):
        try:
            with psycopg.connect(DSN) as c:
                barrier.wait(timeout=10)
                c.execute("INSERT INTO pillar.parent_child_relationships(parent_id,child_id,kind) VALUES(%s,%s,'biological')", pair)
            return "ok"
        except psycopg.Error as error:
            return error.sqlstate

    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(add_parent, [(a, b), (b, a)]))
    assert sorted(results) == ["23514", "ok"], f"Concurrent ancestry check failed: {results}"
    barrier = Barrier(2)

    def add_marriage(pair):
        try:
            with psycopg.connect(DSN) as c:
                barrier.wait(timeout=10)
                c.execute("INSERT INTO pillar.marriages(spouse_a_id,spouse_b_id,married_on) VALUES(LEAST(%s::uuid,%s::uuid),GREATEST(%s::uuid,%s::uuid),'2020-01-01')", (*pair, *pair))
            return "ok"
        except psycopg.Error as error:
            return error.sqlstate

    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(add_marriage, [(a, b), (b, d)]))
    assert sorted(results) == ["23514", "ok"], f"Concurrent marriage check failed: {results}"
    with psycopg.connect(DSN) as c:
        c.execute("DELETE FROM pillar.parent_child_relationships WHERE parent_id=ANY(%s)", ([a, b, d],))
        c.execute("DELETE FROM pillar.marriages WHERE spouse_a_id=ANY(%s)", ([a, b, d],))
        c.execute("DELETE FROM pillar.people WHERE id=ANY(%s)", ([a, b, d],))
    print("PASS concurrent opposing-parent and shared-spouse writes", flush=True)


if __name__ == "__main__":
    main()
