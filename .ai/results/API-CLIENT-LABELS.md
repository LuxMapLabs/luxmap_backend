# API-CLIENT-LABELS — kết quả thực thi

Ngày: 02/10/2026. Nhánh: `feat/api-client-labels`. Agent: Codex.

Đã hiện thực phạm vi code; **chưa đủ tiêu chí test xanh** vì sandbox chặn socket của VSTest.
Không commit/push. Không đọc `.env`, không kết nối DB/Docker, không chạy test cần DB,
không xuất OpenAPI JSON, không thêm package, không sửa Contract/mock.

## Thay đổi

- `src/LuxMap.Shared/Http/ClientSurfaceAttribute.cs`: attribute cho class/action, enum `Mobile | Web`;
  không đánh dấu thì dùng chung. Đây chỉ là metadata tài liệu, không đổi phân quyền.
- `src/LuxMap.Modules.Identity/Auth/AuthController.cs`: gắn mobile cho login/register/refresh/logout;
  `GET /auth/me` không gắn.
- `src/LuxMap.Modules.Identity/Auth/Web/WebAuthController.cs`: gắn web trên controller.
- `src/LuxMap.Api/OpenApi/ClientSurfaceOperationFilter.cs`: mọi operation có `x-luxmap-client`;
  chỉ mobile/web và `GET /auth/me` có tiền tố summary. Summary chưa có thì dùng tên method sẵn có.
- `src/LuxMap.Api/OpenApi/SwaggerSetup.cs`: đăng ký filter cạnh capability filter, thêm hướng dẫn
  endpoint dùng chung xác thực Bearer. Code hiện không khai mô tả tag riêng.
- `docs/openapi/tools/gen_consolidated_spec.py`: sửa đúng mô tả hai tag, thêm hướng dẫn dùng chung;
  đọc extension để giữ tiền tố khi ghi đè summary viết tay. Giữ nguyên thuật toán operationId/tag.
- `tests/LuxMap.Api.Tests/OpenApiSpecTests.cs`: thêm 10 ca (2 Fact + 8 InlineData), kỳ vọng literal:
  mọi operation có extension; 4 mobile, 3 web, `/auth/me` shared; `/poles` shared không thêm tiền tố;
  tag Auth/WebAuth giữ nguyên.
- `tracking.html`: ghi trạng thái và phần xác minh còn chờ.

## Bằng chứng kiểm tra

### Build

Lượt `dotnet build -warnaserror` mặc định không trả output trong sandbox. Tắt build server, dùng
một MSBuild node thì chạy được nhưng restore audit báo:

```text
error NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json.
Build FAILED.
    0 Warning(s)
    16 Error(s)
```

Lượt xác nhận dùng package đã có, tắt audit **chỉ qua tham số lệnh**, không sửa project/config:

```sh
DOTNET_EnableDiagnostics=0 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 MSBUILDDISABLENODEREUSE=1 \
  dotnet build -warnaserror --disable-build-servers -m:1 \
  -p:UseSharedCompilation=false -p:NuGetAudit=false
```

Output thật (log `/tmp/api-client-labels-verified-build.log`):

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:12.18
```

✅ Toàn solution, gồm test mới, biên dịch 0 warning/0 error.
⚠️ Không xác minh được NuGet vulnerability audit do hạn chế mạng.

### Test không cần DB

Đã đọc kiểm tra ba project; test guard Persistence dùng interceptor chặn trước pipeline DB.
Chạy riêng đúng ba project, không chạy cả solution:

```sh
DOTNET_EnableDiagnostics=0 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet test tests/LuxMap.Shared.Tests --no-build --no-restore -m:1
DOTNET_EnableDiagnostics=0 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet test tests/LuxMap.Persistence.Tests --no-build --no-restore -m:1
DOTNET_EnableDiagnostics=0 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet test tests/LuxMap.Infrastructure.Storage.Tests --no-build --no-restore -m:1
```

Cả ba lệnh exit 1 với cùng output:

```text
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
Test Run Aborted.
```

⚠️ Test chưa bắt đầu; không có số pass để báo cáo. Đây là socket giao tiếp nội bộ VSTest,
không phải kết nối DB. Không xin nâng quyền hoặc thay đổi sandbox.

Không chạy `OpenApiSpecTests`: `LuxMapSwaggerFactory` khởi động `Program`, mà
`src/LuxMap.Api/Program.cs` gọi `DotNetEnv.Env.TraversePath().Load()` trước khi dựng host.
Chạy fixture này sẽ vi phạm lệnh không đọc `.env`. Test mới đã biên dịch, chờ Claude chạy.

### Logic Python và rà diff

Dùng `python3` + `ast.parse` đọc file generator, lấy đúng ba AST node gán summary, đọc client,
và nhánh tiền tố trong vòng lặp operation; thực thi trên dictionary giả trong bộ nhớ.
Không thực thi toàn generator, không đọc/ghi spec JSON. Mỗi ca có `SUMMARY` literal `Nội dung`
và summary cũ `Tiền tố cũ`; assert cả summary cuối và extension không đổi.

Output thật:

```text
PASS POST /api/v1/auth/login: [Mobile] Nội dung
PASS POST /api/v1/auth/web/login: [Web] Nội dung
PASS GET /api/v1/auth/me: [Dùng chung] Nội dung
PASS GET /api/v1/poles: Nội dung
Python syntax + 4 summary rewrite cases: PASS; no JSON written
```

✅ `git diff --check` không có output; đã đọc lại diff và hai file mới.
Route, operationId, tên tag, DTO và package không đổi.

## Bàn giao

- Claude cần chạy lại ba project test ngoài hạn chế socket này, rồi test OpenAPI mới theo fixture sẵn có.
- Xuất JSON và kiểm Swagger thực tế do Claude thực hiện theo ticket; lượt này chưa xác minh UI/HTTP.
- Không có quyết định kiến trúc hay D-item mới. Không suy thêm client cho endpoint nghiệp vụ.
- Đã dừng ở bàn giao, không commit/push.
