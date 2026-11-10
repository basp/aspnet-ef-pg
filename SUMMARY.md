# SUMMARY

**Project:** PostgreSQL × EF Core × ASP.NET health-check resiliency study
**Status:** Conclusions captured; spec verified; published at
[`basp/aspnet-ef-pg`](https://github.com/basp/aspnet-ef-pg)

## What this was

A focused, hands-on study of one operational question that teams usually
just assume: **when an application enables Entity Framework's automatic
retry policy (`EnableRetryOnFailure`) for PostgreSQL, does that same
safety net also protect the health-check endpoint that orchestrators
rely on?** A small application was built to make the assumption testable —
including fault-injection machinery that breaks connections and queries
using the *real* PostgreSQL error codes, the same code path production
failovers produce.

## Key findings (translated to what they mean)

1. **Good news, verified:** health checks do inherit the retry policy.
   Under sustained injected faults, `/health` stayed healthy across
   consecutive monitoring checks — the probe recovers exactly like any
   other query. Teams do not need a separate resilience configuration for
   EF-based `DbContext` health checks.
2. **The flip side, made visible:** the same retry policy makes health
   checks *slow* before they fail. With the database unreachable, the
   endpoint stalls roughly three minutes before reporting unhealthy
   (defaults). For monitoring pipelines this means: **align caller
   timeouts with the server's retry budget, or alarms will read
   timeouts instead of the underlying outage.**
3. **Caller cancellation is not as effective as it appears:** cancelling
   an HTTP request does *not* stop the server-side retry ladder, which
   keeps working through its budget afterward. Worth remembering when
   load-dropping a misbehaving backend.
4. **Failure shapes are now quantified:** backoff is geometric with
   jitter (capped), budget exhaustion manifests as a ~2-minute silent
   stall per request, and recovery after an outage is immediate.
5. **The mechanism is now specified and tuned:** the demo exposes
   configuration knobs (per-class fault rates, retry count, delay cap) so
   the behavior can be re-exercised and re-verified without recompiling,
   and its six durable behavior requirements are captured as a versioned
   spec (`ef-connection-resiliency`) rather than tribal knowledge.

## Caveats

- Conclusions are specific to PostgreSQL (`Npgsql`) + EF Core + the
  .NET health-check framework; the *method* transfers, the numbers may
  not (session records carry the small-sample caveats).
- This was a demo-grade verification project, deliberately without
  automated tests. That becomes a follow-up's responsibility if the
  patterns move into a production codebase.

## Recommendation

Use these findings where a real application wires EF health checks into
orchestrator monitoring: trust that retry policy coverage extends to the
probe, but review client-side timeouts against the server's retry budget
and treat multi-minute health-check latency as an outage signal in its
own right. The ready evidence trail is `README.md` (overview +
findings), `TAKEAWAYS.md` (session records), and
`openspec/specs/ef-connection-resiliency/spec.md` (the durable behavior
contract).
