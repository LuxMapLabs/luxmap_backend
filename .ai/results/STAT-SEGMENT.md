# STAT-SEGMENT — phase 2, 06/11/2026

Branch: `feat/STAT-segment-repair-timeliness`. User giao quyền ghi worktree; không push.

## Đã triển khai

- Controller nhận `segment_id`, chuẩn hóa whitespace/trim như fixture-status; cập nhật remarks.
- `RepairTimelinessQuery.SegmentId`, lọc đúng một chỗ trên `repairs` trước hai query finished/overdue.
- Hai ca theory (có/không group commune) kiểm số cụ thể trên hai tuyến, NULL, thiếu/rỗng/whitespace và trim; một fact kiểm tuyến trống, không tồn tại, ngoài scope (có đối chứng caller xã đó).
- ST-11 ghi đúng quyết định Mỹ duyệt 06/11, cộng thêm / SELF-SIGNED. Sinh lại hai spec; tracking ghi giới hạn kiểm chứng.
- Không schema/migration/dependency/enum/shape response mới; không đổi `group_by`.

## Kiểm chứng thực tế

**Phần dưới là lượt chạy của Codex trong sandbox (không chạy được test). Claude đã chạy lại ngoài sandbox — xem mục "Kiểm chứng ngoài sandbox (Claude, 06/11/2026)" ở cuối.** Lượt sandbox:

**Chưa đạt toàn bộ tiêu chí nghiệm thu.** Build biên dịch được nhưng test runner bị sandbox chặn socket; chưa test nào chạy và không thể khẳng định test xanh hay phá thử đỏ đúng assertion. Docker socket cũng bị từ chối. Không chạy test trên `luxmap_dev`.

Lượt đầu `dotnet build --disable-build-servers` và test mặc định vướng MSBuild named pipe; chuyển `-m:1 -p:UseSharedCompilation=false` (giữ `--disable-build-servers`). Không đổi cấu hình repo để né sandbox.

### Build ban đầu, một tiến trình

```sh
dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false
```

Exit code: `0`.

```text
/Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Shared.Tests/LuxMap.Shared.Tests.csproj : warning NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json.
    42 Warning(s)
    0 Error(s)

Time Elapsed 00:00:27.86
```

### Toàn bộ suite

```sh
dotnet test --disable-build-servers -m:1 --no-restore -p:UseSharedCompilation=false
```

Exit code: `1`.

```text
Test Run Aborted.
/Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Shared.Tests/LuxMap.Shared.Tests.csproj : warning NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json.
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
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
```

### Phá thử hai nhánh (không chứng minh được do môi trường)

Backup: `cp src/LuxMap.Modules.Statistics/StatisticsService.cs /tmp/stat-segment-service.cs`.

1. Bỏ filter chung, chỉ thêm `.Where(w => query.SegmentId == null || w.SegmentId == query.SegmentId)` vào `finished`: `overdue` không lọc.
2. Khôi phục bằng `cp`, rồi bỏ filter chung và chỉ thêm biểu thức trên vào `overdue`: `finished` không lọc.
3. Mỗi lượt đều khôi phục bằng `cp /tmp/stat-segment-service.cs src/LuxMap.Modules.Statistics/StatisticsService.cs` trước bước tiếp theo.

### Bỏ lọc overdue

```sh
dotnet test tests/LuxMap.Api.Tests --disable-build-servers -m:1 --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~Repair_segment
```

Exit code: `1`.

```text
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
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
```

### Bỏ lọc finished

```sh
dotnet test tests/LuxMap.Api.Tests --disable-build-servers -m:1 --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~Repair_segment
```

Exit code: `1`.

```text
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
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
```

### Build lại sau cp khôi phục

```sh
dotnet build --disable-build-servers -m:1 --no-restore -p:UseSharedCompilation=false
```

Exit code: `0`.

```text
/Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Shared.Tests/LuxMap.Shared.Tests.csproj : warning NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json.
    21 Warning(s)
    0 Error(s)

Time Elapsed 00:00:07.23
```

### Chạy lại test mới sau khôi phục

```sh
dotnet test tests/LuxMap.Api.Tests --disable-build-servers -m:1 --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~Repair_segment
```

Exit code: `1`.

```text
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-stat-segment/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
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
```

`cmp src/LuxMap.Modules.Statistics/StatisticsService.cs /tmp/stat-segment-service.cs`: exit 0, không output (bản đúng đã khôi phục).

### OpenAPI

`cp .env.example .env` để CLI dựng host, file ignored, không commit. CLI bị treo khi bật watcher cấu hình; chạy lại với `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` thành công. Không khởi chạy API hay seed/migrate dev.

### Tool restore

```sh
dotnet tool restore
```

Exit code: `0`.

```text
Tool 'swashbuckle.aspnetcore.cli' (version '10.2.3') was restored. Available commands: swagger

Restore was successful.
```

### Xuất spec

```sh
DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
```

Exit code: `0`.

```text
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/wt-stat-segment/docs/openapi/luxmap-v1.json
```

### Sinh spec hợp nhất

```sh
python3 docs/openapi/tools/gen_consolidated_spec.py
```

Exit code: `0`.

```text
wrote docs/openapi/luxmap-v1.5.json: paths=112 implemented_ops=143 not_implemented_ops=0 schemas=273
```

### npx offline không tìm được cache metadata

```sh
npx --offline @redocly/cli lint docs/openapi/luxmap-v1.5.json
```

Exit code: `1`.

```text
npm error code ENOTCACHED
npm error request to https://registry.npmjs.org/@redocly%2fcli failed: cache mode is 'only-if-cached' but no cached response is available.
npm error Log files were not written due to an error writing to the directory: /Users/nhm809/.npm/_logs
npm error You can rerun the command with `--loglevel=verbose` to see the logs in your terminal
```

### Lint bằng CLI có sẵn trong cache

```sh
node /Users/nhm809/.npm/_npx/dfb62dab4341508f/node_modules/@redocly/cli/bin/cli.js lint docs/openapi/luxmap-v1.5.json
```

Exit code: `0`.

```text

Reference: https://redocly.com/docs/cli/rules/oas/no-unused-components


docs/openapi/luxmap-v1.5.json: validated in 78ms

Woohoo! Your API description is valid. 🎉
You have 10 warnings.

```

Cùng CLI lint bản trước thay đổi: exit 0, 10 warnings. Bản mới: exit 0, 10 warnings (không warning mới). Diff hai spec chỉ thêm `segment_id` vào operation repair-timeliness.

## Diff / commit

`git diff --check`: exit 0, không output. Ticket `.ai/tasks/STAT-SEGMENT.md` đã untracked trước khi bắt đầu, giữ nguyên và không stage theo whitelist của ticket.

## D-item

Không có quyết định sản phẩm/kiến trúc còn mơ hồ. Chưa thể xác minh runtime, test xanh/phá thử đỏ trong môi trường này; cần chạy lại ngoài sandbox có VSTest socket và Docker.

## Git bị chặn bởi sandbox

Đã review diff code/test/drift/spec; `git diff --check` exit 0.

```sh
git add src/LuxMap.Modules.Statistics/StatisticsController.cs src/LuxMap.Modules.Statistics/StatisticsService.cs tests/LuxMap.Api.Tests/StatisticsTests.cs docs/contract-drift.md docs/openapi/luxmap-v1.json docs/openapi/luxmap-v1.5.json tracking.html .ai/results/STAT-SEGMENT.md
git commit -m 'feat(statistics): filter repair timeliness by segment'
```

Cả hai lệnh exit 128, output thật giống nhau:

```text
fatal: Unable to create '/Users/nhm809/Documents/LuxMap/luxmap_backend/.git/worktrees/wt-stat-segment/index.lock': Operation not permitted
```

Git metadata nằm ngoài writable roots. Không stage/commit được; không đổi branch, không push. Không có commit hash để bàn giao. Cần môi trường cho phép ghi Git metadata để hoàn thành commit sau khi chạy lại kiểm thử.

## Kiểm chứng ngoài sandbox (Claude, 06/11/2026)

Review diff: đúng đề bài; số kỳ vọng trong hai test tính tay khớp (tuyến chính 3/1/1/1, quá hạn 1, rate 0.5; tuyến khác 1/1/0/0, quá hạn 1, rate 1; không lọc 5/2/2/1, quá hạn 3). Đổi dấu nháy cong trong XML-doc thành nháy thẳng.

```sh
dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter "FullyQualifiedName~StatisticsTests"
```
```text
Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18
```

Phá thử — xoá dòng `if (query.SegmentId is { } segment) repairs = …` (một bộ lọc dùng chung cho `finished` và `overdue`, nên phá một chỗ là phá cả hai):
```sh
dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter "FullyQualifiedName~Repair_segment"   # exit 1
```
```text
Failed!  - Failed:     3, Passed:     0, Skipped:     0, Total:     3
```
Khôi phục bằng `cp` từ bản sao; `git diff --stat` của `StatisticsService.cs` trở lại đúng 2 dòng thêm.

Toàn bộ suite sau khi sửa XML-doc:
```sh
dotnet test --disable-build-servers     # exit 0
```
```text
Passed!  - Failed:     0, Passed:   386, Skipped:     0, Total:   386 - LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45 - LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    80, Skipped:     0, Total:    80 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:  1380, Skipped:     7, Total:  1387 - LuxMap.Api.Tests.dll
```

⚠️ Lint OpenAPI (exit 0, 10 warning nền) là số của Codex; Claude không chạy lại.
