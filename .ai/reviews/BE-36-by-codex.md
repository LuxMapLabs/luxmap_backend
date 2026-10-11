# BE-36 Phase 2 — Codex review (12/10/2026)

> Ghi lại nguyên văn bởi Claude: bản Codex ghi trong worktree review đã bị xoá trước khi chép ra; nội dung dưới đây chép
> từ output của lượt review đó (gpt-6.1-sol, ~91k token). Phần "Đã xử lý" ở cuối do Claude thêm.

Diff `7a9c1f9..b560f8a` (test, ci, docs). Đã đọc task, quyết định cuối P1 và review P1. Chỉ ghi file này; không Docker/build/test/commit.

## Standards

1. **P3 — `docs/development.md:51–54`: tài liệu CI còn mô tả cơ chế cũ.** Vẫn nói Compose dựng DB, fixture không bootstrap và `SeedPassword` đọc trực tiếp file `.env`. Trái `TestDatabase.cs:135–145`, `AuthTestExtensions.cs:14–16` và `ci.yml:154–165`. **Sửa:** viết lại đoạn này: Compose chỉ EMQX; test tự dựng/migrate/seed/mock PostGIS; helper đọc env sau `LoadEnvironment`.

## Spec

1. **P2 — `tests/LuxMap.Api.Tests/CorsTests.cs:96`, `tests/LuxMap.Api.Tests/LuxMapApiFactory.cs:6`: buộc ca không DB phụ thuộc Docker.** `LuxMapTestHost.cs:20` gọi `EnsureReady` trước cả kiểm CORS; lọc ca startup-fail hoặc `CorrelationIdTests` nay vẫn phải dựng container, migrate và chạy Python. Trái mục KHÔNG ĐƯỢC làm của task và phần sửa sau review P1 nêu đích danh hai nhóm này. **Sửa:** host riêng cho ca không DB, loại hosted services và truy cập persistence thật; coverage cho phép ngoại lệ hẹp, vẫn bắt mọi host dùng DB đi qua bootstrap/guard.

2. **P2 — `tests/LuxMap.Api.Tests/TestDatabase.cs:128`: lỗi cleanup thoát thẳng khỏi `ProcessExit`.** Nếu Docker mất kết nối trước lúc thoát, `DisposeAsync` có thể ném lỗi I/O vào handler, làm tiến trình kết thúc lỗi sau khi test đã xanh. Handler cũng chờ đồng bộ không có giới hạn riêng. Đây là đường lỗi suy ra từ code, chưa tái hiện: [Testcontainers 4.15.0](https://github.com/testcontainers/testcontainers-dotnet/blob/4.15.0/src/Testcontainers/Clients/TestcontainersClient.cs#L154) không nuốt lỗi kết nối khi xóa container. **Sửa:** cleanup có giới hạn thời gian, bắt lỗi trong handler và ghi chẩn đoán; giữ resource reaper làm đường dự phòng.

## Đối chiếu năm trọng tâm

- ✅ `Lazy<Task>` + `ExecutionAndPublication` khởi tạo một lần; `Task.Run` tránh capture synchronization context xUnit. Bootstrap không gọi ngược `EnsureReady`, không thấy vòng chờ/deadlock.
- ✅ Với Program/factory hiện tại, `Services`, `Server`, `CreateDefaultClient`, Kestrel và host con `WithWebHostBuilder` đều đi qua `CreateHost`; host con giữ delegate của factory gốc. Đối chiếu [ASP.NET Core 10.0.11](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Mvc/Mvc.Testing/src/WebApplicationFactory.cs#L133).
- ✅ Guard bootstrap chạy trước migrate/seed; SQL mock dùng kết nối container, giữ `BEGIN/COMMIT`. Npgsql nhận nhiều câu và bỏ qua dấu `;` trong `DO $$` đúng cách ([parser](https://github.com/npgsql/npgsql/blob/v10.0.1/src/Npgsql/SqlQueryParser.cs#L314)).
- ✅ Không thấy fallback sang DB dev khi thiếu dependency. Docker `Build()` ngoài catch nhưng thư viện đã báo daemon/config thiếu; thiếu `python3` ném `Win32Exception` từ `Process.Start`, không đi vào nhánh `null` ở dòng 168. Hai pipe được đọc đồng thời.
- ✅ CI dùng `steps.services.outcome`, giữ EMQX, cờ E2E, ports và callback keys. CLAUDE.md đúng về teardown/sequence chung, S3 giả và guard sau startup.

📌 DotNetEnv tìm `.env` từ CWD, còn precheck kiểm file root. File khác ở lượt bootstrap bị `AssertTargets` chặn trước ghi. Guard host thường chạy sau `Start`; chỉ bảo đảm với cấu hình không đổi giữa các lượt dựng host, chưa thấy code hiện tại đổi đích sau bootstrap. [DotNetEnv 3.2.0](https://github.com/tonerdo/dotnet-env/blob/v3.2.0/src/DotNetEnv/Env.cs#L21).

⚠️ Chỉ xác minh tĩnh. Kết quả 866/46/45/352, hai skip MQTT, 84 s và hai phép phá guard là bằng chứng Claude do người dùng cung cấp; không chạy lại, chưa xác minh MQTT E2E trên CI.

Tổng: Standards **1 P3**; Spec **2 P2**; không có P1 đã chứng minh.

## Đã xử lý (Claude, 12/10/2026)

| Phát hiện | Xử lý |
|---|---|
| P2-1 Docker cho ca không DB | `NoDatabaseTestHost` + `NoDatabaseApiFactory`: thay `NpgsqlDataSource` bằng địa chỉ không ai nghe (không đổi biến toàn tiến trình). `CorrelationIdTests`, `PaginationTests`, `CorsTests` (cả `SettingsFactory`) chuyển sang. **Không gỡ hosted services**: `RemoveAll<IHostedService>` sẽ gỡ luôn `GenericWebHostService` dựng pipeline HTTP. Kiểm: 23 test đó xanh và `docker events` không ghi container nào được tạo; `NoDatabaseHostTests` chứng minh host đó không tới được DB nào |
| P2-2 cleanup | `ProcessExit` chờ tối đa 10 s, bắt mọi lỗi, ghi một dòng chẩn đoán; reaper vẫn là dự phòng |
| P3 docs | `docs/development.md` mục CI viết lại |
