# WO-SURVEY-CHAIN — Phase 1 (Claude)

Khảo sát tĩnh trên `origin/dev` @ `a9a85df`. Không sửa code.

## 1. Nguồn "lần khảo sát đã duyệt"

- `survey_sweep.work_order_id` (`SurveySweep.cs:15`), `status = accepted`, `accepted_run_id` (`:38`).
- Kết quả công bố theo cột: `luminance_history (sweep_id, pole_id, run_id, classified_as, evaluated_at)`
  (`SurveyPublication.cs:31-51`), ghi cho **mọi** cột mục tiêu khi accept (`SurveyReviewService.cs:277-283`), kể cả khi
  `pole_current_status` không đổi vì capture cũ hơn (`Late_and_equal_capture_only_add_history…`).
- Fault do accept sinh (`SurveyReviewService.cs:291-316`) đã chống trùng theo **loại hiệu lực + tập mở**, nên đi từ fault
  (`origin_observation_id`) sẽ **bỏ sót** sự cố cũ bị chống trùng → không đáp ứng C-2. Đi từ `luminance_history` thì phủ cả hai.
- Nhiều sweep đã duyệt trên một phiếu: lấy dòng history **mới nhất theo `evaluated_at`** cho mỗi cột (tiebreak `published_at`,
  rồi `sweep_id` theo `length, id`). Lần mới nói `normal` thì cột không còn là phát hiện của phiếu.
- Ánh xạ: `SurveyPublicationRules.FaultFor` (`:83`) — `out → lamp_out`, `dim → lamp_dim`, còn lại null.

## 2. Port

Khai ở WorkOrders (Survey đã tham chiếu WorkOrders — `LuxMap.Modules.Survey.csproj`):

```csharp
public sealed record SurveyFinding(string PoleId, FaultType FaultType);
/// Survey owns what an accepted survey saw; WorkOrders cannot reference Survey.
public interface ISurveyFindingLookup
{
    Task<IReadOnlyList<SurveyFinding>> FindingsAsync(string workOrderId, string communeId, CancellationToken ct);
}
```

Hiện thực `Survey/Review/SurveyFindingLookup.cs`, đăng ký `SurveyModule` cạnh `IPolePhotoPublication` (`SurveyModule.cs:37`).
Lọc `commune_id = <xã chính>` (C-4) ngay trong truy vấn history.

## 3. `FollowUp`

```text
carried = parent.task_kind == survey
    ? faults WHERE commune_id = parent.commune_id AND pole_id ∈ findings.poles AND fault_status ∈ FaultStatusSets.Open
              AND NOT EXISTS active work_order_fault          (C-3)
          → lọc trong bộ nhớ: (pole_id, override ?? fault_type) ∈ findings   (C-2, loại hiệu lực)
    : links fault_present (giữ nguyên FR-2)
```

- Rỗng → 409 `NOTHING_TO_FOLLOW_UP` (giữ mã). `fault_ids` vẫn là tập con, lỗi 400 vẫn kèm `carried_fault_ids`.
- Đua: tập tính **trước** khoá; `Insert` khoá `FaultLocks` rồi đọc lại và kiểm `Eligible` + liên kết đang chạy
  (`WorkOrderService.cs:240-256`) → sự cố bị bác/đã giao trong khe hở cho 409 như FR-2 hiện nay. Chấp nhận (người dùng bấm lại).
- `Eligible(Inspection)` nhận `detected|confirmed|in_progress` — khớp `Open`.

## 4. `AllowedActions`

`FollowUpKinds(Survey).Length > 0` ⇒ Quản lý thấy `follow_up` trên phiếu khảo sát `verified` tự động (`WorkOrderRules.cs:33`).
FE cần biết: nút "Tạo lịch kiểm tra" xuất hiện ở phiếu khảo sát; có thể nhận `NOTHING_TO_FOLLOW_UP`.

## 5. Test / drift / spec

- Đặt trong `SurveyPublicationTests` (đã có phiếu khảo sát, hai xã, `Plant`/`Accept`), gọi `WorkOrderService` trực tiếp:
  - carry = fault mới của xã chính + fault cũ cùng loại bị chống trùng; **không** fault xã B, **không** fault đang ở phiếu khác,
    **không** fault `rejected`, **không** fault khác loại; con có `parent = root = order`, `task_kind = inspection`.
  - sweep sau nói `normal` → 409 `NOTHING_TO_FOLLOW_UP`.
  - phiếu khảo sát chưa `verified` → 409 `INVALID_STATE_TRANSITION`; khảo sát → repair → 409 kèm `allowed_follow_up_kinds = [inspection]`.
  - Sabotage: bỏ lọc xã / bỏ lọc liên kết / bỏ lọc loại → mỗi cái đỏ một assert.
- `Transition_http_literal…`/`allowed_actions` literal: kiểm test hiện có có giả định survey không có `follow_up`.
- Drift: mục mới **SC-CHAIN-1…** (mở rộng FR-2), SELF-SIGNED. OpenAPI không đổi hình dạng (chỉ ngữ nghĩa).
- Teardown `SurveyPublicationTests`: xoá `work_order_fault` + phiếu con trước `fault` / `work_order`.

## 6. D-item

- **D-1** Nhiều sweep: lấy mới nhất theo cột (đề xuất) hay hợp mọi sweep đã duyệt?
- **D-2** Có mang sự cố `in_progress` không đang ở phiếu nào (repair bị huỷ để lại)? `Eligible` cho phép — đề xuất **giữ**, không
  thêm luật.

---

## Gộp với thiết kế độc lập của Codex (20/10/2026)

Hai bản trùng ở nguồn (history của accepted run), luật nhiều sweep (mới nhất trước rồi mới lọc out/dim), port qua Survey, không ghi link vào phiếu khảo sát. **Lấy thêm từ Codex:** lọc theo `data_source`; lọc lại **sau** `FaultLocks` và bỏ sự cố bận thay vì 409 (C-3 đúng cả khi đua); sắp `carried_fault_ids` theo độ dài. **Không lấy:** khoá `FOR NO KEY UPDATE` trên phiếu cha và tách `InsertCore` — nhánh kiểm tra → sửa chữa hiện cũng không khoá cha, tập accepted đổi giữa chừng chỉ làm thiếu/thừa một phát hiện mới, không hỏng dữ liệu; thay bằng tham số `Keep` của `NewWorkOrder`. D-1 chốt theo khuyến nghị chung (mới nhất theo cột). Bản Codex đầy đủ: `.ai/results/WO-SURVEY-CHAIN-p1-by-codex.md`.
