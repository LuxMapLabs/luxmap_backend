# ASSET-WORKBOOK-MULTISHEET

Implemented on `feat/ASSET-WORKBOOK-multisheet`; no commit, per instruction. WB-12…WB-20 remain SELF-SIGNED.

Files changed / added:
- `src/LuxMap.Modules.Assets/Import/`: `WorkbookSheets.cs`, `WorkbookSheetRow.cs` (new); `WorkbookDocument.cs`, `AssetImportWorkbook.cs`, `AssetImportService.cs`, `AssetImportController.cs`, `ImportRow.cs`, `ImportContracts.cs`.
- `src/LuxMap.Modules.Assets/Export/AssetExportWorkbook.cs`; `Crud/AssetsController.cs` (summary only).
- `tests/LuxMap.Api.Tests/`: replaced `AssetImportWorkbookTests.cs`, `AssetExportWorkbookTests.cs`; updated `AssetWorkbookFile.cs`; added `AssetWorkbookTestContext.cs` for own communes, signed fixture clients, snapshots and cleanup. Existing `AssetImportFixture` / `AssetDatabaseCollection` reused.
- `scripts/make_asset_workbook_template.py`, both `docs/templates/assets-workbook*.xlsx`, `docs/templates/README.md`, `docs/agents/traps/assets.md`, `tracking.html`, generator SUMMARY entries in `docs/openapi/tools/gen_consolidated_spec.py`.
- Deleted `Import/WorkbookColumns.cs`, `Import/WorkbookImportRow.cs`, flat-row projection / merge and obsolete one-sheet tests.

Mapping: `Tuyến đường` → `PlanSegmentsAsync`; `Tủ điện` → `PlanCabinetsAsync`; `Mạch điện` → `PlanFeedersAsync`; `Cột điện` → `PlanPolesAsync`; `Bóng đèn` → `PlanFixturesAsync`. Names use trim + NFC; missing sheets contribute no rows. Columns retain the CSV vocabulary. Multipart commune is defaulted only for a single-commune scope and checked with `CommuneFilter.Narrow`; required data_source is validated without a default. `WorkbookSheetRow` injects both through the existing planner indexer. Shared planners handle geometry, new-pole notes and immutable fitting matching with workbook-only behavior flags / matching mode. CSV / GeoJSON defaults retain their behavior.

Kept bounded SAX reading, 100 MB unzipped cap, five advisory locks, FK load order, intermediate saves with whole-file rollback and failed-parent suppression. Errors / warnings use `WorkbookRowError` (sheet + Excel row). Export reads each scoped kind independently, preserves WKT and retired fittings, reports exclusions and source counts. Regenerated templates with the exact requested Python interpreter and inspected the team's reference workbook with it.

Verification:

```text
dotnet build --disable-build-servers --no-restore -m:1
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:06.45
```

Exit code 0. Python / openpyxl checks:

```text
assets-workbook.xlsx: sheet order, headers, colours, Text refs, named ranges, ZIP OK; asset rows [0, 0, 0, 0, 0]
assets-workbook.example.xlsx: sheet order, headers, colours, Text refs, named ranges, ZIP OK; asset rows [2, 1, 1, 3, 1]
```

`git diff --check`: clean. Tests compile; **tests not run**. Integration behavior, mutation checks, Excel UI / dropdown interaction and Swagger export / generation / lint were not verified in this sandbox. Run the workbook integration classes and specified mutations with PostGIS outside the sandbox, then regenerate OpenAPI. No migration, package change, CSV/GeoJSON endpoint change, push, commit, Contract / drift / CLAUDE.md / AGENTS.md / generated OpenAPI JSON edit, or consumer message.

Open questions: none blocking implementation; matched fittings stay immutable (including their stored data_source), as required by the explicit matching rules. Guide / README state this caveat. External verification and WP5 notification remain tracked in `tracking.html`.

## Claude verification (18/11/2026, outside the sandbox)

- **Bug fixed:** a child row whose parent failed in an earlier sheet still got "cabinet_source / feeder_source needs a …_id"
  (the label check ran with the suppressed, null parent). `ImportRowReader.SuppressReference()` sets `ReferenceSuppressed`; both
  label checks skip when it is set. Caught by `Duplicate_refs_report_first_row…` once its assertion printed the rows.
- **Test fixes:** raw `INSERT INTO fixture` missed NOT NULL `lamp_count` and `slot` (2 tests); the template comparison included
  openpyxl's `_xlnm._FilterDatabase` names (compare dropdown ranges only); two tests assumed sheet order for rows of one import
  (shared `created_at`, EF does not insert in sheet order) — now found by ref.
- Workbook + CSV + mock-set + concurrency + capability tests: 74 pass / 1 pre-existing skip. Whole solution: exit 0 —
  Api 1884 pass / 7 skip, Shared 446, Persistence 45, Storage 87, IotBridge 6.
- Break-tests (restored with `cp` + `touch`, green after): rollback off → 17 red; feeder blank clears route → 2 red;
  (pole, install_date) matching off → 10 red; unknown-column error off → 3 red; label suppression off → 1 red.
- OpenAPI regenerated: request gains `commune_id`, `data_source` (required); `WorkbookRowError`; lint valid, 10 warnings as on dev.
