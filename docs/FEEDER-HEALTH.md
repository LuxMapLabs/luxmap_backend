# Trạng thái sức khỏe feeder và trạng thái bật/tắt đèn

Cập nhật: **26/11/2026**. Quyết định: **FEEDER-HEALTH-1**, xem [contract-drift.md](contract-drift.md).
Tài liệu dành cho FE và người kiểm thử API. Các JSON dưới đây là ví dụ minh họa, không phải dữ liệu đọc trực tiếp từ VPS.

## 1. Hai trường cần phân biệt

Trong API **kiểm kê feeder**:

| Trường | Mục đích | Giá trị |
|---|---|---|
| `power_state` | Sức khỏe tổng hợp của feeder | `normal`, `warning`, `incident`, `offline`, `unknown` |
| `lamp_state` | Đầu ra bật/tắt quan sát được từ node | `on`, `off`, `unknown` |
| `control` | Thông tin node/relay và chế độ báo cáo, nếu có | Object hoặc `null` |

Ví dụ feeder đang bật nhưng dòng giảm nhẹ:

```json
{
  "feeder_id": "FDR-009",
  "power_state": "warning",
  "lamp_state": "on"
}
```

Đây là trích đoạn các trường liên quan. `warning` không có nghĩa đèn đã tắt. Ngược lại, người dùng chủ động tắt mạch có thể nhận `power_state: normal`, `lamp_state: off`.
`lamp_state` là quan sát từ mẫu điện/ACK hợp lệ, không phải bằng chứng độc lập rằng mọi bóng thực tế đều sáng.

## 2. API được áp dụng

### Chi tiết feeder

```http
GET https://luxmap.com.vn/api/v1/assets/feeders/FDR-009
Authorization: Bearer <access_token>
```

GET không có body. Phản hồi 200:

```json
{
  "feeder": {
    "control": null,
    "power_state": "warning",
    "lamp_state": "on",
    "feeder_id": "FDR-009",
    "external_ref": "FDR-A",
    "feeder_name": "Mạch điện A",
    "commune_id": "COM-070",
    "commune_name": "Commune 01",
    "has_geometry": false,
    "pole_count": 3,
    "cabinet": {
      "cabinet_id": "CAB-011",
      "cabinet_name": "Tủ điện A",
      "location": {
        "lat": 10.965681,
        "lng": 106.504581
      },
      "cabinet_source": "verified"
    },
    "updated_at": "2026-11-26T05:00:00Z",
    "updated_by": "USR-2090",
    "updated_by_name": "Nguyễn Tiến"
  },
  "geom_wkt": null,
  "created_at": "2026-11-14T10:00:00Z"
}
```

FE đọc `response.feeder.power_state` và `response.feeder.lamp_state`.

### Danh sách feeder

```http
GET https://luxmap.com.vn/api/v1/assets/feeders?page=1&page_size=20
Authorization: Bearer <access_token>
```

GET không có body. Phản hồi có cấu trúc phân trang:

```json
{
  "page": 1,
  "page_size": 20,
  "total": 1,
  "items": [
    {
      "control": null,
      "power_state": "warning",
      "lamp_state": "on",
      "feeder_id": "FDR-009",
      "external_ref": "FDR-A",
      "feeder_name": "Mạch điện A",
      "commune_id": "COM-070",
      "commune_name": "Commune 01",
      "has_geometry": false,
      "pole_count": 3,
      "cabinet": {
        "cabinet_id": "CAB-011",
        "cabinet_name": "Tủ điện A",
        "location": {
          "lat": 10.965681,
          "lng": 106.504581
        },
        "cabinet_source": "verified"
      },
      "updated_at": "2026-11-26T05:00:00Z",
      "updated_by": "USR-2090",
      "updated_by_name": "Nguyễn Tiến"
    }
  ]
}
```

FE đọc `response.items[i].power_state` và `response.items[i].lamp_state`.
Hai API yêu cầu quyền `ReadNetwork`, giới hạn theo xã được phép của tài khoản. `page_size` tối đa 200 theo quy ước phân trang chung.

## 3. Ý nghĩa năm trạng thái

| `power_state` | Nhãn hiển thị | Điều kiện |
|---|---|---|
| `unknown` | Chưa có IoT | Không có nguồn IoT phù hợp và không có liên kết node/relay cho feeder |
| `normal` | Bình thường | Node online, điện mới, không lỗi; khi ON đã đủ cửa sổ 5 mẫu hợp lệ; khi OFF chủ động không cần cửa sổ ON |
| `warning` | Cảnh báo | Có lỗi nhẹ, điện cũ, thiếu mẫu, đã gán nhưng chưa có dữ liệu hoặc chưa xác minh được nguồn Bridge |
| `incident` | Sự cố | Có ít nhất một lỗi nặng, trong khi node chưa được xác định offline |
| `offline` | Mất kết nối IoT | Có thời điểm liên lạc hợp lệ trước đó và đã quá thời gian chờ kết nối |

Node vừa đăng ký chưa gửi dữ liệu là `warning`, không phải `normal` và chưa đủ chứng cứ để báo `offline`.
Có liên kết relay native nhưng chưa có nguồn Bridge để xác minh cũng là `warning`.
Bridge lỗi/không truy cập được thì nguồn đã gán giữ `warning`; không suy ra rằng node mất điện.

## 4. Danh sách cảnh báo và sự cố

### Lỗi điện

| Mã lỗi | `warning` | `incident` |
|---|---|---|
| `current_drop` | Dòng giảm **trên 5% đến dưới 20%** | Dòng giảm **từ 20% trở lên** |
| `voltage_deviation` | Áp đã vượt dung sai cấu hình nhưng lệch **dưới 20%** | Áp đã vượt dung sai cấu hình và lệch **từ 20% trở lên**, cả tăng và giảm |
| `voltage_loss` | Không áp dụng | Lệnh ON nhưng áp trung bình gần 0, ngưỡng hiện tại `abs(V_avg) <= 0.1 V` |
| `telemetry_stale` | Dữ liệu điện cũ khi node vẫn còn liên lạc | Không tự nâng thành sự cố chỉ vì điện cũ |

Các mã điện được đánh giá từ **5 mẫu ON hợp lệ** theo quy tắc chẩn đoán hiện tại. Chỉ xét giảm dòng khi điện áp của các mẫu hợp lệ trong dung sai; không biến mất áp thành lỗi giảm dòng.
Dòng giảm đúng 5% không phát sinh `current_drop`. Các nhãn chỉ phân loại lỗi đã được chẩn đoán, không tự tạo lỗi mới khi chưa đủ dữ liệu.

Công thức đối chiếu:

```text
dòng giảm (%) = (nominal_current_ma - current_avg) / nominal_current_ma × 100
áp lệch (%)   = abs(voltage_avg - nominal_voltage) / nominal_voltage × 100
```

Ngưỡng sự cố 20% dùng giá trị `observed/reference` gốc; không dùng phần trăm đã làm tròn để tránh nâng 19,999% thành sự cố.
Ví dụ baseline 100 mA: 94 mA là warning; 80 mA là incident. Với áp danh định 5 V, dung sai 10%: 4,4 V là warning; 4 V hoặc 6 V là incident.

### Lỗi phần cứng

| Mã lỗi | Mô tả | Nhãn |
|---|---|---|
| `ina226_unavailable` | Không nhận INA226 | `incident` |
| `ina226_read_failed` | Đọc thông số điện thất bại | `incident` |
| `ina226_shunt_out_of_range` | Shunt vượt dải đo | `incident` |
| `ds3231_unavailable` | Không nhận DS3231 | `warning` |
| `ds3231_read_failed` | Đọc DS3231 thất bại | `warning` |
| `ds3231_time_invalid` | Giờ DS3231 không hợp lệ | `warning` |
| `bh1750_unavailable` | Không nhận cảm biến ánh sáng | `warning` |
| `bh1750_read_failed` | Đọc ánh sáng thất bại | `warning` |
| `microsd_unavailable` | Không nhận/mount được microSD | `warning` |
| `microsd_read_failed` | Đọc thẻ thất bại | `warning` |
| `microsd_write_failed` | Ghi thẻ thất bại | `warning` |
| `lora_tx_blocked` | LoRa bị bận lâu | `warning` khi node chưa offline |
| `lora_rx_frame_error` | Lỗi CRC/khung LoRa | `warning` khi node chưa offline |

Ba lỗi INA226 là sự cố **khả năng giám sát điện**, không tự chứng minh đường dây mất điện hoặc bóng hỏng.
Chỉ dùng mã phần cứng từ bản tin health còn mới; không duy trì kết luận lỗi hiện tại chỉ dựa trên bản tin cũ.

## 5. Ưu tiên khi có nhiều lỗi

1. Chưa có IoT phù hợp: `unknown`.
2. Đã xác nhận node mất liên lạc quá thời gian chờ: `offline`.
3. Còn liên lạc hoặc chưa xác định offline, có lỗi nặng: `incident`.
4. Có lỗi nhẹ hoặc chưa đủ dữ liệu để kết luận: `warning`.
5. Đủ dữ liệu, không có lỗi: `normal`.

Ví dụ:

| Tình huống | `power_state` |
|---|---|
| Giảm dòng 8% và lỗi microSD | `warning` |
| Giảm dòng 25% và lỗi microSD | `incident` |
| Có lỗi INA226 nhưng node đã offline | `offline` |
| Điện cũ nhưng node vừa trả ACK | `warning` |
| Node ON, điện mới nhưng chỉ có 1–4 mẫu | `warning` |
| Node báo OFF chủ động, dữ liệu mới, không lỗi khác | `normal` |

`offline` căn cứ vào liên lạc đã xác thực của **node**: mẫu đo, ACK hoặc health. Gateway online không đủ chứng minh node online.
Timeout dùng `max_silence_seconds` của nguồn, mặc định 60 giây. Không suy ra offline từ `measured_at` cũ khi ACK/health vẫn mới.

## 6. Xem lỗi cụ thể và lịch sử

Đọc nguyên nhân hiện tại:

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/diagnostics
```

Đọc lịch sử các đợt lỗi:

```http
GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/diagnostics/history?page=1&page_size=20
```

`power_state` chỉ là nhãn tổng hợp, không thay `anomalies[]`. Nhiều mã lỗi cùng lúc vẫn có các đợt lịch sử riêng.
Đọc thêm [IOT-DIAGNOSTICS.md](IOT-DIAGNOSTICS.md) để biết DTO lỗi, gộp đợt và vòng đời Fault.

## 7. Lưu ý tích hợp FE và triển khai

- Đây là thay đổi ý nghĩa `power_state` trên **danh sách/chi tiết kiểm kê feeder**. FE dùng bảng nhãn mới cho hai API này.
- Muốn hiển thị Bật/Tắt từ hai API kiểm kê, dùng `lamp_state`. Không dùng `power_state == on/off` nữa.
- `control.control_mode` là chế độ báo cáo `on/off/auto`, không phải nhãn sức khỏe. AUTO vẫn có thể có đầu ra ON hoặc OFF.
- `power_state` trên API command/electrical và bản đồ trụ/fixture/tủ vẫn giữ `on/off/unknown`. Không dùng chung bảng nhãn với power_state kiểm kê feeder.
- Không tự cập nhật trạng thái sang ON/OFF chỉ vì FE vừa gửi lệnh; chờ dữ liệu/ACK do BE trả.
- Trạng thái tính khi GET; không có API ghi trực tiếp power_state. GET này không tạo Fault hay lịch sử lỗi.
- Không thay Fault severity/lifecycle và không đổi quy tắc bật/tắt/AUTO.
- Việc đọc nguồn được giới hạn ngân sách 5 giây cho một lượt lookup; nguồn đã gán nhưng chưa xác minh được giữ warning.
- Không có migration hay thay đổi firmware cho chức năng này. Cần deploy BE mới trước khi dùng schema mới trên VPS.
- Tài liệu hướng dẫn FE; phần triển khai hiện tại không sửa source FE.

Lỗi HTTP của hai API kiểm kê vẫn theo contract: 401 nếu chưa đăng nhập, 403 nếu thiếu quyền, 404 nếu feeder chi tiết không tồn tại/ngoài phạm vi; 409 khi cấu hình nguồn điều khiển xung đột.
