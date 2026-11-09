# Proposal

## Why

The demo's central result — that `EnableRetryOnFailure` on the PostgreSQL EF
provider also applies to the Entity Framework `DbContext` health check
(`AddDbContextCheck`) — currently lives only in code, a one-session log file,
and scratch notes (`TAKEAWAYS.md` / `QUESTIONS.md`). No OpenSpec captures
exist at all. The knowledge is about to be shared with the team (via
`README.md`), so now is the moment to give it a durable, requirement-shaped
home before it drifts or gets lost.

## What Changes

- Introduce the project's **first capability spec**, capturing how the demo
  app's EF connection resiliency behaves across the paths it exercises:
  - transient-fault retry behavior of the `/health` health-check probe
    (the demonstrated thesis),
  - the probe-path limitation (command-level 57P01 fault injection cannot
    reach the `ExecuteNonQuery`-based probe),
  - the ordinary query path (`/items`) incl. the retry ladder/budget and the
    budget-exhaustion edge behavior (~121s stall → `RetryLimitExceededException`).
- Record requirements based on **verified session evidence** (2026-11-09 run)
  plus analytically certain code facts; explicitly exclude run-artifact
  numbers (latency averages, observed fault ratios) from requirements.
- Scope decision (recorded): **one combined capability**, not separate
  `ef-retry-on-failure` and `health-checks` capabilities.
- Scope decision (recorded): **manual verification only** (demo/proof-of-concept);
  automated verification is deferred to the team if the idea gets incorporated
  into a real codebase.
- No application code changes in this change. Two cheap verification
  experiments (health check against a hard-down database; write path via
  `POST /items`) are scheduled as tasks to confirm or revise the spec before
  it is archived, since the spec will otherwise describe those behaviors from
  expectation rather than evidence.

## Capabilities

### New Capabilities

- `ef-connection-resiliency`: How the host's EF connection resiliency
  (`EnableRetryOnFailure` on the Npgsql provider) governs retries and failure
  surfaces where they interact with observability surfaces — the `/health`
  `DbContext` health check and the ordinary query/command endpoints — under
  injected transient PostgreSQL faults.

### Modified Capabilities

- (none — this is the first spec in the project)

## Impact

- **OpenSpec only**: creates `openspec/specs/ef-connection-resiliency/` (via
  this change's delta) at archive time; no production code is touched.
- **Knowledge documents**: `README.md`, `TAKEAWAYS.md` and `QUESTIONS.md`
  remain the narrative/evidence companions; the spec references their role
  without duplicating them. `QUESTIONS.md` items resolved by the scheduled
  experiments should be updated as a side effect of applying this change.
- **Verification environment**: the scheduled experiments need the existing
  Docker Postgres (`some-postgres`, `localhost:5432`, credentials per
  `appsettings.Development.json`) and the demo host (`dotnet run
  --project Host`, `http://localhost:5264`); no new dependencies or services.
