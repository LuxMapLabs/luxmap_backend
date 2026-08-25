# LuxMap — API Contract v1.1 (BẢN HỢP NHẤT)

**Trạng thái:** Bản hợp nhất, thay thế cả `api-contract-v1.md` và bản bổ sung v1.1 rời. Đây là tài liệu duy nhất cần đọc.
**Ngày chốt v1.0:** 2026-08-23 · **Ngày hợp nhất v1.1:** 2026-08-24
**Nguyên tắc:** Bản này là **hợp đồng**. Muốn đổi field/enum → mở issue, cả BE và FE cùng duyệt, tăng version. Không đổi ngầm.

> **Về cách đánh số.** Task list v2.1 đã publish và trỏ tới số mục của v1.0 (mục 1, mục 2.1–2.3, mục 2.4, mục 2.5, mục 3). Vì vậy toàn bộ đánh số cũ được **giữ nguyên**, nội dung mới nối vào sau dưới dạng mục 2.8, 2.9 và mục 7. Thứ tự nhìn hơi lạ nhưng mọi tham chiếu trong task list vẫn đúng.

---

## Nhật ký thay đổi so với v1.0

| Mục | Thay đổi |
|---|---|
| 1 | `source_channel`: `manual` → **`field_report`** |
| 1 | Thêm enum **`data_source`** |
| 0 | Bổ sung quy ước ID đầy đủ: bảng prefix, cách sinh, quy tắc opaque |
| 2.4 | Chốt hình dạng từng item của `GET /faults` |
| 2.8 | **Mới** — `POST /faults` (BE-41) |
| 2.9 | **Mới** — nhóm endpoint lux readings (BE-42) |
| 6 | Gỡ "phân quyền theo `commune_id`" khỏi danh sách chưa chốt |
| 7 | **Mới** — đặc tả phân quyền theo địa bàn (BE-08) |

FE cần đọc lại: mục 1 (enum đổi), 2.4 (hình dạng item), 0.2 (prefix ID cho entity mới).

---

## 0. Quy ước toàn cục (khoá cứng)

| Hạng mục | Quyết định | Ghi chú |
|---|---|---|
| Base URL | `/api/v1` | |
| Định dạng JSON | **snake_case** | .NET set `JsonNamingPolicy.SnakeCaseLower` |
| Hệ toạ độ API | **EPSG:4326** (WGS84) | GeoJSON thứ tự `[lng, lat]` |
| EPSG:3405 (VN-2000) | Chỉ dùng nội bộ DB / xuất báo cáo | **Không bao giờ trả ra API.** FE không reproject. |
| Thời gian | ISO 8601 UTC, hậu tố `Z` | DB `TIMESTAMPTZ`; Npgsql yêu cầu `DateTimeKind.Utc` |
| Ngày (không giờ) | `YYYY-MM-DD` | `install_date`, `warranty_expiry`, `night_of` |
| ID | Chuỗi có prefix — xem 0.1–0.4 | Dễ debug, FE không cần đoán kiểu |
| Phân trang | `?page=1&page_size=50` → `{page, page_size, total, items[]}` | `page_size` tối đa 200 |
| Lỗi | `{ "error": { "code": "...", "message": "...", "details": {} } }` | HTTP code chuẩn |
| Auth | `Authorization: Bearer <jwt>` | FE mock bằng token giả giai đoạn đầu |

### Quy tắc GeoJSON

- Mọi endpoint bản đồ trả về **`FeatureCollection`** chuẩn.
- Toàn bộ dữ liệu nghiệp vụ nằm trong `feature.properties` (phẳng, không lồng nhau) → gán trực tiếp vào MapLibre layer, không cần transform.
- `feature.id` **không dùng**; dùng `properties.pole_id`.

### 0.1 Dạng ID

```
<PREFIX>-<số thứ tự đã pad 0>
```

- Prefix: **chữ in hoa, 2–5 ký tự, gợi nhớ được**. Không dùng prefix 2 ký tự trừ khi là viết tắt đã quen (`WO` = work order).
- Số chữ số: **4** cho thực thể khối lượng lớn, **3** cho thực thể khối lượng nhỏ, **6** cho frame và detection.

### 0.2 Bảng prefix

Bốn dòng đầu đã dùng trong bộ mock FO-26, không đổi:

| Entity | Prefix | Ví dụ |
|---|---|---|
| `Pole` | `POLE` | `POLE-0001` |
| `Fault` | `FAULT` | `FAULT-0001` |
| `RoadSegment` | `SEG` | `SEG-001` |
| `AdministrativeUnit` | `COM` | `COM-001` |
| `Fixture` | `FIX` | `FIX-0001` |
| `Feeder` | `FDR` | `FDR-001` |
| `IotNode` | `NODE` | `NODE-001` |
| `SurveySweep` | `SWP` | `SWP-001` |
| `SurveyFrame` | `FRM` | `FRM-000001` |
| `Detection` | `DET` | `DET-000001` |
| `LuxReading` | `LUX` | `LUX-0001` |
| `WorkOrder` | `WO` | `WO-0001` |
| `RepairEvidence` | `EVD` | `EVD-0001` |
| `ExternalUnit` | `EXT` | `EXT-001` |
| `AppUser` | `USR` | `USR-001` |
| Cụm sự cố (`cluster_id`) | `CLS` | `CLS-001` |

`LuminanceBaseline` và `TelemetryReading` **không có ID hiển thị** — khoá theo `(pole_id, ...)` và `(node_id, reading_time)`, không bao giờ tham chiếu trực tiếp từ FE.

### 0.3 ID là chuỗi mờ (opaque)

**Client không được phân tích cú pháp ID.** Không tách prefix, không parse số, không so sánh thứ tự, không giả định độ dài cố định.

Khi vượt ngưỡng chữ số, ID dài ra tự nhiên — cột thứ 10000 là `POLE-10000`. Không có cắt bớt, không có tràn số.

### 0.4 Cách sinh ID — phía server

Dùng **sequence của PostgreSQL**, sinh chuỗi ngay ở tầng DB:

```sql
CREATE SEQUENCE pole_id_seq;

ALTER TABLE pole
  ALTER COLUMN pole_id
  SET DEFAULT 'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0');
```

EF Core map bằng `.HasDefaultValueSql(...)` kèm `.ValueGeneratedOnAdd()`.

Sequence an toàn với concurrency sẵn — không cần lock hay bảng counter tự chế, và import hàng loạt (BE-12) không phải xử lý gì thêm. Chấp nhận: sequence không rollback nên insert lỗi để lại khoảng trống trong dãy số. ID chỉ cần duy nhất, không cần liên tục.

**Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID client sinh) làm khoá khử trùng lặp; server gán ID thật khi nhận và trả lại ánh xạ `client_op_id → <id thật>` để client cập nhật cache cục bộ.

---

## 1. Enum — KHOÁ CỨNG (FE hardcode được, không cần gọi API)

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

**Lưu ý cho FE:**

- `unknown` ≠ lỗi. Nghĩa là sweep gần nhất không phủ được cột đó (bị che, ảnh hỏng, chưa quét). Phải có màu/ký hiệu riêng, **không gộp vào `out`**.
- `dim` là trạng thái trung gian và là giá trị cốt lõi của đề tài → màu phải phân biệt rõ ở cả zoom xa. Không dùng vàng nhạt dễ chìm trên nền bản đồ.
- `runtime_decline` chỉ đến từ IoT, `lamp_dim`/`lamp_out` chỉ đến từ CV. Một cột có thể có **cả hai** cùng lúc.

**`source_channel` và `data_source` là hai chiều khác nhau.** `source_channel` cho biết *kênh nào phát hiện ra*; `data_source` cho biết *dữ liệu đến từ đâu*. Một sự cố có thể mang `source_channel = cv` và `data_source = calibration_rig` cùng lúc.

`data_source` gắn trên `SurveySweep`, `SurveyFrame`, `Fault`, `TelemetryReading`, `LuxReading`. **Mọi endpoint thống kê phải lọc và nhóm được theo trường này** — CV-11, CV-18 và IOT-16 đều yêu cầu báo cáo tách riêng theo nguồn dữ liệu, không lưu từ lúc ingest thì tới lúc thống kê không tách ra được. Giá trị `field` giữ cho tương lai; phạm vi hiện tại không sinh bản ghi nào mang giá trị này.

---

## 2. Endpoints

### 2.1 `GET /api/v1/poles` — cột đèn trên bản đồ

Query params:

| Param | Kiểu | Bắt buộc | Ví dụ |
|---|---|---|---|
| `bbox` | `minLng,minLat,maxLng,maxLat` | **Có** | `106.48,10.96,106.52,10.99` |
| `status` | CSV enum | Không | `dim,out` |
| `power_source` | enum | Không | `solar` |
| `segment_id` | string | Không | `SEG-001` |
| `commune_id` | string | Không | `COM-001` |
| `has_open_fault` | bool | Không | `true` |

**Bắt buộc truyền `bbox`.** Không có endpoint "lấy tất cả" — tránh FE vô tình kéo vài nghìn cột.
Nếu bbox trả > 2000 cột → BE trả `413` kèm `error.code = "BBOX_TOO_LARGE"`, FE hiển thị "Phóng to để xem chi tiết".

Response: `FeatureCollection` — xem `mock-poles.geojson`.

`properties` của mỗi cột:

```
pole_id, segment_id, fixture_status, status_confidence (0..1|null),
power_source, fixture_type, lamp_watt, install_date, warranty_expiry,
commune_id, last_seen_at, last_sweep_id, open_fault_count,
has_iot_node (bool), near_sensitive_poi (bool)
```

> `near_sensitive_poi` = gần trường học/chợ/cầu/ngã ba. Dùng cho ưu tiên sửa chữa — FE nên có icon phụ.

---

### 2.2 `GET /api/v1/poles/{pole_id}` — chi tiết + lịch sử

Trả về đầy đủ để dựng panel chi tiết **trong 1 request** (không bắt FE gọi 4 lần).
Xem `mock-pole-detail.json`. Gồm:

- `fixture` — thông số lắp đặt, bảo hành
- `current_status` — trạng thái hiện tại + kênh xác định (`cv`/`iot`)
- `iot_node` — `null` nếu cột không có node (đa số cột sẽ là `null`)
- `luminance_baseline` — `baseline_value`, `dim_threshold_ratio` (0.80), `out_threshold_ratio` (0.15)
- `luminance_history[]` — chuỗi thời gian đã chuẩn hoá, mỗi điểm có `baseline_ratio` + `classified_as`
- `runtime_history[]` — chỉ có khi cột có IoT node; `runtime_hours` theo từng đêm
- `open_faults[]`
- `recent_frames[]` — ảnh tham chiếu, có `thumbnail_url`

**Điểm FE cần nắm:** biểu đồ độ sáng phải vẽ **`baseline_ratio`** (tỉ lệ so với chính cột đó), **không phải** giá trị tuyệt đối, và phải kẻ đường ngưỡng `dim_threshold_ratio`. Đây là cách duy nhất người dùng hiểu vì sao hệ thống gọi một cột là `dim`.

Ngưỡng `dim_threshold_ratio` và `out_threshold_ratio` **cấu hình được qua BE-33**, không hard-code.

---

### 2.3 `GET /api/v1/segments` — đoạn đường

Query: `bbox` (bắt buộc), `commune_id`.
Response: `FeatureCollection` của `LineString`.

`properties`: `segment_id, segment_name, road_class, length_m, pole_count, controller_node_id, has_active_segment_fault`

> Khi `has_active_segment_fault = true` → FE highlight **cả tuyến**, không chỉ từng điểm. Đây là output của Spatial Fault Clustering (lỗi breaker / đứt dây), khác về bản chất với lỗi từng bóng.

---

### 2.4 `GET /api/v1/faults` — danh sách lỗi (phân trang, KHÔNG phải GeoJSON)

Query: `bbox`, `status`, `severity`, `fault_type`, `source_channel`, `data_source`, `segment_id`, `cluster_id`, `sort` (mặc định `-priority_score`), `page`, `page_size`.

Hình dạng mỗi item:

```
fault_id, pole_id (nullable), fixture_id (nullable), segment_id (nullable),
location { lat, lng },
fault_type, fault_status, severity, source_channel, data_source,
priority_score, status_confidence (0..1 | null),
cluster_id (nullable), detected_at, updated_at,
work_order_id (nullable), note, reported_by (nullable)
```

Xem `mock-faults.json`.

- `location` luôn có, để FE vẫn chấm được lên bản đồ khi cần.
- `priority_score` là số thực do CV-16 tính. **Client không sắp xếp lại phía mình** — thứ tự mặc định do server quyết.
- `cluster_id` khác null nghĩa là sự cố thuộc một cụm cấp đoạn; FE nên gộp hiển thị thay vì liệt kê từng dòng.

---

### 2.5 `PATCH /api/v1/faults/{fault_id}` — kỹ sư xác nhận/bác bỏ

```json
{ "fault_status": "confirmed", "override_fault_type": "lamp_dim", "note": "Đã kiểm tra tại chỗ" }
```

- Chỉ cho phép: `detected → confirmed | rejected`, `confirmed → in_progress → resolved → verified`
- BE trả `409` nếu chuyển trạng thái không hợp lệ → FE disable nút thay vì để user bấm rồi lỗi.

---

### 2.6 Work Orders

- `GET /api/v1/work-orders` — query: `wo_status`, `assigned_to`, `segment_id`, phân trang. Xem `mock-work-orders.json`.
- `POST /api/v1/work-orders` — body: `{ title, fault_ids[], assigned_to?, due_date? }`
- `PATCH /api/v1/work-orders/{id}` — đổi `wo_status`, gán người
- `POST /api/v1/work-orders/{id}/evidence` — `multipart/form-data`: `file`, `kind=before|after`, `captured_at`, `lat`, `lng`

### 2.7 IoT & Sweeps (ưu tiên thấp cho FE giai đoạn 1)

- `GET /api/v1/iot-nodes?bbox=` → `FeatureCollection` (xem `mock-iot-nodes.geojson`)
- `GET /api/v1/sweeps` → lịch sử các đợt quét: `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct, processing_status, data_source`
- `GET /api/v1/frames/{frame_id}/thumbnail` → ảnh JPEG

---

### 2.8 `POST /api/v1/faults` — báo sự cố tại hiện trường (BE-41)

Cho tổ khảo sát báo sự cố phát sinh tại chỗ (FM-19).

```json
{
  "client_op_id": "uuid-do-client-sinh",
  "pole_id": "POLE-0123",
  "fixture_id": "FIX-0145",
  "location": { "lat": 10.9712, "lng": 106.4983 },
  "fault_type": "lamp_out",
  "severity": "medium",
  "note": "Bóng vỡ, thấy khi đi qua",
  "photo_frame_id": null
}
```

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `client_op_id` | Có | UUID client sinh, khử trùng lặp khi retry hoặc đồng bộ offline |
| `pole_id` | **Không** | Null khi sự cố ở cột chưa có trong hồ sơ tài sản |
| `fixture_id` | Không | Chỉ khi cột mang nhiều bóng và biết rõ bóng nào |
| `location` | **Có khi `pole_id` null** | Bắt buộc để kỹ sư tìm được chỗ; bỏ qua nếu đã có `pole_id` |
| `fault_type` | Có | Không nhận `segment_outage`, `node_offline`, `runtime_decline` — ba loại này chỉ engine sinh |
| `severity` | Không | Mặc định `medium` |
| `note` | Có | Tối thiểu 10 ký tự |
| `photo_frame_id` | Không | Ảnh chụp tại chỗ, upload trước qua luồng evidence |

**Server áp cứng, client không set được:** `source_channel` = `field_report`, `data_source` = `field`, `fault_status` = **`detected`**, `reported_by` = user trong JWT.

`fault_status` khởi tạo là `detected` chứ không phải `confirmed`, kể cả khi người báo là cán bộ. Sự cố do người báo vẫn đi qua đúng một luồng duyệt như sự cố do engine sinh — kỹ sư xác nhận, không ai tạo thẳng ra sự cố đã xác nhận.

**Response `201`:** đối tượng fault đầy đủ, cùng dạng một item của mục 2.4, kèm `fault_id` đã gán và `client_op_id` gửi lên.

| Code | HTTP | Khi nào |
|---|---|---|
| `POLE_NOT_FOUND` | 404 | `pole_id` không tồn tại |
| `LOCATION_REQUIRED` | 400 | Không có `pole_id` mà cũng không có `location` |
| `FAULT_TYPE_NOT_REPORTABLE` | 400 | `fault_type` thuộc nhóm chỉ engine sinh |
| `DUPLICATE_OP` | **200** | `client_op_id` đã xử lý — trả lại fault đã tạo, **không phải lỗi** |

`DUPLICATE_OP` trả 200 chứ không phải 409, vì client retry là hành vi bình thường ở chế độ offline.

---

### 2.9 Lux readings (BE-42)

Ground truth cho RQ1. CV-12 đối chiếu số đo lux với phân loại của hệ thống.

**`POST /api/v1/lux-readings`**

```json
{
  "client_op_id": "uuid",
  "pole_id": "POLE-0047",
  "measured_at": "2026-10-02T19:42:00Z",
  "lux_value": 12.4,
  "meter_model": "UNI-T UT383",
  "data_source": "calibration_rig",
  "note": "Mức suy giảm 60%"
}
```

`lux_value` là số thực, đơn vị lux, không âm. `measured_at` phải là ISO 8601 UTC hậu tố `Z`. `data_source` bắt buộc — trong Nhánh C hầu hết là `calibration_rig`.
Response `201` kèm `lux_id`. Trùng `client_op_id` → **200** và trả bản ghi đã có.

**`GET /api/v1/poles/{pole_id}/lux-readings`** — chuỗi số đo của một cột, sắp theo `measured_at` tăng dần. Dùng cho panel chi tiết cột.

**`GET /api/v1/lux-readings`** — query: `pole_id`, `from`, `to`, `data_source`, `page`, `page_size`. Phân trang chuẩn.

Dùng cho CV-12 kéo toàn bộ số đo về đối chiếu hàng loạt. Mỗi item kèm sẵn **`nearest_luminance`** — điểm `luminance_history` gần nhất về thời gian của cùng cột, gồm `baseline_ratio`, `classified_as` và `observed_at`; `null` nếu không có điểm nào trong ±48 giờ.

Ghép cặp ở server thay vì để CV-12 tự ghép: logic tìm điểm gần nhất phải giống hệt giữa lần chạy phân tích và lần chạy báo cáo. Nếu mỗi bên tự ghép thì hai con số sẽ khác nhau mà không ai biết vì sao.

**Bộ hiệu chuẩn được mô hình hoá như tài sản thật.** Đăng ký bộ hiệu chuẩn FO-07 như một `RoadSegment` bình thường (ví dụ `SEG-900`) với các `Pole` và `Fixture` tương ứng, `data_source = calibration_rig`. Nhờ vậy toàn bộ pipeline chạy không cần nhánh riêng — baseline, phân loại, lux, biểu đồ lịch sử đều dùng chung một đường. Nếu tách thành thực thể riêng thì mọi truy vấn phải xử lý hai trường hợp, và phần kiểm chứng ở CV-09 sẽ không chạy đúng đường mà hệ thống thật chạy.

---

## 3. Đồng bộ offline (Field Module)

- `GET /api/v1/sync/bundle?segment_id=SEG-001&since=<iso>` → gói gọn: poles + segments + open faults + work orders được giao, để cache xuống máy.
- `POST /api/v1/sync/push` → đẩy thay đổi offline lên, mỗi item có `client_op_id` (UUID do client sinh).
- **Idempotency:** BE khử trùng lặp theo `client_op_id`. FE cứ retry thoải mái, không sợ tạo trùng.
- Xung đột: server thắng, trả `conflicts[]` để FE hiển thị cho người dùng xử lý.
- `sync/bundle` chỉ đóng gói dữ liệu **trong phạm vi địa bàn của user** — xem mục 7.

---

## 4. Việc FE làm được NGAY (không chờ BE)

1. Dựng map shell với MapLibre GL JS + nền OSM raster, load thẳng 4 file mock kèm theo.
2. Chốt symbology: 4 màu trạng thái + phân biệt grid/solar + icon phụ cho `has_iot_node` và `near_sensitive_poi` + legend.
3. Cluster ở zoom xa, hiện từng cột ở zoom gần (MapLibre có sẵn `cluster: true`).
4. Panel chi tiết cột từ `mock-pole-detail.json` — gồm 2 biểu đồ: `baseline_ratio` theo đêm (có đường ngưỡng), và `runtime_hours` theo đêm.
5. Đồng bộ state map vào URL: `?lat=&lng=&zoom=&pole=&status=`
6. Bảng danh sách lỗi sắp theo `priority_score`, click → bay tới cột trên bản đồ.
7. Highlight `SEG-003` để test luồng hiển thị lỗi cả đoạn.

Mock data đã cố tình cài sẵn: 103 cột (70 normal / 9 dim / 17 out / 7 unknown), một cụm lỗi cả đoạn trên `SEG-003`, 11 IoT node, và một cột solar (`POLE-0047`) có chuỗi runtime suy giảm dần 18 đêm — dùng để test biểu đồ cảnh báo sớm pin.

---

## 5. Việc BE phải khớp

1. Bảng/cột PostgreSQL đặt **snake_case toàn chữ thường**, không quote.
2. Mọi `TIMESTAMPTZ` ghi/đọc bằng `DateTimeKind.Utc` — sai kind Npgsql sẽ ném exception.
3. Cột hình học lưu `geometry(Point, 4326)` / `geometry(LineString, 4326)`, có **GIST index** — query `bbox` bắt buộc dùng `ST_Intersects` với index, không quét bảng.
4. Endpoint `bbox` phải trả trong **< 500ms với 2000 cột**.
5. Trả đúng enum ở dạng chuỗi thường, **không trả số** (int enum của .NET sẽ làm hỏng FE).
6. Sinh ID bằng sequence PostgreSQL theo bảng prefix mục 0.2 — **quyết trước BE-09**, sửa sau là phải sửa toàn bộ entity.
7. Thêm `data_source` vào `SurveySweep`, `SurveyFrame`, `Fault`, `TelemetryReading`, `LuxReading`; mọi API thống kê lọc và nhóm được theo trường này.
8. `client_op_id` khử trùng lặp áp dụng cho `POST /faults`, `POST /lux-readings` và `POST /sync/push` — trùng thì trả **200**, không phải lỗi.
9. Đăng ký bộ hiệu chuẩn như `RoadSegment` thật, không tạo thực thể riêng.

---

## 6. Chưa chốt (chưa chặn FE)

- Vector tile thay GeoJSON khi vượt ~5000 cột
- Cơ chế realtime (WebSocket/SSE) khi sweep xử lý xong — giai đoạn 1 dùng polling
- Định dạng lưu ảnh gốc & chính sách lưu trữ dài hạn

*(Phân quyền chi tiết theo `commune_id` đã chuyển sang mục 7 — không còn để mở.)*

---

## 7. Phân quyền theo địa bàn (BE-08)

JWT mang claim `commune_ids` — mảng các `COM-xxx` mà user được phép truy cập.

| Vai trò | Phạm vi |
|---|---|
| Kỹ sư bảo trì | Đúng các xã trong claim |
| Tổ khảo sát / sửa chữa | Đúng các xã trong claim |
| Cơ quan quản lý | Có thể gồm nhiều xã |
| Quản trị | Toàn hệ thống, claim mang giá trị đặc biệt `*` |

Quy tắc:

- **Lọc ở server, luôn luôn.** Mọi truy vấn tự động giới hạn trong phạm vi claim, kể cả khi client không truyền gì.
- Query param `commune_id` là bộ lọc **thu hẹp trong phạm vi được phép**, không phải cách mở rộng phạm vi.
- Yêu cầu `commune_id` ngoài phạm vi → **403** `COMMUNE_FORBIDDEN`.
- Truy cập trực tiếp một tài nguyên ngoài phạm vi (ví dụ `GET /poles/POLE-9999`) → **404**, không phải 403. Trả 403 sẽ tiết lộ tài nguyên đó tồn tại.
- `GET /sync/bundle` chỉ đóng gói dữ liệu trong phạm vi claim.
