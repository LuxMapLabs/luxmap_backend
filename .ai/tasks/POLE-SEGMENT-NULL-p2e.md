---
ticket: POLE-SEGMENT-NULL
title: PR 2e — nút bật: POST / PUT / import cho phép cột chưa tuyến + bộ lọc `unassigned_segment` (D-2, D-11)
status: done
phase: 2
owner: claude (viết) · codex `-p river` (review)
branch: feat/POLE-SEGMENT-NULL-2e-writers
contract_refs:
  - "docs/contract-drift.md — mục POLE-SEGMENT-NULL (D-2, D-11)"
depends_on:
  - "PR 2a (#198), 2b (#199), 2c (#200), 2d (#201) đã merge"
---

## Bối cảnh

Codex `gpt-6.1-sol` vẫn báo `You've hit your usage limit … try again at 4:03 PM` dù Mỹ đã đăng nhập lại, nên PR này do Claude viết, Codex `-p river` (chỉ đọc) review.
D-2 (Mỹ chốt 02/11/2026): POST vắng/`null` = chưa tuyến; PUT khoá bắt buộc nhưng nhận `null`; import ô trống = chưa tuyến với cột mới, giữ tuyến với cột đã có; bộ lọc `unassigned_segment=true`.

## KHÔNG làm

Không migration; không `segment_source` (D-13); không sửa `api-contract-v1.1.md` (drift SELF-SIGNED); không đổi mock đã bàn giao (D-11 A).
