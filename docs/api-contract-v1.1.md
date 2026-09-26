# LuxMap — API Contract v1.7 (BẢN HỢP NHẤT)

**Trạng thái:** Bản hợp nhất, **thay thế** v1.0 → v1.3 và toàn bộ `docs/contract-drift.md` cũ (nay ở
`docs/archive/contract-drift-v1.md`). Đây là tài liệu duy nhất cần đọc. **Tên file giữ
`api-contract-v1.1.md`** để mọi liên kết cũ vẫn đúng (quyết định D-1, Dylan, 18/09/2026).
**Ngày chốt v1.0:** 23/08/2026 · **v1.1:** 24/08/2026 · **v1.2:** 11/09/2026 (mục 4 Auth) ·
**v1.3:** 15/09/2026 (đổi đường dẫn lux) · **v1.4:** 18/09/2026 (hợp nhất — BE-REVIEW-02) ·
**v1.5:** 19/09/2026 (`GET /auth/me`) · **v1.6:** 22/09/2026 (bỏ đèn solar khỏi enum) ·
**v1.7:** 25/09/2026 (vai trò theo Phiếu đăng ký FA26SE222 v1.2 + ma trận capability).
**Nguyên tắc:** Bản này là **hợp đồng**. Muốn đổi field/enum → mở issue, cả BE và FE cùng duyệt, tăng
version. Không đổi ngầm. Chỗ lệch mới ghi vào `docs/contract-drift.md` (log mới, mở từ 18/09/2026).

⚠️ **Tên file spec `luxmap-v1.5.json` GIỮ NGUYÊN ở v1.6 và v1.7, cố ý** — cùng lý lẽ D-1 đã áp cho chính
tài liệu này: đổi tên làm chết mọi liên kết và mọi lệnh đã viết sẵn. Lần đổi `luxmap-v1.4.json` →
`luxmap-v1.5.json` đã làm hỏng lệnh lint trong `README.md` và để `CLAUDE.md` trỏ vào file không còn
tồn tại. Số trong tên là **phiên bản nó ra đời**, không phải phiên bản Contract hiện hành.

**Bản máy đọc:** `docs/openapi/luxmap-v1.5.json` — khớp 1-1 với tài liệu này (45 operation: 32
`implemented`, 13 `not_implemented` — đếm lại ở v1.7; mỗi operation nghiệp vụ mang `x-luxmap-capability` và
`x-luxmap-roles` sinh từ code), sinh bằng `docs/openapi/tools/gen_consolidated_spec.py` từ
`docs/openapi/luxmap-v1.json` (spec xuất từ code, **không sửa tay**) cộng các endpoint chưa có code.

**Ký hiệu**

| Ký hiệu | Nghĩa |
|---|---|
| `implemented` | Code trên `dev` đang phục vụ đúng như mô tả |
| `[NOT IMPLEMENTED]` | Contract đã đặc tả, code chưa có. **Không xoá.** Ticket ghi kèm |
| `[OPEN → O-n]` | Chưa chốt; nội dung ở **mục 9** |

> **Về cách đánh số.** Task list v2.1 trỏ tới số mục của v1.0 (mục 1, 2.1–2.5, 3). Bảng ánh xạ cũ → mới:
> §0 → 1 · §1 → 3.1 · §2.1 → 5.1 · §2.2 → 5.1 · §2.3 → 5.2 · §2.4/2.5/2.8 → 5.4 · §2.6 → 5.5 ·
> §2.7 → 5.6 · §2.9 → 5.7 · §2.10 → 4 · §3 → 5.8 · §5 → 6 · §6 → 7 · §7 → 2.

---

## 1. Tổng quan và quy ước toàn cục (khoá cứng)

### 1.1 Bảng quy ước

| Hạng mục | Quyết định | Ghi chú |
|---|---|---|
| Base URL | `/api/v1` | Version nằm trong URL, không header |
| JSON | **snake_case** | `JsonNamingPolicy.SnakeCaseLower` |
| Enum | **chuỗi thường** | Không bao giờ int |
| Hệ toạ độ API | **EPSG:4326**, GeoJSON `[lng, lat]` | EPSG:3405 chỉ nội bộ DB / báo cáo, **không bao giờ ra API** |
| Thời gian | ISO 8601 UTC hậu tố `Z` | DB `TIMESTAMPTZ`. Giá trị **không có** hậu tố (kể cả trên query string) được đọc là UTC, không bao giờ là giờ máy chủ |
| Ngày không giờ | `YYYY-MM-DD` | `install_date`, `removed_date`, `warranty_expiry`, `night_of`, `due_date` |
| ID | Chuỗi có prefix — mục 1.2 | Chuỗi đục |
| Phân trang | `?page=1&page_size=50` → `{page, page_size, total, items[]}` | mục 1.3 |
| Lỗi | `{ "error": { "code", "message", "details" } }` | mục 1.4 |
| Correlation id | Header `X-Correlation-Id` trên **mọi** response; trên response lỗi thêm `error.details.correlation_id` | Client gửi lên (≤128 ký tự `[A-Za-z0-9-_.:]`) thì server dùng lại, ngoài khuôn hoặc thiếu thì server sinh |
| Auth | `Authorization: Bearer <jwt>` | mục 4 |

### 1.2 ID — dạng, bảng prefix, cách sinh

```
<PREFIX>-<số thứ tự đã pad 0>
```

| Entity | Prefix | Ví dụ | Khuôn (SÀN, không phải trần) |
|---|---|---|---|
| `Pole` | `POLE` | `POLE-0001` | `^POLE-[0-9]{4,}$` |
| `Fault` | `FAULT` | `FAULT-0001` | `^FAULT-[0-9]{4,}$` |
| `RoadSegment` | `SEG` | `SEG-001` | `^SEG-[0-9]{3,}$` |
| `AdministrativeUnit` | `COM` | `COM-001` | `^COM-[0-9]{3,}$` |
| `Fixture` | `FIX` | `FIX-0001` | `^FIX-[0-9]{4,}$` |
| `Feeder` | `FDR` | `FDR-001` | `^FDR-[0-9]{3,}$` |
| `IotNode` | `NODE` | `NODE-001` | `^NODE-[0-9]{3,}$` |
| `SurveySweep` | `SWP` | `SWP-001` | `^SWP-[0-9]{3,}$` |
| `SurveyFrame` | `FRM` | `FRM-000001` | `^FRM-[0-9]{6,}$` |
| `Detection` | `DET` | `DET-000001` | `^DET-[0-9]{6,}$` |
| `LuxReading` | `LUX` | `LUX-0001` | `^LUX-[0-9]{4,}$` |
| `WorkOrder` | `WO` | `WO-0001` | `^WO-[0-9]{4,}$` |
| `RepairEvidence` | `EVD` | `EVD-0001` | `^EVD-[0-9]{4,}$` |
| `ExternalUnit` | `EXT` | `EXT-001` | `^EXT-[0-9]{3,}$` |
| `AppUser` | `USR` | `USR-001` | `^USR-[0-9]{3,}$` |
| Cụm sự cố (`cluster_id`) | `CLS` | `CLS-001` | `^CLS-[0-9]{3,}$` |

- **Số chữ số là TỐI THIỂU, không cố định.** `POLE-9999` kế tiếp là `POLE-10000`. Client **không** parse,
  **không** so sánh thứ tự, **không** giả định độ dài. Khuôn dùng `[0-9]` chứ không `\d` (`\d` khớp cả
  chữ số Unicode). Xác nhận 18/09/2026 (trước là `SELF-SIGNED` 07/09).
- **Sinh ở server** bằng sequence PostgreSQL và hàm:

```sql
CREATE FUNCTION luxmap_format_id(prefix text, value bigint, digits integer) RETURNS text
LANGUAGE sql IMMUTABLE STRICT AS $$
    SELECT prefix || '-' || lpad(value::text, greatest(digits, length(value::text)), '0')
$$;
ALTER TABLE pole ALTER COLUMN pole_id SET DEFAULT luxmap_format_id('POLE', nextval('pole_id_seq'), 4);
```

  🔴 **Không dùng `LPAD(nextval(...)::text, 4, '0')` trần** — nó **cắt bớt** khi vượt độ rộng (cột thứ
  10000 thành `POLE-1000`, đụng cột thứ 1000). Phải là **hàm** để `nextval` chỉ gọi một lần mỗi hàng.
- `LuminanceBaseline`, `TelemetryReading`, `RefreshToken` không có ID hiển thị.
- **Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID); server trả ánh xạ.
  Ngoại lệ **duy nhất**: bộ seed demo BE-39 được INSERT ID tường minh rồi `setval` sequence (D-6), vì
  FE đã hardcode `POLE-0047`; đó là hành động của hệ thống, không phải của client.

### 1.3 Phân trang

`{page, page_size, total, items[]}`. `total` là tổng dòng khớp bộ lọc. `page_size` tối đa **200**;
vượt thì **kẹp im lặng** về 200 và response ghi `page_size: 200` — client phải đọc `page_size` trong
response. `page`/`page_size` không phải số → dùng mặc định (1 / 50), không lỗi.

### 1.4 Mô hình lỗi và mã lỗi

```json
{ "error": { "code": "BBOX_TOO_LARGE", "message": "...", "details": { "correlation_id": "..." } } }
```

`details` là túi ngữ cảnh tự do, **luôn có mặt**, luôn chứa `correlation_id`. `message` để người đọc,
client **không** parse.

| Mã | HTTP | Khi nào |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Body/query sai định dạng hoặc thiếu trường; chi tiết từng field trong `details` |
| `LOCATION_REQUIRED` | 400 | `POST /faults` không `pole_id` và không `location` |
| `FAULT_TYPE_NOT_REPORTABLE` | 400 | `POST /faults` với `fault_type` chỉ engine sinh |
| `SERVER_OWNED_FIELD` | 400 | Body mang trường server sở hữu (`lux_id`, `commune_id` ở `POST /lux-readings`) |
| `UNAUTHENTICATED` | 401 | Access token thiếu / sai chữ ký / hết hạn / sai `iss` / sai `aud` — một mã. **Route không tồn tại khi chưa đăng nhập cũng trả 401** (không dò được route) |
| `INVALID_CREDENTIALS` | 401 | Sai tài khoản **hoặc** mật khẩu — một mã |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh token thiếu/sai/hết hạn/thu hồi/dùng lại/sai nhóm — một mã |
| `ACCOUNT_LOCKED` | 403 | Đúng mật khẩu, tài khoản khoá (cả login lẫn refresh) |
| `COMMUNE_FORBIDDEN` | 403 | `commune_id` ngoài phạm vi claim (mục 2), hoặc claim `["*"]` lệch vai trò |
| `ROLE_FORBIDDEN` | 403 | Đã đăng nhập nhưng **vai trò** không nằm trong danh sách của capability mà endpoint yêu cầu (mục 2; v1.4, D-4) |
| `ORIGIN_NOT_ALLOWED` | 403 | `Origin` thiếu / lạ / `null` — chỉ nhóm `/auth/web/*` |
| `POLE_NOT_FOUND` | 404 | `pole_id` không tồn tại **hoặc ngoài phạm vi** — cùng một câu trả lời |
| `ASSET_NOT_FOUND` | 404 | Tài sản (`/assets/…`) không tồn tại **hoặc ngoài phạm vi** |
| `NOT_FOUND` | 404 | Route không tồn tại (đã đăng nhập) |
| `METHOD_NOT_ALLOWED` | 405 | Sai method trên route có thật |
| `IDENTIFIER_TAKEN` | 409 | Đăng ký trùng username/email (`POST /auth/register` — **DEPRECATED**, mục 4.1) |
| `EXTERNAL_REF_TAKEN` | 409 | `(commune_id, external_ref)` đã có |
| `ASSET_IN_USE` | 409 | `DELETE` bị khoá ngoại từ chối; `details.constraint`, `details.table` |
| `CROSS_COMMUNE_REFERENCE` | 409 | Gắn cột vào mạch điện **khác xã** với cột (v1.4, D-5) |
| `POLE_HAS_ACTIVE_FIXTURE` | 409 | Cột đã có bóng đang dùng; ngừng dùng trước (v1.4, D-11) |
| `BBOX_TOO_LARGE` | 413 | bbox quá 2000 cột |
| `UNSUPPORTED_MEDIA_TYPE` | 415 | Upload không phải `.csv`/`.geojson`, hoặc `Content-Type` sai |
| `UNSUPPORTED_IMAGE_FORMAT` | 415 | Ảnh không phải JPEG theo magic bytes `FF D8 FF` |
| `DUPLICATE_OP` | **200** | `client_op_id` đã xử lý — trả bản ghi đã có, **không phải lỗi** |
| `INTERNAL_ERROR` | 500 | Lỗi chưa xử lý; thông điệp chung, tra log bằng `correlation_id` |
| `REQUEST_FAILED` | khác | Mọi status khác không có mã riêng |

### 1.5 GeoJSON và toạ độ

- Endpoint bản đồ trả **`FeatureCollection`** chuẩn; dữ liệu nghiệp vụ **phẳng** trong
  `feature.properties`; **không dùng `feature.id`**, dùng `properties.<x>_id`.
- API luôn EPSG:4326. FE không reproject. Không tính khoảng cách bằng trừ toạ độ.

### 1.6 Hai chiều dữ liệu: `source_channel` và `data_source`

`source_channel` = kênh phát hiện (`cv | iot | field_report`). `data_source` = dữ liệu đến từ đâu
(`field | public_imagery | calibration_rig | simulated`). Một bản ghi mang cả hai.

`data_source` gắn trên **tám** entity: `Pole`, `Fixture`, `RoadSegment` (lưu, lọc được, **không emit**
ra `properties`), `SurveySweep`, `SurveyFrame`, `Fault`, `TelemetryReading`, `LuxReading` (emit).
`Feeder` không có. **Mọi endpoint thống kê phải lọc và nhóm được theo trường này**, và **không bao giờ
trả một con số gộp ba nguồn**. `GET /poles` và `GET /segments` **mặc định loại `calibration_rig`**;
muốn thấy bộ hiệu chuẩn thì truyền `data_source=calibration_rig` tường minh (v1.4, D-13).

Nguồn của từng giá trị (v1.7, D-R10 — phạm vi theo Phiếu đăng ký v1.2):

| Giá trị | Dữ liệu |
|---|---|
| `field` | Thu **ngoài thực địa ở xã đối tác**: video đêm của đèn thật, số đo sáng **tương đối** bằng điện thoại, kiểm tra trạng thái đèn bằng mắt ban đêm |
| `public_imagery` | Ảnh đêm công khai — nguồn khởi động AI trong lúc chờ quay thực địa |
| `calibration_rig` | Controlled Reference Capture Set trên **mô hình testbed tự dựng** (đèn LED, mức sáng biết trước) |
| `simulated` | Telemetry mô phỏng |

🔴 Dữ liệu từ testbed (ảnh **và** telemetry) **không bao giờ** mang `field` — gộp nó vào `field` là
trộn thí nghiệm có kiểm soát với hiện trường. Telemetry thật từ thiết bị IoT trên testbed mang giá trị
nào (`calibration_rig` hay giá trị mới) còn mở: `[OPEN → O-9]`.

---

## 2. Phân quyền — theo địa bàn và theo vai trò

Bốn vai trò đăng nhập theo **Phiếu đăng ký FA26SE222 v1.2**, mục 3.2.c (v1.7, D-R6). JWT mang claim
`commune_ids` (mảng). Quản trị hệ thống mang `["*"]`.

| Vai trò | Giá trị enum `user_role` | Thay cho (≤ v1.6) | Phạm vi địa bàn |
|---|---|---|---|
| Cấp giám sát (Superior) | `superior` | `management_agency` | Một **tập xã** qua gán xã (có thể nhiều xã). **Chỉ đọc** |
| Quản lý (Manager) | `manager` | `maintenance_engineer` | Đúng các xã trong claim |
| Kỹ sư hiện trường (Field Engineer) | `field_engineer` | `field_crew` | Đúng các xã trong claim |
| Quản trị hệ thống (System Admin) | `system_admin` | `administrator` | Toàn hệ thống, `*` |

- **Không có role Tổ khảo sát riêng** — khảo sát đêm, kiểm tra và sửa chữa đều là Kỹ sư hiện trường.
- **Công dân (Citizen) không có tài khoản và không có role.** Công dân báo sự cố qua **mã QR trên cột**;
  báo cáo đó vào một hàng chờ riêng, Quản lý duyệt rồi mới thành `fault` — **không** thêm giá trị mới
  vào `source_channel` (v1.7, D-R1). Chưa có endpoint.
- **Không có tự đăng ký.** Tài khoản do **Quản trị hệ thống** tạo, gán vai trò và gán xã (v1.7, D-R11).
  `POST /auth/register` còn chạy nhưng **DEPRECATED**, gỡ ở BE-33a (mục 4.1).
- Cấp giám sát không có cấp "huyện" riêng: phạm vi của họ là danh sách xã được gán (v1.7, D-R3).

**Quy tắc địa bàn**

- **Lọc ở server, luôn luôn.** Query param `commune_id` chỉ **thu hẹp**; ngoài phạm vi → **403
  `COMMUNE_FORBIDDEN`** (kể cả khi chỉ một trong nhiều giá trị sai). Truy cập trực tiếp tài nguyên
  ngoài phạm vi → **404**, không 403 (403 sẽ lộ rằng nó tồn tại). `GET /sync/bundle` chỉ đóng gói
  dữ liệu trong phạm vi claim.
- Tài khoản chưa được gán xã có `commune_ids: []`, đăng nhập được nhưng **không thấy gì**.

**Quy tắc vai trò (v1.7, D-R5 — thay D-14 của v1.4)**

- Mỗi endpoint nghiệp vụ yêu cầu **một capability**; mỗi capability liệt kê **tường minh danh sách vai
  trò chính xác** được vào. **Không có thứ bậc**: không có "từ Quản lý trở lên" — vai trò được vào vì
  được **nêu tên**, vai trò không được nêu tên thì bị từ chối. (v1.4 D-14 ghi "*một* vai trò chính xác";
  v1.7 nới thành *danh sách*, vì phiếu có vai trò chỉ-đọc và có việc chia giữa Quản lý và Kỹ sư hiện
  trường — một-vai-trò chỉ diễn đạt được điều đó bằng cách để endpoint trần, tức mở cho cả bốn.)
- **Không endpoint nghiệp vụ nào chỉ dựa vào "đã đăng nhập"**. Ngoại lệ duy nhất: `GET /auth/me`
  (nói về chính người gọi, không về mạng lưới).
- Ma trận hiện hành:

| Capability | Vai trò được vào | Endpoint |
|---|---|---|
| `ReadNetwork` | superior, manager, field_engineer, system_admin | mọi `GET /assets/*`, `GET /poles`, `GET /segments` |
| `ReadLuxReadings` | superior, manager, field_engineer, system_admin | `GET /lux-readings`, `GET /lux-readings/poles/{id}` |
| `ManageAssets` | **manager** | mọi `POST`/`PUT`/`DELETE` `/assets/*` và `POST /assets/import/{kind}` |
| `RecordLuxReading` | **field_engineer** | `POST /lux-readings` |
| `ControlLighting` | **manager** | chưa có endpoint — điều khiển ON/OFF/AUTO thiết bị được hỗ trợ (testbed demo), mục 5.6 |
| `ManageUsers` | **system_admin** | chưa có endpoint — tạo account, gán vai trò và xã (BE-33) |

- `/auth/*` cấp token không cần token.
- Quản trị hệ thống **đọc** dữ liệu nghiệp vụ qua `*` để vận hành và hỗ trợ, nhưng **không ghi** nghiệp
  vụ nào; nạp dữ liệu đầu kỳ dùng seeder, không qua API (v1.7, D-R12).
- Capability cho sweep/session review, fault, work order… được thêm **cùng ticket** của chúng, theo
  đúng luật này: `[OPEN → O-2]`.
- Sai vai trò → **403 `ROLE_FORBIDDEN`**. Đây là mã khác `COMMUNE_FORBIDDEN`.
- ⚠️ Access token phát trước khi đổi vai trò (≤ v1.6) còn mang giá trị cũ tới **60 phút**; không
  capability nào nhận giá trị cũ, nên token đó bị 403 cho tới lần refresh kế tiếp (refresh đọc vai trò
  từ DB).

---

## 3. Mô hình miền, enum, máy trạng thái

### 3.1 Enum — KHOÁ CỨNG (FE hardcode được)

```
fixture_status : normal | dim | out | unknown
power_source   : grid
fixture_type   : led_road_lamp
fault_type     : lamp_out | lamp_dim | segment_outage | node_offline | runtime_decline
fault_status   : detected | confirmed | rejected | in_progress | resolved | verified
severity       : low | medium | high | critical
source_channel : cv | iot | field_report
data_source    : field | public_imagery | calibration_rig | simulated
wo_status      : open | assigned | in_progress | done | verified | cancelled
node_role      : segment_controller | sampled_fixture
node_status    : online | offline | never_reported
road_class     : inter_commune | inter_village
user_role      : superior | manager | field_engineer | system_admin
```

- 🔴 **`power_source` và `fixture_type` còn MỘT giá trị kể từ v1.6 (22/09/2026).** Đèn năng lượng
  mặt trời ra khỏi phạm vi đồ án, nên `solar` và `solar_all_in_one` bị **xoá khỏi enum**, không phải
  để lại không dùng: một giá trị enum mà không gì sinh ra được là giá trị mà ticket sau sẽ tưởng
  mình được phép ghi. CHECK ở DB đã siết theo (migration `DropSolarFixtures`), và bộ mock FO-26 đổi
  45 cột solar sang `grid`.
  **Phần PIN thì không đi theo:** IoT vẫn đo runtime và `runtime_decline` **vẫn là `fault_type` hợp
  lệ** — giờ sáng của đèn lưới cũng suy giảm được. Chỉ cái ĐÈN solar là hết.
- `unknown` ≠ lỗi (sweep gần nhất không phủ được cột). Ký hiệu riêng, **không gộp vào `out`**.
- `dim` là giá trị cốt lõi của đề tài → màu phải phân biệt rõ ở cả zoom xa.
- `runtime_decline` chỉ từ IoT; `lamp_dim`/`lamp_out` chỉ từ CV; một cột có thể mang cả hai.

### 3.2 Máy trạng thái `fault_status`

```
detected → confirmed | rejected
confirmed → in_progress → resolved → verified
```

Chuyển sai luồng → **409**.

**Fault MỞ** = `fault_status ∈ { detected, confirmed, in_progress }` (v1.4, O-7). Đây là tập dùng cho
`open_fault_count`, `has_open_fault`, `open_faults[]`, BE-28 và BE-40. `rejected` = chưa từng là sự
cố; `resolved` = đã sửa; `verified` = đã nghiệm thu.

`wo_status` chưa có luồng đặc tả → `[OPEN → O-3]`.

### 3.3 Thực thể và quy tắc dữ liệu

`Pole` · `Fixture` · `RoadSegment` · `Feeder` · `IotNode` · `TelemetryReading` · `SurveySweep` ·
`SurveyFrame` · `Detection` · `LuminanceBaseline` · `LuxReading` · `Fault` · `FaultCluster` ·
`WorkOrder` · `ExternalUnit` · `RepairEvidence` · `AdministrativeUnit` · `AppUser` · `RefreshToken`.

- `Pole` và `Fixture` tách riêng; trạng thái thuộc **vị trí cột** (`pole_current_status`), bóng không có
  trạng thái. **Một cột có tối đa MỘT bóng đang dùng** (`removed_date IS NULL`); thay bóng = ngừng dùng
  bóng cũ (`removed_date`) rồi ghi bóng mới; bóng đã ngừng dùng là lịch sử (v1.4, D-11).
  `removed_date >= install_date`, và ngừng dùng **đúng một lần** (v1.4, Q-4).
- Mạch điện (`feeder`) của một cột phải **cùng xã** với cột; tuyến (`segment`) thì **không** bắt buộc
  — `road_class = inter_commune` là đường chạy giữa các xã (v1.4, D-5/F-01).
- `status_confidence` (cả `pole_current_status` lẫn `fault`) là số **hữu hạn trong `0..1`**, `null` khi
  và chỉ khi `fixture_status = unknown` (ràng buộc DB).
- Bộ hiệu chuẩn FO-07 được đăng ký như `RoadSegment`/`Pole`/`Fixture` thật với
  `data_source = calibration_rig` — pipeline chạy đúng một đường.
- `LuxReading` (người đo bằng điện thoại, số đọc **tương đối**) **không phải** `luminance_history` (CV,
  tỉ lệ so với baseline của chính cột). Chúng chỉ gặp nhau ở `nearest_luminance` (mục 5.7).
- **Lưu trữ (v1.7, D-R16).** Ảnh, telemetry và hồ sơ sự cố được giữ **tối thiểu hết thời hạn bảo hành**
  của tài sản. Hết bảo hành **không** kích hoạt xoá hay archive gì. Tài sản hết bảo hành vẫn quản lý
  bình thường, chỉ mang **cảnh báo "hết bảo hành"** — tính từ `warranty_expiry` của bóng đang dùng so
  với hôm nay, **không lưu thành cột trạng thái** — hiện ở bản đồ, danh sách tài sản, và fault / work
  order của tài sản đó, để Quản lý biết lần sửa này phải tự chi trả. Chưa hiện thực (BE-31, BE-35).
- **Audit trail bắt buộc (v1.7, D-R13).** Mọi phát hiện tự động và mọi quyết định của người (fault,
  lệnh điều khiển, review survey session) ghi vào một bảng audit **append-only dùng chung**. Thiết kế ở
  BE-19; tới lúc đó `fault` chỉ giữ quyết định mới nhất trên chính dòng.

---

## 4. Xác thực — `/api/v1/auth` — `implemented`

Hai nhóm, client chọn bằng đường dẫn. **7 endpoint cấp token không cần access token**; endpoint thứ
tám, `GET /auth/me` (mục 4.7), thì **cần**. Access token sống 60 phút, luôn ở body, không bao giờ
trong cookie.

### 4.1 Nhóm mobile

**`POST /api/v1/auth/login`** — body `{ "username", "password" }` → `200` đúng bốn trường:

```json
{ "access_token": "<jwt>", "refresh_token": "<chuỗi mờ>", "token_type": "Bearer", "expires_in": 3600 }
```

**`POST /api/v1/auth/refresh`** — body `{ "refresh_token" }` → `200` cùng bốn trường; token cũ thu
hồi ngay; client **phải lưu token mới**.

**`POST /api/v1/auth/logout`** — body `{ "refresh_token" }` → **`204`** với mọi giá trị; thiếu trường
→ `400 VALIDATION_FAILED`.

**`POST /api/v1/auth/register`** — 🔴 **DEPRECATED (v1.7, D-R11) — sẽ gỡ ở BE-33a.** Mô hình chính
thức: **Quản trị hệ thống tạo account**, gán vai trò và gán xã; không có tự đăng ký. Endpoint còn chạy
cho tới khi BE-33a thêm `POST /api/v1/admin/users` (capability `ManageUsers`), để không lúc nào hệ thống
mất cách tạo tài khoản. **WP6: bỏ màn đăng ký.** Hành vi hiện tại, giữ nguyên tới lúc gỡ:
body `{ "username", "email", "full_name", "password" }` → `201`:

```json
{ "user_id": "USR-005", "username": "...", "email": "...", "full_name": "...",
  "role": "field_engineer", "commune_ids": [],
  "message": "Account created. An administrator must assign communes before any data becomes visible." }
```

Server áp cứng `role` và `commune_ids` (trường thừa bị bỏ qua); mật khẩu tối thiểu **12 ký tự**, không
ràng buộc thành phần; trùng → `409 IDENTIFIER_TAKEN`; **không trả token**. Giới hạn độ dài: OpenAPI.

### 4.2 Nhóm web

**`POST /api/v1/auth/web/login`** — body `{ "username", "password", "remember_me"? }` (`remember_me`
thiếu = `false`) → `200` đúng ba trường `{ access_token, token_type, expires_in }` + `Set-Cookie`.
Cookie web còn hiệu lực trong request bị thu hồi trước khi mở phiên mới (cách duy nhất để đổi
"ghi nhớ"/"không ghi nhớ"). Đăng nhập thất bại không thu hồi gì.

**`POST /api/v1/auth/web/refresh`** — không body; đọc cookie → `200` ba trường + `Set-Cookie` mới.
Cookie thiếu/sai → `401 INVALID_REFRESH_TOKEN`, **không đụng cookie**. Body gửi kèm bị bỏ qua.

**`POST /api/v1/auth/web/logout`** — không body; luôn `204`, luôn `Set-Cookie` xoá.

**Chặn `Origin`** cho cả ba, xét trước mọi thứ khác: thiếu / lạ / `null` → `403 ORIGIN_NOT_ALLOWED`.
Tách biệt với CORS. **CORS:** `Access-Control-Allow-Origin` = đúng origin (không `*`),
`Allow-Credentials: true`, expose `X-Correlation-Id`.

### 4.3 Cookie refresh token

| Thuộc tính | Giá trị |
|---|---|
| Tên | `__Secure-luxmap_rt` |
| `HttpOnly` / `Secure` | có / có |
| `SameSite` | `Lax` |
| `Path` | `/api/v1/auth/web` |
| `Domain` | không set (host-only) |
| `Expires` | `remember_me = true`: hạn refresh token; ngược lại **không set** (cookie phiên) |

Hằng số của API, không đổi theo môi trường. Giá trị là chuỗi mờ.

### 4.4 Loại phiên và thời hạn

| Loại | Tạo bởi | Refresh token | Trần |
|---|---|---|---|
| `mobile` | `/auth/login` | 30 ngày, trượt | 90 ngày |
| `web_persistent` | `/auth/web/login`, `remember_me=true` | 14 ngày, trượt | 90 ngày |
| `web_session` | `/auth/web/login`, `remember_me` false/thiếu | **12 giờ tuyệt đối** | 12 giờ |

- Refresh giữ nguyên loại phiên. Token nhóm này không dùng được ở nhóm kia (`401`, không thu hồi);
  logout sai nhóm → `204`, không thu hồi gì.
- Dùng lại token đã thay: trong 30 s → `401`, phiên vẫn sống; sau 30 s → `401` **và thu hồi cả chuỗi
  đăng nhập đó**. Phiên khác của cùng user không bị ảnh hưởng.
- Hai refresh cùng lúc bằng một token: đúng **một** thắng. Web nên gom refresh xuyên tab.

### 4.5 Claim trong access token

| Claim | Kiểu | Ví dụ |
|---|---|---|
| `sub` | chuỗi | `USR-001` |
| `role` | chuỗi đơn (mục 3.1 `user_role`) | `manager` |
| `commune_ids` | luôn là mảng | `["COM-001"]` · Quản trị hệ thống `["*"]` |
| `iss` / `aud` | chuỗi | `luxmap-api` / `luxmap-clients` |

### 4.6 Ràng buộc triển khai web

FE và API **cùng site** (cùng `https`, cùng eTLD+1; domain trong Public Suffix List thì mỗi subdomain
là một site). Gọi `/auth/web/*` **từ browser** với `credentials: 'include'`; không gọi từ server của
FE (không có `Origin` → 403, cookie không tới browser).

### 4.7 `GET /api/v1/auth/me` — người đang đăng nhập — `implemented` (v1.5)

**Cần access token.** Phục vụ **cả hai nhóm**: mobile và web nhận cùng một access token và gửi cùng
một cách, chỉ refresh token là khác nhau, nên **không có** `/auth/web/me`.

`200`:

```json
{ "user_id": "USR-003", "username": "engineer", "email": "engineer@luxmap.local",
  "full_name": "Quản lý", "role": "manager", "commune_ids": ["COM-001"] }
```

Đúng sáu trường, bằng `register` (mục 4.1) bỏ `message`.

| Trường | Ghi chú |
|---|---|
| `role` | Giá trị `user_role` mục 3.1 |
| `commune_ids` | Mảng, **có thể rỗng** với tài khoản chưa được gán xã. Quản trị hệ thống: `["*"]` |

🔴 **Đọc từ DATABASE, không phải từ claim trong token.** Đây là lý do endpoint tồn tại thay vì để FE
tự giải mã JWT:

- Access token sống **60 phút**, nên xã vừa được quản trị gán **không** xuất hiện trong claim cho tới
  lần đăng nhập sau. `/auth/me` trả giá trị đúng ngay lập tức.
- `full_name` và `email` **không có trong token**, mà tên hiển thị là thứ FE cần đầu tiên.

Không bao giờ phát `password_hash`, `is_locked`, `has_system_wide_scope`.

| Mã | HTTP | Khi nào |
|---|---|---|
| `UNAUTHENTICATED` | 401 | Thiếu / sai / hết hạn token, **hoặc** token còn hạn nhưng tài khoản đã bị xoá |

Tài khoản bị **khoá** không bị từ chối ở đây: access token của nó vẫn chạy trên mọi endpoint khác cho
tới khi hết hạn, và trả 403 riêng ở endpoint này là một luật không tồn tại ở đâu khác.

> ⚠️ **FE không nên tự giải mã JWT để lấy `role` hay `commune_ids`.** Làm vậy được, vì payload chỉ là
> base64, nhưng giá trị sẽ cũ tới 60 phút. Dùng `/auth/me` khi vào app và sau khi quản trị đổi quyền.

---

## 5. Endpoint theo resource

### 5.1 Cột đèn — `[NOT IMPLEMENTED]` (BE-14, BE-20)

**`GET /api/v1/poles`** — Query: `bbox` **bắt buộc** (`minLng,minLat,maxLng,maxLat`), `status` (CSV
enum), `power_source`, `segment_id`, `commune_id`, `has_open_fault`, `data_source` (mặc định loại
`calibration_rig`, mục 1.6). Quá 2000 cột → `413 BBOX_TOO_LARGE`, FE hiển thị "Phóng to để xem chi
tiết". Response `FeatureCollection` (xem `mock-poles.geojson`), `properties`:

```
pole_id, segment_id, fixture_status, status_confidence (0..1|null), power_source, fixture_type,
lamp_watt, install_date, warranty_expiry, commune_id, last_seen_at, last_sweep_id,
open_fault_count, has_iot_node, near_sensitive_poi
```

`status_confidence` là `null` **khi và chỉ khi** `fixture_status = unknown`. Các trường lắp đặt lấy từ
bóng **đang dùng** (duy nhất, mục 3.3). `data_source`, `external_ref`, `feeder_id` **không** emit.
`near_sensitive_poi` = gần trường học/chợ/cầu/ngã ba, do người vận hành đặt.

**`GET /api/v1/poles/{pole_id}`** — Đủ trong **một** request, hình dạng theo `mock-pole-detail.json`:
`pole_id, segment_id, segment_name, commune_id, location{lat,lng}, fixture{fixture_type, power_source,
lamp_watt, install_date, warranty_expiry}, current_status{fixture_status, status_confidence,
determined_at, source_channel}, iot_node{node_id, node_status, last_report_at}|null,
luminance_baseline{baseline_value, baseline_window_nights, dim_threshold_ratio, out_threshold_ratio,
computed_at}, luminance_history[]{observed_at, sweep_id, normalized_luminance, baseline_ratio,
classified_as}, runtime_history[]{night_of, runtime_hours, on_at, off_at, source} (chỉ khi có node),
open_faults[]{fault_id, fault_type, severity, fault_status, priority_score}, recent_frames[]{frame_id,
sweep_id, captured_at, thumbnail_url, distance_m, heading_deg}`.

`baseline_ratio` và `classified_as` **tính ở backend**; FE vẽ `baseline_ratio` kèm đường ngưỡng
`dim_threshold_ratio`. Ngưỡng cấu hình qua BE-33 (mặc định 0.80 / 0.15), không hard-code. `thumbnail_url`
là đường dẫn **tương đối qua API**, không presigned. Ngoài phạm vi → `404`.

### 5.2 Đoạn đường — `[NOT IMPLEMENTED]` (BE-14)

**`GET /api/v1/segments`** — Query `bbox` bắt buộc, `commune_id`, `data_source` (mặc định loại
`calibration_rig`). `FeatureCollection` của `LineString`; `properties`: `segment_id, segment_name,
road_class, length_m, pole_count, controller_node_id, has_active_segment_fault`. `length_m` là giá trị
**khai báo**. `has_active_segment_fault = true` → FE highlight cả tuyến (đầu ra CV-15).

### 5.3 Quản lý kiểm kê tài sản — `/api/v1/assets/…` — `implemented`

Tách khỏi `/poles` (endpoint bản đồ, mục 5.1). Ghi = **Quản lý** (`ManageAssets`); đọc = `ReadNetwork`
(cả bốn vai trò). Quản trị hệ thống ghi được tới v1.6, **từ v1.7 thì không** (mục 2).

| Endpoint | Body / Query | Trả về |
|---|---|---|
| **`GET /api/v1/assets/segments`** | `commune_id[]`, `page`, `page_size` | `PagedResult<string>` — **chỉ ID**. Chỗ giữ chỗ tới BE-12b |
| **`GET /api/v1/assets/feeders`** | như trên | như trên |
| **`GET /api/v1/assets/poles`** | như trên | như trên |
| **`POST /api/v1/assets/segments`** | `{external_ref?, segment_name, road_class, length_m, geom_wkt, commune_id, data_source}` | `201` + `Location`, không body; `400` xã không tồn tại; `403` xã ngoài phạm vi; `409 EXTERNAL_REF_TAKEN` |
| **`POST /api/v1/assets/feeders`** | `{external_ref?, feeder_name, commune_id, geom_wkt?}` | `201` + `Location` |
| **`POST /api/v1/assets/poles`** | `{external_ref?, segment_id, feeder_id?, commune_id, geom_wkt, near_sensitive_poi?, data_source}` | `201` + `Location`; `404 ASSET_NOT_FOUND` tuyến/mạch; `409 CROSS_COMMUNE_REFERENCE` mạch khác xã |
| **`POST /api/v1/assets/fixtures`** | `{pole_id, fixture_type, power_source, lamp_watt, install_date, removed_date?, warranty_expiry?, data_source}` — `commune_id` chép từ cột | `201` + `Location`; `409 POLE_HAS_ACTIVE_FIXTURE`; `400` nếu `removed_date < install_date` |
| **`DELETE /api/v1/assets/poles/{poleId}`** | — | `204`; `404 ASSET_NOT_FOUND`; `409 ASSET_IN_USE` (khoá ngoại từ chối — kể cả qua bóng của cột) |
| **`PUT /api/v1/assets/poles/{poleId}/feeder`** | `{"feeder_id": "FDR-001" \| null}` — khoá **bắt buộc**, `null` = không mạch | `204`; thiếu khoá → `400`; `409 CROSS_COMMUNE_REFERENCE` |
| **`PUT /api/v1/assets/fixtures/{fixtureId}/removal`** | `{removed_date}` | `204`; `400` nếu trước `install_date` hoặc đã ngừng dùng |
| **`POST /api/v1/assets/import/{kind}`** | multipart `file` ≤ 10 MB, `kind ∈ segments\|feeders\|poles\|fixtures`, `.csv`/`.geojson`/`.json` | **`200`** `{inserted, updated, failed, total_errors, truncated, rows[]{row, column, message}}` — 200 kể cả khi có dòng hỏng; `rows[]` cắt ở 100; `400` kind sai; `415` đuôi file sai |

Nhập: kiểm **toàn bộ** file trước, ghi tập hợp lệ trong **một** transaction. Upsert theo
`(commune_id, external_ref)` cho tuyến/mạch/cột; bóng **insert-only** (từ chối khi cột đang có bóng
dùng; bóng đã ngừng dùng không chặn). Xã ngoài phạm vi, mạch khác xã, `removed_date < install_date`
là **lỗi theo dòng**. Mỗi request nạp đúng một loại; thứ tự tự thực thi qua `*_external_ref`. Mẫu:
`docs/templates/`. `external_ref` **không** emit ra API.

### 5.4 Sự cố

**`GET /api/v1/faults`** — `[NOT IMPLEMENTED]` (BE-40). Phân trang JSON, **không** GeoJSON. Query:
`bbox, status, severity, fault_type, source_channel, data_source, pole_id, segment_id, cluster_id,
sort (mặc định -priority_score), page, page_size`. `pole_id` nhận **một** giá trị; cột không tồn tại và
cột ngoài phạm vi trả **giống hệt**: `200` + rỗng (kênh dò sự tồn tại bị đóng). Mỗi item:

```
fault_id, pole_id|null, fixture_id|null, segment_id|null, location{lat,lng},
fault_type, fault_status, severity, source_channel, data_source,
priority_score|null, status_confidence (0..1|null), cluster_id|null, detected_at, updated_at,
work_order_id|null, note, reported_by|null
```

Xem `mock-faults.json`. `priority_score` do CV-16 tính, client không sắp lại. `cluster_id` khác null →
FE gộp hiển thị. `work_order_id` **luôn `null`** cho tới BE-21, khoá vẫn có mặt — không xây UI phụ
thuộc nó.

**`PATCH /api/v1/faults/{fault_id}`** — `[NOT IMPLEMENTED]` (BE-19). Body
`{ fault_status, override_fault_type?, note? }`; sai luồng (mục 3.2) → `409` để FE disable nút trước.

**`POST /api/v1/faults`** — `[NOT IMPLEMENTED]` (BE-41). Kỹ sư hiện trường báo tại chỗ (FM-19). Body:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `client_op_id` | Có | UUID, khử trùng lặp |
| `pole_id` | Không | null khi cột chưa có trong hồ sơ |
| `fixture_id` | Không | Chỉ khi biết rõ bóng |
| `location` | Có khi `pole_id` null | |
| `commune_id` | Có khi `pole_id` null **và** user có nhiều xã | Có pole thì server tra từ pole, client gửi → `400`; không pole và user đúng một xã → lấy từ claim (v1.4, O-10) |
| `fault_type` | Có | không nhận `segment_outage`, `node_offline`, `runtime_decline` |
| `severity` | Không | mặc định `medium` |
| `note` | Có | ≥ 10 ký tự |
| `photo_frame_id` | Không | Ảnh upload trước qua luồng evidence |

Server áp cứng `source_channel = field_report`, `data_source = field`, `fault_status = detected`
(kể cả cán bộ báo), `reported_by` = JWT. `201` item đầy đủ kèm `client_op_id`; trùng → **`200
DUPLICATE_OP`**; `404 POLE_NOT_FOUND`; `400 LOCATION_REQUIRED` / `FAULT_TYPE_NOT_REPORTABLE`.

### 5.5 Phiếu công việc — `[NOT IMPLEMENTED]` (BE-21..BE-24)

- **`GET /api/v1/work-orders`** — query `wo_status`, `assigned_to`, `segment_id`, phân trang; item theo
  `mock-work-orders.json`: `work_order_id, title, segment_id, cluster_id, fault_ids[], wo_status,
  assigned_to, priority_score, created_at, due_date`.
- **`POST /api/v1/work-orders`** — `{ title, fault_ids[], assigned_to?, due_date? }` → `201`.
- **`PATCH /api/v1/work-orders/{work_order_id}`** — đổi `wo_status`, gán người; luồng: `[OPEN → O-3]`.
- **`POST /api/v1/work-orders/{work_order_id}/evidence`** — `multipart/form-data`: `file` (JPEG theo
  magic bytes), `kind=before|after`, `captured_at`, `lat`, `lng`; sai định dạng → `415
  UNSUPPORTED_IMAGE_FORMAT`. Video bằng chứng: **planned** (v1.7, D-R14) — tới lúc có ticket video,
  endpoint này vẫn chỉ nhận JPEG.

### 5.6 IoT, sweep, ảnh — `[NOT IMPLEMENTED]`

- **`GET /api/v1/iot-nodes`** — `bbox` bắt buộc → `FeatureCollection`; `properties` theo
  `mock-iot-nodes.geojson`: `node_id, node_role, node_status, pole_id, segment_id, battery_pct,
  last_report_at` (BE-14).
- **`GET /api/v1/sweeps`** — `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct,
  processing_status, data_source` (BE-17). Enum `processing_status`: `[OPEN → O-4]`.
- **`GET /api/v1/frames/{frame_id}/thumbnail`** — JPEG, **proxy qua API, không presigned** (phạm vi
  địa bàn áp cho ảnh như cho hàng); kích thước 320 px cạnh dài / q80 là **tạm**: `[OPEN → O-4]` (BE-15).
- **Video khảo sát đêm — planned** (v1.7, D-R14). Phiếu v1.2 cho Kỹ sư hiện trường quay video liên tục;
  hiện chỉ frame JPEG được nhận. Hướng đề xuất: upload thẳng MinIO bằng presigned URL, sync khi có mạng;
  tách frame ở server hay trên máy chốt ở ticket video (cùng WP6).
- **Điều khiển đèn ON / OFF / AUTO — chưa đặc tả** (v1.7, D-R7). Chỉ trên **thiết bị chiếu sáng được hỗ
  trợ (testbed demo)**: mô hình đèn LED tự dựng, **không** lưới chiếu sáng của xã. Chỉ thiết bị có
  `supports_remote_control = true` nhận lệnh — thiết bị ngoài thực địa luôn `false`. Capability
  `ControlLighting` = Quản lý; mọi lệnh ghi audit. Cột và endpoint thuộc ticket device registry.

### 5.7 Số đo lux — `implemented`

Một trong các nguồn ground truth cho RQ1; CV-12 đối chiếu với phân loại của hệ thống. ⚠️ **Số đọc TƯƠNG
ĐỐI** (v1.7, D-R15): `lux_value` là số đọc cảm biến ánh sáng của **điện thoại**, theo quy trình cố định —
cùng máy, cùng app, cùng tư thế đo. Chỉ dùng để so giữa các cột và giữa các đêm; **không** phải giá trị
lux tuyệt đối, **không** dùng để đánh giá đạt/không đạt chuẩn chiếu sáng.

**`POST /api/v1/lux-readings`**

```json
{ "client_op_id": "uuid", "pole_id": "POLE-0047", "measured_at": "2026-10-02T19:42:00Z",
  "lux_value": 12.4, "meter_model": "Pixel 7a", "data_source": "field", "note": "..." }
```

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `client_op_id` | Có | UUID, khử trùng lặp |
| `pole_id` | Có | Bộ hiệu chuẩn cũng là cột thật |
| `measured_at` | Có | ISO 8601 UTC `Z` |
| `lux_value` | Có | Số đọc tương đối, số thực, không âm, **hữu hạn** (NaN/Infinity → `400`); không có trần. Trên 200 server chỉ log cảnh báo và vẫn lưu — đó là **kiểm tra hợp lệ dữ liệu thô**, không mang nghĩa trắc quang |
| `data_source` | Có | Đo ngoài thực địa → `field`; đo trên testbed → `calibration_rig` (mục 1.6) |
| `meter_model` | Không | **Model điện thoại** dùng để đo |
| `note` | Không | |
| `lux_id`, `commune_id` | **Cấm** | Server sở hữu → `400 SERVER_OWNED_FIELD`; `commune_id` tra từ cột |

`measured_by` = user trong JWT, lưu FK, **không emit**. `201` kèm `lux_id`; trùng `client_op_id` →
**`200`** bản ghi đã có; `404 POLE_NOT_FOUND` (cả khi ngoài phạm vi). Response: `lux_id, client_op_id,
pole_id, measured_at, lux_value, meter_model, data_source, note`.

**`GET /api/v1/lux-readings/poles/{poleId}`** — chuỗi của một cột, sắp `measured_at` tăng dần, **có phân
trang** (một cột rig tích hàng trăm điểm). **Đổi đường dẫn ở v1.3 (BREAKING)** — đường cũ
`/poles/{id}/lux-readings` trả 404.

**`GET /api/v1/lux-readings`** — query `pole_id, from, to, data_source, page, page_size`. `from`/`to`
là biên **đóng** hai đầu; thiếu hậu tố `Z` được đọc là UTC. Mỗi item kèm
`nearest_luminance{baseline_ratio, classified_as, observed_at}|null` — điểm `luminance_history` gần
nhất theo **thời gian** ±48 giờ của cùng cột, ghép ở server. ⚠️ **Hiện luôn `null`** cho tới khi BE-17
tạo `luminance_history` — khác với "không có điểm trong ±48 giờ"; CV-12 không phân biệt được hai ca
qua API. Nợ có chủ: BE-15/BE-17.

### 5.8 Đồng bộ offline — `[NOT IMPLEMENTED]` (BE-43)

- **`GET /api/v1/sync/bundle`** — `?segment_id=&since=` → `{generated_at, poles (5.1), segments (5.2),
  open_faults[] (item 5.4), work_orders[] (item 5.5)}`, trong phạm vi claim. Hình dạng đề xuất, chốt
  ở FW kế tiếp: `[OPEN → O-5]`.
- **`POST /api/v1/sync/push`** — `{operations[]{client_op_id, op_type, payload}}` → `{applied[]{client_op_id,
  id}, conflicts[]{client_op_id, reason, server_state}}`; khử trùng lặp theo `client_op_id`; xung đột:
  **server thắng**. Hình dạng đề xuất: `[OPEN → O-5]`.

---

## 6. Việc BE phải khớp

1. Bảng/cột `snake_case` thường, không quote. 2. `TIMESTAMPTZ` với `DateTimeKind.Utc`.
3. `geometry(Point|LineString, 4326)` + GIST; bbox dùng `ST_Intersects` với index, không quét bảng.
4. bbox < 500 ms với 2000 cột. 5. Enum chuỗi thường. 6. ID bằng sequence + `luxmap_format_id`.
7. `data_source` trên tám entity (mục 1.6). 8. `client_op_id` khử trùng lặp: `POST /faults`,
`POST /lux-readings`, `POST /sync/push` — trùng trả **200**. 9. Bộ hiệu chuẩn là `RoadSegment` thật.
10. Mọi cột `double precision` đo được có CHECK hữu hạn (`<> 'NaN'`, `<> 'Infinity'`).

## 7. Chưa chốt (chưa chặn FE)

Vector tile khi vượt ~5000 cột; realtime khi sweep xong (giai đoạn 1 polling). (Lưu ảnh dài hạn đã
chốt ở v1.7 — mục 3.3.)

## 8. Bộ mock FO-26 và việc FE làm được ngay

`mocks/`: **103 cột** (70 `normal` / 10 `dim` / 16 `out` / 7 `unknown`), cụm lỗi cả đoạn trên `SEG-003`,
**12 IoT node**, `POLE-0047` (`dim`, `NODE-047`, runtime suy giảm 18 đêm — v1.6 đổi cột này sang `grid`, chuỗi runtime giữ nguyên). Mock đã khớp bảng
prefix mục 1.2 từ 18/09/2026 (`NODE-047`, `SWP-001..030`, `FRM-088213`, `USR-004`; bỏ `supplier`) —
**WP5/WP6 phải kéo lại**. Việc FE làm được ngay: map shell MapLibre + 4 file mock; symbology 4 màu + icon `has_iot_node`/`near_sensitive_poi`; cluster ở zoom xa; panel chi tiết với 2 biểu đồ
(`baseline_ratio` + ngưỡng, `runtime_hours`); state map vào URL; bảng lỗi theo `priority_score`;
highlight `SEG-003`.

## 9. Open items

| O | Nội dung | Owner | Hạn |
|---|---|---|---|
| **O-1** | Tên tuyến `segment_name` BE ≠ FE (`SEG-001..003`) — cần người biết địa bàn | Thịnh/Ngọc | FW kế tiếp |
| **O-2** | Capability cho sweep/frame + review session (BE-15/17), fault (BE-19/41), work order + lịch làm việc (BE-21/24) — theo luật danh sách vai trò của mục 2 (v1.7) | Dylan + WP5/WP6 | cùng ticket, trước khi hiện thực |
| **O-3** | Máy trạng thái `wo_status` (BE-22) | Dylan | trước BE-21 |
| **O-4** | Enum `processing_status` của sweep; kích thước thumbnail (320/q80 tạm) | Dylan + WP5 | FW kế tiếp, trước W6 |
| **O-5** | Hình dạng `sync/bundle` / `sync/push` (đề xuất ở 5.8) | Dylan + WP6 | FW kế tiếp, trước W14 |
| **O-6** | `feeder_id` cho 103 cột mock: file gán riêng `mocks/mock-pole-feeders.csv` (D-7) — cần người biết mạch điện; chặn RQ2/CV-15 | Dylan + FO | trước BE-13 |
| **O-7** | FK ghép `(feeder_id, commune_id)` (D-10) — ticket riêng trước BE-13; tới lúc đó mọi đường ghi phải gọi kiểm cùng xã | BE1 | trước BE-13 |
| **O-8** | Thư viện Redocly `license` cho spec; server staging/prod trong `servers` | Dylan | khi có |
| **O-9** | `data_source` của telemetry **thật** từ thiết bị IoT trên testbed: `calibration_rig` hay giá trị mới — không được là `field` (v1.7, D-R10) | Dylan | trước IoT ingest |

## 10. Changelog

| Phiên bản | Ngày | Người quyết | Thay đổi |
|---|---|---|---|
| v1.7 | 25/09/2026 | **Mỹ (Dylan)** · `SELF-SIGNED` | **BREAKING, đổi giá trị claim `role`.** Vai trò theo Phiếu đăng ký FA26SE222 v1.2: `management_agency`→`superior`, `maintenance_engineer`→`manager`, `field_crew`→`field_engineer`, `administrator`→`system_admin` (song ánh; migration `RenameUserRolesToRegistrationV12`, `Down()` không mất dữ liệu). D-14 "một vai trò chính xác" → **danh sách vai trò chính xác cho mỗi capability**, vẫn cấm thứ bậc; ma trận ở mục 2. **Hai đổi hành vi:** ghi `/assets/*` + import chuyển từ Quản trị sang **Quản lý**; `POST /lux-readings` chỉ **Kỹ sư hiện trường** (trước: mọi vai trò). `POST /auth/register` **DEPRECATED**, gỡ ở BE-33a. Citizen không có account (QR, chưa có endpoint). Kèm: nguồn `data_source` theo phạm vi mới (mục 1.6, O-9); lux là số đọc tương đối (5.7); lưu trữ + cảnh báo hết bảo hành (3.3); audit trail bắt buộc (3.3); video và điều khiển ON/OFF/AUTO là planned (5.5, 5.6). Quyết định D-R1…D-R18 ở `docs/contract-drift.md`. ⚠️ Chạm bề mặt API và ký một mình → **chưa ổn định cho tới FW kế tiếp xác nhận** |
| v1.6 | 22/09/2026 | **Dylan** · `SELF-SIGNED` | **BREAKING, thu hẹp enum.** `power_source` còn `grid`; `fixture_type` còn `led_road_lamp`. Đèn solar ra khỏi phạm vi đồ án. Xoá giá trị thay vì để lại không dùng — giá trị enum không ai sinh ra được là giá trị ticket sau tưởng mình được ghi. Kèm migration `DropSolarFixtures` (đổi 45 hàng RỒI mới siết CHECK — ngược lại là migration gãy), bộ mock FO-26 đổi 45 cột, và bỏ `power_source` khỏi listing cột-chưa-gán của BE-13 (trường đó chỉ sinh ra để tách "solar nên không mạch" khỏi "chưa ai gán"). **`runtime_decline` GIỮ NGUYÊN** — chỉ đèn solar bị bỏ, phần runtime/IoT thì không. ⚠️ Chạm bề mặt API và ký một mình → **chưa ổn định cho tới FW kế tiếp xác nhận** |
| v1.5 | 19/09/2026 | **Dylan** | Thêm mục 4.7 `GET /api/v1/auth/me`. Không đổi hình dạng nào đã publish: 7 endpoint auth cũ giữ nguyên từng byte. Lý do: login chỉ trả token, và access token không mang `full_name`/`email` còn `commune_ids` thì đứng yên 60 phút |
| v1.4 | 18/09/2026 | **Dylan** (BE-REVIEW-02) | Hợp nhất ba nguồn. Gộp và đóng drift 2, 3, 4, 6, 7, 8, 10, 12, 14, 15, 17–21, 27–31, 33–39, 43 và quyết định A–E; mã lỗi mới `ROLE_FORBIDDEN` (D-4), `CROSS_COMMUNE_REFERENCE` (D-5), `POLE_HAS_ACTIVE_FIXTURE` (D-11); một bóng đang dùng/cột (D-11) và `removed_date >= install_date` (Q-4); `data_source` tám entity + mặc định loại `calibration_rig` (D-13); `user_role` + EXACT-ROLE + bảng ghi (D-14); fault MỞ (O-7 cũ); `commune_id` trong `POST /faults` (O-10 cũ); `from`/`to` không `Z` = UTC (D-12); `status_confidence` hữu hạn 0..1; mock đổi ID (D-9); §4 số liệu mock cập nhật. Đánh số mục lại, bảng ánh xạ ở đầu file |
| v1.3 | 15/09/2026 | Dylan | **BREAKING** `GET /poles/{id}/lux-readings` → `GET /lux-readings/poles/{id}` |
| v1.2 | 11/09/2026 | Dylan | Nhóm Auth (mobile + web cookie) |
| v1.1 | 24/08/2026 | cả nhóm, FW-00 | `manual` → `field_report`; `data_source`; quy ước ID; hình dạng item `GET /faults`; `POST /faults`; lux; phân quyền địa bàn |
| v1.0 | 23/08/2026 | cả nhóm, FW-00 | Bản đầu |
