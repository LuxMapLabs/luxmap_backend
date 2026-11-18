# Kết nối node và độ mới dữ liệu điện

Node vẫn có thể nhận ON/OFF/AUTO khi dữ liệu INA226 chưa tới server.
Từ thay đổi IOT-CONTACT-1, hai tình trạng này được trả riêng; không sửa FE.

## API

Dùng Bearer token có ReadNetwork trong phạm vi xã của node, Body none.

```http
GET https://luxmap.com.vn/api/v1/iot/nodes?page=1&page_size=20
GET https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/electrical
GET https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/diagnostics
GET https://luxmap.com.vn/api/v1/iot/diagnostics/summary?page=1&page_size=20
```

Danh sách node và electrical theo node dùng status để nói kết nối.
Diagnostics dùng connection_status riêng; status vẫn là kết quả chẩn đoán điện/phần cứng.
Summary dùng connection_status, electrical_status, diagnostic_status từ snapshot job,
cần xem evaluated_at. Snapshot quá 90 giây trả source_unavailable.

## Danh sách node — ví dụ

```json
{
  "page": 1,
  "page_size": 20,
  "total": 1,
  "items": [
    {
      "node_id": "NODE-004",
      "server_id": "iot-server-01",
      "status": "online",
      "last_received_at": "2026-11-18T12:24:06Z",
      "electrical_status": "stale",
      "last_seen_at": "2026-11-18T12:30:00Z",
      "last_command_ack_at": "2026-11-18T12:30:00Z",
      "cabinet_id": "CAB-011",
      "commune_id": "COM-070",
      "location": { "lat": 10.965681, "lng": 106.504581 }
    }
  ]
}
```

Đây là ví dụ lúc gọi 12:30:05Z. Node vừa ACK, nhưng mẫu điện cũ.
last_received_at giữ nghĩa cũ là giờ nhận latestTelemetry; không ghi đè nó bằng giờ ACK.

| Trường | Ý nghĩa |
|---|---|
| status | online / offline / no_data; danh sách còn có unconfigured / source_unavailable |
| last_seen_at | UTC liên lạc node đã xác thực gần nhất |
| last_command_ack_at | UTC ACK lệnh hợp lệ gần nhất, kể cả ACK mới cho lệnh đã bị thay thế |
| last_received_at | UTC nhận mẫu điện latestTelemetry ở Bridge |
| electrical_status | fresh / stale / no_data; danh sách còn unconfigured / source_unavailable |
| connection_status | Kết nối ở diagnostics và summary, cùng nghĩa status của danh sách node |

## Điện theo node — ví dụ

```json
{
  "node_id": "NODE-004",
  "server_id": "iot-server-01",
  "status": "online",
  "measurement": {
    "received_at": "2026-11-18T12:24:06Z",
    "lamp_command": "ON",
    "voltage": 5,
    "current_ma": 100,
    "power_mw": 500
  },
  "power_state": "off",
  "electrical_status": "stale",
  "last_seen_at": "2026-11-18T12:30:00Z",
  "last_command_ack_at": "2026-11-18T12:30:00Z"
}
```

Mẫu đo cũ vẫn báo ON, nhưng ACK mới báo OFF nên power_state là off.
ACK xác nhận đầu ra điều khiển, không phải xác nhận dòng điện thực tế.
Node chỉ có ACK mà chưa từng có mẫu sẽ online, electrical_status=no_data, measurement=null.

## Quy tắc

- Cùng dùng max_silence_seconds của nguồn, mặc định 60 giây, nhưng kiểm hai tín hiệu độc lập.
- Đúng 60 giây vẫn online/fresh; quá 60 giây chuyển offline/stale.
- Chỉ bản tin node đã hợp lệ mới là bằng chứng: telemetry mới nhận, ACK mới, sensorHealth hợp lệ.
- Gateway heartbeat chỉ chứng minh gateway online, không chứng minh các node online.
- POST command được Bridge chấp nhận chưa phải bằng chứng node nhận được lệnh; phải có ACK.
- ACK lặp / telemetry trùng / sensorHealth trùng không kéo dài last_seen_at.
- Thời điểm ở tương lai không được dùng làm bằng chứng online hoặc điện fresh.
- Độ mới điện yêu cầu giờ nhận và giờ đo RTC (nếu có) cùng trong ngưỡng.
  Thiếu RTC ở dữ liệu legacy dùng giờ nhận; RTC sai định dạng hoặc ở tương lai không làm mẫu fresh.
- Gửi bù microSD có thể làm node online vì đang liên lạc; điện vẫn stale nếu giờ đo đã cũ.
- Khi gửi bù mẫu cũ hơn latestTelemetry, Bridge lưu last_seen_at riêng để không làm sai mẫu mới nhất.
- Khi không có bằng chứng liên lạc lần nào trả no_data; khi đã có nhưng hết hạn trả offline.
- mất kết nối Bridge không thể kết luận node offline: danh sách trả source_unavailable.

## Cảnh báo và Fault

Diagnostics tiếp tục dùng telemetry_stale cho dữ liệu điện cũ,
kể cả connection_status=online. Không dùng mã này một mình để ghi nhãn “node mất kết nối”.

Job chỉ tạo Fault NodeOffline của nhánh telemetry_stale khi connection_status=offline
và last_seen_at đã quá ngưỡng. Node online nhưng điện stale chỉ có cảnh báo dữ liệu điện.
Fault mất kết nối đã detected có thể phục hồi khi có liên lạc mới liên tục đủ 90 giây,
chuyển rejected và ghi audit nếu chưa có WorkOrder. Có WorkOrder giữ quy trình nghiệm thu hiện có.
ACK không tự xóa lỗi điện/phần cứng hoặc xác nhận đèn thật sự sáng/tắt.

Gợi ý hiển thị: status=online → “Node online”; electrical_status=stale → “Dữ liệu điện đã cũ”.
Có thể hiện cả hai cùng lúc. Không đổi status của gateway theo dữ liệu node.

## Tương thích và deploy

BE mới đọc được Bridge cũ qua LastCommandAck/LatestTelemetry/SensorHealth,
nhưng cần deploy cả BE và Bridge để theo dõi chính xác tiếp nhận gửi bù và ACK lệnh đã thay thế.
Bridge lưu lastSeenAt, lastCommandAckAt trong devices.json; giữ data/ qua restart.
Không migration PostgreSQL, không đổi MQTT topic/firmware, không mất dữ liệu lịch sử.

API feeder electrical giữ trạng thái số đo theo lần gắn nguồn để tra cứu lịch sử mạch;
xem kết nối thiết bị ở danh sách node hoặc electrical theo node. Lịch sử điện 30 giây không đổi.
