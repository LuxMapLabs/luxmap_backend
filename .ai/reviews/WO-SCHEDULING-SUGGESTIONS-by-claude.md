# WO-SCHEDULING-SUGGESTIONS — Claude review

21/11/2026 · Claude Code, configured model **claude-sonnet-5-5**. Independent of earlier River findings.

Reviewed feature: **96797a7**, base **7d20bf3**. Diff: `git diff 7d20bf3...96797a7`.
Two independent parallel read-only sessions: Standards and Spec. Only Read/Grep/Glob enabled, no shell,
write tools or external connectors. This review did not run tests, mutate production source, push or open a PR.
The earlier 191-test regression is historical evidence, not a fresh test run.

## Standards

**Claude found no hard violation of documented project standards.** The reviewer checked action-level
ManageWorkOrders authorization, commune scoping/Narrow, read-only behavior, reuse of shared open/eligibility/overdue
rules, nullable SQL comparisons, severity/ID ordering, source separation and test isolation.

Original report: [Claude Standards](WO-SCHEDULING-SUGGESTIONS-by-claude-standards.md).

Optional observations and verification:

1. **P3, duplicated severity ranking** — `src/LuxMap.Modules.WorkOrders/SchedulingSuggestions.cs:177`.
   The same four-level ranking also exists in FaultQueryService (private expression) and AutoDispatchPlanner.
   A shared expression could reduce maintenance, but the present conditional projects into SQL and has the correct
   order. No behavior defect was identified. No cross-module refactor was made during a read-only review.
2. **Inspection eligibility is intentionally narrower** — `SchedulingSuggestions.cs:170`.
   The suggestion group deliberately selects detected faults, as the spec table states. The review's sentence that
   confirmed faults would appear in neither group is incorrect: WorkOrderRules.Eligible(Repair, Confirmed) is true,
   so they appear in fault_repair unless another filter excludes them. The source already does this; no fix needed.
3. **Nullable observation ordering** — `SchedulingSuggestions.cs:100`.
   Both reviewers suggested considering never-observed poles before previously unknown poles. Current publication
   logic explicitly sets LastSeenAt to null for unknown (`Modules.Survey/Review/PoleStatusPublication.cs:22`).
   Survey-needed candidates are absent status rows or unknown rows (`SchedulingSuggestions.cs:189`), so valid
   current data has null ObservedAt in both cases. The proposed scenario with a dated unknown row is not produced
   by the shared writer. Ordering those two cases by a different observation timestamp would be a new prioritization
   requirement, not an established bug. Existing ordering then uses CreatedAt, ID length, ID.
4. **P3, tiny helper duplication** — `SchedulingSuggestionsController.cs:38` and `:41`.
   Empty-vs-absent query handling is correct. Extracting a helper is optional; no change made.

The original report's controller citations :493/:504 and several service citations are captured-diff offsets.
Verified action-level policies are at controller :18 and :29; Narrow is at service :146; scoped road metadata at :104.

Confirmed hard Standards findings: **0**. Optional maintenance suggestions retained: severity ranking and small helper reuse.

## Spec

**Claude found no confirmed P0–P2 functional defect.** Inspection/repair eligibility, target release/hold behavior,
GPS prerequisites, survey on undrawn routes, unknown separation, shared overdue/night boundaries, data sources,
ID ordering and nullable UTC output were compared with the spec and existing commands.

Original report: [Claude Spec](WO-SCHEDULING-SUGGESTIONS-by-claude-spec.md).

- The nullable-ordering suspicion is not a confirmed defect; see the actual writer invariant above.
- Overdue reads historical target links as well as held links (`SchedulingSuggestions.cs:221`). Current commands
  release targets only when orders are verified/cancelled, while overdue selects open/assigned/in_progress.
  Thus there is no current conflicting state. Revisit this query if individual targets can ever be released while
  an order remains active; that capability is not part of the current request or data model.
- Faults without a pole legitimately have pole_count = 0; the warning item represents a fault, including IoT faults.

Confirmed functional Spec findings: **0**. No production or public-contract change made.

## Limits and disposition

Static independent review only. Claude did not freshly execute EF translation, the HTTP suite or generator checks.
Historical runtime evidence and the known baseline IoT 503 remain in the phase-2 results; this review does not
turn the earlier full-suite failure into a pass. No new deviation, schema requirement or capability change was found.
The requested review is complete. Optional refactors are not prerequisites to using the delivered feature.

Standards: **0 hard violations**, two minor maintenance suggestions; worst confirmed defect: none.
Spec: **0 confirmed functional defects**; worst confirmed defect: none.
