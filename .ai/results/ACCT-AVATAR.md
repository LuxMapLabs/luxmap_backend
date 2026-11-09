# ACCT-AVATAR — phase 2 (09/11/2026)

Đã implement theo quyết định A / ACCT-5 SELF-SIGNED; chưa xác nhận build/runtime.

- `Identity/Profiles/AvatarService.cs`, `AvatarController.cs`: bốn route, policy từng action, JPEG sanitize qua port hiện có (1 MiB / 16 triệu pixel / 512 px), object trước row, key cố định, ghi cả bộ năm cột trong một SaveChanges, DELETE chỉ gỡ liên kết; private cache/ETag/304/nosniff, admin 404 không phân biệt tài khoản thiếu.
- AppUser + IdentityConfigurations: năm cột nullable, CHECK all-or-none và byte ≥0; IdentityModule đăng ký service và đúng hai StoredObjectColumn tường minh. Không migration/snapshot.
- Thêm cuối hai DTO `avatar_url`, một helper dựng URL self/admin với Unix giây UTC. Thêm AVATAR_NOT_FOUND, ManageOwnProfile và literal ở hai test ma trận.
- StorageBucket.Avatar / luxmap-avatar; hai compose, backup.sh, hướng dẫn mirror VPS/Supabase, traps media/auth, development, drift ACCT-5, tracking.html. Không thay .env.example/Contract/OpenAPI/README (README không có bảng bucket).
- AvatarTests có ca HTTP cho upload/view, byte JPEG giải mã được, EXIF/GPS/orientation, original 512 và thumbnail 320, magic bytes/mime sai, PNG/WebP/rác/JPEG hỏng, byte/pixel quá giới hạn, thiếu file, replacement/key/bytes/URL/ETag, unlink idempotent, null DTO, bốn vai trò/401/403/admin 404, conditional GET, hai POST đồng thời, lỗi store trước publish row, metadata không anonymous, CHECK partial/negative. Tài khoản tiền tố avatar- riêng, không sửa seed; host kế thừa LuxMapTestHost qua AccountTestFactory, store PhotoStore hiện có.
- Cập nhật test hiện có: CurrentUserTests exact shape, ObjectStorageTests bucket name, SystemMonitoringTests array và số bucket 4→5. AnonymousEndpointTests giữ nguyên.

## Kiểm chứng thật

✅ `bash -n deploy/backup.sh`: exit 0, không output.
✅ Kiểm tra tĩnh bằng đọc/rg và Python: AGENTS.md còn symlink, đúng hai đăng ký inventory, các nơi dựng DTO đã cập nhật. Output:

```text
Static scope checks passed; AGENTS.md remains a symlink; exactly two avatar inventory registrations.
```

⚠️ Lệnh đã thử `dotnet build LuxMap.slnx --disable-build-servers --no-restore` → exit **1**:

```text
/usr/local/share/dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk/targets/Microsoft.PackageDependencyResolution.targets(266,5): error NETSDK1004: Assets file '/Users/nhm809/Documents/LuxMap/wt-avatar/src/LuxMap.Api/obj/project.assets.json' not found. Run a NuGet package restore to generate this file. [/Users/nhm809/Documents/LuxMap/wt-avatar/src/LuxMap.Api/LuxMap.Api.csproj]
```

Chưa build thành công; chưa thể xác nhận cảnh báo/biên dịch. Không chạy test/runtime/phá thử/Swagger/EF/git theo chỉ thị. Không commit.

Đã đọc xác nhận, không sửa: StorageUsage.TotalsAsync dùng Enum.GetValues; StorageReconciliationJob lặp enum; SystemStatusService đọc bucket từ totals, nên tự có Avatar. backup-offsite.sh mirror cả /backup, nên tự nhận dữ liệu bucket mới từ backup.sh (không phải script này tự liệt kê enum).

## Claude tiếp tục

Các lệnh sau **chưa chạy**, chạy ở môi trường có restore/Docker theo docs/development.md:

```bash
dotnet build LuxMap.slnx --disable-build-servers
dotnet ef migrations add AddAccountAvatars -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations
# Đọc Up/Down trước khi apply: chỉ năm cột/check app_user và CHECK bucket cần đổi.
dotnet test LuxMap.slnx --disable-build-servers --filter 'FullyQualifiedName~AvatarTests|FullyQualifiedName~CurrentUserTests|FullyQualifiedName~ObjectStorageTests|FullyQualifiedName~RoleCapabilityMatrixTests|FullyQualifiedName~CapabilityMatrixTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~AnonymousEndpointTests|FullyQualifiedName~SystemMonitoringTests'
dotnet test LuxMap.slnx --disable-build-servers
```

Migration cần cho phép `avatar` trong `ck_storage_reconciliation_bucket` nữa: MonitoringEntities.HasContractEnum(Bucket) tự sinh CHECK mới từ enum. Kiểm tra cả Up/Down và snapshot khi Claude sinh. Test host apply migration vào PostGIS riêng, không chạy test trên luxmap_dev. Provision bucket qua sidecar; kiểm tay trên MinIO thật còn cần Claude. Không sinh OpenAPI ở lượt này theo ticket.

## Phá thử — chưa chạy, Claude sẽ chạy

- (a) AvatarService.UploadAsync: bỏ `sanitizer.SanitizeJpegAsync`, đưa nguyên byte vào StoreImageAsync ⇒ `Upload_serves_clean_rotated_thumbnail_and_both_account_views_link_to_it` phải đỏ ở EXIF/orientation/original size.
- (b) JpegSanitizer.CleanAsync: bỏ khối kiểm/resize MaxLongEdge ⇒ cùng test phải đỏ tại `Assert.Equal(512, original.Height)`.
- (c) Avatar admin GET: bỏ AVATAR_NOT_FOUND, trả 200 rỗng ⇒ `Anonymous_is_401_on_all_routes_and_missing_admin_avatars_are_indistinguishable` đỏ ở 404.
- (d) AvatarController.Admin: đổi ManageUsers → ManageOwnProfile ⇒ `Each_role_manages_own_avatar_and_only_admin_reads_another` đỏ ở 403 cho manager/field_engineer/superior.
- (e) Nếu đưa **SaveChanges publish row** lên trước StoreImageAsync ⇒ `Storage_failure_never_publishes_a_row_before_objects` đỏ vì callback quan sát row và sau lỗi phải còn null. Test này không chứng minh việc chỉ gán thuộc tính trong ChangeTracker sớm hơn (chưa save); không tuyên bố phá thử đó đã được canh.
- (f) AvatarUrls.For: bỏ `?v=` ⇒ `Replacement_reuses_keys_changes_bytes_url_etag_then_unlink_is_idempotent` đỏ ở URL khác; test upload đầu cũng assert URL cụ thể.
- (g) IdentityModule: bỏ một StoredObjectColumn ⇒ `SystemMonitoringTests.Every_object_key_column_…` đỏ sau khi migration có năm cột thật.

## Câu hỏi/giới hạn cần review

- D-1: Ticket yêu cầu `?v=` Unix **giây**, đồng thời nói mỗi upload đổi URL. Hai upload trong cùng giây không thể bảo đảm URL khác với định dạng này. Hiện giữ nguyên định dạng đã chốt, ETag lấy ticks UTC (µs); test replacement chủ động tiến fake clock 1 giây. Có muốn đổi độ phân giải cache version ở ticket sau không? Đã ghi giới hạn ở ACCT-5, không tự đổi API.
- Không có khoá/transaction qua S3 như ticket: SaveChanges ghi trọn bộ metadata, nhưng test store không chứng minh tính nguyên tử giữa hai object S3 và row trong mọi thứ tự đua/lỗi. Chưa thử MinIO thật, chưa kiểm byte thực tế khi S3 lỗi giữa hai PUT; key cố định sẽ bị upload sau ghi đè theo quyết định A.

## Claude đã xác minh (09/11/2026)

- Build `LuxMap.slnx`: exit 0, 0 cảnh báo. `dotnet test LuxMap.slnx`: exit 0 — 1499 passed / 7 skipped (Api.Tests).
- Migration `AddAccountAvatars` do Claude sinh (`dotnet ef`): năm cột nullable + `ck_app_user_avatar_all_or_none` + **mở `ck_storage_reconciliation_bucket` thêm `'avatar'`**
  (Codex phát hiện đúng). Sửa `Down()`: xoá dòng `storage_reconciliation` của bucket `avatar` trước khi trả CHECK cũ (nếu không `Down` lỗi khi đã có báo cáo).
  `Down()` đã chạy thật qua `WorkOrderCancellationMigrationTests` (lùi qua mọi migration sau `previous`).
- Hai test migration cũ đỏ vì ghi `AppUser` bằng EF ở schema cũ (thiếu cột `avatar_*`) → thêm `OldSchemaUser.InsertAsync` (SQL) và ghi bẫy vào `traps/testing.md`.
- Hai sửa test của Codex: `Cache-Control` so theo giá trị đã parse (ASP.NET đổi thứ tự chuỗi); ca "thiếu file" gửi form có field khác (form multipart rỗng sai chuẩn bị chặn trước controller).
- D-1 (`?v=` Unix giây): đổi sang **mili-giây** (code, test, drift, trap) để hai upload cùng giây vẫn đổi URL.
- Phá thử (build **project test** rồi `--no-build`): (a) bỏ sanitize ⇒ 2 test đỏ · (b) bỏ resize ⇒ test ≤512 đỏ · (c) admin không 404 ⇒ 2 đỏ · (d) policy admin ⇒ 3 đỏ (manager/engineer/agency) ·
  (e) publish row trước khi ghi object ⇒ `Storage_failure_never_publishes_a_row_before_objects` đỏ · (f) bỏ `?v=` ⇒ 3 đỏ · (g) bỏ `StoredObjectColumn` ⇒ `Every_object_key_column_…` đỏ. Khôi phục bằng `cp`.
- OpenAPI: 151 operation, lint hợp lệ (10 cảnh báo nền). `gen_consolidated_spec.py`: thêm tóm tắt 4 route và sửa luật "anonymous" — mọi `/auth/*` trừ đúng `/auth/me` bị coi là công khai, nay loại cả `/auth/me/*`.
- Chưa kiểm: MinIO thật (bucket `luxmap-avatar` tạo qua sidecar `minio-mc` khi deploy), ảnh chụp từ điện thoại thật.
