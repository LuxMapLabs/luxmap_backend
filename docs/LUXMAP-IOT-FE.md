# Tích hợp IoT LuxMap cho Frontend

Cập nhật gộp Server ID 19/11/2026: xem [bảng API và quy trình migration](IOT-SERVER-IDENTITY-API-CHANGES.md). Mã FE chưa được sửa; các URL gateway cũ đã deprecated.

FE gọi [Backend LuxMap cổng 5141](http://localhost:5141/swagger/index.html) bằng header `Authorization: Bearer <access_token>`. Khi deploy, thay localhost bằng địa chỉ backend của môi trường tương ứng.

```text
FE ↔ HTTP API LuxMap ↔ HTTP nội bộ IoT Bridge ↔ MQTT ↔ ESP32 gateway ↔ LoRa ↔ ESP32 node
```

## 1. Phân biệt các ID

| Trường | Ví dụ | Nơi sử dụng |
|---|---|---|
| `server_id` | `iot-server-01` | Mã ESP32 gateway trong LuxMap, đồng thời là MQTT Client ID và username. |
| `device_id` | `LUXMAP-P001` | ID phần cứng trong firmware node, dùng cho tuyến MQTT. |
| `node_id` | `NODE-001` | ID tài sản do backend sinh, dùng trong API điều khiển và đọc dữ liệu. |

## 2. Đăng ký một bộ IoT mới

Môi trường local cần cấu hình khóa bridge, khóa provisioning và origin bridge theo [hướng dẫn chạy backend](development.md#chạy-iot-bridge-local).

Chuẩn bị tủ điện có GPS và feeder thuộc tủ đó. Có thể gán nguồn khi feeder chưa có trụ. Một tủ có thể chứa nhiều IoT node; từng node vẫn thuộc đúng một tủ cùng xã. Các ID và tọa độ dưới đây là ví dụ cho bộ thứ hai.

### 2.1 Admin đăng ký server và vị trí gateway

`POST /api/v1/iot/servers` · quyền `ManageSettings` · thành công `201`

```json
{
  "server_id": "iot-server-01",
  "commune_id": "COM-070",
  "location": { "lat": 10.965681, "lng": 106.504581 },
  "radius_km": 0.75
}
```

Backend dùng địa chỉ mặc định được cấu hình ở `IotDiagnostics:BaseUrl` hoặc origin duy nhất trong allowlist. Trên VPS, origin nội bộ thường là `http://iot-bridge:5000`. JSON tạo server không có trường `base_url`; FE gửi server_id, commune_id, location và radius_km (0–1000 km, mặc định 0). Địa chỉ Bridge do người triển khai cấu hình; nếu thiếu/sai hoặc chưa allowlist thì API trả 503 IOT_SOURCE_UNAVAILABLE. Dùng `PUT /api/v1/iot/servers/{serverId}/location` để cập nhật xã/phường, GPS và radius_km.

ESP32 gateway dùng địa chỉ broker TLS, Server ID và secret đã cấp. Client ID và username tự bằng server_id. Firmware tự yêu cầu danh sách node trên MQTT sau khi kết nối và làm mới mỗi 10 giây. Với gateway vật lý mới, Admin gọi `POST /api/v1/iot/servers/{serverId}/credential` một lần và nhập `password` từ phản hồi trên trang cấu hình ESP32. Gateway có thể kết nối khi danh sách node còn rỗng. Tránh lưu secret vào log hoặc bộ nhớ của FE.

### 2.2 Admin tạo node và gán feeder

`POST /api/v1/iot/nodes` · quyền `ManageSettings`

```json
{
  "server_id": "iot-server-01",
  "feeder_id": "FDR-009",
  "radius_km": 0.2,
  "device_id": "LUXMAP-P001",
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500
}
```

Request tạo node chỉ có 7 trường: server_id, feeder_id, device_id, ba ngưỡng danh định và radius_km. Backend suy ra cabinet_id từ feeder_id; feeder phải đã thuộc tủ điện trong cùng xã với server. radius_km hợp lệ từ 0 đến 1000 km, mặc định 0. BE tự đặt data_source=calibration_rig và supports_remote_control=true; không nhập hiệu chuẩn hay số bóng. Tủ, feeder và server phải cùng xã. `device_id` khớp `DEVICE_ID` trong firmware node. Các ngưỡng danh định dùng V, mA và mW của cả feeder. Backend thêm thiết bị vào đúng server_id đã cấp key. Không gửi gateway_id; mỗi gateway hỗ trợ tối đa 32 node được phép.

API trả `201` khi tạo và `200` khi gửi lại cùng cấu hình. Response có `node_id`, `server_id`, `device_id`, `feeder_id`. Dùng `node_id` cho API điều khiển và đọc dữ liệu. Thêm node bằng API này tự thêm tuyến, dùng lại secret và liên kết trực tiếp với feeder. Các trụ và bóng được xác định theo feeder.

### Quản lý tuyến và nguồn đo

- Đọc `GET /api/v1/iot/servers` để lấy credential_issued và heartbeat của từng server. `GET /api/v1/iot/servers/{serverId}/nodes?page=1&page_size=20` trả node thuộc đúng server, có lọc xã. API routes cũ deprecated, chỉ trả tuyến của server đang chọn.
- Manager gọi `DELETE /api/v1/iot/nodes/{nodeId}/feeder` (Body none) để chỉ gỡ khỏi feeder: 204 idempotent; giữ node trong tủ, giữ lịch sử, gỡ binding MQTT và relay. Không tự gửi ON/OFF. Sau đó dùng PUT để gắn lại.
- Manager gọi `PUT /api/v1/iot/nodes/{nodeId}` với `server_id`, `device_id`, `feeder_id`, `radius_km` và các ngưỡng đo để gắn hoặc cập nhật nguồn. Thành công trả `204`. Với node đã gỡ khỏi tủ, chọn feeder có tủ trong cùng xã để gắn lại cùng `node_id`. Thiết bị phải có tuyến MQTT và gateway đã được cấp secret.
- Manager gọi `DELETE /api/v1/iot/nodes/{nodeId}` để gỡ node khỏi tủ và mạch. Thành công trả `204`, kể cả khi gọi lại. Node rời danh sách IoT đang lắp và bản đồ. Có thể xem hồ sơ qua `GET /api/v1/assets/iot-nodes/{nodeId}` với `cabinet_id: null`, và vẫn gọi được các endpoint lịch sử nếu node đã có nguồn. Liên kết relay bị gỡ, dữ liệu lịch sử và secret gateway được giữ; quyền MQTT của device_id bị gỡ. Cấu hình nguồn khai báo tĩnh cần được đưa vào DB khi muốn sửa qua API. Khi gỡ node, backend lưu bản sao nguồn tĩnh để tra cứu lịch sử.

## 3. Hiển thị trên bản đồ

| API | Dữ liệu FE dùng |
|---|---|
| `GET /api/v1/iot/servers` | Marker gateway từ `server_id`, `commune_id`, `location.lat/lng`, `radius_km` và `status`. `location: null` nghĩa là chưa có tọa độ hiển thị. |
| `GET /api/v1/iot/nodes?page=1&page_size=12` | Marker node từ `items[].location`, lấy theo tọa độ tủ điện. API phân trang và trả thêm `cabinet_id`, `commune_id`, `server_id`, `radius_km`, `status`, `last_received_at`, `last_seen_at`, `last_command_ack_at` và `electrical_status`. status phản ánh liên lạc node; last_received_at vẫn là giờ nhận mẫu điện. |
| `GET /api/v1/iot/poles/{poleId}/control` | Khi bấm trụ, lấy `feeder_id`, `node_id` và `can_control_feeder`. |
| `GET /api/v1/map/poles` | `features[].properties.power_state` là trạng thái cấp điện của mạch chứa trụ. |
| `GET /api/v1/map/poles/{poleId}` | `power_state` của trụ và của các bóng trong `fixtures[]`. |
| `GET /api/v1/assets/feeders`, `GET /api/v1/assets/feeders/{feederId}` | `power_state`: normal/warning/incident/offline/unknown; `lamp_state`: on/off/unknown. |
| `GET /api/v1/map/cabinets` | `feeders[].power_state` của các mạch trong tủ. |

Danh sách node trả `page`, `page_size`, `total` và `items`. Trang bắt đầu từ 1. Mặc định 50 mục, tối đa 200. Trang vượt quá tổng số trả `items: []`.

Gateway báo `online` theo heartbeat. Node báo `online` theo độ mới của mẫu điện. Một gateway có thể online khi node chưa có dữ liệu mới. FE hiện nút điều khiển khi `can_control_feeder: true` và người dùng có quyền `ControlLighting`. Khi control_channel là iot_bridge, gọi endpoint ở mục 4. Nút tác động lên **cả feeder**, ví dụ **Tắt mạch FDR-001**. Các trụ cùng feeder tra về cùng node. `power_state` là `on`, `off` hoặc `unknown` theo đầu ra do node báo. `fixture_status` tiếp tục phản ánh tình trạng chiếu sáng của trụ như `normal`, `dim`, `out` hoặc `unknown`.

| API | Giá trị `status` | Ý nghĩa |
|---|---|---|
| `GET /iot/servers` | `online` | Bridge nhận heartbeat gateway trong thời gian chờ. |
| | `offline` | Gateway đã được ánh xạ nhưng heartbeat vắng hoặc quá hạn. |
| | `unconfigured` | Chưa xác định được thiết bị hoặc tuyến gateway cho server. |
| | `broker_down` | Broker MQTT của bridge chưa hoạt động. |
| | `unreachable` | Backend không đọc được phản hồi trạng thái hợp lệ từ bridge. |
| `GET /iot/nodes` | `online` | Mẫu điện gần nhất còn trong ngưỡng thời gian của node. |
| | `offline` | Mẫu điện gần nhất đã quá ngưỡng thời gian của node. |
| | `no_data` | Nguồn đã cấu hình và chưa có mẫu điện. |
| | `unconfigured` | Node chưa có ánh xạ nguồn đo hợp lệ. |
| | `source_unavailable` | Bridge lỗi hoặc đã hết ngân sách chờ 5 giây của trang. |

Các đường dẫn rút gọn trong bảng dùng tiền tố `/api/v1`.

## 4. Gửi lệnh và đặt lịch AUTO

`POST /api/v1/iot/nodes/{nodeId}/command` · quyền `ControlLighting`

| Nút | JSON |
|---|---|
| Bật | `{"mode":"MANUAL","command":"ON"}` |
| Tắt | `{"mode":"MANUAL","command":"OFF"}` |
| AUTO theo lịch hiện có | `{"mode":"AUTO","command":null}` |

Giữ nguyên chữ HOA trong `mode` và `command` của kênh IoT bridge. Đây là ngoại lệ so với enum chữ thường của Contract. `auto_schedule` trong phản hồi có thể là `null` khi chưa từng đặt lịch.

Để đặt lịch mới, gửi hai mốc khác nhau theo định dạng 24 giờ `HH:mm`.

```json
{
  "mode": "AUTO",
  "command": null,
  "auto_schedule": {
    "day_start": "07:00",
    "night_start": "19:00"
  }
}
```

Từ 07:00 đến trước 19:00, AUTO điều khiển theo cảm biến ánh sáng. Từ 19:00 đến trước 07:00 hôm sau, AUTO luôn bật. Node dùng giờ địa phương từ DS3231. Lệnh MANUAL giữ lại lịch AUTO đã lưu.

1. Khi POST trả `202 Accepted`, giữ `command_id` và hiển thị **Đang chờ node xác nhận**.
2. Gọi `GET /api/v1/iot/nodes/{nodeId}/command` để đọc lệnh thành công gần nhất, lịch đã xác nhận và `power_state`. Lệnh mới đang chờ không thay kết quả GET. Chưa có lệnh thành công: `status: no_data`, command_id/mode/command null, command_acknowledged false.
3. Theo dõi lệnh vừa POST bằng command_id trong `GET /api/v1/iot/nodes/{nodeId}/command/history`. GET command chỉ đổi sang ID đó khi node xác nhận; ID khác có thể là lệnh thành công trước đó.
4. Hiển thị **Bật** hoặc **Tắt** theo `power_state` do backend trả. Trong AUTO, đọc lại để cập nhật khi lịch hoặc cảm biến làm mạch đổi đầu ra. Khi dữ liệu đầu ra thiếu hoặc quá hạn, hiển thị **Chưa rõ**.
5. Khi cần xác minh mạch thực sự ngắt tải, đọc mẫu điện mới sau OFF. ACK xác nhận firmware đã xử lý lệnh.

Lệnh mới có `expires_at` theo UTC, hạn thực hiện 20 giây tính từ lúc BE tiếp nhận. Bridge và gateway ngừng phát lại khi hết hạn; node kiểm tra RTC ngay trước đổi đầu ra. Hết hạn không tự đảo trạng thái đèn. AUTO đã áp dụng tiếp tục chạy. Backend từ chối gửi khi gateway chưa online. API tra điều khiển trụ trả `control_channel: "iot_bridge"` cho mạch điều khiển qua bridge.

### Lịch sử điều khiển

`GET /api/v1/iot/nodes/{nodeId}/command/history?page=1&page_size=20` · quyền `ReadNetwork`.

Response có `page`, `page_size`, `total` và `items`. Mới nhất ở đầu. Mặc định 50, tối đa 200 mục mỗi trang. Tăng `page` để lấy trang tiếp theo. Khi node chưa có lệnh đã ghi audit, API trả trang rỗng.

Mỗi mục có `command_id`, `node_id`, `feeder_id`, `server_id`, `device_id`, `mode`, `command`, lịch đã gửi trong `auto_schedule`, tên hiển thị của người gửi trong `requested_by` và giờ UTC `requested_at`. Nếu tài khoản đã bị xóa, `requested_by` trả mã tài khoản đã ghi audit. `bridge_accepted_at` ghi lúc backend lưu xác nhận chấp nhận lệnh từ Bridge.

`status`: `pending` khi chưa có ACK và còn hạn; `expired` khi hết 20 giây chưa có xác nhận hợp lệ; `success` khi node xác nhận thực hiện. ACK nhận ở giây 21 chỉ được công nhận nếu executed_at chứng minh đã thực hiện trước hạn (ví dụ giây 18); ACK sau hạn thiếu giờ thực hiện bị từ chối. ACK trễ hợp lệ đổi đúng dòng lịch sử từ expired sang success, có late_ack=true và ghi audit command_late_ack một lần. Các trường bổ sung: expires_at, executed_at (có thể null với firmware cũ), acknowledged_at, late_ack. Khi Bridge không truy cập được, API trả `503`, không suy diễn trạng thái ACK.

GET command trả status success hoặc no_data; POST trả status pending. GET command lấy lệnh thành công mới nhất theo version phát lệnh, ACK cũ về trễ không ghi đè thành công mới hơn. Trong AUTO, mode AUTO không biểu thị đèn đang ON hay OFF; power_state và mẫu điện phản ánh đầu ra quan sát.

Phải deploy đồng bộ BE, Bridge và nạp firmware node/gateway; RTC DS3231 giữ giờ Việt Nam, ACK chuyển sang UTC Unix seconds. Đồng hồ sai sẽ ảnh hưởng xét hạn. Firmware cũ không có giờ thực hiện chỉ được công nhận nếu ACK về trước hạn. Không có thay đổi mã nguồn FE trong đợt này.

Lịch sử cũng gồm các lần AUTO thực sự đổi ON/OFF theo telemetry của node:
- `requested_by: "sensor"`, `mode: "AUTO"`, `command: "ON"` hoặc `"OFF"`, `status: "success"`.
- `command_id: "sensor-<cursor>"` định danh sự kiện quan sát, không phải mã lệnh gửi xuống node.
- `requested_at` là giờ đo DS3231 đổi sang UTC; nếu không có giờ đo dùng giờ nhận. `bridge_accepted_at` là giờ Bridge nhận mẫu chứng minh chuyển trạng thái.
- `auto_schedule: null` cho sự kiện cảm biến vì mẫu điện không chứa bản sao lịch lúc đo. Lệnh AUTO của người dùng vẫn có `command: null` và tên người gửi.
- Mẫu đầu tiên chỉ làm mốc; chỉ ghi khi hai mẫu kế tiếp cùng AUTO có trạng thái khác nhau. Không ghi lại trạng thái giữ nguyên, không tính lần chuyển từ MANUAL sang AUTO như một sự kiện sensor.
- Dữ liệu nguồn là telemetry SQLite bền vững, không phụ thuộc cache 1.000 mẫu hay việc FE có gọi API; gửi bù được xếp theo giờ đo và bản tin trùng không sinh thêm log. Khoảng gắn node–feeder giới hạn dữ liệu của từng nguồn.
- Sự kiện sensor và lệnh người dùng được gộp trước khi đếm `total`, sắp xếp mới nhất trước và phân trang. Phải deploy cả BE và Bridge mới; Bridge cũ thiếu endpoint nội bộ sẽ trả `503`.

Ví dụ một mục AUTO tự đổi trạng thái:
```json
{
  "command_id": "sensor-123",
  "node_id": "NODE-004",
  "feeder_id": "FDR-009",
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "mode": "AUTO",
  "command": "OFF",
  "auto_schedule": null,
  "requested_by": "sensor",
  "requested_at": "2026-11-21T09:00:00Z",
  "bridge_accepted_at": "2026-11-21T09:00:02Z",
  "status": "success"
}
```


## 5. Dữ liệu điện và lỗi

| API | Cách dùng |
|---|---|
| `GET /api/v1/iot/feeders/{feederId}/electrical` | Mẫu mới nhất đã ghi cho mạch. Sau khi tháo node, có thể xem mẫu cuối với `status: offline` và `power_state: unknown`. Feeder chưa có mẫu trả `no_data` nếu đã gắn nguồn hoặc `unconfigured` nếu chưa gắn. |
| `GET /api/v1/iot/feeders/{feederId}/electrical/history?page=1&page_size=20&sort_by=measured_at` | Từng mẫu điện, mặc định theo giờ đo DS3231. |
| `GET /api/v1/iot/nodes/{nodeId}/diagnostics` | Hiện cảnh báo ở cấp `feeder_id` từ `anomalies[]` và kết quả ước lượng số bóng thiếu khi có. |

Một API lịch sử trả feeder_id, sort_by (mặc định measured_at), page, page_size, total, items.
Mặc định 20 mẫu mỗi trang, tối đa 200; from bao gồm, to loại trừ theo sort_by. Không còn trung bình 30 giây.
Item gồm node_id, measured_at, received_at, reception_type (live/backfill/null), voltage, current_ma, power_mw, lamp_command.
Mẫu gửi bù giữ measured_at gốc; received_at và reception_type giữ lần nhận đầu. Route /history/received đã bỏ.
Mục tiêu firmware tạo mẫu mỗi giây; tốc độ nhận thực tế phụ thuộc LoRa/ACK/mạng, không tạo mẫu giả.
Xem [hướng dẫn và JSON đầy đủ](IOT-ELECTRICAL-HISTORY.md).

Mẫu điện mới nhất vẫn trả measurement.voltage/current_ma/power_mw, không dùng giá trị trung bình lịch sử.

| Trường | Giá trị | Ý nghĩa |
|---|---|---|
| `electrical.status` | `online` | Mẫu điện gần nhất còn trong ngưỡng thời gian. |
| | `offline` | Mẫu điện gần nhất quá ngưỡng thời gian. |
| | `no_data` | Chưa có mẫu điện. |
| `diagnostics.status` | `normal` | Chưa phát hiện bất thường theo cấu hình hiện tại. |
| | `anomaly` | Có ít nhất một bất thường trong `anomalies`. |
| | `stale` | Mẫu đã cũ và không dùng để chẩn đoán điện hiện tại. |
| | `no_data` | Chưa có mẫu để chẩn đoán. |
| `anomalies[].code` | `telemetry_stale` | Thời gian từ mẫu cuối vượt ngưỡng. |
| | `voltage_loss` | Điện áp gần 0 khi đèn được yêu cầu bật. |
| | `voltage_deviation` | Điện áp lệch khỏi giá trị tham chiếu quá ngưỡng. |
| | `lamp_count_shortfall` | Số bóng sáng ước lượng thấp hơn số bóng hiệu chuẩn. |
| `lamp_count.status` | `insufficient_samples` | Chưa đủ 25 mẫu hợp lệ. |
| | `undetermined` | Đủ 25 mẫu nhưng chưa khớp rõ một nhóm hiệu chuẩn. |
| | `estimated` | Một nhóm hiệu chuẩn đủ điều kiện để trả số bóng sáng và số bóng thiếu. |

### Lỗi sức khỏe cảm biến

`GET /api/v1/iot/nodes/{nodeId}/diagnostics` trả cùng cấu trúc `anomalies[]` cho lỗi điện và lỗi cảm biến. Mỗi mục có `code`, `observed`, `reference`, `deviation_percent` và `limit`. Bốn trường số của các mã lỗi cảm biến có giá trị `null`.

| Mã lỗi | Nội dung hiển thị |
|---|---|
| `ds3231_unavailable` | Không nhận hoặc không khởi tạo được DS3231. |
| `ds3231_read_failed` | Đọc DS3231 qua I²C thất bại. |
| `ds3231_time_invalid` | Giờ trên DS3231 không hợp lệ hoặc đồng hồ báo mất nguồn. |
| `ina226_unavailable` | Không nhận hoặc không khởi tạo được INA226. |
| `ina226_read_failed` | Đọc thanh ghi INA226 thất bại. |
| `ina226_shunt_out_of_range` | Số đo shunt chạm giới hạn của INA226. |
| `bh1750_unavailable` | Không nhận hoặc không khởi tạo được BH1750. |
| `bh1750_read_failed` | Đọc mẫu ánh sáng BH1750 thất bại. |
| `microsd_unavailable` | Không khởi tạo được hàng đợi trên microSD. |
| `microsd_read_failed` | Không đọc được hàng đợi hoặc gặp bản ghi lỗi trên microSD. |
| `microsd_write_failed` | Không ghi được mẫu lên microSD, kể cả khi file hàng đợi đã đạt giới hạn. |
| `lora_tx_blocked` | Node có gói chờ phát và AUX ở LOW liên tục ít nhất 10 giây. |
| `lora_rx_frame_error` | Node liên tiếp nhận nhiều frame LoRa lỗi CRC, quá dài hoặc thiếu dữ liệu. |

`sensor_health.received_at` là giờ UTC Bridge nhận bản tin sức khỏe. `sensor_health.status` là `fresh` trong 60 giây, `stale` khi quá hạn hoặc `unknown` khi chưa có bản tin. Báo cáo mới chứa lỗi cảm biến thì trạng thái chung là `anomaly` kể cả khi `measurement` là `null`. Báo cáo cũ giữ thời điểm nhận để FE biết trạng thái chưa được cập nhật.

Node xác nhận lỗi và phục hồi của DS3231, INA226 và BH1750 sau 3 lần kiểm tra liên tiếp cho cùng kết quả. Lỗi microSD dựa trên kết quả khởi tạo, đọc hoặc ghi thực tế. Lỗi LoRa do node quan sát có thể được gửi khi đường truyền còn hoạt động hoặc khi hoạt động lại. Khi mất hoàn toàn đường truyền, FE dựa vào `telemetry_stale` và `sensor_health.status` để hiển thị dữ liệu đã cũ.

Node gửi snapshot khi trạng thái đổi và định kỳ 30 giây khi đã nhận ACK. Khi cảm biến phục hồi, snapshot mới bỏ mã lỗi đó. Gateway chuyển các bản tin này giữa LoRa và các topic MQTT `luxmap/{deviceId}/sensorHealth` và `luxmap/{deviceId}/sensorHealthAck`. Với lỗi INA226, API bỏ qua cảnh báo dựa trên giá trị điện của mẫu cũ. Với lỗi DS3231, API bỏ qua ước lượng số bóng dựa trên giờ đo.

### Ngưỡng điện hiện hành

API dùng trung bình 5 mẫu điện hợp lệ, cảnh báo current_drop khi dòng giảm quá 5% so với nominal_current_ma.
Không đếm bóng hư; không gửi lamp_count_calibration hoặc installed_lamp_count khi tạo node.
Đặt nominal_voltage, nominal_current_ma và nominal_power_mw theo nguồn đo thực tế của cả feeder.
PUT dùng UpdateNodeRequest gồm 7 trường: server_id, device_id, feeder_id, radius_km và 3 nominal_*. Không xuất các thuộc tính NodeProfile nội bộ ra Swagger; cấu hình/profile cũ vẫn đọc được.

| HTTP | `error.code` | Ý nghĩa |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Dữ liệu đầu vào, origin hoặc liên kết node và feeder không hợp lệ. |
| 404 | `ASSET_NOT_FOUND` | Tài sản không tồn tại hoặc nằm ngoài phạm vi truy cập. |
| 409 | `IOT_REGISTRATION_CONFLICT` | Đăng ký xung đột với cấu hình hiện có. |
| 503 | `IOT_SOURCE_UNAVAILABLE` | Nguồn chưa dùng được hoặc bridge không trả phản hồi hợp lệ. |

`401` là cần đăng nhập. `403` là thiếu quyền. Hiển thị thông báo từ `error.message` khi có.


## Nhiều node trong cùng tủ — phase 1 (15/11/2026)

API đọc tủ trong assets và map trả đủ `iot_node_ids[]`, thứ tự `created_at, length(node_id), node_id`.
`iot_node_id` cũ bằng phần tử đầu hoặc null, không phải thiết bị chính. Giữ ảnh và trạng thái các feeder đã có.

Hai kênh Bridge và relay đang cùng tồn tại. Nếu feeder đã gắn relay của một node, source Bridge phải dùng cùng node.
Nối relay hoặc gán source trái quy tắc trả **409 `IOT_REGISTRATION_CONFLICT`** kèm `feeder_id` và hai node trong details.
Nguồn tĩnh + nguồn DB trùng feeder cũng trả 409 khi đọc power/map/control hoặc gửi lệnh; FE hiển thị thông báo cấu hình
và yêu cầu người quản lý sửa liên kết, không diễn giải là mất điện hoặc tự đổi nguồn.
Phase 2 hợp nhất đường điều khiển sẽ được đặc tả riêng; phase 1 giữ relay và các endpoint hiện hành.

## Dashboard chẩn đoán và Fault IoT (18/11/2026)

Đọc GET /api/v1/iot/diagnostics/summary?page=1&page_size=20 bằng quyền ReadNetwork.
Mỗi item có node_id, feeder_id, diagnostic_status, anomaly_codes[], estimated_lit, estimated_missing, fault_ids và evaluated_at.
diagnostic_status nhận normal, anomaly, stale, no_data, unconfigured hoặc source_unavailable.
Danh sách đầy đủ 17 anomaly_codes và cách xử lý null, stale, nguồn lỗi nằm trong [IOT-DIAGNOSTICS.md](IOT-DIAGNOSTICS.md).

Job đánh giá độc lập với FE, tạo Fault detected sau debounce. Detected tự phục hồi và chưa có WorkOrder chuyển rejected có audit.
Fault đã có phiếu giữ trạng thái nghiệp vụ và ghi recovered_at. Đếm bóng thiếu chỉ xác định feeder.
Xem Fault testbed qua GET /api/v1/faults?source_channel=iot&data_source=calibration_rig&page=1&page_size=20.
Manager xác nhận sự cố và giao phiếu qua luồng WorkOrder hiện tại.
Profile mới 5v-quiet-v1 phải được chọn cho nguồn đo R002/64 averages/AUX ổn định 1,5 giây.
## Quản lý MQTT key gateway — cập nhật 18/11/2026

Cả ba API dùng Bearer token của Admin (`ManageSettings`). `GET` và `POST` trả thông tin bí mật,
có header `Cache-Control: no-store`. FE chỉ hiển thị cho người dùng được phép, tránh ghi vào log hoặc localStorage.

| Thao tác | Method và URL |
|---|---|
| Cấp key mới | `POST /api/v1/iot/servers/{serverId}/credential` |
| Xem lại key | `GET /api/v1/iot/servers/{serverId}/credential` |
| Thu hồi key | `DELETE /api/v1/iot/servers/{serverId}/credential` |

Với bộ hiện tại, URL đầy đủ là
`https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/credential`.
Cả ba thao tác dùng Body none.

POST và GET thành công trả 200 với cùng dạng JSON:

```json
{
  "server_id": "iot-server-01",
  "client_id": "iot-server-01",
  "username": "iot-server-01",
  "password": "<MQTT secret 64 ký tự>",
  "node_ids": ["LUXMAP-P001"]
}
```

POST trả 409 IOT_REGISTRATION_CONFLICT nếu gateway đang có key.
GET trả 404 ASSET_NOT_FOUND khi chưa cấp hoặc đã thu hồi key.
GET với key cũ chỉ được lưu hash trả 409 IOT_CREDENTIAL_NOT_RETRIEVABLE; cần Admin chủ động DELETE rồi POST
để cấp key mới có thể xem lại. Các key cũ tiếp tục xác thực được trước khi thu hồi.

DELETE thành công trả 204 No Content, gọi lại vẫn 204. Bridge xóa credential, ngắt kết nối MQTT của gateway,
xóa heartbeat cũ và từ chối key đã thu hồi. Tuyến gateway, node và dữ liệu lịch sử được giữ để cấp lại key bằng POST.
Sau POST cấp lại, nhập password mới vào cấu hình ESP32 gateway.

Bridge có các route nội bộ GET/POST/DELETE `/api/luxmap/gateways/{gatewayId}/credential`.
Backend gửi X-Iot-Bridge-Key và X-Iot-Provisioning-Key. FE chỉ gọi API backend bằng Bearer token.

Secret mới được bảo vệ bằng ASP.NET Core Data Protection trước khi lưu cạnh hash trong
`data/gateway-credentials.json`. Keyring được lưu bền trong `data/keyring/`, application name cố định
`LuxMap.IotBridge.GatewayCredentials`. Deploy cần giữ volume data qua restart và backup cả credential lẫn keyring.
Keyring chứa khóa giải mã và cần quyền đọc giới hạn cho service. Việc lưu keyring vào filesystem không tự cấu hình
mã hóa keyring trên Linux. Nếu mất keyring, GET trả 503; cần khôi phục backup hoặc thu hồi/cấp lại key.
Không có migration PostgreSQL cho thay đổi credential này. Cần deploy đồng thời BE và IoT Bridge.


## Trạng thái liên lạc node và tuổi dữ liệu điện — 18/11/2026

GET /api/v1/iot/nodes: status là liên lạc node. Thêm electrical_status, last_seen_at, last_command_ack_at;
last_received_at vẫn là thời điểm nhận latestTelemetry.
GET /api/v1/iot/nodes/{id}/electrical dùng cùng quy tắc và trả measurement cũ để tra cứu.
Node ACK mới có thể status=online nhưng electrical_status=stale hoặc no_data.
Gateway online không đồng nghĩa các node online. POST lệnh thành công chưa phải ACK.

Diagnostics trả connection_status riêng, status vẫn là chẩn đoán.
Summary thêm connection_status/electrical_status và các thời điểm từ snapshot, xem evaluated_at.
telemetry_stale nói mẫu điện cũ; không dùng nó một mình làm nhãn “Node mất tín hiệu”.
Các lỗi điện/phần cứng không được xóa chỉ vì node trả ACK.
[Chi tiết và JSON Postman](IOT-NODE-STATUS.md).


## Cập nhật 24/11/2026 — lỗi điện trung bình năm mẫu và lịch sử feeder mỗi 5 giây
Dùng GET /api/v1/iot/feeders/{feederId}/diagnostics cho hiện tại và
GET /api/v1/iot/feeders/{feederId}/diagnostics/history?page=1&page_size=20&from=...&to=... cho lịch sử.
Bỏ lamp_count/estimated_lit/estimated_missing; current_drop so trung bình 5 mẫu với nominal_current_ma, giảm >5%.
voltage_loss/voltage_deviation cũng dùng áp trung bình 5 mẫu. electrical_window nêu mẫu/trung bình;
measurement vẫn là mẫu mới nhất. History một dòng/đợt/mã lỗi, cập nhật ended_at mỗi 5 giây,
status active/resolved/interrupted; lọc khoảng giao và phân trang. Mục này thay hướng dẫn đếm bóng cũ.
Chi tiết JSON, ngưỡng, thời gian và 17 mã lỗi: [IOT-DIAGNOSTICS.md](IOT-DIAGNOSTICS.md).


## Đọc cấu hình node trước khi sửa (19/11/2026)

GET /api/v1/iot/nodes/{nodeId}, Bearer token ReadNetwork trong phạm vi xã, Body none.
Trả node_id, server_id, device_id, feeder_id, nominal_voltage (V), nominal_current_ma (mA), nominal_power_mw (mW) và radius_km.
Đây là giá trị danh định đã lưu; không gọi Bridge và không trả secret. 404 khi node thiếu/ngoài xã/chưa có nguồn hoặc đã gỡ nguồn.
Dùng bảy trường ngoài node_id, gồm radius_km, cho PUT cùng URL bằng token Manager; Admin dùng POST tạo node.
Xem JSON và chi tiết trong [IOT-SERVER-IDENTITY-API-CHANGES.md](IOT-SERVER-IDENTITY-API-CHANGES.md).


## AUTO cả ngày và AUTO theo giờ

API:
`POST https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/command`

AUTO dùng cảm biến cả ngày:
```json
{
  "mode": "AUTO",
  "auto_type": "full_time"
}
```

AUTO theo giờ:
```json
{
  "mode": "AUTO",
  "auto_type": "scheduled",
  "auto_schedule": {
    "day_start": "06:00",
    "night_start": "18:00"
  }
}
```

- `full_time`: BH1750 điều khiển cả 24 giờ; không gửi auto_schedule.
- `scheduled`: từ 06:00 đến trước 18:00 dùng cảm biến; từ 18:00 đến trước 06:00 hôm sau luôn bật. Giờ DS3231 theo Việt Nam.
- Dưới 50 lux bật, trên 50 lux tắt **sau 5 mẫu hợp lệ liên tiếp**. Đúng 50 lux hoặc đọc lỗi reset bộ đếm và giữ đầu ra. Thay chế độ/lịch reset chuỗi; khoảng ban đêm scheduled luôn bật ngay.
- AUTO cũ không gửi auto_type/null vẫn là scheduled. Scheduled bỏ lịch thì dùng lịch Bridge đang có; nếu không có lịch, mặc định cũ 06:00–18:01. Muốn đúng 18:00 hãy gửi lịch như ví dụ.
- MANUAL ON/OFF không gửi auto_type hoặc gửi null. auto_type sai, MANUAL có auto_type, full_time kèm lịch hoặc lịch không hợp lệ trả 400 VALIDATION_FAILED.
- GET `/api/v1/iot/nodes/NODE-004/command` và POST response có auto_type. full_time có auto_schedule null; scheduled có lịch; MANUAL có auto_type null.
- GET `/api/v1/iot/nodes/NODE-004/command/history?page=1&page_size=20` trả auto_type của lệnh lúc gửi hoặc của telemetry AUTO lúc đo. Lệnh AUTO người gửi có command null; AUTO tự đổi đầu ra có command ON/OFF và requested_by sensor. Bản ghi cũ thiếu trường được hiểu scheduled.
- Node lưu loại AUTO và lịch trong NVS, giữ qua mất điện. ACK full_time phải có autoType full_time; ACK firmware cũ không được dùng để xác nhận full_time.
- Không đổi R002, trung bình INA226 16 lần, chế độ đo FAST, nguồn relay hay lịch truyền LoRa. Firmware chưa được nạp tự động.
- Cần deploy BE + Bridge mới và nạp cả firmware node/gateway. full_time vẫn dùng cơ chế xác thực, quyền, TTL và ACK hiện có; RTC cần hợp lệ để nhận lệnh mới có hạn.

## Thêm nhiều node bằng Excel (24/11/2026)

`POST https://luxmap.com.vn/api/v1/iot/nodes/import`

Dùng Bearer token **Admin**. Postman → Body → form-data → key `file`, đổi kiểu từ Text sang File và chọn `.xlsx`. Không gửi JSON và không tự gõ header Content-Type; Postman tạo multipart boundary. Swagger có ô chọn file trên cùng endpoint.

Đọc sheet tên `Nodes` nếu có; nếu không, đọc sheet đầu tiên. File mẫu `example.xlsx` có `Sheet1` được hỗ trợ. Dòng đầu là tiêu đề; mỗi dòng tiếp theo là một node. Các cột có thể đổi thứ tự, tên cột không phân biệt hoa/thường và được bỏ khoảng trắng hai đầu.

| Cột | Yêu cầu |
|---|---|
| server_id | Bắt buộc, server đã đăng ký và cấp quyền gateway |
| feeder_id | Bắt buộc, feeder đã thuộc tủ cùng xã với server; BE tự suy cabinet_id |
| device_id | Bắt buộc, khớp DEVICE_ID trên ESP32 node |
| nominal_voltage | Số dương, đơn vị V |
| nominal_current_ma | Số dương, dòng chuẩn của cả feeder khi đủ tải, đơn vị mA |
| nominal_power_mw | Số dương, đơn vị mW |
| radius_km | Tùy chọn, bỏ trống hoặc thiếu cột mặc định 0; 0–1000 km |

Chỉ nhập số hoặc giá trị text có dấu chấm thập phân, không kèm đơn vị. Không nhận công thức: cần dán giá trị trước khi upload. Giới hạn 100 dòng dữ liệu/file và 5 MiB/file; bỏ qua dòng trống. File sai cấu trúc/cột hoặc quá giới hạn bị từ chối trước khi tạo node.

HTTP 200 nghĩa là file đã được xử lý; **phải kiểm tra failed và từng items**. Ví dụ:

```json
{
  "sheet": "Sheet1",
  "total": 3,
  "created": 2,
  "existing": 0,
  "failed": 1,
  "items": [
    { "row": 2, "server_id": "iot-server-01", "feeder_id": "FDR-009", "device_id": "LUXMAP-P001", "status": "created", "node_id": "NODE-004", "error": null },
    { "row": 3, "server_id": "iot-server-01", "feeder_id": "FDR-010", "device_id": "LUXMAP-P002", "status": "failed", "node_id": null, "error": { "code": "ASSET_NOT_FOUND", "message": "Không tìm thấy feeder.", "field": null } },
    { "row": 4, "server_id": "iot-server-02", "feeder_id": "FDR-011", "device_id": "LUXMAP-P003", "status": "created", "node_id": "NODE-005", "error": null }
  ]
}
```

Các mã node trong ví dụ là minh họa; BE tự cấp mã thực tế. `row` là số dòng Excel, tính cả header ở dòng 1. `error.field` cho biết cột sai nếu xác định được. Lỗi từng dòng giữ mã nghiệp vụ như VALIDATION_FAILED, ASSET_NOT_FOUND, IOT_REGISTRATION_CONFLICT, IOT_SOURCE_UNAVAILABLE.

Mỗi dòng có transaction riêng: dòng lỗi không xóa những node đã tạo ở dòng trước. Gửi lại cùng file/cấu hình trả existing và đúng node_id cũ; không tạo thêm node. Cấu hình đã có mà khác ngưỡng/thiết bị trả conflict; dùng API PUT để sửa. Dòng trùng feeder hoặc cặp server_id/device_id trong cùng file bị báo failed ở lần xuất hiện sau. Sau khi sửa lỗi có thể gửi lại toàn bộ file.

Import tự đồng bộ quyền MQTT như POST /iot/nodes; không có bước gán relay/pole/cabinet trong Excel. Không tự cấp lại secret gateway hoặc đổi firmware. Có thể có dòng failed nếu Bridge không truy cập được, tuyến MQTT không hợp lệ hoặc feeder/server không cùng xã. API này tạo thiết bị thật; không phải preview hay dry-run.


## Phân biệt sức khỏe feeder và bật/tắt (26/11/2026)

API kiểm kê feeder dùng `power_state` cho sức khỏe5 mức; lấy bật/tắt từ `lamp_state`. Trường `power_state` của command/electrical/map vẫn là on/off/unknown. Không dùng chung bộ nhãn giữa hai nhóm API.
Mẫu mới và ACK kết nối được đánh giá độc lập. Node có ACK mới, điện cũ là warning. Chưa gán nguồn/relay là unknown; đã gán nhưng chưa đủ dữ liệu là warning. Xem bảng lỗi nhẹ/nặng và ưu tiên trong [IOT-DIAGNOSTICS.md](IOT-DIAGNOSTICS.md).

Hướng dẫn riêng về sức khỏe feeder, JSON và bảng lỗi: [FEEDER-HEALTH.md](FEEDER-HEALTH.md).


## IOT-FAULT-RECOVERY-1 (26/11/2026)

Sự cố IoT mới/tự phục hồi/tạo Inspection vẫn `detected`; tự phục hồi ghi recovered_at,
không tự rejected. Rejected chỉ do Manager quyết định trực tiếp hoặc nghiệm thu
Inspection IoT với fault_absent; fault_present -> confirmed, inconclusive giữ nguyên.
GET lịch sử diagnostics theo feeder thêm fault_id/fault_status nullable; status vẫn
active/resolved/interrupted, độc lập tiến độ công việc. FE không cần mở lại lỗi đã
phục hồi để tạo Inspection. Xem docs/IOT-DIAGNOSTICS.md phần vòng đời và dữ liệu cũ.
