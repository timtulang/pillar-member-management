-- Run as the schema owner after neon-setup.sql, then grant this group to a
-- dedicated login created in the Neon console (see README). Do not use the
-- schema owner's connection string for the web application.
BEGIN;
DO $$ BEGIN
  IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='pillar_runtime') THEN
    CREATE ROLE pillar_runtime NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
  END IF;
END $$;
REVOKE ALL ON SCHEMA pillar,pillar_auth FROM PUBLIC;
GRANT USAGE ON SCHEMA pillar,pillar_auth TO pillar_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA pillar,pillar_auth TO pillar_runtime;
REVOKE UPDATE,DELETE,TRUNCATE ON pillar.audit_events FROM pillar_runtime;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA pillar,pillar_auth TO pillar_runtime;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA pillar TO pillar_runtime;
COMMIT;

-- Replace pillar_app with the dedicated login you created, then execute:
-- GRANT pillar_runtime TO pillar_app;
-- New Neon console-created roles may inherit neon_superuser. If so, revoke
-- that inheritance from the runtime login before using it. Verify its grants.
