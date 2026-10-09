# WatchTower workflow

WatchTower is a simple uptime monitoring platform. Users add websites or API endpoints, and the system checks whether they are online. Everything runs in one ASP.NET Core process: the REST API, the web dashboard, and the background checker. Data lives in PostgreSQL.

## 1. The whole system

The user works in the dashboard, which calls the REST API. Separately, a background service keeps checking every monitor on its own schedule and records the results. The dashboard picks up new results by polling every 10 seconds.

```mermaid
flowchart TB
  user([User])

  subgraph browser [Browser]
    dashboard["Web dashboard: HTML, CSS, JS from wwwroot"]
  end

  subgraph apiProcess [WatchTower.Api process]
    authApi["AuthController: /api/auth/register and /login"]
    jwtCheck{"Valid JWT?"}
    unauthorized["401 JSON error"]
    dataApi["MonitorsController and IncidentsController"]
    authService["AuthService: hash password, verify, issue JWT"]
    monitorService["MonitorService: validate input, filter by owner"]

    subgraph worker [Background worker]
      scheduler["MonitorSchedulerService: wakes every 5s"]
      executor["MonitorCheckExecutor: HTTP request, ping, or TCP connect, with timeout"]
      result{"Got the expected answer?"}
      upResult["CheckResult: Up"]
      downResult["CheckResult: Down"]
      tracker["IncidentTracker: open or resolve incidents"]
    end
  end

  db[("PostgreSQL: Users, Monitors, CheckResults, Incidents")]
  targets["Monitored websites and APIs"]

  user --> dashboard
  dashboard -->|"email and password"| authApi
  authApi --> authService
  authService -->|"save password hash only"| db
  authService -->|"JWT access token"| dashboard

  dashboard -->|"Bearer JWT, polls every 10s"| jwtCheck
  jwtCheck -->|No| unauthorized
  jwtCheck -->|Yes| dataApi
  dataApi --> monitorService
  monitorService -->|"only the current user's rows"| db
  monitorService -->|"JSON responses"| dashboard

  scheduler -->|"find monitors that are due"| db
  scheduler --> executor
  executor -->|"GET url"| targets
  executor --> result
  result -->|Yes| upResult
  result -->|"No: wrong code, timeout, DNS or TLS error"| downResult
  upResult --> tracker
  downResult --> tracker
  tracker -->|"save check, monitor status, incident changes"| db
```

## 2. Registering, logging in, and calling protected endpoints

Passwords are hashed before they are saved; the plain password is never stored. After login, every request to a protected endpoint carries the JWT, and the API only returns rows owned by the user in that token.

```mermaid
sequenceDiagram
  actor User
  participant Dashboard
  participant API as WatchTower API
  participant DB as PostgreSQL

  User->>Dashboard: Enter email and password
  Dashboard->>API: POST /api/auth/register
  API->>API: Validate email and password length, then hash the password
  alt Email already registered
    API-->>Dashboard: 409 Conflict
  else New email
    API->>DB: Insert user with the password hash
    API-->>Dashboard: 201 userId and email
  end

  Dashboard->>API: POST /api/auth/login
  API->>DB: Find user by email
  API->>API: Verify the password against the stored hash
  alt Wrong email or password
    API-->>Dashboard: 401 Invalid email or password
  else Correct
    API-->>Dashboard: 200 accessToken, a JWT valid for 60 minutes
    Dashboard->>Dashboard: Store the token in localStorage
  end

  Dashboard->>API: GET /api/monitors with Authorization Bearer token
  API->>API: Check JWT signature, issuer, audience and expiry
  API->>DB: Select monitors where UserId matches the token
  API-->>Dashboard: 200 JSON list of monitors
```

## 3. Adding a monitor

The Add monitor page has a sidebar with three monitor types. Picking one swaps the form in the middle to the inputs that type needs:

| Type | Inputs | What counts as "online" |
|------|--------|-------------------------|
| Website URL | URL, expected HTTP status | A GET request returns the expected status |
| IP address | IP, then either Ping or a TCP port | The host answers a ping, or the port accepts a TCP connection |
| API endpoint | Method, URL, headers, body, expected status, optional "response must contain" text | The response has the expected status and, if set, contains the text |

Every type also has a name, a check interval and a timeout. The API rejects bad settings before anything is saved.

```mermaid
flowchart LR
  pick["User picks Website, IP address or API in the sidebar"] --> form["Fills in the inputs for that type"]
  form --> post["POST /api/monitors with type"]
  post --> validate{"Settings valid for that type?"}
  validate -->|No| badRequest["400 JSON error shown in the form"]
  validate -->|Yes| save["Save monitor with status Pending"]
  save --> created["201 JSON monitor"]
  created --> firstCheck["Background worker checks it within about 5 seconds"]
```

The validation rules are:

- URLs must be absolute `http` or `https` URLs (`https` only, outside development).
- URLs and IP addresses must not point at `localhost` or a private, loopback or link-local address.
- The interval must be between 30 seconds and 24 hours.
- The timeout must be greater than zero and shorter than the interval.
- The expected status code must be between 100 and 599.
- TCP ports must be between 1 and 65535.
- API monitors: GET and HEAD requests can't have a body, HEAD can't check response text, and headers like `Host` or `Content-Length` are not allowed.

## 4. One background check cycle

The scheduler wakes every 5 seconds, finds monitors whose next check is due, and runs up to 10 checks at a time. Each result is compared with the monitor's previous state to decide whether an incident starts or ends.

```mermaid
sequenceDiagram
  participant Scheduler as MonitorSchedulerService
  participant DB as PostgreSQL
  participant Executor as MonitorCheckExecutor
  participant Target as Website, server or API
  participant Tracker as IncidentTracker

  loop Every 5 seconds
    Scheduler->>DB: Find enabled monitors where last check plus interval has passed
    loop Each due monitor, up to 10 in parallel
      Scheduler->>Executor: Run the check
      alt Website or API
        Executor->>Target: HTTP request (method, headers, body), cancelled after the timeout
      else IP address with a port
        Executor->>Target: TCP connect to ip:port
      else IP address without a port
        Executor->>Target: ICMP ping
      end
      alt Expected status and text, port open, or ping reply
        Target-->>Executor: Answer in time
        Executor-->>Scheduler: CheckResult Up, with status code and response time
      else Wrong status, missing text, timeout, refused or unreachable
        Executor-->>Scheduler: CheckResult Down, with an error message and failure reason
      end
      Scheduler->>Tracker: Compare the result with the previous state
      alt Was up or never checked, now down
        Tracker->>DB: Add a new open incident and queue a "DOWN" email
      else Was down, now up
        Tracker->>DB: Mark the open incident as resolved and queue a "RESOLVED" email
      else Status did not change
        Tracker->>Tracker: Nothing to do
      end
      Scheduler->>DB: Save the check result, monitor status, incident and email in one transaction
    end
  end
```

## 4b. Sending the alert emails

Emails are queued in the database (an "outbox") and sent by a separate background worker, so a slow or broken mail server never holds up the checks and no email is lost if the app restarts.

```mermaid
sequenceDiagram
  participant Dispatcher as NotificationDispatcherService
  participant DB as PostgreSQL
  participant SMTP as Mail server
  participant User

  loop Every 5 seconds
    Dispatcher->>DB: Find pending emails that are due
    loop Each email
      Dispatcher->>SMTP: Send the email
      alt Accepted
        Dispatcher->>DB: Mark as Sent
        SMTP-->>User: "DOWN: Payments service" with an explanation of what went wrong
      else Mail server error
        Dispatcher->>DB: Record the error and retry later (30s, 1m, 2m ... up to 30m)
        Note over Dispatcher,DB: After the maximum number of attempts, mark as Failed
      end
    end
  end
```

The explanation comes from the check's failure reason. For example, a timeout says the server didn't respond in time, a 503 says the service is unavailable (often maintenance or overload), a DNS failure says the domain couldn't be resolved, and a closed TCP port says nothing accepted the connection.

## 5. Monitor status as the user sees it

The dashboard shows one of four statuses for each monitor. Incidents open and close on the transitions between Up and Down.

```mermaid
stateDiagram-v2
  [*] --> Pending: Monitor created
  Pending --> Up: First check passes
  Pending --> Down: First check fails, incident opened
  Up --> Down: Check fails, incident opened
  Down --> Up: Check passes, incident resolved
  Pending --> Paused: User pauses
  Up --> Paused: User pauses
  Down --> Paused: User pauses
  Paused --> Pending: Resumed, never checked
  Paused --> Up: Resumed, last check passed
  Paused --> Down: Resumed, last check failed
```

## Where each step lives in the code

| Step | Code |
|------|------|
| Web UI: Dashboard, Add monitor, Incidents | `WatchTower.Api/wwwroot/` (`js/dashboard.js`, `js/monitor-form.js`, `js/incidents.js`) |
| Incident report and filters | `IncidentsController`, `IncidentService` |
| Register and login | `AuthController`, `AuthService`, `JwtTokenService` |
| JWT validation and JSON 401 | `Program.cs` |
| Monitor CRUD and ownership checks | `MonitorsController`, `MonitorService` |
| Settings validation | `MonitorValidator`, `UrlSafetyValidator` |
| Background scheduling | `MonitorSchedulerService` |
| HTTP, ping and TCP checks | `MonitorCheckExecutor` |
| Incident rules | `IncidentTracker` |
| Alert email text and explanations | `IncidentEmailComposer`, `IncidentExplainer` |
| Queuing and sending alert emails | `IncidentNotifier`, `NotificationDispatcherService`, `EmailNotificationProcessor`, `SmtpEmailSender` |
| Error responses | `ExceptionHandlingMiddleware` |
| Database tables and relationships | `AppDbContext` |
