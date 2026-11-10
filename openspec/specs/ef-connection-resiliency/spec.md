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

### Requirement: Health-check probe exercises both fault classes

The health-check probe path (`CanConnectAsync` executes its test query as a
non-query command) SHALL be subject to both simulated fault classes:
connection-level fault injection (SQLSTATE 08001) when the probe opens its
connection, and command-level fault injection (SQLSTATE 57P01) when the
probe's test query executes, as far as each class's configured fault
activity reaches.

#### Scenario: Probe faults and recovers under command-level injection

- **GIVEN** both fault-injection interceptors are registered with active fault injection
- **WHEN** `/health` is requested repeatedly and the probe's test query encounters an injected 57P01 command fault
- **THEN** the combined health status is healthy on responses whose probe faulted and was subsequently retried
- **AND** the application log contains simulated command-fault events attributed to health-check probe executions, followed by transient-exception retry warnings

#### Scenario: No simulated faults while injection is disabled

- **GIVEN** the fault injection rate is configured to 0
- **WHEN** `/health`, `/items`, and `POST /items` are requested repeatedly
- **THEN** no simulated fault events appear in the application log and no operation's timing is prolonged by retries

#### Scenario: Probe retry-budget exhaustion under sustained dual-class injection

- **GIVEN** both fault classes are active with each class's configured rate, so each probe attempt is independently subject to connection- and command-level faults (at the default demonstration configuration: 50% each, making a fully faulted attempt sequence materially possible)
- **WHEN** every attempt of the probe's retry budget encounters injected faults at either or both levels
- **THEN** the health check reports unhealthy and the endpoint responds with a failing status (HTTP 503 in the demo API), with wall time equal to the sum of capped retry delays plus per-attempt fault durations
- **AND** the application log records the transient retry warnings for the exhausted attempts, with the retry-limit-exceeded failure carried by the unhealthy health-check result rather than logged as a separate error line

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

### Requirement: Fault injection rate and resiliency settings are configurable

The demonstration SHALL take its fault-injection rate, maximum retry count,
and maximum retry delay from configuration, with defaults equal to the
previously hardcoded demonstration configuration (50% fault rate, 10
retries, 30-second delay cap); a configured rate of 0 SHALL disable fault
injection. Changing these values SHALL scale the demonstrated behavior
exactly as the corresponding fixed values do.

#### Scenario: Defaults preserve the demonstrated configuration

- **GIVEN** the configuration keys for the fault rate and retry settings are absent
- **WHEN** the demo runs and exercises `/health` and the data endpoints
- **THEN** fault injection is active at 50% and the retry behavior matches the previously fixed settings (10 retries, 30-second delay cap)

#### Scenario: Configured rate adjusts observed fault frequency

- **GIVEN** a fault rate other than 50% is configured
- **WHEN** the endpoints are exercised repeatedly
- **THEN** simulated fault events occur on approximately the configured fraction of attempts (inherited randomness), and each faulted-but-retried operation behaves identically to its counterpart under any other rate

#### Scenario: Configured retry settings reshape the budget

- **GIVEN** a maximum retry count or maximum retry delay other than the default is configured
- **WHEN** an operation exhausts the retry budget or times out
- **THEN** the number of retry warnings and the delay cap visible in the application log match the configured values (per the existing geometric-backoff requirement)
