# POLE-SEGMENT-NULL — kết quả PR 2a · 02/11/2026

## 1. Phạm vi và kết quả

Chỉ thực hiện **PR 2a** theo `.ai/tasks/POLE-SEGMENT-NULL-p2.md`, nền D-1…D-14 Mỹ chốt 02/11/2026.
Đã đọc AGENTS.md (vẫn symlink `CLAUDE.md`), `.ai/README.md`, sources/commands, Phase 1 mục 2/6/7 và D-12,
các trap migrations/assets/commune-scope/testing, ERD, TOPO-INFER và mẫu `PolePhotoMigrationTests`.

- `Pole.SegmentId` → `string?`, `Pole.Segment` → `RoadSegment?`; bỏ `.IsRequired()` ở cấu hình FK.
- EF sinh `20261102092632_PoleSegmentNullable` + designer/snapshot. `Up()` chỉ nới `pole.segment_id`.
  **Không drop/re-add FK**, không đổi index/commune/CHECK, không thêm FK ghép hay `segment_source`.
- EF ban đầu sinh `defaultValue: ""` ở `Down()`: đã bỏ hoàn toàn, thay bằng preflight đếm null → `RAISE EXCEPTION`
  nếu còn; sau đó raw `SET NOT NULL`. Cả hai lệnh không suppress transaction; `SET NOT NULL` là chốt cuối
  nếu có writer xen giữa. Không UPDATE/DELETE/default/backfill. Chưa apply migration lên bất kỳ DB nào.
- Bảy file consumer chỉ thêm toán tử null-forgiving `!` để triệt 14 warning nullable do thay entity; **không thêm nhánh,
  bộ lọc, giá trị fallback, không đổi DTO hay thuật toán**. Hành vi cho cột đã có tuyến giữ nguyên ở mã nguồn.
  Các vị trí này vẫn chưa hỗ trợ null; xem mục 4. Đây là khẳng định từ diff/model, **chưa xác minh HTTP runtime**.
- Thêm đúng hai test yêu cầu vào `PoleSegmentNullableTests`, cùng `AssetDatabaseCollection`, dùng `AssetImportFixture`
  kế thừa `LuxMapTestHost`; test rollback dùng container riêng theo mẫu có sẵn.
  Test đầu kiểm round-trip null + geometry/commune, filter khác xã, guard Added/Modified/Deleted khác xã và FK non-null sai.
  Test sau kiểm catalog nullable/no default, FK tên/Restrict/đích và index, refusal có thông điệp + giữ hàng/schema/history,
  gán tuyến thật rồi Down thành công và reapply. Không đổi kỳ vọng test cũ.
- Drift thêm đủ D-1…D-14, bốn trường Decision/Decision maker/Chạm API/Nền và **Chưa báo WP5/WP6/WP4**;
  ghi rõ chưa có hiệu lực với client tới PR 2e. ERD đổi `road_segment |o--o{ pole`, ghi quyết định.
- Không đổi Contract, OpenAPI, `tracking.html`, POST/PUT/import/filter/template/mock/script. Không stage/commit/push.
  Không chạy lệnh DB trên `luxmap_dev`, không `ef database update`, không khởi động API nghiệp vụ.

**✅ Verified:** build khôi phục cuối exit 0, 0 warning/0 error; kiểm offline model/snapshot và SQL sinh; diff không lỗi whitespace.
**⚠️ Could not verify:** toàn bộ test runtime, catalog PostGIS, Up/Down trên container, HTTP và phá thử assertion do sandbox chặn socket/Docker.
**📌 Remaining assumptions:** chưa có writer API nào tạo null trước PR 2e; chỉ test DbContext dựng null. Các consumer chưa hỗ trợ null không được bật sớm.

## 2. Migration đã đọc — nguyên văn Up()/Down() cuối cùng

Đã đọc cả bản EF sinh (phát hiện default rỗng) và bản cuối sau sửa; không apply. Nguyên văn hai phương thức dưới đây
được chép trực tiếp từ file migration, không viết lại từ trí nhớ:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "segment_id",
                table: "pole",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE unassigned_count bigint;
                BEGIN
                    SELECT count(*) INTO unassigned_count FROM pole WHERE segment_id IS NULL;
                    IF unassigned_count > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back PoleSegmentNullable: % pole(s) still have no segment. Assign verified segments first, or roll back the application and keep the nullable schema.', unassigned_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("ALTER TABLE pole ALTER COLUMN segment_id SET NOT NULL;");
        }
```

## 3. Bằng chứng lệnh chạy thật

### Build và môi trường

Lệnh build ban đầu không có tùy chọn serial bị kẹt trong sandbox, đã gửi Ctrl-C; lệnh serial dùng CLI home tạm
nhưng chưa chỉ cache NuGet thất bại DNS (exit 1). Trích output thật của lần đó:

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2a-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 MSBuildEnableWorkloadResolver=false dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false
```

```text
/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Citizen/LuxMap.Modules.Citizen.csproj : error NU1301:   nodename nor servname provided, or not known (api.nuget.org:443) [/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/LuxMap.slnx]
/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Citizen/LuxMap.Modules.Citizen.csproj : error NU1301:   nodename nor servname provided, or not known [/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/LuxMap.slnx]
/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj : error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json. [/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/LuxMap.slnx]
/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj : error NU1301:   nodename nor servname provided, or not known (api.nuget.org:443) [/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/LuxMap.slnx]
/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj : error NU1301:   nodename nor servname provided, or not known [/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/LuxMap.slnx]
    0 Warning(s)
    21 Error(s)

Time Elapsed 00:00:06.22
[exit=1]
```
Sau đó chỉ định cache `/Users/nhm809/.nuget/packages`, dùng `-p:NuGetAudit=false --ignore-failed-sources` để restore offline
(không sửa dependency/config của repo). Lần đầu compile thành công có 14 warning do nullable; sửa tối thiểu bằng `!`.
Sau phá thử đã khôi phục file bằng `copyfile` + cập nhật mtime, build lại đúng mã nguồn cuối, output đầy đủ:

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2a-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false dotnet build --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false
```

```text
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.54

[exit=0]
```

```bash
docker info --format '{{.ServerVersion}}'
```

```text

permission denied while trying to connect to the docker API at unix:///Users/nhm809/.docker/run/docker.sock

[exit=1]
```
### Sinh migration và kiểm offline — không kết nối DB

`dotnet-ef migrations add` bị kẹt ở wrapper/host, đã ngắt exit 130. Để sinh được model, dùng factory **tạm**
`PoleSchemaOfflineFactory : IDesignTimeDbContextFactory<LuxMapDbContext>` trong startup assembly: gọi đúng
`PersistenceServiceCollectionExtensions.Configure`, cùng 13 module của Program, scope rỗng, data source
`Host=127.0.0.1;Port=1;Database=pole_schema_offline;Username=offline`. Factory không gọi `Open`/migrate/query.
Gọi trực tiếp EF tool DLL tránh wrapper MSBuild; **đã xoá factory và build lại API**, không có factory trong diff hay binary cuối.
Output sinh migration thật (gồm nguyên lệnh trực tiếp):

```bash
python3 /tmp/pole-2a-ef-direct.py
```

```text
dotnet exec --depsfile /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.deps.json --runtimeconfig /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.runtimeconfig.json /Users/nhm809/.dotnet/tools/.store/dotnet-ef/10.0.11/dotnet-ef/10.0.11/tools/net8.0/any/tools/net8.0/any/ef.dll migrations add PoleSegmentNullable --assembly /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Persistence.dll --startup-assembly /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll --project src/LuxMap.Persistence/LuxMap.Persistence.csproj --project-dir /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence --output-dir Migrations --root-namespace LuxMap.Persistence --language C# --nullable --verbose
Using assembly 'LuxMap.Persistence'.
Using startup assembly 'LuxMap.Api'.
Using application base '/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0'.
Using working directory '/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a'.
Using root namespace 'LuxMap.Persistence'.
Using project directory '/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence'.
Remaining arguments: .
Finding DbContext classes...
Using environment 'Development'.
Finding IDesignTimeDbContextFactory implementations...
Found IDesignTimeDbContextFactory implementation 'PoleSchemaOfflineFactory'.
Found DbContext 'LuxMapDbContext'.
Finding DbContext classes in the project...
Using DbContext factory 'PoleSchemaOfflineFactory'.
Using context 'LuxMapDbContext'.
Finding design-time services referenced by assembly 'LuxMap.Api'...
Finding design-time services referenced by assembly 'LuxMap.Persistence'...
Using design-time services from assembly 'Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite'.
Finding design-time services for provider 'Npgsql.EntityFrameworkCore.PostgreSQL'...
Using design-time services from provider 'Npgsql.EntityFrameworkCore.PostgreSQL'.
Finding IDesignTimeServices implementations in assembly 'LuxMap.Api'...
No design-time services were found.
Writing migration to '/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence/Migrations/20261102092632_PoleSegmentNullable.cs'.
Writing model snapshot to '/Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs'.
'LuxMapDbContext' disposed.
Done. To undo this action, use 'ef migrations remove'
[exit=0]
```
Sau khi sửa Down và xoá factory, một console tạm ngoài repo (`/private/tmp/luxmap-pole-2a-check`) dựng cùng context/model
bằng 13 module, dùng `HasPendingModelChanges()`, metadata FK/index, reflection `[Required]` trên request, và
`IMigrationsSqlGenerator.Generate` cho `new PoleSegmentNullable` (không mở kết nối). Đây là **model/SQL sinh offline**, không phải catalog DB.
Console không nằm trong artifact bàn giao; không thêm project/dependency vào repo. Output thật:

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2a-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false dotnet run --project /private/tmp/luxmap-pole-2a-check/check.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:BuildInParallel=false
```

```text
Pole.SegmentId nullable: True
FK: fk_pole_road_segment_segment_id, required: False, delete: Restrict
Index: ix_pole_segment_id
Pending model changes: False
CreatePoleRequest.SegmentId [Required]: True
UpdatePoleRequest.SegmentId [Required]: True
Up SQL:
TransactionSuppressed: False
ALTER TABLE pole ALTER COLUMN segment_id DROP NOT NULL;

Down SQL:
TransactionSuppressed: False
DO $$
DECLARE unassigned_count bigint;
BEGIN
    SELECT count(*) INTO unassigned_count FROM pole WHERE segment_id IS NULL;
    IF unassigned_count > 0 THEN
        RAISE EXCEPTION 'Cannot roll back PoleSegmentNullable: % pole(s) still have no segment. Assign verified segments first, or roll back the application and keep the nullable schema.', unassigned_count;
    END IF;
END $$;

TransactionSuppressed: False
ALTER TABLE pole ALTER COLUMN segment_id SET NOT NULL;


[exit=0]
```
### Test toàn bộ — chưa test nào chạy được

VSTest bị `SocketException (13): Permission denied` khi mở listener, trước khi chạy test; không có số test pass.
Docker cũng bị chặn độc lập như output ở trên. Worktree không có `.env`; nếu vượt qua hạn chế socket/Docker thì Claude
cần cấu hình `.env` theo hướng dẫn repo trước khi chạy fixture. Không tạo `.env` hay dùng DB có sẵn để lách test host.
Output đầy đủ:

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2a-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false dotnet test --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false
```

```text
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.

[exit=1]
```
### Phá thử — có thử lệnh, CHƯA chứng minh test đỏ do chốt

Hai mutation được chạy tuần tự; mỗi lần sao lưu file, sửa, gọi test có build lại, rồi **khôi phục trong `finally` bằng
`shutil.copyfile` (tương đương `cp`) + `os.utime` (tương đương `touch`)**, không dùng `mv`.
Cả hai exit 1 vì VSTest socket, **không phải vì assertion**. Claude phải chạy lại cả baseline lẫn hai phá thử trên môi trường đủ quyền.

1. Khôi phục `.IsRequired()` tại `PoleConfiguration.SegmentId`.

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2a-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~Pole_without_segment_round_trips_and_stays_commune_scoped
```

```text
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.

[exit=1]
```
2. Vô hiệu preflight bằng đổi `IF unassigned_count > 0 THEN` → `IF FALSE THEN`, vẫn giữ SET NOT NULL.
   Test yêu cầu `P0001` + thông điệp riêng, nên lỗi NOT NULL thuần (`23502`) không thể được coi là refusal preflight đúng.

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2a-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~Nullable_pole_segment_schema_and_rollback_are_explicit
```

```text
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2a/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.

[exit=1]
```
## 4. Chỗ xử lý null để PR 2b–2e — KHÔNG hiện thực ở 2a

Danh sách dưới là phân tích tĩnh, kế thừa inventory đầy đủ của Phase 1 mục 1; không tuyên bố đã chạy endpoint với null.
Các dấu `!` chỉ phục vụ biên dịch, không phải lớp hỗ trợ null.

| File:dòng | Việc để PR sau |
|---|---|
| `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:79,81,107,134` | **2b**: chặn null==null ở Targets, nhóm chưa tuyến cuối, position null, thứ tự D-1 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:45,125` | **2b**: dùng chung Targets, recheck target dưới khoá, không nhận pole null không liên quan |
| `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:728,749` | **2b**: dời pole null không đo corridor và không dựng segment_distance giả (D-5) |
| `src/LuxMap.Modules.Faults/FaultReportService.cs:143,207` | **2b**: kiểm luồng sinh fault nullable giữ xã/vị trí/nguồn, D-7/D-14 |
| `src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:61`; `Review/SurveyReviewService.cs:307` cùng module | **2b**: giữ sự kiện/snapshot cũ khi tuyến pole đổi, không sửa fault/history cũ |
| `src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:87`; `src/LuxMap.Modules.Faults/Entities/FaultCluster.cs:39` | **2b**: giữ survey không target null; cluster từ chối null, không nới schema (D-8), lỗi D-14 |
| `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:410` | **2b**: citizen/accept + nullable pole target nghiệp vụ; DTO/read nullable phối hợp 2c |
| `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:125,879,966` | **2c**: inventory/topology lists và tên tuyến null thay fallback rỗng (D-6) |
| `src/LuxMap.Modules.Map/Features/MapQueryService.cs:155`; `PoleDetailService.cs:78,79` cùng thư mục | **2c**: giữ hàng map/detail và model đọc nullable, không đổi nhãn thành chuỗi giả |
| `src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:90,108,110` | **2c**: từ chối topology có pole null, trả pole_ids, không bịa cạnh (D-3) |
| `src/LuxMap.Modules.Citizen/PublicReportService.cs:31,71` | **2c**: PublicQrInfo.segment_name null, QR vẫn hợp lệ (D-6); bảo vệ nghiệp vụ citizen ở 2b |
| `src/LuxMap.Modules.Statistics/StatisticsService.cs:67,93,187,224` | **2c**: nhóm null và đếm riêng, warranty/CSV không mất hàng; phân biệt aggregate null (D-9) |
| `src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:666,667` | **2c**: lọc null khỏi affected_segment_ids, đếm cột chưa tuyến riêng, không tăng uncontrollable vì null (D-10) |
| `src/LuxMap.Modules.Sync/SyncBundleService.cs:51,112,122` | **2d**: union target chưa tuyến của phiếu mở được giao; metadata/cache/eviction phải có WP6 xác nhận (D-4) |
| `src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:109,314`; `AssetCrudService.cs:294,477` cùng thư mục | **2e**: nút bật POST/PUT optional theo D-2; hiện Required/lookup vẫn giữ |
| `src/LuxMap.Modules.Assets/Import/AssetImportService.cs:98,383,424,437` | **2e**: header optional, phân biệt blank new/existing, unknown ref vẫn lỗi; thêm unassigned_segment filter ở inventory |
| `scripts/photos_to_poles.py:797`; `mocks/`, `docs/templates/`, `README.md` | **2e**: D-11/template/hướng dẫn; giữ mock cũ, thêm ca riêng |
| `docs/openapi/` và generator | **2c**: sinh nullable read spec; **2e**: sinh request/filter spec tương ứng khi bật writer; 2a không sửa |

## 5. Soát diff và bàn giao

`git diff origin/dev --stat` có ba file Phase 1/task đã tồn tại trong HEAD trước lượt này; **không phải thay đổi 2a**.
Không xoá chúng để ép stat đúng checklist. Các file mới untracked không xuất hiện trong `git diff --stat`; được liệt kê qua status.

```bash
git diff origin/dev HEAD --stat
```

```text
 .ai/results/POLE-SEGMENT-NULL-p1.md | 3828 +++++++++++++++++++++++++++++++++++
 .ai/tasks/POLE-SEGMENT-NULL-p2.md   |   99 +
 .ai/tasks/POLE-SEGMENT-NULL.md      |  102 +
 3 files changed, 4029 insertions(+)

[exit=0]
```

```bash
git diff origin/dev --stat
```

```text
 .ai/results/POLE-SEGMENT-NULL-p1.md                | 3828 ++++++++++++++++++++
 .ai/tasks/POLE-SEGMENT-NULL-p2.md                  |   99 +
 .ai/tasks/POLE-SEGMENT-NULL.md                     |  102 +
 docs/contract-drift.md                             |   34 +
 docs/database/erd.md                               |   10 +-
 .../Configurations/AssetConfigurations.cs          |    2 +-
 src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs |    4 +-
 src/LuxMap.Modules.Assets/Entities/Pole.cs         |    4 +-
 src/LuxMap.Modules.Citizen/CitizenReviewService.cs |    2 +-
 .../Features/CabinetTopologyService.cs             |    2 +-
 src/LuxMap.Modules.Map/Features/MapQueryService.cs |    2 +-
 .../Features/PoleDetailService.cs                  |    2 +-
 .../Lighting/LightingCommandService.cs             |    2 +-
 src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs    |    2 +-
 .../Migrations/LuxMapDbContextModelSnapshot.cs     |    2 -
 15 files changed, 4082 insertions(+), 15 deletions(-)

[exit=0]
```

```bash
git status --short
```

```text
 M docs/contract-drift.md
 M docs/database/erd.md
 M src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs
 M src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs
 M src/LuxMap.Modules.Assets/Entities/Pole.cs
 M src/LuxMap.Modules.Citizen/CitizenReviewService.cs
 M src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs
 M src/LuxMap.Modules.Map/Features/MapQueryService.cs
 M src/LuxMap.Modules.Map/Features/PoleDetailService.cs
 M src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs
 M src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs
 M src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs
?? .ai/results/POLE-SEGMENT-NULL-p2a.md
?? src/LuxMap.Persistence/Migrations/20261102092632_PoleSegmentNullable.Designer.cs
?? src/LuxMap.Persistence/Migrations/20261102092632_PoleSegmentNullable.cs
?? tests/LuxMap.Api.Tests/PoleSegmentNullableTests.cs

[exit=0]
```

```bash
git diff --check
```

```text

[exit=0]
```

```bash
git diff HEAD --name-only -- docs/api-contract-v1.1.md docs/openapi tracking.html; git diff --cached --name-only
```

```text

[exit=0]

[exit=0]
```

## 6. D-item mới / điểm còn cần xác nhận

- **D-15 — bằng chứng runtime chưa đủ do môi trường:** Claude chạy lại build/test baseline, hai test PostGIS mới và hai phá thử,
  kiểm exit code thực tế trước khi approve/commit. Chưa có bằng chứng test xanh, chưa xác nhận catalog hay rollback runtime.
  Không đề nghị thay kiến trúc hay lách sandbox; D-1…D-14 giữ nguyên.
- **D-16 — checklist diff so với origin/dev chứa nền Phase 1:** output `git diff origin/dev HEAD --stat` chứng minh ba file `.ai/`
  có sẵn trong commit nền. Claude xác nhận phạm vi PR lúc dựng diff vào dev; lượt này không tự sửa/xoá lịch sử hay stage chúng.

Không có quyết định kiến trúc mới. Không stage, không commit, không push; `tracking.html` để Claude cập nhật sau review theo task.
