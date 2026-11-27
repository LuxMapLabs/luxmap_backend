# Bắt đầu và kết thúc sửa feeder

Dùng Bearer token của **kỹ sư hiện trường được giao phiếu sửa chữa** (`field_engineer`). Hai nút chỉ áp dụng cho phiếu `task_kind: "repair"` đã chuyển sang `wo_status: "in_progress"` bằng API bắt đầu phiếu.

## Bắt đầu sửa

`POST https://luxmap.com.vn/api/v1/work-orders/{workOrderId}/repair`

```json
{"action": "start"}
```

Backend lấy các lỗi còn liên kết với phiếu. Với từng lỗi, ưu tiên `feeder_id`. Nếu trường này null, backend tra `pole_id` để lấy feeder của trụ. Nếu kết quả chỉ có một feeder, backend tự chọn. Các feeder phải thuộc cùng xã với phiếu.

Khi bắt đầu lần đầu mà phiếu liên quan nhiều feeder, API trả `409 WORK_ORDER_FEEDER_SELECTION_REQUIRED` và `error.details.feeder_ids`. FE cho kỹ sư chọn một mã từ danh sách rồi gửi lại. Để bắt đầu sửa một feeder khác trong cùng phiếu, gửi rõ `feeder_id` của mạch đó.

```json
{"action": "start", "feeder_id": "FDR-009"}
```

Phản hồi `200 OK` khi bắt đầu lúc 10:00 giờ Việt Nam:

```json
{
  "id": 42,
  "work_order_id": "WO-0123",
  "feeder_id": "FDR-009",
  "status": "repairing",
  "started_at": "2026-11-27T03:00:00Z",
  "ended_at": null
}
```

Gửi lại `start` khi khoảng sửa của cùng phiếu vẫn đang mở trả lại bản ghi đó. Nếu một phiếu khác đang sửa feeder này, API trả `409 FEEDER_ALREADY_REPAIRING`.

## Sửa xong

Gọi lại cùng URL với `{"action": "finish"}`. Backend dùng bản ghi đã lưu lúc bắt đầu, kể cả khi trụ đã được chuyển mạch. Nếu phiếu có nhiều khoảng sửa đang mở, gửi thêm `feeder_id` để chọn đúng khoảng. Kết quả có `status: "finished"` và `ended_at` là giờ server ghi. Gọi lại `finish` cho khoảng vừa đóng trả lại giờ kết thúc đã lưu.

Nếu kỹ sư hoàn thành phiếu hoặc Quản lý hủy phiếu trước khi gọi `finish`, backend tự đóng các khoảng đang mở trong cùng giao dịch và ghi audit. Các điều kiện nghiệp vụ của API hoàn thành phiếu, gồm ảnh sau sửa, vẫn áp dụng.

## FE đọc trạng thái và lịch sử

- `GET /api/v1/assets/feeders/{feederId}` trả `feeder.power_state: "repairing"` trong lúc sửa. Danh sách feeder dùng cùng giá trị. `lamp_state` là trạng thái bật/tắt riêng và có thể là `unknown` trong lúc tạm ngưng đọc.
- `GET /api/v1/iot/feeders/{feederId}/diagnostics` và `GET /api/v1/iot/feeders/{feederId}/electrical` trả `status: "repairing"` khi tương ứng. Backend bỏ lượt đọc điện hiện tại và không dùng thời gian sửa để phát hiện hay kết luận phục hồi lỗi IoT của feeder.
- Bridge vẫn nhận và ACK dữ liệu của node để tránh tồn hàng đợi. Sau khi kết thúc sửa, chẩn đoán điện chỉ dùng mẫu có **giờ đo sau** `ended_at`. Đồ thị lịch sử vẫn giữ các mẫu đã nhận.
- `GET /api/v1/iot/feeders/{feederId}/electrical/history?page=1&page_size=20&sort_by=measured_at` trả thêm `repair_periods`. `page`, `page_size` và `total` vẫn tính riêng cho các mẫu điện. Khi truyền `from` hoặc `to`, API lấy những khoảng sửa giao với dải thời gian được hỏi. FE dùng `work_order_id` để đối chiếu khoảng sửa với phiếu hiện tại.

```json
{
  "feeder_id": "FDR-009",
  "sort_by": "measured_at",
  "page": 1,
  "page_size": 20,
  "total": 0,
  "items": [],
  "repair_periods": [
    {
      "work_order_id": "WO-0123",
      "started_at": "2026-11-27T03:00:00Z",
      "ended_at": "2026-11-27T03:30:00Z"
    }
  ]
}
```

Ví dụ này hiển thị dải **10:00–10:30 — Đang sửa** theo UTC+7. Nếu `ended_at: null`, dải kéo đến thời điểm hiện tại.

## Mã lỗi để FE xử lý

| HTTP | Mã | Khi nào |
| --- | --- | --- |
| 400 | `VALIDATION_FAILED` | `action` sai hoặc `feeder_id` rỗng. |
| 403 | `ROLE_FORBIDDEN` | Tài khoản không có quyền thực hiện phiếu. |
| 404 | `WORK_ORDER_NOT_FOUND` | Phiếu không tồn tại, ngoài phạm vi hoặc được giao cho người khác. |
| 409 | `INVALID_STATE_TRANSITION` | Phiếu không phải phiếu sửa chữa hoặc chưa ở `in_progress`. |
| 409 | `WORK_ORDER_FEEDER_NOT_FOUND` | Không tìm được feeder hợp lệ từ lỗi còn liên kết. |
| 409 | `WORK_ORDER_FEEDER_SELECTION_REQUIRED` | Cần chọn feeder. Danh sách nằm trong `error.details.feeder_ids`. |
| 409 | `FEEDER_ALREADY_REPAIRING` | Feeder đã có một khoảng sửa mở bởi phiếu khác. |
| 409 | `FEEDER_REPAIR_NOT_STARTED` | Chưa có bản ghi sửa phù hợp để kết thúc. |
