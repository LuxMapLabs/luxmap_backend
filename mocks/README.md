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

- **103 cột**: 70 `normal` · 10 `dim` · 16 `out` · 7 `unknown`
- **`SEG-003`** có một cụm lỗi cả đoạn — dùng test luồng highlight toàn tuyến
  (`has_active_segment_fault = true`)
- **12 IoT node**: 3 `segment_controller` · 9 `sampled_fixture`
- **`POLE-0047`** có chuỗi runtime suy giảm dần qua 18 đêm — dùng test biểu đồ
  cảnh báo sớm. Mang `fixture_status = dim` và có `NODE-047`: đèn **mờ dần**,
  không tắt phụt.
  ⚠️ **Trước 22/09/2026 đây là cột solar và câu chuyện là pin yếu.** Đèn solar
  đã ra khỏi phạm vi đồ án, nên cột này nay là `grid` / `led_road_lamp`. Chuỗi
  runtime và `runtime_decline` **giữ nguyên** — giờ sáng của đèn lưới cũng suy
  giảm được, và đó vẫn là kịch bản IoT phát hiện sớm
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

## O-6 — mạch điện cho bộ mock (chờ FO điền)

Hai file rỗng đã dựng sẵn khuôn, **chưa có dữ liệu nào**. Người điền cần biết mạch điện thật
của địa bàn — Contract mục 9 giao O-6 cho **Dylan + FO**.

| File | Ai điền | Nội dung |
|---|---|---|
| `mock-feeders.csv` | FO | Danh sách tủ điện. **Chưa có dòng nào** |
| `mock-pole-feeders.csv` | FO | Cột nào đấu vào tủ nào. 103 dòng đã điền sẵn phần tra cứu |

> ⚠️ **O-6 như Contract đang ghi là THIẾU MỘT NỬA.** Mục 9 chỉ nhắc
> `mock-pole-feeders.csv`, nhưng bộ mock **không có một tủ điện nào** — không có `mock-feeders.*`,
> và bảng `feeder` trên DB dev đang rỗng. Không gán cột vào tủ chưa tồn tại được, nên
> `mock-feeders.csv` phải điền **trước**.

### Điền thế nào

**Chỉ sửa cột cuối cùng, `feeder_external_ref`.** Bốn cột đầu là bản chép từ
`mock-poles.geojson` để tra cứu cho dễ — sửa chúng không có tác dụng gì, vì chương trình nạp
khớp theo `pole_external_ref` và **bỏ qua** phần còn lại. Sinh lại được bất cứ lúc nào từ chính
`mock-poles.geojson`.

**Cả 103 dòng đều cần điền.**

> ⚠️ **Con số này ĐÃ ĐỔI ngày 22/09/2026.** Trước đó chỉ 58 dòng cần điền: 45 cột là
> `solar_all_in_one` và cột solar không đấu vào mạch nào. Đèn solar nay đã ra khỏi phạm vi đồ án,
> nên **mọi cột đều chạy điện lưới và đều phải có tủ điện**. Cột `power_source` đã bỏ khỏi file —
> nó chỉ còn một giá trị nên không phân biệt được gì nữa.
>
> Kéo theo: **`SEG-002` nay cũng cần tủ điện.** Ghi chú cũ nói tuyến đó toàn solar nên không bao giờ
> sinh cụm lỗi theo mạch — điều đó **không còn đúng**.

| Tuyến | Số cột cần gán |
|---|---|
| `SEG-001` | 46 |
| `SEG-002` | 31 |
| `SEG-003` | 26 |
| | **103** |

Giá trị trong `feeder_external_ref` phải khớp một `external_ref` có trong `mock-feeders.csv`.
Không phải `FDR-001` — mã đó do DB sinh lúc INSERT, người soạn file không biết trước. Cùng quy ước
`*_external_ref` mà bốn template ở `docs/templates/` đang dùng.

### Sau khi điền

Việc nạp thuộc **BE-39**, qua `scripts/seed_mock_set.py`, **không** qua endpoint import — endpoint
import không giữ được ID của mock (BE-REVIEW-02 ràng buộc 7b). Script hiện ghi cứng
`feeder_id = NULL` cho cả 103 cột; sửa chỗ đó là việc của ticket nạp, không phải của người điền file.

**Xong O-6 chưa gỡ chặn hết CV-15** — CV-15 còn phụ thuộc **BE-13**, mà BE-13 đang chờ duyệt hình
dạng endpoint (`docs/review/BE-13-topology-shape.md`).

## Đổi ID 18/09/2026 (BE-REVIEW-02, D-9) — WP5 và WP6 phải kéo lại

Bộ mock nay khớp bảng prefix Contract §0.2 (drift 6 đóng):

| Trước | Sau | Vì sao |
|---|---|---|
| `NODE-0001` … `NODE-0085` | `NODE-001` … `NODE-085` | `NODE` pad 3 chữ số (sàn) |
| `SWEEP-2026-07-21-01` … `SWEEP-2026-08-19-01` | `SWP-001` … `SWP-030` (theo thứ tự thời gian) | prefix `SWP`, 3 chữ số |
| `FRM-88213` | `FRM-088213` | `FRM` pad 6 chữ số |
| `USR-khang` | `USR-004` | tài khoản seed `crew` (thứ tự `SeedUsers`) |
| `supplier: "SUP-004"` | *(bỏ)* | không có prefix `SUP` trong §0.2; `supplier` không thuộc hình dạng §2.2 |

Tên tuyến (`segment_name`) BE ≠ FE (drift 42) **chưa** đổi — chờ người biết địa bàn.

