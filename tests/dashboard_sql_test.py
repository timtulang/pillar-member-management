"""Test dashboard queries against a disposable PostgreSQL database without networking.
Requires initdb and postgres on PATH. Never reads appsettings or connects to Neon.
"""
import re
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def source_query(path, expression):
    text = (ROOT / path).read_text()
    match = re.search(expression + r'\s*"""(.*?)"""', text, re.S)
    assert match, f"Query not found in {path}"
    return match.group(1).strip()


demographics = source_query("Models/DemographicSummary.cs", r'Query\s*=')
demographics = demographics.replace("@zone", "'Asia/Manila'")
serving = source_query("Pages/Ministries/Index.cshtml.cs", r'Serving\s*=\s*await db.Read\(')
enrollments = source_query("Pages/Ministries/Index.cshtml.cs", r'Enrollments\s*=\s*await db.Read\(')
events = source_query("Pages/Events/Index.cshtml.cs", r'Events\s*=\s*await db.Read\(')

fixture = """
BEGIN;
INSERT INTO pillar.people(id,first_name,surname,gender,birth_date,congregation_status,reported_civil_status,archived_at,deceased_on)
SELECT md5('fixture-'||n)::uuid,'Person '||n,'Synthetic',gender,birthday,status::pillar.congregation_status,civil::pillar.civil_status,archived,dead
FROM (VALUES
 (1,'Female',(now() AT TIME ZONE 'Asia/Manila')::date-interval '30 years','member','married',NULL::timestamptz,NULL::date),
 (2,'Male',(now() AT TIME ZONE 'Asia/Manila')::date-interval '17 years','regular_attendee','single',NULL,NULL),
 (3,'Non-binary',(now() AT TIME ZONE 'Asia/Manila')::date-interval '12 years','visitor','unknown',NULL,NULL),
 (4,NULL,NULL,'visitor','unknown',NULL,NULL),
 (5,'Legacy description',(now() AT TIME ZONE 'Asia/Manila')::date-interval '65 years','inactive','other',NULL,NULL),
 (6,'Female',CURRENT_DATE-interval '30 years','member','married',now(),NULL),
 (7,'Male',CURRENT_DATE-interval '60 years','member','married',NULL,CURRENT_DATE)
) AS data(n,gender,birthday,status,civil,archived,dead);
INSERT INTO pillar.ministries(id,name,is_active) VALUES
 (md5('ministry-a')::uuid,'Fixture active',true),(md5('ministry-b')::uuid,'Fixture inactive',false);
INSERT INTO pillar.ministry_assignments(person_id,ministry_id,valid_from,valid_to) VALUES
 (md5('fixture-1')::uuid,md5('ministry-a')::uuid,CURRENT_DATE-10,NULL),
 (md5('fixture-2')::uuid,md5('ministry-a')::uuid,CURRENT_DATE,NULL),
 (md5('fixture-3')::uuid,md5('ministry-a')::uuid,CURRENT_DATE+1,NULL),
 (md5('fixture-4')::uuid,md5('ministry-a')::uuid,CURRENT_DATE-10,CURRENT_DATE),
 (md5('fixture-5')::uuid,md5('ministry-b')::uuid,CURRENT_DATE-10,NULL),
 (md5('fixture-6')::uuid,md5('ministry-a')::uuid,CURRENT_DATE-10,NULL),
 (md5('fixture-7')::uuid,md5('ministry-a')::uuid,CURRENT_DATE-10,NULL);
INSERT INTO pillar.discipleship_programs(id,name,is_active) VALUES
 (md5('program-a')::uuid,'Fixture program',true),(md5('program-b')::uuid,'Fixture inactive program',false);
INSERT INTO pillar.discipleship_enrollments(person_id,program_id,status)
SELECT md5('fixture-'||n)::uuid,md5('program-a')::uuid,status::pillar.discipleship_status
FROM (VALUES (1,'in_progress'),(2,'completed'),(3,'paused'),(4,'reported_participation'),(6,'in_progress'),(7,'completed')) AS data(n,status);
INSERT INTO pillar.discipleship_enrollments(person_id,program_id,status) VALUES (md5('fixture-5')::uuid,md5('program-b')::uuid,'in_progress');
INSERT INTO pillar.app_users(id,identity_issuer,identity_subject) VALUES(md5('actor')::uuid,'test','synthetic');
INSERT INTO pillar.event_occurrences(id,title,starts_at,ends_at,time_zone,check_in_opens_at,check_in_closes_at,created_by_user_id,cancelled_at)
SELECT md5(title)::uuid,title,now()+offset_days*interval '1 day',now()+offset_days*interval '1 day'+interval '1 hour','UTC',now()+offset_days*interval '1 day'-interval '1 hour',now()+offset_days*interval '1 day'+interval '2 hours',md5('actor')::uuid,cancelled
FROM (VALUES ('Past',-1,NULL::timestamptz),('Near',1,NULL),('Later',3,NULL),('Cancelled',2,now())) AS data(title,offset_days,cancelled);
"""
checks = f"""
CREATE TEMP TABLE demo AS {demographics};
CREATE TEMP TABLE serving AS {serving};
CREATE TEMP TABLE enrollments AS {enrollments};
CREATE TEMP TABLE event_list AS SELECT row_number() OVER () position, listing.* FROM ({events}) listing;
DO $$ BEGIN
 IF (SELECT count(*) FROM (SELECT dimension FROM demo GROUP BY dimension HAVING sum(people)=5) totals)<>4 THEN RAISE EXCEPTION 'Each demographic must include exactly five eligible profiles'; END IF;
 IF (SELECT people FROM demo WHERE dimension='age' AND category='13_17') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Teen age boundary incorrect'; END IF;
 IF (SELECT people FROM demo WHERE dimension='age' AND category='0_12') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Child age boundary incorrect'; END IF;
 IF (SELECT people FROM demo WHERE dimension='age' AND category='65_plus') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Senior age boundary incorrect'; END IF;
 IF (SELECT people FROM demo WHERE dimension='age' AND category='not_provided') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Missing birthdays must be included'; END IF;
 IF (SELECT people FROM demo WHERE dimension='gender' AND category='female') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Case normalization or exclusions failed'; END IF;
 IF (SELECT people FROM demo WHERE dimension='gender' AND category='non_binary') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Legacy hyphen normalization failed'; END IF;
 IF (SELECT people FROM demo WHERE dimension='gender' AND category='other_recorded') IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'Legacy values disappeared'; END IF;
 IF (SELECT count(*) FROM serving)<>2 THEN RAISE EXCEPTION 'Current roster must exclude future, ended, archived, deceased, and inactive records'; END IF;
 IF (SELECT count(*) FROM enrollments)<>4 THEN RAISE EXCEPTION 'Program roster must retain all progress statuses for eligible profiles'; END IF;
 IF (SELECT count(*) FROM enrollments WHERE status IN ('in_progress','reported_participation'))<>2 THEN RAISE EXCEPTION 'Learning count incorrect'; END IF;
 IF (SELECT title FROM event_list ORDER BY position LIMIT 1)<>'Near' THEN RAISE EXCEPTION 'Nearest upcoming event should be first'; END IF;
END $$;
ROLLBACK;
"""

with tempfile.TemporaryDirectory(prefix="pillar-dashboard-test-") as tmp:
    subprocess.run(["initdb", "-D", tmp + "/db", "--no-locale", "-E", "UTF8", "-A", "trust"], check=True, capture_output=True)
    for label, sql in [("Schema", (ROOT / "database/neon-setup.sql").read_text()), ("Queries", fixture + checks)]:
        result = subprocess.run(["postgres", "--single", "-D", tmp + "/db", "-j", "postgres"], input=sql + "\n\n", text=True, capture_output=True)
        if result.returncode or re.search(r'\b(ERROR|FATAL|PANIC):', result.stderr):
            raise RuntimeError(f"{label} failed:\n{result.stderr}")
print("PASS demographic totals, age boundaries, legacy values, roster eligibility, progress counts, and upcoming event ordering.")
