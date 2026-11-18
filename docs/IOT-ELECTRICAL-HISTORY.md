# Lịch sử điện theo feeder — trung bình mỗi 30 giây

Cả hai API dùng Bearer token với quyền ReadNetwork trong phạm vi xã của feeder.
Postman chọn Body → none. Dữ liệu cũ vẫn thuộc feeder qua các lần tháo hoặc thay node.

## Hai API

Theo giờ đo DS3231:

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history?page=1&page_size=20&from=2026-11-18T07:00:00Z&to=2026-11-18T08:00:00Z
```

Theo giờ Bridge nhận:

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/electrical/history/received?page=1&page_size=20&from=2026-11-18T07:00:00Z&to=2026-11-18T08:00:00Z
```

Cả hai sắp mới nhất trước. API cũ theo node đã bỏ.
Không dùng sort_by, after hoặc limit cho lịch sử điện công khai nữa.

| Tham số | Ý nghĩa |
|---|---|
| from | Tùy chọn, lấy mẫu từ thời điểm này, bao gồm |
| to | Tùy chọn, lấy mẫu trước thời điểm này, loại trừ; phải lớn hơn from |
| page | Mặc định 1 |
| page_size | Mặc định 20, chuẩn hóa từ 1 đến 200 theo quy ước phân trang chung |

from/to là ISO 8601. Nên gửi UTC với Z; 07:00Z tương ứng 14:00 Việt Nam.
Nếu dùng offset +07:00, URL encode dấu + thành %2B. Thời gian không có offset được hiểu UTC theo convention BE.
from/to được áp dụng trên giờ đo ở API thứ nhất, giờ nhận ở API thứ hai.
Bỏ một cận nghĩa là không giới hạn phía đó; bỏ cả hai đọc toàn bộ lịch sử.

## Phản hồi 200

```json
{
  "feeder_id": "FDR-009",
  "time_basis": "measured_at",
  "interval_seconds": 30,
  "page": 1,
  "page_size": 20,
  "total": 120,
  "items": [
    {
      "period_start": "2026-11-18T07:59:30Z",
      "period_end": "2026-11-18T08:00:00Z",
      "sample_count": 3,
      "voltage_avg": 5.02,
      "current_ma_avg": 96.5,
      "power_mw_avg": 484.43,
      "is_complete": true
    }
  ]
}
```

Đây là ví dụ minh họa; API thứ hai trả time_basis = received_at với cùng DTO.

| Trường | Ý nghĩa |
|---|---|
| feeder_id | Mạch điện cần xem |
| time_basis | measured_at hoặc received_at |
| interval_seconds | Luôn 30 |
| total | Số khoảng 30 giây có mẫu hợp lệ sau lọc, trước phân trang |
| period_start / period_end | Mốc UTC của khoảng [bắt đầu, kết thúc) |
| sample_count | Số mẫu hợp lệ thực tế dùng tính trung bình |
| voltage_avg | Trung bình điện áp (V) |
| current_ma_avg | Trung bình dòng điện (mA) |
| power_mw_avg | Trung bình công suất từng mẫu (mW), không nhân hai giá trị trung bình |
| is_complete | false nếu khoảng giờ chưa kết thúc; true chỉ nói thời gian đã qua |

## Quy tắc tính

- Chia theo đồng hồ thành giây 00–29 và 30–59 của từng phút. Mỗi phút tối đa hai dòng.
- Lọc thời gian trước khi tính: from/to nằm giữa khoảng sẽ chỉ lấy những mẫu trong giới hạn.
- Một mẫu phải có cả V/mA/mW dạng số hợp lệ. Mẫu OFF có giá trị 0 vẫn là mẫu hợp lệ.
- Không tạo dòng cho khoảng trống, không điền 0 thay dữ liệu bị mất.
- Mẫu thiếu RTC bị bỏ ở API theo giờ đo, vẫn có thể xuất hiện ở API giờ nhận.
- Giờ DS3231 không có offset được hiểu UTC+07 rồi đổi UTC. Giờ nhận là UTC.
- Mẫu gửi bù microSD vẫn về đúng giờ đo ở API thứ nhất; API thứ hai gom theo lúc Bridge nhận.
- is_complete=true không đảm bảo bất biến: mẫu gửi bù có thể cập nhật các khoảng cũ.
- Không trung bình trạng thái ON/OFF; DTO này chỉ trả số đo. Xem điện hiện tại qua
  GET /api/v1/iot/feeders/FDR-009/electrical.
- Nếu thay node/Bridge trong một khoảng, gộp theo sample_count, không trung bình hai trung bình.
- Trang rỗng vẫn trả total đúng; khi đổi khoảng thời gian quay về page=1.

## Lỗi và triển khai

400 VALIDATION_FAILED cho from/to sai thứ tự hoặc ngày không đúng ISO.
404 ASSET_NOT_FOUND nếu feeder không tồn tại hoặc ngoài xã được xem.
503 IOT_SOURCE_UNAVAILABLE nếu Bridge lỗi hoặc dữ liệu upstream sai định dạng.
Feeder chưa từng có nguồn trả total=0, items=[].

Deploy đồng thời BE và IoT Bridge mới. Giữ volume data/ và telemetry.sqlite.
Không thay firmware, không xóa mẫu thô; chẩn đoán 25 mẫu và đọc điện mới nhất vẫn dùng mẫu thô.
Bridge tính nhóm tại SQLite, BE chỉ đọc các khoảng đã tổng hợp. Khi feeder qua nhiều Bridge,
BE đọc danh sách mốc khoảng để đếm hợp và gộp trang theo trọng số, trong ngân sách upstream 5 giây.
Khoảng truy vấn dài có thể tốn thời gian hơn; FE nên truyền from/to theo cửa sổ người dùng xem.
