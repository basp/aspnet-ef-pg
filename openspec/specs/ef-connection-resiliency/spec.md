# ef-connection-resiliency Specification

## Purpose

Define how the host's PostgreSQL EF connection resiliency
(`EnableRetryOnFailure`) governs retries and failure surfaces where they
interact with the health-check and ordinary query endpoints, so the
demonstrated behavior is durably specified rather than known only from
session logs.

## Requirements

### Requirement: Health check stays healthy under sustained transient connection faults

The `/health` endpoint's `DbContext` health check SHALL remain healthy when
transient connection faults occur while the health-check probe opens its
database connection, because the retry strategy wraps the probe. The
endpoint's response SHALL NOT report degraded or unhealthy due to such
transient faults, and each recovered attempt SHALL be observable as a retry
warning in the application log.

#### Scenario: Repeated health checks under sustained connection-fault injection

- **GIVEN** connection fault injection is active (simulated 08001 errors on a fraction of connection attempts)
- **WHEN** `/health` is requested repeatedly
- **THEN** the combined health status is healthy on every response, including responses to requests whose probe attempt(s) faulted and were retried
- **AND** for faulted attempts the application log contains "A transient exception occurred during execution. The operation will be retried after Xms." followed by a successful retry

#### Scenario: Persistent database outage ends in an unhealthy result

- **GIVEN** the database is persistently unreachable (service unavailable, no fault injection needed)
- **WHEN** `/health` is requested and the health-check probe exhausts its entire retry budget (10 retries; each delay capped at `maxRetryDelay`, so wall time is the sum of capped delays plus per-attempt connection-failure duration in the demo configuration)
- **THEN** the health check reports unhealthy and the endpoint responds with a failing status (HTTP 503 in the demo API)
- **AND** the application log records the transient retry warnings for the exhausted attempts, with the retry-limit-exceeded failure carried by the unhealthy health-check result rather than logged as a separate error line

### Requirement: Health-check probe exercises only connection-level fault injection

The health-check probe path (`CanConnectAsync`) executes its test query as a
non-reader command, so in the demo as built, command-level fault injection
(57P01) SHALL NOT apply to the health-check path; only connection-level
fault injection (08001) can affect it. The health-check capability does not
cover command-level transient faults by construct of this demo.

#### Scenario: Health checks produce no command-fault activity

- **GIVEN** both fault-injection interceptors are registered and active
- **WHEN** `/health` is requested repeatedly
- **THEN** fault activity observed on the health-check path consists of connection faults (08001) only
- **AND** no command-level fault events (57P01) occur in connection with health-check probe executions

### Requirement: Data endpoints recover from transient faults invisibly to callers

The ordinary data endpoints SHALL recover from transient faults injected at
both the connection and command execution levels — covering reads and writes
alike, since both execute through reader-based command pipelines (EF executes
inserts with read-back) — such that callers receive a successful response
whenever a retry attempt succeeds, without any error observable in the
response.

#### Scenario: Query succeeds despite injected transient faults

- **GIVEN** connection fault (08001) and command fault (57P01) injection are active
- **WHEN** the items query is requested while transient faults occur on some attempts
- **THEN** the response is successful and contains the full result set once the retry strategy finds an attempt that succeeds
- **AND** the application log shows the transient-exception retry warnings for each faulted attempt

#### Scenario: Item creation succeeds despite injected transient faults

- **GIVEN** connection fault (08001) and command fault (57P01) injection are active
- **WHEN** an item is created while transient faults occur on some attempts of the insert and its surrounding connection/command executions
- **THEN** the creation ultimately succeeds (HTTP 201 in the demo API) and the item is persisted once the retry strategy finds an attempt that succeeds
- **AND** the application log shows both connection-level and command-level fault events and retry warnings for the faulted attempts

### Requirement: Retry timing follows geometric backoff capped at maxRetryDelay

Retry delays for a failing operation SHALL start at zero for the first
retry and then grow geometrically from an initial delay, with jitter, and be
capped at the configured `maxRetryDelay`; the total time an operation may
spend retrying SHALL be the sum of its retry delays plus fault durations.

#### Scenario: Consecutive faults produce escalating capped delays

- **GIVEN** a single operation experiences consecutive transient faults on every attempt
- **WHEN** the retry strategy retries the operation
- **THEN** the logged retry delays for that operation escalate geometrically with each retry (observed in the demo configuration: 0ms, ~1s, ~3s, ~7.5s before delays reach the cap) and each delay beyond the growth phase is capped at the configured maximum

### Requirement: Retry-budget exhaustion surfaces as a late failure

When transient faults persist across all retry attempts of an operation, the
operation SHALL fail with a retry-budget-exceeded error after it has spent
its entire retry budget, rather than hanging indefinitely or failing early.

#### Scenario: Persistent faults exhaust the retry budget

- **GIVEN** every attempt of an operation perpetually encounters injected transient faults
- **WHEN** the retry budget (10 retries under the configured settings) is spent
- **THEN** the affected request ultimately fails with a retry-limit-exceeded error response (HTTP 500 in the demo API) after wall time equal to the sum of capped retry delays plus per-attempt fault duration
- **AND** the application log records that the maximum number of retries was exceeded
