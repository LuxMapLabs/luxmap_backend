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
- **⚠️ ID là TỐI THIỂU N chữ số, không phải ĐÚNG N.** Contract mục 0.3: vượt ngưỡng thì ID dài ra — cột thứ 10000 là `POLE-10000`, **không phải** `POLE-1000`. Hai hệ quả:
  - **Không bao giờ `ORDER BY pole_id`.** So chuỗi thì `POLE-10000 < POLE-9999`. Sắp theo `created_at` hoặc theo sequence. Lọc theo khoảng trên text cũng sai từ cột thứ 10000.
  - Regex/validator phía FE và mobile phải là `^POLE-\d{4,}$`, **không phải** `\d{4}`.
- **Cách sinh ID — mô tả chuẩn, mọi chỗ khác trỏ về đây.** ID sinh ở tầng DB qua `DEFAULT luxmap_format_id('POLE', nextval('pole_id_seq'), 4)`. Tên sequence theo khuôn `<thing>_id_seq` — `pole_id_seq`, `fixture_id_seq`, `segment_id_seq`… (bảng đầy đủ ở `PrefixedId.cs`).
  - **Không dùng `LPAD(nextval(...)::text, 4, '0')`.** `LPAD` của Postgres **cắt bớt** khi giá trị dài hơn độ rộng: `nextval` = 12345 với width 4 cho ra `'2345'`. Kết quả là ID trùng, sai **câm** — không ràng buộc nào bắt được. Đã lật ở commit `8ea9930`.
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

**1c. `HasQueryFilter` chỉ áp lên ĐỌC. Ghi được canh bởi guard riêng ở `SaveChanges`.**

Đây là chỗ BE-08 từng thủng, và nó thủng **âm thầm**. `HasQueryFilter` chèn `commune_id` vào `WHERE`
của truy vấn — nó không tham gia `Add()`, `Update()`, `Remove()`. Trước hotfix này, một kỹ sư chỉ có
`COM-001` gửi `commune_id: "COM-002"` sẽ **ghi thẳng vào DB**. Khoá ngoại không cứu: nó chứng minh xã
đó **tồn tại**, không nói gì về việc ai được ghi. Rồi chính query filter làm hàng vừa tạo **vô hình với
người tạo ra nó** — không exception, không log, dữ liệu biến mất.

Vì sao lọt lâu: tiêu chí nghiệm thu của BE-08 trong `tasks-backend.csv` ghi *"Kỹ sư bảo trì chỉ
**THẤY** tài sản thuộc địa bàn mình"*. **"Thấy" là đọc.** Cả 8 test scope đều là `GET`,
`ScopeTestController` chỉ có `HttpGet`. Ghi chép lại đây để không ai tưởng BE-08 vốn đã đủ.

**Hai lớp, hai việc khác nhau — cần cả hai:**

| Lớp | Ở đâu | Cho cái gì |
|---|---|---|
| `CommuneFilter.Narrow` | Entry point của controller, gọi tường minh | 403 với thông điệp tử tế, nêu đúng `commune_id` bị từ chối. Người dùng hiểu vì sao. |
| `CommuneWriteGuard` ở `SaveChanges` | `LuxMapDbContext`, tự động | **Backstop không thể quên.** Phủ mọi entity `ICommuneScoped`, kể cả của BE-15/BE-18/BE-21 trước khi chúng được viết. |

Cùng nguyên lý `ValidateCommuneReferences()`: biến quy ước-phải-nhớ thành ràng buộc-không-thể-quên.
Không dùng `ActionFilter` — attribute phải gắn, mà cái bị quên chính là cái rò.

Guard kiểm `Added`, `Modified` (**cả `OriginalValues` LẪN `CurrentValues`** — đổi commune ra ngoài là
cho đi tài sản, đổi từ ngoài vào trong là chiếm tài sản, cùng một lỗi) và `Deleted` (trên
`OriginalValues`).

> 🔴 **ĐIỀU KIỆN PHẢI KIỂM LẠI — không phải ghi chú.**
>
> **Guard `SaveChanges` không thấy cascade do DB thực hiện.** `ChangeTracker` chỉ biết những gì EF
> theo dõi; `ON DELETE CASCADE` chạy trong Postgres, sau khi guard đã xong.
>
> **Hiện an toàn**, vì mọi bảng cascade — `fixture`, `pole_current_status` — đều **cùng commune với
> `pole` cha**. Xoá một pole hợp lệ chỉ kéo theo dữ liệu của chính commune đó.
>
> **Bất kỳ FK cascade MỚI nào giữa hai bảng có thể khác commune đều phá vỡ giả định này.** Thêm một
> cascade như vậy mà không kèm cơ chế canh riêng nghĩa là mở lại đúng lỗ hổng hotfix này vừa bịt —
> lần này qua đường xoá, và không có test nào hiện tại bắt được. Ai thêm `OnDelete(Cascade)` vào một
> quan hệ liên-commune phải xử lý việc đó **trong cùng migration**, không để lại sau.

**Cửa sau: `EnterUnscopedSystemWriteBackdoor()`.**

Tên dài và xấu có chủ đích. Dùng ở `IdentitySeeder` (khi BE-39 seed tài sản) và ở fixture test
(`AssetSchemaFixture.WriteAsSystemAsync`). **Không bao giờ nới nó.**

Đường tắt hấp dẫn nhất là cho **scope rỗng** đi qua, vì seeder và fixture đều có scope rỗng. Nhưng
scope rỗng cũng chính là scope của **caller chưa đăng nhập** và của **token không có `commune_ids`** —
coi nó là quyền tức là mở guard cho đúng những người nó sinh ra để chặn. Muốn bỏ qua thì phải **làm
một hành động**, nhìn thấy được ở call site và trong diff.

Guard ném `LuxMapException` → **403 `COMMUNE_FORBIDDEN`**, ném **TRƯỚC** `base.SaveChanges`: ném từ
trong pipeline của EF sẽ bị bọc thành `DbUpdateException` và middleware BE-04 trả 500 thay vì 403.

Chi phí đo được: **~3,7 µs mỗi entity** (1 entity 6,0 µs · 50 entity 182,6 µs · 500 entity 1,86 ms).
Import CSV của BE-12 với 500 dòng tốn dưới 2 ms — nhỏ hơn nhiều so với chính vòng đi-về DB.

**1b. `AdministrativeUnit` nằm ở `LuxMap.Persistence`, và KHÔNG implement `ICommuneScoped`.**

Nó không phải khái niệm của Identity — nó là **mốc neo phạm vi** cho 15/16 entity. Đặt cạnh chính cơ chế thực thi nó (`ICommuneScoped`, `HasCommuneScope()`, `HasCommuneReference()`, chốt chặn khởi động) thì mọi module khai được FK thật qua tham chiếu `Persistence` vốn đã có, không phải phụ thuộc Identity.

**Không lọc chính bảng neo** — sẽ thành vòng lặp ngữ nghĩa: dòng định nghĩa một xã bị giấu bởi chính phạm vi suy ra từ nó. Endpoint liệt kê commune truy vấn tường minh theo `commune_ids` trong JWT. **Đừng "sửa cho nhất quán".**

Mọi cột `commune_id` khác **bắt buộc** `HasCommuneReference()`. Chốt chặn quét **theo cột**, không theo interface — nên nó bắt được cả entity mang `commune_id` mà quên implement `ICommuneScoped`, đúng lỗ hổng mà XML doc của interface tự thừa nhận. FK khai **không có navigation property**: coupling giữa module giữ ở mức chuỗi ID, không để `pole.Commune.Name` rải khắp nơi. `Restrict`, không bao giờ cascade một đơn vị hành chính.

Vì sao FK này không phải cầu toàn: `commune_id` mồ côi **không gây lỗi** — query filter nằm trong `WHERE` nên dòng đó vô hình với tất cả mọi người. Không exception, không log, dữ liệu biến mất.

**2. Tạo schema và quyền ghi là hai chuyện khác nhau.**

`pole_current_status` do **BE-09 tạo bảng** (BE-14 chạy trước BE-15 và cần 4 trường đó), nhưng
**BE-15/BE-17 sở hữu quyền ghi**. BE-12 (CRUD tài sản + import CSV) **không được đụng vào bảng này**.
Đó là lý do nó là bảng riêng chứ không phải 4 cột trên `pole` — ranh giới nằm trong lược đồ, không
chỉ trong quy ước.

Nợ FK duy nhất của BE-09: `pole_current_status.last_sweep_id` là `text` chưa có FK; BE-15 thêm ràng
buộc trong migration của nó.

### Bốn quy tắc chốt ở BE-10 — khoảng cách và SRID

**1. `SpatialFunctions.DistanceMeters` là đường tính khoảng cách hợp lệ DUY NHẤT.**

Nó dịch sang `ST_Distance(ST_Transform(a,3405), ST_Transform(b,3405))` bằng
`HasDbFunction().HasTranslation()` — **không có function nào trong DB, không có migration nào**.
Bốn API còn lại bị cấm ở **compile-time** qua `BannedSymbols.txt` (RS0030 = error trong
`.editorconfig`): `Geometry.Distance`, `EF.Functions.Distance`, `EF.Functions.IsWithinDistance`,
`EF.Functions.DistanceKnn`. Trên cột 4326 cả bốn trả **ĐỘ**: cặp cột cách 34.973 m ra `0.00032`,
sai **109.290 lần**, mà kết quả vẫn là `double` dương trông hợp lý.

⚠️ **Lệnh cấm có lỗ.** BannedApiAnalyzers chỉ khớp khi **mọi tham số được truyền tường minh**.
`EF.Functions.Distance(a, b, false)` bị bắt; `EF.Functions.Distance(a, b)` — bỏ trống `useSpheroid` —
**lọt hoàn toàn**, kể cả khi cấm nguyên type bằng `T:`. Đúng dạng người ta hay gõ nhất. Đã bịt bằng
`BannedDistanceApiTests` quét văn bản mã nguồn; đừng xoá test đó vì tưởng analyzer đã lo.

**2. Quy ước HAI TẦNG cho BE-13, BE-14, BE-29 — bắt buộc.**

`ST_Transform` trên cột đã đánh index **VÔ HIỆU HOÁ index đó**: index dựng trên `geom` (4326), còn
predicate lại là một hàm của `geom`. Đo thật trên 2500 cột đã `ANALYZE`:

| Truy vấn | Plan | Cost | Buffers | Execution |
|---|---|---|---|---|
| Chỉ `ST_Distance(ST_Transform(...)) < 500` | `Seq Scan on pole` | 62608.25 | 77 | 1.082 ms |
| Thêm `ST_Intersects(geom, envelope)` phía trước | `Bitmap Index Scan on ix_pole_geom` | 86.34 | 5 | 0.029 ms |

**Lọc thô bằng bbox `ST_Intersects` trên 4326 trước** (đi qua GIST index), **rồi mới tinh chỉnh bằng
khoảng cách 3405** trên tập nhỏ còn lại. Đảo thứ tự là mất index — và ở BE-14 thì đó là ngưỡng 500 ms
của Contract mục 5.4.

⚠️ **Predicate khoảng cách 3405 KHÔNG BAO GIỜ được là điều kiện dẫn dắt của một join.**
Plan A ở trên ước lượng `rows=833` — đúng bằng 2500/3, tức PostgreSQL đã rơi về hằng số selectivity
mặc định cho bất đẳng thức: `ST_Distance(ST_Transform(...))` là hàm của cột, nên **không có thống kê
nào áp được**. Thực tế trả về **1** dòng — sai **833 lần**.

Ở một truy vấn đơn lẻ thì vô hại: cost quá cao khiến planner vẫn ưu tiên index nếu có. Nhưng khi
BE-13/BE-14/BE-29 đặt predicate này vào một `JOIN` hoặc subquery, ước lượng sai 833 lần sẽ chọn nhầm
join strategy (nested loop thay vì hash, hoặc ngược lại) — và **không có gì cảnh báo**: truy vấn vẫn
ra đúng kết quả, chỉ chậm dần theo dữ liệu cho tới lúc không ai nhớ vì sao. Luôn thu hẹp bằng bbox
trước để planner có một `rows` thật để làm việc.

**3. Không có API .NET nào trả về `Geometry` đã transform.**

`ST_Transform` chỉ tồn tại **bên trong cây SQL**. Giá trị duy nhất đi ra tầng .NET là `double` mét.
Không phải chuyện phong cách: toạ độ 3405 rò ra API lệch **226 m** trên bản đồ FE — đủ để đặt cột
sang tuyến khác, vẫn đủ nhỏ để trông "gần đúng" và không ai nghi ngờ. Chi tiết số liệu ở XML doc của
`SpatialConstants.SridVn2000`.

**4. `RoadSegment.LengthM` là giá trị KHAI BÁO, không phải giá trị dẫn xuất.**

Đừng "sửa cho đúng" bằng `ST_Length`. Nó là property đã publish ở Contract mục 2.3, và khoảng cách
3405 là khoảng cách trên **mặt phẳng chiếu** — ngắn hơn trên ellipsoid khoảng **73 ppm** do hệ số tỉ
lệ lưới UTM. Lấy `ST_Length` ghi đè sẽ làm số liệu FE nhảy mà không ai giải thích được vì sao.

### Sáu quy tắc chốt ở BE-11 — lưu trữ ảnh

**1. PROXY qua API, KHÔNG BAO GIỜ presigned URL.**

Mọi byte ảnh đi qua endpoint .NET. MinIO bind `127.0.0.1`, không bao giờ phơi ra ngoài.

Lý do không phải hiệu năng mà là **phân quyền**: chuỗi bảo vệ của BE-08 có bốn lớp — claim
`commune_ids` trong JWT có chữ ký → `CommuneScope` (không nhận input client) → `CommuneFilter.Narrow`
(403 khi vượt phạm vi) → `HasQueryFilter` (đưa `commune_id` vào `WHERE`). **Cả bốn đều bám vào một
truy vấn EF.** Presigned URL là chữ ký HMAC do MinIO cấp; MinIO không biết `commune_id` là gì, không
đọc JWT, không có bảng `administrative_unit`. Byte rời MinIO là **không lớp nào chạy**.

Nặng nhất: **thu hồi quyền không hồi tố.** Chuyển kỹ sư sang xã khác thì query filter chặn ngay ở
request kế tiếp; URL đã ký vẫn sống tới lúc hết hạn — dù link đã bị chia sẻ, đã vào log proxy, hay đã
rò qua header `Referer`.

Khớp luôn Contract mục 2.7 (`GET /api/v1/frames/{frame_id}/thumbnail` → JPEG) và
`mock-pole-detail.json` (`"thumbnail_url": "/api/v1/frames/FRM-88213/thumbnail"` — đường dẫn tương
đối, không host, không chữ ký). FE đã dựng theo hình dạng đó.

**2. Hai bucket, key phân tầng, KHÔNG nhúng `commune_id`.**

```
luxmap-survey     original/{frame_id}.jpg      thumb/{frame_id}.jpg
luxmap-evidence   original/{evidence_id}.jpg   thumb/{evidence_id}.jpg
```

`commune_id` nằm ngoài key **có chủ đích**: phân quyền có đúng một nguồn sự thật là cột `commune_id`
với khoá ngoại thật tới `administrative_unit`. Một bản sao trong key là **câu trả lời thứ hai không
ràng buộc** cho cùng câu hỏi, và không có gì phát hiện hai bản lệch nhau — đúng loại drift âm thầm mà
BE-09 đã bỏ công loại trừ. Nó chỉ có lợi khi dùng policy theo prefix ở tầng MinIO, tức chỉ khi chọn
presigned — mà quy tắc 1 đã loại.

⚠️ **KHÔNG BAO GIỜ sắp xếp theo object key.** Trong key là ID có prefix, độ rộng là TỐI THIỂU chứ
không cố định, nên `FRM-100000` đứng trước `FRM-999999` khi so chuỗi. Cùng cái bẫy đã ghi cho
`ORDER BY pole_id` ở mục 0. Object store không phải index.

**3. Ghi OBJECT trước, commit ROW sau.**

Cả hai chiều đều có thể hỏng giữa chừng. Chiều này hỏng thành **object mồ côi** — tốn byte, BE-35 đối
chiếu ra được. Chiều ngược lại hỏng thành **row mồ côi**, tức `thumbnail_url` trả 404 ngay trước mặt
người dùng. Chọn chiều để lỗi rơi vào chỗ máy dọn được, không phải chỗ người nhìn thấy.

**Nợ để lại:** chưa có job đối chiếu object mồ côi. Thuộc **BE-35** (W16), cùng chỗ với báo cáo dung
lượng — mỗi lần ghi đã trả về **số byte thật đã ghi**, không phải `Content-Length` client khai.

**4. Ảnh gốc NGUYÊN BYTE. Thumbnail là object RIÊNG.**

Không re-encode, không strip EXIF, không xoay theo orientation. BE-16 từ chối frame thiếu
ISO/shutter/aperture/GPS/heading, mà ảnh auto-exposure **không đo lại được sau khi chụp**.

⚠️ **ImageSharp KHÔNG tự vứt EXIF khi resize** — kiểm chứng thực nghiệm, không phải giả định: test
đòi thumbnail sạch metadata đã **fail** cho tới khi có dòng `image.Metadata.ExifProfile = null`. Nên
việc bỏ metadata khỏi thumbnail là **tường minh**, đừng xoá tưởng thừa. Quan trọng nhất là GPS:
thumbnail là object được phục vụ rộng nhất, nhét toạ độ chụp vào đó là đặt trường nhạy cảm nhất vào
chỗ ít được bảo vệ nhất.

**5. Chỉ nhận JPEG, quyết bằng MAGIC BYTES.**

`FF D8 FF`. Không tin `Content-Type`, không tin đuôi file — PNG đổi tên `.jpg` bị chặn, JPEG khai sai
header vẫn qua. Lớp phòng thủ thứ hai: ImageSharp chạy trên một `Configuration` **chỉ đăng ký
JpegConfigurationModule**, không phải `Configuration.Default` — PNG dựng sẵn để tấn công không chỉ bị
từ chối bởi chính sách, mà **không có code path nào phân tích được nó**.

**6. Thumbnail sinh ĐỒNG BỘ, 320px cạnh dài, JPEG q80 — con số TẠM.**

Contract chỉ nói "ảnh JPEG", **không quy định kích thước**. 320/q80 là tôi chọn — **phải chốt với FE ở
FW-00**, nếu không hai bên tự quyết khác nhau.

Đồng bộ vì `tasks-backend.csv` đặt "sinh thumbnail" vào tiêu chí của **BE-11**, còn Hangfire (BE-26)
mãi W12 mới có. Cái giá: mỗi upload buffer **một ảnh gốc + thumbnail trong RAM** (~5–15 MB mỗi frame).
Tuần tự thì phẳng; **hàng trăm frame một sweep đến cùng lúc thì không** — batching và giới hạn đồng
thời là việc của **BE-15**, đừng để phát hiện lúc có tải.

**Vị trí code:** `IObjectStore` ở `LuxMap.Shared` (không kèm package nào); adapter + magic bytes +
thumbnail ở `LuxMap.Infrastructure.Storage`; test ở `LuxMap.Infrastructure.Storage.Tests`, **không cần
MinIO và không cần DB**. Không đặt vào `LuxMap.Persistence` (tên đó chỉ về EF/Postgres) và không đặt
package vào `Shared` (sẽ kéo S3 SDK lẫn image codec vào cả hai assembly test đang sạch hạ tầng).

**Bucket do sidecar `minio-mc` tạo**, không phải ứng dụng. .NET chỉ fail-fast trên **cấu hình** thiếu,
theo khuôn `LuxMapConnectionString` và `JwtOptions.Validate` — repo không có tiền lệ chạm dịch vụ
ngoài lúc khởi động, kể cả PostgreSQL. Sidecar bắt buộc `restart: "no"`: mặc định compose sẽ khởi
động lại nó **vô hạn** sau mỗi lần chạy thành công.

**Hai ràng buộc về ImageSharp — kiểm chứng bằng test, không phải giả định:**

- **ImageSharp GIỮ EXIF qua resize** (trái với giả định thông thường). Việc bỏ metadata khỏi
  thumbnail phải **TƯỜNG MINH**: `ExifProfile` / `XmpProfile` / `IptcProfile` = `null`. Không dựa vào
  hành vi mặc định. Lý do là **bảo mật, không phải dung lượng**: thumbnail là object phục vụ rộng
  nhất, GPS chụp là trường nhạy cảm nhất — để nguyên là đặt dữ liệu nhạy nhất vào chỗ ít bảo vệ nhất,
  trong một hệ thống phân quyền theo địa bàn. Canh bằng
  `The_thumbnail_carries_no_gps_so_a_widely_served_object_cannot_leak_capture_locations`; test đó
  **không phải chuyện dọn dẹp metadata**, đừng xoá khi refactor.

- **Ghim ImageSharp ở 3.x.** Từ 4.x, task validate lúc build đòi `SixLaborsLicenseKey`, và
  `ContinueOnError="$(Configuration.StartsWith('Debug'))"` nghĩa là Debug chỉ cảnh báo còn
  **Release/CI/deploy GÃY**. Điều khoản Split License không đổi; chỉ khác cái cổng kiểm key. Muốn lên
  4.x phải **xin key TRƯỚC**. Đã đưa vào FW-00.

**Nợ có tên người đòi:** `S3ObjectStore` **chưa có test tự động** — bộ test BE-11 cố ý không cần
MinIO, nên adapter là mảnh duy nhất không được phủ. Đã kiểm end-to-end thủ công một lần lúc làm
BE-11 (8.2 KiB gốc + 1.8 KiB thumbnail vào `luxmap-survey`, SHA-256 vòng tròn khớp) nhưng **không có
gì canh nó từ đó trở đi**. Chủ nợ là **BE-36** (Testcontainers, W17–W18) — thêm MinIO container bên
cạnh PostGIS. Đừng để nợ này chỉ nằm trong báo cáo một phiên làm việc.

### Bốn quy tắc chốt ở BE-42 — số đo lux

**1. `LuxReading` KHÔNG phải `luminance_history`. Lẫn hai cái là hỏng nghiên cứu.**

| | `LuxReading` (BE-42) | `luminance_history` (BE-15/BE-17 + CV) |
|---|---|---|
| Ai sinh | **Người** cầm máy đo | **CV** xử lý ảnh sweep |
| Thời gian | `measured_at` | `observed_at` |
| Giá trị | `lux_value` — **tuyệt đối, đơn vị lux** | `baseline_ratio` — **tỉ lệ, không đơn vị** |
| Truy vết | `meter_model` | `sweep_id` |
| ID hiển thị | `LUX-0001` | **không có** |

Contract mục 2.9 gọi lux là **ground truth cho RQ1**: CV-12 dùng nó để **chấm** phân loại của CV.
Ghi lux vào chuỗi luminance là để CV tự chấm chính mình, và làm lệch luôn biểu đồ của BE-20.
Chúng gặp nhau **đúng một chỗ**: trường `nearest_luminance` của `GET /lux-readings`, ghép theo
**THỜI GIAN** ±48 giờ (không phải không gian).

**2. `commune_id` do SERVER tra từ `pole_id`, client gửi là 400.**

Guard `SaveChanges` kiểm một commune **có trong scope không** — nó **không** kiểm commune đó có
**khớp với pole** không. Nếu client gửi được `commune_id`, họ có thể gửi commune của chính mình kèm
pole của xã khác: cả hai lớp đều pass, bản ghi vào nhầm xã. Đọc pole trước cũng cho 404 đúng Contract
mục 7 — pole ngoài phạm vi thì query filter làm nó **không tồn tại**, không phải 403.

Lý lẽ denormalize của `Fixture` (*"không bao giờ là resource riêng"*) **KHÔNG áp dụng** ở đây —
`LuxReading` có endpoint riêng. Cột vẫn denormalize (bắt buộc, nếu không thì lọt cả query filter lẫn
guard), nhưng **không emit ra response**.

**3. `nearest_luminance` LUÔN `null` cho tới BE-17 — khác với "không có điểm trong ±48h".**

Bảng `luminance_history` chưa tồn tại. **Khoá vẫn được emit**, không bị bỏ khỏi JSON, để CV-12 bind
theo hình dạng cuối ngay bây giờ. **Nợ có chủ: BE-15/BE-17** nối nguồn thật và xoá mục drift số 17.

**4. FK `lux_reading → pole` là `Restrict`, KHÔNG `Cascade`.**

Lux là sự kiện đã xảy ra và là ground truth RQ1 — xoá pole không được âm thầm xoá dữ liệu nghiên cứu.
Cũng để **không tạo bảng cascade thứ ba**: guard `SaveChanges` không thấy cascade do DB thực hiện
(xem mục 1c). `measured_by` cũng `Restrict` tới `app_user` — xoá được người đo là mất dấu vết.

**Không gắn policy vai trò.** Bốn policy của BE-08 vẫn chưa dùng ở dòng sản xuất nào vì chưa ai quyết
vai trò nào được ghi gì. Đoán một cái ở đây là vô tình tạo tiền lệ. Phạm vi địa bàn **vẫn được canh**
— qua lượt đọc pole và qua guard.

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

**Đã chốt ở mục 0.1–0.4:** ID sinh bằng **sequence PostgreSQL**, format ngay ở tầng DB — biểu thức `DEFAULT` chuẩn nằm ở **mục 0**, không chép lại ở đây. EF Core map bằng `.HasDefaultValueSql()` + `.ValueGeneratedOnAdd()`. Sequence an toàn concurrency sẵn, không cần bảng counter. Chấp nhận có khoảng trống trong dãy số khi insert lỗi.

> ⚠️ **Ghi chép lịch sử:** chỗ này trước đây ghi dạng `LPAD`. Cách đó SAI và đã bị bỏ ở commit `8ea9930` — lý do ở **mục 0**. Đừng khôi phục lại dạng `LPAD`.

**Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID); server gán ID thật khi nhận và trả lại ánh xạ.

**`CREATE FUNCTION` phải luôn đứng TRƯỚC mọi migration dùng nó trong `DEFAULT`.** `luxmap_format_id` được tạo trong `FixPrefixedIdOverflow`, và `DEFAULT` của **6 cột ID** gọi nó. Nếu sau này gộp migration thì thứ tự đó là **ràng buộc cứng**, không phải chi tiết trình bày — đảo thứ tự là DB không dựng được. `Down()` drop function để đối xứng, chép đúng chữ ký trong code: `DROP FUNCTION IF EXISTS luxmap_format_id(text, bigint, int)` — `int`, không phải `integer`.

> **6 chứ không phải 16 — cách phân biệt, để không ai sửa ngược.** 6 là số cột **tồn tại tại thời điểm migration** đó: `segment_id`, `pole_id`, `fixture_id`, `feeder_id`, `user_id`, `commune_id`. `PrefixedIds` khai **16 SPEC**, nhưng 10 spec còn lại thuộc entity chưa được tạo (`Fault`, `SurveyFrame`, `LuxReading`…). Số cột thật sẽ tăng dần khi các entity đó ra đời; con số 16 chỉ đúng cho *bảng prefix*, không đúng cho *migration này*.

Ba task mới v2.0 dễ bị quên vì không có trong kế hoạch cũ: **BE-40**, **BE-41**, **BE-43**. Cả ba đang chặn WP6.
