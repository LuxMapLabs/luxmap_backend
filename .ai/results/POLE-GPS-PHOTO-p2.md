# POLE-GPS-PHOTO — Phase 2: hiện thực

24/11/2026 · Claude (Codex không chạy được `gpt-6.1-sol`: "not supported when using Codex with a ChatGPT account"; Mỹ chọn để
Claude tự làm) · nhánh `feat/POLE-GPS-PHOTO`. Đặc tả: drift POLE-GPS-PHOTO PGP-1…PGP-8.

## Đã làm

| Phần | File |
|---|---|
| PGP-2 — `complete` đòi ảnh theo cột, `409 POLE_EVIDENCE_REQUIRED` `{work_order_id, pole_ids}` | `WorkOrderService.cs` (`RequirePolePhotosAsync`), `ErrorCodes.cs`, `WorkOrdersController.cs` (khai 200 / 409) |
| PGP-3 / PGP-6 / PGP-7 — job chép ảnh, 1 phút | `Jobs/PolePhotoCopyJob.cs`, đăng ký ở `WorkOrdersModule.cs` |
| Lõi tạo ảnh tài sản từ JPEG đã kiểm (Assets sở hữu bảng) | `Assets/Photos/PoleAssetPhotoCopy.cs`; `AssetPhotoService.RequestHash` / `Snapshot` thành `internal` để dùng chung, không chép |
| PGP-4 — sổ `work_order_photo_copy` | `Entities/WorkOrderPhotoCopy.cs`, `Configurations/WorkOrderPhotoCopyConfiguration.cs`, migration `AddWorkOrderPhotoCopy`, `scripts/copy_dev_to_supabase.py` |
| Test | `WorkOrderPoleGpsPhotoTests.cs` (mới); 4 test GPS cũ chụp ảnh trước `complete`; teardown `WorkOrderTests` thêm hai bảng; `PhotoStore.OpenAsync` trả `STORAGE_OBJECT_MISSING` như adapter S3 |
| Tài liệu | trap `faults-workorders.md` (POLE-GPS-PHOTO), `docs/POLE-GPS-PHOTOS-FE.md`, drift (sửa tên trường `reason` của `conflicts[]`) |

## Tiêu chí → test

| Tiêu chí | Test |
|---|---|
| Thiếu ảnh một cột → 409 đúng `pole_ids` theo thứ tự target; ảnh không gắn cột không tính; cột không báo không cần ảnh; ảnh vòng trước `return` tính | `Complete_needs_a_photo_tied_to_every_pole_it_reports` |
| `/sync/push` → `conflicts[]` `reason = POLE_EVIDENCE_REQUIRED`, không ghi gì | `Sync_push_answers_a_missing_pole_photo_as_a_conflict` |
| Lệnh `done` không chép; `verify` không tự chép; job chép đúng số ảnh, object riêng cùng byte, `uploaded_by`, `client_op_id` UUIDv5, audit `system` có `evidence_id` / `work_order_id`; chạy lại không chép thêm, không audit; xoá cột sau khi chép bị từ chối và rollback | `Only_a_verified_order_has_its_photos_copied_into_the_pole_galleries_once` |
| Gallery còn 1 chỗ → ảnh mới nhất `copied`, còn lại `skipped_limit` | `A_nearly_full_gallery_takes_the_newest_photo_and_the_rest_are_recorded_as_skipped` |
| Gallery bị lấp đúng lúc job ghi object → đếm lại dưới khoá cột → `skipped_limit` | `A_gallery_filled_while_the_job_writes_the_object_is_counted_again_under_the_pole_lock` |
| Object mất → `missing_object`; > 40 MP → `skipped_pixels` | `A_photo_that_is_gone_or_above_the_pixel_cap_is_recorded_and_never_copied` |

Lệnh `cancelled` không có test riêng: điều kiện chọn ảnh là `wo_status = verified` (phá thử B bên dưới đỏ khi bỏ điều kiện này).

## Phá thử (đã chạy, khôi phục bằng `cp` + `touch`)

| Phá | Test đỏ |
|---|---|
| A — bỏ `await RequirePolePhotosAsync(...)` | `Complete_needs_a_photo…`, `Sync_push_answers…` (2/2 đỏ) |
| B — bỏ `w.WoStatus == WorkOrderStatus.Verified` trong truy vấn job | `Only_a_verified_order…` (đỏ) |
| C — bỏ đếm lại trong `PoleAssetPhotoCopy.StageAsync` | `A_gallery_filled_while_the_job_writes…` (đỏ) |

**Không phá thử được riêng:** điều kiện "chưa có dòng sổ" trong truy vấn job. Bỏ nó đi thì khoá chính của sổ vẫn chặn chép trùng (lượt
sau rollback, chỉ để lại object mồ côi), nên test vẫn xanh — hai lớp chồng nhau, mỗi lớp đủ chặn trùng một mình.

## Migration

`Up()` chỉ `CreateTable work_order_photo_copy` + 4 index; `Down()` chỉ `DropTable`. Snapshot: EF sinh lại hai CHECK nhiều dòng với `\n` thay
`\r\n` — đã trả về nguyên bản để diff chỉ còn bảng mới. **Chưa áp** lên `luxmap_dev` / VPS.

## Chưa làm / cần biết

- Chưa áp migration, chưa deploy. Job chạy được ngay khi deploy (`Jobs:Enabled`), không cần cấu hình.
- Dung lượng: mỗi ảnh được chép tốn thêm một bản (D-1).
- Lệnh đã `verified` **trước** khi deploy cũng sẽ được chép ở lần chạy đầu (truy vấn không lọc theo ngày). Muốn chỉ áp cho lệnh mới thì
  phải chốt thêm — hiện theo đúng chữ PGP-3.
