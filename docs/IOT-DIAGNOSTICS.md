# Chẩn đoán và lịch sử lỗi IoT theo feeder

Cập nhật 19/11/2026. Chỉ BE; FE không cần tính lại ngưỡng. Thời gian API: UTC, ISO 8601 hậu tố Z.
Quyền ReadNetwork, Bearer token; feeder ngoài phạm vi xã hoặc không tồn tại trả 404 ASSET_NOT_FOUND.

## 1. Lỗi hiện tại

GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/diagnostics

Đọc trực tiếp nguồn đang gán, không ghi log khi GET. Thiếu nguồn/Bridge không dùng được: 503 IOT_SOURCE_UNAVAILABLE.
API cũ /api/v1/iot/nodes/{nodeId}/diagnostics và /api/v1/iot/diagnostics/summary còn giữ đường dẫn.

Ví dụ các trường chính:
~~~json
{
  "node_id": "NODE-004",
  "feeder_id": "FDR-009",
  "status": "anomaly",
  "measurement": {
    "received_at": "2026-11-19T08:02:00Z",
    "lamp_command": "ON",
    "voltage": 5.08,
    "current_ma": 86.6,
    "power_mw": 439.928
  },
  "electrical_window": {
    "status": "evaluated",
    "samples": 2,
    "required_samples": 2,
    "voltage_avg": 5.08,
    "current_ma_avg": 86.6,
    "power_mw_avg": 439.928,
    "current_evaluation_available": true
  },
  "anomalies": [
    {
      "code": "current_drop",
      "observed": 86.6,
      "reference": 93.5546875,
      "deviation_percent": -7.43,
      "limit": 5
    }
  ],
  "sensor_health": {
    "status": "fresh",
    "received_at": "2026-11-19T08:02:00Z"
  }
}
~~~

Response còn có connection_status, electrical_status, last_seen_at, last_command_ack_at, measured_at,
measurement_key/sequence, sensor_health_key, firmware_version và calibration_version.
measurement là mẫu mới nhất; observed của lỗi điện là trung bình HAI mẫu, không nhất thiết bằng measurement.
Không còn lamp_count, estimated_lit, estimated_missing, votes hoặc threshold_counts.

## 2. Trung bình HAI mẫu

Hai mẫu phải ON, V/mA/mW hữu hạn, cùng session, sequence kề nhau, không trùng.
Giờ đo DS3231 tăng theo sequence, giờ đo/giờ nhận CẢ HAI mẫu còn trong max_silence_seconds.
Không nhận SD replay cũ, đứt sequence hoặc lặp bản tin làm hai mẫu. Không đánh giá lỗi điện khi INA226/DS3231 báo lỗi.
Chỉ có 0/1 mẫu: electrical_window.status=insufficient_samples, required_samples=2, các trung bình null.
Đủ hai mẫu: status=evaluated. OFF/stale/hardware lỗi: electrical_window=null.
Đây là hai bản tin đo độc lập, không phải số lần INA226 lấy trung bình nội bộ.

### current_drop

current_avg = (I1 + I2) / 2.
deviation_percent = (current_avg - nominal_current_ma) / nominal_current_ma * 100.

Chỉ báo khi current_avg < nominal_current_ma * 0.95; đúng 5% không báo, tăng dòng không thuộc lỗi này.
CẢ HAI điện áp phải >0.1 V và trong dung sai nominal_voltage. current_evaluation_available cho biết điều kiện xét dòng.
Mất áp/sai áp không được coi dòng tải giảm. Ngưỡng 5% cố định, không dùng current_tolerance_percent cũ.
Baseline là nominal_current_ma của từng feeder; không tự học từ mẫu đang lỗi, không gán 93.55 mA cho mọi feeder.
Bộ 10 bóng vừa đo: 93.5546875 mA (~93.55). Cần cập nhật nguồn bằng PUT trong mục 6 để dùng đúng chuẩn.
Hai mẫu nhạy nhiễu hơn 25 mẫu; giữ firmware R002, AVG16, chờ LoRa nghỉ 1500ms đã kiểm chứng.

### voltage_loss / voltage_deviation

Áp trung bình = (V1+V2)/2.
- |áp trung bình| <=0.1 V: voltage_loss; observed=áp trung bình, reference/deviation_percent=null, limit=0.1 V.
- Còn lại lệch quá voltage_tolerance_percent so với nominal_voltage: voltage_deviation.
  observed=áp trung bình, reference=nominal_voltage, deviation_percent có dấu, limit là % (mặc định 10).
- Đúng dung sai không báo. Hai lỗi áp loại trừ nhau.
- Job khoảng 30 giây: đủ hai mẫu có thể tạo Fault ở lần đánh giá tiếp theo, không còn chờ 25 mẫu/3 phút.
  API hiện tại không đợi job.

## 3. Danh sách 17 mã lỗi — DTO chung

| code | Ý nghĩa |
|---|---|
| current_drop | Dòng trung bình 2 mẫu giảm quá 5% |
| telemetry_stale | Điện cũ, không đồng nghĩa node mất kết nối |
| voltage_loss | Áp trung bình gần 0 khi ON |
| voltage_deviation | Áp trung bình lệch quá dung sai |
| ds3231_unavailable | Không nhận DS3231 |
| ds3231_read_failed | Đọc DS3231 thất bại |
| ds3231_time_invalid | Giờ DS3231 không hợp lệ |
| ina226_unavailable | Không nhận INA226 |
| ina226_read_failed | Đọc INA226 thất bại |
| ina226_shunt_out_of_range | Shunt vượt dải đo |
| bh1750_unavailable | Không nhận BH1750 |
| bh1750_read_failed | Đọc BH1750 thất bại |
| microsd_unavailable | Không nhận/mount được microSD |
| microsd_read_failed | Đọc microSD thất bại |
| microsd_write_failed | Ghi microSD thất bại |
| lora_tx_blocked | LoRa bị bận lâu |
| lora_rx_frame_error | LoRa sai CRC/khung nhận |

Mọi anomalies[] dùng chung {code, observed, reference, deviation_percent, limit}.
Lỗi hardware không có trị số: bốn trường số là null.
Bỏ current_deviation/power_deviation/current_when_off/lamp_count_shortfall.

status chẩn đoán: normal | anomaly | stale | no_data.
electrical_window.status: evaluated | insufficient_samples.
sensor_health.status: fresh | stale | unknown.
Summary diagnostic_status thêm unconfigured | source_unavailable; anomaly_codes[] chỉ chứa 17 mã trên.
Kết nối và độ mới điện độc lập: xem [IOT-NODE-STATUS.md](IOT-NODE-STATUS.md).

## 4. Lịch sử theo từng đợt lỗi

GET https://luxmap.com.vn/api/v1/iot/feeders/FDR-009/diagnostics/history?page=1&page_size=20&from=2026-11-19T00:00:00Z&to=2026-11-20T00:00:00Z

Không có body. page>=1, page_size=1..200 (mặc định 50), from/to tùy chọn.
from>=to: 400 VALIDATION_FAILED. Lọc đợt giao khoảng [from,to), không chỉ các đợt bắt đầu trong khoảng.
Trả đầy đủ mốc gốc; không cắt đợt theo bộ lọc. Mới nhất theo started_at rồi history_id giảm dần.
total là số đợt sau lọc, không phải số mẫu hay số phút.

~~~json
{
  "page": 1,
  "page_size": 20,
  "total": 1,
  "items": [
    {
      "history_id": 1,
      "feeder_id": "FDR-009",
      "node_id": "NODE-004",
      "code": "current_drop",
      "status": "active",
      "started_at": "2026-11-19T00:01:00Z",
      "ended_at": "2026-11-19T00:03:00Z",
      "closed_at": null,
      "anomaly": {
        "code": "current_drop",
        "observed": 86.6,
        "reference": 93.5546875,
        "deviation_percent": -7.43,
        "limit": 5
      }
    }
  ]
}
~~~

- Job chạy ~30 giây, ghi tối đa một lượt mỗi phút UTC cho mỗi node.
- 00:01 lỗi, 00:02/00:03 vẫn lỗi: một log started_at=00:01, ended_at cập nhật đến 00:03.
- Dòng giảm 7% rồi 10% vẫn cùng log; anomaly là bằng chứng ở lần ghi gần nhất.
- Mỗi code có đợt riêng; nhiều lỗi đồng thời có nhiều log.
- active: chưa đóng; ended_at là phút cuối XÁC NHẬN lỗi, không khẳng định lỗi vẫn có ở thời điểm GET.
- resolved: có cửa sổ/health hợp lệ xác nhận hết lỗi; closed_at là phút phục hồi, ended_at vẫn là phút lỗi cuối.
- interrupted: đổi node/feeder/nguồn/ngưỡng hoặc không có quan sát lỗi liên tục qua hơn một phút; không coi là phục hồi.
- Upstream không dùng được không kéo dài ended_at và không tự đóng resolved.
- GET không ghi log. Lịch sử lưu PostgreSQL, vẫn đọc theo feeder khi gỡ/thay node.
- Chỉ thu thập sau deploy/migration, không bịa dữ liệu lịch sử trước đó.

## 5. Fault và audit

current_drop -> current_anomaly; voltage_loss -> segment_outage; voltage_deviation -> voltage_anomaly;
hardware -> iot_hardware_fault. telemetry_stale chỉ tạo node_offline khi node thực sự offline (không chỉ replay/điện cũ).
Lỗi điện đủ 2 mẫu tạo Fault, hardware giữ debounce 5 phút, chống trùng Fault chưa đóng giữ nguyên.
Phục hồi đủ chứng cứ mới 90 giây: detected chưa có WorkOrder -> rejected + audit;
đã giao việc thì chỉ ghi chú phục hồi, không tự bỏ công việc. Lịch sử lỗi khác với vòng đời Fault/WorkOrder.
Fault cũ không bị xóa khi bỏ đếm bóng.

## 6. Cấu hình baseline bộ thử

Manager trong phạm vi xã dùng request đầy đủ (đúng node/feeder của mình):

PUT https://luxmap.com.vn/api/v1/iot/nodes/NODE-004

~~~json
{
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009",
  "nominal_voltage": 5,
  "nominal_current_ma": 93.5546875,
  "nominal_power_mw": 467.7734375,
  "voltage_tolerance_percent": 10,
  "max_silence_seconds": 60
}
~~~

Các trường cấu hình đếm bóng cũ còn được nhận để tương thích nhưng không ảnh hưởng chẩn đoán.
Cần deploy BE và apply migration AddIotDiagnosticEpisodes. Không cần nạp lại firmware cho thay đổi thuật toán BE này.
