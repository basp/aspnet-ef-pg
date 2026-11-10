# Management Briefing — EF Health-Check Resiliency Study

**Project:** PostgreSQL x EF Core x ASP.NET health-check resiliency study
**Status:** Conclusions captured; spec verified; published at
[`basp/aspnet-ef-pg`](https://github.com/basp/aspnet-ef-pg)
**Date:** 2026-11-10

## Why this matters

Teams enable Entity Framework's automatic retry policy (`EnableRetryOnFailure`)
for PostgreSQL so that brief outages and failovers don't take the application
down. Operations teams then trust the `/health` endpoint to tell the
orchestrator the truth. **Nobody checks whether the health check itself is
covered by the retry policy** — the reliability of monitoring rests on an
unverified assumption. This project verified that assumption directly: a small
test application injects the *real* PostgreSQL error codes that production
failovers produce, at configurable rates, and records what the health check and
data endpoints actually do.

## The answer, in plain terms

1. **Good news, verified twice: the health check is covered.** Under sustained
   fault injection, `/health` stayed healthy on every consecutive check
   (21/21, then 20/20 across two sessions). **No separate resilience
   configuration is needed for EF-based health checks** — teams can rely on
   this instead of just assuming it.

2. **The catch: with the database hard-down, health checks stall before they
   fail.** The endpoint took **~3 minutes** (194 seconds) to finally report
   unhealthy, because the retry policy works through its whole budget first.
   Consequence for monitoring: **client-side timeouts must be aligned with the
   server's retry budget**, otherwise alerts fire as "timeout" instead of
   naming the real outage.

3. **Cancelling an HTTP request does not stop the server-side retries** — the
   retry ladder keeps running after the caller has given up. Worth knowing
   when load-dropping a misbehaving backend.

4. **Failure behavior is now quantified, not folklore:** retry delays grow
   geometrically with jitter (capped at 30s), budget exhaustion appears as a
   ~2-minute silent stall per request, and recovery after a real outage is
   immediate (~120ms to healthy).

5. **The knowledge is captured as a contract, not tribal knowledge:** six
   durable behavior requirements are formalized in a versioned spec
   (`ef-connection-resiliency`), and three change tracks were implemented,
   verified, and archived. Failure rates per fault class are configurable
   without recompiling, so the behavior can be re-exercised and re-verified
   at any time.

## What it cost and what it produced

- **Scope:** one focused sandbox application plus documentation — a
  deliberately small, demo-grade verification, not a product.
- **Deliverables:** the test application (published repo), a versioned
  behavior spec with six verified requirements, four session records with
  evidence, and this document.
- **Follow-up candidates:** one cosmetic code cleanup only — no behavior
  changes remain open.

## Caveats (important for how far the numbers carry)

- Results are specific to PostgreSQL (Npgsql) + EF Core + the .NET
  health-check framework. The *method* transfers to other stacks; the exact
  numbers may not.
- Verification was manual (small samples). Automated tests become relevant
  if these patterns are incorporated into a production codebase — that would
  be that team's responsibility.

## Recommendation

Where a real application wires EF health checks into orchestrator monitoring:

- **Trust** that retry-policy coverage extends to the health-check probe.
- **Audit** client-side timeouts against the server's retry budget.
- **Treat** multi-minute health-check latency as an outage signal in its own
  right, not as a slow-but-healthy check.

## Where the evidence lives

- `README.md` — overview, headline findings, how to run
- `TAKEAWAYS.md` — session-by-session records with log evidence
- `openspec/specs/ef-connection-resiliency/spec.md` — the durable behavior
  contract (six requirements, all demonstrated)
