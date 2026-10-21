# BE-12a — xếp hàng import đồng thời theo loại

Ngày: 20/10/2026 · Nhánh: `fix/BE-12a-import-race` · Nền: `0b926bc`.
Yêu cầu phiên làm việc là đặc tả cho bản sửa này; không có task file BE-12a riêng trong `.ai/tasks/`.
Không commit, không push theo yêu cầu.

## Thay đổi

- `src/LuxMap.Modules.Assets/Import/AssetImportService.cs`: mở transaction trước planner; câu lệnh đầu là
  `SELECT pg_advisory_xact_lock(@p0)` qua `ExecuteSqlInterpolatedAsync`. Khoá bigint cố định:

  | Loại | Khoá |
  |---|---|
  | segments | `0x4C4D4901` |
  | cabinets | `0x4C4D4902` |
  | feeders | `0x4C4D4903` |
  | poles | `0x4C4D4904` |
  | fixtures | `0x4C4D4905` |

  Không phụ thuộc thứ tự enum hoặc `GetHashCode`. Import hiếm nên chấp nhận tuần tự theo loại, kể cả khác xã.
  Mọi ghi hàng giữ nguyên đường ChangeTracker → `AssetStamp.Touch` → `SaveChanges` / `CommuneWriteGuard`.
  Nhánh không có thay đổi vẫn clear tracker rồi trả kết quả, dispose transaction để rollback và nhả khoá, không commit.
  Lỗi planner / ghi / commit vẫn thoát qua `await using`; các đường CRUD không lấy khoá import.
- `tests/LuxMap.Api.Tests/AssetImportConcurrencyTests.cs`: dùng `AssetDatabaseCollection` / `AssetImportFixture`, xã và
  người quản lý do fixture tạo/dọn, external_ref ngẫu nhiên, không literal ID. Hai request cùng file feeder chờ bằng
  `Task.WhenAll`; interceptor chỉ đăng ký qua DI của host con, không thêm hook production.
- `CLAUDE.md`: thay hạn chế upsert cũ bằng quy tắc xếp hàng, lý do không dùng SQL thô `ON CONFLICT`, phạm vi khoá.
- `tracking.html`: ghi trạng thái và phần xác minh còn lại.

## Cách test bắt race

1. Giữ request đầu tại `ReaderExecutedAsync` sau khi PostgreSQL trả kết quả tra feeder rỗng.
2. Bắt đầu request thứ hai trong khi request đầu còn giữ transaction và chưa ghi.
3. Có lock: khi request thứ hai bắt đầu lấy advisory lock, nhả truy vấn đầu; request đầu ghi/commit rồi request sau
   mới lập kế hoạch. Không có lock: chỉ nhả khi truy vấn thứ hai cũng đã trả về, nên cả hai giữ kết quả chưa có hàng
   và cùng chèn — một response 500 làm assert HTTP 200 đỏ. Không dựa vào độ trễ ngẫu nhiên hoặc số dòng lớn.
4. Assert cả hai 200, failed = 0, tổng inserted = 2, tổng unchanged = 2, updated = 0; kiểm DB đúng một hàng mỗi mã.
5. Gửi lại cùng file: unchanged = 2, chứng minh request không ghi đã kết thúc transaction và nhả khoá.

Đây là thiết kế test qua đọc mã; **chưa chạy test hoặc phá thử** trong phiên này theo yêu cầu.
Lệnh dành cho người chạy Docker:

```bash
dotnet test --filter 'FullyQualifiedName~AssetImportConcurrencyTests'
```

## Bằng chứng terminal

Worktree mới chưa có `obj/project.assets.json`; lần build `--no-restore` đầu báo `NETSDK1004`.
Restore bằng cache cục bộ, không đổi dependency:

```text
$ dotnet restore --source /Users/nhm809/.nuget/packages --ignore-failed-sources -m:1 -nr:false
  Restored .../tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj (in 136 ms).
  Restored .../src/LuxMap.Api/LuxMap.Api.csproj (in 83 ms).
exit 0

$ dotnet build --no-restore -warnaserror -m:1 -nr:false -p:UseSharedCompilation=false
  LuxMap.Api.Tests -> .../tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:22.60
exit 0

$ git diff --check
(không output), exit 0
```

Hai lượt build trước đã dừng / hủy khi thiếu assets hoặc chờ compiler; lượt thành công tắt shared compilation để
chạy được trong sandbox. Không chạy `dotnet test`, không chạm Docker/DB/production. Chưa xác minh hành vi chạy thật.

## Xác minh và đưa lên PR (Claude, 21/10/2026)

Commit gốc `aaf0f9a` (worktree `wt-import-race`, chưa push, nền `0b926bc`) **chưa từng chạy test**. Đưa lên nhánh mới
`fix/BE-12a-import-queue` từ `dev` (`7bf869e`):

- **Chỉ áp diff** của `aaf0f9a` lên `AssetImportService.cs` của `dev` — lấy nguyên file sẽ xoá phần đọc `lamp_count` của LAMP-COUNT (#168).
- Đoạn chữ sửa trong `CLAUDE.md` cũ nay nằm ở `docs/agents/traps/assets.md` (sau #167, CLAUDE-MD-CLEANUP); thêm câu nối rơ-le
  (`PUT /assets/iot-nodes/{id}/relays/…`) không được khoá import phủ.
- Comment `PlanFeedersAsync` "chỉ script seed ghi `feeder_control`" đã lỗi thời từ LIGHT-CTRL 2a → sửa.
- Review Codex không liên quan (`.ai/reviews/OPS-DEPLOY-login-by-codex.md`, nằm chung commit gốc) tách thành commit riêng.

```
$ dotnet test --filter 'FullyQualifiedName~AssetImportConcurrencyTests'     # hai lượt
Passed!  - Failed: 0, Passed: 1, Total: 1
Passed!  - Failed: 0, Passed: 1, Total: 1
# phá thử: bỏ dòng pg_advisory_xact_lock
  Failed …Concurrent_imports_of_the_same_file_insert_each_reference_once_and_both_answer_200
# khôi phục
Passed!  - Failed: 0, Passed: 1, Total: 1
```
