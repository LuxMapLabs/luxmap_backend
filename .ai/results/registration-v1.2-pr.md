# REG-v1.2 — chuẩn bị PR vào dev (26/09/2026)

Người dùng duyệt push + mở PR, commit bản đăng ký đã loại bỏ thông tin định danh,
ghi hai bẫy test vào CLAUDE.md, và giữ nguyên full_name đã sửa tay trên luxmap_dev.
Không chạy --seed hoặc migration trong lượt này. Bản gốc đăng ký giữ ngoài Git.

## Tích hợp dev

Base mới: origin/dev `11b57e3` (PR #48, #49, #50).
Giải quyết 6 file xung đột; giữ response BE-12b và policy capability. Ba endpoint
chi tiết tài sản mới đều dùng ReadNetwork. AssetReadShapeTests dùng ManagerClientAsync
thay helper AdminClientAsync đã đổi tên. Hai OpenAPI được sinh lại từ code.
Contract v1.7 giữ cả lịch sử BE-12b (22/09) và REG-v1.2 (25/09); không tự tạo quyết định API mới.

## Kiểm chứng

Lệnh build: `dotnet build --no-restore --disable-build-servers -warnaserror`

```

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.93
```

Lệnh test: `dotnet test --no-build --no-restore --disable-build-servers --settings luxmap.runsettings -m:1`.
Đặt ConnectionStrings__LuxMap từ cấu hình local, chỉ thay database thành luxmap_test;
không in thông tin xác thực. Xác nhận DB test có migration RenameUserRolesToRegistrationV12.

```
Passed!  - Failed:     0, Passed:   419, Skipped:     0, Total:   419, Duration: 8 s - LuxMap.Api.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 823 ms - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 85 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   149, Skipped:     0, Total:   149, Duration: 40 ms - LuxMap.Shared.Tests.dll (net10.0)
```

Tổng 606/606; bao gồm HTTP ma trận 4 vai trò, coverage capability, response BE-12b và scope.

OpenAPI: `dotnet swagger tofile` → `python3 docs/openapi/tools/gen_consolidated_spec.py`.
Kết quả thật: `paths=36 implemented_ops=35 not_implemented_ops=13 schemas=105`.
Lint: `npx --yes @redocly/cli@2.0.0 lint docs/openapi/luxmap-v1.5.json`.

```
docs/openapi/luxmap-v1.5.json: validated in 53ms

Woohoo! Your API description is valid. 🎉
You have 3 warnings.

```

Kiểm tra thủ công: bản đăng ký không còn họ tên, mã sinh viên hoặc khối team;
nội dung từ mục 3 trở đi giữ nguyên. Diff CSV theo ô so với origin/dev có đúng ba thay đổi:
- BE-07, cột tên task (index 2).
- BE-08, cột notes (index 13).
- BE-41, cột tiêu chí nghiệm thu (index 3).

## Giới hạn

- CI trên GitHub chỉ chạy sau khi mở PR; kết quả local không thay thế CI.
- Chưa xác nhận tích hợp UI trên repo FE/Mobile; PR phải nêu breaking role và quyền ghi.
- Quyết định SELF-SIGNED vẫn chờ FW xác nhận như Contract đã ghi.
