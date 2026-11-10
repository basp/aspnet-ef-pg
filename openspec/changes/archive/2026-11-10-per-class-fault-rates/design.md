# Design

## Context

The shared `Demo:FaultInjection:RatePercent` was introduced by the just-
archived `parameterize-fault-injection` change (defaults 50/10/30s, strict
0–100 validation, boot-time fail-fast, restart-to-apply). Both interceptors
construct from `IConfiguration` already. Session-3 verification showed the
shared rate's side effect: dual 50% classes drop probe per-attempt survival
to ~25% (exhaustion ~4% per call), while the single-class baseline was
~50% survival (~0.05% exhaustion). See proposal; behavioral contract in
the delta spec.

## Goals / Non-Goals

**Goals:**

- Two independent rates so a session can control each fault class's
  exposure separately, including fully asymmetric configurations.
- Defaults (50% per class) keep the just-verified session-3 behavior bit
  for bit — no re-verification debt for everything already recorded.
- Same validation/ownership model as the existing knobs (fail-fast at
  boot naming the key; restart to apply).

**Non-Goals:**

- Keeping a backward-compatible shared `RatePercent` fallback; changing
  retry or reflection mechanics; per-endpoint or per-SQLSTATE rates;
  hot reload; automated tests.

## Decisions

1. **Replace the shared rate entirely; no legacy fallback.** Config keys
   become `Demo:FaultInjection:ConnectionRatePercent` and
   `Demo:FaultInjection:CommandRatePercent`. A kept-but-deprecated
   `RatePercent` fallback would create three-way precedence conflicts and
   "which key did I set?" ambiguity for a demo whose audience reads its
   README; a clean swap with a documented rename is honest and simpler.
   Old configs with `RatePercent` simply fall through to defaults until
   updated (missing keys ⇒ defaults, as already specified).
2. **Key naming mirrors the interceptor class names** so the mapping
   connection-class → `ConnectionOpeningAsync` (08001) and command-class →
   reader/non-query hooks (57P01) is self-documenting, consistent with the
   existing `Demo:*` vocabulary.
3. **One validation helper, two keys.** `DemoConfiguration` keeps a single
   `ReadInt`-style internal path (0–100 validation, fail-fast naming the
   key) exposed as two methods; each interceptor reads only its own key at
   construction. No Options pattern, matching the established design
   stance.
4. **Session-3 odds are reproduced exactly at defaults.** Both interceptors
   at 50% produce the same per-attempt survival (~25%) the verification
   session recorded, so the archived observations and the spec's
   capture-mining remain valid without restatement.

## Risks / Trade-offs

- **Two keys instead of one costs a little setup friction for the common
  "just make it fault" case** → defaults cover it (both 50% by default);
  key names are stable and documented.
- **The disabled-class asymmetric scenario is probabilistic** → verification
  asserts zero events of the disabled class across a bounded run (a
  bounded "no evidence of" check), matching how the disabled-rate pass was
  verified in the prior change; the semantics are deterministic per
  attempt (rate 0 never throws) even though observation is statistical.
- **README/TAKEAWAYS drift risk if any reference names the old key** → the
  implementation group includes a repo-wide grep-style check on
  `RatePercent` mentions to catch stragglers (task list).

## Open Questions

None. All material choices (key names, no-legacy, per-class 0-disable)
were pre-scoped through the prior change's capture trail.
