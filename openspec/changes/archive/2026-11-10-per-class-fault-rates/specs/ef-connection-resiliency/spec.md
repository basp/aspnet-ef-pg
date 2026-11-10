# Spec Delta: ef-connection-resiliency

## MODIFIED Requirements

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

- **GIVEN** the rates of both fault classes are configured to 0
- **WHEN** `/health`, `/items`, and `POST /items` are requested repeatedly
- **THEN** no simulated fault events appear in the application log and no operation's timing is prolonged by retries

#### Scenario: Probe retry-budget exhaustion under sustained dual-class injection

- **GIVEN** both fault classes are active with each class's configured rate, so each probe attempt is independently subject to connection- and command-level faults (at the default demonstration configuration: 50% each, making a fully faulted attempt sequence materially possible)
- **WHEN** every attempt of the probe's retry budget encounters injected faults at either or both levels
- **THEN** the health check reports unhealthy and the endpoint responds with a failing status (HTTP 503 in the demo API), with wall time equal to the sum of capped retry delays plus per-attempt fault durations
- **AND** the application log records the transient retry warnings for the exhausted attempts, with the retry-limit-exceeded failure carried by the unhealthy health-check result rather than logged as a separate error line

### Requirement: Fault injection rate and resiliency settings are configurable

The demonstration SHALL take its fault-injection rates per fault class — a
connection-class rate governing the 08001 injector and a command-class rate
governing the 57P01 injector — and its resiliency settings (maximum retry
count, maximum retry delay) from configuration, with per-class rate
defaults each equal to the previously demonstrated 50% shared rate and
retry defaults equal to the previously fixed values (10 retries, 30-second
delay cap); a configured rate of 0 SHALL disable its own fault class only.
Changing any of these values SHALL scale the demonstrated behavior exactly
as the corresponding fixed values do.

#### Scenario: Defaults preserve the demonstrated configuration

- **GIVEN** the configuration keys for the per-class fault rates and retry settings are absent
- **WHEN** the demo runs and exercises `/health` and the data endpoints
- **THEN** both fault classes are active at 50% (per-attempt behaviors match the previously verified shared-rate session) and the retry behavior matches the previously fixed settings (10 retries, 30-second delay cap)

#### Scenario: Configured rate adjusts observed fault frequency

- **GIVEN** a fault-class rate other than 50% is configured for one class while the other class's rate is unchanged
- **WHEN** the endpoints are exercised repeatedly
- **THEN** simulated fault events of the configured class occur on approximately its configured fraction of attempts (inherited randomness), the untouched class displays its own configured frequency, and each faulted-but-retried operation behaves identically to its counterpart under any other rate combination

#### Scenario: Configured retry settings reshape the budget

- **GIVEN** a maximum retry count or maximum retry delay other than the default is configured
- **WHEN** an operation exhausts the retry budget or times out
- **THEN** the number of retry warnings and the delay cap visible in the application log match the configured values (per the existing geometric-backoff requirement)

#### Scenario: Rates gate behavior asymmetrically

- **GIVEN** one fault-class rate is configured to 0 with the other class active
- **WHEN** `/health`, `/items`, and `POST /items` are exercised
- **THEN** no simulated fault events of the disabled class appear anywhere in the application log, while the active class's events continue on their reachable paths (connection faults at connection opens; command faults on reader and non-query commands alike), and each request responds only per the remaining class's fault activity — recovering when a retry succeeds, failing late on budget exhaustion
