# QUESTIONS

Open questions raised during exploration of the EF connection-resiliency +
health-check demo. Deferred for later evaluation — nothing here is a
requirement or spec commitment yet.

## Context (decided)

- **Spec purpose.** The spec captures how ASP.NET health checks (especially
  the PostgreSQL EF `DbContext` health check) behave in conjunction with the
  EF connection-resilience feature (`EnableRetryOnFailure`). The project is
  also a learning exercise for setting up a minimal ASP.NET API with EF and
  for getting a feel for .NET / ASP.NET / EF framework features.
- **Spec organization.** One capability spec for now; split into two only if
  a future change would otherwise be very hard to express.
- **Verification.** Manual verification only (demo is a proof-of-concept);
  automated verification becomes the team's responsibility if this demo's
  idea gets incorporated into a real codebase.

## Unverified behaviors (cheap experiments, worth doing before/during spec formalization)

1. **Health check hard-down behavior — never observed.** ~~Expected result is
   a ~121s stall then an `Unhealthy` check result, but this is unconfirmed on
   the health-check path.~~
   **Resolved 2026-11-10** (second-session experiment, `docker stop
   some-postgres`): `/health` returned HTTP **503 Unhealthy** after
   **~194s** — 10 escalating retries with delays capped at 30s plus ~7s per
   failed connection attempt, then `RetryLimitExceededException` swallowed
   by the health-check service into an `Unhealthy` result (message
   `(null)`). Notably the retry-limit-exceeded failure is NOT logged on the
   health-check path (unlike ordinary requests); the retry warnings are the
   only log trail. Recorded in the delta spec as a scenario under the
   health-check requirement. Recovery after `docker start` was immediate.
2. **Write path — never exercised.** ~~Two sub-questions hang there:~~
   **Resolved 2026-11-10** (second-session experiment): the expectation was
   wrong in one half — EF executes inserts via `ExecuteReader` (insert with
   read-back), so the 57P01 command-level fault injection IS reachable on the
   write path (observed: 2 of 8 command-fault events landed on inserts; the
   other 6 on reads). Writes recover exactly like reads (all `POST /items`
   succeeded through retried fault sequences; no write-path budget
   exhaustion was directly captured, but the mechanism is identical to the
   read path): recovery-or-exhaust, no caller-visible difference between
   fault classes. The delta spec folds the write path into the data-endpoints
   requirement with an item-creation scenario.
   ~~Interceptor coverage / retry-semantics questions as originally framed.~~
3. **Is the `customTestQuery` lambda redundant?** **Resolved 2026-11-10**
   (code check): yes — `AddDbContextCheck`'s default test query already is
   `(context, ct) => context.Database.CanConnectAsync(ct)`, so the explicit
   lambda in `Program.cs` is decorative and kept only for explicitness. No
   demo change made (documented here only).

## Smaller threads

4. **Framework version pairing.** The host targets `net11.0` (RC SDK) while
   EF Core and Npgsql provider packages are 10.x stable. Intentional
   representative pairing, or accidental versioning worth a README comment?
5. **Fate of `TAKEAWAYS.md`.** Currently a root-level scratch capture.
   Candidates: keep as repo readme-adjacent notes, fold the evidence into
   the OpenSpec change's design rationale, or drop once the spec exists.
6. **Spec id naming.** Depends on scope kept as one spec; something like
   `ef-retry-on-failure` vs `health-check-resiliency` vs a combined id
   still to be chosen when the change is captured.
