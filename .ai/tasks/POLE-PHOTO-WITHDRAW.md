---
ticket: POLE-PHOTO-WITHDRAW
title: Kỹ sư hủy (xóa mềm) ảnh/clip cột chưa được Quản lý duyệt — DELETE /work-orders/{id}/pole-photos/{photo_id}
status: ready
phase: 2               # Mỹ chốt 17/11/2026: xóa MỀM — implement thẳng
owner: codex
branch: feat/POLE-PHOTO-WITHDRAW
contract_refs:
  - "docs/contract-drift.md — POLE-PHOTO-AI (PP-1…PP-10), PHOTO-REVIEW (PP-11…PP-15), POLE-VIDEO (PV-*)"
  - "CLAUDE.md — D-R16 lưu trữ, D-R13 audit"
depends_on: []
---

## Bối cảnh

WP6 (Thịnh, mobile) cần cho kỹ sư hủy ảnh/clip cột chụp nhầm trước khi Quản lý duyệt. Đề xuất của WP6:

- `DELETE /api/v1/work-orders/{work_order_id}/pole-photos/{photo_id}` — dùng chung ảnh và clip (cùng row `work_order_pole_photo`).
  Không body. Thành công **204 No Content**. Gọi lại cùng ID đã hủy (đúng quyền) vẫn **204** để mobile retry.
- Chỉ kỹ sư **được giao** phiếu, phiếu `in_progress`, ảnh **chưa có decision**.
- Sau khi hủy: ảnh + kết quả AI không còn xuất hiện ở danh sách / luồng duyệt của Quản lý. Cột vẫn thuộc phiếu, nhận ảnh/clip mới.
- Lỗi: 404 `WORK_ORDER_NOT_FOUND` (không tồn tại hoặc không giao cho người gọi), 404 `POLE_PHOTO_NOT_FOUND` (không có / không thuộc
  phiếu), 409 `WORK_ORDER_NOT_IN_PROGRESS`, 409 `POLE_PHOTO_ALREADY_DECIDED`.

**Mỹ chốt (17/11/2026): XÓA MỀM.** Xóa thật vi phạm D-R16 (ảnh giữ tới hết bảo hành, hết bảo hành cũng không xóa) và làm audit
`Submitted` / `Analyzed` trỏ vào ID không tồn tại (D-R13). Bytes trong object store **giữ nguyên**.

## Phải đọc trước

- `CLAUDE.md` + `docs/agents/traps/migrations.md`, `media-storage.md`, `faults-workorders.md`, `auth-accounts.md`, `testing.md`
- `docs/contract-drift.md` mục POLE-PHOTO-AI, PHOTO-REVIEW, POLE-VIDEO
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs` (đặc biệt `ReviewAsync` — khuôn khóa và kiểm quyền cần theo),
  `WorkOrderPolePhotosController.cs`, `WorkOrderPoleVideosController.cs`, `WorkOrderPolePhotoVideo.cs`, `WorkOrderPolePhotoVideoUpload.cs`,
  `Entities/WorkOrderPolePhoto.cs`, `Configurations/WorkOrderPolePhotoConfiguration.cs`
- `.ai/context/commands.md` (thêm migration, xuất spec, generator, lint)

## Yêu cầu

1. **Schema** — migration `AddPolePhotoWithdrawal`:
   - `withdrawn_at timestamptz NULL`, `withdrawn_by text NULL` (FK → `app_user`, `Restrict`, index `ix_pole_photo_withdrawn_by`
     đặt bằng `HasDatabaseName`).
   - CHECK `ck_pole_photo_withdrawal`: `(withdrawn_at IS NULL AND withdrawn_by IS NULL) OR (withdrawn_at IS NOT NULL AND withdrawn_by IS NOT NULL AND decision IS NULL)`.
   - Đọc `Up()` / `Down()` trước khi coi là xong: không Drop/Alter thứ không yêu cầu, không `xmin`, `Down()` đối xứng.
2. **Endpoint** trên `WorkOrderPolePhotosController`: `[HttpDelete("{photo_id}")]`, `[Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]`,
   trả `204 NoContent`; `ProducesResponseType` 204/404/409 để spec sinh đúng.
3. **Service `WithdrawAsync`** — thứ tự kiểm (đúng như WP6 liệt kê):
   1. `Order(id, own: true)` → 404 `WORK_ORDER_NOT_FOUND` (không tồn tại / không giao cho người gọi / ngoài scope xã).
   2. Ảnh thuộc phiếu (đọc **kể cả** đã hủy) → 404 `POLE_PHOTO_NOT_FOUND`.
   3. Đã hủy → **204 ngay**, không ghi gì, không audit — **bất kể** trạng thái phiếu lúc đó (retry sau khi phiếu đã đổi trạng thái vẫn 204).
   4. Khóa theo thứ tự toàn cục **work_order → pole → photo** (như `ReviewAsync`), đọc lại sau khóa; nếu sau khóa ảnh đã bị hủy
      (request song song) → 204.
   5. Phiếu không `in_progress` → 409 `WORK_ORDER_NOT_IN_PROGRESS`; `Decision != null` → 409 `POLE_PHOTO_ALREADY_DECIDED`.
   6. Ghi `WithdrawnAt = UtcMicrosecondClock.UtcNow()`, `WithdrawnBy = actor`; **đúng một** `AuditEvent` (`AuditAction.Deleted`,
      entity `WorkOrder`, commune theo ảnh, before/after gồm `photo_id`, `pole_id`, `cv_state`, `final_cv_state`, có/không clip),
      một `SaveChanges`, commit.
4. **Ảnh đã hủy biến mất khỏi mọi đường đọc/ghi khác** → coi như không tồn tại (404 `POLE_PHOTO_NOT_FOUND`):
   list (`GET …/pole-photos`, kể cả `total`), detail, thumbnail, original, video stream (kể cả không ghi `video_viewed_*`),
   `PATCH …/review`, `POST …/decision`, và mọi chỗ khác đọc `Set<WorkOrderPolePhoto>()` (grep toàn `src/`). Làm bằng **một** chỗ
   lọc dùng chung trong service (không rải `WithdrawnAt == null` khắp nơi); KHÔNG dùng global query filter (đường DELETE cần thấy
   row đã hủy để trả 204).
5. **Upload replay** (`client_op_id` đã dùng cho một ảnh/clip nay đã hủy, ở cả upload ảnh lẫn upload clip): trả **409
   `POLE_PHOTO_WITHDRAWN`** (mã mới, `details.photo_id`) thay vì snapshot — mobile phải dùng `client_op_id` mới cho lần chụp mới.
   Ghi rõ trong drift.
6. Object store, `IObjectInventory` / `StoredObjectColumn` (BE-35): **không đổi** — bytes vẫn giữ, vẫn được đếm.
7. Kiểm `/sync/push` / `/sync/bundle`: nếu bundle có trả ảnh cột thì phải lọc ảnh đã hủy; **không** thêm thao tác hủy qua sync
   (nếu thấy cần → ghi D-item).
8. Spec: xuất `luxmap-v1.json` → chạy generator → lint theo `.ai/context/commands.md`.
9. `docs/contract-drift.md`: mục mới **POLE-PHOTO-WITHDRAW (17/11/2026)**, mã **PP-16…** — Mỹ chốt, **SELF-SIGNED, nền tạm**, chạm bề
   mặt API (endpoint mới + mã `POLE_PHOTO_WITHDRAWN`), consumer WP6 (đề xuất gốc của Thịnh) và WP5 (ảnh đã hủy biến khỏi hàng duyệt).
   Nêu: lý do xóa mềm (D-R16, D-R13), thứ tự lỗi, 204 idempotent, replay 409, bytes giữ nguyên.
10. `tracking.html`: thêm mục hoàn thành (commit dự kiến `feat(work-orders): let the assigned engineer withdraw an undecided pole photo`).

Tiêu chí xong (test tích hợp, cùng collection với test pole-photo hiện có):

- [ ] DELETE ảnh chưa quyết → 204, body rỗng; list không còn (cả `total`), detail/thumbnail/original/review/decision → 404 `POLE_PHOTO_NOT_FOUND`.
- [ ] DELETE lần hai → 204, không thêm audit; DELETE lần hai sau khi phiếu đã `done` → vẫn 204.
- [ ] Clip (row có video) hủy được; video stream → 404, không ghi `video_viewed_*`.
- [ ] Người không được giao (kể cả Quản lý) → 404 `WORK_ORDER_NOT_FOUND` / 403 theo policy (ghi rõ cái nào thực tế xảy ra);
      ảnh của phiếu khác → 404 `POLE_PHOTO_NOT_FOUND`.
- [ ] Phiếu `done` → 409 `WORK_ORDER_NOT_IN_PROGRESS`; ảnh đã confirm/reject → 409 `POLE_PHOTO_ALREADY_DECIDED`.
- [ ] Sau hủy, upload ảnh mới cùng cột → 201; replay `client_op_id` của ảnh đã hủy → 409 `POLE_PHOTO_WITHDRAWN`.
- [ ] Đúng một `AuditEvent` `Deleted` mỗi lần hủy thật.
- [ ] Test migration / DB: CHECK `ck_pole_photo_withdrawal` từ chối row vừa có `withdrawn_at` vừa có `decision`, và `withdrawn_at` thiếu `withdrawn_by`.
- [ ] `CapabilityPolicyCoverageTests` vẫn xanh (endpoint có capability).

## KHÔNG ĐƯỢC làm

- Không xóa row, không xóa object trong store (xóa mềm là quyết định đã chốt).
- Không thêm capability / vai trò mới; không `[AllowAnonymous]`.
- Không dùng `ExecuteUpdate` / `ExecuteDelete`; không `FromSql("SELECT * … FOR UPDATE")` trên entity.
- Không sửa `docs/api-contract-v1.1.md` (spec `docs/openapi/*.json` chỉ đổi qua generator).
- Không đụng `SurveyFindingLookup` (ticket WSC-PHOTO song song; ảnh đã quyết không hủy được nên lookup không cần lọc).
- Không đổi luồng `/decision` ngoài việc 404 với ảnh đã hủy (cổng inconclusive / video-not-viewed giữ nguyên).
- Không ghim ID bằng literal trong test.
- **Không commit, không push.** Sandbox không có Docker: không chạy được test tích hợp / `dotnet ef database update` — ghi rõ, Claude sẽ chạy.

## Kết quả

Ghi `.ai/results/POLE-PHOTO-WITHDRAW.md`: file đã sửa, nội dung `Up()`/`Down()` của migration (dán thật), test mới, lệnh build /
generator / lint đã chạy kèm output thật, D-item nếu có.
