# PR #256 vs CABINET-MULTI-NODE phase 1

Reviewed for Mỹ on 14/11/2026. PR: https://github.com/LuxMapLabs/luxmap_backend/pull/256

## Pinned revisions and scope

- PR base (`dev`): `3dc41c4a40f8667a959ba5c6654758d62491eaee`.
- PR head: `39d21d4bc74c8792a121727e1e6b80619d8d7308` (`Link IoT bridge nodes directly to feeders`).
- Phase 1 code: `6c7c9793dbbd7ff417648b420a56756da2dbd22a`; branch head reviewed: `ff2e64c`.
- PR diff: `git diff 3dc41c4...39d21d4`; phase 1 diff: `git diff b280775...6c7c979`.
- User asked for compatibility review, not fixes, merging or phase 2 implementation.
- Two independent Codex CLI `0.164.0` reviews used provider `river`, model
  `deepseek-ai/DeepSeek-V4.1-Flash-1017`, effort high, sandbox read-only; both exited 0.
- Standards session: `01a1dae8-9561-7501-9fef-66e49c49c2c6`.
- Spec session: `01a1dae8-9561-7c70-aabf-1dceb669edf1`.
- GitHub reported PR `MERGEABLE` against dev; both CI shards succeeded
  ([CI run](https://github.com/LuxMapLabs/luxmap_backend/actions/runs/43612681786)).
  Its `mergeStateStatus` was `BLOCKED`; MERGEABLE does not mean it has merged or that phase 1 is included.

## Verified merge simulation

`git merge-tree --write-tree --name-only` writes merge objects only, without changing the working tree or branches.

```text
origin/dev + origin/pr-256: exit 0, no conflict
origin/dev + ff2e64c:       exit 1, 10 conflict paths
origin/pr-256 + ff2e64c:    exit 1, the SAME 10 conflict paths
```

Conflicts with the current phase-1 branch:

- `docs/contract-drift.md`
- `docs/database/erd.md`
- `docs/openapi/luxmap-v1.5.json`
- `docs/openapi/luxmap-v1.json`
- `docs/openapi/tools/gen_consolidated_spec.py`
- `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs`
- `src/LuxMap.Modules.Map/Features/MapProperties.cs`
- `src/LuxMap.Modules.Map/Features/MapQueryService.cs`
- `tests/LuxMap.Api.Tests/CabinetTests.cs`
- `tracking.html`


These conflicts already exist against updated dev; do not attribute them solely to #256.
The three C# conflicts must preserve the newer photo/gallery and feeder-power fields **together with** phase 1's ordered
`iot_node_ids[]` and computed legacy scalar. Choosing either whole side loses behavior. Export/regenerate OpenAPI after
resolving source; do not keep one side's generated JSON wholesale.

Migration operations do not oppose one another: phase 1 relaxes `iot_node.cabinet_id` uniqueness, while #256 adds
uniqueness on `iot_source_registration`'s JSON feeder ID. No table removal is introduced by either migration.

## Concrete combined-behavior checks

### P2: duplicate configured + DB bridge mapping crashes feeder reads

PR-head `src/LuxMap.Modules.IOT/IotDiagnosticsService.cs:276–290` gathers every node sharing a cabinet and calls
`links.Add(row.FeederId, ...)`. A DB source for node A plus a deployment-config source for node B can both name feeder F.
The new unique index covers only DB rows, and the configured-source check runs only when saving a source.
With phase 1 allowing both nodes in one cabinet, the next `GET /assets/feeders` throws a duplicate-key exception → 500.
The same helper is used by map power-state reads. This is reproduced, not inferred from merge markers.

### Configuration consistency risk: Bridge and Telemetry can name different controllers

PR-head `IotDiagnosticsController.cs:173–183` validates only same cabinet/commune; `CompatibleFeederAsync` at
`IotDiagnosticsService.cs:266–272` has the same rule. If Telemetry's `feeder_control` assigns F to B, Bridge can register
A's source for F. Observed: `/assets/feeders` returns `control.node_id = B`, while `/iot/poles/{id}/control` returns A.
Bridge power is also taken from A, while the legacy relay path retains B. This may send operators to two different nodes
for the same feeder. The reviewers differ on whether independent channels are intentional; retain this as a concrete
integration/design risk requiring an explicit policy, not an unsupported claim that #256 physically deletes relay support.
Actual command/topology redesign would be outside this review and phase 1.

## Reproduction evidence and limits

A detached temporary checkout of **PR head** ran existing Testcontainers/LuxMapTestHost fixtures on a fresh PostGIS container.
Only temporary test code was added. It dropped the old cabinet unique index inside that container and created the second node
through the test fixture, reproducing phase 1's cardinality. The phase-1 branch and dev/VPS databases were not modified.
The duplicate configured source was installed into the host's IOptions value after persisting the first DB source, equivalent
to a deployment retaining that DB row while introducing a second static source; no deployment config was edited.

This is a focused simulation of the cardinality change, **not** a build or test of a fully resolved combined merge.
Patch: `PR-256-phase1-repro.patch` (zero context, applies to the pinned PR checkout with
`git apply --unidiff-zero <path-to-patch>`; intentionally contains failing expectations). The reconstructed patched blob
was checked against the original reproduction blob hash `598cdb2`. The temporary checkout and private test configuration
were removed after saving this patch.

```text
$ dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-restore --filter FullyQualifiedName~PR256_phase1_ --logger 'console;verbosity=minimal'
[xUnit.net 00:00:07.02]     LuxMap.Api.Tests.IotDiagnosticsTests.PR256_phase1_should_not_expose_two_controllers_for_one_feeder [FAIL]
[xUnit.net 00:00:07.27]     LuxMap.Api.Tests.IotDiagnosticsTests.PR256_phase1_duplicate_config_and_database_feeder_should_not_crash_reads [FAIL]
  Failed LuxMap.Api.Tests.IotDiagnosticsTests.PR256_phase1_should_not_expose_two_controllers_for_one_feeder [580 ms]
Expected: "NODE-005"
Actual:   "NODE-004"
  Failed LuxMap.Api.Tests.IotDiagnosticsTests.PR256_phase1_duplicate_config_and_database_feeder_should_not_crash_reads [248 ms]
   HTTP 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"a3a678a6-cbc5-4b30-9af1-e66b390f6dd8"}}}
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 832 ms - LuxMap.Api.Tests.dll (net10.0)
process exit code: 1
```

Dynamic NODE IDs are actual test output, not fixed IDs used in the fixture. A failing reproduction demonstrates the problem;
it does not mean the PR's original CI suite failed. No full combined build is claimed while conflicts remain unresolved.

## Standards (raw river report)

**Axis: phase-1 compatibility**

- **P2 — New semantic conflict: same-cabinet source mapping can name a different node than the feeder’s phase-1 controller.** `IotDiagnosticsController.cs:173-186` accepts `POST/PUT /iot/nodes/{nodeId}/source` when the node and feeder merely share cabinet/commune; `IotDiagnosticsService.cs:252-260` uses the same check for commands. Phase 1 keeps `feeder_control`, whose primary key is `feeder_id` (`IotConfigurations.cs:95`), so in a multi-node cabinet feeder F is wired to exactly one node, B. Trigger: nodes A and B in one cabinet; F is B’s `feeder_control`; register A’s source as F. Registration succeeds. Then `AssetCrudService.cs:56-64` returns `control` for F from B while returning `power_state` for F from A; `POST /iot/nodes/A/command` targets A. Operator-visible result: relay/control and power come from different controllers, and ON/OFF may be sent to the non-wired node. Required: until phase 2 removes relays, reject a source that contradicts an existing `feeder_control` row, or explicitly return which node produced control versus power. The new unique index is only on `iot_source_registration` (`20261114120000_UniqueIotSourceFeeder.cs:14-15`), so it does not enforce agreement with `feeder_control`. This is a newly introduced interaction, not a preexisting one.

- **P2 — Text merge conflict with array-loss risk.** PR head still exposes only scalar `IotNodeId` in `MapProperties.cs:176-203`, `MapQueryService.cs:421-441`, and `AssetCrudService.cs:66,172`; phase 1 adds required `iot_node_ids[]` plus legacy scalar = first element ordered by `created_at, length(id), id`. Resolving these hunks to the PR side drops the arrays. Keeping both naively is also wrong: PR’s `IotNodeId = row.IotNodeId` is an unordered `FirstOrDefault()` (`MapQueryService.cs:421-424`), so with multiple nodes it may differ from `iot_node_ids[0]`. Required: retain phase 1’s ordered array and derive the scalar from it, while retaining PR’s power-state lookup.

- **P3 — Migration rule breach relevant to the same conflict area.** `20261114120000_UniqueIotSourceFeeder.cs:13-16` creates the unique index only in raw SQL; it is absent from the EF model/snapshot. The repo’s migration trap says a DB-only constraint not in the snapshot is invisible to future migrations. After resolving the phase-1/PR snapshot conflict, this index can be silently omitted from generated schema changes.

No proven duplicate-feeder migration failure from phase 1: `feeder_control.feeder_id` is the PK, so legacy phase-1-valid source registrations are already feeder-unique. Read-only inspection; no build/tests run.

## Spec (raw river report)

**Axis report — #256 vs CABINET-MULTI-NODE phase 1**

- **P2 — merge hazard, not a head-code defect:** PR-head `src/LuxMap.Modules.Map/Features/MapQueryService.cs:415-434` still constructs cabinet properties with scalar `IotNodeId`. Phase 1 changes this same `CabinetsAsync` body to ordered `IotNodeIds[]` (D-3). Because merge-tree conflicts here, resolving from the PR side would silently lose `iot_node_ids[]`. Resolution must retain phase 1’s array and add the PR’s feeder `power_state`. The same union is required at `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:31-32,99-108`: keep `ICabinetDeviceLookup` arrays while adding `IFeederPowerLookup`. This is a text/conflict-resolution risk; the PR head predates phase 1 and does not itself prove array removal.

- **P2 — stale source uniqueness:** `src/LuxMap.Modules.IOT/IotDiagnosticsService.cs:286-290` calls `links.Add(feederId, …)` without guarding duplicate feeder links. Uniqueness exists only for DB source rows (`src/LuxMap.Persistence/Migrations/20261114120000_UniqueIotSourceFeeder.cs:11-17`). `IotRegistrationStore.cs:144-148` checks configured nodes only during source writes, so a later configuration change can leave one configured node and a different DB node in the same cabinet both claiming feeder `F`. Phase 1 makes that cabinet state valid. On the next `/map/poles`, `/map/cabinets`, or feeder-power read, `Dictionary.Add` throws instead of returning `unknown`/`conflict`. This violates the bridge rule “one feeder has at most one node via bridge” and is newly reachable because multiple nodes may inhabit the cabinet. It is not evidence that a same-cabinet check proves same-controller mapping.

- **No semantic cardinality conflict found:** distinct bridge-source feeders on multiple nodes resolve independently. Bridge-source versus `feeder_control` divergence is intentional per `IOT-BRIDGE-FEEDER` (“API relay của Telemetry tiếp tục…”), so I do not classify it as relay removal. However, `tests/LuxMap.Api.Tests/IotDiagnosticsTests.cs:146-167` exercises only one node in the cabinet; the duplicate/stale mapping path above remains untested.

## Maintainer validation of raw observations

- Standards' same-feeder/two-controller behavior is reproduced; Spec considers channel separation intentional. Keep both
  interpretations visible; the concrete mismatch is established, the desired cross-channel ownership policy needs a decision.
- Both axes' array-loss observation is a real **conflict-resolution hazard**, not proof that #256 independently removes
  phase 1's array. The old phase-1 branch is not part of the PR base.
- Spec's configured/DB duplicate-key crash is reproduced with HTTP 500.
- Standards' P3 assertion that a raw index is itself forbidden or will be dropped by the phase-1 snapshot merge is **not
  confirmed**. This repository already permits named DB-only constraints; the snapshot auto-merged in merge-tree, and no
  generated operation dropping the bridge index was observed. Do not turn that claim into a blocking finding.
- Raw reports use some approximate line references; validated source locations for commands are `IotDiagnosticsService.cs:350–357`,
  mapping validation `IotDiagnosticsController.cs:173–183`, duplicate insertion `IotDiagnosticsService.cs:286–290`.

Summary per axis: Standards reported 2 P2 observations + 1 unconfirmed P3 (worst reported P2);
Spec reported 2 P2 observations (worst P2). Reports stay separate and are not merged/reranked.
Confirmed outcome: 10 inherited text conflicts, a reproduced duplicate-mapping 500, and a reproduced cross-channel controller
mismatch whose policy needs to be explicit before combining both control paths.
No changes were posted to GitHub; no merge, deployment, push or production fix performed.
