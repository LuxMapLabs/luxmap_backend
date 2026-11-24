---
ticket: POLE-GPS-PHOTO
title: Lịch "Cập nhật vị trí cột" — bắt buộc ảnh cho mỗi cột báo toạ độ, ảnh đã duyệt được chép sang ảnh tài sản của cột
status: in_progress
phase: 2
owner: claude (P1, brief, kiểm), codex (P2 code)
branch: feat/POLE-GPS-PHOTO
contract_refs:
  - "drift POLE-GPS-PHOTO PGP-1…PGP-8 (SELF-SIGNED 24/11/2026) — đặc tả của ticket này"
  - "drift WO-POLE-GPS PG-10…PG-12, EV-3 / EV-4, APH-1, POLE-DELETE-PHOTOS"
  - "docs/POLE-GPS-PHOTOS-FE.md — tài liệu FE, cập nhật cùng PR"
depends_on: []
---

## Bối cảnh

Cột import từ sổ sách chưa có toạ độ (POLE-GEOM-NULL). Kỹ sư đi gắn GPS bằng lịch `pole_gps_update` và chụp ảnh cột cùng lúc. Mỹ chốt
24/11/2026 PGP-1…PGP-3, rồi D-1…D-8 của Phase 1 (`.ai/results/POLE-GPS-PHOTO-p1.md`) — tất cả theo khuyến nghị. Drift đã viết lại
PGP-3 / PGP-4 và thêm PGP-6…PGP-8; **đọc drift, không đọc bản cũ trong lịch sử git**.

## Phải đọc trước

- `docs/contract-drift.md` mục **POLE-GPS-PHOTO** (PGP-1…PGP-8), **WO-POLE-GPS**, **EV-3 / EV-4**, **APH-1**, **POLE-DELETE-PHOTOS**
- `.ai/results/POLE-GPS-PHOTO-p1.md` — chỗ sửa, lý do, khuôn có sẵn
- `docs/agents/traps/faults-workorders.md`, `assets.md`, `media-storage.md`, `migrations.md`, `testing.md`
- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs` (`Act`, `Report`, `MovePolesAsync`), `WorkOrderEvidence.cs`, `Jobs/WorkOrderOverdueJob.cs`
- `src/LuxMap.Modules.Assets/Photos/AssetPhotoService.cs`, `Configurations/PoleAssetPhotoConfiguration.cs`

## Yêu cầu

1. **PGP-2 — `complete` đòi ảnh.** Trong `WorkOrderService.Act`, nhánh `complete` của `pole_gps_update`, **sau** `Report(...)`: mỗi cột có
   `ReportedLat != null` phải có ≥ 1 `repair_evidence` của lệnh, `kind = observation`, `pole_id` = cột. Thiếu → `409 POLE_EVIDENCE_REQUIRED`
   (`ErrorCodes`), `details {work_order_id, pole_ids}` theo thứ tự `Position` của target. Một truy vấn cho mọi cột, không N truy vấn.
   Áp cả `/sync/push` (đi cùng `Act`). OpenAPI khai 409 ở `complete`.
2. **PGP-3 / PGP-6 / PGP-7 — job chép ảnh.** Job mới ở `LuxMap.Modules.WorkOrders/Jobs/` theo khuôn `WorkOrderOverdueJob` (đăng ký, lease,
   context unscoped), chu kỳ 1 phút. Chọn ảnh: lệnh `pole_gps_update` đã `verified`, ảnh `observation` có `pole_id` thuộc target có
   `ReportedLat != null`, chưa có dòng sổ. Mỗi cột: ảnh **mới nhất theo `captured_at`** trước; số chỗ còn = 10 − số ảnh tài sản đang hiện.
   Đọc object **ngoài mọi transaction / khoá**, kiểm qua `IOriginalJpegReader` (trần 16 MiB / 40 MP), ghi object mới `PAP-…`, rồi trong
   transaction: khoá cột (`SELECT 1 … FOR UPDATE`, như upload ảnh tài sản), đếm lại chỗ, chèn dòng ảnh tài sản + dòng sổ + đúng một audit,
   một `SaveChanges` mỗi ảnh. Lõi "tạo ảnh tài sản từ JPEG đã kiểm" đặt ở `AssetPhotoService` (Assets sở hữu bảng); job gọi nó.
   Không chép được → dòng sổ `skipped_limit` / `skipped_pixels` / `missing_object` (không audit, không ảnh), log cảnh báo.
   Một ảnh lỗi không chặn ảnh khác; lỗi hạ tầng tạm (kho không trả lời) thì **không** ghi sổ, để chu kỳ sau thử lại.
3. **PGP-4 — bảng sổ `work_order_photo_copy`.** Migration mới: bảng như PGP-4, CHECK `outcome` và `(outcome = 'copied') = (photo_id IS NOT NULL)`,
   FK `Restrict` (`evidence_id → repair_evidence`, `work_order_id → work_order`, `photo_id → pole_asset_photo`, `commune_id` qua
   `HasCommuneReference()`), `ICommuneScoped`. Thêm bảng vào `PLAN` của `scripts/copy_dev_to_supabase.py`. **Không** sửa `pole_asset_photo`.
4. **Tài liệu:** `docs/POLE-GPS-PHOTOS-FE.md` bỏ 🆕 ở phần đã làm; trap mới ở `docs/agents/traps/faults-workorders.md` (job chép ảnh:
   I/O ngoài khoá, sổ một-lần, khoá cột như upload). OpenAPI xuất lại → generator → lint (`.ai/context/commands.md`).

## Tiêu chí xong

- [ ] `dotnet build` không cảnh báo mới; `dotnet test` mã thoát 0
- [ ] Test complete: thiếu ảnh một cột → 409 đúng `pole_ids` (thứ tự target); ảnh không gắn cột không tính; ảnh vòng trước `return` tính;
  cột không báo toạ độ không cần ảnh; `/sync/push` ra `conflicts[]` mã `POLE_EVIDENCE_REQUIRED`; lệnh loại khác không đổi
- [ ] Test job: lệnh `verified` → đúng số ảnh tài sản, object mới, `uploaded_by` = người chụp, audit `system`; chạy lại không chép trùng;
  lệnh `done` / `cancelled` không chép; cột còn 1 chỗ → 1 `copied` (ảnh mới nhất) + phần còn lại `skipped_limit`; object mất → `missing_object`;
  ảnh > 40 MP → `skipped_pixels`; xoá cột sau khi chép vẫn từ chối 409 `ASSET_IN_USE` và rollback sạch
- [ ] Phá thử: bỏ kiểm ảnh ở complete → test đỏ; bỏ điều kiện "chưa có dòng sổ" → test chép trùng đỏ; bỏ đếm chỗ → test giới hạn đỏ
- [ ] Migration: đọc `Up()` / `Down()` trước khi apply; `Down()` đối xứng
- [ ] OpenAPI + generator + lint

## KHÔNG ĐƯỢC làm

- Không đọc GPS từ EXIF; không sửa ảnh gốc (PGP-1).
- Không trỏ chung key giữa hai bảng; không sửa CHECK của `pole_asset_photo`; không thêm cột vào `pole_asset_photo`.
- Không giữ khoá hay transaction qua tải / giải mã / ghi object.
- Không đổi hình dạng `/sync/push`, `WorkOrderDetail`, item gallery; không đổi `verify`.
- Không đổi tập nhãn EV-3, `/pole-photos`, luật PL-3, quyền `UploadAssetPhotos`, `PUT /assets/poles/{id}/location`.
- Không sửa `docs/api-contract-v1.1.md`; không áp migration lên `luxmap_dev` / VPS; không `git push`; không `git add -A` / `git add .`.
