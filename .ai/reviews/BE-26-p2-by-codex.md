# BE-26 Phase 2 — review bởi Codex, 14/10/2026

**Kết luận: cần sửa 1 P1 và 1 P2 trước khi duyệt.** Không thấy lỗi nghiệp vụ có đủ bằng chứng trong hai job. Không sửa `src/` hay `tests/`.

Phạm vi: `git diff origin/dev...HEAD`, merge-base `40694ff`, hai commit `e0b83dc` và `0336624`; HEAD khi review là `0336624` (detached). Đã đọc `AGENTS.md` (đặc biệt BE-26), `.ai/context/sources.md`, `.ai/tasks/BE-26.md`, `.ai/context/commands.md`, `.ai/README.md`, quyết định D-1…D-9 ở `.ai/results/BE-26-p1.md`, kết quả P2 và drift JB-1…JB-8. Review dùng hai trục Standards/Spec; kết luận bên dưới phân biệt lỗi logic/kiểm chứng với nghiệp vụ.

## Phát hiện — logic và kiểm chứng (Standards)

### F-1 · P1 — DB lỗi ở bước khởi tạo làm worker thoát và host dừng, không có lượt retry

- **Vị trí:** `src/LuxMap.Persistence/Jobs/ScheduledJobRunner.cs:103` (lời gọi `EnsureAsync` trước `try` ở dòng 107).
- **Kịch bản:** API khởi động khi Postgres đang restart hoặc kết nối DB lỗi tạm thời. `EnsureAsync` ném `NpgsqlException`; exception thoát khỏi `ExecuteAsync`, nên không tới nhánh log/retry mỗi poll. Với mặc định `BackgroundServiceExceptionBehavior.StopHost`, lỗi này dừng cả host. Nếu đổi sang `Ignore` thì worker vẫn đã chết, DB hồi phục cũng không có job nào chạy nữa. Đây khác với lỗi `ClaimAsync`/`FailAsync` sau khi đã khởi tạo: các lỗi đó có catch ngoài vòng chạy.
- **Bằng chứng chạy:** chương trình độc lập trong `/private/tmp/be26-review-worker/` tham chiếu project Persistence hiện tại, dựng **chính `ScheduledJobRunner` và `ScheduledJobWorker`**, một job giả và Generic Host mặc định. Data source chỉ trỏ tới Unix socket không tồn tại `/private/tmp/be26-db-does-not-exist`, không chạm DB dev. Kết quả:

  ```text
  ExecuteTask.IsFaulted=True
  Exception=NpgsqlException
  ApplicationStopping=True
  Default behavior=StopHost
  ```

  Luồng host bắt task nền fault rồi gọi `StopApplication` cũng khớp [mã nguồn Generic Host](https://github.com/dotnet/runtime/blob/v10.0.10/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs).
- **Đề xuất:** đưa bước ensure vào vùng retry của worker, và chỉ đánh dấu đã ensure sau khi INSERT thành công. Giữ `ON CONFLICT DO NOTHING`; sau lỗi chờ poll rồi ensure lại trước khi claim. Không chỉ chuyển `EnsureAsync` vào một catch rồi bỏ qua nó: DB chưa có các dòng job thì claim sẽ mãi không có gì.
- **Kiểm chứng sau sửa:** test lỗi lần ensure đầu, lần kế thành công; host vẫn chạy và job được thực thi. Tránh chỉ test lỗi từ `IScheduledJob.RunAsync`, vì test hiện có không đi qua lỗi khởi tạo này.

### F-2 · P2 — test worker có race giữa tín hiệu job thành công và UPDATE hoàn tất lease

- **Vị trí:** `tests/LuxMap.Api.Tests/ScheduledJobTests.cs:167` / `:171`; tín hiệu phát ở `:481`, trong khi runner hoàn tất ở `src/LuxMap.Persistence/Jobs/ScheduledJobRunner.cs:133`.
- **Kịch bản:** `FlakyJob` lần hai gọi `SecondRun.TrySetResult()` rồi trả về. Test tỉnh dậy và gọi `worker.StopAsync` ngay. Worker chưa chắc đã hoàn thành `CompleteAsync`; `StopAsync` hủy cùng token mà UPDATE sử dụng. Nếu hủy trước UPDATE commit, worker để lease hết hạn theo thiết kế, còn dòng DB vẫn có `run_count = 0` và `last_error` từ lần thất bại đầu. Assertion ở dòng 179–180 đỏ ngẫu nhiên dù logic job đúng. `RunContinuationsAsynchronously` không tạo quan hệ thứ tự giữa continuation của test và UPDATE DB.
- **Bằng chứng:** thứ tự lời gọi và token trong code; **chưa tái hiện trên DB** vì sandbox chặn VSTest/Docker. Không khẳng định test đã đỏ ở lượt review này.
- **Đề xuất:** sau tín hiệu lần chạy thứ hai, đợi có timeout cho dòng `scheduled_job` đạt `RunCount == 1`, `LeaseToken == null`, `LastError == null`, rồi mới dừng worker; hoặc phát tín hiệu sau runner complete nếu có seam phù hợp. Không dùng một delay cố định.

## Nghiệp vụ (Spec)

Không có phát hiện P0/P1/P2/P3 có đủ bằng chứng về việc hiện thực sai D-3…D-8. Giới hạn sync và việc thông báo WP6 chưa làm được ghi riêng bên dưới; không tự thay quyết định đã chốt.

## Đã kiểm, không thấy lỗi

### 1. Claim, fence, lỗi DB và hủy worker

- `ScheduledJobRunner.cs:50–63`: claim là một statement, autocommit. Subquery lấy row lock trước khi trả `job_name`, và giữ lock đến hết UPDATE/transaction. Instance thứ hai gặp lock đang giữ thì `SKIP LOCKED`; nếu lấy được phiên bản row sau khi instance trước commit, `SELECT FOR UPDATE` ở READ COMMITTED kiểm lại điều kiện lease trên phiên bản mới. Vì vậy outer WHERE chỉ so tên **không tự tạo ra một lần claim thứ hai** khi lease còn hiệu lực. Cơ chế này khớp [PostgreSQL: READ COMMITTED](https://www.postgresql.org/docs/17/transaction-iso.html) và [locking clause / SKIP LOCKED](https://www.postgresql.org/docs/17/sql-select.html). Đây là kết luận từ code và tài liệu chính thức; chưa chạy tranh chấp PostgreSQL trong lượt review này.
- `ScheduledJobRunner.cs:75–86`: Complete và Fail đều có `job_name AND lease_token`; worker cũ không thể ghi đè token của lượt takeover. Lease hết hạn nhưng chưa ai takeover thì token cũ vẫn được finish, phù hợp fence theo token. Fence bảo vệ sổ lịch; unique nghiệp vụ bảo vệ các lượt job chạy chồng nhau.
- `ScheduledJobRunner.cs:140–143`: Fail dùng command mới từ data source, không tái sử dụng connection đã bị lỗi của job. Nếu DB vẫn hỏng khiến Fail cũng ném lỗi, catch ở dòng 118 bắt, log và poll lại; lease còn đến hạn rồi được thu hồi. `last_error` không ghi được lúc DB unavailable là giới hạn tự nhiên, không phải một lỗi retry mới. **F-1 là ngoại lệ ở bước ensure.**
- `ScheduledJobRunner.cs:136–138`: hủy khi dừng để lease treo tới hạn là chủ ý có thể chấp nhận theo D-2. Mặc định chậm tối đa phần lease còn lại (~300 giây) cộng poll; các lượt đã ghi một phần được chạy bù nhờ unique. Không cần giải phóng lease bằng token đã hủy.

### 2. WorkOrderOverdueJob / D-3, D-4

- `WorkOrderOverdueJob.cs:28–40`: `due_date < NightOf(now)`, đúng ngày đêm của agenda; chỉ `open`, `assigned`, `in_progress`; null không trễ; `done` không báo; không lọc `task_kind`. Scan và recheck dùng cùng `OverdueOn`.
- `WorkOrderOverdueJob.cs:74–88`: đọc lại qua context đúng xã; ghi dấu `(work_order_id, due_date)` và thông báo trong một SaveChanges; bắt chính `pk_work_order_overdue_notice`. Transaction SaveChanges rollback toàn bộ bên thua, nên không có thông báo dư. Không track/ghi dòng WorkOrder, không đổi `updated_at`/version hay tạo audit giả.
- `WorkOrderOverdueJob.cs:79–81`, `Notifier.cs:36–49,61–74`: người được giao + các Quản lý active giữ xã. `Stage` bỏ null và distinct ordinal; assignee đồng thời là manager chỉ nhận một lần; actor null không loại mất người nhận. D-3 không yêu cầu lọc active cho chính assignee.
- `LuxMapDbContext.cs:35–36`, `CommuneScopeBuilderExtensions.cs:106–107`: không actor ⇒ `CurrentAssigneeRestriction == null`; filter không che phiếu đã giao. Scope xã của `JobContextFactory.Open` vẫn có hiệu lực.
- `WorkOrderOverdueJob.cs:42–53,64–65`: trên tập ứng viên hữu hạn, mỗi lượt báo thành công loại khóa đó khỏi scan tiếp; một lô đầy mà không thành công thì dừng. Không thấy vòng lặp vô hạn trên dữ liệu không đổi. Khi bên khác đã xử lý lô hoặc ứng viên đổi, vòng này có thể dừng sớm và lượt lịch sau bù; không phải xử lý mãi một lô không tiến triển.

### 3. NodeOfflineJob / D-5…D-8 và các đường hiện có

- `NodeOfflineJob.cs:43–74`: `< cutoff` khớp `IotOptions.StatusAt`, tại đúng ngưỡng còn online; null last report không sinh fault. Recheck so last report sau scan. `detected_at = last_report_at + OfflineAfter` ổn định giữa instance/lượt chạy. Kiểm tồn tại không lọc `fault_status`: rejected/resolved/verified không bị sinh lại trong cùng đợt; last report mới tạo đợt mới. Không có code tự đóng fault.
- `NodeOfflineJob.cs:77–109`: đúng tọa độ tủ, nguồn dữ liệu thiết bị, `iot`, `medium`, `detected`, null pole/fixture/segment; audit actor `iot`. Mỗi SaveChanges chỉ có một fault, một audit event và các notification. Catch đúng `ux_fault_origin_node_id_detected_at`; finally clear tracker để không giữ audit cũ cho node tiếp.
- `IotConfigurations.cs:41–44`: FK ghép cabinet + commune buộc thiết bị cùng xã với tủ. `IgnoreQueryFilters` chỉ đọc `Geom` theo cabinet đã liên kết; không trả bản ghi tủ hay dữ liệu xã khác ra ngoài.
- `FaultOriginNodeConfiguration.cs:19–33`: FK ghép node + commune đúng. CHECK có vế `origin_node_id IS NULL`, nên không chặn các writer cũ: `FaultReportService.cs:158–164,205–213`, `Review/SurveyReviewService.cs` (new Fault), và Citizen qua `FaultReportService` đều không đặt OriginNodeId. Patch chỉ đổi override cho lamp; không đổi loại gốc node_offline để thoát unique partial.
- `FaultQueryService.cs:109–155`: GET list/detail dùng left join pole và tọa độ riêng của fault; null pole/segment không bị dereference. `origin_node_id` không emit, đúng JB-7. Số liệu tình trạng bóng dựa trên pole/current status; thống kê timeliness dựa trên WorkOrder, không đòi fault phải có cột.
- `WorkOrderService.cs:240–285`: tạo phiếu từ fault cùng xã cho phép segment null, không cần pole. Với phiếu repair vẫn phải xác nhận fault trước theo luật hiện có; job sinh `detected` là đúng. Detail dùng left join; `WorkOrderPoles.cs:91` lọc pole null, phiếu chỉ node_offline có danh sách cột rỗng hợp lệ.

### 4. Migration và index

- `20261014065813_AddScheduledJobs.cs`: Up chỉ thêm cột nullable, hai bảng, ràng buộc/index/FK và mở rộng CHECK notification; không có DML sửa/xóa dữ liệu, không drop index cũ. OriginNodeId mới null trên dữ liệu cũ nên CHECK/FK không loại bản ghi hiện có.
- Index FK đầy đủ `(origin_node_id, commune_id)` tồn tại riêng cạnh unique partial `(origin_node_id, detected_at) WHERE fault_type = 'node_offline'`; bảng dấu có index FK ghép và commune riêng. Scheduled table chỉ có hai job hiện tại, không có nhu cầu index quét lớn đã chứng minh.
- Down chỉ tháo phần thêm; tạo lại CHECK notification sẽ thất bại nếu còn type `work_order_overdue`, đúng hạn chế **đã ghi trong AGENTS**. Không coi đây là phát hiện mới hay tự chạy rollback có mất bảng dấu/nguồn node. Chưa apply Up/Down thực tế trong lượt này.

### 5. Test, teardown và đồng hồ 2001

- `ScheduledJobTests.cs:73–95`: xóa audit/notification/fault/dấu quá hạn trước work order/node/cabinet/user/commune; các test này không tạo link WorkOrderFault. Audit purge dùng SET LOCAL trong transaction; job giả được xóa theo tên riêng.
- Đã grep thật `DueDate`, `LastReportAt`, `due_date`, `last_report_at` trong `tests/`, `scripts/`, `mocks/` và đọc các nguồn ngày liên quan. Không thấy dữ liệu ứng viên của class khác trước `2001-03-10`. Mock due dates là `2026-08-24/25/28`, last reports `2026-08-20`; test khác dùng 2026/giờ hiện tại/null. Vì vậy đồng hồ 2001 cô lập **với dữ liệu hiện có**, không phải một đảm bảo cho fixture mới trong tương lai.
- Các assert dùng literal cho trạng thái, người nhận, ngưỡng ngày, ngưỡng ±1 microsecond, event/audit và dấu hạn; không thấy xanh giả rõ ràng do chỉ so tập ở chỗ cần thứ tự hay do test tự tính cùng công thức với job. Test worker có **F-2**.
- Các ca đồng thời dùng `Task.WhenAll` nhưng không có barrier bảo đảm hai bên cùng tới INSERT; kết quả xanh một lần chưa chứng minh catch tranh chấp đã được đi qua. P2 ghi nhận phá thử bỏ catch từng làm đỏ; lượt review này không chạy lại được để xác nhận. Chưa có ca batch ≥500 hay lỗi ensure DB; không suy diễn những nhánh đó đã được test.

### 6. Jobs:Enabled=false trên DeferredHostBuilder

- Đọc đúng [DeferredHostBuilder v10.0.11](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Mvc/Mvc.Testing/src/DeferredHostBuilder.cs): `ConfigureHostConfiguration` áp callback ngay vào `_hostConfiguration`; `Build` biến config thành args trước khi gọi entry point. [WebApplicationFactory cùng version](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Mvc/Mvc.Testing/src/WebApplicationFactory.cs) sử dụng builder này cho entry point minimal hosting. `Program.cs:35` thực sự gọi `WebApplication.CreateBuilder(args)`, trước bind options ở dòng 87.
- Thử nghiệm `/private/tmp/be26-review-host/` load **DLL Mvc.Testing 10.0.11 trong cache**, tạo DeferredHostBuilder bằng reflection, áp chính callback của LuxMapTestHost, rồi đặt biến môi trường `Jobs__Enabled=true`. Factory nhận args và gọi WebApplication.CreateBuilder; output:

  ```text
  args=--Jobs= --Jobs:Enabled=false
  builder.Configuration[Jobs:Enabled]=false
  ```

  Chạy với `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` để tránh watcher bị treo trong sandbox. Không thay provider của Jobs. Kết luận: cách ép config tới builder.Configuration **có tác dụng trên đường host hiện tại**; không cần đợi assertion sau Start mới biết worker bị tắt. Chưa dựng toàn bộ LuxMapTestHost vì test DB không truy cập được.

## Giới hạn nghiệp vụ / việc còn lại

- **Sync bundle không chứa phiếu chỉ sửa node_offline.** `SyncBundleService.cs:109–112,125–128` chỉ chọn phiếu chạm tuyến; fault list cũng lọc theo tuyến/cột ở `FaultQueryService.cs:103–104`. Phiếu chỉ node_offline có cả pole và segment null thì không xuất hiện, kể cả khi caller gửi tuyến tường minh. Không gây 500 và phù hợp Contract §5.8 hiện tại; muốn kỹ sư xử lý offline tại tủ cần quyết định riêng về phạm vi bundle. Không tự gán segment trái D-7. Luồng đọc/tạo phiếu online vẫn hợp lệ.
- D-9/JB-8 và việc báo type notification mới cho WP5/WP6 vẫn chưa hoàn tất theo drift/P2; chưa gửi thông báo từ phiên review này.
- Dữ liệu demo đang cũ sẽ được job báo quá hạn/offline ở lượt đầu như P2 đã nêu; đó là hành vi đúng quyết định, không phải seed test bị job này làm nhiễu khi dùng năm 2001.

## Kiểm chứng đã chạy

✅ Build code và assembly test thành công, không sửa package hay source:

```text
dotnet build -m:1 -nodeReuse:false -p:NuGetAudit=false -p:BuildInParallel=false --ignore-failed-sources
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:19.85
```

NuGet audit **tắt riêng cho lệnh này** để restore từ cache trong môi trường hạn chế mạng; không chứng nhận advisory đã hết. Hai probe ở trên chạy bằng SDK 10.0.302/runtime 10.0.10.

⚠️ **Không chạy được ScheduledJobTests hay full suite**. Lệnh đã thử:

```text
dotnet test tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ScheduledJobTests' -m:1 -nodeReuse:false
System.Net.Sockets.SocketException (13): Permission denied
... Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start ...
Test Run Aborted.
```

VSTest bị chặn bind socket trước khi chạy test. `docker info --format '{{.ServerVersion}}'` cũng bị permission denied tại Docker socket. Thử initdb của PostgreSQL cài sẵn trong `/private/tmp` bị `shmget: Operation not permitted`; không dùng DB dev để thay thế. Vì vậy không xác nhận lại con số 14/14 của tác giả, race SQL, race INSERT hay migration trên DB trong lượt này.

📌 Giả định còn lại: giữ D-1…D-9 đã chốt và phạm vi sync theo Contract hiện tại. Cần sửa F-1/F-2, rồi chạy lại test trên môi trường cho phép Docker/socket trước khi duyệt.

Đã ghi báo cáo và cập nhật `tracking.html`. `git diff --check` qua; không có thay đổi tracked trong `src/` hay `tests/`. Chưa commit được: `git add` bị sandbox từ chối tạo `/Users/nhm809/Documents/LuxMap/luxmap_backend/.git/worktrees/luxmap_backend-codex-be26/index.lock` (`Operation not permitted`). Không push.
