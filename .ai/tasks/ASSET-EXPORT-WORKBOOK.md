---
ticket: ASSET-EXPORT-WORKBOOK
title: Xuất kiểm kê tài sản ra MỘT sheet Excel đúng mẫu import workbook (vòng xuất → sửa → nạp lại)
status: ready
phase: 2
owner: codex
branch: feat/ASSET-IMPORT-workbook
contract_refs:
  - "drift ASSET-IMPORT-WORKBOOK WB-1…WB-6 (mẫu sheet), WB-7…WB-11 (ticket này)"
  - "drift BE-31 W-6 / W-7 — EXPORT_TOO_LARGE, tên file theo ngày"
depends_on: [ASSET-IMPORT-WORKBOOK]
---

## Bối cảnh

ASSET-IMPORT-WORKBOOK cho Quản lý nạp cả sổ kiểm kê từ một sheet Excel. Chiều ngược lại chưa có: không có endpoint nào xuất
danh mục tài sản (CSV hay Excel). Ticket này thêm export ra **đúng mẫu sheet của import**, để người dùng tải về, sửa trong Excel,
nạp lại qua `POST /assets/import/workbook`, và **mọi dòng không sửa nạp lại là `unchanged`**. Consumer: WP5 (nút "Tải kiểm kê
Excel" ở màn tài sản). Cùng PR #271.

Ba quyết định đã chốt (Mỹ, 18/11/2026): quyền `ReadNetwork` (cả 4 vai trò, trong phạm vi xã); quá 5 000 cột → **413**
`EXPORT_TOO_LARGE`; làm trên nhánh của PR #271.

## Phải đọc trước

- `.ai/tasks/ASSET-IMPORT-WORKBOOK.md` mục 3 (cột sheet, luật gộp trùng, bộ đèn) và `.ai/results/ASSET-IMPORT-WORKBOOK.md`
- `docs/contract-drift.md` mục `ASSET-IMPORT-WORKBOOK` (WB-1…WB-11, cuối file)
- `CLAUDE.md`; `docs/agents/traps/assets.md` (mục ASSET-IMPORT-WORKBOOK, ONE-FIXTURE, `updated_by`), `commune-scope.md`,
  `spatial-map.md` (không `ORDER BY` ID trần), `testing.md`
- `src/LuxMap.Modules.Assets/Import/*` (đặc biệt `WorkbookDocument`, `WorkbookImportRow`, `AssetImportWorkbook`),
  `src/LuxMap.Modules.Assets/Crud/AssetsController.cs` (`Narrow`, `ReadNetwork`), `src/LuxMap.Shared/Csv/CsvExport.cs`
  (`FileName`, `MaxRows`, `EXPORT_TOO_LARGE`), `src/LuxMap.Modules.Statistics/StatisticsController.cs` (cách trả file)
- `scripts/make_asset_workbook_template.py` (header, dropdown, định dạng ngày của template)

## Yêu cầu

### 1. Endpoint

`GET /api/v1/assets/export/workbook?commune_id=COM-001&commune_id=…` — `[Authorize(Policy = LuxMapPolicies.ReadNetwork)]`.

- `commune_id` tuỳ chọn, lặp được; đi qua **`CommuneFilter.Narrow`** như `GET /assets/poles` (ngoài phạm vi → 403
  `COMMUNE_FORBIDDEN`). Vắng = mọi xã trong phạm vi người gọi. Đọc **qua query filter**, không `IgnoreQueryFilters`.
- Đếm cột đèn trong phạm vi trước khi dựng file: **> 5 000** (`WorkbookDocument.MaxRows`) → **413** `EXPORT_TOO_LARGE` (mã có
  sẵn), message gợi ý lọc theo `commune_id`. Không có cột nào → 200 với file chỉ có header.
- 200, `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `Content-Disposition` attachment
  tên `assets-<yyyy-MM-dd>.xlsx` (ngày UTC hôm nay, cùng cách `CsvExport.FileName`).
- OpenAPI: 200 khai content type xlsx dạng binary, 403, 413. Một schemaId một kiểu.

### 2. Một định nghĩa cột

Thêm **một** danh sách cột có thứ tự trong C# (vd `WorkbookColumns`) — đúng 26 cột, đúng thứ tự header của
`docs/templates/assets-workbook.xlsx` — và dùng nó cho export. Import dùng lại nó cho cột bắt buộc ở header nếu đổi được mà
không đổi hành vi. Test (mục 5) khẳng định header export == header template == danh sách này.

### 3. Nội dung sheet 1 `import` — một dòng một cột đèn **xuất được**

| Cột sheet | Lấy từ |
|---|---|
| `commune_id`, `data_source` | `pole.commune_id`, `pole.data_source` |
| `segment_ref`, `segment_name`, `road_class`, `segment_length_m` | Tuyến của cột (nếu có **và** có `external_ref`); trống nếu cột không có tuyến hoặc tuyến không có mã (nạp lại: trống = giữ tuyến) |
| `cabinet_ref`, `cabinet_name`, `cabinet_lat`, `cabinet_lng` | Tủ của mạch của cột (nếu có **và** có `external_ref`); trống nếu không (nạp lại: trống = giữ tủ, CAB-6) |
| `feeder_ref`, `feeder_name`, `cabinet_source` | Mạch của cột; `cabinet_source` = `feeder.cabinet_source` |
| `pole_ref`, `pole_lat`, `pole_lng`, `near_sensitive_poi`, `pole_note`, `feeder_source` | Cột; toạ độ từ `geom` (null → trống); `feeder_source` = `pole.feeder_source` |
| `fixture_type`, `power_source`, `lamp_watt`, `lamp_count`, `install_date`, `warranty_expiry`, `fixture_note` | Bộ đèn **đang dùng** (`removed_date IS NULL`, tối đa một — ONE-FIXTURE); không có → cả nhóm trống. Bộ đã tháo không xuất |

- Enum ghi **chuỗi thường trên dây** (`WireEnum.Name`), không tên C#. Bool `true`/`false` dạng chữ. Số (`lat`, `lng`,
  `segment_length_m`, `lamp_watt`, `lamp_count`) là **ô số**, toạ độ đủ chính xác để nạp lại ra **đúng** giá trị double đang lưu.
  Ngày là **ô ngày** (serial OADate, định dạng `yyyy-mm-dd`). Văn bản (ref, tên, ghi chú) là ô chuỗi — `pole_ref` kiểu `001`
  phải giữ số 0 đầu.
- Ô trống: **không ghi ô** (đừng ghi chuỗi rỗng).
- Thứ tự dòng: `commune_id`, rồi `segment_ref` (tuyến không mã / không tuyến xếp cuối), rồi cột theo
  `created_at, length(pole_id), pole_id` — **không** `ORDER BY` ID trần.
- Header đông cứng (freeze dòng 1), dropdown giống template (`data_source`, `road_class`, `fixture_type`, `power_source`,
  `cabinet_source`, `feeder_source`, `near_sensitive_poi`), giá trị lấy từ enum C#, không gõ tay lần hai.

### 4. Cột không xuất được — sheet 2 `Không xuất`, không bao giờ bỏ im lặng

Cột đèn **không** vào sheet 1 khi nạp lại dòng đó sẽ hỏng dữ liệu hoặc lỗi:

- cột **không có `external_ref`** — import nhận diện cột bằng `pole_ref`; để trống thì lỗi, điền `POLE-0001` thì tạo cột trùng;
- cột có mạch mà **mạch không có `external_ref`** — `feeder_ref` trống khi nạp lại sẽ **tháo** cột khỏi mạch.

Những cột đó liệt kê ở sheet 2 `Không xuất` (chỉ tạo khi có ít nhất một): `pole_id`, `commune_id`, `reason` (tiếng Việt, nêu
rõ thiếu mã gì và cách sửa: gán mã qua form tài sản). Sheet 2 nằm **sau** sheet 1 để import (chỉ đọc sheet đầu) bỏ qua nó.

### 5. Giới hạn đã biết — ghi vào drift, không sửa ở ticket này

Một dòng chỉ có một `data_source`. Tuyến / tủ / bộ đèn có `data_source` **khác** cột của nó: export vẫn ghi `data_source` của
cột; nạp lại sẽ báo mâu thuẫn gộp trùng hoặc đổi `data_source` của tài sản cha. Tuyến / tủ / mạch **không có cột nào** không xuất
hiện trong sheet (import không xoá nên nạp lại không mất chúng).

Tiêu chí xong — mỗi dòng kiểm chứng được bằng một lệnh:

- [ ] `dotnet build --disable-build-servers` không warning mới.
- [ ] `dotnet test` xanh (mã thoát 0), có `AssetExportWorkbookTests` trong `AssetDatabaseCollection` phủ tối thiểu:
  1. **Round-trip:** nạp sheet đầy đủ (tuyến, tủ, mạch, 4 cột: có/không GPS, có/không bộ đèn, `lamp_count = 2`, ghi chú có dấu
     phẩy và tiếng Việt, `pole_ref` dạng `007`) → export lọc đúng xã → nạp lại **nguyên file** → `committed`, mọi lớp
     `inserted = updated = 0`, `updated_at` / `updated_by` không đổi;
  2. giá trị từng ô đúng (enum chữ thường, ngày `yyyy-mm-dd`, toạ độ bằng đúng double đang lưu, ô trống không có);
  3. cột không `external_ref` và cột có mạch không mã → không ở sheet 1, có ở sheet `Không xuất` kèm `pole_id` + lý do;
     không có trường hợp nào thì không có sheet 2;
  4. bộ đèn đã tháo không xuất; cột chỉ có bộ đã tháo → nhóm bộ đèn trống;
  5. thứ tự dòng đúng mục 3, kể cả `POLE-9999` / `POLE-10000` cùng mẻ (assert **có thứ tự**);
  6. `commune_id` ngoài phạm vi → 403 `COMMUNE_FORBIDDEN`; người không đăng nhập → 401; cả 4 vai trò → 200;
  7. > 5 000 cột trong phạm vi → 413 `EXPORT_TOO_LARGE` (dựng bằng hạ ngưỡng có kiểm soát hoặc insert hàng loạt rồi dọn — theo
     `testing.md`; không để sót dữ liệu);
  8. header export == header `docs/templates/assets-workbook.xlsx` == danh sách cột C#; content type + tên file đúng.
- [ ] Phá thử (Claude làm): ghi toạ độ làm tròn → test 1 đỏ; bỏ chuyển cột thiếu mã sang sheet 2 → test 3 đỏ.
- [ ] `CapabilityPolicyCoverageTests` xanh.
- [ ] Spec sinh lại (thêm SUMMARY vào `gen_consolidated_spec.py`), lint sạch; diff spec chỉ thêm path này.
- [ ] `docs/templates/README.md` mục "Một sheet Excel" thêm đoạn export (endpoint, quyền, 413, sheet `Không xuất`, giới hạn mục 5).

## KHÔNG ĐƯỢC làm

- Không migration, không đổi schema, không thêm capability mới, không sửa `LuxMapPolicies.Matrix`.
- Không đổi hành vi import (workbook hay `{kind}`); refactor dùng chung danh sách cột không được đổi kết quả test import hiện có.
- Không thêm package (đã có `DocumentFormat.OpenXml`). Không `IgnoreQueryFilters` trong đường đọc export.
- Không xuất `pole_id` / `segment_id` hệ thống vào cột `*_ref` của sheet 1.
- Không sửa `CLAUDE.md`, `docs/api-contract-v1.1.md`, không sửa tay `docs/openapi/*.json`. Không commit.

## Kết quả

Ghi `.ai/results/ASSET-EXPORT-WORKBOOK.md`: file đã đổi, cách giữ một định nghĩa cột, lệnh đã chạy và kết quả, điều chưa làm,
câu hỏi mở. Ngắn.
