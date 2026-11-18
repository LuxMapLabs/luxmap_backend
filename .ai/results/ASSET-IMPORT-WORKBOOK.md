# ASSET-IMPORT-WORKBOOK

Implemented WB-1…WB-6 (SELF-SIGNED, provisional until the next FW review). No commit, migration, additional package, Contract edit, or hand-edited OpenAPI.

## Files

- `src/LuxMap.Modules.Assets/Import/`: controller action, workbook result/count contracts; new `AssetImportWorkbook.cs`, `WorkbookDocument.cs`, `WorkbookImportRow.cs`; shared service/row-reader changes.
- `tests/LuxMap.Api.Tests/AssetImportWorkbookTests.cs`, `AssetWorkbookFile.cs`: 19 fact/theory methods covering the 16 requested cases plus shared strings, cached formulas, booleans, Excel errors, and cell/column limits; `AssetDatabaseCollection` / existing `AssetImportFixture`.
- `scripts/make_asset_workbook_template.py`, both `docs/templates/assets-workbook*.xlsx`, template README and fixtures CSV, `tracking.html`.
- Existing task, drift entry and OpenXml package reference were already present and retained.

## Planner reuse

Extracted `LockKindAsync` and `PlanAsync` from the existing per-kind transaction wrapper. The workbook coordinator owns one transaction, takes all five existing advisory keys in ascending order, calls the same planners in `LoadOrder`, and saves between kinds to resolve generated IDs. Any reported error rolls back and clears tracking; counts/warnings are zero/empty.

Five `WorkbookImportRow` projections implement `IImportRow`, preserve Excel row numbers, map internal columns to sheet columns, provide coordinate pairs, and carry failed-parent references. `ImportRowReader` maps errors and can invalidate a dependent row without adding a cascading “matches nothing” error. Workbook references resolve within their defining commune; CSV/GeoJSON retain their original lookup behavior.

`PlanFixturesAsync` has an explicit workbook matching mode using its existing typed validation. Matching active fittings count as unchanged without writes/stamp changes; differences produce cell errors. CSV/GeoJSON fixtures remain insert-only.

## Verification

✅ `dotnet build --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false`: exit 0, **0 warnings / 0 errors**, full solution including tests. Default parallel build stalled; cancelled it and used one worker.

✅ Temporary standalone C# probe against built assemblies (no DB): actual OpenXml parser reads generated templates as 0/5 rows, sparse cells, shared strings/whitespace, cached formulas, numeric integer normalization, booleans and serial dates; rejects >5000 rows and overlong inline/shared cells with sheet column names.

✅ Generator run with the supplied Python interpreter; openpyxl reopened both outputs and checked sheet order, 26 headers, seven dropdowns, date formats, five example rows and required example scenarios. `git diff --check` passed.

⚠️ **Tests not run**: integration, capability coverage, rollback/matching sabotage, and example endpoint ingestion require Docker/PostGIS unavailable in the sandbox. Tests compile. Excel/LibreOffice visual opening was not verified.

⚠️ Swagger CLI export (standard command and direct cached CLI invocation, output targeted to `/private/tmp`) stalled without output; cancelled. OpenAPI export/generation/lint **not done**; `docs/openapi/*.json` untouched. No network restore attempted.

## Remaining / questions

- Run integration/capability tests and the two sabotage checks, export → consolidate → lint OpenAPI, visually open templates, and import the example on the target test DB outside the sandbox.
- Example uses **COM-070** from existing `*.example.csv`, `public_imagery`, and published enum values. Seed/FO-26 code resolves `study_site` dynamically; no fixed commune ID is guaranteed on a fresh DB. Regenerate with `--commune-id <actual-study-site-id>` when necessary. Live DB identity could not be verified here.
- WP5 notification remains pending as recorded in the drift; no messages sent. No implementation questions remain.

## Claude verification (18/11/2026, outside the sandbox)

- **Bug fixed:** `WorkbookImportRow.ColumnName("geom_wkt")` mapped feeders (and segments) to `pole_lat`, so `PlanFeedersAsync`
  read the pole's latitude as a feeder route → every row with pole GPS + `feeder_ref` failed "Expected a LineString". 5 workbook
  tests were red on PostGIS; now maps to `null` for segments / feeders, indexer returns blank.
- **Added:** `WorkbookDocument.MaxUncompressedBytes` (100 MB, sum of declared zip entry sizes) before OpenXml opens the package,
  test `A_workbook_that_inflates_past_the_cap_is_400_before_parsing`. Residual: relies on declared sizes.
- **Added:** test `Committed_templates_parse_and_the_example_fails_only_on_commune_scope` (empty template commits with 0 errors;
  example's only errors are `commune_id` scope). `AssetImportMockSetTests.RepositoryRoot` made `internal` to share it.
- `dotnet test` (whole solution) exit 0 before the last two additions; workbook + mock-set tests 27/27 after them; full re-run below.
- Break-tests (restored with `cp` + `touch`, green after): rollback disabled → 12 red; fixture matching disabled → 2 red
  (re-import, different fixture); zip cap disabled → 1 red; example `road_class = lien_xa` → template test red.
- OpenAPI exported (outside sandbox it completes), `gen_consolidated_spec.py` needed a SUMMARY entry for the new path; diff = 1
  path + `WorkbookImportResult` + `WorkbookKindCounts`; redocly lint valid, 10 warnings = same as origin/dev.
- Not verified: opening the templates in Excel / LibreOffice (neither installed); openpyxl and the endpoint parser read them.
