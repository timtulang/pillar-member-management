# Third-party components

The app references ASP.NET Core / Entity Framework Core, Npgsql, Dapper, and QRCoder through NuGet. Exact resolved versions and dependency hashes are recorded in `packages.lock.json`; the corresponding upstream licenses apply to those packages.

The browser QR decoder is jsQR 1.4.0, bundled unmodified at `wwwroot/vendor/jsQR.js`. Its Apache 2.0 license is included at `wwwroot/vendor/jsQR.LICENSE`. The scanner is self-hosted and does not load code from a third-party CDN at runtime.

The verification tools use Playwright and psycopg; they are development-only requirements listed under `tests/`.
