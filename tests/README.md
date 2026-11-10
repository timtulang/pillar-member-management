# Local verification

Use a disposable PostgreSQL 17 database initialized with `database/neon-setup.sql`, then apply the runtime grants and create a restricted login. Provision a synthetic administrator with `--create-staff`. Run the app locally with MFA enabled. These tests create synthetic family/event/staff records and must never target a production database.

Install `requirements.txt` in a Python virtual environment and install Chromium (`playwright install chromium`, or provide an existing Chromium binary through `CHROMIUM_PATH`). The default binary path is `/usr/bin/chromium`.

Provide these environment variables through a private local secret mechanism:

| Variable | Meaning |
| --- | --- |
| `PILLAR_TEST_DATABASE` | psycopg connection string to the restricted **local** test login, e.g. `host=127.0.0.1 port=5432 dbname=pillar user=pillar_app password=...` |
| `PILLAR_TEST_URL` | Local running app, default `http://127.0.0.1:5080` |
| `PILLAR_TEST_ADMIN_EMAIL` | Synthetic administrator email |
| `PILLAR_TEST_ADMIN_PASSWORD` | Its password |
| `PILLAR_TEST_TOTP_SECRET` | Its TOTP enrollment secret, only if already enrolled; a fresh account is enrolled by the test |
| `PILLAR_TEST_ARTIFACTS` | Optional screenshot directory, default `artifacts/test` |

Run:

```sh
python tests/database_test.py
python tests/browser_test.py
```

The browser test covers MFA enrollment/login; member creation; household history; parentage preservation after marriage; stale edit rejection; ministry/discipleship/baptism records; issued QR decoding and the actual camera loop using a synthetic video stream; concurrent duplicate scans; anti-forgery rejection; revocation; attendance corrections; role and object-route restrictions; immediate account disablement; event cancellation; desktop/mobile rendering; and browser JavaScript errors.

The database test exercises temporal exclusion constraints, ancestry cycles, overlapping marriages across spouse columns, one main ministry, one unrevoked card, card/person foreign keys, attendance uniqueness, manual-entry checks, append-only audit permissions, schema-change denial, and concurrent graph mutations. Transactional fixtures are rolled back; the concurrency fixtures are explicitly removed.

Physical phone/USB hardware and a real Neon deployment require their own acceptance check. The camera test runs the actual decoder/scanner against a generated ID image streamed through a browser canvas; it does not claim a physical camera was tested.

## Form and dashboard checks

Run `dotnet run --project tests/FeatureChecks` for database-independent validation,
recurrence (including month ends and daylight-saving transitions), demographic
presentation, and rendering of the actual Razor forms. No real accounts or data
are used. An optional output directory writes synthetic HTML and the demographic
SQL query for further inspection.

Run `python3 tests/dashboard_sql_test.py` to verify demographic totals, age boundaries,
legacy values, roster eligibility, progress counts, and event ordering against a
fresh disposable PostgreSQL cluster. It requires `initdb` and `postgres` on PATH,
uses no network connection, and deletes its temporary cluster afterward.

The browser suite also checks 320, 390, 430, and 768 pixel layouts across the
overview, directory, member forms and profiles, ministries, events, and staff
access. It exercises the Ministry / Program tabs and collapsed mobile navigation,
including Escape-key behavior, and checks for page-level horizontal overflow.
These are browser viewport checks; physical iOS and Android testing is separate.

## Vercel security checks

Run `dotnet run --project tests/SecurityChecks` for certificate-encrypted key
storage, cross-instance session decryption, certificate rotation, configuration
failures, and the Vercel/default proxy trust boundaries. The key repository is
in-memory; this test never connects to a real database.

Run `python3 tests/key_storage_sql_test.py` for the actual PostgreSQL migration,
restricted grants, idempotency, and preservation of existing keys. It uses an
offline disposable PostgreSQL cluster and requires `initdb` and `postgres`.
