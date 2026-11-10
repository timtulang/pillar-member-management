# Pillar Family — ASP.NET Core member management

A working ASP.NET Core 10 Razor Pages application with PostgreSQL, local ASP.NET Core Identity staff authentication, required authenticator MFA, printable QR member cards, and staff-operated attendance scanning.

## Included

- Member directory with search, status filters, pagination, create/edit, archive/restore, and optimistic concurrency on profile edits.
- Parent/child links, dated marriage records, primary household moves and history. Moving out or marrying preserves parent links.
- Ministries, assignment history, preferences, discipleship program summaries, and confirmed baptism records.
- Individual event occurrences, timezone-aware schedules, camera/USB QR scanning, manual attendance, duplicate protection, void/reinstate, and cancellation.
- Revocable and expiring member cards; printable 54 × 86 mm layout. Raw QR credentials are not stored server-side.
- Staff provisioning, role management, disabled-account enforcement on each request, password changes, TOTP enrollment, recovery codes, and an append-only activity log.
- A single Neon setup script, restricted runtime grants, Dockerfile, local PostgreSQL Compose file, and browser/database verification scripts.

No uploaded member data is bundled or automatically imported. Lookups are seeded from the spreadsheet's ministry/discipleship options. This application does not connect to the existing Supabase prototype.

## 1. Create the Neon database

Use a **new database or branch**. Sign in to Neon, open its SQL Editor, select that database, and run the entire contents of:

```
database/neon-setup.sql
```

This is the SQL query/script to run on Neon. It creates `pillar` (18 management tables), `pillar_auth` (the standard ASP.NET Identity tables), enums, indexes, family constraints, and lookup values in one transaction. It requires permission to create the supported `btree_gist` extension. It intentionally fails safely on existing objects; it is an initial schema, not an upgrade migration.

Then run:

```
database/003_runtime_permissions.sql
```

Create a dedicated login named `pillar_app` using your database administration process, with a unique password stored in your secret manager. Grant it the runtime group:

```sql
GRANT pillar_runtime TO pillar_app;
```

Use a login with no ownership, schema-creation, role-management, or inherited administrative privileges. If Neon automatically added `neon_superuser` membership to the new login, remove that membership as an authorized administrator and verify the login's effective permissions before use. The web app does not need the schema owner's connection string. Keep the owner connection for future reviewed migrations.

The `001_members.sql`, `002_identity.sql`, and `constraints.sql` files are source/reference fragments already incorporated into `neon-setup.sql`. **Do not additionally run them.**

## 2. Configure the app

Install the .NET 10 SDK. From the project directory:

```sh
dotnet restore --locked-mode
dotnet build
```

Supply an Npgsql connection string through `ConnectionStrings__Pillar` in your deployment secret manager, or `dotnet user-secrets` during local development. Do not commit the real value. Neon supplies URI-style strings too; this app expects **Npgsql key=value format**:

For local work, set `ASPNETCORE_ENVIRONMENT=Development` before starting the app or running the staff-provisioning command. User secrets are loaded only in Development. Production requires persistent data-protection keys (a private directory, or database storage for Vercel) and verified TLS as described below. Development also loads the ignored `appsettings.Local.json`; environment variables take precedence.

```text
Host=ep-YOUR-ENDPOINT-pooler.REGION.aws.neon.tech;Port=5432;Database=neondb;Username=pillar_app;Password=YOUR_SECRET;SSL Mode=VerifyFull;Channel Binding=Require;Pooling=true;Maximum Pool Size=20;Timeout=15;Command Timeout=30
```

Use your actual Neon endpoint, database, and login. The pooled endpoint is suitable for ordinary app queries; use the direct endpoint for administration/migrations. The app uses transaction-level advisory locks, not session-level locks. Production startup refuses database connections without `SSL Mode=VerifyFull`.

Configuration variables:

| Variable | Value / purpose |
| --- | --- |
| `ConnectionStrings__Pillar` | Private PostgreSQL connection string |
| `Pillar__ChurchName` | Defaults to `Pillar Family` |
| `Pillar__TimeZone` | Defaults to `Asia/Manila`; event times use their recorded timezone |
| `Pillar__CardValidityDays` | Defaults to `365` |
| `AllowedHosts` | Your app's hostname, e.g. `members.example.org`; local defaults are localhost and 127.0.0.1 |
| `Security__DataProtectionKeyPath` | Private persistent directory for authentication/data-protection keys when using FileSystem storage |
| `Security__TrustedProxies__0` | Your actual reverse proxy IP, only when running behind a proxy; add numbered entries as needed |
| `Security__RequireMfa` | Defaults to `true`; disabling is accepted only in Development |

Persist the data-protection directory across releases. Restrict it to the app's OS identity and use encrypted storage/backup for these sensitive keys. Share the same protected key ring if you run multiple app instances.

## 3. Create the first administrator

There is no default password and no public registration. Set these values using your shell's secret input mechanism or deployment secret manager; do not put a real password in source control or command history:

```text
Bootstrap__Email=your-administrator@example.org
Bootstrap__Password=<unique password with at least 14 characters>
Bootstrap__Role=administrator
```

With the database configured, run:

```sh
dotnet run -- --create-staff
```

Or against a published build:

```sh
dotnet Pillar.dll --create-staff
```

The password must include uppercase, lowercase, a number, and a symbol. The CLI verifies password policy and duplicate accounts. Remove the bootstrap environment values immediately afterward. This command is also an operator recovery route for creating another authorized administrator if the original account becomes inaccessible; it does not reset an existing account.

At first login, enroll an authenticator app and save the one-time recovery codes privately. Staff roles do not bypass MFA. Verify the intended staff member's email before provisioning: account creation marks it confirmed because this app does not send verification emails. Additional accounts can be created by an administrator on **Staff & access**. Users can change passwords under **Account security**.

## 4. Run locally

Set `ASPNETCORE_ENVIRONMENT=Development`, provide `ConnectionStrings__Pillar`, and run:

```sh
dotnet run --urls http://localhost:5080
```

Open `http://localhost:5080`. Camera access is allowed on localhost; a phone accessing a remote host needs HTTPS.

For a local database instead of Neon, set a local-only `POSTGRES_PASSWORD` secret and run `docker compose up -d postgres`. Compose initializes a new volume from `database/neon-setup.sql`. Use host `localhost`, port `5432`, database `pillar`, and username `pillar_local`. `SSL Mode=Disable` is acceptable only for this local Development database. Existing Docker volumes are not reinitialized. Do not remove a volume containing data you need.

## 5. Deploy

```sh
dotnet publish -c Release -o artifacts/publish
```

Host the published output on an ASP.NET-capable service, VM, IIS, or container platform. A Dockerfile is included:

```sh
docker build -t pillar-members .
```

The container runs as a non-root user and listens on port 8080. Supply secrets at runtime, persist `/app/keys`, and terminate HTTPS at your trusted reverse proxy. Configure the actual proxy IP(s), `AllowedHosts`, and your public hostname. The app honors forwarded protocol/client-IP headers only from trusted proxies, uses secure cookies in production, and redirects HTTP to HTTPS. Do not expose the internal HTTP listener directly to the internet.

For Vercel, use its Container runtime and follow [the Vercel deployment guide](docs/VERCEL.md). Database-backed, certificate-encrypted data-protection keys and the additive SQL migration are included.

This application is a real .NET server; static-only hosting cannot run it directly.

## Family workflow

1. Create the parents, son, and spouse as distinct people. Non-attending relatives can be minimal visitor profiles without login accounts.
2. Create the original household under **Families**, then link people through **Member profile → Move to a household**.
3. On the son's profile, link his parents by their member IDs. A matching surname never creates a relationship automatically.
4. When he marries, record the spouse and marriage date. His parent links remain intact.
5. If a new household starts, create it and record his move on its actual effective date. The old membership closes and stays in history. Move his spouse separately, since their move dates can differ.

Dates use `[start, end)` periods: the end date is the first date on which the relationship/membership no longer applies. Unknown start dates remain NULL. A same-day household correction replaces that day's current row and is audited instead of creating a zero-length period. Historical periods remain visible. V1 allows only one primary household and one main ministry at a time, and enforces one marriage at a time across both spouse columns. Unknown marriage dates are treated as unbounded and may require clarification before recording another historical marriage.

## QR and attendance behavior

- Open a member profile, choose **Member ID**, and issue a card. Print or privately save the generated card immediately. Reissuing always revokes the previous credential, including an expired one.
- The QR contains a random 256-bit token with a version prefix, not personal data or the member code. PostgreSQL stores its SHA-256 digest. Neither logs nor audit entries contain raw tokens.
- Create an event as a manager. Operators can scan camera/USB QR inputs or enter a member code with a reason for manual check-in. Operators cannot browse member profiles, family records, ministries, or staff permissions.
- The server resolves the person from the token, checks card/member/event eligibility, supplies the actor and timestamp, and writes the attendance transactionally. Repeated or concurrent scans count once.
- Managers can void/reinstate attendance with a reason. Voided records are excluded from counts. A rescan does not silently reinstate a void.
- Printed QR codes can be copied; staff still confirm the displayed name. This is online, attended check-in, not proof of physical identity. Camera frames stay in the browser. Only decoded QR payloads are sent to the server.

## Security and operational notes

- All writes use parameterized SQL or ASP.NET Identity's EF stores. A whitelist controls the few catalog table choices. Forms use ASP.NET anti-forgery tokens, dedicated inputs, validation, and Razor output encoding.
- Passwords use ASP.NET Identity's password hasher. MFA, login lockout, rate limits, trusted proxy handling, current role/disablement checks, secure cookies, CSP, and no-store responses are configured.
- A single transaction advisory lock serializes domain writes, including family changes, revocation, and attendance. This intentionally favors correctness for one church over high write throughput. Database triggers also serialize graph validation; exclusions protect temporal uniqueness.
- Audit rows are append-only and store actions/record IDs/field names, not full personal-data snapshots. Operational profile archives are not erasure. Establish retention, consent, privileged deletion/anonymization, and encrypted backup procedures before loading real records, particularly children's data. Test restore on a separate database.
- Use synthetic data in development. Do not commit connection strings, user secrets, `.env` files, data-protection keys, private card PDFs, or database exports.
- Public registration, email delivery/password-reset emails, bulk spreadsheet import, recurring event generation, legal guardianship/custody, card photos, multi-church tenancy, and offline scanning are not included. The existing spreadsheet's “Regular Attendee (Member)” distinction should be clarified before any future import.

## Verification

See `tests/README.md`. Tests must target a disposable local database and synthetic staff accounts. They create synthetic family/event records and exercise destructive corrections to those records. Never point them at your production Neon database.

The application does not run migrations on startup. Commit and review future database migrations separately; `neon-setup.sql` is only for first installation.

## Staff workflows

- Member forms use controlled gender choices and readable status labels. Phone,
  email, child counts, and related dates are checked on the server. Existing
  nonstandard gender text remains visible when editing; choose a listed value or
  Not provided before saving that profile.
- Administrators and membership managers can create single events or daily,
  weekly, and monthly repeats. Choose an interval and 2–52 occurrences (including
  the first). All occurrences are created together with separate attendance lists
  and audit records. Times follow the selected local timezone; monthly repeats
  clamp to the month's last day when necessary. An invalid or ambiguous
  daylight-saving time rejects the entire schedule. Occurrences are managed
  individually; this does not create an indefinitely repeating background job.
- Ministries & growth uses compact Ministry / Program tabs with searchable, expandable rosters and profile links.
  Ministry rosters show current assignments. Program rosters distinguish reported
  participation, in progress, completed, and paused records.
- Overview demographics show age, gender, church status, and reported civil status.
  They include unarchived profiles without a recorded date of death. Missing
  details remain in the totals, and ages use the configured church timezone.

These features use the existing database schema; no database migration is needed.
