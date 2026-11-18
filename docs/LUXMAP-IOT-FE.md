# Tích hợp IoT LuxMap cho Frontend

FE gọi [Backend LuxMap cổng 5141](http://localhost:5141/swagger/index.html) bằng header `Authorization: Bearer <access_token>`. Khi deploy, thay localhost bằng địa chỉ backend của môi trường tương ứng.

```text
FE ↔ HTTP API LuxMap ↔ HTTP nội bộ IoT Bridge ↔ MQTT ↔ ESP32 gateway ↔ LoRa ↔ ESP32 node
```

## 1. Phân biệt các ID

| Trường | Ví dụ | Nơi sử dụng |
|---|---|---|
| `server_id` | `iot-server-01` | Mã server trong LuxMap, dùng để đăng ký vị trí và liên kết node. |
| `gatewayId` | `LUXMAP-GW001` | Client ID MQTT của ESP32 gateway. |
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
  "location": { "lat": 10.965681, "lng": 106.504581 }
}
```

Backend dùng địa chỉ mặc định được cấu hình ở `IotDiagnostics:BaseUrl` hoặc origin duy nhất trong allowlist. Trên VPS, origin nội bộ thường là `http://iot-bridge:5000`. Gửi `base_url` rõ ràng khi cần chọn một origin khác trong allowlist. Dùng `PUT /api/v1/iot/servers/{serverId}/location` để cập nhật xã/phường và GPS.

ESP32 gateway dùng địa chỉ broker TLS, Gateway ID, MQTT username và secret đã cấp. Firmware tự yêu cầu danh sách node trên MQTT sau khi kết nối và làm mới mỗi 10 giây. Với gateway vật lý mới, Admin gọi `POST /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential` một lần và nhập `password` từ phản hồi trên trang cấu hình ESP32. Gateway có thể kết nối khi danh sách node còn rỗng. Tránh lưu secret vào log hoặc bộ nhớ của FE.

### 2.2 Admin tạo node và gán feeder

`POST /api/v1/iot/nodes` · quyền `ManageSettings`

```json
{
  "server_id": "iot-server-01",
  "cabinet_id": "CAB-011",
  "feeder_id": "FDR-009",
  "device_id": "LUXMAP-P001",
  "data_source": "calibration_rig",
  "supports_remote_control": true,
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500
}
```

Tủ, feeder và server phải cùng xã. `device_id` khớp `DEVICE_ID` trong firmware node. Các ngưỡng danh định dùng V, mA và mW của cả feeder. Nếu thiết bị đã thuộc một gateway có secret, backend dùng lại tuyến đó. Nếu bridge có nhiều gateway đã cấp secret và thiết bị chưa có tuyến, gửi thêm `gateway_id`, ví dụ `LUXMAP-GW001`. Mỗi gateway hỗ trợ tối đa 32 node được phép.

API trả `201` khi tạo và `200` khi gửi lại cùng cấu hình. Response có `node_id`, `server_id`, `device_id`, `feeder_id` và `gateway_id`. Dùng `node_id` cho API điều khiển và đọc dữ liệu. Thêm node bằng API này tự thêm tuyến, dùng lại secret và liên kết trực tiếp với feeder. Các trụ và bóng được xác định theo feeder.

### Quản lý tuyến và nguồn đo

- Admin gọi `GET /api/v1/iot/servers/{serverId}/gateways/routes` để đọc `gateway_id`, `device_ids` và `credential_issued`. Nếu nhiều server dùng chung bridge, danh sách phản ánh toàn bộ tuyến của bridge đó.
- Manager gọi `PUT /api/v1/iot/nodes/{nodeId}` với `server_id`, `device_id`, `feeder_id` và các ngưỡng đo để gắn hoặc cập nhật nguồn. Thành công trả `204`. Với node đã gỡ khỏi tủ, chọn feeder có tủ trong cùng xã để gắn lại cùng `node_id`. Thiết bị phải có tuyến MQTT và gateway đã được cấp secret.
- Manager gọi `DELETE /api/v1/iot/nodes/{nodeId}` để gỡ node khỏi tủ và mạch. Thành công trả `204`, kể cả khi gọi lại. Node rời danh sách IoT đang lắp và bản đồ. Có thể xem hồ sơ qua `GET /api/v1/assets/iot-nodes/{nodeId}` với `cabinet_id: null`, và vẫn gọi được các endpoint lịch sử nếu node đã có nguồn. Liên kết relay bị gỡ, còn dữ liệu lịch sử, tuyến MQTT và secret được giữ. Cấu hình nguồn khai báo tĩnh cần được đưa vào DB khi muốn sửa qua API. Khi gỡ node, backend lưu bản sao nguồn tĩnh để tra cứu lịch sử.

## 3. Hiển thị trên bản đồ

| API | Dữ liệu FE dùng |
|---|---|
| `GET /api/v1/iot/servers` | Marker gateway từ `server_id`, `commune_id`, `location.lat/lng` và `status`. `location: null` nghĩa là chưa có tọa độ hiển thị. |
| `GET /api/v1/iot/nodes?page=1&page_size=12` | Marker node từ `items[].location`, lấy theo tọa độ tủ điện. API phân trang và trả thêm `cabinet_id`, `commune_id`, `server_id`, `status`, `last_received_at`, `last_seen_at`, `last_command_ack_at` và `electrical_status`. status phản ánh liên lạc node; last_received_at vẫn là giờ nhận mẫu điện. |
| `GET /api/v1/iot/poles/{poleId}/control` | Khi bấm trụ, lấy `feeder_id`, `node_id` và `can_control_feeder`. |
| `GET /api/v1/map/poles` | `features[].properties.power_state` là trạng thái cấp điện của mạch chứa trụ. |
| `GET /api/v1/map/poles/{poleId}` | `power_state` của trụ và của các bóng trong `fixtures[]`. |
| `GET /api/v1/assets/feeders`, `GET /api/v1/assets/feeders/{feederId}` | `power_state` của feeder trong danh sách và chi tiết. |
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
2. Gọi `GET /api/v1/iot/nodes/{nodeId}/command` để đọc lịch, trạng thái lệnh hiện tại và `power_state`.
3. Hiển thị **Node đã xác nhận** khi `command_id` khớp và `command_acknowledged: true`. Nếu GET trả mã khác, lệnh mới đã thay lệnh đang theo dõi.
4. Hiển thị **Bật** hoặc **Tắt** theo `power_state` do backend trả. Trong AUTO, đọc lại để cập nhật khi lịch hoặc cảm biến làm mạch đổi đầu ra. Khi dữ liệu đầu ra thiếu hoặc quá hạn, hiển thị **Chưa rõ**.
5. Khi cần xác minh mạch thực sự ngắt tải, đọc mẫu điện mới sau OFF. ACK xác nhận firmware đã xử lý lệnh.

Lệnh mới có `expires_at` theo UTC và hết hạn sau 5 phút. Nếu đến hạn mà chưa nhận ACK cho đúng mã lệnh, hiển thị **Hết hạn chờ xác nhận**. Backend từ chối gửi khi gateway chưa online. API tra điều khiển trụ trả `control_channel: "iot_bridge"` cho mạch điều khiển qua bridge.

### Lịch sử điều khiển

`GET /api/v1/iot/nodes/{nodeId}/command/history?page=1&page_size=20` · quyền `ReadNetwork`.

Response có `page`, `page_size`, `total` và `items`. Mới nhất ở đầu. Mặc định 50, tối đa 200 mục mỗi trang. Tăng `page` để lấy trang tiếp theo. Khi node chưa có lệnh đã ghi audit, API trả trang rỗng.

Mỗi mục có `command_id`, `node_id`, `feeder_id`, `server_id`, `device_id`, `mode`, `command`, lịch đã gửi trong `auto_schedule`, tên hiển thị của người gửi trong `requested_by` và giờ UTC `requested_at`. Nếu tài khoản đã bị xóa, `requested_by` trả mã tài khoản đã ghi audit. `bridge_accepted_at` ghi lúc backend lưu xác nhận chấp nhận lệnh từ Bridge.

`status` có ba giá trị: `success` khi Bridge nhận ACK của node đúng mã lệnh trong vòng 12 giây từ `requested_at`; `pending` khi còn trong thời gian chờ; `expired` khi đã hết 12 giây và thiếu ACK đúng hạn. ACK muộn vẫn hiện `expired`. Mốc này dành cho lịch sử, hạn thực thi lệnh vẫn là 5 phút. Có thể xem khi gateway offline. Khi Bridge không truy cập được để đối chiếu ACK, API trả `503`.

## 5. Dữ liệu điện và lỗi

| API | Cách dùng |
|---|---|
| `GET /api/v1/iot/feeders/{feederId}/electrical` | Mẫu mới nhất đã ghi cho mạch. Sau khi tháo node, có thể xem mẫu cuối với `status: offline` và `power_state: unknown`. Feeder chưa có mẫu trả `no_data` nếu đã gắn nguồn hoặc `unconfigured` nếu chưa gắn. |
| `GET /api/v1/iot/feeders/{feederId}/electrical/history?page=1&page_size=20` | Trung bình mỗi 30 giây theo giờ DS3231. |
| `GET /api/v1/iot/feeders/{feederId}/electrical/history/received?page=1&page_size=20` | Trung bình mỗi 30 giây theo giờ Bridge nhận. |
| `GET /api/v1/iot/nodes/{nodeId}/diagnostics` | Hiện cảnh báo ở cấp `feeder_id` từ `anomalies[]` và kết quả ước lượng số bóng thiếu khi có. |

Hai API lịch sử trả feeder_id, time_basis, interval_seconds=30, page, page_size, total, items.
Mặc định 20 khoảng mỗi trang, tối đa 200. Thêm from/to ISO 8601 để lọc mẫu trước khi tính;
from bao gồm, to loại trừ. Cả hai mới nhất trước. API lịch sử theo node đã bỏ, không dùng sort_by/after/limit.
Mỗi item có period_start, period_end, sample_count, voltage_avg (V), current_ma_avg (mA),
power_mw_avg (mW), is_complete. Khoảng trống không tạo dòng. Mẫu OFF vẫn được tính.
Mẫu gửi bù nằm theo giờ đo ở API thứ nhất, giờ nhận ở API thứ hai; thiếu RTC chỉ có ở API thứ hai.
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

### Hiệu chuẩn bộ thử 10 bóng ở nguồn khoảng 4 V

Thêm `"lamp_count_calibration": "4v"` vào JSON của `POST /api/v1/iot/nodes` khi tạo hoặc `PUT /api/v1/iot/nodes/{nodeId}` khi sửa nguồn hiện có. Đặt `nominal_voltage` theo điện áp danh định của bộ đang đo để cảnh báo điện áp dùng đúng mốc. Cấu hình `"5v"` và cờ cũ `use_ten_lamp_calibration: true` vẫn chọn hiệu chuẩn 5 V nếu phù hợp.

Hiệu chuẩn 4 V chỉ ước lượng trong phạm vi **6–10 bóng sáng** của bộ thử. Backend xét 25 mẫu ON liên tiếp cùng phiên ở **4,00–4,13 V**, theo thứ tự đo của node. Mẫu cuối phải có giờ RTC còn mới. Nếu có khoảng ngắt, mẫu mất áp, OFF hoặc ra ngoài dải, backend không nối qua điểm đó. Mẫu gửi bù được xếp theo số thứ tự, dãy cũ vừa đến Bridge không đủ để báo thiếu bóng. Firmware lưu một mẫu khoảng mỗi 10 giây nên cần khoảng 4 phút dữ liệu liên tục.

Cả 25 số đo phải nằm trong dải dòng quan sát của một nhóm. Với 10, 8 và 7 bóng, lần lượt cần ít nhất 10 mẫu `>65`, `≥50` và `≥45` mA. Nhóm 9 bóng cần không có mẫu `>65` mA và nhóm 6 bóng cần không có mẫu `≥45` mA. Cửa sổ không khớp rõ trả `undetermined`. Đây là các mốc thử dựa trên dữ liệu của bộ đèn hiện tại.

`lamp_count.median_current_ma` và `lamp_count.threshold_counts` giúp đối chiếu kết quả 4 V. Với 4 V, `votes` là số mẫu nằm trong từng dải quan sát, nên một mẫu có thể được tính ở nhiều dải. `required_votes: 25` là điều kiện cả cửa sổ phải nằm trong dải của nhóm được ước lượng.

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
| Cấp key mới | `POST /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential` |
| Xem lại key | `GET /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential` |
| Thu hồi key | `DELETE /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential` |

Với bộ hiện tại, URL đầy đủ là
`https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/gateways/LUXMAP-GW001/credential`.
Cả ba thao tác dùng Body none.

POST và GET thành công trả 200 với cùng dạng JSON:

```json
{
  "client_id": "LUXMAP-GW001",
  "username": "LUXMAP-GW001",
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
