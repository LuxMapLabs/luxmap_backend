---
ticket: WSC-PHOTO
title: Follow-up từ phiếu khảo sát mang sang cả sự cố từ ảnh/clip cột đã được Quản lý xác nhận
status: ready
phase: 2               # quyết định đã chốt (Mỹ, 17/11/2026) — implement thẳng, không phase 1
owner: codex
branch: fix/WSC-PHOTO-follow-up
contract_refs:
  - "docs/contract-drift.md — WO-SURVEY-CHAIN (WSC-1…WSC-5)"
  - "docs/contract-drift.md — POLE-PHOTO-AI (PP-9, PP-10), PHOTO-REVIEW (PP-14, PP-15)"
depends_on: []
---

## Bối cảnh

WP5 (Khang) báo: `POST /work-orders/{id}/follow-up` trên phiếu **khảo sát** luôn trả 409 `NOTHING_TO_FOLLOW_UP` khi kỹ sư khảo
sát bằng ảnh / clip từng cột (`work_order_pole_photo`) thay vì sweep. Nguyên nhân: `SurveyFindingLookup.FindingsAsync`
(`src/LuxMap.Modules.Survey/Review/SurveyFindingLookup.cs`) chỉ đọc `survey_sweep` đã duyệt + `luminance_history`. Trong khi đó
Quản lý confirm ảnh `off` **đã** tạo sự cố `lamp_out` confirmed (`ReviewedPolePhotoPublisher`) — follow-up chỉ không thấy cột đó.

Đây là sửa lỗi hành vi, **không đổi bề mặt API** (không trường mới, không mã lỗi mới, không migration).

## Phải đọc trước

- `CLAUDE.md` + `docs/agents/traps/survey.md`, `docs/agents/traps/faults-workorders.md`, `docs/agents/traps/testing.md`
- `docs/contract-drift.md` mục **WO-SURVEY-CHAIN** và **POLE-PHOTO-AI** / **PHOTO-REVIEW**
- `src/LuxMap.Modules.Survey/Review/SurveyFindingLookup.cs`, `ReviewedPolePhotoPublisher.cs`, `SurveyPublicationRules`
- `src/LuxMap.Modules.WorkOrders/ISurveyFindingLookup.cs`, `WorkOrderService.FollowUp`, `WorkOrderPolePhotoService.DecideAsync`
- Test hiện có của WO-SURVEY-CHAIN (tìm theo `NOTHING_TO_FOLLOW_UP` / `FollowUp` trong `tests/`)

## Yêu cầu

1. `SurveyFindingLookup.FindingsAsync(workOrderId, communeId)` xét thêm **ảnh/clip cột của chính phiếu đó**:
   `WorkOrderId == workOrderId && CommuneId == communeId && Decision == "confirm"`. Nhãn hiệu lực = `FinalCvState ?? CvState`
   (confirm bảo đảm là `on`/`off`). Ảnh `reject` hoặc chưa quyết **không** tính. Bất kể `StatusApplied` (giống sweep: lịch sử theo
   phiếu, không theo fault).
2. Gộp theo cột, xử lý **theo thứ tự thời gian quan sát** (sweep: `luminance_history.evaluated_at`; ảnh: `captured_at`) — đúng ngữ nghĩa
   publisher, KHÔNG phải "lấy bản mới nhất rồi map" một cách ngây thơ:
   - kết quả sweep → trạng thái = `classified_as` của nó (nguồn dữ liệu của dòng history);
   - ảnh `off` → `out` (nguồn dữ liệu của ảnh);
   - ảnh `on` → nếu trạng thái đang là `dim` hoặc `normal` thì **giữ nguyên** (ON không bác được dim — luật publisher,
     `classification_preserved`); còn lại (`out`, `unknown`, chưa có) → `normal`.
   - Trạng thái cuối → `SurveyPublicationRules.FaultFor` như hiện tại (out → lamp_out, dim → lamp_dim, còn lại không có finding).
   - Hoà giờ giữa sweep và ảnh: **sweep thắng** (publisher chỉ áp ảnh khi `captured_at` **mới hơn hẳn**). Hoà giữa hai ảnh: thứ tự
     tất định (`decided_at`, rồi `photo_id` theo độ dài rồi ordinal). Giữ tiebreak sweep hiện có.
   Viết thành một hàm thuần nhỏ dễ test; không chép lại luật ON/dim ở chỗ khác nếu đã có một định nghĩa dùng chung được — nếu
   publisher và lookup cần cùng luật, rút ra MỘT chỗ (luật một định nghĩa, `CLAUDE.md`).
3. Cập nhật doc comment của `ISurveyFindingLookup` / `SurveyFinding` / `SurveyFindingLookup` cho đúng nguồn mới.
4. `docs/contract-drift.md`: thêm dòng **WSC-6** vào bảng WO-SURVEY-CHAIN (ngày 17/11/2026, Mỹ chốt, không chạm bề mặt API,
   consumer WP5 đã báo lỗi) mô tả luật ở mục 2.
5. `tracking.html`: thêm mục hoàn thành (ngày, task, file, commit message dự kiến `fix(work-orders): carry confirmed pole-photo faults into survey follow-up`).

Tiêu chí xong:

- [ ] Test tích hợp mới (cùng class/collection với test WO-SURVEY-CHAIN hiện có) bao: (a) phiếu khảo sát **không sweep**, một ảnh `off`
      confirmed → follow-up 201 mang `lamp_out` của ảnh; (b) ảnh chưa quyết / bị reject → vẫn 409 `NOTHING_TO_FOLLOW_UP`;
      (c) sweep `out` rồi ảnh `on` mới hơn → không mang; (d) sweep `dim` rồi ảnh `on` mới hơn → vẫn mang `lamp_dim`;
      (e) ảnh `off` cũ hơn sweep `normal` → không mang; (f) ảnh `off` có nhãn kỹ sư (`final_cv_state`) với AI `on` → mang.
- [ ] Unit test cho hàm gộp thuần (thứ tự, hoà giờ).
- [ ] `dotnet build` sạch cảnh báo mới.

## KHÔNG ĐƯỢC làm

- Không migration, không endpoint mới, không mã lỗi mới, không đổi hình dạng request/response.
- Không sửa `docs/api-contract-v1.1.md`, `docs/openapi/*.json`.
- Không đổi `WorkOrderService.FollowUp` trừ khi bắt buộc — sửa ở lookup (port giữ nguyên chữ ký).
- Không thêm project reference mới (Survey đã tham chiếu WorkOrders; WorkOrders KHÔNG được tham chiếu Survey).
- Không đụng luồng `/decision` (cổng `POLE_PHOTO_INCONCLUSIVE`, `POLE_VIDEO_NOT_VIEWED` giữ nguyên — BE-2 của WP5 làm sau).
- Không ghim ID bằng literal trong test; không `ORDER BY` ID trần.
- **Không commit, không push.** Sandbox không có Docker: không chạy được test tích hợp — ghi rõ "chưa chạy", Claude sẽ chạy.

## Kết quả

Ghi `.ai/results/WSC-PHOTO.md`: file đã sửa, tóm tắt luật, danh sách test mới, lệnh build đã chạy và output thật, D-item nếu có.
