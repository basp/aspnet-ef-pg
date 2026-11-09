# Design

## Context

The demo host (`Host/`) already implements everything the delta spec
describes: Npgsql `EnableRetryOnFailure(10, 30s)`, two fault-injection
interceptors (08001 connection-level, 57P01 command-level, 50% each), a
`DbContext` health check, and ordinary query/write endpoints. Session
evidence lives in `TAKEAWAYS.md` (n=1 run, 2026-11-09); open items in
`QUESTIONS.md`; OpenSpec previously had zero specs, so this change creates
the first capability without an existing layout to preserve.

The unusual aspect of this "implementation" is that its main deliverable is
a spec, and three of its claimed behaviors (well: their edge variants —
hard-down health checks and the write path) are verified by cheap
experiments rather than code changes.

## Goals / Non-Goals

**Goals:**

_ Provide information to the team about the behavior of the feature in the form
  of extra documentation and guidance documents. You can be pretty meticulous 
  with this.
- Turn session evidence into requirement-shaped behavior, keeping
  observable/log-verifiable claims as requirements and scenarios.
- Gate the two expected-but-unobserved behavior areas behind explicit
  verification experiments scheduled for the apply phase.
- Keep the evidence documents (`TAKEAWAYS.md`, `QUESTIONS.md`,
  `README.md`) as the narrative layer; the spec stays requirement-only.

**Non-Goals:**

- No application code changes (demo improvements are future changes).
- No automated tests, no test project (recorded team decision).
- No requirements from single-session measured numbers (latency averages,
  observed fault ratios, exhaustion counts in absolute terms).

## Decisions

1. **Evidence policy: verified session observations + analytically certain
   code facts only.** Requirements for health-check resilience,
   probing/fault-injection path coverage, query recovery, retry backoff,
   and budget exhaustion are backed either by log evidence from the
   2026-11-09 run or by code mechanics (probe uses `ExecuteNonQuery`;
   command interceptor overrides only `ReaderExecutingAsync`). The
   hard-down health-check case and the write path are **excluded from the
   delta until the scheduled experiments confirm them**; if an experiment
   contradicts expectation, the delta is revised before sync/archive.
   - Alternative considered: writing expected behavior as requirements
     now. Rejected: the spec would describe behaviors from anticipation,
     which is exactly what this capture was designed to avoid.
2. **One combined capability at a flat path.**
   `specs/ef-connection-resiliency/spec.md`. The project has no existing
   spec organization to follow, the demo is small, and splitting retry
   mechanics from health-check behavior would force cross-references
   between two specs for a system that is a single-process app. A
   later split is a normal delta operation (new capability + REMOVED
   requirements) if needed.
   - Alternative considered: two capabilities (`ef-retry-on-failure`,
     `health-check-resiliency`). Deferred per recorded scope decision.
3. **Config values appear in scenarios as the demo's configured settings,
   not as universal limits.** The backoff pattern and ~121s total budget
   follow from the current `maxRetryCount: 10` / `maxRetryDelay: 30s`
   configuration; scenarios cite it as configured settings. If the demo's
   retry configuration changes, the scenarios are re-validated rather than
   silently stale. Absolute measures of repeated exhaustion occurrences (3
   in one session) stay out of the spec.
4. **Manual scenario validation is log-based.** Each scenario is
   checkable by launching the demo, exercising the endpoint, and reading
   the console log for the named log events (retry warnings, fault
   injections, completion statuses). This suits the recorded decision that
   verification stays manual until the team adopts the idea.

## Risks / Trade-offs

- [Unverified requirement areas (hard-down, write path) may not behave as
  expected] → They are deliberately absent from the delta; tasks gate
  them, and the delta is reconciled with measured outcomes before archive.
- [Fault-injection randomness makes corner behaviors (budget exhaustion)
  non-reproducible on demand] → The exhaustion scenario is validated by
  either persistence across many attempts or by temporarily raising the
  fault rate as part of the experiment; TAKEAWAYS already documents the
  correlation concern, so the spec claims manifestation, not frequency.
- [Knowledge docs drift from the spec over time] → QUESTIONS.md items that
  the experiments answer get updated as an apply side effect; README
  already points readers into the OpenSpec flow.

## Open Questions

Deferrable, none of which change the specs or task breakdown:

- Whether the explicit `customTestQuery` lambda in `Program.cs` is
  redundant with the `AddDbContextCheck` default (cosmetic; the spec is
  worded independently of that choice).
- net11.0-RC ↔ EF 10.x-stable version pairing commentary (README concern).
- Final home of `TAKEAWAYS.md` once the spec is archived.
