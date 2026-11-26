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
    "samples": 5,
    "required_samples": 5,
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
measurement là mẫu mới nhất; observed của lỗi điện là trung bình NĂM mẫu, không nhất thiết bằng measurement.
Không còn lamp_count, estimated_lit, estimated_missing, votes hoặc threshold_counts.

## 2. Trung bình NĂM mẫu

Năm mẫu phải ON, V/mA/mW hữu hạn, cùng session và có sequence khác nhau. Các mẫu có thể đến từ nhiều gói LoRa và sequence có thể có khoảng trống.
Giờ đo DS3231 tăng theo sequence, giờ đo/giờ nhận CẢ NĂM mẫu còn trong max_silence_seconds.
Mẫu gửi bù không được tính và bản tin lặp chỉ tính một lần. Cửa sổ dừng khi gặp mẫu OFF hoặc dữ liệu không hợp lệ. Không đánh giá lỗi điện khi INA226/DS3231 báo lỗi.
Chưa đủ 5 mẫu: status=insufficient_samples và electrical_window.status=insufficient_samples, required_samples=5, các trung bình null. Đây là chưa đủ bằng chứng, không phải điện bình thường và không phải một mã anomaly mới.
Đủ năm mẫu: status=evaluated. OFF/stale/hardware lỗi: electrical_window=null.
Đây là năm bản tin đo độc lập, không phải số lần INA226 lấy trung bình nội bộ.

### current_drop

current_avg = (I1 + I2 + I3 + I4 + I5) / 5.
deviation_percent = (current_avg - nominal_current_ma) / nominal_current_ma * 100.

Chỉ báo khi current_avg < nominal_current_ma * 0.95; đúng 5% không báo, tăng dòng không thuộc lỗi này.
CẢ NĂM điện áp phải >0.1 V và trong dung sai nominal_voltage. current_evaluation_available cho biết điều kiện xét dòng.
Mất áp/sai áp không được coi dòng tải giảm. Ngưỡng 5% cố định, không dùng current_tolerance_percent cũ.
Baseline là nominal_current_ma của từng feeder; không tự học từ mẫu đang lỗi, không gán 93.55 mA cho mọi feeder.
93.5546875 mA (~93.55) là ví dụ hiệu chuẩn trước đây, không phải baseline chung. Lượt đo thực tế 5 log của bộ đủ 10 đèn ngày 24/11/2026 có dòng trung bình 106.5 mA tại áp trung bình 5.3996 V. Người quản trị chọn baseline đúng điều kiện nguồn và cập nhật bằng PUT trong mục 6; thay đổi này không tự sửa nominal_current_ma trong DB.
Giữ INA226 R002 và AVG16; cách đo/truyền LoRa theo firmware đã nạp. Việc đổi cửa sổ trên BE không thay lịch đo của node.

### voltage_loss / voltage_deviation

Áp trung bình = (V1+V2+V3+V4+V5)/5.
- |áp trung bình| <=0.1 V: voltage_loss; observed=áp trung bình, reference/deviation_percent=null, limit=0.1 V.
- Còn lại lệch quá voltage_tolerance_percent so với nominal_voltage: voltage_deviation.
  observed=áp trung bình, reference=nominal_voltage, deviation_percent có dấu, limit là % (mặc định 10).
- Đúng dung sai không báo. Hai lỗi áp loại trừ nhau.
- Job khoảng 5 giây: đủ năm mẫu có thể tạo Fault ở lần đánh giá tiếp theo, không còn chờ 25 mẫu/3 phút.
  API hiện tại không đợi job.

## 3. Danh sách 17 mã lỗi — DTO chung

| code | Ý nghĩa |
|---|---|
| current_drop | Dòng trung bình 5 mẫu giảm quá 5% |
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

status chẩn đoán: normal | anomaly | stale | no_data | insufficient_samples.
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
      "fault_id": "FAULT-0034",
      "fault_status": "detected",
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

- Job chạy khoảng 5 giây, ghi tối đa một lượt mỗi khoảng 5 giây UTC cho mỗi node.
- 00:01 lỗi, 00:02/00:03 vẫn lỗi: một log started_at=00:01, ended_at cập nhật đến 00:03.
- Dòng giảm 7% rồi 10% vẫn cùng log; anomaly là bằng chứng ở lần ghi gần nhất.
- Mỗi code có đợt riêng; nhiều lỗi đồng thời có nhiều log.
- active: chưa đóng; ended_at là mốc 5 giây cuối XÁC NHẬN lỗi, không khẳng định lỗi vẫn có ở thời điểm GET.
- resolved: có cửa sổ/health hợp lệ xác nhận hết lỗi; closed_at là mốc 5 giây phục hồi, ended_at vẫn là mốc 5 giây xác nhận lỗi cuối.
- interrupted: đổi node/feeder/nguồn/ngưỡng hoặc không có quan sát lỗi liên tục qua hơn một phút; không coi là phục hồi.
- Upstream không dùng được không kéo dài ended_at và không tự đóng resolved.
- GET không ghi log. Lịch sử lưu PostgreSQL, vẫn đọc theo feeder khi gỡ/thay node.
- Chỉ thu thập sau deploy/migration, không bịa dữ liệu lịch sử trước đó.

## 5. Fault và audit

current_drop -> current_anomaly; voltage_loss -> segment_outage; voltage_deviation -> voltage_anomaly;
hardware -> iot_hardware_fault. telemetry_stale chỉ tạo node_offline khi node thực sự offline (không chỉ replay/điện cũ).
Lỗi điện đủ 5 mẫu tạo Fault, hardware giữ debounce 5 phút, chống trùng Fault chưa đóng giữ nguyên.
Phục hồi đủ chứng cứ mới 90 giây: giữ nguyên fault_status (kể cả detected), ghi recovered_at + audit Reported;
tái phát xóa recovered_at, cập nhật bằng chứng và ghi audit một lần trên cùng Fault đang mở. Lịch sử lỗi khác với vòng đời Fault/WorkOrder.
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

Cập nhật 26/11/2026 (IOT-DIAG-5): dùng 5 mẫu ON mới nhất, cùng session, định danh không trùng; không bắt sequence sát nhau. Sắp theo sequence và kiểm tra giờ đo giảm dần, không theo thứ tự nhận HTTP. Hai gói LoRa 4 mẫu có thể ghép đủ cửa sổ dù mất một số sequence ở giữa. Không lấy qua lần OFF, dữ liệu hỏng, timestamp đảo chiều; mẫu gửi bù có reception_type=backfill không được dùng. Mỗi mẫu phải còn mới theo max_silence_seconds ở cả measured_at và received_at. Bản ghi trùng cùng nội dung chỉ tính một lần. Với các mẫu trong lịch sử, dùng giờ nhận sớm nhất trong nhóm trùng; nội dung mâu thuẫn cùng identity ngắt cửa sổ. Client cũ thiếu reception_type vẫn phải qua các kiểm tra thời gian. Thuật toán: electrical-mean5-batched-v2. Node hiện đo khoảng 1 giây/mẫu, gửi theo đợt tối đa 4 mẫu; không cam kết đủ cửa sổ sau đúng 5 giây. Job/lịch sử giữ chu kỳ khoảng 5 giây. Thiếu mẫu không đóng Fault hoặc đánh dấu đợt lỗi đã phục hồi. Chỉ sửa BE, không cần nạp lại thiết bị.


## Sức khỏe feeder trong kiểm kê (FEEDER-HEALTH-1, 26/11/2026)

GET `/api/v1/assets/feeders/{feederId}` trả `feeder.power_state`; GET `/api/v1/assets/feeders` trả `items[].power_state`.
Trường này đổi từ on/off/unknown sang **unknown/normal/warning/incident/offline**. Đầu ra bật/tắt nằm ở trường mới `lamp_state` (on/off/unknown). `control` vẫn giữ chế độ báo cáo của node.

Ví dụ các trường liên quan (không phải toàn response):
```json
{
  "feeder_id": "FDR-009",
  "power_state": "warning",
  "lamp_state": "on"
}
```

| Giá trị | Ý nghĩa |
|---|---|
| unknown | Không có nguồn IoT và không có relay/node được gán cho feeder |
| normal | Node online, mẫu điện mới, không có lỗi; ON đủ cửa sổ 5 mẫu, OFF chủ động không cần cửa sổ ON |
| warning | Có lỗi nhẹ, dữ liệu điện cũ, chưa đủ mẫu hoặc đã gán nhưng nguồn chưa kiểm chứng/Bridge không dùng được |
| incident | Có ít nhất một lỗi nặng khi node chưa được xác định offline |
| offline | Có bằng chứng liên lạc node trước đây nhưng đã quá max_silence_seconds; ACK/health mới vẫn chứng minh online |

### Phân loại mã lỗi

- **incident**: voltage_loss; ina226_unavailable; ina226_read_failed; ina226_shunt_out_of_range.
- **current_drop**: giảm trên 5% đến dưới20% là warning; từ20% trở lên là incident, chỉ khi API chẩn đoán đã xác nhận lỗi và điện áp hợp lệ.
- **voltage_deviation**: đã vượt dung sai cấu hình, lệch dưới20% là warning; từ20% trở lên là incident (cả tăng và giảm).
- **warning**: telemetry_stale; ds3231_unavailable/read_failed/time_invalid; bh1750_unavailable/read_failed; microsd_unavailable/read_failed/write_failed; lora_tx_blocked; lora_rx_frame_error.
- Ngưỡng20% dùng observed/reference gốc, không dùng deviation_percent đã làm tròn để tránh nâng 19,999% thành sự cố20%.

Ưu tiên: chưa gán → unknown; đã mất liên lạc → offline; còn liên lạc thì incident > warning > normal. Mẫu điện cũ nhưng ACK mới → warning, không offline. Thiếu dữ liệu không báo normal. Có IoT nhưng Bridge lỗi → warning, không giả kết luận node offline hoặc chưa có IoT.
Các lỗi đồng thời vẫn giữ riêng trong diagnostics/history; power_state là nhãn ưu tiên cao nhất. Không thay đổi Fault severity/lifecycle, chế độ điều khiển hay lịch sử lỗi.

Lookup giữ một lượt đọc Bridge cho mỗi feeder được cấu hình, DB đọc theo trang trước phần HTTP; không gọi chẩn đoán HTTP vòng lại vào BE. Có ngân sách5 giây; phần chưa xác định giữ warning.

Phạm vi thay đổi là API kiểm kê feeder. `power_state` trên API command/electrical, bản đồ trụ/fixture/tủ vẫn giữ on/off/unknown, vì đó là đầu ra quan sát; không dùng chúng để suy nhãn sức khỏe mới. Không đổi firmware, không migration và không sửa FE. FE kiểm kê muốn hiển thị Bật/Tắt cần dùng lamp_state sau khi BE mới được deploy.

Hướng dẫn riêng về sức khỏe feeder, JSON và bảng lỗi: [FEEDER-HEALTH.md](FEEDER-HEALTH.md).


## Vòng đời Fault IoT và lịch kiểm tra — 26/11/2026

`status` của episode (`active`, `resolved`, `interrupted`) chỉ mô tả quan sát phần cứng.
`fault_id` là liên kết lưu bền của episode; `fault_status` là trạng thái hiện tại của
Fault đó, không phải snapshot trạng thái khi episode được tạo. Cả hai nullable khi
chưa đủ điều kiện tạo Fault; dữ liệu cũ chưa có liên kết vẫn null, không đoán theo mã.
Gỡ/thay node không xóa liên kết lịch sử. FK ghép bảo đảm Fault cùng xã.

1. Phát hiện -> `detected`. Phục hồi đủ bằng chứng mới liên tục 90 giây chỉ ghi
   `recovered_at`, note, audit Reported và thông báo. Không tự rejected/confirmed.
2. Manager tạo Inspection -> vẫn `detected`, có work_order_id. Lỗi đã phục hồi vẫn
   tạo Inspection được; không cần mở lại qua PATCH.
3. Manager nghiệm thu `fault_present` -> `confirmed`; riêng Fault source_channel=iot,
   `fault_absent` -> `rejected`; `inconclusive` giữ nguyên. Người thao tác ghi confirmed_by.
   Các kênh khác giữ FR-2a: fault_absent vẫn chờ quyết định riêng.
4. Manager cũng có thể PATCH Fault detected -> rejected. Hủy phiếu hoặc trả báo cáo
   không tự reject Fault. Không thêm iot_review_status, không đổi nghĩa rejected.
5. Sửa chữa theo luồng có sẵn: confirmed -> in_progress -> resolved -> verified.
   Nghiệm thu Repair hiện chuyển resolved rồi verified trong cùng giao dịch.

Fault tái phát cùng node/feeder/code dùng lại bản ghi đang mở; không đổi quyết định
Manager hoặc WorkOrder. Notification/audit phục hồi và tái phát chỉ ghi khi đổi đợt,
không spam mỗi lượt job. Debounce phần cứng vẫn 5 phút; điện vẫn cửa sổ 5 mẫu.

Mã lỗi mới vẫn tạo Fault riêng. Chỉ tự gom vào đúng một Inspection cùng xã, node và
feeder, còn open/assigned và đang giữ Fault của node đó. Có nhiều phiếu phù hợp,
phiếu đang bị khóa, in_progress/done/verified/cancelled, hoặc Repair: giữ Fault mới
detected để Manager chọn. Không gom chỉ vì cùng tủ. Ghi audit và thông báo bổ sung.
Các thao tác vòng đời WorkOrder và việc thêm Fault được khóa theo phiếu; job không
đợi khóa WO khi đang giữ khóa Fault, tránh deadlock với nghiệm thu.

### Khắc phục dữ liệu cũ

Script `scripts/maintenance/repair-node-004-iot-faults.sql` mặc định dry-run rồi
ROLLBACK. Chạy bằng psql sau khi deploy code mới/migration; không chạy như migration
tự động cho mọi môi trường. Chỉ xét COM-070/NODE-004/FDR-009/CAB-011 và ba mã được
báo cáo; phải có audit Iot Rejected, không có quyết định User, không có WorkOrder
liên quan. Dùng Fault mở hiện có hoặc khôi phục một bản mới nhất mỗi mã về detected.
Các bản trùng không có FK tham chiếu được lưu bản gốc vào bảng backup bảo trì rồi
gộp/xóa; mọi audit cũ được giữ nguyên, thêm audit System. Dòng có FK được giữ và báo
để kiểm tra thủ công. Không khẳng định số lượng là 44 trước khi kiểm tra DB thực tế.

```bash
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f scripts/maintenance/repair-node-004-iot-faults.sql
# Sau khi đọc kết quả dry-run và sao lưu DB, áp dụng cùng script:
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -v apply=true -f scripts/maintenance/repair-node-004-iot-faults.sql
```

Script chạy lại không mở Fault Manager đã từ chối và không tạo bản ghi mới.
Sau xử lý kiểm tra GET /api/v1/faults?data_source=calibration_rig và thử tạo Inspection.
API JSON không có confirmed_by; kiểm chứng ứng viên dùng DB/audit phía vận hành.
