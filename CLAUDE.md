# LuxMap — Backend (WP2)

Nền tảng GIS + IoT + Computer Vision quản lý tài sản và sự cố chiếu sáng đường nông thôn. Đề tài **LuxMap: A GIS, IoT
and Computer Vision Platform for Rural Road Lighting Asset and Fault Management** (Phiếu FA26SE222 v1.4,
`docs/registration/FA26SE222_v1.4.md` — nguồn chuẩn cho actor, chức năng, NFR, deliverable; bản v1.2 là lịch sử).
Capstone W1–W21: 07/09/2026 – 31/01/2027 · Repo này là **WP2, phụ trách: BE1 – Mỹ**.

**Ai gọi API:** Web SPA (WP5), Android native (WP6), engine CV (WP4) — ba consumer ứng dụng; cộng **thiết bị IoT testbed**
(`/api/v1/device/…`, scheme `Device`) và **broker MQTT** (callback xác thực, scheme `Broker`). **Công dân không có tài khoản**:
báo sự cố qua mã QR trên cột (`/public/…`, không token), Quản lý duyệt ở `/citizen-reports`.

**Phiếu v1.4 (27/09/2026) — kênh đo độ sáng.** CV chỉ quyết **ON/OFF**; độ sáng đo bằng **BH1750 gắn trên xe khảo sát**, lấy mẫu
liên tục suốt sweep; **`dim` = CV nói ON + đỉnh lux lúc ngang cột dưới ngưỡng baseline của chính cột**. Mỗi phiên = video + log lux
BLE + GPS track + cấu hình quay, chung đồng hồ `elapsedRealtimeNanos`, server xử lý toàn bộ. Trạng thái quyết định (`docs/contract-drift.md`
mục *Registration v1.4* và *Quyết định khảo sát 07/10*): D-R20 / D-R22 / D-R23 **chốt 07/10**; D-R21 / D-R24 **SELF-SIGNED 27/09**;
D-R28 = **proxy streaming qua API** (BE-15 P1, 02/10); D-R25 (`calibration_rig`) và D-R27 **còn chờ**.

## Nguồn sự thật

Thứ tự ưu tiên khi mâu thuẫn:

1. **`docs/api-contract-v1.1.md`** — bản hợp nhất **v1.15** (tên file giữ để liên kết cũ còn đúng); bản máy đọc là
   `docs/openapi/luxmap-v1.5.json` (file SINH — xem `docs/agents/traps/assets.md` mục BE-REVIEW-02 số 8).
2. **Quyết định đã chốt trong `docs/contract-drift.md` nhưng chưa hợp nhất vào Contract thắng chữ của Contract** (Mỹ ký 20/10/2026).
   Đây là cách repo đang chạy: thiết bị IoT ở tủ điện (I-18), sort mặc định sự cố (P-3), `out_threshold_ratio = null` (D-R22).
   Quyết định `SELF-SIGNED` chạm bề mặt API là **nền tạm** cho tới FW kế tiếp — ticket xây lên nó phải ghi rõ.
3. **`docs/tasks-backend.csv`** — task list v2.1, phạm vi và lịch.
4. **File này + `docs/agents/traps/*.md`** — quy ước làm việc và những chỗ dễ sai. Không phải đặc tả.
5. **`.ai/`** — đứng cuối.

Phát hiện mâu thuẫn: làm theo thứ tự trên, ghi chỗ lệch, nêu ở buổi review — đừng tự chọn bên.

### Ghi phát hiện vào ĐÚNG tầng

Nguyên tắc vận hành quyết định (FW-00, 07/09/2026): tóm tắt ở đầu `docs/contract-drift.md`, nguyên bản ở
`docs/archive/contract-drift-v1.md`. Mục áp thường xuyên nhất:

| Loại phát hiện | Ghi vào |
|---|---|
| **Deviation** — code / mock lệch Contract | `docs/contract-drift.md` |
| **Luật áp nhiều ticket** cho FE / BE / mobile | `docs/api-contract-v1.1.md`, **chỉ sau khi duyệt và tăng version** |
| **Ràng buộc kỹ thuật nội bộ** — bẫy, quy ước, thứ dễ sai âm thầm | File này (luật chung) hoặc `docs/agents/traps/<vùng>.md` (luật của một vùng) |
| **Tiến độ** — trạng thái ticket, việc tồn đọng | `tracking.html` |

Ghi sai tầng cũng tệ như không ghi: ràng buộc nhét vào `tracking.html` trôi mất khi mục đóng; deviation nhét vào đây không bao giờ
tới WP5/WP6 (họ không đọc file này); quyết định nội bộ đẩy thẳng vào Contract là đổi thứ FE đã code theo mà chưa ai duyệt.
Quyết định **không ghi vào repo thì coi như chưa xảy ra**. Im lặng quá 3 ngày làm việc là **approve** với thay đổi không chạm bề mặt
API, **ESCALATE** với thay đổi có chạm — không bao giờ là approve.

## Tra cứu theo vùng — đọc file tương ứng TRƯỚC khi sửa code ở vùng đó

| Vùng code | File |
|---|---|
| Entity mang `commune_id`, query filter, `CommuneWriteGuard`, FK cascade, backdoor | `docs/agents/traps/commune-scope.md` |
| Tài sản (`Modules.Assets`): CRUD, import, topology, tủ điện, khung xã, dời cột, bảo hành, bộ mock | `docs/agents/traps/assets.md` |
| Truy vấn không gian, endpoint `/map/…`, chi tiết cột, độ phủ sweep | `docs/agents/traps/spatial-map.md` |
| Ảnh / object store / thumbnail / ImageSharp / MinIO | `docs/agents/traps/media-storage.md` |
| Khảo sát (`Modules.Survey`), lux, model YOLO (`Modules.AI`), ảnh cột trong phiếu | `docs/agents/traps/survey.md` |
| Sự cố, phiếu công việc, audit, thông báo, job nền, đồng bộ offline | `docs/agents/traps/faults-workorders.md` |
| Auth, tài khoản, mời qua email, giới hạn đăng nhập | `docs/agents/traps/auth-accounts.md` |
| Công dân: mã QR, `/public/…`, hàng chờ duyệt | `docs/agents/traps/citizen.md` |
| IoT, scheme `Device`, lệnh ON/OFF/AUTO, MQTT | `docs/agents/traps/iot-lighting.md` |
| Migration, index, CHECK, cột số thực, ghi hàng loạt | `docs/agents/traps/migrations.md` |
| Test, fixture, teardown, sequence, test host | `docs/agents/traps/testing.md` |
| Docker / VPS / Caddy, CI, script chép DB | `docs/agents/traps/deploy-ops.md` |

Ràng buộc kỹ thuật mới của một vùng → ghi vào file của vùng đó; luật áp **mọi** vùng → ghi vào file này.

## Giao thức `.ai/` — nhiều agent

Repo được Claude Code và Codex cùng phục vụ. **`AGENTS.md` là symlink tới file này** (Codex đọc `AGENTS.md`) — giữ nó là symlink.
Trước một ticket đọc: `.ai/context/sources.md` → `.ai/tasks/<ticket>.md` (phạm vi, tiêu chí xong, **KHÔNG ĐƯỢC làm**) →
`.ai/context/commands.md` (đầy đủ: `docs/development.md`; lần đầu: `README.md`). Kết quả `.ai/results/<ticket>.md`, review
`.ai/reviews/<ticket>-by-<agent>.md`; vòng đời và quy tắc hai pha ở `.ai/README.md`. `.ai/` **không nhận** bốn loại phát hiện ở bảng
trên. Codex build đồng thời trong worktree sandbox: build / test ở cây chính bằng `--disable-build-servers` (hoặc
`MSBUILDDISABLENODEREUSE=1`) — nếu không, MSBuild dùng lại node của Codex và lỗi `MSB3021 Access … denied`.

## Phạm vi và `data_source`

| Ở đâu | Làm gì | `data_source` |
|---|---|---|
| **Ngoài thực địa** (xã đối tác) | Quay video đêm đèn thật, đo lux bằng BH1750 trên xe, kiểm tra bằng mắt ban đêm. **Không** lắp thiết bị, **không** thao tác lưới chiếu sáng của xã | `field` |
| **Testbed tự dựng** (đèn LED) | Toàn bộ IoT, demo điều khiển ON/OFF/AUTO | `calibration_rig` (Contract O-9) |
| **Khởi động AI** | Ảnh đêm công khai | `public_imagery` |
| Telemetry mô phỏng | Runtime khi chưa có thiết bị | `simulated` |

Controlled Reference Capture Set **rời deliverable** ở v1.4; giá trị `calibration_rig` **giữ** trong enum (D-R25, chờ quyết, không seed thêm).

- 🔴 **Dữ liệu testbed không bao giờ mang `field`.** Gộp thí nghiệm có kiểm soát vào số liệu hiện trường là đúng lỗi luật này sinh ra để chặn.
- Nguồn dữ liệu giữ **tách bạch từ lúc ingest tới lúc thống kê**: không lưu nguồn ngay ở sweep / fault thì tới báo cáo không tách ra được
  nữa. Mọi API thống kê lọc và nhóm được theo `data_source` (Contract mục 1).
- `data_source` có trên dữ liệu đo / sự kiện (sweep, frame, fault, telemetry, lux) **và** trên tài sản (`pole`, `fixture`, `road_segment`,
  tủ điện): ở tài sản nó **lưu và lọc được nhưng không emit ra `properties` bản đồ**.
- **`source_channel` là chiều khác:** kênh phát hiện (`cv` / `iot` / `field_report`), còn `data_source` là dữ liệu đến từ đâu. Một sự cố
  có thể `cv` + `calibration_rig` cùng lúc.

## Quy ước API (Contract mục 0)

- Base URL `/api/v1`. JSON **snake_case** (`JsonNamingPolicy.SnakeCaseLower`). Lỗi `{ "error": { "code", "message", "details" } }` +
  correlation id. Phân trang `?page=1&page_size=50` → `{page, page_size, total, items[]}`, `page_size` ≤ 200. Auth `Bearer <jwt>`.
- **Enum là chuỗi thường**, trên dây lẫn khi đọc: `LuxMapJsonOptions` giữ `allowIntegerValues: false` (từ chối `2` lẫn `"2"` → 400).
  Query string đi qua **`WireEnum.ParseCsv` / `WireEnum.Name`** — `Enum.TryParse` không đọc được giá trị nhiều từ (`calibration_rig`).
- **Thời gian: ISO 8601 UTC, hậu tố `Z`**; DB `timestamptz`, Npgsql đòi `DateTimeKind.Utc` — xử lý ở biên. Thời điểm app sinh rồi trả
  thẳng từ entity đang track: dùng **`UtcMicrosecondClock.UtcNow()`** (Postgres chỉ lưu µs; Linux có tick 100 ns).
- **Ngày không giờ `YYYY-MM-DD`.** Ngày / giờ trên query string nhận bằng kiểu `DateOnly` / `DateTime` (đi qua
  `IsoDateQueryModelBinderProvider` + `UtcNormalization.ToUtc`), không nhận `string` rồi tự parse — binder mặc định đọc `06/10` thành 10/06.
- **Số thực trên JSON:** `FiniteDoubleConverter` chặn `"NaN"` / `"Infinity"` — giữ nó (`JsonNumberHandlingTests`).
- **ID là chuỗi có prefix** (`POLE-0001`, `FAULT-0001`, `SEG-001`, `COM-001`), sinh ở DB bằng
  `DEFAULT luxmap_format_id('POLE', nextval('pole_id_seq'), 4)`; bảng prefix ở `PrefixedId.cs`. Không dùng `LPAD(…, 4, '0')`: nó **cắt**
  giá trị dài hơn độ rộng (12345 → `1234`), sinh ID sai / trùng.
- **ID là TỐI THIỂU N chữ số:** cột thứ 10000 là `POLE-10000`. Vì vậy **không bao giờ `ORDER BY` ID trần** (`POLE-10000 < POLE-9999`),
  kể cả làm tiebreaker, và không lọc khoảng trên ID chuỗi; khuôn đúng **`ORDER BY created_at, length(id), id`** (`now()` là đầu transaction nên cả mẻ chung `created_at`).
  Test thứ tự phải assert **có thứ tự**, trên ID hai bên ngưỡng độ rộng, ghi trong cùng transaction (`PrefixedIdOrderingTests`).
  Validator phía client: `^POLE-[0-9]{4,}$` (`[0-9]`, không `\d`).
- **Client không sinh ID hiển thị.** Thao tác offline mang `client_op_id` (UUID); server gán ID thật và trả lại.
- **GeoJSON:** endpoint bản đồ trả `FeatureCollection`, toạ độ `[lng, lat]`, dữ liệu nghiệp vụ **phẳng** trong `properties`, **không
  dùng `feature.id`** (dùng `properties.pole_id`).
- **Toạ độ:** API luôn EPSG:4326. EPSG:3405 (VN-2000) chỉ trong SQL và báo cáo xuất — không bao giờ ra API. Không tính khoảng cách bằng
  trừ toạ độ (`docs/agents/traps/spatial-map.md`).

## Enum — khoá cứng (Contract mục 1)

```
fixture_status : normal | dim | out | unknown
power_source   : grid                            # v1.6 — solar đã bỏ
fixture_type   : led_road_lamp                   # v1.6 — solar_all_in_one đã bỏ
fault_type     : lamp_out | lamp_dim | segment_outage | node_offline | runtime_decline
fault_status   : detected | confirmed | rejected | in_progress | resolved | verified
severity       : low | medium | high | critical
source_channel : cv | iot | field_report
data_source    : field | public_imagery | calibration_rig | simulated
wo_status      : open | assigned | in_progress | done | verified | cancelled
node_role      : segment_controller
node_status    : online | offline | never_reported
road_class     : inter_commune | inter_village
```

**Không thêm giá trị, không đổi tên, không dùng int** — FE đã hardcode. Giá trị không gì sinh ra được thì **xoá khỏi enum** (như solar ở
v1.6), không để treo: CHECK ở DB sẽ cho ghi nó.

- **`unknown` không phải lỗi** — lần quan sát công bố gần nhất không phủ được cột. Ký hiệu riêng ở FE, **không gộp vào `out`** ở bất kỳ
  thống kê nào.
- **Nguồn `fault_type`:** `runtime_decline`, `node_offline` chỉ từ IoT. `lamp_out` / `lamp_dim` từ **sweep đã duyệt** (`cv`), từ **kỹ sư
  báo tại chỗ** (`field_report`, BE-41), và từ **ảnh cột được Quản lý xác nhận** (`cv`, hoặc `field_report` khi kỹ sư đổi nhãn AI).
  Một cột mang được nhiều loại cùng lúc. Contract `:285` và `:703` đang nói khác nhau về điểm này (drift CLAUDE-MD-1).
- **Luồng `fault_status`:** `detected → confirmed | rejected`, `confirmed → in_progress → resolved → verified`. Sai luồng → **409** để FE
  disable nút trước.
- **Sự cố "mở"** = `detected | confirmed | in_progress`, định nghĩa ĐÚNG MỘT chỗ: `FaultStatusSets.Open` / `FaultStatusSets.IsOpen`.
  Không viết lại tập này ở SQL, LINQ hay test.
- **Enum lưu dạng chữ** (`HasContractEnum`): `OrderBy(x => x.Severity)` sắp theo bảng chữ cái (`critical, high, low, medium`). Sắp theo
  mức bằng hạng tường minh dạng `Expression` để EF sinh `CASE` (`FaultQueryService.SeverityRank`); test trồng giá trị theo thứ tự mà
  sắp chữ cái sẽ lộ sai.
- **Mô hình IoT:** thiết bị chỉ ở **tủ điện tổng**, nối tới đèn qua rơ-le gộp vào feeder (`feeder_control`); không IoT trên từng cột,
  không `battery_pct`; `has_iot_node` luôn `false`. Contract §5.6 chưa cập nhật (drift I-18) — ticket IoT theo drift.

## API đã đặc tả — luật bề mặt

Hình dạng request / response: tra Contract và `docs/openapi/luxmap-v1.5.json`, không chép vào đây. Luật dễ làm sai:

| Endpoint | Luật |
|---|---|
| `GET /map/poles`, `/map/segments`, `/map/iot-nodes` | Dưới **`/api/v1/map/`** (drift MAP-1), không phải `/poles`. `bbox` **bắt buộc** — không có endpoint "lấy tất cả"; quá 2000 cột → **413 `BBOX_TOO_LARGE`** |
| `GET /map/poles/{id}` | **Đủ trong 1 request** (BE-20): bóng đang dùng, trạng thái hiện tại, baseline, `luminance_history[]`, sự cố mở, frame người gọi mở được. Ngoài phạm vi xã → 404 |
| `GET /faults` | **Phân trang JSON, không GeoJSON**; mỗi item có `location{lat,lng}`. Sort mặc định **`-severity`** (drift P-3) |
| `PATCH /faults/{id}` | `fault_status`, `override_fault_type`, `severity`, `note`. `fault_type` không bao giờ sửa — phân loại lại vào `override_fault_type` |
| `POST /faults` | Chỉ Kỹ sư hiện trường. Có cột → xã / tuyến / `data_source` **lấy từ cột**; không cột → bắt buộc `location`. `client_op_id` gửi lại → 200 cùng sự cố |
| `POST /work-orders` | `task_kind` bắt buộc; survey nhận `commune_id` + `segment_ids`, inspection / repair không nhận `commune_id` |
| `POST /work-orders/{id}/follow-up` | Chuỗi khảo sát → kiểm tra → sửa chữa chung `case_id`; server tự mang sự cố sang (FR-2, WSC) |
| `GET /work-orders/{id}/poles` | Cột trên tuyến được giao ∪ cột mang sự cố của phiếu, trạng thái **lần quan sát công bố gần nhất**, theo thứ tự dọc đường (`position` là thứ tự, không phải khoảng cách) |
| `POST /work-orders/{id}/evidence` | `before`/`after` chỉ ở phiếu sửa chữa, `observation` chỉ ở phiếu kiểm tra; repair không `complete` được khi chưa có ảnh `after` (409) |
| `GET /sync/bundle`, `POST /sync/push` | Chỉ `field_engineer`; bundle là bản chụp đầy đủ (gửi `since` → 400); push đi qua **service của endpoint gốc**, kiểm capability **từng thao tác** |

- **Trạng thái hiện tại của cột** là **lần quan sát đã công bố gần nhất** — từ sweep được duyệt **hoặc** ảnh cột được Quản lý xác nhận
  (cùng writer `PoleStatusPublication`); chưa có thì `unknown`.
- **`baseline_ratio` và `classified_as` tính ở backend**, trên từng điểm `luminance_history` — FE không tự tính. `dim_threshold_ratio`
  (mặc định 0.80) cấu hình được, không hard-code. **`out_threshold_ratio` luôn `null`**: `out` do CV OFF quyết, ratio chỉ dùng cho `dim` (D-R22).
- `has_active_segment_fault = true` → FE tô **cả tuyến** (sự cố cấp đoạn, CV-15).
- **Task chưa có đặc tả → đặc tả trước khi hiện thực**, đừng tự sinh endpoint rồi coi như xong. Còn để mở: vector tile khi vượt ~5000 cột,
  realtime khi sweep xong (hiện polling).
- **Lưu trữ (D-R16):** ảnh, telemetry, fault giữ tối thiểu qua hết bảo hành; hết bảo hành **không** xoá — chỉ gắn cảnh báo, tính lúc đọc
  từ `fixture.warranty_expiry` (nullable: "không biết" khác "còn bảo hành"), định nghĩa duy nhất ở `WarrantyWindow`.

## Mô hình dữ liệu

Lược đồ đích theo Phiếu v1.4: **`docs/database/erd.md`** (quyết định Q1–Q17) — ticket tạo bảng mới đọc nó trước; đổi một quyết định Q-x
thì ghi lại.

- **`Pole` và `Fixture` tách riêng.** Cột là kết cấu, bóng là thiết bị thay được; lịch sử trạng thái thuộc **vị trí cột**, nên `Fixture`
  không có cột trạng thái nào. Mỗi cột tối đa **một bóng đang dùng**.
- **`SurveyFrame` và `RepairEvidence` là hai luồng ảnh riêng.** Ảnh khảo sát là dữ liệu chính, không phải file đính kèm.
- **Ảnh không nằm trong DB**: object store giữ bytes, row giữ key.
- **`external_ref`** (khoá tự nhiên của import) là **mã vĩnh viễn** do nhóm quản lý; mã kiểm kê của xã đi cột riêng (`docs/agents/traps/assets.md`).
- **Sự cố là sự kiện đã xảy ra:** FK tới nó và từ nó là `Restrict`. Mặc định của repo là **mọi FK `Restrict`**; cascade là ngoại lệ có tên
  (xem phạm vi xã bên dưới).

## Phạm vi xã (commune scope)

Chi tiết và lý do: `docs/agents/traps/commune-scope.md`.

- Entity **có thể là gốc của một truy vấn** mang `commune_id` và implement **`ICommuneScoped`**; entity luôn đi tới qua một gốc đã scope
  thì không. Mọi cột `commune_id` khai **`HasCommuneReference()`** (FK thật tới `administrative_unit`, `Restrict`, không navigation) —
  chốt chặn lúc dựng model quét theo cột.
- **Đọc** được canh bởi `HasQueryFilter` (đưa `commune_id` vào `WHERE`). **Ghi** được canh bởi **`CommuneWriteGuard`** ở `SaveChanges`
  (Added / Modified cả giá trị cũ lẫn mới / Deleted). Entry point gọi **`CommuneFilter.Narrow`** để trả 403 có thông điệp. Cần cả hai lớp.
- **`commune_id` do server suy từ cha** (cột → bóng, ảnh, lux, sự cố có cột); client gửi → 400. Guard chỉ kiểm commune **trong scope**, không
  kiểm commune **khớp cha**.
- Bỏ qua guard chỉ bằng **`EnterUnscopedSystemWriteBackdoor()`**, nhìn thấy ở call site. **Scope rỗng không bao giờ là quyền** — đó là scope
  của caller chưa đăng nhập.
- 🔴 **Guard không thấy cascade do DB thực hiện.** Cascade hiện an toàn vì cùng xã (cột → bóng, trạng thái cột) hoặc không thuộc xã nào
  (`account_token`, `sync_operation`). Thêm cascade giữa hai bảng **có thể khác xã** phải kèm cơ chế canh riêng trong cùng migration.
- `AdministrativeUnit` **không** bị lọc theo xã (nó là mốc neo); endpoint liệt kê xã truy vấn tường minh theo claim.
- Phạm vi đọc từ claim `commune_ids` của JWT: đổi xã / vai trò có hiệu lực khi access token được cấp lại (≤ 60 phút), không phải ngay.
- `ExecuteUpdate` / `ExecuteDelete` bị cấm ở compile-time vì đi vòng ChangeTracker (`docs/agents/traps/migrations.md`).

## Vai trò và phân quyền

| Vai trò | `user_role` | Tài khoản demo |
|---|---|---|
| **Cấp giám sát** — bản đồ, thống kê, báo cáo; **chỉ đọc** | `superior` | `agency` |
| **Quản lý** — tài sản, giao việc, duyệt sự cố / khảo sát / ảnh, điều khiển đèn testbed | `manager` | `engineer` |
| **Kỹ sư hiện trường** — khảo sát đêm, kiểm tra, sửa chữa | `field_engineer` | `crew` |
| **Quản trị hệ thống** — tài khoản, vai trò, cấu hình, xã | `system_admin` | `admin` |

Username seed và biến `SEED_*_PASSWORD` giữ tên cũ (định danh, không phải nhãn).

- **Capability, không thứ bậc.** Ma trận ở **`LuxMapPolicies.Matrix`** (`LuxMap.Shared`) là nguồn duy nhất; mỗi capability là **danh sách
  vai trò được nêu tên** (`RequireClaim(role, …)`, OR). Mọi endpoint nghiệp vụ gắn `[Authorize(Policy = …)]` một capability; fallback
  ("đã đăng nhập") chỉ cho `GET /auth/me`. Capability mới thêm **cùng ticket** với endpoint của nó.
- 🔴 **Capability rỗng là MỞ** (`RequireClaim(role)` không giá trị nhận mọi vai trò) — `AuthorizationSetup` từ chối khởi động; muốn bỏ thì xoá.
- 🔴 **Test kỳ vọng là LITERAL**, không đọc từ ma trận (`RoleCapabilityMatrixTests`, `CapabilityMatrixTests`); đổi quyền là sửa cả hai cùng diff.
  `CapabilityPolicyCoverageTests` đỏ khi endpoint thiếu capability; endpoint của **máy** (thiết bị, broker) là ngoại lệ có tên trong
  `MachineEndpoints`.
- 🔴 **`[AllowAnonymous]` gắn trên TỪNG action, không bao giờ cấp class** — cấp class thắng `[Authorize]` cấp method, và test hành vi không
  bắt được; canh bằng assert metadata route (`docs/agents/traps/auth-accounts.md`).
- **403 có hai mã:** `ROLE_FORBIDDEN` (policy vai trò) và `COMMUNE_FORBIDDEN` (địa bàn, và claim `*` lệch vai trò).
- **Không tự đăng ký** (D-R11): Quản trị tạo tài khoản, người dùng nhận email mời và tự đặt mật khẩu.
- **Công dân không có tài khoản, không có vai trò** (D-R1); báo cáo QR vào **hàng chờ riêng**, Quản lý duyệt rồi mới thành `fault`.
  Không thêm giá trị `source_channel` cho công dân.
- **Điều khiển ON/OFF/AUTO** (D-R7): chỉ Quản lý (`ControlLighting`), chỉ thiết bị `supports_remote_control = true` — tức thiết bị
  **testbed**; mọi lệnh ghi audit. Không viết như thể hệ thống điều khiển lưới chiếu sáng của xã.
- `LuxMapPolicies` đặt ở `LuxMap.Shared` là quyết định kiến trúc **chờ FW-00** (`docs/agents/traps/assets.md`, BE-12a quy tắc 4).

## Bẫy chung — áp mọi ticket

Vi phạm thì build và test vẫn xanh; chi tiết ở file vùng được trỏ.

- **Migration:** đọc `Up()` / `Down()` **trước** khi apply — không `DropIndex` / `DropColumn` / `AlterColumn` mình không yêu cầu, `Down()`
  đối xứng. Index dẫn đầu bằng `commune_id` (kể cả partial) → khai lại `HasIndex("CommuneId")`. Tên index cần đúng → `HasDatabaseName`;
  Postgres cắt tên > 63 ký tự. `IsRowVersion()` sinh `AddColumn xmin` phải bỏ. Ràng buộc chỉ có ở DB không nằm trong snapshot.
  → `migrations.md`
- **Cột `double precision` đo được** có CHECK loại `NaN` và `±Infinity` (`x <> 'NaN'::float8 AND x <> 'Infinity'::float8`); `>= 0` không
  đủ, `x = x` vô dụng ở Postgres. → `migrations.md`
- **Khoảng cách** chỉ qua `SpatialFunctions.DistanceMeters`; lọc thô `ST_Intersects` trên cột 4326 trần **trước**, tinh chỉnh khoảng cách
  3405 sau; không `Geometry` đã transform nào rời SQL. → `spatial-map.md`
- **Ảnh:** mọi byte qua API (không presigned URL); ghi object **trước**, commit row **sau**; nhận JPEG theo **magic bytes**, decode chỉ qua
  `ThumbnailFactory.JpegOnly` (tiền đề tắt advisory ImageSharp); thumbnail bỏ metadata **tường minh**. → `media-storage.md`
- **Audit (D-R13):** entity `IAudited` → **đúng một** `AuditEvent` mỗi `SaveChanges`, stage bằng `IAuditTrail.Record` trên cùng DbContext,
  không save riêng. Bảng audit append-only. → `faults-workorders.md`
- **Khoá hàng:** đọc qua query filter **trước** (ngoài phạm vi → 404, không chờ khoá), rồi `ExecuteSql("SELECT 1 … FOR UPDATE")`, rồi đọc lại
  bằng LINQ. Không `FromSql("SELECT * … FOR UPDATE")` trên entity có `xmin`. Thứ tự khoá cố định trong một luồng; không giữ khoá qua stream
  upload hay chạy model. Trong transaction, bắt lỗi xong **rollback trước** mọi truy vấn khác.
- **Ranh giới module:** không thêm project reference ngược chiều phụ thuộc; hỏi qua **port** khai ở module cần, module sở hữu hiện thực
  (`IActiveWorkOrderLookup`, `ICabinetDeviceLookup`, `ISurveyCorridor`, `IPolePhotoPublication`, `ISurveyFindingLookup`).
- **Luật một định nghĩa:** tập trạng thái mở (`FaultStatusSets`), bảo hành (`WarrantyWindow`), ngưỡng hành lang khảo sát
  (`SurveyProcessing:RouteCorridorM`), ngưỡng offline thiết bị (`IotOptions`) — gọi, không chép.
- **Kiểm tra nằm trong service** khi có kênh khác controller gọi (sync, MQTT, job): DataAnnotations của controller không chạy ở đó.
- **Body JSON là `sealed class` + `init`**, không positional record có attribute kiểm tra trên tham số (500 ở request đồng thời đầu tiên).
- **Swagger:** một schemaId một kiểu .NET qua mọi module; hai `ProducesResponseType` cùng mã thì cái sau **thay** cái trước;
  `gen_consolidated_spec.py` có schema viết tay đè schema sinh. Sau khi đổi endpoint: xuất `luxmap-v1.json` → chạy generator → lint
  (`.ai/context/commands.md`). Object nullable xuất ra là `$ref` trần — hạn chế chung, đã ghi ở tracking.
- **`ExceptionHandlingMiddleware.WriteAsync` gọi `Response.Clear()`** — xoá cả header. Lỗi cần header riêng (`Retry-After`, challenge
  `Device`) tự ghi body.
- **Thông báo** stage bằng `Notifier.Stage` trong `SaveChanges` của thao tác nghiệp vụ; văn bản tự do đưa vào thông báo qua `Notifier.Clip`
  (cắt an toàn cặp surrogate).
- **Bảng mới** → thêm vào `PLAN` của `scripts/copy_dev_to_supabase.py` trong cùng PR (script dừng khi gặp bảng lạ). → `deploy-ops.md`

## Test

Chi tiết: `docs/agents/traps/testing.md`.

- Mọi factory của `LuxMap.Api.Tests` kế thừa **`LuxMapTestHost`** (container PostGIS riêng mỗi lượt chạy); test không chạm DB dùng
  `NoDatabaseTestHost`. Không bao giờ chạy test trên `luxmap_dev`; `.env` không chứa `ConnectionStrings__LuxMap`.
- Class ghi tài sản nằm trong **`AssetDatabaseCollection`** (chung `pole_id_seq` trong một lượt) — đừng tách collection. **Không ghim ID
  bằng literal**; lùi sequence thì trả về chỗ cũ trong `finally`.
- Test đổi trạng thái tài khoản tự tạo tài khoản riêng; không sửa tài khoản seed dùng chung. Tiền tố tài khoản / xã riêng mỗi class.
- Teardown xoá theo thứ tự FK `Restrict` (bảng con trước cha, tài sản trước tài khoản). Dữ liệu khảo sát bất biến không xoá được — đừng
  gắn nó vào tài khoản của fixture.
- SQL có số thực độc lập culture (`string.Create(CultureInfo.InvariantCulture, …)` hoặc parameter).
- Mỗi chốt chặn mới có **phá thử**: phá chốt → test đỏ → khôi phục bằng `cp` / `touch` (không `mv` bản sao). Đọc **mã thoát** của `dotnet test`,
  không chỉ dòng `Passed!`. Đổi branch rồi thì build lại trước khi kết luận (`--no-build` chạy bin cũ).
- Test chứng minh "có lấy khoá" giữ khoá bằng `FOR NO KEY UPDATE`, không `FOR UPDATE` (FK tự lấy KEY SHARE nên `FOR UPDATE` cho xanh giả).

## Quy tắc dễ sai âm thầm

Vi phạm thì hệ thống vẫn chạy, số liệu vẫn ra, nhưng kết quả nghiên cứu vô nghĩa.

- **Cấu hình quay kiểm ở cấp phiên** (D-R24: ISO, shutter, lấy nét, WB cố định; tắt EIS / HDR / night mode). Phiên thiếu hoặc sai cấu hình
  → từ chối kèm lý do; không nhận, không suy đoán. Ảnh auto-exposure không sửa được sau khi quay.
- **`dim` là tỉ lệ so với baseline của chính cột đó**, không bao giờ là lux tuyệt đối; `out` do CV OFF. Lux thủ công (`LuxReading`) chỉ dùng
  so **tương đối** giữa cột / đêm theo quy trình đo cố định — không phải lux tuyệt đối, không dùng chấm đạt / không đạt chuẩn chiếu sáng.
- **`unknown` đếm riêng, không gộp vào `out`.**
- **Mọi phát hiện tự động ghi kèm phiên bản model / artifact / firmware** (BE-34). Không tái lập được thì không phải kết quả.
- **Mọi phát hiện tự động và mọi quyết định của người vào audit trail** (D-R13). Cột `confirmed_by` / `resolved_by` trên `fault` chỉ là
  quyết định mới nhất.
- **Precision / recall báo RIÊNG từng lớp** (ON/OFF của CV; Normal / Dim / Out so với field verification). Con số gộp giấu lớp khó.
- **Báo tỉ lệ cột có mạch điện được xác nhận** (`feeder_source = verified`); cột không gán được mạch thì gom cụm theo hình học và ghi độ
  tin cậy giảm.
- **Sự cố cấp đoạn là một nguyên nhân**, không phải N sự cố bóng (CV-15: `cluster_id`, `segment_outage`).
- **Telemetry ingest idempotent theo `(node_id, reading_time)`** — gửi trùng là bình thường.
- **Phiên đêm cắt qua nửa đêm.** Không tính runtime theo ngày lịch.
- **Thiết bị im lặng quá ngưỡng → sự cố `node_offline`.** Im lặng là tín hiệu.

## Stack và database

- .NET modular monolith, ASP.NET Core Web API, `/api/v1`. PostgreSQL + **PostGIS** (không pgvector); EF Core + Npgsql + NetTopologySuite
  (`Point`, `LineString`).
- Object store S3 (MinIO local, `AWSSDK.S3`), ảnh qua ImageSharp **3.x** (4.x đòi license key). Video qua ffmpeg / ffprobe.
- Model YOLO ONNX (`Microsoft.ML.OnnxRuntime`). MQTT (`MQTTnet`, broker EMQX, tuỳ chọn). Mail SMTP (MailKit). Serilog, Swagger + JWT.
- **Job nền: bảng `scheduled_job` + lease trên DB** (BE-26), không Hangfire. Redis có trong compose nhưng chưa có consumer.
- Test tích hợp: Testcontainers + PostGIS thật.
- Tên bảng / cột **`snake_case` chữ thường, không quote**. Mọi `timestamptz` đọc / ghi `DateTimeKind.Utc`. `geometry(Point|LineString,4326)`
  có **GIST index**; query `bbox` đi qua index (kiểm bằng `EXPLAIN` trên SQL EF sinh); endpoint bbox **< 500 ms với 2000 cột**.
- Một statement lỗi **abort cả transaction** — không tiếp tục sau khi bắt exception trong transaction.

## Quy ước làm việc

- Nhánh mang mã task: `feat/BE-09-pole-fixture-entity`. PR vào **`dev`**.
- **Stage theo đường dẫn tường minh — không `git add -A` / `git add .`** (nuốt file untracked không liên quan).
- Mã task: `BE-` backend · `IOT-` telemetry · `CV-` vision · `FW-` web · `FM-` mobile · `FO-` field ops.
- Mọi API cùng định dạng response, có correlation id. Test tích hợp chạy trên PostGIS thật, phủ truy vấn không gian.
- **Không** đổi field / enum đã publish (lệch thì ghi drift và nêu ở review). **Không** đề xuất IoT cho mọi cột — sparse IoT là thiết kế.
  **Không** giả định có mạng — mobile chạy trọn ca offline. **Không** âm thầm bỏ frame / dữ liệu không hợp lệ — từ chối có lý do.

## Agent skills

- **Issue tracker:** ticket là `.ai/tasks/<mã>.md`, không GitHub Issues — `docs/agents/issue-tracker.md`.
- **Triage labels:** vai trò triage ánh xạ vào `status:` / `owner:` của ticket — `docs/agents/triage-labels.md`.
- **Domain docs:** thuật ngữ ở `CONTEXT.md`; quyết định vào `docs/contract-drift.md` / file này; **không** có `docs/adr/` — `docs/agents/domain.md`.
