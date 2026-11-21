# WO-SCHEDULING-SUGGESTIONS — PR readiness and FE handoff

21/11/2026. User requested a Claude readiness check and the concrete FE changes; no authorization to push/open PR.

Baseline pinned: **origin/dev 5e3178a8ec3fe70760d9aeecac287f342d873498**, merged PR #289.
Reviewed candidate tree: **5573451008014b7cb78c876ac3c9196abe509486** (code/spec snapshot during merge).
Final local merge commit: **99ea6cc** (same code/spec, adds tracking evidence). `git diff 5e3178a...99ea6cc`
contains the feature, not the already-merged import implementation. Only conflict was the append point in
`docs/contract-drift.md`; both SS and IIS blocks were retained. No merge left pending.

Two independent native Claude Code sessions, configured model **claude-sonnet-5-5**, only Read/Grep/Glob,
strict empty MCP configuration. Reviewer could read the feature worktree and FE repository, not execute tests
or mutate them. Reports predate final QA completion and merge commit; conditions are resolved below.

## Standards

Claude: **GO to open the PR into dev**, no documented hard violation. Checks covered manager capability,
commune scope/scoped metadata, SQL paging/stable IDs, reuse of eligibility/open/overdue definitions,
source separation, read-only snapshots and approved temporary drift placement.

[Verbatim Standards report](WO-SCHEDULING-SUGGESTIONS-pr-readiness-by-claude-standards.md).

Optional judgement observations: severity ranking duplication, string blockers, broad nullable projection,
readability of overdue subqueries, summary performance not benchmarked. These are not confirmed functional
regressions. The summary is not a bbox map endpoint; no measured production-scale latency is claimed.
The microsecond clock difference only supplies a calendar-night calculation in List, not a timestamp write.
No speculative refactor or API expansion made for these observations.

## Spec

Claude: **GO to open the backend PR** conditional on finishing merge and green focused QA; **FE still needs
integration**. No candidate code blocker found. Existing GPS/survey commands/forms can be reused.

[Verbatim Spec and FE report](WO-SCHEDULING-SUGGESTIONS-pr-readiness-by-claude-spec-fe.md).

Confirmed FE work: two API calls and generated types, summary/cards/items state/paging, proper source/commune
filters independent of calendar month, prefill into existing commands, preserving all selected fault IDs,
poleless inspection support, explicit route handling, refresh/409 recovery and direct opening of held/overdue
orders (including undated orders). The concrete mapping and acceptance checklist are in
[the WP5 handoff](../../docs/work-order-scheduling-suggestions.md#handoff-wp5--đối-chiếu-fe-ngày-21112026).

## Corrections and limits checked against actual code

- The Spec report correctly names FE's query **`?work_order=<id>`** (`utils/notifications/notificationRoute.ts:6`),
  while DTOs use `work_order_id`. Use the existing constant, not a new query key. Existing month navigation
  requires scheduled_date plus presence in calendar cases; undated held orders need a direct-detail fallback.
- The original dashboard prefill is fixed to inspection, but the shared prefillContext prop already accepts
  TaskKind. Add a suggestion handler using recommended_task_kind; do not rewrite unrelated dashboard behavior.
- Raw spec is acceptable to the generator, but repository source priority designates the **consolidated**
  `docs/openapi/luxmap-v1.5.json` as machine-readable contract. We rehearsed the actual FE generator with it
  in a temporary tree and typechecked the generated work-order types. No FE files were written. Review
  generator diff when WP5 regenerates; do not hand-edit generated DTOs or assume nullable enum refs are sound.
- `state` and pagination apply to items only. Summary gets commune/segment/source, never state or calendar dates.
  `apiClient` sets no array serializer; explicitly serialize commune_id as repeated keys (URLSearchParams or
  equivalent), avoiding Axios's default bracket notation. No change to global serialization is required.
- FaultType currently has eight values (the raw enum summary comment lists only five); approved IoT extensions
  exist. Fault cards are all actionable faults, not a lamp-only count. No fault_type filter was specified for
  this feature. Use an accurate label; do not client-filter a page then present it as a server total.
- Summary/items use separate HTTP snapshots by design, so concurrent changes can alter counts between calls.
  Creation revalidates and can return 409. Refresh, never treat a suggestion read as a reservation.
- Current tests exercise the inaccessible cross-commune road branch (`SEGMENT_NOT_ACCESSIBLE`), not the
  `CROSS_COMMUNE_REFERENCE` branch for a manager who can see both communes. This is a narrow coverage limit,
  not a confirmed wrong response; no blanket claim of every branch covered.
- FE `PoleSelector` keeps one fault per pole; `FaultSelector` initially loads only 50 and resets selection on
  segment changes. These must not erase suggestion prefill. The manual form also silently falls back to the
  first available route and blocks non-IoT poleless inspections. Confirmed consumer integration obstacles.
- FE GPS verify has pre-existing route reconstruction in `workScheduleSaga.ts:566-662`. The rule against
  inventing route geometry is in **`docs/agents/traps/assets.md:477` (RG-1)**, not RG-5. This needs WP5 handling
  for undrawn routes; it is not introduced by these BE read APIs. No FE behavior was modified here.
- `initial_status` import from current dev only writes new simulated pole status, not faults. Default field
  warnings therefore won't show that demo data; selecting simulated still doesn't synthesize fault warnings.
- Historical full-suite IoT 503 was reproduced on **old base 7d20bf3**, not claimed re-reproduced on current
  dev 5e3178a. The feature's future required CI must settle its current merge result before merge/deploy.

## Fresh runtime verification on merged candidate

All commands exited **0**, checked separately from log text:

- `dotnet build --disable-build-servers -warnaserror`: 0 warning/error.
- HTTP integration regression: **206 passed**, includes new scheduling, GPS, completion, sync, authorization,
  OpenAPI and upstream AssetImportInitialStatus tests; isolated PostGIS test host, no dev database.
- `python3 -m unittest discover -s scripts/tests`: **20 passed**.
- Fresh Swagger export byte-identical to committed raw spec. Consolidated generator unchanged:
  158 paths, 206 operations, 340 schemas.
- Redocly CLI **2.0.0** (CI-pinned) validates with **9 warnings**. Historical unpinned CLI reported 10;
  different CLI versions, not a claimed warning reduction in feature code.
- Original FE generator copied to temporary scripts directory, run on consolidated feature spec: exit0,
  new DTOs in workorders/orders.ts and two enums in common/enums.ts. Generated-only strict TypeScript
  noEmit check with installed FE compiler: exit0. Not a FE application build/UI test.
- No unmerged files, `git diff --check` clean. Original backend checkout and FE working tree untouched.

Full solution suite was **not rerun** during this sync/readiness check. Earlier phase2 full run was
2538 passed / 7 skipped / 1 failed (IoT diagnostics registration 503), reproduced on old base; retain this
in future PR validation notes. No claim the entire suite or feature CI is currently green.

## Publication status

**Ready to open a BE PR into dev.** Both required feature CI shards (`Merge result / work-orders`,
`Merge result / rest`) must pass on the PR/merge candidate before merge; current dev PR289 having green
checks does not establish feature CI. API is additive **SELF-SIGNED pending WP5/WP6 acknowledgement**;
consumer integration remains separate. Deploy API before FE calls the new routes. No migration required.

No push, PR, deployment, FE code change or message to WP5/WP6 was performed. The user must explicitly
allow publication. The local readiness review and handoff work is complete.

Standards: **0 confirmed hard violations**, worst confirmed defect: none.
Spec: **0 confirmed backend functional defects**; FE integration gaps documented separately.
