-- Run in the existing Neon database's SQL Editor as the database owner.
-- Safe to re-run: existing key records and all member/staff data are preserved.
BEGIN;
CREATE SCHEMA IF NOT EXISTS pillar_security;
REVOKE ALL ON SCHEMA pillar_security FROM PUBLIC;

CREATE TABLE IF NOT EXISTS pillar_security.data_protection_keys (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    friendly_name text NOT NULL,
    xml text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
REVOKE ALL ON pillar_security.data_protection_keys FROM PUBLIC;

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'pillar_runtime') THEN
        CREATE ROLE pillar_runtime NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END $$;
GRANT USAGE ON SCHEMA pillar_security TO pillar_runtime;
GRANT SELECT, INSERT ON pillar_security.data_protection_keys TO pillar_runtime;
REVOKE UPDATE, DELETE, TRUNCATE ON pillar_security.data_protection_keys FROM pillar_runtime;
COMMIT;

-- If your app uses a dedicated login, it needs the existing pillar_runtime group.
-- Replace pillar_app with your actual runtime login, then run this separately:
-- GRANT pillar_runtime TO pillar_app;
