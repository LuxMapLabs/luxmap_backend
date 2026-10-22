---
ticket: LAMP-COUNT
title: Cột cần đôi — số bóng giống nhau trên một bộ đèn (lamp_count)
status: done
phase: 2
owner: claude
branch: feat/LAMP-COUNT
---

Mỹ hỏi 21/10/2026: "một cột đèn lắp 2 cái bóng đã làm chưa?" — chưa: D-11 chỉ cho một bóng đang dùng mỗi cột. Hai hướng đưa ra:
A (một bản ghi mang `lamp_count`) và B (nhiều bóng đang dùng). **Mỹ chọn A.** Chi tiết LMC-1…LMC-4 ở `docs/contract-drift.md`.

KHÔNG: bỏ `ux_fixture_pole_id_active`, thêm trạng thái theo bóng, thêm PUT cho fixture.
