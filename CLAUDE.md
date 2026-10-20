# LuxMap — Backend (WP2)

Nền tảng GIS + IoT + Computer Vision quản lý tài sản và sự cố chiếu sáng đường nông thôn.
Tên đề tài (Phiếu đăng ký FA26SE222 v1.4, mục 3.1 — không đổi từ v1.2): **LuxMap: A GIS, IoT and Computer Vision Platform for
Rural Road Lighting Asset and Fault Management** — *LuxMap: Hệ thống bản đồ số GIS tích hợp IoT và thị giác
máy tính để quản lý tài sản và sự cố chiếu sáng đường giao thông nông thôn*. Phiếu (bản markdown:
`docs/registration/FA26SE222_v1.4.md`, từ 27/09/2026; bản v1.2 là lịch sử) là nguồn chuẩn cho actor, chức
năng, NFR và deliverable.

> 🔴 **Phiếu v1.4 (27/09/2026) đổi kênh đo độ sáng — nền của BE-42 và của phân loại `dim` đã đổi, CHƯA
> CHỐT.** CV chỉ quyết **ON/OFF**; độ sáng đo bằng **BH1750FVI gắn trên xe khảo sát — đầu xe máy**, lấy mẫu liên tục suốt sweep;
> **Dim = CV nói ON + đỉnh lux lúc ngang cột dưới ngưỡng baseline của chính cột đó**. Enum mục 1 **không
> đổi**. Các đề xuất D-R20…D-R28 ở `docs/contract-drift.md` (mục *Registration v1.4*) **chưa có chữ ký** và
> đa số chạm API → mọi chỗ bên dưới nói "đo sáng bằng điện thoại", "lux là ground truth chấm CV" là **nền
> cũ**: đừng xây ticket mới lên đó. **D-R21 / D-R24 đã chốt hướng (SELF-SIGNED 27/09, nền tạm)**: mỗi
> phiên = video + log lux BLE + GPS track + cấu hình quay, **chung đồng hồ `elapsedRealtimeNanos`**,
> server xử lý toàn bộ; upload video (clip <1 phút, ~100–150 MB) là **D-R28 — đề xuất proxy qua API, chờ quyết**. Khảo sát:
> `.ai/results/registration-v1.4-phase1.md`.
Capstone FA26SE222 · W1–W21: 07/09/2026 – 31/01/2027 · Repo này là **WP2, phụ trách: BE1 – Mỹ**.

Backend phục vụ 3 consumer: **Web SPA** (WP5), **Android native** (WP6), **engine CV** (WP4).
Không có consumer nào khác. **Công dân (Citizen) không có tài khoản**: họ báo sự cố qua mã QR trên cột
— `/public/…` không token, Quản lý duyệt ở `/citizen-reports` (D-R1, CITIZEN-QR P2a–P2c; xem mục "Vai trò").

## Nguồn sự thật

Ba tài liệu, thứ tự ưu tiên khi mâu thuẫn:

1. **`docs/api-contract-v1.1.md`** — bản hợp nhất **v1.8** (05/10/2026; tên file giữ nguyên để liên kết cũ còn đúng). **Thắng mọi thứ khác.** Đã gộp v1.0 → v1.4, toàn bộ drift 1–43 và các quyết định BE-REVIEW-02; bản máy đọc khớp 1-1 là `docs/openapi/luxmap-v1.5.json`. Log drift cũ ở `docs/archive/contract-drift-v1.md`, log mới ở `docs/contract-drift.md`.
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
3. `.ai/context/commands.md` — lệnh build/test/migrate (bản đầy đủ: `docs/development.md`; chạy lần đầu: `README.md`)

Ghi kết quả vào `.ai/results/<ticket>.md`, review vào `.ai/reviews/<ticket>-by-<agent>.md`. Quy ước
đầy đủ, vòng đời ticket và quy tắc hai pha: `.ai/README.md`.

⚠️ `.ai/` **đứng cuối** thứ tự ưu tiên — sau Contract, sau `tasks-backend.csv`, sau file này. Và nó
**không nhận** bốn loại phát hiện ở bảng trên: deviation vẫn về `contract-drift.md`, ràng buộc kỹ
thuật vẫn về đây, tiến độ vẫn về `tracking.html`. Một deviation ghi trong `.ai/results/` là deviation
**không bao giờ tới tay WP5/WP6**.

---

## Phạm vi — theo Phiếu đăng ký v1.2 (D-R10, 25/09/2026)

> ⚠️ **v1.4 (27/09):** dòng "Ngoài thực địa" bên dưới còn ghi *đo sáng tương đối bằng điện thoại* — v1.4 đo
> bằng **module BH1750 trên xe khảo sát** (vẫn **không** lắp gì lên cột hay lưới của xã). Controlled
> Reference Capture Set **rời deliverable** ở v1.4; giá trị `calibration_rig` **giữ** trong enum (D-R25).
> Luật tách `data_source` giữ nguyên. Bảng giữ nguyên tới khi D-R21/D-R25 chốt.

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
reference trên testbed. ⚠️ **Ở v1.4 lux BH1750 là ĐẦU VÀO của phân loại `dim`, không còn là ground truth
của nó** — ground truth `dim` mới chưa chốt (**D-R23, ưu tiên cao**). Bốn quyết định từng dựa trên FO-01 **chưa sửa code**, mỗi cái là một follow-up
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
node_role      : segment_controller                  # 28/09 — sampled_fixture đã bỏ (I-1)
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
> ⚠️ **Mô hình IoT (28/09/2026, `docs/contract-drift.md` → "BE-14 / IoT"; hiện thực BE-14b).** Thiết bị chỉ ở **tủ điện tổng**, không IoT trên từng cột, không `battery_pct`. Thiết bị nối tới đèn qua **rơ-le gộp vào feeder** (`feeder_control`, 0/1 thiết bị mỗi feeder), không có bảng thiết bị ↔ cột. `has_iot_node` luôn `false`; `controller_node_ids[]`, `segment_ids[]`, `feeder_ids[]` tính lúc đọc. Contract §5.6 **chưa** cập nhật (drift I-18) — ticket IoT theo drift, không theo Contract hay mock cũ.

- `runtime_decline` **chỉ** đến từ IoT. `lamp_dim` và `lamp_out` **chỉ** đến từ CV (v1.4: `lamp_dim` do CV **và** cảm biến BH1750 cùng quyết — `source_channel` giữ `cv`? **D-R20, chờ quyết**). Một cột có thể mang **cả hai cùng lúc** — mô hình dữ liệu phải cho phép.
- Luồng `fault_status` hợp lệ: `detected → confirmed | rejected`, rồi `confirmed → in_progress → resolved → verified`. Chuyển sai luồng → **409** để FE disable nút trước, không để user bấm rồi mới lỗi (BE-19).

---

## Endpoint đã đặc tả — không tự thiết kế lại

> **Bốn endpoint bản đồ nằm dưới `/api/v1/map/` (drift MAP-1, 02/10/2026)**, không phải `/poles`, `/segments`,
> `/iot-nodes` như Contract v1.7 ghi — SELF-SIGNED, chờ FW. Không client nào gọi đường cũ lúc đổi.

| Endpoint | Ràng buộc |
|---|---|
| `GET /map/poles` | `bbox` **bắt buộc**, không có endpoint "lấy tất cả". Lọc: `status`, `power_source`, `segment_id`, `commune_id`, `has_open_fault`. Quá 2000 cột → **413** `BBOX_TOO_LARGE`. |
| `GET /map/poles/{id}` | Trả **đủ trong 1 request** (BE-20, hình dạng thật ở `docs/contract-drift.md` → BE-20): `fixture` (null nếu không có bóng đang dùng), `current_status` (`unknown` khi chưa có sweep công bố), `iot_node` (**luôn null**), `luminance_baseline` + `luminance_baselines[]` (theo chiều đi), `luminance_history[]` (≤30 điểm, cũ → mới), `runtime_history[]` (**luôn rỗng**), `open_faults[]`, `recent_frames[]` (chỉ frame người gọi mở được). Ngoài phạm vi xã → 404. |
| `GET /map/segments` | `bbox` bắt buộc. `FeatureCollection` của `LineString`. |
| `GET /faults` | **Phân trang JSON, KHÔNG phải GeoJSON.** Mỗi item có `location{lat,lng}`. Sắp mặc định `-priority_score`. |
| `PATCH /faults/{id}` | Body: `fault_status`, `override_fault_type?`, `note?`. |
| `POST /faults` | **BE-41.** Chỉ Kỹ sư hiện trường (`ReportFaults`). Có cột → xã/tuyến/**`data_source` lấy từ cột** (không áp cứng `field`); không cột → bắt buộc `location`. `client_op_id` gửi lại → 200 cùng sự cố. `detected_at?` cho hàng chờ offline. **Không có `photo_frame_id`**: ảnh gửi sau qua `POST /faults/{id}/photos` (EV-2) — cùng bảng `repair_evidence`, một cha (phiếu **hoặc** sự cố). |
| `GET/POST/PATCH /work-orders` | `POST` body: `{title, fault_ids[], assigned_to?, due_date?}` |
| `GET /work-orders/{id}/poles` | **WO-12.** Cột trên đoạn được giao (hợp cột mang fault của phiếu) kèm trạng thái đèn **lần khảo sát đã duyệt gần nhất**, theo thứ tự dọc đường — kỹ sư biết TRƯỚC khi đi. Quyền xem = quyền xem phiếu (ngoài phạm vi / không phải người được giao → 404); cột xã ngoài phạm vi trên đường liên xã không hiện. `position` là thứ tự, không phải khoảng cách. |
| `POST /work-orders/{id}/evidence` | multipart: `file`, `kind`, `captured_at`, `lat`, `lng`, `client_op_id?` (BE-24). `kind`: **`before`/`after` chỉ ở phiếu sửa chữa, `observation` chỉ ở phiếu kiểm tra** (EV-1). Chỉ người được giao, phiếu `in_progress`. Phiếu sửa chữa **không `complete` được khi chưa có ảnh `after`** (409 `AFTER_EVIDENCE_REQUIRED`). Xem: `GET /work-orders/{id}/evidence`, `GET /evidence/{id}/thumbnail\|original` (proxy, quyền theo phiếu cha). |
| `GET /map/iot-nodes` | `bbox`, trả `FeatureCollection` |
| `GET /sweeps` | `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct, processing_status` |
| `GET /sync/bundle` | **BE-43, Contract v1.13 §5.8.** Chỉ `field_engineer` (`SyncOffline`). `segment_id` lặp ≤ 20, vắng = tuyến của phiếu mở của tôi. **Bản chụp đầy đủ, không `since`** (gửi → 400). Cột = properties bản đồ + `note`; phiếu dạng chi tiết |
| `POST /sync/push` | **BE-43.** 5 `op_type` qua **service của endpoint gốc**, kiểm capability **từng thao tác**. `applied` / `conflicts` (server thắng, kèm `server_state`) / `rejected`. Khử trùng lặp: cột `client_op_id` sẵn có, hoặc bảng `sync_operation` |

### `properties` của `GET /map/poles`

```
pole_id, segment_id, fixture_status, status_confidence (0..1|null),
power_source, fixture_type, lamp_watt, install_date, warranty_expiry,
commune_id, last_seen_at, last_sweep_id, open_fault_count,
has_iot_node, near_sensitive_poi
```

### `properties` của `GET /map/segments`

```
segment_id, segment_name, road_class, length_m, pole_count,
controller_node_ids[], has_active_segment_fault      # 28/09: danh sách tính lúc đọc (I-7b)
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
| ~~BE-12~~ | ~~CRUD tài sản + import CSV~~ → **BE-12a đã đặc tả và hiện thực**; hình dạng response khi ĐỌC là **BE-12b**, đã hiện thực ở Contract §5.3.1 (SELF-SIGNED, chờ FW xác nhận) |
| BE-15, BE-16 | Upload sweep, validate metadata phơi sáng |
| BE-27 | Notification — **chốt tên bảng/entity cùng FE2 trước W16** |
| BE-29, BE-30 | Dashboard còn lại (BE-28 **đã hiện thực** 07/10/2026 — `/statistics/…`, drift BE-28 ST-1…ST-10, SELF-SIGNED; BE-30 thêm `group_by` không đổi hình dạng; **BE-31 đã hiện thực** 12/10/2026 — `warranty_status`, `/statistics/warranty`, `format=csv`, drift W-1…W-8) |
| BE-33→35 | Quản trị danh mục, node, model version |
| ~~BE-41~~ | ~~`POST /faults`~~ → **đã đặc tả ở mục 2.8** |
| ~~BE-42~~ | ~~endpoint lux~~ → **đã đặc tả ở mục 2.9** |

BE-41 và BE-42 đã được đặc tả ở bản hợp nhất (mục 2.8 và 2.9). **BE-12a** được đặc tả riêng ngoài
Contract và đã hiện thực — nhóm endpoint `/assets/…`, xem mục BE-12a bên dưới; **BE-12b** (hình dạng
response khi đọc một tài sản) đã hiện thực ở Contract §5.3.1; quyết định SELF-SIGNED còn chờ FW xác nhận. Phần còn lại trong bảng vẫn chưa có đặc
tả — cần thống nhất trước khi hiện thực, đừng tự sinh endpoint rồi coi như xong.

Còn để mở: vector tile khi vượt ~5000 cột, realtime khi sweep xong (giai đoạn 1 dùng polling). Phân quyền theo `commune_id` **đã chốt ở mục 7**, không còn để mở.

**Lưu trữ đã chốt (D-R16, Contract §3.3):** ảnh, telemetry, fault giữ **tối thiểu qua hết bảo hành** của
tài sản; hết bảo hành **không** xoá / archive gì — chỉ gắn **cảnh báo "hết bảo hành"**, tính từ
`fixture.warranty_expiry` của bóng đang dùng so với hôm nay, **không lưu thành cột trạng thái** (hiện ở bản
đồ, danh sách tài sản, fault / work order). ⚠️ Ngày bảo hành nằm trên **`fixture`**, không trên `pole`, và
**nullable** — "không biết hạn bảo hành" phải hiện khác "còn bảo hành". Hiện thực: **BE-31 xong** (`warranty_status`, drift W-1…W-8); retention còn BE-35.

---

## Domain model

`Pole` · `Fixture` · `RoadSegment` · `Feeder` · `ElectricalCabinet` · `IotNode` · `TelemetryReading` ·
`SurveySweep` · `SurveyFrame` · `Detection` · `LuminanceBaseline` · `LuxReading` ·
`Fault` · `FaultCluster` · `WorkOrder` · `RepairEvidence` ·
`AdministrativeUnit` · `AppUser` · `RefreshToken`

> **Lược đồ đích theo Phiếu v1.4 — [`docs/database/erd.md`](docs/database/erd.md) (OPS-SCHEMA, 01/10/2026).**
> 17 bảng hiện có giữ nguyên; 22 bảng mới vào qua migration của ticket sở hữu, theo các quyết định Q1–Q17 ở
> đó. `ExternalUnit` đã **bỏ** (Q13 — Phiếu v1.4 chỉ giao việc cho Kỹ sư hiện trường). Ticket tạo bảng mới
> phải đọc file đó trước; đổi một quyết định Q-x thì ghi lại, đừng lặng lẽ thiết kế khác.

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

Dòng release OSS đứng yên ở `RELEASE.2025-09-07` ⇒ đây là **hoãn, không phải giải**. ⚠️ **quay.io nay
cũng trả 401** cho kéo ẩn danh (26/09, kiểm lại 11/10/2026) — chỉ máy còn image trong cache mới chạy được
MinIO. Test S3 thật (tách khỏi BE-36, quyết định D-7) phải chọn nguồn image mới trước.

**Hai ràng buộc về ImageSharp — kiểm chứng bằng test, không phải giả định:**

- **ImageSharp GIỮ EXIF qua resize** (trái với giả định thông thường). Việc bỏ metadata khỏi
  thumbnail phải **TƯỜNG MINH**: `ExifProfile` / `XmpProfile` / `IptcProfile` = `null`. Không dựa vào
  hành vi mặc định. Lý do là **bảo mật, không phải dung lượng**: thumbnail là object phục vụ rộng
  nhất, GPS chụp là trường nhạy cảm nhất — để nguyên là đặt dữ liệu nhạy nhất vào chỗ ít bảo vệ nhất,
  trong một hệ thống phân quyền theo địa bàn. Canh bằng
  `The_thumbnail_carries_no_gps_so_a_widely_served_object_cannot_leak_capture_locations`; test đó
  **không phải chuyện dọn dẹp metadata**, đừng xoá khi refactor.

- 🔴 **Năm advisory của ImageSharp 3.1.12 đang được TẮT theo mã (08/10/2026, `Directory.Build.props`)** — bản vá chỉ có ở
  4.1.2, mà 4.x cần license key (gạch dưới). Chấp nhận được **CHỈ VÌ** mọi lần decode đi qua cấu hình chỉ-JPEG
  (`ThumbnailFactory.JpegOnly`, sau kiểm `FF D8 FF`): TIFF / histogram / ICC không có đường tới. `Image.Load(stream)` không kèm
  `DecoderOptions` dùng `Configuration.Default` (mọi codec) và **phá đúng tiền đề đó** — `JpegOnlyDecodeTests` đỏ. Lên 4.1.2 thì
  gỡ khối suppress.
- **Ghim ImageSharp ở 3.x.** Từ 4.x, task validate lúc build đòi `SixLaborsLicenseKey`, và
  `ContinueOnError="$(Configuration.StartsWith('Debug'))"` nghĩa là Debug chỉ cảnh báo còn
  **Release/CI/deploy GÃY**. Điều khoản Split License không đổi; chỉ khác cái cổng kiểm key. Muốn lên
  4.x phải **xin key TRƯỚC**. Đã đưa vào FW-00.

**Nợ có tên người đòi:** `S3ObjectStore` chỉ được test trên **client S3 giả** (`MissingObjectTests`
dựng nó trên `FakeS3`; `SurveyIngestTests` cũng vậy) — **chưa có test nào với MinIO thật**. Đã kiểm
end-to-end thủ công một lần lúc làm BE-11 (8.2 KiB gốc + 1.8 KiB thumbnail vào `luxmap-survey`, SHA-256
vòng tròn khớp) nhưng **không có gì canh đường thật từ đó trở đi**. BE-36 (11/10/2026) **tách** món này
thành ticket riêng (D-7) vì chưa có nguồn image MinIO kéo được. Đừng để nợ này chỉ nằm trong báo cáo
một phiên làm việc.

### Bốn quy tắc chốt ở BE-42 — số đo lux

> 🔴 **Nền đã đổi ở Phiếu v1.4 (27/09/2026) — bốn quy tắc dưới đây mô tả code HIỆN CÓ, chưa phải hướng
> đi.** Quy tắc 1 dựa trên tiền đề *lux là ground truth chấm CV*; ở v1.4 đỉnh lux theo cột **chính là**
> tín hiệu `dim`, nên chuỗi luminance sẽ lấy từ lux (D-R22, D-R23). `lux_value` "cảm biến điện thoại"
> (D-R15) thành module BH1750 trên xe, lấy mẫu theo sweep — D-R21 **chốt hướng (B)** (SELF-SIGNED): chuỗi mẫu thô theo sweep + đỉnh theo cột vào chuỗi luminance; `LuxReading` giữ cho đo **thủ công**. Đặc tả BE-15 chưa viết; upload video (D-R28) còn chặn.
> Quy tắc 2 (`commune_id` server tra) và 4 (`Restrict`) **giữ nguyên** dù chọn hướng nào. XML doc của
> `LuxReading.cs` còn ghi *"ABSOLUTE `lux_value`"* — lệch cả D-R15, sửa ở Phase 2.

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
| Teardown của test (13 chỗ) | Xoá hàng loạt là cách duy nhất dọn được dưới scope rỗng. **BE-36 KHÔNG xoá nhu cầu**: cả lượt chạy dùng chung một DB, class sau vẫn thấy dữ liệu class trước. Chỉ gỡ được khi mỗi collection có DB riêng. |

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

**Enum JSON cũng phải chặn số lúc đọc:** giữ `allowIntegerValues: false` ở `LuxMapJsonOptions` để từ chối
cả `2` lẫn `"2"` (kể cả enum nullable), trả 400 `VALIDATION_FAILED` trước tầng ghi. Canh bằng
`JsonEnumHandlingTests` và `JsonPipelineConventionTests`; query string vẫn đi qua `WireEnum`, không qua converter JSON.

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

**BE-36 (11/10/2026) chỉ xoá NỬA giữa các lượt của lớp lỗi này** — mỗi lượt một DB sạch, nên cặn và
sequence trôi từ lượt trước không còn. **Nửa trong một lượt vẫn còn nguyên:** mọi class vẫn chung một DB
và chung `pole_id_seq`, nên hai quy tắc trên vẫn bắt buộc và **đừng tách collection**.

### Hai bẫy test chốt ở REG-v1.2 — culture và tài khoản seed

**SQL có số thực phải độc lập culture.** Ưu tiên truyền giá trị bằng SQL parameters. Khi test cần
sinh chuỗi SQL literal, dùng `string.Create(CultureInfo.InvariantCulture, $"…")` như
`MapEndpointTests.PlantPolesAsync`. Culture `en_VN` từng biến `108.5` thành `108,5`, khiến
`ST_MakePoint` nhận thêm đối số và PostGIS báo `22023: Geometry has Z dimension`. Đổi locale của
máy chỉ che lỗi; kiểm tra phải chạy được với locale dấu phẩy.

**Test đổi trạng thái tài khoản phải tự tạo tài khoản riêng và dọn trong `finally`.** Các tài khoản
seed được nhiều collection dùng để đăng nhập. Khoá, đổi role, password hoặc scope của chúng sẽ
ảnh hưởng test khác dù có khôi phục ở teardown. `AuthEndpointTests` từng khoá `crew` trong vài ms,
khiến `RoleCapabilityMatrixTests` ngẫu nhiên nhận 403 ngay ở login. Dùng mẫu
`CreateThrowawayAccountAsync` / `DeleteAccountAsync` trong `AuthEndpointTests`; chỉ sửa tài khoản
thuộc test đó, giữ tài khoản seed dùng chung ổn định.

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
và phân trang không ổn định**. Fault CV-16 chưa chấm phải nằm **cuối**, không phải đầu.

> 🔴 **`ix_fault_priority_score` trên DB là `DESC` TRẦN, không phải `DESC NULLS LAST`** (đo 29/09/2026:
> `pg_indexes` → `btree (priority_score DESC)`). Postgres xếp NULL **lên đầu** với `DESC`, nên index
> này không phục vụ thứ tự NULL-cuối. `IsDescending()` của EF không sinh `NULLS LAST`; câu cũ ở đây
> và comment trong `FaultConfigurations` đã nói sai. **Truy vấn phải tự ép NULL xuống cuối**
> (`OrderBy(x => x.PriorityScore == null)` trước) — BE-40 làm vậy, canh bằng sabotage trong
> `FaultListTests`. Muốn index phục vụ được thì cần migration riêng (BE-32), đừng tưởng đã có.

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

   ⚠️ **Hình dạng cũng phải chặn, không chỉ múi giờ.** Binder của framework đọc ngày theo văn hoá bất biến (kiểu Mỹ), nên
   `06/10/2026` thành **10/06**, trả 200, không lỗi (gặp ở BE-25 `night_of`; có sẵn ở `scheduled_from/to`, lux `from/to`).
   `IsoDateQueryModelBinderProvider` (`LuxMap.Api/Http`) chặn mọi `DateOnly`/`DateTime` trên query string không đúng ISO → 400,
   rồi giao lại binder cũ nên nghĩa không đổi. Tham số ngày mới **không cần làm gì** — chỉ đừng nhận ngày dưới dạng `string`
   rồi tự `DateTime.Parse`, như vậy là đi vòng qua chốt chặn. Canh bằng `A_bound_that_is_not_iso_8601_is_a_400_never_a_guess`.

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
`AuthTestFactory` chưa — từ BE-36 token đó chết cùng container cuối lượt, nhưng trong một lượt vẫn tích lại.

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
policy đang serialize chúng ra, nên giá trị client đọc được chính là giá trị nó gửi lại được. Dùng
**`WireEnum.ParseCsv` / `WireEnum.Name`** ở `LuxMap.Shared/Http` (chuyển từ `MapController` ở BE-40,
cùng `BoundingBox`, vì Faults không tham chiếu được Map). Đừng chép bản thứ ba.

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

Bốn vai trò đăng nhập theo Phiếu v1.2 mục 3.2.c — **không đổi ở v1.4** (Contract v1.7 §2, D-R6):

| Vai trò | `user_role` | Thay cho (≤ v1.6) | Tài khoản demo |
|---|---|---|---|
| **Cấp giám sát** (Superior) — bản đồ, thống kê, báo cáo; **chỉ đọc** | `superior` | `management_agency` | `agency` |
| **Quản lý** (Manager) — tài sản, giao việc khảo sát / sửa chữa, duyệt sự cố, điều khiển đèn testbed | `manager` | `maintenance_engineer` | `engineer` |
| **Kỹ sư hiện trường** (Field Engineer) — khảo sát đêm, kiểm tra, sửa chữa (Tổ khảo sát đã gộp vào đây) | `field_engineer` | `field_crew` | `crew` |
| **Quản trị hệ thống** (System Admin) — tài khoản, vai trò, cấu hình, giám sát, log | `system_admin` | `administrator` | `admin` |

Phân quyền theo vai trò (ma trận capability, mục BE-12a quy tắc 4) **và** theo địa bàn. Cấp giám sát
xem nhiều xã bằng **danh sách xã được gán**, không có cấp huyện (D-R3). Username seed và biến
`SEED_*_PASSWORD` **giữ tên cũ** — chúng là định danh, không phải nhãn (D-R6).

**Không có tự đăng ký (D-R11).** Quản trị hệ thống tạo tài khoản, gán vai trò và xã qua `/admin/users`; người
dùng nhận **email mời** và tự đặt mật khẩu (BE-33a, Contract v1.8 §4.8–4.9). `POST /auth/register` **đã gỡ**.

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
- **Precision / recall báo RIÊNG cho `out` và `dim`** (NFR phiếu v1.2; v1.4 tách thành P/R/F1 cho **ON/OFF** của CV cộng đối chiếu Normal / Dim / Out với **field verification** — vẫn báo riêng từng lớp). `dim` là lớp khó và đáng giá; một con số gộp sẽ giấu nó — cùng họ lỗi với gộp `unknown` vào `out`.
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
| **BE-42 `LuxReading`** | FM-14, FO-14 (đo lux W5), CV-12 | **W4 — đã đẩy sớm, đừng trễ** · ⚠️ v1.4: FO-14 đổi sang BH1750 trên xe, việc thật sự cần sớm là ingest mẫu theo sweep (D-R21, D-R27) |
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

> ✅ **`external_ref` — CHỐT 03/10/2026 (Mỹ, D-R10 đóng): mã VĨNH VIỄN do nhóm quản lý; mã kiểm kê của xã đi cột RIÊNG.**
>
> - Cột thực địa giữ mã hiện có `KS-<ngày>-<giờ chụp>` (114 cột Long Phước / Long Bình), tuyến giữ `osm-…`; bộ mock giữ
>   `pole_id` / `segment_id` của mock. **Không đổi tên, không ghi đè** `external_ref` sau khi đã nạp.
> - Mã kiểm kê của xã (nếu xã giao, ví dụ số sơn trên cột) vào cột **`inventory_code`** riêng — nullable, unique trong mỗi xã —
>   thêm bằng migration **khi xã thật sự giao**, ánh xạ bằng vị trí + người kiểm. **Không** dùng nó làm khoá import.
> - 🔴 **Bẫy còn mở:** `KS-…` sinh từ **giờ chụp ảnh**, tức là mã của **lần chụp**, không phải của cột. Khảo sát lại bằng
>   `scripts/photos_to_poles.py` sẽ cấp mã MỚI cho cột cũ ⇒ import tạo **cột trùng**. `--existing` hiện chỉ báo trong review,
>   **chưa** dùng lại mã. Phải sửa công cụ (dùng lại `external_ref` của cột cũ khi vị trí khớp, ca mơ hồ vào review) **trước
>   lần khảo sát ảnh thứ hai**.
> - Đường "xoá sạch nạp lại" cho cột thực địa còn mở tới khi **phiên khảo sát thực địa đầu tiên được xử lý** (`pole_observation`
>   trỏ vào `pole` bằng `Restrict`); quyết định này làm cho đường đó không còn cần thiết.
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

### CI: cấu hình host chỉ đặt ở bước cần dựng host

`Cors__AllowedOrigins__0` dùng cho export OpenAPI và migrate/seed phải ở `env` của từng step. Đặt ở cấp job làm `CorsTests.Startup_stops_on_a_missing_or_malformed_allowlist(null)` khởi động thành công vì allowlist từ môi trường, dù test đang kiểm cấu hình thiếu. Giữ môi trường regression test độc lập với cấu hình của CLI (PR #52, 27/09/2026).


### BE-23a — audit chung, append-only và giới hạn thật (27/09/2026)

`LuxMap.Persistence/Audit/` chứa `AuditEvent`, `IAuditTrail` và `IAudited`.
`Record` chỉ stage event vào **cùng DbContext scoped**; service chụp actor, vai trò, thời điểm,
before/after rồi gọi đúng một `SaveChanges` cùng thay đổi nghiệp vụ. Không save riêng audit.
Guard ở cả sync/async từ chối sửa/xoá audit (kể cả backdoor); khi có thay đổi `IAudited`,
bắt buộc đúng một audit mới. Backdoor chỉ bỏ yêu cầu có event cho seed/fixture.
Guard đếm event, không chứng minh nội dung event khớp thao tác: service và test nghiệp vụ phải kiểm điều đó.
BE-23 sẽ gắn marker cho WorkOrder/WorkOrderFault; Fault đợi BE-19.

Trigger DB chặn UPDATE/DELETE/TRUNCATE với SQLSTATE `55000`.
**Append-only chặn lỗi ứng dụng, KHÔNG chặn người có quyền SQL.**
`luxmap.audit_purge` là custom GUC, không phải quyền bảo mật; user có SQL có thể đặt nó,
và tài khoản `luxmap` local hiện là superuser. Không mô tả cơ chế này như chống giả mạo tuyệt đối.
Chỉ teardown test được `SET LOCAL luxmap.audit_purge = 'on'` trong transaction riêng,
rồi DELETE với WHERE theo dữ liệu của test. Không đặt ở mức session/connection pool.
`AuditGuardTests.Production_source_never_enables_the_test_purge_switch` quét `src/` (trừ migration)
để chặn đường bật GUC trong ứng dụng; giữ test này cùng `BannedBulkWriteApiTests`.
FK actor và commune đều RESTRICT: muốn dọn tài khoản/xã test phải dọn audit của test trước.
Rollback migration xoá bảng audit và mất dữ liệu audit; vòng apply/rollback/reapply chỉ chạy
trên DB test, khi bảng audit trống.

### BE-23 — xmin, tên partial index và quyền người được giao (28/09/2026)

`IsRowVersion()` trên uint dùng cột hệ thống xmin. **EF vẫn sinh AddColumn xmin trên fault và
DropColumn trong Down**, dù Npgsql xử lý đặc biệt lúc sinh SQL: đọc migration và bỏ thao tác
cột vật lý, giữ mapping trong model/Designer/snapshot. Không tạo một cột xmin riêng.

`HasIndex(..., "tên")` đặt tên model index, **chưa bảo đảm tên DB** khi convention snake_case
chạy. Đã sinh `ix_work_order_fault_fault_id1` thay vì `ux_work_order_fault_fault_id_active`;
phải thêm `HasDatabaseName` vì service nhận diện 23505 bằng tên constraint thật.

WorkOrder gộp IAssigneeScoped vào cùng lambda commune, tham chiếu DbContext. WorkOrderFault
chỉ scope commune; BE-24/BE-43 phải lookup WO cha trước. Sau concurrency cũng lookup lại có
filter: reassign vừa xảy ra có thể biến WO thành 404 với người được giao cũ.

Fault.CommuneId nay là alternate-key property để FK bảng nối bảo đảm cùng xã; giống Feeder,
EF không cho đổi commune trên entity đã track. Fault không chuyển xã.

**Timestamp app sinh rồi trả thẳng từ entity đang track phải cắt về micro giây trước khi lưu.**
Dùng `UtcMicrosecondClock.UtcNow()` (Shared), giữ cùng một `now` cho entity, fault và audit.
PostgreSQL `timestamptz` chỉ lưu µs; `DateTime.UtcNow` trên Linux có tick 100 ns nên response
có thể khác DB dù local macOS (đồng hồ µs) xanh. PR #54 đã lộ sai lệch completed_at/resolved_at.
Test phải cấp thời gian cố định có tick lẻ dưới µs qua TimeProvider, không chỉ thử đồng hồ thật.

### BE-40 — danh sách sự cố (29/09/2026)

**Faults KHÔNG tham chiếu được WorkOrders** — chiều ngược đã có (WorkOrders gọi `FaultTransitions`).
Câu hỏi "fault nào đang nằm trong phiếu nào" đi qua port **`IActiveWorkOrderLookup`** khai ở Faults,
WorkOrders hiện thực. **BE-19 dùng lại đúng port này** để trả 409 khi fault thuộc phiếu sửa chữa đang
chạy (WO-10) — đừng thêm project reference, và đừng chuyển endpoint fault sang WorkOrders.

Port đọc `WorkOrderFault` (chỉ scope theo xã), **không** đọc `WorkOrder` (scope theo cả người được
giao), nên ID trả về cả với phiếu người gọi không mở được — đó là quyết định F-3, không phải rò.

`location` của fault = `lat/lng` của fault, thiếu thì điểm của cột — **cùng luật** với chi tiết WO.
CHECK `ck_fault_pole_or_location` cho phép fault có cột mà không toạ độ, nên chỉ đọc `fault.lat` là
trả `null` ở chỗ Contract bắt buộc có giá trị.

### FR-2 — chuỗi phiếu công việc (29/09/2026)

**`root_work_order_id` NULL ở lịch đầu, KHÔNG trỏ vào chính nó.** API tính `case_id = root ?? work_order_id`.
Trỏ vào chính nó thì mọi chỗ đang ghi phiếu (seed `seed_mock_set.py`, `Plant` trong test, fixture của
BE-40) phải biết cột này, và EF phải chèn một hàng tham chiếu chính nó. Lọc chuỗi bằng
`work_order_id = X OR root_work_order_id = X`. CHECK `ck_work_order_chain_complete` bắt cha và gốc
null **cùng nhau**; FK ghép `(…, commune_id)` giữ cả chuỗi trong một xã. Tính nhất quán
`root = cha.root ?? cha.id` **chỉ canh ở service** — không ràng buộc DB nào so hai hàng.

**Nghiệm thu kiểm tra ĐỔI trạng thái sự cố (FR-2a).** `fault_present` + `detected` → `confirmed`, làm
trong cùng `verify`, cùng audit. Đây là ngoại lệ thứ hai của "chỉ repair lan truyền" (WO-2). BE-19 phải
biết: sự cố `confirmed` có thể có `confirmed_by` là người nghiệm thu một phiếu, không phải người gọi
`PATCH /faults`.

**Khảo sát → kiểm tra (WO-SURVEY-CHAIN, drift WSC-1…WSC-5).** Phiếu khảo sát **không bao giờ** có `work_order_fault`: tập
mang sang tính lúc gọi `follow-up`, qua port **`ISurveyFindingLookup`** (Survey hiện thực — Survey tham chiếu WorkOrders, không
ngược lại). Chọn kết quả **mới nhất** của từng cột rồi mới xét `out`/`dim`; lọc ngược lại thì một `out` cũ sống lại sau `normal`.
Nhánh khảo sát truyền `NewWorkOrder.Keep`: `Insert` lọc lại **sau** `FaultLocks` và **bỏ** sự cố vừa bị phiếu khác lấy thay vì 409.
Đừng "sửa" thành tạo sự cố lúc `verify`: duyệt sweep đã sinh sự cố (`SurveyReviewService.Publish`).

### BE-19 — duyệt sự cố, audit và khoá hàng (29/09/2026)

**`Fault` là `IAudited`.** Mọi đường ghi sự cố (BE-41 báo tại chỗ, ingest CV/IoT, IOT-11) phải thêm
**đúng một** audit trong cùng `SaveChanges` — engine ghi actor `cv`/`iot`. Seeder và fixture đi qua
`EnterUnscopedSystemWriteBackdoor()`. ⚠️ Guard audit chạy **trước** guard xã (`LuxMapDbContext`): test
muốn chứng minh guard xã trên sự cố phải kèm một audit hợp lệ, nếu không nó đỏ vì lý do khác
(`FaultSchemaTests.WriteAsync`).

**Khoá hàng `fault` trước khi kiểm (`FaultLocks`).** Tạo phiếu **đọc** trạng thái sự cố rồi chèn liên
kết mà không ghi vào `fault`, nên `xmin` không thấy race với `PATCH /faults`. Cả hai phía mở transaction,
`SELECT … FOR UPDATE` theo thứ tự `fault_id` (thứ tự khoá, **không** phải thứ tự hiển thị), rồi mới đọc
lại. Hệ quả: hai lần tạo phiếu trên cùng sự cố nay **tuần tự**; unique index
`ux_work_order_fault_fault_id_active` còn là lớp chặn cuối, không còn là lớp trả lời. Trong transaction,
bắt lỗi xong phải **rollback trước** mọi truy vấn khác (`WorkOrderService.Save`).

**`fault_type` là loại kênh báo, không bao giờ sửa.** Phân loại lại đi vào `override_fault_type`; API dùng
`override ?? fault_type` cho cả trả về lẫn lọc. Lọc bằng hai nhánh, danh sách của nhánh override phải kiểu
`FaultType?` — EF không bind được một danh sách không-null vào cột nullable, cũng không dịch được
`Contains(override ?? type)`.

⚠️ **`Down()` của `AddFaultReview` gãy khi đã có audit `entity_type = 'fault'`** — nó dựng lại CHECK cũ
chỉ nhận `work_order`, mà bảng audit không cho xoá. Rollback migration này chỉ chạy được trên DB chưa có
quyết định sự cố nào (cùng họ lưu ý của BE-23a).

**Test chuyển trạng thái phải thử chuyển HỢP LỆ ở §3.2 nhưng cấm ở `PATCH`** (`confirmed → in_progress`).
Chuyển mà máy trạng thái vốn cấm thì 409 dù có luật riêng của `PATCH` hay không — sabotage gỡ luật đó đã
xanh cho tới khi thêm cặp này.

### Enum lưu dạng CHỮ: không bao giờ `ORDER BY` thẳng theo cột (29/09/2026)

`HasContractEnum` lưu enum thành **text** (đúng giá trị trên dây), nên `OrderBy(x => x.Severity)` sắp theo
**bảng chữ cái**: `critical, high, low, medium` — sai mà trông như có thứ tự. Cùng họ với bẫy
`ORDER BY pole_id` ở mục 0. Muốn sắp theo mức thì dùng **hạng tường minh** dạng `Expression` để EF sinh
`CASE` (`FaultQueryService.SeverityRank`); một hàm C# gọi trong `OrderBy` thì EF **không dịch được**.
Test phải trồng các giá trị theo thứ tự mà sắp chữ cái sẽ lộ sai (`Default_order_is_severity_by_rank_…`).

### BE-14b — thiết bị IoT ở tủ điện (28/09/2026)

**Schema và quyền ghi tách nhau như `pole_current_status`.** `feeder_control` là bảng riêng khoá theo
`feeder_id`, KHÔNG phải cột trên `feeder`: chế độ ON/OFF/AUTO do thiết bị báo, còn `PUT
/assets/feeders/{id}` là thay thế toàn phần và sẽ xoá mất nó. Cả hai FK của `feeder_control` mang
`commune_id` (khuôn O-7), nên `ak_iot_node_node_id_commune_id` là đích FK — trông thừa, đừng xoá.

**`iot_node.data_source` siết hơn enum.** `ck_iot_node_data_source_not_field` chỉ nhận
`calibration_rig | simulated`: nhóm không lắp thiết bị ngoài thực địa (D-R10). Mở rộng CHECK này là
quyết định, không phải sửa lỗi.

**`node_status` không lưu.** `IotOptions.StatusAt` tính từ `last_report_at` và `Iot:OfflineAfter`
(mặc định 1 giờ); đúng bằng ngưỡng vẫn `online`. IOT-11 phải sinh `node_offline` từ **cùng**
`IotOptions`, không tự đặt ngưỡng thứ hai.

**Teardown của fixture phải xoá `feeder_control` rồi `iot_node` TRƯỚC `feeder` và xã** — cả hai
`Restrict` (cùng họ bẫy BE-14 số 3). `AssetSchemaFixture` và `AssetImportFixture` đã làm.

🔴 **`gen_consolidated_spec.py` có schema VIẾT TAY ĐÈ lên schema xuất từ code** (`S["PoleProperties"]`,
`S["SegmentProperties"]`…). Đổi một record `properties` trong code mà không sửa bản viết tay thì spec
hợp nhất vẫn ra hình dạng cũ, và CI không đỏ vì spec sinh lại vẫn khớp chính nó. Khi hiện thực một
stub `ni(...)`, xoá luôn stub **và** schema viết tay đi kèm (`S["IotNode*"]` đã xoá ở BE-14b).

**Mock `indent=1`.** Sửa mock bằng script phải round-trip từng byte trước khi đổi (đọc → ghi lại → so
bằng); đổi định dạng làm diff của WP5/WP6 thành vô nghĩa.

### BE-15 P2a — khoá hàng, upload dài và EF design-time (02/10/2026)

**Không khoá hàng bằng `FromSql("SELECT * … FOR UPDATE")` trên entity có row version `xmin`.**
`SELECT *` không trả cột hệ thống `xmin`, nên EF ném `42703: column xmin does not exist` → 500 ở
mọi lượt ghi. Khuôn đúng là `FaultLocks`: `ExecuteSql("SELECT 1 … FOR UPDATE")` rồi mới đọc bằng
LINQ, để đọc vẫn đi qua query filter xã và người được giao.

**Không giữ transaction hay khoá hàng qua một lượt stream upload.** Clip 100–300 MB qua 4G mất vài
phút; giữ `FOR UPDATE` trên `work_order` suốt lúc đó làm mọi thao tác của Quản lý trên phiếu treo.
Khuôn ở `SurveyIngestService.Clip`: kiểm dưới khoá → nhả → stream object (key mang hash) → khoá lại,
kiểm lại, ghi dòng. Object mồ côi khi thua race là chấp nhận được (BE-11 quy tắc 3). Canh bằng
`A_clip_in_flight_does_not_hold_the_work_order_lock`.

**Không thêm `IDesignTimeDbContextFactory`.** EF dùng host thật để dựng model lúc design-time; một
factory tự liệt kê assembly module sẽ âm thầm thiếu module mới, và `migrations add` sinh `DropTable`
cho bảng của module đó. Sinh migration trong môi trường không có `.env` thì đặt
`ConnectionStrings__LuxMap` giả cho lệnh đó, đừng đổi đường dựng model.

### BE-15 — ffprobe: lấy packet PTS, sắp thứ tự và giữ rotation (17/10/2026)

**Dùng `packet=pts,flags`, loại cờ discard `D` rồi sort tăng dần; không dùng `frame=pts` để probe toàn clip.** `frame=` buộc
ffprobe giải mã video: HEVC 148 s vượt timeout 60 s dù stdout đã nhỏ. Packet đến theo thứ tự giải mã,
**không phải thứ tự hiển thị khi có B-frame**; phải sort PTS trước khi kiểm clock/chọn frame/seek.
**MOV/MP4 có edit list có thể giữ packet tham chiếu ngoài đoạn hiển thị**, cả đầu lẫn đuôi clip.
Packet mang `D` không thành frame: lọc trước khi đọc PTS, kiểm clock và chọn ảnh; chỉ sort là chưa đủ.
Không suy từ FPS, không khử PTS trùng: với packet được hiển thị, thiếu PTS → `VIDEO_INVALID`,
trùng → `CLOCK_VIDEO_MAPPING`. Không khẳng định packet/frame tương đương cho mọi codec/container.
Rotation vẫn ở **`stream.side_data_list`**:
`stream=width,height,time_base:stream_side_data=rotation:packet=pts,flags -of json=compact=1`.
Không thêm `-show_frames` (bật mọi trường frame, vượt stdout 16 MiB) hay `-show_packets` (bật lại
mọi trường packet). Giữ trần stdout, timeout và quota hiện có. Dung lượng JSON còn phụ thuộc metadata
và độ dài PTS, không suy giới hạn số phút chung chỉ từ FPS. `DescribeAsync` băm assembly extractor
nên sửa lệnh tự đổi `artifact_version`, không cần tăng số version thủ công.
Canh bằng `Large_frame_probe_stays_bounded_and_extracts_the_last_frame` (video tự sinh, lệnh cũ vượt trần),
`B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images` (đối chiếu packet/frame PTS
và mã màu ảnh; bỏ sort phải đỏ), `Edit_list_discards_head_or_tail_packets_and_extracts_only_displayed_frames`
(edit list cắt đầu/đuôi, PTS và mã màu ảnh đầu/cuối; bỏ lọc discard phải đỏ), test thiếu/trùng PTS và VFR/rotation sẵn có.

### BE-15 P2c — FK ghép không giữ được hàng có cột null (04/10/2026)

**FK ghép `MATCH SIMPLE` bỏ qua hẳn hàng có BẤT KỲ cột nào null — kể cả các cột còn lại không null.**
`luminance_history` trỏ tới `pole_observation` và `luminance_baseline` bằng FK ghép có `pole_id`; hàng
`not_observed` có `observation_id` và `baseline_id` đều null nên **không FK nào kiểm `pole_id`**, và cột xoá được
dù lịch sử bất biến vẫn trỏ vào nó (review Codex sau merge #80). Đã thêm FK đơn `fk_luminance_history_pole_pole_id`
(`Restrict`). Bảng mới có FK ghép trên cột nullable phải tự hỏi: khi phần nullable là null, cái gì còn giữ các cột kia?
Test xoá phải đọc dependent bằng `AsNoTracking` — entity đang track làm EF tự từ chối ở client, test xanh mà DB không
hề được kiểm (`A_pole_with_only_not_observed_history_cannot_be_deleted`).

### BE-20 — chi tiết cột (04/10/2026)

**Một schemaId trong Swagger chỉ ứng một kiểu .NET — qua MỌI module.** `PoleDetail` đã là kiểu của BE-12b
(`Assets.Crud`), nên kiểu cùng tên ở `Map` làm `swagger tofile` ném `Can't use schemaId` — mà `dotnet build` và toàn bộ test
vẫn xanh, chỉ spec không xuất được. Kiểu của `/map/poles/{id}` mang tiền tố `PoleMap*`. Đặt tên response mới thì grep
tên đó trong cả `src/` trước.

**Cái gì được LIỆT KÊ phải mở được.** Danh sách frame của chi tiết cột và `GET /frames/{id}/thumbnail` dùng **cùng một**
`SurveyMediaAccess` (xã của cả run + người được giao của phiếu). Viết lại điều kiện ở chỗ thứ hai thì hai nơi sẽ lệch nhau và
client nhận URL mà mở ra 404 — hoặc tệ hơn, thấy frame của xã khác.

### Chép DB lên Supabase — bảng mới phải vào `PLAN` (04/10/2026)

`scripts/copy_dev_to_supabase.py` **dừng** khi gặp bảng không có trong `PLAN` — cố ý, để không bảng nào bị bỏ sót âm
thầm. **Ticket tạo bảng thì thêm bảng vào `PLAN` trong cùng PR**: bộ lọc giữ xã (bảng không có `commune_id` lọc theo cha), và
đúng thứ tự khoá ngoại (script tự kiểm). Bảng nằm trong **vòng** khoá ngoại thì khai vào `TWO_PASS` như `survey_sweep` ↔
`survey_processing_run`, không gỡ ràng buộc. Diễn tập theo `docs/deploy/supabase.md` (cờ `--source-db`, chỉ cho đích localhost).

### BE-33a — tài khoản do Quản trị tạo, mời qua email (05/10/2026)

**Phạm vi `*` ⇔ `system_admin` nay là CHECK của DB** (`ck_app_user_system_wide_scope_matches_role`). Test muốn chứng
minh lớp kiểm ở tầng phân quyền (`CommuneScopeConsistency`) phải **ký thẳng một JWT** lệch vai trò bằng
`AccessTokenIssuer`, không `UPDATE` cờ trên tài khoản (DB từ chối, và tài khoản seed là của chung). DB dev cũ có cặn
vi phạm thì chạy `scripts/cleanup_be12_leftover_accounts.sql` **trước** migration `AdminCreatedAccounts`.

**`password_hash` NULL ⇔ `password_set_at` NULL** (`ck_app_user_password_set_together`; NULL = đã mời, chưa đặt mật
khẩu). Mọi chỗ tạo `AppUser` có mật khẩu — seeder, fixture, test — phải đặt **cả hai**. Đăng nhập tài khoản chưa đặt
mật khẩu trả đúng `401 INVALID_CREDENTIALS` như sai mật khẩu.

**`user_id` rút từ sequence TRƯỚC khi chèn** (`UserAdminService.CreateAsync`, khuôn `FaultReportService.NextFaultId`),
để danh sách xã và link mời gắn mã thật trong cùng một `SaveChanges`. Khoá có tiền tố sinh bằng `DEFAULT` của DB, EF
không có giá trị tạm cho nó.

**Cấu hình mail bắt buộc lúc khởi động** (`SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM_ADDRESS`, `SMTP_FROM_NAME`,
`WEB_APP_BASE_URL`; `SMTP_USERNAME`/`SMTP_PASSWORD` đi cùng nhau). Dev trỏ Mailpit (`docker compose up -d mailpit`,
hộp thư ở `http://localhost:8025`); CI ghi các biến này vào `.env` tạm; test thay `IEmailSender` bằng
`RecordingEmailSender`. Có login thì **luôn TLS** (465 SSL, còn lại STARTTLS bắt buộc), không bao giờ "STARTTLS nếu có".
**Không log link hay token thô** — chỉ băm SHA-256 nằm trong `account_token`.

**`account_token` cascade từ `app_user`** — ngoại lệ có chủ đích của luật "mọi FK `Restrict`": nó là thông tin đăng
nhập tạm như `refresh_token`, không thuộc xã nào nên vùng mù cascade của guard `SaveChanges` không áp. Nó nằm trong
`SKIPPED` của `copy_dev_to_supabase.py` (link gắn với `WEB_APP_BASE_URL` của nguồn).

**Kiểm tra thu hồi chứng chỉ SMTP giữ mặc định CHẶT của MailKit — không soft-fail, không tắt (05/10/2026).** Trên
macOS, .NET trả `RevocationStatusUnknown` cho chứng chỉ của `smtp.gmail.com` (chỉ có CRL, không OCSP) nên gửi thư thất
bại; trên Linux cùng code, cùng mạng, cùng phút thì qua (đo trong container `runtime-deps:10.0`). Mỹ giao làm theo khuyến
nghị của Codex: **Mac dùng Mailpit, Gmail chỉ chạy từ Linux**. Chấp nhận "không xác định" (soft-fail) nghĩa là ai giữ
khoá của một chứng chỉ đã thu hồi mà chặn được đường tải CRL sẽ được cho qua — và kết nối đó mang mật khẩu ứng dụng
Gmail cùng link đặt mật khẩu. Đừng thêm `CheckCertificateRevocation = false` hay callback nới lỏng "cho chạy được".
Nếu máy chủ Linux đích cũng lỗi thì mới xét lại, và callback phải: chỉ dung thứ `RevocationStatusUnknown` /
`OfflineRevocation`, đòi `SslPolicyErrors` **bằng đúng** `RemoteCertificateChainErrors` (sai tên miền luôn bị từ chối),
kèm test cho từng cờ.

**Bộ giới hạn tần suất chia theo `RemoteIpAddress`.** Sau reverse proxy mọi client chung một địa chỉ (của proxy) ⇒
chung một xô 5 lần / 15 phút. Deploy sau proxy phải cấu hình forwarded headers **trước**.

**Đăng nhập có giới hạn tần suất (LOGIN-RATE, drift LG-1…LG-3), và mọi test đăng nhập từ cùng một địa chỉ.** `LuxMapTestHost`
nâng `RateLimits:Login:PermitLimit` lên 1.000.000 trong `CreateHost`; giá trị đặt ở hook đó **thắng `UseSetting`**, nên test muốn hạ
giới hạn phải override `LoginPermitLimit` (khuôn `LoginRateLimitTestFactory`), không `UseSetting` — làm vậy test vẫn chạy, chỉ không
bao giờ thấy 429. Endpoint đăng nhập mới phải gắn **cùng** policy `LuxMapRateLimits.Login`: policy riêng là xô riêng, kẻ dò mật khẩu
xen kẽ hai đường để được gấp đôi số lần.

**`ExceptionHandlingMiddleware.WriteAsync` gọi `Response.Clear()` — xoá luôn header đã đặt.** Lỗi nào cần header
riêng (`Retry-After` của 429) phải tự ghi body, như `RateLimitSetup.OnRejected`.

**`FakeTimeProvider` không lùi được** (`Advance` âm ném ngoại lệ). Đẩy đồng hồ host lên rồi không lùi làm **mọi access
token cấp sau đó chưa tới hạn hiệu lực** → 401 hàng loạt ở các test sau, trông như lỗi phân quyền. Muốn test hết hạn
thì kiểm thời hạn đã cấp rồi dời `expires_at` của chính bản ghi đó về quá khứ (`AccountAdminTests.ExpireAsync`).

**Test luật "Quản trị cuối cùng" chạy trong transaction rồi rollback** (khoá các admin khác chỉ trong transaction đó).
Đổi trạng thái admin thật trên DB dùng chung sẽ đua với các class test khác đang tạo admin (`WorkOrderTests`).

### POLE-NOTE — ghi chú cột (05/10/2026)

**Ghi chú đi qua bốn đường:** `PUT /assets/poles/{id}/note` (`EditPoleNotes`, kỹ sư + quản lý), form tạo
(`POST /assets/poles`, `note?`), form sửa (`PUT /assets/poles/{id}`) và import cột (cột `note` tuỳ chọn). Ở form sửa, `note`
là **trường duy nhất GIỮ NGUYÊN khi vắng** trong một `PUT` thay thế toàn phần (đọc bằng `PoleNoteInput.Read`, `JsonElement`):
đừng "sửa cho nhất quán" thành vắng = xoá — form cũ chưa có ô ghi chú sẽ xoá sạch ghi chú (cùng họ bẫy `feeder_id`). Ở import,
**ô trống cũng là giữ** — import không bao giờ xoá ghi chú; thay ghi chú khác nội dung thì `warnings[]` **trích nguyên văn cũ**,
vì không có lịch sử ghi chú nào khác để khôi phục. Canh bằng
`Replacing_the_pole_without_the_note_key_keeps_the_note` và
`The_import_overwrites_a_different_note_warning_with_the_old_text_and_never_clears_one`. Trường `JsonElement` mới trong request
phải thêm vào `JsonElementFieldSchemaFilter`, nếu không spec ra `{}` và FM-04 sinh `Any`.

**Ghi chú KHÔNG có tác giả riêng từ N-4 (06/10/2026).** Ai sửa — ghi chú hay bất cứ gì — là `updated_by` của cột.

### `updated_by` của tài sản — chỉ đóng dấu khi giá trị THẬT SỰ đổi (06/10/2026, drift N-4)

`pole`, `road_segment`, `feeder`, `fixture` implement `IUpdateStamped`; **mọi** đường ghi gọi `AssetStamp.Touch` **sau khi**
gán giá trị (sau `Add` với hàng mới). `Touch` đọc `Entry().State` — `Entry()` chạy dò thay đổi cho đúng entity đó — nên gán
lại giá trị bằng giá trị đang lưu thì hàng vẫn `Unchanged` và **không** đóng dấu. Hệ quả cho code mới:

- 🔴 **Không gán `UpdatedAt` trực tiếp** trên bốn bảng này. Chính phép gán đó làm hàng thành `Modified`, phá luật "nạp lại file
  y hệt không đổi gì" — và test so `updated_at` mới bắt được, test so dữ liệu thì không.
- Geometry đọc lại từ WKT là object mới mỗi lần, nhưng comparer của NTS so theo giá trị nên vẫn `Unchanged` — đã kiểm bằng
  test (`Re_importing_an_identical_file_changes_nothing_and_a_changed_row_names_the_importer`), đừng thêm so sánh tay.
- Import đếm `updated` / `unchanged` theo **giá trị trả về** của `Touch`, không đếm theo "khớp `external_ref`".
- `updated_by` là FK `Restrict` tới `app_user` trên **cả bốn bảng**: fixture test tạo tài khoản rồi để tài khoản đó ghi tài
  sản phải xoá **tài sản trước, tài khoản sau** (bài học PR #93). Script `copy_dev_to_supabase.py` chỉ chép tài khoản seed, nên
  hàng có `updated_by` là tài khoản khác sẽ vấp FK — cùng giới hạn đã có với `reported_by` / `created_by`.
- Chỉ người **gần nhất**. Cần lịch sử lượt nạp thì là bảng riêng (`asset_import`), không phải nới cột này.

### BE-27 — thông báo (05/10/2026)

**Thông báo được STAGE trên DbContext của người gọi, không bao giờ tự lưu** (`Notifier.Stage`, module
`LuxMap.Modules.Notifications`). Nó đi chung `SaveChanges` với thay đổi nghiệp vụ, nên thao tác thua tranh chấp hay
rollback không để lại thông báo (canh bằng `NotificationTests.The_losing_side_of_a_race_leaves_no_notice`). Đừng gọi
`SaveChanges` riêng cho thông báo, và đừng inject một service scoped giữ context của request: worker khảo sát là
singleton, tự mở context theo job và lưu theo từng xã — nó stage đúng một lần, ở lượt lưu của **xã neo**.

**FK `notification.recipient_user_id → app_user` là `Restrict`.** Mọi teardown xoá tài khoản test phải xoá
`notification` trước (theo `commune_id` của fixture là đủ, vì thông báo luôn mang xã của sự kiện). Quên là teardown
gãy, để lại tài khoản và xã — và `AccountAdminTests` (đọc `page_size=200` tài khoản đang hoạt động) đỏ ở lượt sau
vì DB đã quá 200 tài khoản. Test đổi luật người nhận mà thấy hàng loạt `23503 … fk_notification_app_user…` là đây.

**"Quản lý của xã" = `Notifier.ManagersCoveringAsync`** (đang hoạt động = không khoá **và** đã đặt mật khẩu; có
**đủ** mọi xã truyền vào). Sự kiện mới cần báo Quản lý thì gọi nó, đừng viết lại truy vấn — khảo sát liên xã mà báo
cho Quản lý thiếu một xã là báo cho người không duyệt được.

**Văn bản tự do vào thông báo (lý do trả phiếu, tên phiếu) phải qua `Notifier.Clip`, không cắt `Substring` tay.**
Cắt giữa một cặp surrogate (emoji) để lại nửa ký tự không mã hoá được UTF-8 — lượt ghi đó hỏng, kéo theo **cả thao tác
nghiệp vụ** đi cùng giao dịch (review Codex). **Đánh dấu đã đọc khoá hàng trước rồi mới đọc** (khuôn `FaultLocks`), để
hai thiết bị cùng bấm không ghi đè `read_at` đầu tiên.

**Câu chữ thuộc module sở hữu sự kiện** (`WorkOrderNotices`, `SurveyNotices`, `FaultNotices`) — Notifications không
tham chiếu ngược WorkOrders / Survey / Faults. Thêm `type` mới: thêm enum, câu chữ ở module đó, CHECK sinh lại qua
migration, Contract 5.9.

### BE-36 — test chạy trên container PostGIS của chính lượt chạy (11/10/2026)

**Mọi factory trong `LuxMap.Api.Tests` kế thừa `LuxMapTestHost`, không bao giờ kế thừa thẳng
`WebApplicationFactory<Program>`.** `LuxMapTestHost.CreateHost` chờ `TestDatabase` (một container
`imresamu/postgis:17-3.5` mỗi lượt, migrate → seed → SQL của `seed_mock_set.py`) rồi kiểm data source của
host trỏ đúng container. Factory kế thừa thẳng thì đọc DB từ `.env` — **`luxmap_dev`** — và test vẫn xanh.
Canh bằng `TestHostCoverageTests` (phá thử: `DeviceProbeFactory` kế thừa thẳng → đỏ nêu đúng tên).
Ngoại lệ có tên: **`NoDatabaseTestHost`** cho test không chạm DB (CORS, correlation id, phân trang, lỗi khởi động) — chạy
không cần Docker. Nó **không** được đổi `ConnectionStrings__LuxMap` (biến toàn tiến trình, host container đọc song song) mà
thay chính `NpgsqlDataSource` bằng địa chỉ không ai nghe: test lỡ chạm DB thì đỏ "connection refused", không lặng lẽ dùng
`.env` (`NoDatabaseHostTests`).

**Chỉ đặt `ConnectionStrings__LuxMap` (chuỗi nguyên khối), không đặt `POSTGRES_*`.** `Program` nạp `.env` bằng
DotNetEnv với mặc định **ghi đè** biến đã có, ở **mỗi lần** dựng host — các biến thành phần sẽ bị giá trị dev
đè. Vì vậy `.env` **không được** chứa `ConnectionStrings__LuxMap`: `TestDatabase.LoadEnvironment` từ chối chạy.
⚠️ Kiểm "host trỏ đúng container" chạy **sau** khi host đã dựng và khởi động — nó bắt được lỗi, nhưng thứ
**ngăn** ghi nhầm là lượt kiểm `.env` phía trước. Đừng gỡ lượt kiểm đó vì tưởng `AssertTargets` đã đủ.

**Không có chế độ "chạy test trên DB có sẵn"** (D-5): biến `ConnectionStrings__LuxMap` đặt trong terminal bị
container ghi đè. Thêm chế độ đó phải kèm một guard riêng cấm DB dev — guard hiện tại chỉ biết container.

**Nạp mock bằng SQL stdout của script, không bằng `--apply`.** `--apply` mặc định `luxmap_dev` và xoá toàn
bảng; SQL chạy qua kết nối của `TestDatabase` thì đích không thể lạc.

**Mật khẩu seed đọc từ biến môi trường** (`AuthTestExtensions.SeedPassword` gọi `TestDatabase.LoadEnvironment`),
cùng nguồn với `SeedCredentials` — trước đây hai bên đọc hai nơi (file và env).

⚠️ **Khôi phục file sau phá thử bằng `cp` hoặc `touch`, KHÔNG bằng `mv` bản sao lưu.** `mv` giữ thời điểm sửa CŨ của bản
sao, cũ hơn DLL vừa build từ bản đã phá ⇒ MSBuild coi module là chưa đổi và giữ DLL bản phá; test đỏ ở lượt chạy toàn bộ trong khi
mã nguồn đã đúng (gặp thật ở CITIZEN-QR P2a). `git checkout` cũng không cứu được file chưa được track.

⚠️ **`dotnet test --no-build` sau khi đổi branch chạy bin CŨ.** Gặp thật ở P1: bin build trước khi pull
TOPO-INFER làm hàng loạt test đỏ `23514 ck_pole_feeder_source_matches_feeder` — trông như lỗi DB, thực ra
là thiếu code. Build lại trước khi kết luận.

### BE-31 — bảo hành và xuất CSV (12/10/2026)

**`warranty_status` có ĐÚNG MỘT định nghĩa: `WarrantyWindow` (`LuxMap.Shared/Contracts/Warranty`).** Mọi bề mặt đọc tính
bằng `WarrantyWindow.Classify` **sau** truy vấn (`AssetCrudService.WithWarranty`, `MapQueryService`, `PoleDetailService`),
không viết lại luật trong projection SQL. Chỗ duy nhất luật phải nằm trong SQL là **lọc** của `/statistics/warranty`: nó lọc
trên `window.Today` / `window.LastExpiringDay`, không tự tính ngày, và `WarrantyTests.The_status_filter_agrees_with_the_one_definition_on_every_edge`
đối chiếu từng dòng trả về với `Classify` trên cả năm mép. Lấy "hôm nay" qua `WarrantyCalendar` — múi giờ đọc từ chính
`WorkOrders:Agenda:TimeZone`, đừng khai khoá múi giờ thứ hai.

`ActiveFixture.WarrantyStatus` và `PoleMapFixture.WarrantyStatus` **mặc định `unknown`**, vì chúng được gán sau truy vấn: đường
đọc mới nào quên gán thì ra `unknown` — sai nhưng an toàn — chứ không ra `active`. Đừng đổi mặc định.

🔴 **Hai `ProducesResponseType` cùng một mã trạng thái: cái SAU THAY cái trước, không gộp.** Khai
`[ProducesResponseType<T>(200, "application/json")]` rồi `[ProducesResponseType<string>(200, "text/csv")]` làm schema `T` **biến
khỏi spec** (gặp thật: `FixtureStatusStatistics` và ba schema nữa mất), mà build, test và lint đều xanh. Action trả thêm CSV
gắn `[CsvExport]`; `CsvExportOperationFilter` thêm `text/csv` bên cạnh JSON. So spec theo **ngữ nghĩa** (path/schema đổi gì),
đừng chỉ đọc số dòng diff.

**CSV đi qua đúng một đường: `CsvExport.Write`**, tuần tự hoá từng dòng bằng chính `LuxMapJsonOptions` rồi đọc ô ra — nên tên
cột, enum, ngày, `null` viết y như JSON. Dòng phải **phẳng** (ô object/array → ném lỗi). Báo cáo mới cần CSV thì trả record
phẳng và gọi nó, đừng tự format. `GetTypeInfo` cần options đã có resolver: `Write` khoá options trước (`MakeReadOnly`) — gặp
thật ở test đơn vị, đỏ hay xanh tuỳ test nào chạy trước.

Generator spec có `S["PoleProperties"]` viết tay với `additionalProperties: false` — trường mới trên `properties` bản đồ phải
thêm vào đó (đã thêm `warranty_status`), nếu không spec hợp nhất từ chối chính response thật.

### CITIZEN-QR P2a — mã QR trên cột (12/10/2026, drift QR-1…QR-6)

- 🔴 **`code` là chìa khoá gửi báo cáo, không phải định danh.** Mọi thao tác của Quản lý đi theo **`qr_id`** (`QR-0001`); `code`
  không bao giờ nằm trong đường dẫn (log request ghi nguyên `RequestPath`), trong audit hay trong log (D-16). Audit `qr_code` chỉ ghi
  `qr_id`, `pole_id`, `replaces` / `replaced_by`. Canh bằng `Issuing_again_revokes_the_code_in_use_and_the_audit_never_holds_a_code`.
- **Một mã đang dùng mỗi cột**: `IssueAsync` đọc cột qua filter TRƯỚC (ngoài phạm vi là 404, Quản lý xã khác không chờ khoá), rồi
  `FOR UPDATE` hàng `pole`, rồi mới tìm mã đang dùng; `ux_qr_code_pole_id_active` là lớp chặn cuối. Phá thử bỏ khoá →
  `Simultaneous_presses_leave_exactly_one_code_in_use` đỏ 3/3. Thu hồi + cấp mới là **hai** `SaveChanges` trong một giao dịch (mỗi lần
  đúng một audit, BE-23a).
- **Hai `HasIndex` trên CÙNG cột phải có tên model** (`HasIndex(x => x.PoleId, "tên")`) — không thì EF coi lần gọi thứ hai là lần đầu
  và **đè** nó. Ở đây cần cả unique partial `ux_qr_code_pole_id_active` lẫn `ix_qr_code_pole_id` đầy đủ (partial không phục vụ hàng
  đã thu hồi — quy tắc partial index).
- **Thu hồi khoá CÙNG hàng `pole`, cùng thứ tự (pole → mã), như cấp mới** (review Codex P2a). Chỉ khoá hàng `qr_code` thì một lượt
  cấp mới đọc mã là "đang dùng" trước khi thu hồi commit, rồi ghi đè người / thời điểm thu hồi kèm audit `revoked` thứ hai (bảng
  không có row version). Canh bằng `Issue_and_revoke_both_wait_for_the_pole_lock` (giữ khoá `FOR NO KEY UPDATE`; phá thử → đỏ).
- `commune_id` của mã **chép từ cột** ở server, như `fixture`; client không gửi được.
- Teardown xoá `qr_code` **trước** `pole` và tài khoản (FK `Restrict` tới cả hai). Rollback `AddQrCodes` gãy khi `audit_event` đã có
  `entity_type = 'qr_code'` hoặc `action = 'revoked'` (cùng họ BE-19).

### CITIZEN-QR P2b — form công khai, không token (13/10/2026, drift PB-1…PB-8)

- 🔴 **Endpoint công khai KHÔNG dùng `LuxMapDbContext` của request.** Request ẩn danh có phạm vi xã **rỗng**: thấy không hàng nào,
  ghi không được gì. `CitizenScopeFactory.Unscoped()` chỉ cho hai việc — mã → cột / xã, và tra gửi lại theo `client_op_id`; mọi đọc / ghi
  còn lại qua `CitizenScopeFactory.Open(<xã của cột>)`, nơi query filter, `CommuneWriteGuard` và guard audit chạy bình thường. Không
  backdoor, không `*`, không lấy xã từ client hay từ token tình cờ gửi kèm.
- **`[AllowAnonymous]` trên TỪNG action của `PublicController`**, không cấp class. Ba nơi nêu đích danh hai route và phải sửa cùng diff
  khi đổi: `AnonymousEndpointTests` (cả hai phép kiểm), `OpenApiSpecTests` (ngoại lệ `/api/v1/public/`), generator spec (`is_anonymous`).
- **Mã đi trong BODY, không bao giờ trong đường dẫn** — log request ghi `RequestPath` (D-16). Endpoint mới của phía công khai giữ luật này.
- **Ảnh công dân qua `IImageSanitizer` TRƯỚC `StoreImageAsync`** (ngoại lệ BE-11 quy tắc 4, chỉ cho ảnh công dân): đọc có giới hạn byte →
  magic bytes → `IdentifyAsync` + giới hạn pixel (chỉ đọc header) → decode trong cổng `SemaphoreSlim` → `AutoOrient` → bỏ EXIF/XMP/IPTC/ICC →
  mã hoá lại. Decode luôn qua `ThumbnailFactory.JpegOnly` (tiền đề tắt advisory ImageSharp). Phá thử: bỏ giới hạn pixel → test bom pixel đỏ;
  giữ EXIF → test GPS đỏ.
- **Thứ tự của `SubmitAsync` là thiết kế, đừng đảo** (review P2b #2, #4–#7): kiểm → tra mã → băm nội dung (ảnh đọc dạng luồng) → gửi lại
  sớm → hạn mức sớm (không khoá) → **làm sạch MỌI ảnh** → khoá `pole` → **đọc lại mã còn hiệu lực** (thu hồi giữa chừng thắng) → **gửi lại
  lần nữa** → **hạn mức lần nữa, cộng byte thật của ảnh sắp ghi** → ghi object → ghi dòng. Object ghi **dưới khoá** để lượt thua hạn mức hay
  thua đua gửi lại không để lại ảnh mồ côi; sau làm sạch mỗi ảnh vài trăm KB nên khoá chỉ giữ vài giây (khác clip BE-15). Quá hạn mức thì
  **vẫn nhận chữ**, bỏ ảnh. `last_submitted_at` lấy **max** (lượt chậm đến sau lượt nhanh không được ghi lùi — CHECK sẽ ra 500). Ngân sách byte
  toàn hệ thống **không** tuần tự hoá giữa các xã: hai lượt cùng lúc ở hai xã có thể vượt bằng ảnh của chính chúng — đừng mô tả là chặt.
- **Cổng làm sạch lấy chỗ TRƯỚC khi đọc ảnh vào RAM**, hàng chờ có hạn (quá → 503 `SERVER_BUSY`), ảnh sạch thu nhỏ về `PhotoMaxLongEdge`
  (2560) — nên bước tạo thumbnail (decode lần hai, ngoài cổng) chỉ gặp ảnh nhỏ. `MaxPhotos` tối đa **3** cũng vì CHECK của `citizen_photo`.
- **Khoá ngoại ghép giữ một lượt gửi cùng cột + xã với báo cáo và mã của nó** (`ak_citizen_report_…`, `ak_qr_code_qr_id_pole_id_commune_id` —
  trông thừa, là đích FK). `report → pole` vẫn là FK đơn (pole không có khoá `(pole_id, commune_id)`; server chép xã từ mã).
- ⚠️ **Postgres cắt tên định danh ở 63 ký tự, im lặng.** Tên FK ghép EF sinh ra có thể dài hơn (`fk_citizen_submission_citizen_report_report_id_pole_id_commune_id`
  = 66) và trên DB thành tên khác — code nhận diện lỗi theo `ConstraintName` hay test so tên đầy đủ sẽ trượt. Đặt `HasConstraintName` ngắn
  nếu cần bắt theo tên.
- 🔴 **Forwarded headers: danh sách RỖNG = TIN MỌI PEER, không phải "không tin ai".** ASP.NET Core bỏ hẳn bước kiểm IP khi cả
  `KnownProxies` lẫn `KnownIPNetworks` trống (bản đầu của P2b xoá mặc định loopback rồi để trống — Codex review bắt, P1). Nên **không
  cấu hình proxy ⇒ middleware KHÔNG nằm trong pipeline** (`TrustedProxies.Any`); có cấu hình ⇒ đúng các proxy đó, loopback mặc định bị
  xoá, middleware đứng **đầu** pipeline. Triển khai sau reverse proxy phải khai proxy, nếu không cả xã chung một xô giới hạn. Canh bằng
  `With_no_proxy_configured_a_forwarded_address_is_ignored_from_every_peer` và `Forwarded_addresses_count_only_from_a_trusted_proxy`
  (giả địa chỉ peer bằng `IStartupFilter`).
- Gửi lại: khoá `client_op_id` gắn với **mã + băm nội dung** (kể cả ảnh), tra ở cả bước sớm lẫn khi bắt trùng `ux_citizen_submission_client_op_id`.
- Teardown: `citizen_photo` (theo submission) → `citizen_submission` → `citizen_report` → `qr_code` → `pole`. Rollback `AddCitizenReports` gãy
  khi `audit_event` đã có actor `public` hoặc entity `citizen_report` (cùng họ BE-19).

### CITIZEN-QR P2c — hàng chờ duyệt (13/10/2026, drift PC-1…PC-10)

- 🔴 **Mọi quyết định (accept / merge / reject) khoá hàng `pole` TRƯỚC, rồi `citizen_report`, rồi (merge) `fault`** — cùng khoá pole mà
  lượt gửi công khai giữ khi nộp vào báo cáo. Bỏ khoá pole thì một lượt gửi đang chạy có thể gắn vào báo cáo vừa được quyết và không bao
  giờ tới tay Quản lý. Đọc lần đầu là **projection không tracking**, sau khoá mới nạp entity — đọc tracking trước khoá thì lần đọc sau trả
  lại chính entity cũ. Canh bằng `A_decision_waits_for_the_pole_lock_a_public_send_holds` (giữ khoá `FOR NO KEY UPDATE`). **Đừng** thêm
  khoá pole vào `PATCH /faults` hay tạo phiếu để "chặn trùng cho chặt": các đường đó khoá fault trước, sẽ thành vòng fault → pole.
- **`submission_count` là phiên bản của báo cáo** vì lượt gửi **chỉ được thêm**. Tính năng nào xoá / gộp lượt gửi sau này phải thay bằng
  dấu phiên bản thật, nếu không 409 `CITIZEN_REPORT_CHANGED` sẽ để lọt quyết định trên nội dung chưa đọc.
- **Citizen không bao giờ `new Fault`.** Chấp nhận đi qua `FaultReportService.StageReviewedReportAsync` (Faults): đọc cột qua filter, lấy
  xã / tuyến / `data_source` của cột, xác nhận qua `FaultTransitions.Apply`, stage đúng một audit `created` — **không** save. Người gọi
  save trong transaction của mình rồi mới đổi báo cáo (save thứ hai, audit `accepted`).
- 🔴 **`CitizenPhoto` là `IAudited` từ P2c** (gợi ý AI là phát hiện tự động, D-R13). Mọi đường ghi ảnh phải có đúng một audit trong cùng
  `SaveChanges`: lượt gửi công khai (`submitted`, actor `public`) và gợi ý (`analyzed`, actor `cv`, entity `citizen_report`).
- **Không inject `YoloModel` vào service trên đường request.** Constructor của nó ném khi thiếu file model ⇒ DI làm cả màn chi tiết 500.
  `YoloCitizenPhotoHinter` resolve lười trong vùng bắt lỗi; gợi ý là tuỳ chọn, hàng chờ thì không. Model chạy **ngoài** transaction và
  khoá; chỉ lượt ghi khoá hàng ảnh và chỉ ghi ảnh **còn `ai_hint` null** — hai lượt mở cùng lúc có thể cùng chạy model nhưng chỉ một
  ghi, một event (`The_hint_is_an_audited_automated_finding_written_once`, phá thử đỏ 3/3).
- `artifact_version` của gợi ý = phiên bản detector **+** băm luật gợi ý (một bóng, ngưỡng ảnh ban ngày). Đổi `Citizen:DaylightMeanLuma`
  là đổi phiên bản — đừng tách luật ra khỏi chuỗi đó.
- Test model thật **không** khẳng định model thấy gì: nó gọi khung đen có một điểm sáng là một bóng `out`. Chỉ canh hợp đồng (nhãn hợp lệ,
  confidence đúng khi kết luận, dạng phiên bản) — cùng quy ước AI-1.
- Teardown: `citizen_photo` → `citizen_submission` → `citizen_report` **trước `fault`** — báo cáo đã chấp nhận / gộp giữ sự cố bằng
  `Restrict`. Rollback `AddCitizenReview` gãy khi audit đã có `accepted` / `merged` / `analyzed` (cùng họ BE-19).

### BE-17 — độ phủ sweep (13/10/2026, drift SC-1…SC-6)

- **Độ phủ đọc từ ẢNH CHỤP GIS của run** (`survey_processing_run.gis_snapshot`: `segments`, `communes`, `poles` kèm `segment_id`), không từ tài
  sản hôm nay — câu trả lời phải là cái run đã thấy, như `results` / `UnobservedPoles`. Chỉ `length_m` (khai báo) và hình học lấy từ
  `road_segment` hiện tại.
- 🔴 **Phân số của `survey_pass` đo trên 3405** (`ST_LineLocatePoint` × `ST_Length` của tuyến chiếu). Cắt khoảng trống phải cắt **trên cùng
  phép chiếu đó** rồi chuyển về 4326 **trong SQL**: `ST_Transform(ST_LineSubstring(ST_Transform(geom,3405), f, t), 4326)`. Cắt thẳng trên
  4326 bằng phân số 3405 lệch trên tuyến cong (hai trục lệch tỉ lệ ~cos(vĩ độ)); để toạ độ 3405 rời SQL là phạm BE-10 quy tắc 3.
- **`SqlQuery<T>` đi qua quy ước snake_case**: alias cột phải là `geo_json`, `ordinal` — alias `"GeoJson"` ném `required column 'geo_json'
  was not present` lúc chạy, build vẫn xanh.
- **Quyền độ phủ = luật ảnh khảo sát cho một run:** `CoversWholeRun` (xã của cột) **và** mọi xã trong `gis_snapshot.communes` (xã của
  phiếu). Độ phủ vẽ tuyến; tuyến liên xã có thể thuộc xã mà run không có cột nào.
- `SweepResponse` không còn `sealed` — chỉ để `SweepDetailResponse` thêm `latest_run`; danh sách vẫn trả kiểu gốc, không mang run.
- ⚠️ **Object nullable xuất ra spec là `$ref` TRẦN, không `nullable`** (`latest_run`, `CoverageGap.geometry`, và sẵn có từ trước:
  `PoleMapDetail.fixture`). Đó là hạn chế chung của tầng sinh Swagger, ghi ở "Vấn đề đang mở" của tracking — sửa một lần cho mọi schema,
  đừng vá riêng từng endpoint.
- Khoảng trống **đúng 1 m được giữ**, chỉ khoảng **ngắn hơn** bị bỏ (SC-4); so sánh có dung sai `1e-9` vì phân số × chiều dài hiếm khi rơi
  đúng 1 m.
- Test ghi run / quan sát tay: `classified_as = unknown` là cách duy nhất không cần frame (`ck_pole_observation_classification`).
- 🔴 **Dữ liệu khảo sát dựng trong test KHÔNG xoá được** (run / lượt / quan sát bất biến), nên phiếu và sweep của nó **ở lại** — đừng gắn
  chúng vào tài khoản của fixture: `work_order.assigned_to` / `created_by` giữ tài khoản bằng `Restrict`, teardown của
  `AssetDatabaseCollection` gãy (`23503 fk_work_order_app_user_assigned_to`) dù mọi test xanh. Tạo tài khoản riêng **một lần cho cả class**
  (`InitializeAsync` của `IAsyncLifetime` chạy **mỗi test** — khuôn seed tĩnh có khoá ở `SweepCoverageTests`), tên xã kèm GUID
  (`ix_administrative_unit_name` unique).
- 🔴 **Đọc mã thoát của `dotnet test`, không chỉ dòng `Passed!`.** Lỗi teardown collection in `[Test Collection Cleanup Failure …]` và trả
  mã 1, trong khi dòng tổng vẫn ghi `Failed: 0` — CI đỏ ở #140 vì đúng chỗ này, lượt chạy local bỏ sót vì chỉ lọc dòng tổng.

### BE-43 — đồng bộ offline (06/10/2026)

**Module `LuxMap.Modules.Sync` đứng trên đỉnh đồ thị phụ thuộc** (tham chiếu Map, Faults, WorkOrders, Survey, Assets) và
**không ghi dòng nghiệp vụ nào** — mỗi thao tác của hàng chờ gọi đúng service của endpoint gốc. Thêm `op_type` mới thì gọi service
của nó, đừng chép logic kiểm tra / ghi vào Sync: hai đường ghi sẽ lệch nhau ngày một trong hai đổi. Kiểm tra mà controller gốc làm
ngoài service (DataAnnotations, `RejectServerOwnedFields`) phải chuyển **vào service** hoặc gọi lại tường minh — `LuxReadingService.
RejectServerOwnedFields` đã chuyển vì lý do đó.

🔴 **Kiểm capability TỪNG thao tác**, ngoài `SyncOffline` ở cửa (`SyncPushService.PolicyFor` → `IAuthorizationService`, cùng tên
policy với endpoint gốc, không chép danh sách vai trò). Một endpoint gói năm loại ghi mà chỉ kiểm cửa là mở `ReportFaults` cho mọi vai
trò được sync. Test HTTP không phân biệt được hai lớp khi `SyncOffline` chỉ có `field_engineer`, nên lớp thứ hai được canh bằng một host
**từ chối riêng `ReportFaults`** (`Each_operation_is_checked_against_its_own_capability_not_only_the_door`).

**`sync_operation` được STAGE trước khi gọi service** và ghi bởi chính `SaveChanges` của service (khuôn `Notifier.Stage`): thao tác lỗi
hay thua tranh chấp không để lại dòng. Chỉ thao tác **đã áp** được ghi; conflict / rejected thì lần gửi lại đánh giá lại. Có **hai lớp**
khử trùng lặp, cả hai đều so `op_type` + `entity_id` trước khi báo "replayed": tra sớm (`RecordedAsync`) và bắt trùng
`pk_sync_operation` khi đua. Phá thử cho thấy tắt riêng một lớp vẫn xanh — cố ý; tắt cả hai thì
`A_client_op_id_reused_for_a_different_operation_is_rejected_and_writes_nothing` đỏ. Đừng "đơn giản hoá" đường bắt trùng thành
"luôn replayed": thao tác KHÁC dùng lại khoá sẽ bị báo đã áp dù không ghi gì.

**Tra chéo khoá giữa ba nơi giữ khoá** (`fault`, `lux_reading`, `sync_operation` — `SpentElsewhereAsync`) chỉ là best effort: không
nguyên tử với lượt ghi, nên hai yêu cầu đồng thời dùng chung khoá cho hai *loại* khác nhau có thể cùng qua. Chấp nhận vì mỗi nơi vẫn
áp thao tác của nó tối đa một lần. Đừng mô tả nó như ràng buộc chặt.

**`performed_at` không bao giờ để bước sau sớm hơn bước trước** (`WorkOrderService.Effective`): không gửi giờ mà giờ server còn sớm
hơn mốc trước thì lấy mốc trước. Canh bằng `A_completion_is_never_stored_before_its_start`.

**Giữa hai thao tác phải `ResetAsync`** (rollback giao dịch đang mở + `ChangeTracker.Clear()`): entity mà thao tác lỗi để lại sẽ bị
`SaveChanges` của thao tác sau ghi luôn.

**`SetPoleNoteAsync(…, expected)` khoá hàng `pole` (`SELECT 1 … FOR UPDATE`) rồi mới đọc qua query filter** — chỉ khi có `base_note`;
so theo **nội dung**, không theo `updated_at` (giờ đó đổi khi sửa bất cứ gì của cột).

**`PoleProperties` không còn `sealed`** — chỉ để `SyncPoleProperties` thêm `note` mà vẫn phẳng. Dựng bằng copy constructor của record
(`[SetsRequiredMembers]`); thêm trường vào `PoleProperties` thì gói tự có.

**Test dựng nhiều cột trong MỘT `SaveChanges` không được giả định ID theo thứ tự `AddRange`** — EF không giữ thứ tự khi DB sinh khoá
(cùng bẫy 7b). Kỳ vọng thứ tự phải sắp theo luật ID (`IdOrder` trong `SyncTests`).

**Generator spec:** stub `ni()` của hai endpoint sync và các schema viết tay `Sync*`, `PoleFeature(Collection)`, `SegmentFeature(Collection)`,
`PointGeometry`, `LineStringGeometry` đã xoá (mồ côi khi stub đi). `object?` trong C# xuất `nullable` không `type` → lint lỗi; vá ở phần
"lint fix" của generator như `ApiError.details`.

## Agent skills

### Issue tracker

Ticket là file `.ai/tasks/<mã>.md` theo giao thức `.ai/` (mã từ `docs/tasks-backend.csv`), không dùng GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Vai trò triage ánh xạ vào trường `status:` (và `owner:`) sẵn có của ticket, không thêm label riêng. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: thuật ngữ ở `CONTEXT.md` gốc repo; quyết định vào `docs/contract-drift.md` / `CLAUDE.md`, **không** có `docs/adr/`. See `docs/agents/domain.md`.

### CABINET — trụ điện tổng là tài sản, IoT là thiết bị gắn lên trụ (07/10/2026, drift CAB-1…CAB-8)

**Toạ độ thuộc `electrical_cabinet`, không thuộc `iot_node` nữa** — `iot_node.geom` đã bỏ. Mọi chỗ cần vị trí thiết bị
(bản đồ, IOT-11 sinh `node_offline` / `runtime_decline` với `location` = toạ độ tủ theo I-9) phải join trụ qua
`iot_node.cabinet_id`. Lọc bbox thì `ST_Intersects` trên **`electrical_cabinet.geom` trần** (`MapQueryService.IotNodeQuery`),
canh bằng `MapQueryPlanTests.The_device_and_cabinet_bbox_queries_can_ride_the_cabinet_gist_index`.

🔴 **Bốn ràng buộc chỉ có ở DB, EF không biết** (`CabinetConstraints`, raw SQL trong `AddElectricalCabinet`): hai unique đích
`(feeder_id, cabinet_id)`, `(node_id, cabinet_id)` và hai FK rơ-le-cùng-trụ của CAB-4. Lý do: mỗi đích
chứa một cột người ta sửa được; khai `HasAlternateKey` là biến cột đó thành key property và EF ném ngay trong
`DetectChanges` (cùng bẫy `Feeder.CommuneId` của O-7), còn alternate key trên cột nullable thì EF không cho. Hệ quả:
**snapshot không thấy chúng, migration sau không tự dựng lại, và không ai biết khi một cái mất** — chỉ
`CabinetTests.Every_database_only_cabinet_constraint_exists` biết. Gộp/squash migration thì phải chép khối SQL đó theo.

- **Viết `feeder_control` thì `cabinet_id` phải là trụ của CẢ thiết bị lẫn feeder** — gắn feeder vào trụ trước. Hệ quả ngược:
  feeder đang có rơ-le **không đổi trụ / tháo trụ được**, thiết bị đang có rơ-le không chuyển trụ được (FK `NO ACTION`).
  Service trả 409 `ASSET_IN_USE`; import báo lỗi theo dòng — **phải** bắt ở bước kiểm, FK ở bước ghi làm hỏng cả mẻ (500).
- **Assets không thấy `iot_node` / `feeder_control`** (Telemetry tham chiếu Assets, không ngược lại): hỏi qua port
  **`ICabinetDeviceLookup`** (Telemetry hiện thực), khuôn `IActiveWorkOrderLookup`. Đừng thêm project reference, đừng
  `SqlQuery` thẳng bảng của Telemetry từ Assets.
- **`PUT /assets/feeders/{id}`: `cabinet_id` vắng = GIỮ** — ngoại lệ thứ hai của thay thế toàn phần, cùng khuôn `note` của cột.
  Import feeder: ô trống / thiếu cột = giữ, import không bao giờ tháo trụ. Đừng "sửa cho nhất quán" với `feeder_id` của cột.
- **Teardown xoá `feeder_control` → `iot_node` → `feeder` → `electrical_cabinet`** (tất cả `Restrict`). Fixture tài sản đã làm;
  fixture mới tạo thiết bị phải theo.
- Trụ có thể `field`, **kể cả khi đang mang thiết bị** — luật CAB-5 đã **gỡ** (Mỹ, 07/10/2026, migration
  `DropFieldCabinetRule`), canh bằng `A_cabinet_carrying_a_device_may_become_field_data`. Thiết bị thì không bao giờ `field`
  (`ck_iot_node_data_source_not_field` giữ nguyên); mở rộng nó là quyết định phạm vi (lắp IoT ngoài xã), không phải sửa lỗi.


### TOPO-INFER — nhãn nguồn gốc topology (07/10/2026, drift TI-1…TI-6)

Topology ngoài thực địa là **suy luận công khai**: `pole.feeder_source` và `feeder.cabinet_source` (`verified | inferred`), mỗi
nhãn **null đúng khi quan hệ null** (`ck_pole_feeder_source_matches_feeder`, `ck_feeder_cabinet_source_matches_cabinet`).

- 🔴 **Mọi đường ghi `pole.feeder_id` / `feeder.cabinet_id` đi qua `TopologyLink`** (`Resolve` rồi `ApplyPoleFeeder` /
  `ApplyFeederCabinet`). Đó là chỗ DUY NHẤT có luật TI-2 (vắng + quan hệ không đổi → giữ; đổi → `inferred`; `null` / sai kiểu /
  nhãn không quan hệ → 400) và chỗ **ghi theo cặp**: EF chỉ ghi cột đổi so với snapshot, nên hai lượt xen nhau (A xác minh F1,
  B chuyển sang F2) sẽ để lại `(F2, verified)` — cặp không ai khẳng định. `Apply*` đánh dấu cả hai cột khi một trong hai đổi,
  và **không** đánh dấu gì khi cả hai giữ nguyên (giữ luật N-4: nạp lại file y hệt không đóng dấu). Canh bằng
  `Interleaved_writers_never_leave_a_pair_nobody_asserted`. Gán `FeederId` / `CabinetId` trực tiếp trong code mới là mở lại lỗ đó.
- **Không đường nào tự nhận `verified`.** Backfill, seed, mặc định đều `inferred`. Fixture test gán quan hệ phải kèm nhãn (CHECK).
- **Import: ô trống = vắng.** GeoJSON object / mảng ở ô nhãn phải là lỗi dòng — `GeoJsonImportRow` đọc chúng thành `null`, tức
  "trống", nên cột có nghĩa "trống = giữ" phải hỏi `IImportRow.IsNonScalar` trước (`ImportRowReader.Label`).
- **`GET /map/cabinets/{id}/topology`** (`CabinetTopologyService`): sơ đồ **logic**, thứ tự dọc đường bằng `LengthIndexedLine`
  trên 4326 — **chỉ thứ tự, không bao giờ khoảng cách**. Hình học tuyến đọc **bỏ query filter** (tuyến `inter_commune` có thể
  thuộc xã khác, cùng lý lẽ WO-12); bỏ `IgnoreQueryFilters` thì tra cứu trượt và ra 500. Cạnh đầu lấy nhãn **thấp hơn** của hai
  quan hệ. `verified` của cột nghĩa là "cột thuộc mạch này", **không** phải cạnh đó là dây thật.
- Chưa làm (ticket riêng): chặn trộn cột `field` với trụ không-`field` (phải kiểm ở ba đường), script đề xuất quan hệ suy luận.

### LIGHT-CTRL 2a — scheme `Device` + đăng ký thiết bị (08/10/2026, drift LC-1…LC-10)

- 🔴 **Scheme `Device` KHÔNG phải mặc định; endpoint thiết bị gắn `[Authorize(Policy = DeviceAuth.Policy)]`, KHÔNG BAO GIỜ
  `[AllowAnonymous]`** (nó bỏ toàn bộ authorization). Policy `DeviceOnly` ghim `AddAuthenticationSchemes(Device)` — gỡ dòng đó thì
  Bearer của người dùng chạm được endpoint thiết bị (phá thử: 3 test đỏ). Endpoint thiết bị thật (2b) phải là **ngoại lệ có tên** trong
  `CapabilityPolicyCoverageTests` (policy ngoài ma trận), không nới luật chung.
- **Principal thiết bị:** `commune_ids` = đúng một xã của thiết bị (không bao giờ `*`), `device_node_id`, **không** `role` ⇒
  `CommuneScopeAccessor` có sẵn cho ra phạm vi một xã, query filter + guard áp như mọi request; không capability nào nhận nó. Mọi truy
  vấn lệnh (2b) còn phải lọc `node_id = device_node_id` — cùng xã không có nghĩa là được đụng thiết bị khác.
- **Bí mật:** `DeviceSecret` (`LuxMap.Shared`): 32 byte → base64url; chỉ lưu SHA-256 hex (`iot_node.credential_hash`, CHECK cặp với
  `credential_set_at`); so `FixedTimeEquals`; mọi kiểu sai đều cùng một 401. **Không bao giờ log bí mật hay header `Authorization`**;
  bí mật chỉ xuất hiện đúng một lần trong response cấp / xoay.
- **Nối rơ-le khoá hàng `iot_node`** (`SELECT 1 … FOR UPDATE` rồi mới đọc qua filter); lệnh điều khiển (2b) phải lấy **cùng** khoá.
  Nối lại một rơ-le = xoá dòng `feeder_control` cũ + chèn dòng mới trong một `SaveChanges` (EF xoá trước, chèn sau — canh bằng test).
- **Header theo RFC 9110:** chữ `Device` không phân biệt hoa thường, sau nó 1+ dấu cách; credential không chứa khoảng trắng.
- 🔴 **`ExceptionHandlingMiddleware.WriteAsync` gọi `Response.Clear()` ⇒ xoá `WWW-Authenticate`.** Trang 401 chỉ giữ lại challenge
  **trần** của scheme `Device` (`ApiConventionsSetup.DeviceChallenge`). **Đừng "sửa cho đều" thành giữ mọi challenge:** challenge
  của JwtBearer mang `error_description` (token hết hạn, sai khoá ký…) — đúng nguyên nhân mà `UNAUTHENTICATED` cố ý không tách.
- **Khoá hàng sau khi đã scope:** đọc thiết bị qua filter xã TRƯỚC, rồi mới `FOR UPDATE` theo `node_id` **và** `commune_id` — khoá
  trước thì Quản lý xã khác chờ / đo được khoá của thiết bị ngoài phạm vi. Hàng `feeder` không nằm dưới khoá đó, nên đua chuyển trụ
  được FK DB chặn và service **dịch lại thành 409** (`fk_feeder_control_feeder_same_cabinet`, `pk_feeder_control`), không để 500.
- **Controller test cần xác thực không được nằm trong `TestEndpointsController`** (`[AllowAnonymous]` cấp class thắng). Khuôn:
  `DeviceProbeController` + `DeviceProbeFactory` (host riêng, cùng DB) — không thêm ApplicationPart vào `AssetImportFixture` dùng chung.

### LIGHT-CTRL 2b — lệnh ON / OFF / AUTO (08/10/2026, drift LC-11)

- **Một dịch vụ, mọi kênh:** `LightingCommandService` giữ trọn vòng đời (bấm, giao, ACK, hết hạn, thay). Adapter MQTT sau này
  **gọi đúng các hàm đó** — không viết luồng lệnh thứ hai, không để QoS của broker thay ACK nghiệp vụ.
- 🔴 **Mọi lượt ghi chạm lệnh khoá hàng `iot_node` trước** (đọc qua filter → `FOR UPDATE` theo `node_id` + `commune_id` → đọc
  lại). Lần bấm nhiều thiết bị khoá theo thứ tự `node_id` ordinal; nối rơ-le lấy cùng khoá. Sau khoá phải **lập kế hoạch lại** và so —
  lệch thì 409 `LIGHTING_TARGET_CHANGED`, không âm thầm gửi theo kế hoạch cũ. Phá thử: bỏ khoá → `Two_presses_at_once…` đỏ.
- **Audit đúng một event mỗi `SaveChanges`** ⇒ bước chạm nhiều lệnh (hết hạn, giao, thay do nối rơ-le) **lưu từng lệnh một** trong
  cùng transaction. Nhánh trả lời sớm của ACK vẫn phải **commit** — không thì các lệnh hết hạn vừa lưu bị rollback.
- **`seq` là cột identity**, không vẽ trước: DB cấp lúc INSERT, tăng dần qua mọi phiên; hai lần bấm trên một thiết bị đã tuần tự
  nhờ khoá. `command_id` thì **vẽ trước** (khuôn `NextFaultId`) vì audit của lần bấm phải liệt kê nó. Sắp lệnh theo `seq`,
  **không** theo `command_id` (luật độ rộng tối thiểu, mục 0).
- **`control_mode` chỉ đổi theo báo cáo của thiết bị, chỉ khi `seq` mới hơn `mode_seq` VÀ chưa có lệnh mới hơn nào được giao cho
  rơ-le đó** (D-10, I-6) — kể cả ACK trên lệnh đã đóng (`COMMAND_CLOSED`, vẫn lưu audit `reported`). `mode_seq` một mình **không đủ**:
  nó nằm trên dòng `feeder_control`, mà tháo rơ-le là xoá dòng ⇒ tháo + nối lại sẽ reset nó (Codex review 2b). Lịch sử lệnh thì còn.
- **Tắt `supports_remote_control` thay mọi lệnh đang mở** (registry, actor Quản lý) và **poll không bao giờ giao** cho thiết bị đã tắt
  cờ (actor `system` nếu cờ đổi đường khác) — D-R7.
- 🔴 **Test chứng minh "có lấy khoá" phải giữ khoá bằng `FOR NO KEY UPDATE`, không `FOR UPDATE`.** Chèn hàng có FK tới `iot_node`
  lấy KEY SHARE, và `UPDATE last_report_at` lấy NO KEY UPDATE — với `FOR UPDATE` cả hai tự chờ, test xanh dù service bỏ khoá
  (phá thử đã lộ). Khuôn: `LightingCommandTests.HoldingTheDeviceLockAsync`; poll phải đẩy `last_report_at` ra tương lai trước.
- **Lệnh thiết bị luôn lọc thêm `node_id` = principal** — cùng xã không có nghĩa là được đụng lệnh của thiết bị khác.
- **Endpoint thiết bị là ngoại lệ có tên** trong `CapabilityPolicyCoverageTests.DeviceEndpoints` và trong `OpenApiSpecTests`
  (`/api/v1/device/` khai scheme `Device`, không capability). Thêm endpoint thiết bị = sửa cả hai, nhìn thấy trong diff.
- Teardown: `lighting_command` → `lighting_request` **trước** `feeder_control`, `iot_node`, `feeder` và tài khoản (đều `Restrict`).
  Rollback `AddLightingCommands` gãy khi `audit_event` đã có giá trị mới (`system`, `lighting_*`, …) — cùng họ BE-19.

### AI-1 — model YOLO trong backend (08/10/2026)

- **Một model, hai lối vào:** `YoloModel` (singleton, nạp lúc dùng lần đầu) phục vụ cả `YoloOnOffDetector` (pipeline khảo sát,
  keyed `IOnOffDetector` khoá `"yolo"` — Survey **không** tham chiếu AI) lẫn `POST /ai/detect`. Đừng viết lối decode / tiền xử lý thứ hai.
- 🔴 **Decode chỉ qua `JpegMagicBytes` + `ThumbnailFactory.JpegOnly`** — tiền đề để tắt advisory ImageSharp. Bản #118 decode bằng
  cấu hình mặc định và tin `Content-Type`; `JpegOnlyDecodeTests` giờ đỏ nếu ai viết lại như vậy.
- **Model được KIỂM lúc nạp:** input `[1,3,640,640]`, output `[1,6,N]`, metadata `names` đúng `normal, out` theo thứ tự, SHA-256
  khớp `Ai:ModelSha256`. Đổi model = đổi mã ghim trong cùng PR. Tiền xử lý theo Ultralytics: xoay EXIF, letterbox xám 114,
  resize **bilinear** (`KnownResamplers.Triangle` — mặc định của ImageSharp là bicubic, lệch với `cv2.INTER_LINEAR`).
- **`artifact_version` của detector băm cả ngưỡng + tiền xử lý + ánh xạ lớp**, không chỉ file model: đổi ngưỡng mà giữ phiên bản
  là làm mất truy vết BE-34. Test canh: `As_the_survey_detector_its_output_passes_the_pipeline_validation`.
- Không có ảnh đèn thật trong test ⇒ test **không** khẳng định model phát hiện được gì; chúng canh hợp đồng (nhãn, bbox, phiên bản,
  validation) và hậu xử lý trên tensor dựng tay. Độ chính xác là việc của WP4.

### LC-12 — kênh MQTT cho thiết bị (08/10/2026)

- **Adapter MQTT chỉ chuyển byte.** Mọi quyết định nằm ở `MqttLightingHandler` (test không cần broker) và `LightingCommandService`.
  Thêm loại bản tin = thêm nhánh ở handler gọi hàm của service; đừng viết logic lệnh trong `MqttLightingChannel`.
- **Worker không có principal ⇒ `LightingScopeFactory` tạo context phạm vi = xã của thiết bị** (khuôn `JobScope`). Tra thiết bị
  **không lọc** chỉ để lập danh tính / phạm vi (`CommuneOfAsync`, `NodesWithOpenCommandsAsync`); mọi xử lý nghiệp vụ đi qua context
  có phạm vi.
- **Kiểm tra mà controller làm bằng DataAnnotations phải nằm trong service** (`AckAsync`: `seq`, `result` bắt buộc, `error` ≤ 500) —
  bản tin MQTT không đi qua controller. Cùng luật BE-43.
- 🔴 **Endpoint của MÁY (thiết bị, broker) là ngoại lệ có tên** ở `CapabilityPolicyCoverageTests.MachineEndpoints` (route → policy).
  Callback broker dùng scheme `Broker` (khoá `Mqtt:CallbackKey`, so băm thời gian cố định), **không** `[AllowAnonymous]`, ẩn khỏi
  OpenAPI (`[ApiExplorerSettings(IgnoreApi = true)]`).
- **ACL nằm trong câu trả lời của callback** (`BrokerAuthController.DeviceAcl`/`BackendAcl`), EMQX `sources = []`, `no_match = deny`,
  `deny_action = disconnect`: subscribe / publish trái phép làm **ngắt kết nối**, không chỉ trả mã. Test ACL hai lớp: literal
  (`BrokerAuthTests`) + broker thật (`MqttEndToEndTests`); phá thử nới ACL → cả hai đỏ.
- **MQTTnet 5 KHÔNG ném lỗi khi broker từ chối CONNECT** — đọc `MqttClientConnectResult.ResultCode`. Test "sai bí mật bị từ chối"
  viết bằng `Assert.Throws` sẽ xanh giả.
- **EMQX kiểm callback lúc khởi động; không thấy API thì thử lại sau 15 s**, trong lúc đó từ chối mọi login. Test end-to-end chờ bằng
  kết nối thử với bí mật đúng (`ReadyDeviceAsync`). Host test phải `ListenAnyIP` — container gọi về qua `host-gateway` trên Linux.
- **`docker-compose.yml`: biến của service trong profile vẫn bị nội suy khi chạy profile khác** ⇒ dùng `${VAR:-default}`, không
  `:?` — nếu không, `docker compose up` của người có `.env` cũ sẽ gãy. Thiếu `Mqtt__CallbackKey` thì broker từ chối mọi client.

### BE-26 — job nền theo lịch trên lease của DB (14/10/2026, drift JB-1…JB-8)

- **Không Hangfire.** `LuxMap.Persistence/Jobs`: bảng `scheduled_job` (một dòng mỗi job, runner tự `INSERT … ON CONFLICT DO NOTHING`
  lúc khởi động), `ScheduledJobRunner` (claim = **một** câu `UPDATE … WHERE job_name = (SELECT … FOR UPDATE SKIP LOCKED)`; mọi so sánh
  giờ dùng **`now()` của DB**), `ScheduledJobWorker` (`Jobs:Enabled`, `Jobs:PollSeconds`, `Jobs:LeaseSeconds`, `Jobs:RetrySeconds`).
  Job mới = một `IScheduledJob` đăng ký singleton trong module sở hữu việc đó; không sửa runner.
- 🔴 **Lease KHÔNG phải thứ chống trùng.** Một lượt có thể chạy quá lease, hai instance có thể cùng chạy: mọi thứ job ghi phải có
  **khoá tự nhiên + unique ở DB** (`pk_work_order_overdue_notice` = (phiếu, hạn); `ux_fault_origin_node_id_detected_at` = (thiết bị,
  lúc vượt ngưỡng)) và bắt đúng tên ràng buộc đó khi đua. Phá thử bỏ lượt bắt ⇒ test đồng thời đỏ. "Kiểm rồi chèn" một mình là sai.
- **Job quét theo TRẠNG THÁI, không theo khe giờ**: một lượt xử lý mọi thứ đang đến hạn, nên server tắt vài giờ thì lượt đầu bù hết.
  Mốc của sự kiện lấy từ **dữ liệu** (`last_report_at + OfflineAfter`), không từ lúc job chạy — mọi instance tính ra cùng giá trị.
- **Không principal ⇒ `JobContextFactory`**: `Unscoped()` (scope rỗng, chỉ đọc với `IgnoreQueryFilters` để tìm ứng viên — guard
  từ chối mọi lượt ghi ở đó) và `Open(xã)` cho mọi lượt ghi, đọc lại ứng viên **qua filter** trước khi ghi. Không backdoor, không `*`.
  Audit qua `JobContextFactory.AuditFor(db, correlation)`; correlation `job:<tên>:<lease token>`.
- **Một luật, một biểu thức.** Điều kiện của job dùng cho cả lượt quét lẫn lượt đọc lại (`WorkOrderOverdueJob.OverdueOn`). Bản đầu đọc
  lại bằng một bản sao viết tay — phá thử luật quét thì **không test nào đỏ** vì bản sao che đi. Luật im lặng của thiết bị phải viết
  lại trong SQL (EF không dịch `IotOptions.StatusAt`), nên test đối chiếu thẳng với `StatusAt` ở **hai phía** ngưỡng.
- 🔴 **Test chạy job với đồng hồ năm 2001 và trồng dữ liệu 2000–2001.** Job quét MỌI xã của DB dùng chung: chạy với giờ thật sẽ ghi dấu
  quá hạn cho phiếu của class khác, và teardown của class đó vấp FK `Restrict`. `LuxMapTestHost.CreateHost` **ép** `Jobs:Enabled=false`
  và từ chối host nào vẫn bật — đừng gỡ để "test worker thật"; test worker dựng `ScheduledJobWorker` trực tiếp với job giả.
- `fault.origin_node_id` cấu hình ở **Telemetry** (`FaultOriginNodeConfiguration`), như `origin_observation_id` ở Survey: Faults không
  tham chiếu Telemetry; Telemetry nay tham chiếu Faults. FK ghép `(origin_node_id, commune_id)` → `iot_node`; index FK khai tường minh
  cạnh unique partial (quy tắc partial index). CHECK `ck_fault_node_source`: có thiết bị ⇒ `iot` và không cột.
- Teardown xoá `fault` (giữ `iot_node`) và `work_order_overdue_notice` (giữ `work_order`) **trước** cha. Rollback `AddScheduledJobs` gãy
  khi `notification` đã có `work_order_overdue` (CHECK dựng lại không nhận) — cùng họ BE-19.
- 🔴 **Mọi lời gọi DB của worker nằm TRONG vòng retry, kể cả bước tạo dòng lúc khởi động.** Exception thoát khỏi
  `BackgroundService.ExecuteAsync` với mặc định `StopHost` **dừng cả API** — DB khởi động chậm hơn API là đủ (review Codex F-1). Canh bằng
  `A_database_unavailable_at_startup_does_not_end_the_worker` (khoá bảng + `Command Timeout` 1 s; phá thử → đỏ).
- Test worker đợi **dòng `scheduled_job`** đạt trạng thái cuối rồi mới `StopAsync`: tín hiệu từ job đến TRƯỚC lượt `CompleteAsync`, và dừng
  sớm sẽ hủy chính lượt ghi đó (F-2).
- Job dùng `UtcMicrosecondClock` một lần mỗi lượt; một lượt quá hạn xử lý tối đa `Batch` (500) phiếu mỗi vòng, lặp tới khi hết.


### COMMUNE-BBOX — khung bản đồ của xã (14/10/2026, drift CB-1…CB-6)

- **bbox là KHUNG NHÌN, không phải ranh giới.** Đừng dùng nó để lọc, ẩn hay từ chối tài sản, và đừng suy `commune_id` từ nó: phân quyền
  vẫn một nguồn là cột `commune_id` (§7). Ranh giới thật (polygon) vẫn chưa có nguồn.
- Bốn cột `bbox_*` trên `administrative_unit` đọc/ghi qua **`GetBbox()` / `SetBbox()`** (EF không dịch được — đọc sau truy vấn). Trung điểm
  (`center`) và khung gộp (`CommuneBbox.Union`) **tính lúc đọc, không lưu**.
- `/admin/communes` nằm ở **module Admin** (nay tham chiếu Persistence). `AdministrativeUnit` không bị lọc theo xã — capability
  `ManageCommunes` là thứ duy nhất giữ endpoint cho Quản trị.
- `PUT /admin/communes/{id}` dùng **`[JsonRequired]`** để cả hai khoá bắt buộc mà `bbox` vẫn nhận `null` — khác khuôn `JsonElement` của
  `feeder_id` / `note`, vì `bbox` là object có schema riêng. Gỡ `[JsonRequired]` thì form thiếu khoá sẽ xoá khung (canh bằng
  `Put_needs_both_keys_and_an_explicit_null_clears_the_frame`).
- Test tạo xã đặt tên tiền tố `bbox-test-` và tài khoản riêng; teardown xoá **tài khoản trước, xã sau** (`app_user_commune` giữ xã).
- 🔴 **`AccountTestFactory.DeleteTestAccountsAsync()` xoá MỌI tài khoản tiền tố `acct-`, và `AccountAdminTests` gọi nó sau MỖI test.**
  Class khác dùng chung tiền tố hay gọi chung hàm đó sẽ xoá tài khoản của nhau khi chạy song song → test đỏ ngẫu nhiên và
  `40P01 deadlock` ở teardown (gặp thật ở COMMUNE-BBOX: 11 test đỏ, mỗi class chạy riêng thì xanh). Class mới: **tiền tố riêng** (`cbx-`)
  và chỉ xoá tiền tố đó. Cũng đừng assert **tổng số** xã / tài khoản — class khác tạo cùng lúc; kiểm theo id hoặc tên.

### POLE-LOCATION — dời cột (14/10/2026, drift PL-1…PL-5)

- **Ngưỡng hành lang có ĐÚNG MỘT nguồn: `SurveyProcessing:RouteCorridorM`.** Assets hỏi qua port **`ISurveyCorridor`** (Survey hiện thực,
  khuôn `ICabinetDeviceLookup`) — đừng khai hằng 25 hay option thứ hai ở Assets: cột dời quá ngưỡng của khảo sát sẽ **âm thầm** rơi khỏi
  mọi sweep sau (`SurveyChainageQuery.Poles` lọc theo đúng ngưỡng đó), không lỗi nào báo.
- **Khoảng cách tới tuyến đọc `RoadSegment` BỎ query filter** — tuyến `inter_commune` có thể thuộc xã người gọi không thấy (cùng lý lẽ
  WO-12, TOPO-INFER). Chỉ một `double` rời truy vấn. Bỏ `IgnoreQueryFilters` → 500 (`The_corridor_is_measured_from_a_segment_owned_by_another_commune`).
- **`Pole` KHÔNG phải `IAudited`**: audit `relocated` do service ghi chủ động, guard không ép. Làm `Pole` thành `IAudited` sẽ bắt **mọi**
  đường ghi cột (form, import, ghi chú, sync, seeder, fixture) kèm audit — quyết định riêng. Hệ quả: sửa `geom` qua form đầy đủ / import chưa có vết.
- Thứ tự: kiểm có cột **qua filter** (404) → `FOR UPDATE` → đọc lại → đo → `AssetStamp.Touch` (không đổi ⇒ không audit). Rollback
  `AuditPoleRelocation` gãy khi đã có audit `pole` / `relocated` (cùng họ BE-19).

### OPS-DEPLOY-VPS — Docker, Caddy, một VPS (15/10/2026, `docs/deploy/vps.md`, drift DEP-1…DEP-4)

- **Image build NGAY TRÊN SERVER** (`deploy/deploy.sh` → `docker compose build`), nên không có `-r` / RID nào: image luôn đúng kiến trúc
  của máy chạy nó. Migration là **EF bundle trong cùng image** (`/app/efbundle`), service `migrate` profile `tools`, chạy **sau** backup.
- `dotnet ef migrations bundle` **dựng host** để tìm DbContext, mà `Program` dừng khi thiếu cấu hình ⇒ Dockerfile đặt giá trị giả
  `build-only` **chỉ cho lệnh đó**. Thêm biến bắt buộc lúc khởi động thì thêm vào dòng đó, nếu không image không build được.
  ⚠️ `-c` của `dotnet ef` là **`--context`**, không phải cấu hình build (`No DbContext named 'Release'`) — dùng `--configuration`.
- 🔴 **`deploy/compose.yml` là project `luxmap-prod`, KHÔNG BAO GIỜ `luxmap`.** Compose dev mang `name: luxmap` và đặt tên volume tường minh
  `luxmap_postgres_data`; cùng tên thì stack production gắn vào **DB dev**, và `--remove-orphans` xoá container dev.
- 🔴 **Caddyfile gắn theo THƯ MỤC (`./caddy:/etc/caddy`), không theo file.** Trình soạn thảo và `git pull` thay file bằng inode mới; bind mount
  một file giữ inode cũ đã xoá ⇒ `caddy reload` lỗi và **cấu hình cũ chạy tiếp** (gặp thật lúc diễn tập).
- **Trong Caddyfile, mọi `handle` chạy trước `respond` trần** ⇒ chặn một đường dẫn con phải là `handle <đường dài hơn>`, không phải
  `respond @matcher` (đã thử: `/api/v1/internal/*` vẫn tới API).
- 🔴 **Body khai lớn hơn mức API nhận ra 502, không phải 413**, nếu không có gì chặn trước: API trả 413 ngay từ `Content-Length` rồi đóng kết nối
  trong lúc Caddy còn gửi body (`broken pipe`). Caddy tự trả 413 cho `Content-Length > 314572800` = `SurveyIngestService.ClipLimit`.
  **Đổi `ClipLimit` (hay thêm endpoint nhận body lớn hơn) thì sửa con số trong `deploy/caddy/Caddyfile` cùng diff.** Endpoint multipart
  (ảnh 16 MiB) không bị — API đọc rồi trả 400 bình thường (đo 120 MB: 3/3 lần 400).
- **`ForwardedHeaders__KnownProxies__0` = IP cố định của Caddy (`172.30.0.10`)**, đặt trong `compose.yml`. Đổi IP hay subnet của Caddy thì sửa cả
  hai cùng lúc; giá trị rỗng làm API **dừng lúc khởi động** (không phải IP), còn thiếu hẳn thì mọi client chung một xô giới hạn (phá thử đã đo).
- Cảnh báo DataProtection (`keys … may not be persisted`) lúc khởi động là vô hại: không code nào dùng DataProtection. Nếu sau này dùng
  (cookie auth, antiforgery) thì phải gắn volume cho thư mục khoá, nếu không mỗi lần deploy làm mất hiệu lực dữ liệu đã bảo vệ.
- Script chép dữ liệu lên server vẫn là `scripts/copy_dev_to_supabase.py` (đích = PostGIS của VPS qua SSH tunnel, `localhost` nên không đòi TLS);
  luật "bảng mới phải vào `PLAN`" vẫn áp.

### POLE-PHOTO-AI — ảnh một cột, duyệt rồi công bố (15/10/2026, drift PP-1…PP-10, nền tạm)

- Khi sửa AI ảnh đơn, dùng port Shared `ISingleLampPhotoHinter` và luật `YoloSingleLampPhotoHinter`; Citizen adapter chỉ đổi tên nhãn.
  Giữ khoá `Citizen:DaylightMeanLuma`, chuỗi băm `citizen-hint:single-lamp;daylight-mean-luma<=…` và prefix **cq** để không đổi phiên bản
  Citizen khi output giữ nguyên. Model resolve lazy; lỗi model trả null, cancellation truyền tiếp.
- Khi kiểm cột thuộc phiếu, dùng `WorkOrderPoleService.Targets` (cùng WO-12), không trang đầu. Xã/nguồn ảnh theo cột, có thể khác xã phiếu;
  mọi read ảnh đi qua phiếu cha trước. Hàng ảnh scoped theo xã chưa đủ chặn kỹ sư khác trong cùng xã.
- Khi ghi current-status, đi qua `Survey/Review/PoleStatusPublication.StageAsync` (sweep và ảnh dùng chung), caller khoá **hàng pole** cả
  khi chưa có current row. Thứ tự **work_order → pole → photo**; storage/model ở ngoài transaction, rồi khoá và kiểm lại quyền/target.
- **ON giữ dim/normal và confidence cũ nhưng tiến clock quan sát**, chặn ảnh OFF cũ ghi đè. Chỉ capture mới hơn
  `LastEvaluatedAt ?? LastSeenAt` mới áp; ảnh cũ/bằng vẫn confirm được nhưng không đổi row/sinh fault. Ảnh áp xong
  **LastSweepId/LastRunId=null**, không gán ảnh cho sweep; không ghi history/baseline. `classification_preserved` vẫn StatusApplied=true.
- Fault từ ảnh do WorkOrders cấu hình FK ghép ảnh/pole/xã; source CV, lamp_out, không trộn origin khác. D-5d chốt **confirmed**
  (sweep hiện vẫn detected); severity dùng cấu hình survey, chống trùng theo effective type + `FaultStatusSets.Open` dưới khoá cột.
  Source/xã của cột đổi sau upload →409 khi confirm. ON không đóng fault; ảnh không thay evidence after.
- Mỗi save **một** audit, transaction bao trọn upload/decision: submitted(User), analyzed(Cv), publication/applied(User),
  fault/created(User), decision/confirmed hoặc rejected(User). Save phân tích lỗi phải rollback cả ảnh và submitted.
  Replay/GET không infer hay audit lại; quyết định lần hai →409.
- Khi sửa giới hạn ảnh, giữ cap 40 MP **trước decode** ở `IOriginalJpegReader` kể cả thiếu model. Body 17 MiB được chặn trước binding
  bởi `PolePhotoBodyLimit`; Caddy phải giữ cùng guard Content-Length riêng route để tránh 502 từ early 413 (xem PP-8).
- 🔴 **Body JSON khai bằng `sealed class` + property `init`, KHÔNG positional record có attribute kiểm tra trên tham số.**
  `PolePhotoDecisionRequest` dạng record làm MVC ném `No property found that maps to constructor parameter 'Note'` → **500**, chỉ ở
  lượt chạy toàn bộ (request đầu tiên đồng thời); chạy riêng test thì xanh 10/10. Khuôn đúng là các request ở `WorkOrderRequests.cs`.
- **DB-only** `ak_pole_photo_target` / `fk_pole_photo_same_commune` không ở EF snapshot: bảo toàn khi sinh migration sau, canh catalog/FK.
  Dọn fault **trước ảnh**, ảnh trước work_order/pole/user, audit trước actor/xã. PLAN copy phải giữ parent/users của ảnh và fault có nguồn ảnh.
  Rollback migration mất metadata/kết quả/quyết định và origin ảnh của fault; object/audit không tự mất. Trên môi trường có dữ liệu ưu tiên
  rollback ứng dụng, giữ schema; apply/down/reapply chỉ kiểm trên container test riêng trống.
