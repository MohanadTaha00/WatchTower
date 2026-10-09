# WatchTower

Uptime monitoring platform: register users, add website, IP address and API monitors, background checks (HTTP, ping, TCP), check history, and an incident report. JSON REST API with JWT authentication.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (for PostgreSQL)

## Quick start

1. Start PostgreSQL and Mailpit (a local test mail server for alert emails):

```bash
docker compose up -d postgres mailpit
```

2. Run the API (applies migrations on startup):

```bash
dotnet run --project WatchTower.Api
```

3. Open the dashboard at `http://localhost:5073/`, or the API docs at `http://localhost:5073/swagger`.

## Dashboard

The API serves a small web app from `WatchTower.Api/wwwroot` (plain HTML, CSS and JavaScript modules, no build step). After logging in (the JWT is kept in the browser's local storage), the top bar switches between three pages:

- **Dashboard** (`#/dashboard`): every monitored endpoint with its type, status (Up, Down, Pending, Paused), response time and last check. From here you can edit, pause, resume and delete a monitor, or click **Details** to see:
  - Uptime for the last 24 hours, 7 days and 30 days, plus the average and 95th-percentile response time.
  - A response time chart for the chosen range (24 hours in 30-minute buckets, 7 days in 3-hour buckets, 30 days in 12-hour buckets). It plots the average of successful checks, marks periods with failed checks in red, and shows the details of each period on hover.
  - The recent check history.
- **Add monitor** (`#/add`): a sidebar to pick what you're checking, and a form in the middle with the inputs for that type:
  - *Website URL*: URL and expected HTTP status.
  - *IP address*: IP address, checked with ping or by connecting to a TCP port.
  - *API endpoint*: method, URL, headers, request body, expected status, and optional text the response must contain.
- **Incidents** (`#/incidents`): a report for a chosen period, with total, open and resolved counts, average time to resolve, total downtime, a per-monitor breakdown, and a filterable list showing whether each incident has been resolved. The nav shows a badge with the number of open incidents.

Pages refresh every 10 seconds by polling the REST API.

## Passwords

Passwords are stored only as salted PBKDF2 hashes, so nobody (including an admin with database access) can read them back. Instead:

- **Change password** (`#/account`, opened by clicking your email in the top bar): enter the current password and a new one. You stay signed in, and every other browser or device is signed out.
- **Forgot password** (link on the login form): enter your email and WatchTower sends a reset link to it (the response is the same whether or not the account exists). The link works once, expires after `PasswordReset:TokenLifetimeMinutes` (30), and only its SHA-256 hash is stored. Requests for the same account are limited to one per `PasswordReset:ResendCooldownSeconds` (60). Setting the new password signs out all existing sessions.

Sessions are revoked through a security stamp: every JWT carries the user's current stamp, the API checks it on each request, and changing or resetting the password rotates it. Reset emails go through the same SMTP settings as alerts (see below), so set up email first.

API: `POST /api/auth/change-password` (logged in, returns a new token), `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`.

## Two-factor authentication

Optional, per user, turned on from the Account page. Each user picks one method:

- **Authenticator app**: scan a QR code with Google Authenticator, Microsoft Authenticator, 1Password or similar, then confirm with a code. Codes follow RFC 6238 (6 digits, 30 seconds); a code is accepted for one step either side for clock drift and can only be used once. The secret is encrypted in the database with ASP.NET Data Protection.
- **Email code**: confirm a code sent to your address; afterwards each login emails a fresh 6-digit code (through the same SMTP settings as alerts).

Turning it on shows 10 single-use recovery codes (stored hashed) for when the phone or inbox is unavailable. To switch methods, turn it off (password required) and set up the other one.

At login, a correct password returns `{ "requiresTwoFactor": true, "challengeToken": ..., "method": ... }` instead of a token. The client then posts the code (or a recovery code) with the challenge token to `POST /api/auth/login/2fa`. A challenge expires after `TwoFactor:ChallengeLifetimeMinutes` (10) and is burned after `TwoFactor:MaxAttempts` (5) wrong codes. Email codes can be resent with `POST /api/auth/login/2fa/resend`.

Account endpoints (logged in): `GET /api/account/2fa`, `POST /api/account/2fa/authenticator/setup|enable`, `POST /api/account/2fa/email/setup|enable`, `POST /api/account/2fa/disable`, `POST /api/account/2fa/recovery-codes`.

Data Protection keys live in your user profile by default (`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` on Windows). On a server, set `DataProtection:KeysPath` to a persistent folder: if the keys are lost, authenticator secrets can't be decrypted and those users must log in with a recovery code.

## Email alerts

When a monitor goes down, WatchTower emails the monitor's owner (the email address they registered with). The email explains the incident in plain language, for example "responded with HTTP 503 (Service Unavailable) instead of the expected 200. The service says it is unavailable, which usually means maintenance or overload", along with the target, the time it was detected, the HTTP status, the response time and the technical error. When the monitor comes back up, a second email says how long the outage lasted.

How it works:

- When an incident opens or resolves, the email is written into the `EmailNotifications` table in the same database save as the incident. A slow or broken mail server never delays checks, and no email is lost if the app restarts.
- The `NotificationDispatcherService` background worker sends queued emails every few seconds over SMTP. Failed sends are retried with growing delays (30s, 1m, 2m … up to 30m); after `Notifications:MaxAttempts` tries the email is marked `Failed`.
- Only changes between up and down send email. A monitor that stays down doesn't send repeat emails.

By default, emails go to Mailpit: open `http://localhost:8025` to see them.

### Sending real emails with Gmail

1. Turn on 2-Step Verification for your Google account, then create an App Password at <https://myaccount.google.com/apppasswords> (name it "WatchTower" and copy the 16-character code).
2. Store the login in .NET user-secrets. They're kept in your user profile, outside the repository, so the password is never committed:

```bash
dotnet user-secrets set "Smtp:Host" "smtp.gmail.com" --project WatchTower.Api
dotnet user-secrets set "Smtp:Port" "587" --project WatchTower.Api
dotnet user-secrets set "Smtp:Security" "StartTls" --project WatchTower.Api
dotnet user-secrets set "Smtp:Username" "you@gmail.com" --project WatchTower.Api
dotnet user-secrets set "Smtp:FromAddress" "you@gmail.com" --project WatchTower.Api
dotnet user-secrets set "Smtp:Password" "your16charapppassword" --project WatchTower.Api
```

3. Restart the API, log in with an account registered under the address that should receive alerts, and send a test email with `POST /api/notifications/test` (for example from Swagger at `/swagger`). If it fails, the response contains the mail server's error message.

User-secrets only apply in the Development environment. In production, set the same keys as environment variables (`Smtp__Host`, `Smtp__Password`, ...) or in your host's secret store.

Other providers work the same way, for example:

```json
"Smtp": { "Host": "smtp.sendgrid.net", "Port": 587, "Security": "StartTls", "Username": "apikey", "Password": "<secret>", "FromAddress": "alerts@yourdomain.com" },
"Notifications": { "DashboardUrl": "https://watchtower.yourdomain.com/" }
```

Set `Notifications:Enabled` to `false`, or leave `Smtp:Host` empty, to turn email off.

## Configuration

| Setting | Location | Notes |
|---------|----------|--------|
| Database | `ConnectionStrings:DefaultConnection` | Default matches `docker-compose.yml` (host port 5433, so it doesn't clash with a locally installed PostgreSQL on 5432) |
| JWT secret | `Jwt:Key` | Required, 32+ bytes. Empty in `appsettings.json`; `appsettings.Development.json` has a local-only key. See [Secrets and production settings](#secrets-and-production-settings) |
| Scheduler | `Scheduler:*` | Poll interval and max concurrent checks |
| Monitoring | `Monitoring:*` | HTTPS requirement, interval bounds, SSRF host blocks |
| Email server | `Smtp:*` | Host, port, security (`None`, `StartTls`, `SslOnConnect`, `Auto`), login and sender. Defaults to Mailpit on `localhost:1025` |
| Email alerts | `Notifications:*` | On/off, dashboard link used in emails, send interval, max retries |
| Rate limiting | `RateLimiting:AuthPermitsPerMinute` | Per-IP limit on `/api/auth/*` (returns 429) |
| CORS | `Cors:AllowedOrigins` | Explicit origins for a browser frontend |

Development relaxes `RequireHttpsUrls` in `appsettings.Development.json`.

### Secrets and production settings

No real secrets are stored in this repository. The values that are committed are for local development only:

- The database login (`watchtower` / `watchtower`) matches the Docker Compose containers, which only listen on `127.0.0.1`.
- The JWT key in `appsettings.Development.json` is public, so it's only used when `ASPNETCORE_ENVIRONMENT` is `Development`. Outside Development the API refuses to start until you set your own key.

Keep real values out of the repo: use `dotnet user-secrets` on your machine (stored in your user profile), and environment variables or your host's secret store on a server. Environment variables use `__` in place of `:`:

```bash
Jwt__Key=<at least 32 random characters>
ConnectionStrings__DefaultConnection="Host=...;Database=watchtower;Username=...;Password=..."
Smtp__Host=smtp.gmail.com
Smtp__Username=you@gmail.com
Smtp__Password=<app password>
Smtp__FromAddress=you@gmail.com
Notifications__DashboardUrl=https://your-domain/
DataProtection__KeysPath=/var/watchtower/keys
```

To generate a key in PowerShell: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`, or with OpenSSL: `openssl rand -base64 48`.

## Example API flow

Register:

```bash
curl -s -X POST http://localhost:5073/api/auth/register ^
  -H "Content-Type: application/json" ^
  -d "{\"email\":\"user@example.com\",\"password\":\"password123\"}"
```

Login:

```bash
curl -s -X POST http://localhost:5073/api/auth/login ^
  -H "Content-Type: application/json" ^
  -d "{\"email\":\"user@example.com\",\"password\":\"password123\"}"
```

Create a monitor (replace `TOKEN`):

```bash
curl -s -X POST http://localhost:5073/api/monitors ^
  -H "Authorization: Bearer TOKEN" ^
  -H "Content-Type: application/json" ^
  -d "{\"name\":\"Example\",\"type\":\"Website\",\"url\":\"https://example.com\",\"intervalSeconds\":60,\"timeoutSeconds\":10,\"expectedStatusCode\":200}"
```

`type` is `Website` (the default), `IpAddress` or `Api`. Other examples of the request body:

```json
{ "name": "DB server", "type": "IpAddress", "ipAddress": "203.0.113.10", "port": 5432, "intervalSeconds": 60, "timeoutSeconds": 5 }
{ "name": "Gateway", "type": "IpAddress", "ipAddress": "203.0.113.1", "intervalSeconds": 60, "timeoutSeconds": 5 }
{ "name": "Orders API", "type": "Api", "httpMethod": "POST", "url": "https://api.example.com/orders/health",
  "requestHeaders": { "X-Api-Key": "secret" }, "requestBody": "{\"ping\":true}",
  "expectedStatusCode": 200, "expectedBodyContains": "\"status\":\"ok\"", "intervalSeconds": 60, "timeoutSeconds": 10 }
```

An `IpAddress` monitor without a `port` is checked with ping; with a `port` it's checked by opening a TCP connection.

List monitors, check history, incidents, and the incident report:

```bash
curl -s http://localhost:5073/api/monitors -H "Authorization: Bearer TOKEN"
curl -s "http://localhost:5073/api/monitors/{id}/checks?page=1&pageSize=20" -H "Authorization: Bearer TOKEN"
curl -s "http://localhost:5073/api/monitors/{id}/stats?range=7d" -H "Authorization: Bearer TOKEN"   # range: 24h, 7d or 30d
curl -s "http://localhost:5073/api/incidents?status=open&days=30&monitorId={id}" -H "Authorization: Bearer TOKEN"
curl -s "http://localhost:5073/api/incidents/summary?days=30" -H "Authorization: Bearer TOKEN"
```

## Health

- `GET /health` — liveness (process is up; no dependency checks)
- `GET /health/ready` — readiness (includes database connectivity)

## Tests

```bash
dotnet test WatchTower.slnx
```

Integration tests run the full API in-process against an in-memory database (no PostgreSQL needed) and cover JWT auth, password hashing, monitor CRUD for all three types, ownership isolation, validation, the incident report, and health. Unit tests cover the check executor (HTTP via a stub `HttpMessageHandler`, plus real TCP and ping against localhost) and incident open/resolve transitions.

## Architecture

See [docs/WORKFLOW.md](docs/WORKFLOW.md) for diagrams of the full workflow: system overview, login, adding a monitor, the background check cycle, and monitor statuses.

- **WatchTower.Api** — HTTP controllers, JWT, Swagger, exception handling
- **WatchTower.Application** — DTOs, validation, service contracts
- **WatchTower.Domain** — entities
- **WatchTower.Infrastructure** — EF Core, auth, monitor checks, background scheduler
- **WatchTower.Tests** — xUnit integration and unit tests

Background `MonitorSchedulerService` runs each monitor's check (HTTP request, ping or TCP connect) on its interval, records results, and opens/closes incidents on up/down transitions, queuing an alert email each time. `NotificationDispatcherService` delivers those emails. `IMonitorEventPublisher` is a no-op hook for future SignalR realtime updates.

## Solution commands

```bash
dotnet build WatchTower.slnx
dotnet ef migrations add MigrationName -p WatchTower.Infrastructure -s WatchTower.Api -o Persistence/Migrations
```
