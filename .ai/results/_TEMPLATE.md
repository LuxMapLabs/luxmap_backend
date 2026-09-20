---
ticket: BE-xx
phase: 1               # 1 = khảo sát · 2 = implement
agent: <agent>
branch: <branch>
date: YYYY-MM-DD
status: done           # done | blocked
---

## Đã làm gì

Liệt kê theo việc, không theo file. Mỗi việc một dòng.

## Bằng chứng

Dán **output thật**, không viết lại từ trí nhớ. Mỗi khối phải có lệnh sinh ra nó.

```
$ dotnet test
<output>
```

```
$ docker compose exec postgres psql -U luxmap -d luxmap_dev -c '\d pole'
<output>
```

## File đã đổi

| File | Đổi gì |
|---|---|

## D-item — cần Mỹ chốt

| # | Câu hỏi | Các hướng | Ảnh hưởng nếu chọn sai |
|---|---|---|---|
| D-1 | | | |

## Lệch Contract phát hiện được

Có lệch thì **không ghi ở đây** — đăng ký vào `docs/contract-drift.md` và dẫn số drift về đây:

- Drift <số>: <một dòng>

## Chưa làm / cố ý bỏ

Nêu rõ, kèm lý do. Im lặng bỏ một yêu cầu là lỗi nặng hơn làm sai.
