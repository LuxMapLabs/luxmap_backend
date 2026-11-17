---
ticket: SURVEY-VERIFY-PHOTOS
title: Chặn verify phiếu khảo sát khi còn ảnh/clip cột chưa có decision (BE-2 của WP5)
status: ready
phase: 2               # Mỹ + WP5 (Khang) chốt 17/11/2026 — implement thẳng
owner: codex
branch: feat/SURVEY-VERIFY-PHOTOS
contract_refs:
  - "docs/contract-drift.md — POLE-PHOTO-AI (PP-9), POLE-VIDEO (PV-c3), POLE-PHOTO-WITHDRAW (PP-16…PP-19), WO-SURVEY-CHAIN (WSC-6)"
depends_on: [POLE-PHOTO-WITHDRAW đã merge vào dev]
---

## Bối cảnh

WP5 (Khang) báo Quản lý verify được phiếu khảo sát trong khi ảnh/clip cột của phiếu chưa có decision nào, nên follow-up sau đó
không có gì để mang sang. Mỹ và Khang chốt (17/11/2026) phương án **chặn verify**:

- **Giữ nguyên** cổng `409 POLE_PHOTO_INCONCLUSIVE` (ảnh inconclusive → Quản lý bấm `return`, kỹ sư gán nhãn bằng `PATCH …/review`).
- **Giữ nguyên** cổng `409 POLE_VIDEO_NOT_VIEWED` (Quản lý phải mở clip trên Web trước khi confirm).
- **Mới:** `POST /work-orders/{id}/verify` trên phiếu **khảo sát** mà còn ảnh/clip chưa có decision → **409** kèm danh sách `photo_id`.
- Không auto-confirm, không đoán nhãn.

## Phải đọc trước

- `CLAUDE.md` + `docs/agents/traps/faults-workorders.md`, `docs/agents/traps/testing.md`
- `docs/contract-drift.md` mục POLE-PHOTO-AI, POLE-VIDEO, POLE-PHOTO-WITHDRAW, WO-SURVEY-CHAIN
- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs` → `Act` (khuôn có sẵn: `AFTER_EVIDENCE_REQUIRED` cho repair `complete`)
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs` (`Photos()` — một chỗ lọc ảnh đã hủy)

## Yêu cầu

1. Trong `WorkOrderService.Act`, khi `action == "verify"` và `wo.TaskKind == TaskKind.Survey`: nếu phiếu còn ảnh/clip cột **chưa hủy**
   (`WithdrawnAt == null`) và **chưa có decision** (`Decision == null`) → `409 POLE_PHOTOS_UNDECIDED`,
   `details.photo_ids` = mảng `photo_id` theo thứ tự `captured_at, uploaded_at, length(photo_id), photo_id` (đúng thứ tự list PP-5).
   Kiểm **sau** `RequireAction` (sai trạng thái / sai quyền vẫn trả mã cũ), **trước** mọi thay đổi.
   Ảnh đã hủy và ảnh đã reject/confirm không chặn. Phiếu không có ảnh nào → verify như cũ.
   Không chép điều kiện "ảnh còn sống" ra chỗ thứ hai: nếu cần, đưa ra một helper dùng chung với `WorkOrderPolePhotoService.Photos()`
   (luật một định nghĩa, `CLAUDE.md`).
2. Chỉ phiếu `survey`. `inspection` / `repair` / `pole_gps_update` **không** đổi (ghi rõ trong drift là cố ý).
3. Không đổi `allowed_actions` (vẫn có `verify`); FE dựa vào 409 + `photo_ids`.
4. Controller: thêm `ProducesResponseType` 409 nếu route verify chưa khai; xuất spec → generator → lint (`.ai/context/commands.md`).
5. `docs/contract-drift.md`: mục mới **SURVEY-VERIFY-PHOTOS (17/11/2026)**, mã **SVP-1…** — Mỹ + WP5 chốt, SELF-SIGNED, nền tạm,
   chạm bề mặt API (mã lỗi mới). Ghi: hai cổng decision giữ nguyên và lý do, hướng dẫn cho WP5 (return + review cho inconclusive;
   mở clip trước confirm; reject ảnh không dùng), chỉ áp phiếu khảo sát.
6. `tracking.html`: thêm mục hoàn thành (commit dự kiến `feat(work-orders): block survey verify while pole photos are undecided`).

Tiêu chí xong (test tích hợp, đặt cạnh test pole-photo / verify hiện có):

- [ ] Phiếu khảo sát `done` có 1 ảnh chưa quyết + 1 clip chưa quyết + 1 ảnh đã reject + 1 ảnh đã hủy → verify 409 `POLE_PHOTOS_UNDECIDED`,
      `photo_ids` đúng hai ID chưa quyết, đúng thứ tự; phiếu vẫn `done`, không audit `Verified`.
- [ ] Quyết hết (confirm/reject) → verify 200 `verified`.
- [ ] Phiếu khảo sát không ảnh → verify 200 như cũ.
- [ ] Phiếu inspection `done` có ảnh chưa quyết → verify vẫn 200 (không đổi hành vi).
- [ ] Thứ tự: ID có độ rộng khác nhau (`length` trước ordinal) — không ghim ID literal, tạo ảnh hai bên ngưỡng nếu cần hoặc assert theo thứ tự `captured_at`.

## KHÔNG ĐƯỢC làm

- Không migration. Không đổi `/decision`, `/review`, cổng inconclusive, cổng video.
- Không auto-confirm / auto-reject ảnh; không suy nhãn.
- Không đổi hành vi verify của task kind khác.
- Không sửa `docs/api-contract-v1.1.md` (spec chỉ qua generator).
- **Không commit, không push.** Sandbox không có Docker: ghi rõ test tích hợp chưa chạy, Claude sẽ chạy.

## Kết quả

Ghi `.ai/results/SURVEY-VERIFY-PHOTOS.md`: file đã sửa, test mới, lệnh build / generator / lint và output thật, D-item nếu có.
