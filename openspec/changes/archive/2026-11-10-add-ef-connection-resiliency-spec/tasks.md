# Tasks

## 1. Verification environment and baseline re-confirmation

- [x] 1.1 Relaunch the demo against the existing `some-postgres` container (`dotnet run --project Host`) and verify boot plus one clean `/health` request: response `Healthy`, log line confirms the EF close (`SELECT 1`, `Executed DbCommand`) without faults. Postgres is already running on `localhost:5432` with matching credentials.
- [x] 1.2 Re-confirm the 2026-11-09 baseline under sustained connection-fault injection: request `/health` ~20 times and verify the Requirement 1 and 2 log evidence firsthand (`Healthy` on every response; retry warnings for faulted attempts; zero command-level 57P01 events tied to the probe path). Record any deviation from the baseline before continuing.

## 2. Hard-down experiment (gates spec reconciliation for the unreachable-database case)

- [x] 2.1 With the demo running, stop the database (`docker stop some-postgres`) and request `/health` repeatedly; record the observed stall duration and final health-check result. Expected per design decision 1: escalation of retry delays (capped at 30s), exhaustion of the retry budget (~121s of delays), then an `Unhealthy`-level outcome, with the retry-limit-exceeded log event. Verify by console log evidence captured alongside the response statuses. *(Note: service outage window — restart in 2.2.)*
- [x] 2.2 Restart the database (`docker start some-postgres`), re-request `/health` until it recovers, and verify the baseline healthy state is restored (round-trip leaves the demo and database in the pre-experiment state).
- [x] 2.3 Reconcile the delta spec with the measured outcome: if observations match the expected shape, extend the health-check requirement with a hard-down scenario (persistent outage ends in an unhealthy result after the retry budget); if they contradict it, revise the requirement to describe what the environment actually produces. Verify `specs/ef-connection-resiliency/spec.md` reflects only experiment-backed or code-mechanic-backed claims.

## 3. Write-path experiment (gates whether the write path earns a requirement)

- [x] 3.1 Loop `POST /items` (and a `GET /items` comparison) while both fault injectors are active and verify which fault classes occur on the write path: expected is connection-level (08001) only, because writes execute via non-reader command paths the 57P01 interceptor does not cover. Verify from the console log (fault events + successful write completions). Capture at least one fully faulted retry sequence and one recovery. *(Outcome recorded: expectation WRONG — EF inserts execute via `ExecuteReader`, so 57P01 is reachable on writes; 2 of 8 command-fault events hit inserts. Full recovery seen on all 8 POSTs incl. deep sequences (1.1s, 11.9s); one fully faulted retry sequence captured on the read path (~177.7s to RetryLimitExceededException).)*
- [x] 3.2 Decide the write path's spec fate with fixed wording: if the write path behaves as the query path (recover-or-exhaust under connection faults), add it to the existing query-path requirement's scenarios; if it shows genuinely different behavior, add its own ADDED requirement block. Either way, update the write-path entry in `QUESTIONS.md` with the measured outcome. Verify the delta spec and QUESTIONS.md tell the same story. *(Both done: requirement renamed to data endpoints with an item-creation scenario added; QUESTIONS.md item 2 records the corrected understanding.)*

## 4. Evidence document upkeep

- [x] 4.1 Update `QUESTIONS.md`: mark the hard-down health check, write-path, and (if performed) `customTestQuery`-redundancy items as resolved with one-line outcomes so the doc cannot mislead a later reader into thinking they are still open. Verify against the experiment notes from groups 2–3.
- [x] 4.2 Add a short second-session confirmation note to `TAKEAWAYS.md` (what was reproduced, what was newly learned, same n=1 caveat extended to n=2 sessions) and confirm `README.md` links still point correctly. Verify the docs' numbers match the session logs, not memory.

## 5. Final validation

- [x] 5.1 Run `openspec validate` for the change and confirm it passes (address any strict-mode warnings where reasonable, e.g. scenario format); verify `specs/ef-connection-resiliency/spec.md` has `## Purpose`, `### Requirement:` blocks with ≥1 four-hashtag scenario each, and no requirement text above the 500-character warning threshold.
- [x] 5.2 Re-run `openspec status --change "add-ef-connection-resiliency-spec"` and verify all artifacts report done; confirm the change is ready for review and sync. *(Status: 4/4 artifacts done; delta conflicts none; tasks all marked in this final pass.)*

## Workflow follow-up

- After team review, archive the change (`/opsx-archive`) so `ef-connection-resiliency` becomes the project's first main spec, then verify the archived result under `openspec/specs/`.
- Demo improvements (command-level fault coverage for the probe, README cleanup, parameterized fault rate) intentionally remain outside this change; capture each as its own future OpenSpec change when wanted.
