---
ticket: WO-BATCH
title: Tạo hàng loạt phiếu công việc — POST /work-orders/batch, một transaction, lỗi thì rollback cả lô
status: ready
phase: 2
owner: codex
branch: feat/WO-batch
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: WO-BATCH-1 (kiểm docs/contract-drift.md; dùng số kế tiếp nếu đã bị lấy)"
depends_on: []
---

## Bối cảnh

FE (WP5) có popup "Phân lịch kiểm tra tự động": Quản lý xem bản đề xuất (ticket sau: `WO-AUTODISPATCH`) rồi bấm "Duyệt tạo tất cả N ca". Thay vì N request lẻ
(mạng chập chờn → tạo dở dang), FE gửi **một** request. Mỹ chốt (09/11/2026), **không phải D-item**:

- `POST /api/v1/work-orders/batch`, capability **`ManageWorkOrders`** có sẵn (KHÔNG thêm capability).
- Body `{ "items": [ <đúng hình dạng body của POST /work-orders>, … ] }`. Mỗi phần tử đi qua **đúng logic của `Create`** (kiểm tra, ID, audit, thông báo) — không viết lại luật.
- **Một transaction cho cả lô.** Bất kỳ phần tử nào lỗi ⇒ **rollback toàn bộ**, không phiếu nào được tạo; trả đúng mã lỗi mà `POST /work-orders` sẽ trả cho phần tử đó, thêm `details.index` (vị trí 0-based của phần tử trong `items`).
- Tranh chấp fault (phần tử khác trong cùng lô, hoặc người khác vừa lấy fault) ⇒ **409** như hiện nay (`FAULT_ALREADY_IN_WORK_ORDER` / mã `LinkConflict` đang dùng) + `details.index`, rollback cả lô.
- Thành công → **201** `{ "count": N, "created_items": [ WorkOrderDetail, … ] }`, theo đúng thứ tự `items`. Không có header `Location`.
- Giới hạn: `items` bắt buộc, **1 ≤ độ dài ≤ 50**, ngoài khoảng → 400 `VALIDATION_FAILED` (`details.field = "items"`). Phần tử `null` → 400 cùng mã (`details.index`).
- **Không có idempotency** (`client_op_id`) ở bản này — thao tác của Quản lý trên web, không phải hàng đợi offline. Ghi vào drift: FE khoá nút và tải lại danh sách khi lỗi mạng, đừng gửi lại mù.
- **Quota / cân bằng tải KHÔNG kiểm ở đây** — Quản lý được đổi người, ngày trong popup. Chỉ có các luật của `Create` hiện tại.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/faults-workorders.md` + `docs/agents/traps/testing.md`
- `src/LuxMap.Modules.WorkOrders/{WorkOrderService,WorkOrdersController,WorkOrderRequests,WorkOrderResponses}.cs` — chú ý `Create`, `CreateSurvey`, `Insert`, `Save`, `Record`, `Notify`, `Detail`
- `src/LuxMap.Modules.Faults/FaultLocks.cs` (thứ tự khoá), `src/LuxMap.Shared/Http/LuxMapException.cs` (`Details` là read-only; bạn phải dựng ngoại lệ mới mang `index`)
- `tests/LuxMap.Api.Tests/WorkOrderTests.cs` (cách trồng sự cố, tài khoản, kiểm `GET /work-orders/{id}`)

## Yêu cầu

1. **Hiện thực trong `WorkOrderService`** (không tạo service song song): thêm `CreateBatch(BatchCreateWorkOrdersRequest, ct)`.
   - Hiện `Insert` và `CreateSurvey` **tự mở** `BeginTransactionAsync` — EF không cho lồng. Tách thân của chúng thành phần ghi **không** mở/commit transaction (`InsertCore` / `CreateSurveyCore`…), còn `Create` thường vẫn mở transaction và commit như cũ (hành vi và test cũ không đổi). Batch mở **một** transaction, gọi phần ghi cho từng phần tử, commit một lần ở cuối.
   - **Khoá trước toàn lô:** gom mọi `fault_ids` của mọi phần tử (khử trùng), `FaultLocks.LockAsync` **một lần**, đã sắp xếp, ngay sau khi mở transaction và **trước** phần tử đầu tiên — hai lô chồng chéo fault theo thứ tự khác nhau sẽ không tự deadlock (40P01 ⇒ 500). Khoá ở từng phần tử vẫn giữ trong thân dùng chung nếu nó vốn gọi `LockAsync` (gọi lại trên hàng đã khoá là vô hại).
   - **Mỗi phần tử một `SaveChanges`** (đúng một `AuditEvent` mỗi lần save, luật D-R13) và đọc `Detail` sau khi save; hai phần tử cùng lô chứa **cùng một fault** phải thấy nhau (phần tử sau thấy liên kết của phần tử trước trong cùng transaction) ⇒ 409.
   - Lỗi: bắt `LuxMapException` từ phần tử `i`, **rollback transaction trước** mọi truy vấn khác (CLAUDE.md: "bắt lỗi xong rollback trước"; `Save` đã tự rollback khi 409 / concurrency — đừng rollback hai lần gây lỗi), rồi ném `LuxMapException` **cùng `Code`, `StatusCode`, `Message`, details cũ cộng `index`**. Lỗi không phải `LuxMapException` thì để nguyên (không bọc).
   - Thông báo (`Notify`) vẫn stage theo từng phiếu trong từng `SaveChanges`; rollback thì không có thông báo nào.
2. **Hợp đồng:** `BatchCreateWorkOrdersRequest` là `sealed class` + `init` (KHÔNG positional record có attribute — CLAUDE.md), `Items` kiểu `CreateWorkOrderRequest[]?`. `BatchCreateWorkOrdersResponse` (`Count`, `CreatedItems`) snake_case tự qua `LuxMapJsonOptions`. Controller: `[HttpPost("batch")]` **đặt trước** mọi route có `{id}` nếu cần để không bị nuốt, `[Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]`, `ProducesResponseType` 201/400/401/403/404/409.
3. **Validation lô** ở service (không chỉ DataAnnotations): số phần tử, phần tử null. Mọi validation của từng phần tử là của `Create` (kể cả `SERVER_OWNED_FIELD` và `Schedule`).
4. **Test** (`tests/LuxMap.Api.Tests/WorkOrderBatchTests.cs`, collection và tiền tố tài khoản/xã riêng theo `testing.md`; assert giá trị cụ thể):
   - lô 3 phần tử hợp lệ (2 inspection khác sự cố + 1 survey nếu dựng được tiện, nếu không thì 3 inspection) ⇒ 201, `count = 3`, `created_items` đúng thứ tự, mỗi phiếu `GET /work-orders/{id}` thấy được, `assigned_to` / `wo_status` (`assigned` khi có người, `open` khi không) / `scheduled_date` / `due_date` / `fault_ids` đúng, **mỗi phiếu đúng một `AuditEvent` `created`**, thông báo `assigned` tới đúng kỹ sư;
   - **rollback toàn lô:** phần tử 3 sai (ví dụ fault không tồn tại ⇒ 404 `FAULT_NOT_FOUND`, `details.index = 2`) ⇒ **không** có phiếu nào của phần tử 1–2 trong DB, không audit, không thông báo, fault vẫn chưa gắn phiếu;
   - phần tử sai ở **đầu** lô và ở **giữa** lô (index đúng, hai ca);
   - hai phần tử dùng chung một fault ⇒ 409 mã `LinkConflict`, `details.index = 1`, rollback;
   - fault đã gắn phiếu mở từ trước ⇒ 409 + `index`; fault `status` không đủ điều kiện ⇒ 409 `FAULT_STATUS_NOT_ELIGIBLE` + `index`;
   - `assigned_to` không đủ điều kiện (kỹ sư xã khác) ⇒ mã lỗi hiện có của `RequireAssignee` + `index`;
   - `items` vắng / rỗng / 51 phần tử / phần tử `null` ⇒ 400 `VALIDATION_FAILED`; trường server-owned (`work_order_id`) trong phần tử ⇒ 400 `SERVER_OWNED_FIELD` + `index`;
   - `due_date < scheduled_date` ⇒ lỗi hiện có của `Schedule` + `index`;
   - 401 không token; 403 `ROLE_FORBIDDEN` với `field_engineer` và `superior`; `manager` được (ma trận `ManageWorkOrders` chỉ có manager; `system_admin` ⇒ 403);
   - **đua:** hai lô chứa cùng tập fault theo **thứ tự đảo ngược** chạy đồng thời ⇒ đúng một lô 201, lô kia 409, **không có 500**; lặp lại ít nhất 20 lần hoặc chứng minh được thứ tự khoá;
   - `POST /work-orders` đơn lẻ vẫn trả y như cũ (test hiện có xanh, thêm ít nhất một test khẳng định không đổi nếu thiếu).
5. **Phá thử** (ghi vào results "chưa chạy, Claude chạy", kèm đúng dòng): (a) bỏ `index` trong lỗi ⇒ test index đỏ; (b) commit sau mỗi phần tử (hoặc không bọc transaction chung) ⇒ test rollback toàn lô đỏ; (c) bỏ khoá toàn lô trước vòng lặp ⇒ test đua đảo thứ tự đỏ (nếu không tái hiện ổn định, ghi thẳng); (d) policy `ManageWorkOrders` → `ReadWorkOrders` ⇒ test 403 đỏ; (e) bỏ giới hạn 50 ⇒ test 51 đỏ.
6. **Tài liệu:** `docs/contract-drift.md` dòng WO-BATCH-1 (SELF-SIGNED 09/11/2026): endpoint, hình dạng, rollback toàn lô, `details.index`, giới hạn 1–50, không idempotency + khuyến nghị FE, không kiểm quota. `docs/agents/traps/faults-workorders.md`: bẫy ngắn (khoá toàn lô trước, phần ghi dùng chung không tự mở transaction, rollback trước khi ném lại). `tracking.html` thêm thẻ. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*` (Claude sinh lại).
7. Ghi `.ai/results/WO-BATCH.md` ngắn: đã làm gì, lệnh Claude cần chạy, điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới
- [ ] Test ở mục 4 có mặt (Codex không chạy được test; Claude chạy `dotnet test` + phá thử)
- [ ] `WorkOrderTests` hiện có không đổi hành vi; endpoint mới nằm trong ma trận `ManageWorkOrders` (`CapabilityPolicyCoverageTests` xanh, không sửa literal ma trận vì không đổi quyền)

## KHÔNG ĐƯỢC làm

- Không migration, không đổi schema, không thêm capability, không thêm giá trị enum, không đổi field đã publish.
- Không viết lại luật kiểm tra của `Create` / `CreateSurvey` (tách ra dùng chung, không chép).
- Không kiểm / áp quota hay cân bằng tải; không ngầm sắp xếp lại `items`.
- Không thêm `client_op_id`; không thêm đường dẫn khác ngoài `/work-orders/batch`.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*`; không chạm project khác ngoài WorkOrders + test + tài liệu (nếu cần sửa `LuxMap.Shared` / `Faults` để có hàm dùng chung, ghi vào results và dừng ở mức nhỏ nhất).
- Không chạy git, `dotnet test`, swagger, `dotnet ef` (sandbox không có quyền).
- Không tự chọn phương án khác với các mục "Mỹ chốt"; điểm mơ hồ → ghi vào results thành câu hỏi, đừng đoán.
