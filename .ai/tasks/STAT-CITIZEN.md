---
ticket: STAT-CITIZEN
title: GET /statistics/citizen-feedback — đếm hàng chờ báo cáo công dân theo trạng thái
status: ready
phase: 2
owner: codex
branch: feat/STAT-citizen-feedback
contract_refs:
  - "Chưa lên Contract — họ ST-* trong docs/contract-drift.md (SELF-SIGNED, nền tạm). Mã của ticket này: ST-13 (ST-11 và ST-12 nằm ở hai PR chưa merge — KHÔNG dùng số 11/12, KHÔNG sửa dòng của chúng)"
depends_on: []
---

## Bối cảnh

WP5 dựng biểu đồ "Tiến độ xử lý phản ánh QR" và tỉ lệ xử lý theo tuyến. FE đã chốt (06/11/2026): thống kê theo thực thể
**`CitizenReport`** (một hạng mục hàng chờ = mọi lần gửi về MỘT cột trong MỘT đêm, còn pending), trạng thái chuẩn
`CitizenReportStatus` (`pending | accepted | merged | rejected`), tỉ lệ dạng số thực 0..1,
`resolution_rate = (accepted + merged + rejected) / total`, dùng capability `ReadStatistics`, mốc ngày là `night_of`,
cột chưa gán tuyến gom vào nhóm `segment_id: null`.

## Quyết định đã chốt (Mỹ + FE, 06/11/2026) — không phải D-item

- Endpoint: `GET /api/v1/statistics/citizen-feedback`. Capability **`ReadStatistics`** (không capability mới; `field_engineer` bị loại, ST-8).
- Query:
  - `group_by` — CSV, hợp lệ **`commune`, `segment`** (enum chiều RIÊNG cho endpoint này, như `WorkOrderStatisticsDimension`; không đụng `StatisticsDimension`). Giá trị khác → 400.
  - `commune_id` — lặp được, tùy chọn, `CommuneFilter.Narrow`.
  - `segment_id` — tùy chọn, trim, rỗng = không lọc; khớp theo **`pole.segment_id` của cột báo cáo**.
  - `data_source` — CSV, của **cột** (`CitizenReport.DataSource`, D-5 / ST-1); **mặc định loại `calibration_rig`** như `fixture-status`; hỏi đích danh thì có. `WireEnum.ParseCsv<DataSource>`.
  - `from` / `to` — `DateOnly` tùy chọn, lọc theo **`citizen_report.night_of`**, bao cả hai đầu, **không cửa sổ mặc định**. Dải hợp lệ và `from > to` → 400 `VALIDATION_FAILED`, `details.field` đúng tên — dùng lại `ValidateDateBounds` / hằng sẵn có trong `StatisticsService`, không chép.
  - `format=csv` như W-5 (`[CsvExport]`).
- Response (JSON snake_case): `as_of` (UTC), `group_by` (**`data_source` luôn đứng đầu**, rồi các chiều đã yêu cầu theo thứ tự nhập — như `Dimensions(..., withDataSource: true)` của fixture-status), `rows[]`.
- Mỗi hàng, khoá luôn có mặt: `data_source`, `commune_id` (null nếu không nhóm theo `commune`), `segment_id` (null nếu không nhóm theo `segment` **HOẶC** cột chưa gán tuyến — khi `group_by` có `segment`, null nghĩa là "chưa phân tuyến"; ghi rõ trong ST-13 và XML-doc), `pending, accepted, merged, rejected`, `total` (= tổng bốn số), `resolution_rate` (số thực 0..1, 4 chữ số thập phân, `MidpointRounding.AwayFromZero`, **tính ở backend**, `(accepted + merged + rejected) / total`; hàng nào cũng có `total ≥ 1` nên không null). KHÔNG `segment_name`, KHÔNG `label`, KHÔNG tổng cấp ngoài.
- Chỉ trả nhóm có dữ liệu; không dữ liệu → `rows: []`. Thứ tự hàng xác định: `data_source` (theo enum, không theo chữ), `commune_id` (độ dài rồi chuỗi), `segment_id` (độ dài rồi chuỗi, **null cuối**).
- Đếm **hạng mục `CitizenReport`**, không đếm `CitizenSubmission` (từng lần gửi). `rejected` tính là đã xử lý (FE chốt).
- Gốc truy vấn `CitizenReport` (đã scope xã bằng query filter) INNER JOIN `Pole` theo `PoleId` để lấy `SegmentId`. Gom nhóm ở DB một truy vấn; không kéo báo cáo về bộ nhớ. Không nhân dòng.
- Ranh giới module: `Statistics` hiện KHÔNG tham chiếu `Citizen`. Thêm **đúng một** `ProjectReference` `LuxMap.Modules.Citizen` vào `LuxMap.Modules.Statistics.csproj` (đã kiểm tra: `Citizen` không tham chiếu `Statistics`, không vòng; `Statistics` vốn đã đọc `WorkOrders`, `Assets`). Không thêm tham chiếu nào khác.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/commune-scope.md` + `docs/agents/traps/citizen.md` + `docs/agents/traps/testing.md`
- `docs/contract-drift.md` mục ST-1…ST-10, W-5, và CITIZEN-QR (D-5, PC-*)
- `src/LuxMap.Modules.Statistics/*.cs`; `src/LuxMap.Modules.Citizen/Entities/CitizenReport.cs`, `.../Configurations/CitizenReportConfigurations.cs`
- `tests/LuxMap.Api.Tests/StatisticsTests.cs`, và test của Citizen (cách trồng `CitizenReport` hợp lệ: CHECK `ck_citizen_report_decision_columns`, `ck_citizen_report_times_ordered`, index duy nhất pending/cột/đêm — status khác `pending` cần cột quyết định hợp lệ)
- `RoleCapabilityMatrixTests` / `CapabilityPolicyCoverageTests` (endpoint mới có làm test nào đỏ không)

## Yêu cầu

1. Contracts: `CitizenFeedbackDimension { Commune, Segment }`, `CitizenFeedbackStatistics`, `CitizenFeedbackRow` (record `required` init, XML-doc ngắn).
2. Service `CitizenFeedbackAsync(query, ct)` + `CitizenFeedbackQuery`; controller action đủ `[ProducesResponseType]` 200/400/403/413, `[CsvExport]`, `<remarks>` nêu mốc `night_of`, nguồn dữ liệu, ý nghĩa `segment_id` null, công thức `resolution_rate`.
3. Test trong `StatisticsTests.cs` (hoặc file mới cạnh nó nếu sạch hơn — nêu lựa chọn), assert giá trị cụ thể:
   - không nhóm: một hàng mỗi `data_source`; bốn đếm, `total`, `resolution_rate` đúng (vd 1 pending + 2 accepted + 1 merged + 1 rejected → 0.8; toàn pending → 0).
   - `group_by=segment`: hai tuyến + một cột `segment_id` NULL → hàng `segment_id: null` có mặt, **tổng toàn xã không hụt**, null xếp cuối; `group_by=commune,segment` với hai xã trong scope (token hai xã như `StatisticsTests` đã làm cho work-orders — xem `Work_order_groups_cover_two_visible_communes…`).
   - `data_source`: báo cáo trên cột `calibration_rig` mặc định KHÔNG xuất hiện, `data_source=calibration_rig` thì có; `data_source` luôn là chiều nhóm (không bao giờ cộng hai nguồn vào một hàng).
   - `from/to` theo `night_of`: hai đầu bao gồm, ngoài dải → 400, `from > to` → 400, định dạng sai → 400; không bound → tính hết.
   - `segment_id` lọc đúng (qua cột), trim, rỗng = không lọc, tuyến không có báo cáo → 200 `rows: []`.
   - scope: xã ngoài scope không rò số; `commune_id` ngoài scope → 403 `COMMUNE_FORBIDDEN`; `field_engineer` → 403 `ROLE_FORBIDDEN`.
   - `format=csv` đúng tên cột + BOM + CRLF; `format=xml` → 400; `group_by=data_source` hoặc `task_kind` → 400.
   - 2 lần gửi vào cùng một hạng mục chỉ tính MỘT (đếm theo `CitizenReport`, không theo submission).
4. **Phá thử** (CLAUDE.md), viết vào results là "chưa chạy, Claude sẽ chạy" kèm đúng dòng: (a) đổi INNER JOIN `Pole` thành lọc/gom sai (vd bỏ nhóm `segment_id` null) → test cột chưa gán tuyến đỏ; (b) bỏ lọc mặc định `calibration_rig` → test nguồn đỏ; (c) bỏ lọc `night_of` → test `from/to` đỏ; (d) đổi công thức tỉ lệ (vd bỏ `rejected`) → test tỉ lệ đỏ.
5. Tài liệu:
   - `docs/contract-drift.md`: thêm dòng **ST-13** ngay sau dòng ST-10 (không sửa dòng cũ). Ghi: cộng thêm / SELF-SIGNED, hình dạng, đếm theo `CitizenReport`, mốc `night_of`, `rejected` là đã xử lý, null `segment_id`, `data_source` theo ST-1, CSV, project reference mới.
   - `docs/openapi/tools/gen_consolidated_spec.py`: thêm `SUMMARY` / `SECTION` như STAT-WO đã làm cho `/statistics/work-orders`. KHÔNG tự chạy swagger / sinh spec (sandbox treo) — Claude làm.
   - `tracking.html`: thêm thẻ như thẻ STAT-WO / STAT-SEGMENT (ngày, task, file, commit dự kiến).

Tiêu chí xong:

- [ ] `dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false` không lỗi / warning mới
- [ ] `git diff --stat` chỉ gồm: Statistics module (3 file + csproj), `StatisticsTests.cs` (+ test capability nếu buộc), drift, `gen_consolidated_spec.py`, `tracking.html`, `.ai/tasks/STAT-CITIZEN.md`, `.ai/results/STAT-CITIZEN.md`

## KHÔNG ĐƯỢC làm

- Không migration; không đổi schema, index, enum (`CitizenReportStatus`, `source_channel`, `data_source`), response shape của endpoint khác.
- Không thêm `segment_name`, `label`, tổng/tỉ lệ cấp ngoài. Không tính theo `CitizenSubmission`. Không thêm giá trị `source_channel` cho công dân.
- Không sửa `docs/api-contract-v1.1.md`. Không chạm `fixture-status`, `repair-timeliness`, `work-orders`, module Citizen (chỉ đọc entity).
- Không tạo capability mới. Không `ExecuteUpdate/ExecuteDelete`. Không chạy test trên `luxmap_dev`. Không `git push`; không `git add -A` / `git add .`.
- Không tự chọn khác "Quyết định đã chốt"; chỗ mơ hồ → D-item cuối results.

## Giới hạn sandbox (đã biết)

Sandbox của bạn **không chạy được test, không chạy được `dotnet swagger`, không ghi được `.git/index.lock`**. Đừng cố vòng qua.
Làm: code + test + docs, `dotnet build`, ghi `.ai/results/STAT-CITIZEN.md` **ngắn gọn** (đã làm gì, output build thật, D-item, phá thử chưa chạy).
Không dán log dài. KHÔNG commit, KHÔNG stage — để nguyên cây làm việc.
