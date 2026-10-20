# Domain docs

Repo single-context. Cách các skill đọc tài liệu domain ở repo này.

## Trước khi khảo sát, đọc theo thứ tự ưu tiên

Đọc `CLAUDE.md`, mục **Nguồn sự thật**, cho thứ tự hiện hành (kể cả quyết định drift đã chốt);
quy ước theo vùng ở `docs/agents/traps/*.md`. Thuật ngữ đọc `CONTEXT.md` ở gốc repo nếu có.

`CONTEXT.md` chưa có thì **im lặng bỏ qua**; `/domain-modeling` tạo nó khi một thuật ngữ thật sự được chốt.

## Quyết định ghi ở đâu — KHÔNG có `docs/adr/`

Repo này đã có sổ quyết định; một thư mục ADR sẽ là câu trả lời thứ hai không ràng buộc cho cùng câu hỏi.

| Loại | Ghi vào |
|---|---|
| Quyết định của nhóm, deviation so với Contract | `docs/contract-drift.md` (luật FW-00 ở đầu file: ai ký, im lặng bao lâu) |
| Ràng buộc kỹ thuật nội bộ, bẫy | `CLAUDE.md` (luật chung) / `docs/agents/traps/<vùng>.md` (luật vùng) |
| Luật áp nhiều ticket | Contract, chỉ sau khi duyệt và tăng version |

Khi `/domain-modeling` muốn tạo ADR, ghi thành một mục trong `docs/contract-drift.md` thay vào đó.

## Dùng đúng thuật ngữ

Dùng tên trong Contract mục 1 (enum) và `CONTEXT.md`: `fixture_status`, `data_source`, `source_channel`, "đêm" (12:00
Asia/Ho_Chi_Minh), "fault mở" (`FaultStatusSets.Open`)… Đừng đổi sang từ đồng nghĩa.

## Báo mâu thuẫn

Output trái một quyết định trong `docs/contract-drift.md` hoặc Contract thì nói thẳng, không lặng lẽ ghi đè:

> _Trái drift ST-5 (đêm 12:00), nhưng nên mở lại vì…_
