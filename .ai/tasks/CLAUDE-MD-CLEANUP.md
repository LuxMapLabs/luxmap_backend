---
ticket: CLAUDE-MD-CLEANUP
title: Tách CLAUDE.md thành lõi + tra cứu theo vùng, sửa nội dung lỗi thời
status: review
phase: 2
owner: claude
branch: docs/CLAUDE-MD-CLEANUP
---

Mỹ chốt 20/10/2026: tách lõi + tra cứu, không xoá luật nào; review ngay, sửa sau khi PR #161 và `feat/PHOTO-REVIEW` merge.
Phase 1: `.ai/results/CLAUDE-MD-CLEANUP-p1.md` (cấu trúc, Claude) + `CLAUDE-MD-CLEANUP-p1-by-codex.md` (kiểm chứng, Codex). D-1…D-6 chờ Mỹ.

KHÔNG: đổi luật kỹ thuật (chỉ sửa chỗ sai so với code/drift có bằng chứng), sửa Contract, sửa `.ai/tasks/*` cũ / `docs/archive` / `docs/review`.

## Mỹ chốt D-item Phase 1 (20/10/2026) — "ok" cho toàn bộ khuyến nghị

- **D-1** Nhận cấu trúc lõi (600–800 dòng) + 11 file `docs/agents/traps/*.md`.
- **D-2** Áp mọi mục S/C/D/W có bằng chứng lúc chuyển; mỗi sửa một dòng trong `CLAUDE-MD-CLEANUP-p2.md`.
- **D-3** Bỏ số đo / nhật ký lịch sử, giữ kết luận + tên test canh; không tạo file lịch sử.
- **D-4** **Ký:** quyết định đã chốt trong `contract-drift.md` nhưng chưa hợp nhất vào Contract thắng chữ Contract.
- **D-5** Contract tự mâu thuẫn nguồn `lamp_out` (`api-contract-v1.1.md:285` ↔ `:703`) → một mục drift, không sửa Contract.
- **D-6** Tách hai câu: lux thủ công so giữa cột/đêm; phân loại `dim` chỉ theo baseline của chính cột.

Phase 2 bắt đầu khi PR #161 và `feat/PHOTO-REVIEW` đã merge vào `dev`.
