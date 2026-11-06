# STAT-WO — phase 2 (06/11/2026)

- Đã viết endpoint/controller/contracts/service: DB GroupBy + sáu conditional Count, scope xã, tuyến qua Any/EXISTS,
  scheduled_date bao hai đầu, NULL bị loại khi có bound; JSON/CSV và thứ tự hàng theo ticket.
- Dùng enum riêng `WorkOrderStatisticsDimension` (ticket cho phép), giữ `StatisticsDimension` cũ để hai endpoint cũ
  vẫn từ chối task_kind. Tách kiểm dải ngày thành helper dùng chung; không đổi cửa sổ repair-timeliness.
- Viết test trong StatisticsTests (gồm token legacy hai xã, teardown target trước phiếu), ST-12 sau ST-10,
  generator SUMMARY/SECTION và tracking. Không sửa Contract, schema, capability hay dòng ST cũ.

✅ Build thật: `dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false`, exit 0:
```text
    42 Warning(s)
    0 Error(s)
Time Elapsed 00:00:26.82
```
Build cuối `--no-restore` sau rà soát: 21 Warning(s), 0 Error(s), 00:00:05.13.
Warning chỉ NU1900 (không truy cập api.nuget.org để lấy dữ liệu vulnerability). `git diff --check`: exit 0, không output.

⚠️ `dotnet tool restore`: Restore was successful. Swagger theo commands.md không trả output, đã Ctrl-C (exit 130).
Thử lại tắt CLI telemetry với timeout 40s: `Swagger timed out after 40 seconds; export not verified.`
Hai OpenAPI chưa đổi; chưa chạy generator/lint trên spec cũ. Claude cần xuất luxmap-v1.json →
`python3 docs/openapi/tools/gen_consolidated_spec.py` → `npx @redocly/cli lint docs/openapi/luxmap-v1.5.json`.
Không chạy test/manual HTTP, không commit/stage/push theo yêu cầu. Claude chạy StatisticsTests + toàn suite ngoài sandbox.

Phá thử **chưa chạy, Claude sẽ chạy**, mỗi lần khôi phục bằng `cp` rồi build lại:
1. StatisticsService.cs:187–188, bỏ `|| db.Set<WorkOrderSegment>().Any(...)` →
   `Work_order_segment_filter_matches_direct_and_survey_targets_without_multiplying_orders` phải đỏ.
2. StatisticsService.cs:190–191, bỏ hai Where ScheduledDate →
   `Work_order_dates_are_inclusive_and_either_bound_excludes_unscheduled_orders` phải đỏ.
3. Cùng dòng 190/191, đổi so sánh thành `(w.ScheduledDate ?? from) >= from` /
   `(w.ScheduledDate ?? to) <= to` → test ngày trên phải đỏ vì nhận thêm phiếu NULL.

D-item: không phát sinh. Nền API vẫn SELF-SIGNED ST-12; chưa xác minh runtime/SQL trên PostGIS.

## Kiểm chứng ngoài sandbox (Claude, 06/11/2026)

Review diff: đúng đề — gốc `WorkOrder` đã scope xã, gom nhóm ở DB, lọc tuyến bằng `Any`, enum chiều riêng. Worktree mới thiếu `.env`
(gitignore) nên lượt chạy đầu đỏ hết trong 0,3 s; `cp .env.example .env` rồi chạy lại.

```sh
dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter "FullyQualifiedName~StatisticsTests"   # exit 0
```
```text
Passed!  - Failed:     0, Passed:    33, Skipped:     0, Total:    33
```

Phá thử (mỗi lần `cp` khôi phục; cuối cùng `cmp` với bản gốc: giống hệt):
```text
a  bỏ nhánh work_order_segment      exit 1  Failed: 1  Work_order_segment_filter_matches_direct_and_survey_targets_without_multiplying_orders
b  bỏ hai Where ScheduledDate       exit 1  Failed: 1  Work_order_dates_are_inclusive_and_either_bound_excludes_unscheduled_orders
c  (ScheduledDate ?? from) >= from  exit 1  Failed: 1  Work_order_dates_are_inclusive_and_either_bound_excludes_unscheduled_orders
```

OpenAPI: `dotnet swagger tofile` chạy được ngoài sandbox ("successfully written"); `gen_consolidated_spec.py` → `paths=113 implemented_ops=144`;
`npx @redocly/cli lint docs/openapi/luxmap-v1.5.json` → valid, 10 warnings. Diff v1.5: chỉ thêm path mới và đổi số operation (143 → 144) ở mô tả.

Toàn suite `dotnet test --disable-build-servers` (exit 0):
```text
Passed!  - Failed:     0, Passed:    45 - LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    80 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:   386 - LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:  1395, Skipped: 7 - LuxMap.Api.Tests.dll
```
Test chạy trên PostGIS Testcontainers thật, nên SQL do EF sinh đã được kiểm chứng (dòng "chưa xác minh runtime/SQL" ở trên không còn đúng).
