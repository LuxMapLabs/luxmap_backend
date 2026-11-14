# CABINET-MULTI-NODE-DEV — independent source review

Reviewed 15/11/2026 in `/Users/nhm809/Documents/LuxMap/wt-cabinet-multi-dev`,
branch `feat/CABINET-multiple-iot-nodes-dev`, against base `df35665` and
`.ai/tasks/CABINET-MULTI-NODE-DEV.md`.

## Result

No actionable source correctness, dependency-injection, commune-scope, concurrency, or migration findings
remain in the reviewed implementation. No implementation files were modified by the reviewer.

## Specific checks

- Cabinet-level uniqueness is removed in service/model/migration while required cabinet and composite
  commune/same-cabinet FKs remain. The rollback locks the node table and refuses duplicate cabinets without
  deleting or merging devices. Model changes are limited to that index, apart from generated file encoding.
- Inventory lookup groups all ordered node IDs per cabinet; inventory and map use the same ordering and keep
  nullable `iot_node_id` as the first array element. Dev's `PhotoCount`, `CoverPhoto`, `AssetPhotos`, cabinet
  feeder details and feeder `PowerState` survive the conflict resolution.
- `IotFeederSourceLookup` is singleton-safe: its only retained dependency is `IOptions<IotOptions>`; every
  query uses the context passed by the caller. MQTT therefore reads Bridge bindings under its fixed commune
  context, rather than resolving an unrelated HTTP-scoped context.
- The existing dependency direction remains IOT → Telemetry → Assets. Telemetry depends on an interface
  implemented in IOT, without a reverse project reference. The singleton MQTT factory does not capture a
  scoped DbContext or diagnostics client through that interface.
- Source registration and relay assignment acquire locks in node → feeder order. After the feeder lock,
  both inspect the opposing channel before writing. Lighting plan/delivery and Bridge command sending reject
  pre-existing conflicting ownership through the shared lookup.
- Configured and persisted sources are combined without dictionary duplicate-key exceptions; incompatible
  feeder ownership produces a named 409. Scoped candidate nodes prevent exposure of unrelated communes.
- Persisted Bridge → Bridge duplication is already blocked by the **DB-only** unique expression index
  `ux_iot_source_registration_feeder_id` from `20261114120000_UniqueIotSourceFeeder`; it is intentionally absent
  from the EF snapshot. `SaveAsync` translates its 23505 into 409. Do not infer missing uniqueness from the
  snapshot or from `ReadAsync([nodeId])` alone.
- Phase 1 still retains Telemetry, relays, firmware protocol, permissions and historical tables. No phase 2
  module deletion or node-to-one-feeder constraint was introduced.

## Reviewer verification

```text
$ git diff --check HEAD -- src tests
(no output; exit 0)
$ rg -n '^(<<<<<<<|=======|>>>>>>>)' src tests
(no matches)
$ rg -n 'ux_iot_source_registration_feeder_id|profile.*feeder_id' src/LuxMap.Persistence/Migrations/20261114120000_UniqueIotSourceFeeder.cs
14:            CREATE UNIQUE INDEX ux_iot_source_registration_feeder_id
15:                ON iot_source_registration ((profile ->> 'feeder_id'));
21:        migrationBuilder.Sql("DROP INDEX ux_iot_source_registration_feeder_id;");
```

The implementing agent owns build, test, sabotage and HTTP/DB smoke evidence. The reviewer did not run
duplicate test/build processes. Generated OpenAPI was still being regenerated when this source review ran,
so stale intermediate JSON was excluded; regeneration/lint and final dashboard/results remain completion checks.
The original workspace, dev/VPS databases, remote branches and deployment were not touched.

## Follow-up — bulk query regression

Reviewed the final `IotFeederSourceLookup` left join and `FeederStateQueryTests` update on 15/11/2026.
No actionable findings. Folding the relay owner into the scoped feeder/node candidate query preserves
the conflict check, including the no-relay case (`DefaultIfEmpty`). `feeder_control` has feeder ID as its
primary key, so this join adds at most one control owner per candidate and does not multiply source owners.
The existing commune filters still apply to every joined root.

The query regression continues to compare page sizes 1 and 200 and now requires exactly two bulk queries
touching `feeder_control` (relay state and Bridge ownership), with joins in both captured queries. This
accounts for the new guard without allowing one query per feeder. The implementing agent observed six
total SQL queries at both sizes; the reviewer did not rerun the suite.

```text
$ git diff --check HEAD -- src/LuxMap.Modules.IOT/IotFeederSourceLookup.cs tests/LuxMap.Api.Tests/FeederStateQueryTests.cs
(no output; exit 0)
```
