# WO-BATCH — phase 2 (09/11/2026)

Đã implement trong worktree này, chưa xác minh runtime; không chạy git/test/swagger/ef, không migration, không sửa Contract/OpenAPI hoặc ma trận quyền.

- WorkOrderService: CreateCore / InsertCore / CreateSurveyCore dùng chung luật tạo đơn; batch 1–50 phần tử, khóa hợp fault ID trước vòng lặp, từng phiếu một save + Detail, commit một lần. Lỗi LuxMapException rollback nếu transaction còn hiện hành rồi giữ code/status/message/details và thêm index. Save vẫn tự rollback trước lookup conflict như cũ.
- DTO sealed/init (Items đúng CreateWorkOrderRequest[]?), response count/created_items; POST batch dùng ManageWorkOrders, 201 không Location, metadata 201/400/401/403/404/409. ValidateNever trên Items chuyển validation nghiệp vụ phần tử vào service để có index.
- WorkOrderBatchTests.cs: collection AssetDatabaseCollection, tài khoản/xã riêng; 2 inspection + 1 survey, GET/detail, audit/notice; rollback ở index 0/1/2 và cả survey; trùng fault, fault đã liên kết/status không hợp lệ, assignee khác xã, thiếu/null/rỗng/51/50 phần tử, null/server-owned/title/kind/schedule, 401/403, tạo đơn + Location, đua đảo thứ tự 20 vòng.
- Đã ghi WO-BATCH-1 SELF-SIGNED trong contract-drift, bẫy trong traps/faults-workorders, thẻ tracking. Không đổi project Shared/Faults.

## Kiểm chứng

✅ Đọc/rà soát source: batch khóa trước vòng lặp, Core không mở/commit transaction, vẫn một Record/Notify/save mỗi phiếu; policy giữ nguyên.

⚠️ Chưa build thành công; chưa chạy test, phá thử hoặc kiểm HTTP/DB thực tế. Không có số test pass hay khẳng định không có cảnh báo mới.

Lệnh thử `dotnet build LuxMap.slnx --disable-build-servers` không xuất output trong thời gian chờ; đã gửi Ctrl-C, tool trả:

```text
Attempting to cancel the build...
```

Lượt có timeout 30 giây chạy `dotnet build LuxMap.slnx --disable-build-servers --no-restore` cho output thật:

```text
Build timed out after 30 seconds; no completed build.
/usr/local/share/dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk/targets/Microsoft.PackageDependencyResolution.targets(266,5): error NETSDK1004: Assets file '/Users/nhm809/Documents/LuxMap/wt-wo-batch/src/LuxMap.Api/obj/project.assets.json' not found. Run a NuGet package restore to generate this file. [/Users/nhm809/Documents/LuxMap/wt-wo-batch/src/LuxMap.Api/LuxMap.Api.csproj]
```

Không nhận được exit code hoàn tất của build đầu; sandbox không cho kiểm/dừng theo process list (`pkill: Cannot get process list`). Không coi build là đã thành công.

## Claude cần chạy

Sau khi chốt D-1 và có restore/package assets:

```bash
dotnet build LuxMap.slnx --disable-build-servers
dotnet test --disable-build-servers --filter 'FullyQualifiedName~WorkOrderBatchTests|FullyQualifiedName~WorkOrderTests|FullyQualifiedName~CapabilityPolicyCoverageTests'
dotnet test --disable-build-servers
```

Sau đó sinh lại Swagger/OpenAPI + lint theo `.ai/context/commands.md`; Codex không chạy các lệnh này. Review diff và commit do Claude thực hiện theo yêu cầu phiên này.

**Phá thử — chưa chạy, Claude chạy**, mỗi lần khôi phục bằng cp/touch và build lại:

1. WorkOrderService.cs:159: bỏ `details["index"] = index;` → `Missing_fault_at_any_index_rolls_back_orders_links_audits_and_notices` / test validation index phải đỏ.
2. WorkOrderService.cs:163: chuyển `await transaction.CommitAsync(ct);` vào cuối mỗi lượt vòng lặp (hoặc bỏ transaction chung) → test rollback ở index 1/2 phải đỏ (kiểm cả DB/audit/notification).
3. WorkOrderService.cs:145: bỏ `await FaultLocks.LockAsync(db, faultIds, ct);` trước vòng lặp → `Reversed_fault_batches_have_one_winner_and_one_conflict_for_twenty_rounds` phải phát hiện 500/deadlock nếu tái hiện được. Chưa chạy nên chưa chứng minh test đua tái hiện ổn định khi phá; báo thẳng nếu 20 vòng vẫn xanh. SQL FaultLocks hiện có ORDER BY fault_id giữ thứ tự khóa toàn tập khi không phá.
4. WorkOrdersController.cs:52: đổi policy `ManageWorkOrders` thành `ReadWorkOrders` → `Read_only_roles_cannot_create_batches` phải đỏ.
5. WorkOrderService.cs:140: bỏ `or > 50` → ca 51 trong `Items_must_be_present_and_contain_one_to_fifty_elements` phải đỏ.

## Cần chốt / giới hạn

**D-1 — quyền system_admin:** `src/LuxMap.Shared/Authorization/LuxMapPolicies.cs:155` hiện `[ManageWorkOrders] = [UserRole.Manager]`. Ticket yêu cầu admin 201 nhưng đồng thời giữ capability/không đổi literal ma trận. Đã giữ policy và ma trận hiện hành. Test `No_token_is_unauthorized_and_system_admin_can_create` giữ kỳ vọng 201 theo ticket, **dự kiến đỏ ở bước admin (thực tế policy trả 403)**. Mỹ chọn: sửa tiêu chí ticket theo quyền hiện hành, hay duyệt mở ManageWorkOrders cho admin (tác động cả endpoint cũ và cần sửa hai ma trận literal)? Không tự mở quyền trong ticket này.

JSON binding lỗi kiểu/enum/date diễn ra trước service nên vẫn là lỗi MVC có JSON path, chưa có index nghiệp vụ; không thay pipeline chung ngoài phạm vi ticket. Các lỗi nghiệp vụ và DataAnnotations tương đương của Create được service kiểm, có index.
