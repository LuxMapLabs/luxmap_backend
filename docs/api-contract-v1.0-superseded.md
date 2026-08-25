# LuxMap — API Contract v1.0 (BẢN CHỐT)

**Trạng thái:** Chốt để FE bắt đầu làm ngay. BE hiện thực theo đúng bản này.
**Ngày chốt:** 2026-08-23
**Nguyên tắc:** Bản này là **hợp đồng**. Muốn đổi field/enum → mở issue, cả BE và FE cùng duyệt, tăng version. Không đổi ngầm.

---

## 0. Quy ước toàn cục (khoá cứng)

| Hạng mục | Quyết định | Ghi chú |
|---|---|---|
| Base URL | `/api/v1` | |
| Định dạng JSON | **snake_case** | .NET set `JsonNamingPolicy.SnakeCaseLower` |
| Hệ toạ độ API | **EPSG:4326** (WGS84) | GeoJSON thứ tự `[lng, lat]` |
| EPSG:3405 (VN-2000) | Chỉ dùng nội bộ DB / xuất báo cáo | **Không bao giờ trả ra API**. FE không reproject. |
| Thời gian | ISO 8601 UTC, hậu tố `Z` | DB `TIMESTAMPTZ`; Npgsql yêu cầu `DateTimeKind.Utc` |
| Ngày (không giờ) | `YYYY-MM-DD` | `install_date`, `warranty_expiry`, `night_of` |
| ID | string có prefix: `POLE-0001`, `FAULT-0001`, `SEG-001` | Dễ debug, FE không cần đoán kiểu |
| Phân trang | `?page=1&page_size=50` → `{page, page_size, total, items[]}` | `page_size` tối đa 200 |
| Lỗi | `{ "error": { "code": "...", "message": "...", "details": {} } }` | HTTP code chuẩn |
| Auth | `Authorization: Bearer <jwt>` | FE mock bằng token giả giai đoạn đầu |

### Quy tắc GeoJSON
- Mọi endpoint bản đồ trả về **`FeatureCollection`** chuẩn.
- Toàn bộ dữ liệu nghiệp vụ nằm trong `feature.properties` (phẳng, không lồng nhau) → gán trực tiếp vào MapLibre layer, không cần transform.
- `feature.id` **không dùng**; dùng `properties.pole_id`.

---

## 1. Enum — KHOÁ CỨNG (FE hardcode được, không cần gọi API)

```
fixture_status : normal | dim | out | unknown
power_source   : grid | solar
fixture_type   : led_road_lamp | solar_all_in_one
fault_type     : lamp_out | lamp_dim | segment_outage | node_offline | runtime_decline
fault_status   : detected | confirmed | rejected | in_progress | resolved | verified
severity       : low | medium | high | critical
source_channel : cv | iot | manual
wo_status      : open | assigned | in_progress | done | verified | cancelled
node_role      : segment_controller | sampled_fixture
node_status    : online | offline | never_reported
road_class     : inter_commune | inter_village
```

**Lưu ý cho FE:**
- `unknown` ≠ lỗi. Nghĩa là sweep gần nhất không phủ được cột đó (bị che, ảnh hỏng, chưa quét). Phải có màu/ký hiệu riêng, **không gộp vào `out`**.
- `dim` là trạng thái trung gian và là giá trị cốt lõi của đề tài → màu phải phân biệt rõ ở cả zoom xa. Không dùng vàng nhạt dễ chìm trên nền bản đồ.
- `runtime_decline` chỉ đến từ IoT, `lamp_dim`/`lamp_out` chỉ đến từ CV. Một cột có thể có **cả hai** cùng lúc.

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

---

### 2.3 `GET /api/v1/segments` — đoạn đường

Query: `bbox` (bắt buộc), `commune_id`.
Response: `FeatureCollection` của `LineString`.

`properties`: `segment_id, segment_name, road_class, length_m, pole_count, controller_node_id, has_active_segment_fault`

> Khi `has_active_segment_fault = true` → FE highlight **cả tuyến**, không chỉ từng điểm. Đây là output của Spatial Fault Clustering (lỗi breaker / đứt dây), khác về bản chất với lỗi từng bóng.

---

### 2.4 `GET /api/v1/faults` — danh sách lỗi (phân trang, KHÔNG phải GeoJSON)

Query: `bbox`, `status`, `severity`, `fault_type`, `source_channel`, `segment_id`, `cluster_id`, `sort` (mặc định `-priority_score`), `page`, `page_size`.

Mỗi item có `location: {lat, lng}` để FE vẫn chấm được lên bản đồ khi cần.
Xem `mock-faults.json`.

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
- `GET /api/v1/sweeps` → lịch sử các đợt quét: `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct, processing_status`
- `GET /api/v1/frames/{frame_id}/thumbnail` → ảnh JPEG

---

## 3. Đồng bộ offline (Field Module — Khang)

- `GET /api/v1/sync/bundle?segment_id=SEG-001&since=<iso>` → gói gọn: poles + segments + open faults + work orders được giao, để cache xuống máy.
- `POST /api/v1/sync/push` → đẩy thay đổi offline lên, mỗi item có `client_op_id` (UUID do client sinh).
- **Idempotency:** BE khử trùng lặp theo `client_op_id`. FE cứ retry thoải mái, không sợ tạo trùng.
- Xung đột: server thắng, trả `conflicts[]` để FE hiển thị cho người dùng xử lý.

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

## 5. Việc BE phải khớp

1. Bảng/cột PostgreSQL đặt **snake_case toàn chữ thường**, không quote.
2. Mọi `TIMESTAMPTZ` ghi/đọc bằng `DateTimeKind.Utc` — sai kind Npgsql sẽ ném exception.
3. Cột hình học lưu `geometry(Point, 4326)` / `geometry(LineString, 4326)`, có **GIST index** — query `bbox` bắt buộc dùng `ST_Intersects` với index, không quét bảng.
4. Endpoint `bbox` phải trả trong < 500ms với 2000 cột.
5. Trả đúng enum ở dạng chuỗi thường, **không trả số** (int enum của .NET sẽ làm hỏng FE).

---

## 6. Chưa chốt (cần quyết trong 2 tuần tới, chưa chặn FE)

- Vector tile thay GeoJSON khi vượt ~5000 cột
- Cơ chế realtime (WebSocket/SSE) khi sweep xử lý xong — giai đoạn 1 dùng polling
- Phân quyền chi tiết theo `commune_id`
- Định dạng lưu ảnh gốc & chính sách lưu trữ dài hạn
