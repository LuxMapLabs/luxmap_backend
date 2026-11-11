---
ticket: MULTI-FIXTURE
title: Một cột mang tối đa hai bóng đang dùng, mỗi bóng một bản ghi fixture
status: done
phase: 2
owner: claude
branch: feat/MULTI-FIXTURE
contract_refs:
  - "mục 5.1 — properties bản đồ (thêm fixture_count)"
  - "docs/contract-drift.md — MULTI-FIXTURE MF-1…MF-8"
depends_on: [LAMP-COUNT]
---

## Bối cảnh

Mỹ hỏi lại 11/11/2026: "1 pole có thể có nhiều fixture, cụ thể là 2" — LAMP-COUNT (21/10) chọn hướng A (một bản ghi mang `lamp_count`), nay đảo sang hướng B: hai bản ghi `fixture` riêng, mỗi bản ghi có ID / công suất / bảo hành riêng. Ba lựa chọn Mỹ chốt: trạng thái **theo cột**, API **thêm** `fixtures[]` giữ field cũ, **giữ** `lamp_count`.

## KHÔNG ĐƯỢC làm

- Trạng thái, baseline theo từng bóng (CV / lux không tách được hai bóng).
- Đổi / bỏ field đã publish (`fixture`, `active_fixture`, `lamp_watt`, `lamp_count`).
- Lộ `slot` ra API; thêm PUT / DELETE cho fixture.
