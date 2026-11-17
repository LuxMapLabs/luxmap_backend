# POLE-PHOTO-WITHDRAW — kết quả phase 2 (17/11/2026)

## Đã triển khai

Theo quyết định Mỹ trong ticket, không qua phase khảo sát; không commit, không push.

- DELETE ảnh/clip chưa quyết, đúng người được giao và scope xã. Thứ tự lỗi đúng ticket; 204 body rỗng, retry sau `done` vẫn 204, không save/audit thêm.
- Khóa `work_order → pole → photo`, đọc lại sau khóa; một `SaveChanges` + audit `Deleted`, entity `WorkOrder`, xã theo ảnh. Before/after có photo/pole, nhãn AI, nhãn kỹ sư, `has_video`, dấu hủy.
- `Photos(includeWithdrawn = false)` là chỗ duy nhất lọc dấu hủy. List/total/detail/media/review/decision và lượt ghi xem video dùng truy vấn chung. DELETE và replay thấy cả row đã hủy. Không global soft-delete filter, không đổi inventory hoặc các `StoredObjectColumn`.
- Replay cả upload JPEG và MP4 trả 409 `POLE_PHOTO_WITHDRAWN`, `details.photo_id`, trước hash/status; không infer/ghi object. Phải dùng operation ID mới cho capture mới.
- Đã đọc sync contracts/bundle/push: bundle không chứa pole-photo, push chỉ các op hiện có; không thêm op hủy. Không sửa `SurveyFindingLookup` hoặc logic cổng decision.
- Copy-plan bổ sung điều kiện FK `withdrawn_by`: giữ ảnh đã hủy nếu người hủy có trong tập user được chép, loại nếu thiếu cha; không xóa dấu hủy hay bytes.
- Drift mới **PP-16…PP-19**, Mỹ SELF-SIGNED, nền tạm, consumer WP6 (Thịnh đề xuất) / WP5. Chưa gửi thông báo ra ngoài. Không sửa Contract văn bản.

## File đã sửa / thêm

- `src/LuxMap.Modules.WorkOrders/Entities/WorkOrderPolePhoto.cs`
- `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoVideo.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotosController.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderPoleVideosController.cs` (mô tả lỗi replay)
- `src/LuxMap.Persistence/Migrations/20261117113932_AddPolePhotoWithdrawal.cs`
- `src/LuxMap.Persistence/Migrations/20261117113932_AddPolePhotoWithdrawal.Designer.cs`
- `src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs`
- `tests/LuxMap.Api.Tests/WorkOrderPolePhotoWithdrawalTests.cs`
- `tests/LuxMap.Api.Tests/PolePhotoWithdrawalMigrationTests.cs`
- `scripts/copy_dev_to_supabase.py`
- `scripts/tests/test_pole_photo_copy_plan.py`
- `docs/contract-drift.md`
- `docs/openapi/tools/gen_consolidated_spec.py`
- `docs/openapi/luxmap-v1.json` (xuất từ code)
- `docs/openapi/luxmap-v1.5.json` (generator)
- `tracking.html`
- `.ai/results/POLE-PHOTO-WITHDRAW.md`

`.ai/tasks/POLE-PHOTO-WITHDRAW.md` có sẵn ở trạng thái untracked lúc bắt đầu; không sửa. Giữ nguyên symlink AGENTS.md.

## Migration — nguyên văn Up()/Down()

Đã đọc cả hai: Up chỉ thêm 2 cột nullable không default, index có tên tường minh, CHECK và FK Restrict; Down đối xứng, không xmin, không Drop/Alter ngoài phạm vi.
Không chạy `database update` hoặc apply/rollback trên DB. Rollback ứng dụng nên giữ schema/dấu hủy; Down làm mất dấu hủy và chỉ được kiểm thử ở DB test trống riêng.

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolePhotoWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "withdrawn_at",
                table: "work_order_pole_photo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "withdrawn_by",
                table: "work_order_pole_photo",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pole_photo_withdrawn_by",
                table: "work_order_pole_photo",
                column: "withdrawn_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pole_photo_withdrawal",
                table: "work_order_pole_photo",
                sql: "(withdrawn_at IS NULL AND withdrawn_by IS NULL) OR (withdrawn_at IS NOT NULL AND withdrawn_by IS NOT NULL AND decision IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_pole_photo_withdrawn_by",
                table: "work_order_pole_photo",
                column: "withdrawn_by",
                principalTable: "app_user",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_pole_photo_withdrawn_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropIndex(
                name: "ix_pole_photo_withdrawn_by",
                table: "work_order_pole_photo");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pole_photo_withdrawal",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                table: "work_order_pole_photo");

            migrationBuilder.DropColumn(
                name: "withdrawn_by",
                table: "work_order_pole_photo");
        }
    }
}
```

## Test mới / mở rộng

Tất cả test HTTP/DB mới ở `AssetDatabaseCollection` qua partial `WorkOrderTests` hiện có hoặc `PolePhotoWithdrawalMigrationTests`; không ghim ID.

`WorkOrderPolePhotoWithdrawalTests.cs` có 12 phương thức (theory mở rộng thành 21 case):

1. Hủy ảnh đã engineer-review → 204, row/AI/bytes giữ nguyên, list/total/lọc pole_id ẩn, mọi read/review/decision 404, audit đủ snapshot và upload capture mới cùng cột 201.
2. Retry trước/sau done không thay dấu hủy, đúng một audit; assignee khác vẫn 404.
3. Quyền: manager/superior/admin 403 `ROLE_FORBIDDEN`; engineer khác 404 `WORK_ORDER_NOT_FOUND`; anonymous 401.
4. Ngoài xã/khác parent/thứ tự membership trước status.
5. Confirm/reject cấm hủy; trạng thái done được kiểm trước decision.
6. Clip hủy được, Range/video 404, không ghi viewed, bytes giữ nguyên.
7. JPEG replay và MP4 dùng operation của JPEG đã hủy cùng trả 409 mới + photo_id, kể cả sau done, không thêm object/AI.
8. MP4 thật đã upload và hủy: replay cùng key/hash trả 409 mới.
9. Tranh chấp withdraw/withdraw, withdraw/review, withdraw/decision: đọc lại sau khóa; đúng một Deleted; kiểm thứ tự khóa.
10. CHECK từ chối thiếu từng trường withdrawal và row vừa withdrawal vừa decision.
11. Lỗi save rollback withdrawal/audit, ảnh vẫn đọc được.
12. Decision thắng khóa trước → withdrawal 409, không dấu hủy/audit Deleted.

`PolePhotoWithdrawalMigrationTests.cs`: migrate, kiểm nullable/FK Restrict/index/CHECK/model, down về migration liền trước rồi reapply trên container riêng.

Python copy-plan mở rộng test hiện có: giữ row withdrawn có cha được chép, loại row có withdrawer không được chép; thực thi predicate SQL sản xuất trên SQLite memory.

## Bằng chứng chạy thật

### Build / restore

`dotnet build --disable-build-servers` ban đầu treo im lặng trong sandbox, đã hủy. Một lượt chẩn đoán `--no-restore -m:1 /p:UseSharedCompilation=false` trả NETSDK1004 vì worktree mới chưa có project.assets.json; sau đó restore từ cache local, không đổi dependency:

```text
$ dotnet restore --source /Users/nhm809/.nuget/packages --disable-parallel --disable-build-servers -m:1
  Determining projects to restore...
CSSM_ModuleLoad(): One or more parameters passed to a function were not valid.
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/src/LuxMap.Modules.Identity/LuxMap.Modules.Identity.csproj (in 74 ms).
  ... (các project khác cũng Restored, exit 0)
```

Lượt build cuối sau khi có migration và toàn bộ test mới:

```text
$ dotnet build --disable-build-servers --no-restore -m:1 /p:UseSharedCompilation=false
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:25.10
```

### EF / OpenAPI

CLI khởi tạo host cần env. Không có `.env` trong worktree; wrapper tạm `/tmp/luxmap-withdraw-tools.py` nạp các tên biến đã khai trong `.env.example`, sinh giá trị ngẫu nhiên cho JWT/DB/MinIO, đặt `Cors__AllowedOrigins__0=https://localhost:3000`, `Swagger__Enabled=true`, `MSBUILDDISABLENODEREUSE=1`. Không lưu secret vào repo. Host ban đầu treo ở file watcher; các lệnh thành công chạy thêm **`DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`**. Không sửa code startup để lách sandbox.

```text
$ dotnet ef migrations add AddPolePhotoWithdrawal -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations --no-build --verbose
Writing migration to '/Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/src/LuxMap.Persistence/Migrations/20261117113932_AddPolePhotoWithdrawal.cs'.
Writing model snapshot to '/Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs'.
Done. To undo this action, use 'ef migrations remove'

$ dotnet ef migrations has-pending-model-changes -p src/LuxMap.Persistence -s src/LuxMap.Api --no-build
No changes have been made to the model since the last migration.

$ dotnet tool restore --add-source /Users/nhm809/.nuget/packages --ignore-failed-sources
Tool 'swashbuckle.aspnetcore.cli' (version '10.2.3') was restored. Available commands: swagger

Restore was successful.

$ dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/wt-pole-photo-withdraw/docs/openapi/luxmap-v1.json

$ python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=148 implemented_ops=191 not_implemented_ops=0 schemas=330
```

Python assertions đọc cả hai file JSON và kiểm DELETE không requestBody, 204 không content, đủ 204/404/409, đúng capability và role:

```text
export: DELETE has no body, 204/404/409, ExecuteWorkOrders, field_engineer only
consolidated: DELETE has no body, 204/404/409, ExecuteWorkOrders, field_engineer only
```

`npx --no-install @redocly/cli lint docs/openapi/luxmap-v1.5.json` không lấy được registry:

```text
npm error code ENOTFOUND
npm error syscall getaddrinfo
npm error errno ENOTFOUND
npm error network request to https://registry.npmjs.org/@redocly%2fcli failed, reason: getaddrinfo ENOTFOUND registry.npmjs.org
```

Bản `--offline` cũng ENOTCACHED. Dùng CLI có sẵn trên máy (cùng linter, không đổi dependency):

```text
$ node /Users/nhm809/.npm/_npx/1738445911c9ab26/node_modules/@redocly/cli/bin/cli.js lint docs/openapi/luxmap-v1.5.json
No configurations were provided -- using built in recommended configuration by default.
...
docs/openapi/luxmap-v1.5.json: validated in 87ms

Woohoo! Your API description is valid. 🎉
You have 10 warnings.
```

Exit 0. Đã lint cả spec `HEAD` trước thay đổi bằng cùng CLI: cũng **10 warning** (license, localhost, ref sibling, unused components), không có warning mới.

### Python / diff

```text
$ python3 -m unittest scripts.tests.test_pole_photo_copy_plan -v
test_excluded_photo_drops_its_fault_and_every_child_but_keeps_unrelated_faults (scripts.tests.test_pole_photo_copy_plan.PolePhotoCopyPlanTests.test_excluded_photo_drops_its_fault_and_every_child_but_keeps_unrelated_faults) ... ok

----------------------------------------------------------------------
Ran 1 test in 0.008s

OK

$ git diff --check
(no output, exit 0)
```

## Chưa kiểm chứng / bàn giao Claude

- **Không chạy bất kỳ test tích hợp nào**: sandbox không có Docker/Postgres, theo chỉ thị ticket. Test mới chỉ đã compile; chưa chứng minh hành vi HTTP/DB hoặc phá thử bằng runtime.
- `CapabilityPolicyCoverageTests` **chưa chạy**, không tuyên bố xanh. Exported spec xác nhận policy/role trên endpoint mới; không thay thế bộ test metadata đó.
- Chưa apply/Down/reapply migration trên DB thật, chưa chạy `dotnet ef database update`, chưa kiểm tay HTTP. SQL EF Up/Down đã xuất không cần DB; xem phần bổ sung bên dưới.
- Claude cần build lại rồi chạy ít nhất:

```bash
dotnet build --disable-build-servers
dotnet test --no-build --filter 'FullyQualifiedName~WorkOrderTests|FullyQualifiedName~PolePhotoWithdrawalMigrationTests|FullyQualifiedName~CapabilityPolicyCoverageTests'
python3 -m unittest scripts.tests.test_pole_photo_copy_plan -v
```

Sau đó chạy phá thử các chốt mới (lọc withdrawal, idempotent, CHECK/audit) theo trap testing; không chạy trên luxmap_dev.

## D-item

**Không phát sinh D-item kiến trúc/phạm vi cần Mỹ chốt thêm.** Quyết định đã chốt được ghi đúng tầng trong drift **PP-16…PP-19**.
Lưu ý triển khai đã ghi rõ ở PP-19: key của row đã hủy trả mã withdrawn trước hash/status, kể cả khi đổi loại media; membership DELETE và quyền vẫn kiểm theo ticket.

## SQL migration đã xuất (không kết nối DB)

```text
$ dotnet ef migrations script 20261116090002_IndexFeederTelemetryBindingAudit 20261117113932_AddPolePhotoWithdrawal -p src/LuxMap.Persistence -s src/LuxMap.Api --no-build -o /tmp/pole-photo-withdraw-up.sql
(no stdout, exit 0)
```

```sql
START TRANSACTION;
ALTER TABLE work_order_pole_photo ADD withdrawn_at timestamp with time zone;

ALTER TABLE work_order_pole_photo ADD withdrawn_by text;

CREATE INDEX ix_pole_photo_withdrawn_by ON work_order_pole_photo (withdrawn_by);

ALTER TABLE work_order_pole_photo ADD CONSTRAINT ck_pole_photo_withdrawal CHECK ((withdrawn_at IS NULL AND withdrawn_by IS NULL) OR (withdrawn_at IS NOT NULL AND withdrawn_by IS NOT NULL AND decision IS NULL));

ALTER TABLE work_order_pole_photo ADD CONSTRAINT fk_pole_photo_withdrawn_by FOREIGN KEY (withdrawn_by) REFERENCES app_user (user_id) ON DELETE RESTRICT;

INSERT INTO __ef_migrations_history (migration_id, product_version)
VALUES ('20261117113932_AddPolePhotoWithdrawal', '10.0.11');

COMMIT;

```

```text
$ dotnet ef migrations script 20261117113932_AddPolePhotoWithdrawal 20261116090002_IndexFeederTelemetryBindingAudit -p src/LuxMap.Persistence -s src/LuxMap.Api --no-build -o /tmp/pole-photo-withdraw-down.sql
(no stdout, exit 0)
```

```sql
START TRANSACTION;
ALTER TABLE work_order_pole_photo DROP CONSTRAINT fk_pole_photo_withdrawn_by;

DROP INDEX ix_pole_photo_withdrawn_by;

ALTER TABLE work_order_pole_photo DROP CONSTRAINT ck_pole_photo_withdrawal;

ALTER TABLE work_order_pole_photo DROP COLUMN withdrawn_at;

ALTER TABLE work_order_pole_photo DROP COLUMN withdrawn_by;

DELETE FROM __ef_migrations_history
WHERE migration_id = '20261117113932_AddPolePhotoWithdrawal';

COMMIT;

```


## Claude review (17/11/2026)

- Trả `ReviewAsync` về thứ tự cũ (`Writable` ngay sau khoá phiếu) — đổi thứ tự là ngoài phạm vi ticket.
- `ReplayAsync`: row chưa hủy giữ thứ tự cũ (hash khác → `IDEMPOTENCY_CONFLICT` trước khi kiểm phiếu); row đã hủy → `POLE_PHOTO_WITHDRAWN` sau kiểm phiếu, bất kể hash (PP-19 giữ nguyên).
- Chạy thật (PostGIS Testcontainers): 22/22 ca withdrawal + migration; `WorkOrder|PolePhoto|Capability|Migration|Sync` 517 pass / 2 skip; `python3 -m unittest tests.test_pole_photo_copy_plan` OK.
- Phá thử: `Photos()` bỏ lọc `WithdrawnAt` → 4/22 đỏ; khôi phục bằng `cp` + `touch` → 22/22 xanh.
- `Down()` đọc tay, đối xứng; chưa chạy down/reapply trên DB.
