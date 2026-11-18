# ASSET-EXPORT-WORKBOOK

Implemented WB-7…WB-11 (SELF-SIGNED, provisional until FW review), subject to the existing import limitation below. No commit.

- Added `Export/AssetExportWorkbook.cs`: scoped count/read, 5000-pole cap, deterministic order, typed OpenXml cells with round-trip double precision, date styles, frozen header, enum dropdowns, optional `Không xuất` sheet.
- Updated `AssetsModule.cs`, `Crud/AssetsController.cs`: DI and `GET /api/v1/assets/export/workbook`, `ReadNetwork`, repeated commune filter, xlsx attachment and 403/413 metadata.
- Added `Import/WorkbookColumns.cs`: one immutable ordered list of 26 columns with required-header flags. Export iterates it; `WorkbookDocument` derives the same three required names in their original order; `AssetWorkbookFile` derives its default headers. No import behavior change.
- Added `AssetExportWorkbookTests.cs`: nine integration methods in `AssetDatabaseCollection`, existing fixture, isolated communes and cleanup. Covers all eight requested categories plus blank code-less segment/cabinet groups. Ordering prefers POLE-9999/10000, falling back to the next free digit boundary if the existing overflow fixture retains those IDs. Bulk limit setup uses checked high IDs without advancing the shared sequence.
- Updated generator SUMMARY, template README, assets traps, `tracking.html`; appended the feeder-geometry finding to the existing drift section, preserving the pre-existing WB edits.

Verification:

```text
dotnet build --disable-build-servers --no-restore -m:1
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:07.16
```

Static Python/XML check: 26 shared headers equal the committed template, every column has an export mapping, no query-filter bypass in export. `git diff --check`: exit 0.

**Tests not run** (no Docker/Postgres). Runtime round trip, OpenXml validation assertions, capability coverage, sabotage and Excel opening remain unverified. Swagger export, spec generation and lint were not attempted; `docs/openapi/*.json` untouched. No migration, packages, capabilities, protected rule/Contract edits, or commit.

Open question: `PlanFeedersAsync` assigns `feeder.Geom = geometry`, while workbook rows have no feeder geometry. Re-import therefore clears an existing drawn feeder route and changes its stamps. Import-created feeders have null geometry and are covered by the unchanged round-trip test. Recorded in drift/traps/template README; fixing import is prohibited in this ticket. A separate decision is needed to preserve existing feeder geometry for workbook rows. WB-11 mixed-data-source and parent-without-poles limitations also remain.

## Claude verification (18/11/2026, outside the sandbox)

- **Open question resolved — import bug fixed in the same PR:** `IImportRow.CarriesGeometry` (default true; workbook: cabinets
  and poles only). `PlanFeedersAsync` assigns `Geom` only when the row carries geometry, so a workbook keeps a drawn feeder route
  (WB-5); CSV / GeoJSON keep "blank geom_wkt clears". New test `Reimport_keeps_a_drawn_feeder_route_and_counts_the_feeder_unchanged`.
- **Test fix:** `Missing_pole_or_feeder_refs…` assumed pole IDs follow sheet order; one import shares `created_at` and EF does not
  insert in sheet order, so the expected order is now derived from the DB IDs (length, then ordinal) — the spec's rule.
- Export + import workbook + CSV import + mock-set tests: 49 pass / 1 pre-existing skip.
- Break-tests (restored with `cp` + `touch`, 10/10 green after): doubles rounded to 6 places → 4 red (round trip, cells, code-less
  groups, feeder route); `Không xuất` routing disabled → 1 red; feeder `Geom` assigned unconditionally → 1 red.
- Full suite and OpenAPI: see the commit.
