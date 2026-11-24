# Claude PR readiness — spec-fe

21/11/2026 · Native Claude Code · claude-sonnet-5-5

Read-only independent session (Read/Grep/Glob); no shell, writes, push or PR. Original report below, including reviewer assumptions subsequently checked in the aggregate.

**Verdict:** GO to open the backend PR into `dev`, but not to merge or deploy yet. The frontend is NO-GO as a product: it has no suggestions code at all (a search of `luxmap-web/src` found only an unrelated `useElectricalCascade.ts` match). I found no code blocker in the candidate. I ran nothing, so every statement below comes from reading source.

## 1. Spec coverage

**Covered**
- Both GETs carry action-level `ManageWorkOrders` (`SchedulingSuggestionsController.cs:17-18, 28-29`).
- All five groups always appear, with zeros when empty (`SchedulingSuggestions.cs:64-77`). Counts distinguish faults from distinct poles and segments (`:72-73`).
- Fault warnings use the effective type (`:176`) and a severity rank (`:177-178`). A fault counts as held when it has an unreleased `WorkOrderFault` link (`:180`).
- Inspection only takes `detected` faults. Repair takes `confirmed` and `in_progress` through `WorkOrderRules.Eligible` (`:170-171`).
- Missing GPS is `Geom == null`, including poles with no segment, and blockers are explained (`:188-206`).
- Survey is `Geom != null` with no non-unknown status row (`:189-190`). Unknown never becomes a fault. Undrawn roads stay schedulable, and `segment_has_geometry` is informational (`:122`).
- The source filter defaults to `field` (`:149`). No month filter exists.
- Creation reuses the existing commands. GPS goes through `WorkOrderService.cs:240-283`, survey through `:213-234`, and fault orders through `:374-437`. Those commands re-check and return 409 `POLE_ALREADY_IN_WORK_ORDER` or `FAULT_ALREADY_IN_WORK_ORDER` (`:828-831`).
- Upstream `initial_status` is simulated-only and limited to normal, dim or out (`AssetImportService.cs:516-534`). It writes a status row and creates no fault. Those poles never appear as warnings under the default `field` source.

**Gaps and limits (none block opening the PR)**
1. There is no `fault_type` filter. The fault groups mix lamp faults with IoT and segment faults. `FaultType` has 8 values (`DomainEnums.cs:45-55`), so "broken light" is a label the frontend must apply. The summary cannot split lamp faults from the rest.
2. Summary and items are separate transactions (`:62`, `:89`), so a summary count and a page total can disagree.
3. The generated `SchedulingSuggestionItem` marks only `id` and `commune_id` as required. Enum fields are bare `$ref`s without nullability (diff lines 864-965). Frontend code must treat everything else as nullable.
4. The `CROSS_COMMUNE_REFERENCE` branch (`:206`) has no test, as River also noted. It is only reachable by a multi-commune manager.
5. The 24 Claude/River review conclusions are static. I did not re-verify the EF translation of `GroupBy(x => 1)` with `Distinct().Count()` (`:66-74`).

**Conditions before opening**
- The pending merge must be committed so the PR is made from tree `5573451`.
- Read the exit code of the focused regression that is still running.
- The PR text must say the API is SELF-SIGNED and awaiting WP5/WP6 acknowledgement. It must also report the `IotDiagnosticsTests` 503 failure honestly.

**Before merge:** the feature branch has no CI yet, so both shards must pass. The IoT 503 was only reproduced on old base `7d20bf3`, so CI must show it is not present on `5e3178a`. There is no migration, so deploy the API before the frontend.

## 2. Frontend handoff

**What exists:** the survey, GPS, inspection and repair forms, `createWorkOrderRequest` with PDF upload (`workScheduleSaga.ts:343-351`), the 409 messages (`:139-153`), `faultAPI.getFaultDetail` (`faultAPI.ts:39`), and the `?work_order=` deep link. **What is missing:** all suggestion code.

**1. Types.** Run `node scripts/generate-types.js <candidate>/docs/openapi/luxmap-v1.json`. The script accepts a local file (`generate-types.js:29-33`). The raw `luxmap-v1.json` is my choice because the default source is `/swagger/v1/swagger.json` (`:69`). The new types land in `types/workorders/orders.ts` (`:188-191`) and the two enums in `common/enums.ts`. Never edit generated types. The script deletes types absent from the spec (`:356-361`), so check the diff for churn.

**2. `workScheduleAPI.ts`.** Add two GET methods, one for the summary and one for items. Keep them separate from `getAutoDispatchPreview`, which is a POST (`:442`). Send `commune_id`, `segment_id`, `state`, `page` and `page_size` (≤200). Omit `data_source` for the `field` default. Never send `scheduled_from` or `scheduled_to`. Check that `apiClient` serializes a repeated `commune_id` array correctly.

**3. Slice and saga.** Add state for the summary, items per kind, page, total and loading. Refresh both GETs wherever `fetchWorkOrdersRequest` follows a mutation (`:361, 390, 418, 683, 712, 735, 761`). Also refresh on a failed create. That includes every 409 code, and `handleCreateWorkOrder` currently does not refresh on failure (`:365-369`).

**4. `WorkSchedulePage.tsx`.** Add summary cards per kind showing total, ready, in_work_order and blocked. Show "N faults on M poles", never one number. Add item lists with a state filter that defaults to `ready`, plus pagination. Keep the manual create (`:692`) and auto-dispatch (`:693`) buttons.

**5. Prefill.** Reuse `prefillContext` (`:159-173`). `initialPhase` is hard-coded to `'inspection'` (`:207`), so make it follow `recommended_task_kind`. Use `item.commune_id` and not `activeCommuneId`. Only `ready` items are creatable. For `in_work_order` and `overdue` items, open `work_order_id`. For `blocked` items, show `blocking_reason` and send the manager to asset editing.

**Blockers in the existing form**
- **Poleless faults on inspection.** The submit button is disabled without poles unless an IoT target exists (`CreateManualModal.tsx:251-266`). The page treats a fault as IoT only when it has no pole and no segment (`WorkSchedulePage.tsx:184`). A `segment_outage` fault that has a segment but no pole cannot be submitted, although the backend accepts `fault_ids` alone (`WorkOrderService.cs:196-209`). The suggestion item has no IoT metadata, so call `getFaultDetail` for `cabinet_id`, `origin_node_id`, `feeder_id` and `iot_anomaly_code`. IoT faults have no pole or segment (`IotDiagnosticJob.cs:129-130`).
- **Multiple faults per pole.** `PoleSelector` keeps only the first fault per pole (`:90-97`) and overwrites `faultIds` on any toggle (`:139-144`). Submit the suggestion's full `fault_ids` directly. Repair uses `FaultSelector`, which is limited to one segment and 50 items (`FaultSelector.tsx:58-66`).
- **Silent segment fallback.** If the suggested segment is not in the loaded list, the form silently picks the first segment (`CreateManualModal.tsx:129-139`). Block instead.
- **Survey.** The form sends one segment (`WorkSchedulePage.tsx:365-368`). Create one order per segment, or send unique ordered `segment_ids` in one commune (`WorkOrderService.cs:217-219`).
- **GPS.** The QR PDF is a form-only requirement (`CreateManualModal.tsx:265-266`). The backend create does not need it (`WorkOrderService.cs:240-283`), so no backend change is needed. Keep one segment and one commune per order, at most 200 poles, and chunk larger groups.
- **Opening an order outside the calendar month.** The deep link switches month from `scheduled_date` (`:286-292`). An order with a null date cannot be opened that way, so that case needs a decision.
- **Route geometry.** Treat `segment_has_geometry=false` as advisory only. Existing GPS verify already rebuilds route geometry (`workScheduleSaga.ts:566-662`). That behavior is outside this feature, but the product owner should know it exists.

## 3. GO / NO-GO

- **Open backend PR:** GO, once the merge commit is made and the focused regression is green.
- **Merge or deploy:** NO-GO until feature CI passes. Deploy the API before the frontend.
- **Frontend product readiness:** NO-GO. The frontend needs the new code above and the three form fixes: poleless inspection, preserving multiple faults per pole, and the segment guard.

📌 Remaining assumptions: no test results are current, and I did not inspect `apiClient` array serialization.
