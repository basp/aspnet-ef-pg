# Sandbox

Just a small playground to demonstrate ideas, problems and other fun stuff.

Currently it demonstrates the ASP.NET health check framework and EF connection
resiliency in a very narrow context: whether `EnableRetryOnFailure` on the
PostgreSQL EF provider also covers Entity Framework `DbContext` health checks
(`AddDbContextCheck`). It doubles as a learning project for setting up a
minimal ASP.NET API with EF Core — a feel for what .NET, ASP.NET and EF offer
out of the box.

## How it works

```
                         REQUESTS
                             |
     /health  ------------------------------
     /items   ----------------------        |
     POST /items ---------------     |      |
                         |           v      v
         +---------------v---------------------+
         |              Program.cs             |
         |                                     |
         |  DbContext: FooContext              |
         |   - UseNpgsql(...)                  |
         |   - EnableRetryOnFailure(10, 30s)   |
         |   - + 2 fault-injection interceptors|
         |                                     |
         |  Health checks:                     |
         |   - AddDbContextCheck<FooContext>   |  <-- the point of the demo
         |   - AddCheck<FooHealthCheck>        |  <-- trivial always-healthy baseline
         +-------+-----------------------------+
                 |
                 v
       PostgreSQL @ localhost:5432
```

- **Fault injection** (`Host/SimulatedConnectionFaultInterceptor.cs` and
  `Host/SimulatedTransientErrorInterceptor.cs`): two scoped DI interceptors
  that throw real `PostgresException`s on a configurable fraction of
  attempts (`Demo:FaultInjection:RatePercent`, default 50%), logged via
  `ILogger` so every simulated fault is visible in the console (a rate of
  `0` disables injection):
  - `ConnectionOpeningAsync` throws SQLSTATE `08001`
    ("connection could not be established"),
  - `ReaderExecutingAsync` throws SQLSTATE `57P01` (`admin_shutdown`,
    simulated failover),
  - `NonQueryExecutingAsync` throws the same `57P01` on non-reader commands —
    which makes the EF health-check probe's `SELECT 1` faultable, so the
    probe is exercised by both fault classes.
- **Resiliency**: `pg.EnableRetryOnFailure(maxRetryCount, maxRetryDelay)`
  on the Npgsql options, read from configuration (`Demo:Retry:MaxRetryCount`,
  default 10 — and `Demo:Retry:MaxRetryDelaySeconds`, default 30s); both
  SQLSTATEs are transient per Npgsql.
- **Health checks**: `/health` registers the EF `DbContext` check with a
  `CanConnectAsync` test query so the probe travels the same connection and
  command pipeline as normal queries.

## How to run

1. Provide PostgreSQL on `localhost:5432` with username `postgres`,
   password `foo`, database `postgres` (see
   `Host/appsettings.Development.json`), e.g.:

   ```
   docker run --name some-postgres -e POSTGRES_PASSWORD=foo -p 5432:5432 -d postgres
   ```

   The schema is captured by an `InitialCreate` migration in
   `Host/Migrations/`, but the app does not apply migrations at startup —
   run `dotnet ef database update --project Host` once, or make sure the
   `Items` table already exists (e.g. from a prior session). `/health` only
   needs a reachable database.
2. `dotnet run --project Host` → `http://localhost:5264`.
3. Endpoints:

   | Route        | What it does                                        |
   |--------------|-----------------------------------------------------|
   | `/`          | Hello world                                         |
   | `/health`    | Health checks (the demonstration)                   |
   | `/items`     | `SELECT *` through EF (ordinary query path)        |
   | `POST /items`| Add one item (body: `{"name": "...", "description": "..."}`) |
4. Watch the console: despite the 50% injected fault rate, requests and
   health checks keep succeeding apart from occasional retry-budget
   exhaustions, and EF's retry activity
   ("A transient exception occurred... will be retried after Xms") is
   visible in the logs.

## Configuration

The demo knobs live under a `Demo` section (defaults shown in
`Host/appsettings.Development.json`; environment variables override, e.g.
`Demo__FaultInjection__RatePercent`):

| Key                                | Default | Meaning                                            |
|------------------------------------|---------|----------------------------------------------------|
| `Demo:FaultInjection:RatePercent`  | 50      | Fault probability per attempt (percent); `0` disables. |
| `Demo:Retry:MaxRetryCount`         | 10      | EF retry budget per operation.                     |
| `Demo:Retry:MaxRetryDelaySeconds`  | 30      | Cap for each geometric-with-jitter retry delay.    |

Missing keys fall back to those defaults; out-of-range values (rate outside
0–100, negative retry settings) fail at startup with the offending key
named. Knobs are read once at startup — restart to apply a change.

## What we observed (single session, 2026-11-09)

- `/health` returned `Healthy` 21/21 under sustained transient connection
  faults. Log evidence shows `NpgsqlRetryingExecutionStrategy` wrapping the
  health-check probe (`CanConnectAsync` → `NpgsqlDatabaseCreator.ExistsAsync`):
  the **demonstrated thesis holds** — `EnableRetryOnFailure` covers
  `AddDbContextCheck`.
- **Probe-path subtlety**: the health-check probe runs `SELECT 1` as
  `ExecuteNonQuery`, and the command fault injector only overrides
  `ReaderExecutingAsync` — so SQLSTATE 57P01 faults cannot reach the
  health-check path; only connection-level (08001) faults are exercised there.
  *(2026-11-10 note, change `parameterize-fault-injection`: the injector now
  also overrides `NonQueryExecutingAsync`, so 57P01 faults DO reach the
  probe in the current build; this paragraph describes runs against the
  earlier build.)*
- The retry ladder is exponential (0, 1, 2, 4, 8, 16, 30, ... seconds, capped
  at `maxRetryDelay`), totalling ~121s across 10 retries.
- Retry-budget exhaustion is rare under this fault rate but was observed a
  few times: ~2 minutes of silent stall, then `RetryLimitExceededException`
  surfaces as a 500. Client HTTP timeouts (typically ~100–120s) expire
  before the server's retry budget is spent — and cancelling the HTTP
  request does not cancel the server-side retry ladder.
- Health-check timings: average ~184ms, max ~1067ms (n=1; treat as
  observation, not contract).

Full details and evidence: [`TAKEAWAYS.md`](TAKEAWAYS.md)
(second session, 2026-11-10, additionally verified the hard-down behavior —
`/health` returning `503 Unhealthy` after ~194s when the database is stopped —
and the write path: inserts execute via `ExecuteReader`, so SQLSTATE 57P01
fault injection is reachable on writes, too).
Open questions answered so far: [`QUESTIONS.md`](QUESTIONS.md).

## Status

- OpenSpec is set up and the first capability spec is captured: see
  [`openspec/specs/ef-connection-resiliency/spec.md`](openspec/specs/ef-connection-resiliency/spec.md),
  applied and archived as the change `add-ef-connection-resiliency-spec`
  on 2026-11-10 (manual verification only — automated tests become relevant
  if this idea is incorporated into a real codebase).
- Candidate improvements are captured but deliberately not implemented yet:
  make fault injection reachable on the health-check probe (adding a
  `NonQueryExecutingAsync` override would require a spec delta, since the
  current spec pins the probe path to connection-level faults by construct),
  and parameterizing the fault rate and retry settings.
- Framework version pairing: the host targets `net11.0` (RC SDK) while the
  EF Core and Npgsql provider packages are 10.x stable; noted here as the
  current representative pairing (see [`QUESTIONS.md`](QUESTIONS.md)).
