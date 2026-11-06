---
ticket: STAT-WO
title: GET /statistics/work-orders — đếm phiếu theo task_kind × wo_status
status: ready
phase: 2
owner: codex
branch: feat/STAT-work-orders
contract_refs:
  - "Chưa lên Contract — họ ST-* trong docs/contract-drift.md (SELF-SIGNED, nền tạm). Mã của ticket này: ST-12 (ST-11 nằm ở branch STAT-SEGMENT chưa merge — KHÔNG dùng số 11, KHÔNG sửa dòng ST-11)"
depends_on: []
---

## Bối cảnh

WP5 dựng biểu đồ "Cơ cấu lệnh công tác" (số phiếu sửa chữa / kiểm tra / khảo sát theo tiến độ). FE đã chốt (06/11/2026):
backend trả đếm **thô** theo `wo_status` × `task_kind`; FE tự gộp thành Hoàn thành / Đang làm / Chờ. Không nhãn tiếng Việt, không
tổng thừa ở cấp trên.

## Quyết định đã chốt (Mỹ + FE, 06/11/2026) — không phải D-item

- Endpoint: `GET /api/v1/statistics/work-orders`. Capability **`ReadStatistics`** (đã có, ST-8). Không `field_engineer`.
- Query:
  - `group_by` — CSV, giá trị hợp lệ **`commune`, `task_kind`** (wire snake_case, qua `WireEnum.ParseCsv`). `segment` → 400 (như `repair-timeliness`, ST-9).
  - `commune_id` — lặp được, tùy chọn, qua `CommuneFilter.Narrow` (không bắt buộc).
  - `segment_id` — tùy chọn, trim, rỗng = không lọc. Phiếu khớp khi **`work_order.segment_id == X` HOẶC tồn tại dòng `work_order_segment` (cùng phiếu, `segment_id == X`)**. Dùng `Any(...)` / `EXISTS`, KHÔNG `JOIN` làm nhân dòng. Phiếu survey nhiều tuyến vì thế khớp với từng tuyến của nó; điều này nghĩa là cộng số liệu của nhiều lần gọi theo tuyến có thể đếm lặp — ghi rõ trong ST-12.
  - `from` / `to` — `DateOnly`, tùy chọn (binder ISO sẵn có), lọc theo **`work_order.scheduled_date`** (`>= from`, `<= to`, bao cả hai đầu). Có `from` hoặc `to` ⇒ phiếu `scheduled_date` NULL **bị loại**. Không truyền gì ⇒ tính **tất cả**, kể cả chưa lên lịch. **Không có cửa sổ mặc định.** Ngoài dải `EarliestNight…LatestNight` hoặc `from > to` ⇒ 400 `VALIDATION_FAILED` với `details.field` đúng tên, như `repair-timeliness` — dùng lại hằng và cách tạo lỗi sẵn có, không chép.
  - `format=csv` — như hai endpoint kia (W-5, `[CsvExport]`, `CsvExport.IsRequested`).
- Response (JSON snake_case):
  - `as_of` (UTC), `group_by` (mảng tên chiều đã yêu cầu theo thứ tự nhập, `[]` khi không nhóm), `rows[]`.
  - Mỗi hàng, **khoá luôn có mặt**: `commune_id` (null nếu không nhóm theo `commune`), `task_kind` (null nếu không nhóm theo `task_kind`), rồi sáu đếm `open, assigned, in_progress, done, verified, cancelled`, rồi `total` (= tổng sáu số; như `pole_count` của fixture-status). KHÔNG `label`, KHÔNG tổng/tỉ lệ ở cấp ngoài.
  - Chỉ trả nhóm có dữ liệu. Không `group_by` ⇒ tối đa **một** hàng (cả hai khoá null); không có phiếu ⇒ `rows: []`.
  - Thứ tự hàng xác định: `commune_id` (độ dài rồi chuỗi, như fixture-status), rồi `task_kind` theo thứ tự `repair, inspection, survey`.
- Đếm theo **phiếu**, không theo `case_id` (ST-7); không tách `data_source` (phiếu không mang trường này, ST-7).
- Nằm trong `StatisticsController` / `StatisticsService` hiện có; gốc truy vấn là `WorkOrder` đã scope xã.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/commune-scope.md` + `docs/agents/traps/testing.md` + `docs/agents/traps/faults-workorders.md`
- `docs/contract-drift.md` mục ST-1…ST-10 và W-5 (CSV)
- `src/LuxMap.Modules.Statistics/*.cs` (cả controller, service, contracts), `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs` (`WorkOrderSegment`)
- `tests/LuxMap.Api.Tests/StatisticsTests.cs`, `RoleCapabilityMatrixTests.cs` / `CapabilityPolicyCoverageTests` (xem endpoint mới có làm test nào đỏ / cần thêm dòng literal không)

## Yêu cầu

1. Contracts: `StatisticsDimension` thêm `TaskKind` — **cẩn thận**: `fixture-status` và `repair-timeliness` đang dùng enum này, phải vẫn 400 với `task_kind` (thêm test chứng minh, hoặc dùng enum riêng nếu sạch hơn; nêu lựa chọn trong results). `WorkOrderStatusStatistics` + `WorkOrderStatusRow` (record `required` init, XML-doc ngắn như các record cạnh nó).
2. Service `WorkOrdersAsync(query, ct)`: một truy vấn gom nhóm ở DB (đếm có điều kiện theo `WoStatus`), KHÔNG kéo phiếu về bộ nhớ. Enum đếm so sánh bằng `WorkOrderStatus.*`, không chuỗi.
3. Controller action + `[ProducesResponseType]` đủ 200/400/403/413 như các action bên cạnh, XML-doc `<remarks>` nêu mốc `scheduled_date`, quy tắc phiếu chưa lên lịch, quy tắc khớp tuyến (kể cả survey nhiều tuyến).
4. Test (đọc tay, assert giá trị cụ thể) trong `StatisticsTests.cs`:
   - không nhóm: một hàng, sáu đếm đúng, `total` đúng, cả ba `task_kind` cùng có mặt.
   - `group_by=task_kind` ba hàng đúng thứ tự `repair, inspection, survey`; `group_by=commune,task_kind` với hai xã trong scope.
   - `from/to`: phiếu `scheduled_date` NULL bị loại khi có `from` hoặc `to`, vẫn có khi không truyền; cả hai đầu bao gồm; `from > to` → 400; ngoài dải → 400; ngày sai định dạng → 400.
   - `segment_id`: phiếu repair khớp qua `work_order.segment_id`; phiếu survey `segment_id` NULL khớp qua `work_order_segment`; phiếu survey **hai tuyến** hiện ở cả hai tuyến và KHÔNG nhân đôi dòng khi lọc một tuyến; tuyến không có phiếu → 200 `rows: []`.
   - scope: xã ngoài scope người gọi không rò số; `field_engineer` → 403 `ROLE_FORBIDDEN`.
   - `format=csv` trả CSV đúng tên cột; `format=xml` → 400.
   - `group_by=segment` → 400; `task_kind` trên hai endpoint cũ vẫn 400.
5. **Phá thử** (CLAUDE.md): (a) bỏ nhánh `work_order_segment` trong lọc tuyến → test survey đỏ; (b) bỏ lọc `scheduled_date` → test `from/to` đỏ; (c) bỏ chuẩn hoá NULL-loại-khi-có-bound (đổi sang `?? from`/coalesce) nếu có thể → đỏ. Khôi phục bằng `cp`, không `mv`. Sandbox của bạn chặn test (xem bên dưới) — vẫn **viết** các phá thử vào results như "chưa chạy, Claude sẽ chạy" kèm đúng dòng cần phá.
6. Tài liệu:
   - `docs/contract-drift.md`: thêm dòng **ST-12** ngay sau dòng ST-10 (không sửa dòng cũ; ST-11 do branch khác thêm). Ghi: cộng thêm / SELF-SIGNED, hình dạng, mốc `scheduled_date`, phiếu chưa lên lịch, lọc tuyến qua `work_order_segment` + hệ quả đếm lặp khi cộng theo tuyến, không `data_source` (ST-7), CSV.
   - OpenAPI: chạy lại theo `.ai/context/commands.md` (xuất `luxmap-v1.json` → `gen_consolidated_spec.py` → redocly lint), commit hai file sinh. Nếu `gen_consolidated_spec.py` có danh sách operation cứng, cập nhật theo `docs/agents/traps/assets.md` BE-REVIEW-02 số 8.
   - `tracking.html`: thêm thẻ như thẻ STAT-SEGMENT / ACCOUNT-ONE-COMMUNE đang có (ngày, task, file, commit dự kiến).

Tiêu chí xong:

- [ ] `dotnet build --disable-build-servers` không lỗi / warning mới
- [ ] Mọi test mới + toàn suite xanh (Claude chạy ngoài sandbox)
- [ ] `git diff --stat` chỉ gồm: Statistics module (3 file), `StatisticsTests.cs` và test capability nếu buộc phải, drift, hai OpenAPI (+ generator nếu cần), `tracking.html`, `.ai/tasks/STAT-WO.md`, `.ai/results/STAT-WO.md`

## KHÔNG ĐƯỢC làm

- Không migration; không đổi schema, index, enum công khai (`wo_status`, `task_kind`), response shape của endpoint khác.
- Không thêm `label`, không tính tỉ lệ / tổng cấp ngoài, không gom "Hoàn thành / Đang làm / Chờ" ở backend.
- Không sửa `docs/api-contract-v1.1.md`. Không chạm citizen, `fixture-status`, `repair-timeliness` (ST-11 đang ở branch khác).
- Không `JOIN` làm nhân dòng phiếu; không kéo phiếu về bộ nhớ để đếm.
- Không tạo capability mới. Không chạy test trên `luxmap_dev`. Không `git push`; không `git add -A` / `git add .`.
- Không tự chọn khác "Quyết định đã chốt"; chỗ mơ hồ → D-item cuối results.

## Giới hạn sandbox (đã biết từ lần trước)

Sandbox của bạn **không chạy được test** (VSTest bind socket bị chặn, Docker bị từ chối) và **không ghi được `.git/index.lock`**.
Đừng cố vòng qua. Làm: viết code + test + docs, `dotnet build --disable-build-servers -m:1 -p:UseSharedCompilation=false`,
sinh OpenAPI + lint nếu chạy được, ghi `.ai/results/STAT-WO.md` **ngắn gọn** (đã làm gì, output build thật, D-item, danh sách phá thử
chưa chạy). **Không dán nội dung log dài** vào results. KHÔNG commit — để nguyên cây làm việc, Claude sẽ chạy test, phá thử và commit.
