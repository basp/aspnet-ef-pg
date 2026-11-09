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

1. **Health check hard-down behavior — never observed.** The session showed
   `/health` surviving transient connection faults (08001) and `/items`
   failing with `RetryLimitExceededException`, but not what happens when the
   database is *persistently* unreachable (e.g. stop the `some-postgres`
   container): expected result is a ~121s stall then an `Unhealthy` check
   result, but this is unconfirmed on the health-check path. This is
   arguably the most operationally interesting behavior for the thesis.
2. **Write path — never exercised.** `POST /items` (`SaveChangesAsync`) was
   not run at all. Two sub-questions:
   - Interceptor coverage: fault injection likely cannot reach writes
     either, since `SimulatedTransientErrorInterceptor` only overrides
     `ReaderExecutingAsync` and writes go through `ExecuteNonQuery` /
     `ExecuteScalar` (only 08001 connection faults could apply).
   - Retry semantics for writes: `EnableRetryOnFailure` retrying
     non-idempotent operations has the known "commit succeeded but caller
     lost the connection" verification problem. Whether the demo should
     demonstrate (or caveat) write-path resiliency is unresolved.
   Verifying would show whether the demo's claim ("resiliency works") is
   complete or reader-only.
3. **Is the `customTestQuery` lambda redundant?** The default test query for
   `AddDbContextCheck` is `CanConnectAsync` already, so the explicit lambda
   in `Program.cs` may be decorative. Five-minute code check; tighten demo
   code either way (keep for explicitness or remove as noise).

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
