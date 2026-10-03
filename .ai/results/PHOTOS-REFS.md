# PHOTOS-REFS — Kết quả thực thi

Ngày: 03/10/2026. Agent: Codex. Nhánh: `fix/photos-existing-refs`.

## Đã thực hiện

- `scripts/photos_to_poles.py`: đọc `--existing` lặp lại từ JSON assets/poles (trang hoặc mảng trang), CSV import POINT, và GeoJSON cũ. Kiểm mã rỗng/trùng trong từng file và giữa các file trước khi ghi kết quả; lỗi định dạng/toạ độ trả thông báo CLI.
- Bắt buộc `--existing` hoặc xác nhận `--first-survey`. Hai chế độ loại trừ nhau. Thêm `--match-m` mặc định 8 m; kiểm `0 < match_m <= review_m`, hữu hạn.
- Ghép sau gộp/bỏ nhóm và sửa GPS. Chỉ gắn khi đúng một cột cũ trong bán kính ghép; hai cột gần, cột nằm giữa bán kính ghép và review, hoặc nhiều nhóm cùng khớp một cột đều chờ người duyệt.
- Nhóm khớp dùng mã vĩnh viễn trong `observations.csv.external_ref`; nhóm mơ hồ để trống mã. `poles.csv` và `fixtures.csv` chỉ có cột mới, tránh import thay thế toàn phần xoá feeder/đổi tuyến.
- `position_updates.csv`: `external_ref`, `old_lat`, `old_lng`, `proposed_lat`, `proposed_lng`, `distance_m`, `photo_count`; chỉ để duyệt, không phải file import.
- Giữ thuật toán `assign_refs`, cấp mã theo nhóm gốc nhưng loại ảnh của nhóm đã khớp/chờ review khỏi lượt cấp mã. Nhóm khớp không chiếm hậu tố mới. Mã mới trùng mã cũ ngoài vùng ghép làm dừng trước khi ghi.
- `scripts/tests/test_photos_to_poles.py`: 19 test unittest chạy CLI thật bằng subprocess, tự tạo JPEG tối thiểu có TIFF/EXIF thời gian, múi giờ và GPS. Toàn bộ dùng thư viện chuẩn Python.
- `.github/workflows/ci.yml`: thêm bước `python3 -m unittest discover -s scripts/tests` ngay sau setup Python.
- Đã cập nhật docstring, phần công cụ ảnh của `README.md`, và `tracking.html`.

## Bằng chứng kiểm tra

✅ Lệnh đã chạy:

```text
$ python3 -m unittest discover -s scripts/tests
...................
----------------------------------------------------------------------
Ran 19 tests in 1.104s

OK
```

Phủ khớp mã cũ/không xuất cập nhật tài sản hoặc fixture; ba ca mơ hồ; gộp/bỏ nhóm trước ghép;
cột mới và chạy lại ổn định; cùng giây nhưng nhóm khớp không chiếm mã mới; giữ hậu tố khi bỏ nhóm mới;
thiếu chế độ khảo sát; khảo sát đầu; CSV BOM và JSON nhiều trang, nhiều file;
mã rỗng/trùng; GeoJSON cũ cảnh báo; file hỏng; bán kính sai; xung đột mã mới/cũ; bán kính tuỳ chỉnh.
Test còn kiểm nội dung ảnh sao chép giữ nguyên bytes và các CSV kết quả.

✅ Kiểm tra thủ công đầu ra CLI bằng hai JPEG tự sinh trong thư mục tạm: một ảnh cách cột cũ 3 m,
một ảnh cách 50 m. Dùng helper sinh dữ liệu của bộ test qua `runpy.run_path`, chạy CLI rồi đọc CSV:

```text
2 ảnh, 0 bị loại -> 2 nhóm cột
poles.csv: 1 dòng · review.csv: 0 ca cần xem · 0 gợi ý gộp · kết quả ở /private/var/folders/p4/lqscdcx518n3_rqm92rh0n7w0000gn/T/tmpwbpc6fcb/out
⚠️  poles.csv còn ô bắt buộc để trống (external_ref / segment_external_ref / commune_id) — điền trước khi import.
```

Giá trị đọc từ CSV thực tế:

- `poles.csv`: chỉ P002, mã `KS-20261003-190040`, `POINT(106.0000000 10.0004497)`.
- `observations.csv`: P001 → `KS-CU`, `POLE-0001`; P002 → `KS-20261003-190040`.
- `position_updates.csv`: `KS-CU`, cũ `(10.0000270, 106.0000000)`, đề xuất `(10.0000000, 106.0000000)`, `3.0` m, `1` ảnh.
- Cảnh báo ô trống đúng dự kiến vì ca kiểm này không cung cấp tuyến/xã.

✅ `git diff --check` không xuất lỗi; đã rà diff. Nhánh xác nhận bằng `git branch --show-current`:

```text
fix/photos-existing-refs
```

## Giới hạn và giả định

⚠️ Chưa chạy CI Linux hoặc kiểm ảnh thực địa/DB/API thật. Không chạy build/test .NET vì ticket chỉ sửa công cụ Python và cấm dùng DB thật.

📌 Người chạy cung cấp đủ danh sách cột của vùng khảo sát. Công cụ chỉ đọc file, không tải trang API còn thiếu.
GeoJSON cũ thiếu hẳn trường `external_ref` là ngoại lệ tương thích: cảnh báo và giữ ca gần cột ở review;
trường có mặt nhưng rỗng vẫn là lỗi. CSV không chứa `pole_id` vẫn gắn được bằng mã cũ, còn
`observations.csv.existing_pole` để trống. Mã mới ổn định với cùng ảnh, danh sách cột cũ và tham số ghép;
đổi các đầu vào ghép có thể đổi tập nhóm được cấp mã.

Không đọc `.env`, không gọi DB/API thật, không đọc thư mục `img*` hoặc `Videos/`, không sửa .NET hay
`docs/templates/*`, không thêm dependency. Không stage, commit hoặc push theo yêu cầu. Không có thay đổi
Contract/schema cần quyết định mới. Đã dừng sau bàn giao này.

## Claude kiểm (03/10/2026)

- `python3 -m unittest discover -s scripts/tests`: 19/19.
- **Dữ liệu thật:** chạy lại trên chính 150 ảnh 28/09 với đúng lệnh đã nạp (`img_osm/LENH_img_out7.txt`) và `--existing` là
  114 cột thực địa xuất từ `luxmap_dev` (`external_ref`, `geom_wkt`): **0 cột mới** trong `poles.csv`, 109 nhóm khớp lại đúng
  mã `KS-…` cũ (lệch 0,00 m), 5 nhóm vào `gan_cot_cu` — đúng, vì trong DB có 3 cột cách nhau 2,4 m và 2 cột cách nhau 4,1 m
  (`KS-20260928-222349/352/356`, `KS-20260928-223122/123`): ảnh mới với GPS lệch vài mét không thể quy chắc về một cột. Năm cột
  này có thể là **cùng một cột chụp nhiều lần** lúc nạp — đề nghị xem lại ảnh.
- Phá thử: cho ghép khi có hai cột cũ trong bán kính ⇒ `test_two_nearby_old_poles` đỏ; bỏ bắt buộc `--existing`/`--first-survey`
  ⇒ `test_missing_existing_stops` đỏ. Đã khôi phục.
