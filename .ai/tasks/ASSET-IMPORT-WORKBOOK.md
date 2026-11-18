---
ticket: ASSET-IMPORT-WORKBOOK
title: Nạp toàn bộ kiểm kê tài sản từ MỘT sheet Excel (.xlsx), nguyên tử, một transaction
status: done
phase: 2
owner: claude
branch: feat/ASSET-IMPORT-workbook
contract_refs:
  - "mục 0 — quy ước lỗi, enum chuỗi thường, ngày YYYY-MM-DD"
  - "drift BE-12a — nhóm /assets/import/{kind} và hình dạng ImportResult (ngoài Contract)"
depends_on: []
---

## Bối cảnh

Xã bàn giao sổ kiểm kê dưới dạng **một bảng Excel phẳng**: mỗi dòng là một cột đèn, kèm tuyến, tủ điện, mạch và bộ đèn
của nó. Hệ thống hiện chỉ nhận 5 file CSV / GeoJSON riêng (`POST /assets/import/{segments|cabinets|feeders|poles|fixtures}`),
người dùng phải tự tách sổ thành 5 file đúng thứ tự khoá ngoại. Ticket này thêm **một endpoint nhận một file `.xlsx`, một
sheet**, tách thành 5 lớp ở server, kiểm toàn bộ rồi ghi **trong một transaction, có lỗi thì không ghi gì**. Consumer: WP5
(modal import tài sản). Demo Review 2 dùng luồng này ở Flow 01.

Bốn quyết định đã chốt (Mỹ, 18/11/2026): (1) all-or-nothing; (2) bộ đèn nạp lại so khớp — giống thì `unchanged`, khác thì lỗi
dòng; (3) thư viện `DocumentFormat.OpenXml`; (4) toạ độ là hai cột `lat` / `lng`.

## Phải đọc trước

- `CLAUDE.md` (lõi), `docs/agents/traps/assets.md` (mục *Sáu quy tắc chốt ở BE-12a*, POLE-NOTE, `updated_by`, CABINET,
  TOPO-INFER, POLE-SEGMENT-NULL, POLE-GEOM-NULL, ROAD-SEGMENT-GEOM-NULL, ONE-FIXTURE, FIXTURE-NOTE),
  `docs/agents/traps/commune-scope.md`, `docs/agents/traps/testing.md`
- `docs/templates/README.md` — luật từng cột của 5 file CSV (một phần đã lỗi thời so với code; **code là chuẩn**)
- `src/LuxMap.Modules.Assets/Import/*` — toàn bộ, đặc biệt `AssetImportService` (planner từng lớp, khoá advisory,
  `ReferenceIndexAsync`, `Resolve`, `OccupiedPolesAsync`) và `ImportRow.cs` (`IImportRow`, `ImportRowReader`)
- `tests/LuxMap.Api.Tests/AssetImport*.cs` — fixture, collection, cách dựng file trong test
- `docs/contract-drift.md` mục `ASSET-IMPORT-WORKBOOK` (cuối file) — bản luật bề mặt API của ticket này

## Yêu cầu

### 1. Endpoint

`POST /api/v1/assets/import/workbook`, `multipart/form-data`, trường `file`. Policy `ManageAssets` (như `{kind}`).
`[RequestSizeLimit]` = `AssetImportController.MaxUploadBytes` (10 MB).

- Route literal `workbook` phải thắng route `{kind}` — kiểm bằng test, không giả định.
- Nhận khi **đuôi `.xlsx`** VÀ **4 byte đầu là `PK\x03\x04`** (zip). Khác → **415** `UNSUPPORTED_MEDIA_TYPE`, message
  "Upload a .xlsx workbook." (`.xls`, `.csv`, file đổi đuôi đều 415). Zip hỏng / không mở được bằng OpenXml → **400**
  `VALIDATION_FAILED`, không 500.
- Response **200** + `WorkbookImportResult` (mục 4) cả khi có lỗi dòng — cùng lý do với `ImportResult`.

### 2. Đọc sheet

- Đọc **worksheet đầu tiên theo thứ tự trong workbook** (`workbook.xml` `<sheets>`), bỏ qua mọi sheet khác (template có sheet
  "Hướng dẫn" thứ hai).
- Dòng 1 là header. Tên cột so khớp **sau khi trim, phân biệt hoa thường** (Ordinal, như `CsvDocument`). Cột lạ bỏ qua. Cột
  trùng tên → lỗi dòng 1, không ghi gì.
- **Dòng mà mọi ô trống** (sau trim) → bỏ qua, không đếm, không lỗi (Excel hay để lại dòng định dạng rỗng).
- Số dòng báo lỗi = **số dòng Excel thật** (`<row r="…">`), không phải chỉ số sau khi bỏ dòng trống. Ô thiếu trong XML (cột
  thưa, `<c r="F7">`) phải đặt đúng cột theo tham chiếu ô, không theo thứ tự xuất hiện.
- Kiểu ô: shared string, inline string, number, boolean (`1`/`0` → `true`/`false`), công thức → **giá trị cache** (`<v>`);
  ô lỗi (`t="e"`, `#N/A`…) → lỗi dòng ở cột đó. Số đọc bằng `InvariantCulture`; số nguyên Excel lưu `100` hoặc `100.0` đều là
  `100`.
- **Ngày**: ô số ở cột ngày (`install_date`, `warranty_expiry`) là serial OADate → `DateOnly` (không cần đọc style); ô chuỗi
  phải đúng `YYYY-MM-DD`. Serial có phần giờ ≠ 0 → lỗi dòng.
- **Giới hạn chống file bom** (đọc streaming bằng `OpenXmlReader` / SAX, không DOM cả sheet): tối đa **5 000 dòng dữ liệu**
  không trống, tối đa **200 cột**, mỗi ô tối đa **2 000 ký tự**. Vượt → lỗi dòng 1 (dòng / cột) hoặc lỗi ô, dừng đọc sớm, không
  ghi gì. Bảng shared string cũng đọc streaming và bị chặn cùng giới hạn độ dài.

### 3. Cột của sheet và cách tách thành 5 lớp

Một dòng = **một cột đèn**, tuỳ chọn kèm bộ đèn của nó. `*` = bắt buộc có trong header **và** có giá trị.

| Nhóm | Cột | Luật |
|---|---|---|
| Chung | `commune_id`*, `data_source`* | `data_source` áp cho tuyến, tủ, cột, bộ đèn tạo / sửa từ dòng này (feeder không có `data_source`) |
| Tuyến | `segment_ref`, `segment_name`, `road_class`, `segment_length_m` | Có `segment_ref` ⇒ `segment_name`, `road_class` bắt buộc. Sheet **không có hình tuyến**: tuyến mới chưa hình; tuyến đã có **giữ hình**; `segment_length_m` theo luật ASSET-SEGMENT-LENGTH-NULL (ô trống ở tuyến đã có ⇒ giữ) |
| Tủ điện | `cabinet_ref`, `cabinet_name`, `cabinet_lat`, `cabinet_lng` | Có `cabinet_ref` ⇒ ba cột kia bắt buộc (tủ luôn có vị trí, CAB-1) |
| Mạch | `feeder_ref`, `feeder_name`, `cabinet_source` | Có `feeder_ref` ⇒ `feeder_name` bắt buộc. Mạch thuộc tủ ở `cabinet_ref` cùng dòng; `cabinet_ref` trống ⇒ **giữ** tủ đang có (CAB-6). `cabinet_source` là nhãn quan hệ mạch → tủ (TOPO-INFER) |
| Cột đèn | `pole_ref`*, `pole_lat`, `pole_lng`, `near_sensitive_poi`, `pole_note`, `feeder_source` | Cột thuộc tuyến / mạch ở `segment_ref` / `feeder_ref` cùng dòng (trống ⇒ luật hiện tại của import cột). `lat`/`lng` phải có cả hai hoặc trống cả hai; trống ⇒ cột mới chưa GPS, cột đã có giữ vị trí. `feeder_source` là nhãn cột → mạch |
| Bộ đèn | `fixture_type`, `power_source`, `lamp_watt`, `lamp_count`, `install_date`, `warranty_expiry`, `fixture_note` | Cả nhóm tuỳ chọn; **điền bất kỳ ô nào** ⇒ `fixture_type`, `power_source`, `lamp_watt`, `install_date` bắt buộc. Không có `removed_date`: sheet là kiểm kê **đang dùng** |

Toạ độ: `lat` ∈ [-90, 90], `lng` ∈ [-180, 180], số hữu hạn → `Point(lng, lat)` SRID 4326 (thứ tự `[lng, lat]`). Cảnh giác dấu
phẩy thập phân kiểu Việt (`10,97`): ô chuỗi không parse được bằng Invariant → lỗi dòng, **không** tự đổi `,` thành `.`.

**Gộp trùng.** Tuyến / tủ / mạch được nhận diện bằng `(commune_id, *_ref)` và lặp lại trên nhiều dòng:

- Dòng **đầu tiên** nhắc tới một ref định nghĩa nó. Dòng sau: ô **trống** = không nói gì (không mâu thuẫn); ô **có giá trị khác**
  dòng đầu → lỗi ở dòng sau, cột đó, message nêu số dòng đã định nghĩa giá trị trước. So sánh sau trim, Ordinal; số so theo giá trị.
- Mạch xuất hiện với hai `cabinet_ref` khác nhau (cả hai khác trống) → lỗi như trên.
- `pole_ref` trùng trong cùng `commune_id` → lỗi ở mọi dòng sau dòng đầu ("một dòng một cột đèn").

**Bộ đèn nạp lại (quyết định 2).** Cột chưa có bộ đèn đang dùng → thêm. Cột đã có bộ đèn đang dùng (`removed_date IS NULL`):
so `fixture_type`, `power_source`, `lamp_watt`, `lamp_count` (ô trống = 1), `install_date`, `warranty_expiry`, `fixture_note`;
ô **trống** ở `warranty_expiry` / `fixture_note` = không nói gì. Mọi giá trị có mặt **bằng** → `unchanged`, không ghi, không đổi
`updated_by`. Có giá trị **khác** → lỗi dòng ở cột đó: "Pole already has a fitting in service with a different <col>; replace a
fitting through the asset form." Không bao giờ cập nhật hay thay bộ đèn đang dùng qua workbook. **Không đổi** hành vi insert-only
của `POST /assets/import/fixtures`.

### 4. Nguyên tử và kết quả

- Lấy **cả năm** khoá `pg_advisory_xact_lock` của import theo **thứ tự tăng dần** (cùng khoá với `{kind}`), trong **một**
  transaction, trước mọi lần đọc.
- Chạy năm lớp theo `ImportKindExtensions.LoadOrder`, **tái dùng planner hiện có** (không viết lại luật kiểm tra từng lớp), gọi
  `SaveChanges` sau mỗi lớp **bên trong** transaction để lớp sau resolve được ID vừa sinh qua `ReferenceIndexAsync`. Nếu có lỗi
  ở bất kỳ lớp nào (kể cả lỗi gộp trùng phát hiện trước khi chạy planner): **rollback**, `ChangeTracker.Clear()`, trả kết quả
  `committed = false`, mọi số đếm `inserted/updated/unchanged = 0`. Lỗi ở lớp trước **vẫn chạy tiếp** các lớp sau để báo đủ lỗi
  một lần, miễn không sinh lỗi dây chuyền giả: dòng tham chiếu một ref mà chính ref đó đã lỗi ở lớp trước → **không** báo thêm
  "matches nothing" cho dòng đó.
- Lỗi khi ghi (deadlock, mất kết nối, `CommuneWriteGuard`) → để nổ như hiện tại (500 / 403 qua middleware), không nuốt.
- Lỗi báo bằng **tên cột của sheet** (`segment_ref`, `pole_lat`…), không bằng tên cột nội bộ CSV (`external_ref`, `geom_wkt`).
  Lỗi vị trí ghép từ hai ô (`pole_lat`/`pole_lng`) báo ở cột `pole_lat`. Mọi message tiếng Anh như các lỗi import hiện có.
- Hình dạng (snake_case qua `LuxMapJsonOptions`):

```
WorkbookImportResult {
  committed: bool,
  segments, cabinets, feeders, poles, fixtures: { inserted, updated, unchanged },
  failed,            // số dòng Excel có ít nhất một lỗi
  total_errors, truncated, rows: ImportRowError[],      // cắt ở ImportResult.MaxReportedErrors
  total_warnings, warnings: ImportRowError[]            // cảnh báo ghi chú cột bị thay, như import cột
}
```

`rows[]` và `warnings[]` sắp theo `(row, column)` tăng dần. Khi `committed = false` thì `warnings[]` rỗng (không gì được ghi).

### 5. Template

- `scripts/make_asset_workbook_template.py` (openpyxl) sinh `docs/templates/assets-workbook.xlsx` (sheet 1 `import`: chỉ header,
  data validation dropdown cho `data_source`, `road_class`, `fixture_type`, `power_source`, `cabinet_source`, `feeder_source`,
  `near_sensitive_poi`; cột ngày định dạng `yyyy-mm-dd`; sheet 2 `Hướng dẫn`: bảng cột ở mục 3 bằng tiếng Việt) và
  `docs/templates/assets-workbook.example.xlsx` (cùng header + 4–6 dòng mẫu: 2 cột chung tuyến/tủ/mạch, 1 cột chưa GPS, 1 cột
  không bộ đèn, 1 cột cần đôi `lamp_count = 2`). Commit cả script lẫn hai file sinh ra.
- `docs/templates/README.md`: thêm mục "Một sheet Excel" (endpoint, cột, gộp trùng, nguyên tử, bộ đèn nạp lại). Sửa luôn các
  dòng đã lỗi thời so với code trong cùng file (cột `segment_external_ref`, `geom_wkt` của cột không còn bắt buộc; `length_m`
  của tuyến; `fixtures.csv` thiếu `lamp_count`, `note`) và thêm `lamp_count`, `note` vào `docs/templates/fixtures.csv`.

### 6. Swagger / OpenAPI

Xuất `luxmap-v1.json` → chạy generator → lint theo `.ai/context/commands.md`. Một schemaId một kiểu (`WorkbookImportResult`,
`WorkbookKindCounts`). Không sửa tay `docs/openapi/luxmap-v1.5.json`.

Tiêu chí xong — mỗi dòng kiểm chứng được bằng một lệnh:

- [ ] `dotnet build --disable-build-servers` không warning mới.
- [ ] `dotnet test` xanh (mã thoát 0), có `AssetImportWorkbookTests` trong `AssetDatabaseCollection` phủ tối thiểu:
  1. sheet đầy đủ 5 lớp (2 cột chung tuyến/tủ/mạch, cột chưa GPS, cột không bộ đèn) → `committed`, số đếm đúng từng lớp,
     liên kết cột → tuyến / mạch → tủ đúng trong DB;
  2. **một lỗi ở dòng cuối** (vd. `data_source` sai ở bộ đèn) → `committed = false`, **DB không đổi** (đếm 5 bảng trước / sau);
  3. nạp lại **y hệt** sheet ở (1) → `committed`, mọi lớp `inserted = updated = 0`, bộ đèn `unchanged`, `updated_by`/`updated_at`
     không đổi;
  4. bộ đèn khác `lamp_watt` với bộ đang dùng → lỗi dòng, không ghi gì;
  5. gộp trùng: cùng `segment_ref` khác `segment_name` → lỗi ở dòng sau với số dòng dòng đầu; ô trống ở dòng sau không lỗi;
  6. `pole_ref` trùng → lỗi;
  7. số dòng lỗi là số dòng Excel thật khi có dòng trống ở giữa và ô thưa;
  8. ngày dạng serial số và dạng chuỗi đều đọc đúng; serial có giờ và chuỗi `24/03/2022` lỗi;
  9. `pole_lat` có mà `pole_lng` trống → lỗi; `10,97` dạng chuỗi → lỗi;
  10. chỉ đọc sheet đầu (sheet 2 có rác không gây lỗi);
  11. thiếu `pole_ref` ở header → một lỗi dòng 1; header trùng tên → lỗi dòng 1;
  12. `commune_id` ngoài phạm vi người gọi → lỗi dòng, không ghi;
  13. `.xls` / `.csv` / file text đổi đuôi `.xlsx` → 415; zip không phải OpenXml → 400;
  14. vượt 5 000 dòng → lỗi, không ghi;
  15. `POST /assets/import/workbook` đi vào action workbook, không vào `{kind}` (route metadata hoặc hành vi);
  16. lỗi báo bằng tên cột sheet (`segment_ref`), không bằng `external_ref`.
- [ ] Phá thử (Claude làm, ngoài sandbox): bỏ rollback khi có lỗi → test (2) đỏ; bỏ so khớp bộ đèn → test (3) hoặc (4) đỏ.
- [ ] `CapabilityPolicyCoverageTests` xanh (endpoint mới có capability).
- [ ] Spec sinh lại, lint sạch; `git diff docs/openapi` chỉ thêm path + 2 schema.
- [ ] Template mở được bằng Excel / LibreOffice; nạp `assets-workbook.example.xlsx` qua endpoint trên DB test → `committed`.

## KHÔNG ĐƯỢC làm

- Không tạo migration, không đổi schema. Không bảng `asset_import` / lịch sử lượt nạp.
- Không đổi hành vi, request hay response của 5 endpoint `{kind}` hiện có (kể cả fixtures insert-only).
- Không chép lại luật kiểm tra từng lớp: workbook đi qua planner hiện có; tách phần chung ra nếu cần, không nhân đôi.
- Không đọc cả sheet vào DOM (`WorksheetPart.Worksheet`), không `Load()` shared strings vào bộ nhớ không giới hạn.
- Không tự đổi `,` thành `.` trong số, không đoán định dạng ngày ngoài `YYYY-MM-DD` / serial.
- Không `ExecuteUpdate` / `ExecuteDelete`, không bypass `CommuneWriteGuard`, không `EnterUnscopedSystemWriteBackdoor`.
- Không sửa `docs/api-contract-v1.1.md`, không sửa tay `docs/openapi/*.json`, không sửa `CLAUDE.md`.
- Không thêm dependency nào ngoài `DocumentFormat.OpenXml` 3.5.1 (đã thêm sẵn vào `LuxMap.Modules.Assets.csproj`); test dùng
  chính nó để dựng file xlsx.
- Không commit (Claude commit sau khi verify).

## Kết quả

Ghi `.ai/results/ASSET-IMPORT-WORKBOOK.md`: file đã đổi, cách tái dùng planner (đổi gì ở `AssetImportService` / `ImportRow`),
lệnh đã chạy và kết quả, điều chưa làm được, câu hỏi còn mở.
