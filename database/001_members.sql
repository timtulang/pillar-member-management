-- Run ONCE on a fresh Neon database/branch as the database owner.
BEGIN;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE SCHEMA pillar;
REVOKE ALL ON SCHEMA pillar FROM PUBLIC;
SET LOCAL search_path = pillar, public;

CREATE TYPE "congregation_status" AS ENUM (
  'visitor',
  'regular_attendee',
  'member',
  'inactive',
  'transferred'
);

CREATE TYPE "civil_status" AS ENUM (
  'single',
  'married',
  'separated',
  'divorced',
  'widowed',
  'other',
  'unknown'
);

CREATE TYPE "household_role" AS ENUM (
  'adult',
  'child',
  'dependent',
  'other'
);

CREATE TYPE "parent_relationship_kind" AS ENUM (
  'biological',
  'adoptive',
  'step',
  'foster'
);

CREATE TYPE "marriage_end_reason" AS ENUM (
  'divorce',
  'annulment',
  'spouse_death',
  'other'
);

CREATE TYPE "baptism_location" AS ENUM (
  'pillar',
  'elsewhere',
  'unknown'
);

CREATE TYPE "discipleship_status" AS ENUM (
  'reported_participation',
  'in_progress',
  'completed',
  'paused'
);

CREATE TYPE "staff_role" AS ENUM (
  'administrator',
  'membership_manager',
  'attendance_operator'
);

CREATE TYPE "attendance_method" AS ENUM (
  'qr_scan',
  'manual'
);

CREATE TABLE "people" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "member_code" varchar(24) UNIQUE NOT NULL,
  "first_name" varchar(150) NOT NULL,
  "middle_name" varchar(150),
  "surname" varchar(150) NOT NULL,
  "suffix" varchar(30),
  "preferred_name" varchar(150),
  "gender" varchar(50),
  "birth_date" date,
  "residential_address" text,
  "phone" varchar(40),
  "email" varchar(254),
  "congregation_status" congregation_status NOT NULL DEFAULT 'visitor',
  "membership_date" date,
  "reported_civil_status" civil_status NOT NULL DEFAULT 'unknown',
  "civil_status_reported_on" date,
  "reported_child_count" int,
  "invited_by_person_id" uuid,
  "inviter_name_reported" varchar(300),
  "registered_at" timestamptz,
  "deceased_on" date,
  "archived_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "updated_at" timestamptz NOT NULL DEFAULT (now()),
  CONSTRAINT "nonnegative_reported_children" CHECK (reported_child_count IS NULL OR reported_child_count >= 0),
  CONSTRAINT "not_own_inviter" CHECK (invited_by_person_id IS NULL OR invited_by_person_id <> id),
  CONSTRAINT "valid_lifespan" CHECK (deceased_on IS NULL OR birth_date IS NULL OR deceased_on >= birth_date)
);

CREATE TABLE "households" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "display_name" varchar(200) NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  "archived_at" timestamptz
);

CREATE TABLE "household_memberships" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "household_id" uuid NOT NULL,
  "person_id" uuid NOT NULL,
  "role" household_role NOT NULL DEFAULT 'other',
  "is_primary" boolean NOT NULL DEFAULT true,
  "valid_from" date,
  "valid_to" date,
  "recorded_at" timestamptz NOT NULL DEFAULT (now()),
  CONSTRAINT "valid_household_period" CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to > valid_from)
);

CREATE TABLE "parent_child_relationships" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "parent_id" uuid NOT NULL,
  "child_id" uuid NOT NULL,
  "kind" parent_relationship_kind NOT NULL,
  "valid_from" date,
  "valid_to" date,
  "recorded_at" timestamptz NOT NULL DEFAULT (now()),
  CONSTRAINT "different_parent_child" CHECK (parent_id <> child_id),
  CONSTRAINT "valid_parentage_period" CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to > valid_from)
);

CREATE TABLE "marriages" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "spouse_a_id" uuid NOT NULL,
  "spouse_b_id" uuid NOT NULL,
  "married_on" date,
  "ended_on" date,
  "end_reason" marriage_end_reason,
  "recorded_at" timestamptz NOT NULL DEFAULT (now()),
  CONSTRAINT "canonical_distinct_spouses" CHECK (spouse_a_id < spouse_b_id),
  CONSTRAINT "valid_marriage_period" CHECK (ended_on IS NULL OR married_on IS NULL OR ended_on > married_on),
  CONSTRAINT "marriage_end_details" CHECK ((ended_on IS NULL AND end_reason IS NULL) OR (ended_on IS NOT NULL AND end_reason IS NOT NULL))
);

CREATE TABLE "baptisms" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "person_id" uuid NOT NULL,
  "baptized_on" date,
  "location" baptism_location NOT NULL,
  "church_name" varchar(200),
  "recorded_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "ministries" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "name" varchar(150) UNIQUE NOT NULL,
  "is_active" boolean NOT NULL DEFAULT true
);

CREATE TABLE "ministry_roles" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "name" varchar(100) UNIQUE NOT NULL
);

CREATE TABLE "ministry_assignments" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "person_id" uuid NOT NULL,
  "ministry_id" uuid NOT NULL,
  "ministry_role_id" uuid,
  "is_main" boolean NOT NULL DEFAULT false,
  "valid_from" date,
  "valid_to" date,
  CONSTRAINT "valid_ministry_period" CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to > valid_from)
);

CREATE TABLE "ministry_preferences" (
  "person_id" uuid NOT NULL,
  "ministry_id" uuid NOT NULL,
  "recorded_at" timestamptz NOT NULL DEFAULT (now()),
  PRIMARY KEY ("person_id", "ministry_id")
);

CREATE TABLE "discipleship_programs" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "name" varchar(200) UNIQUE NOT NULL,
  "is_active" boolean NOT NULL DEFAULT true
);

CREATE TABLE "discipleship_enrollments" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "person_id" uuid NOT NULL,
  "program_id" uuid NOT NULL,
  "status" discipleship_status NOT NULL DEFAULT 'reported_participation',
  "started_on" date,
  "completed_on" date,
  "recorded_at" timestamptz NOT NULL DEFAULT (now()),
  CONSTRAINT "valid_discipleship_dates" CHECK (completed_on IS NULL OR started_on IS NULL OR completed_on >= started_on),
  CONSTRAINT "completion_date_requires_completed_status" CHECK (completed_on IS NULL OR status = 'completed')
);

CREATE TABLE "app_users" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "identity_issuer" varchar(300) NOT NULL,
  "identity_subject" varchar(300) NOT NULL,
  "person_id" uuid UNIQUE,
  "disabled_at" timestamptz,
  "created_at" timestamptz NOT NULL DEFAULT (now())
);

CREATE TABLE "app_user_roles" (
  "app_user_id" uuid NOT NULL,
  "role" staff_role NOT NULL,
  "granted_by_user_id" uuid NOT NULL,
  "granted_at" timestamptz NOT NULL DEFAULT (now()),
  PRIMARY KEY ("app_user_id", "role")
);

CREATE TABLE "member_cards" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "person_id" uuid NOT NULL,
  "token_digest" bytea UNIQUE NOT NULL,
  "issued_at" timestamptz NOT NULL DEFAULT (now()),
  "expires_at" timestamptz NOT NULL,
  "issued_by_user_id" uuid NOT NULL,
  "revoked_at" timestamptz,
  "revoked_by_user_id" uuid,
  CONSTRAINT "sha256_digest_length" CHECK (octet_length(token_digest) = 32),
  CONSTRAINT "valid_card_expiry" CHECK (expires_at > issued_at),
  CONSTRAINT "valid_card_revocation_time" CHECK (revoked_at IS NULL OR revoked_at >= issued_at),
  CONSTRAINT "card_revocation_actor" CHECK ((revoked_at IS NULL AND revoked_by_user_id IS NULL) OR (revoked_at IS NOT NULL AND revoked_by_user_id IS NOT NULL))
);

CREATE TABLE "event_occurrences" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "title" varchar(200) NOT NULL,
  "location" varchar(250),
  "starts_at" timestamptz NOT NULL,
  "ends_at" timestamptz NOT NULL,
  "time_zone" varchar(100) NOT NULL,
  "check_in_opens_at" timestamptz NOT NULL,
  "check_in_closes_at" timestamptz NOT NULL,
  "cancelled_at" timestamptz,
  "created_by_user_id" uuid NOT NULL,
  "created_at" timestamptz NOT NULL DEFAULT (now()),
  CONSTRAINT "valid_event_period" CHECK (ends_at > starts_at),
  CONSTRAINT "valid_checkin_window" CHECK (check_in_closes_at > check_in_opens_at)
);

CREATE TABLE "attendance_records" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "event_occurrence_id" uuid NOT NULL,
  "person_id" uuid NOT NULL,
  "checked_in_at" timestamptz NOT NULL DEFAULT (now()),
  "method" attendance_method NOT NULL,
  "member_card_id" uuid,
  "recorded_by_user_id" uuid NOT NULL,
  "manual_reason" varchar(300),
  "voided_at" timestamptz,
  "voided_by_user_id" uuid,
  "void_reason" varchar(300),
  CONSTRAINT "attendance_method_details" CHECK ((method = 'qr_scan' AND member_card_id IS NOT NULL AND manual_reason IS NULL) OR (method = 'manual' AND member_card_id IS NULL AND manual_reason IS NOT NULL AND length(trim(manual_reason)) > 0)),
  CONSTRAINT "attendance_void_details" CHECK ((voided_at IS NULL AND voided_by_user_id IS NULL AND void_reason IS NULL) OR (voided_at IS NOT NULL AND voided_by_user_id IS NOT NULL AND void_reason IS NOT NULL AND length(trim(void_reason)) > 0)),
  CONSTRAINT "valid_attendance_void_time" CHECK (voided_at IS NULL OR voided_at >= checked_in_at)
);

CREATE TABLE "audit_events" (
  "id" uuid PRIMARY KEY DEFAULT (gen_random_uuid()),
  "actor_user_id" uuid,
  "system_actor" varchar(100),
  "action" varchar(100) NOT NULL,
  "entity_type" varchar(100) NOT NULL,
  "entity_id" uuid NOT NULL,
  "occurred_at" timestamptz NOT NULL DEFAULT (now()),
  "correlation_id" uuid NOT NULL,
  "changed_fields" jsonb NOT NULL DEFAULT ('[]'::jsonb),
  CONSTRAINT "one_audit_actor" CHECK ((actor_user_id IS NOT NULL AND system_actor IS NULL) OR (actor_user_id IS NULL AND system_actor IS NOT NULL))
);

CREATE INDEX ON "people" ("surname", "first_name");

CREATE INDEX ON "people" ("invited_by_person_id");

CREATE INDEX ON "household_memberships" ("person_id", "valid_from");

CREATE INDEX ON "household_memberships" ("household_id", "valid_to");

CREATE INDEX ON "parent_child_relationships" ("parent_id", "child_id", "kind");

CREATE INDEX ON "parent_child_relationships" ("child_id");

CREATE INDEX ON "marriages" ("spouse_a_id", "spouse_b_id");

CREATE INDEX ON "marriages" ("spouse_b_id");

CREATE INDEX ON "baptisms" ("person_id");

CREATE INDEX ON "ministry_assignments" ("person_id", "ministry_id");

CREATE INDEX ON "ministry_assignments" ("ministry_id");

CREATE INDEX ON "ministry_assignments" ("ministry_role_id");

CREATE INDEX ON "ministry_preferences" ("ministry_id");

CREATE UNIQUE INDEX ON "discipleship_enrollments" ("person_id", "program_id");

CREATE INDEX ON "discipleship_enrollments" ("program_id");

CREATE UNIQUE INDEX ON "app_users" ("identity_issuer", "identity_subject");

CREATE INDEX ON "app_user_roles" ("granted_by_user_id");

CREATE UNIQUE INDEX ON "member_cards" ("id", "person_id");

CREATE INDEX ON "member_cards" ("person_id");

CREATE INDEX ON "member_cards" ("issued_by_user_id");

CREATE INDEX ON "member_cards" ("revoked_by_user_id");

CREATE INDEX ON "event_occurrences" ("starts_at");

CREATE INDEX ON "event_occurrences" ("created_by_user_id");

CREATE UNIQUE INDEX ON "attendance_records" ("event_occurrence_id", "person_id");

CREATE INDEX ON "attendance_records" ("person_id", "checked_in_at");

CREATE INDEX ON "attendance_records" ("member_card_id", "person_id");

CREATE INDEX ON "attendance_records" ("recorded_by_user_id");

CREATE INDEX ON "attendance_records" ("voided_by_user_id");

CREATE INDEX ON "audit_events" ("entity_type", "entity_id", "occurred_at");

CREATE INDEX ON "audit_events" ("actor_user_id", "occurred_at");

COMMENT ON COLUMN "people"."member_code" IS 'Stable display identifier, e.g. PF-000123. Never an authentication secret.';

COMMENT ON COLUMN "people"."gender" IS 'Self-reported; NULL means not provided.';

COMMENT ON COLUMN "people"."residential_address" IS 'Individual address from the form; household membership does not assert co-residence.';

COMMENT ON COLUMN "people"."phone" IS 'Text, not numeric; preserve leading zeros and country prefix. Not unique.';

COMMENT ON COLUMN "people"."email" IS 'Optional; not unique because family members may share contact details.';

COMMENT ON COLUMN "people"."membership_date" IS 'Actual date if known; distinct from registration/import time.';

COMMENT ON COLUMN "people"."reported_civil_status" IS 'Latest self-report, not a substitute for marriage records.';

COMMENT ON COLUMN "people"."reported_child_count" IS 'Optional legacy self-report; not the calculated number of linked children.';

COMMENT ON COLUMN "people"."inviter_name_reported" IS 'Unresolved source value; do not create or link a person by name alone.';

COMMENT ON COLUMN "people"."registered_at" IS 'Form timestamp when known; confirm source timezone before import.';

COMMENT ON COLUMN "households"."display_name" IS 'Family label; not unique and not an identity key.';

COMMENT ON TABLE "household_memberships" IS 'SQL exclusion constraints must prevent overlapping periods for the same person/household, and overlapping primary households for a person. Role changes close a period and open another.';

COMMENT ON COLUMN "household_memberships"."valid_from" IS 'NULL means membership start unknown.';

COMMENT ON COLUMN "household_memberships"."valid_to" IS 'Exclusive; NULL means ongoing.';

COMMENT ON TABLE "parent_child_relationships" IS 'SQL must prevent overlapping duplicate relationships and cyclic ancestry. Do not infer parentage from a shared surname or household. Guardianship/custody is outside this v1 model.';

COMMENT ON COLUMN "parent_child_relationships"."valid_to" IS 'NULL means ongoing. Moving out or marrying does not end parentage.';

COMMENT ON TABLE "marriages" IS 'Multiple historical marriages are supported, including remarriage to the same person. Prevent overlapping duplicate spouse-pair periods in SQL. A household move never creates or ends a marriage automatically.';

COMMENT ON COLUMN "marriages"."married_on" IS 'Unknown allowed. Anniversary is derived from a known marriage date.';

COMMENT ON TABLE "baptisms" IS 'A Yes to Baptized in Pillar creates a confirmed baptism with location pillar. No/blank does not prove the person is unbaptized.';

COMMENT ON COLUMN "baptisms"."baptized_on" IS 'May be unknown even when baptism is confirmed.';

COMMENT ON COLUMN "ministry_roles"."name" IS 'Examples: Member, Coordinator, Assistant Coordinator. These confer no application permissions.';

COMMENT ON TABLE "ministry_assignments" IS 'SQL must prevent overlapping assignments to the same ministry and overlapping main ministries per person. Preferences are not assignments.';

COMMENT ON COLUMN "ministry_assignments"."ministry_role_id" IS 'NULL if role has not been confirmed.';

COMMENT ON COLUMN "discipleship_programs"."name" IS 'Examples: G.R.O.W.T.H.; Pillar Discipleship Guide.';

COMMENT ON TABLE "discipleship_enrollments" IS 'One summary per person/program in v1. A checked program in the spreadsheet means reported participation, not confirmed completion. Add lesson-level progress only if required.';

COMMENT ON TABLE "app_users" IS 'Reference a trusted authentication provider, such as existing Supabase Auth. Validate issuer/audience/signature/expiry server-side. This is not a password table.';

COMMENT ON COLUMN "app_users"."person_id" IS 'Optional link. Many church members will have no login.';

COMMENT ON TABLE "app_user_roles" IS 'Roles are granted by authorized administrators only. First administrator is provisioned through a controlled bootstrap. Role grants/removals are audited.';

COMMENT ON TABLE "member_cards" IS 'Reissuing a lost card revokes the old credential transactionally. Default one unrevoked card per person via a partial unique index; expired cards must be revoked during replacement. The QR grants no login or profile access.';

COMMENT ON COLUMN "member_cards"."token_digest" IS 'SHA-256 of a cryptographically random 32-byte opaque token; raw token is never stored or logged.';

COMMENT ON TABLE "event_occurrences" IS 'One row per actual service/session. Two services on the same day have distinct attendance lists.';

COMMENT ON COLUMN "event_occurrences"."time_zone" IS 'IANA name, e.g. Asia/Manila.';

COMMENT ON TABLE "attendance_records" IS 'Duplicate scans return the existing attendance result. Counts exclude voided rows. Reinstatement is an explicit authorized, audited correction, never an automatic rescan.';

COMMENT ON COLUMN "attendance_records"."checked_in_at" IS 'Server-controlled time.';

COMMENT ON COLUMN "attendance_records"."member_card_id" IS 'Composite FK below guarantees the card belongs to this person.';

COMMENT ON TABLE "audit_events" IS 'Append-only under normal application roles; mutations and audit inserts commit together. Security-managed retention is a separate process. Temporal family tables carry relationship history.';

COMMENT ON COLUMN "audit_events"."actor_user_id" IS 'NULL only for identified system/import jobs.';

COMMENT ON COLUMN "audit_events"."entity_id" IS 'Polymorphic audit reference; deliberately not a foreign key.';

COMMENT ON COLUMN "audit_events"."changed_fields" IS 'Allowlisted field names only; no full personal-data snapshots, secrets, or QR tokens.';

ALTER TABLE "people" ADD FOREIGN KEY ("invited_by_person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "household_memberships" ADD FOREIGN KEY ("household_id") REFERENCES "households" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "household_memberships" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "parent_child_relationships" ADD FOREIGN KEY ("parent_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "parent_child_relationships" ADD FOREIGN KEY ("child_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "marriages" ADD FOREIGN KEY ("spouse_a_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "marriages" ADD FOREIGN KEY ("spouse_b_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "baptisms" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "ministry_assignments" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "ministry_assignments" ADD FOREIGN KEY ("ministry_id") REFERENCES "ministries" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "ministry_assignments" ADD FOREIGN KEY ("ministry_role_id") REFERENCES "ministry_roles" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "ministry_preferences" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "ministry_preferences" ADD FOREIGN KEY ("ministry_id") REFERENCES "ministries" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "discipleship_enrollments" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "discipleship_enrollments" ADD FOREIGN KEY ("program_id") REFERENCES "discipleship_programs" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "app_users" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "app_user_roles" ADD FOREIGN KEY ("app_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "app_user_roles" ADD FOREIGN KEY ("granted_by_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "member_cards" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "member_cards" ADD FOREIGN KEY ("issued_by_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "member_cards" ADD FOREIGN KEY ("revoked_by_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "event_occurrences" ADD FOREIGN KEY ("created_by_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "attendance_records" ADD FOREIGN KEY ("event_occurrence_id") REFERENCES "event_occurrences" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "attendance_records" ADD FOREIGN KEY ("person_id") REFERENCES "people" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "attendance_records" ADD FOREIGN KEY ("recorded_by_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "attendance_records" ADD FOREIGN KEY ("voided_by_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "attendance_records" ADD FOREIGN KEY ("member_card_id", "person_id") REFERENCES "member_cards" ("id", "person_id") ON DELETE RESTRICT ON UPDATE RESTRICT DEFERRABLE INITIALLY IMMEDIATE;

ALTER TABLE "audit_events" ADD FOREIGN KEY ("actor_user_id") REFERENCES "app_users" ("id") DEFERRABLE INITIALLY IMMEDIATE;

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
