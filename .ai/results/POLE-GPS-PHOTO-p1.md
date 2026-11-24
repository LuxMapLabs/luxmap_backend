# POLE-GPS-PHOTO — Phase 1: khảo sát

24/11/2026 · Claude · nhánh `docs/POLE-GPS-PHOTOS-fe` (tách từ `origin/dev` d962b59) · **không có code**. Dừng chờ Mỹ chốt D-1…D-8.

## Tóm tắt

- **PGP-2 (bắt buộc ảnh ở `complete`)** làm được như đã chốt: một truy vấn trong `WorkOrderService.Act`, không migration, không
  tranh chấp nguy hiểm.
- **PGP-3 / PGP-4 (chép ảnh lúc `verify`) không làm được như đã ghi.** "Trỏ chung key, không chép byte" vướng một CHECK có sẵn,
  vướng giới hạn pixel, và làm báo cáo dung lượng đếm đôi. Đề xuất: **chép byte** (D-1), làm trong **job nền** sau khi `verify` commit
  (D-2), ghi vết bằng **bảng sổ chép của WorkOrders** thay cho cột `source_evidence_id` trên bảng ảnh tài sản (D-5).
- Phải xử lý thêm hai trường hợp: gallery của cột chỉ hiện tối đa **10** ảnh (D-3), và ảnh bằng chứng **không có giới hạn pixel**, trong
  khi ảnh tài sản giới hạn 40 MP (D-4).

## 1. PGP-2 — `complete` đòi ảnh theo cột

**Chỗ sửa:** `WorkOrderService.Act`, nhánh `complete` của `pole_gps_update`, **sau** `Report(targets, poleLocations)` (400 hình dạng đi
trước, đúng PGP-2). Tập cột cần ảnh = `targets` có `ReportedLat != null` sau `Report`. Truy vấn:
`repair_evidence WHERE work_order_id = id AND kind = 'observation' AND pole_id IN (…)`. Index đã có: `ix_repair_evidence_pole_id`.

**Tranh chấp:** upload ảnh khoá `work_order FOR UPDATE` rồi chèn; `complete` không khoá mà dựa `xmin` của phiếu. Ảnh bằng chứng
**không có đường xoá** nên chiều nguy hiểm ("đã kiểm có ảnh, rồi ảnh biến mất") không xảy ra. Chiều còn lại (ảnh đang lên lúc
`complete` đọc) chỉ cho ra 409 thừa, gửi lại là qua. Không cần thêm khoá.

**`/sync/push`:** op lỗi được cô lập (`ResetAsync`: rollback + `ChangeTracker.Clear`), nên `Report` đã sửa target trong bộ nhớ không rò sang
op sau. Nhưng 409 (khác `IDEMPOTENCY_CONFLICT`) đi vào **`conflicts[]`**, và `conflicts[]` mang `reason` (mã lỗi), `message`, `server_state`,
**không mang `details`** (`SyncPushService` dòng 126). Mobile sẽ **không** nhận được `details.pole_ids` qua sync → D-7.

**Mã lỗi:** `POLE_EVIDENCE_REQUIRED` mới trong `ErrorCodes`. Khuôn: `AFTER_EVIDENCE_REQUIRED`.

## 2. PGP-3 — vì sao "trỏ chung key" không được

| Vướng | Bằng chứng |
|---|---|
| CHECK key | `ck_pole_asset_photo_keys`: `object_key = 'original/' \|\| photo_id \|\| '.jpg' AND thumbnail_key = 'thumb/' \|\| photo_id \|\| '.jpg'` (`PoleAssetPhotoConfiguration.cs:17`). Key của ảnh bằng chứng là `…/EVD-….jpg` → vi phạm. Nới CHECK là bỏ một bất biến của APH-1 |
| Kích thước | `pole_asset_photo.width/height` bắt buộc, CHECK `width * height <= 40 000 000`. `repair_evidence` **không lưu kích thước**; upload bằng chứng đi `ImagePipeline.PrepareAsync`, **không** có trần pixel (ảnh tài sản đi `OriginalJpegReader`, có `IdentifyAsync` + trần 40 MP). Muốn biết kích thước vẫn phải đọc object gốc |
| Dung lượng | `StorageUsage.TotalsAsync` cộng `count` / `sum(byte_count)` từng cột key bằng `UNION ALL` — hai dòng cùng key bị **đếm hai lần** |
| Request hash | `request_hash` của ảnh tài sản = `sha256(pole_id + "\n" + sha256(original))` — cần byte gốc |

Kết luận: đằng nào cũng phải đọc byte gốc. Đọc rồi thì **chép hẳn** qua đúng đường upload ảnh tài sản (`OriginalJpegReader.ReadAsync` →
`StoreImageAsync(Evidence, PAP-…)`). Như vậy mọi bất biến APH-1 giữ nguyên, dung lượng đếm đúng, và xoá cột (POLE-DELETE-PHOTOS) không đổi gì.

Giá: dung lượng của ảnh được chép **tăng gấp đôi** (bằng chứng vẫn giữ). Một ảnh điện thoại khoảng 3–5 MB, 1–3 ảnh mỗi cột. Đĩa VPS đang
khoảng 79% — nên biết trước.

## 3. Chép trong `verify` hay trong job nền

`verify` đang chạy trong một transaction, khoá phiếu → các cột (`MovePolesAsync`). Luật repo: **không giữ khoá qua I/O** (CLAUDE.md,
*Khoá hàng*). Chép N ảnh là N lần tải + giải mã + ghi hai object.

| | Đồng bộ trong `verify` | Job nền sau commit (khuyến nghị) |
|---|---|---|
| I/O | Phải làm **trước** transaction, tức là trước khi biết `verify` có qua không. `POLE_OFF_SEGMENT` → để lại object mồ côi | Sau commit, không khoá nào |
| Thời gian `verify` | Lệnh 30 cột × 2 ảnh ≈ vài chục giây; tối đa `pole_ids` = 200 | Như hôm nay |
| Lỗi kho ảnh | Làm `verify` hỏng theo, dù GIS không liên quan | Job thử lại, `verify` không bị ảnh hưởng |
| Gallery có ảnh | Ngay | Sau ≤ một chu kỳ job (đề xuất 1 phút) |

Khuôn job có sẵn: bảng `scheduled_job` + lease (BE-26). Job đọc bằng context unscoped, như `WorkOrderOverdueJob`.

## 4. Định danh dòng chép, audit

- `uploaded_by` = người chụp (`repair_evidence.uploaded_by`). `created_at` = lúc chép. `data_source` lấy từ cột.
- `client_op_id` bắt buộc, khác `0`, unique theo `(uploaded_by, client_op_id)`. Ảnh bằng chứng có thể **không** có `client_op_id`, và
  dùng lại UUID của mobile thì có thể đụng một upload gallery thật. Đề xuất **UUIDv5(namespace cố định, `evidence_id`)**: tất định, không đụng UUIDv4 ngẫu nhiên.
- `request_hash`: công thức như upload (có byte gốc sau D-1).
- Audit: `PoleAssetPhoto` là `IAudited`; `AuditWriteGuard` đòi **đúng một** audit mới mỗi `SaveChanges` có thay đổi `IAudited` → mỗi ảnh
  một `SaveChanges`, một event `pole_asset_photo / created`, actor `System` (job), `after` = snapshot như upload + `evidence_id`,
  `work_order_id`. Khuôn: `AssetPhotoService.RemoveAllForPoleAsync`.
- Lõi tạo ảnh tài sản **từ byte đã kiểm** nên nằm ở `AssetPhotoService` (Assets sở hữu bảng). Job ở WorkOrders gọi nó — đúng chiều
  phụ thuộc đã có (WorkOrders đã reference Assets).

## 5. Ghi vết: sổ chép thay cho `source_evidence_id`

PGP-4 đặt `source_evidence_id` lên `pole_asset_photo`, tức là thêm FK từ bảng của **Assets** sang bảng của **WorkOrders** — ngược chiều
phụ thuộc module. Job cũng cần đánh dấu ảnh **đã xử lý nhưng không chép** (vượt 10 ảnh, quá 40 MP, object mất), nếu không thì lần
quét nào cũng gặp lại chúng.

Đề xuất bảng **`work_order_photo_copy`** thuộc WorkOrders, một dòng mỗi ảnh bằng chứng đã xử lý: `evidence_id` (PK, FK), `work_order_id`,
`pole_id`, `commune_id`, `outcome` (`copied | skipped_limit | skipped_pixels | missing_object`), `photo_id` (nullable, FK tới
`pole_asset_photo`, CHECK `outcome = 'copied'` ⇔ `photo_id IS NOT NULL`), `decided_at`. Mọi FK `Restrict`, cùng xã. Không `IAudited`
(dòng chép đã có audit riêng). Job chọn: ảnh `observation` của phiếu `pole_gps_update` **đã `verified`**, `pole_id` thuộc cột có toạ độ
báo, **chưa có dòng sổ**. Bảng mới → `PLAN` của `scripts/copy_dev_to_supabase.py`.

Xoá cột: ảnh tài sản bị xoá trước, FK `photo_id` chặn → 409 `ASSET_IN_USE` với constraint của sổ. Thực tế không đổi gì: cột có ảnh bằng
chứng vốn đã bị `repair_evidence → pole` (Restrict) chặn xoá. Chỉ tên constraint trong `details` khác.

## 6. Ảnh được giữ lại khi không chép được

- **Giới hạn 10 ảnh hiện:** `AssetPhotoService.MaxPhotos = 10`, quá → 409 `ASSET_PHOTO_LIMIT_REACHED`. Kỹ sư chụp 3 ảnh × vài vòng
  `return` là đủ chạm trần với cột đã có ảnh.
- **Trên 40 MP:** máy 50 MP (8160 × 6120 ≈ 49,9 MP) qua được bằng chứng, không qua được ảnh tài sản.
- **Object mất / hỏng:** `STORAGE_OBJECT_MISSING`, hoặc `UNSUPPORTED_IMAGE_FORMAT` từ reader.

## D-item — chờ Mỹ chốt

| Mã | Câu hỏi | Khuyến nghị |
|---|---|---|
| **D-1** | Sửa PGP-3 "không chép byte" → **chép byte** qua đường upload ảnh tài sản? | **Có** — mục 2. Chấp nhận dung lượng gấp đôi cho các ảnh này |
| **D-2** | Chép **đồng bộ trong `verify`** hay **job nền** sau commit? | **Job nền**, chu kỳ 1 phút — mục 3. `verify` không đổi thời gian, không đổi lỗi |
| **D-3** | Cột đã đầy hoặc sắp đầy 10 ảnh? | Chép ảnh **mới nhất theo `captured_at`** trước, tới khi đầy; phần còn lại ghi `skipped_limit`. Không bao giờ làm `verify` lỗi |
| **D-4** | Ảnh > 40 MP, object mất, giải mã lỗi? | Bỏ qua, ghi `outcome` tương ứng, log cảnh báo. Không tự thu nhỏ ảnh |
| **D-5** | Sửa PGP-4: bảng sổ `work_order_photo_copy` (WorkOrders) **thay** cột `source_evidence_id` trên `pole_asset_photo`? | **Có** — mục 5. Item gallery **không** đổi (không lộ nguồn) |
| **D-6** | Định danh dòng chép: `uploaded_by` = người chụp, `client_op_id` = UUIDv5(`evidence_id`), audit actor `System`? | **Có** — mục 4 |
| **D-7** | 409 `POLE_EVIDENCE_REQUIRED` qua `/sync/push` không có `details`. Thêm `details` vào `conflicts[]` (đổi hình dạng sync, mọi op) hay để mobile tự tính cột thiếu? | **Mobile tự tính** (app biết ảnh nào đã chụp cho cột nào); không đổi sync trong ticket này |
| **D-8** | FE có cần biết kết quả chép (số ảnh đã chép / bỏ qua) không? Nếu cần: thêm `photo_copy {copied, skipped}` theo cột vào `WorkOrderDetail` | **Chưa cần** — FE ghi "Ảnh sẽ được thêm vào hồ sơ cột trong ít phút". Làm sau nếu WP5 hỏi |

## Đã đọc

`WorkOrderService.cs` (`Act`, `Report`, `MovePolesAsync`), `WorkOrderEvidence.cs`, `WorkOrderPolePhotoService.cs`,
`AssetPhotoService.cs`, `PoleAssetPhotoConfiguration.cs`, `PoleRelocation.cs`, `OriginalJpegReader.cs`, `ImagePipeline.cs`,
`S3ObjectStore.cs`, `ObjectInventory.cs`, `StorageUsage.cs`, `AuditWriteGuard.cs`, `SyncPushService.cs`, `WorkOrderOverdueJob.cs`,
`docs/agents/traps/media-storage.md`, `faults-workorders.md` (BE-23a), drift WO-POLE-GPS / EV-3 / EV-4 / APH-1 / POLE-DELETE-PHOTOS.
