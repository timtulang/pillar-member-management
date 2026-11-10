-- Included inside 001_members.sql, before COMMIT.
CREATE SEQUENCE pillar.member_code_seq;
CREATE FUNCTION pillar.next_member_code() RETURNS text LANGUAGE sql VOLATILE
SET search_path = pg_catalog AS $$
  SELECT 'PF-' || lpad(n, greatest(6,length(n)), '0')
  FROM (SELECT nextval('pillar.member_code_seq')::text n) s;
$$;
ALTER TABLE pillar.people ALTER COLUMN member_code SET DEFAULT pillar.next_member_code();
ALTER TABLE pillar.people ADD COLUMN version bigint NOT NULL DEFAULT 1;
ALTER TABLE pillar.people ADD CONSTRAINT nonblank_names CHECK (length(trim(first_name))>0 AND length(trim(surname))>0);
CREATE FUNCTION pillar.touch_person() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
BEGIN NEW.updated_at=clock_timestamp(); NEW.version=OLD.version+1; RETURN NEW; END $$;
CREATE TRIGGER people_updated BEFORE UPDATE ON pillar.people FOR EACH ROW EXECUTE FUNCTION pillar.touch_person();

ALTER TABLE pillar.household_memberships ADD CONSTRAINT household_membership_no_overlap
EXCLUDE USING gist (person_id WITH =, household_id WITH =, daterange(valid_from,valid_to,'[)') WITH &&);
ALTER TABLE pillar.household_memberships ADD CONSTRAINT one_primary_household
EXCLUDE USING gist (person_id WITH =, daterange(valid_from,valid_to,'[)') WITH &&) WHERE (is_primary);
ALTER TABLE pillar.parent_child_relationships ADD CONSTRAINT parentage_no_duplicate_period
EXCLUDE USING gist (parent_id WITH =,child_id WITH =,kind WITH =,daterange(valid_from,valid_to,'[)') WITH &&);
ALTER TABLE pillar.ministry_assignments ADD CONSTRAINT ministry_no_duplicate_period
EXCLUDE USING gist (person_id WITH =,ministry_id WITH =,daterange(valid_from,valid_to,'[)') WITH &&);
ALTER TABLE pillar.ministry_assignments ADD CONSTRAINT one_main_ministry
EXCLUDE USING gist (person_id WITH =,daterange(valid_from,valid_to,'[)') WITH &&) WHERE (is_main);
ALTER TABLE pillar.marriages ADD CONSTRAINT marriage_no_duplicate_period
EXCLUDE USING gist (spouse_a_id WITH =,spouse_b_id WITH =,daterange(married_on,ended_on,'[)') WITH &&);
CREATE UNIQUE INDEX one_unrevoked_card ON pillar.member_cards(person_id) WHERE revoked_at IS NULL;

CREATE FUNCTION pillar.validate_parentage() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(3402026);
  IF NEW.parent_id=NEW.child_id THEN RAISE EXCEPTION 'Self-parentage is not allowed' USING ERRCODE='23514'; END IF;
  IF EXISTS (
    WITH RECURSIVE descendants(id) AS (
      SELECT NEW.child_id
      UNION
      SELECT p.child_id FROM pillar.parent_child_relationships p JOIN descendants d ON p.parent_id=d.id WHERE p.id<>NEW.id
    ) SELECT 1 FROM descendants WHERE id=NEW.parent_id
  ) THEN RAISE EXCEPTION 'Ancestry cycles are not allowed' USING ERRCODE='23514'; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER parentage_valid BEFORE INSERT OR UPDATE ON pillar.parent_child_relationships
FOR EACH ROW EXECUTE FUNCTION pillar.validate_parentage();

-- V1 policy: one ongoing marriage per person, across either spouse column.
-- Unknown dates are unbounded and may need staff review before a second historical marriage is entered.
CREATE FUNCTION pillar.validate_marriage() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(3402026);
  IF EXISTS(SELECT 1 FROM pillar.marriages m WHERE m.id<>NEW.id
    AND (m.spouse_a_id IN (NEW.spouse_a_id,NEW.spouse_b_id) OR m.spouse_b_id IN (NEW.spouse_a_id,NEW.spouse_b_id))
    AND daterange(m.married_on,m.ended_on,'[)') && daterange(NEW.married_on,NEW.ended_on,'[)'))
  THEN RAISE EXCEPTION 'Marriage periods for a spouse cannot overlap' USING ERRCODE='23514'; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER marriage_valid BEFORE INSERT OR UPDATE ON pillar.marriages
FOR EACH ROW EXECUTE FUNCTION pillar.validate_marriage();

CREATE FUNCTION pillar.immutable_audit() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
BEGIN RAISE EXCEPTION 'Audit events are append-only'; END $$;
CREATE TRIGGER audit_immutable BEFORE UPDATE OR DELETE ON pillar.audit_events
FOR EACH ROW EXECUTE FUNCTION pillar.immutable_audit();
ALTER TABLE pillar.audit_events ADD CONSTRAINT audit_fields_array CHECK(jsonb_typeof(changed_fields)='array');

INSERT INTO pillar.ministries(name) VALUES ('IT'),('Music'),('Dance'),('Media'),('Administrative & Service'),('Youth / Young Pro'),('Children');
INSERT INTO pillar.ministry_roles(name) VALUES ('Member'),('Coordinator'),('Assistant Coordinator');
INSERT INTO pillar.discipleship_programs(name) VALUES ('G.R.O.W.T.H.'),('Pillar Discipleship Guide');
COMMENT ON TABLE pillar.app_users IS 'Application staff profile mapped to ASP.NET Core Identity through identity_subject. No public registration.';

ALTER TABLE pillar.attendance_records ADD COLUMN last_correction_reason varchar(300);
COMMIT;
