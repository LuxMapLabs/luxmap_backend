# River — Spec review

Provider: `river`; model: `deepseek-ai/DeepSeek-V4.1-Flash-1017`. Independent read-only review of the working
change against base 7d20bf3, task, consumer guide, SS-1…SS-5 and existing commands. Original report follows.

**Verified Findings**
No actionable SPEC/CORRECTNESS defect found in the new service, controller, spec, or tests.

I checked the shared rules the spec delegates to: `WorkOrderRules.Eligible` (inspection only `Detected`, repair excludes `Detected`), `WorkOrderOverdueJob.OverdueOn`, `WorkOrderFault`/`WorkOrderPoleTarget` release semantics, `CommuneFilter.Narrow`, source-by-object rules, survey-by-segment holding, distinct counts, paging order, and the raw/consolidated OpenAPI shape. The implementation matches SS-1…SS-5 and the integration doc. The fault segment filter follows the existing `GET /faults?segment_id` semantics, so it is not a mismatch.

**Optional Suggestions**
- **Low — test gap: `CROSS_COMMUNE_REFERENCE` is unexercised.** `src/LuxMap.Modules.WorkOrders/SchedulingSuggestions.cs:205` is reachable only for a multi-commune manager, but `SchedulingSuggestionTests.cs:120-135` uses a single-commune manager and asserts `SEGMENT_NOT_ACCESSIBLE`. Add a test where the manager is scoped to both communes, the pole is in `home` with `SegmentId = foreignSegment`, and assert `CROSS_COMMUNE_REFERENCE` and `state = "blocked"`.
- **Low — test gap: blocked item state/filter is never asserted.** The mapping at `SchedulingSuggestions.cs:114-115` and the `state=blocked` filter are not checked; blocked tests only assert `blocking_reason` and `blocked_count` (`SchedulingSuggestionTests.cs:130-135`, `:276`). Add `Assert.Equal("blocked", item.state)` and a `?state=blocked` request.
- **Low — test gap: overdue fault/GPS source branches.** `SchedulingSuggestions.cs:219-228` is only exercised for inspection-without-faults and survey (`SchedulingSuggestionTests.cs:212-236`, `:283-306`). Add fault-bound inspection/repair and pole-target GPS orders with overdue dates and assert their source filtering. The fault `segment_id` filter (`SchedulingSuggestions.cs:163`) is similarly unexercised; add `?segment_id=` to the fault count test.
- **Low — generated docs:** `docs/openapi/luxmap-v1.5.json:16622` and `:16746` lack the operation `summary` entries defined in `docs/openapi/tools/gen_consolidated_spec.py:220-221`. Re-run the generator to avoid dead `SUMMARY` entries; no runtime impact.