---
ticket: FR-3 (BE-23 bổ sung — đề nghị của WP5)
phase: 2
agent: claude
branch: feat/BE-23-materials-notes
date: 2026-09-29
status: done
---

## Đã làm gì

- Mỹ chốt FR-3 ngày 29/09/2026: hai trường văn bản tự do thay vì một (drift FR-3).
- `work_order.materials_note` (Quản lý: `POST`, `PATCH`) và `work_order.materials_used` (Kỹ sư được giao: `complete`).
- Cắt khoảng trắng; chuỗi rỗng lưu `null`; CHECK `ck_work_order_materials_note_not_blank`, `ck_work_order_materials_used_not_blank`.
- `PATCH`: thiếu khoá giữ nguyên, `null` xoá, cùng giá trị là no-op (không audit, không đổi `updated_at`).
- `complete` ghi lại `materials_used` mỗi lần (như `report_note`); `return` không xoá nó.
- Hai trường có ở `WorkOrderDetail` và audit snapshot; **không** ở item danh sách.

## Bằng chứng

Migration `20260929062843_AddWorkOrderMaterials` — đọc trước khi apply: `Up()` chỉ 2 `AddColumn` (text, nullable) + 2 `AddCheckConstraint`; không `DropIndex`, không cột `xmin`; `Down()` đối xứng.

```
apply:    materials_note materials_used ck_work_order_materials_note_not_blank ck_work_order_materials_used_not_blank
rollback: (không còn gì)
reapply:  materials_note materials_used ck_work_order_materials_note_not_blank ck_work_order_materials_used_not_blank
max(migration_id) = 20260929062843_AddWorkOrderMaterials        # luxmap_test
```

```
$ dotnet build            → 0 Warning(s), 0 Error(s)
$ dotnet test --no-build  (luxmap_test)
Passed! 159 Shared · 36 Persistence · 18 Storage · 486 Api   = 699 (696 + 3 mới)
```

Sabotage:

```
== S1: complete writes the report into the plan
  Failed WorkOrderTests.Materials_plan_and_materials_used_are_kept_apart
== S2: PATCH ignores a materials-only change
  Failed WorkOrderTests.Materials_plan_and_materials_used_are_kept_apart
== restored → Passed 21/21
```

OpenAPI sinh lại bằng tool; lint 3 warning nền; `OpenApi*` 21/21 xanh. Schema có `materials_note` ở `CreateWorkOrderRequest`, `PatchWorkOrderRequest`, `WorkOrderDetail`; `materials_used` ở `CompleteWorkOrderRequest`, `WorkOrderDetail`.

## File đã đổi

| File | Đổi gì |
|---|---|
| `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs` | 2 thuộc tính |
| `…/Configurations/WorkOrderConfigurations.cs` | 2 CHECK |
| `…/WorkOrderRequests.cs`, `WorkOrderResponses.cs`, `WorkOrdersController.cs`, `WorkOrderService.cs` | Nhận, lưu, trả, audit |
| `src/LuxMap.Persistence/Migrations/*AddWorkOrderMaterials*`, snapshot | Migration |
| `tests/LuxMap.Api.Tests/WorkOrderTests.cs` | 1 Fact + 1 Theory (2 ca) |
| `docs/openapi/*`, `docs/contract-drift.md`, `tracking.html` | Spec, FR-3 đã hiện thực, tiến độ |

## D-item — cần Mỹ chốt

| # | Câu hỏi | Ghi chú |
|---|---|---|
| D-1 | Migrate `luxmap_dev`? | Chỉ thêm 2 cột nullable + 2 CHECK; dữ liệu hiện có không vi phạm (cột mới toàn `null`) |

## Chưa làm / cố ý bỏ

- Chưa migrate `luxmap_dev` (chờ D-1).
- Chưa báo WP5/WP6.
