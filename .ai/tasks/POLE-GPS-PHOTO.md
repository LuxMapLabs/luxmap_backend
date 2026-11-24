---
ticket: POLE-GPS-PHOTO
title: Lịch "Cập nhật vị trí cột" — bắt buộc ảnh cho mỗi cột báo toạ độ, verify chép ảnh sang ảnh tài sản của cột
status: todo
phase: 1
owner: chưa giao
branch: feat/POLE-GPS-PHOTO
contract_refs:
  - "drift POLE-GPS-PHOTO PGP-1…PGP-5 (SELF-SIGNED 24/11/2026) — đặc tả của ticket này"
  - "drift WO-POLE-GPS PG-10…PG-12, EV-3 / EV-4, APH-1, POLE-DELETE-PHOTOS"
  - "docs/POLE-GPS-PHOTOS-FE.md — tài liệu FE, cập nhật cùng PR"
depends_on: []
---

## Bối cảnh

Cột import từ sổ sách chưa có toạ độ (POLE-GEOM-NULL). Kỹ sư đi gắn GPS bằng lịch `pole_gps_update` và chụp ảnh cột cùng lúc. Mỹ chốt
24/11/2026: GPS là trường riêng, không đọc EXIF (PGP-1); mỗi cột báo toạ độ phải có ảnh (PGP-2); `verify` chép ảnh sang thư viện ảnh
tài sản của cột (PGP-3, PGP-4). Hôm nay ảnh là tuỳ chọn và ở lại phiếu.

## Phải đọc trước

- `docs/contract-drift.md` mục **POLE-GPS-PHOTO**, **WO-POLE-GPS**, **EV-3 / EV-4**, **APH-1**, **POLE-DELETE-PHOTOS**
- `docs/agents/traps/faults-workorders.md`, `assets.md`, `media-storage.md`, `migrations.md`, `testing.md`
- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs` (complete / verify, PG-12), `WorkOrderEvidence.cs`
- `src/LuxMap.Modules.Assets/Photos/AssetPhotoService.cs`, `Entities/PoleAssetPhoto.cs`, `AssetPhotoReader.cs` (ảnh bìa)

## Yêu cầu

1. **PGP-2** — `complete` của `pole_gps_update` (cả `/sync/push`) từ chối `409 POLE_EVIDENCE_REQUIRED` khi một cột trong `pole_locations`
   chưa có ảnh `observation` gắn cột đó trong lệnh. Mã lỗi vào `ErrorCodes`, OpenAPI khai 409.
2. **PGP-3 / PGP-4** — `verify` chép ảnh trong cùng transaction với ghi `pole.geom`; không chép byte; cột `source_evidence_id` + unique
   một phần + FK **DB-only** (Assets không reference ngược WorkOrders — ranh giới module). Lõi tạo ảnh tài sản nằm ở Assets, WorkOrders gọi.

## Phase 1 — khảo sát, dừng chờ Mỹ

Trả lời rồi dừng (không viết code):

- `PoleAssetPhoto` bắt buộc `Width` / `Height`, `ClientOpId` (Guid, không null), `RequestHash`; `repair_evidence` không có kích thước.
  Lấy kích thước bằng cách nào (Identify từ object gốc ngoài transaction? thêm cột vào `repair_evidence` lúc upload?), và `ClientOpId` /
  `RequestHash` của dòng chép là gì (khuôn idempotency của gallery đang theo `(uploaded_by, client_op_id)`).
- Đọc object gốc lúc `verify` có giữ khoá qua I/O không (cấm — CLAUDE.md, Bẫy chung, *Khoá hàng*); nếu cần đọc thì đọc trước transaction.
- Audit: dòng chép mang audit gì (IAudited — đúng một `AuditEvent` mỗi `SaveChanges`?) — đối chiếu cách `verify` đang ghi audit `relocated`.
- Có trả `source_evidence_id` ra item gallery không (PGP-4 để mở).
- `POLE-DELETE-PHOTOS`: xoá cột xoá dòng ảnh tài sản — xác nhận FK mới không chặn đường đó và không xoá ảnh bằng chứng.

## Tiêu chí xong (Phase 2)

- [ ] `dotnet build` không cảnh báo mới; `dotnet test` mã thoát 0
- [ ] Test: complete thiếu ảnh một cột → 409 đúng `pole_ids`; ảnh không gắn cột không tính; ảnh vòng trước `return` tính; cột không báo
  toạ độ không cần ảnh; qua `/sync/push` cùng luật; verify tạo đúng số dòng ảnh tài sản trỏ cùng key; verify 409 `POLE_OFF_SEGMENT`
  không chép gì; verify gọi lại / chép hai lần không trùng; xoá cột sau verify vẫn chạy như POLE-DELETE-PHOTOS
- [ ] Phá thử: bỏ kiểm ảnh ở complete → test đỏ; bỏ transaction chung → test "không chép khi 409" đỏ
- [ ] Migration đọc `Up()` / `Down()` trước khi apply; `Down()` từ chối khi còn `source_evidence_id` khác NULL
- [ ] OpenAPI xuất lại + generator + lint; `docs/POLE-GPS-PHOTOS-FE.md` đổi "sắp có" thành hiện trạng

## KHÔNG ĐƯỢC làm

- Không đọc GPS từ EXIF; không sửa ảnh gốc (PGP-1).
- Không chép byte sang object mới; không thêm xoá object vào `IObjectStore`.
- Không đổi tập nhãn EV-3, `/pole-photos`, luật PL-3, quyền `UploadAssetPhotos`, `PUT /assets/poles/{id}/location`.
- Không sửa `docs/api-contract-v1.1.md`; không áp migration lên `luxmap_dev` / VPS; không `git push`; không `git add -A` / `git add .`.
