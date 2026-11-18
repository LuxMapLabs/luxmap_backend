---
ticket: ASSET-WORKBOOK-MULTISHEET
title: Đổi workbook kiểm kê từ MỘT sheet phẳng sang MỖI LOẠI TÀI SẢN MỘT SHEET trong một file .xlsx (import + export)
status: ready
phase: 2
owner: codex
branch: feat/ASSET-WORKBOOK-multisheet
contract_refs:
  - "drift ASSET-IMPORT-WORKBOOK WB-1…WB-11 (bị thay một phần — mục WB-12…WB-20)"
depends_on: [ASSET-IMPORT-WORKBOOK, ASSET-EXPORT-WORKBOOK]
---

## Bối cảnh

#271 / #276 (đã merge, đã deploy 18/11) nạp và xuất kiểm kê bằng **một sheet phẳng**: một dòng một cột đèn, thông tin tuyến /
tủ / mạch lặp lại. Cách đó không biểu diễn được tài sản chưa có cột đèn, hình tuyến / lộ trình mạch, lịch sử bộ đèn, và phải
gộp trùng + dò mâu thuẫn. Mỹ chọn (18/11/2026) mẫu **mỗi loại tài sản một sheet** theo file `Assets_Template.xlsx` của nhóm:
mỗi tài sản ghi đúng một lần, sheet con tham chiếu sheet cha bằng mã (dropdown). Vẫn **một file, một lần nạp, một transaction**.

Web (WP5) chưa có nút nào gọi hai endpoint workbook, nên đổi định dạng lúc này không làm vỡ consumer nào. Đường dẫn giữ nguyên.

Quyết định đã chốt (Mỹ, 18/11/2026):
1. Hình học là `geom_wkt` (WKT, `POINT(lng lat)` / `LINESTRING(…)`) như CSV và file mẫu — thay WB-5 (`*_lat` / `*_lng`).
2. Cột hệ thống không nhận mà **có dữ liệu** → lỗi, không ghi gì (không âm thầm bỏ dữ liệu).
3. Sheet Bóng đèn nhận bộ **đã tháo** (`removed_date`); nạp lại khớp theo **(cột, `install_date`)**.
4. Import workbook kiểm "cột **mới** không có `geom_wkt` thì bắt buộc `note`". CSV và form không đổi.

## Phải đọc trước

- `CLAUDE.md`; `docs/agents/traps/assets.md` (ASSET-IMPORT-WORKBOOK, ONE-FIXTURE, FIXTURE-NOTE, `updated_by`, POLE-GEOM-NULL,
  ROAD-SEGMENT-GEOM-NULL, TOPO-INFER, CABINET), `commune-scope.md`, `testing.md`
- `docs/templates/README.md` — luật từng cột của 5 file CSV (sheet = file CSV tương ứng, trừ `commune_id` / `data_source`)
- `src/LuxMap.Modules.Assets/Import/*`, `src/LuxMap.Modules.Assets/Export/*`, `tests/LuxMap.Api.Tests/Asset*Workbook*.cs`
- File mẫu của nhóm: `/Users/nhm809/Downloads/Assets_Template.xlsx` (tên sheet, cột, màu, dropdown theo named range)

## Yêu cầu

### 1. Định dạng file

Một `.xlsx`. Sheet nhận diện theo **tên** (so sau trim + chuẩn hoá Unicode NFC, phân biệt hoa thường), **không** theo vị trí:

| Sheet | Loại | Cột (header dòng 1) — `*` bắt buộc có trong header |
|---|---|---|
| `Tuyến đường` | segments | `external_ref`*, `segment_name`*, `road_class`*, `length_m`, `geom_wkt` |
| `Tủ điện` | cabinets | `external_ref`*, `cabinet_name`*, `geom_wkt`* |
| `Mạch điện` | feeders | `external_ref`*, `feeder_name`*, `cabinet_external_ref`, `cabinet_source`, `geom_wkt` |
| `Cột điện` | poles | `external_ref`*, `segment_external_ref`, `feeder_external_ref`, `feeder_source`, `geom_wkt`, `near_sensitive_poi`, `note` |
| `Bóng đèn` | fixtures | `pole_external_ref`*, `fixture_type`*, `power_source`*, `lamp_watt`*, `install_date`*, `lamp_count`, `removed_date`, `warranty_expiry`, `note` |

- Luật từng cột = luật của file CSV cùng loại (`docs/templates/README.md`), **đi qua planner hiện có**, không viết validator thứ hai.
- Sheet nào **vắng** = không nạp loại đó. Không có sheet nào trong 5 tên → lỗi `(sheet: null, row: 1)` "No asset sheet found".
- Sheet khác (vd `Hướng dẫn`) bỏ qua. **Ngoại lệ:** sheet tên `IoT` có ít nhất một dòng dữ liệu → lỗi "IoT import is not supported
  yet" (không âm thầm bỏ). Sheet `IoT` chỉ có header → bỏ qua.
- Cột **`commune_id` / `data_source` có mặt** trong một sheet → lỗi header (hai giá trị này đặt khi upload, mục 2).
- Cột **không nằm trong bảng trên** mà có ít nhất một ô khác trống → lỗi header của sheet đó, nêu tên cột (quyết định 2). Cột lạ
  toàn trống → bỏ qua.
- Dòng trống hoàn toàn bỏ qua; số dòng báo lỗi là **số dòng Excel thật** của sheet đó.
- Giới hạn giữ nguyên ý nghĩa, tính **mỗi sheet**: ≤ 5 000 dòng dữ liệu, ≤ 200 cột, ô ≤ 2 000 ký tự; tổng giải nén ≤ 100 MB.
- Ô ngày: chuỗi `YYYY-MM-DD` hoặc ô ngày Excel (serial nguyên). Ô số đọc Invariant. Ref là chuỗi (ô số `7` đọc là `"7"` — template
  định dạng cột ref là Text).

### 2. Xã và nguồn dữ liệu đặt khi upload

`POST /api/v1/assets/import/workbook`, multipart: `file` (bắt buộc), **`data_source`** (bắt buộc, enum `data_source`, **không mặc
định** — dữ liệu testbed không bao giờ được thành `field` vì quên chọn), `commune_id` (tuỳ chọn).

- `commune_id` vắng → xã duy nhất trong phạm vi người gọi (Quản lý có đúng một xã, ACCT-1). Phạm vi ≠ đúng một xã (vd `*`) và
  vắng `commune_id` → 400 `VALIDATION_FAILED` `details.field = commune_id`. Có `commune_id` → qua `CommuneFilter.Narrow`
  (ngoài phạm vi → 403 `COMMUNE_FORBIDDEN`).
- `data_source` áp cho tuyến, tủ, cột, bộ đèn **tạo hoặc sửa** từ file (mạch không có `data_source`). Sai / vắng → 400.
- Mỗi dòng của mọi sheet nhận `commune_id` / `data_source` này như thể có trong file — planner không đổi.

### 3. Nạp: nguyên tử, thứ tự khoá ngoại

Giữ cơ chế #271: một transaction, năm khoá advisory tăng dần, chạy planner theo `LoadOrder` (tuyến → tủ → mạch → cột → bộ đèn),
`SaveChanges` giữa các lớp; bất kỳ lỗi nào → rollback, `committed = false`, số đếm 0. Dòng con tham chiếu một mã **lỗi ở sheet cha**
không báo thêm "matches nothing" (như #271).

Khác #271 — **bỏ** phần chiếu dòng phẳng ra 5 lớp, gộp trùng giữa dòng, `WorkbookColumns` 26 cột, ánh xạ tên cột: sheet đã dùng
đúng tên cột CSV. `external_ref` trùng **trong cùng sheet** → lỗi ở dòng sau, nêu dòng đầu (CSV hiện gặp thì sao — đọc và giữ cùng
luật nếu đã có).

**Hình học:**
- `Tủ điện`, `Cột điện`: như CSV (tủ bắt buộc; cột trống = cột mới chưa GPS / cột cũ giữ vị trí).
- `Tuyến đường`, `Mạch điện`: ô `geom_wkt` **trống hoặc thiếu cột = giữ** hình / lộ trình đang có (cả mạch — khác CSV, nơi ô trống
  xoá lộ trình mạch). Muốn xoá lộ trình dùng form. Thay `IImportRow.CarriesGeometry` bằng luật này.

**Cột không GPS (quyết định 4):** dòng `Cột điện` tạo cột **mới**, `geom_wkt` trống và `note` trống → lỗi cột `note`
"A pole without geom_wkt needs a note describing where it is". Cột đã có không bị kiểm (đã có vị trí hoặc đã có ghi chú cũ).

**Bộ đèn (quyết định 3) — thay WB-4:** khoá khớp `(pole, install_date)`.
- Chưa có bộ nào của cột với `install_date` đó → **thêm** (dòng có `removed_date` = bản ghi lịch sử, đã tháo). Vẫn tuân ONE-FIXTURE:
  thêm bộ **đang dùng** khi cột đã có bộ đang dùng khác → lỗi như CSV.
- Có đúng một bộ khớp → so `fixture_type`, `power_source`, `lamp_watt`, `lamp_count` (trống = 1), `removed_date`, `warranty_expiry`,
  `note`; ô trống ở `warranty_expiry` / `note` = không nói gì. Bằng hết → `unchanged`, không ghi, không đổi `updated_by`. Khác →
  lỗi ở cột đó "change a fitting through the asset form". Workbook **không bao giờ sửa** bộ đã có.
- Có > 1 bộ cùng `(pole, install_date)` → lỗi "ambiguous".
- `POST /assets/import/fixtures` (CSV) **giữ** insert-only.

### 4. Kết quả

`WorkbookImportResult` giữ hình dạng, **trừ** phần tử lỗi / cảnh báo thêm **`sheet`** (tên sheet, `null` cho lỗi cấp file):
`{ sheet, row, column, message }` (schema mới `WorkbookRowError`; `ImportRowError` của CSV **không đổi**). Sắp theo thứ tự sheet
trong `LoadOrder`, rồi `row`, rồi `column`.

### 5. Export

`GET /api/v1/assets/export/workbook?commune_id=…` — quyền, phạm vi, tên file giữ nguyên (WB-7).

- Sheet theo thứ tự: `Hướng dẫn` (nội dung như template), `Tuyến đường`, `Tủ điện`, `Mạch điện`, `Cột điện`, `Bóng đèn`, và
  `Không xuất` nếu có. Cột đúng bảng mục 1, **không** có `commune_id` / `data_source`.
- **Mọi** tài sản trong phạm vi, kể cả tuyến / tủ / mạch không có cột; hình tuyến / mạch xuất WKT; bộ đèn **cả đang dùng lẫn đã tháo**.
  Mục tiêu: xuất → nạp lại nguyên file (cùng xã, cùng `data_source`) = mọi lớp `unchanged`, dấu cập nhật không đổi.
- Lọc nhiều xã: export cho phép (`commune_id` lặp), nhưng file nhiều xã **không nạp lại được** (upload nhận một xã) — ghi rõ ở sheet
  `Hướng dẫn` của file xuất và README.
- `Không xuất` (`kind`, `id`, `commune_id`, `reason`): tài sản **không có `external_ref`** (mọi loại); cột đèn thuộc mạch không mã (nạp
  lại sẽ tháo mạch); bộ đèn của cột không mã. Không bỏ im lặng.
- Bất kỳ loại nào > 5 000 dòng trong phạm vi → 413 `EXPORT_TOO_LARGE`.
- Giới hạn còn lại (thay WB-11): mỗi file một `data_source` khi nạp lại — tài sản có nguồn khác nhau trong cùng xã sẽ bị đổi nguồn
  theo giá trị chọn lúc upload. Export ghi số tài sản theo từng `data_source` vào sheet `Hướng dẫn` để người dùng thấy trước.

### 6. Template

- `scripts/make_asset_workbook_template.py` sinh lại `docs/templates/assets-workbook.xlsx` (+ `.example.xlsx`) theo bố cục file của
  nhóm: sheet `Hướng dẫn` đầu tiên, 5 sheet tài sản, header bắt buộc xanh đậm / tuỳ chọn xám, cột ref định dạng Text, dropdown enum
  và dropdown mã cha qua named range (`ds_tuyen_duong`, `ds_tu_dien`, `ds_mach_dien`, `ds_cot_dien`). **Không** có sheet `IoT`,
  **không** có cột `note` ở `Tủ điện`.
- Nội dung `Hướng dẫn` sửa so với file của nhóm: nạp **cả file một lần** (không lưu từng sheet ra CSV); chọn xã / nguồn dữ liệu khi
  upload; tất cả-hoặc-không; mã `external_ref` là **mã kiểm kê của xã**, không dùng dạng `POLE-0001` / `SEG-001` (trùng định dạng ID
  hệ thống); cột không GPS phải có `note`.
- Ví dụ trong `.example.xlsx` dùng mã kiểu `CTA-001`, `TUYEN-A` như `*.example.csv`.

## Tiêu chí xong

- [ ] `dotnet build --disable-build-servers` không warning mới; `dotnet test` xanh (mã thoát 0).
- [ ] Test import (PostGIS, `AssetDatabaseCollection`): đủ 5 sheet → `committed`, liên kết đúng; một lỗi ở sheet cuối → DB không
  đổi; nạp lại y hệt → `unchanged` + dấu không đổi; sheet vắng; không sheet tài sản; sheet `IoT` có dữ liệu → lỗi; `IoT` rỗng → bỏ
  qua; cột `commune_id` trong sheet → lỗi; cột lạ có dữ liệu → lỗi, toàn trống → qua; `data_source` vắng → 400; `commune_id` vắng
  với Quản lý một xã → dùng xã đó; ngoài phạm vi → 403; tuyến + mạch giữ hình khi ô trống / thiếu cột; cột mới không GPS không
  note → lỗi, cột cũ không bị kiểm; bộ đèn: lịch sử + đang dùng, khớp / khác / mơ hồ / ONE-FIXTURE; `sheet` + số dòng Excel đúng.
- [ ] Test export: round-trip đủ (gồm tuyến không cột, tủ không mạch, hình tuyến + lộ trình mạch, bộ đã tháo); `Không xuất`; 413;
  thứ tự sheet + header == template; enum / ngày / WKT / ref Text đúng.
- [ ] Phá thử (Claude): bỏ rollback; ô `geom_wkt` mạch trống xoá lộ trình; bỏ khớp `(pole, install_date)`; bỏ lỗi cột lạ — mỗi cái
  làm ít nhất một test đỏ.
- [ ] Test template đã commit: nạp `assets-workbook.xlsx` → `committed` 0 dòng; `.example.xlsx` (đổi xã qua form) → `committed`.
- [ ] Code một sheet bị xoá hẳn (`AssetImportWorkbook` phần chiếu dòng, `WorkbookImportRow`, `WorkbookColumns`, test cũ không còn
  đúng); không để code chết.
- [ ] OpenAPI sinh lại: request multipart thêm `data_source`, `commune_id`; `WorkbookRowError`; lint sạch.
- [ ] Drift WB-12…WB-20 thay các mục bị thay; README + traps cập nhật.

## KHÔNG ĐƯỢC làm

- Không migration (ghi chú tủ điện, import IoT là ticket riêng).
- Không đổi 5 endpoint CSV / GeoJSON `{kind}` (kể cả luật ô `geom_wkt` mạch trống = xoá).
- Không đổi luật kiểm cột không GPS ở CSV / form.
- Không thêm package. Không `IgnoreQueryFilters` ở đường đọc. Không sửa `CLAUDE.md`, Contract, không sửa tay `docs/openapi/*.json`.
