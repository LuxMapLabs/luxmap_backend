# WO-SCHEDULING-SUGGESTIONS — River review and resolutions

Provider: **river**, configured model **deepseek-ai/DeepSeek-V4.1-Flash-1017**. Baseline 7d20bf3.
Two axes were reviewed independently, first with read-only repository access and then using a frozen source snapshot.
Original reports are retained next to this file. The initial tool-assisted Standards run was stopped without a final
report after prolonged exploration and replaced with a snapshot review; it is not counted as a passing review.
An API response that exhausted its reasoning-token budget without final output is likewise not counted.

## Standards

River snapshot reported one medium finding: OpenAPI bounds supposedly serialized as strings. **Rejected with
runtime evidence**: both raw and consolidated generated JSON contain integer minimum 1 and maximum 200;
OpenApiSpecTests.Scheduling_suggestions_publish_manager_policy_wire_kinds_and_real_pagination calls GetInt32
on maximum and passes. The installed dependency is Microsoft.OpenApi/2.7.5. No speculative library-type change
was applied to already-valid output.

Actual parser output:
```
docs/openapi/luxmap-v1.json page minimum: 1 int maximum: None
docs/openapi/luxmap-v1.json page_size minimum: 1 int maximum: 200
docs/openapi/luxmap-v1.5.json page minimum: 1 int maximum: None
docs/openapi/luxmap-v1.5.json page_size minimum: 1 int maximum: 200
```

The supplementary completed River report objected to documenting SELF-SIGNED drift in x-luxmap-contract.
**Rejected against repo instructions**: the user authorized the feature; AGENTS.md explicitly recognizes
SELF-SIGNED API decisions as temporary foundations pending FW review. SS-1…SS-5 and the task record this
provenance. Existing IotTelemetryHistoryOperationFilter already uses the identical extension pattern, and the
consolidated generator explicitly maps established drift sections. Removing the marker would hide provenance,
not improve conformance. No other hard security, correctness, authorization, scope, paging/N+1 or ordering
violations were reported by that completed review.

Confirmed actionable Standards findings remaining: **0** (two reported items disproved/resolved against evidence).

## Spec

The initial full repository review found **no actionable SPEC/CORRECTNESS defect**. It suggested optional tests:

- Blocked state/filter: added real HTTP state=blocked assertions, requiring blocked state and no recommendation.
- Overdue fault/GPS source branches: added a test with fault-bound inspection/repair from calibration_rig and
  GPS targeting field poles, proving sources are taken from the right objects. Added explicit fault segment filters.
- Cross-commune GPS branch: retained existing guard; a normal manager currently has one commune (ACCT-1), so
  a new multi-commune-account fixture would violate current setup semantics. Existing test verifies that an
  inaccessible referenced road remains blocked and its name is hidden. No production guard was weakened.
- Claimed missing generated summaries: parser lookup proves both summaries exist in the consolidated spec;
  the generator was rerun deterministically. No runtime/docs change was necessary for that claim.

The snapshot review reported a medium concern that a missing current-status row might yield DateTime.MinValue.
**Rejected with source and HTTP evidence**: PoleCurrentStatus.LastSeenAt is already DateTime? (not DateTime),
so the selected query and FirstOrDefault are nullable. Added assertions that both never-observed and unknown
candidates return observed_at JSON null. The final 191-test regression, including those assertions, passes.

Confirmed actionable Spec findings remaining: **0** (one snapshot finding disproved by actual type and response;
initial tool-assisted review had zero defects). Optional test coverage improvements implemented as above.

## Final verification

- Build --disable-build-servers -warnaserror: 0 warnings/errors.
- Final focused HTTP/GPS/offline/capability/OpenAPI regression: **191 passed**, including **13 new tests**.
- Six deliberate guard mutations: correct targeted tests failed; source restored, rebuilt and regression passed.
- Python scripts: 20 passed. OpenAPI: 206 operations, valid, 10 baseline warnings, deterministic export.
- Full suite: one existing IoT server-registration 503 failure, independently reproduced at pristine 7d20bf3;
  not claimed as a passing full suite. See phase-2 evidence for commands, exit codes and exact counts.
- No source changes were needed for the unverified reviewer assumptions; stronger tests were added.
- No migration, dependency changes, deployment, push or PR.

Standards: **0 confirmed remaining**, worst: none. Spec: **0 confirmed remaining**, worst: none.
