# Claude PR readiness — standards

21/11/2026 · Native Claude Code · claude-sonnet-5-5

Read-only independent session (Read/Grep/Glob); no shell, writes, push or PR. Original report below, including reviewer assumptions subsequently checked in the aggregate.

**Standards verdict: GO to open the PR into `dev`.** I found no documented hard violation in the candidate. This is a read-only review of the diff and source; I ran no tests, so build and regression results are as you reported them.

**Checked against CLAUDE.md and the traps**
- **Scope:** both routes use the existing `ManageWorkOrders` capability, so no new capability or `[AllowAnonymous]` (`SchedulingSuggestionsController.cs:18,29`). `CommuneFilter.Narrow` returns 403 `COMMUNE_FORBIDDEN` (`SchedulingSuggestions.cs:146`). Reads go through the query-filtered sets. Road names use the scoped `RoadSegment` set, so out-of-scope names come back null (`:104`, `:205`).
- **Stable IDs and paging:** `OrderBy severity rank, due, observed, created_at, Id.Length, Id` then `Skip/Take` in SQL (`:99-101`). This matches the `created_at, length(id), id` rule. The `POLE-999999`/`POLE-1000000` test is deliberately beyond sequence reach (`testing.md:18`) and has a free-ID precondition.
- **Reuse:** it calls `FaultStatusSets.Open`, `WorkOrderRules.Eligible` and `WorkOrderOverdueJob.OverdueOn` (`:170,215`) rather than copying them. Creation goes through the existing POST, so there is no duplicate write path.
- **Source separation:** the default is `field`, and `data_source` is filtered, never merged (`:149`). Overdue source is derived from the order's targets, not the order.
- **Read-only and snapshot:** no `SaveChanges`, no audit and no notification. Both reads run in a RepeatableRead transaction, and validation happens before it opens (`:62,89`).
- **Layering of docs:** this is a SELF-SIGNED additive entry (SS-1…5) in `contract-drift.md`, and `api-contract-v1.1.md` is untouched, as the rule requires. It does not imply WP5/WP6 consent.
- **No schema change:** `survey_needed` reads `PoleCurrentStatus` as a missing/`unknown` check. Upstream `initial_status` only affects new `simulated` poles, which the default `field` filter excludes.

**Judgement smells (not blockers)**
- **Duplicated Code:** the severity `CASE` is copied again (`:177`). Copies already exist at `AutoDispatchQueries.cs:85` and the private `FaultQueryService.cs:218`. The "not already finished" work-order rule (`:200`) is also written inline.
- **Mysterious Name / Divergent Change:** the `Overdue()` predicate (`:215-229`) is hard to audit. One sparse nullable `Candidate` serves five kinds.
- **Primitive Obsession:** `BlockingReason` is a raw string.
- **Perf evidence:** the summary runs five correlated-subquery counts, with no `EXPLAIN` evidence.
- **Minor:** `List` uses `clock.GetUtcNow()` (`:90`) while `Summary` uses `UtcMicrosecondClock` (`:60`).

**Open versus merge**
- **Open:** fine, with the focused candidate regression finishing green.
- **Merge:** needs feature-branch CI with both shards green, since there is none yet. Resolve the `IoTDiagnosticsTests` 503 against that CI, because latest dev CI was green. Regenerate and lint the OpenAPI output (I couldn't verify the generated JSON).
- **Deploy:** the API deploy must precede any FE call.

**What FE must change** (all manager-only and online)
1. Add calls to `GET /work-orders/suggestions` and `/suggestions/items?kind=…`. Kinds are `fault_inspection`, `fault_repair`, `missing_gps`, `survey_needed` and `overdue`. `state` is optional, and `page_size` is at most 200.
2. Send `data_source` explicitly, because the default is `field` only. Simulated or demo data stays hidden otherwise, and counts from different filters must not be summed.
3. Show `total` and `pole_count` separately (overdue `pole_count` is 0). Show `segment_has_geometry=false` as a prompt to draw the road.
4. Prefill the existing create form only when `state=ready`, using `recommended_task_kind`:
   - Inspection and repair: `fault_ids`, grouped by `commune_id`, at most 200.
   - GPS: `segment_id` plus `pole_ids`, same segment, at most 200.
   - Survey: the item's `commune_id` plus de-duplicated `segment_ids`.
5. For `in_work_order` and `overdue`, link to `work_order_id`. For `blocked`, show `blocking_reason`.
6. Treat suggestions as advisory: POST can still return 409, so reload the summary and page after any create, verify or cancel.
7. Mobile needs no change. Tell WP5/WP6 explicitly, since they haven't acknowledged this.
