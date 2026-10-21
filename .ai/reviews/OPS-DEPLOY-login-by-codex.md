# Review backend `99ec6fd..0b926bc`

Ngày: 20/10/2026 · Chỉ đọc · Không sửa code/script/runbook thuộc phần review.

## Phạm vi

Đã đọc `git log --oneline 99ec6fd..0b926bc`, diff và code liên quan. Trọng tâm theo yêu cầu:
PR #155 (runbook / deploy / backup), #156 (rate limit đăng nhập), #160 (`CHANGE_ME`), #165 (cron).
Đọc thêm diff backend WO-SURVEY-CHAIN trong cùng khoảng và task / review hiện có.

## Kết quả hai trục

- **Quy ước repo:** chưa có lỗi đủ bằng chứng để báo. Không nêu góp ý phong cách hay lỗi do công cụ đã kiểm.
- **Yêu cầu/hành vi:** chưa có lỗi đủ bằng chứng để báo trong phạm vi chỉ định.

Các đường đã đối chiếu:

- Mobile và web cùng policy `Login`; bucket theo `RemoteIpAddress`. `UseLuxMapForwardedHeaders` chạy trước rate limiter;
  `KnownProxies` khớp IP Caddy `172.30.0.10` trong compose. Không thấy đường lấy IP từ header client không tin cậy.
- Test host nâng permit trong `CreateHost`; test chuyên biệt hạ qua override. Header đặt peer chỉ nằm trong test host.
- Pipeline kiểm `CHANGE_ME` bỏ dòng comment, có `|| true` cho nhánh không khớp dưới `set -euo pipefail`; bước backup
  tạo/kiểm quyền thư mục trước build. Chưa thấy lỗi shell mới từ diff.
- Cron `15 20` theo UTC đúng 03:15 UTC+7; lệnh môi trường tối giản giữ HOME và PATH chứa Docker trên VPS theo runbook.
- Runbook xoá mật khẩu dev khi API còn tắt; cặp NULL khớp CHECK và luồng gửi thư mời. Script copy không mang refresh/account
  token dev sang đích. Hướng dẫn URL web/mobile khớp nội dung thay đổi, chưa kiểm consumer ngoài repo.
- Diff WO-SURVEY-CHAIN khớp task hiện có: lấy kết quả mới nhất theo cột trong các sweep đã duyệt của đúng phiếu;
  lọc nguồn / loại hiệu lực / xã neo, đọc lại fault và loại phiếu đang giữ sau khoá.

## Xác minh

```text
$ bash -n deploy/deploy.sh deploy/backup.sh
(không output), exit 0
```

Không chạy `dotnet test`, Docker, cron thật hay lệnh trên production. Kết luận review dựa trên đọc mã và kiểm cú pháp shell;
không khẳng định đã kiểm hành vi trên VPS. Không có phát hiện để gắn nhãn `[BLOCKER|NÊN SỬA|NHỎ]`.
