---
ticket: BE-23 (work order — Phase 2)
reviewer: claude
reviewed: feat/BE-23-work-orders — working tree trên cec66ff (Codex chưa commit được)
date: 2026-09-28
verdict: approved            # vòng 2, 28/09/2026
---

## Phạm vi đã review

Toàn bộ thay đổi Codex để trong working tree (24 file sửa, 21 file mới). Đối chiếu với:
`.ai/tasks/BE-23.md`; CHỐT BỔ SUNG của `BE-23-p1.md`; `BE-23-claude-decisions.md` §3.2–§13;
`.ai/results/BE-23-p2-work-orders.md`; Contract §2, §3.1, §3.2, §5.4, §5.5; `CLAUDE.md` các mục BE-23a,
BE-12a quy tắc 4, partial index, ĐỌC migration, BE-14 bẫy 1–4, REG-v1.2.

**Đã chạy:** đọc migration, lớp filter, controller, service, rules, cấu hình, seed, drift; build;
toàn bộ `dotnet test` trên `luxmap_test`; 4 sabotage riêng trên `WorkOrderService.cs`. Cả 4 đã khôi
phục, `cmp` với bản sao lưu cho kết quả giống hệt. Không sửa `src/`.

## Phát hiện

| # | Mức | File:dòng | Vấn đề | Đề xuất |
|---|---|---|---|---|
| 1 | major | `tests/LuxMap.Api.Tests/WorkOrderTests.cs:236-268`, `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:158` | Điều kiện người được giao (D7) **chỉ được test trên `PUT /{id}/assignee`**. `POST /work-orders` có `assigned_to` cũng giao việc, thẳng vào `assigned`, nhưng không test nào canh đường đó. Sabotage C xoá dòng 158 (`RequireAssignee` trong `Create`), `WorkOrderTests` vẫn **14/14 xanh**. Khi đó Quản lý tạo được việc giao cho Superior, cho tài khoản bị khoá, hoặc cho Kỹ sư không có quyền trên xã. CHECK và FK không chặn được, vì FK chỉ chứng minh user **tồn tại**. Neo: `BE-23.md` D7 ("`role = field_engineer`, `is_locked = false`, và `commune_id` của WO ∈ `app_user_commune`"); `BE-23-claude-decisions.md` §13 `WorkOrderAssigneeTests` ("Không phải FE, bị khoá, khác xã, không tồn tại: cùng 409"). Code hôm nay **đúng**, lỗ nằm ở test, đúng loại "canh một nửa" mà `CLAUDE.md` mục BE-08 ghi: kiểm một đường, đường kia lọt. | Chạy cùng vòng 4 lý do (superior / khoá / khác xã / không tồn tại) qua `POST` có `assigned_to`, assert 409 `ASSIGNEE_NOT_ELIGIBLE` với `details` giống hệt đường `PUT`, **và** assert không có WO lẫn dòng audit nào được tạo. Chứng minh bằng sabotage C: phải đỏ. |

## Đã kiểm chứng

**Migration `20260928025712_AddWorkOrders`** (đọc `Up()`/`Down()`):
- Không có `DropIndex`, không có `AddColumn xmin`.
- `ix_work_order_commune_id` và `ix_work_order_fault_commune_id` được khai tường minh. Cặp partial `ux_work_order_fault_fault_id_active` và `ix_work_order_fault_fault_id` đầy đủ đúng quy ước partial index.
- Mọi FK là `Restrict`; hai FK ghép trỏ về `ak_work_order_…` và `ak_fault_fault_id_commune_id`.
- CHECK có cho `task_kind`, `wo_status`, `inspection_outcome` và các luật trạng thái.
- `Down()` drop 2 bảng, rồi AK trên `fault`, rồi sequence: đối xứng với `Up()`.

**Build + toàn bộ test (`luxmap_test`):**

```
0 Warning(s) · 0 Error(s)
Passed!  - Failed: 0, Passed: 157 - LuxMap.Shared.Tests.dll
Passed!  - Failed: 0, Passed:  36 - LuxMap.Persistence.Tests.dll
Passed!  - Failed: 0, Passed:  18 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed: 0, Passed: 461 - LuxMap.Api.Tests.dll
$ SELECT … audit_event / work_order / work_order_fault
audit|0     wo|3     link|11      ← 3 WO + 11 liên kết là seed mock; audit không sót
```

**Sabotage riêng** (`dotnet test tests/LuxMap.Api.Tests --filter WorkOrderTests`):

| | Phá gì | Kết quả |
|---|---|---|
| A | Bỏ no-op khi giao lại đúng người (`:216`) | **Đỏ 3.** `Patch_null_noop…`, `Transition_http…`, `Assignee_eligibility…` |
| B | `resolved_by` = Quản lý thay vì người được giao (`:268`) | **Đỏ 1.** `Repair_propagation…` |
| C | Xoá `RequireAssignee` trong `Create` (`:158`) | **Xanh 14/14 → phát hiện 1** |
| D | Không giải phóng liên kết khi verify/cancel (`:283`) | **Đỏ 2.** `Partial_unique_index…`, `Repair_propagation…` |

**Chín điểm của đề review:**

| Điểm | Kết quả |
|---|---|
| 1. Fallback / capability rỗng | Đạt. 11 action đều mang `[Authorize(Policy=…)]`. Ba capability mới có danh sách vai trò khác rỗng. Hai bảng literal (`CapabilityMatrixTests`, `RoleCapabilityMatrixTests`) được thêm cùng diff. Test metadata khẳng định không route `work-orders` nào có `IAllowAnonymous` |
| 2. Ghi né `CommuneWriteGuard` | Không né được. `commune_id` của WO do server suy ra: từ fault (một xã, khác xã thì 409) hoặc từ `road_segment` (đi qua query filter, ngoài xã thì 404). Client gửi `commune_id` thì 400 `SERVER_OWNED_FIELD`. `WorkOrder` và `WorkOrderFault` đều là `ICommuneScoped`, FK ghép chặn liên kết khác xã ở tầng DB. Không có `ExecuteUpdate`/`ExecuteDelete`. SQL thô duy nhất là `SELECT` sinh ID, dựng từ `PrefixedIds` |
| 3. FE-A thấy WO của FE-B | Không thấy. Filter người được giao gộp **một** `HasQueryFilter` với filter xã, tham chiếu qua `context`, đúng bẫy 1 của authorization-guide. `GET /{id}`, `start`, `complete` đều trả 404 `WORK_ORDER_NOT_FOUND`. `?assigned_to=<người khác>` ra `total: 0`. Có test "sau khi admin gọi, filter không kế thừa `*`". `complete` validate body trước khi tra WO, nhưng 400 xảy ra với mọi ID nên không lộ việc WO có tồn tại |
| 4. `task_kind` / D3 | `WorkOrderRules.Eligible` dùng `FaultStatusSets.IsOpen`, không chép tay tập trạng thái. Kiểm ở `Create`, là đường **duy nhất** nối fault vào WO (không endpoint nào thêm fault sau khi tạo). Test phủ 6 trạng thái × 2 loại việc |
| 5. Audit | Mỗi thao tác 2xx ghi 1 dòng, no-op ghi 0 (sabotage A đỏ). Snapshot là record riêng, không serialize entity hay `AppUser`. Lượt thua race rollback cả WO lẫn audit (`Link_race_rolls_back…`) |
| 6. Migration | Đạt (xem trên) |
| 7. Thứ tự ID | `created_at DESC, length DESC, id DESC`, đảo ngược đúng khuôn. `Listing_orders…` ghi `WO-9999`/`WO-10000` trong **cùng một** transaction và assert **có thứ tự**. Assert chỗ còn trống trước khi ghi, không lùi sequence |
| 8. Response / drift | Item giữ đủ 10 trường của `mock-work-orders.json` và thêm `task_kind`, `commune_id`, `scheduled_date`, `updated_at`. WO-1…WO-11 đã đăng ký ở `contract-drift.md`, gồm WO-3 (enum `task_kind`, `inspection_outcome`) và WO-5 (`task_kind` bắt buộc, **BREAKING** so với §5.5). Spec sinh lại bằng tool; `gen_consolidated_spec.py` sửa ở script, không sửa JSON |
| 9. Test literal / sabotage | Bảng 6×9 và eligibility đều literal. Ba trên bốn sabotage đỏ đúng chỗ; lỗ C là phát hiện 1 |

## Ý kiến, không phải phát hiện

- `WorkOrderService.cs:215-216` kiểm `RequireAssignee` **trước** no-op. Giao lại đúng người đang được
  giao, khi người đó vừa bị khoá hoặc bị gỡ xã, sẽ ra 409 thay vì no-op 200 như decisions §5. 409
  có lẽ còn hữu ích hơn (Quản lý biết phải giao lại). Nên quyết một bên rồi ghi vào drift WO-4.
- `Detail` trả `location {0, 0}` khi fault không có `lat`/`lng` và cột không đọc được (`:101-102`).
  CHECK `ck_fault_pole_or_location` làm ca này gần như không xảy ra, nhưng `(0, 0)` là toạ độ hợp lệ
  ngoài biển, và mobile dẫn đường theo nó. `null` sẽ trung thực hơn.
- `Save` (`:330`) dùng `FirstAsync` sau khi thua race. Nếu WO thắng bị huỷ ngay sau đó, câu này ném
  `InvalidOperationException` (500). Hiếm, không đáng giữ bản sửa lại.
- Vai trò trong filter người được giao lấy từ **claim JWT**, còn điều kiện giao việc đọc từ **DB**. Kỹ
  sư bị đổi vai trò vẫn mang filter cũ tới 60 phút. Cùng họ với ghi chú token 60 phút ở Contract §2,
  chấp nhận được.
- Còn treo từ BE-23a: test quét purge đọc cả `src/LuxMap.Api/logs/` (xem `BE-23-by-claude.md` vòng 2).

## Review lại — vòng 2 (28/09/2026)

| # | Kết quả | Bằng chứng |
|---|---|---|
| 1 | **Đã sửa.** Thêm `Create_rejects_ineligible_assignee_without_order_or_audit` gồm 4 ca: superior / khoá / khác xã / không tồn tại. Test so `details` của `POST` và `PUT` từng byte (cùng correlation id), và khẳng định không sinh WO lẫn audit. `src/` không đổi so với vòng 1 (`cmp` với bản sao lưu) | Sabotage C chạy lại: **đỏ 4/4** (vòng 1 xanh 14/14) |

```
0 Warning(s) · 0 Error(s)
157 + 36 + 18 + 465 = 676 passed, 0 failed (luxmap_test)
audit_event sau suite: 0
```

**Kết luận: approved.** Các mục "Ý kiến" ở vòng 1 giữ nguyên, không chặn merge. Chưa push.

## Review lại — vòng 3, CI PR #54 (28/09/2026)

CI run `36374528444` đỏ ở `Repair_propagation_and_inspection_outcomes_and_release`: `completed_at` của
response là `…46.8506205Z`, còn `resolved_at` đọc từ DB là `…46.8506200Z`. **Lỗi thật, không phải
flaky.** `WorkOrderService` trả detail từ entity đang track với tick 100 ns (Linux), còn `timestamptz`
chỉ lưu µs. Hai vòng review trước không bắt được vì chỉ chạy test trên macOS, nơi đồng hồ vốn ở µs.

**Bản sửa:** `UtcMicrosecondClock.UtcNow()` (Shared) thay cho cả 4 chỗ `DateTime.UtcNow` trong
service, vẫn một `now` chung cho WO, fault và audit. Test dùng `TimeProvider` cố định với tick lẻ
1/5/9. Bẫy đã ghi vào `CLAUDE.md`.

```
build 0 warning · 160 + 36 + 18 + 465 = 679 passed (luxmap_test)
sabotage: helper trả DateTime.UtcNow nguyên → UtcMicrosecondClockTests đỏ 3/3 trên macOS; đã khôi phục
```

**Nợ, ngoài phạm vi BE-23:** `AssetCrudService` / `AssetImportService` gán `UpdatedAt =
DateTime.UtcNow` theo cùng khuôn. Chúng có trả thẳng giá trị đó ra response hay không thì chưa kiểm.

**Kết luận vòng 3: approved.** Chờ CI chạy lại sau push.
