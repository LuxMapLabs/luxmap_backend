# Mẫu import kiểm kê tài sản (BE-12)

Bốn file CSV để nạp hồ sơ tài sản chiếu sáng. Cột lấy **trực tiếp từ schema thật** trong
PostgreSQL (`\d pole`, `\d fixture`, `\d road_segment`, `\d feeder`), không suy từ entity class.

> **Endpoint import đã có (BE-12a).** Mỗi lần nạp MỘT loại file:
>
> ```
> POST /api/v1/assets/import/{segments|feeders|poles|fixtures}
> multipart/form-data, field `file`, tối đa 10 MB
> ```
>
> Chỉ **Quản trị** được nạp. Trả **200** kèm kết quả `{inserted, updated, failed, total_errors,
> truncated, rows[]}` — 200 kể cả khi có dòng hỏng, vì những dòng hợp lệ đã ghi thật.
>
> ⚠️ **Contract v1.1 vẫn không đặc tả endpoint này.** Cả đường dẫn lẫn hình dạng kết quả đều là
> drift đã đăng ký, cần chốt ở FW-00. Hình dạng response khi ĐỌC một tài sản thuộc **BE-12b**,
> đang chờ duyệt — hiện `GET` chỉ trả danh sách ID.

---

## Thứ tự import — bắt buộc theo đúng thứ tự này

```
1. segments.csv   →  2. feeders.csv   →  3. poles.csv   →  4. fixtures.csv
```

**Mỗi lần gọi nạp đúng một loại file**, nên khi nạp `poles.csv` thì các tuyến đã nằm sẵn trong DB
và đã có mã thật. Nạp sai thứ tự không hỏng dữ liệu — nó **hỏng ở bước kiểm tra**: mọi dòng pole sẽ
báo `segment_external_ref` không khớp gì cả, và không dòng nào được ghi.

Lý do là **khoá ngoại**, không phải sở thích:

| Bước | Vì sao phải trước | Ràng buộc thật |
|---|---|---|
| 1. `segments` | `pole.segment_id` **NOT NULL** — không có tuyến thì không tạo được cột | `fk_pole_road_segment_segment_id` |
| 2. `feeders` | `pole.feeder_id` **nullable**, nhưng nếu điền thì tuyến điện phải tồn tại | `fk_pole_feeder_feeder_id` |
| 3. `poles` | `fixture.pole_id` **NOT NULL** | `fk_fixture_pole_pole_id` |
| 4. `fixtures` | — | — |

Cả bốn bảng đều tham chiếu `administrative_unit`, nên **xã phải tồn tại trước bước 1**
(`fk_*_administrative_unit_commune_id`, `ON DELETE RESTRICT`).

---

## Quy tắc chung

### Cột server sở hữu — KHÔNG có trong template

Không điền, không thêm cột. Gửi lên sẽ bị từ chối chứ không bị bỏ qua im lặng.

| Cột | Vì sao |
|---|---|
| `segment_id`, `feeder_id`, `pole_id`, `fixture_id` | DB sinh qua `DEFAULT luxmap_format_id(...)`. Client **không sinh ID hiển thị** (Contract mục 0.4). |
| `created_at`, `updated_at` | `DEFAULT now()` |
| `fixture.commune_id` | Sao chép từ cột mang bóng đó. Để người nhập điền thì hai giá trị lệch nhau được, và **không có gì phát hiện**. |

### Hình học — một cột `geom_wkt`

- **Kinh độ TRƯỚC, vĩ độ SAU**: `POINT(106.4900 10.9700)`. Đây là thứ tự của WKT và của
  GeoJSON, ngược với cách người Việt hay đọc "vĩ độ, kinh độ".
- **EPSG:4326** (WGS84). Không dùng VN-2000/EPSG:3405 — hệ đó chỉ dùng nội bộ khi tính
  khoảng cách, toạ độ 3405 lọt vào đây sẽ lệch **226 m** trên bản đồ.
- `LINESTRING` nhiều điểm ngăn bằng dấu phẩy: `LINESTRING(106.4900 10.9700, 106.4950 10.9705)`.
  Vì bên trong có dấu phẩy nên **ô này phải nằm trong dấu nháy kép**.
- Số thập phân dùng **dấu chấm**, không dùng dấu phẩy.

### Tham chiếu bằng mã, tên chỉ để đọc

`commune_id` và các cột `*_external_ref` là thứ được dùng. Các cột `segment_name` và `feeder_name`
trong `poles.csv` **chỉ để người nhập đọc cho dễ và bị BỎ QUA khi nạp** — kết quả import chỉ có lỗi
theo dòng, không có kênh cảnh báo, nên tên lệch sẽ không được báo. Đừng dựa vào chúng.

**Không có `commune_name`, và đó là chủ đích.** `segment_name` có nghĩa thật vì một xã có
nhiều tuyến và `SEG-001` thì không đọc được. Nhưng người soạn file kiểm kê của xã mình biết
rõ mình đang ở xã nào — thêm `commune_name` vào cả ba file không giúp gì mà **nhân ba cơ hội
lệch dữ liệu**.

### `external_ref` — mã kiểm kê của đơn vị, CÓ lưu trong database

Ba file `segments.csv`, `feeders.csv`, `poles.csv` đều có cột `external_ref` **bắt buộc**;
`fixtures.csv` trỏ về cột bằng `pole_external_ref`, còn `poles.csv` trỏ về tuyến và mạch điện bằng
`segment_external_ref` và `feeder_external_ref`.

```
road_segment.external_ref  text NULL   UNIQUE (commune_id, external_ref) WHERE external_ref IS NOT NULL
feeder.external_ref        text NULL   UNIQUE (commune_id, external_ref) WHERE external_ref IS NOT NULL
pole.external_ref          text NULL   UNIQUE (commune_id, external_ref) WHERE external_ref IS NOT NULL
```

**Vì sao tham chiếu bằng `external_ref` chứ không bằng `SEG-001` / `FDR-001`:** những mã đó do DB
sinh lúc INSERT, **người soạn file không thể biết trước**. Nếu `poles.csv` đòi `segment_id` thì bộ
bốn file không nạp được từ đầu đến cuối — phải nạp tuyến, mở DB tra mã, rồi mới điền vào file cột.
Dùng mã của chính đơn vị quản lý thì soạn xong cả bộ một lần.

Ba lý do lưu cột này vào DB, không phải một:

1. **Nối lại được giữa các đợt nhập.** Không lưu thì không có cách nào biết `CTA-001` trong
   file tháng này là `POLE-0042` đã nhập tháng trước.
2. **Import trở nên idempotent.** Lược đồ không có khoá tự nhiên nào khác, nên nạp lại cùng một
   file sẽ sinh trùng toàn bộ. `UNIQUE (commune_id, external_ref)` chính là khoá đó, và
   **trùng thì UPDATE chứ không phải lỗi**.
3. **Tổ sửa chữa ngoài hiện trường đọc mã sơn trên cột**, không đọc `POLE-0042`.

**`NULL` được phép trong DB** — cột nạp từ ảnh vệ tinh không có mã kiểm kê nào — nên unique là
**partial index** (`WHERE external_ref IS NOT NULL`), nhiều cột không mã vẫn cùng tồn tại trong một
xã. Nhưng **trong file import thì bắt buộc**: không có nó thì không upsert được.

**Không emit ra API.** Contract không có trường này; thêm vào response là đổi hình dạng đã publish.
Lưu và tra được, giống `data_source` ở tầng tài sản. Đã đăng ký drift.

> ⚠️ **`fixture` KHÔNG có `external_ref`, và nạp bóng là INSERT-ONLY.**
> Một cột mang nhiều bóng qua thời gian, nên không mã nào trong file chỉ đúng **một lần lắp đặt**.
> Nạp lại `fixtures.csv` sẽ báo lỗi từng dòng *"cột này đã có bóng"* thay vì âm thầm nhân đôi lịch
> sử thiết bị. Thay bóng dùng endpoint CRUD: `PUT /assets/fixtures/{id}/removal` cho bóng cũ, rồi
> `POST /assets/fixtures` cho bóng mới.

### Encoding — bẫy Excel thật, không phải lý thuyết

**Lưu bằng `CSV UTF-8 (Comma delimited) (*.csv)`**, không phải `CSV (Comma delimited)`.
Hai mục này nằm cạnh nhau trong menu *Save As* của Excel và **chỉ mục đầu ghi UTF-8**.

| Bẫy | Hậu quả | Cách tránh |
|---|---|---|
| Chọn nhầm `CSV (Comma delimited)` | Excel bản Việt ghi **CP1258** hoặc **UTF-16LE**. `Tuyến` đọc ra `Tuy?n` — **không có lỗi nào được ném** | Chọn `CSV UTF-8` |
| **BOM** `EF BB BF` đầu file | Tên cột đầu tiên thành `﻿segment_name`, không khớp header | Trình nhập bỏ BOM; nhưng nếu tự viết script thì phải xử lý |
| **Delimiter `;`** | Windows locale Việt Nam đặt list separator mặc định là dấu chấm phẩy, nên file "CSV" xuất ra ngăn bằng `;` | Kiểm tra file bằng Notepad trước khi gửi |
| **CRLF** | Giá trị cột cuối dính `\r` | Trình nhập cắt; không cần làm gì |
| Ô chứa dấu phẩy | Vỡ cột | Bọc trong nháy kép — Excel làm tự động |

Ngày tháng theo **`YYYY-MM-DD`** (Contract mục 0). Excel rất hay tự đổi sang `dd/mm/yyyy`
theo locale — **định dạng cột thành Text trước khi gõ**.

---

## `segments.csv`

| Cột | Bắt buộc | Kiểu | Ràng buộc / giá trị hợp lệ |
|---|---|---|---|
| `external_ref` | **Có** | text | Mã tuyến của đơn vị quản lý. `poles.csv` trỏ về bằng `segment_external_ref`. Trùng trong cùng xã → **UPDATE** |
| `segment_name` | **Có** | text | |
| `road_class` | **Có** | enum | `inter_commune` · `inter_village` — `ck_road_segment_road_class` |
| `length_m` | **Có** | integer | Mét. **Giá trị KHAI BÁO**, không tính lại từ `geom_wkt` |
| `geom_wkt` | **Có** | LineString | `geometry(LineString,4326)`, NOT NULL |
| `commune_id` | **Có** | mã | FK `administrative_unit` |
| `data_source` | **Có** | enum | `field` · `public_imagery` · `calibration_rig` · `simulated` — `ck_road_segment_data_source` |

> `length_m` là giá trị **khai báo**, không phải dẫn xuất. Đừng "sửa cho đúng" bằng
> `ST_Length` — số đo trên mặt phẳng chiếu ngắn hơn trên ellipsoid khoảng 73 ppm, ghi đè
> sẽ làm số liệu FE nhảy mà không ai giải thích được.

## `feeders.csv`

| Cột | Bắt buộc | Kiểu | Ràng buộc |
|---|---|---|---|
| `external_ref` | **Có** | text | Mã mạch điện của đơn vị quản lý. `poles.csv` trỏ về bằng `feeder_external_ref` |
| `feeder_name` | **Có** | text | |
| `commune_id` | **Có** | mã | FK `administrative_unit` |
| `geom_wkt` | Không | LineString | **Nullable** — nhánh C không khảo sát tuyến cáp, để trống thay vì bịa lộ trình |

> ⚠️ **`feeder` KHÔNG có cột `data_source`.** Bốn bảng kia có, bảng này không — kiểm từ
> `\d feeder`. Đừng thêm cột đó vào file này.

## `poles.csv`

| Cột | Bắt buộc | Kiểu | Ràng buộc / giá trị hợp lệ |
|---|---|---|---|
| `external_ref` | **Có** | text | Mã cột của đơn vị quản lý. `fixtures.csv` trỏ về bằng `pole_external_ref`. Trùng trong cùng xã → **UPDATE** |
| `segment_external_ref` | **Có** | text | Khớp `external_ref` trong `segments.csv`. Không khớp → lỗi theo dòng |
| `segment_name` | Không | text | Chỉ để đọc, **bị bỏ qua khi nạp** |
| `feeder_external_ref` | Không | text | Khớp `external_ref` trong `feeders.csv`. **Để trống với cột solar** — cột `solar_all_in_one` không nối lưới nào |
| `feeder_name` | Không | text | Chỉ để đọc, **bị bỏ qua khi nạp** |
| `commune_id` | **Có** | mã | FK `administrative_unit` |
| `geom_wkt` | **Có** | Point | `geometry(Point,4326)`, NOT NULL |
| `near_sensitive_poi` | Không | bool | `true` / `false`. Mặc định `false`. Gần trường học, chợ, cầu, ngã ba |
| `data_source` | **Có** | enum | `field` · `public_imagery` · `calibration_rig` · `simulated` — `ck_pole_data_source` |

## `fixtures.csv`

| Cột | Bắt buộc | Kiểu | Ràng buộc / giá trị hợp lệ |
|---|---|---|---|
| `pole_external_ref` | **Có** | text | Khớp `external_ref` trong `poles.csv`. **Cột đã có bóng → lỗi theo dòng** (insert-only) |
| `fixture_type` | **Có** | enum | `led_road_lamp` · `solar_all_in_one` — `ck_fixture_fixture_type` |
| `power_source` | **Có** | enum | `grid` · `solar` — `ck_fixture_power_source` |
| `lamp_watt` | **Có** | integer | |
| `install_date` | **Có** | date | `YYYY-MM-DD` |
| `removed_date` | Không | date | Để trống nghĩa là bóng **đang lắp**. Thay bóng thì điền vào dòng cũ và thêm một dòng mới |
| `warranty_expiry` | Không | date | Không phải bóng nào cũng có bảo hành |
| `data_source` | **Có** | enum | `field` · `public_imagery` · `calibration_rig` · `simulated` — `ck_fixture_data_source` |

> **Một cột mang được nhiều bóng** — đó là cách ghi lịch sử thay bóng. Nhưng **import không làm
> việc đó**: nhiều dòng cùng `pole_external_ref` trong một file, hoặc nạp lại file cho cột đã có
> bóng, đều bị từ chối theo dòng. Chính vì "một cột nhiều bóng" nên không có khoá tự nhiên nào để
> upsert, và đoán sai sẽ nhân đôi lịch sử thiết bị mà không ai phát hiện. Thay bóng dùng CRUD.
>
> ⚠️ **`fixture` không có cột trạng thái nào.** Tình trạng sáng/mờ/tắt thuộc **vị trí cột**
> (`pole_current_status`), do luồng xử lý ảnh khảo sát ghi (BE-15/BE-17). Import kiểm kê
> **không được** đụng vào bảng đó.

---

## Đối chiếu với bộ mock FO-26

Kiểm chứng template có đủ để nạp lại 103 cột trong `mocks/mock-poles.geojson`. Mọi
`properties` của mock phải có chỗ, hoặc được giải thích thuộc đâu.

| `properties` của mock | Nằm ở đâu |
|---|---|
| `pole_id` | ⚠️ **DB sinh** qua `luxmap_format_id('POLE', …)`. Khi nạp bộ mock, dùng chính giá trị này làm `external_ref` — đó là mã duy nhất bộ mock có |
| `segment_id` | ✅ `poles.csv` → `segment_external_ref` (dùng `segment_id` của mock làm mã tuyến) |
| `commune_id` | ✅ `poles.csv` → `commune_id` |
| `near_sensitive_poi` | ✅ `poles.csv` → `near_sensitive_poi` |
| `power_source` | ✅ `fixtures.csv` → `power_source` (**bảng `fixture`**, không phải `pole`) |
| `fixture_type` | ✅ `fixtures.csv` → `fixture_type` |
| `lamp_watt` | ✅ `fixtures.csv` → `lamp_watt` |
| `install_date` | ✅ `fixtures.csv` → `install_date` |
| `warranty_expiry` | ✅ `fixtures.csv` → `warranty_expiry` |
| `fixture_status` | ❌ Bảng `pole_current_status` — **BE-15/BE-17 sở hữu quyền ghi**, import cấm đụng |
| `status_confidence` | ❌ `pole_current_status` — như trên |
| `last_seen_at` | ❌ `pole_current_status` — như trên |
| `last_sweep_id` | ❌ `pole_current_status` — như trên |
| `open_fault_count` | ❌ **Tính lúc query** — `COUNT` trên `fault` (BE-18), không lưu |
| `has_iot_node` | ❌ **Tính lúc query** — semi-join sang `iot_node` (IOT-10), không lưu |

**Kết luận: 15/15 trường đều có chỗ.** 9 trường nạp được từ template (4 ở `poles.csv`,
5 ở `fixtures.csv`), 1 do DB sinh, 4 thuộc bảng mà import không được ghi, 2 là giá trị
dẫn xuất lúc truy vấn.

Nghĩa là **template đủ để dựng lại phần tài sản của bộ mock**. Phần trạng thái và phần
thống kê không thuộc kiểm kê — chúng đến từ luồng khảo sát và từ truy vấn.

---

## Kết quả nạp — đọc thế nào

```json
{
  "inserted": 490,
  "updated": 0,
  "failed": 10,
  "total_errors": 14,
  "truncated": false,
  "rows": [
    { "row": 12, "column": "road_class", "message": "'lien_xa' is not one of: inter_commune, inter_village." }
  ]
}
```

- **`row` là số dòng TRONG FILE**, header là dòng 1 — mở file ra là thấy đúng chỗ.
- **`total_errors` có thể lớn hơn `failed`**: một dòng sai ba cột thì đếm ba lỗi, một dòng hỏng.
- **`rows[]` cắt ở 100 phần tử**, `truncated: true` khi bị cắt. `total_errors` vẫn là số thật.
- **Toàn bộ file được kiểm TRƯỚC, rồi mới ghi tập hợp lệ trong MỘT transaction.** Dòng sai không
  bao giờ chạm tới database. Nếu bước ghi hỏng (mất kết nối, deadlock) thì **rollback cả mẻ** và
  trả 500 — không có ca "ghi được một nửa".
- **Trả 200 kể cả khi `failed > 0`**, vì những dòng hợp lệ đã ghi thật. Đây là drift đã đăng ký:
  Contract không phủ hình dạng này.

**Bẫy hay gặp nhất:** cả file báo `Column missing from the header row` ở dòng 1. Gần như luôn là
file lưu sai định dạng — delimiter `;`, hoặc BOM dính vào tên cột đầu. Trình nhập tự nhận cả hai,
nhưng nếu tên cột cũng sai chính tả thì nó không đoán hộ.

---

## 🔴 Việc phải làm khi nạp FO-26: bộ mock không mang `feeder_id`

`mock-poles.geojson` có 15 `properties` và **không có cột nào cho tuyến điện**. Nạp bộ mock
qua template này sẽ cho ra **103 cột chưa gắn feeder nào**.

Vì sao đây không phải chi tiết nhỏ:

- **CV-15 gom cụm sự cố theo MẠCH ĐIỆN, không theo đường.** Một cầu chì nhảy hay một dây
  đứt lan theo **feeder**, và đó là thứ biến một đám cột tối rời rạc thành **một** nguyên
  nhân `segment_outage` thay vì N sự cố bóng lẻ.
- **BE-13 tồn tại để trả lời "mọi cột trên feeder X"**, và đó là đầu vào bắt buộc của CV-15.
- Không có `feeder_id`, truy vấn đó trả rỗng cho toàn bộ 103 cột — **không có gì để gom**.

Đây là khoảng trống của **bộ mock FO-26**, không phải của template: cột `feeder_id` có sẵn
trong `poles.csv` và trong lược đồ. Nhưng nó phải được lấp trước khi CV-15 có thể chạy, bằng
một trong hai cách:

1. **Bổ sung `feeder_id` vào `mock-poles.geojson`** — cần thống nhất với WP5/WP6 vì bộ mock
   là hình dạng FE đã code theo.
2. **Gán thủ công sau khi nạp** — nhanh hơn, nhưng phải ghi lại cách gán, nếu không thì kết
   quả gom cụm không tái lập được.

Cột `solar_all_in_one` thì **đúng là không có feeder** (không nối lưới nào) — chỉ cột lưới
mới thiếu. Đừng gán feeder cho cột solar để "cho đủ".

### Bốn trường bộ mock còn thiếu để nạp được

Kiểm bằng test `AssetImportMockSetTests` — nó nạp trọn 3 tuyến + 103 cột + 103 bóng qua đúng
endpoint thật, sau khi bổ sung ba trường đầu:

| Trường | Thiếu ở đâu | Test làm gì |
|---|---|---|
| `external_ref` | cả hai file | Lấy chính `pole_id` / `segment_id` của mock — mã của mock **là** mã kiểm kê cho tới khi có mã thật |
| `commune_id` | `mock-segments.geojson` không có; `mock-poles.geojson` ghi `COM-001` | Ghi đè bằng xã của test |
| `data_source` | cả hai file | Đặt `public_imagery` — đúng bản chất dữ liệu ảnh đêm công khai của nhánh C |
| `feeder_id` | cả hai file | **KHÔNG bổ sung.** Test khẳng định cả 103 cột có `feeder_id` NULL, để khoảng trống này không bị che đi |

**BE-39 phải xử lý bốn trường này trước khi seed**, nếu không bộ mock nạp lên sẽ không dựng lại
được đúng thứ FE đang code theo.
