---
ticket: PHOTOS-REFS
title: Công cụ ảnh — khảo sát lại phải gắn vào mã cột cũ, không sinh cột trùng
status: ready
phase: 2
owner: codex
branch: fix/photos-existing-refs
depends_on:
  - "Quyết định external_ref (03/10/2026): CLAUDE.md mục external_ref, docs/contract-drift.md mục 'external_ref — chốt'"
---

## Bối cảnh

`scripts/photos_to_poles.py` biến thư mục ảnh thực địa thành bản nháp import. `external_ref` của cột mới là
`<prefix>-<ngày>-<giờ chụp>` — mã của **lần chụp**. Nhóm đã chốt `external_ref` là **mã vĩnh viễn**. Vấn đề khi **khảo sát
ảnh lần hai** trên đoạn đã có cột:

1. Không chạy `--existing` ⇒ mọi cột chụp lại thành **cột mới** với mã mới ⇒ import tạo **cột trùng**, không cảnh báo gì.
2. `--existing` chỉ đọc GeoJSON theo `pole_id` (không biết `external_ref`), và nhóm ảnh gần cột cũ bị **bỏ khỏi poles.csv**
   ⇒ quan sát mới (`observations.csv`, bật/tắt bằng mắt) **không gắn được** vào cột cũ.

Đọc trước: docstring đầu file, `assign_refs`, `read_existing`, `find_reviews`, `main`; `AGENTS.md` mục external_ref và BE-12a
(import là **thay thế toàn phần**: dòng thiếu `feeder_external_ref` **xoá mạch điện** của cột).

## Yêu cầu

1. **`--existing` nhận danh sách cột đã có KÈM `external_ref`**, từ một trong hai dạng (tự nhận dạng):
   - JSON trả về của `GET /api/v1/assets/poles` (một trang `{items:[…]}` hoặc mảng nhiều trang), lấy `external_ref`,
     `location.lat/lng`, `pole_id`;
   - một `poles.csv` theo khuôn import (`external_ref` + `geom_wkt` POINT), ví dụ bản đã nạp lần trước.
   Có thể truyền nhiều lần `--existing`. GeoJSON cũ theo `pole_id` vẫn đọc được nhưng báo rõ là không có `external_ref`.
   Cột có `external_ref` rỗng hoặc trùng ⇒ lỗi rõ, dừng, không ghi gì.
2. **Ghép nhóm ảnh với cột cũ** (sau khi gộp/bỏ nhóm như hiện nay):
   - cột cũ gần nhất trong `--match-m` (mặc định **8 m**, vì cột cách nhau 20–25 m và toạ độ là chỗ người chụp đứng) **và**
     không có cột cũ thứ hai trong `--match-m` ⇒ **khớp**;
   - hai cột cũ trong `--match-m`, hoặc gần nhất nằm giữa `--match-m` và `--review-m` ⇒ **mơ hồ**, vào `review.csv`
     (kind mới, ví dụ `gan_cot_cu`), **không** ghi vào poles.csv, **không** tự gắn;
   - hai nhóm ảnh cùng khớp một cột cũ ⇒ review, không tự quyết.
3. **Nhóm khớp KHÔNG ghi vào `poles.csv`** (không có dòng cập nhật cột cũ — tránh xoá mạch điện / đổi tuyến qua import). Thay vào đó:
   - `observations.csv` thêm cột **`external_ref`**: mã cũ với nhóm khớp, mã mới với cột mới; để quan sát bật/tắt gắn đúng cột;
   - file mới **`position_updates.csv`** (`external_ref`, toạ độ cũ, toạ độ đề xuất, khoảng cách m, số ảnh) — chỉ để **người
     xem duyệt**, không phải file import.
4. **Không cho quên `--existing`:** chạy không có `--existing` thì **dừng** với thông báo rõ, trừ khi truyền cờ tường minh
   `--first-survey` (khảo sát lần đầu, chưa có cột nào). Cập nhật docstring và ví dụ lệnh.
5. Mã của cột mới vẫn theo `assign_refs` (ổn định qua các lần chạy lại trên cùng ảnh). Một nhóm khớp cột cũ **không** tiêu
   tốn mã mới.
6. **Test** chỉ dùng thư viện chuẩn: `scripts/tests/test_photos_to_poles.py` (`unittest`), tự sinh JPEG tối thiểu có EXIF
   (thời gian, GPS) trong code, phủ: khớp đúng mã cũ; mơ hồ (hai cột cũ gần, khoảng giữa 8–20 m, hai nhóm cùng một cột) vào
   review; cột mới nhận mã mới; thiếu `--existing` mà không `--first-survey` ⇒ dừng; đọc cả hai dạng `--existing`; `external_ref`
   rỗng/trùng ⇒ dừng. Thêm bước CI chạy `python3 -m unittest discover -s scripts/tests`.
7. Cập nhật `README.md` phần công cụ ảnh. Ghi `.ai/results/PHOTOS-REFS.md` (tiếng Việt).

## KHÔNG ĐƯỢC làm

- Không thêm thư viện ngoài (chỉ stdlib). Không gọi API/DB thật; không đọc `.env`; không đọc thư mục `img*`, `Videos/` của repo.
- Không đổi khuôn import (`docs/templates/*`), không đổi code .NET.
- Không commit, không push, không `git add -A`.
