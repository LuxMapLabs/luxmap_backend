# STAT-CITIZEN — phase 2, 06/11/2026

- Đã viết endpoint/DTO/query/service, đúng một reference Statistics → Citizen; một truy vấn DB gom CitizenReport INNER JOIN Pole, giữ nhóm chưa tuyến, nguồn luôn nhóm riêng, lọc night_of và tính resolution_rate ở backend.
- Test nằm trong `StatisticsTests.cs` để dùng lại fixture/scope/teardown hiện có; phủ yêu cầu ticket, gồm JWT hai xã kiểu legacy (không đổi luật tài khoản ACCT-1), hai submission/một report, CSV và lỗi query/quyền.
- Đã thêm ST-13 sau ST-10, SUMMARY/SECTION generator và thẻ tracking. Không sửa Contract/spec JSON, Citizen/schema hay hành vi endpoint cũ. Nhánh này chưa có STAT-WO/ValidateDateBounds: helper mới dùng lại EarliestNight/LatestNight sẵn có, không đổi repair-timeliness.

✅ Build thật: `dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false`, exit 0:
```text
Build succeeded.
    42 Warning(s)
    0 Error(s)
Time Elapsed 00:00:06.25
```
Chỉ cảnh báo `NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json.` Không warning C#. Python AST của generator hợp lệ (chỉ parse, không thực thi).

⚠️ Test/API runtime, Swagger/sinh/lint spec và git diff chưa chạy theo giới hạn đã chỉ định. Không stage/commit/push. Claude chạy kiểm chứng và review tiếp.

Phá thử: **chưa chạy, Claude sẽ chạy** (đều ở `src/LuxMap.Modules.Statistics/StatisticsService.cs`):
- (a) Dòng 128 `join p in poles on r.PoleId equals p.PoleId`: đổi thành `join p in poles.Where(p => p.SegmentId != null) ...` → `Citizen_segment_groups_keep_unassigned_poles_last_without_losing_the_commune_total` phải đỏ.
- (b) Dòng 120 `: reports.Where(r => r.DataSource != DataSource.CalibrationRig);`: đổi thành `: reports;` → `Citizen_calibration_rig_requires_an_explicit_source_filter` phải đỏ.
- (c) Bỏ dòng 121–122 lọc `r.NightOf >= from` / `r.NightOf <= to` → `Citizen_date_bounds_use_night_of_inclusively_with_no_default_window` phải đỏ.
- (d) Dòng 161 bỏ `+ r.Rejected` khỏi tử số ResolutionRate → `Citizen_items_count_each_status_once_and_keep_sources_separate` phải đỏ (0.8 thành 0.6).
Khôi phục sau phá bằng cp/touch, build lại, kiểm exit code test.

📌 D-item: không phát sinh; API dựa trên ST-13 SELF-SIGNED, nền tạm tới FW.

## Kiểm chứng ngoài sandbox (Claude, 06/11/2026)

Review diff: đúng đề — INNER JOIN `Pole`, gom nhóm ở DB, `data_source` luôn là chiều nhóm đứng đầu, `segment_id` null xếp cuối, tỉ lệ tính ở backend,
một `ProjectReference` Statistics → Citizen (Citizen không tham chiếu Statistics, không vòng). Worktree thiếu `.env`: `cp .env.example .env`.

```text
dotnet test --filter StatisticsTests   exit 0   Passed 34/34
```
Phá thử (mỗi lần `cp` khôi phục; cuối cùng `cmp` với bản gốc: giống hệt):
```text
a  join chỉ lấy cột có tuyến        exit 1  Failed 2: Citizen_segment_groups_keep_unassigned_poles_last…, Citizen_segment_filter_uses_the_pole…
b  bỏ lọc mặc định calibration_rig  exit 1  Failed 1: Citizen_calibration_rig_requires_an_explicit_source_filter
c  bỏ lọc night_of                  exit 1  Failed 1: Citizen_date_bounds_use_night_of_inclusively_with_no_default_window
d  bỏ rejected khỏi tử số           exit 1  Failed 3: …calibration_rig…, …csv_has_json_columns_bom_crlf_and_rounded_rate, …count_each_status_once…
```
OpenAPI: swagger ghi được ngoài sandbox; `gen_consolidated_spec.py` → `paths=113 implemented_ops=144`; redocly lint valid, 10 warnings.
Diff v1.5: thêm path mới + một dòng mô tả bị sinh lại.

Toàn suite `dotnet test --disable-build-servers`:
- Lượt 1: exit 1, 1 test đỏ ngoài phạm vi — `WorkOrderTests.Parallel_attempts_of_one_operation_end_on_one_row`
  (`InvalidOperationException: Operations that change non-concurrent collections must have exclusive access` — race `Dictionary` trong test;
  diff này không chạm WorkOrders). Chạy riêng 3 lần: xanh.
- Lượt 2: exit 0 — 45 + 386 + 80 + 1396 (7 skipped).

⚠️ Khi merge với PR #211: cả hai thêm `ValidateDateBounds` vào `StatisticsService` — giữ MỘT bản; hai thẻ `contract-drift.md` (ST-12, ST-13), `tracking.html`, `gen_consolidated_spec.py`, hai OpenAPI cần giữ cả hai bên (sinh lại OpenAPI sau khi gộp).
