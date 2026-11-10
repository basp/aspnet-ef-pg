# Sandbox

A small playground that exists to answer one specific question:

> **Does `EnableRetryOnFailure` on the PostgreSQL EF provider also cover the
> Entity Framework `DbContext` health check (`AddDbContextCheck`)?**

It matters in real deployments: `EnableRetryOnFailure` is enabled for
application queries, and then a Kubernetes liveness-style `/health` probe is
trusted to be resiliency-aware — without anyone checking whether the probe
actually gets the same retried safety net. This demo makes that assumption
testable — and, alongside it, pokes at what else the combination does under
sustained faults, hard outages, and budget exhaustion.

It doubles as a learning project for setting up a minimal ASP.NET API with
EF Core — a feel for what .NET, ASP.NET and EF offer out of the box.

## The headline findings

All evidence is from manual demo sessions (small samples — treat numbers as
observations, not contracts; full records in [`TAKEAWAYS.md`](TAKEAWAYS.md)):

- **The thesis holds.** `/health` stayed `Healthy` under sustained transient
  connection faults (21/21 and 20/20 responses across two sessions) — the
  retry strategy wraps the health-check probe just like any other query.
- **Hard-down is a long stall, not a fast failure.** With the database
  stopped, `/health` returned `503 Unhealthy` after ~194s. Client-side
  HTTP timeouts (typically 100–120s) expire *before* the server's retry
  budget is spent — and cancelling the HTTP request does not cancel the
  server-side retry ladder.
- **Faults are simulated with real `PostgresException`s at EF hook level**
  (08001 connection faults, 57P01 `admin_shutdown` failovers — on the probe
  path too, since the `NonQueryExecutingAsync` override), so the demo
  exercises the exact transient-SQLSTATE machinery the retry strategy
  recognizes.
- **Everything is tunable without recompiling** — per-class fault rates,
  retry count, delay cap — via configuration (see
  [Configuration](#configuration)).

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
         |   - EnableRetryOnFailure(Demo:Retry)|
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
  attempts, logged via `ILogger` so every simulated fault is visible in the
  console (a class's rate of `0` disables that class only):
  - `ConnectionOpeningAsync` throws SQLSTATE `08001`
    ("connection could not be established"), at the connection class's rate
    (`Demo:FaultInjection:ConnectionRatePercent`, default 50%),
  - `ReaderExecutingAsync` throws SQLSTATE `57P01` (`admin_shutdown`,
    simulated failover),
  - `NonQueryExecutingAsync` throws the same `57P01` on non-reader commands —
    which makes the EF health-check probe's `SELECT 1` faultable, so the
    probe is exercised by both fault classes; the command class's rate is
    `Demo:FaultInjection:CommandRatePercent` (default 50%).
- **Resiliency**: `pg.EnableRetryOnFailure(maxRetryCount, maxRetryDelay)`
  on the Npgsql options, read from configuration (`Demo:Retry:MaxRetryCount`,
  default 10 — and `Demo:Retry:MaxRetryDelaySeconds`, default 30s); both
  SQLSTATEs are transient per Npgsql.
- **Health checks**: `/health` registers the EF `DbContext` check with a
  `CanConnectAsync` test query so the probe travels the same connection and
  command pipeline as normal queries.

What a single probe looks like in the console when both classes inject —
fault, retry, fault again, retry, recover (log lines trimmed, timings
verbatim from a session-3 run):

```
dbug: Host.SimulatedTransientErrorInterceptor   Simulating transient error (non-query).
info: ... transient exception ... will be retried after 0ms.
dbug: Host.SimulatedConnectionFaultInterceptor  Simulating connection fault.
info: ... transient exception ... will be retried after 1071ms.
dbug: DefaultHealthCheckService                 Health check FooContext with status Healthy
                                                completed after 1672.3672ms
```

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
4. Watch the console: with the default 50% per-class fault rates, requests
   and health checks usually succeed through retried fault sequences
   (occasionally spending their retry budget — see the session evidence),
   and EF's retry activity ("A transient exception occurred... will be
   retried after Xms") is visible in the logs.

## Configuration

The demo knobs live under a `Demo` section (defaults shown in
`Host/appsettings.Development.json`; environment variables override, e.g.
`Demo__FaultInjection__ConnectionRatePercent`):

| Key                                     | Default | Meaning                                            |
|-----------------------------------------|---------|----------------------------------------------------|
| `Demo:FaultInjection:ConnectionRatePercent` | 50  | Connection-class (08001) fault probability per attempt (percent); `0` disables the connection class only. |
| `Demo:FaultInjection:CommandRatePercent`    | 50  | Command-class (57P01) fault probability per attempt (percent); `0` disables the command class only. |
| `Demo:Retry:MaxRetryCount`              | 10      | EF retry budget per operation.                     |
| `Demo:Retry:MaxRetryDelaySeconds`       | 30      | Cap for each geometric-with-jitter retry delay.    |

Missing keys fall back to those defaults; out-of-range values (rates outside
0–100, negative retry settings) fail at startup with the offending key
named. Knobs are read once at startup — restart to apply a change.

## What the sessions observed

Session records in chronological order; statements are scoped to the build
they ran against (the demo evolves — session 2 corrected session 1's
backoff reading, session 3 made the probe faultable by both fault classes).

**Session 1 (2026-11-09):**

- `/health` returned `Healthy` 21/21 under sustained transient connection
  faults. Log evidence shows `NpgsqlRetryingExecutionStrategy` wrapping the
  health-check probe (`CanConnectAsync` → `NpgsqlDatabaseCreator.ExistsAsync`):
  the **demonstrated thesis holds** — `EnableRetryOnFailure` covers
  `AddDbContextCheck`.
- **Probe-path subtlety**: at the time, the command fault injector only
  overrode `ReaderExecutingAsync`, so 57P01 faults could not reach the
  health-check path; only connection-level (08001) faults were exercised
  there.
- The retry ladder was read as exponential (0, 1, 2, 4, 8, 16, 30, ...
  seconds, capped at `maxRetryDelay`), totalling ~121s across 10 retries.
- Retry-budget exhaustion was observed a few times: ~2 minutes of silent
  stall, then `RetryLimitExceededException` surfaces as a 500.
- Health-check timings: average ~184ms, max ~1067ms (n=1; treat as
  observation, not contract).

**Session 2 (2026-11-10, `add-ef-connection-resiliency-spec` change):**
reproduced the thesis (20/20 `Healthy`); corrected the backoff reading —
delays grow **geometrically with jitter** (observed 0, ~1s, ~3s, ~7.5s, then
capped at 30s), not as the plain 0/1/2/4/8 ladder noted above; verified the
hard-down path (`503 Unhealthy` after ~194s when the database is stopped);
and proved the write path executes inserts via `ExecuteReader`, so 57P01
fault injection is reachable on writes, too. Captured as the project's
first archived OpenSpec spec.

**Session 3 (2026-11-10, `parameterize-fault-injection` change):** knobs
verified (disabled pass: zero faults; small-budget pass: delays capped at
the configured 1s with ≤2 warnings per operation); the probe now chains
57P01 non-query faults *and* 08001 connection faults to a `Healthy`
recovery; and dual-class interception raises `/health` retry-budget
exhaustion to a visible ~4% per call at the default 50% rate — so the
healthy-when-retried finding now stands alongside an honest
occasionally-unhealthy flip side.

**Session 4 (2026-11-10, `per-class-fault-rates` change):** the shared
fault rate became two per-class rates (`Demo:FaultInjection:ConnectionRatePercent`
/ `CommandRatePercent`, each defaulting to 50 — defaults preserve every
session-3 observation). Both asymmetric passes verified: connection class
disabled silenced 08001 entirely while 57P01 continued on probe, read, and
write paths; command class disabled silenced 57P01 (reader and non-query
alike) and returned probe per-attempt survival to the single-class
baseline (avg ~1s vs ~3.9s under dual-50%, recovery chains connection
faults only).

Full details and evidence: [`TAKEAWAYS.md`](TAKEAWAYS.md).
Open questions answered so far: [`QUESTIONS.md`](QUESTIONS.md).

## Status

- OpenSpec is set up and the first capability spec is captured: see
  [`openspec/specs/ef-connection-resiliency/spec.md`](openspec/specs/ef-connection-resiliency/spec.md),
  applied and archived as the change `add-ef-connection-resiliency-spec`
  on 2026-11-10 (manual verification only — automated tests become relevant
  if this idea is incorporated into a real codebase).
- The change `parameterize-fault-injection` (probe-path symmetry +
  configuration knobs) is implemented, verified in-session, and archived
  (2026-11-10); the spec now covers the symmetric probe and the
  configuration knobs — 6 requirements in total.
- The change `per-class-fault-rates` (separate connection- vs command-class
  fault rates) is implemented, verified in-session, and archived
  (2026-11-10); the spec now covers the per-class rate configurability and
  the per-class disabled-injection wording — still 6 requirements in total.
- Remaining candidate, captured for a future change: cosmetic
  deduplication of the interceptors' roll-and-throw shape (no behavior
  change expected — would be a skip-specs change).
- Framework version pairing: the host targets `net11.0` (RC SDK) while the
  EF Core and Npgsql provider packages are 10.x stable; noted here as the
  current representative pairing (see [`QUESTIONS.md`](QUESTIONS.md)).
