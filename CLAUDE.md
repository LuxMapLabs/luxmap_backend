# LuxMap — Backend (WP2)

Nền tảng GIS + IoT + Computer Vision quản lý tài sản và sự cố chiếu sáng đường nông thôn.
Capstone FA26SE222 · W1–W21: 07/09/2026 – 31/01/2027 · Repo này là **WP2, phụ trách: BE1 – Mỹ**.

Backend phục vụ 3 consumer: **Web SPA** (WP5), **Android native** (WP6), **engine CV** (WP4).
Không có consumer nào khác. Không có API công khai cho người dân.

## Nguồn sự thật

Ba tài liệu, thứ tự ưu tiên khi mâu thuẫn:

1. **`docs/api-contract-v1.1.md`** — bản hợp nhất, chốt 24/08/2026. **Thắng mọi thứ khác.** Đã gộp v1.0 và bản bổ sung; không cần đọc `api-contract-v1.md` nữa.
2. **`docs/tasks-backend.csv`** — task list v2.1, phạm vi và lịch.
3. File này — quy ước làm việc và những chỗ dễ sai. Không phải đặc tả.

Khi phát hiện mâu thuẫn: làm theo Contract, ghi lại chỗ lệch, nêu ở buổi review — đừng tự ý chọn bên nào.

> **Lệch `manual` / `field_report` đã xử lý:** bản hợp nhất chốt **`field_report`**, khớp task list. Giá trị `manual` của v1.0 đã bỏ.

---

## Phạm vi đã chốt — Nhánh C

Chốt 24/08/2026 (FO-01). **Không có thử nghiệm hiện trường.** Ba nguồn dữ liệu:

| Nguồn | Dùng cho | Tính chất |
|---|---|---|
| Ảnh đêm công khai | Phần phát hiện | Không có metadata phơi sáng, nhãn cảm quan |
| Bộ hiệu chuẩn tự dựng (FO-07) | Chuẩn hoá + phân loại | Ground truth photometric **duy nhất** |
| Dữ liệu IoT mô phỏng (FO-25) | Runtime | Mô phỏng, neo vào tài liệu công bố |

**Hệ quả cho backend:** nguồn dữ liệu phải giữ tách bạch từ lúc ingest tới lúc thống kê. Nếu không lưu được nguồn ngay ở `SurveySweep` và `Fault`, tới lúc báo cáo sẽ không tách ra được nữa — và CV-11, CV-18, IOT-16 đều yêu cầu báo cáo tách riêng. Gộp số liệu giữa dữ liệu có ground truth thật và dữ liệu gán nhãn cảm quan là lỗi nghiêm trọng, không phải chi tiết trình bày.

Trường riêng cho việc này đã được chốt ở **mục 1**:

```
data_source : field | public_imagery | calibration_rig | simulated
```

Gắn trên `SurveySweep`, `SurveyFrame`, `Fault`, `TelemetryReading`, `LuxReading`. Mọi API thống kê phải lọc và nhóm được theo trường này (BE-28, BE-30).

**Cộng thêm `Pole`, `Fixture`, `RoadSegment`** (chốt ở BE-09). Contract mục 1 không liệt kê ba cái này nhưng mục 2.9 bắt buộc phải có — không có `data_source` ở tầng tài sản thì không tách được cột hiệu chuẩn khỏi cột thật lúc thống kê. Contract sẽ lên v1.2. **Ba cột này LƯU và LỌC được, nhưng KHÔNG emit ra `properties`** — bộ mock là nguồn chuẩn cho hình dạng response và không có trường này.

**Đây là chiều khác với `source_channel`.** `source_channel` = kênh nào phát hiện ra (`cv` / `iot` / `field_report`). `data_source` = dữ liệu đến từ đâu. Một sự cố có thể mang `source_channel = cv` và `data_source = calibration_rig` cùng lúc.

**Bộ hiệu chuẩn FO-07 được đăng ký như `RoadSegment` thật** (mục 2.9), không tạo thực thể riêng — để pipeline chạy đúng một đường, không rẽ nhánh.

---

## Quy ước toàn cục — Contract mục 0

- Base URL `/api/v1`
- **JSON snake_case** — `JsonNamingPolicy.SnakeCaseLower`
- **Enum trả về là chuỗi thường.** Int enum của .NET sẽ làm hỏng FE — đây là lỗi Contract nêu đích danh.
- **Thời gian: ISO 8601 UTC, hậu tố `Z`.** DB `TIMESTAMPTZ`, Npgsql yêu cầu `DateTimeKind.Utc` — sai kind là ném exception. Xử lý ở biên, không vá tại chỗ gọi.
- **Ngày không giờ: `YYYY-MM-DD`** — `install_date`, `warranty_expiry`, `night_of`.
- **ID là chuỗi có prefix:** `POLE-0001`, `FAULT-0001`, `SEG-001`, `COM-001`. Không phải `int`, không phải `Guid`. Bảng prefix đầy đủ và cách sinh ở **mục 0.1–0.4**.
- **Phân trang:** `?page=1&page_size=50` → `{page, page_size, total, items[]}`. `page_size` **tối đa 200**.
- **Lỗi:** `{ "error": { "code": "...", "message": "...", "details": {} } }` + correlation id.
- Auth: `Authorization: Bearer <jwt>`.

### GeoJSON

- Endpoint bản đồ trả **`FeatureCollection`** chuẩn.
- Toạ độ thứ tự **`[lng, lat]`**.
- Dữ liệu nghiệp vụ nằm **phẳng** trong `feature.properties`, không lồng nhau — FE gán thẳng vào MapLibre layer.
- **Không dùng `feature.id`.** Dùng `properties.pole_id`.

### Toạ độ

- **API luôn trả EPSG:4326.**
- **EPSG:3405 (VN-2000) chỉ dùng nội bộ DB và xuất báo cáo — không bao giờ trả ra API.** FE không reproject.
- Không tính khoảng cách bằng cách trừ toạ độ.

---

## Enum — khoá cứng, Contract mục 1

```
fixture_status : normal | dim | out | unknown
power_source   : grid | solar
fixture_type   : led_road_lamp | solar_all_in_one
fault_type     : lamp_out | lamp_dim | segment_outage | node_offline | runtime_decline
fault_status   : detected | confirmed | rejected | in_progress | resolved | verified
severity       : low | medium | high | critical
source_channel : cv | iot | field_report        # v1.0 ghi 'manual', đã bỏ
data_source    : field | public_imagery | calibration_rig | simulated
wo_status      : open | assigned | in_progress | done | verified | cancelled
node_role      : segment_controller | sampled_fixture
node_status    : online | offline | never_reported
road_class     : inter_commune | inter_village
```

**Không thêm giá trị, không đổi tên, không dùng int.** FE đã hardcode.

Ràng buộc nghiệp vụ đi kèm:

- **`unknown` không phải lỗi** — nghĩa là sweep gần nhất không phủ được cột đó. Có ký hiệu riêng ở FE, **không gộp vào `out`** ở bất kỳ thống kê nào (BE-28).
- `runtime_decline` **chỉ** đến từ IoT. `lamp_dim` và `lamp_out` **chỉ** đến từ CV. Một cột có thể mang **cả hai cùng lúc** — mô hình dữ liệu phải cho phép.
- Luồng `fault_status` hợp lệ: `detected → confirmed | rejected`, rồi `confirmed → in_progress → resolved → verified`. Chuyển sai luồng → **409** để FE disable nút trước, không để user bấm rồi mới lỗi (BE-19).

---

## Endpoint đã đặc tả — không tự thiết kế lại

| Endpoint | Ràng buộc |
|---|---|
| `GET /poles` | `bbox` **bắt buộc**, không có endpoint "lấy tất cả". Lọc: `status`, `power_source`, `segment_id`, `commune_id`, `has_open_fault`. Quá 2000 cột → **413** `BBOX_TOO_LARGE`. |
| `GET /poles/{id}` | Trả **đủ trong 1 request**: `fixture`, `current_status`, `iot_node` (null với đa số cột), `luminance_baseline`, `luminance_history[]`, `runtime_history[]` (chỉ khi có node), `open_faults[]`, `recent_frames[]`. |
| `GET /segments` | `bbox` bắt buộc. `FeatureCollection` của `LineString`. |
| `GET /faults` | **Phân trang JSON, KHÔNG phải GeoJSON.** Mỗi item có `location{lat,lng}`. Sắp mặc định `-priority_score`. |
| `PATCH /faults/{id}` | Body: `fault_status`, `override_fault_type?`, `note?`. |
| `GET/POST/PATCH /work-orders` | `POST` body: `{title, fault_ids[], assigned_to?, due_date?}` |
| `POST /work-orders/{id}/evidence` | multipart: `file`, `kind=before\|after`, `captured_at`, `lat`, `lng` |
| `GET /iot-nodes` | `bbox`, trả `FeatureCollection` |
| `GET /sweeps` | `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct, processing_status` |
| `GET /sync/bundle` | `?segment_id=&since=` → poles + segments + open faults + work orders được giao |
| `POST /sync/push` | Khử trùng lặp theo `client_op_id` (UUID client sinh). Xung đột: **server thắng**, trả `conflicts[]` |

### `properties` của `GET /poles`

```
pole_id, segment_id, fixture_status, status_confidence (0..1|null),
power_source, fixture_type, lamp_watt, install_date, warranty_expiry,
commune_id, last_seen_at, last_sweep_id, open_fault_count,
has_iot_node, near_sensitive_poi
```

### `properties` của `GET /segments`

```
segment_id, segment_name, road_class, length_m, pole_count,
controller_node_id, has_active_segment_fault
```

`has_active_segment_fault = true` → FE highlight **cả tuyến**. Đây là output của spatial clustering (CV-15), khác bản chất với lỗi từng bóng.

### `luminance_baseline` và `luminance_history`

`baseline_value`, `dim_threshold_ratio` (mặc định **0.80**), `out_threshold_ratio` (mặc định **0.15**) — cấu hình được qua BE-33, **không hard-code**.

Mỗi điểm trong `luminance_history` phải có sẵn **`baseline_ratio`** và **`classified_as`**. **Tính ở backend.** FW-13 vẽ biểu đồ theo `baseline_ratio` kèm đường ngưỡng — nếu backend trả giá trị tuyệt đối thì FE sẽ tự tính và hai bên lệch nhau.

---

## Contract phủ tới đâu

Contract v1.0 viết để **gỡ chặn FE**, nên chỉ phủ phần đọc cho bản đồ. Những task sau **không có trong Contract** — cần đặc tả trước khi hiện thực, đừng tự sinh endpoint rồi coi như xong:

| Task | Thiếu |
|---|---|
| BE-07 | Endpoint đăng ký / đăng nhập / refresh |
| BE-12 | CRUD tài sản + import CSV |
| BE-15, BE-16 | Upload sweep, validate metadata phơi sáng |
| BE-27 | Notification — **chốt tên bảng/entity cùng FE2 trước W16** |
| BE-28→31 | Toàn bộ dashboard và thống kê |
| BE-33→35 | Quản trị danh mục, node, model version |
| ~~BE-41~~ | ~~`POST /faults`~~ → **đã đặc tả ở mục 2.8** |
| ~~BE-42~~ | ~~endpoint lux~~ → **đã đặc tả ở mục 2.9** |

BE-41 và BE-42 đã được đặc tả ở bản hợp nhất (mục 2.8 và 2.9). Phần còn lại trong bảng vẫn chưa có đặc tả — cần thống nhất trước khi hiện thực, đừng tự sinh endpoint rồi coi như xong.

Còn để mở: vector tile khi vượt ~5000 cột, realtime khi sweep xong (giai đoạn 1 dùng polling), chính sách lưu ảnh dài hạn. Phân quyền theo `commune_id` **đã chốt ở mục 7**, không còn để mở.

---

## Domain model

`Pole` · `Fixture` · `RoadSegment` · `Feeder` · `IotNode` · `TelemetryReading` ·
`SurveySweep` · `SurveyFrame` · `Detection` · `LuminanceBaseline` · `LuxReading` ·
`Fault` · `FaultType` · `FaultHistory` · `WorkOrder` · `ExternalUnit` · `RepairEvidence` ·
`AdministrativeUnit` · `AppUser` · `RefreshToken`

Điểm dễ sai:

- **`Pole` và `Fixture` tách riêng** (BE-09). Cột là kết cấu vật lý; bóng là thiết bị gắn trên đó. Một cột mang được nhiều bóng, bóng thay được trong khi cột vẫn tồn tại. Lịch sử tình trạng thuộc về **vị trí cột** — nên `Fixture` **không có cột trạng thái nào**. CV đọc ảnh đêm: nó thấy một nguồn sáng ở một vị trí, không tách được bóng số 1 với bóng số 2. Và trạng thái theo từng bóng sẽ buộc phải có quy tắc tổng hợp, mà quy tắc đúng thì không tồn tại: hai bóng một `out` một `unknown` đòi hỏi xếp `unknown` vào thang bậc so với `out`, đúng thứ Contract mục 1 cấm.
- **`SurveyFrame` và `RepairEvidence` là hai luồng ảnh riêng** (BE-11). Ảnh khảo sát là dữ liệu chính, không phải file đính kèm.
- **`LuxReading`** (BE-42) cần xong ở **W4** — FO-14 đo lux ở W5, FM-14 và CV-12 phụ thuộc.
- Ảnh **không** nằm trong database. MinIO giữ bytes, row giữ key.

### Hai quy tắc chốt ở BE-09 — áp cho 14 entity còn lại

**1. Khi nào denormalize `commune_id` và implement `ICommuneScoped`.**

> Entity nào **có thể là gốc của một truy vấn** thì mang `commune_id` và implement `ICommuneScoped`.
> Entity nào **bao giờ cũng đi tới qua một gốc đã scope** thì không.

`Pole` có `commune_id` sẵn. `Fixture` và `PoleCurrentStatus` được **denormalize thêm** — không phải
vì tiện, mà vì cả hai rất dễ trở thành gốc truy vấn: một dashboard viết
`context.Set<PoleCurrentStatus>().GroupBy(...)` là rò dữ liệu ngay. Có `ICommuneScoped` thì chốt chặn
lúc dựng model bắt được; dựa vào "nhớ join" thì chỉ code review bắt được.

`SurveyFrame` và `TelemetryReading` thì **không** — chúng luôn đi qua sweep hoặc node.

⚠️ Chốt chặn chỉ thấy entity **đã** implement interface. Entity có `commune_id` mà quên implement sẽ
lọt — đó là giới hạn thật.

**2. Tạo schema và quyền ghi là hai chuyện khác nhau.**

`pole_current_status` do **BE-09 tạo bảng** (BE-14 chạy trước BE-15 và cần 4 trường đó), nhưng
**BE-15/BE-17 sở hữu quyền ghi**. BE-12 (CRUD tài sản + import CSV) **không được đụng vào bảng này**.
Đó là lý do nó là bảng riêng chứ không phải 4 cột trên `pole` — ranh giới nằm trong lược đồ, không
chỉ trong quy ước.

Nợ FK duy nhất của BE-09: `pole_current_status.last_sweep_id` là `text` chưa có FK; BE-15 thêm ràng
buộc trong migration của nó.

### Vai trò

**Cơ quan quản lý** · **Kỹ sư bảo trì** · **Tổ khảo sát/sửa chữa** · **Quản trị**.
Phân quyền theo vai trò **và** theo địa bàn.

**Không có vai trò Người dân.** `Fault` do engine sinh (`cv` / `iot`) hoặc do tổ khảo sát báo tại chỗ (`field_report`). Không có luồng nào để một người "tạo sự cố" rồi hệ thống tin ngay — kỹ sư **duyệt** chứ không tạo.

---

## Quy tắc dễ sai âm thầm

Vi phạm thì hệ thống vẫn chạy, số liệu vẫn ra, nhưng kết quả nghiên cứu vô nghĩa.

- **Từ chối `SurveyFrame` thiếu metadata phơi sáng** (BE-16). Thiếu ISO / shutter / aperture / GPS / heading → từ chối kèm lý do rõ. Không nhận, không suy đoán. Ảnh auto-exposure **không sửa được sau khi chụp**.
- **Ngưỡng phân loại là tỉ lệ so với baseline của chính cột đó**, không bao giờ là giá trị luminance tuyệt đối.
- **Không so sánh độ sáng giữa hai cột khác nhau.**
- **`unknown` đếm riêng, không gộp vào `out`.**
- **Mọi phát hiện tự động ghi kèm phiên bản model và firmware** (BE-34). Không tái lập được thì không phải kết quả.
- **Mọi quyết định của kỹ sư vào `FaultHistory`** (BE-18).
- **Sự cố cấp đoạn là một nguyên nhân, không phải N sự cố bóng.** CV-15 sinh `cluster_id` và `fault_type = segment_outage`.
- **Telemetry ingest idempotent theo `(node_id, reading_time)`** (IOT-09). Store-and-forward chắc chắn gửi trùng — đó là hoạt động bình thường.
- **Phiên đêm cắt qua nửa đêm.** Không tính runtime theo ngày lịch.
- **Node im lặng quá ngưỡng → `Fault` loại `node_offline`.** Im lặng là tín hiệu, không phải khoảng trống.

---

## Stack và database

- .NET modular monolith · ASP.NET Core Web API · `/api/v1`
- PostgreSQL + **PostGIS** + Redis — **không pgvector** (BE-02)
- EF Core + Npgsql + **NetTopologySuite** — map cả `Point` và `LineString` (BE-03)
- MinIO · Hangfire · Serilog · Swagger + JWT security scheme
- Testcontainers cho test tích hợp với PostGIS thật (BE-36)

Yêu cầu Contract mục 5:

1. Tên bảng và cột **`snake_case` toàn chữ thường, không quote**. Postgres fold identifier không quote; mixed case buộc quote ở mọi nơi.
2. Mọi `TIMESTAMPTZ` đọc/ghi bằng `DateTimeKind.Utc`.
3. `geometry(Point,4326)` / `geometry(LineString,4326)` có **GIST index**. Query `bbox` **bắt buộc dùng `ST_Intersects` với index**, không quét bảng — kiểm chứng bằng `EXPLAIN`.
4. Endpoint `bbox` trả **dưới 500ms với 2000 cột**.
5. Enum trả chuỗi thường.

Ngoài ra: một statement lỗi **abort cả transaction** — chặt hơn SQL Server. Không cố tiếp tục sau khi bắt exception trong transaction scope.

---

## Ai đang chờ endpoint của repo này

| Task | Ai chờ | Hạn ngầm |
|---|---|---|
| BE-05 OpenAPI spec | FM-04 sinh DTO Kotlin | W1 |
| BE-07 auth | FM-05 | W1 |
| **BE-42 `LuxReading`** | FM-14, FO-14 (đo lux W5), CV-12 | **W4 — đã đẩy sớm, đừng trễ** |
| BE-13 topology | CV-05 pole association, CV-15 clustering | W4 |
| BE-14 bbox | FW-08, FM-15 | W4 |
| BE-19, BE-20 | FW-12, FW-13, FM-17 | W8–W9 |
| BE-24 evidence | FM-18 | W11 |
| BE-27 Notification | FM-21 | W12 |
| BE-41 `field_report` fault | FM-19 | W13 |
| BE-43 sync | FM-20 offline đầy đủ | W14–W15 |

### Bộ mock FO-26 — đã bàn giao

`mock-poles.geojson`, `mock-pole-detail.json`, `mock-faults.json`, `mock-work-orders.json`, `mock-iot-nodes.geojson`.

Nội dung cố ý cài sẵn: **103 cột** (70 `normal` / 10 `dim` / 16 `out` / 7 `unknown`), một **cụm lỗi cả đoạn trên `SEG-003`**, **12 IoT node**, và **`POLE-0047`** là cột solar có chuỗi runtime suy giảm dần 18 đêm (`dim`, có `NODE-0047` — pin yếu làm đèn mờ dần, không tắt phụt).

FE đang code theo bộ này. **BE-39 phải seed lại đúng bộ mock đó** để demo khớp với những gì FE đã dựng.

---

## Anti-pattern

- Đừng đổi field hoặc enum — Contract đã publish. Lệch thì ghi lại và nêu ở review, không tự sửa.
- Đừng trả EPSG:3405 ra API.
- Đừng trả `GET /faults` dưới dạng GeoJSON.
- Đừng trả enum dạng số.
- Đừng dùng `feature.id`.
- Đừng làm endpoint "lấy tất cả" không có `bbox`.
- Đừng bắt FE gọi nhiều lần để dựng màn chi tiết cột.
- Đừng để FE tự tính `baseline_ratio` hay `classified_as`.
- Đừng gộp `unknown` vào `out`.
- Đừng gộp số liệu giữa ba nguồn dữ liệu của nhánh C.
- Đừng hard-code ngưỡng dim/out hay trọng số ưu tiên.
- Đừng đề xuất node IoT cho mọi cột — sparse IoT là thiết kế, không phải điểm cần tối ưu.
- Đừng thêm luồng người dân gửi phản ánh.
- Đừng giả định có mạng — mobile phải chạy trọn ca offline.
- Đừng âm thầm bỏ frame không hợp lệ.

---

## Quy ước làm việc

- Nhánh Git mang mã task: `feat/BE-09-pole-fixture-entity`, `feat/BE-43-sync-bundle`.
- Mã task: `BE-` backend · `IOT-` telemetry · `CV-` vision/analytics · `FW-` web · `FM-` mobile · `FO-` field ops.
- Mọi API cùng định dạng response, có correlation id (BE-04).
- Test tích hợp chạy trên PostGIS thật, phủ truy vấn không gian và bbox.

## Thứ tự hiện tại

W1: nền tảng **BE-01 → BE-00 → BE-02..BE-07**, cộng **FW-00 review Contract cùng cả nhóm**. BE-01 phải trước BE-00 — chưa có solution thì không áp quy ước vào đâu được.

Sau đó: GIS tài sản (W2–W4) → khảo sát (W5–W7) → sự cố (W7–W9) → quy trình (W9–W12) → dashboard (W13–W15) → quản trị (W15–W17) → hoàn thiện (W17–W21).

**Đã chốt ở mục 0.1–0.4:** ID sinh bằng **sequence PostgreSQL**, format ngay ở tầng DB qua `DEFAULT 'POLE-' || LPAD(nextval(...)::text, 4, '0')`, EF Core map bằng `.HasDefaultValueSql()` + `.ValueGeneratedOnAdd()`. Sequence an toàn concurrency sẵn, không cần bảng counter. Chấp nhận có khoảng trống trong dãy số khi insert lỗi.

**Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID); server gán ID thật khi nhận và trả lại ánh xạ.

Ba task mới v2.0 dễ bị quên vì không có trong kế hoạch cũ: **BE-40**, **BE-41**, **BE-43**. Cả ba đang chặn WP6.
