# Cập nhật Server ID — 19/11/2026

Luồng thêm thiết bị dùng server_id làm MQTT Client ID và username, bỏ gateway_id riêng.
Dùng POST/GET/DELETE /api/v1/iot/servers/{serverId}/credential. POST /iot/nodes chỉ chọn server_id.
Các ví dụ mã gateway riêng bên dưới là luồng cũ; xem hướng dẫn hiện hành và chuyển đổi tại
[IOT-SERVER-IDENTITY-API-CHANGES.md](IOT-SERVER-IDENTITY-API-CHANGES.md).

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
- Không gửi base_url trong JSON. Backend dùng IotDiagnostics__BaseUrl=http://iot-bridge:5000 trên VPS; giữ fallback khi allowlist chỉ có một origin. Không có địa chỉ hợp lệ: 503 IOT_SOURCE_UNAVAILABLE.

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
| `nominal_voltage`, `nominal_current_ma`, `nominal_power_mw` | Mức điện áp, dòng và công suất danh định của cả feeder, lần lượt tính bằng V, mA và mW. |

Một request tạo node, gán nguồn đo theo feeder và bổ sung tuyến MQTT cho gateway. Gateway dùng lại secret đã có. Bản ghi node liên kết trực tiếp với feeder nên JSON chỉ cần `feeder_id` cho phần liên kết mạch.

Thành công lần đầu trả `201 Created`. Gửi lại cùng cấu hình trả `200 OK` cùng mã node. Ví dụ phản hồi:

```json
{
  "node_id": "NODE-004",
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009"
}
```

Sau khi đăng ký, kiểm tra gateway qua [GET /api/v1/iot/servers](https://luxmap.com.vn/api/v1/iot/servers), node qua [GET /api/v1/iot/nodes?page=1&page_size=20](https://luxmap.com.vn/api/v1/iot/nodes?page=1&page_size=20) và mẫu mới nhất qua [GET /api/v1/iot/nodes/NODE-004/electrical](https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/electrical). Gateway online theo heartbeat MQTT. Node online khi có mẫu điện đủ mới. Dùng token có quyền đọc và phạm vi xã/phường của node.

Với node đã có ID, Manager dùng `PUT /api/v1/iot/nodes/{nodeId}` để gắn hoặc đổi nguồn đo. PUT chỉ gửi server_id, device_id, feeder_id và 3 nominal_*. Manager dùng `DELETE /api/v1/iot/nodes/{nodeId}/feeder` để chỉ gỡ feeder, giữ tủ. `DELETE /api/v1/iot/nodes/{nodeId}` gỡ cả khỏi tủ và feeder. Backend lưu bản ghi node, liên kết nguồn đã gỡ và dữ liệu lịch sử. Có thể gắn lại cùng `node_id` bằng PUT, chọn feeder có tủ trong cùng xã.

BE tự đặt data_source=calibration_rig và supports_remote_control=true khi thêm node. JSON tạo node chỉ có server_id, cabinet_id, feeder_id, device_id và 3 giá trị nominal_*.

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

- Lệnh `MANUAL` đi với `command: "ON"` hoặc `"OFF"`. Lệnh `AUTO` do người dùng gửi có `command: null`; sự kiện tự đổi trạng thái trong AUTO có `command: "ON"` hoặc `"OFF"`, `requested_by: "sensor"` và `auto_schedule: null`. `auto_schedule` là lịch trong request gốc và có thể là `null` nếu request dùng lại lịch hiện có.
- `requested_by` là tên hiển thị của người gửi. Khi thiếu tên, API trả mã tài khoản đã ghi audit. `requested_at` là lúc backend ghi nhận yêu cầu, theo UTC.
- `bridge_accepted_at` là lúc backend ghi nhận Bridge chấp nhận lệnh. Khi giá trị là `null`, chưa có bản ghi xác nhận từ Bridge. Muốn xem ACK của ESP32 cho lệnh hiện tại, gọi [GET /api/v1/iot/nodes/NODE-004/command](https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/command) rồi đối chiếu `command_id` và `command_acknowledged`.

`status` chỉ còn `pending` khi chưa có ACK và `success` khi có ACK hợp lệ, kể cả ACK muộn; không có quá hạn trong lịch sử. Sự kiện AUTO từ telemetry có `status: "success"`, `requested_at` là giờ đo (UTC) và `bridge_accepted_at` là giờ nhận mẫu. Lệnh và sự kiện cùng phân trang, mới nhất trước. Xem quy tắc chống trùng, gửi bù và giới hạn dữ liệu theo thời gian gán nguồn tại [LUXMAP-IOT-FE.md](LUXMAP-IOT-FE.md).

## 5. API lịch sử dữ liệu điện theo feeder

Theo giờ đo:

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history?page=1&page_size=20&from=2026-11-18T07:00:00Z&to=2026-11-18T08:00:00Z
```

Theo giờ Bridge nhận:

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history/received?page=1&page_size=20&from=2026-11-18T07:00:00Z&to=2026-11-18T08:00:00Z
```

Cả hai ReadNetwork, scope xã của feeder, mới nhất trước. Mỗi dòng trung bình 30 giây;
total đếm khoảng có mẫu sau lọc; page_size mặc định 20, tối đa 200.
API lịch sử theo node đã bỏ. sort_by/after/limit không dùng ở lịch sử công khai nữa.
Lịch sử lệnh ở mục 4 vẫn theo node, không đổi.

Xem [JSON, quy tắc trung bình, múi giờ, gửi bù và phân trang](IOT-ELECTRICAL-HISTORY.md).

## 6. Đọc lỗi và triển khai

400 khi from/to không hợp lệ, 401 khi thiếu token, 403 khi thiếu quyền,
404 khi feeder không thấy trong phạm vi xã, 503 khi Bridge không đọc được.
Feeder chưa có nguồn trả total=0, items=[].
Deploy đồng thời BE và Bridge; giữ volume data/telemetry.sqlite.


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
