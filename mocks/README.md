# Bộ mock FO-26

Bàn giao kèm API Contract v1.1. Đây là **nguồn dữ liệu chuẩn** cho cả nhóm:
WP5 và WP6 code giao diện theo bộ này, BE-39 seed database từ chính bộ này.

## Danh sách file

| File | Endpoint tương ứng | Contract |
|---|---|---|
| `mock-poles.geojson` | `GET /poles` | 2.1 |
| `mock-pole-detail.json` | `GET /poles/{id}` | 2.2 |
| `mock-segments.geojson` | `GET /segments` | 2.3 |
| `mock-faults.json` | `GET /faults` | 2.4 |
| `mock-work-orders.json` | `GET /work-orders` | 2.6 |
| `mock-iot-nodes.geojson` | `GET /iot-nodes` | 2.7 |

## Những gì đã cố ý cài sẵn

- **103 cột**: 70 `normal` · 9 `dim` · 17 `out` · 7 `unknown`
- **`SEG-003`** có một cụm lỗi cả đoạn — dùng test luồng highlight toàn tuyến
  (`has_active_segment_fault = true`)
- **11 IoT node**
- **`POLE-0047`** là cột solar, có chuỗi runtime suy giảm dần qua 18 đêm —
  dùng test biểu đồ cảnh báo sớm pin
- Đa số cột có `iot_node = null`, đúng như thực tế kiến trúc sparse IoT

Bảy cột `unknown` không phải lỗi dữ liệu. `unknown` nghĩa là sweep gần nhất
không phủ được cột đó, và phải có ký hiệu riêng trên bản đồ — không gộp màu
với `out`.

## Quy tắc sử dụng

- **Repo backend là nguồn gốc.** Bản trong repo FE chỉ là bản sao để đọc.
- Sửa file ở đây thì **phải báo WP5 và WP6** — họ đang code theo.
- BE-39 đọc thẳng từ thư mục này, **không copy sang `src/`**. Có hai bản là
  chắc chắn sẽ lệch, và lúc demo mới phát hiện.
- Commit vào git, không `.gitignore`. Đây là một phần của hợp đồng.
