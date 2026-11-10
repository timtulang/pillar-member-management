# Deploy Pillar to Vercel

The app uses Vercel's Container runtime with the root Dockerfile and vercel.json.
Neon stores application data and the shared, encrypted authentication key ring.
No database migrations run automatically at startup.

## 1. Run the database migration

In the Neon SQL Editor, select the **existing database and production branch**
that your local app uses. Run all of `database/004_data_protection.sql` as the
schema/database owner. This creates only `pillar_security.data_protection_keys`
and its access grants. It is safe to run again and preserves existing records.
Do not run `neon-setup.sql` again on an existing database.

The app login needs membership in the existing `pillar_runtime` role. If you
already configured the restricted application login from README.md, this is
already in place. Otherwise, apply `database/003_runtime_permissions.sql`, create
a dedicated login through Neon, and grant it the group (replace the login name):

```sql
GRANT pillar_runtime TO pillar_app;
```

Use the app login's connection string in Vercel. Keep the owner login for schema
administration. The migration itself also works when run by the current database
owner; an owner connection has access to the new table, but the deployed app
should use the restricted login.

Optional verification, without exposing the encrypted key XML:

```sql
SELECT to_regclass('pillar_security.data_protection_keys') AS key_table;
SELECT count(*) AS stored_keys FROM pillar_security.data_protection_keys;
```

Zero keys before the first deployment is normal. The app initializes the key ring
at startup and stops with an error if it cannot access or encrypt the keys.

## 2. Keep the encryption certificate

The prepared files are in `.keys/vercel/`:

- `certificate-base64.txt`: value for `Security__DataProtectionCertificate`.
- `password.txt`: value for `Security__DataProtectionCertificatePassword`.
- `certificate.pfx`: a private backup of the certificate and RSA private key.

The folder is excluded from Git, Docker builds, and publish output. Back up all
three files privately. Reuse the same values on every production deployment.
Never put these contents in SQL or commit them to Git. The certificate encrypts
the authentication keys; it is separate from the HTTPS certificate Vercel manages.

On a different checkout, generate the files once if they do not already exist:

```bash
bash scripts/create-protection-certificate.sh
```

The script refuses to overwrite existing files. Do not generate a replacement on
every deployment. Losing the certificate prevents reading the existing key ring.
The generated certificate is valid for ten years; plan a rotation before expiry.

## 3. Import the project

Push the prepared code to a private Git repository. In Vercel, choose **Add New →
Project**, import the repository, and use the directory containing `Pillar.csproj`
and `Dockerfile` as the root. The framework preset is **Container** (also set in
vercel.json). Leave build/output overrides empty; the Dockerfile builds the app.

Vercel's Container runtime is currently beta. Its documentation defines `PORT`
as the HTTP port Vercel connects to; this Dockerfile listens on 8080.

## 4. Set Production environment variables

Add these in Vercel's project environment-variable settings **before deploying**.
Use the **Secret** type for the three credential values indicated below. Values
are plain text; do not include surrounding quotation marks.

| Name | Value | Type |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Config |
| `PORT` | `8080` | Config |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Config |
| `Security__HostingPlatform` | `Vercel` | Config |
| `Security__DataProtectionStore` | `Database` | Config |
| `Security__DataProtectionCertificate` | Entire contents of `.keys/vercel/certificate-base64.txt` | Secret |
| `Security__DataProtectionCertificatePassword` | Contents of `.keys/vercel/password.txt` | Secret |
| `ConnectionStrings__Pillar` | Your app login's Neon Npgsql connection string | Secret |
| `AllowedHosts` | Your exact production hostname, e.g. `pillar-family.vercel.app` | Config |
| `Security__RequireMfa` | `true` | Config |
| `Pillar__TimeZone` | `Asia/Manila` (or your church's timezone) | Config |

The connection string must use Npgsql key/value format and verified TLS:

```text
Host=YOUR-NEON-POOLED-HOST;Database=neondb;Username=pillar_app;Password=YOUR_PASSWORD;SSL Mode=VerifyFull;Channel Binding=Require;Pooling=true;Maximum Pool Size=20;Timeout=15;Command Timeout=30
```

Copy your actual endpoint and database name from Neon. For multiple approved
hostnames, separate names with semicolons in `AllowedHosts`. Do not include
`https://` or paths. Use your stable production alias, not just a temporary
per-deployment hostname. Other hosts intentionally return an error.

Database mode ignores the Dockerfile's `Security__DataProtectionKeyPath` setting;
there is no persistent local disk requirement. Do not use Development in Vercel.
Keep production secrets scoped to Production. Preview deployments should use a
separate Neon branch/database and their own certificate and allowed hosts.

`Security__HostingPlatform=Vercel` trusts Vercel's managed ingress to supply the
original HTTPS scheme and client IP. It must only be used in Vercel's private
container runtime, where requests reach the app through that ingress. For a VM
or directly exposed server, leave this option unset and configure
`Security__TrustedProxies` instead. Forwarded Host is never trusted by this mode.

## 5. Deploy and verify

Deploy, then open the stable HTTPS production URL. Existing administrator accounts
and authenticator enrollment work when you use the same Neon database. Switching
from the old filesystem key ring requires signing in again; it does not erase
accounts, passwords, authenticator enrollment, members, or attendance.

Check login and MFA, a member form submission, an event, and the mobile layout.
Redeploy without changing certificate settings, then verify the existing session
and form submissions still work. Camera scanning on a phone requires HTTPS.

Database availability is required for startup and normal operation. This is
intentional: the app never silently falls back to a temporary key ring.

## Development configuration

The real local connection was moved to ignored `appsettings.Local.json`. It loads
only when `ASPNETCORE_ENVIRONMENT=Development`. Environment variables and command
line values override it. Local development keeps its normal filesystem keys
unless you explicitly select Database storage.

If an actual password was committed to Git before this change, rotate that
password in Neon; removing it from the current file does not remove Git history.

## Certificate rotation (later)

Keep the existing certificate/password. Generate the replacement in a different
private directory. Set the old certificate/password as:

```text
Security__DataProtectionPreviousCertificates__0__Certificate
Security__DataProtectionPreviousCertificates__0__Password
```

Then set the replacement as `Security__DataProtectionCertificate` and
`Security__DataProtectionCertificatePassword`. Keep older certificates available
for as long as any retained key records use them; adding a new certificate does
not re-encrypt old rows. ASP.NET's normal key rotation creates new keys protected
by the current certificate. Back up the database and the necessary certificates
together. Never delete the table to fix a deployment error.

## References

- [Vercel Container runtime](https://vercel.com/docs/functions/runtimes/container)
- [Vercel request headers](https://vercel.com/docs/headers/request-headers)
- [Vercel environment variable types](https://vercel.com/changelog/environment-variables-now-use-config-and-secret-types)
- [ASP.NET Core key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0)
