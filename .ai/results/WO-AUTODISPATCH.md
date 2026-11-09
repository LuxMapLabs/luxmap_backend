# WO-AUTODISPATCH — phase 2 (09/11/2026)

Đã viết endpoint preview chỉ đọc, `AutoDispatchQueries`, planner thuần và `EngineerLoadBoard` công khai dùng lại.
Tách eligibility và phép chiếu dọc tuyến dùng chung với service hiện có; thêm cấu hình validate khi startup,
unit test + integration trong `WorkOrderAutoDispatchTests.cs` (partial `WorkOrderTests`, dùng fixture/teardown riêng từng test),
drift WO-AUTODISPATCH-1 SELF-SIGNED, bẫy và tracking. Không sửa Contract/OpenAPI, schema, migration hay capability.
Không chạy git/test/Swagger/EF; không commit theo yêu cầu phiên này.

## Bằng chứng

✅ Build toàn solution (bao gồm test) đã chạy thật. Restore bằng cache NuGet cục bộ vì lượt mặc định không tiến triển:

```text
dotnet build LuxMap.slnx --disable-build-servers -m:1 -p:NuGetAudit=false --source /Users/nhm809/.nuget/packages
```

Lượt đầu build thành công nhưng có EF1002/xUnit2031 ở test mới; đã sửa cả hai. Build cuối sau mọi sửa code:

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore -m:1
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:05.56
```

Kiểm tĩnh bằng Python: `appsettings.json: valid JSON`, `AGENTS.md symlink: True`.
Lượt đầu `--no-restore` trước restore báo NETSDK1004 thiếu `src/LuxMap.Api/obj/project.assets.json`;
hai lượt build song song mặc định không tiến triển đã gửi Ctrl-C. Build một node bằng cache ở trên mới là bằng chứng hoàn tất.
⚠️ Chưa chạy HTTP thật, test hoặc phá thử; không khẳng định test xanh hay hành vi runtime đã được xác minh.

## Claude cần chạy

```bash
dotnet build LuxMap.slnx --disable-build-servers
dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter 'FullyQualifiedName~AutoDispatchPlannerTests|FullyQualifiedName~WorkOrderTests.Auto_dispatch|FullyQualifiedName~CapabilityPolicyCoverageTests'
dotnet test --disable-build-servers
```

Test phủ ví dụ 7 cột, split 8/9/16/17 + hai fault chung cột, hình học đảo thứ tự insert,
T7/CN + mốc trước trưa, quota 3×2 + critical/priority, tải mọi loại phiếu/done/cancelled,
ranh tuần T2–CN/ngày trống, tie daily/numeric ID, nhóm riêng xã/no engineer,
status/active vs released link/no segment/locked/wrong commune, cap 2000,
không ghi bốn bảng theo xã riêng của test + JSON lặp lại, auth/body/scope, options >0, giới hạn 366 ngày.

**Phá thử: chưa chạy, Claude chạy** (khôi phục bằng cp/touch, build lại trước lượt xác nhận):

| Chốt | Đúng dòng để phá | Test phải đỏ |
|---|---|---|
| a. Cuối tuần | `AutoDispatchPlanner.cs:52`, sau `var date = start.AddDays(offset);` thêm nhảy T7/CN sang T2 | `Auto_dispatch_never_skips_weekends`, `Nights_plus_lead_days_include_both_weekend_days` |
| b. Quota | `AutoDispatchPlanner.cs:53`, `< quotaPerDay` → `<= quotaPerDay` | `Three_engineers_times_two_slots_prioritizes_critical_then_score_and_delays_low` |
| c. Severity | `AutoDispatchPlanner.cs:94`, max hạng → max `Severity.ToString()` theo alphabet | `Three_engineers_times_two_slots_prioritizes_critical_then_score_and_delays_low` |
| d. Cancelled | `AutoDispatchQueries.cs:68`, bỏ `w.WoStatus != WorkOrderStatus.Cancelled` | `Auto_dispatch_load_includes_every_kind_and_done_but_excludes_cancelled` (cancelled) |
| e. Tuần | `AutoDispatchPlanner.cs:37`, đổi WeekStart thành `date.AddDays(-(int)date.DayOfWeek)` | `Weekly_then_daily_load_then_numeric_id_break_ties_and_sunday_belongs_to_previous_week`, `Auto_dispatch_counts_only_the_monday_to_sunday_week_and_scheduled_orders` |
| f. Cột phân biệt | `AutoDispatchPlanner.cs:84`, group theo fault ID thay pole ID | `Splitting_uses_road_order_and_distinct_poles`, `Auto_dispatch_chunks_in_actual_road_order_without_splitting_a_pole` |
| g. Released | `AutoDispatchQueries.cs:41`, bỏ `&& link.ReleasedAt == null` | `Auto_dispatch_filters_status_active_links_and_engineer_eligibility` |
| h. Không ghi | `AutoDispatchQueries.cs`, trước return thêm một notification/audit hợp lệ có commune đang preview + SaveChanges | `Auto_dispatch_reads_only_is_repeatable_and_matches_the_seven_pole_example` |

## Điểm chưa chốt / giới hạn

- **D-1 — quyền admin:** ticket yêu cầu system_admin được, nhưng WO-1 + authorization guide + Matrix hiện chỉ manager có
  ManageWorkOrders; giữ nguồn cao hơn, test assert admin 403. Chưa sửa Shared hay hai bảng quyền literal. Mỹ cần chốt quyền
  trước khi có thể kiểm HTTP caller `*` nhiều xã; planner có test tách xã độc lập. Đã ghi mâu thuẫn ở drift, không chỉ ở results.
- **WO-BATCH chưa hiện thực trong cây** (controller không có batch), bỏ kiểm liên thông theo ticket. Danh sách trường tạo
  thực tế có bảy khoá (ticket gọi sáu): task_kind/title/scheduled_date/due_date/assigned_to/fault_ids/note.
- Tie ID hai bên ngưỡng hiện được canh trong unit bằng USR-9999/USR-10000; **chưa có integration tạo tài khoản qua sequence
  ở ngưỡng đó**. Integration dùng tài khoản tự tạo bằng default sequence, không ghim ID. Cần bổ sung test ngưỡng DB trong
  collection chạy một mình nếu reviewer đòi đúng tiêu chí sequence, tránh tranh chấp `user_id_seq` với collection khác.
- Hết 366 ngày: giữ proposal chưa gán ngày khởi điểm, lý do `SEARCH_LIMIT_EXCEEDED`; đã ghi drift.
  `fault_count` đếm fault trong proposals; `pole_count` đếm đơn vị, gồm cả fault không cột.
- Tên tuyến ngoài scope không lộ: dùng segment ID như fallback, chỉ đọc geometry không filter để có đúng thứ tự WO-12.
- Chưa gửi thông báo FE. Chưa kiểm Swagger theo lệnh cấm của phiên; spec giữ nguyên.
