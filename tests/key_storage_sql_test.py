"""Exercise the key-storage migration on a disposable, offline PostgreSQL cluster."""
import re
import subprocess
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
migration = (root / "database/004_data_protection.sql").read_text()
checks = """
BEGIN;
SET ROLE pillar_runtime;
INSERT INTO pillar_security.data_protection_keys(friendly_name,xml) VALUES('synthetic','<encryptedSecret />');
SELECT friendly_name FROM pillar_security.data_protection_keys;
RESET ROLE;
DO $$ BEGIN
 IF (SELECT count(*) FROM pillar_security.data_protection_keys)<>1 THEN RAISE EXCEPTION 'Key insert/read failed'; END IF;
 IF has_table_privilege('pillar_runtime','pillar_security.data_protection_keys','UPDATE') THEN RAISE EXCEPTION 'Runtime can update keys'; END IF;
 IF has_table_privilege('pillar_runtime','pillar_security.data_protection_keys','DELETE') THEN RAISE EXCEPTION 'Runtime can delete keys'; END IF;
 IF has_table_privilege('pillar_runtime','pillar_security.data_protection_keys','TRUNCATE') THEN RAISE EXCEPTION 'Runtime can truncate keys'; END IF;
 IF has_schema_privilege('pillar_runtime','pillar_security','CREATE') THEN RAISE EXCEPTION 'Runtime can create schema objects'; END IF;
END $$;
COMMIT;
"""
with tempfile.TemporaryDirectory(prefix="pillar-key-storage-test-") as tmp:
    subprocess.run(["initdb", "-D", tmp + "/db", "--no-locale", "-E", "UTF8", "-A", "trust"], check=True, capture_output=True)
    for label, sql in [("Migration", migration), ("Permissions", checks), ("Re-run", migration), ("Preservation", "DO $$ BEGIN IF (SELECT count(*) FROM pillar_security.data_protection_keys)<>1 THEN RAISE EXCEPTION 'Re-running migration lost existing keys'; END IF; END $$;")]:
        result = subprocess.run(["postgres", "--single", "-D", tmp + "/db", "-j", "postgres"], input=sql + "\n\n", text=True, capture_output=True)
        if result.returncode or re.search(r'\b(ERROR|FATAL|PANIC):', result.stderr):
            raise RuntimeError(f"{label} failed:\n{result.stderr}")
print("PASS key storage SQL, restricted permissions, repeatable migration, and preservation of existing keys.")
