---
ticket: BE-xx
reviewer: <agent>
reviewed: <commit sha hoặc branch>
date: YYYY-MM-DD
verdict: changes_requested    # approved | changes_requested | blocked
---

## Phạm vi đã review

Commit/diff nào, đối chiếu với file nào (Contract mục …, `CLAUDE.md` mục …). Nói rõ **đã chạy gì**:
đọc diff, chạy test, hay chỉ đọc code.

## Phát hiện

| # | Mức | File:dòng | Vấn đề | Đề xuất |
|---|---|---|---|---|
| 1 | blocker | | | |
| 2 | major | | | |
| 3 | minor | | | |

Mức: **blocker** = sai Contract / hở quyền / mất dữ liệu · **major** = đúng nhưng sẽ vỡ ở ticket sau
· **minor** = phong cách, đặt tên.

Mỗi phát hiện phải chỉ ra **file:dòng** và **câu nào trong Contract/`CLAUDE.md`** bị vi phạm. Không
có chỗ neo thì đó là ý kiến, ghi xuống mục dưới.

## Đã kiểm chứng

```
$ <lệnh>
<output>
```

Với phát hiện dạng "test không bắt được lỗi này": chứng minh bằng **sabotage** — sửa hỏng có chủ đích
rồi dán output cho thấy test vẫn xanh.

## Ý kiến, không phải phát hiện

Thứ không neo được vào Contract hay `CLAUDE.md`. Để riêng để khỏi lẫn với blocker.
