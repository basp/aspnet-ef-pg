# Tasks

## 1. Retry-settings configuration (defaults preserve today's behavior)

- [ ] 1.1 Add a `Demo` section to `Host/appsettings.Development.json` with the default values: `FaultInjection:RatePercent: 50`, `Retry:MaxRetryCount: 10`, `Retry:MaxRetryDelaySeconds: 30`. Verify `dotnet build Host` still succeeds and the file parses (demo boots).
- [ ] 1.2 Read `Demo:Retry:MaxRetryCount` and `Demo:Retry:MaxRetryDelaySeconds` in `Program.cs`'s `AddDbContext` lambda and pass them to `EnableRetryOnFailure` (defaults when absent; fail-fast validation per design decision 3). Verify: demo boots, first `/health` logs the same retry behavior as before (behavior indistinguishable with defaults).

## 2. Fault-injection rate configuration

- [ ] 2.1 Inject `IConfiguration` into both interceptors and read `Demo:FaultInjection:RatePercent` at construction (default 50; rate outside 0–100 throws at startup naming the key; 0 disables per design decision 3). Verify by a throwaway run with an out-of-range value failing at boot with the named key, then a 0-rate run: default `50` restored afterward (behavior with default rate unchanged).

## 3. Probe-path command fault injection

- [ ] 3.1 Add a `NonQueryExecutingAsync` override to the command interceptor copying the existing `ReaderExecutingAsync` roll-and-throw shape, with the variant noted in the log (`"Simulating transient error (non-query)."`). Verify `dotnet build Host` succeeds and the interceptor's fault log shows the non-query variant on the first faulted probe run (deferred to group 4's directed check).

## 4. Verification session (manual, per project stance)

- [ ] 4.1 Relaunch the demo against the running `some-postgres` container (`dotnet run --project Host`); restart the container first if it is not up. Verify boot and one clean `/health` (round-trip `.Healthy`, no fault events). Record the setup in this task checkbox as the established sessions did.
- [ ] 4.2 Defaults-equivalence pass: with defaults (50/10/30s), request `/health` ~20 times and exercise `GET /items` + `POST /items`. Expected: outcomes indistinguishable from prior sessions — `Healthy` throughout, retry warnings on faulted attempts, recovery-or-exhaust on data endpoints. Any deviation must be reconciled in the delta spec before continuing.
- [ ] 4.3 Probe-57P01-reachability pass: keep fault injection active and loop `/health` until at least one `"(non-query)"` fault event appears tied to a probe execution, followed by retry warnings and a `Healthy` response. Expected per the delta spec's new probe requirement: health stays healthy, both fault classes visible on the probe path, chain evidence from first session logs.
- [ ] 4.4 Disabled-rate pass: restart with `RatePercent: 0`, run `/health`, `GET /items`, `POST /items`; verify zero simulated fault events and no retry warnings; restore `50` afterward.
- [ ] 4.5 Retry-settings pass: restart with `MaxRetryCount: 2`, `MaxRetryDelaySeconds: 1` and verify the retry ladder escalates then caps per the configured values (retry warnings count ≤ 2, cap ~1s) — as far as the configured geometric-backoff requirement; restore defaults afterward.

## 5. Documentation upkeep

- [ ] 5.1 Update README's "Fault injection" bullet (`NonQueryExecutingAsync` now also throws 57P01) and add a Configuration section documenting the three keys, their defaults, the 0-disables semantic, and the restart-to-apply rule; append a dated note to the "What we observed" probe-subtlety paragraph pointing at the current build's symmetric probe. Verify README's code sketches match the shipped `Program.cs`/interceptor code.
- [ ] 5.2 Reconcile delta spec with the measured session: any discrepancy between the probe requirement's expected log chains and observations is corrected in `specs/ef-connection-resiliency/spec.md` of this change; verify spec claims are experiment-backed or code-mechanic-backed only.
- [ ] 5.3 Append a session note to `TAKEAWAYS.md` (what was reproduced vs. newly learned: probe symmetry reached, knobs verified; keep the n=1/n=2 caveats extended); confirm `QUESTIONS.md` needs no new items (none are expected; the follow-ups it lists are implemented here).

## 6. Final validation

- [ ] 6.1 Run `openspec validate` and address strict-mode findings (scenario hashtag count, requirement length) where reasonable; verify the delta's REMOVED block carries Reason and Migration and both ADDED requirements have ≥1 scenario each.
- [ ] 6.2 Run `openspec status --change "parameterize-fault-injection"`; confirm all artifacts done and the working tree contains only code+docs+spec intended for this change; leave the demo shut down as prior sessions did (killed `dotnet run`/`Host.exe`, database container left running).

## Workflow follow-up

- After review, archive the change so the probe requirement is replaced in `openspec/specs/ef-connection-resiliency/spec.md`; verify the resulting main spec.
- Commit and push (`git push main`) so `basp/aspnet-ef-pg` reflects the change.
