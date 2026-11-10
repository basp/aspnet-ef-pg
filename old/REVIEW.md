# Review of `RETRY.md`

Status: **Draft – technical pass required before publishing as authoritative guidance.**

The document has a sound core message (keep liveness independent of downstream dependencies; bound retrying work with cancellation; isolate diagnostic checks from operational retries). The concerns below should be resolved before readers are told to copy the code or the configuration.

## Concerns

### 1. Timeout handling in the resilience examples (High)
- The operational pipeline's retry predicate handles `TimeoutException`, but Polly timeouts throw `TimeoutRejectedException`. A per-attempt timeout may therefore not be retried as intended.
- The custom retry loop (`ExecuteWithBudgetAsync`) throws `TimeoutException` when it reaches its attempt limit, even if the failures were other transient errors. It also references an undefined `IsTransient`.
- Strategy ordering (overall timeout → retry → circuit breaker → per-attempt timeout) should be justified and verified against Polly's actual behavior.
- Locations: "Linked Token Cancellation Pattern", section 7.2.

### 2. Health-check result vs. probe outcome (High)
- Examples return `Degraded` for unexpected responses, while the text says readiness failures should be `Unhealthy`.
- By default `Degraded` maps to HTTP 200, so Kubernetes would consider the probe successful. Show the `ResultStatusCodes` mapping explicitly or return `Unhealthy`.
- The catch block that swallows caller cancellation (`when (!cancellationToken.IsCancellationRequested)`) should be explained.
- Locations: sections 6.2, 6.3, 7.3.

### 3. Dedicated pool overstated (Medium)
- A two-connection pool provides isolation, not a guarantee of availability; it can itself be exhausted by concurrent checks.
- "Gold standard" and "guarantees" should be softened.
- Clarify `NpgsqlDataSource` lifecycle/disposal (manual `Build()` and `AddKeyedSingleton` bypass DI-managed disposal) and note that `Minimum Pool Size=1` keeps an idle connection open per pod.
- Location: section 6.3.

### 4. Idempotency caveat missing (High)
- A transient failure can occur after the database commits but before the client receives the acknowledgement ("lost ACK"). Retrying such writes can duplicate effects.
- This is only mentioned via the related-document link. Add a prominent warning beside the operational retry guidance (sections 2 and 7).

### 5. Unverified or overly categorical claims (Medium)
- Verify that `AddDbContextCheck` / `CanConnectAsync()` runs through EF Core's retrying execution strategy for the targeted EF Core / Npgsql versions, and cite it.
- Verify metric names, instruments, and thresholds in the "Key Metrics to Alert On" table (e.g., `resilience.polly.*`, `npgsql.connection_pool.waiting_requests`, `health_check.execution.duration`) and the meter names registered in the OpenTelemetry sample.
- Verify the claim that Polly v8 is "allocation-free"; "low-allocation" is safer.
- Verify the `UIResponseWriter` dependency (`AspNetCore.HealthChecks.UI.Client`) is mentioned or replaced.
- Specific numeric claims (e.g., 64× amplification, failover duration of 5–15 s, 50+ pods) should be labeled as illustrative.

### 6. Kubernetes wording (Low)
- A liveness failure causes the kubelet to restart the container following the termination grace period (SIGTERM, then SIGKILL); it is not an immediate SIGKILL.
- Readiness removes the pod from service endpoints; in-flight and already-routed traffic is not guaranteed to stop instantly.
- The "Kubelet kills all pods simultaneously" claim in 5.2 assumes all pods fail liveness at once; state that assumption.
- Locations: section 4, 5.2, sequence diagram 3.2.

### 7. Internal consistency (Low)
- Section 6.2 reuses a `DbContext` connection that may be shared or already open; confirm the scope/disposal behavior.
- The downstream check probes `/healthz/live` of another service; consider whether `/healthz/ready` is the correct target and avoid dependency chains in readiness checks.
- Probe timing examples (`timeoutSeconds: 1` with 800 ms internal limits) should state the margin rationale.

## Suggested Process

1. **Triage**: Create an issue (or checklist) per concern above; fix severity High first (1, 2, 4).
2. **Read the samples critically**: The code is illustrative and is not meant to be compiled or run. Review each sample by reading it for missing members (e.g., `IsTransient`), wrong exception types, and mismatches with the surrounding prose.
3. **Verify claims against documentation**: Check the key claims against official docs and source, and cite them inline:
   - Polly timeout exception type and retry predicate behavior.
   - `Degraded` / `Unhealthy` HTTP status mapping in ASP.NET Core health checks.
   - Whether `CanConnectAsync()` is retried by the execution strategy.
   - Npgsql behavior when the pool is exhausted.
   - Metric and meter names in Polly, Npgsql, and ASP.NET Core documentation.
4. **Soften claims**: Replace absolute language ("guarantees", "gold standard", "authoritative") with qualified wording, and label illustrative numbers as examples.
5. **Add caveats**: Idempotency warning, `Degraded` mapping note, probe-restart semantics.
6. **Re-review**: Have a second engineer review the revised document, then update the status above and remove this file (or move resolved items to a changelog).

## Checklist

- [ ] Retry predicate handles `TimeoutRejectedException`; `IsTransient` defined
- [ ] Health-check status mapping documented and consistent with prose
- [ ] Dedicated pool claims softened; lifecycle/disposal documented
- [ ] Idempotency warning added near operational retry guidance
- [ ] EF Core `CanConnectAsync` retry behavior verified and cited
- [ ] Metric/meter names and thresholds verified
- [ ] Kubernetes restart/readiness wording corrected
- [ ] Code samples read through for consistency with the prose (no compilation needed)
- [ ] Second-engineer review completed
