# Proposal

## Why

The single shared fault rate (`Demo:FaultInjection:RatePercent`, 50% for both
interceptors) conflates two independent fault classes. Session-3 evidence
showed the consequence: injecting both classes at 50% drops the probe's
per-attempt survival to ~25%, making `/health` retry-budget exhaustion a
visible ~4% per call — materially different from the single-class baseline
(~0.05%) the earlier sessions verified. Separate per-class rates let a
session restore one class's exposure while keeping the other active, and
make asymmetric demonstrations (connection-fault-only or
command-fault-only) directly reproducible.

## What Changes

- **Two configured rates replace the shared one**: `ConnectionRatePercent`
  (governs the 08001 connection interceptor) and `CommandRatePercent`
  (governs the 57P01 command interceptor), each defaulting to 50, each 0
  disabling its own class only. The shared `RatePercent` key is removed
  from configuration and code.
- **Config semantics**: out-of-range or unparseable values fail at startup
  naming the key (as today); both rates read once at startup (restart to
  apply, as today).
- **Spec delta** on `ef-connection-resiliency`: the configurability
  requirement is updated for two rates with a new asymmetric-rate scenario,
  and the probe requirement's disabled-injection scenario wording is
  updated for two rates ("the rate" → "a class's rate"). All other
  requirements keep their verified behavior; defaults (50/50) preserve
  every session-3 observation.

## Capabilities

### New Capabilities

<!-- None: fault-injection behavior stays owned by the existing single capability. -->

### Modified Capabilities

- `ef-connection-resiliency`: the configurability requirement is updated to
  take two fault-class rates from configuration (defaults preserved) with a
  new scenario covering asymmetric rates (one class active, one disabled);
  the probe requirement's disabled-injection scenario is reworded per
  class. No other requirement changes.

## Impact

- `Host/DemoConfiguration.cs` — two rate methods (connection/command) and
  shared validation helper; the old single-rate method is removed.
- `Host/SimulatedConnectionFaultInterceptor.cs`,
  `Host/SimulatedTransientErrorInterceptor.cs` — each reads its own class's
  rate key.
- `Host/appsettings.Development.json`, `README.md` (Configuration table),
  `TAKEAWAYS.md` — document the two keys.
- Verification stays manual (demo run) per project stance; defaults pass
  plus one asymmetric pass expected.
