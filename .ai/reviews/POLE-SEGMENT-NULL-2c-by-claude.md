# POLE-SEGMENT-NULL PR 2c — review của Claude · 03/11/2026

Codex dừng vì báo hết hạn mức trước khi viết file kết quả; Claude đọc toàn bộ diff `src`, chạy lại build / test / OpenAPI, phá thử sáu chốt và viết `.ai/results/POLE-SEGMENT-NULL-p2c.md`.

## Kết luận: ĐẠT — không có yêu cầu sửa

- Guard cabinet đặt ở ĐẦU, trước mọi `ContainsKey`/dictionary; lỗi 409 mang `operation`, `pole_ids` (≤ 100, `created_at, length(pole_id), pole_id`), `pole_count`.
- `segment_name` / `segment_id` là `null` thật ở JSON (test kiểm `JsonValueKind.Null`, không `""`); SQL dùng `FirstOrDefault`, không INNER JOIN road.
- Lighting: `affected_segment_ids` lọc null, `unassigned_segment_pole_count` là `int?` (null khi đọc lại bản lưu), không migration, `uncontrollable_pole_count` không đổi nghĩa.
- Thống kê: nhóm `segment_id: null` đứng cuối trong cùng nguồn/xã; `unknown` / `never_surveyed` giữ nghĩa.
- OpenAPI chỉ nới quan hệ cột → tuyến; test kiểm `SegmentId` PK, `SegmentProperties`, `segment_ids[]` KHÔNG nullable.
- Bằng chứng: build 0 warning; Api 1241 pass / 7 skip; OpenAPI giống từng byte khi sinh lại; 6 phá thử đều đỏ đúng lý do (bảng ở file kết quả).

## Ghi chú (không chặn merge)

- Ca hành vi đổi cho cột CÓ tuyến: `segment_name` của cột nằm trên tuyến liên xã bị ẩn bởi phạm vi xã đổi từ `""` sang `null` (D-19). Cần nêu khi báo WP5.
- Phản hồi POST lighting tạo mới mang số đếm, replay thì `null`: đúng thiết kế đã ghi, nhưng client phải coi `null` là "không ghi lại" (D-18).
- Đột biến `First` → `FirstOrDefault` là tương đương (cùng SQL); đừng coi nó là chốt cần test.
- 2c đổi `docs/openapi/*.json`; #199 (2b) cũng đổi. Sau khi #199 merge phải rebase và SINH LẠI (không giải xung đột bằng tay).
