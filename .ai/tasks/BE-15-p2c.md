---
ticket: BE-15
title: Phase 2c — Quản lý duyệt phiên khảo sát, công bố trạng thái cột, baseline, sự cố CV, ảnh frame
status: review
phase: 2
owner: codex
branch: feat/BE-15-survey-review
depends_on:
  - "P2b-1 (PR #75) và P2b-2 (PR #76) đã merge: run, pass, pole_observation có phân loại, survey_frame, detection"
  - "Thiết kế Phase 1: .ai/results/BE-15-p1.md §2.5, §2.6, §3 (results/review/thumbnail), §5 bước 13–15, §11 (D-08, D-10, D-11, D-12, D-14) + 'Sửa D-05'"
---

## Bối cảnh

Sau P2b, một phiên khảo sát đã xử lý nằm ở `awaiting_review` với quan sát theo từng cột (phân loại `normal/dim/out/unknown`,
frame đại diện). P2c là bước **Quản lý duyệt**: xem kết quả, **chấp nhận** (công bố) hoặc **trả lại**. Chỉ khi chấp nhận thì bản đồ,
chuỗi độ sáng, baseline và sự cố mới đổi. Mục chạm API là **SELF-SIGNED, nền tạm tới FW**.

🔴 **§2.5 của Phase 1 có câu đã lỗi thời:** "Baseline không có ⇒ ON vẫn unknown". **Đã thay bằng "Sửa D-05" (02/10):** ON mà không
đánh giá được độ sáng ⇒ `normal`, `baseline_ratio = null`, loại khỏi precision/recall của `dim`. Làm theo bản đã sửa.

## Phải đọc trước

- `AGENTS.md` toàn bộ — audit (BE-23a/BE-19: `Fault` là `IAudited`, đúng một event mỗi `SaveChanges`), khoá hàng (`FaultLocks`;
  không `FromSql SELECT *` trên entity có `xmin`), `FaultStatusSets.Open` (không chép tập), CHECK hữu hạn cho số thực,
  `pole_current_status.status_confidence` CHECK 0..1, quy tắc BE-11 (thumbnail qua API, không presigned), `ModelDefaultValueTests`
  (không đặt DB default khác default CLR cho cột value type), partial index, đọc migration, cấm `ExecuteUpdate/Delete`, alias SQL
  snake_case, **`gen_consolidated_spec.py` có stub viết tay** cho `GET /frames/{frame_id}/thumbnail` — hiện thực thì xoá stub `ni(...)`
  và schema viết tay đi kèm.
- `.ai/results/BE-15-p1.md` §2.5, §2.6, §3, §5 bước 13–15, §11; `.ai/results/BE-15-p2b1.md`, `.ai/results/BE-15-p2b2.md`.
- Code: `src/LuxMap.Modules.Survey/**` (Processing, Frames, Ingest), `src/LuxMap.Modules.Faults/**`, `PoleCurrentStatus`.

## Yêu cầu

1. **Schema (một migration):** `luminance_baseline` + `baseline_member` + `luminance_history` theo §2.5; sửa `pole_current_status`
   (thêm `last_evaluated_at`, `last_run_id`); sửa `fault` (thêm `origin_observation_id` FK `pole_observation`); sửa `survey_sweep`
   (`accepted_run_id`, người/lúc duyệt, ghi chú trả lại). Bất biến cho baseline/member/history như các bảng P2b (trigger dùng
   `luxmap.audit_purge`, thêm tên migration vào danh sách miễn của `AuditGuardTests`). FK `Restrict`. `ICommuneScoped` cho bảng có thể
   là gốc truy vấn (`luminance_history`, `luminance_baseline`).
2. **Endpoint** (capability đã khai: `ReadSurveys`, `ReviewSurveys`):
   - `GET /sweeps/{id}/results` — kết quả từng cột của run (mặc định run mới nhất thành công; `run_id` tuỳ chọn), phân trang ≤ 200:
     trạng thái CV, confidence, đỉnh lux, baseline/ratio, `classified_as`, lý do, `frame_id` đại diện, độ tin cậy ghép.
   - `POST /sweeps/{id}/review` — `{client_op_id, run_id, decision: accept|return, note?, expected_version}`; `return` bắt buộc `note`.
     Idempotent theo `client_op_id`; 409 khi run không thuộc sweep / không thành công / sweep đã quyết khác / version cũ; 404 ngoài phạm vi.
     Người duyệt phải có phạm vi phủ **mọi xã** của tập cột trong run (D-03), nếu không 403/409 rõ ràng.
   - `GET /frames/{frame_id}/thumbnail` (Contract §2.7) — JPEG qua API, phân quyền qua sweep cha **trước** khi mở object; 404 ngoài quyền.
3. **Chấp nhận = công bố, trong MỘT transaction** (mỗi xã một `SaveChanges` + một audit, D-10):
   - Mỗi cột trong run một dòng `luminance_history` (cột có nhiều observation ở nhiều lượt: chọn **đại diện theo chất lượng**, không theo
     lux cao nhất; các lượt mâu thuẫn ON/OFF ⇒ `unknown` với lý do). `evaluated_at` = thời điểm khảo sát của observation, không phải giờ duyệt.
   - `pole_current_status`: khoá hàng theo thứ tự `pole_id` (thứ tự khoá, không phải hiển thị); **chỉ ghi khi `evaluated_at` mới hơn
     `last_evaluated_at`** (D-08 — phiên duyệt muộn không đè trạng thái mới hơn; vẫn ghi history). `status_confidence` trong 0..1.
   - **Sự cố CV:** `out` ⇒ `lamp_out`, `dim` ⇒ `lamp_dim` (`source_channel = cv`, D-14), `data_source` của sweep, `origin_observation_id`,
     `detection_model_version` của run, `priority_score` null (CV-16 sau). **Khoá cột rồi kiểm `FaultStatusSets.Open`** (D-12): đã có sự cố
     mở cùng loại hiệu lực (`override_fault_type ?? fault_type`) trên cột ⇒ không tạo thêm. Severity mặc định **cấu hình được, tạm**:
     `lamp_out` → `medium`, `lamp_dim` → `low`, cột `near_sensitive_poi` tăng một bậc (tối đa `high`). Mỗi sự cố một audit (actor `cv`).
   - Sweep `accepted`, `accepted_run_id`; **không** tự hoàn thành phiếu (D-02).
4. **Baseline** (sau khi công bố, cùng transaction): với mỗi cột có quan sát **đủ điều kiện làm member** (CV `on`, đỉnh lux riêng của cột
   — không `peak_shared`/`paired_poles`/`lux_gap`/`lux_saturated`, phân loại không `unknown`), theo **từng chiều đi**, từ các sweep **đã
   chấp nhận**: đủ `BaselineMinimumMembers` (mặc định **3**, cấu hình được, chờ WP4 chốt sau quay thử) ⇒ tạo phiên bản baseline mới
   (bất biến; giá trị = trung vị đỉnh lux của member; ghi member). Hiện thực `ISurveyBaselineLookup` thật thay `EmptySurveyBaselineLookup`:
   khi phân loại một sweep, chỉ dùng baseline có member **thuộc sweep khác và xảy ra trước** sweep đó (không tự chấm), cùng chiều đi.
5. **Trả lại:** sweep `returned`, ghi chú bắt buộc, audit; không công bố gì. Sweep đã gửi là bất biến — khảo sát lại là sweep mới.
6. **Test tích hợp** (Claude chạy trên PostGIS): accept công bố đủ history/current status/faults/baseline; idempotent; D-08 (sweep cũ duyệt
   sau không đè trạng thái mới hơn); không tạo sự cố trùng khi đã có sự cố mở; hai lần duyệt đồng thời chỉ một thắng; phạm vi xã (người
   duyệt thiếu xã ⇒ từ chối); thumbnail ngoài quyền ⇒ 404; baseline không dùng chính sweep đang đánh giá; sau khi có baseline, sweep kế
   tiếp phân loại được `dim`/`normal` (`dim_evaluation_eligible = true`) — chứng minh vòng khép kín bằng chuỗi phiên mô phỏng.
   Test không DB: chọn đại diện theo chất lượng, mâu thuẫn ON/OFF, luật severity, tính baseline (trung vị, đủ member, đúng chiều).
7. Ghi `.ai/results/BE-15-p2c.md` (tiếng Việt); cập nhật `docs/survey-ingest-p2a.md` (hoặc tài liệu khảo sát) phần duyệt; ghi drift
   SELF-SIGNED cho endpoint mới và luật severity tạm vào `docs/contract-drift.md` (mục BE-15).

## KHÔNG ĐƯỢC làm

- Không sửa ghép cột trước khi duyệt (D-11, ticket sau). Không tự đóng/giải quyết sự cố cũ khi đèn sáng lại (để người duyệt).
- Không adapter Roboflow, không gọi dịch vụ ngoài. Không đổi enum Contract.
- **Không đọc `.env`.** Không kết nối DB/Docker/MinIO. Không chạy test cần DB. Không `database update`, không xuất OpenAPI.
- Không thêm NuGet package. Không `ExecuteUpdate/Delete`. Không `IDesignTimeDbContextFactory`. Không commit, không push, không `git add -A`.

## Dừng ở đâu

Xong thì dừng hẳn và ghi results. Claude review, chạy test tích hợp + OpenAPI, lặp review tới khi sạch.
