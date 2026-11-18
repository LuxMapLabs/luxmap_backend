# API chẩn đoán lỗi IoT LuxMap

## Gọi API

- Route chung: `GET /api/v1/iot/nodes/{nodeId}/diagnostics`
- Testbed: [Chẩn đoán NODE-004 trên VPS](https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/diagnostics)
- Trong Postman, chọn **GET**, **Authorization → Bearer Token** và nhập access token của LuxMap. Chọn **Body → none**. API không cần query string.
- Tài khoản cần quyền `ReadNetwork`. Các vai trò có quyền này gồm `system_admin`, `superior`, `manager` và `field_engineer`. API áp dụng phạm vi xã/phường của tài khoản khi đọc node.

`NODE-004` là mã node trong LuxMap. Nguồn đo của node này dùng mã thiết bị MQTT `LUXMAP-P001` và mạch điện `FDR-009` trong testbed. FE truyền mã node LuxMap vào URL.

## Cách đọc JSON

### Trường cấp ngoài

| Trường | Ý nghĩa |
|---|---|
| `node_id` | Mã node LuxMap được chẩn đoán. |
| `feeder_id` | Mạch điện mà nguồn đo của node đang giám sát. |
| `status` | Kết quả chẩn đoán chung, không phải trạng thái kết nối. Xem bảng trạng thái bên dưới. |
| `connection_status` | Kết nối node: online / offline / no_data, dựa trên liên lạc đã xác thực. |
| `electrical_status` | Độ mới điện: fresh / stale / no_data. ACK không làm mới điện. |
| `last_seen_at` | UTC liên lạc node mới nhất (ACK/telemetry/sensorHealth). |
| `last_command_ack_at` | UTC ACK hợp lệ mới nhất; ACK lặp không đổi thời điểm. |
| `measurement` | Mẫu điện gần nhất. Có thể là `null` nếu chưa có mẫu. Nếu mẫu đã cũ, API vẫn có thể trả mẫu cuối để tra cứu. |
| `anomalies` | Danh sách cảnh báo hiện tại. Mỗi lỗi dùng cùng DTO `IotAnomaly`. Nếu chưa phát hiện cảnh báo, mảng là `[]`. |
| `lamp_count` | Kết quả ước lượng số bóng khi đủ điều kiện chạy kiểm tra. Có thể là `null`. |
| `sensor_health` | Độ mới của bản tin tình trạng phần cứng mà Bridge nhận từ node. |

| Giá trị `status` | Ý nghĩa |
|---|---|
| `normal` | Mẫu điện còn mới và chưa có cảnh báo theo dữ liệu đang có. |
| `anomaly` | Có bất thường điện, thiếu bóng hoặc lỗi phần cứng từ bản tin mới. Lỗi phần cứng mới vẫn báo `anomaly` khi chưa có mẫu điện. |
| `stale` | Mẫu điện đã quá hạn và có cảnh báo `telemetry_stale`. Nếu đồng thời có lỗi phần cứng từ bản tin mới, trạng thái chung là `anomaly` và mảng cảnh báo có cả hai lỗi. |
| `no_data` | Chưa có mẫu điện và chưa có lỗi phần cứng được xác nhận từ bản tin còn mới. |

### Mẫu điện và tình trạng phần cứng

| Trường | Ý nghĩa |
|---|---|
| `measurement.received_at` | Thời điểm Bridge nhận mẫu điện, theo UTC. |
| `measurement.lamp_command` | Trạng thái đèn mà node báo trong mẫu, `ON` hoặc `OFF`. |
| `measurement.voltage` | Điện áp, đơn vị V. |
| `measurement.current_ma` | Dòng điện, đơn vị mA. |
| `measurement.power_mw` | Công suất, đơn vị mW. |
| `sensor_health.received_at` | Thời điểm Bridge nhận bản tin tình trạng phần cứng, theo UTC. Giá trị `null` nếu chưa có bản tin hợp lệ. |
| `sensor_health.status` | `fresh` khi bản tin còn trong 60 giây, `stale` khi đã quá hạn, `unknown` khi chưa có bản tin hợp lệ. `fresh` cho biết độ mới của bản tin. Xem `anomalies[]` để biết phần cứng có lỗi hay không. |

### Form chung của mọi cảnh báo

| Trường | Ý nghĩa |
|---|---|
| `code` | Mã cảnh báo để FE chọn nội dung hiển thị. |
| `observed` | Giá trị quan sát được. Đơn vị thay đổi theo mã lỗi, ví dụ giây, volt hoặc số bóng. |
| `reference` | Giá trị chuẩn để so sánh, ví dụ điện áp danh định. |
| `deviation_percent` | Phần trăm sai lệch có dấu. Số âm nghĩa là thấp hơn chuẩn. |
| `limit` | Ngưỡng kích hoạt cảnh báo, đơn vị theo mã lỗi. |

Mỗi cảnh báo luôn có đủ 5 trường. Giá trị số nào không áp dụng thì trả `null`. FE có thể dùng một thành phần hiển thị, chọn thông báo từ `code` và chỉ hiện các trường số có giá trị.

## Danh sách 17 mã cảnh báo

### Mẫu điện và số bóng

| Mã | Khi nào xuất hiện | Số liệu đi kèm |
|---|---|---|
| `telemetry_stale` | Giờ nhận hoặc giờ đo RTC đã quá ngưỡng nguồn, mặc định 60 giây. Có thể xuất hiện khi node vẫn online nhờ ACK. | `observed` là tuổi mẫu tính bằng giây, `limit` là ngưỡng tính bằng giây. |
| `voltage_loss` | Đèn báo `ON`, mẫu còn mới và trị tuyệt đối của điện áp đo được ≤ 0,1 V. | `observed` là điện áp đo được, `limit` là 0,1 V. |
| `voltage_deviation` | Đèn báo `ON`, mẫu còn mới và điện áp lệch điện áp danh định quá ngưỡng, mặc định 10%. | `observed` và `reference` tính bằng V. `deviation_percent` và `limit` tính bằng %. |
| `lamp_count_shortfall` | Cửa sổ 25 mẫu ON đủ điều kiện ước lượng cho thấy số bóng sáng thấp hơn số bóng hiệu chuẩn. | `observed` là số bóng thiếu. Chi tiết nằm trong `lamp_count`. |

Với `voltage_deviation`, số đo 4,4 V so với mức chuẩn 5 V cho kết quả `(4,4 − 5) / 5 × 100 = −12%`. Nếu ngưỡng là 10%, API trả `observed: 4.4`, `reference: 5`, `deviation_percent: -12` và `limit: 10`.

Khi `lamp_count.status` là `estimated`, đọc số bóng sáng và số bóng thiếu qua `estimated_lit` và `estimated_missing`. `insufficient_samples` nghĩa là chưa đủ 25 mẫu hợp lệ. `undetermined` nghĩa là đã đủ mẫu nhưng chưa xác định được một nhóm hiệu chuẩn. Xem [quy tắc hiệu chuẩn và dữ liệu 25 mẫu](LUXMAP-IOT-FE.md#hiệu-chuẩn-bộ-thử-10-bóng-ở-nguồn-khoảng-4-v).

### Cảm biến, microSD và LoRa

Bốn trường số trong bảng dưới có giá trị `null`.

| Thiết bị | Mã | Khi nào xuất hiện |
|---|---|---|
| DS3231 | `ds3231_unavailable` | Không nhận hoặc không khởi tạo được đồng hồ. |
| DS3231 | `ds3231_read_failed` | Đồng hồ đã khởi tạo nhưng đọc giờ qua I²C thất bại. |
| DS3231 | `ds3231_time_invalid` | Đọc được đồng hồ nhưng giờ không hợp lệ hoặc đồng hồ báo từng mất nguồn. |
| INA226 | `ina226_unavailable` | Không nhận hoặc không khởi tạo được cảm biến điện. |
| INA226 | `ina226_read_failed` | Cảm biến đã khởi tạo nhưng đọc thanh ghi đo thất bại. |
| INA226 | `ina226_shunt_out_of_range` | Số đo tại điện trở shunt chạm giới hạn của INA226. |
| BH1750 | `bh1750_unavailable` | Không nhận hoặc không khởi tạo được cảm biến ánh sáng. |
| BH1750 | `bh1750_read_failed` | Cảm biến đã sẵn sàng đo nhưng đọc ánh sáng thất bại. |
| MicroSD | `microsd_unavailable` | Không khởi tạo được hàng đợi dữ liệu trên thẻ, ví dụ chưa gắn thẻ hoặc không mở được hệ thống tệp. |
| MicroSD | `microsd_read_failed` | Không mở hoặc đọc được hàng đợi, hoặc gặp bản ghi lỗi. |
| MicroSD | `microsd_write_failed` | Không ghi được mẫu lên thẻ. File hàng đợi hiện có giới hạn 64 MiB. |
| LoRa | `lora_tx_blocked` | Có gói chờ phát và chân AUX giữ LOW liên tục ít nhất 10 giây. |
| LoRa | `lora_rx_frame_error` | Node nhận lặp lại các gói LoRa lỗi CRC, quá dài hoặc thiếu dữ liệu. |

Node xác nhận lỗi và phục hồi của DS3231, INA226 và BH1750 sau 3 lần kiểm tra liên tiếp có cùng kết quả. Bridge lưu bản tin tình trạng rồi trả ACK. Khi cảm biến phục hồi, bản tin mới bỏ mã lỗi đó.

Nếu đường LoRa mất hoàn toàn, node chỉ gửi được lỗi LoRa khi đường truyền hoạt động lại. Trong lúc chờ, FE dùng `telemetry_stale` và `sensor_health.status` để hiện dữ liệu đã cũ. Các dấu hiệu này có thể xuất hiện khi node mất nguồn hoặc đường truyền bị gián đoạn.

## Ví dụ JSON

Node vừa báo lỗi cảm biến ánh sáng và ghi microSD. Mẫu điện gần nhất đã 92 giây. Cả ba cảnh báo dùng cùng form.

```json
{
  "node_id": "NODE-004",
  "feeder_id": "FDR-009",
  "status": "anomaly",
  "measurement": {
    "received_at": "2026-11-17T06:28:40Z",
    "lamp_command": "ON",
    "voltage": 4.024,
    "current_ma": 51.25,
    "power_mw": 206.23
  },
  "anomalies": [
    {
      "code": "telemetry_stale",
      "observed": 92,
      "reference": null,
      "deviation_percent": null,
      "limit": 60
    },
    {
      "code": "bh1750_read_failed",
      "observed": null,
      "reference": null,
      "deviation_percent": null,
      "limit": null
    },
    {
      "code": "microsd_write_failed",
      "observed": null,
      "reference": null,
      "deviation_percent": null,
      "limit": null
    }
  ],
  "lamp_count": null,
  "sensor_health": {
    "status": "fresh",
    "received_at": "2026-11-17T06:30:05Z"
  }
}
```

Nếu node báo lỗi phần cứng trước khi Bridge có mẫu điện đầu tiên, `measurement` là `null`. FE vẫn đọc được mã lỗi trong `anomalies[]`.

## Mã phản hồi HTTP

| HTTP | `error.code` | Ý nghĩa |
|---|---|---|
| 200 | — | Đọc chẩn đoán thành công. Các bất thường nằm trong `anomalies[]`. |
| 401 | `UNAUTHENTICATED` | Thiếu token hoặc token hết hiệu lực. |
| 403 | `ROLE_FORBIDDEN` | Tài khoản thiếu quyền đọc mạng lưới. |
| 404 | `ASSET_NOT_FOUND` | Không tìm được node trong phạm vi được phép. |
| 503 | `IOT_SOURCE_UNAVAILABLE` | Nguồn đo chưa dùng được hoặc BE không đọc được dữ liệu hợp lệ từ Bridge. |

## Phiên bản cần chạy để test

Để nhận các mã lỗi phần cứng trên VPS, cần firmware node và gateway có hỗ trợ `sensorHealth`, cùng bản IoT Bridge và LuxMap API tương ứng. Có thể kiểm tra theo thứ tự **node gửi → gateway chuyển MQTT → Bridge trả `sensorHealthAck` → API hiện mã trong `anomalies[]`**.

Xem [OpenAPI của backend](openapi/luxmap-v1.5.json) và [hướng dẫn tích hợp IoT cho FE](LUXMAP-IOT-FE.md).


## Luồng chẩn đoán và Fault tự động — cập nhật 18/11/2026

Backend chạy job `iot_diagnostics` mỗi 30 giây. Job đọc Bridge, lưu snapshot và bằng chứng trong PostgreSQL.
FE gọi API đọc snapshot để hiển thị. Việc mở Dashboard không phải điều kiện để phát hiện sự cố.
Chu kỳ thực tế có thể dài hơn khi nhiều nguồn Bridge phản hồi chậm.

### API tổng hợp có phân trang

```http
GET https://luxmap.com.vn/api/v1/iot/diagnostics/summary?page=1&page_size=20
```

Dùng Bearer token và quyền `ReadNetwork`, giới hạn theo xã của tài khoản.
`page_size` tối đa 200. API lấy một trang từ DB, không gọi Bridge lần lượt cho từng node trong request.

```json
{
  "page": 1,
  "page_size": 20,
  "total": 1,
  "items": [
    {
      "node_id": "NODE-004",
      "feeder_id": "FDR-009",
      "diagnostic_status": "anomaly",
      "anomaly_codes": ["lamp_count_shortfall"],
      "estimated_lit": 8,
      "estimated_missing": 2,
      "fault_ids": ["FAULT-0001"],
      "evaluated_at": "2026-11-18T04:00:00Z"
    }
  ]
}
```

Ví dụ minh họa. ID Fault thực tế được DB cấp. `fault_ids` chứa sự cố đang xử lý hoặc chờ nghiệm thu của node.
`estimated_lit` và `estimated_missing` có thể null. `evaluated_at` là thời điểm job đánh giá gần nhất theo UTC.

### diagnostic_status gồm những giá trị nào?

| diagnostic_status | Ý nghĩa |
|---|---|
| `normal` | Dữ liệu đủ để đánh giá và chưa phát hiện cảnh báo theo dữ liệu đang có. |
| `anomaly` | Có ít nhất một cảnh báo điện, số bóng hoặc phần cứng còn mới. |
| `stale` | Mẫu điện quá hạn, có `telemetry_stale`. Lỗi phần cứng mới có thể làm trạng thái tổng thành `anomaly`. |
| `no_data` | Chưa có mẫu điện hợp lệ và chưa có lỗi phần cứng mới xác nhận. |
| `unconfigured` | Node chưa có nguồn Bridge để job đánh giá. |
| `source_unavailable` | Có nguồn nhưng đọc Bridge thất bại, cấu hình không dùng được, hoặc snapshot quá 90 giây. |

API chi tiết `GET /iot/nodes/{nodeId}/diagnostics` tiếp tục dùng trường `status` với bốn giá trị
`normal | anomaly | stale | no_data`. Lỗi nguồn của API chi tiết trả HTTP 503.
API tổng hợp trả `diagnostic_status` cho từng hàng để một nguồn lỗi không làm hỏng cả trang.
Snapshot chưa có ở node mới được xem là chưa được job cấu hình/đánh giá. Dùng `evaluated_at` để phân biệt.
`normal` vẫn có thể đi cùng `lamp_count.status=insufficient_samples` hoặc `undetermined`; FE cần đọc kết quả đếm riêng.

### anomaly_codes gồm những mã nào?

`anomaly_codes` là mảng các chuỗi bên dưới. Mảng `[]` nghĩa là snapshot hiện tại không có mã cảnh báo.
Khi nguồn chưa sẵn sàng, mảng rỗng chưa chứng minh thiết bị hoạt động bình thường; đọc `diagnostic_status` trước.

| anomaly_code | Nội dung |
|---|---|
| `telemetry_stale` | Mẫu điện quá hạn. Có thể do nguồn đo hoặc đường truyền gián đoạn. |
| `voltage_loss` | Điện áp ≤ 0,1 V trong mẫu ON còn mới. |
| `voltage_deviation` | Điện áp lệch quá ngưỡng so với danh định khi ON. |
| `lamp_count_shortfall` | Ước lượng số bóng sáng thấp hơn số bóng chuẩn của feeder. |
| `ds3231_unavailable` | Không nhận hoặc khởi tạo được DS3231. |
| `ds3231_read_failed` | Đọc DS3231 thất bại. |
| `ds3231_time_invalid` | Giờ DS3231 không hợp lệ. |
| `ina226_unavailable` | Không nhận hoặc khởi tạo được INA226. |
| `ina226_read_failed` | Đọc INA226 thất bại. |
| `ina226_shunt_out_of_range` | Điện áp shunt chạm giới hạn đo. |
| `bh1750_unavailable` | Không nhận hoặc khởi tạo được BH1750. |
| `bh1750_read_failed` | Đọc BH1750 thất bại. |
| `microsd_unavailable` | Không khởi tạo được hàng đợi microSD. |
| `microsd_read_failed` | Đọc hàng đợi microSD thất bại. |
| `microsd_write_failed` | Ghi hàng đợi microSD thất bại. |
| `lora_tx_blocked` | Có gói cần gửi nhưng AUX giữ LOW quá ngưỡng. |
| `lora_rx_frame_error` | Nhận lặp lại khung LoRa lỗi CRC, thiếu hoặc quá dài. |

Ba mã `current_deviation`, `power_deviation`, `current_when_off` tiếp tục được loại khỏi chẩn đoán.

### Từ cảnh báo sang Fault

| anomaly_code | fault_type | Điều kiện tạo |
|---|---|---|
| `telemetry_stale` khi connection_status=offline | `node_offline` | Vượt `max_silence_seconds` của nguồn Bridge. |
| `voltage_loss` | `segment_outage` | Lỗi giữ liên tục 3 phút trong các mẫu ON mới. |
| `voltage_deviation` | `voltage_anomaly` | Lỗi giữ liên tục 3 phút trong các mẫu ON mới. |
| `lamp_count_shortfall` | `lamp_out` | Hai cửa sổ 25 mẫu ON mới, không chồng nhau, xác nhận thiếu bóng. |
| 13 mã phần cứng | `iot_hardware_fault` | Bản tin phần cứng fresh tiếp tục xác nhận lỗi trong 5 phút. |

Các Fault tạo với `source_channel=iot`, `fault_status=detected` và giữ `data_source` của node.
`telemetry_stale` mô tả mẫu điện cũ; điều khiển vẫn có thể trả ACK.
Chỉ tạo Fault `node_offline` khi `connection_status=offline` và `last_seen_at` quá ngưỡng.
Nếu node vẫn online, giữ cảnh báo điện cũ trong diagnostics, không tạo Fault mất kết nối giả.
Job offline cũ tiếp tục dùng ngưỡng hệ thống cho thiết bị ngoài luồng Bridge.

Bằng chứng theo node, nguồn và mã lỗi được lưu DB qua restart. Cùng một bản tin không tăng xác nhận.
Khoảng gián đoạn bằng chứng mới quá 90 giây làm bắt đầu lại thời gian xác nhận.
Mẫu đo cũ gửi bù chỉ phục vụ lịch sử, không dùng làm bằng chứng điện mới hoặc phục hồi.
Phần cứng cần `sensor_health.status=fresh`.

Chống trùng bằng khóa node + feeder + mã lỗi khi Fault đang `detected | confirmed | in_progress | resolved`.
Khóa hàng và unique index bảo vệ trường hợp nhiều backend cùng chạy.
Một lần lỗi đã phát hiện không tạo lại chỉ vì job chạy lại hoặc Manager đã đóng nó trong cùng đợt lỗi.

### Tự phục hồi và audit

Sau ít nhất 90 giây có các bản tin mới xác nhận lỗi đã hết:

- Fault `detected` chưa có WorkOrder chuyển `rejected`, ghi `recovered_at`, ghi chú hệ thống và audit actor `iot`.
- Fault đã gắn WorkOrder hoặc đã tiến sang trạng thái khác giữ trạng thái nghiệp vụ, thêm dấu hiệu phục hồi và audit.
- Không gán một tài khoản giả vào `confirmed_by`. Quyết định tự động có actor và correlation ID trong audit.
- Dữ liệu nguồn không đọc được, bản tin cũ, đếm bóng chưa đủ mẫu hoặc không xác định không được xem là phục hồi.

Fault trả thêm `origin_node_id`, `feeder_id`, `cabinet_id`, `iot_anomaly_code`, `recovered_at`.
Evidence lưu bằng chứng đo, thuật toán `iot-faults-v2-contact` (bản lịch sử có thể là `iot-faults-v1`), phiên bản hiệu chuẩn và firmware được báo trong sensorHealth
tại thời điểm đánh giá. Firmware của bản đo lịch sử chưa có metadata vẫn chưa xác định được.

Dòng tổng chỉ ước lượng số bóng thiếu của feeder. `pole_id` và `fixture_id` được giữ null khi chưa xác định bóng nào hỏng.
Manager dùng `fault_ids` để tạo phiếu qua API WorkOrder hiện tại. Phiếu sửa chữa cần Fault được xác nhận theo luồng hiện có.

### Xem sự cố testbed trên Dashboard

```http
GET https://luxmap.com.vn/api/v1/faults?source_channel=iot&data_source=calibration_rig&page=1&page_size=20
```

Dùng quyền `ReadFaults` và phạm vi xã. Danh sách Fault mặc định tiếp tục lọc dữ liệu calibration_rig ra.
FE truyền bộ lọc trên khi đang xem bộ thử.

### Chọn hiệu chuẩn mới 5v-quiet-v1

Profile dùng cho R002, trung bình 64 lần và node đo khi LoRa nghỉ, AUX HIGH ổn định 1,5 giây.
Điện áp được nhận cho đếm bóng là 4,80–5,30 V. Hai profile `4v` và `5v` giữ hành vi cũ.

32 mẫu thực nghiệm cho mỗi cấu hình:

| Bóng | Khoảng đo thực nghiệm mA | Trung bình mA |
|---|---|---|
| 7 | 58,75–71,25 | 66,72 |
| 8 | 75–85 | 80,31 |
| 9 | 81,25–91,25 | 86,60 |
| 10 | 91,25–101,25 | 96,37 |

Khoảng bỏ phiếu có biên từ chối để tách phần chồng lấn:

| Số bóng | Khoảng bỏ phiếu mA |
|---|---|
| 7 | 58,75–72,25 |
| 8 | 74,75–82,20 |
| 9 | 84,75–90,20 |
| 10 | 92,75–102,50 |

Cửa sổ cần 25 mẫu liên tiếp cùng session, đúng thứ tự sequence, ON, đủ mới theo DS3231 và trong khoảng điện áp.
Tối thiểu 20/25 mẫu phải thuộc cùng một dải và trung vị phải thuộc chính dải đó.
Khoảng trống giữa các dải và các mức 1–6 chưa hiệu chuẩn cho kết quả `undetermined`.
Số bóng thiếu = max(0, installed_lamp_count − estimated_lit).
Các ngưỡng này là hiệu chuẩn bộ thử, cần đo lại khi đổi nguồn, điện trở LED, shunt hay cách đấu.

Manager sửa nguồn hiện có bằng request đầy đủ:

```http
PUT https://luxmap.com.vn/api/v1/iot/nodes/NODE-004
```

```json
{
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009",
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500,
  "lamp_count_calibration": "5v-quiet-v1",
  "installed_lamp_count": 10
}
```

Không tự chuyển các nguồn đang dùng profile cũ. Admin tạo nguồn mới cũng có thể chọn `lamp_count_calibration` khi đăng ký.
API tổng hợp và job cần bản BE mới cùng migration `AddIotDiagnosticFaults`.


## Kết nối và dữ liệu điện tách biệt — cập nhật 18/11/2026

ACK mới có thể cho connection_status=online trong khi electrical_status=stale và anomalies vẫn có telemetry_stale.
Không dùng telemetry_stale một mình để gắn nhãn node mất kết nối hoặc tạo NodeOffline Fault.
Job chỉ tạo NodeOffline nếu last_seen_at thực sự quá ngưỡng; khi hồi phục vẫn tuân thủ debounce/audit.
Mẫu gửi bù phải kiểm giờ đo, không chỉ received_at.
Xem [quy tắc, JSON và tương thích](IOT-NODE-STATUS.md).
