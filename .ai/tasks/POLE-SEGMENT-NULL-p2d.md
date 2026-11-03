---
ticket: POLE-SEGMENT-NULL
title: PR 2d — sync bundle mang cột chưa tuyến của phiếu mở (D-4)
status: done
phase: 2
owner: claude (viết) · codex `-p river` (review)
branch: feat/POLE-SEGMENT-NULL-2d-sync
contract_refs:
  - "docs/contract-drift.md — mục POLE-SEGMENT-NULL (D-4)"
depends_on:
  - "PR 2a (#198), 2b (#199), 2c (#200) đã merge"
---

## Bối cảnh

Codex (`gpt-6.1-sol`) vẫn báo `You've hit your usage limit … try again at 4:03 PM` dù Mỹ đã đăng nhập lại hai tài khoản, nên PR này do Claude viết và Codex chạy ở chế độ review qua profile `river` (nhà cung cấp riêng, chỉ đọc).
D-4 (Mỹ chốt 02/11/2026): bundle mặc định gồm cột / sự cố / phiếu chưa tuyến của phiếu mở được giao; không kéo mọi cột chưa tuyến của xã; không `null` trong `segment_ids[]`; gói nêu tuyến giữ nguyên.

## Phạm vi đã làm

`SyncBundleService` (`MyUnassignedPolesAsync`), `MapQueryService.PolesOnSegmentsAsync`, `FaultQueryService.OpenOnSegmentsAsync` (thêm tham số `extraPoleIds`), comment `SyncContracts`,
chuỗi tóm tắt `/sync/bundle` trong `gen_consolidated_spec.py`, drift, trap. Không migration; `docs/openapi/luxmap-v1.json` KHÔNG đổi (không chạm CODEOWNERS).

## KHÔNG làm

POST/PUT/import vẫn bắt buộc tuyến (2e); không bộ lọc `unassigned_segment`; không trường metadata mới; không đổi `/sync/push`.
