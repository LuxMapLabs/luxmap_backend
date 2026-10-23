# BE-34 Phase 2 — review by Codex (`codex exec -p river`, DeepSeek-V4.1-Flash, read-only, 23–24/10/2026, ~1.26M tokens)

Verbatim findings, then how each was handled. Claude verified every finding against the code first.

## Findings (verbatim)

- **P1** `src/LuxMap.Modules.Survey/Ingest/SurveyRawParser.cs:135` — `Label` accepts `\0`/lone surrogates; `SurveyIngestService.cs:173` writes the object before the DB insert, so a valid JSON escape like `"app_version":"\u0000"` passes parsing, leaves an orphan object, then fails the text insert with 500. Fix: reject control characters and invalid UTF-16 in `Label` before `store.StoreStreamAsync`.
- **P1** `scripts/copy_dev_to_supabase.py:89` — the new `artifact_version` filter drops rows created by a non-copied admin even when kept `iot_node`/`iot_node_firmware` rows reference them, so the COPY aborts on the FK. Fix: include every version referenced by kept devices/history, or null `created_by` for non-copied admins.
- **P2** `src/LuxMap.Persistence/Migrations/20261023155921_AddVersionTraceability.cs:183` — the Down guard checks only `artifact_version`; `iot_node_firmware` can legally point at any registry component, so a history row using a non-firmware version is dropped silently. Fix: also refuse when `iot_node_firmware` has rows or `iot_node.firmware_version_id IS NOT NULL`.
- **P2** `src/LuxMap.Modules.Admin/Versions/ArtifactVersionService.cs:88` — `^[0-9a-f]{64}$` accepts a trailing `\n` in .NET, so a 65-character hash passes validation; PostgreSQL then either rejects it as 500 or stores the newline. Fix: use `\A[0-9a-f]{64}\z` or check `Length == 64`.
- **P2** `src/LuxMap.Modules.Admin/Versions/ArtifactVersionService.cs:46` — metadata is serialized without rejecting `\u0000`/invalid UTF-16; `jsonb` rejects NUL, and the only caught exception is unique violation, so the request returns 500. Fix: validate metadata strings/keys before save or map PostgreSQL `22P05` to `VALIDATION_FAILED`.
- **P2** `src/LuxMap.Modules.Admin/Versions/VersionUsage.cs:69` — `ComposedText` is ambiguous when a model version contains `+`; a fault for `1.0.0+build` is also counted under `1.0.0`. Fix: reject `+` for components that can be composed, or store model and rules separately.
- **P2** `src/LuxMap.Modules.Admin/Versions/VersionUsage.cs:39` — `survey_run` sums five separate column queries, so a run citing the same version id in two columns is counted twice. Fix: count distinct `run_id` with one `@id IN (...)` predicate across the run columns.
- **P2** `src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:298` — `EnsureFirmwareAsync` runs before comparing the node’s current firmware, so every heartbeat performs an `INSERT … ON CONFLICT` plus `SELECT` on the global registry under the device lock. Fix: compare the current version first and call the registry only on a change.
- **P2** `src/LuxMap.Modules.Survey/Ingest/SurveyRawParser.cs:136` — the 200-character cap uses UTF-16 `.Length`, while the DB CHECK uses `char_length`; a valid 200-emoji label is refused by the parser but fits the database. Fix: count Unicode scalar values with `EnumerateRunes()`.
- **P3** `src/LuxMap.Modules.Admin/Versions/ArtifactVersionService.cs:47` — `MetadataMaxBytes` is compared to a UTF-16 string length, not UTF-8 bytes, so non-ASCII metadata is wrongly rejected. Fix: compare `Encoding.UTF8.GetByteCount(raw)`.
- **P3** `src/LuxMap.Modules.Admin/Versions/VersionUsage.cs:69` — composed `left()` predicates and the text-version columns have no supporting indexes, so usage scans `fault`, `citizen_photo`, and `work_order_pole_photo` fully. Fix: add expression indexes on the model prefix or persist a normalized model column.
- **P3** `tests/LuxMap.Api.Tests/VersionedResultColumnTests.cs:40` — `NotCounted` only asserts the column exists; registering `work_order_pole_photo.artifact_version` would double-count photos and the test would stay green. Fix: assert `NotCounted` keys are absent from `registered`.
- **P3** `tests/LuxMap.Api.Tests/VersionedResultColumnTests.cs:24` — the test checks schema→registration only; a typo in a registration’s table/column, or a wrong `ParentKey` at line 49, is never detected and fails only at runtime. Fix: assert every registration exists in `inSchema` and every `ParentKey` matches the parent registration.
- **P3** `tests/LuxMap.Api.Tests/ArtifactVersionTests.cs:74` — the test claims “a device already reported” but registers through the admin API twice; the device-first conflict and immutable hash path are not proven. Fix: heartbeat first, then POST the same version and expect 409.
- **P3** `tests/LuxMap.Api.Tests/MqttChannelTests.cs:166` — the invalid-firmware test asserts no current/history row but not that no `artifact_version` row was created. Fix: assert the registry has no row for the rejected value.
- **P3** `tests/LuxMap.Api.Tests/SurveyIngestTests.cs:222` — “nothing stored” asserts only no DB row, not that the object store was untouched; the NUL/orphan case above would pass this test. Fix: assert the storage count/key is unchanged.
## Disposition (Claude, 24/10/2026)

| # | Finding | Outcome |
|---|---|---|
| P1 | `Label` accepts `\u0000` → object stored, then 500 on insert | **Fixed** — `Label` refuses control characters and lone surrogates before the store write; test `nul` → 400 (sabotage: red) |
| P1 | Copy script drops admin-registered versions a kept device cites → FK abort | **Fixed** — `preflight` refuses up front, naming the versions (the script copies only the 4 seed accounts on purpose) |
| P2 | `Down()` guard ignores `iot_node_firmware` / `iot_node.firmware_version_id` | **Fixed** — guard also refuses when either holds a row |
| P2 | `^…$` accepts a trailing `\n` in .NET | **Fixed** — `\A…\z`; test with 64 hex + `\n` → 400 (sabotage: red) |
| P2 | Metadata `\u0000` → jsonb error → 500 | **Fixed** — `22P05` / `22P02` mapped to 400 `VALIDATION_FAILED`; test (sabotage: red) |
| P2 | `ComposedText` ambiguous when a model version contains `+` | **No change** — model versions are `yolo-<16 hex>` / `fake-<hex>` (`YoloOnOffDetector.cs:52`, `OnOffDetector.cs:54`), never `+`; firmware is never in a composed column |
| P2 | `survey_run` may double count across its five columns | **No change** — each column cites a different component's row, and a `version_id` belongs to exactly one component, so at most one column can match |
| P2 | Every heartbeat runs `INSERT … ON CONFLICT` under the lock | **Fixed** — compares the current firmware first; the registry is touched only on a change |
| P2 | 200-character cap counts UTF-16 units, DB counts scalars | **Fixed** — counts Unicode scalars; test with 200 emoji → accepted |
| P3 | Metadata limit compares UTF-16 length, not UTF-8 bytes | **Fixed** — `Encoding.UTF8.GetByteCount` |
| P3 | No index for the usage scans | **Deferred** — admin-only, rare call; recorded in the results file |
| P3 | `NotCounted` not asserted absent from registrations | **Fixed** |
| P3 | Registrations not checked against the schema | **Fixed** — `Every_registered_table_and_column_exists` |
| P3 | "Device reported first" conflict not actually tested | **Fixed** — heartbeat first, then POST → 409, row unchanged |
| P3 | Invalid-firmware test does not check the registry | **Fixed** |
| P3 | Oversized-label test does not check the object store | **Partly** — the parser runs before `StoreStreamAsync` (`SurveyIngestService.cs:170-173`), and the NUL case now fails in the parser; no store assertion added |
