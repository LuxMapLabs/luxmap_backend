# Lịch sử từng mẫu điện theo feeder

## Một API duy nhất

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history?page=1&page_size=20&sort_by=measured_at
```

Dùng Bearer token có ReadNetwork và đúng xã của feeder; GET không có body. Mặc định lấy thời gian **measured_at** từ DS3231, mới nhất trước. Không lấy received_at thay giờ đo khi gửi bù.
API `/electrical/history/received` đã bỏ (404); API theo node không được khôi phục. Không còn gom/trung bình 30 giây.

| Tham số | Quy tắc |
|---|---|
| sort_by | Mặc định measured_at; có thể chọn received_at nếu cần đối chiếu giờ nhận |
| from | ISO 8601, bao gồm thời điểm bắt đầu; theo sort_by (mặc định measured_at) |
| to | ISO 8601, loại trừ thời điểm kết thúc; phải lớn hơn from |
| page | Mặc định 1 |
| page_size | Mặc định 20, tối đa 200 theo quy ước phân trang |

Ví dụ lọc theo giờ đo:
```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history?page=1&page_size=20&from=2026-11-25T07:00:00Z&to=2026-11-25T08:00:00Z
```

## JSON phản hồi minh họa

```json
{
  "feeder_id": "FDR-009",
  "sort_by": "measured_at",
  "page": 1,
  "page_size": 20,
  "total": 120,
  "items": [
    {
      "node_id": "NODE-004",
      "measured_at": "2026-11-25T07:59:59Z",
      "received_at": "2026-11-25T08:00:00Z",
      "reception_type": "live",
      "voltage": 5.02,
      "current_ma": 96.5,
      "power_mw": 484.43,
      "lamp_command": "ON"
    },
    {
      "node_id": "NODE-004",
      "measured_at": "2026-11-25T07:59:58Z",
      "received_at": "2026-11-25T08:05:00Z",
      "reception_type": "backfill",
      "voltage": 5.01,
      "current_ma": 95,
      "power_mw": 475.95,
      "lamp_command": "ON"
    }
  ]
}
```

- total đếm mẫu sau lọc trước phân trang; mỗi item là mẫu gốc, không phải trung bình.
- measured_at: giờ đo DS3231, được đổi từ giờ Việt Nam sang UTC. Z là UTC, cộng 7 giờ để hiển thị Việt Nam. Không có RTC hợp lệ thì null; mẫu đó không khớp bộ lọc from/to theo giờ đo.
- received_at: giờ Bridge nhận và lưu mẫu lần đầu. Retry không làm thay đổi giờ này.
- reception_type: live là node phát mẫu mới; backfill là gửi bù hàng đợi; null nếu firmware cũ chưa có metadata. Không suy loại nhận từ chênh lệch giờ đo và giờ nhận trên server.
- voltage/current_ma/power_mw lần lượt V/mA/mW; lamp_command là ON/OFF của mẫu đo. Mẫu OFF bằng 0 vẫn được trả.
- Giữ lịch sử theo feeder qua các lần tháo/thay node. Việc gán mẫu vào feeder dùng khoảng hiệu lực của binding tại giờ đo; không chuyển mẫu cũ sang feeder đang gắn hiện tại.
- Không sinh mẫu giả, không điền 0 cho khoảng mất dữ liệu. Mặc định sắp theo measured_at nên gửi bù về đúng vị trí giờ đo. Có thể dùng sort_by=received_at để kiểm tra đợt gửi bù, vẫn cùng API.
- Trang có thể dịch chuyển khi có mẫu mới hoặc gửi bù; nên truyền from/to cố định khi xem lịch sử.

## Firmware và lưu trữ

Node tạo bản ghi mục tiêu mỗi giây (trước đây 10 giây), giữ INA226 R002 và trung bình 16 lần. Mỗi bản ghi lưu SD trước gửi; vẫn chỉ bỏ khi ACK lưu bền vững từ Bridge. Gửi trực tiếp/bù được đánh dấu receptionType trên wire, không thay bản gốc SD. Mẫu thuộc phiên hiện tại mới tạo trong cửa sổ 2,5 giây được đánh dấu live; hàng đợi cũ hoặc sau restart là backfill. Mẫu lặp giữ loại nhận đầu tiên tại Bridge.

Gateway chuyển JSON qua MQTT ngay sau khi nhận LoRa, không chờ gom 30 giây; giữ ACK server. Server lưu từng mẫu ngay, không chờ cron hoặc timer 1 giây. Mục tiêu mỗi giây tạo một mẫu **không bảo đảm mỗi giây server nhận đúng một mẫu**: LoRa half-duplex, ACK, nguồn và mạng quyết định tốc độ thực tế; khi mất mạng mẫu nhận dồn sau đó. Cần nạp firmware và đo trên phần cứng để xác nhận tốc độ.

Bộ nhớ đệm SD hiện giới hạn file 64 MiB; nếu tốc độ LoRa thấp hơn tốc độ tạo mẫu kéo dài, hàng đợi sẽ tăng. Không coi HTTP polling là bằng chứng dữ liệu mới đã đến. FE có thể gọi GET lại mỗi giây; chưa triển khai SSE/WebSocket.

## Triển khai và lỗi

Deploy BE + Bridge cùng phiên bản; nạp node/gateway mới nếu muốn nhịp 1 giây và reception_type. Giữ volume data/telemetry.sqlite, không xóa lịch sử và không có migration Postgres mới.
400 khi sort_by/from/to sai; 404 khi feeder không tồn tại/ngoài xã hoặc gọi route cũ; 503 khi Bridge lỗi/upstream sai; feeder chưa có binding trả total=0/items=[].
