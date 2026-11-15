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

`POST /api/v1/iot/nodes/register` · quyền `ManageSettings`

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
- Manager gọi `PUT /api/v1/iot/nodes/{nodeId}/source` với `server_id`, `device_id`, `feeder_id` và các ngưỡng đo. Thành công trả `204`. Backend kiểm tra lại xã, feeder, tủ điện và quyền MQTT.
- Manager gọi `DELETE /api/v1/iot/nodes/{nodeId}/source` để gỡ liên kết. Thành công trả `204`. Lịch sử trên bridge, tuyến MQTT và secret vẫn được giữ. Nguồn chỉ khai báo trong cấu hình triển khai trả `409` khi sửa hoặc gỡ.

## 3. Hiển thị trên bản đồ

| API | Dữ liệu FE dùng |
|---|---|
| `GET /api/v1/iot/servers` | Marker gateway từ `server_id`, `commune_id`, `location.lat/lng` và `status`. `location: null` nghĩa là chưa có tọa độ hiển thị. |
| `GET /api/v1/iot/nodes?page=1&page_size=12` | Marker node từ `items[].location`, lấy theo tọa độ tủ điện. API phân trang và trả thêm `cabinet_id`, `commune_id`, `server_id`, `status` và `last_received_at`. |
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

Mỗi mục có `command_id`, `node_id`, `feeder_id`, `server_id`, `device_id`, `mode`, `command`, lịch đã gửi trong `auto_schedule`, mã tài khoản `requested_by` và giờ UTC `requested_at`. `bridge_accepted_at` có giá trị khi backend đã lưu xác nhận từ Bridge, và bằng `null` nếu chưa ghi nhận xác nhận. Để xem ACK của node cho lệnh hiện tại, gọi `GET …/command`. Lịch sử đọc từ DB LuxMap và có thể truy cập khi gateway offline.

## 5. Dữ liệu điện và lỗi

| API | Cách dùng |
|---|---|
| `GET /api/v1/iot/nodes/{nodeId}/electrical` | Hiện mẫu mới nhất và `power_state`. Kiểm tra `status` và `received_at` để phân biệt mẫu mới với mẫu cũ. |
| `GET /api/v1/iot/nodes/{nodeId}/electrical/history?after=0&limit=100` | Đọc lịch sử. Dùng `next_cursor` làm `after` cho trang sau hoặc lần lấy mẫu mới. |
| `GET /api/v1/iot/nodes/{nodeId}/diagnostics` | Hiện cảnh báo ở cấp `feeder_id` từ `anomalies[]` và kết quả ước lượng số bóng thiếu khi có. |

Mẫu mới nhất dùng `measurement.voltage` (V), `measurement.current_ma` (mA) và `measurement.power_mw` (mW). Lịch sử dùng `items[].reading.data.{voltage,current,power,lux}` theo thứ tự V, mA, mW, lx. `timestamp` trong dữ liệu lịch sử là giờ đo theo RTC DS3231 của node. `received_at` là giờ UTC khi server nhận mẫu. Dữ liệu gửi bù có thể được nhận muộn hơn lúc đo.

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
| | `current_when_off` | Dòng điện vượt ngưỡng cho phép khi đèn ở trạng thái OFF. |
| | `voltage_loss` | Điện áp gần 0 khi đèn được yêu cầu bật. |
| | `voltage_deviation` | Điện áp lệch khỏi giá trị tham chiếu quá ngưỡng. |
| | `current_deviation` | Dòng điện lệch khỏi giá trị tham chiếu quá ngưỡng. |
| | `power_deviation` | Công suất lệch khỏi giá trị tham chiếu quá ngưỡng. |
| | `lamp_count_shortfall` | Số bóng sáng ước lượng thấp hơn số bóng hiệu chuẩn. |
| `lamp_count.status` | `insufficient_samples` | Chưa đủ 25 mẫu hợp lệ. |
| | `undetermined` | Đủ 25 mẫu nhưng chưa đủ phiếu để chọn một khung dòng. |
| | `estimated` | Một khung đủ phiếu để ước lượng số bóng sáng và số bóng thiếu. |

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
