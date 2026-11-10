# SURVEY-RECURRING — phase 2 implementation

Ngày: 09/11/2026. Agent: Codex. Worktree: `/Users/nhm809/Documents/LuxMap/wt-survey-recurring`.

## Phạm vi đã thực hiện

- `SurveyScheduleCalendar`: hàm thuần dùng ngày địa phương, tuần T2–CN, mốc T5 17:00 duy nhất; starts_on_week,
  first_occurrence_date và cửa sổ sinh phiếu từ hôm nay. Dùng múi giờ của WorkOrderAgendaOptions, API dùng TimeProvider.
- Hai entity/configuration mới `SurveyRecurringSchedule` / `SurveyRecurringOccurrence`: scope xã, FK thật, lịch audited,
  prefix SSCH và sequence theo HasPrefixedId; unique segment/day, CHECK weekday/week/note, receipt PK.
  FK receipt→schedule ghép commune_id để cascade không xuyên scope; receipt→work_order Restrict.
- GET/POST/PATCH/DELETE `/api/v1/survey-schedules/recurring`, phân trang/thứ tự ID tối thiểu N chữ số,
  field validation/server-owned, kiểm xã/tuyến/assignee, bật lại tính lại tuần bắt đầu, một audit mỗi thay đổi.
  GET lấy tên tuyến/người theo lô, không truy vấn thêm cho từng item.
- Dùng ReadWorkOrders / ManageWorkOrders trên từng action; không capability mới, không đổi ma trận.
  ManageWorkOrders giữ **chỉ manager**, admin/superior/field_engineer bị 403 khi ghi.
- Job `survey_recurring_generate` đăng ký IScheduledJob, chu kỳ 15 phút. Scan read-only IgnoreQueryFilters,
  mỗi occurrence ghi qua Open(commune), một transaction nhỏ, receipt/order/segment/audit/notice một SaveChanges.
  Hai lớp chống trùng: receipt PK (bắt đúng constraint và rollback) và survey chưa cancelled cùng tuyến/cùng tuần.
  Huỷ phiếu tự sinh không xoá receipt; xoá lịch cascade receipt và giữ phiếu.
- Tách phần ghi CreateSurvey thành `WorkOrderService.StageSurvey` nhận creator tường minh, dùng chung với API;
  request vẫn tự save rồi Detail, batch giữ transaction hiện có. Job giữ created_by của lịch và audit actor System,
  correlation của runner; thông báo Assigned hiện có, không thêm loại mới.
- Dùng chung `WorkOrderAssignees.RequireAssignee` / RequireSurveyAssignee / EligibleUsers;
  `WorkOrderLoads.Read` tách từ preview; thêm `EngineerLoadBoard.SelectOn` dùng chung Day/Week,
  không đổi Select. Job đọc tải đã commit ở mỗi occurrence nên tính cả phiếu vừa sinh; ngày cố định bỏ quota.
  Người được chỉ định mất eligibility thì fallback, không ai đủ điều kiện → open + Warning.
- AuditEntityType.SurveyRecurringSchedule và AuditAction.Deleted là **enum lưu trữ nội bộ** theo comment
  ở Persistence/Audit/AuditEvent.cs (không phải enum publish). Không thêm giá trị enum contract/notification.
- Test mới: `tests/LuxMap.Shared.Tests/SurveyScheduleCalendarTests.cs`,
  `tests/LuxMap.Api.Tests/SurveyRecurringScheduleTests.cs`, `SurveyRecurringJobTests.cs`.
  Hai file API là partial WorkOrderTests để dùng lại fixture/account/commune riêng từng test và AssetDatabaseCollection;
  teardown thêm receipt/schedule trước phiếu/tuyến/người. Thời gian giả bằng FakeTimeProvider;
  JWT mint bằng đồng hồ thật để validation JWT không bị fake nbf/exp. Test scope nhiều xã dùng token test,
  không chèn hai app_user_commune cho một account (ACCT-1).
- Cập nhật PrefixedIdTests (25 prefix, thêm SSCH), PLAN script copy, drift SURVEY-SCHED-1,
  traps faults-workorders/commune-scope và tracking.html.

Không chạy git, dotnet test, swagger, dotnet ef; không tạo migration, không sửa snapshot/Migrations,
không sửa Contract/OpenAPI. Không commit theo chỉ dẫn phiên này. Chỉ sửa file trong worktree.

## Kiểm chứng thực tế

✅ Build toàn solution (bao gồm biên dịch các test mới) bằng đúng lệnh được cho phép; kết quả cuối xem bên dưới.
✅ Python syntax của script copy, dùng ast.parse (không chạy script, không truy cập DB):

```text
copy_dev_to_supabase.py: Python syntax OK
```

✅ Rà nguồn mới: không DateTime.UtcNow, ExecuteUpdate/ExecuteDelete hoặc backdoor trong service/calendar/job.
Các backdoor trong test chỉ phục vụ fixture/plant dữ liệu theo mẫu sẵn có.

⚠️ Chưa chạy test, chưa kiểm chứng HTTP/DB thực tế, migration, cascade, PK race hoặc capability coverage khi host chạy.
Không suy từ build xanh thành test xanh. Không có số test pass để báo cáo.

## Migration dành cho Claude — CHƯA CHẠY

```bash
dotnet ef migrations add SurveyRecurringSchedules -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations
```

Đọc Up/Down trước khi dùng. Migration phải gồm:

- Sequence bigint `survey_recurring_schedule_id_seq`, schedule_id text DEFAULT
  `luxmap_format_id('SSCH', nextval('survey_recurring_schedule_id_seq'), 4)`.
- `survey_recurring_schedule`: schedule_id text PK; commune_id/segment_id/created_by text NOT NULL;
  day_of_week smallint NOT NULL; assigned_to text NULL; is_active boolean NOT NULL DEFAULT true;
  starts_on_week date NOT NULL; note text NULL; created_at/updated_at timestamptz NOT NULL.
- CHECK `ck_survey_recurring_schedule_day` (1..7), `ck_survey_recurring_schedule_week` (ISODOW=1),
  `ck_survey_recurring_schedule_note` (nullable hoặc char_length≤1000).
- Unique index **`ux_survey_recurring_schedule_segment_day`** (segment_id, day_of_week);
  alternate key (schedule_id, commune_id). Index CommuneId khai tường minh, FK AssignedTo/CreatedBy có index theo convention.
- `survey_recurring_occurrence`: schedule_id text, scheduled_date date, commune_id text,
  work_order_id text, created_at timestamptz; tất cả NOT NULL.
  PK **`pk_survey_recurring_occurrence`** (schedule_id, scheduled_date).
- FK **`fk_survey_recurring_occurrence_schedule`**: (schedule_id, commune_id) → schedule alternate key, Cascade.
  FK **`fk_survey_recurring_occurrence_work_order`**: (work_order_id, commune_id) → work_order alternate key, Restrict.
  Index CommuneId khai tường minh; index FK ghép theo convention.
  Hai bảng HasCommuneReference→administrative_unit Restrict; schedule→road_segment/app_user Restrict.
- Nới **`ck_audit_event_entity_type`** thêm `survey_recurring_schedule` và
  **`ck_audit_event_action`** thêm `deleted` (HasContractEnum sinh từ enum nội bộ).
- Không Drop/Alter cột/index ngoài phạm vi, không AddColumn xmin. Kiểm tất cả tên sinh ≤63 ký tự.
  Đây là tên/cột đã khai trong source, **chưa phải schema đã quan sát ở DB**.

Rollback: dừng job, bỏ receipt trước schedule rồi sequence; phiếu đã sinh vẫn giữ nguyên.
Audit append-only có thể đã mang giá trị mới: **không purge audit để làm Down xanh**;
Claude cần xử lý CHECK khi rollback theo traps/migrations.md (giữ superset nếu đã có audit mới).
Script copy tự lấy sequence từ catalogue, không cần danh sách sequence mới bằng tay.

## Kiểm thử dành cho Claude — CHƯA CHẠY

Sau khi sinh và rà migration, build lại rồi chạy test trên container riêng theo testing.md:

```bash
dotnet build LuxMap.slnx --disable-build-servers -m:1 -p:NuGetAudit=false --source /Users/nhm809/.nuget/packages
dotnet test LuxMap.slnx --disable-build-servers -m:1 --no-restore --filter 'FullyQualifiedName~SurveyScheduleCalendarTests|FullyQualifiedName~WorkOrderTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~PrefixedIdTests'
dotnet test LuxMap.slnx --disable-build-servers -m:1 --no-restore
```

Các test có mặt bao gồm: ba ví dụ 2025; trước/đúng 17:00; T2 00:00/CN/vắt năm/tất cả 7 ngày;
CRUD/status/error/field/audit/cross scope/paging/ID width; generated order fields/segment/correlation/notice/list/agenda;
hai lượt chạy và hai instance; survey thủ công→huỷ→sinh; huỷ phiếu sinh không sinh lại;
ngày đã qua/inactive/future starts; preferred engineer bị khoá; fallback theo tuần/ngày/tie ID,
bỏ quota và tính phiếu mới; không kỹ sư→open và Warning; xoá lịch cascade receipt, phiếu còn.
Helper chạy job còn assert không có log Error để lỗi PK bị nuốt thành log Error không được coi là thành công.

## Phá thử — chưa chạy, Claude chạy

Khôi phục bằng cp/touch; build lại; đọc exit code test, không dùng DLL cũ.
Đường dẫn dưới đây tính từ src/LuxMap.Modules.WorkOrders/:

| Phá ở dòng | Test phải đỏ |
|---|---|
| Jobs/SurveyRecurringGenerateJob.cs:75–76: bỏ `db.Add(new SurveyRecurringOccurrence …)` | `Recurring_manual_survey_suppresses_the_week_until_cancelled_but_cancelled_generated_order_never_regenerates`; `Recurring_job_stages_order_receipt_audit_and_assigned_notice_once_and_is_visible_in_agenda` (assert receipt sau chạy hai lần). Chỉ đếm phiếu chưa cancelled có thể bị lớp chống trùng cùng tuần che lỗi, nên test assert receipt và huỷ phiếu riêng. |
| SurveyScheduleCalendar.cs:10: đổi `<` thành `<=` | `Setup_uses_the_local_cutoff_and_first_week` tại 17:00; tương tự dòng 13 → `Generation_horizon_uses_the_same_cutoff`/job window. |
| Jobs/SurveyRecurringGenerateJob.cs:53–55: bỏ kiểm survey cùng tuần | `Recurring_manual_survey_suppresses_the_week_until_cancelled_but_cancelled_generated_order_never_regenerates`. |
| Jobs/SurveyRecurringGenerateJob.cs:23 **và** :48: bỏ cả scan/recheck `IsActive` | `Recurring_job_skips_inactive_future_starts_and_past_days` (inactive). Bỏ một lớp thì lớp còn lại vẫn chặn đúng. |
| SurveyScheduleCalendar.cs:25: bỏ `WeekStart(date) >= startsOnWeek` | `Recurring_job_skips_inactive_future_starts_and_past_days` (future) và calendar future-start. |
| SurveyRecurringController.cs:21: POST ManageWorkOrders → ReadWorkOrders | `Recurring_write_policy_is_manager_only_and_superior_can_read`. |
| Configurations/SurveyRecurringConfigurations.cs:29: bỏ `.IsUnique()` | `Recurring_rejects_server_owned_immutable_empty_and_invalid_fields` (409 duplicate). Phải sửa schema DB test/migration tạm tương ứng để thật sự bỏ UNIQUE, không chỉ build code với DB vẫn giữ index. |

## Điều cần lưu ý khi review

📌 API/luật mới đang SELF-SIGNED, chưa hợp nhất Contract/OpenAPI theo yêu cầu.
📌 Q7/ACCT-1: job ghi một xã, pole eligibility theo xã lịch; đường được tham chiếu có thể đọc liên xã,
nhưng assignee vẫn phải đủ điều kiện cho xã lịch và xã tuyến, giống kiểm survey hiện có.
Không mở quyền nhiều xã hoặc đổi logic dữ liệu cũ trong ticket này.
📌 Chưa có API xoá vật lý work order trong module hiện tại; FK receipt→work_order Restrict giữ biên bản sinh.
DELETE được triển khai ở ticket này là xoá **lịch**, không xoá phiếu.
Không có quyết định kiến trúc mới cần chọn thay yêu cầu của Mỹ; phần chưa chắc là kết quả chạy DB/test/migration.

## Output build cuối (terminal thật)

Lệnh:

```bash
dotnet build LuxMap.slnx --disable-build-servers -m:1 -p:NuGetAudit=false --source /Users/nhm809/.nuget/packages
```

Đoạn cuối output, process exit code **0**:

```text
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-survey-recurring/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-survey-recurring/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/wt-survey-recurring/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-survey-recurring/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:13.49
```

SDK có in `CSSM_ModuleLoad(): One or more parameters passed to a function were not valid.` lúc restore;
build vẫn kết thúc exit 0 và 0 warnings như trên. Không có output test/EF/Swagger vì không chạy.
