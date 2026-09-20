---
ticket: BE-xx
title: <một dòng>
status: draft          # draft | ready | in_progress | blocked | done
phase: 1               # 1 = khảo sát (không sửa code) · 2 = implement
owner: <agent>         # agent đang giữ quyền ghi
branch: <branch>
contract_refs:         # mục Contract ticket này chạm tới
  - "mục 0.2 — ID hiển thị"
depends_on: []         # D-item hoặc ticket đang chặn
---

## Bối cảnh

Vì sao ticket này tồn tại. Ba đến năm câu. Nêu rõ consumer nào đang chờ (WP4 / WP5 / WP6).

## Phải đọc trước

- `docs/api-contract-v1.1.md` mục <…>
- `CLAUDE.md` mục <…>
- <file code liên quan>

## Yêu cầu

1. …
2. …

Tiêu chí xong — mỗi dòng phải kiểm chứng được bằng một lệnh:

- [ ] `dotnet test` xanh, có test mới tên `<…>` bao trường hợp `<…>`
- [ ] …

## KHÔNG ĐƯỢC làm

- Không tạo migration khi chưa hỏi.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json`.
- Không chạm `<bảng/file ngoài phạm vi>`.
- Không tự chọn phương án kiến trúc — ghi thành D-item.
- Không viết định danh cột/constraint từ trí nhớ; đọc schema thật rồi dán output.
- <ràng buộc riêng của ticket này>

## Phase 1 dừng ở đâu

Khảo sát xong thì **dừng hẳn**, ghi `results/BE-xx-p1.md`, chờ Mỹ chốt D-item. Không viết code ở
phase 1, kể cả khi đã rõ phải sửa gì.
