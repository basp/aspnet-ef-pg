# Spec Delta: ef-connection-resiliency

## REMOVED Requirements

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

**Reason**: The instance this requirement documented (the command interceptor
overriding only reader execution, so the non-query probe bypassed command
fault injection) is being removed in this change. The requirement recorded a
demo-construction limitation as a guarantee; that guarantee no longer holds.

**Migration**: The probe's relationship to command-level fault injection is
now specified constructively in the ADDED requirement
"Health-check probe exercises both fault classes" below.

## ADDED Requirements

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
