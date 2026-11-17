# Đăng ký IoT server, IoT node và xem lịch sử trên LuxMap

Hướng dẫn này dùng `iot-server-01`, gateway `LUXMAP-GW001` và node vật lý `LUXMAP-P001` làm ví dụ. Tủ là `CAB-011`, mạch điện là `FDR-009` và xã/phường là `COM-070`. Sau khi tạo, API cấp mã tài sản dạng `NODE-004`. Hãy dùng các ID thực tế của môi trường đang thao tác.

Trong Postman, chọn **Authorization → Bearer Token** và dán access token của tài khoản phù hợp. Với request có JSON, chọn **Body → raw → JSON**. Với các request GET và request cấp secret, chọn **Body → none**.

```text
Postman / FE → API LuxMap → IoT Bridge → MQTT TLS → ESP32 gateway → LoRa → ESP32 node
```

| Mã hoặc địa chỉ | Ý nghĩa |
|---|---|
| `iot-server-01` | `server_id` do Admin đặt để quản lý server, xã/phường và vị trí trên LuxMap. |
| `http://iot-bridge:5000` | `base_url` HTTP nội bộ mà backend VPS dùng để gọi Bridge. Nhiều server có thể dùng chung Bridge. |
| `mqtts://luxmap.com.vn:8883` | Địa chỉ broker công khai để ESP32 gateway kết nối qua TLS. |
| `LUXMAP-GW001` | `gateway_id`, đồng thời là MQTT Client ID và username của gateway ví dụ. |
| `LUXMAP-P001` | `device_id` vật lý, phải khớp `DEVICE_ID` trên ESP32 node. |
| `NODE-004` | `node_id` do backend cấp. Dùng mã này để gọi API điều khiển và đọc lịch sử. |

## 1. Admin thêm IoT server

**[POST /api/v1/iot/servers](https://luxmap.com.vn/api/v1/iot/servers)** · quyền `ManageSettings`

```json
{
  "server_id": "iot-server-01",
  "commune_id": "COM-070",
  "location": {
    "lat": 10.965681,
    "lng": 106.504581
  }
}
```

- `server_id` là mã duy nhất, do Admin chọn.
- `commune_id` là mã xã/phường chứa bộ thiết bị. Khi gán node, server, tủ và feeder phải cùng xã/phường.
- `location` là GPS của gateway. `lat` là vĩ độ, `lng` là kinh độ. Vị trí node trên bản đồ lấy từ tủ điện.
- Có thể bỏ `base_url` nếu backend đã đặt địa chỉ mặc định hoặc allowlist có một origin duy nhất. Nếu cần chọn cụ thể, thêm `"base_url": "http://iot-bridge:5000"` khi đó là địa chỉ đã được allowlist trên VPS.

Khi thành công, API trả `201 Created` và JSON sau.

```json
{
  "server_id": "iot-server-01"
}
```

Nếu server đã tồn tại và cần đổi xã/phường hoặc GPS, gọi [PUT /api/v1/iot/servers/iot-server-01/location](https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/location) với `commune_id` và `location`. Thành công trả `204 No Content`.

## 2. Chuẩn bị gateway

Gateway đã có MQTT secret dùng lại thông tin đã lưu. Với **gateway vật lý mới**, Admin cấp secret một lần bằng [POST /api/v1/iot/servers/iot-server-01/gateways/LUXMAP-GW001/credential](https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/gateways/LUXMAP-GW001/credential). Để **Body → none**. API trả `200 OK` với `client_id`, `username`, `password` và `node_ids`. Sao chép `password` ngay khi cấp. Gọi cấp lại cho cùng gateway trả `409`.

Trên trang cấu hình ESP32 gateway, nhập Wi-Fi của nơi lắp đặt, broker `mqtts://luxmap.com.vn:8883`, Gateway ID `LUXMAP-GW001`, MQTT username `LUXMAP-GW001` và secret vừa cấp. Firmware nhiều node tự lấy danh sách được phép qua MQTT khi kết nối và làm mới mỗi 10 giây.

## 3. Admin tạo node, gán feeder và tuyến MQTT

Xem `cabinet_id`, `commune_id` và `feeder_ids` của tủ bằng [GET /api/v1/assets/cabinets/CAB-011](https://luxmap.com.vn/api/v1/assets/cabinets/CAB-011). Chọn feeder thuộc đúng tủ. Sau đó dùng token của Admin cho request sau.

**[POST /api/v1/iot/nodes](https://luxmap.com.vn/api/v1/iot/nodes)** · quyền `ManageSettings`

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

| Trường | Cách chọn |
|---|---|
| `server_id` | Dùng mã đã đăng ký ở bước 1. |
| `cabinet_id`, `feeder_id` | Tủ điện và mạch điện đã import. Feeder xác định các trụ và bóng chịu tác động của node. |
| `device_id` | Sao chép đúng `DEVICE_ID` của ESP32 node, kể cả chữ hoa/thường. |
| `data_source` | Dùng `calibration_rig` cho bộ thử vật lý hoặc `simulated` cho dữ liệu mô phỏng. |
| `supports_remote_control` | Đặt `true` để cho phép điều khiển node này. |
| `nominal_voltage`, `nominal_current_ma`, `nominal_power_mw` | Mức điện áp, dòng và công suất danh định của cả feeder, lần lượt tính bằng V, mA và mW. |
| `gateway_id` | Có thể bỏ nếu thiết bị đã có tuyến hợp lệ hoặc Bridge chỉ có một gateway đã cấp secret. Nếu thiết bị chưa có tuyến và có nhiều gateway được cấp secret trên cùng Bridge, gửi thêm `"gateway_id": "LUXMAP-GW001"`. |

Một request tạo node, gán nguồn đo theo feeder và bổ sung tuyến MQTT cho gateway. Gateway dùng lại secret đã có. Bản ghi node liên kết trực tiếp với feeder nên JSON chỉ cần `feeder_id` cho phần liên kết mạch.

Thành công lần đầu trả `201 Created`. Gửi lại cùng cấu hình trả `200 OK` cùng mã node. Ví dụ phản hồi:

```json
{
  "node_id": "NODE-004",
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009",
  "gateway_id": "LUXMAP-GW001"
}
```

Sau khi đăng ký, kiểm tra gateway qua [GET /api/v1/iot/servers](https://luxmap.com.vn/api/v1/iot/servers), node qua [GET /api/v1/iot/nodes?page=1&page_size=20](https://luxmap.com.vn/api/v1/iot/nodes?page=1&page_size=20) và mẫu mới nhất qua [GET /api/v1/iot/nodes/NODE-004/electrical](https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/electrical). Gateway online theo heartbeat MQTT. Node online khi có mẫu điện đủ mới. Dùng token có quyền đọc và phạm vi xã/phường của node.

Với node đã có ID, Manager dùng `PUT /api/v1/iot/nodes/{nodeId}` để gắn hoặc đổi nguồn đo. Manager dùng `DELETE /api/v1/iot/nodes/{nodeId}` để gỡ node khỏi tủ và feeder. Backend lưu bản ghi node, liên kết nguồn đã gỡ và dữ liệu lịch sử. Có thể gắn lại cùng `node_id` bằng PUT, chọn feeder có tủ trong cùng xã.

## 4. API lịch sử lệnh ON, OFF và AUTO

**[GET /api/v1/iot/nodes/NODE-004/command/history?page=1&page_size=20](https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/command/history?page=1&page_size=20)** · quyền `ReadNetwork`

API đọc audit từ database LuxMap, mỗi yêu cầu gửi lệnh qua Bridge là một dòng, mới nhất trước theo `requested_at`. Có thể xem khi gateway đang offline.

```json
{
  "page": 1,
  "page_size": 20,
  "total": 1,
  "items": [
    {
      "command_id": "0619006a07ac41848f8256b1f843300b",
      "node_id": "NODE-004",
      "feeder_id": "FDR-009",
      "server_id": "iot-server-01",
      "device_id": "LUXMAP-P001",
      "mode": "AUTO",
      "command": null,
      "auto_schedule": {
        "day_start": "07:00",
        "night_start": "19:00"
      },
      "requested_by": "Nguyễn Tiến",
      "requested_at": "2026-11-15T08:00:00Z",
      "bridge_accepted_at": "2026-11-15T08:00:01Z",
      "status": "success"
    }
  ]
}
```

- `MANUAL` đi với `command: "ON"` hoặc `"OFF"`. `AUTO` đi với `command: null`. `auto_schedule` là lịch trong request gốc và có thể là `null` nếu request dùng lại lịch hiện có.
- `requested_by` là tên hiển thị của người gửi. Khi thiếu tên, API trả mã tài khoản đã ghi audit. `requested_at` là lúc backend ghi nhận yêu cầu, theo UTC.
- `bridge_accepted_at` là lúc backend ghi nhận Bridge chấp nhận lệnh. Khi giá trị là `null`, chưa có bản ghi xác nhận từ Bridge. Muốn xem ACK của ESP32 cho lệnh hiện tại, gọi [GET /api/v1/iot/nodes/NODE-004/command](https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/command) rồi đối chiếu `command_id` và `command_acknowledged`.

`status` là `success` nếu Bridge nhận ACK hợp lệ trong 12 giây tính từ `requested_at`, `pending` khi còn chờ ACK hoặc `expired` khi hết thời gian chờ. ACK muộn vẫn có trạng thái `expired`.

## 5. API lịch sử dữ liệu điện

**[GET /api/v1/iot/feeders/FDR-009/electrical/history?page=1&page_size=20&sort_by=measured_at](https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history?page=1&page_size=20&sort_by=measured_at)** · quyền `ReadNetwork`

API đọc các mẫu SQLite đã ghi cho feeder qua các lần gắn node. Gọi `GET /api/v1/iot/feeders/FDR-009/electrical` để xem mẫu cuối. Dùng `sort_by` để chọn thứ tự mới nhất trước.

| `sort_by` | Dựa vào | Khi dùng |
|---|---|---|
| `measured_at` | `items[].reading.data.timestamp`, giờ đo trên node | Vẽ biểu đồ theo lúc đo, kể cả mẫu gửi bù. Mẫu thiếu giờ đo nằm cuối. |
| `received_at` | `items[].reading.received_at`, giờ UTC Bridge nhận mẫu | Xem mẫu vừa tới Bridge. Đây là mặc định khi bỏ `sort_by`. |

```json
{
  "page": 1,
  "page_size": 20,
  "total": 2,
  "items": [
    {
      "cursor": 125,
      "node_id": "NODE-004",
      "server_id": "iot-server-01",
      "reading": {
        "data": {
          "timestamp": "2026-11-15T15:00:10",
          "session_id": "a1b2c3d4",
          "sequence_number": 42,
          "mode": "MANUAL",
          "lamp_command": "ON",
          "lux": 27.5,
          "voltage": 5,
          "current": 102.5,
          "power": 512.5
        },
        "received_at": "2026-11-15T08:00:12Z",
        "gateway_id": "LUXMAP-GW001"
      }
    },
    {
      "cursor": 126,
      "node_id": "NODE-004",
      "server_id": "iot-server-01",
      "reading": {
        "data": {
          "timestamp": "2026-11-15T15:00:00",
          "session_id": "a1b2c3d4",
          "sequence_number": 41,
          "mode": "MANUAL",
          "lamp_command": "ON",
          "lux": 27.5,
          "voltage": 5,
          "current": 100,
          "power": 500
        },
        "received_at": "2026-11-15T08:00:15Z",
        "gateway_id": "LUXMAP-GW001"
      }
    }
  ]
}
```

Mẫu đo lúc **15:00:10** đứng đầu khi dùng `measured_at`. Đổi sang `received_at`, mẫu Bridge nhận lúc **08:00:15 UTC** đứng đầu. Đồng hồ DS3231 trên node lưu giờ địa phương, nên `timestamp` trong API không có hậu tố múi giờ. `voltage` tính bằng V, `current` bằng mA, `power` bằng mW và `lux` bằng lx. `cursor` là ID bản ghi trong kho mẫu Bridge.

Các tích hợp đồng bộ theo node có thể dùng `GET /api/v1/iot/nodes/{nodeId}/electrical/history` với `?after=0&limit=100` và đọc `next_cursor`, `has_more`. Dùng riêng bộ tham số đó. Trộn với `page`, `page_size` hoặc `sort_by` trả `400`.

## 6. Phân trang và đọc lỗi

- Cả hai API lịch sử mặc định `page=1`, `page_size=50`, tối đa 200 mục mỗi trang. `total` là tổng bản ghi của node. `items` là dữ liệu của trang hiện tại.
- Giữ nguyên `page_size` và `sort_by` khi chuyển trang. Tăng `page` nếu `page × page_size < total`. Khi đổi kiểu sắp xếp, quay về trang 1.
- `200` với `items: []` là trang rỗng. `401` cần kiểm tra token. `403` cần kiểm tra quyền. `404` có thể do ID sai hoặc node nằm ngoài phạm vi xã/phường.
- Lịch sử điện trả `503` nếu nguồn đo chưa được gán hoặc backend không đọc được Bridge. Deploy cả backend và IoT Bridge có API phân trang để dùng `page` và `sort_by` trên VPS.

Xem thêm [tài liệu tích hợp FE](LUXMAP-IOT-FE.md) và [cấu hình IoT Bridge trên VPS](deploy/vps.md#10-iot-bridge--mqtt-tls-cho-testbed-iot-bridge-vps-14112026).
