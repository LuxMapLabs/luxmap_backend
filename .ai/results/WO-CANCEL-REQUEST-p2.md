# WO-CANCEL-REQUEST — Phase 2 (26/10/2026, Codex)

## Phạm vi đã hiện thực

Theo Phase 2 của ticket và D-1…D-14 đã chốt cuối báo cáo p1. Branch `feat/WO-CANCEL-REQUEST`.

- Năm trường trạng thái phụ trên `work_order`; enum riêng `none | pending | rejected`, default `none`.
  CHECK enum + invariant null/lý do/thời gian/trạng thái assigned. Một migration duy nhất.
- Hai POST xin/bác, capability `ExecuteWorkOrders` / `ManageWorkOrders`, DTO sealed + init;
  trim rồi kiểm 1…500 UTF-16 **trong service**. Dùng lỗi 400/404/409 đã có.
- Chỉ assigned, cả inspection/repair/survey; xin lại sau bác, không cooldown.
  Reset đủ năm trường trong audit của hành động gốc khi giao lại/bỏ giao/cancel/start/complete/verify/ngày thật đổi;
  no-op, đổi title/vật tư giữ nguyên. Tạo/follow-up không sao chép yêu cầu.
- Chiếu đủ năm trường qua ItemsAsync và Detail (agenda kế thừa item, bundle gọi detail), filter một trạng thái,
  hai allowed_actions theo vai trò/người được giao/trạng thái phụ.
- Hai notification type + hai audit action; snapshot đủ năm trường, một event và một SaveChanges.
  Thông báo tiếng Việt qua WorkOrderNotices → Notifier.Stage/Clip. Xin báo manager hoạt động phủ xã neo;
  bác báo assignee. Không manager vẫn lưu, không fallback. Race dùng xmin/Save hiện có.
- Contract v1.19 SELF-SIGNED (§0, §5.5, §5.9, changelog), drift CR-1…CR-6, trap/README, spec sinh từ code.
  CLAUDE.md chỉ cập nhật tham chiếu phiên bản; AGENTS.md vẫn là symlink.
- Không đổi wo_status/capability/sync op, không thêm bảng, không sửa PLAN copy DB (đã có work_order/notification/audit_event).
  Không sửa tracking.html, tasks-backend.csv hoặc current.md theo phạm vi ticket. Không push.

## Migration — đã đọc trước lần apply đầu tiên

Lệnh sinh:

```sh
dotnet ef migrations add AddWorkOrderCancellationRequests -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations --no-build
```

Output thật (exit 0, sau khi tạo .env local theo README):

```text
Done. To undo this action, use 'ef migrations remove'
```

Đã mở toàn bộ Up/Down sinh ra bằng `cat` trước bất kỳ test/apply nào. Không có DropIndex/AlterColumn/xmin.
Up chỉ thêm năm cột, hai CHECK trạng thái phụ và nới ck_notification_type / ck_audit_event_action với đầy đủ tập cũ.
Down chỉ bỏ các cột/CHECK vừa thêm và dựng lại đúng hai tập cũ; bổ sung chốt từ chối mất dữ liệu trước mọi thao tác Down.
Không đổi CHECK entity_type, wo_status hoặc sync_operation. Dưới đây là output `cat` bản đã kiểm, trước khi test apply:

```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderCancellationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reject_note",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancellation_rejected_at",
                table: "work_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_request_note",
                table: "work_order",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_request_status",
                table: "work_order",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<DateTime>(
                name: "cancellation_requested_at",
                table: "work_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_cancellation_request_state",
                table: "work_order",
                sql: "(cancellation_request_status = 'none' AND cancellation_request_note IS NULL AND cancellation_requested_at IS NULL\n    AND cancellation_reject_note IS NULL AND cancellation_rejected_at IS NULL)\nOR (wo_status = 'assigned' AND cancellation_request_note IS NOT NULL\n    AND char_length(btrim(cancellation_request_note)) BETWEEN 1 AND 500 AND cancellation_requested_at IS NOT NULL\n    AND ((cancellation_request_status = 'pending' AND cancellation_reject_note IS NULL AND cancellation_rejected_at IS NULL)\n        OR (cancellation_request_status = 'rejected' AND cancellation_reject_note IS NOT NULL\n            AND char_length(btrim(cancellation_reject_note)) BETWEEN 1 AND 500\n            AND cancellation_rejected_at IS NOT NULL AND cancellation_rejected_at >= cancellation_requested_at)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_order_cancellation_request_status",
                table: "work_order",
                sql: "\"cancellation_request_status\" IN ('none', 'pending', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_cancellation_requested', 'work_order_cancellation_rejected', 'work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received', 'work_order_overdue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('cancellation_requested', 'cancellation_rejected', 'created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM work_order WHERE cancellation_request_status <> 'none'
                        OR cancellation_request_note IS NOT NULL OR cancellation_requested_at IS NOT NULL
                        OR cancellation_reject_note IS NOT NULL OR cancellation_rejected_at IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM notification WHERE type IN ('work_order_cancellation_requested', 'work_order_cancellation_rejected'))
                        OR EXISTS (SELECT 1 FROM audit_event WHERE action IN ('cancellation_requested', 'cancellation_rejected')) THEN
                        RAISE EXCEPTION 'Cannot roll back AddWorkOrderCancellationRequests while cancellation data exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_cancellation_request_state",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_order_cancellation_request_status",
                table: "work_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropColumn(
                name: "cancellation_reject_note",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_rejected_at",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_request_note",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_request_status",
                table: "work_order");

            migrationBuilder.DropColumn(
                name: "cancellation_requested_at",
                table: "work_order");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received', 'work_order_overdue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
        }
    }
}

```

Apply/down/reapply chỉ diễn ra trong container `cancellation_migration_test` có mật khẩu ngẫu nhiên;
HTTP tests dùng container ngẫu nhiên của LuxMapTestHost. Không gọi database update với DB dev.
Rollback ứng dụng có thể giữ schema cộng thêm; nếu đã có dữ liệu mới thì Down từ chối, ưu tiên forward fix,
không sửa/xóa audit để ép thu hẹp enum.

## Build và test — output thực tế

Worktree ban đầu chưa restore và thiếu .env. Docker trong sandbox báo permission denied;
`docker info` ngoài sandbox trả `29.6.1`, nên chạy kiểm chứng ngoài sandbox với quyền đã cấp.
Các lệnh build trong sandbox không hoàn tất thành công; không dùng chúng làm bằng chứng xanh.
Restore dependency theo repo, tạo .env từ .env.example (ignored, không commit), không đổi dependency manifest.
Lần sinh migration đầu thiếu POSTGRES_PASSWORD nên exit 1; sau tạo .env thì sinh thành công, không kết nối DB dev.

Build đã chạy:

```sh
dotnet build --no-restore --disable-build-servers -warnaserror
```

```text

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:17.13
```

Các lượt test đầu (đã sửa hết trước khi kết luận):

- focused: exit 1 do test migration thiếu import UserRole (chưa chạy test).
- focused2: exit 1, 10 pass / 2 fail: MVC đổi query trắng thành null nên bỏ lọc; fixture migration thiếu PasswordSetAt.
- focused3: exit 1, 12 pass / 1 fail: fixture audit thiếu actor_role/snapshot bắt buộc.
- focused4: exit 0, 13 pass; đã sửa query trắng thành 400 và hoàn thiện fixture.

```text

Test Run Successful.
Total tests: 13
     Passed: 13
 Total time: 16,3489 Seconds
```

Regression chọn vùng, không chạy toàn suite; chỉ một dotnet test tại một thời điểm:

```sh
dotnet test tests/LuxMap.Api.Tests --no-restore --disable-build-servers --filter 'FullyQualifiedName~WorkOrderTests|FullyQualifiedName~CancellationMigration|FullyQualifiedName~NotificationTests|FullyQualifiedName~SyncTests|FullyQualifiedName~RoleCapabilityMatrixTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~JsonEnumHandlingTests' --logger 'console;verbosity=normal'
```

Exit 0, output thật:

```text

Test Run Successful.
Total tests: 291
     Passed: 291
 Total time: 1,7155 Minutes
```

Bao gồm HTTP thật trong test host: 401/403/404, các cạnh trạng thái, no-op/reset, pending/rejected,
ngày đặt/xóa null, lý do emoji, list/detail/agenda/bundle, filter total/ngày tương lai, cả ba task kind,
manager bị khóa/chưa đặt mật khẩu/thiếu xã/không manager, audit actor/note/snapshot/microsecond,
hai request/hai reject/request đua cancel hoặc reassign (chỉ bên thắng có audit/notices),
unknown sync op không block start, start reset pending/replay, complete/return/verify và các guard evidence cũ.
RoleCapabilityMatrixTests và CapabilityPolicyCoverageTests xanh; không cần đổi ma trận vì dùng lại hai capability.

## Phá thử — output thực tế từng lượt

Chạy tuần tự, một test process tại một thời điểm; mỗi lượt sửa đúng một chốt, chạy test và đọc exit code,
khôi phục file bằng `cp <backup> <source>` (không mv), rồi build/test lại cùng filter.
Có 20 lượt trên triển khai đầu; sau rà diff, gom điều kiện xin/bác vào `WorkOrderRules.Allows` dùng chung
cho service và allowed_actions, rồi phá lại cả ba điều kiện (tổng 23 lượt).
Mỗi red bên dưới có test `[FAIL]` thực sự; không lấy compile error làm bằng chứng bắt được lỗi.

Lệnh chung (không --no-build ở các lượt phá/khôi phục):

```sh
dotnet test tests/LuxMap.Api.Tests --no-restore --disable-build-servers --filter 'FullyQualifiedName~<filter>' --logger 'console;verbosity=normal'
```

| Lượt | Chốt bị phá | Filter |
|---|---|---|
| reason | Bỏ độ dài bắt buộc 1…500 trong service | Cancellation_validates_trimmed_reason |
| pending_request / shared_pending_request | Cho xin lặp khi pending | Cancellation_substate |
| assigned_only / shared_assigned_only | Cho xin ở in_progress | Cancellation_requires |
| pending_reject / shared_pending_reject | Cho bác khi chưa pending | Cancellation_substate |
| assign_reset | Bỏ reset khi đổi/bỏ người giao | Cancellation_reset |
| date_reset | Bỏ reset khi dời lịch | Cancellation_reset |
| patch_keeps_title | Reset cả PATCH chỉ title/vật tư | Cancellation_reset |
| act_reset | Bỏ reset khi start, gồm sync gọi service | Cancellation_online_only |
| item_projection / detail_projection | Trả none/null thay trạng thái/lý do thật | Cancellation_filter |
| audit_snapshot | Bỏ lý do khỏi snapshot audit | Cancellation_substate |
| request_notice / reject_notice | Bỏ người nhận thông báo mới | Cancellation_applies |
| active_manager | Cho manager bị khóa nhận thông báo | Cancellation_applies |
| allowed_request / allowed_reject | Bỏ action được phê duyệt | Cancellation_substate |
| capability | Gỡ policy ExecuteWorkOrders khỏi endpoint xin | CapabilityPolicyCoverageTests OR Cancellation_requires |
| state_check | Vô hiệu CHECK invariant trạng thái phụ | Cancellation_schema |
| old_audit_enum | Làm mất relocated khỏi tập cũ trong CHECK audit | Cancellation_schema |
| down_guard | Bỏ kiểm dữ liệu trạng thái phụ khi Down | Cancellation_schema |

### reason

Red exit `1`:

```text
[xUnit.net 00:00:07.48]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_validates_trimmed_reason_in_service_before_lookup [FAIL]
[xUnit.net 00:00:07.48]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:07.48]                  ↓ (pos 0)
[xUnit.net 00:00:07.48]       Expected: "VALIDATION_FAILED"
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,7913 Seconds
```

### pending_request

Red exit `1`:

```text
[xUnit.net 00:00:06.49]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:06.49]       a POST /WO-0004/request-cancellation: expected 409, got 200: {"segment_ids":[],"note":null,"review_note":null,"report_note":null,"materials_note":null,"materials_used":null,"created_by":"USR-012","assigned_at":"2026-10-26T10:42:21.941625Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":true,"allowed_actions":["start"],"faults":[],"work_order_id":"WO-0004","title":"Work order test","commune_id":"COM-006","task_kind":"inspection","segment_id":"SEG-006","cluster_id":null,"parent_work_order_id":null,"case_id":"WO-0004","fault_ids":[],"wo_status":"assigned","cancellation_request_status":"pending","cancellation_request_note":"Repeat","cancellation_requested_at":"2026-10-26T10:42:22.108386Z","cancellation_reject_note":null,"cancellation_rejected_at":null,"assigned_to":"USR-008","priority_score":null,"created_at":"2026-10-26T10:42:21.941625Z","updated_at":"2026-10-26T10:42:22.108386Z","due_date":null,"scheduled_date":null}
[xUnit.net 00:00:06.49]       Stack Trace:
[xUnit.net 00:00:06.49]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderTests.cs(115,0): at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation)
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,9568 Seconds
```

### assigned_only

Red exit `1`:

```text
[xUnit.net 00:00:06.49]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_requires_assignee_commune_capability_and_assigned_status [FAIL]
[xUnit.net 00:00:06.49]       a POST /WO-0006/request-cancellation: expected 409, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"466b61e3-5851-4c40-9f12-866bd7d47c73"}}}
[xUnit.net 00:00:06.49]       Stack Trace:
[xUnit.net 00:00:06.49]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderTests.cs(115,0): at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation)
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 8,3911 Seconds
```

### pending_reject

Red exit `1`:

```text
[xUnit.net 00:00:06.43]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:06.43]       manager POST /WO-0004/reject-cancellation: expected 409, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"7bd825fd-76cf-4a1d-a201-db5a2a75144e"}}}
[xUnit.net 00:00:06.43]       Stack Trace:
[xUnit.net 00:00:06.43]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderTests.cs(115,0): at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation)
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 7,9615 Seconds
```

### assign_reset

Red exit `1`:

```text
[xUnit.net 00:00:06.33]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_reset_and_noop_preserve_exactly_the_approved_fields(state: "rejected") [FAIL]
[xUnit.net 00:00:06.33]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:06.33]                  ↓ (pos 0)
[xUnit.net 00:00:06.33]       Expected: "none"
Test Run Failed.
Total tests: 2
     Failed: 2
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 2
     Passed: 2
 Total time: 7,7323 Seconds
```

### date_reset

Red exit `1`:

```text
[xUnit.net 00:00:06.57]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_reset_and_noop_preserve_exactly_the_approved_fields(state: "rejected") [FAIL]
[xUnit.net 00:00:06.57]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:06.57]                  ↓ (pos 0)
[xUnit.net 00:00:06.57]       Expected: "none"
Test Run Failed.
Total tests: 2
     Failed: 2
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 2
     Passed: 2
 Total time: 7,4992 Seconds
```

### patch_keeps_title

Red exit `1`:

```text
[xUnit.net 00:00:06.60]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_reset_and_noop_preserve_exactly_the_approved_fields(state: "rejected") [FAIL]
[xUnit.net 00:00:06.60]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:06.60]                  ↓ (pos 0)
[xUnit.net 00:00:06.60]       Expected: "rejected"
Test Run Failed.
Total tests: 2
     Failed: 2
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 2
     Passed: 2
 Total time: 7,4879 Seconds
```

### act_reset

Red exit `1`:

```text
[xUnit.net 00:00:06.46]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_online_only_unknown_op_does_not_block_offline_start_and_replay [FAIL]
[xUnit.net 00:00:06.46]       System.Collections.Generic.KeyNotFoundException : The given key was not present in the dictionary.
[xUnit.net 00:00:06.47]       Stack Trace:
[xUnit.net 00:00:06.47]            at System.Text.Json.JsonElement.GetProperty(String propertyName)
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,8670 Seconds
```

### item_projection

Red exit `1`:

```text
[xUnit.net 00:00:06.40]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_filter_list_detail_agenda_and_bundle_agree [FAIL]
[xUnit.net 00:00:06.40]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:06.40]                  ↓ (pos 0)
[xUnit.net 00:00:06.40]       Expected: "pending"
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,9775 Seconds
```

### detail_projection

Red exit `1`:

```text
[xUnit.net 00:00:06.42]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_filter_list_detail_agenda_and_bundle_agree [FAIL]
[xUnit.net 00:00:06.42]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:06.42]                  ↓ (pos 0)
[xUnit.net 00:00:06.42]       Expected: "pending"
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 7,0466 Seconds
```

### audit_snapshot

Red exit `1`:

```text
[xUnit.net 00:00:06.75]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:06.75]       Assert.Equal() Failure: Strings differ
[xUnit.net 00:00:06.75]       Expected: "Xe hỏng"
[xUnit.net 00:00:06.75]       Actual:   null
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,8583 Seconds
```

### request_notice

Red exit `1`:

```text
[xUnit.net 00:00:06.47]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_applies_to_repair_and_survey_and_notifies_only_active_anchor_managers [FAIL]
[xUnit.net 00:00:06.47]       Assert.Single() Failure: The collection was empty
[xUnit.net 00:00:06.47]       Stack Trace:
[xUnit.net 00:00:06.47]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderCancellationTests.cs(198,0): at LuxMap.Api.Tests.WorkOrderTests.Cancellation_applies_to_repair_and_survey_and_notifies_only_active_anchor_managers()
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,8751 Seconds
```

### reject_notice

Red exit `1`:

```text
[xUnit.net 00:00:06.57]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_applies_to_repair_and_survey_and_notifies_only_active_anchor_managers [FAIL]
[xUnit.net 00:00:06.57]       Assert.Single() Failure: The collection was empty
[xUnit.net 00:00:06.57]       Stack Trace:
[xUnit.net 00:00:06.57]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderCancellationTests.cs(204,0): at LuxMap.Api.Tests.WorkOrderTests.Cancellation_applies_to_repair_and_survey_and_notifies_only_active_anchor_managers()
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,8887 Seconds
```

### active_manager

Red exit `1`:

```text
[xUnit.net 00:00:06.60]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_applies_to_repair_and_survey_and_notifies_only_active_anchor_managers [FAIL]
[xUnit.net 00:00:06.60]       Assert.Empty() Failure: Collection was not empty
[xUnit.net 00:00:06.60]       Collection: [Notification { Body = "WO-0006 — Planted work order. Kỹ sư xin hủy / từ c"···, CommuneId = "COM-005", CreatedAt = 2026-10-26T10:46:23.9980930Z, EntityId = "WO-0006", EntityType = WorkOrder, ··· }]
[xUnit.net 00:00:06.60]       Stack Trace:
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,9807 Seconds
```

### allowed_request

Red exit `1`:

```text
[xUnit.net 00:00:06.42]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:06.42]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:06.42]       Collection: ["start", "bogus"]
[xUnit.net 00:00:06.42]       Not found:  "request_cancellation"
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,9010 Seconds
```

### allowed_reject

Red exit `1`:

```text
[xUnit.net 00:00:06.42]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:06.42]       Assert.Contains() Failure: Item not found in collection
[xUnit.net 00:00:06.42]       Collection: ["assign", "unassign", "edit", "cancel", "bogus"]
[xUnit.net 00:00:06.42]       Not found:  "reject_cancellation"
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,8767 Seconds
```

### capability

Red exit `1`:

```text
[xUnit.net 00:00:05.64]     LuxMap.Api.Tests.CapabilityPolicyCoverageTests.Every_endpoint_that_is_not_anonymous_names_a_capability_policy [FAIL]
[xUnit.net 00:00:05.64]       Assert.Empty() Failure: Collection was not empty
[xUnit.net 00:00:05.64]       Collection: ["POST /api/v1/work-orders/{id}/request-cancellation"]
[xUnit.net 00:00:05.64]       Stack Trace:
Test Run Failed.
Total tests: 7
     Passed: 5
     Failed: 2
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 7
     Passed: 7
 Total time: 6,7201 Seconds
```

### state_check

Red exit `1`:

```text
[xUnit.net 00:00:08.32]     LuxMap.Api.Tests.WorkOrderCancellationMigrationTests.Cancellation_schema_and_rollback_preserve_prior_values_and_refuse_loss [FAIL]
[xUnit.net 00:00:08.32]       Assert.Throws() Failure: No exception was thrown
[xUnit.net 00:00:08.32]       Expected: typeof(Npgsql.PostgresException)
[xUnit.net 00:00:08.32]       Stack Trace:
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 8,8569 Seconds
```

### old_audit_enum

Red exit `1`:

```text
[xUnit.net 00:00:08.34]     LuxMap.Api.Tests.WorkOrderCancellationMigrationTests.Cancellation_schema_and_rollback_preserve_prior_values_and_refuse_loss [FAIL]
[xUnit.net 00:00:08.34]       Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details.
[xUnit.net 00:00:08.34]       ---- Npgsql.PostgresException : 23514: new row for relation "audit_event" violates check constraint "ck_audit_event_action"
[xUnit.net 00:00:08.34]
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 8,7687 Seconds
```

### down_guard

Red exit `1`:

```text
[xUnit.net 00:00:08.34]     LuxMap.Api.Tests.WorkOrderCancellationMigrationTests.Cancellation_schema_and_rollback_preserve_prior_values_and_refuse_loss [FAIL]
[xUnit.net 00:00:08.34]       Assert.Throws() Failure: No exception was thrown
[xUnit.net 00:00:08.34]       Expected: typeof(Npgsql.PostgresException)
[xUnit.net 00:00:08.34]       Stack Trace:
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 8,9895 Seconds
```

### shared_pending_request

Red exit `1`:

```text
[xUnit.net 00:00:07.79]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:07.79]       Assert.DoesNotContain() Failure: Item found in collection
[xUnit.net 00:00:07.79]                             ↓ (pos 1)
[xUnit.net 00:00:07.79]       Collection: ["start", "request_cancellation"]
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,8569 Seconds
```

### shared_assigned_only

Red exit `1`:

```text
[xUnit.net 00:00:06.35]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_requires_assignee_commune_capability_and_assigned_status [FAIL]
[xUnit.net 00:00:06.35]       a POST /WO-0006/request-cancellation: expected 409, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"4b0a4964-e2c3-45c5-9292-8e7f67aab0fb"}}}
[xUnit.net 00:00:06.35]       Stack Trace:
[xUnit.net 00:00:06.35]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderTests.cs(115,0): at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation)
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,7677 Seconds
```

### shared_pending_reject

Red exit `1`:

```text
[xUnit.net 00:00:06.38]     LuxMap.Api.Tests.WorkOrderTests.Cancellation_substate_has_only_approved_edges_and_current_decision_semantics [FAIL]
[xUnit.net 00:00:06.38]       manager POST /WO-0004/reject-cancellation: expected 409, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"f12f191d-3533-4c54-80c4-1608f522a947"}}}
[xUnit.net 00:00:06.38]       Stack Trace:
[xUnit.net 00:00:06.38]         /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/WorkOrderTests.cs(115,0): at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation)
Test Run Failed.
Total tests: 1
     Failed: 1
```

Khôi phục bằng cp, green exit `0`:

```text
Test Run Successful.
Total tests: 1
     Passed: 1
 Total time: 6,9314 Seconds
```

## Catalog PostgreSQL trong test migration

Output thật từ WorkOrderCancellationMigrationTests (container riêng); test đã kiểm default của dòng cũ,
không có xmin vật lý, index commune còn, không pending model changes, từng enum cũ và mới ghi được,
Down sạch/reapply thành công và Down có dữ liệu mới từ chối nguyên tử:

Lệnh (exit 0, 1 pass / 0 fail / 0 skip):

```sh
dotnet test tests/LuxMap.Api.Tests --no-build --no-restore --disable-build-servers --filter 'FullyQualifiedName~Cancellation_schema' --logger 'console;verbosity=detailed'
```

```text
 ck_work_order_cancellation_request_status: CHECK ((cancellation_request_status = ANY (ARRAY['none'::text, 'pending'::text, 'rejected'::text])))
 ck_work_order_cancellation_request_state: CHECK ((((cancellation_request_status = 'none'::text) AND (cancellation_request_note IS NULL) AND (cancellation_requested_at IS NULL) AND (cancellation_reject_note IS NULL) AND (cancellation_rejected_at IS NULL)) OR ((wo_status = 'assigned'::text) AND (cancellation_request_note IS NOT NULL) AND ((char_length(btrim(cancellation_request_note)) >= 1) AND (char_length(btrim(cancellation_request_note)) <= 500)) AND (cancellation_requested_at IS NOT NULL) AND (((cancellation_request_status = 'pending'::text) AND (cancellation_reject_note IS NULL) AND (cancellation_rejected_at IS NULL)) OR ((cancellation_request_status = 'rejected'::text) AND (cancellation_reject_note IS NOT NULL) AND ((char_length(btrim(cancellation_reject_note)) >= 1) AND (char_length(btrim(cancellation_reject_note)) <= 500)) AND (cancellation_rejected_at IS NOT NULL) AND (cancellation_rejected_at >= cancellation_requested_at))))))
 ck_notification_type: CHECK ((type = ANY (ARRAY['work_order_cancellation_requested'::text, 'work_order_cancellation_rejected'::text, 'work_order_assigned'::text, 'work_order_unassigned'::text, 'work_order_rescheduled'::text, 'work_order_returned'::text, 'work_order_cancelled'::text, 'work_order_completed'::text, 'work_order_verified'::text, 'survey_returned'::text, 'survey_ready_for_review'::text, 'survey_processing_failed'::text, 'fault_reported'::text, 'citizen_report_received'::text, 'work_order_overdue'::text])))
 ck_audit_event_action: CHECK ((action = ANY (ARRAY['cancellation_requested'::text, 'cancellation_rejected'::text, 'created'::text, 'assigned'::text, 'reassigned'::text, 'unassigned'::text, 'started'::text, 'completed'::text, 'verified'::text, 'returned'::text, 'cancelled'::text, 'details_changed'::text, 'confirmed'::text, 'rejected'::text, 'submitted'::text, 'requested'::text, 'delivered'::text, 'applied'::text, 'failed'::text, 'expired'::text, 'superseded'::text, 'reported'::text, 'revoked'::text, 'accepted'::text, 'merged'::text, 'analyzed'::text, 'relocated'::text])))
 Apply / Down / reapply: passed. Down refused pending, each new notification, and new audit; schema/history intact.
```

## Kiểm chứng trên mã cuối và spec

Sau rà diff, bỏ điều kiện lặp: service và allowed_actions dùng chung WorkOrderRules.Allows.
Đã phá lại ba điều kiện và chạy lại regression. Bổ sung assert follow-up mới có đủ năm trường ở none/null.
Build dưới đây là sau mọi thay đổi code; `--no-build` ở các bước tiếp theo dùng đúng DLL vừa build.

### build — exit 0

```sh
dotnet build --no-restore --disable-build-servers -warnaserror
```

```text

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.64
```

### regression — exit 0

```sh
dotnet test tests/LuxMap.Api.Tests --no-build --no-restore --disable-build-servers --filter 'FullyQualifiedName~WorkOrderTests|FullyQualifiedName~CancellationMigration|FullyQualifiedName~NotificationTests|FullyQualifiedName~SyncTests|FullyQualifiedName~RoleCapabilityMatrixTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~JsonEnumHandlingTests' --logger 'console;verbosity=minimal'
```

```text
Test run for /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   291, Skipped:     0, Total:   291, Duration: 1 m 30 s - LuxMap.Api.Tests.dll (net10.0)
```

### swagger — exit 0

```sh
dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
```

```text
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/docs/openapi/luxmap-v1.json
```

### generator — exit 0

```sh
python3 docs/openapi/tools/gen_consolidated_spec.py
```

```text
wrote docs/openapi/luxmap-v1.5.json: paths=110 implemented_ops=141 not_implemented_ops=0 schemas=267
```

### lint — exit 0

```sh
npx --yes @redocly/cli@2.0.0 lint docs/openapi/luxmap-v1.5.json
```

```text

docs/openapi/luxmap-v1.5.json: validated in 88ms

Woohoo! Your API description is valid. 🎉
You have 8 warnings.

```

### shared-rules — exit 0

```sh
dotnet test tests/LuxMap.Shared.Tests --no-build --no-restore --disable-build-servers --filter 'FullyQualifiedName~WorkOrderStateMachineTests|FullyQualifiedName~FaultEligibilityTests'
```

```text
Test run for /Users/nhm809/Documents/LuxMap/luxmap-wt-WO-CANCEL/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 9 ms - LuxMap.Shared.Tests.dll (net10.0)
```

Swagger chạy với `Swagger__Enabled=true`, `Cors__AllowedOrigins__0=https://localhost:3000` theo commands.md.
JSON local được so với bản HEAD trước thay đổi: đúng hai POST mới, không mất operation cũ, đúng capability/role,
không đổi wo_status, enum notification giữ đủ 13 giá trị cũ + 2 mới, năm trường trên mọi schema đọc:

```text
Operations: 139 -> 141 ; raw Swagger: 141 ; version: 1.19
Only added operations: [('post', '/api/v1/work-orders/{id}/reject-cancellation'), ('post', '/api/v1/work-orders/{id}/request-cancellation')]
WorkOrderItem : all 5 cancellation fields
WorkOrderDetail : all 5 cancellation fields
AgendaWorkOrder : all 5 cancellation fields
AgendaWorkOrder : all 5 cancellation fields
request-cancellation cap:execute_work_orders ['field_engineer']
reject-cancellation cap:manage_work_orders ['manager']
NotificationType old= 13 new= 15
WorkOrderStatus old= 6 new= 6
CancellationRequestStatus: ['none', 'pending', 'rejected']
No old operation or wo_status removed/changed; schemas and capabilities verified.

```

Lint có **8 warning** (license, hai localhost server và năm schema ID/LatLng chưa dùng).
Đã lint cả spec HEAD trước thay đổi với cùng Redocly 2.0.0: exit 0, cũng 8 warning; không sửa ngoài phạm vi.
Output baseline:

```text
../../../../../private/tmp/wo-cancel-baseline-spec.json: validated in 73ms

Woohoo! Your API description is valid. 🎉
You have 8 warnings.

```

## Kết luận và giới hạn

✅ **Verified:** build warning-as-error 0 warning/error; API regression **291 pass, 0 fail, 0 skip**;
Shared rules **7 pass, 0 fail, 0 skip**; 23 lượt phá → đỏ (exit 1) → cp khôi phục → xanh (exit 0).
Migration apply/down/reapply và từ chối Down có dữ liệu mới đã chạy trên PostGIS riêng;
kiểm HTTP qua host thật và JSON/spec local, OpenAPI **141 implemented / 0 not implemented**, lint exit 0.
`git diff --check` sạch. AGENTS.md vẫn là symlink; không thay tracking/tasks/current, sync op hay copy PLAN.

⚠️ **Could not verify / ngoài phạm vi:** không chạy toàn suite theo ticket, không apply DB dev/prod, không kiểm UI WP5/WP6.
Contract v1.19 vẫn SELF-SIGNED, cần WP5/WP6 xác nhận ở FW (CR-1…CR-6); không gửi thông báo cho người khác trong phiên này.

📌 **Remaining assumptions:** giữ nguyên D-9 (màn hình cũ quyết định lên yêu cầu hiện tại), D-10 (online-only),
D-13 (dời lịch = hết hiệu lực); không phát sinh D-item cần quyết định thêm.

Commit dự kiến: `feat(WO-CANCEL-REQUEST): support work order cancellation requests`.
Stage theo đường dẫn tường minh; không push, không Co-Authored-By. Hash commit báo ở câu trả lời cuối (tránh tự tham chiếu trong commit).
