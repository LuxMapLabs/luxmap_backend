# LuxMap — API Contract v1.2 (DRAFT HỢP NHẤT) — BE-REVIEW-02

**Trạng thái:** BẢN NHÁP, chưa có hiệu lực. Sinh ra ở BE-REVIEW-02 (18/09/2026) bằng cách gộp
`docs/api-contract-v1.1.md` (tiêu đề bên trong đã là **v1.3**), `docs/contract-drift.md` (mục 1–43 và
quyết định A–F) và `docs/openapi/luxmap-v1.json` (sinh từ code, 21 operation), rồi đối chiếu với code
thật trên `origin/dev` (`f8bb55b`).

**Số hiệu:** tên file theo đề bài là `v1.2`, nhưng file hiện hành đã tự gọi là v1.3 → số hiệu thật
(v1.4 hay v2.0) là **D-1, chờ Dylan**. Bản OpenAPI đi kèm: `docs/openapi/luxmap-v1.2.draft.json`
(khớp 1-1 về method + path + trường với tài liệu này; kiểm bằng script ở BE-REVIEW-02 mục 4).

**Nguyên tắc của bản nháp này**

| Ký hiệu | Nghĩa |
|---|---|
| *(không ký hiệu)* | **FOLD** — đã chốt, đã có trong code hoặc đã nằm trong v1.3, người quyết rõ ràng |
| `[SELF-SIGNED]` | Đã nằm trong v1.3 từ 07/09/2026 nhưng chưa được Thịnh/Ngọc xác nhận. **Giữ nguyên chữ**, không đổi, và liệt kê lại ở Open items |
| `[NOT IMPLEMENTED]` | Contract đã đặc tả, code chưa có. **Không xoá.** |
| `[PENDING DYLAN → O-n]` | CONFIRM / CONFLICT / drift chưa đăng ký. Nội dung nằm ở **mục 9 Open items**, thân Contract chỉ trỏ tới |

> Thân Contract chỉ chứa mục **FOLD** và phần chữ đã publish ở v1.3. Mọi thứ khác nằm ở mục 9.
> Riêng nhóm `/assets/…` (drift 29/30/33/34/43 — thông qua theo hạn, chưa duyệt) được **liệt kê ở
> mục 5.3 với nhãn `[PENDING DYLAN → O-4]`** vì code đang phục vụ nó và OpenAPI phải khớp 1-1; nhãn
> nói rõ nó chưa phải Contract.

---

## 1. Tổng quan và quy ước toàn cục

### 1.1 Bảng quy ước (khoá cứng — kế thừa v1.0)

| Hạng mục | Quyết định | Ghi chú |
|---|---|---|
| Base URL | `/api/v1` | Version nằm trong URL, không header |
| JSON | **snake_case** | `JsonNamingPolicy.SnakeCaseLower` |
| Enum | **chuỗi thường** | Không bao giờ int |
| Hệ toạ độ API | **EPSG:4326**, GeoJSON `[lng, lat]` | EPSG:3405 chỉ nội bộ DB / báo cáo, **không bao giờ ra API** |
| Thời gian | ISO 8601 UTC hậu tố `Z` | DB `TIMESTAMPTZ`, `DateTimeKind.Utc` |
| Ngày không giờ | `YYYY-MM-DD` | `install_date`, `warranty_expiry`, `night_of`, `due_date` |
| ID | Chuỗi có prefix — mục 1.2 | Chuỗi đục |
| Phân trang | `?page=1&page_size=50` → `{page, page_size, total, items[]}` | `page_size` tối đa 200 |
| Lỗi | `{ "error": { "code", "message", "details" } }` | Mục 1.4 |
| Auth | `Authorization: Bearer <jwt>` | Mục 5.1 |

### 1.2 ID — dạng, bảng prefix, cách sinh (v1.0 §0.1–0.4, khuôn ID theo quyết định A `[SELF-SIGNED]`)

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

- **Số chữ số là TỐI THIỂU.** `POLE-9999` kế tiếp là `POLE-10000`. Client **không** parse, **không**
  so sánh thứ tự, **không** giả định độ dài. `[0-9]` chứ không phải `\d`.
- **Sinh ở server** bằng sequence PostgreSQL + hàm `luxmap_format_id(prefix, value, digits)` —
  `lpad(value::text, greatest(digits, length(value::text)), '0')`. **Không dùng `LPAD(..., 4, '0')`
  trần** (cắt bớt ID). `[SELF-SIGNED]` quyết định D — code đã đúng từ `8ea9930`; xem O-3.
- `LuminanceBaseline`, `TelemetryReading`, `RefreshToken` không có ID hiển thị.
- **Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID); server trả ánh xạ.

### 1.3 Phân trang

`{page, page_size, total, items[]}`. `total` là tổng dòng khớp bộ lọc. `page_size` tối đa **200**.
Hành vi khi vượt 200 và khi giá trị không phải số: `[PENDING DYLAN → O-2]` (drift 7).

### 1.4 Mô hình lỗi

```json
{ "error": { "code": "BBOX_TOO_LARGE", "message": "...", "details": {} } }
```

`details` là túi ngữ cảnh tự do, **luôn có mặt** (rỗng là `{}`). Header `X-Correlation-Id` có trên
**mọi** response. Vị trí thứ hai của correlation id (`details.correlation_id`): `[PENDING DYLAN → O-2]`
(drift 4).

**Mã lỗi đã có trong Contract v1.3** (thân Contract):

| Mã | HTTP | Khi nào | Nguồn |
|---|---|---|---|
| `BBOX_TOO_LARGE` | 413 | bbox quá 2000 cột | §2.1 |
| `POLE_NOT_FOUND` | 404 | `pole_id` không tồn tại hoặc ngoài phạm vi | §2.8, §2.9 |
| `LOCATION_REQUIRED` | 400 | không `pole_id` và không `location` | §2.8 |
| `FAULT_TYPE_NOT_REPORTABLE` | 400 | `fault_type` thuộc nhóm chỉ engine sinh | §2.8 |
| `DUPLICATE_OP` | **200** | `client_op_id` đã xử lý — không phải lỗi | §2.8, §5.8 |
| `COMMUNE_FORBIDDEN` | 403 | `commune_id` ngoài phạm vi claim | §7 |
| `VALIDATION_FAILED` | 400 | body sai định dạng / thiếu trường | §2.10.6 |
| `INVALID_CREDENTIALS` | 401 | sai tài khoản **hoặc** mật khẩu — một mã | §2.10.6 |
| `ACCOUNT_LOCKED` | 403 | đúng mật khẩu, tài khoản khoá | §2.10.6 |
| `INVALID_REFRESH_TOKEN` | 401 | refresh token thiếu/sai/hết hạn/thu hồi/dùng lại/sai nhóm | §2.10.6 |
| `ORIGIN_NOT_ALLOWED` | 403 | `Origin` thiếu/lạ/`null` (chỉ nhóm web) | §2.10.2 |
| `IDENTIFIER_TAKEN` | 409 | đăng ký trùng username/email | §2.10.6 |

**Mã lỗi code đang phát mà Contract chưa ghi** — `[PENDING DYLAN → O-2]`: `INTERNAL_ERROR`,
`UNAUTHENTICATED`, `UNSUPPORTED_IMAGE_FORMAT`, `UNSUPPORTED_MEDIA_TYPE`, `ASSET_NOT_FOUND`,
`EXTERNAL_REF_TAKEN`, `ASSET_IN_USE`, `SERVER_OWNED_FIELD`, `NOT_FOUND`, `METHOD_NOT_ALLOWED`,
`REQUEST_FAILED`, và việc 403 do **sai vai trò** hiện trả `COMMUNE_FORBIDDEN`.

### 1.5 GeoJSON và toạ độ

- Endpoint bản đồ trả **`FeatureCollection`** chuẩn; dữ liệu nghiệp vụ **phẳng** trong
  `feature.properties`; **không dùng `feature.id`**, dùng `properties.<x>_id`.
- API luôn EPSG:4326. FE không reproject. Không tính khoảng cách bằng trừ toạ độ.

### 1.6 Hai chiều dữ liệu: `source_channel` và `data_source`

`source_channel` = kênh phát hiện (`cv | iot | field_report`). `data_source` = dữ liệu đến từ đâu
(`field | public_imagery | calibration_rig | simulated`). Một bản ghi mang cả hai. Mọi endpoint thống
kê phải lọc và nhóm được theo `data_source`. Danh sách entity mang `data_source`: `[PENDING DYLAN → O-5]`
(drift 10 — §1 v1.3 vẫn ghi 5 entity, code có 8).

---

## 2. Phân quyền theo địa bàn (§7 v1.1 — FOLD)

JWT mang claim `commune_ids` (mảng). Quản trị mang `["*"]`.

| Vai trò (tiếng Việt) | Phạm vi |
|---|---|
| Kỹ sư bảo trì | Đúng các xã trong claim |
| Tổ khảo sát / sửa chữa | Đúng các xã trong claim |
| Cơ quan quản lý | Có thể gồm nhiều xã |
| Quản trị | Toàn hệ thống, `*` |

- **Lọc ở server, luôn luôn.** Query param `commune_id` chỉ **thu hẹp**; ngoài phạm vi → **403
  `COMMUNE_FORBIDDEN`**. Truy cập trực tiếp tài nguyên ngoài phạm vi → **404**, không 403.
- `GET /sync/bundle` chỉ đóng gói dữ liệu trong phạm vi claim.
- Giá trị enum trên dây của 4 vai trò và **vai trò nào được GHI** gì: `[PENDING DYLAN → O-6]`
  (drift 3, 31, 31b).

---

## 3. Mô hình miền, enum, máy trạng thái

### 3.1 Enum — KHOÁ CỨNG (12 enum, §1 v1.0; FE hardcode)

```
fixture_status : normal | dim | out | unknown
power_source   : grid | solar
fixture_type   : led_road_lamp | solar_all_in_one
fault_type     : lamp_out | lamp_dim | segment_outage | node_offline | runtime_decline
fault_status   : detected | confirmed | rejected | in_progress | resolved | verified
severity       : low | medium | high | critical
source_channel : cv | iot | field_report
data_source    : field | public_imagery | calibration_rig | simulated
wo_status      : open | assigned | in_progress | done | verified | cancelled
node_role      : segment_controller | sampled_fixture
node_status    : online | offline | never_reported
road_class     : inter_commune | inter_village
```

- `unknown` ≠ lỗi (sweep gần nhất không phủ được cột). Ký hiệu riêng, **không gộp vào `out`**.
- `runtime_decline` chỉ từ IoT; `lamp_dim`/`lamp_out` chỉ từ CV; một cột có thể mang cả hai.
- Enum vai trò người dùng (`user_role`): `[PENDING DYLAN → O-6]`.

### 3.2 Máy trạng thái `fault_status` (§2.5 — FOLD)

```
detected → confirmed | rejected
confirmed → in_progress → resolved → verified
```

Chuyển sai luồng → **409**. Định nghĩa "fault MỞ" cho `open_fault_count`, `has_open_fault`,
`open_faults[]`: `[PENDING DYLAN → O-7]` (drift 27; code đã chốt `detected | confirmed | in_progress`).

`wo_status` chưa có luồng đặc tả (BE-22) — `[PENDING DYLAN → O-8]`.

### 3.3 Thực thể (tham chiếu)

`Pole` · `Fixture` · `RoadSegment` · `Feeder` · `IotNode` · `TelemetryReading` · `SurveySweep` ·
`SurveyFrame` · `Detection` · `LuminanceBaseline` · `LuxReading` · `Fault` · `FaultCluster` ·
`WorkOrder` · `ExternalUnit` · `RepairEvidence` · `AdministrativeUnit` · `AppUser` · `RefreshToken`.
`Pole` và `Fixture` tách riêng; trạng thái thuộc vị trí cột (`pole_current_status`). Bộ hiệu chuẩn
FO-07 được đăng ký như `RoadSegment`/`Pole`/`Fixture` thật với `data_source = calibration_rig` (§2.9).

---

## 4. Xác thực — `/api/v1/auth` (§2.10 v1.2 — FOLD, quyết định F, Dylan 11/09/2026)

Hai nhóm, client chọn bằng đường dẫn. Cả 7 endpoint không cần access token. Access token sống 60
phút, luôn ở body, không bao giờ trong cookie.

### 4.1 Nhóm mobile — `implemented`

**`POST /api/v1/auth/login`** — body `{ "username", "password" }` → `200` đúng bốn trường:

```json
{ "access_token": "<jwt>", "refresh_token": "<chuỗi mờ>", "token_type": "Bearer", "expires_in": 3600 }
```

**`POST /api/v1/auth/refresh`** — body `{ "refresh_token" }` → `200` cùng bốn trường; token cũ thu
hồi ngay; client phải lưu token mới.

**`POST /api/v1/auth/logout`** — body `{ "refresh_token" }` → **`204`** với mọi giá trị; thiếu trường
→ `400 VALIDATION_FAILED`.

**`POST /api/v1/auth/register`** — body `{ "username", "email", "full_name", "password" }` → `201`:

```json
{ "user_id": "USR-005", "username": "...", "email": "...", "full_name": "...",
  "role": "field_crew", "commune_ids": [],
  "message": "Account created. An administrator must assign communes before any data becomes visible." }
```

Server áp cứng `role` và `commune_ids`; mật khẩu tối thiểu 12 ký tự; trùng → `409 IDENTIFIER_TAKEN`;
không trả token. Giới hạn độ dài: xem OpenAPI.

### 4.2 Nhóm web — `implemented`

**`POST /api/v1/auth/web/login`** — body `{ "username", "password", "remember_me"? }` → `200` đúng ba
trường `{ access_token, token_type, expires_in }` + `Set-Cookie`. Cookie web còn hiệu lực trong request
sẽ bị thu hồi trước khi mở phiên mới.

**`POST /api/v1/auth/web/refresh`** — không body; đọc cookie → `200` ba trường + `Set-Cookie` mới.
Cookie thiếu/sai → `401 INVALID_REFRESH_TOKEN`, **không đụng cookie**.

**`POST /api/v1/auth/web/logout`** — không body; luôn `204`, luôn `Set-Cookie` xoá.

**Chặn `Origin`** cho cả ba: thiếu / lạ / `null` → `403 ORIGIN_NOT_ALLOWED`. CORS: `Allow-Origin` =
đúng origin, `Allow-Credentials: true`, expose `X-Correlation-Id`.

### 4.3 Cookie refresh token

| Thuộc tính | Giá trị |
|---|---|
| Tên | `__Secure-luxmap_rt` |
| `HttpOnly` / `Secure` | có / có |
| `SameSite` | `Lax` |
| `Path` | `/api/v1/auth/web` |
| `Domain` | không set |
| `Expires` | `remember_me = true`: hạn refresh token; ngược lại **không set** (cookie phiên) |

### 4.4 Loại phiên và thời hạn

| Loại | Tạo bởi | Refresh token | Trần |
|---|---|---|---|
| `mobile` | `/auth/login` | 30 ngày trượt | 90 ngày |
| `web_persistent` | `/auth/web/login`, `remember_me=true` | 14 ngày trượt | 90 ngày |
| `web_session` | `/auth/web/login`, `remember_me` false/thiếu | 12 giờ tuyệt đối | 12 giờ |

Token nhóm này không dùng được ở nhóm kia (`401`, không thu hồi). Dùng lại token đã thay: trong 30 s
→ `401` phiên vẫn sống; sau 30 s → `401` và thu hồi cả chuỗi. Hai refresh cùng lúc: đúng một thắng.

### 4.5 Claim trong access token

| Claim | Kiểu | Ví dụ |
|---|---|---|
| `sub` | chuỗi | `USR-001` |
| `role` | chuỗi đơn | `maintenance_engineer` |
| `commune_ids` | luôn là mảng | `["COM-001"]` · Quản trị `["*"]` |
| `iss` / `aud` | chuỗi | `luxmap-api` / `luxmap-clients` |

### 4.6 Ràng buộc triển khai web

FE và API **cùng site** (cùng eTLD+1, cùng `https`). Gọi từ browser với `credentials: 'include'`;
không gọi nhóm auth từ server của FE.

---

## 5. Endpoint theo resource

### 5.1 Cột đèn — `[NOT IMPLEMENTED]` (BE-14, BE-20)

**`GET /api/v1/poles`** — §2.1. Query: `bbox` **bắt buộc** (`minLng,minLat,maxLng,maxLat`), `status`
(CSV enum), `power_source`, `segment_id`, `commune_id`, `has_open_fault`. Quá 2000 cột → `413
BBOX_TOO_LARGE`. Response `FeatureCollection`, `properties`:

```
pole_id, segment_id, fixture_status, status_confidence (0..1|null), power_source, fixture_type,
lamp_watt, install_date, warranty_expiry, commune_id, last_seen_at, last_sweep_id,
open_fault_count, has_iot_node, near_sensitive_poi
```

`status_confidence` là `null` **khi và chỉ khi** `fixture_status = unknown` (CHECK trong DB).
`data_source`, `external_ref`, `feeder_id` **không** emit. Mặc định loại `calibration_rig`? →
`[PENDING DYLAN → O-5]`.

**`GET /api/v1/poles/{pole_id}`** — §2.2. Đủ trong **một** request, hình dạng theo
`mock-pole-detail.json`: `pole_id, segment_id, segment_name, commune_id, location{lat,lng}, fixture{…},
current_status{fixture_status, status_confidence, determined_at, source_channel}, iot_node|null,
luminance_baseline{baseline_value, baseline_window_nights, dim_threshold_ratio, out_threshold_ratio,
computed_at}, luminance_history[]{observed_at, sweep_id, normalized_luminance, baseline_ratio,
classified_as}, runtime_history[]{night_of, runtime_hours, on_at, off_at, source} (chỉ khi có node),
open_faults[]{fault_id, fault_type, severity, fault_status, priority_score}, recent_frames[]{frame_id,
sweep_id, captured_at, thumbnail_url, distance_m, heading_deg}`. `baseline_ratio` và `classified_as`
**tính ở backend**. Ngưỡng cấu hình qua BE-33 (mặc định 0.80 / 0.15). Trường `supplier` trong mock
**không** đưa vào (O-9, drift 6). Ngoài phạm vi → `404`.

### 5.2 Đoạn đường — `[NOT IMPLEMENTED]` (BE-14)

**`GET /api/v1/segments`** — §2.3. Query `bbox` bắt buộc, `commune_id`. `FeatureCollection` của
`LineString`; `properties`: `segment_id, segment_name, road_class, length_m, pole_count,
controller_node_id, has_active_segment_fault`. `has_active_segment_fault = true` → FE highlight cả tuyến.

### 5.3 Quản lý kiểm kê tài sản — `/api/v1/assets/…` — `implemented` — `[PENDING DYLAN → O-4]`

> Không có trong Contract v1.3. Drift 29/30/33/34 thông qua theo hạn (B, `SELF-SIGNED`), drift 43
> `SELF-SIGNED` 18/09/2026. Liệt kê ở đây vì code đang phục vụ; **chưa phải Contract**. Ghi = Quản trị;
> đọc = mọi vai trò đã đăng nhập (O-6).

| Endpoint | Body / Query | Trả về |
|---|---|---|
| **`GET /api/v1/assets/segments`** | `commune_id[]`, `page`, `page_size` | `PagedResult<string>` — **chỉ ID** (chỗ giữ chỗ tới BE-12b, drift 34) |
| **`GET /api/v1/assets/feeders`** | như trên | như trên |
| **`GET /api/v1/assets/poles`** | như trên | như trên |
| **`POST /api/v1/assets/segments`** | `{external_ref?, segment_name, road_class, length_m, geom_wkt, commune_id, data_source}` | `201` + `Location`, không body; `409 EXTERNAL_REF_TAKEN` |
| **`POST /api/v1/assets/feeders`** | `{external_ref?, feeder_name, commune_id, geom_wkt?}` | `201` + `Location` |
| **`POST /api/v1/assets/poles`** | `{external_ref?, segment_id, feeder_id?, commune_id, geom_wkt, near_sensitive_poi?, data_source}` | `201` + `Location`; `404 ASSET_NOT_FOUND` tuyến/mạch; `403` mạch khác xã |
| **`POST /api/v1/assets/fixtures`** | `{pole_id, fixture_type, power_source, lamp_watt, install_date, removed_date?, warranty_expiry?, data_source}` — `commune_id` chép từ cột | `201` + `Location` |
| **`DELETE /api/v1/assets/poles/{poleId}`** | — | `204`; `404 ASSET_NOT_FOUND`; `409 ASSET_IN_USE` (khoá ngoại từ chối, `details.constraint/table`) |
| **`PUT /api/v1/assets/poles/{poleId}/feeder`** | `{"feeder_id": "FDR-001" \| null}` — khoá **bắt buộc**, `null` là giá trị | `204`; thiếu khoá → `400`; mạch khác xã → `403` |
| **`PUT /api/v1/assets/fixtures/{fixtureId}/removal`** | `{removed_date}` | `204` |
| **`POST /api/v1/assets/import/{kind}`** | multipart `file` ≤ 10 MB, `kind ∈ segments\|feeders\|poles\|fixtures`, `.csv` hoặc `.geojson` | **`200`** `{inserted, updated, failed, total_errors, truncated, rows[]{row, column, message}}` — 200 kể cả khi có dòng hỏng; `rows[]` cắt ở 100 |

Upsert theo `(commune_id, external_ref)` cho tuyến/mạch/cột; bóng **insert-only**. Mỗi request nạp
đúng một loại; thứ tự tự thực thi qua tham chiếu `*_external_ref`. Mẫu file: `docs/templates/`.

### 5.4 Sự cố

**`GET /api/v1/faults`** — §2.4 — `[NOT IMPLEMENTED]` (BE-40). Phân trang JSON, **không** GeoJSON.
Query: `bbox, status, severity, fault_type, source_channel, data_source, pole_id, segment_id,
cluster_id, sort (mặc định -priority_score), page, page_size`. `pole_id` nhận **một** giá trị; cột không
tồn tại và cột ngoài phạm vi trả **giống hệt**: `200` + rỗng `[SELF-SIGNED]` (A → O-3). Mỗi item:

```
fault_id, pole_id|null, fixture_id|null, segment_id|null, location{lat,lng},
fault_type, fault_status, severity, source_channel, data_source,
priority_score, status_confidence (0..1|null), cluster_id|null, detected_at, updated_at,
work_order_id|null, note, reported_by|null
```

`work_order_id` **luôn `null`** cho tới BE-21, khoá vẫn có mặt `[SELF-SIGNED]` (C → O-3).

**`PATCH /api/v1/faults/{fault_id}`** — §2.5 — `[NOT IMPLEMENTED]` (BE-19). Body
`{ fault_status, override_fault_type?, note? }`; sai luồng → `409`.

**`POST /api/v1/faults`** — §2.8 — `[NOT IMPLEMENTED]` (BE-41). Body:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `client_op_id` | Có | UUID, khử trùng lặp |
| `pole_id` | Không | null khi cột chưa có trong hồ sơ |
| `fixture_id` | Không | |
| `location` | Có khi `pole_id` null | |
| `fault_type` | Có | không nhận `segment_outage`, `node_offline`, `runtime_decline` |
| `severity` | Không | mặc định `medium` |
| `note` | Có | ≥ 10 ký tự |
| `photo_frame_id` | Không | |

Server áp cứng `source_channel = field_report`, `data_source = field`, `fault_status = detected`,
`reported_by` = JWT. `201` item đầy đủ kèm `client_op_id`; trùng → **`200 DUPLICATE_OP`**;
`404 POLE_NOT_FOUND`; `400 LOCATION_REQUIRED` / `FAULT_TYPE_NOT_REPORTABLE`. `commune_id` khi
`pole_id` null: `[PENDING DYLAN → O-10]` (drift 25).

### 5.5 Phiếu công việc — §2.6 — `[NOT IMPLEMENTED]` (BE-21..BE-24)

- **`GET /api/v1/work-orders`** — query `wo_status`, `assigned_to`, `segment_id`, phân trang; item theo
  `mock-work-orders.json`: `work_order_id, title, segment_id, cluster_id, fault_ids[], wo_status,
  assigned_to, priority_score, created_at, due_date`.
- **`POST /api/v1/work-orders`** — `{ title, fault_ids[], assigned_to?, due_date? }` → `201`.
- **`PATCH /api/v1/work-orders/{work_order_id}`** — đổi `wo_status`, gán người; luồng: O-8.
- **`POST /api/v1/work-orders/{work_order_id}/evidence`** — `multipart/form-data`: `file` (JPEG theo
  magic bytes), `kind=before|after`, `captured_at`, `lat`, `lng`; sai định dạng → `415
  UNSUPPORTED_IMAGE_FORMAT` (drift 15 → O-2).

### 5.6 IoT, sweep, ảnh — §2.7 — `[NOT IMPLEMENTED]`

- **`GET /api/v1/iot-nodes`** — `bbox` bắt buộc → `FeatureCollection`; `properties` theo
  `mock-iot-nodes.geojson`: `node_id, node_role, node_status, pole_id, segment_id, battery_pct,
  last_report_at`.
- **`GET /api/v1/sweeps`** — `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct,
  processing_status, data_source`; enum `processing_status` chưa có → O-11.
- **`GET /api/v1/frames/{frame_id}/thumbnail`** — JPEG, **proxy qua API, không presigned**; kích thước
  320 px / q80 là **tạm** (O-11).

### 5.7 Số đo lux — §2.9 — `implemented`

**`POST /api/v1/lux-readings`**

```json
{ "client_op_id": "uuid", "pole_id": "POLE-0047", "measured_at": "2026-10-02T19:42:00Z",
  "lux_value": 12.4, "meter_model": "UNI-T UT383", "data_source": "calibration_rig", "note": "..." }
```

`lux_value` số thực, không âm, **không NaN/Infinity** (400 ở API, CHECK ở DB). `201` kèm `lux_id`;
trùng `client_op_id` → **`200`** bản ghi đã có. Response: `lux_id, client_op_id, pole_id, measured_at,
lux_value, meter_model, data_source, note`. Bắt buộc/không, `measured_by`, `SERVER_OWNED_FIELD`:
`[PENDING DYLAN → O-12]` (drift 18, 19, 21).

**`GET /api/v1/lux-readings/poles/{poleId}`** — chuỗi của một cột, sắp `measured_at` tăng dần. **Đổi
đường dẫn ở v1.3 (BREAKING)** — đường cũ `/poles/{id}/lux-readings` trả 404. Có phân trang:
`[PENDING DYLAN → O-12]` (drift 20).

**`GET /api/v1/lux-readings`** — query `pole_id, from, to, data_source, page, page_size`. Mỗi item
kèm `nearest_luminance{baseline_ratio, classified_as, observed_at}|null` — điểm gần nhất theo **thời
gian** ±48 giờ, ghép ở server. Hiện **luôn `null`** cho tới BE-17: `[PENDING DYLAN → O-12]` (drift 17).
`from`/`to` phải có hậu tố `Z` (xem BE-REVIEW-02 F-02).

### 5.8 Đồng bộ offline — §3 — `[NOT IMPLEMENTED]` (BE-43)

- **`GET /api/v1/sync/bundle`** — `?segment_id=&since=` → poles + segments + open faults + work orders
  được giao, trong phạm vi claim. Hình dạng từng khối **suy ra** từ §5.1/5.2/5.4/5.5 → O-13.
- **`POST /api/v1/sync/push`** — mỗi item có `client_op_id`; khử trùng lặp; xung đột: **server thắng**,
  trả `conflicts[]`. Hình dạng chưa đặc tả → O-13.

---

## 6. Việc BE phải khớp (§5 v1.1 — FOLD, giữ nguyên)

1. Bảng/cột `snake_case` thường, không quote. 2. `TIMESTAMPTZ` với `DateTimeKind.Utc`.
3. `geometry(Point|LineString, 4326)` + GIST; bbox dùng `ST_Intersects` với index.
4. bbox < 500 ms với 2000 cột. 5. Enum chuỗi thường. 6. ID bằng sequence + `luxmap_format_id`.
7. `data_source` trên các entity (O-5). 8. `client_op_id` khử trùng lặp: `POST /faults`,
`POST /lux-readings`, `POST /sync/push` — trùng trả **200**. 9. Bộ hiệu chuẩn là `RoadSegment` thật.

## 7. Chưa chốt (§6 v1.1)

Vector tile khi vượt ~5000 cột; realtime khi sweep xong (giai đoạn 1 polling); lưu ảnh dài hạn.

## 8. Changelog v1.1 → v1.2 (draft)

| Mục | Thay đổi | Drift gốc | Loại |
|---|---|---|---|
| Toàn bộ | Gộp ba nguồn thành một tài liệu; mỗi endpoint mang trạng thái `implemented` / `[NOT IMPLEMENTED]` | — | cấu trúc |
| 1.2 | Khuôn ID `[0-9]{n,}` cho 16 prefix; hàm `luxmap_format_id` thay `LPAD` | 13, 14, 37, 39 (A, D) | FOLD (D là sửa lỗi) / A còn `[SELF-SIGNED]` |
| 1.4 | Bảng mã lỗi hợp nhất từ §2.1/2.8/7/2.10.6 | — | FOLD |
| 4 | Nhóm `/auth` mobile + web chép nguyên từ v1.2 §2.10 | 1, 5, 41 (F) | FOLD |
| 5.4 | `pole_id` trên `GET /faults`; `work_order_id` luôn `null` | 35, 38 (A, C) | `[SELF-SIGNED]` → O-3 |
| 5.7 | Đường dẫn `/lux-readings/poles/{id}` (v1.3) | — | FOLD |
| 5.1 | `status_confidence` null ⇔ `unknown` (bất biến đã có CHECK) | 12 | FOLD |
| 5.4 | Hình dạng item `GET /faults` — mock đã khớp §2.4 | 28, 36 | FOLD (mock) |
| 9 | Mọi CONFIRM / CONFLICT / drift chưa đăng ký | 2, 3, 4, 6, 7, 8, 10, 12, 15, 17–21, 23, 25, 27, 29–34, 42, 43, 31b | Open items |

**Không đưa vào Contract (nội bộ, đã ghi ở CLAUDE.md):** drift 9, 11, 16, 22, 24, 26, 32, 40 (phần
quy trình).

---

## 9. Open items — `[PENDING DYLAN]`

Mỗi mục là một lựa chọn. Số hiệu O-n dùng chung với BE-REVIEW-02 mục 5.

| O | Nội dung | Phương án | Đề xuất của reviewer |
|---|---|---|---|
| **O-1** | Số hiệu bản hợp nhất và tên file (D-1). File hiện hành `api-contract-v1.1.md` đã ghi v1.3 bên trong | (a) v1.4, giữ tên file; (b) v2.0 vì gộp lớn + có BREAKING v1.3; (c) v1.2 theo đề bài (mâu thuẫn với header) | **(a)** — thay đổi là gộp + ghi rõ, không phá thêm hình dạng nào |
| **O-2** | Mã lỗi ngoài Contract (drift 2, 8, 15, 21, 33, 43 + `NOT_FOUND`, `METHOD_NOT_ALLOWED`, `REQUEST_FAILED`, `UNSUPPORTED_MEDIA_TYPE`), correlation id ở `details` (drift 4), `page_size` kẹp im lặng (drift 7), route sai → 401 khi chưa đăng nhập (drift 8), **403 sai vai trò đang trả `COMMUNE_FORBIDDEN`** | (a) FOLD nguyên trạng, thêm bảng vào 1.4; (b) FOLD nhưng tách mã mới `ROLE_FORBIDDEN` cho sai vai trò; (c) giữ ngoài Contract | **(b)** — `COMMUNE_FORBIDDEN` có nghĩa §7 rõ ràng; WP5/WP6 sẽ hiện sai thông điệp |
| **O-3** | Ba quyết định `SELF-SIGNED` đã nằm trong v1.3: A (`pole_id` filter + khuôn ID), C (`work_order_id` null), D (`LPAD` → hàm); và 43 (`DELETE`/`PUT feeder`/`ASSET_IN_USE`) | (a) xác nhận cả bốn; (b) xác nhận D (sửa lỗi) + 43, để A/C tới FW kế tiếp | **(a)** — code đã khớp cả bốn; A/C chưa có code nên xác nhận không tốn gì |
| **O-4** | Nhóm `/assets/…` (drift 29, 30, 33, 34, 43): đường dẫn, hình dạng import 200, `GET` trả ID (hạn dùng BE-12b) | (a) FOLD mục 5.3 thành Contract, ghi `GET` là tạm; (b) chờ BE-12b rồi FOLD một lần | **(a)** — FE cần biết nó tồn tại và không phải `/poles` |
| **O-5** | `data_source` trên `Pole`, `Fixture`, `RoadSegment` (drift 10 — chốt nhưng chữ §1 chưa đổi); mặc định `GET /poles` loại `calibration_rig` (CLAUDE.md nói có, Contract không nói) | (a) sửa §1 thành 8 entity + ghi rõ mặc định loại `calibration_rig` và param `data_source` để lấy; (b) chỉ sửa §1 | **(a)** — thống kê trộn ba nguồn là lỗi nghiêm trọng, phải là luật Contract |
| **O-6** | Enum `user_role` (drift 3: `management_agency \| maintenance_engineer \| field_crew \| administrator`); vai trò nào được GHI (drift 31); EXACT-ROLE hay HIERARCHY (31b) | (a) FOLD 4 giá trị + EXACT-ROLE + bảng ghi (assets: Quản trị); (b) FOLD 4 giá trị, hoãn ma trận ghi | **(a)** — BE-12a đã tạo tiền lệ; đổi sau BE-15/18/21 đắt hơn |
| **O-7** | Định nghĩa fault MỞ (drift 27) = `detected \| confirmed \| in_progress` | (a) FOLD; (b) khác | **(a)** — đã có `FaultStatusSets.Open` + 2 test canh |
| **O-8** | Máy trạng thái `wo_status` (BE-22) chưa đặc tả | (a) đặc tả ở FW kế tiếp trước BE-21; (b) để BE-22 tự đề xuất | **(a)** |
| **O-9** | Mock lệch bảng prefix (drift 6): `USR-khang`, `SWEEP-2026-…`, `FRM-88213`, `SUP-004`, `NODE-0047` (4 chữ số); tên tuyến BE≠FE (drift 42); `feeder_id` thiếu trong mock (drift 23) | (a) sửa mock theo Contract + báo WP5/WP6; (b) đổi Contract theo mock (NODE 4 chữ số) | **(a)** cho 6/42; **23 cần người biết địa bàn** |
| **O-10** | `POST /faults`: `commune_id` trong body khi `pole_id` null, user nhiều xã phải chọn (drift 25) | (a) thêm trường tuỳ chọn `commune_id` vào §5.4; (b) từ chối `pole_id` null với user nhiều xã | **(a)** |
| **O-11** | `processing_status` của sweep chưa có enum; thumbnail 320 px / q80 tạm | (a) chốt enum + kích thước ở FW kế tiếp; (b) để BE-15/17 đề xuất | **(a)** — FE cần trước W6 |
| **O-12** | §2.9: `pole_id` và `client_op_id` bắt buộc, `meter_model`/`note` tuỳ chọn (18); `measured_by` từ JWT, không emit (19); phân trang `/lux-readings/poles/{id}` (20); `SERVER_OWNED_FIELD` (21); `nearest_luminance` luôn null tới BE-17 (17) | (a) FOLD cả năm; (b) khác | **(a)** — tất cả đã chạy và có test |
| **O-13** | Hình dạng `sync/bundle` và `sync/push` (§3 chỉ nói bốn khối) | (a) chốt hình dạng draft 5.8 ở FW kế tiếp; (b) để BE-43 đề xuất W14 | **(a)** — FM-20 chặn |
| **O-14** | Số phận `contract-drift.md` (D-2) | (a) archive thành `docs/archive/contract-drift-v1.md` + pointer sang changelog, mở `docs/contract-drift.md` mới cho v1.4; (b) giữ nguyên, đánh dấu ĐÓNG từng mục | **(a)** |
