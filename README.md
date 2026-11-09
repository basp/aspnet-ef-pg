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
  that throw real `PostgresException`s on 50% of attempts, logged via
  `ILogger` so every simulated fault is visible in the console:
  - `ConnectionOpeningAsync` throws SQLSTATE `08001`
    ("connection could not be established"),
  - `ReaderExecutingAsync` throws SQLSTATE `57P01` (`admin_shutdown`,
    simulated failover).
- **Resiliency**: `pg.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: 30s)`
  on the Npgsql options; both SQLSTATEs are transient per Npgsql.
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

   Note: there are no migrations in this repo — the `Items` table must exist
   (e.g. created once via a prior session or `EnsureCreated`) for `/items`
   to work; `/health` only needs a reachable database.
2. `dotnet run --project Host` → `http://localhost:5264`.
3. Endpoints:

   | Route        | What it does                                        |
   |--------------|-----------------------------------------------------|
   | `/`          | Hello world                                         |
   | `/health`    | Health checks (the demonstration)                   |
   | `/items`     | `SELECT *` through EF (ordinary query path)        |
   | `POST /items`| Add one item (body: `{"name": "...", "description": "..."}`) |
4. Watch the console: despite the 50% injected fault rate, requests and
   health checks keep succeeding, and EF's retry activity
   ("A transient exception occurred... will be retried after Xms") is
   visible in the logs.

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

- OpenSpec is initialized; a single capability spec about health checks ×
  connection resiliency is planned (manual verification only — automated
  tests become relevant if this idea is incorporated into a real codebase).
- Candidate improvements are captured but deliberately not implemented yet:
  make fault injection reachable on the health-check probe and write paths,
  README/demo cleanup (vestigial TODO comment, unused SQLite package
  reference), and parameterizing the fault rate.
