# Design

## Context

Single-project demo (`Host`); all request traffic is async (`ToListAsync`,
`CanConnectAsync`, `SaveChangesAsync`). Both fault-injection interceptors are
registered as scoped services and resolved inside the `AddDbContext` lambda,
which already has `builder.Configuration` in reach. The demo deliberately
runs without automated tests and without Options infrastructure; the config
surface today is a connection string in `appsettings.Development.json`.
Motivation is in the proposal; behavioral contract in the delta spec. The
existing `ReaderExecutingAsync` override is the style template for the new
non-query override.

## Goals / Non-Goals

**Goals:**

- The three knobs (fault rate, max retry count, max retry delay) live in
  configuration and are overridable by files or environment without
  recompiling; environment-variable override works through the standard
  configuration provider chain.
- The health-check probe path is subject to command-level (57P01) fault
  injection, symmetric with the probe's connection-level exposure.
- Defaults (50 / 10 / 30s) exactly preserve the previously hardcoded behavior
  so every existing requirement and documented observation remains honest.
- Session log attribution stays possible: faulted non-query executions must
  be distinguishable from reader executions in the console log.

**Non-Goals:**

- Separate rates or separate enable/disable switches per fault class (one
  rate governs both interfering interceptors), Options pattern, hot reload of
  knobs, automated tests, any change to transient SQLSTATE sets or retry
  semantics.

## Decisions

1. **Plain `IConfiguration` reads, not an Options pattern.** Interceptors are
   demo crash dummies; each scoped instance reads
   `Demo:FaultInjection:RatePercent` in its constructor, and `Program.cs`
   reads `Demo:Retry:*` inside the `AddDbContext` lambda where
   `EnableRetryOnFailure` is called. `IOptions<T>`/validation plumbing buys
   nothing a demo needs; the codebase has no Options usage to match.
   Rejected alternative: a small typed options class — adds a registration
   and a type for three ints.

2. **Key names under a `Demo` section:**
   `Demo:FaultInjection:RatePercent`, `Demo:Retry:MaxRetryCount`,
   `Demo:Retry:MaxRetryDelaySeconds`. Defaults applied when keys are absent,
   so `appsettings.Development.json` documents them explicitly with those
   default values and the section also stays self-explanatory in the README.

3. **`0` = disabled via the rate itself; strict validation otherwise.** Rate
   must be an integer 0–100; retry count ≥ 0; delay seconds ≥ 0. Out-of-range
   or unparseable configured values throw at startup with a message naming
   the key — fail-fast over silent clamping, because a silently stuck-at-0
   (no faults at all) or stuck-at-100 (every attempt faults; everything 500s)
   demo would mislead an audience. A separate boolean "disable" switch was
   rejected as redundant with `RatePercent: 0`.

4. **Async-only `NonQueryExecutingAsync` override, matching existing style.**
   Both interceptors implement only `_..Async` hooks today because all demo
   traffic is async; the new override copies `ReaderExecutingAsync`'s
   roll-and-throw shape one-for-one. Log line gains the executed variant,
   e.g. `"Simulating transient error (non-query)."`, so path attribution
   during verification sessions stays as analyzable as the reader/connection
   prefixes already are.

5. **Knobs are read at service construction and DbContext configuration time;
   restart applies changes.** No `IOptionsSnapshot`/hot-reload wiring. A demo
   restart is instant; keeping "restart to re-configure" avoids a moving
   target during an observed run. Documented in the README.

## Risks / Trade-offs

- **Non-query injection may reach EF machinery beyond the probe** (DDL,
  internal existence checks) rather than only `SELECT 1` probes → acceptable
  for a demo: every affected command is logged, transient classification is
  unchanged (57P01), and the retry strategy governs it exactly like a probe
  fault would. Noted for observation, not treated as a defect.
- **Randomness makes "approximately the configured rate" fuzzier at low
  rates** (verifying a 10% setting by eye) → verification sessions run enough
  requests and treat log-event frequency as approximate only; the spec
  scenario already says "inherited randomness".
- **Combined change mixes a behavior fix and a convenience knob** → one
  verification session covers both deliberately: verify defaults first
  (equivalence with today), then prove 57P01 reaches `/health` with 57P01 +
  high rate, then verify a disabled run at rate 0 is fault-free.
- **Typos in keys silently fall back to defaults** (config systems read
  missing keys as null) → defaults are safe and correct; a `Warning`-level
  log entry on the applied defaults is not worth the noise for a demo, so the
  README documents the keys instead. Tracked: if keys drift again, revisit.
