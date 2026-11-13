# Tích hợp IoT LuxMap cho Frontend

FE gọi [Backend LuxMap cổng 5142](http://localhost:5142/swagger/index.html) bằng header `Authorization: Bearer <access_token>`. Khi deploy, thay localhost bằng địa chỉ backend của môi trường tương ứng.

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

Chuẩn bị tủ điện có GPS, feeder thuộc tủ đó và một trụ đèn thuộc feeder. Mỗi tủ hiện gắn tối đa một IoT node. Các ID và tọa độ dưới đây là ví dụ cho bộ thứ hai.

Admin thực hiện bước 2.1–2.2. Manager thực hiện bước 2.3–2.4 trong xã được cấp quyền.

### 2.1 Đăng ký server và vị trí gateway

`POST /api/v1/iot/servers` · quyền `ManageSettings` · thành công `201`

```json
{
  "server_id": "iot-server-02",
  "base_url": "http://127.0.0.1:5000",
  "commune_id": "COM-001",
  "location": { "lat": 10.78, "lng": 106.7 }
}
```

`base_url` là HTTP origin của IoT Bridge nhìn từ backend và phải nằm trong allowlist. Ví dụ loopback dùng khi hai dịch vụ chạy trực tiếp trên cùng máy. Để chỉnh GPS hoặc xã/phường của server đã có, gọi `PUT /api/v1/iot/servers/{serverId}/location` với `commune_id` và `location`. Thành công trả `204`.

### 2.2 Đăng ký tuyến MQTT

`PUT /api/v1/iot/servers/iot-server-02/gateways/LUXMAP-GW002/route` · quyền `ManageSettings`

```json
{ "device_id": "LUXMAP-P002" }
```

`204` nghĩa là tuyến có hiệu lực ngay trên bridge đang chạy. Gửi lại đúng cặp vẫn trả `204`. Nếu gateway hoặc thiết bị đã thuộc cặp khác, API trả `409`. Firmware gateway hiện chuyển tiếp một node.

Backend gửi khóa quản trị cho bridge ở phía server. FE chỉ gửi Bearer token. Người vận hành cấp secret bằng API backend `POST /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential` với quyền `ManageSettings`, rồi nhập địa chỉ broker, Gateway ID, Node ID, username và secret trên trang cấu hình ESP32. API trả client_id, username, password và node_ids. Hiển thị password cho Admin ngay khi cấp và tránh lưu vào localStorage hoặc log. Cấp lại trả 409. Node dùng LoRa và `DEVICE_ID` của firmware phải khớp `device_id` đã đăng ký.

### 2.3 Tạo node và gán relay

`POST /api/v1/assets/iot-nodes` · quyền `ManageAssets` · thành công `201`

```json
{
  "cabinet_id": "CAB-002",
  "data_source": "calibration_rig",
  "supports_remote_control": true
}
```

Lấy `node_id` từ header `Location`, ví dụ `/api/v1/assets/iot-nodes/NODE-002`. Tiếp theo gọi `PUT /api/v1/assets/iot-nodes/{nodeId}/relays/1` với cùng quyền. Feeder phải thuộc tủ của node.

```json
{ "feeder_id": "FDR-002" }
```

### 2.4 Gán nguồn đo cho node

`POST /api/v1/iot/nodes/{nodeId}/source` · quyền `ManageAssets` · thành công `201`

```json
{
  "server_id": "iot-server-02",
  "device_id": "LUXMAP-P002",
  "feeder_id": "FDR-002",
  "pole_id": "POLE-0002",
  "relay_no": 1,
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500
}
```

Manager chỉ gán nguồn cho node và server cùng xã, với thiết bị đã được Admin đăng ký tuyến và cấp secret MQTT. Nhập chỉ số danh định thực tế để API chẩn đoán so sánh. Backend kiểm tra trụ thuộc feeder đã gán vào relay của node.

## 3. Hiển thị trên bản đồ

| API | Dữ liệu FE dùng |
|---|---|
| `GET /api/v1/iot/servers` | Marker gateway từ `server_id`, `commune_id`, `location.lat/lng` và `status`. `location: null` nghĩa là chưa có tọa độ hiển thị. |
| `GET /api/v1/iot/nodes` | Marker node từ `items[].location`, lấy theo tọa độ tủ điện. API phân trang và trả thêm `cabinet_id`, `commune_id`, `server_id`, `status` và `last_received_at`. |
| `GET /api/v1/iot/poles/{poleId}/control` | Khi bấm trụ, lấy `feeder_id`, `node_id`, `relay_no` và `can_control_feeder`. |

Gateway báo `online` theo heartbeat. Node báo `online` theo độ mới của mẫu điện. Một gateway có thể online khi node chưa có dữ liệu mới. FE hiện nút điều khiển khi `can_control_feeder: true` và người dùng có quyền `ControlLighting`. Khi control_channel là iot_bridge, gọi endpoint ở mục 4. Nút tác động lên **cả feeder**, ví dụ **Tắt mạch FDR-001**. Điều khiển qua bridge hiện hỗ trợ relay 1.

## 4. Gửi lệnh và đặt lịch AUTO

`POST /api/v1/iot/nodes/{nodeId}/command` · quyền `ControlLighting`

| Nút | JSON |
|---|---|
| Bật | `{"mode":"MANUAL","command":"ON"}` |
| Tắt | `{"mode":"MANUAL","command":"OFF"}` |
| AUTO theo lịch hiện có | `{"mode":"AUTO","command":null}` |

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
2. Gọi `GET /api/v1/iot/nodes/{nodeId}/command` để đọc lịch và trạng thái lệnh hiện tại.
3. Hiển thị **Node đã xác nhận** khi `command_id` khớp và `command_acknowledged: true`. Nếu GET trả mã khác, lệnh mới đã thay lệnh đang theo dõi.
4. Khi cần xác minh mạch thực sự ngắt tải, đọc mẫu điện mới sau OFF. ACK xác nhận firmware đã xử lý lệnh.

Lệnh mới có `expires_at` theo UTC và hết hạn sau 5 phút. Nếu đến hạn mà chưa nhận ACK cho đúng mã lệnh, hiển thị **Hết hạn chờ xác nhận**. Backend từ chối gửi khi gateway chưa online. API tra điều khiển trụ trả `control_channel: "iot_bridge"` cho mạch điều khiển qua bridge.

## 5. Dữ liệu điện và lỗi

| API | Cách dùng |
|---|---|
| `GET /api/v1/iot/nodes/{nodeId}/electrical` | Hiện mẫu mới nhất, kiểm tra `status` và `received_at` để phân biệt mẫu mới với mẫu cũ. |
| `GET /api/v1/iot/nodes/{nodeId}/electrical/history?after=0&limit=100` | Đọc lịch sử. Dùng `next_cursor` làm `after` cho trang sau hoặc lần lấy mẫu mới. |
| `GET /api/v1/iot/nodes/{nodeId}/diagnostics` | Hiện cảnh báo từ `anomalies[]` và kết quả ước lượng số bóng thiếu khi có. |

`400` thường là dữ liệu đầu vào hoặc ánh xạ relay không hợp lệ. `401` là cần đăng nhập. `403` là thiếu quyền. `404` là tài sản không tồn tại hoặc ngoài phạm vi truy cập. `409` là cấu hình trùng. `503` là nguồn IoT chưa cấu hình hoặc backend chưa gọi được bridge. Hiển thị thông báo từ `error.message` khi có.
