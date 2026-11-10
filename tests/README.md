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
