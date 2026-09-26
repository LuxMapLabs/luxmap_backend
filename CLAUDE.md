# LuxMap — Backend (WP2)

Nền tảng GIS + IoT + Computer Vision quản lý tài sản và sự cố chiếu sáng đường nông thôn.
Tên đề tài (Phiếu đăng ký FA26SE222 v1.2, mục 3.1): **LuxMap: A GIS, IoT and Computer Vision Platform for
Rural Road Lighting Asset and Fault Management** — *LuxMap: Hệ thống bản đồ số GIS tích hợp IoT và thị giác
máy tính để quản lý tài sản và sự cố chiếu sáng đường giao thông nông thôn*. Phiếu (bản markdown:
`docs/registration/FA26SE222_v1.2.md`) là nguồn chuẩn cho actor, chức năng, NFR và deliverable.
Capstone FA26SE222 · W1–W21: 07/09/2026 – 31/01/2027 · Repo này là **WP2, phụ trách: BE1 – Mỹ**.

Backend phục vụ 3 consumer: **Web SPA** (WP5), **Android native** (WP6), **engine CV** (WP4).
Không có consumer nào khác. **Công dân (Citizen) không có tài khoản**: họ báo sự cố qua mã QR trên cột
(chưa có endpoint — D-R1, xem mục "Vai trò").

## Nguồn sự thật

Ba tài liệu, thứ tự ưu tiên khi mâu thuẫn:

1. **`docs/api-contract-v1.1.md`** — bản hợp nhất **v1.7** (25/09/2026; tên file giữ nguyên để liên kết cũ còn đúng). **Thắng mọi thứ khác.** Đã gộp v1.0 → v1.4, toàn bộ drift 1–43 và các quyết định BE-REVIEW-02; bản máy đọc khớp 1-1 là `docs/openapi/luxmap-v1.5.json`. Log drift cũ ở `docs/archive/contract-drift-v1.md`, log mới ở `docs/contract-drift.md`.
2. **`docs/tasks-backend.csv`** — task list v2.1, phạm vi và lịch.
3. File này — quy ước làm việc và những chỗ dễ sai. Không phải đặc tả.

Khi phát hiện mâu thuẫn: làm theo Contract, ghi lại chỗ lệch, nêu ở buổi review — đừng tự ý chọn bên nào.

> **Lệch `manual` / `field_report` đã xử lý:** bản hợp nhất chốt **`field_report`**, khớp task list. Giá trị `manual` của v1.0 đã bỏ.

---

## Ghi phát hiện vào ĐÚNG tầng

Nguyên tắc vận hành quyết định của nhóm nằm ở **đầu `docs/contract-drift.md`** (FW-00, chốt
07/09/2026) — năm mục, nói về *cách quyết* chứ không phải quyết cái gì. Đọc khi cần biết ai ký, im
lặng bao lâu thì tính là gì, và một quyết định treo thì đi đâu.

**Mục 5 là mục áp dụng thường xuyên nhất**, vì nó quyết định file nào nhận phát hiện vừa tìm ra:

| Loại phát hiện | Ghi vào |
|---|---|
| **Deviation** — code lệch Contract, mock lệch Contract | `docs/contract-drift.md` |
| **Luật áp nhiều ticket** — quy tắc chung cho FE/BE/mobile | `docs/api-contract-v1.1.md`, **chỉ sau khi duyệt và tăng version** |
| **Ràng buộc kỹ thuật nội bộ** — bẫy, quy ước, thứ dễ sai âm thầm | **File này** |
| **Tiến độ** — trạng thái ticket, việc tồn đọng | `tracking.html` |

**Ghi sai tầng cũng tệ như không ghi:**

- Một **ràng buộc kỹ thuật** nhét vào `tracking.html` sẽ **trôi mất khi mục đó được đóng** — bài học
  còn nguyên trong repo này: quy tắc `LPAD` cắt ID và quy tắc partial index đều phải nằm ở đây mới
  còn tác dụng ở ticket sau.
- Một **deviation** nhét vào `CLAUDE.md` **không bao giờ tới tay WP5/WP6** — họ không đọc file này.
- Một **quyết định nội bộ** đẩy thẳng vào Contract là đổi thứ FE đã code theo mà chưa ai duyệt.

⚠️ Ba mục còn lại đáng nhớ khi làm việc một mình: quyết định **không ghi vào repo thì coi như chưa
xảy ra**; im lặng quá 3 ngày làm việc là **approve với thay đổi không chạm bề mặt API** nhưng
**ESCALATE với thay đổi có chạm** — không bao giờ là approve; và một quyết định `SELF-SIGNED` chạm bề
mặt API **chưa ổn định** cho tới khi FW kế tiếp xác nhận, nên ticket xây lên trên nó **phải ghi rõ nền
là tạm**.

---

## Giao thức `.ai/` — làm việc nhiều agent

Repo này có thể được nhiều agent cùng phục vụ (Claude Code, Codex). Vùng bàn giao giữa chúng là
`.ai/`, và **`AGENTS.md` là symlink tới chính file này** — Codex đọc `AGENTS.md` theo quy ước, nên
một file duy nhất phục vụ cả hai, và symlink luôn phân giải theo branch đang đứng. Đừng tạo lại
`AGENTS.md` thành file thường: bản `AGENTS.md` cũ (untracked, 17/09/2026) là bản chép đông cứng của
`CLAUDE.md` trên `dev`, nên trên nhánh này nó thiếu trọn 74 dòng, vẫn ghi Contract v1.1, và không có
tám ràng buộc BE-REVIEW-02 lẫn luật `[AllowAnonymous]` cấp CLASS.

Trước khi bắt đầu một ticket, đọc theo thứ tự:

1. `.ai/context/sources.md` — tra nhanh file nào trả lời câu hỏi gì
2. `.ai/tasks/<ticket>.md` — phạm vi, tiêu chí xong, và mục **KHÔNG ĐƯỢC làm**
3. `.ai/context/commands.md` — lệnh build/test/migrate (bản đầy đủ: `README.md`)

Ghi kết quả vào `.ai/results/<ticket>.md`, review vào `.ai/reviews/<ticket>-by-<agent>.md`. Quy ước
đầy đủ, vòng đời ticket và quy tắc hai pha: `.ai/README.md`.

⚠️ `.ai/` **đứng cuối** thứ tự ưu tiên — sau Contract, sau `tasks-backend.csv`, sau file này. Và nó
**không nhận** bốn loại phát hiện ở bảng trên: deviation vẫn về `contract-drift.md`, ràng buộc kỹ
thuật vẫn về đây, tiến độ vẫn về `tracking.html`. Một deviation ghi trong `.ai/results/` là deviation
**không bao giờ tới tay WP5/WP6**.

---

## Phạm vi — theo Phiếu đăng ký v1.2 (D-R10, 25/09/2026)

Phiếu v1.2 **thay FO-01** (24/08/2026, "không có thử nghiệm hiện trường" — nay `SUPERSEDED`, ghi ở
`docs/contract-drift.md`). Phạm vi thực tế của nhóm:

| Ở đâu | Làm gì | `data_source` |
|---|---|---|
| **Ngoài thực địa** (xã đối tác) | Quay **video đêm** đèn đường thật; đo sáng **tương đối bằng điện thoại**; kiểm tra trạng thái đèn **bằng mắt** ban đêm (ground truth lớp `out`). **Không** lắp thiết bị, **không** thao tác lưới chiếu sáng của xã | `field` |
| **Mô hình testbed tự dựng** (đèn LED) | Toàn bộ IoT (trạng thái nguồn, dòng điện, thời gian vận hành); demo **điều khiển cưỡng chế ON/OFF/AUTO**; **Controlled Reference Capture Set** — mức sáng biết trước (trước đây gọi là "bộ hiệu chuẩn tự dựng", FO-07) | `calibration_rig` (telemetry testbed: Contract O-9) |
| **Khởi động AI** | Ảnh đêm công khai + controlled reference, trong lúc chờ quay thực địa (phiếu mục 4) | `public_imagery` |
| Telemetry mô phỏng | Runtime khi chưa có thiết bị (FO-25) | `simulated` |

🔴 **Dữ liệu testbed không bao giờ mang `field`.** Gộp thí nghiệm có kiểm soát vào số liệu hiện trường là
đúng loại lỗi luật tách `data_source` sinh ra để chặn.

⚠️ **Ground truth không còn một nguồn.** Trước đây bộ hiệu chuẩn là "ground truth photometric duy nhất";
nay có video thực địa + số đo tương đối bằng điện thoại + kiểm tra bằng mắt cho lớp `out` + controlled
reference trên testbed. Bốn quyết định từng dựa trên FO-01 **chưa sửa code**, mỗi cái là một follow-up
"nền đã đổi, cần xét lại" trong `tracking.html`: `external_ref` là danh tính vĩnh viễn (**ưu tiên cao**),
`administrative_unit` không geometry, `data_source = field` (nay sẽ có bản ghi), và ground truth.

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
    - ⚠️ **`created_at` MỘT MÌNH KHÔNG ĐỦ, và chỗ hở nằm ở tiebreaker.** `now()` của Postgres là
      thời điểm **bắt đầu transaction**, nên **cả mẻ nạp dùng chung đúng một giá trị** — đo thật:
      cả 103 cột mock có cùng một `created_at`. Vậy `created_at` không sắp xếp gì bên trong một mẻ,
      và **tiebreaker mới là thứ quyết định thứ tự thật**. Để `ThenBy(pole_id)` trần là mở lại đúng
      cái bug dòng trên vừa cấm.
    - **Khuôn đúng: `ORDER BY created_at, length(id), id`.** Sắp theo độ dài trước thì ID ngắn đứng
      trước, khôi phục thứ tự số trong cùng một prefix. Áp ở `ListAsync` (BE-12a) và
      `TopologyPageAsync` (BE-13); listing mới **phải** theo khuôn này.
    - 🔴 **Test so TẬP không bắt được lỗi này.** `Assert.Equal` trên hai collection đã sắp vẫn xanh
      khi thứ tự sai. Phải assert **có thứ tự**, trên ID nằm hai bên ngưỡng độ rộng, và **ghi trong
      cùng một transaction** để chúng trùng `created_at`. Canh bằng `PrefixedIdOrderingTests`.
  - Regex/validator phía FE và mobile phải là `^POLE-[0-9]{4,}$`, **không phải** `[0-9]{4}` — và `[0-9]`, không `\d` (`\d` khớp chữ số Unicode).
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
power_source   : grid                            # v1.6 — solar đã bỏ
fixture_type   : led_road_lamp                   # v1.6 — solar_all_in_one đã bỏ
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

> 🔴 **`power_source` và `fixture_type` THU HẸP còn một giá trị — Contract v1.6, 22/09/2026.** Đèn
> năng lượng mặt trời ra khỏi phạm vi đồ án. Giá trị bị **xoá khỏi enum**, không để lại không dùng:
> một giá trị không gì sinh ra được là giá trị ticket sau tưởng mình được phép ghi, và CHECK ở DB
> sẽ cho qua. Migration `DropSolarFixtures` **đổi 45 hàng RỒI mới siết CHECK** — đảo thứ tự là
> migration gãy trên chính dữ liệu nó sắp bảo vệ, và `Down()` khôi phục được ràng buộc nhưng
> **không khôi phục được dữ liệu** (đổi solar→grid là mất thông tin).
>
> ⚠️ **Phần PIN không đi theo.** IoT vẫn đo runtime, `runtime_decline` vẫn là `fault_type` hợp lệ —
> giờ sáng của đèn lưới cũng suy giảm được. `POLE-0047` giữ chuỗi 18 đêm, chỉ đổi sang `grid`.
>
> Hệ quả đã áp: BE-13 **bỏ `power_source`** khỏi listing cột-chưa-gán (trường đó chỉ sinh ra để tách
> *"solar nên không mạch"* khỏi *"chưa ai gán"*, mà vế đầu nay không tồn tại); O-6 từ **58/103 dòng
> cần điền thành 103/103**, vì mọi cột nay đều chạy điện lưới.

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
| ~~BE-07~~ | ~~Endpoint đăng ký / đăng nhập / refresh~~ → **đã đặc tả ở Contract mục 4 (v1.2)** |
| ~~BE-12~~ | ~~CRUD tài sản + import CSV~~ → **BE-12a đã đặc tả và hiện thực**; hình dạng response khi ĐỌC là **BE-12b**, còn chờ duyệt |
| BE-15, BE-16 | Upload sweep, validate metadata phơi sáng |
| BE-27 | Notification — **chốt tên bảng/entity cùng FE2 trước W16** |
| BE-28→31 | Toàn bộ dashboard và thống kê |
| BE-33→35 | Quản trị danh mục, node, model version |
| ~~BE-41~~ | ~~`POST /faults`~~ → **đã đặc tả ở mục 2.8** |
| ~~BE-42~~ | ~~endpoint lux~~ → **đã đặc tả ở mục 2.9** |

BE-41 và BE-42 đã được đặc tả ở bản hợp nhất (mục 2.8 và 2.9). **BE-12a** được đặc tả riêng ngoài
Contract và đã hiện thực — nhóm endpoint `/assets/…`, xem mục BE-12a bên dưới; **BE-12b** (hình dạng
response khi đọc một tài sản) vẫn đang chờ Thịnh/Ngọc duyệt. Phần còn lại trong bảng vẫn chưa có đặc
tả — cần thống nhất trước khi hiện thực, đừng tự sinh endpoint rồi coi như xong.

Còn để mở: vector tile khi vượt ~5000 cột, realtime khi sweep xong (giai đoạn 1 dùng polling). Phân quyền theo `commune_id` **đã chốt ở mục 7**, không còn để mở.

**Lưu trữ đã chốt (D-R16, Contract §3.3):** ảnh, telemetry, fault giữ **tối thiểu qua hết bảo hành** của
tài sản; hết bảo hành **không** xoá / archive gì — chỉ gắn **cảnh báo "hết bảo hành"**, tính từ
`fixture.warranty_expiry` của bóng đang dùng so với hôm nay, **không lưu thành cột trạng thái** (hiện ở bản
đồ, danh sách tài sản, fault / work order). ⚠️ Ngày bảo hành nằm trên **`fixture`**, không trên `pole`, và
**nullable** — "không biết hạn bảo hành" phải hiện khác "còn bảo hành". Hiện thực: BE-31 / BE-35.

---

## Domain model

`Pole` · `Fixture` · `RoadSegment` · `Feeder` · `IotNode` · `TelemetryReading` ·
`SurveySweep` · `SurveyFrame` · `Detection` · `LuminanceBaseline` · `LuxReading` ·
`Fault` · `FaultCluster` · `WorkOrder` · `ExternalUnit` · `RepairEvidence` ·
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
| `CommuneWriteGuard` ở `SaveChanges` | `LuxMapDbContext`, tự động | **Backstop không thể quên.** Phủ mọi entity `ICommuneScoped`, kể cả của BE-15/BE-18/BE-21 trước khi chúng được viết — **trừ một ca, xem ngay dưới**. |

> ⚠️ **Một ngoại lệ đã biết, phát sinh từ O-7: sửa `commune_id` của chính `Feeder` đã được track.**
> `HasAlternateKey` biến `Feeder.CommuneId` thành **key property**, mà EF Core cấm sửa key trên entity
> đang track — nó ném `InvalidOperationException` ngay trong `DetectChanges`, **trước khi guard chạy**,
> nên ra **500** chứ không phải 403 `COMMUNE_FORBIDDEN`.
>
> **Dữ liệu không hề gặp rủi ro** — cả hai lớp đều TỪ CHỐI, chỉ khác lớp nào từ chối và từ chối có dễ
> đọc không. Ghi lại vì câu "phủ mọi entity" ở trên là câu mà ticket sau sẽ dựa vào. Ghim bằng
> `Changing_a_tracked_feeders_own_commune_is_refused_by_EF_before_the_guard_sees_it`.

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

Tên dài và xấu có chủ đích. Hiện chỉ dùng ở fixture test (`AssetSchemaFixture.WriteAsSystemAsync`,
`AssetImportFixture`, `PoleWriteTests`…); `IdentitySeeder` chưa cần vì nó chỉ ghi entity không
`ICommuneScoped`. BE-39 seed tài sản sẽ là caller thật đầu tiên trong `src/`. **Không bao giờ nới nó.**

Đường tắt hấp dẫn nhất là cho **scope rỗng** đi qua, vì seeder và fixture đều có scope rỗng. Nhưng
scope rỗng cũng chính là scope của **caller chưa đăng nhập** và của **token không có `commune_ids`** —
coi nó là quyền tức là mở guard cho đúng những người nó sinh ra để chặn. Muốn bỏ qua thì phải **làm
một hành động**, nhìn thấy được ở call site và trong diff.

Guard ném `LuxMapException` → **403 `COMMUNE_FORBIDDEN`**, ném **TRƯỚC** `base.SaveChanges`: ném từ
trong pipeline của EF sẽ bị bọc thành `DbUpdateException` và middleware BE-04 trả 500 thay vì 403.

> ⚠️ **Con số ~3,7 µs/entity ghi ở đây trước kia ĐO NHẦM ĐỐI TƯỢNG.** Nó là lượt
> `ChangeTracker.Entries<T>()` **ấm** — không phải việc kiểm phạm vi (0,21 µs), cũng không phải chi
> phí thật của guard. Phép đo **A/B** ở BE-12a cho kết luận là **không đo nổi**: chi phí guard nhỏ
> hơn nhiễu của chính phép ghi. Xem mục "Chi phí `CommuneWriteGuard`". **Đừng trích lại 3,7 µs — và
> cũng đừng trích con số nào khác** như thể nó là giá của guard.

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
`mock-pole-detail.json` (`"thumbnail_url": "/api/v1/frames/FRM-088213/thumbnail"` — đường dẫn tương
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

⚠️ **Video là PLANNED, không phải được phép (D-R14).** Phiếu v1.2 cho Kỹ sư hiện trường quay video (khảo
sát và thiết bị), nhưng cho tới ticket video quy tắc này **giữ nguyên: chỉ JPEG**. Hướng đề xuất cho
ticket đó là upload thẳng MinIO bằng presigned URL — **ngược quy tắc 1** ở trên, nên ticket video phải tự
trả lời chuyện phân quyền theo địa bàn cho byte video, không được coi là đã giải.

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

**Image MinIO kéo từ `quay.io`, không phải Docker Hub — pin bằng INDEX digest.**

Docker Hub đã gỡ hẳn `minio/minio` và `minio/mc`: API repository trả **404** chứ không phải 401, nên
`docker login` vô ích. Registry riêng của MinIO trên quay.io còn phục vụ đúng hai tag đang pin, cùng
bytes — digest manifest list ở quay trùng khít digest của image Docker Hub còn trong cache.

⚠️ **Digest phải là digest của MANIFEST LIST, không phải per-platform.** Pin nhầm digest arm64 sẽ làm
**mọi máy Windows/x86 của nhóm không kéo nổi image**, và lỗi chỉ lộ trên máy người khác chứ không bao
giờ trên máy vừa sửa. Lấy bằng `docker buildx imagetools inspect <ref>`, đọc dòng `Digest:` cấp cao
nhất — `docker manifest inspect` **không** in số đó.

Dòng release OSS đứng yên ở `RELEASE.2025-09-07` ⇒ đây là **hoãn, không phải giải**. **BE-36**
(Testcontainers, W17–W18) thừa hưởng ràng buộc này: container MinIO nó thêm phải trỏ `quay.io`.

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
| Ai sinh | **Người** đo bằng **điện thoại** | **CV** xử lý ảnh sweep |
| Thời gian | `measured_at` | `observed_at` |
| Giá trị | `lux_value` — **số đọc TƯƠNG ĐỐI** của cảm biến ánh sáng điện thoại | `baseline_ratio` — **tỉ lệ, không đơn vị** |
| Truy vết | `meter_model` — **model điện thoại** | `sweep_id` |
| ID hiển thị | `LUX-0001` | **không có** |

⚠️ **`lux_value` là TƯƠNG ĐỐI (D-R15, Contract §5.7).** Đo theo quy trình cố định — cùng máy, cùng app,
cùng tư thế — nên sai số hệ thống của cảm biến là chung cho mọi lần đo và triệt tiêu khi so tương đối.
Chỉ dùng để so giữa các cột và giữa các đêm; **không bao giờ** là lux tuyệt đối, **không** dùng để đánh
giá đạt/không đạt chuẩn chiếu sáng. Ngưỡng cảnh báo 200 là kiểm tra hợp lệ dữ liệu thô, không mang nghĩa
trắc quang. Tên cột **giữ nguyên**.

Contract §5.7 gọi lux là **một trong các nguồn ground truth cho RQ1**: CV-12 dùng nó để **chấm** phân loại của CV.
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

**Phân quyền (Contract v1.7):** `POST` = `RecordLuxReading` (**chỉ Kỹ sư hiện trường**), `GET` =
`ReadLuxReadings` (cả bốn vai trò). Tới v1.6 `POST` **không gắn policy nào**, nên mọi vai trò đã đăng nhập
đều ghi được — kể cả Cấp giám sát chỉ-đọc. Phạm vi địa bàn **vẫn được canh riêng** — qua lượt đọc pole
và qua guard.

### Sáu quy tắc chốt ở BE-12a — nhập và sửa tài sản

**1. Nhóm endpoint là `/api/v1/assets/…`, KHÔNG phải `/poles`.**

Contract mục 2.1 đã đặc tả `GET /poles`: `bbox` bắt buộc, trả `FeatureCollection`, quá 2000 cột →
413. Đó là **endpoint bản đồ của BE-14**. Một danh sách kiểm kê trả lời cùng đường dẫn sẽ chiếm mất
chỗ đó. Hai bề mặt, hai việc khác nhau — đừng gộp.

```
GET/POST      /api/v1/assets/{segments|feeders|poles}    POST /api/v1/assets/fixtures
PUT/DELETE    /api/v1/assets/{segments|feeders|poles}/{id}
PUT           /api/v1/assets/poles/{id}/feeder           PUT  /api/v1/assets/fixtures/{id}/removal
POST          /api/v1/assets/import/{segments|feeders|poles|fixtures}
```

**Fixture KHÔNG có DELETE, và cũng không có PUT thay thế** — ngừng dùng thiết bị là việc của
`fixture.removed_date`, vì đó là sự kiện có thật, còn một dòng gõ nhầm thì không. Ba loại còn lại xoá
được và **khoá ngoại quyết định**, không kiểm trong code: `fault` / `lux_reading` giữ pole,
`pole` / `fault` / `fault_cluster` giữ segment, `pole.feeder_id` giữ feeder — tất cả `Restrict`, vi
phạm là 409 `ASSET_IN_USE` kèm tên constraint trong `details` (kể cả khi ràng buộc vấp ở bóng của
cột). Drift 43 (18/09/2026) mở đường cho pole; **drift 44 (20/09/2026)** cho năm endpoint còn lại.

> ⚠️ **`PUT` ở đây là THAY THẾ TOÀN PHẦN, không phải patch — và nó cắn.** Body thiếu `feeder_id` sẽ
> **XOÁ mạch điện của cột**, âm thầm. Ai chỉ muốn đổi mạch thì dùng `PUT /assets/poles/{id}/feeder`,
> endpoint đó phân biệt được "không gửi" với "gửi null" (`SetPoleFeederRequest`). Đã ghim bằng
> `Replacing_a_pole_without_a_feeder_id_clears_its_circuit` — test đó **ghi lại một quyết định**,
> đừng "sửa" cho field dính lại.
>
> **`commune_id` KHÔNG nằm trong ba request update.** Chuyển tài sản sang xã khác không phải sửa mà
> là chuyển giao: đổi luôn ai nhìn thấy dòng đó, và phải kiểm scope ở **cả hai** phía. Riêng với
> `feeder` thì còn nặng hơn — `RequireFeederInCommuneAsync` chỉ chạy khi ghi **POLE**, không bao giờ
> chạy khi feeder đổi xã, nên cho sửa sẽ kéo tủ điện ra khỏi các cột đang đấu vào nó và **mọi cặp đó
> lặng lẽ thành liên-xã mà không còn lượt ghi nào bắt được**.

**2. `external_ref` — khoá tự nhiên DUY NHẤT của lược đồ, trên BA bảng.**

`road_segment`, `feeder`, `pole`. `text NULL` trong DB, **BẮT BUỘC trong file import**, unique là
**partial index** `(commune_id, external_ref) WHERE external_ref IS NOT NULL`. **KHÔNG emit ra API.**

`fixture` **không có** — một cột mang nhiều bóng qua thời gian nên không mã nào chỉ đúng một lần lắp
đặt. Vì vậy **nhập bóng là INSERT-ONLY**: cột đã có bóng → lỗi theo dòng. Upsert bừa ở đây sẽ nhân
đôi lịch sử thiết bị mà không ai phát hiện.

Template tham chiếu bằng `segment_external_ref` / `feeder_external_ref` / `pole_external_ref`, **không
phải `SEG-001` / `FDR-001`** — mã đó do DB sinh lúc INSERT, người soạn file không biết trước. Trước
BE-12a bộ bốn file không nạp được liền mạch: phải nạp tuyến, mở DB tra mã, rồi mới điền vào file cột.

Xem quy tắc partial index ngay dưới đây — nó **không** phải chuyện riêng của BE-12a.

**3. Ngữ nghĩa nhập: kiểm TOÀN BỘ trước, ghi tập hợp lệ trong MỘT transaction, trả 200.**

Dòng vi phạm FK / CHECK / enum / phạm vi xã **phải bị bắt ở bước kiểm**, không được lọt xuống bước
ghi. Lỗi ở bước ghi **rollback cả mẻ, trả 500**, không đổ lỗi cho dòng nào.

> 🔴 **HẠN CHẾ ĐÃ BIẾT — không phải "thiết kế". Upsert KHÔNG nguyên tử.**
>
> Bước đọc-trước và bước ghi là hai câu lệnh riêng. **Hai request nạp cùng một file đồng thời**: cả
> hai đọc không thấy gì, cả hai `Add`, và cái thua đụng `ux_*_commune_external_ref` ngay trong
> `SaveChanges`. Đó **đúng là** một lỗi mức-dòng lọt xuống bước ghi, và nó nổi lên dưới dạng
> `DbUpdateException` thô → 500, **không phải** kết quả validate có số dòng.
>
> **Chưa sửa ở BE-12a** — một transaction, thua thì không ghi gì, dữ liệu không hỏng, và hai người
> quản lý nạp cùng file trong cùng một giây chưa đáng thiết kế riêng. Ghi lại vì **BE-12b kế thừa đúng
> đường ghi này**, và vì bản sửa đúng là **upsert thật (`ON CONFLICT DO UPDATE`)**, không phải thêm
> một lượt kiểm nữa — thêm kiểm chỉ thu hẹp cửa sổ chứ không đóng được.

Kết quả: `{inserted, updated, failed, total_errors, truncated, rows[]}`.

- **200 chứ không phải 4xx**, vì các dòng hợp lệ đã ghi thật; bọc trong `{error:…}` là nói sai. Tiền
  lệ: `POST /lux-readings` trùng `client_op_id` trả 200 (Contract mục 5.8). **Không dùng 207** — nó
  không có trong Contract và không có trong repo.
- **`rows[]` là MẢNG, không phải dictionary khoá-là-số-dòng.** Dictionary không cam kết thứ tự, và
  khoá số dạng chuỗi thì `"10"` đứng trước `"9"` — danh sách người ta đọc sẽ nhảy lung tung.
- Cắt ở **100** phần tử, `total_errors` vẫn là số thật. Một file sai delimiter làm hỏng mọi dòng;
  không cắt thì body trả về lớn hơn cả file gửi lên.

**Mỗi request nạp ĐÚNG MỘT loại file.** Đó là thứ làm cho tham chiếu an toàn: tuyến của một cột đã
được commit ở request trước nên đã có `SEG-001` thật. Nạp cả bốn loại trong một transaction sẽ phải
phân giải tham chiếu tới hàng chưa có ID. Thứ tự tự thực thi: nạp cột trước tuyến thì **mọi** dòng
báo `segment_external_ref` không khớp gì cả.

**4. Phân quyền — capability, mỗi capability là một DANH SÁCH vai trò chính xác, KHÔNG phải một bậc.**

> Từ Contract v1.7 (D-R5, 25/09/2026). Tới v1.6 mỗi policy là **một** vai trò và GET để trần; ghi
> tài sản là của Quản trị. Nay:

| | |
|---|---|
| POST / PUT / DELETE / import | `[Authorize(Policy = LuxMapPolicies.ManageAssets)]` — **chỉ Quản lý** |
| GET | `[Authorize(Policy = LuxMapPolicies.ReadNetwork)]` — cả bốn vai trò |

Ma trận ở **`LuxMapPolicies.Matrix`**, nguồn DUY NHẤT; `AuthorizationSetup` đăng ký policy bằng vòng lặp
trên nó, `RequireClaim(role, <danh sách>)` — OR giữa các giá trị, **không** thứ bậc. Vai trò được vào vì
được **nêu tên**. Quản trị hệ thống **không** ghi tài sản nữa (D-R12): nó đọc qua `*`, nhưng `*` làm
guard `SaveChanges` cho nó qua hết — nên **chỉ policy** đứng giữa nó và bảng tài sản.

**Không endpoint nghiệp vụ nào được dựa vào fallback** (fallback = "đã đăng nhập" = cả bốn vai trò, tức
mở cho Cấp giám sát chỉ-đọc trên cả endpoint ghi). Ngoại lệ có tên: `GET /auth/me`.
`CapabilityPolicyCoverageTests` đỏ khi endpoint thiếu capability, khi endpoint nêu policy ngoài ma trận,
hoặc khi policy đăng ký nhận vai trò ma trận không nêu. Capability chưa có endpoint (`ControlLighting`,
`ManageUsers`) khai sẵn; capability khác (survey review, work order, fault) thêm **cùng ticket** của nó.

> 🔴 **Capability RỖNG là capability MỞ, không phải đóng.** `RequireClaim(role)` không kèm giá trị nào
> chỉ đòi claim `role` **tồn tại** — tức nhận **mọi** vai trò. Phát hiện bằng sabotage: bỏ vai trò duy
> nhất của `ManageAssets` là Cấp giám sát tạo được tuyến. `AuthorizationSetup` nay **từ chối khởi động**
> khi một capability rỗng; muốn bỏ capability thì **xoá** nó, đừng làm rỗng.

> 🔴 **Test kỳ vọng phải là LITERAL, không đọc từ ma trận.** Test suy kỳ vọng từ `Matrix` sẽ đồng ý với
> mọi ma trận, kể cả ma trận sai. `RoleCapabilityMatrixTests` (HTTP, 4 vai trò × 6 capability) và
> `CapabilityMatrixTests` (Shared, không cần Docker) chép bảng của Contract; đổi quyền là sửa cả hai
> trong cùng diff.

`LuxMapPolicies` nằm ở **`LuxMap.Shared`** từ BE-12a — chuyển từ
`LuxMap.Api` sang **`LuxMap.Shared`**: host tham chiếu module chứ không ngược lại, nên controller
trong module không thấy được hằng khai ở host.

> ⚠️ **Đây là quyết định KIẾN TRÚC, chưa được chốt ở cấp nhóm — đang chờ FW-00.**
> Hệ quả: khái niệm authorization rò vào `LuxMap.Shared`, mà `LuxMap.Persistence` và các assembly
> không-API cũng tham chiếu. Chấp nhận được **nếu** coi `Shared` là nơi chứa hằng liên-tầng — đúng
> vai trò nó đang giữ cho `ErrorCodes` và `PrefixedIds`. Nếu không chấp nhận thì đường đúng là **mỗi
> module tự khai tên policy của mình, host đăng ký khớp** — đổi được sau, chỉ là đổi ở nhiều chỗ
> hơn. Chốt ở FW-00 rồi hãy sửa; đừng để một ticket import quyết thay.

Ma trận vai trò nay nằm ở Contract **§2** (v1.7); drift 31 và D-14 là lịch sử.

**5. `CommuneFilter.Narrow` giờ có call site thật.**

Trước BE-12a nó không được gọi ở đâu trong `src/`. Nay gọi ở hai chỗ: tham số `?commune_id=` của
danh sách, và `commune_id` trong body khi tạo tài sản. Nó trả **403 nêu đúng xã bị từ chối**; query
filter một mình chỉ cho ra 200 rỗng, người dùng không hiểu vì sao.

⚠️ **Guard `SaveChanges` KHÔNG bao giờ nổ trên đường HTTP của BE-12a**, vì mọi lối ghi đã kiểm phạm
vi ở entry point trước. Đó là phân tầng đúng — nhưng nghĩa là **không thể** viết test HTTP cho guard
ở ticket này; test guard đi thẳng qua `DbContext`
(`The_write_guard_still_refuses_a_pole_for_a_foreign_commune_even_with_no_entry_point_check`).

**Trong import thì xã ngoài phạm vi là lỗi THEO DÒNG, không phải 403 cả request** — file là một mẻ,
một dòng sai không được vứt 499 dòng còn lại.

**6. `commune_id` lấy từ đâu — khác nhau theo bảng, và có lý do.**

| Bảng | Nguồn |
|---|---|
| `road_segment`, `feeder`, `pole` | **BODY / file**, kiểm bằng `Narrow` |
| `fixture` | **Chép từ pole.** Client gửi cũng không đọc |

`pole` **không** suy được từ segment: `road_class = inter_commune` nghĩa là đường chạy **giữa** các
xã, nên cột của nó nằm ở xã khác với xã sở hữu tuyến là chuyện hợp lệ. Còn `fixture` thì luôn ở
đúng xã của cột mang nó — cho file khai sẽ để hai giá trị lệch nhau mà không có gì phát hiện.

**Parser CSV tự viết**, ở `LuxMap.Shared/Csv/` (144 dòng code, không thêm package). Xử lý BOM UTF-8,
CRLF, ô bọc nháy kép chứa dấu phẩy, và tự dò delimiter `,` / `;` bằng cách **chỉ đếm ký tự ngoài
nháy** — hai dấu phẩy trong `LINESTRING(...)` không được phép thắng phiếu. Đặt ở Shared vì nó không
kéo theo dependency nào, nên test chạy trong assembly **không cần DB và không cần Docker**.

**GeoJSON parse tay bằng `System.Text.Json`** cho `Point` và `LineString`: NTS lõi **không có
GeoJSON reader** (namespace `IO` chỉ có GML2, GML3, KML), và thêm `NetTopologySuite.IO.GeoJSON4STJ`
là thêm package.

> ⚠️ **`ImportGeoJsonAsync` phải là `async`, không được trả thẳng inner Task.** `JsonDocument` giữ
> bộ nhớ pooled mà mọi `JsonElement` trỏ vào; trả Task từ hàm không-async sẽ dispose document **trước
> khi** import đọc xong hàng. Đã gặp thật: 500 `INTERNAL_ERROR` khi nạp bộ mock.

> ⚠️ **`WKTReader` trả SRID 0, không phải 4326.** WKT không mang hệ toạ độ nên reader không có gì để
> đọc. Phải gán SRID **tường minh** trên mọi geometry rời `AssetGeometry`.

**Giới hạn upload 10 MB**, nhận qua `IFormFile`. Mặc định của framework không phải thứ ai cũng nhớ:
Kestrel `MaxRequestBodySize` = **30.000.000 byte (~28,6 MB)**, **thấp hơn** con số 128 MB của
`FormOptions.MultipartBodyLengthLimit` mà người ta hay trích; và form **value** bị chặn ở 4 MB, nên
gửi GeoJSON dưới dạng field sẽ vỡ ở một ngưỡng chẳng liên quan. Repo trước đó chưa cấu hình cái nào.

### Chi phí `CommuneWriteGuard` — đo A/B bắt cặp, KHÔNG kết luận được con số

Con số **~3,7 µs/entity** ghi ở mục BE-09 **đo nhầm đối tượng**: nó là lượt `Entries<T>()` ấm, không
phải việc kiểm phạm vi. Nhưng cộng các phần lại cũng sai nốt, theo chiều ngược: `Entries<T>()` gọi
`DetectChanges`, mà `SaveChanges` **đằng nào cũng gọi** — nên guard có thể chỉ **dời** lượt quét đó
sớm lên chứ không thêm.

**Chi phí biên của guard nằm dưới ngưỡng phân giải của phép đo.** Đo A/B bắt cặp trên 1000 entity
(`SaveChanges` có guard vs không, D1 = `EnterUnscopedSystemWriteBackdoor`), 30 cặp sau 8 cặp
warm-up: **trung vị delta ~8 µs/entity**, nhưng **IQR (14,3 ms) rộng hơn trung vị (8,5 ms)** và chỉ
**20/30 cặp dương**, không đạt ngưỡng 27/30. **Kết luận dùng được: chi phí guard nhỏ hơn nhiễu của
chính phép ghi nó bảo vệ** (~8 ms trên nền ~55 ms cho 1000 dòng). **Không có con số điểm nào đáng
trích.**

Ba số 9,69 / 3,69 / 0,21 là **phân rã bên trong guard đo trong bộ nhớ**, KHÔNG phải chi phí biên —
**đừng cộng, đừng trích** khi nói guard tốn bao nhiêu.

> **Giả thuyết "guard chỉ dời `DetectChanges` sang sớm hơn" chưa bị bác cũng chưa được xác nhận ở độ
> phân giải này.** 20/30 cặp dương và trung vị dương thì nghiêng về việc guard **thêm** chi phí thật,
> nhưng không qua cổng nên chưa kết luận được. Đó là câu hỏi còn mở cho ai muốn đo lại.

⚠️ **Đừng đo lại bằng chính cách này.** Ba lượt chạy đã cho ba kết quả trượt cổng, và lượt cuối lộ ra
một **nhiễu có chu kỳ**: 10 cặp delta âm rơi vào **đúng các cặp 1, 4, 7, 10, … — chu kỳ 3, không sót
cặp nào**, và nhánh B ở đúng những cặp đó vọt lên ~60–70 ms. Đó là **hiện tượng hệ thống**, không
phải nhiễu ngẫu nhiên, nên tăng số lượt sẽ không làm nó biến mất. Muốn đo lại thì phải tìm ra chu kỳ
3 ấy là gì trước (checkpoint của PostgreSQL? xoay vòng connection pool?), hoặc bỏ hẳn phép ghi thật.

Bốn lượt đã chạy, để không ai lặp lại:

| Lượt | Cấu hình | Kết quả | Cổng |
|---|---|---|---|
| 1–4 | 5 cặp, 1 cặp warm-up, trung-vị-A trừ trung-vị-B | 5,92 – 14,84 µs/entity | không có cổng; **cách trừ đã sai** |
| 5 | 30 cặp, 2 cặp warm-up, delta trong cặp | 26/30 dương · trung vị 6,45 · IQR 9,56 | **trượt cả hai** |
| 6 | 30 cặp, **8 cặp** warm-up, delta trong cặp | 20/30 dương · trung vị 8,46 · IQR 14,33 | **trượt cả hai, tệ hơn** |

Ba số đo trực tiếp, ghi được vì chúng đo đúng thứ chúng nói (`The_parts_of_the_guard_measured_separately`):

| Đo cái gì | µs/entity |
|---|---|
| `Entries<T>()` kèm `DetectChanges` | 8,6 – 9,7 |
| `Entries<T>()` lượt ấm — **con số 3,7 cũ** | 3,7 – 8,5, **không tái lập ổn định** |
| Vòng kiểm phạm vi — **công việc thật của guard** | **0,21 – 0,23** |

⚠️ **Đừng cộng ba số này lại thành "chi phí guard".** Đó chính là lỗi quy-sai đã sinh ra con số 3,7,
chỉ lệch chiều ngược. Chúng chỉ để hiểu chi phí **nằm ở đâu** bên trong guard — phép đo A/B, thứ duy
nhất trả lời được guard tốn thêm bao nhiêu, **không kết luận được**.

> **Nếu có người lấy cớ hiệu năng đòi nới guard:** việc kiểm phạm vi tốn **0,21 µs** — nới nó ra
> không mua được gì. Đường tối ưu đúng là tắt `AutoDetectChangesEnabled` quanh vòng nạp rồi gọi
> `DetectChanges()` **một lần** trước `SaveChanges`: bỏ lượt quét thừa, **giữ nguyên** phần kiểm.

### Quy trình: ĐỌC migration sinh ra TRƯỚC khi apply

**Bắt buộc, không phải khuyến nghị.** Sau `dotnet ef migrations add`, mở file `Up()` và `Down()` ra
đọc **trước** khi `database update`. Kiểm hai câu hỏi:

1. **Có thao tác nào mình không yêu cầu không?** Đặc biệt là `DropIndex`, `DropColumn`,
   `AlterColumn`. Thêm một cột thì migration chỉ được có `AddColumn` (+ index nếu mình khai).
2. **`Down()` có đối xứng với `Up()` không?**

**Đây là một LỚP LỖI, không phải hai sự cố rời rạc.** Cả hai lỗi nặng nhất của repo tới giờ đều
thuộc lớp này — chúng **qua compile, qua toàn bộ test, và chỉ lộ ra khi có người đọc SQL**:

| Lỗi | Qua được gì | Lộ ra khi |
|---|---|---|
| **BE-06** — `LPAD` cắt bớt ID, cột thứ 10000 thành `POLE-1000` | compile ✓ test ✓ | đọc lại biểu thức `DEFAULT` |
| **BE-12a** — EF `DropIndex` ba index `commune_id` khi thêm partial unique index | compile ✓ test ✓ | đọc migration sinh ra |

Không có công cụ nào trong stack bắt được lớp này: analyzer không đọc SQL, test tích hợp chạy trên DB
đã apply nên nó *thấy* lược đồ mới là bình thường, và cả hai lỗi đều **không ném exception** — chúng
làm dữ liệu sai hoặc truy vấn chậm dần. Thứ duy nhất bắt được là mắt người, một lần, trước khi apply.

### Quy ước: PARTIAL INDEX không bao giờ thay thế được index khoá ngoại

**Khi thêm bất kỳ index nào dẫn đầu bằng `commune_id`, phải khai lại `HasIndex("CommuneId")` tường
minh trong cùng cấu hình đó.**

EF Core tạo index cho khoá ngoại bằng **convention**, và convention **bỏ qua** nếu đã có index khác
dẫn đầu bằng cùng cột. Nó **không phân biệt index đầy đủ với partial index** — đó là toàn bộ vấn đề.
Một partial index chỉ phủ tập con hàng thoả `WHERE` của nó; nó **không** phục vụ được truy vấn trên
những hàng còn lại.

Vì sao ở repo này thì nghiêm trọng: **query filter BE-08 đưa `commune_id` vào `WHERE` của MỌI truy
vấn**. Mất index `commune_id` là mất index của toàn hệ thống, không phải của một tính năng.

Gặp thật ở BE-12a: `HasIndex("CommuneId", "ExternalRef")` với filter `external_ref IS NOT NULL` làm
migration **DROP cả ba** `ix_pole_commune_id`, `ix_road_segment_commune_id`, `ix_feeder_commune_id`.
Index còn lại không phủ hàng `external_ref IS NULL` — tức **đa số cột**, những cột nạp từ ảnh công
khai. Bắt được lúc đọc migration sinh ra, **không phải** lúc chạy: không có lỗi, chỉ chậm dần.

**Ticket sắp đụng vào:** **BE-15** và **BE-17** gần như chắc chắn — bất kỳ unique index nào kiểu
`(commune_id, <cái gì đó>)`. Mẫu đúng ở `ExternalRefColumn.HasExternalRef`.

Xem quy tắc quy trình ngay dưới đây — không có `DropIndex` nào được phép xuất hiện trong một
migration mà bạn chỉ định thêm cột.

### Quy ước: `ExecuteUpdate` / `ExecuteDelete` bị CẤM ở compile-time

`CommuneWriteGuard` là override của `SaveChanges` và nó duyệt **ChangeTracker**. `ExecuteUpdate` và
`ExecuteDelete` dịch thẳng ra SQL, **không đi qua ChangeTracker**, nên chúng **vô hiệu hoá hoàn toàn
kiểm phạm vi Contract mục 7** ở đường ghi.

Đây là **lỗ hổng kiến trúc**, cùng họ với lỗ hổng BE-08 (query filter chỉ áp lên đọc): guard được
viết ra để bịt phía ghi, mà một API bulk mở lại đúng cái lỗ đó.

Bốn ký hiệu đã vào `BannedSymbols.txt`, RS0030 = error:
`ExecuteUpdate`, `ExecuteUpdateAsync`, `ExecuteDelete`, `ExecuteDeleteAsync`.

Đường đúng: **nạp hàng rồi `Remove` / sửa**, để guard nhìn thấy. Nếu thao tác thật sự nằm ngoài mọi
phạm vi thì **nói ra**: `EnterUnscopedSystemWriteBackdoor()` kèm `#pragma warning disable RS0030`
giải thích vì sao.

Hai nhóm ngoại lệ đang tồn tại, đều chính đáng và đều đã pragma tường minh:

| Chỗ | Lý do |
|---|---|
| `AuthService` (4 chỗ) | `RefreshToken` **không** `ICommuneScoped` — guard chưa bao giờ áp. Và luồng xoay token của BE-07 **phụ thuộc** vào số dòng mà `UPDATE` có điều kiện trả về để phân xử refresh đồng thời; nạp-rồi-sửa không diễn đạt được điều đó mà không tái tạo race. |
| Teardown của test (13 chỗ) | Xoá hàng loạt là cách duy nhất dọn được dưới scope rỗng. **BE-36 xoá luôn nhu cầu** — mỗi lần chạy một DB sạch. |

⚠️ **BannedApiAnalyzers có lỗ, đã đo ở BE-10:** nó chỉ khớp khi **mọi tham số được truyền tường
minh**, mà cả hai API bulk đều có `CancellationToken` tuỳ chọn. Nên có thêm `BannedBulkWriteApiTests`
quét văn bản mã nguồn, khẳng định **mọi** lần gọi nằm trong một vùng `#pragma warning disable RS0030`
và **không file nào để vùng đó hở tới cuối file**. Đừng xoá test đó vì tưởng analyzer đã lo.

### Quy ước cho MỌI cột `double precision` đo được

**Cột số thực biểu diễn một đại lượng đo được PHẢI có CHECK loại `NaN` và `±Infinity`.**
`>= 0` là **không đủ**.

**Vì sao:** Postgres xếp `NaN` **LỚN HƠN mọi số thực** — ngược hoàn toàn với IEEE 754, nơi mọi phép
so sánh với `NaN` đều false. Nên `'NaN'::float8 >= 0` là **`true`**, và `'Infinity' >= 0` cũng vậy.
Chỉ `-Infinity` bị `>= 0` chặn.

⚠️ **Idiom `x = x` KHÔNG dùng được ở Postgres.** Trong IEEE 754 thì `NaN <> NaN`, nên `x = x` bắt
được `NaN`. **Postgres coi `NaN` BẰNG chính nó**, nên `x = x` là tautology thật và bắt được **con số
không**. Kiểm: `SELECT 'NaN'::float8 = 'NaN'::float8` → `t`.

Khuôn đúng:

```sql
CHECK (x >= 0 AND x <> 'NaN'::float8 AND x <> 'Infinity'::float8)
```

**Đây là lỗi câm.** Không exception, không log — chỉ là một hàng trông bình thường trong mọi danh
sách, rồi mọi phép trung bình, độ lệch chuẩn và tương quan tính từ nó trả về `NaN`.

**Cả JSON cũng hở, không chỉ DB.** `JsonSerializerDefaults.Web` bật `AllowReadingFromString`, và với
kiểu số thực cờ đó nhận luôn chuỗi `"NaN"` / `"Infinity"`. Đã bịt bằng `FiniteDoubleConverter` ở
`LuxMapJsonOptions` — **đừng gỡ tưởng thừa**, canh bằng `JsonNumberHandlingTests`. Không dùng
`JsonNumberHandling.Strict` vì nó chặn luôn số có nháy kép hợp lệ như `"12.4"`, đổi hình dạng wire
của mọi endpoint để sửa một vấn đề chỉ nằm ở ba literal.

**Ticket sắp tạo cột kiểu này — phải áp quy ước ngay từ migration đầu:**

| Ticket | Cột |
|---|---|
| BE-15 / BE-17 | `normalized_luminance`, `baseline_ratio` |
| BE-33 | `dim_threshold_ratio`, `out_threshold_ratio` |

> 🔴 **`pole_current_status.status_confidence` ĐANG HỞ — chưa sửa, chờ quyết.**
> Nó nhận `NaN`, `Infinity`, **và cả giá trị ngoài `0..1`** (đã chèn thật `42.5` và nó vào). CHECK duy
> nhất trên cột đó là `ck_pole_current_status_confidence_matches_status`, chỉ ràng buộc **NULL hay
> không** so với `fixture_status`, không nói gì về giá trị. XML doc ghi *"0..1"* nhưng **không có
> ràng buộc nào thực thi**. Quyền ghi bảng đó thuộc **BE-15/BE-17** nên bản sửa thuộc về đó.

### Quy ước: MỌI test ghi tài sản nằm trong `AssetDatabaseCollection`, và KHÔNG test nào ghim ID bằng literal

Hai quy tắc, một nguyên nhân: **`pole_id_seq` là tài nguyên TOÀN CỤC dùng chung**, còn DB phát triển
thì dùng chung giữa mọi lượt chạy.

**1. Một collection duy nhất cho 16 class ghi tài sản.** xUnit chạy song song các class **khác
collection**, tuần tự các class **cùng collection**. `PrefixedIdOverflowTests` phải `setval` ghim
sequence tới ngưỡng độ rộng đệm rồi chèn — việc đó **không chịu được người ghi thứ hai**: lượt lùi
sequence trao cho hàng xóm một ID đã có, còn `nextval` của hàng xóm cướp mất ID vừa ghim. Bên thua
chết vì `pk_pole`, thông điệp không hề nhắc tới race.

Đo thật, 5 lượt mỗi bên, cùng máy cùng DB: tách hai collection → **4,1–4,3 s, 4/5 lượt đỏ**; gộp một
collection → **5,2–5,4 s, 5/5 xanh**. **Một giây đổi lấy bộ test không còn đỏ ngẫu nhiên.** Đừng tách
ra lại vì lý do tốc độ.

**2. Không literal ID nào trong test — chọn dải từ bảng LIVE.**

> 🔴 `AssetSchemaFixture` chèn 2500 cột bắt đầu từ **chỗ `pole_id_seq` đang đứng**, KHÔNG phải từ 1.

`PrefixedIdOverflowTests` từng giành literal `3000` / `30000`, canh bằng
`Assert.True(SyntheticPoleCount < 3000)` — câu đó **mã hoá một tiền đề sai**. Sau khi BE-39 seed bộ
mock FO-26, sequence đứng ở **854**, khối 2500 cột rơi vào **855..3354**, trùm lên 3000. Hậu quả
không dừng ở một test: lượt `setval(2999)` **làm nhiễm độc sequence** cho khoảng 355 lượt chèn kế
tiếp, nên một lượt chạy đỏ 36 test, lượt sau đỏ 76 — trông y hệt flaky, thực ra tất định.

Đường đúng: hỏi bảng xem số nào còn trống rồi mới ghim (`FreeFourDigitDecadeAsync`), và nêu tiền đề
thành assert có thông điệp (`RequireFreeAsync`) thay vì để nó lộ ra dưới dạng `23505` trần từ trong
`SaveChanges`.

⚠️ Ngưỡng **9999 → 10000** là ngoại lệ: nó do chính độ rộng đệm quy định nên **không dời đi đâu
được**. Vì vậy thập niên `1000` bị loại khỏi danh sách ứng viên — bội mười của nó đúng là 10000.

**BE-36 (Testcontainers, W17–W18) xoá bỏ toàn bộ lớp lỗi này** — mỗi lượt chạy một DB sạch, sequence
bắt đầu từ 0, không còn dải nào bị chiếm trước. Tới lúc đó có thể xét tách lại collection.

### Năm quy tắc chốt ở BE-18 — sự cố

**1. Ghi vết đặt TRÊN bảng `fault`, không có `FaultHistory`.**

Contract mục 2.5 chỉ cho kỹ sư đổi **ba** trường (`fault_status`, `override_fault_type`, `note`), nên
số quyết định đáng ghi là hữu hạn và biết trước. Cột `reported_by` · `confirmed_by`+`confirmed_at` ·
`resolved_by`+`resolved_at` trả lời thẳng câu hỏi *ai làm gì*. Một bảng change-log tổng quát nặng hơn
vấn đề, và vẫn phải quyết "actor là gì khi engine sinh sự cố".

⚠️ **Đánh đổi: chỉ giữ quyết định MỚI NHẤT, không giữ chuỗi.** Một fault đi
`detected → confirmed → rejected` chỉ còn trạng thái cuối.

> 🔴 **Audit trail nay là BẮT BUỘC, không còn "nếu BE-19 cần" (D-R13, 25/09/2026).** Phiếu v1.2 đòi
> *"an audit trail of every automated finding and engineer decision"*. Hướng đã chốt: **một bảng audit
> append-only dùng chung** cho quyết định fault (BE-18/19), lệnh điều khiển đèn (D-R7) và review survey
> session — **không** làm `FaultHistory` riêng cho từng loại. Thiết kế chi tiết ở Phase 1 của **BE-19**.
> Các cột trên dòng `fault` vẫn giữ (trả lời nhanh *ai quyết mới nhất*), nhưng không còn là toàn bộ vết.

`reported_by` **NULL với fault do CV/IoT sinh**, và đó là câu trả lời đúng chứ không phải dữ liệu
thiếu — `source_channel` đã nói engine nào. **Không dựng user hệ thống giả**: nó làm mọi dòng trông
đồng đều trong khi ghi một việc chưa từng xảy ra, rồi lại hiện ra trong mọi thống kê "ai báo sự cố".

**2. `commune_id` có HAI nguồn, vì `pole_id` nullable.**

| Ca | Nguồn |
|---|---|
| Có `pole_id` | Server tra từ pole. Client gửi → **400** |
| `pole_id` NULL | Lấy từ **scope JWT**. Đúng một commune → lấy luôn. Nhiều hơn một → **400**, client phải chọn (và giá trị chọn phải trong scope) |

Không suy được từ `lat`/`lng`: **`administrative_unit` không có cột geometry** — cố ý khi đó, vì chưa có
nguồn ranh giới thật (`AdministrativeUnit.cs`). ⚠️ Lý do này dựa trên FO-01 (nay `SUPERSEDED`) — **nền đã
đổi, cần xét lại** (follow-up trong `tracking.html`). Đã đăng ký drift: Contract mục 2.8 không có
`commune_id` trong body.

**3. `priority_score` là cột LƯU, nullable.**

Contract nói **CV-16 tính** — một tiến trình ngoài API, nên giá trị phải có chỗ ghi vào. Và mục 2.4
đặt thứ tự mặc định `-priority_score`: sắp theo biểu thức tính lúc query thì **không dùng được index
và phân trang không ổn định**. Index là `DESC NULLS LAST` — fault CV-16 chưa chấm nằm cuối, không
phải đầu.

⚠️ **Nợ:** BE-33 đổi trọng số thì phải **tính lại toàn bảng**. Job nền là **BE-26 (W12)**.

**4. `fault_cluster` là BẢNG, không phải cột text trần.**

Contract mục 2.4 chỉ lọc theo `cluster_id` nên một cột text là đủ cho endpoint — nhưng nó sẽ là **ID
không ràng buộc thứ HAI**, cùng họ với `pole_current_status.last_sweep_id`, và không có gì phát hiện
một giá trị trỏ vào cụm không tồn tại. Bảng cũng cho lần gom cụm chỗ tự ghi lại:
`clustering_model_version` là thứ **BE-34** cần để trả lời *kết quả nào do model phiên bản nào sinh*.

Chưa có gì ghi vào bảng này — CV-15 gom cụm, mà CV-15 phụ thuộc **BE-13** (chưa làm).

**5. Toàn bộ FK là `Restrict`. Fault là sự kiện đã xảy ra.**

Xoá cột không làm cho việc "đèn từng hỏng ngày 20/8" chưa từng xảy ra, và cột ghi vết trên chính dòng
đó **là** tiêu chí nghiệm thu của ticket. Cascade còn mở rộng **vùng mù** ở mục 1c — guard
`SaveChanges` không thấy cascade do DB thực hiện.

**Định nghĩa "fault MỞ" — CHỐT, một chỗ duy nhất: `FaultStatusSets.Open`.**

```
open fault  ==  fault_status ∈ { detected, confirmed, in_progress }
```

Contract liệt kê 6 trạng thái nhưng **không nói cái nào là mở** — `open_fault_count` xuất hiện đúng
một lần ở mục 2.1, trong một khối code, **không kèm câu mô tả nào**; `has_open_fault` và
`open_faults[]` cũng vậy. Trong khi đó `open_fault_count`, BE-28 và BE-40 đều cần **cùng một** câu
trả lời. Ba ticket tự quyết riêng sẽ ra ba con số khác nhau mà không ai giải thích được.

Ba trạng thái bị loại vì **ba lý do khác nhau** — đó là lý do tập này được viết ra tường minh thay vì
diễn đạt kiểu "chưa xong": `rejected` = kỹ sư kết luận nó chưa từng là sự cố (dương tính giả),
`resolved` = đã sửa, `verified` = đã nghiệm thu. Chỉ cái đầu là phán xét về **bản thân sự cố**.

> 🔴 **KHÔNG viết lại tập trạng thái này ở bất kỳ đâu khác.** Không `IN ('detected', …)` trong SQL,
> không `new[] { … }` trong LINQ, không danh sách chép tay trong test. Cần biết một fault có mở
> không thì gọi **`FaultStatusSets.IsOpen(status)`**; cần cả tập thì dùng **`FaultStatusSets.Open`**.
> Bản sao thứ hai sẽ đúng vào ngày nó được viết và sai vào ngày định nghĩa đổi, **và không có gì phát
> hiện được** — cùng loại lỗi câm mà `commune_id` mồ côi và `LPAD` cắt ID đã gây ra.

**Drift 27 giờ ĐÃ CÓ câu trả lời**, và đó là quyết định **nội bộ backend**, không phải đổi Contract:
Contract không định nghĩa nên không có gì để mâu thuẫn. Vẫn giữ mục drift để nêu ở FW-00 — WP5 cần
biết con số họ nhận được đếm theo tập nào.

**Hai test canh, và chúng bắt hai thứ KHÁC nhau** (`OpenFaultCountTests`, ở `LuxMap.Shared.Tests` nên
chạy được cả khi không có Docker):

| Test | Canh gì | Đỏ khi |
|---|---|---|
| `The_count_on_every_pole_matches_the_faults_that_are_actually_open` | **DỮ LIỆU** — `open_fault_count` của cả 103 cột trong `mock-poles.geojson` khớp số đếm thật từ `mock-faults.json` | ai đó thêm/sửa fault mà quên cập nhật count |
| `Only_detected_confirmed_and_in_progress_count_as_open` | **ĐỊNH NGHĨA** — dựng fault trong bộ nhớ phủ cả 6 trạng thái | ai đó đổi tập trong `FaultStatusSets.Open` |

⚠️ **Không gộp hai test làm một.** Kiểm bằng phá hoại: thêm `FaultStatus.Rejected` vào
`FaultStatusSets.Open` làm test **định nghĩa ĐỎ** nhưng test **dữ liệu vẫn XANH** — vì cả 28 fault
trong bộ mock hiện đều mở (21 `detected` + 7 `confirmed`, không có `rejected`/`resolved`/`verified`
nào), nên nới định nghĩa không đổi con số nào. Test dữ liệu **không thể** phân biệt các định nghĩa
"open" khác nhau cho tới khi bộ mock có fault đã đóng.

**Hai test dùng CHUNG một hàm đếm** (`CountByPole`). Nếu tách thành hai đường thì test định nghĩa
không còn bảo vệ test dữ liệu — nó sẽ chỉ khẳng định một ý kiến riêng.

### Tám ràng buộc chốt ở BE-REVIEW-02 (18/09/2026)

**1. Mạch điện của cột phải cùng xã với cột. ✅ Nay là RÀNG BUỘC DB — xem mục O-7 bên dưới.**
`RequireFeederInCommuneAsync` (POST/PUT) và kiểm theo dòng trong `PlanPolesAsync` (import) vẫn là thứ
**trả lời**: 409 `CROSS_COMMUNE_REFERENCE`, không phải 403. Đường ghi mới (seeder BE-39, sync BE-43)
**vẫn nên gọi** — không phải để an toàn mà để có thông điệp tử tế; quên thì FK chặn, nhưng chặn bằng
500. **Tuyến thì KHÔNG kiểm**: `road_class = inter_commune` là đường chạy giữa các xã, cột ở xã khác
với chủ tuyến là hợp lệ.

**2. `DateTime` trên query string KHÔNG đi qua `UtcDateTimeConverter`.** Converter đó chỉ áp cho JSON.
Query binder trả `Kind=Unspecified` khi thiếu `Z`, và `.ToUniversalTime()` trên nó dịch theo múi giờ
máy chủ (đo được 7 giờ trên máy Asia/Saigon). Chuẩn hoá bằng `UtcNormalization.ToUtc` — Unspecified
= UTC, đúng Contract. Test `A_from_or_to_bound_without_a_Z_suffix…` chỉ đỏ trên máy không phải UTC,
đừng xoá vì thấy nó "luôn xanh" trên CI.

**3. Một cột có tối đa MỘT bóng đang dùng.** `ux_fixture_pole_id_active` là UNIQUE partial
(`pole_id WHERE removed_date IS NULL`). Thay bóng = ngừng dùng bóng cũ rồi ghi bóng mới; bóng có
`removed_date` là lịch sử, không chặn và không bị chặn. CRUD trả 409 `POLE_HAS_ACTIVE_FIXTURE`, import
báo theo dòng. BE-14 flatten từ **bóng đang dùng** — nay là duy nhất, không cần quy tắc tổng hợp.

**4. `removed_date >= install_date` và ngừng dùng đúng một lần.** CHECK
`ck_fixture_removed_after_install` ở DB; API trả 400 `VALIDATION_FAILED` nêu cả hai ngày; PUT lần hai
là 400 — `removed_date` là lịch sử thiết bị, không ghi đè.

**5. `pole_current_status.status_confidence` đã có CHECK hữu hạn 0..1** (`ck_pole_current_status_confidence_range`,
đóng drift 24). BE-15/BE-17 ghi vào bảng này phải qua nó; NaN/Infinity/42.5 giờ là 500 chứ không âm
thầm vào — hãy validate ở API trước cho ra 400.

**6. 403 có HAI mã.** `ROLE_FORBIDDEN` khi policy vai trò từ chối (`ForbiddenCodeResultHandler` ghi lý do
lên `HttpContext.Items`, trang status-code đọc lại); `COMMUNE_FORBIDDEN` chỉ cho địa bàn và cho claim
`["*"]` lệch vai trò. Đừng ném `COMMUNE_FORBIDDEN` cho việc không phải địa bàn.

**7b. Nạp bộ mock: dùng `scripts/seed_mock_set.py`, KHÔNG dùng endpoint import.**
Endpoint import nạp đúng dữ liệu nhưng **không giữ được ID của mock**: EF Core không bảo toàn thứ tự
`Add` khi database sinh khoá. Đo thật ngày 20/09 trên bảng rỗng với sequence đã đặt lại về 1: **102
trên 103 cột** rơi vào ID khác, `POLE-0047` hoá thành cột `POLE-0062` của mock, `SEG-001` mang tên
tuyến C. FE hardcode `POLE-0047` nên như vậy là hỏng demo. Script ghi ID tường minh rồi `setval`
sequence qua vùng đã dùng, đúng quyết định D-6.

**7. DB dev dùng chung bị test đẩy sequence.** `pole_id_seq` ở 396 625 với 0 cột (đo 18/09/2026):
cột thật đầu tiên trên DB này là `POLE-396626`. Hệ quả cho **BE-39**: seed bộ mock phải INSERT ID tường
minh (`POLE-0001…`) rồi `setval` sequence lên trên giá trị lớn nhất — ngoại lệ hệ thống đã chốt (D-6),
ghi trong Contract mục 1.2. Fixture test đã dọn token của tài khoản seed nó đăng nhập (N-5);
`AuthTestFactory` chưa — BE-36 (Testcontainers) là bản sửa gốc.

**8. Spec hợp nhất `docs/openapi/luxmap-v1.5.json` là file SINH.** Sinh bằng
`python3 docs/openapi/tools/gen_consolidated_spec.py` từ `luxmap-v1.json` (xuất từ code) — chạy lại
**sau mỗi lần** xuất `luxmap-v1.json`, rồi `npx @redocly/cli lint`. Không sửa tay cả hai.

### Bốn bẫy chốt ở BE-14 — bản đồ bbox (22/09/2026)

**1. 🔴 `Enum.TryParse` KHÔNG đọc được giá trị enum trên query string.**

Giá trị trên dây là snake_case thường (`calibration_rig`, `field_report`, `node_offline`), còn tên
thành viên .NET là `CalibrationRig`. `Enum.TryParse(ignoreCase: true)` **không biết dấu gạch dưới**,
nên nó **nhận giá trị một từ** (`normal`, `dim`, `out`) và **từ chối mọi giá trị nhiều từ**.

Đây là dạng lỗi ẩn kỹ nhất trong ticket này: bộ lọc thường dùng chạy tốt, chỉ vài giá trị trả 400, và
không ai nghĩ tới việc bộ phân tích enum mới là thủ phạm. Đã gặp thật ở `data_source=calibration_rig`.

Khuôn đúng: so với **tên trên dây**, sinh bằng `JsonNamingPolicy.SnakeCaseLower.ConvertName` — đúng
policy đang serialize chúng ra, nên giá trị client đọc được chính là giá trị nó gửi lại được. Xem
`MapController.WireName`.

**2. 🔴 Test THỨ TỰ không thay được test KẾ HOẠCH, và ngược lại.**

`ST_Intersects(geom, envelope)` trên cột 4326 thô là dạng **duy nhất** đi được `ix_pole_geom`. Bọc bất
kỳ hàm nào quanh cột — `ST_Transform`, `ST_Buffer` — thì **hàng trả về vẫn đúng y hệt**, chỉ mất index.
Kiểm bằng phá hoại: thêm `.Buffer(0)` làm plan rơi về `ix_pole_commune_id`, **không một assert dữ liệu
nào đỏ**.

Vì vậy `MapQueryPlanTests` chạy `EXPLAIN` trên **SQL mà EF thật sự sinh** (`ToQueryString()`), không
phải SQL viết tay. `SpatialIndexTests` của BE-09 vẫn EXPLAIN một câu viết tay kèm chú thích *"what
BE-14 will issue"* — đó là **dự đoán**, và giờ đã có bản đối chứng.

**3. 🔴 Teardown của fixture PHẢI xoá mọi bảng `Restrict` trỏ vào `pole`.**

`fault.pole_id` và `lux_reading.pole_id` đều `Restrict`. `AssetImportFixture` xoá
`fixture → pole → feeder → road_segment` nhưng **thiếu cả hai** — nên một test tạo fault làm lượt xoá
pole gãy, **cả teardown gãy theo**, và cột ở lại mang ID mà sequence sẽ phát lại. Lượt chạy sau chết
vì `pk_pole`, trông như flaky.

Đo được: 217 cột + 20 fault mồ côi tích lại qua vài lượt. Mọi test có ghi fault đều đang rò cột theo
đường này mà không ai thấy.

**4. 🔴 Lùi sequence dùng chung thì PHẢI trả nó về chỗ cũ.**

`PrefixedIdOverflowTests` cố ý `setval` lùi để chạm ngưỡng độ rộng, rồi **để nguyên**. Các lượt chèn
sau trong cùng run leo dần từ chỗ thấp đó và đụng bất cứ hàng nào đang nằm trên đường —
đúng "nhiễm độc ~355 lượt chèn" đã ghi ở mục bộ test tài sản.

Chọn dải trống **không đủ**: `FreeFourDigitDecadeAsync` kiểm thập niên nó ghi vào, **không kiểm cả
đoạn phía trên** mà sequence sẽ đi qua. Nay mỗi lượt lùi nằm trong `try/finally` trả sequence về giá
trị đọc được trước đó. Sau khi bịt, bộ test **xanh 3 lượt liên tiếp ngay cả khi 217 cột mồ côi vẫn
còn** — tức đây mới là nguyên nhân, không phải rác dữ liệu.

⚠️ **Đừng thêm `setval` vào fixture để "sàn" sequence theo ID lớn nhất.** Tôi đã thử: nó đẩy khối
2500 cột của `AssetSchemaFixture` vào đúng dải `9999/10000` mà `PrefixedIdOverflowTests` giữ, và ngưỡng
đó **không dời đi đâu được**. Trả sequence về chỗ cũ là đường đúng.

### Ba bẫy chốt ở O-7 — FK ghép `(feeder_id, commune_id)` (21/09/2026)

`pole` trỏ tới `feeder` bằng **cả hai** cột, nên "mạch điện của cột nằm trong xã của cột" là **luật
của lược đồ**, không còn là một lượt gọi hàm phải nhớ. Migration `FeederCommuneCompositeFk`. FK đơn
cột `fk_pole_feeder_feeder_id` **bị thay thế**, không phải thêm chồng — hai FK chồng nhau sẽ làm
`details.constraint` của `409 ASSET_IN_USE` thành không đoán được.

**1. 🔴 `MATCH SIMPLE` là thứ giữ cho cột-không-có-mạch hợp lệ. ĐỪNG "siết" sang `MATCH FULL`.**

Mặc định của Postgres là `MATCH SIMPLE`: hàng nào có **bất kỳ** cột FK nào null thì **bỏ qua hẳn**
lượt kiểm — `feeder_id` null, `commune_id` không null, hàng vẫn vào.

⚠️ **Lý do ban đầu của quy tắc này đã đổi, quy tắc thì không.** Trước v1.6 nó tồn tại vì cột
`solar_all_in_one` không đấu vào mạch nào; đèn solar nay hết, nên **mọi cột rồi sẽ có feeder**.
Nhưng `feeder_id` vẫn nullable và vẫn phải nullable: cột mới tạo chưa gán mạch, và `PUT` thay thế
toàn phần vẫn xoá mạch được. `MATCH FULL` vẫn sẽ từ chối đúng những hàng đó. `MATCH FULL` đòi các cột phải null **cùng nhau**; vì
`commune_id` không bao giờ null, nó sẽ từ chối **mọi** cột không có mạch trong bảng, tức đa số.

Đây là loại sửa trông *đúng hơn* lúc review. Canh bằng
`A_pole_with_no_feeder_is_still_legal_even_though_its_commune_is_not_null`.

**2. 🔴 `ak_feeder_feeder_id_commune_id` TRÔNG thừa và KHÔNG thừa.**

`feeder_id` đã là khoá chính, nên unique trên `(feeder_id, commune_id)` tự nó chẳng thêm gì — và đó
đúng là lý do sẽ có người xoá nó đi như rác. Nó là **đích** của FK ghép: Postgres chỉ cho khoá ngoại
trỏ vào cột có ràng buộc unique. Xoá nó là kéo theo cả FK ghép.

**3. `segment_id` KHÔNG có khoá tương tự, và đó là cố ý.** `road_class = inter_commune` nghĩa là
đường chạy **giữa** các xã (BE-REVIEW-02 ràng buộc 1). Thêm "cho đối xứng" sẽ làm tuyến liên xã hợp
lệ trở thành không ghi được. Canh bằng `A_pole_may_still_sit_on_a_segment_owned_by_another_commune`.

**Hai lớp, và cần cả hai — đừng bỏ lớp app vì "DB lo rồi".** `RequireFeederInCommuneAsync` vẫn chạy
trước và vẫn là thứ trả lời, vì FK một mình nổ ra `DbUpdateException` trần → middleware BE-04 trả
**500**. Cùng hình dạng `CommuneFilter.Narrow` chồng lên `CommuneWriteGuard`: thông điệp đọc được ở
trước, thứ không thể quên ở sau.

⚠️ **FK bắt được một ca mà tầng app CHƯA BAO GIỜ phủ:** đổi `commune_id` của **chính cột** trong khi
nó đang đấu vào một tủ điện. Lượt kiểm đọc xã của cột làm vế cố định nên không có gì kích hoạt. Hôm
nay không request nào hỏi được điều đó, nhưng đó là tính chất của controller tuần này chứ không phải
của dữ liệu.

⚠️ **Tác dụng phụ của alternate key: KHÔNG sửa được `commune_id` của `Feeder` đang track nữa.**
`Feeder.CommuneId` nay là **key property**, và EF Core cấm sửa key trên entity đang track: ném
`InvalidOperationException` từ `DetectChanges`, **trước** `CommuneWriteGuard`, nên ra **500** thay vì
403 `COMMUNE_FORBIDDEN`. **Dữ liệu không gặp rủi ro** — cả hai lớp đều từ chối. Cố ý **không** vá:
chuyển feeder sang xã khác vốn đã bị BE-12a cấm (nó kéo tủ điện ra khỏi các cột đang đấu vào, và không
còn lượt ghi nào bắt được), nên EF đang thực thi một luật nhóm đã chốt. Dịch lại lỗi thì phải bắt
`InvalidOperationException` quanh `SaveChanges` và khớp theo thông điệp có thể đổi khi nâng phiên bản
— bắt quá rộng một kiểu lỗi quá phổ biến, để làm đẹp thông điệp cho một thao tác không bao giờ được
phép thành công. Xem thêm ngoại lệ ghi ở mục 1c.

⚠️ **Index đổi hình:** `ix_pole_feeder_id` → `ix_pole_feeder_id_commune_id`. Vẫn dẫn đầu bằng
`feeder_id` nên phục vụ được mọi truy vấn lọc theo feeder, và vì query filter BE-08 luôn nhét
`commune_id` vào `WHERE`, index ghép còn phủ tốt hơn. **`ix_pole_commune_id` KHÔNG bị drop** — đã soi
riêng lúc đọc migration, vì đây đúng là cái bẫy đã cắn ở BE-12a (xem quy ước partial index).

### `[AllowAnonymous]` cấp CLASS thắng `[Authorize]` cấp METHOD (19/09/2026)

Thêm một endpoint **cần token** vào một controller đang mang `[AllowAnonymous]` ở cấp class thì gắn
`[Authorize]` lên method **không cứu được**: ASP.NET Core cho opt-out ở xa hơn thắng. Compiler nói
thẳng điều đó qua **ASP0026**, và repo này để 0 warning nên nó nhìn thấy được, nhưng warning không
phải error.

Đã suýt phát sinh thật ở `GET /auth/me`: `AuthController` mang `[AllowAnonymous]` cấp class từ BE-07,
nên endpoint mới sẽ **mở cho người chưa đăng nhập**. Cách sửa đã áp: chuyển `[AllowAnonymous]` xuống
**từng** method cấp token (`login`, `register`, `refresh`, `logout`), để mặc-định-đóng của BE-08 áp
cho mọi endpoint viết sau.

> 🔴 **Test theo HÀNH VI không bắt được cái này.** Với `[AllowAnonymous]` cấp class, request không
> token vào `/auth/me` **vẫn trả 401** — vì action chạy, không thấy claim `sub`, rồi tự ném. Cùng
> status, khác hoàn toàn lý do: endpoint đã **tới được**, và endpoint kế tiếp ai đó thêm vào
> controller sẽ thừa hưởng opt-out mà không ai đọc lại. Thứ bắt được là **assert trên metadata của
> route**: `endpoint.Metadata.GetMetadata<IAllowAnonymous>()` phải `null`
> (`CurrentUserTests.It_is_not_declared_anonymous_even_though_its_four_siblings_are`). Kiểm bằng phá
> hoại: trả `[AllowAnonymous]` về cấp class thì test hành vi **vẫn xanh**, test metadata **đỏ**.

### Vai trò

Bốn vai trò đăng nhập theo Phiếu v1.2 mục 3.2.c (Contract v1.7 §2, D-R6):

| Vai trò | `user_role` | Thay cho (≤ v1.6) | Tài khoản demo |
|---|---|---|---|
| **Cấp giám sát** (Superior) — bản đồ, thống kê, báo cáo; **chỉ đọc** | `superior` | `management_agency` | `agency` |
| **Quản lý** (Manager) — tài sản, giao việc khảo sát / sửa chữa, duyệt sự cố, điều khiển đèn testbed | `manager` | `maintenance_engineer` | `engineer` |
| **Kỹ sư hiện trường** (Field Engineer) — khảo sát đêm, kiểm tra, sửa chữa (Tổ khảo sát đã gộp vào đây) | `field_engineer` | `field_crew` | `crew` |
| **Quản trị hệ thống** (System Admin) — tài khoản, vai trò, cấu hình, giám sát, log | `system_admin` | `administrator` | `admin` |

Phân quyền theo vai trò (ma trận capability, mục BE-12a quy tắc 4) **và** theo địa bàn. Cấp giám sát
xem nhiều xã bằng **danh sách xã được gán**, không có cấp huyện (D-R3). Username seed và biến
`SEED_*_PASSWORD` **giữ tên cũ** — chúng là định danh, không phải nhãn (D-R6).

**Không có tự đăng ký (D-R11).** Quản trị hệ thống tạo tài khoản, gán vai trò và xã. `POST /auth/register`
còn chạy nhưng **DEPRECATED**, gỡ ở BE-33a — đừng xây gì mới lên nó.

**Công dân (Citizen) không có tài khoản, không có vai trò (D-R1).** Họ báo sự cố qua **QR trên cột**; báo
cáo vào **hàng chờ riêng**, Quản lý duyệt rồi mới thành `fault`. **Không** thêm giá trị vào
`source_channel`, và **không** để báo cáo của dân đi thẳng vào `fault` — nguyên tắc cũ vẫn đúng: không có
luồng nào để một người "tạo sự cố" rồi hệ thống tin ngay, **người có vai trò duyệt** chứ không tạo.
`Fault` do engine sinh (`cv` / `iot`) hoặc do Kỹ sư hiện trường báo tại chỗ (`field_report`).

**Điều khiển ON/OFF/AUTO (D-R7).** Chỉ **Quản lý** (`ControlLighting`); chỉ thiết bị
`supports_remote_control = true` — tức thiết bị **testbed** tự dựng; thiết bị ngoài thực địa luôn
`false`, vì nhóm không có quyền vận hành lưới chiếu sáng của xã; **mọi lệnh ghi audit**. Docs viết
*"supported lighting devices (testbed demo)"*. Đừng viết như thể hệ thống điều khiển lưới thật.

---

## Quy tắc dễ sai âm thầm

Vi phạm thì hệ thống vẫn chạy, số liệu vẫn ra, nhưng kết quả nghiên cứu vô nghĩa.

- **Từ chối `SurveyFrame` thiếu metadata phơi sáng** (BE-16). Thiếu ISO / shutter / aperture / GPS / heading → từ chối kèm lý do rõ. Không nhận, không suy đoán. Ảnh auto-exposure **không sửa được sau khi chụp**.
- **Ngưỡng phân loại là tỉ lệ so với baseline của chính cột đó**, không bao giờ là giá trị luminance tuyệt đối.
- **Không so sánh độ sáng giữa hai cột khác nhau.**
- **`unknown` đếm riêng, không gộp vào `out`.**
- **Mọi phát hiện tự động ghi kèm phiên bản model và firmware** (BE-34). Không tái lập được thì không phải kết quả.
- **Mọi phát hiện tự động và mọi quyết định của người đều vào audit trail** (D-R13): bảng audit append-only dùng chung, thiết kế ở BE-19. Cột `confirmed_by/at`, `resolved_by/at` trên dòng `fault` (BE-18) chỉ là quyết định mới nhất.
- **Precision / recall báo RIÊNG cho `out` và `dim`** (NFR phiếu v1.2). `dim` là lớp khó và đáng giá; một con số gộp sẽ giấu nó — cùng họ lỗi với gộp `unknown` vào `out`.
- **Báo tỉ lệ cột có mạch điện được xác nhận** (NFR *feeder topology coverage*). Cột không gán được mạch thì gom cụm chỉ theo hình học, và **độ tin cậy giảm phải được ghi lại** — không im lặng coi như có mạch.
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

Nội dung cố ý cài sẵn: **103 cột** (70 `normal` / 10 `dim` / 16 `out` / 7 `unknown`), một **cụm lỗi cả đoạn trên `SEG-003`**, **12 IoT node**, và **`POLE-0047`** có chuỗi runtime suy giảm dần 18 đêm (`dim`, có `NODE-047` — đèn mờ dần, không tắt phụt; từ Contract v1.6 là cột `grid` / `led_road_lamp`).

FE đang code theo bộ này. **BE-39 phải seed lại đúng bộ mock đó** để demo khớp với những gì FE đã dựng.

> 🔴 **Lập trường về `external_ref` của bộ mock — MỞ LẠI, ưu tiên cao (D-R10, 25/09/2026).**
>
> Bộ mock không mang mã kiểm kê nào, nên cách nạp duy nhất là lấy chính `pole_id` / `segment_id` của
> mock làm `external_ref` (`AssetImportMockSetTests` làm đúng vậy).
>
> Lập trường cũ ("danh tính ngoài VĨNH VIỄN, di trú mã ngoài phạm vi") dựa trên FO-01 — không có thử
> nghiệm hiện trường nên không có mã kiểm kê thật nào sẽ về. **FO-01 nay `SUPERSEDED`**: phiếu v1.2 có
> field trial với xã đối tác, nên mã kiểm kê thật **có thể** về. **Nền đã đổi, cần xét lại** — và phải xét
> **trước khi** dữ liệu kiểm kê thật về, vì dữ kiện này vẫn nguyên: đường "xoá sạch nạp lại" **đóng từ
> W5**, do `fault` và `lux_reading` trỏ vào `pole` bằng `Restrict` và FO-14 đo lux ở W5. Sau mốc đó chỉ
> còn đường `UPDATE pole SET external_ref = …` kèm bảng ánh xạ.

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
- Đừng gộp số liệu giữa các nguồn `data_source` — và đừng gắn `field` cho dữ liệu testbed.
- Đừng hard-code ngưỡng dim/out hay trọng số ưu tiên.
- Đừng đề xuất node IoT cho mọi cột — sparse IoT là thiết kế, không phải điểm cần tối ưu.
- Đừng cho báo cáo QR của công dân đi thẳng vào `fault`, và đừng tạo tài khoản / vai trò cho công dân.
- Đừng viết điều khiển ON/OFF/AUTO như thể nó chạm lưới chiếu sáng của xã — chỉ thiết bị testbed.
- Đừng giả định có mạng — mobile phải chạy trọn ca offline.
- Đừng âm thầm bỏ frame không hợp lệ.

---

## Quy ước làm việc

- Nhánh Git mang mã task: `feat/BE-09-pole-fixture-entity`, `feat/BE-43-sync-bundle`.
- **Stage theo đường dẫn tường minh. Không bao giờ `git add -A` hay `git add .`** — nó quét cả file
  untracked không liên quan. Đã xảy ra ở BE-12: `AGENTS.md` (1055 dòng, có sẵn trong working tree)
  bị nuốt vào commit đầu của ticket.
- Mã task: `BE-` backend · `IOT-` telemetry · `CV-` vision/analytics · `FW-` web · `FM-` mobile · `FO-` field ops.
- Mọi API cùng định dạng response, có correlation id (BE-04).
- Test tích hợp chạy trên PostGIS thật, phủ truy vấn không gian và bbox.

## Thứ tự hiện tại

W1 (xong): nền tảng **BE-01 → BE-00 → BE-02..BE-07**. W2–W3 (đang): GIS tài sản — BE-09/10/11/12a/12 xong, BE-42 và BE-18 đẩy sớm, BE-REVIEW-02 xong 18/09/2026. O-7 (FK ghép) xong 21/09/2026. **Kế tiếp: BE-13** — cần **O-6** (file `feeder_id` cho bộ mock).

Sau đó: GIS tài sản (W2–W4) → khảo sát (W5–W7) → sự cố (W7–W9) → quy trình (W9–W12) → dashboard (W13–W15) → quản trị (W15–W17) → hoàn thiện (W17–W21).

**Đã chốt ở mục 0.1–0.4:** ID sinh bằng **sequence PostgreSQL**, format ngay ở tầng DB — biểu thức `DEFAULT` chuẩn nằm ở **mục 0**, không chép lại ở đây. EF Core map bằng `.HasDefaultValueSql()` + `.ValueGeneratedOnAdd()`. Sequence an toàn concurrency sẵn, không cần bảng counter. Chấp nhận có khoảng trống trong dãy số khi insert lỗi.

> ⚠️ **Ghi chép lịch sử:** chỗ này trước đây ghi dạng `LPAD`. Cách đó SAI và đã bị bỏ ở commit `8ea9930` — lý do ở **mục 0**. Đừng khôi phục lại dạng `LPAD`.

**Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID); server gán ID thật khi nhận và trả lại ánh xạ.

**`CREATE FUNCTION` phải luôn đứng TRƯỚC mọi migration dùng nó trong `DEFAULT`.** `luxmap_format_id` được tạo trong `FixPrefixedIdOverflow`, và `DEFAULT` của **6 cột ID** gọi nó. Nếu sau này gộp migration thì thứ tự đó là **ràng buộc cứng**, không phải chi tiết trình bày — đảo thứ tự là DB không dựng được. `Down()` drop function để đối xứng, chép đúng chữ ký trong code: `DROP FUNCTION IF EXISTS luxmap_format_id(text, bigint, int)` — `int`, không phải `integer`.

> **6 chứ không phải 16 — cách phân biệt, để không ai sửa ngược.** 6 là số cột **tồn tại tại thời điểm migration** đó: `segment_id`, `pole_id`, `fixture_id`, `feeder_id`, `user_id`, `commune_id`. `PrefixedIds` khai **16 SPEC**, nhưng 10 spec còn lại thuộc entity chưa được tạo (`Fault`, `SurveyFrame`, `LuxReading`…). Số cột thật sẽ tăng dần khi các entity đó ra đời; con số 16 chỉ đúng cho *bảng prefix*, không đúng cho *migration này*.

Ba task mới v2.0 dễ bị quên vì không có trong kế hoạch cũ: **BE-40**, **BE-41**, **BE-43**. Cả ba đang chặn WP6.
