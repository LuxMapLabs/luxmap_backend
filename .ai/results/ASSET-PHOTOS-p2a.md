# ASSET-PHOTOS — Phase 2-A: báo cáo triển khai

12/11/2026 · Codex · branch `feat/ASSET-PHOTOS-a` · nền lượt hai `ab3f7e8e7c54952d35c02bc3638dc10befa6c107`.

**Đã hoàn tất mục 1–8.** Thay báo cáo blocker cũ bằng triển khai và bằng chứng chạy thật bên dưới.
Không phát sinh D-item thiết kế mới. Không stage / commit / push / đổi branch; thay đổi để ở working tree theo Bổ sung 12/11.
Không sửa `tracking.html`, `docs/tasks-backend.csv`, `.ai/tasks/current.md`; `AGENTS.md` vẫn là symlink.

## D-13 / D-14 — đã chốt, không còn blocker

- **D-13:** theo Bổ sung 12/11, giữ **năm bucket** Survey/Evidence/Video/Citizen/Avatar (ACCT-5).
  PAP/CAP dùng Evidence; sửa chữ §4.10 và `storage.buckets[]` cho khớp runtime, không đổi provision/backup hoặc tạo bucket mới.
  Drift APH-1 dẫn ACCT-5, không tạo deviation bucket trùng.
- **D-14:** nền **166 operation**, thêm đúng **10 → 176**; giữ Contract/spec **v1.22**, cập nhật phần A trong changelog v1.21.
  Native Swagger giữ tên group/version `v1`; bản hợp nhất là `1.22`. Không xoá hoặc sửa metadata của 166 operation cũ.
  D-01…D-06 vẫn là đặc tả; phần B/FPS-1 giữ nguyên.

## Triển khai theo mục 1–8

| Mục | Kết quả |
|---|---|
| 1. Schema | Hai entity `pole_asset_photo` / `cabinet_asset_photo`, PAP/CAP ≥6 chữ số; ICommuneScoped + query filter + CommuneWriteGuard; IAudited chỉ trên ảnh. FK cha/xã/user Restrict, FK ghép cha+xã; unique user/op; CHECK dữ liệu. Một migration EF, hai sequence, index đầy đủ commune, nới audit entity type. |
| 2. Storage | Evidence; key original/thumb theo ID, original nguyên byte; IOriginalJpegReader chặn bytes/pixel và JPEG-only; thumbnail 320/q80 bỏ EXIF/XMP/IPTC. Object trước row, không bù xoá object khi DB thất bại. Bốn StoredObjectColumn; PLAN có cả ảnh ẩn, sau cha/user, lọc cha/xã/user. |
| 3. API | Mỗi loại 5 operation: POST, GET list, original, thumbnail, DELETE. UploadAssetPhotos = manager/field_engineer; đọc ReadNetwork, ẩn ManageAssets. Kiểm form/field server/UUID/file/bytes trong service; HTTP body cap dùng chung resource filter với luồng ảnh phiếu, giữ cap cũ. |
| 4. Hiển thị | photo_count + cover_photo ở inventory cột/tủ và map cabinets; asset_photos ngoài wrapper chi tiết cột/tủ và map pole detail. AssetPhotoReader lấy theo lô, chung thứ tự created_at/length(id)/id. recent_frames giữ riêng; bbox poles/segments và bundle không đổi. |
| 5. Vòng đời | DELETE chỉ ẩn, lặp 204 không audit thêm; mở/replay ảnh ẩn 404. FK giữ cha cả khi ảnh đã ẩn: 409 ASSET_IN_USE. Nguồn ảnh là snapshot từ cha đọc lại dưới khoá; đổi nguồn cha sau đó không sửa ảnh. Upload/ẩn mỗi lần một SaveChanges cùng một audit Created/DetailsChanged, snapshot trước/sau đủ cha/xã/nguồn. |
| 6. Offline | Online-only; không sync op, không sửa bundle, không chặn work_order_complete. |
| 7. Contract/spec/docs | APH-1 SELF-SIGNED ghi người/ngày/phạm vi/quyết định; Contract đủ prefix/lỗi/quyền/retention/endpoints/summary/gallery/offline; v1.22 giữ nguyên; generator và hai spec được sinh lại; trap assets/media và development cập nhật. |
| 8. Test | HTTP thật qua TestServer + PostGIS dùng một lần; decoder JPEG thật, object-store test double. Test quota/race/replay/khoá/scopes/CHECK/FK/audit/ẩn/retention/monitoring/batch/order/rollback/schema/quyền; 56 lượt phá thử đều đỏ, khôi phục cp + touch; toàn suite mặc định exit 0. |

Service đọc cha qua filter trước IO/khoá; decode/storage không giữ khoá. Sau đó transaction khoá cha bằng SELECT 1 … FOR UPDATE,
đọc lại cha/quyền/nguồn, replay trước quota, save row+audit một lần. Unique operation race rollback **trước** lookup;
UUID cũ nay ngoài scope trả 409 không đọc bỏ scope. Hides lấy cùng khoá cha. Ảnh không đi qua AI/fault/status publication.

## Migration — đã đọc Up / Down / Designer / snapshot trước áp thử

Công cụ repo: `dotnet ef migrations add AddAssetPhotos -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations`.
Lần đầu host timeout; chạy lại với `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`, `MSBUILDDISABLENODEREUSE=1`, `--no-build`:

```text
Done. To undo this action, use 'ef migrations remove'
[exit 0]
```

File: `20261112124951_AddAssetPhotos.cs` + Designer + snapshot. Một migration, không thêm xmin,
không AlterColumn/DropColumn/DropIndex ngoài phạm vi. Tên dài nhất 63 ký tự:
`fk_cabinet_asset_photo_electrical_cabinet_cabinet_id_commune_id`.

Các đoạn Up đã kiểm trực tiếp (trích output file):

```csharp
migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.CreateSequence(
                name: "cabinet_asset_photo_id_seq");

            migrationBuilder.CreateSequence(
                name: "pole_asset_photo_id_seq");
// DEFAULTs do EF sinh:
// luxmap_format_id('CAP', nextval('cabinet_asset_photo_id_seq'), 6)
// luxmap_format_id('PAP', nextval('pole_asset_photo_id_seq'), 6)
```

CHECK thật trong hai CreateTable:

```csharp
table.CheckConstraint("ck_cabinet_asset_photo_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
table.CheckConstraint("ck_cabinet_asset_photo_deleted", "(deleted_at IS NULL) = (deleted_by IS NULL)");
table.CheckConstraint("ck_cabinet_asset_photo_hash", "request_hash ~ '^[0-9a-f]{64}$'");
table.CheckConstraint("ck_cabinet_asset_photo_keys", "object_key = 'original/' || photo_id || '.jpg' AND thumbnail_key = 'thumb/' || photo_id || '.jpg'");
table.CheckConstraint("ck_cabinet_asset_photo_operation", "client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
table.CheckConstraint("ck_cabinet_asset_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
table.CheckConstraint("ck_pole_asset_photo_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
table.CheckConstraint("ck_pole_asset_photo_deleted", "(deleted_at IS NULL) = (deleted_by IS NULL)");
table.CheckConstraint("ck_pole_asset_photo_hash", "request_hash ~ '^[0-9a-f]{64}$'");
table.CheckConstraint("ck_pole_asset_photo_keys", "object_key = 'original/' || photo_id || '.jpg' AND thumbnail_key = 'thumb/' || photo_id || '.jpg'");
table.CheckConstraint("ck_pole_asset_photo_operation", "client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
table.CheckConstraint("ck_pole_asset_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
```

Pole dùng lại alternate key DB-only `ak_pole_photo_target`, không biến CommuneId thành EF key và không tạo key trùng:

```sql
ALTER TABLE pole_asset_photo ADD CONSTRAINT fk_pole_asset_photo_target
    FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT;
```

Cabinet FK ghép dùng alternate key đã có trong model. Mọi FK tới cha, administrative_unit, uploaded_by, deleted_by đều Restrict.
Index đầy đủ `ix_pole_asset_photo_commune_id` / `ix_cabinet_asset_photo_commune_id` được giữ;
unique operation là `ux_pole_asset_photo_user_operation` / `ux_cabinet_asset_photo_user_operation`.
`ck_audit_event_entity_type` giữ 9 giá trị nền và thêm đúng `pole_asset_photo`, `cabinet_asset_photo`; action CHECK không đổi.
Snapshot/Designer không mang thay đổi nghiệp vụ ngoài hai bảng/sequence/audit type; giữ nguyên literal CHECK cũ của work-order.

Down thật (guard đứng trước mọi thao tác xoá):

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM pole_asset_photo)
                       OR EXISTS (SELECT 1 FROM cabinet_asset_photo)
                       OR EXISTS (SELECT 1 FROM audit_event WHERE entity_type IN ('pole_asset_photo', 'cabinet_asset_photo')) THEN
                        RAISE EXCEPTION 'Asset photos or their audit exist; rollback must retain their schema.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "cabinet_asset_photo");

            migrationBuilder.DropTable(
                name: "pole_asset_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.DropSequence(
                name: "cabinet_asset_photo_id_seq");

            migrationBuilder.DropSequence(
                name: "pole_asset_photo_id_seq");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole', 'survey_recurring_schedule')");
        }
```

`AssetPhotoMigrationTests` chạy container **riêng trống**: Up → Down khi trống → Up; thử Down với ảnh đã ẩn ở từng bảng,
với từng audit type mới khi bảng ảnh trống; đều từ chối, giữ cả migration history và hai bảng. Xoá dữ liệu kiểm thử trên container đó
rồi Down/Up lại thành công; `HasPendingModelChanges()` false. Không apply vào luxmap_dev.

## Restore / build / test — output thật

Lần đầu lệnh bắt buộc `dotnet restore LuxMap.slnx -warnaserror` exit 1, lỗi ghi HTTP cache ngoài sandbox:

```text
/Users/nhm809/Documents/LuxMap/luxmap_backend-asset-photos/src/LuxMap.Modules.Faults/LuxMap.Modules.Faults.csproj : error NU1900: Error occurred while getting package vulnerability data: Access to the path '/Users/nhm809/.local/share/NuGet/http-cache/670c1461c29885f9aa22c281d8b7da90845b38e4$ps:_api.nuget.org_v3_index.json/vuln_index.dat-new' is denied. [/Users/nhm809/Documents/LuxMap/luxmap_backend-asset-photos/LuxMap.slnx]
```

Đặt `NUGET_HTTP_CACHE_PATH=/private/tmp/luxmap-asset-photos-nuget-cache` và restore `--force -warnaserror` thành công.
Không tắt NuGet audit. Lượt test đầu thiếu `.env`; đã làm `cp .env.example .env` theo README (file ignored).
Test/EF sau đó timeout IHost 5 phút, không phải bằng chứng Docker bị chặn. Chạy lại với
`DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` cho từng lệnh thì host/tooling chạy được; không đổi cấu hình runtime.
Docker/Testcontainers đã thực sự chạy PostGIS và các lượt migration; không dùng luxmap_dev.

Lệnh chốt (cache tạm, watcher off cho invocation):

```text
$ dotnet restore LuxMap.slnx -warnaserror
  Determining projects to restore...
  All projects are up-to-date for restore.
[exit 0]

$ dotnet build --disable-build-servers
Build succeeded.
    0 Warning(s)
    0 Error(s)
[exit 0]

$ dotnet test --disable-build-servers
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 839 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   423, Skipped:     0, Total:   423, Duration: 427 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    83, Skipped:     0, Total:    83, Duration: 4 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:  1678, Skipped:     7, Total:  1685, Duration: 4 m 45 s - LuxMap.Api.Tests.dll (net10.0)
[exit 0]
```

**Tổng: 2.229 pass, 7 skip, 0 fail** (suite mặc định Category!=Benchmark).
Bảy skip có sẵn ở nền, gồm các ca nhiều xã chờ ACCT-1/D-6b và hai ca MQTT thật:

```text
Skipped LuxMap.Api.Tests.WorkOrderTests.Review_checks_parent_membership_and_the_photos_own_commune [1 ms]
Skipped LuxMap.Api.Tests.WorkOrderTests.Pole_photo_cross_commune_uses_poles_source_and_scope_on_every_read [1 ms]
Skipped LuxMap.Api.Tests.MqttEndToEndTests.A_device_receives_executes_and_acknowledges_over_the_real_broker [1 ms]
Skipped LuxMap.Api.Tests.MqttEndToEndTests.The_broker_enforces_identity_and_each_devices_own_topics [1 ms]
Skipped LuxMap.Api.Tests.FaultReportTests.An_engineer_of_several_communes_names_one_and_only_one_inside_their_scope [1 ms]
Skipped LuxMap.Api.Tests.AssetReplaceAndDeleteTests.A_pole_may_sit_on_a_segment_owned_by_another_commune [1 ms]
Skipped LuxMap.Api.Tests.AssetImportTests.The_upsert_key_is_the_COMPOSITE_when_both_communes_are_inside_the_scope [1 ms]
```

Lượt full đầu exit 1: 5 assertion literal field của AssetReadShapeTests, CabinetTests, PoleDetailEndpointTests còn shape cũ.
Đã thêm đúng field D-04 và assert empty/null; 47 ca liên quan pass, rồi full suite trên bản sửa pass như trên.
Đã bổ sung metadata 200/400/404 cho GET list ảnh, chạy lại **29 OpenApiSpecTests, exit 0**, rồi sinh/lint spec lại.
Lượt full chốt bên trên chạy sau phần metadata và sau cả 56 phá thử; build 0 warning/error. Không sửa skip để đạt kết quả này.

Hai regression thêm trong lúc rà diff đã được chạy **đỏ trước sửa**:

```text
UUID_reused_after_scope_narrows… → INTERNAL_ERROR thay vì 409
Upload_replay_hide_and_retention… (hai loại) → before audit thiếu khoá cha
Failed! - Failed: 3, Passed: 0, Skipped: 0, Total: 3
[exit 1]
```

Đã sửa unique-operation catch chỉ cho hai index đúng, rollback rồi lookup có scope / trả conflict khi row ngoài scope;
audit dùng snapshot immutable đủ metadata trước/sau. Nhóm ảnh + migration + schema sau sửa: **29 pass, exit 0**.

## SQL theo lô — interceptor trên query thực

```text
$ dotnet test tests/LuxMap.Api.Tests --no-restore --disable-build-servers \
  --filter 'FullyQualifiedName~Listing_one_or_two_hundred_assets' --logger 'console;verbosity=detailed'
SQL reads /api/v1/assets/poles: page 1 = 3; page 200 = 3; gallery batch = 1
SQL reads /api/v1/assets/cabinets: page 1 = 4; page 200 = 4; gallery batch = 1
SQL reads /api/v1/map/cabinets: 200 cabinets = 2; gallery batch = 1
Total tests: 2
Passed: 2
[exit 0]
```

Trích SQL EF thực (không query từng tài sản, không bỏ scope):

```sql
SELECT p.pole_id AS "ParentId", p.photo_id, p.byte_count, p.client_op_id, p.commune_id, p.created_at, p.data_source, p.deleted_at, p.deleted_by, p.height, p.object_key, p.request_hash, p.thumbnail_bytes, p.thumbnail_key, p.uploaded_by, p.width
FROM pole_asset_photo AS p
WHERE (@ef_filter__IsSystemWide2 OR p.commune_id = ANY (@ef_filter__CommuneIds)) AND p.deleted_at IS NULL AND p.pole_id = ANY (@ids)
ORDER BY p.created_at, length(p.photo_id)::int, p.photo_id

SELECT c.cabinet_id AS "ParentId", c.photo_id, c.byte_count, c.client_op_id, c.commune_id, c.created_at, c.data_source, c.deleted_at, c.deleted_by, c.height, c.object_key, c.request_hash, c.thumbnail_bytes, c.thumbnail_key, c.uploaded_by, c.width
FROM cabinet_asset_photo AS c
WHERE (@ef_filter__IsSystemWide2 OR c.commune_id = ANY (@ef_filter__CommuneIds)) AND c.deleted_at IS NULL AND c.cabinet_id = ANY (@ids)
ORDER BY c.created_at, length(c.photo_id)::int, c.photo_id
```

Test thứ tự cập nhật hai ID sinh theo registry qua ranh giới PAP/CAP 999999 → 1000000 **cùng transaction và created_at = now()**,
assert thứ tự thật; rollback trả dữ liệu ban đầu, không lùi sequence. Test khoá dùng FOR NO KEY UPDATE ở holder,
chỉ cho upload xong sau nhả khoá; storage đã ghi trước lúc đợi khoá.

## Phá thử từng chốt — 56/56 RED

Mỗi dòng là một invocation `dotnet test <project> --no-restore --disable-build-servers --filter FullyQualifiedName~<filter>`.
Chỉ một dotnet test tại một thời điểm. Mỗi file có bản sao trước phá; khôi phục bằng **cp rồi touch**, không mv.
Không dùng --no-build cho phá thử. Các lỗi dưới đây là assertion/runtime đỏ, không phải build lỗi; mã thoát đều 1.
Cột số là **fail/pass/skip/total** trong lượt cố ý phá (không phải kết quả bản khôi phục).

| # | Chốt bị phá | Filter | Exit | F/P/S/T |
|---|---|---|---|---|
| 1 | `ck_pole_asset_photo_sizes` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 2 | `ck_pole_asset_photo_keys` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 3 | `ck_pole_asset_photo_hash` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 4 | `ck_pole_asset_photo_operation` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 5 | `ck_pole_asset_photo_deleted` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 6 | `ck_pole_asset_photo_data_source` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 7 | `ck_cabinet_asset_photo_sizes` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 8 | `ck_cabinet_asset_photo_keys` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 9 | `ck_cabinet_asset_photo_hash` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 10 | `ck_cabinet_asset_photo_operation` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 11 | `ck_cabinet_asset_photo_deleted` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 12 | `ck_cabinet_asset_photo_data_source` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 13 | `FK-pole-commune` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 14 | `FK-cabinet-commune` | `Database_checks_and_same_commune_FK` | 1 | 1/1/0/2 |
| 15 | `photo-query-filter` | `Photo_rows_have_independent` | 1 | 2/0/0/2 |
| 16 | `photo-write-guard` | `Photo_rows_have_independent` | 1 | 2/0/0/2 |
| 17 | `Pole-audit-marker` | `Photo_rows_have_independent` | 1 | 1/1/0/2 |
| 18 | `Cabinet-audit-marker` | `Photo_rows_have_independent` | 1 | 1/1/0/2 |
| 19 | `jpeg-magic` | `Magic_bytes_are_checked_independently` | 1 | 1/0/0/1 |
| 20 | `bounded-read` | `Bounded_reader_rejects_bytes` | 1 | 1/0/0/1 |
| 21 | `pixel-cap` | `Bounded_reader_rejects_bytes` | 1 | 1/0/0/1 |
| 22 | `strip-Exif` | `Thumbnail_strips_each_profile` | 1 | 1/0/0/1 |
| 23 | `strip-Xmp` | `Thumbnail_strips_each_profile` | 1 | 1/0/0/1 |
| 24 | `strip-Iptc` | `Thumbnail_strips_each_profile` | 1 | 1/0/0/1 |
| 25 | `quota-at-nine` | `Quota_serializes_the_tenth` | 1 | 2/0/0/2 |
| 26 | `replay-before-quota` | `Overlapping_identical_replays` | 1 | 2/0/0/2 |
| 27 | `parent-lock-pole` | `Upload_waits_for_the_parent_lock` | 1 | 1/1/0/2 |
| 28 | `parent-lock-cabinet` | `Upload_waits_for_the_parent_lock` | 1 | 1/1/0/2 |
| 29 | `list-hidden` | `Upload_replay_hide_and_retention` | 1 | 2/0/0/2 |
| 30 | `media-hidden` | `Upload_replay_hide_and_retention` | 1 | 2/0/0/2 |
| 31 | `gallery-hidden` | `Summaries_gallery_and_source_snapshot` | 1 | 2/0/0/2 |
| 32 | `replay-hidden` | `Upload_replay_hide_and_retention` | 1 | 2/0/0/2 |
| 33 | `repeat-hide-audit` | `Upload_replay_hide_and_retention` | 1 | 2/0/0/2 |
| 34 | `parent-retention-pole` | `Upload_replay_hide_and_retention` | 1 | 1/1/0/2 |
| 35 | `parent-retention-cabinet` | `Upload_replay_hide_and_retention` | 1 | 1/1/0/2 |
| 36 | `policy-Pole-Upload` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 37 | `policy-Pole-List` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 38 | `policy-Pole-Original` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 39 | `policy-Pole-Thumbnail` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 40 | `policy-Pole-Hide` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 41 | `policy-Cabinet-Upload` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 42 | `policy-Cabinet-List` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 43 | `policy-Cabinet-Original` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 44 | `policy-Cabinet-Thumbnail` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 45 | `policy-Cabinet-Hide` | `Each_of_the_ten_actions` | 1 | 1/0/0/1 |
| 46 | `registry-pole-Original` | `SystemMonitoringTests.Every_object_key_column` | 1 | 1/0/0/1 |
| 47 | `registry-pole-Thumbnail` | `SystemMonitoringTests.Every_object_key_column` | 1 | 1/0/0/1 |
| 48 | `registry-cabinet-Original` | `SystemMonitoringTests.Every_object_key_column` | 1 | 1/0/0/1 |
| 49 | `registry-cabinet-Thumbnail` | `SystemMonitoringTests.Every_object_key_column` | 1 | 1/0/0/1 |
| 50 | `id-width-order` | `List_and_gallery_order_across` | 1 | 2/0/0/2 |
| 51 | `down-retention-guard` | `AssetPhotoMigrationTests.Fresh_schema` | 1 | 1/0/0/1 |
| 52 | `source-snapshot` | `Summaries_gallery_and_source_snapshot` | 1 | 2/0/0/2 |
| 53 | `server-owned-fields` | `Service_rejects_magic_bytes_server_fields` | 1 | 2/0/0/2 |
| 54 | `request-cap` | `Request_cap_is_enforced` | 1 | 2/0/0/2 |
| 55 | `ck_audit_event_entity_type-extension` | `Upload_replay_hide_and_retention` | 1 | 1/1/0/2 |
| 56 | `UploadAssetPhotos-role-matrix` | `RoleCapabilityMatrixTests` | 1 | 1/112/0/113 |

Ví dụ lỗi thật: 12 CHECK mới/FK bị bỏ → `Assert.Throws() Failure: No exception was thrown`;
filter xã bỏ → `Assert.False() Failure … Actual: True`; bỏ guard audit/write → không còn exception;
bỏ từng profile → `Assert.Null() Failure … ExifProfile/XmpProfile/IptcProfile`;
quota → `[201,201]` thay `[201,409]`; replay sau quota → không còn `[200,201]`;
bỏ khoá → test chờ tín hiệu lock timeout; policy đổi → literal policy không khớp;
registry bỏ → tập cột thực khác tập đăng ký; Down guard bỏ → không còn từ chối rollback.
Bỏ phần nới audit type → upload PAP lỗi 500 thay 201; cho thêm system_admin vào policy upload → test literal đòi 403 nhận 400.
Sau toàn bộ phá thử đã khôi phục và chạy full suite xanh, không chỉ kết luận từ dòng Passed của một test riêng.

Log/script chi tiết của invocation trong `/private/tmp/asset-photos-mutations/` (tạm trên máy); báo cáo này giữ bảng verdict/exit/count
để bàn giao không phụ thuộc log tạm. Test tái lập nằm trong repo.

## Spec / lint / diff

```text
$ dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
Swagger JSON/YAML successfully written …/docs/openapi/luxmap-v1.json
[exit 0]
$ python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=136 implemented_ops=176 not_implemented_ops=0 schemas=310
[exit 0]
$ npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
Your API description is valid.
You have 10 warnings.
[exit 0]
```

Redocly 2.77.0 đo trên **spec nền trước sinh: 10 warning**, sau sinh: **10** (không tăng; số 9 của nền ticket cũ không phải số đo lượt này).
Parser đối chiếu cả hai spec với HEAD: **166 →176, removed=0, old-operation-metadata-changes=0**.
Chỉ 6 schema cũ thay đổi đúng phạm vi: CabinetDetail, CabinetListItem, CabinetProperties, PoleDetail, PoleListItem, PoleMapDetail.
PoleProperties, SegmentProperties, SyncPoleProperties không thêm field. Prefix pattern bổ sung qua generator;
không chỉnh JSON sinh bằng tay. Generator giữ schema từ code, chỉ annotate ID và bổ sung summary/tag cho mười op mới.

✅ Verified: restore/build, HTTP+PostGIS/JPEG, migration Up/Down/guard/catalog/model, kiểm quyền/scope/CHECK/audit/quota/race,
56 phá thử, batch SQL, sinh spec/lint, diff/check và không staged.
⚠️ Could not verify: MinIO thật trên đường network (store test double; adapter/storage test suite đã chạy), xác nhận UI của WP5/WP6.
📌 Remaining assumptions: thumbnail 320/q80, online-only, cách UI nhận cover null / gallery rỗng và cảnh báo không xoá cha
vẫn là nền SELF-SIGNED chờ WP5/WP6 theo D-02…D-06. Đây là việc bàn giao đã nêu trong APH-1, không phải blocker code mới.

## Nhóm file cho commit gợi ý — Claude/Mỹ thực hiện, Codex không stage/commit

Các nhóm thuộc cùng PR vào dev; không trailer Co-Authored-By. AssetsModule.cs có cả registration storage và DI service:
nhóm 1 lấy hunk `using LuxMap.Shared.Storage` + bốn StoredObjectColumn; nhóm 2 lấy hunk Photos namespace và hai AddScoped.

### 1. feat(ASSET-PHOTOS): add scoped asset photo persistence

```text
src/LuxMap.Modules.Assets/Entities/IAssetPhoto.cs
src/LuxMap.Modules.Assets/Entities/PoleAssetPhoto.cs
src/LuxMap.Modules.Assets/Entities/CabinetAssetPhoto.cs
src/LuxMap.Modules.Assets/Configurations/PoleAssetPhotoConfiguration.cs
src/LuxMap.Modules.Assets/Configurations/CabinetAssetPhotoConfiguration.cs
src/LuxMap.Modules.Assets/AssetsModule.cs [storage hunks]
src/LuxMap.Persistence/Audit/AuditEvent.cs
src/LuxMap.Persistence/Migrations/20261112124951_AddAssetPhotos.cs
src/LuxMap.Persistence/Migrations/20261112124951_AddAssetPhotos.Designer.cs
src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs
src/LuxMap.Shared/Contracts/PrefixedId.cs
tests/LuxMap.Shared.Tests/PrefixedIdTests.cs
tests/LuxMap.Api.Tests/AssetPhotoMigrationTests.cs
```

### 2. feat(ASSET-PHOTOS): implement online asset photo galleries

```text
src/LuxMap.Modules.Assets/Photos/AssetPhotoContracts.cs
src/LuxMap.Modules.Assets/Photos/AssetPhotoReader.cs
src/LuxMap.Modules.Assets/Photos/AssetPhotoService.cs
src/LuxMap.Modules.Assets/Photos/PoleAssetPhotosController.cs
src/LuxMap.Modules.Assets/Photos/CabinetAssetPhotosController.cs
src/LuxMap.Modules.Assets/AssetsModule.cs [service DI hunks]
src/LuxMap.Shared/Http/UploadBodyLimitAttribute.cs
src/LuxMap.Modules.WorkOrders/PolePhotoBodyLimitAttribute.cs
src/LuxMap.Shared/Authorization/LuxMapPolicies.cs
src/LuxMap.Shared/Contracts/Errors/ErrorCodes.cs
tests/LuxMap.Shared.Tests/CapabilityMatrixTests.cs
tests/LuxMap.Api.Tests/RoleCapabilityMatrixTests.cs
tests/LuxMap.Infrastructure.Storage.Tests/AssetPhotoJpegTests.cs
```

### 3. feat(ASSET-PHOTOS): expose photo summaries and detail galleries

```text
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs
src/LuxMap.Modules.Map/Features/MapProperties.cs
src/LuxMap.Modules.Map/Features/MapQueryService.cs
src/LuxMap.Modules.Map/Features/PoleMapDetail.cs
src/LuxMap.Modules.Map/Features/PoleDetailService.cs
tests/LuxMap.Api.Tests/AssetPhotoTests.cs
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs
tests/LuxMap.Api.Tests/CabinetTests.cs
tests/LuxMap.Api.Tests/PoleDetailEndpointTests.cs
tests/LuxMap.Api.Tests/OpenApiSpecTests.cs
```

### 4. docs(ASSET-PHOTOS): publish gallery contract and generated spec

```text
docs/api-contract-v1.1.md
docs/contract-drift.md
docs/openapi/tools/gen_consolidated_spec.py
docs/openapi/luxmap-v1.json
docs/openapi/luxmap-v1.5.json
docs/agents/traps/assets.md
docs/agents/traps/media-storage.md
docs/development.md
scripts/copy_dev_to_supabase.py
.ai/results/ASSET-PHOTOS-p2a.md
```

`git diff --cached --stat` rỗng. Không chạy git add/commit/push trong lượt này, không đổi branch, không mở PR/deploy.
