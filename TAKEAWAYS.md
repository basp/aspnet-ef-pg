# TAKEAWAYS

Notes from a guided run-through of the `EnableRetryOnFailure` + health-check
demonstration (2026-11-09). Evidence comes from log analysis of a single
session; treat measured numbers as observations (n=1), not contract.

## What the demo is for

Demonstrate that `EnableRetryOnFailure` on the Npgsql EF provider
(`Npgsql.EntityFrameworkCore.PostgreSQL`) also applies to the Entity Framework
`DbContext` health check registered via `AddDbContextCheck`.

Configuration (Host/Program.cs):

- `pg.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: 30s)`
- Fault injection via two scoped interceptors, 50% chance each attempt:
  - `SimulatedConnectionFaultInterceptor` (`ConnectionOpeningAsync`)
    throws `PostgresException` 08001 ("connection could not be established").
  - `SimulatedTransientErrorInterceptor` (`ReaderExecutingAsync`)
    throws `PostgresException` 57P01 (`admin_shutdown`, simulated failover).
- `/health` registers `AddDbContextCheck<FooContext>` (custom test query:
  `CanConnectAsync`) plus a trivial always-healthy `FooHealthCheck` baseline.
- `/items` and `POST /items` exercise the ordinary query/command path.

## Verified observations

1. **Thesis holds.** `/health` returned `Healthy` 21/21 while the connection
   interceptor faulted ~50% of connection attempts. Log evidence shows the
   `NpgsqlRetryingExecutionStrategy` wrapping the health-check probe
   (`CanConnectAsync` -> `NpgsqlDatabaseCreator.ExistsAsync`):
   fault (08001) -> "A transient exception occurred. The operation will be
   retried after Xms." -> re-open -> check completes healthy.
2. **Probe path subtlety.** The health-check probe executes `SELECT 1` as
   `ExecuteNonQuery`. `SimulatedTransientErrorInterceptor` only overrides
   `ReaderExecutingAsync`, so 57P01 command faults NEVER occur on the
   `/health` path; only 08001 connection faults are exercised there.
3. **Table setup.** No migrations exist in this repo; the `Items` table was
   present from a prior session. `CanConnectAsync` works against any
   reachable database, so `/health` is unaffected; `/items` would return a
   non-transient 42P01 ("relation does not exist") if the table were missing
   and would NOT be retried.
4. **Retry budget vs client patience.** Retry delay ladder is exponential
   with 0ms first delay, doubling, capped at `maxRetryDelay`:
   0, 1, 2, 4, 8, 16, 30, 30, 30... seconds. Total budget for 10 retries is
   ~121s. Three unlucky requests each exhausted all 10 retries, and
   `RetryLimitExceededException` surfaced as a 500 after ~2 minutes. A
   client's HttpClient timeout (120s in the test loop) expires first, so the
   caller sees a timeout while the server continues retrying in the
   background — cancellation of the HTTP request does not cancel the EF
   retry ladder, which outlives it by seconds-to-minutes.
5. **Timing stats (single session).** 21 health checks: average ~184ms,
   max ~1067ms under sustained fault injection. Session totals: 57 retry
   events logged, 85 connection faults (08001), 64 command faults (57P01),
   3 retry-budget exhaustions (one such exception gets logged 3x by
   different loggers, so log-line counts are not request counts).

## Candidate take-aways / open threads

- **Spec capture (next).** The demo's intent and observed behaviors exist
  only in code, README, and this session; no OpenSpec specs or changes are
  captured yet. An initial capability spec should scope strictly to what the
  demo *as built* demonstrates; run numbers (184ms etc.) are artifacts of a
  single session and must not become requirements.
- **Demo improvement candidate.** The health-check probe path only exercises
  connection-level faults. Adding a `NonQueryExecutingAsync` override to
  `SimulatedTransientErrorInterceptor` would let the 57P01 fault reach the
  `AddDbContextCheck` probe, making the demo fully symmetric.
- **Doc/cleanup candidates.**
  - `SimulatedConnectionFaultInterceptor.cs` has a vestigial
    `// TODO: Simulate 08001` comment (the code already does this).
  - `Host.csproj` references `Microsoft.EntityFrameworkCore.Sqlite`, which
    is unused.
  - README does not state the demo's point; worth a paragraph.
- **Config knob idea.** Fault rate (50%) and retry settings are hardcoded;
  parameterizing them would make the demo easier to reason about and safer
  to run repeatedly.

## Session 2 (2026-11-10) — spec-apply experiments

Second session, run while applying the `add-ef-connection-resiliency-spec`
change (baseline re-confirmation plus the two scheduled experiments).

**Reproduced from session 1:**
- The thesis, cleanly: 20/20 `/health` responses `Healthy` under sustained
  connection-fault injection, retry warnings in logs, zero 57P01 activity
  anywhere near the probe path (still zero across both sessions on that
  path).
- One clean `/health` roll measured the true no-fault baseline: ~119ms.

**Newly learned:**
- **Backoff has jitter.** Retry delays are geometric with random jitter
  (observed: 0, ~1s, ~3s, ~7.5s, then capped at 30s), not the plain
  0, 1, 2, 4, 8 ladder earlier noted. The spec delta was revised accordingly
  during this session's reconciliation.
- **Hard-down path (previously unobserved).** With the database stopped,
  `/health` returned **HTTP 503 Unhealthy after ~194s** — 10 escalating
  capped retries plus ~7s per failed connection attempt
  (`Health check FooContext with status Unhealthy completed after
  193477ms`, message `(null)`). The retry-limit-exceeded failure is NOT
  logged on the health-check path; only the retry warnings are. Recovery
  after restarting the container was immediate (~120ms healthy).
- **Writes ARE covered by command-level fault injection.** EF executes
  inserts via `ExecuteReader` (insert with read-back), so the 57P01 fault is
  reachable on `POST /items` — session 1's assumption (writes only connect
  via non-reader paths) was wrong. Attribution across the session: 6 of 8
  command-fault events on reads, 2 on inserts. All 8 POSTs still succeeded
  through their faulted retry sequences (deep recovery examples: 1.1s and
  11.9s POSTs).
- Session-2 event totals (same caveat as always: raw log-line counts exceed
  true event counts because exceptions are re-rendered by multiple loggers):
  ~66 retry warnings, one read-path budget exhaustion (GET, ~177.7s wall to
  `RetryLimitExceededException`), one unhealthy health-check completion
  (the outage probe).

Spec-facing consequences captured in the change
`add-ef-connection-resiliency-spec`: hard-down scenario added, backoff
requirement corrected, write path folded into the data-endpoints
requirement (item-creation scenario added), and `QUESTIONS.md` items 1–3
resolved via these experiments.

## Session end state (2026-11-09 → 2026-11-10)

- Demo app stopped after the run-through (killed `dotnet run`/`Host.exe`);
  relaunched on 2026-11-10 for the spec-apply experiments above.
- `some-postgres` Docker container still running on `localhost:5432`,
  credentials matching `appsettings.Development.json`; `Items` table intact,
  so the demo can be relaunched and re-verified without setup.
- Statistical caveat for the spec: at a 50% fault rate, retry-budget
  exhaustion occurred 3 of ~40 observed requests — far above the naive
  ~1-in-2000 odds. Likely explanation is correlated attempts rather than
  truly independent rolls; do NOT bake the observed rate into any
  requirement, but do record that exhaustion is possible under sustained
  fault injection and how it manifests (about 2 minutes of silent stall
  before a 500 response).
