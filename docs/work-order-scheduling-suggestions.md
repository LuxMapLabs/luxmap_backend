# Gợi ý phân lịch cho Quản lý

WO-SCHEDULING-SUGGESTIONS · 19/11/2026 · additive, SELF-SIGNED, chờ WP5/WP6 xác nhận.

## API

`GET /api/v1/work-orders/suggestions` trả `{generated_at, night_of, groups[]}`. Mỗi nhóm gồm
`kind, total, ready_count, in_work_order_count, blocked_count, pole_count, segment_count`.
Cả năm nhóm luôn có, kể cả khi rỗng. Số cột/tuyến là số ID khác nhau, không phải số sự cố.

`GET /api/v1/work-orders/suggestions/items?kind=…` trả phân trang chuẩn `{page,page_size,total,items[]}`.
`kind` (query bắt buộc): `fault_inspection`, `fault_repair`, `missing_gps`, `survey_needed`, `overdue`.
Mỗi item có `id, kind, state, commune_id, segment_id, segment_name, segment_has_geometry, pole_id, external_ref, note, fault_id,
fault_type, fault_status, severity, data_source, work_order_id, work_order_title, assigned_to, wo_status, recommended_task_kind, blocking_reason,
observed_at, scheduled_date, due_date`. Trường không áp dụng là null. `work_order_id` trỏ phiếu hiện có,
không phải một phiếu được tạo khi GET.
Thông tin phiếu được tải theo lô cho trang hiện tại để FE hiện người được giao/trạng thái mà không gọi từng phiếu.
Ở nhóm overdue, pole_count là 0 vì item là phiếu; segment_id là tuyến đại diện (tuyến đầu của survey),
hoặc tuyến đã chọn ở bộ lọc. Lọc segment_id vẫn xét mọi tuyến/target của phiếu, không chỉ tuyến đại diện.

Cả hai endpoint: capability `ManageWorkOrders` (chỉ manager). Bộ lọc `commune_id` lặp, `segment_id`,
`data_source` CSV; mặc định **field**. Xã ngoài quyền → 403 `COMMUNE_FORBIDDEN`; nguồn / kind sai →
400 `VALIDATION_FAILED`. `page/page_size` theo quy ước chung, page_size tối đa 200. Chi tiết thêm
`state=ready|in_work_order|blocked` (tuỳ chọn). Các bộ lọc không phụ thuộc tháng đang mở trên lịch.

## Nguồn và quy tắc

| kind | Một item / điều kiện | Việc gợi ý |
|---|---|---|
| fault_inspection | Một fault detected | inspection |
| fault_repair | Một fault còn mở, đủ điều kiện repair theo WorkOrderRules | repair |
| missing_gps | Một pole.geom null, kể cả chưa gán tuyến | pole_gps_update |
| survey_needed | Một cột đã có GPS, trạng thái công bố thiếu hoặc unknown | survey |
| overdue | Một phiếu thoả WorkOrderOverdueJob.OverdueOn(đêm hiện tại) | Mở phiếu để xử lý, không tạo phiếu mới |

Fault dùng loại hiệu lực (`override_fault_type ?? fault_type`). Một cột có thể có nhiều sự cố; warning đèn hỏng
phải đọc cả số sự cố và pole_count. Không biến unknown thành lamp_out, không suy thiếu quan sát là hỏng.
Nguồn các warning fault theo fault; GPS/survey theo cột. Overdue lọc nguồn theo đối tượng của phiếu:
fault nếu là inspection/repair có fault, pole target nếu GPS, cột trên tuyến (hoặc tuyến rỗng) nếu survey/inspection tuyến.
Một phiếu nhiều nguồn xuất hiện một lần trong mỗi lượt lọc; không cộng các lượt lọc để suy tổng.

`state=in_work_order` khi fault/pole đang được giữ (`released_at IS NULL`), hoặc tuyến của cột unknown đã có
phiếu survey cùng xã chưa verified/cancelled. Với survey, work_order_id là phiếu tạo sớm nhất trong các phiếu
phù hợp. Overdue luôn trỏ chính phiếu quá hạn. Phiếu done chờ duyệt vẫn giữ mục tiêu, tránh giao lại sớm.

Nếu chưa có phiếu giữ, `state=blocked` khi thiếu điều kiện; `blocking_reason`:
`POLE_SEGMENT_REQUIRED`, `SEGMENT_NOT_ACCESSIBLE`, `CROSS_COMMUNE_REFERENCE` (GPS phải cùng xã tuyến).
Các tên/hình học tuyến ngoài scope không được lộ. Tuyến chưa vẽ **vẫn giao khảo sát/GPS được** theo RG/PG;
`segment_has_geometry=false` giúp FE nhắc vẽ tuyến trước khi nộp sweep (ingest mới yêu cầu hình). Các mục còn lại là ready. Chỉ ready mới có recommended_task_kind.

Mỗi nhóm sắp mức nghiêm trọng giảm dần (fault), mốc cũ trước, rồi độ dài ID và ID. Overdue sắp hạn trước.
Mỗi response đọc cùng một snapshot DB; không ghi audit/notification. Gợi ý không giữ chỗ: POST vẫn kiểm lại và
có thể trả 409 nếu người khác đã nhận việc. Không thêm ngưỡng "lâu chưa khảo sát" chưa được chốt.
Fault/GPS có chốt giữ mục tiêu ở command hiện có. Survey vẫn cho nhiều phiếu cùng tuyến vào các đêm khác nhau;
`in_work_order` là thông tin để tránh giao lặp trên UI, không phải khoá độc quyền tuyến hay cơ chế đặt chỗ.

## Tích hợp Web và Mobile

Vào màn Phân lịch → tải summary → bấm nhóm → tải chi tiết (mặc định UI có thể chọn state=ready).
Giữ trang lịch và nút tạo thủ công. Form lấy targets từ gợi ý, để manager chọn title, kỹ sư và ngày:

- inspection/repair: `POST /work-orders` với task_kind và fault_ids (cùng xã, tối đa 200).
- pole_gps_update: cùng POST với segment_id và pole_ids (cùng tuyến/xã, tối đa 200).
- survey: cùng POST với commune_id và segment_ids (duy nhất, có thứ tự). Một nhóm nhiều cột cùng tuyến chỉ gửi tuyến một lần.
- overdue / in_work_order: mở work_order_id hiện có. blocked: hiển thị lý do và điều hướng bổ sung dữ liệu tài sản.

Sau tạo/duyệt/huỷ → tải lại summary và trang chi tiết. Không giới hạn cảnh báo theo tháng của calendar.
GPS thu thập từ giấy/Excel: import cột chưa GPS bằng luồng hiện có → giao pole_gps_update → kỹ sư complete với
pole_locations → manager verify mới ghi GIS. Offline tiếp tục qua sync bundle/push hiện có; API gợi ý chỉ dành cho manager online.
Chức năng mới không cần migration, không đổi task_kind, không thay đổi hình dạng các endpoint cũ.

## Triển khai và rollback

Đưa commit này lên cùng quy trình deploy API hiện có; không chạy migration hay nạp lại dữ liệu. FE chỉ gọi hai
route mới sau khi API đã được cập nhật. Rollback bằng revert commit / deploy binary trước: chỉ hai route mới
không còn; dữ liệu tài sản, phiếu và các API cũ giữ nguyên, không cần rollback database.
