# Proposal

## Why

The demo's fault-injection and resiliency knobs are hardcoded (50% fault rate, retry settings), so every variation requires recompilation and sustained runs always exhaust the retry budget the same way. Additionally, the health-check probe path is blind to command-level fault injection by interceptor construction, making the demonstration asymmetric for reasons the code does not surface. Both issues were captured as candidate improvements in this project's session records and are deliberately scheduled next.

## What Changes

- **Configurable fault rate**: the two fault-injection interceptors read their fault rate from configuration (`Demo:FaultInjection:RatePercent`, default 50) — `0` disables injection entirely.
- **Configurable retry settings**: `EnableRetryOnFailure(maxRetryCount, maxRetryDelay)` reads from configuration (`Demo:Retry:MaxRetryCount` default 10, `Demo:Retry:MaxRetryDelaySeconds` default 30) instead of hardcoded values in `Program.cs`.
- **Probe-path command faults**: `SimulatedTransientErrorInterceptor` gains a `NonQueryExecutingAsync` override (same roll-and-throw as `ReaderExecutingAsync`), so SQLSTATE 57P01 injection reaches the health-check probe (`CanConnectAsync` executes its test query as a non-query command). This invalidates one requirement of the existing `ef-connection-resiliency` spec, which pins the probe path to connection-level faults "by construct of this demo" — it now exercises both fault classes.
- **Documentation**: README updated to describe the configuration knobs; no behavioral assertions that would change spec scenarios (existing requirements are deliberately parameter-agnostic).

## Capabilities

### New Capabilities

<!-- None: the demonstration's durable behaviors stay owned by the single existing capability. -->

### Modified Capabilities

- `ef-connection-resiliency`: the requirement that pins command-level fault injection out of the health-check probe path (as-built constructor limitation) is replaced — the probe SHALL exercise both connection-level (08001) and command-level (57P01) fault injection; plus a new requirement making the fault rate and retry settings configurable via configuration with the documented defaults (50%, 10 retries, 30s cap), preserving all existing scenario semantics.

## Impact

- `Host/SimulatedTransientErrorInterceptor.cs` — add `NonQueryExecutingAsync` override; both interceptors read the rate from `IConfiguration`.
- `Host/Program.cs` — pass configuration values into `EnableRetryOnFailure`.
- `Host/appsettings.Development.json` — add a `Demo` configuration section with the defaults.
- `openspec/specs/ef-connection-resiliency/spec.md` — delta via `openspec sync`/archive of this change (one requirement replaced, one added).
- `README.md` — document the knobs.
- No package or framework changes; verification is manual (demo run), consistent with the project's verification stance.
