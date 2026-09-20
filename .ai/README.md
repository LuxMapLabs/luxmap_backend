# `.ai/` — vùng bàn giao giữa các agent

Thư mục này là **kênh trao đổi** giữa các agent làm việc trên repo (Claude Code, Codex, và bất kỳ
agent nào sau này). Nó **không phải nguồn sự thật** về hệ thống.

## Thứ tự ưu tiên — không đổi

1. `docs/api-contract-v1.1.md` (Contract, bản hợp nhất v1.4) — **thắng mọi thứ khác**
2. `docs/tasks-backend.csv` — phạm vi và lịch
3. `CLAUDE.md` = `AGENTS.md` — quy ước kỹ thuật (một file, `AGENTS.md` là symlink)
4. `.ai/**` — **đứng cuối**. Một dòng trong `.ai/` mâu thuẫn với ba mục trên thì nó sai, không phải
   ba mục trên sai.

## Ghi vào đúng tầng — `.ai/` KHÔNG nhận bốn loại này

`CLAUDE.md` mục *"Ghi phát hiện vào ĐÚNG tầng"* vẫn áp nguyên. Agent hay ghi nhầm vào đây vì đây là
file nó đang mở:

| Loại phát hiện | KHÔNG ghi vào `.ai/`, ghi vào |
|---|---|
| Deviation — code/mock lệch Contract | `docs/contract-drift.md` |
| Luật áp nhiều ticket | `docs/api-contract-v1.1.md`, sau khi duyệt + tăng version |
| Ràng buộc kỹ thuật nội bộ, bẫy, quy ước | `CLAUDE.md` |
| Tiến độ ticket | `tracking.html` |

⚠️ Một deviation nằm trong `.ai/results/` là deviation **không tồn tại**: WP5/WP6 không đọc thư mục
này, và file sẽ chết theo ticket. `.ai/` chỉ giữ thứ **chỉ có ý nghĩa trong vòng đời một ticket**.

## Bốn thư mục

| Thư mục | Ai ghi | Ai đọc | Nội dung |
|---|---|---|---|
| `context/` | Mỹ | Mọi agent | Con trỏ + lệnh. Không chứa đặc tả, không chứa quy ước |
| `tasks/` | Agent ra đề (hoặc Mỹ) | Agent thực thi | Yêu cầu một ticket, kèm mục KHÔNG ĐƯỢC làm |
| `results/` | Agent thực thi | Agent review + Mỹ | Đã làm gì, bằng chứng chạy thật, D-item phát sinh |
| `reviews/` | Agent review | Agent thực thi + Mỹ | Phát hiện, mức độ, yêu cầu sửa |

Thư mục mô tả **loại artifact**, không mô tả **agent nào**. Ai review thì ghi vào `reviews/`, kể cả
khi đó là chính agent đã viết `tasks/`.

## Vòng đời một ticket

```
tasks/BE-xx.md         (status: ready)      ra đề, chốt phạm vi + cấm
      ↓
results/BE-xx-p1.md    (phase 1)            khảo sát, KHÔNG sửa code — dừng cứng
      ↓  Mỹ chốt D-item
results/BE-xx-p2.md    (phase 2)            implement + output chạy thật
      ↓
reviews/BE-xx-by-<agent>.md                 agent KHÔNG viết code đi review
      ↓
contract-drift.md / CLAUDE.md / tracking.html
```

Hai pha của `results/` là bắt buộc với ticket chạm schema, Contract, hoặc phân quyền. Ticket nhỏ thì
một file `results/BE-xx.md` là đủ.

## Quy tắc bắt buộc

**Bằng chứng, không suy luận.** `results/` phải dán **output terminal/psql thật**. Định danh cột,
tên constraint, số test pass — chép từ màn hình, không viết từ trí nhớ. Một con số không có lệnh
sinh ra nó thì coi như không có.

**Nghi vấn thành D-item.** Agent không tự chọn phương án kiến trúc. Chỗ mơ hồ ghi thành `D-x` ở cuối
`results/`, Mỹ chốt. Không có ngoại lệ vì "thấy rõ quá rồi".

**Một agent ghi tại một thời điểm.** `tasks/current.md` ghi agent nào đang giữ quyền ghi và trên
branch nào. Agent review mặc định **chỉ đọc `src/`** — nó ghi vào `.ai/reviews/`, không sửa code.
Hai agent cùng ghi một branch là hỏng, và `.ai/` không có khoá nào chặn được việc đó.

**Mục KHÔNG ĐƯỢC làm đi theo task file.** Agent thứ hai không có lịch sử chat của bạn với agent thứ
nhất. Ràng buộc không nằm trong `tasks/BE-xx.md` thì nó không tồn tại.

## Đặt tên

`BE-xx.md` theo mã ticket của `docs/tasks-backend.csv`. Nhiều lượt trên cùng ticket thì thêm hậu tố:
`BE-xx-p1.md`, `BE-xx-p2.md`, `BE-xx-by-codex.md`. Không đặt tên theo ngày.

`tasks/current.md` **không vào git** (đã có trong `.gitignore`) — nó đổi mỗi phiên và chỉ làm bẩn
diff. Ba thư mục còn lại **có vào git**: FW-00 nói quyết định không ghi vào repo thì coi như chưa xảy
ra, và `reviews/` là bằng chứng.
