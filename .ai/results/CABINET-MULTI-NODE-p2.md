# CABINET-MULTI-NODE — phase 1 implementation evidence

Scope / authorization: `.ai/tasks/CABINET-MULTI-NODE.md`, `.ai/results/CABINET-MULTI-NODE-p1.md`.
Decisions: `docs/contract-drift.md`, CAB-MN-1…CAB-MN-4. Phase 2 relay removal is excluded.

## Changes

- Replace cabinet-level uniqueness with a normal index; keep the required cabinet and all commune / cabinet FKs.
- Remove the registry's duplicate-cabinet refusal and obsolete unique-index exception translation.
- Return all mounted node IDs through the existing Assets → Telemetry port, inventory detail/list, and cabinet map.
- Keep the legacy scalar computed from the first ordered ID; relay, firmware, credentials and command protocols unchanged.
- Regenerate OpenAPI and physical/logical DBML; update Q10 and operating traps.
- Independent review: `.ai/reviews/CABINET-MULTI-NODE-by-reviewer.md` (no actionable findings).

## Build and focused regression

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-build --filter 'FullyQualifiedName~CabinetTests|FullyQualifiedName~DeviceRegistryTests|FullyQualifiedName~LightingCommandTests|FullyQualifiedName~CabinetMultiNodeMigrationTests|FullyQualifiedName~MapQueryPlanTests' --logger 'console;verbosity=minimal'
Passed!  - Failed:     0, Passed:    74, Skipped:     0, Total:    74, Duration: 9 s - LuxMap.Api.Tests.dll (net10.0)
```

Focused coverage includes concurrent registrations, cabinet list/detail/map arrays and legacy scalar, empty cabinets,
creation-time and ID-width ordering, all nodes following cabinet movement, per-device relay/credential/command isolation,
same-cabinet and same-commune constraints, and migration apply/down/reapply on a dedicated PostGIS container.

## Mutation checks (restored with cp before final verification)

Temporarily replace the rollback preflight `IF EXISTS (...)` with `IF FALSE`:

```text
CabinetMultiNodeMigrationTests.The_migration_preserves_devices_and_refuses_a_rollback_until_cabinets_have_at_most_one_node [FAIL]
Assert.Contains() Failure: Sub-string not found
String:    "could not create unique index "ux_iot_nod"···
Not found: "Cannot roll back AllowMultipleIotNodesPer"···
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 2 s
dotnet test exit code: 1
```

Temporarily remove the `ThenBy(node => node.NodeId.Length)` tiebreaker from the device lookup:

```text
CabinetTests.Mounted_node_ids_use_creation_time_then_numeric_id_order_on_all_cabinet_surfaces [FAIL]
Assert.Equal() Failure: Collections differ
Expected: ["NODE-999999", "NODE-1000000", "NODE-004"]
Actual:   ["NODE-1000000", "NODE-999999", "NODE-004"]
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 326 ms
dotnet test exit code: 1
```

The dynamic IDs above are output from that test run, not hardcoded fixture IDs.

## Manual HTTP verification

Started a standalone API against a new disposable PostGIS container, applied migrations with
`dotnet ef database update --no-build -p src/LuxMap.Persistence -s src/LuxMap.Api`, seeded it with `--seed`,
logged in as the manager, created one cabinet and two nodes using the same cabinet ID, and read all three cabinet surfaces.
The connection string override targeted only the disposable database. No dev / VPS database was used.

Actual sanitized terminal output:

```json
{
  "database": "cabinet_phase1_smoke",
  "create_cabinet": 201,
  "create_nodes": [201, 201],
  "cabinet_id": "CAB-001",
  "iot_node_ids": ["NODE-001", "NODE-002"],
  "legacy_iot_node_id": "NODE-001",
  "inventory_list_detail_and_map": "all return both nodes in the same order"
}
```

Stopped the smoke API and disposable container afterwards.

## Generated artifacts

```text
$ python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=108 implemented_ops=139 not_implemented_ops=0 schemas=264
$ npx --no-install @redocly/cli lint docs/openapi/luxmap-v1.5.json
Your API description is valid.
You have 9 warnings.
exit code: 0
```

The HEAD baseline was linted separately: also valid with 9 warnings, exit 0 (no new warning).

```text
$ python3 scripts/snapshot_to_dbml.py > docs/database/luxmap_physical.dbml
51 bảng, 147 FK, 8 nhóm
$ python3 scripts/physical_to_logical_dbml.py > docs/database/luxmap_logical.dbml
44 bảng (+1 kế hoạch), 135 FK (37 FK ghép)
$ python3 -m unittest discover -s scripts/tests
Ran 20 tests in 1.178s
OK
```

## Full regression

Final solution-wide run used an isolated EMQX with ephemeral environment credentials and `LUXMAP_MQTT_E2E=1`.
Existing stopped dev broker and `.env` stayed unchanged; the isolated broker was stopped afterwards.

```text
$ LUXMAP_MQTT_E2E=1 dotnet test LuxMap.slnx --disable-build-servers --no-restore -warnaserror --logger 'console;verbosity=minimal'
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54, Duration: 4 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   384, Skipped:     0, Total:   384, Duration: 297 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 544 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:  1208, Skipped:     0, Total:  1208, Duration: 2 m 58 s - LuxMap.Api.Tests.dll (net10.0)
Full .NET regression exit code: 0
```

Total: **1691 passed, 0 failed, 0 skipped**, including real MQTT E2E. Code was rebuilt after restoring both mutations.

## Deployment boundary

No migration applied to `luxmap_dev` or VPS, no push or deployment. Existing user edits are excluded from this commit.
Apply the new migration before enabling multi-node registration on an existing database; rollback refuses cabinets with
multiple nodes instead of removing devices. Client migration: consume `iot_node_ids[]`; the scalar remains compatible.
