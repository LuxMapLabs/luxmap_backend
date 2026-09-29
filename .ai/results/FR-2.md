---
ticket: FR-2 + FR-2a (BE-23 bổ sung — đề nghị của WP5)
phase: 2
agent: claude
branch: feat/BE-23-work-order-chain
date: 2026-09-29
status: done
---

## Đã làm gì

- Mỹ chốt FR-2 (mã tiến trình) và FR-2a = C (nghiệm thu kiểm tra xác nhận sự cố `fault_present`), 29/09/2026.
- `work_order.parent_work_order_id`, `work_order.root_work_order_id` (null ở lịch đầu), FK ghép cùng xã, CHECK `ck_work_order_chain_complete`.
- Item + chi tiết phiếu: `parent_work_order_id`, `case_id` (= `root ?? work_order_id`). `GET /work-orders?case_id=`.
- `POST /work-orders/{id}/follow-up` (Quản lý): server mang sự cố `fault_present` của lịch cha sang; `title` mặc định của cha; `fault_ids` tuỳ chọn để tách; cha phải `verified`; cặp hợp lệ hiện chỉ inspection → repair; mã mới `NOTHING_TO_FOLLOW_UP` (409).
- `Create` và `FollowUp` dùng chung một đường ghi (`Insert`): cùng kiểm tra xã, trạng thái sự cố, liên kết đang mở, người được giao, audit.
- C: `verify` inspection chuyển `fault_present` `detected → confirmed` qua `FaultTransitions`, ghi `fault_changes[]`.
- `allowed_actions` của Quản lý có `follow_up` ở phiếu kiểm tra `verified`.

## Bằng chứng

Migration `20260929073216_AddWorkOrderChain` — đọc trước khi apply: 2 `AddColumn`, 2 `CreateIndex`
(`ix_work_order_parent_work_order_id_commune_id`, `ix_work_order_root_work_order_id_commune_id`),
1 `AddCheckConstraint`, 2 `AddForeignKey`; **không** `DropIndex` nào; `Down()` đối xứng.

```
apply:    parent_work_order_id,root_work_order_id  ck_work_order_chain_complete,fk_…parent…,fk_…root…  3 index
rollback: (không cột, không ràng buộc)                                                               1 index (ix_work_order_commune_id còn)
reapply:  giống apply
```

```
$ dotnet build → 0 Warning(s), 0 Error(s)
$ dotnet test --no-build (luxmap_test)
Passed! 159 Shared · 36 Persistence · 18 Storage · 489 Api = 702 (699 + 3 test mới)
```

Test cũ `Repair_propagation_and_inspection_outcomes_and_release` đỏ đúng một assert (`Expected: Detected, Actual: Confirmed`) — đó là hành vi cũ C thay thế; đã sửa kỳ vọng.

Sabotage:

```
== S1: inspection verify leaves faults alone   → 3 test đỏ (2 mới + test cũ đã sửa)
== S2: follow-up carries every linked fault    → 2 test đỏ
== S3: case_id filter misses the children      → 1 test đỏ
== restored                                    → 24/24
```

`python3 scripts/seed_mock_set.py --apply --database luxmap_test` chạy được; `work_order`: 3 hàng, 0 hàng có `root_work_order_id`.

OpenAPI: 48 op implemented (thêm `follow-up`), lint 3 warning nền, `OpenApi*` 21/21.

## D-item — cần Mỹ chốt

| # | Câu hỏi | Ghi chú |
|---|---|---|
| D-1 | Migrate `luxmap_dev` (`AddWorkOrderMaterials` + `AddWorkOrderChain`)? | Chỉ thêm cột nullable, index, CHECK, FK; 3 phiếu seed đều là lịch đầu nên không vi phạm |

## Chưa làm / cố ý bỏ

- Khảo sát trong chuỗi — chờ FR-1/BE-15 (`FollowUpKinds` là chỗ thêm).
- `root = cha.root ?? cha.id` chỉ kiểm được qua API ở độ sâu 2 (inspection → repair); độ sâu 3 cần khảo sát.
- Chưa báo WP5/WP6.
