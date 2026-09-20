# Đọc gì trước khi bắt đầu

Bảng tra cho agent. **File này chỉ là con trỏ** — không chép nội dung từ nơi khác về đây, vì bản chép
sẽ lệch trong vòng một tuần (`AGENTS.md` đã lệch `CLAUDE.md` 102 dòng trong 3 ngày, 17→20/09/2026).

| Câu hỏi | File |
|---|---|
| Endpoint này hình dạng gì, mã lỗi nào, enum nào | `docs/api-contract-v1.1.md` — **thắng mọi thứ** |
| Bản máy đọc khớp 1-1 với Contract | bản hợp nhất mới nhất trong `docs/openapi/` — **tên file đổi theo version**, xem `docs/contract-drift.md` |
| Ticket này phạm vi tới đâu, tuần nào | `docs/tasks-backend.csv` |
| Viết code theo quy ước gì, bẫy nào đã biết | `CLAUDE.md` (= `AGENTS.md`) |
| Ai ký, im lặng bao lâu tính là gì, quyết định treo đi đâu | đầu `docs/contract-drift.md` (FW-00) |
| Code đang lệch Contract ở đâu | phần thân `docs/contract-drift.md`; log cũ `docs/archive/` |
| Viết endpoint mới thì phải làm gì để không hở quyền | `docs/authorization-guide.md` — **đọc trước khi viết** |
| Đọc code theo thứ tự nào để hiểu hệ thống | `docs/code-walkthrough.md` |
| Ticket nào xong, ticket nào đang treo | `tracking.html` — **nguồn duy nhất** về tiến độ |
| Chạy lệnh gì | `README.md`; bản rút gọn ở `.ai/context/commands.md` |

## Ba thứ tuyệt đối không tự ý làm

1. **Không sửa `docs/api-contract-v1.1.md` hay `docs/openapi/*.json`** khi chưa có quyết định đã ghi.
   Lệch phát hiện được thì đăng ký vào `docs/contract-drift.md`, không sửa Contract cho khớp code.
2. **Không tạo migration** khi chưa hỏi. Schema là thứ WP4/WP5/WP6 đã dựng lên trên.
3. **Không tự chọn phương án kiến trúc.** Ghi thành D-item, Mỹ chốt.

## Cột mốc trạng thái (cập nhật khi lệch thực tế)

- `dev` là branch làm việc; `main` chỉ có commit init.
- Các trường status/completion trong `docs/tasks-backend.csv` là **trường chết** — đọc `tracking.html`.
- Open item O-6: `pole.feeder_id` chưa có dữ liệu — đang chặn BE-13 và CV-15.
- Contract hợp nhất là **v1.5**, file máy đọc là `docs/openapi/luxmap-v1.5.json`. `CLAUDE.md` và
  `README.md` đã được đồng bộ (20/09/2026) — chỗ nào còn ghi `luxmap-v1.4.json` là **lịch sử**
  (`tracking.html`, `docs/review/`, `docs/contract-drift.md`), đừng "sửa cho nhất quán".
