---
ticket: ACCT-AVATAR
title: Ảnh đại diện (avatar) của tài khoản — upload / xem / gỡ, JPEG-only, bucket riêng
status: ready
phase: 2
owner: codex
branch: feat/ACCT-avatar
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: ACCT-5 (kiểm docs/contract-drift.md; ACCT-2…4 đã có, dùng số kế tiếp nếu ACCT-5 đã bị lấy)"
depends_on: []
---

## Bối cảnh

FE (WP5) xin avatar ở màn sửa hồ sơ: `avatar_url` trên `/auth/me` và `UserAccountItem`, upload tự phục vụ, có xoá. Mỹ chốt (09/11/2026) theo
thảo luận Claude–Codex (quyết định A), **không phải D-item**:

- **JPEG-only.** FE xin jpg/png/webp nhưng quy tắc "chỉ JPEG, quyết bằng magic bytes" là cứng (`docs/agents/traps/media-storage.md`: ImageSharp advisory bị tắt dựa trên tiền đề chỉ có đường giải mã JPEG). PNG/WebP → `UNSUPPORTED_IMAGE_FORMAT`; **FE tự chuyển sang JPEG ở client** (canvas). Ghi rõ vào drift.
- **Làm sạch trước khi lưu:** đi qua `IImageSanitizer.SanitizeJpegAsync` (như ảnh công dân) — bỏ EXIF / GPS (avatar là ảnh người), xoay theo orientation, scale về cạnh dài ≤ **512 px**, re-encode. Giới hạn: tối đa **1 MiB** (1 048 576 byte) đọc vào, `MaxPixels` 16 000 000. Hằng số đặt một chỗ (`AvatarLimits`), không đưa ra cấu hình.
- **Bucket riêng `luxmap-avatar`** (`StorageBucket.Avatar`), key do `StoreImageAsync(StorageBucket.Avatar, userId, …)` tự sinh: `original/USR-xxxx.jpg`, `thumb/USR-xxxx.jpg`. Upload lại **ghi đè cùng key** (tối đa 2 object / người, không tích mồ côi khi đổi ảnh). Lý do tách bucket là **ngữ nghĩa** (chính sách giữ / xoá khác với bằng chứng sửa chữa), không phải mồ côi.
- **Phục vụ qua API (proxy), không presigned URL** (BE-11 quy tắc 1). `avatar_url` là **đường dẫn tương đối** tới API và đòi Bearer ⇒ FE phải `fetch` + blob URL, `<img src>` trần sẽ 401. Phục vụ **thumbnail** (nhỏ); ảnh đã làm sạch (original) vẫn được lưu.
- **Xoá = chỉ gỡ liên kết** (đặt các cột avatar về NULL). `IObjectStore` chưa có `DeleteAsync` nên byte vẫn nằm trong bucket tới khi người đó upload lại (cùng key, ghi đè). Báo thẳng trong drift: "xoá" **chưa xoá byte**; xoá thật là ticket riêng có chữ ký (thêm `DeleteAsync` + audit).
- **Một capability mới `ManageOwnProfile`** = cả 4 vai trò, cho `POST/GET/DELETE /auth/me/avatar`. Xem avatar người khác: `GET /admin/users/{id}/avatar` dưới `ManageUsers` (system_admin). **Không** có endpoint cho manager xem avatar kỹ sư; nếu FE cần thì ticket riêng.
- **Migration**: thêm 5 cột nullable vào `app_user` — **Claude sinh migration sau khi bạn xong** (`dotnet ef`), bạn KHÔNG tạo file migration, KHÔNG sửa snapshot. Bạn chỉ khai entity + configuration; cột phải khớp tên dưới đây vì `StoredObjectColumn` và `SystemMonitoringTests.Every_object_key_column_…` đọc `information_schema`.

## Cột trên `app_user` (entity `AppUser`, configuration Identity)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `avatar_object_key` | text, nullable | `original/USR-xxxx.jpg` |
| `avatar_byte_count` | bigint, nullable | byte thật `StoreImageAsync` trả về |
| `avatar_thumbnail_key` | text, nullable | `thumb/USR-xxxx.jpg` |
| `avatar_thumbnail_bytes` | bigint, nullable | |
| `avatar_updated_at` | timestamptz, nullable | dùng làm `?v=` và ETag; `DateTimeKind.Utc`, `UtcMicrosecondClock.UtcNow()` |

Thêm CHECK `ck_app_user_avatar_all_or_none`: năm cột cùng NULL hoặc cùng không NULL (CLAUDE.md: ràng buộc CHECK khai trong configuration; byte `>= 0`). Đăng ký `StoredObjectColumn` cho `app_user`: **đúng hai dòng**, `new StoredObjectColumn(StorageBucket.Avatar, "app_user", "avatar_object_key", "avatar_byte_count")` và `("avatar_thumbnail_key", "avatar_thumbnail_bytes")` trong `IdentityModule` (cột không theo tên mặc định `object_key`).

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/media-storage.md` (đủ sáu quy tắc + BE-35) + `docs/agents/traps/auth-accounts.md` + `docs/agents/traps/migrations.md` + `docs/agents/traps/testing.md`
- `src/LuxMap.Shared/Storage/{ObjectStorage,ImageSanitizing,ObjectInventory}.cs`; `src/LuxMap.Infrastructure.Storage/{S3ObjectStore,JpegSanitizer}.cs`
- Mẫu bucket + sanitize + `StoredObjectColumn`: `src/LuxMap.Modules.Citizen/{PublicReportService,CitizenModule}.cs` (bucket `Citizen`), và mọi chỗ dùng `StorageBucket.Citizen` — mỗi chỗ đó có phần tương ứng cần cho `Avatar` (compose, backup, test enum, tài liệu); `grep -rn "luxmap-citizen"` và `grep -rn "StorageBucket.Citizen"`
- Mẫu endpoint upload `IFormFile`: `src/LuxMap.Modules.WorkOrders/FaultPhotosController.cs` (field `file`), giới hạn kích thước request
- `src/LuxMap.Modules.Identity/Auth/{AuthController,AuthService,AuthContracts}.cs` (`CurrentUserResponse`, `FindCurrentUserAsync`), `Accounts/{AccountContracts,UserAdminService,UsersController}.cs`, `Entities/AppUser.cs`, `IdentityModule.cs`
- `src/LuxMap.Shared/Authorization/LuxMapPolicies.cs` + `tests/…/RoleCapabilityMatrixTests.cs`, `CapabilityMatrixTests.cs`, `CapabilityPolicyCoverageTests.cs`, `AnonymousEndpointTests.cs` (endpoint mới KHÔNG anonymous)

## Yêu cầu

1. **Endpoint** (đều `[Authorize(Policy=…)]` từng action; `ProducesResponseType` đủ 400/401/403/404/413/415/…):
   - `POST /api/v1/auth/me/avatar`, `multipart/form-data`, field **`file`**, policy `ManageOwnProfile`. Thành công → **200** `{ "avatar_url": "…" }`. Thiếu `file` → 400 `VALIDATION_FAILED` (`details.field = "file"`). Không phải JPEG / không giải mã được → `UNSUPPORTED_IMAGE_FORMAT` (mã và status như `ErrorCodes` hiện có). Quá 1 MiB → lỗi như sanitizer ném (đọc nó, đừng tự chế mã). `[RequestSizeLimit]` hợp lý (1 MiB + dư multipart) để body lớn bị chặn trước khi đọc.
   - `GET /api/v1/auth/me/avatar` → **thumbnail JPEG** (`image/jpeg`), policy `ManageOwnProfile`. Chưa có avatar → **404** `AVATAR_NOT_FOUND` (thêm vào `ErrorCodes`, dạng mã riêng như `CitizenPhotoNotFound`). Header: `Cache-Control: private, max-age=86400`, `ETag` suy từ `avatar_updated_at` (và `If-None-Match` → 304), `X-Content-Type-Options: nosniff`.
   - `DELETE /api/v1/auth/me/avatar` → **204**, idempotent (không có avatar vẫn 204). Chỉ đặt năm cột về NULL.
   - `GET /api/v1/admin/users/{id}/avatar` → như `GET me` nhưng cho `{id}`; policy `ManageUsers`; id không tồn tại hoặc chưa có avatar → 404 `AVATAR_NOT_FOUND` (không phân biệt, không lộ tồn tại).
2. **`avatar_url`**: thêm vào cuối `CurrentUserResponse` và `UserAccountItem` (string?, **null khi chưa có**). Dạng tương đối không host: `/api/v1/auth/me/avatar?v={avatar_updated_at tính bằng Unix giây, UTC}` trong `/auth/me`; `/api/v1/admin/users/{id}/avatar?v=…` trong `UserAccountItem`. Một hàm duy nhất dựng URL (cùng chỗ), không chép chuỗi ở hai nơi. `?v=` chỉ để FE bỏ cache khi đổi ảnh — server bỏ qua giá trị của nó. Cập nhật mọi nơi dựng hai record này (compile sẽ chỉ chỗ).
3. **Thứ tự ghi (BE-11 quy tắc 3):** sanitize → `StoreImageAsync` (object trước) → rồi mới set cột + `SaveChanges` (row sau). Lỗi sau khi ghi object ⇒ object mồ côi (chấp nhận được, ghi key cố định nên ghi đè lần sau). Không giữ DB transaction / khoá qua thao tác sanitize hay S3.
4. **Đua:** hai upload đồng thời cùng một người là hợp lệ (cùng key, ghi đè, row cuối thắng). Không cần khoá; chỉ cần không 500 và hàng cuối nhất quán (năm cột cùng bộ).
5. **Hạ tầng bucket** (làm đủ mọi nơi `luxmap-citizen` / `StorageBucket.Citizen` xuất hiện, trừ chỗ nội dung riêng của citizen): `StorageBucket.Avatar` + `StorageKeys.NameOf` → `"luxmap-avatar"` (kèm test bucket-name trong `ObjectStorageTests`), `docker-compose.yml` và `deploy/compose.yml` (dòng `mc mb --ignore-existing luxmap/luxmap-avatar` và câu echo), `deploy/backup.sh` (danh sách bucket), `docs/deploy/vps.md` (vòng `mc mirror`), `docs/agents/traps/media-storage.md` (đổi "Bốn bucket" thành năm + dòng avatar), `docs/development.md` nếu có bảng bucket, `docs/deploy/supabase.md` nếu liệt kê bucket. **Không** sửa `backup-offsite.sh`, `SystemStatusService`, `StorageUsage`, `StorageReconciliationJob` (chúng liệt kê theo enum, tự nhận bucket mới); chỉ xác nhận bằng đọc và ghi vào results. Nếu có test liệt kê hết bucket (đếm bốn), cập nhật.
6. **Capability `ManageOwnProfile`**: thêm vào `LuxMapPolicies` + ma trận (cả bốn vai trò, nêu tên), cập nhật literal ở `RoleCapabilityMatrixTests` và `CapabilityMatrixTests`. Đừng dùng fallback "đã đăng nhập".
7. **Cấm / đừng quên:** không `ExecuteUpdate`; không `Response.Clear` mất header (nếu lỗi cần header riêng); thông điệp lỗi không lộ đường dẫn / key; log không in byte, không in tên file người dùng.
8. **Test** (assert giá trị cụ thể; tiền tố tài khoản riêng; tài khoản test tự tạo, KHÔNG sửa tài khoản seed dùng chung; teardown theo FK; dùng object store test hiện có của `LuxMapTestHost` — đọc cách `CitizenReport…Tests` / `WorkOrderPolePhotoTests` dựng ảnh JPEG hợp lệ và khẳng định đã ghi object):
   - upload JPEG hợp lệ → 200, `avatar_url` đúng dạng; `GET me/avatar` trả `image/jpeg`, byte giải mã được, cạnh dài ≤ 512; `/auth/me` có `avatar_url` cùng giá trị; `GET /admin/users/{id}` / danh sách có `avatar_url` dạng admin;
   - **EXIF bị bỏ:** ảnh vào có EXIF (GPS) → ảnh phục vụ ra **không** có profile EXIF/GPS;
   - orientation: ảnh gốc xoay (EXIF orientation 6) → ra đã xoay đúng, không còn tag;
   - 415/`UNSUPPORTED_IMAGE_FORMAT`: PNG, WebP, file rác có đuôi `.jpg`, JPEG khai `Content-Type: image/png` vẫn nhận (quyết bằng magic bytes), PNG khai `image/jpeg` bị từ chối;
   - quá 1 MiB → lỗi đúng mã; ảnh khai kích thước pixel khổng lồ (header nhỏ) → từ chối trước khi decode;
   - thiếu `file` → 400 `details.field = file`;
   - upload lại thay ảnh: `avatar_updated_at` tiến, `avatar_url` đổi `?v=`, nội dung mới, **cùng object key**, vẫn chỉ hai object của người đó trong bucket; năm cột luôn cùng bộ;
   - DELETE: 204, `avatar_url` về null, `GET me/avatar` 404 `AVATAR_NOT_FOUND`; DELETE lần hai vẫn 204;
   - 401 không token (cả bốn endpoint); 403 `ROLE_FORBIDDEN` đúng ca (`GET /admin/users/{id}/avatar` với manager / engineer / agency); cả bốn vai trò gọi được `me/avatar`;
   - `/admin/users/{id}/avatar`: id lạ → 404 `AVATAR_NOT_FOUND`; tồn tại nhưng chưa có ảnh → cùng 404;
   - ETag / `If-None-Match` → 304; sau đổi ảnh ETag khác;
   - đua: hai POST đồng thời cùng người → cả hai 200, cột nhất quán;
   - `SystemMonitoringTests.Every_object_key_column_…`, test enum bucket, test ma trận quyền xanh với mã mới.
9. **Phá thử** (ghi vào results "chưa chạy, Claude sẽ chạy", kèm đúng dòng): (a) bỏ bước sanitize (đưa nguyên byte vào `StoreImageAsync`) ⇒ test EXIF đỏ; (b) bỏ kiểm cạnh dài ⇒ test ≤512 đỏ; (c) bỏ `AVATAR_NOT_FOUND` ở admin (trả 200 rỗng) ⇒ test 404 đỏ; (d) policy `ManageUsers` ⇒ `ManageOwnProfile` ở admin endpoint ⇒ test 403 đỏ; (e) đảo thứ tự "object trước, row sau" (set cột trước `StoreImageAsync`) — chỉ ghi nếu có cách test; (f) bỏ `?v=` ⇒ test đổi URL đỏ; (g) bỏ `StoredObjectColumn` ⇒ `Every_object_key_column_…` đỏ.
10. **Tài liệu:** `docs/contract-drift.md` dòng ACCT-5 (SELF-SIGNED 09/11/2026): bốn endpoint, JPEG-only + FE chuyển đổi, 512 px / 1 MiB, field `file`, `avatar_url` tương đối + Bearer + `fetch`→blob, `?v=`, DELETE chưa xoá byte, `AVATAR_NOT_FOUND`, không endpoint cho manager, việc FE phải làm. `docs/agents/traps/auth-accounts.md` và `media-storage.md` (bẫy ngắn: bucket thứ năm; avatar sanitize khác survey/evidence; delete chỉ gỡ liên kết). `.env.example` không đổi. `tracking.html` thêm thẻ. `README.md` nếu có bảng bucket. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*`, **KHÔNG** tạo migration.
11. Ghi `.ai/results/ACCT-AVATAR.md` ngắn: đã làm gì, lệnh Claude cần chạy, điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới (trước khi có migration, build vẫn qua)
- [ ] Test ở mục 8 có mặt; Claude sinh migration, chạy `dotnet test` xanh (Codex không chạy được test)
- [ ] Không endpoint nào mới nằm ngoài ma trận capability; `AnonymousEndpointTests` không đổi

## KHÔNG ĐƯỢC làm

- Không migration, không sửa model snapshot, không sửa file trong `src/LuxMap.Persistence/Migrations`.
- Không nhận PNG/WebP; không giải mã ngoài `IImageSanitizer` / `ThumbnailFactory.JpegOnly`; không thêm gói NuGet.
- Không presigned URL; không `IObjectStore.DeleteAsync` (ticket riêng); không sửa `S3ObjectStore`.
- Không thêm giá trị enum, không đổi field đã publish; chỉ **thêm** `avatar_url` ở cuối hai record.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*`; không sửa tài khoản seed dùng chung; không chạm `backup-offsite.sh`.
- Không chạy git, dotnet test, swagger, `dotnet ef` (sandbox không có mạng / quyền).
- Không tự chọn phương án kiến trúc khác với các mục "Mỹ chốt"; điểm mơ hồ → ghi vào results thành câu hỏi, đừng đoán.
