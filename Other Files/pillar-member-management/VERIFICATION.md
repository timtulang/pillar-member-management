# Verification record

Verified locally on 10 November 2026 using the .NET 10 SDK, PostgreSQL 17, Chromium, and a restricted PostgreSQL runtime login. All member/event/staff data used during verification was synthetic.

- Debug build: passed, zero warnings/errors.
- Release publish: passed.
- Combined Neon setup SQL: applied successfully to a fresh PostgreSQL database.
- Database invariants: passed, including overlapping household periods, cyclic ancestry, overlapping spouse periods across either spouse column, one main ministry, one unrevoked card, card/person ownership, duplicate attendance, and audit/DDL permission restrictions.
- Concurrent opposing-parent and shared-spouse writes: one transaction succeeds and the conflicting transaction is rejected.
- Browser flows: passed for MFA, member creation, birthday editing, displayed family dates, household transition after marriage, stale edits, ministries, discipleship, baptism, card issuance, actual QR decoding through a simulated camera stream, manual check-in, concurrent duplicate scans, expired/revoked/invalid cards, attendance corrections, closed/cancelled events, CSRF rejection, staff authorization, account disablement, and password changes.
- Responsive review: desktop and 390-pixel mobile layouts inspected. No horizontal page overflow or browser JavaScript errors in the tested flows.
- NuGet vulnerability check, including transitive dependencies: no known vulnerable packages reported by the configured NuGet source at verification time.

The script has not been run against the user's Neon account, and the application has not been publicly deployed. A physical phone camera or USB scanner was not available; the automated camera test exercised the actual browser scanner with the generated QR image via a canvas video stream. Validate your deployment's HTTPS, reverse-proxy configuration, phone permissions, database role grants, and backup/restore process in its target environment.
