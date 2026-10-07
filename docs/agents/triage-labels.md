# Triage labels

Vai trò triage của bộ skill ánh xạ vào trường `status:` / `owner:` sẵn có trong front matter của `.ai/tasks/<mã>.md`.
Không thêm dòng trạng thái thứ hai.

| Vai trò trong skill | Trong ticket `.ai/tasks` | Nghĩa |
|---|---|---|
| `needs-triage` | `status: draft` | Mỹ cần đánh giá |
| `needs-info` | `status: blocked` | Chờ thông tin / D-item chưa chốt / phần cứng — ghi lý do ở `depends_on` |
| `ready-for-agent` | `status: ready` | Đặc tả đủ, agent làm được |
| `ready-for-human` | `status: ready` + `owner: Mỹ` | Cần người làm |
| `wontfix` | `status: wontfix` | Không làm |

Khi skill nói "áp label X", đổi `status:` (và `owner:` nếu cần) theo bảng này.
