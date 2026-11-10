# Resilience Documentation

This directory contains three complementary guides. They split the material by concern: one covers the principles behind safe retries, one covers the EF Core + Npgsql implementation on PostgreSQL, and one covers diagnostic health checks. Read the guide that matches your task; each one ends with links to the other two.

## Guides

| Guide | Audience | Scope |
| :--- | :--- | :--- |
| [**A — Connection Resilience in .NET**](connection-resilience.md) | Architects and developers designing retry behavior | Retry principles, backoff and jitter, retry budgets, timeout and cancellation envelopes, idempotency, retry layering, the Polly operational pipeline, and observability principles. |
| [**B — Entity Framework Core Connection Resilience on PostgreSQL**](efcore-connection-resilience.md) | Developers implementing EF Core + Npgsql | Provider layers, `EnableRetryOnFailure`, SQLSTATE taxonomy, retriable units, concrete idempotency patterns, custom execution strategies, provider telemetry, and chaos testing. |
| [**C — Health Checks in .NET**](health-checks.md) | Developers wiring orchestrator probes | Probe taxonomy, orchestrator timeout envelopes, failure modes of retrying probes, ASP.NET Core health-check configuration, EF Core/Npgsql isolation, downstream probes, and probe observability. |

## Which Guide Do I Need?

```text
Start here
  |
  +-- Designing retries, timeouts, or idempotency?  --> connection-resilience.md
  +-- Using EF Core + Npgsql on PostgreSQL?         --> efcore-connection-resilience.md
  +-- Wiring liveness/readiness/startup probes?     --> health-checks.md
  +-- Reviewing a change that touches any of these? --> the summary checklist in A, B, or C
```
