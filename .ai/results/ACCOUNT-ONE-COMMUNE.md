# ACCOUNT-ONE-COMMUNE — báo cáo thực thi

Ngày 29/10/2026 · worktree `wt-account-one-commune` · thực hiện D-1…D-6 đã chốt.

## Phạm vi đã thực hiện

- `IdentityConfigurations.cs`: UNIQUE `ux_app_user_commune_user_id` trên `user_id`; giữ PK ghép và index/FK cũ.
- `AccountsSingleCommune` + designer/snapshot: xếp theo `assigned_at`, hòa theo `commune_id COLLATE "C"`; xóa dòng dư, NOTICE từng cặp; sau đó tạo UNIQUE. `Down()` chỉ bỏ index, có comment không khôi phục dữ liệu.
- `UserAdminService`: kiểm **số phần tử gốc**, không gộp trùng trước khi kiểm; vai trò thường đúng một xã tồn tại; admin không nhận mảng kể cả `[]`. PATCH đặt xã duy nhất; cùng xã không thêm/xóa dòng; chuyển vai trò thường giữ xã; lên admin xóa xã; rời admin cần xã.
- Giữ DTO/response/JWT `commune_ids[]`, bảng join, capability và toàn bộ logic scope/query filter/guard/Notifier/SurveyProcessor/WorkOrder*.
- XML-doc/comment tài khoản sửa thành một xã; Contract **v1.22**, drift **ACCT-1**, traps auth/scope, Q7 + bảng gán xã của ERD, hướng dẫn backup deploy, hai dòng tương ứng trong `CLAUDE.md`; giữ symlink `AGENTS.md → CLAUDE.md`.
- Sinh spec từ code rồi generator; giữ 141 operation. Generator ghi mô tả validation theo vai trò vào `commune_ids`.
- Đã đọc `IdentitySeeder`: mỗi vai trò thường chỉ thêm một assignment, admin không thêm; không cần sửa. Quét `scripts/` không thấy seed/script gán mới nhiều xã; `copy_dev_to_supabase.py` là chép dữ liệu sẵn có, không phải dữ liệu demo tự gán. Số lượng gán xã trong `docs/deploy/supabase.md` là log lịch sử, giữ nguyên.
- Dashboard cập nhật trong `tracking.html`. **Không chạy git add / commit / push**, theo ràng buộc của Mỹ; Claude sẽ commit.

## Những chỗ cần Mỹ biết

1. Ticket viết `VALIDATION_ERROR`, trong khi Contract và `ErrorCodes.ValidationFailed` đã publish là `VALIDATION_FAILED`. Giữ mã Contract; `details.field = "commune_ids"`, middleware thêm correlation id. Đã ghi ACCT-1.
2. **Tuyến liên xã:** code hiện tại gom mọi xã của cột cùng nguồn trên tuyến rồi kiểm đủ phạm vi của người được giao. Với tài khoản một xã, có cột cùng nguồn xã khác thì **cả job trả `JOB_SCOPE_INVALID`**, chưa xử lý riêng phần xã A/ghi phần B vào độ phủ. Đã thêm test xác nhận và ghi ACCT-1. Giữ processor theo D-5; xử lý từng phần cần ticket riêng.
3. Token cũ có thể giữ phạm vi nhiều xã đến lần cấp lại (tối đa 60 phút), đúng cơ chế hiện hành; không thu hồi phiên ngoài ticket.
4. UNIQUE bảo đảm **tối đa** một assignment ở DB; điều kiện đúng một xã và admin không có assignment nằm trong service theo D-1/D-2. Vẫn kiểm fail-closed cho dữ liệu cũ thiếu assignment, không thêm trigger/schema ngoài quyết định.

## Test đã thêm / viết lại

| Test / fixture | Thay đổi và ý nghĩa |
|---|---|
| `AccountAdminTests.A_scoped_role_needs_communes_that_exist` | Thông báo đúng một xã, thêm hai xã và hai phần tử trùng; giữ xã không tồn tại. |
| `Create_and_patch_reject_zero_or_multiple_entries_before_writing` (3 vai trò) | POST/PATCH 0 hoặc 2 phần tử → 400; không tạo tài khoản/đổi assignment; PATCH xã lạ bị từ chối. |
| `An_admin_rejects_even_an_empty_assignment_array_on_create_and_patch` | Admin từ chối `[]` và mảng có xã. |
| `The_database_rejects_a_second_commune_even_without_the_service` | Chèn EF trực tiếp dòng thứ hai → PostgreSQL 23505, đúng tên UNIQUE. |
| `Patch_changes_communes_and_role_and_keeps_the_scope_flag_in_step` | Đổi xã A→B, gửi lại B, đổi vai trò thường giữ B, lên admin không còn dòng gán, rời admin thiếu xã bị từ chối, gửi xã được chấp nhận. |
| `AccountsSingleCommuneMigrationTests.Migration_keeps_earliest_assignment_breaks_ties_ordinally_and_cannot_restore_deleted_rows` | PostGIS container riêng; hai user, mỗi user ba xã: chọn xã sớm nhất và chọn ordinal khi hòa; xác nhận từng NOTICE, UNIQUE 23505, Down không phục hồi, reapply, giữ index commune, model không pending. |
| `AuthenticationTests.Multi_valued_commune_ids_are_all_readable` | Giữ kiểm parser danh sách ở mức unit với ClaimsPrincipal, không gán nhiều xã trong DB. |
| `CommuneScopeTests.Commune_id_inside_the_scope_narrows_the_result` | Dùng admin `*` để kiểm thu hẹp nhiều xã. |
| `CommuneScopeTests.Empty_commune_claim_sees_nothing` | Ký token lỗi trực tiếp, không sửa user seed. |
| `CurrentUserTests.A_commune_replaced_after_sign_in_shows_up_without_signing_in_again` | Tài khoản riêng, thay xã thay vì thêm xã thứ hai; /me cập nhật ngay, token vẫn cũ. Test legacy thiếu xã dùng tài khoản riêng. |
| `CommuneAdminTests.Auth_me_lists_the_users_communes_with_frames_and_the_box_around_them` | /me của manager một xã đúng frame; test admin nhiều frame sẵn có vẫn giữ. |
| `StatisticsTests` (grouping, overdue), `WarrantyTests` | Hai tài khoản superior, mỗi tài khoản một xã. Assert dữ liệu/thứ tự riêng từng response; không áp thứ tự toàn cục lên hai response ghép. |
| `FaultListTests` (detail/foreign filter) | Hai manager riêng; xã ngoài scope vẫn 404/403, manager xã B đọc được tài nguyên B. |
| `AssetImportFixture`, `PoleDetailEndpointTests`, `QrCodeTests` | Manager thứ hai chỉ xã B; helper đổi tên `ForeignCommuneClientAsync`. Test đọc/cấp QR khác xã dùng đúng tài khoản B. |
| `PoleWriteTests.Creating_a_pole_with_a_feeder_in_its_own_commune_still_succeeds` | Dùng manager xã A, vẫn kiểm đường tạo hợp lệ. |
| `TopologyQueryTests.A_shared_road_lists_only_poles_of_the_callers_commune` | Giữ dữ liệu cột hai xã trên cùng tuyến, assert mỗi caller chỉ thấy cột của mình. Assertion cũ caller thấy cả hai không còn tới được. |
| `WorkOrderTests` | Manager/superior một xã. Fault trộn xã bị 404 trước kiểm 409; listing cột và agenda không lấy cột/tên/vị trí tuyến ngoài phạm vi. Không sửa production service. |
| `SurveyProcessingTests` | Fixture thành phiên một xã, hai manager ở hai xã riêng; thành công chỉ audit xã A/notify manager A. Giữ revoke-scope, kiểm guard, khóa/lease/chainage. Assertion cũ thành công trên cả A+B và audit hai xã không còn áp dụng. |
| `SurveyProcessingTests.A_foreign_pole_on_the_route_fails_the_existing_job_scope_check` | Xác nhận giới hạn liên xã `JOB_SCOPE_INVALID`, không ghi observation ngoài phạm vi. |

## Test không còn tới được — giữ nguyên thân test, Skip tường minh chờ Mỹ (D-6b)

Không xóa test, không nới capability để cho admin gọi endpoint ghi tài sản/ảnh. Cần Mỹ quyết cách duy trì kiểm tra thấp hơn hoặc lưu lịch sử. Những assertion liên xã đã đổi trong các test chung được nêu ở bảng trên.

| File: tên test | Vì sao | Đề xuất để Mỹ quyết |
|---|---|---|
| `AssetImportTests.cs: The_upsert_key_is_the_COMPOSITE_when_both_communes_are_inside_the_scope` | Không còn caller ghi tài sản thấy đồng thời hai xã để phân biệt khóa upsert. | Chuyển phép kiểm khóa composite xuống service với scope hệ thống; giữ test API hai tài khoản riêng. |
| `AssetImportTests.cs: A_pole_whose_feeder_sits_in_another_commune_is_refused_per_row_and_the_same_commune_goes_through` | Feeder xã khác đã bị query filter che trước khi kiểm khớp xã của cột. | Giữ FK/kiểm service; chuyển trường hợp 409 xuống tầng service, giữ API 404. |
| `AssetReplaceAndDeleteTests.cs: A_pole_may_not_be_moved_onto_a_feeder_in_another_commune` | Đổi feeder sang xã khác không còn qua được phạm vi đọc. | Chuyển kiểm khớp xã xuống service/DB, giữ API 404. |
| `AssetReplaceAndDeleteTests.cs: A_pole_may_sit_on_a_segment_owned_by_another_commune` | Caller cần đồng thời thấy cột xã A và tuyến xã B để sửa quan hệ liên xã. | Mỹ quyết có workflow quản trị quan hệ liên xã riêng hay chỉ kiểm mô hình/DB. |
| `PoleWriteTests.cs: A_feeder_from_another_commune_is_refused_even_when_the_caller_can_see_both` | Caller không còn thấy đồng thời cột và feeder khác xã. | Kiểm helper/service hoặc DB; API một xã trả 404. |
| `PoleWriteTests.cs: Creating_a_pole_with_a_feeder_from_another_commune_is_refused` | Tạo cột với feeder xã khác không còn tới kiểm tra 409. | Như trên, giữ kiểm API ngoài phạm vi. |
| `CabinetTests.cs: A_cabinet_of_another_commune_cannot_feed_the_feeder` | Caller không còn thấy đồng thời feeder và tủ khác xã. | Chuyển kiểm khớp xã xuống service/DB; API trả 404. |
| `CabinetTests.cs: Import_refuses_per_row_what_the_database_would_refuse_at_the_write` | File import gồm tài sản hai xã không còn hợp lệ cho một caller. | Tách phần di chuyển feeder có relay cùng xã thành test độc lập; phần chéo xã chuyển tầng service. |
| `FaultReportTests.cs: An_engineer_of_several_communes_names_one_and_only_one_inside_their_scope` | Kỹ sư nhiều xã không còn được tạo qua API hoặc DB. | Giữ nhánh phòng vệ cho token cũ; Mỹ quyết giữ unit test claim hoặc lưu test lịch sử. |
| `WorkOrderPolePhotoReviewTests.cs: Review_checks_parent_membership_and_the_photos_own_commune` | Cần kỹ sư/Quản lý thấy cả xã phiếu lẫn xã ảnh để tạo rồi thu hồi riêng xã ảnh. | Giữ các test membership/cross-scope riêng hiện có; Mỹ quyết cách kiểm dữ liệu lịch sử nhiều xã. |
| `WorkOrderPolePhotoTests.cs: Pole_photo_cross_commune_uses_poles_source_and_scope_on_every_read` | Upload/duyệt ảnh cột xã B trên phiếu xã A cần hai xã cùng lúc. | Mỹ quyết kiểm dữ liệu cũ bằng service hoặc đổi workflow trong ticket riêng; không nới quyền ở đây. |

Hai test MQTT end-to-end đã Skip từ trước, không sửa trong ticket.

## Bằng chứng và lệnh xác minh

### Build, test

Đã dùng đúng các cờ của môi trường:

```bash
dotnet build -p:NuGetAudit=false --disable-build-servers
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --no-restore -p:NuGetAudit=false --disable-build-servers --logger 'trx;LogFileName=account-final.trx'
```

Build cuối **exit 0**, output thật (`/tmp/account-build-verified.log`):

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.22
```

Kết quả cuối **exit 0**, **1.712 pass, 0 fail, 13 Skip** (11 D-6b + 2 MQTT có sẵn):

```text
Passed!  - Failed:     0, Passed:   386, Skipped:     0, Total:   386, Duration: 589 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 1 s - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54, Duration: 4 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:  1227, Skipped:    13, Total:  1240, Duration: 2 m 57 s - LuxMap.Api.Tests.dll (net10.0)
```

TRX: `tests/*/TestResults/account-final.trx` (ignored); log terminal: `/tmp/account-tests-final.log`.

Nhóm tài khoản/migration đã chạy (exit 0):

```text
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 8 s - LuxMap.Api.Tests.dll (net10.0)
```

Lệnh nhóm:

```bash
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --no-restore -p:NuGetAudit=false --disable-build-servers --logger 'trx;LogFileName=account-targeted.trx' --filter 'FullyQualifiedName~AccountAdminTests|FullyQualifiedName~AccountsSingleCommuneMigrationTests'
```

NOTICE thật trong TRX của test migration:

```text
AccountsSingleCommune removed (user_id=USR-001, commune_id=COM-002)
AccountsSingleCommune removed (user_id=USR-001, commune_id=COM-003)
AccountsSingleCommune removed (user_id=USR-002, commune_id=COM-001)
AccountsSingleCommune removed (user_id=USR-002, commune_id=COM-002)
```

### Phá thử và khôi phục

1. Sao chép migration bằng `cp` sang `/tmp/account-migration-good.cs`, đổi `unique: true` thành `false` (schema không có UNIQUE), chạy:

```bash
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --no-restore -p:NuGetAudit=false --disable-build-servers --filter FullyQualifiedName~The_database_rejects_a_second_commune_even_without_the_service
```

Exit **1**, output thật:

```text
Assert.Throws() Failure: No exception was thrown
Expected: typeof(Microsoft.EntityFrameworkCore.DbUpdateException)
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 6 s - LuxMap.Api.Tests.dll (net10.0)
```

Khôi phục bằng `cp /tmp/account-migration-good.cs src/LuxMap.Persistence/Migrations/20261029090000_AccountsSingleCommune.cs`.

2. Sao chép service bằng `cp` sang `/tmp/account-service-good.cs`; đổi `communes.Count != 1` thành `communes.Count == 0`, bỏ chốt nhiều phần tử, chạy:

```bash
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test --no-restore -p:NuGetAudit=false --disable-build-servers --filter FullyQualifiedName~Create_and_patch_reject_zero_or_multiple_entries_before_writing
```

Exit **1**, output thật:

```text
Assert.Equal() Failure: Values differ
Expected: BadRequest
Actual:   InternalServerError
Failed!  - Failed:     3, Passed:     0, Skipped:     0, Total:     3, Duration: 8 s - LuxMap.Api.Tests.dll (net10.0)
```

Khôi phục bằng `cp /tmp/account-service-good.cs src/LuxMap.Modules.Identity/Accounts/UserAdminService.cs`; sau đó chỉnh dấu ngoặc cho khớp style, giữ chốt `!= 1`. Lượt toàn bộ cuối chạy trên code đã khôi phục và build lại, không dùng `--no-build`.

### Spec

```bash
DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py
npm_config_cache=/tmp/account-npm-cache npx --yes @redocly/cli lint docs/openapi/luxmap-v1.5.json
git show HEAD:docs/openapi/luxmap-v1.5.json > /tmp/account-spec-baseline.json
npm_config_cache=/tmp/account-npm-cache npx --yes @redocly/cli lint /tmp/account-spec-baseline.json
```

Cả hai lint exit **0**, cùng **9 warnings**, không thêm warning:

```text
wrote docs/openapi/luxmap-v1.5.json: paths=110 implemented_ops=141 not_implemented_ops=0 schemas=270
Woohoo! Your API description is valid. 🎉
You have 9 warnings.
```

Spec xuất từ code không đổi nội dung JSON so với HEAD (XML-doc không được export ở cấu hình hiện tại); bản hợp nhất đổi version/mô tả qua generator, không sửa JSON bằng tay.

### Môi trường và lượt dò trước đó

- `docker info --format '{{.ServerVersion}}'` trả `29.6.1`. Docker/Testcontainers chạy được; các test DB dùng PostGIS tạm, không dùng `luxmap_dev`.
- Worktree ban đầu thiếu `.env`; lượt test đầu exit 1 vì `TestDatabase.LoadEnvironment`. Đã `cp .env.example .env` (file ignored, không đưa vào diff).
- Hai lượt `dotnet ef migrations add AccountsSingleCommune ... --no-build` đứng ở tìm service provider, đã dừng Ctrl-C (exit 130); migration/designer/snapshot được tạo theo mẫu migration hiện hữu, kiểm `HasPendingModelChanges` trong test container sau apply. Không sửa dependency/props.
- Host lúc đầu khởi động chậm/đứng chờ; lượt kiểm sau đặt `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` hoặc `DOTNET_USE_POLLING_FILE_WATCHER=1` ở lệnh, không đổi cấu hình repo. Không kết luận Docker bị chặn.
- Một lượt build test bắt lỗi fixture cũ gọi helper đã gỡ; đã chuyển test /me sang tài khoản riêng và sửa initializer của token lỗi, sau đó build sạch.
- Lượt full đầu exit **1**: API 1224 pass, 3 fail, 12 skip. Ba lỗi: ảnh liên xã còn gán dòng thứ hai (đưa vào danh sách D-6b); grouping ghép hai response nhưng đòi thứ tự toàn cục (sửa assert theo từng scope); chi tiết fault xã B còn dùng manager A (đổi sang manager B). Lượt cuối xác minh sau các sửa này và sau phá thử.
- `npx --no-install` lần đầu exit 1 do cache npm ngoài sandbox (`EPERM`); dùng cache `/tmp/account-npm-cache`, không sửa quyền/cache người dùng, không thay package/dependency của repo.
- Đã rà `git diff --check`, phạm vi diff và symlink AGENTS. Không chạy git add/commit/push; không chạm cây chính.

## Bàn giao

- ✅ Validation, PATCH, UNIQUE/migration và tài liệu/spec đã thực hiện; kiểm HTTP/DB thật bằng integration test, kèm phá thử.
- ⚠️ 11 test liên xã được giữ với Skip tường minh để Mỹ quyết theo D-6b; hai test MQTT đã Skip từ trước.
- 📌 Backup DB trước deploy là bắt buộc; Down không phục hồi assignment đã xóa. Cần ticket riêng nếu xử lý từng phần tuyến liên xã.
- Commit dự kiến để Claude dùng: `feat(identity): restrict scoped accounts to one commune`.
