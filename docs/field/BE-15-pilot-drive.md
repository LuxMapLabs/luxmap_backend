# Kịch bản quay thử — khảo sát video đêm (BE-15)

Dành cho **FO** (thực địa) và **WP6** (mobile). Soạn 02/10/2026 bởi WP2. Phiên bản 2 (03/10/2026): phương tiện là
**xe máy**, điện thoại và module BH1750 gắn ở **đầu xe** — không phải ô tô. Sửa sau mỗi buổi thử.

## Vì sao cần buổi này

Backend ghép **frame video + GPS + lux** với từng cột trong GIS (Phiếu v1.4 mục *Survey Data Association*). Mọi tham số
của bước ghép hiện là **ước lượng chưa đo**: sai số GPS dọc đường, độ trễ GPS, độ sắc của đỉnh lux, vận tốc tối đa còn
ghép đúng, cửa sổ cắt frame, số lần quét để có baseline. Buổi này đo các con số đó trên **đúng điện thoại, đúng module
BH1750, đúng con đường** sẽ dùng thật. Kết quả cũng là dữ liệu cho chỉ tiêu *pole-association accuracy* của Phiếu (mục
đánh giá thực địa).

Đây là **dữ liệu thực địa thật** (`data_source = field`). Không lắp gì lên cột, không thao tác lưới chiếu sáng của xã.

## Câu hỏi buổi này phải trả lời

| # | Câu hỏi | Dùng để chốt |
|---|---|---|
| Q1 | Timestamp camera trên máy này có cùng gốc `elapsedRealtimeNanos` không (`SENSOR_INFO_TIMESTAMP_SOURCE = REALTIME`)? Ánh xạ PTS ↔ sensor timestamp từng frame có ổn định không? | D-06 — điều kiện để chạy pipeline thật |
| Q2 | GPS lệch dọc đường bao nhiêu mét, trễ bao nhiêu giây, tệ cỡ nào dưới tán cây? | Cửa sổ thời gian quanh mỗi cột |
| Q3 | Đỉnh lux khi đi dưới đèn sáng cao và hẹp cỡ nào ở từng vận tốc? Có đỉnh giả (đèn xe ngược chiều, biển hiệu) không? Module có bão hoà không? | Thuật toán tìm đỉnh |
| Q4 | Tỉ lệ ghép đúng cột thay đổi thế nào theo vận tốc 15 / 20 / 25 / 30 km/h? | Ngưỡng vận tốc (đang giả định 15–25 km/h) |
| Q5 | Cùng một cột, đỉnh lux dao động bao nhiêu giữa các lần quét lặp lại? | Số lần quét tối thiểu để có baseline (D-05) |
| Q6 | Với profile camera đã khoá, đèn ON / OFF có phân biệt rõ trên frame không? Thấy đèn rõ nhất lúc cột còn cách bao xa? | Profile quay (D-R24), cửa sổ cắt frame |
| Q7 | Trên xe máy: rung làm nhoè frame tới mức nào khi **tắt** chống rung (EIS)? Vị trí xe trong làn (cách mép đường bao xa) làm đỉnh lux của cùng một cột thay đổi bao nhiêu? | Giá gắn, có cần cho phép EIS không, quy định làn chạy |

## Chuẩn bị

### Đoạn đường

- Một đoạn **1–1,5 km** ở Long Phước, chọn trong các tuyến **đã có cột trong hệ thống** (114 cột nhập từ ảnh 28/09, mã `P0xx`).
- Đoạn nên có đủ: phần **thoáng trời**, phần **dưới tán cây**, **một khúc cua**, **một ngã ba/ngã tư**, nếu được thì
  **cột ở cả hai bên đường** và **vài đèn đang tắt**.
- Báo trước cho WP2 đoạn đã chọn để backend xuất danh sách cột (mã, toạ độ, thứ tự) cho phiếu ghi.

### Người

**Hai người trên một xe máy**: **người lái** chỉ lo lái, giữ tốc độ và **giữ vị trí trong làn ổn định** (ví dụ cách mép
phải ~1 m, ghi lại con số đã chọn); **người ngồi sau** bấm nút đánh dấu cột và ghi chép. Người lái **không** thao tác điện thoại.

### Thiết bị

- **Đúng mẫu điện thoại** sẽ dùng khi khảo sát thật, sạc đầy, tắt tiết kiệm pin.
- **Giá kẹp điện thoại ở đầu xe máy**, loại chắc, có giảm rung; **không cầm tay**. Ghi lại vị trí, chiều cao so với mặt đường,
  góc ngẩng, hướng camera (trái/phải/thẳng). **Chụp ảnh giá gắn.** Không đổi giá gắn giữa các lượt. Video quay thử 28/09
  quay cầm tay từ xe máy cho thấy nhoè nhiều và camera tự phơi sáng làm đèn sáng thành một đốm cháy — đúng hai thứ buổi này
  phải loại bỏ.
- **Module BH1750 ở đầu xe**, mặt cảm biến hướng thẳng lên trời, không bị tay lái, kính chắn gió, người lái hay đèn pha của
  chính xe che hoặc rọi vào; ghi chiều cao so với mặt đường. Gắn chắc, không lắc theo tay lái nếu được (gắn vào phần đầu xe
  cố định thay vì ghi-đông).
- Nguồn điện cho module; một điện thoại phụ (tuỳ chọn) để ghi chép.

### App (WP6 xác nhận trước buổi quay)

1. Ghi đủ **4 file mỗi phiên**: clip MP4 (< 1 phút/clip), `gps_track` (JSONL), `lux_log` (JSONL), `capture_config`
   (JSON), theo `docs/survey-ingest-p2a.md`. Mọi thời gian theo **`elapsedRealtimeNanos`**, số nano giây ghi **dạng chuỗi**.
2. `capture_config` có **`sensor_timestamp_source`** đọc từ máy thật, và **bảng ánh xạ PTS ↔ sensor timestamp** cho từng
   clip (hoặc file sidecar thời gian từng frame).
3. **Khoá profile camera** (ISO, shutter, lấy nét vô cực, cân bằng trắng, độ phân giải, fps); **tắt** EIS, HDR, night
   mode, tự động phơi sáng. Ghi giá trị **thực tế** camera báo, không chỉ giá trị app yêu cầu.
4. GPS ghi `speed_mps` và `heading_deg` nếu máy có; tần suất GPS cao nhất máy cho phép.
5. BH1750 ở chế độ đo liên tục; ghi rõ **chế độ và tần suất lấy mẫu** (ví dụ phân giải cao ~8 mẫu/giây).
6. **Nút đánh dấu cột** (rất cần): người quan sát bấm đúng lúc xe **ngang cột**; app ghi `phone_elapsed_ns` của mỗi lần bấm
   vào một file thứ năm `marks.jsonl` (`{"mark_no":0,"phone_elapsed_ns":"…"}`). Đây là **ground truth** để chấm ghép cột.
   Nếu app chưa kịp làm nút: người quan sát **đọc to số thứ tự cột** khi ngang cột — tiếng nằm trong audio của video, cùng
   đồng hồ với frame.
7. (Nên có) Hiện vận tốc trên màn hình và cảnh báo khi vượt ngưỡng.

## Trước khi chạy xe — phiếu ground truth

Đi bộ hoặc chạy chậm một lượt **có đèn đường đã bật**, điền cho **từng cột theo thứ tự trên đoạn**:

| Thứ tự | Mã cột (`P0xx`) | Bên đường (T/P) | Trạng thái nhìn bằng mắt (sáng / mờ / tắt) | Loại đèn (cao áp vàng cam / LED trắng) | Ghi chú (bị cây che, cột đôi, gần biển hiệu…) |
|---|---|---|---|---|---|

Cột nào ngoài thực tế có mà hệ thống **không có** (hoặc ngược lại) — ghi rõ. Trạng thái "mờ" bằng mắt là **ước lượng**,
chỉ dùng tham khảo cho ground truth `dim` (D-R23 chưa chốt).

## Các lượt chạy

Bắt đầu sau khi đèn bật ổn định ít nhất 30 phút. **Mỗi lượt là một phiên riêng** trên app. Giữ tốc độ đều nhất có thể; ghi
lại mọi lần dừng, vượt xe, xe tải đỗ che đèn.

| Lượt | Vận tốc | Chiều | Mục đích |
|---|---|---|---|
| 1 | 15 km/h | A → B | Mốc chậm |
| 2 | 15 km/h | B → A | Chiều ngược |
| 3 | 20 km/h | A → B | |
| 4 | 20 km/h | B → A | |
| 5 | 25 km/h | A → B | Ngưỡng trên giả định |
| 6 | 25 km/h | B → A | |
| 7 | 30 km/h | A → B | **Cố ý vượt ngưỡng** để tìm điểm gãy — chỉ khi đường vắng và an toàn |
| 8, 9, 10 | 20 km/h | A → B | **Lặp lại** để đo độ lặp của đỉnh lux (Q5) |
| 11 | 20 km/h | A → B | **Dừng hẳn 10 giây** dưới một cột đang sáng, rồi đi tiếp |
| 12, 13 | 20 km/h | A → B | **Đổi vị trí trong làn**: một lượt sát mép (~0,5 m), một lượt giữa làn — đo ảnh hưởng tới đỉnh lux (Q7) |

Thời gian dự kiến khoảng 2 giờ (13 lượt). **An toàn trước hết**: không vượt tốc độ cho phép, bỏ lượt 7 nếu không an toàn.

### Ghi chép mỗi lượt

Giờ bắt đầu/kết thúc, thời tiết (khô/mưa, có trăng), lượt có sự cố gì (mất GPS, mất BLE, app lỗi, dừng xe), và **tên phiên
(sweep) trên app**.

## Bàn giao cho WP2

Trong **24 giờ** sau buổi quay:

- Với mỗi lượt: tất cả clip MP4, `gps_track.jsonl`, `lux_log.jsonl`, `capture_config.json`, `marks.jsonl` (nếu có).
  Upload qua `/api/v1/sweeps` nếu app đã nối API; nếu chưa, gửi file gốc **nguyên byte** (không nén lại, không cắt video).
- Phiếu ground truth đã điền, ảnh giá gắn điện thoại và module, bảng ghi chép các lượt.
- Mẫu máy, phiên bản app, phiên bản firmware module.

## WP2 sẽ báo lại

Một báo cáo ngắn trả lời Q1–Q7 bằng số đo, gồm: độ lệch và độ trễ GPS dọc đường, hình dạng đỉnh lux theo vận tốc, **tỉ lệ
ghép đúng cột theo vận tốc** (so với `marks`), độ lặp đỉnh lux giữa lượt 3, 8, 9, 10, và đề xuất số cho các tham số (cửa sổ
cắt frame, ngưỡng vận tốc, số lần quét tối thiểu cho baseline). Các tham số này chốt cùng FO/WP4/WP6 rồi mới dùng cho khảo sát
thật.
