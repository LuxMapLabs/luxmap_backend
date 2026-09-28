# BE-23 Phase 2 — work orders: kết quả thực thi

Ngày: 28/09/2026. Agent: Codex. **Đã triển khai và kiểm chứng; chờ Claude review, chưa commit/push.**

Kết quả cuối: build 0 warning/0 error; 672 test xanh; 19 trường hợp sabotage theo §13 đã đỏ rồi khôi phục; migrate apply → rollback → reapply và seed chỉ trên luxmap_test. API là nền tạm tới FW kế tiếp. Các đoạn dừng/lỗi bên dưới là lịch sử, đã được giải; bằng chứng cuối nằm ở cuối báo cáo.

## Đối chiếu

- Nhánh thực tế `feat/BE-23-work-orders`, HEAD `cec66ff0cc2e01de334da71a2a8f88cd9d64bd0b`: merge PR #53 BE-23a, đúng yêu cầu.
- Matrix có bốn vai trò v1.7 và sáu capability, chưa có work order: khớp nền thiết kế.
- `fault` chưa có `work_order_id`; FK hiện có RESTRICT. `audit_event` tồn tại, trống, có CHECK và hai trigger append-only; `work_order` chưa tồn tại. Đây là nền sau BE-23a đã được yêu cầu, không phải mâu thuẫn với lịch sử trước BE-23a.
- Tổng fault trên `luxmap_test`: 7 confirmed + 21 detected, khớp số lượng bộ mock trong decisions.
- **Khác dữ kiện commune:** decisions §D3 và §10.5 dẫn bộ mock tại `COM-070` trên DB dev; trên DB test được chỉ định ở phiên này, toàn bộ 28 fault nằm tại `COM-001`. Hai user `engineer`/`crew` cũng được gán `COM-001`.
- Đây là đối chiếu **hai DB khác nhau**, chưa chứng minh có lỗi dữ liệu hay phải đổi thiết kế. Tuy nhiên yêu cầu Bước 0 nói có bất kỳ kết quả mâu thuẫn với decisions thì dừng và báo; agent không tự quyết coi ID commune khác là ngoại lệ của điều kiện đó. Không đọc hay ghi `luxmap_dev` trong phiên này.

## Bằng chứng Bước 0 — chạy thật

Các lệnh dưới đây được chạy lại khi lập báo cáo, output giữ nội dung, chỉ bỏ khoảng trắng cuối dòng.

```sh
git branch --show-current
```

Exit code: 0

```text
feat/BE-23-work-orders
```

```sh
git log -1
```

Exit code: 0

```text
commit cec66ff0cc2e01de334da71a2a8f88cd9d64bd0b
Merge: f07f974 3a7a9c4
Author: My (Dylan) Nguyen Huu <mexnguyen894@gmail.com>
Date:   Mon Sep 28 09:41:01 2026 +0700

    Merge pull request #53 from LuxMapLabs/feat/BE-23a-audit-event

    feat(audit): add transactional append-only audit storage (BE-23a)
```

```sh
git status --short
```

Exit code: 0

```text
?? ".ai/context/FA26SE222 v1.1.docx"
?? .ai/context/tracking.html
?? .ai/results/review1/
?? LuxMap_TaskList_v2.1.xlsx
```

```sh
docker compose exec -T postgres psql -X -U luxmap -d luxmap_test -c '\d fault' -c '\d audit_event' -c 'SELECT fault_status, count(*) FROM fault GROUP BY 1;' -c '\d app_user' -c '\d app_user_commune'
```

Exit code: 0

```text
                                                               Table "public.fault"
         Column          |           Type           | Collation | Nullable |                                Default
-------------------------+--------------------------+-----------+----------+-----------------------------------------------------------------------
 fault_id                | text                     |           | not null | luxmap_format_id('FAULT'::text, nextval('fault_id_seq'::regclass), 4)
 client_op_id            | text                     |           |          |
 pole_id                 | text                     |           |          |
 fixture_id              | text                     |           |          |
 segment_id              | text                     |           |          |
 commune_id              | text                     |           | not null |
 lat                     | double precision         |           |          |
 lng                     | double precision         |           |          |
 fault_type              | text                     |           | not null |
 fault_status            | text                     |           | not null |
 severity                | text                     |           | not null |
 source_channel          | text                     |           | not null |
 data_source             | text                     |           | not null |
 priority_score          | double precision         |           |          |
 status_confidence       | double precision         |           |          |
 cluster_id              | text                     |           |          |
 detected_at             | timestamp with time zone |           | not null |
 updated_at              | timestamp with time zone |           | not null | now()
 note                    | text                     |           |          |
 reported_by             | text                     |           |          |
 confirmed_by            | text                     |           |          |
 confirmed_at            | timestamp with time zone |           |          |
 resolved_by             | text                     |           |          |
 resolved_at             | timestamp with time zone |           |          |
 detection_model_version | text                     |           |          |
 created_at              | timestamp with time zone |           | not null | now()
Indexes:
    "pk_fault" PRIMARY KEY, btree (fault_id)
    "ix_fault_cluster_id" btree (cluster_id)
    "ix_fault_commune_id" btree (commune_id)
    "ix_fault_confirmed_by" btree (confirmed_by)
    "ix_fault_fault_status" btree (fault_status)
    "ix_fault_fixture_id" btree (fixture_id)
    "ix_fault_pole_id" btree (pole_id)
    "ix_fault_priority_score" btree (priority_score DESC)
    "ix_fault_reported_by" btree (reported_by)
    "ix_fault_resolved_by" btree (resolved_by)
    "ix_fault_segment_id" btree (segment_id)
    "ux_fault_client_op_id" UNIQUE, btree (client_op_id) WHERE client_op_id IS NOT NULL
Check constraints:
    "ck_fault_data_source" CHECK (data_source = ANY (ARRAY['field'::text, 'public_imagery'::text, 'calibration_rig'::text, 'simulated'::text]))
    "ck_fault_fault_status" CHECK (fault_status = ANY (ARRAY['detected'::text, 'confirmed'::text, 'rejected'::text, 'in_progress'::text, 'resolved'::text, 'verified'::text]))
    "ck_fault_fault_type" CHECK (fault_type = ANY (ARRAY['lamp_out'::text, 'lamp_dim'::text, 'segment_outage'::text, 'node_offline'::text, 'runtime_decline'::text]))
    "ck_fault_location_finite" CHECK ((lat IS NULL OR lat <> 'NaN'::double precision AND lat <> 'Infinity'::double precision AND lat <> '-Infinity'::double precision AND lat >= '-90'::integer::double precision AND lat <= 90::double precision) AND (lng IS NULL OR lng <> 'NaN'::double precision AND lng <> 'Infinity'::double precision AND lng <> '-Infinity'::double precision AND lng >= '-180'::integer::double precision AND lng <= 180::double precision))
    "ck_fault_pole_or_location" CHECK (pole_id IS NOT NULL OR lat IS NOT NULL AND lng IS NOT NULL)
    "ck_fault_priority_score_finite" CHECK (priority_score IS NULL OR priority_score <> 'NaN'::double precision AND priority_score <> 'Infinity'::double precision AND priority_score <> '-Infinity'::double precision)
    "ck_fault_severity" CHECK (severity = ANY (ARRAY['low'::text, 'medium'::text, 'high'::text, 'critical'::text]))
    "ck_fault_source_channel" CHECK (source_channel = ANY (ARRAY['cv'::text, 'iot'::text, 'field_report'::text]))
    "ck_fault_status_confidence_range" CHECK (status_confidence IS NULL OR status_confidence >= 0::double precision AND status_confidence <= 1::double precision AND status_confidence <> 'NaN'::double precision AND status_confidence <> 'Infinity'::double precision AND status_confidence <> '-Infinity'::double precision)
Foreign-key constraints:
    "fk_fault_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_fault_app_user_confirmed_by" FOREIGN KEY (confirmed_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    "fk_fault_app_user_reported_by" FOREIGN KEY (reported_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    "fk_fault_app_user_resolved_by" FOREIGN KEY (resolved_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    "fk_fault_fault_cluster_cluster_id" FOREIGN KEY (cluster_id) REFERENCES fault_cluster(cluster_id) ON DELETE RESTRICT
    "fk_fault_fixture_fixture_id" FOREIGN KEY (fixture_id) REFERENCES fixture(fixture_id) ON DELETE RESTRICT
    "fk_fault_pole_pole_id" FOREIGN KEY (pole_id) REFERENCES pole(pole_id) ON DELETE RESTRICT
    "fk_fault_road_segment_segment_id" FOREIGN KEY (segment_id) REFERENCES road_segment(segment_id) ON DELETE RESTRICT

                                   Table "public.audit_event"
     Column     |           Type           | Collation | Nullable |           Default
----------------+--------------------------+-----------+----------+------------------------------
 audit_id       | bigint                   |           | not null | generated always as identity
 occurred_at    | timestamp with time zone |           | not null | now()
 actor_kind     | text                     |           | not null |
 actor_user_id  | text                     |           |          |
 actor_role     | text                     |           |          |
 commune_id     | text                     |           | not null |
 entity_type    | text                     |           | not null |
 entity_id      | text                     |           | not null |
 action         | text                     |           | not null |
 before_state   | jsonb                    |           |          |
 after_state    | jsonb                    |           |          |
 note           | text                     |           |          |
 correlation_id | text                     |           | not null |
Indexes:
    "pk_audit_event" PRIMARY KEY, btree (audit_id)
    "ix_audit_event_actor_user_id" btree (actor_user_id)
    "ix_audit_event_commune_id" btree (commune_id)
    "ix_audit_event_entity" btree (entity_type, entity_id, audit_id)
Check constraints:
    "ck_audit_event_action" CHECK (action = ANY (ARRAY['created'::text, 'assigned'::text, 'reassigned'::text, 'unassigned'::text, 'started'::text, 'completed'::text, 'verified'::text, 'returned'::text, 'cancelled'::text, 'details_changed'::text]))
    "ck_audit_event_actor" CHECK ((actor_kind = 'user'::text) = (actor_user_id IS NOT NULL) AND (actor_user_id IS NULL) = (actor_role IS NULL))
    "ck_audit_event_actor_kind" CHECK (actor_kind = ANY (ARRAY['user'::text, 'cv'::text, 'iot'::text]))
    "ck_audit_event_actor_role" CHECK (actor_role IS NULL OR (actor_role = ANY (ARRAY['superior'::text, 'manager'::text, 'field_engineer'::text, 'system_admin'::text])))
    "ck_audit_event_after_state_object" CHECK (after_state IS NULL OR jsonb_typeof(after_state) = 'object'::text)
    "ck_audit_event_before_state_object" CHECK (before_state IS NULL OR jsonb_typeof(before_state) = 'object'::text)
    "ck_audit_event_entity_type" CHECK (entity_type = 'work_order'::text)
    "ck_audit_event_has_state" CHECK (before_state IS NOT NULL OR after_state IS NOT NULL)
Foreign-key constraints:
    "fk_audit_event_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_audit_event_app_user_actor_user_id" FOREIGN KEY (actor_user_id) REFERENCES app_user(user_id) ON DELETE RESTRICT
Triggers:
    audit_event_append_only_rows BEFORE DELETE OR UPDATE ON audit_event FOR EACH ROW EXECUTE FUNCTION luxmap_audit_event_append_only()
    audit_event_append_only_truncate BEFORE TRUNCATE ON audit_event FOR EACH STATEMENT EXECUTE FUNCTION luxmap_audit_event_append_only()

 fault_status | count
--------------+-------
 confirmed    |     7
 detected     |    21
(2 rows)

                                                           Table "public.app_user"
        Column         |           Type           | Collation | Nullable |                              Default
-----------------------+--------------------------+-----------+----------+--------------------------------------------------------------------
 user_id               | text                     |           | not null | luxmap_format_id('USR'::text, nextval('user_id_seq'::regclass), 3)
 username              | text                     |           | not null |
 email                 | text                     |           | not null |
 full_name             | text                     |           | not null |
 password_hash         | text                     |           | not null |
 password_algorithm    | text                     |           | not null |
 role                  | text                     |           | not null |
 is_locked             | boolean                  |           | not null | false
 has_system_wide_scope | boolean                  |           | not null | false
 created_at            | timestamp with time zone |           | not null | now()
 updated_at            | timestamp with time zone |           | not null | now()
Indexes:
    "pk_app_user" PRIMARY KEY, btree (user_id)
    "ix_app_user_email" UNIQUE, btree (email)
    "ix_app_user_email_lower" UNIQUE, btree (lower(email))
    "ix_app_user_username" UNIQUE, btree (username)
    "ix_app_user_username_lower" UNIQUE, btree (lower(username))
Check constraints:
    "ck_app_user_role" CHECK (role = ANY (ARRAY['superior'::text, 'manager'::text, 'field_engineer'::text, 'system_admin'::text]))
Referenced by:
    TABLE "app_user_commune" CONSTRAINT "fk_app_user_commune_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user(user_id) ON DELETE CASCADE
    TABLE "audit_event" CONSTRAINT "fk_audit_event_app_user_actor_user_id" FOREIGN KEY (actor_user_id) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "fault" CONSTRAINT "fk_fault_app_user_confirmed_by" FOREIGN KEY (confirmed_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "fault" CONSTRAINT "fk_fault_app_user_reported_by" FOREIGN KEY (reported_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "fault" CONSTRAINT "fk_fault_app_user_resolved_by" FOREIGN KEY (resolved_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "lux_reading" CONSTRAINT "fk_lux_reading_app_user_measured_by" FOREIGN KEY (measured_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    TABLE "refresh_token" CONSTRAINT "fk_refresh_token_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user(user_id) ON DELETE CASCADE

                     Table "public.app_user_commune"
   Column    |           Type           | Collation | Nullable | Default
-------------+--------------------------+-----------+----------+---------
 user_id     | text                     |           | not null |
 commune_id  | text                     |           | not null |
 assigned_at | timestamp with time zone |           | not null | now()
Indexes:
    "pk_app_user_commune" PRIMARY KEY, btree (user_id, commune_id)
    "ix_app_user_commune_commune_id" btree (commune_id)
Foreign-key constraints:
    "fk_app_user_commune_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_app_user_commune_app_user_user_id" FOREIGN KEY (user_id) REFERENCES app_user(user_id) ON DELETE CASCADE

```

```sh
cat src/LuxMap.Shared/Authorization/LuxMapPolicies.cs
```

Exit code: 0

```text
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// The capability matrix: every authorization policy of the API and the EXACT roles it admits.
/// This file is the only source <c>AuthorizationSetup</c> registers policies from.
/// </summary>
/// <remarks>
/// Declared in <c>LuxMap.Shared</c> rather than beside the registration in <c>LuxMap.Api</c>, because
/// the two ends live in different assemblies and the dependency only runs one way: the host
/// references the modules, so a module controller writing
/// <c>[Authorize(Policy = LuxMapPolicies.ManageAssets)]</c> could not see a name defined in the host.
/// The names are a CONTRACT between the two, which is exactly what Shared is for.
/// <para>
/// ⚠️ <b>A policy is a LIST of exact roles, never a rank</b> (Contract v1.7 section 2, replacing
/// D-14's "one exact role"). There is no "manager and above": a role is admitted because it is
/// named in <see cref="Matrix"/>, and a role that is not named is refused. Registration form v1.2
/// needs this — the Superior reads but never writes, and some work is split between the Manager and
/// the Field Engineer — and a single-role policy could only express it by leaving endpoints bare.
/// </para>
/// <para>
/// Every business endpoint names one of these policies; none relies on the fallback policy
/// (<c>CapabilityPolicyCoverageTests</c>). A capability without an endpoint yet is declared here so
/// its roles are decided once, before the ticket that builds it: <see cref="ControlLighting"/>
/// (D-R7) and <see cref="ManageUsers"/> (BE-33). Other capabilities — survey review, work orders,
/// fault decisions — are added WITH their ticket, not in advance.
/// </para>
/// </remarks>
public static class LuxMapPolicies
{
    /// <summary>Read the lighting network: map layers, asset inventory, topology.</summary>
    public const string ReadNetwork = "cap:read_network";

    /// <summary>Read lux readings.</summary>
    public const string ReadLuxReadings = "cap:read_lux_readings";

    /// <summary>Create, replace, delete and import poles, fixtures, segments and feeders.</summary>
    public const string ManageAssets = "cap:manage_assets";

    /// <summary>Record a relative light reading taken in the field.</summary>
    public const string RecordLuxReading = "cap:record_lux_reading";

    /// <summary>
    /// Force ON / OFF / AUTO on supported lighting devices (testbed demo). No endpoint yet (D-R7).
    /// </summary>
    public const string ControlLighting = "cap:control_lighting";

    /// <summary>Create accounts and assign roles and communes. No endpoint yet (BE-33).</summary>
    public const string ManageUsers = "cap:manage_users";

    /// <summary>Policy name → the roles it admits. Exhaustive: a policy missing here does not exist.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<UserRole>> Matrix { get; } =
        new Dictionary<string, IReadOnlyList<UserRole>>(StringComparer.Ordinal)
        {
            [ReadNetwork] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ReadLuxReadings] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ManageAssets] = [UserRole.Manager],
            [RecordLuxReading] = [UserRole.FieldEngineer],
            [ControlLighting] = [UserRole.Manager],
            [ManageUsers] = [UserRole.SystemAdmin],
        };
}
```

```sh
docker compose exec -T postgres psql -X -U luxmap -d luxmap_test -c 'SELECT commune_id, fault_status, count(*) FROM fault GROUP BY 1,2 ORDER BY 1,2;' -c 'SELECT to_regclass('"'"'work_order'"'"'), count(*) AS audit_count FROM audit_event;' -c 'SELECT u.user_id, u.username, u.role, u.is_locked, c.commune_id FROM app_user u LEFT JOIN app_user_commune c ON c.user_id=u.user_id WHERE u.username IN ('"'"'engineer'"'"','"'"'crew'"'"') ORDER BY u.username,c.commune_id;'
```

Exit code: 0

```text
 commune_id | fault_status | count
------------+--------------+-------
 COM-001    | confirmed    |     7
 COM-001    | detected     |    21
(2 rows)

 to_regclass | audit_count
-------------+-------------
             |           0
(1 row)

 user_id | username |      role      | is_locked | commune_id
---------+----------+----------------+-----------+------------
 USR-004 | crew     | field_engineer | f         | COM-001
 USR-003 | engineer | manager        | f         | COM-001
(2 rows)

```

```sh
git diff --stat -- src tests scripts mocks docs/openapi docs/api-contract-v1.1.md
```

Exit code: 0

```text
(output rỗng)
```

## Chưa thực hiện

Chưa sửa source/test, tạo hoặc apply migration, seed, chạy build/test/sabotage hay sinh OpenAPI. Không có Up/Down mới để đọc. Không thay đổi dữ liệu DB; không commit/push. Không đăng ký WO-1…WO-11 như phần đã triển khai.

✅ Verified: nhánh/HEAD, Matrix, schema và số lượng fault trên `luxmap_test` bằng output trên.
⚠️ Could not verify: toàn bộ hành vi work order chưa được triển khai; không dùng số test của BE-23a làm bằng chứng BE-23.
📌 Remaining assumptions: chưa có quyết định cho khác biệt commune giữa nền dev trong decisions và DB test hiện tại.

## Đề xuất lúc dừng Bước 0 — đã được thay bằng danh sách cuối file

Chưa có commit triển khai để chia. Nếu reviewer muốn lưu riêng báo cáo kiểm tra nền:

- `docs(be-23): record work-order baseline check` — `.ai/results/BE-23-p2-work-orders.md`, `tracking.html`.

Không commit trong phiên này theo yêu cầu người dùng.

## D-item phát sinh — chưa chốt

**D-WO-P2-01 — ID commune của bộ mock trên DB test.** Đề nghị Mỹ xác nhận tiếp tục dùng `COM-001` đang có trên `luxmap_test`, tra xã từ dữ liệu/`study_site` theo §10 thay vì coi `COM-070` trong nền dev là literal bắt buộc. Không đề xuất đổi ID hoặc chuyển dữ liệu DB test cho giống DB dev. Agent chưa tự chốt; dừng trước triển khai theo điều kiện Bước 0.

### D-WO-P2-01 ĐÃ GIẢI

Claude, 28/09/2026, theo lệnh “tiến hành làm tiếp” của Mỹ: không phải mâu thuẫn thiết kế. `seed_mock_set.py` tra xã bằng `seed_key = 'study_site'` (dòng 55); luxmap_dev: COM-070 / luxmap_test: COM-001, cùng 'Commune 01' seed_key study_site — ID khác chỉ vì sequence. Trên luxmap_test: USR-002 agency superior, USR-003 engineer manager, USR-004 crew field_engineer, đều COM-001, không khoá. Không dùng literal COM-070/COM-001 ở đâu trong code/test/seed. Tiếp tục Phase 2; phần dừng bên trên là lịch sử.

## Migration — đọc bản sinh đầu tiên, CHƯA apply

Done. To undo this action, use 'ef migrations remove'

```csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "work_order_id_seq");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "fault",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_fault_fault_id_commune_id",
                table: "fault",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateTable(
                name: "work_order",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('WO', nextval('work_order_id_seq'), 4)"),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    task_kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    wo_status = table.Column<string>(type: "text", nullable: false),
                    segment_id = table.Column<string>(type: "text", nullable: true),
                    cluster_id = table.Column<string>(type: "text", nullable: true),
                    assigned_to = table.Column<string>(type: "text", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    report_note = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order", x => x.work_order_id);
                    table.UniqueConstraint("ak_work_order_work_order_id_commune_id", x => new { x.work_order_id, x.commune_id });
                    table.CheckConstraint("ck_work_order_assigned_at_matches", "(assigned_to IS NULL) = (assigned_at IS NULL)");
                    table.CheckConstraint("ck_work_order_assignee_matches_status", "(wo_status = 'open' AND assigned_to IS NULL) OR (wo_status IN ('assigned','in_progress','done','verified') AND assigned_to IS NOT NULL) OR wo_status = 'cancelled'");
                    table.CheckConstraint("ck_work_order_closed", "(wo_status IN ('verified','cancelled')) = (closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_work_order_completed", "wo_status NOT IN ('done','verified') OR (completed_at IS NOT NULL AND report_note IS NOT NULL)");
                    table.CheckConstraint("ck_work_order_schedule_before_due", "scheduled_date IS NULL OR due_date IS NULL OR scheduled_date <= due_date");
                    table.CheckConstraint("ck_work_order_started", "wo_status NOT IN ('in_progress','done','verified') OR started_at IS NOT NULL");
                    table.CheckConstraint("ck_work_order_task_kind", "\"task_kind\" IN ('inspection', 'repair')");
                    table.CheckConstraint("ck_work_order_title_not_blank", "btrim(title) <> ''");
                    table.CheckConstraint("ck_work_order_wo_status", "\"wo_status\" IN ('open', 'assigned', 'in_progress', 'done', 'verified', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_work_order_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_app_user_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_cluster_cluster_id",
                        column: x => x.cluster_id,
                        principalTable: "fault_cluster",
                        principalColumn: "cluster_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_fault",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    fault_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspection_outcome = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_fault", x => new { x.work_order_id, x.fault_id });
                    table.CheckConstraint("ck_work_order_fault_inspection_outcome", "\"inspection_outcome\" IS NULL OR \"inspection_outcome\" IN ('fault_present', 'fault_absent', 'inconclusive')");
                    table.CheckConstraint("ck_work_order_fault_release_after_link", "released_at IS NULL OR released_at >= linked_at");
                    table.ForeignKey(
                        name: "fk_work_order_fault_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_fault_fault_id_commune_id",
                        columns: x => new { x.fault_id, x.commune_id },
                        principalTable: "fault",
                        principalColumns: new[] { "fault_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_assigned_to",
                table: "work_order",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_cluster_id",
                table: "work_order",
                column: "cluster_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_commune_id",
                table: "work_order",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_created_by",
                table: "work_order",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_id",
                table: "work_order",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_wo_status",
                table: "work_order",
                column: "wo_status");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_commune_id",
                table: "work_order_fault",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id",
                table: "work_order_fault",
                column: "fault_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id_commune_id",
                table: "work_order_fault",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id1",
                table: "work_order_fault",
                column: "fault_id",
                unique: true,
                filter: "released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_work_order_id_commune_id",
                table: "work_order_fault",
                columns: new[] { "work_order_id", "commune_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_fault");

            migrationBuilder.DropTable(
                name: "work_order");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_fault_fault_id_commune_id",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "fault");

            migrationBuilder.DropSequence(
                name: "work_order_id_seq");
        }
    }
}

```

Phát hiện AddColumn/DropColumn xmin và tên DB index không khớp tên bắt 23505. Sửa trước apply: khai HasDatabaseName, sinh lại; bỏ thao tác xmin vật lý vì đó là cột hệ thống, giữ mapping IsRowVersion. Không DropIndex hiện có.

## Migration cuối — đã ĐỌC trước apply

Không DropIndex, không AddColumn/DropColumn xmin; FK Restrict; index commune_id tường minh; Down đối xứng. Snapshot/Designer giữ mapping xmin để EF dùng concurrency.

```csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "work_order_id_seq");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_fault_fault_id_commune_id",
                table: "fault",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateTable(
                name: "work_order",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "luxmap_format_id('WO', nextval('work_order_id_seq'), 4)"),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    task_kind = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    wo_status = table.Column<string>(type: "text", nullable: false),
                    segment_id = table.Column<string>(type: "text", nullable: true),
                    cluster_id = table.Column<string>(type: "text", nullable: true),
                    assigned_to = table.Column<string>(type: "text", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    review_note = table.Column<string>(type: "text", nullable: true),
                    report_note = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order", x => x.work_order_id);
                    table.UniqueConstraint("ak_work_order_work_order_id_commune_id", x => new { x.work_order_id, x.commune_id });
                    table.CheckConstraint("ck_work_order_assigned_at_matches", "(assigned_to IS NULL) = (assigned_at IS NULL)");
                    table.CheckConstraint("ck_work_order_assignee_matches_status", "(wo_status = 'open' AND assigned_to IS NULL) OR (wo_status IN ('assigned','in_progress','done','verified') AND assigned_to IS NOT NULL) OR wo_status = 'cancelled'");
                    table.CheckConstraint("ck_work_order_closed", "(wo_status IN ('verified','cancelled')) = (closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_work_order_completed", "wo_status NOT IN ('done','verified') OR (completed_at IS NOT NULL AND report_note IS NOT NULL)");
                    table.CheckConstraint("ck_work_order_schedule_before_due", "scheduled_date IS NULL OR due_date IS NULL OR scheduled_date <= due_date");
                    table.CheckConstraint("ck_work_order_started", "wo_status NOT IN ('in_progress','done','verified') OR started_at IS NOT NULL");
                    table.CheckConstraint("ck_work_order_task_kind", "\"task_kind\" IN ('inspection', 'repair')");
                    table.CheckConstraint("ck_work_order_title_not_blank", "btrim(title) <> ''");
                    table.CheckConstraint("ck_work_order_wo_status", "\"wo_status\" IN ('open', 'assigned', 'in_progress', 'done', 'verified', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_work_order_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_app_user_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_app_user_created_by",
                        column: x => x.created_by,
                        principalTable: "app_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_cluster_cluster_id",
                        column: x => x.cluster_id,
                        principalTable: "fault_cluster",
                        principalColumn: "cluster_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_road_segment_segment_id",
                        column: x => x.segment_id,
                        principalTable: "road_segment",
                        principalColumn: "segment_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_fault",
                columns: table => new
                {
                    work_order_id = table.Column<string>(type: "text", nullable: false),
                    fault_id = table.Column<string>(type: "text", nullable: false),
                    commune_id = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspection_outcome = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_fault", x => new { x.work_order_id, x.fault_id });
                    table.CheckConstraint("ck_work_order_fault_inspection_outcome", "\"inspection_outcome\" IS NULL OR \"inspection_outcome\" IN ('fault_present', 'fault_absent', 'inconclusive')");
                    table.CheckConstraint("ck_work_order_fault_release_after_link", "released_at IS NULL OR released_at >= linked_at");
                    table.ForeignKey(
                        name: "fk_work_order_fault_administrative_unit_commune_id",
                        column: x => x.commune_id,
                        principalTable: "administrative_unit",
                        principalColumn: "commune_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_fault_fault_id_commune_id",
                        columns: x => new { x.fault_id, x.commune_id },
                        principalTable: "fault",
                        principalColumns: new[] { "fault_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_fault_work_order_work_order_id_commune_id",
                        columns: x => new { x.work_order_id, x.commune_id },
                        principalTable: "work_order",
                        principalColumns: new[] { "work_order_id", "commune_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_assigned_to",
                table: "work_order",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_cluster_id",
                table: "work_order",
                column: "cluster_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_commune_id",
                table: "work_order",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_created_by",
                table: "work_order",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_segment_id",
                table: "work_order",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_wo_status",
                table: "work_order",
                column: "wo_status");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_commune_id",
                table: "work_order_fault",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id",
                table: "work_order_fault",
                column: "fault_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_fault_id_commune_id",
                table: "work_order_fault",
                columns: new[] { "fault_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_fault_work_order_id_commune_id",
                table: "work_order_fault",
                columns: new[] { "work_order_id", "commune_id" });

            migrationBuilder.CreateIndex(
                name: "ux_work_order_fault_fault_id_active",
                table: "work_order_fault",
                column: "fault_id",
                unique: true,
                filter: "released_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_fault");

            migrationBuilder.DropTable(
                name: "work_order");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_fault_fault_id_commune_id",
                table: "fault");

            migrationBuilder.DropSequence(
                name: "work_order_id_seq");
        }
    }
}

```

### Sabotage S01 capability

Mutation: `src/LuxMap.Shared/Authorization/LuxMapPolicies.cs`: [ManageWorkOrders] = [UserRole.Manager] → [ManageWorkOrders] = [UserRole.Manager, UserRole.Superior]

```sh
dotnet test --filter FullyQualifiedName~CapabilityMatrix
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
A total of 1 test files matched the specified pattern.
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll (.NETCoreApp,Version=v10.0)
No test matches the given testcase filter `(Category!=Benchmark)&(FullyQualifiedName~CapabilityMatrix)` in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
A total of 1 test files matched the specified pattern.

No test matches the given testcase filter `(Category!=Benchmark)&(FullyQualifiedName~CapabilityMatrix)` in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll

  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.12]     LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract [FAIL]
  Failed LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract [11 ms]
  Error Message:
   cap:manage_work_orders: Contract says [manager], matrix says [manager, superior]
  Stack Trace:
     at LuxMap.Shared.Tests.CapabilityMatrixTests.Each_capability_admits_exactly_the_roles_of_the_Contract() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/CapabilityMatrixTests.cs:line 43
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 24 ms - LuxMap.Shared.Tests.dll (net10.0)
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.47]     LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ManageWorkOrders", role: "superior") [FAIL]
  Failed LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(capability: "ManageWorkOrders", role: "superior") [40 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Forbidden
Actual:   BadRequest
  Stack Trace:
     at LuxMap.Api.Tests.RoleCapabilityMatrixTests.Each_role_is_admitted_or_refused_exactly_as_the_matrix_says(String capability, String role) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/RoleCapabilityMatrixTests.cs:line 99
--- End of stack trace from previous location ---
  Standard Output Messages:
   superior        ManageWorkOrders  POST /api/v1/work-orders → HTTP 400



Failed!  - Failed:     1, Passed:    36, Skipped:     0, Total:    37, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S02 state machine

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderRules.cs`: or "edit" or "cancel" => → or "edit" =>

```sh
dotnet test tests/LuxMap.Shared.Tests --filter FullyQualifiedName~WorkOrderStateMachineTests
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.09]     LuxMap.Shared.Tests.WorkOrderStateMachineTests.Literal_six_by_nine_transition_table [FAIL]
  Failed LuxMap.Shared.Tests.WorkOrderStateMachineTests.Literal_six_by_nine_transition_table [2 ms]
  Error Message:
   Done/cancel
  Stack Trace:
     at LuxMap.Shared.Tests.WorkOrderStateMachineTests.Literal_six_by_nine_transition_table() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/WorkOrderRuleTests.cs:line 27
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 6 ms - LuxMap.Shared.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S03 HTTP return

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`: if (!WorkOrderRules.Allows(wo.WoStatus, action)) → if (action != "return" && !WorkOrderRules.Allows(wo.WoStatus, action))

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Transition_http_literal
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.58]     LuxMap.Api.Tests.WorkOrderTests.Transition_http_literal_six_by_nine_table [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Transition_http_literal_six_by_nine_table [231 ms]
  Error Message:
   manager POST /WO-0323/return: expected 409, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"581d5f89-6792-4256-86fc-7daab2cc5d58"}}}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Transition_http_literal_six_by_nine_table() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 181
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 806 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S04 assignee marker

Mutation: `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs`: ICommuneScoped, IAssigneeScoped, IAudited → ICommuneScoped, IAudited

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Actor_and_commune
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.44]     LuxMap.Api.Tests.WorkOrderTests.Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed [137 ms]
  Error Message:
   a GET /WO-0325: expected 404, got 200: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-1494","assigned_at":"2026-09-28T03:14:27.075569Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":true,"allowed_actions":[],"faults":[],"work_order_id":"WO-0325","title":"Planted work order","commune_id":"COM-644","task_kind":"inspection","segment_id":"SEG-4457","cluster_id":null,"fault_ids":[],"wo_status":"assigned","assigned_to":"USR-1490","priority_score":null,"created_at":"2026-09-28T03:14:27.075569Z","updated_at":"2026-09-28T03:14:27.075569Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 195
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 723 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S05 filter overwrite

Mutation: `src/LuxMap.Persistence/Conventions/CommuneScopeBuilderExtensions.cs`: == context.CurrentAssigneeRestriction)); → == context.CurrentAssigneeRestriction));
            modelBuilder.Entity<TEntity>().HasQueryFilter(candidate => context.CurrentAssigneeRestriction == null || EF.Property<string?>(candidate, nameof(IAssigneeScoped.AssignedTo)) == context.CurrentAssigneeRestriction);

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Actor_and_commune
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.43]     LuxMap.Api.Tests.WorkOrderTests.Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed [143 ms]
  Error Message:
   a GET /WO-0329: expected 404, got 200: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-1500","assigned_at":"2026-09-28T03:14:31.240249Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":false,"allowed_actions":["start"],"faults":[],"work_order_id":"WO-0329","title":"Planted work order","commune_id":"COM-650","task_kind":"inspection","segment_id":"SEG-4459","cluster_id":null,"fault_ids":[],"wo_status":"assigned","assigned_to":"USR-1499","priority_score":null,"created_at":"2026-09-28T03:14:31.240249Z","updated_at":"2026-09-28T03:14:31.240249Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 195
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 715 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S06 client commune

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`: request.WorkOrderId, request.CommuneId, request.WoStatus → request.WorkOrderId, request.WoStatus

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Eligibility_scope
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.55]     LuxMap.Api.Tests.WorkOrderTests.Eligibility_scope_and_server_owned_fields [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Eligibility_scope_and_server_owned_fields [260 ms]
  Error Message:
   manager POST : expected 400, got 201: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-1507","assigned_at":null,"started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":null,"allowed_actions":["assign","unassign","edit","cancel"],"faults":[],"work_order_id":"WO-0335","title":"Own field","commune_id":"COM-654","task_kind":"inspection","segment_id":"SEG-4463","cluster_id":null,"fault_ids":[],"wo_status":"open","assigned_to":null,"priority_score":null,"created_at":"2026-09-28T03:14:35.303092Z","updated_at":"2026-09-28T03:14:35.303092Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Eligibility_scope_and_server_owned_fields() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 227
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 828 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S07 caller scope instead of DB membership

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`: && db.Set<AppUserCommune>().Any(link => link.UserId == user.UserId && link.CommuneId == commune) → && db.CurrentCommuneScope.IsSystemWide || (user.Role == UserRole.FieldEngineer && !user.IsLocked && db.CurrentCommuneScope.CommuneIds.Contains(commune))

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Assignee_eligibility
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.50]     LuxMap.Api.Tests.WorkOrderTests.Assignee_eligibility_uses_database_and_has_identical_error_details [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Assignee_eligibility_uses_database_and_has_identical_error_details [200 ms]
  Error Message:
   manager PUT /WO-0336/assignee: expected 409, got 200: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-1512","assigned_at":"2026-09-28T03:14:39.003184Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":true,"allowed_actions":["assign","unassign","edit","cancel"],"faults":[],"work_order_id":"WO-0336","title":"Work order test","commune_id":"COM-660","task_kind":"inspection","segment_id":"SEG-4466","cluster_id":null,"fault_ids":[],"wo_status":"assigned","assigned_to":"USR-1514","priority_score":null,"created_at":"2026-09-28T03:14:38.879956Z","updated_at":"2026-09-28T03:14:39.003184Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Assignee_eligibility_uses_database_and_has_identical_error_details() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 252
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 772 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S08 open definition

Mutation: `src/LuxMap.Shared/Contracts/Enums/FaultStatusSets.cs`: FaultStatus.Detected, FaultStatus.Confirmed, FaultStatus.InProgress → FaultStatus.Detected, FaultStatus.Confirmed, FaultStatus.InProgress, FaultStatus.Rejected

```sh
dotnet test tests/LuxMap.Shared.Tests --filter FullyQualifiedName~FaultEligibilityTests
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.07]     LuxMap.Shared.Tests.FaultEligibilityTests.Literal_eligibility(status: Rejected, inspection: False, repair: False) [FAIL]
  Failed LuxMap.Shared.Tests.FaultEligibilityTests.Literal_eligibility(status: Rejected, inspection: False, repair: False) [< 1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: False
Actual:   True
  Stack Trace:
     at LuxMap.Shared.Tests.FaultEligibilityTests.Literal_eligibility(FaultStatus status, Boolean inspection, Boolean repair) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/WorkOrderRuleTests.cs:line 42
   at InvokeStub_FaultEligibilityTests.Literal_eligibility(Object, Span`1)
   at System.Reflection.MethodBaseInvoker.InvokeWithFewArgs(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)

Failed!  - Failed:     1, Passed:     5, Skipped:     0, Total:     6, Duration: 11 ms - LuxMap.Shared.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S10 done propagates resolved

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`: case "complete": wo.WoStatus = WorkOrderStatus.Done; → case "complete":
                if (wo.TaskKind == TaskKind.Repair) foreach (var f in await db.Set<Fault>().Where(x => ids.Contains(x.FaultId)).ToListAsync(ct)) transitions.Apply(f, FaultStatus.Resolved, now, wo.AssignedTo!);
                wo.WoStatus = WorkOrderStatus.Done;

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Repair_propagation
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.54]     LuxMap.Api.Tests.WorkOrderTests.Repair_propagation_and_inspection_outcomes_and_release [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Repair_propagation_and_inspection_outcomes_and_release [244 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: InProgress
Actual:   Resolved
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Repair_propagation_and_inspection_outcomes_and_release() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 277
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 826 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S11 split SaveChanges

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`:         Record(wo, AuditAction.Created, null, Snapshot(wo, ids), now, request.Note); →         await db.SaveChangesAsync(ct);
        Record(wo, AuditAction.Created, null, Snapshot(wo, ids), now, request.Note);

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Patch_null_noop
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.42]     LuxMap.Api.Tests.WorkOrderTests.Patch_null_noop_audit_content_and_reassignment [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Patch_null_noop_audit_content_and_reassignment [95 ms]
  Error Message:
   manager POST : expected 201, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"e9419161-79b5-4463-bfc8-ab51876934c8"}}}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Create(String kind, String[] faults, String assigned) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 128
   at LuxMap.Api.Tests.WorkOrderTests.Patch_null_noop_audit_content_and_reassignment() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 303
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 674 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S12 audit required

Mutation: `src/LuxMap.Persistence/Audit/AuditWriteGuard.cs`: if (!systemWrite → if (systemWrite

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Audit_required_on_real
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.46]     LuxMap.Api.Tests.WorkOrderTests.Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit [157 ms]
  Error Message:
   Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.InvalidOperationException)
Actual:   typeof(LuxMap.Shared.Http.LuxMapException)
---- LuxMap.Shared.Http.LuxMapException : This write touches a commune outside your permitted scope.
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.<>c__DisplayClass28_1.<<Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit>b__1>d.MoveNext() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 412
--- End of stack trace from previous location ---
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.WorkOrderTests.Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 407
--- End of stack trace from previous location ---
----- Inner Stack Trace -----
   at LuxMap.Persistence.CommuneWriteGuard.Enforce(ChangeTracker changeTracker, CommuneScope scope) in /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/CommuneWriteGuard.cs:line 69
   at LuxMap.Persistence.LuxMapDbContext.EnforceCommuneWriteScope() in /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/LuxMapDbContext.cs:line 110
   at LuxMap.Persistence.LuxMapDbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken) in /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/LuxMapDbContext.cs:line 91
   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken cancellationToken)
   at LuxMap.Api.Tests.WorkOrderTests.<>c__DisplayClass28_2.<Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit>b__3() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 412
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 726 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S14 purge source scan

Mutation: `src/LuxMap.Modules.WorkOrders/PurgeProbe.txt`: None → None

```sh
dotnet test tests/LuxMap.Persistence.Tests --filter FullyQualifiedName~Production_source_never_enables
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.07]     LuxMap.Persistence.Tests.AuditGuardTests.Production_source_never_enables_the_test_purge_switch [FAIL]
  Failed LuxMap.Persistence.Tests.AuditGuardTests.Production_source_never_enables_the_test_purge_switch [8 ms]
  Error Message:
   Assert.DoesNotContain() Failure: Filter matched in collection
                                                                                                                                    ↓ (pos 12)
Collection: [···, "/Users/nhm809/Documents/LuxMap/luxmap_backend/src/"···, "/Users/nhm809/Documents/LuxMap/luxmap_backend/src/"···, "/Users/nhm809/Documents/LuxMap/luxmap_backend/src/"···, "/Users/nhm809/Documents/LuxMap/luxmap_backend/src/"···, "/Users/nhm809/Documents/LuxMap/luxmap_backend/src/"···, ···]
  Stack Trace:
     at LuxMap.Persistence.Tests.AuditGuardTests.Production_source_never_enables_the_test_purge_switch() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/AuditGuardTests.cs:line 88
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 11 ms - LuxMap.Persistence.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S15 xmin WO

Mutation: `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs`: builder.Property(x => x.Version).IsRowVersion(); → builder.Property(x => x.Version).HasColumnName("xmin").ValueGeneratedOnAddOrUpdate();

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Concurrency_verify_return
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.33]     LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit [52 ms]
  Error Message:
   Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details.
---- System.InvalidCastException : Reading as 'System.Int64' is not supported for fields having DataTypeName 'xid'
  Stack Trace:
     at Npgsql.EntityFrameworkCore.PostgreSQL.Update.Internal.NpgsqlModificationCommandBatch.Consume(RelationalDataReader reader, Boolean async, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.ReaderModificationCommandBatch.ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Update.Internal.BatchExecutor.ExecuteAsync(IEnumerable`1 commandBatches, IRelationalConnection connection, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Storage.RelationalDatabase.SaveChangesAsync(IList`1 entries, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(IList`1 entriesToSave, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.ChangeTracking.Internal.StateManager.SaveChangesAsync(StateManager stateManager, Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)
   at Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy.ExecuteAsync[TState,TResult](TState state, Func`4 operation, Func`4 verifySucceeded, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(Boolean acceptAllChangesOnSuccess, CancellationToken cancellationToken)
   at LuxMap.Api.Tests.WorkOrderTests.<>c__DisplayClass16_0.<<Plant>b__0>d.MoveNext() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 148
--- End of stack trace from previous location ---
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 448
--- End of stack trace from previous location ---
----- Inner Stack Trace -----
   at Npgsql.Internal.AdoSerializerHelpers.<GetTypeInfoForReading>g__ThrowReadingNotSupported|0_0(Type type, PgSerializerOptions options, PgTypeId pgTypeId, Exception inner)
   at Npgsql.Internal.AdoSerializerHelpers.GetTypeInfoForReading(Type type, PgTypeId pgTypeId, PgSerializerOptions options)
   at Npgsql.BackendMessages.FieldDescription.<GetInfoCore>g__GetInfoSlow|51_0(Type type, ColumnInfo& lastColumnInfo)
   at Npgsql.BackendMessages.FieldDescription.GetInfoCore(Type type, ColumnInfo& lastColumnInfo)
   at Npgsql.BackendMessages.FieldDescription.GetInfo(Type type, ColumnInfo& lastColumnInfo)
   at Npgsql.NpgsqlDataReader.<GetInfo>g__Slow|132_0(ColumnInfo& info, PgConverter& converter, Size& bufferRequirement, Boolean& asObject, <>c__DisplayClass132_0&)
   at Npgsql.NpgsqlDataReader.GetFieldValueCore[T](Int32 ordinal)
   at Npgsql.NpgsqlDataReader.GetInt64(Int32 ordinal)
   at lambda_method805(Closure, DbDataReader, Int32)
   at Microsoft.EntityFrameworkCore.RelationalPropertyExtensions.GetReaderFieldValue(IProperty property, RelationalDataReader relationalReader, Int32 ordinal, Boolean detailedErrorsEnabled)
   at Npgsql.EntityFrameworkCore.PostgreSQL.Update.Internal.NpgsqlModificationCommand.PropagateResults(RelationalDataReader relationalReader)
   at Npgsql.EntityFrameworkCore.PostgreSQL.Update.Internal.NpgsqlModificationCommandBatch.Consume(RelationalDataReader reader, Boolean async, CancellationToken cancellationToken)
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 624 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S16 lexicographic ordering

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`: .ThenByDescending(x => x.WorkOrderId.Length) → .ThenByDescending(x => x.WorkOrderId)

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Listing_orders_numeric
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.42]     LuxMap.Api.Tests.WorkOrderTests.Listing_orders_numeric_ids_and_parses_snake_case [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Listing_orders_numeric_ids_and_parses_snake_case [125 ms]
  Error Message:
   Assert.Equal() Failure: Collections differ
                                                          ↓ (pos 0)
Expected: string[]                                       ["WO-10000", "WO-9999"]
Actual:   IEnumerableSelectIterator<JsonElement, string> ["WO-9999", "WO-10000"]
                                                          ↑ (pos 0)
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Listing_orders_numeric_ids_and_parses_snake_case() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 383
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 697 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S17 first fault segment

Mutation: `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`: faults.Where(x => x.SegmentId is not null).OrderByDescending(x => x.PriorityScore) → faults.Where(x => x.SegmentId is not null).OrderBy(x => x.CreatedAt)

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Derived_fields
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.48]     LuxMap.Api.Tests.WorkOrderTests.Derived_fields_use_highest_priority_and_live_max [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Derived_fields_use_highest_priority_and_live_max [193 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
                  ↓ (pos 7)
Expected: "SEG-4484"
Actual:   "SEG-4483"
                  ↑ (pos 7)
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Derived_fields_use_highest_priority_and_live_max() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 395
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 768 ms - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S19 mock kind

Mutation: `mocks/mock-work-order-kinds.csv`: WO-0001,inspection → WO-0001,repair

```sh
dotnet test tests/LuxMap.Shared.Tests --filter FullyQualifiedName~MockWorkOrderKindsTests
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.07]     LuxMap.Shared.Tests.MockWorkOrderKindsTests.Every_mock_order_has_a_kind_and_eligible_faults [FAIL]
  Failed LuxMap.Shared.Tests.MockWorkOrderKindsTests.Every_mock_order_has_a_kind_and_eligible_faults [4 ms]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
  Stack Trace:
     at LuxMap.Shared.Tests.MockWorkOrderKindsTests.Every_mock_order_has_a_kind_and_eligible_faults() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/WorkOrderRuleTests.cs:line 68
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 7 ms - LuxMap.Shared.Tests.dll (net10.0)

Exit code: 1
```

Đã khôi phục file sau sabotage (finally).

### Sabotage S09 partial index (luxmap_test)

```sql
SELECT pg_get_indexdef(indexrelid) || ';' FROM pg_index WHERE indexrelid='ux_work_order_fault_fault_id_active'::regclass;
-- Definition captured before mutation:
CREATE UNIQUE INDEX ux_work_order_fault_fault_id_active ON public.work_order_fault USING btree (fault_id) WHERE (released_at IS NULL);

DROP INDEX ux_work_order_fault_fault_id_active;
```

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Partial_unique_index
```

```text
DROP INDEX
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed LuxMap.Api.Tests.WorkOrderTests.Partial_unique_index_rejects_active_duplicates_but_preserves_released_history [204 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(Microsoft.EntityFrameworkCore.DbUpdateException)
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.<>c__DisplayClass29_0.<<Partial_unique_index_rejects_active_duplicates_but_preserves_released_history>b__0>d.MoveNext() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 436
--- End of stack trace from previous location ---
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.WorkOrderTests.Partial_unique_index_rejects_active_duplicates_but_preserves_released_history() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 432
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 823 ms - LuxMap.Api.Tests.dll (net10.0)
[xUnit.net 00:00:01.57]     LuxMap.Api.Tests.WorkOrderTests.Partial_unique_index_rejects_active_duplicates_but_preserves_released_history [FAIL]

Exit code: 1
```

Khôi phục trong finally bằng definition đã đọc:

```text
CREATE INDEX

```

### Sabotage S13 audit trigger (luxmap_test)

```sql
SELECT pg_get_triggerdef(oid) || ';' FROM pg_trigger WHERE tgrelid='audit_event'::regclass AND NOT tgisinternal;
-- Definition captured before mutation:
CREATE TRIGGER audit_event_append_only_rows BEFORE DELETE OR UPDATE ON public.audit_event FOR EACH ROW EXECUTE FUNCTION luxmap_audit_event_append_only();
CREATE TRIGGER audit_event_append_only_truncate BEFORE TRUNCATE ON public.audit_event FOR EACH STATEMENT EXECUTE FUNCTION luxmap_audit_event_append_only();

DROP TRIGGER audit_event_append_only_rows ON audit_event; DROP TRIGGER audit_event_append_only_truncate ON audit_event;
```

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Raw_sql_cannot_mutate_audit
```

```text
DROP TRIGGER
DROP TRIGGER
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(operation: "UPDATE") [34 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(Npgsql.PostgresException)
  Stack Trace:
     at LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(String operation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AuditTrailTests.cs:line 178
--- End of stack trace from previous location ---
  Failed LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(operation: "DELETE") [2 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(Npgsql.PostgresException)
  Stack Trace:
     at LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(String operation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AuditTrailTests.cs:line 178
--- End of stack trace from previous location ---
  Failed LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(operation: "TRUNCATE") [2 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(Npgsql.PostgresException)
  Stack Trace:
     at LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(String operation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AuditTrailTests.cs:line 178
--- End of stack trace from previous location ---

Failed!  - Failed:     3, Passed:     0, Skipped:     0, Total:     3, Duration: 52 ms - LuxMap.Api.Tests.dll (net10.0)
[xUnit.net 00:00:00.82]     LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(operation: "UPDATE") [FAIL]
[xUnit.net 00:00:00.82]     LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(operation: "DELETE") [FAIL]
[xUnit.net 00:00:00.83]     LuxMap.Api.Tests.AuditTrailTests.Raw_sql_cannot_mutate_audit(operation: "TRUNCATE") [FAIL]

Exit code: 1
```

Khôi phục trong finally bằng definition đã đọc:

```text
CREATE TRIGGER
CREATE TRIGGER

```

### Sabotage S18 schedule CHECK (luxmap_test)

```sql
SELECT 'ALTER TABLE work_order ADD CONSTRAINT ck_work_order_schedule_before_due ' || pg_get_constraintdef(oid) || ';' FROM pg_constraint WHERE conname='ck_work_order_schedule_before_due';
-- Definition captured before mutation:
ALTER TABLE work_order ADD CONSTRAINT ck_work_order_schedule_before_due CHECK (((scheduled_date IS NULL) OR (due_date IS NULL) OR (scheduled_date <= due_date)));

ALTER TABLE work_order DROP CONSTRAINT ck_work_order_schedule_before_due;
```

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Schedule_api_database
```

```text
ALTER TABLE
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed LuxMap.Api.Tests.WorkOrderTests.Schedule_api_database_and_inclusive_filter [195 ms]
  Error Message:
   Assert.Throws() Failure: No exception was thrown
Expected: typeof(Npgsql.PostgresException)
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.<>c__DisplayClass25_0.<<Schedule_api_database_and_inclusive_filter>b__0>d.MoveNext() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 361
--- End of stack trace from previous location ---
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.AssetImportFixture.QueryAsync[T](Func`2 query) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/AssetImportFixture.cs:line 218
   at LuxMap.Api.Tests.WorkOrderTests.Schedule_api_database_and_inclusive_filter() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 359
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 760 ms - LuxMap.Api.Tests.dll (net10.0)
[xUnit.net 00:00:01.51]     LuxMap.Api.Tests.WorkOrderTests.Schedule_api_database_and_inclusive_filter [FAIL]

Exit code: 1
```

Khôi phục trong finally bằng definition đã đọc:

```text
ALTER TABLE

```

### S15b — bỏ concurrency, GIỮ mapping xid

S15 đầu đỏ vì provider đọc sai kiểu, không dùng làm bằng chứng race. Lượt này dùng `HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate()` thay IsRowVersion.

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Concurrency_verify_return
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
  Failed LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit [212 ms]
  Error Message:
   Assert.Equal() Failure: Collections differ
                                                     ↓ (pos 1)
Expected: int[]                                [200, 409]
Actual:   ImplicitlyStableOrderedIterator<int> [200, 500]
                                                     ↑ (pos 1)
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 456
   at LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit() in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 459
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 772 ms - LuxMap.Api.Tests.dll (net10.0)
[xUnit.net 00:00:01.48]     LuxMap.Api.Tests.WorkOrderTests.Concurrency_verify_return_has_one_success_one_conflict_and_one_audit [FAIL]

Exit code: 1
```

Đã khôi phục IsRowVersion trong finally.

## Kết quả thực thi cuối — 28/09/2026

Đã triển khai WorkOrder/WorkOrderFault, composite FK Restrict, alternate key fault, xmin, index active partial unique; capability và hai bảng literal; query filter gộp commune/assignee tham chiếu DbContext; 11 operation HTTP, null/omission, state machine, FaultTransitions, audit một dòng/thao tác và rollback race. Seed dùng study_site và username; không đổi mock-work-orders.json. D-R13 XML doc đã sửa.

Test tích hợp gom trong WorkOrderTests (AssetDatabaseCollection), dùng tài khoản/xã throwaway. Teardown purge audit trong transaction riêng trước khi xoá dữ liệu. Test ordering dùng ID hai bên ngưỡng trong cùng transaction và không lùi sequence. Test race dùng barrier ở SaveChanges để buộc hai request đọc cùng phiên bản; test hai POST cạnh tranh fault cũng có barrier. Quyền capability được xét trước lookup: caller không có capability vẫn 403; body sai 400; request hợp lệ có quyền của FE khác luôn 404, không lộ WO.

§13: S01 capability; S02 state literal; S03 HTTP transition; S04 actor; S05 filter; S06 scope; S07 assignee; S08 eligibility; S09 partial unique/race; S10 propagation; S11 audit atomicity; S12 audit required; S13 append-only triggers; S14 purge scan; S15b concurrency (thay S15 sai mapping); S16 ordered IDs; S17 derived fields; S18 schedule CHECK; S19 mock kinds. Tất cả có output đỏ ở trên và đã khôi phục. BannedBulkWriteApiTests không có cột phá hoại, đã mở rộng assembly/entity và xanh trong full suite.

Migration đầu tiên EF sinh xmin vật lý và index tên sai; đã đọc, ghi bằng chứng rồi sửa trước apply. Migration cuối giữ mapping xmin trong model nhưng không tạo/xoá cột hệ thống. Không DropIndex ix_fault_*; thêm index commune_id tường minh; Down đối xứng. Vòng migrate bên dưới chỉ trên luxmap_test.

Các lệnh .NET dùng ConnectionStrings__LuxMap trỏ luxmap_test theo prompt, DOTNET_USE_POLLING_FILE_WATCHER=1 và NUGET_HTTP_CACHE_PATH=/private/tmp/be23-nuget-cache. Không cấp Cors cho test. Cache riêng khắc phục cảnh báo quyền ghi NuGet; không tắt audit dependency.

### Apply migration

Lệnh/kiểm tra: `dotnet ef database update --project src/LuxMap.Persistence --startup-project src/LuxMap.Api`

```text
Build started...
Build succeeded.
Done.
```

### Rollback migration

Lệnh/kiểm tra: `dotnet ef database update AddAuditEvent --project src/LuxMap.Persistence --startup-project src/LuxMap.Api`

```text
Build started...
Build succeeded.
Done.
```

### Reapply migration

Lệnh/kiểm tra: `dotnet ef database update --project src/LuxMap.Persistence --startup-project src/LuxMap.Api`

```text
Build started...
Build succeeded.
Done.
```

### Seed test

Lệnh/kiểm tra: `python3 scripts/seed_mock_set.py --database luxmap_test --apply`

```text
 setval 
--------
      3
(1 row)

 setval 
--------
      3
(1 row)

 setval 
--------
    103
(1 row)

 setval 
--------
    103
(1 row)

 setval 
--------
     28
(1 row)

 setval 
--------
      1
(1 row)
```

### Đối chiếu seed bằng SQL

Lệnh/kiểm tra: `psql luxmap_test: fault_status, work_order/link aggregate, audit count`

```text
 fault_status | count 
--------------+-------
 confirmed    |     4
 detected     |    21
 in_progress  |     3
(3 rows)

 work_order_id | task_kind  |  wo_status  | segment_id | cluster_id | faults | priority 
---------------+------------+-------------+------------+------------+--------+----------
 WO-0001       | inspection | assigned    | SEG-003    | CLS-001    |      7 |     92.9
 WO-0002       | repair     | in_progress | SEG-001    |            |      3 |     96.3
 WO-0003       | inspection | open        | SEG-002    |            |      1 |     72.5
(3 rows)

 audit_count 
-------------
           0
(1 row)
```

### HTTP thật sau seed

Lệnh/kiểm tra: `GET list/detail với engineer và crew; GET WO-0003 với crew; xoá đúng refresh token vừa phát hành`

```text
engineer GET /work-orders -> 200 IDs ['WO-0003', 'WO-0001', 'WO-0002']
WO-0003 ('inspection', 'SEG-002', None, 72.5, 1) allowed_actions ['assign', 'unassign', 'edit', 'cancel']
WO-0001 ('inspection', 'SEG-003', 'CLS-001', 92.9, 7) allowed_actions ['assign', 'unassign', 'edit', 'cancel']
WO-0002 ('repair', 'SEG-001', None, 96.3, 3) allowed_actions ['assign', 'unassign', 'edit', 'cancel']
crew GET /work-orders -> 200 IDs ['WO-0001', 'WO-0002']
WO-0001 ('inspection', 'SEG-003', 'CLS-001', 92.9, 7) allowed_actions ['start']
WO-0002 ('repair', 'SEG-001', None, 96.3, 3) allowed_actions ['complete']
crew GET unassigned WO-0003 -> 404 WORK_ORDER_NOT_FOUND
Smoke token teardown: DELETE 1
Smoke token teardown: DELETE 1
```

### Seed guard

Lệnh/kiểm tra: `Tạo audit probe → seed exit 3, WO còn nguyên → purge riêng probe trong transaction SET LOCAL`

```text
INSERT 0 1

COMMAND: python3 scripts/seed_mock_set.py --database luxmap_test --apply
ERROR:  Work order audit exists; re-seeding would reuse its entity IDs.
CONTEXT:  PL/pgSQL function inline_code_block line 3 at RAISE

Exit code: 3
 work_orders_preserved 
-----------------------
                     3
(1 row)


BEGIN
SET
DELETE 1
COMMIT

 audit_after_teardown 
----------------------
                    0
(1 row)
```

### Build cuối sau khôi phục

Lệnh/kiểm tra: `dotnet build`

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.67
```

### Full suite cuối sau khôi phục

Lệnh/kiểm tra: `dotnet test --no-build`

```text
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   157, Skipped:     0, Total:   157, Duration: 62 ms - LuxMap.Shared.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:    36, Skipped:     0, Total:    36, Duration: 339 ms - LuxMap.Persistence.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 864 ms - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:   461, Skipped:     0, Total:   461, Duration: 14 s - LuxMap.Api.Tests.dll (net10.0)
```

### Export OpenAPI

Lệnh/kiểm tra: `Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1`

```text
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/luxmap_backend/docs/openapi/luxmap-v1.json
```

### Consolidate OpenAPI

Lệnh/kiểm tra: `python3 docs/openapi/tools/gen_consolidated_spec.py`

```text
wrote docs/openapi/luxmap-v1.5.json: paths=43 implemented_ops=46 not_implemented_ops=10 schemas=116
```

### Lint OpenAPI

Lệnh/kiểm tra: `npx --yes @redocly/cli@2.0.0 lint docs/openapi/luxmap-v1.5.json`

```text
npm warn deprecated glob@11.1.0: Old versions of glob are not supported, and contain widely publicized security vulnerabilities, which have been fixed in the current version. Please update. Support for old versions may be purchased (at exorbitant rates) by contacting i@izs.me
No configurations were provided -- using built in recommended configuration by default.

validating docs/openapi/luxmap-v1.5.json...
[1] docs/openapi/luxmap-v1.5.json:3:3 at #/info

Info object should contain `license` field.

1 | {
2 |   "openapi": "3.0.4",
3 |   "info": {
  |   ^^^^^^
4 |     "title": "LuxMap API",
5 |     "description": "Contract v1.7 cộng các drift đã hiện thực (BE-23: WO-1…WO-11, nền tạm tới FW). Sinh bằng docs/openapi/tools/gen_cons...<256 chars>

Warning was generated by the info-license rule.


[2] docs/openapi/luxmap-v1.5.json:8453:14 at #/servers/0/url

Server `url` should not point to example.com or localhost.

8451 | "servers": [
8452 |   {
8453 |     "url": "http://localhost:5141",
     |            ^^^^^^^^^^^^^^^^^^^^^^^
8454 |     "description": "Development — launchSettings.json profile 'http'"
8455 |   },

Warning was generated by the no-server-example.com rule.


[3] docs/openapi/luxmap-v1.5.json:8457:14 at #/servers/1/url

Server `url` should not point to example.com or localhost.

8455 | },
8456 | {
8457 |   "url": "https://localhost:7252",
     |          ^^^^^^^^^^^^^^^^^^^^^^^^
8458 |   "description": "Development — launchSettings.json profile 'https'"
8459 | }

Warning was generated by the no-server-example.com rule.


docs/openapi/luxmap-v1.5.json: validated in 54ms

Woohoo! Your API description is valid. 🎉
You have 3 warnings.

npm notice
npm notice New major version of npm available! 11.17.0 -> 12.1.0
npm notice Changelog: https://github.com/npm/cli/releases/tag/v12.1.0
npm notice To update run: npm install -g npm@12.1.0
npm notice
```

### Schema cuối

Lệnh/kiểm tra: `psql luxmap_test: \d work_order, \d work_order_fault, index fault, current_database và row counts`

```text
                                                         Table "public.work_order"
     Column     |           Type           | Collation | Nullable |                                 Default                                 
----------------+--------------------------+-----------+----------+-------------------------------------------------------------------------
 work_order_id  | text                     |           | not null | luxmap_format_id('WO'::text, nextval('work_order_id_seq'::regclass), 4)
 commune_id     | text                     |           | not null | 
 task_kind      | text                     |           | not null | 
 title          | text                     |           | not null | 
 wo_status      | text                     |           | not null | 
 segment_id     | text                     |           |          | 
 cluster_id     | text                     |           |          | 
 assigned_to    | text                     |           |          | 
 assigned_at    | timestamp with time zone |           |          | 
 created_by     | text                     |           | not null | 
 due_date       | date                     |           |          | 
 scheduled_date | date                     |           |          | 
 note           | text                     |           |          | 
 review_note    | text                     |           |          | 
 report_note    | text                     |           |          | 
 started_at     | timestamp with time zone |           |          | 
 completed_at   | timestamp with time zone |           |          | 
 closed_at      | timestamp with time zone |           |          | 
 created_at     | timestamp with time zone |           | not null | now()
 updated_at     | timestamp with time zone |           | not null | now()
Indexes:
    "pk_work_order" PRIMARY KEY, btree (work_order_id)
    "ak_work_order_work_order_id_commune_id" UNIQUE CONSTRAINT, btree (work_order_id, commune_id)
    "ix_work_order_assigned_to" btree (assigned_to)
    "ix_work_order_cluster_id" btree (cluster_id)
    "ix_work_order_commune_id" btree (commune_id)
    "ix_work_order_created_by" btree (created_by)
    "ix_work_order_segment_id" btree (segment_id)
    "ix_work_order_wo_status" btree (wo_status)
Check constraints:
    "ck_work_order_assigned_at_matches" CHECK ((assigned_to IS NULL) = (assigned_at IS NULL))
    "ck_work_order_assignee_matches_status" CHECK (wo_status = 'open'::text AND assigned_to IS NULL OR (wo_status = ANY (ARRAY['assigned'::text, 'in_progress'::text, 'done'::text, 'verified'::text])) AND assigned_to IS NOT NULL OR wo_status = 'cancelled'::text)
    "ck_work_order_closed" CHECK ((wo_status = ANY (ARRAY['verified'::text, 'cancelled'::text])) = (closed_at IS NOT NULL))
    "ck_work_order_completed" CHECK ((wo_status <> ALL (ARRAY['done'::text, 'verified'::text])) OR completed_at IS NOT NULL AND report_note IS NOT NULL)
    "ck_work_order_schedule_before_due" CHECK (scheduled_date IS NULL OR due_date IS NULL OR scheduled_date <= due_date)
    "ck_work_order_started" CHECK ((wo_status <> ALL (ARRAY['in_progress'::text, 'done'::text, 'verified'::text])) OR started_at IS NOT NULL)
    "ck_work_order_task_kind" CHECK (task_kind = ANY (ARRAY['inspection'::text, 'repair'::text]))
    "ck_work_order_title_not_blank" CHECK (btrim(title) <> ''::text)
    "ck_work_order_wo_status" CHECK (wo_status = ANY (ARRAY['open'::text, 'assigned'::text, 'in_progress'::text, 'done'::text, 'verified'::text, 'cancelled'::text]))
Foreign-key constraints:
    "fk_work_order_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_work_order_app_user_assigned_to" FOREIGN KEY (assigned_to) REFERENCES app_user(user_id) ON DELETE RESTRICT
    "fk_work_order_app_user_created_by" FOREIGN KEY (created_by) REFERENCES app_user(user_id) ON DELETE RESTRICT
    "fk_work_order_fault_cluster_cluster_id" FOREIGN KEY (cluster_id) REFERENCES fault_cluster(cluster_id) ON DELETE RESTRICT
    "fk_work_order_road_segment_segment_id" FOREIGN KEY (segment_id) REFERENCES road_segment(segment_id) ON DELETE RESTRICT
Referenced by:
    TABLE "work_order_fault" CONSTRAINT "fk_work_order_fault_work_order_work_order_id_commune_id" FOREIGN KEY (work_order_id, commune_id) REFERENCES work_order(work_order_id, commune_id) ON DELETE RESTRICT

                        Table "public.work_order_fault"
       Column       |           Type           | Collation | Nullable | Default 
--------------------+--------------------------+-----------+----------+---------
 work_order_id      | text                     |           | not null | 
 fault_id           | text                     |           | not null | 
 commune_id         | text                     |           | not null | 
 linked_at          | timestamp with time zone |           | not null | now()
 released_at        | timestamp with time zone |           |          | 
 inspection_outcome | text                     |           |          | 
Indexes:
    "pk_work_order_fault" PRIMARY KEY, btree (work_order_id, fault_id)
    "ix_work_order_fault_commune_id" btree (commune_id)
    "ix_work_order_fault_fault_id" btree (fault_id)
    "ix_work_order_fault_fault_id_commune_id" btree (fault_id, commune_id)
    "ix_work_order_fault_work_order_id_commune_id" btree (work_order_id, commune_id)
    "ux_work_order_fault_fault_id_active" UNIQUE, btree (fault_id) WHERE released_at IS NULL
Check constraints:
    "ck_work_order_fault_inspection_outcome" CHECK (inspection_outcome IS NULL OR (inspection_outcome = ANY (ARRAY['fault_present'::text, 'fault_absent'::text, 'inconclusive'::text])))
    "ck_work_order_fault_release_after_link" CHECK (released_at IS NULL OR released_at >= linked_at)
Foreign-key constraints:
    "fk_work_order_fault_administrative_unit_commune_id" FOREIGN KEY (commune_id) REFERENCES administrative_unit(commune_id) ON DELETE RESTRICT
    "fk_work_order_fault_fault_fault_id_commune_id" FOREIGN KEY (fault_id, commune_id) REFERENCES fault(fault_id, commune_id) ON DELETE RESTRICT
    "fk_work_order_fault_work_order_work_order_id_commune_id" FOREIGN KEY (work_order_id, commune_id) REFERENCES work_order(work_order_id, commune_id) ON DELETE RESTRICT

          indexname           
------------------------------
 ak_fault_fault_id_commune_id
 ix_fault_cluster_id
 ix_fault_commune_id
 ix_fault_confirmed_by
 ix_fault_fault_status
 ix_fault_fixture_id
 ix_fault_pole_id
 ix_fault_priority_score
 ix_fault_reported_by
 ix_fault_resolved_by
 ix_fault_segment_id
 pk_fault
 ux_fault_client_op_id
(13 rows)

 current_database | audit_rows | work_orders | fault_links 
------------------+------------+-------------+-------------
 luxmap_test      |          0 |           3 |          11
(1 row)
```

OpenAPI: sửa generator vì placeholder not_implemented của work-order đè endpoint thật; giữ evidence BE-24 là chưa triển khai. JSON chỉ sinh bằng tool. Lint hợp lệ, 0 lỗi và 3 warning (license thiếu, hai URL localhost); không tự đặt license hoặc URL triển khai. Build 0 warning là kết quả riêng của dotnet build.

Tài liệu: đăng ký WO-1…WO-11 trong contract-drift, cập nhật authorization-guide, README, bẫy thật trong CLAUDE.md (AGENTS.md vẫn symlink), lệch task list §12 vào tracking. Không sửa Contract, CSV task list, mock JSON, không làm BE-24/BE-27/SLA/ExternalUnit/gom địa lý/survey. Chưa gửi thông báo WP5/WP6; việc xác nhận FW vẫn thuộc người phụ trách.

### Kiểm role DB và diff cuối

`psql -X -h localhost -p 5433 -U luxmap -d luxmap_test -c '\du'`:

```text
                             List of roles
 Role name |                         Attributes                         
-----------+------------------------------------------------------------
 luxmap    | Superuser, Create role, Create DB, Replication, Bypass RLS
```

Role test là superuser; các test trigger append-only vẫn thực sự bị từ chối khi không có GUC purge. Không thay đổi role. `git diff --check` exit 0; diff Contract, task CSV và mock-work-orders.json rỗng. AGENTS.md vẫn symlink tới CLAUDE.md.

## D-item cuối

- **D-WO-P2-01 — ĐÃ GIẢI:** Claude 28/09/2026, theo lệnh “tiến hành làm tiếp” của Mỹ: seed_key study_site là danh tính xã seed; COM-070 ở dev và COM-001 ở test khác vì sequence, không mâu thuẫn thiết kế. USR-002/003/004 ở test lần lượt superior/manager/field_engineer, không khoá, cùng xã seed. Không dùng literal commune trong code/test/seed mới.
- Không tự chốt thêm quyết định kiến trúc. §8 liệt kê bảy mã lỗi dù có chỗ gọi là “sáu”; đã theo danh sách bảy mã cụ thể, ghi rõ trong drift. Các thay đổi API theo uỷ quyền vẫn là nền tạm tới FW kế tiếp.

## ĐỀ XUẤT CHIA COMMIT

Chỉ đề xuất, **chưa commit, chưa push**. Không đưa các file untracked có sẵn ngoài nhiệm vụ vào commit.

### `feat(work-orders): implement scoped inspection and repair workflow`

- `mocks/mock-work-order-kinds.csv`
- `scripts/seed_mock_set.py`
- `src/LuxMap.Api/Authorization/AuthorizationSetup.cs`
- `src/LuxMap.Api/Authorization/CurrentActorAccessor.cs`
- `src/LuxMap.Api/OpenApi/SwaggerSetup.cs`
- `src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs`
- `src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs`
- `src/LuxMap.Modules.Faults/Entities/Fault.cs`
- `src/LuxMap.Modules.Faults/FaultTransitions.cs`
- `src/LuxMap.Modules.Faults/FaultsModule.cs`
- `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs`
- `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs`
- `src/LuxMap.Modules.WorkOrders/LuxMap.Modules.WorkOrders.csproj`
- `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderRules.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrdersModule.cs`
- `src/LuxMap.Persistence/Conventions/CommuneScopeBuilderExtensions.cs`
- `src/LuxMap.Persistence/LuxMapDbContext.cs`
- `src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.Designer.cs`
- `src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs`
- `src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs`
- `src/LuxMap.Shared/Authorization/IAssigneeScoped.cs`
- `src/LuxMap.Shared/Authorization/LuxMapPolicies.cs`
- `src/LuxMap.Shared/Http/OptionalJson.cs`
- `tests/LuxMap.Api.Tests/RoleCapabilityMatrixTests.cs`
- `tests/LuxMap.Api.Tests/WorkOrderTests.cs` — gồm bản sửa review vòng 1: 4 ca POST assigned_to không đủ điều kiện, so details PUT, canh không tạo WO/audit.
- `tests/LuxMap.Persistence.Tests/AssigneeFilterTests.cs`
- `tests/LuxMap.Persistence.Tests/BannedBulkWriteApiTests.cs`
- `tests/LuxMap.Persistence.Tests/LuxMap.Persistence.Tests.csproj`
- `tests/LuxMap.Shared.Tests/CapabilityMatrixTests.cs`
- `tests/LuxMap.Shared.Tests/WorkOrderRuleTests.cs`

### `docs(work-orders): publish provisional contract and execution evidence`

- `.ai/results/BE-23-p2-work-orders.md`
- `CLAUDE.md`
- `README.md`
- `docs/authorization-guide.md`
- `docs/contract-drift.md`
- `docs/openapi/luxmap-v1.5.json`
- `docs/openapi/luxmap-v1.json`
- `docs/openapi/tools/gen_consolidated_spec.py`
- `tracking.html`

## Sửa theo review Claude vòng 1

28/09/2026 — chỉ sửa phát hiện 1 của `.ai/reviews/BE-23-p2-by-claude.md`. Không xử lý các ý kiến ngoài phát hiện.

Thêm theory `Create_rejects_ineligible_assignee_without_order_or_audit` với 4 InlineData: superior, locked, other_commune, missing. Mỗi ca dùng tài khoản và xã throwaway của fixture hiện có; WO đối chứng được Plant qua backdoor (không audit). PUT và POST phải cùng 409 ASSIGNEE_NOT_ELIGIBLE, toàn bộ details giống hệt. Hai request dùng cùng correlation ID vì middleware thêm trường này vào details. Helper Send kiểm audit theo correlation; truy vấn IgnoreQueryFilters sau POST khẳng định chỉ còn WO đối chứng và không có audit nào trong hai xã test. Teardown hiện có purge audit riêng trước xoá dữ liệu, không đụng tài khoản seed.

Thay đổi mã chỉ ở `tests/LuxMap.Api.Tests/WorkOrderTests.cs`. Đề xuất commit feature phía trên đã cập nhật đường dẫn này với nội dung sửa vòng 1; report/tracking thuộc commit docs đã liệt kê. Không commit, không push.

Sabotage đúng dòng `if (request.AssignedTo is not null) await RequireAssignee(request.AssignedTo, commune, ct);` trong Create: xoá tạm, chạy 4 ca mới, khôi phục trong finally. Cả 4 đỏ: superior/locked/other_commune nhận 201, missing nhận 500, đều khác 409. Đã so byte của WorkOrderService.cs sau khôi phục và so SHA-256 toàn bộ source/config dưới src/ với snapshot trước vòng sửa (loại trừ bin/obj/logs): giống hệt.

Các lệnh dùng ConnectionStrings__LuxMap trỏ luxmap_test đúng prompt; DOTNET_USE_POLLING_FILE_WATCHER=1, NUGET_HTTP_CACHE_PATH=/private/tmp/be23-nuget-cache; không đặt Cors cho test. Không migrate hoặc seed lại trong vòng sửa này.

### Test mới trước sabotage

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Create_rejects_ineligible_assignee_without_order_or_audit
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
```

### Sabotage — output đỏ thật

```sh
dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~Create_rejects_ineligible_assignee_without_order_or_audit
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:01.51]     LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "other_commune") [FAIL]
[xUnit.net 00:00:01.87]     LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "missing") [FAIL]
[xUnit.net 00:00:02.23]     LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "superior") [FAIL]
[xUnit.net 00:00:02.58]     LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "locked") [FAIL]
  Failed LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "other_commune") [173 ms]
  Error Message:
   manager POST : expected 409, got 201: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-2113","assigned_at":"2026-09-28T03:30:25.146666Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":false,"allowed_actions":["assign","unassign","edit","cancel"],"faults":[],"work_order_id":"WO-0434","title":"Rejected assignment","commune_id":"COM-939","task_kind":"inspection","segment_id":"SEG-408","cluster_id":null,"fault_ids":[],"wo_status":"assigned","assigned_to":"USR-2117","priority_score":null,"created_at":"2026-09-28T03:30:25.146666Z","updated_at":"2026-09-28T03:30:25.146666Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(String reason) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 298
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.


  Failed LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "missing") [27 ms]
  Error Message:
   manager POST : expected 409, got 500: {"error":{"code":"INTERNAL_ERROR","message":"An unexpected error occurred. Send the correlation id to an administrator to trace it.","details":{"correlation_id":"a068cf14-f241-49ca-a96d-eb453584df9d"}}}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(String reason) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 298
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.


  Failed LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "superior") [17 ms]
  Error Message:
   manager POST : expected 409, got 201: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-2125","assigned_at":"2026-09-28T03:30:25.922542Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":false,"allowed_actions":["assign","unassign","edit","cancel"],"faults":[],"work_order_id":"WO-0438","title":"Rejected assignment","commune_id":"COM-943","task_kind":"inspection","segment_id":"SEG-413","cluster_id":null,"fault_ids":[],"wo_status":"assigned","assigned_to":"USR-2124","priority_score":null,"created_at":"2026-09-28T03:30:25.922542Z","updated_at":"2026-09-28T03:30:25.922542Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(String reason) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 298
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.


  Failed LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(reason: "locked") [14 ms]
  Error Message:
   manager POST : expected 409, got 201: {"note":null,"review_note":null,"report_note":null,"created_by":"USR-2128","assigned_at":"2026-09-28T03:30:26.276255Z","started_at":null,"completed_at":null,"closed_at":null,"assignee_eligible":false,"allowed_actions":["assign","unassign","edit","cancel"],"faults":[],"work_order_id":"WO-0440","title":"Rejected assignment","commune_id":"COM-944","task_kind":"inspection","segment_id":"SEG-415","cluster_id":null,"fault_ids":[],"wo_status":"assigned","assigned_to":"USR-2129","priority_score":null,"created_at":"2026-09-28T03:30:26.276255Z","updated_at":"2026-09-28T03:30:26.276255Z","due_date":null,"scheduled_date":null}
  Stack Trace:
     at LuxMap.Api.Tests.WorkOrderTests.Send(String who, String method, String path, Object body, Int32 expected, String error, Int32 auditExpected, String correlation) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 107
   at LuxMap.Api.Tests.WorkOrderTests.Create_rejects_ineligible_assignee_without_order_or_audit(String reason) in /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/WorkOrderTests.cs:line 298
--- End of stack trace from previous location ---
  Standard Output Messages:
 Creating isolated work-order accounts and communes.



Failed!  - Failed:     4, Passed:     0, Skipped:     0, Total:     4, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)

Exit code: 1
```

### Build sau khôi phục

```sh
dotnet build
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.17
```

### Full test sau khôi phục

```sh
dotnet test --no-build
```

```text
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
Test run for /Users/nhm809/Documents/LuxMap/luxmap_backend/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   157, Skipped:     0, Total:   157, Duration: 82 ms - LuxMap.Shared.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:    36, Skipped:     0, Total:    36, Duration: 394 ms - LuxMap.Persistence.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 1 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)

Passed!  - Failed:     0, Passed:   465, Skipped:     0, Total:   465, Duration: 15 s - LuxMap.Api.Tests.dll (net10.0)
```

✅ Build 0 warning / 0 error; 676 test xanh (157 Shared + 36 Persistence + 18 Storage + 465 API), 0 failed, 0 skipped. Sabotage 4/4 đỏ, đã khôi phục. `git diff --check` exit 0. Không có thay đổi src/ so với trước vòng sửa. Dừng, chờ Claude review.
