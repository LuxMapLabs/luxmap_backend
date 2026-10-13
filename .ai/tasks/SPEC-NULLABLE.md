---
ticket: SPEC-NULLABLE
title: OpenAPI khai đúng object / enum có thể null — `$ref` trần đang nói "không bao giờ null"
status: in_progress
phase: 1
owner: claude (P1b) · codex (P1a, review)
branch: docs/SPEC-NULLABLE-p1
contract_refs:
  - "docs/openapi/luxmap-v1.json (xuất từ code) + luxmap-v1.5.json (sinh) — OpenAPI 3.0.4"
  - "tracking.html 'Vấn đề đang mở' — phát hiện ở review BE-17 (13/10/2026)"
depends_on: []
---

## Bối cảnh

Swashbuckle 10.2.3 + `SupportNonNullableReferenceTypes()` xuất thuộc tính kiểu object / enum **có thể null** thành `$ref` trần. Trong
OpenAPI 3.0 `$ref` không mang được `nullable` bên cạnh, nên spec nói các trường đó **không bao giờ null** — sai với response thật
(`PoleMapDetail.fixture` khi cột không có bóng, `SweepDetailResponse.latest_run` trước lần xử lý đầu…). Client sinh từ spec (WP5 types,
WP6 DTO / FM-04) có thể từ chối response hợp lệ, hoặc khai kiểu không-null rồi vỡ lúc chạy.

## Đo được (Claude, worktree tạm, không commit)

- Tuỳ chọn toàn cục `UseAllOfToExtendReferenceSchemas()`: **88 schema + 121 operation** đổi — bọc MỌI `$ref` vào `allOf`, kể cả ref
  không-null. Quá rộng.
- Thuộc tính `$ref` thật sự nullable trong C#: **40** (18 object, 22 enum). Trong đó:
  - **5** là `*Feature.properties` (generic `Feature<TProperties>`, NRT coi tham số generic không ràng buộc là nullable) — trên dây **không
    bao giờ null**.
  - **Request**: `AcceptCitizenReportRequest`, `ReportFaultRequest`, `CreateSweepRequest`, `DeviceAckRequest`, `FaultOutcomeRequest`,
    `UpdateUserRequest` — nullable trong C# để server tự kiểm "thiếu" và trả 400 có thông điệp, không phải để client gửi `null`.

## Câu hỏi Phase 1

- Q-1. Cơ chế: schema filter **hẹp** chỉ đổi đúng các thuộc tính nullable thành `allOf: [$ref] + nullable: true` (cách 3.0), hay lên
  OpenAPI 3.1 (`oneOf` với `null`) — bộ sinh của WP5 / WP6 đọc được cách nào?
- Q-2. Request có đánh dấu không (khuyến nghị: chỉ response + những request mà `null` có nghĩa riêng, vd. `UpdateUserRequest`?).
- Q-3. Loại `*Feature.properties` khỏi danh sách bằng luật gì (không phải danh sách chép tay).
- Q-4. Test canh: spec xuất ra không còn `$ref` trần ở thuộc tính nullable của response — test đọc NRT bằng reflection, so với spec.
- Q-5. Báo WP5 / WP6 thế nào, và họ phải sinh lại gì.

## Chia việc

| Bước | Ai | Đầu ra |
|---|---|---|
| P1a — WP5/WP6 sinh code từ spec thế nào, `allOf` + `nullable` hay `oneOf` ra kiểu gì, chỗ nào đang giả định không-null | **Codex** (chỉ đọc ref `origin/dev`) | `.ai/results/SPEC-NULLABLE-p1a-by-codex.md` |
| P1b — danh sách, cơ chế, D-item | **Claude** | `.ai/results/SPEC-NULLABLE-p1.md` |

## KHÔNG ĐƯỢC làm (Phase 1)

- Không sửa code, không xuất lại spec, không sửa Contract.
- Không checkout / sửa repo WP5 / WP6.
- Phase 1 dừng ở D-item, chờ Mỹ chốt.
