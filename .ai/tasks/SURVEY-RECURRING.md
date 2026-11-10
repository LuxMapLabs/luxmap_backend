---
ticket: SURVEY-RECURRING
title: Lịch khảo sát định kỳ theo tuần — bảng cấu hình, API, job sinh phiếu sau 17:00 Thứ Năm
status: ready
phase: 2
owner: codex
branch: feat/SURVEY-recurring
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: SURVEY-SCHED-1 (kiểm docs/contract-drift.md; dùng số kế tiếp nếu đã bị lấy)"
depends_on: []   # WO-AUTODISPATCH (#222) và WO-BATCH (#221) đã merge vào dev
---

## Bối cảnh

FE (WP5) cần Quản lý đặt "Tuyến A khảo sát tối Thứ Ba hàng tuần" và hệ thống tự sinh phiếu `survey` mỗi tuần (đảm bảo không bỏ sót tuyến). FE tự vẽ thẻ [Định kỳ] cho tháng
hiện tại và tháng sau từ danh sách cấu hình; **phiếu thật chỉ được sinh sau mốc chốt**. Mỹ chốt (09/11/2026) theo bản đặc tả của FE, **không phải D-item**. Chạy **cả 7 ngày**
trong tuần (khảo sát ban đêm; `day_of_week` 1 = Thứ Hai … 7 = Chủ Nhật). Tuần luôn là **Thứ Hai–Chủ Nhật**, múi `Asia/Ho_Chi_Minh` (`WorkOrderAgendaOptions.TimeZone`).

**Luật mốc chốt 17:00 Thứ Năm (hàm thuần, một chỗ — `SurveyScheduleCalendar`):** gọi `T` là thời điểm thiết lập (giờ địa phương), `M` là Thứ Hai của tuần chứa `T`, `cutoff = M + 3 ngày 17:00`.
- `T < cutoff` ⇒ `starts_on_week = M + 7` (tuần kế tiếp).
- `T ≥ cutoff` ⇒ `starts_on_week = M + 14` (tuần sau nữa).
- `first_occurrence_date = starts_on_week + (day_of_week − 1)`.
- Ví dụ khoá bằng test (lịch 2025, khớp spec): Thứ Ba 04/11/2025 10:00, chọn Thứ Tư ⇒ 12/11/2025; Thứ Sáu 07/11/2025, chọn Thứ Tư ⇒ 19/11/2025; Thứ Hai 10/11/2025, chọn Thứ Sáu ⇒ **21/11/2025**. ⚠️ Spec của FE viết "14/11" cho ví dụ cuối — sai so với chính quy tắc (14/11 là Thứ Sáu của **cùng** tuần); làm theo quy tắc, nêu rõ trong drift. Biên: Thứ Năm 16:59:59 ⇒ trước; 17:00:00 ⇒ sau.

**Bảng `survey_recurring_schedule`** (số ít theo quy ước repo; entity trong `LuxMap.Modules.WorkOrders`, `ICommuneScoped` + `HasCommuneReference()` + query filter + `IAudited`):

| Cột | Kiểu / ràng buộc |
|---|---|
| `schedule_id` | text PK, `DEFAULT luxmap_format_id('SSCH', nextval('survey_recurring_schedule_id_seq'), 4)`; thêm prefix `SSCH` vào bảng prefix (`PrefixedId.cs`) |
| `commune_id` | text NOT NULL (FK `Restrict` tới `administrative_unit` qua `HasCommuneReference`) |
| `segment_id` | text NOT NULL, FK `Restrict` → `road_segment` |
| `day_of_week` | smallint NOT NULL, CHECK 1..7 |
| `assigned_to` | text NULL, FK `Restrict` → `app_user` |
| `is_active` | boolean NOT NULL DEFAULT true |
| `starts_on_week` | date NOT NULL, CHECK là Thứ Hai (`EXTRACT(ISODOW FROM starts_on_week) = 1`) |
| `note` | text NULL, ≤ 1000 ký tự |
| `created_by` | text NOT NULL, FK `Restrict` → `app_user` |
| `created_at`, `updated_at` | timestamptz NOT NULL, UTC, `UtcMicrosecondClock` |

- Unique `ux_survey_recurring_schedule_segment_day` trên (`segment_id`, `day_of_week`) — trùng ⇒ 409 `SURVEY_SCHEDULE_EXISTS`. Index dẫn đầu `commune_id` ⇒ khai `HasIndex("CommuneId")` theo `migrations.md`. `HasDatabaseName` cho tên cần chính xác (≤ 63 ký tự).
- **Bảng `survey_recurring_occurrence`** (chống sinh trùng, bền vững): PK (`schedule_id`, `scheduled_date`); `commune_id` (`HasCommuneReference`), `work_order_id` NOT NULL FK `Restrict` → `work_order`, `created_at`. FK `schedule_id` → schedule: **`Cascade`** (cùng xã, an toàn — guard không thấy cascade của DB; ghi lý do trong configuration và trong `commune-scope.md` mục cascade). Không `ICommuneScoped` bắt buộc nếu chỉ đi qua schedule, nhưng nếu job/ truy vấn có thể là gốc thì mang `commune_id` + `ICommuneScoped`.
- Bảng mới ⇒ thêm vào `PLAN` của `scripts/copy_dev_to_supabase.py` **cùng PR** (và sequence nếu script cần), theo `deploy-ops.md`.
- **Migration: Claude sinh sau khi bạn xong** (`dotnet ef`). Bạn **KHÔNG** tạo file migration, KHÔNG sửa snapshot; chỉ khai entity + configuration (kể cả CHECK, unique, FK, sequence/DEFAULT id qua cách module khác đang làm — đọc `HasPrefixedId`/`PrefixedIds` hiện có và làm giống).

**Endpoint** (`[Authorize(Policy=…)]` từng action; ghi `ManageWorkOrders`, đọc `ReadWorkOrders`; KHÔNG capability mới):
- `GET /api/v1/survey-schedules/recurring?commune_id=&page=&page_size=` ⇒ phân trang chuẩn; `commune_id` vắng = mọi xã trong phạm vi (`CommuneFilter.Narrow`). Item: `schedule_id, commune_id, segment_id, segment_name, day_of_week, assigned_to, assigned_to_name, is_active, note, starts_on_week, first_occurrence_date, created_at, created_by, updated_at`. Sắp `created_at, length(id), id`.
  `first_occurrence_date` của lịch **đã chạy** vẫn là ngày đầu tiên theo `starts_on_week` (FE dùng để vẽ thẻ).
- `POST …/recurring` body `{ commune_id, segment_id, day_of_week, assigned_to?, note? }` ⇒ **201** item (kèm `first_occurrence_date`; FE hiện "Lịch sẽ áp dụng từ…"). Luật: `day_of_week` số nguyên 1..7 (chuỗi/ngoài khoảng ⇒ 400 `VALIDATION_FAILED`, `details.field`); đoạn đường không thấy được ⇒ 404 `ASSET_NOT_FOUND`; đoạn thuộc xã khác `commune_id` và **không** `inter_commune` ⇒ 409 `CROSS_COMMUNE_REFERENCE`; `commune_id` ngoài phạm vi ⇒ 403 `COMMUNE_FORBIDDEN`; `assigned_to` không đủ điều kiện ⇒ 409 `ASSIGNEE_NOT_ELIGIBLE` (cùng kiểm tra như tạo phiếu khảo sát: `EligibleUsers` + `RequireSurveyAssignee` — dùng chung, không chép). Trường server-owned (`schedule_id`, `starts_on_week`, `is_active`, `created_by`, `first_occurrence_date`) có mặt trong body ⇒ 400 `SERVER_OWNED_FIELD`. `is_active = true`; `starts_on_week` do `SurveyScheduleCalendar` + `TimeProvider`.
- `PATCH …/recurring/{id}` body có ít nhất một trong `{ is_active, assigned_to (null được), note (null được) }` (**không** đổi `day_of_week`/`segment_id`/`commune_id` — xoá và tạo lại; gửi chúng ⇒ 400). Bật lại (`false → true`) **tính lại** `starts_on_week` theo luật mốc chốt từ bây giờ và trả `first_occurrence_date` mới. Đổi `assigned_to` kiểm đủ điều kiện như POST. Không tồn tại / ngoài phạm vi ⇒ 404 `SURVEY_SCHEDULE_NOT_FOUND` (mã mới trong `ErrorCodes`).
- `DELETE …/recurring/{id}` ⇒ **204**; xoá lịch + dòng `occurrence` của nó; **phiếu đã sinh giữ nguyên**. Không có ⇒ 404 `SURVEY_SCHEDULE_NOT_FOUND`.
- Mọi thay đổi (tạo / sửa / xoá) có đúng **một** `AuditEvent` mỗi `SaveChanges`, ghi người thực hiện (CLAUDE.md D-R13; xem `IAuditTrail.Record`, thêm giá trị entity-type / action theo cách module khác làm — nếu `AuditAction` / entity-type là enum đã publish, ghi vào results thành câu hỏi thay vì tự thêm giá trị).

**Job `survey_recurring_generate`** (`IScheduledJob`, tên snake_case, `Interval` = 15 phút, lease do hạ tầng `scheduled_job` lo; đăng ký như `WorkOrderOverdueJob`; dùng `JobContextFactory.Unscoped()` đọc ứng viên, `Open(commune)` cho mọi ghi, `AuditFor(db, correlationId)`):
- Mỗi lần chạy, `now` → giờ địa phương. `horizon_end` = Chủ Nhật của **tuần kế tiếp** nếu `now ≥ cutoff của tuần này` (Thứ Năm 17:00), ngược lại Chủ Nhật của **tuần này**. Với mỗi lịch `is_active` và mỗi ngày `date` trong [**hôm nay** (ngày địa phương), `horizon_end`] có `isodow(date) = day_of_week` **và** tuần của `date` ≥ `starts_on_week`: nếu `(schedule_id, date)` chưa có dòng `occurrence` ⇒ sinh phiếu. (Bù tự động nếu job lỡ một lần chạy.)
- **Chống trùng hai lớp:** (1) PK `occurrence` — phiếu và dòng `occurrence` được ghi trong **một** `SaveChanges`; hai instance cùng chạy ⇒ vi phạm PK ⇒ bỏ qua êm (đúng luật `IScheduledJob`: "không check-rồi-insert một mình"); (2) nếu trong **cùng tuần Thứ Hai–Chủ Nhật** của `date` đoạn đường đã có phiếu `survey` (bất kỳ, không `cancelled`) chứa đoạn đó trong `work_order_segment` ⇒ **không sinh** (không ghi `occurrence`; mỗi lần chạy kiểm lại; phiếu thủ công bị huỷ thì lần chạy sau sinh bình thường). Quản lý **huỷ / xoá** một phiếu đã sinh ⇒ dòng `occurrence` vẫn còn ⇒ **không sinh lại**.
- **Phiếu sinh ra:** `task_kind = survey`, `commune_id` = của lịch, một đoạn đường (`segment_ids = [segment_id]`, `work_order_segment.position = 0`), `title = "Khảo sát toàn tuyến - {SegmentName}"`, `scheduled_date = due_date = date`, `note` = ghi chú của lịch, vắng thì `"Tự động sinh từ lịch khảo sát định kỳ {schedule_id}"`, `created_by` = `created_by` của lịch. Dùng **đúng phần ghi của `CreateSurvey`** (ID, `WorkOrderSegment`, audit `Created`, thông báo `Assigned`) — tách phần ghi thành hàm dùng chung nhận người tạo tường minh (`WorkOrderService` hiện dùng `ActorId` từ request, job không có người đăng nhập); **không chép** phần ghi, và không tạo đường mà `POST /work-orders` đơn lẻ đổi hành vi (test hiện có không đổi).
- **Gán người:** `assigned_to` của lịch còn đủ điều kiện (`EligibleUsers` + điều kiện kỹ sư của phiếu khảo sát tại thời điểm sinh) ⇒ dùng; ngược lại (null **hoặc** không còn đủ điều kiện) ⇒ dùng **`EngineerLoadBoard`** cho ngày đó (thêm phương thức chọn trên MỘT ngày cố định, ví dụ `SelectOn(eligible, date)`, dùng chung `Day`/`Week`; **không đổi hành vi `Select` hiện có**; phần đọc tải hiện có tách khỏi `AutoDispatchQueries` thành hàm dùng chung, không chép): ít ca nhất trong tuần Thứ Hai–Chủ Nhật chứa `date`, rồi ít ca nhất trong ngày, rồi `user_id` — **bỏ qua quota ngày** (ngày cố định, không dời), tải tính cả phiếu vừa sinh trong lượt. Không ai đủ điều kiện ⇒ phiếu ở trạng thái `open`, `assigned_to = null`, ghi log mức Warning (không thông báo mới, không loại thông báo mới).
- Mỗi phiếu/occurrence một transaction nhỏ (lỗi một lịch không chặn lịch khác); log tóm tắt như `WorkOrderOverdueJob`. Không ghi bằng `EnterUnscopedSystemWriteBackdoor`.
- **Cấu hình:** hằng số mốc chốt (Thứ Năm, 17:00) ở **một chỗ** (`SurveyScheduleCalendar` / options `WorkOrders:SurveySchedule`), không rải.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/{faults-workorders,migrations,commune-scope,testing,deploy-ops}.md`
- `src/LuxMap.Persistence/Jobs/{IScheduledJob,JobContextFactory}.cs`, `src/LuxMap.Modules.WorkOrders/Jobs/WorkOrderOverdueJob.cs` (mẫu job + chống trùng bằng PK), `WorkOrderService.cs` (`CreateSurvey`, `EligibleUsers`, `RequireAssignee`, `RequireSurveyAssignee`, `Record`, `Notify`), `WorkOrderAgenda.cs`
- Bộ chọn kỹ sư của WO-AUTODISPATCH: `EngineerLoadBoard` (công khai, `AutoDispatchPlanner.cs`: `WeekStart`, `Select(eligible, start)` tìm ngày theo quota), `AutoDispatchQueries.cs` (đọc tải hiện có — phiếu không `cancelled`, mọi loại, có `scheduled_date`, tuần Thứ Hai–Chủ Nhật), `WorkOrderAssignees.EligibleUsers`. **Quyền ghi: `ManageWorkOrders` chỉ có `manager`** — `system_admin` ghi lịch ⇒ 403 `ROLE_FORBIDDEN` (test phải khẳng định đúng như vậy; đừng mở quyền).
- `src/LuxMap.Modules.WorkOrders/Configurations/*.cs`, `WorkOrdersModule.cs`, `src/LuxMap.Shared/**/PrefixedId.cs`, `docs/database/erd.md` (Q-x liên quan), `scripts/copy_dev_to_supabase.py`

## Yêu cầu / Test

1. Thực hiện đúng mô tả trên. `SurveyScheduleCalendar` thuần + test đơn vị đầy đủ (ba ví dụ, biên 16:59:59 / 17:00:00, Chủ Nhật, Thứ Hai 00:00, mọi `day_of_week` 1..7, tuần vắt qua năm).
2. Test tích hợp (`SurveyRecurringScheduleTests.cs`, `SurveyRecurringJobTests.cs`; đồng hồ giả `FakeTimeProvider`; tiền tố riêng; teardown theo `testing.md`; assert giá trị cụ thể): endpoint (201 + `first_occurrence_date` đúng theo đồng hồ giả; 400 / 403 / 404 / 409 mọi nhánh trên; PATCH tắt / bật / đổi người / bật lại tính lại `starts_on_week`; DELETE cascade `occurrence`, phiếu còn; 401, 403 `ROLE_FORBIDDEN` — `field_engineer` không ghi được, `superior` đọc được và không ghi được; `GET` lọc xã, phân trang, thứ tự ID hai bên ngưỡng độ rộng; **mỗi thay đổi đúng một `AuditEvent`**); job (sinh đúng ngày trong cửa sổ trước/sau 17:00 Thứ Năm; **chạy hai lần ⇒ một phiếu**; hai job đồng thời ⇒ một phiếu, không 500; phiếu thủ công cùng tuần ⇒ không sinh, huỷ nó ⇒ sinh; lịch tắt ⇒ không sinh; lịch `starts_on_week` ở tương lai ⇒ không sinh; **huỷ phiếu đã sinh ⇒ không sinh lại**; ngày đã qua (< hôm nay) ⇒ không sinh; kỹ sư chỉ định bị khoá ⇒ chọn người khác theo tải; không kỹ sư ⇒ `open`; tiêu đề / note / `created_by` / ngày / `work_order_segment` đúng; audit `Created` mang correlation `job:survey_recurring_generate:…`; thông báo `Assigned` tới kỹ sư; phiếu của job hiện trong `GET /work-orders` và agenda đúng đêm).
3. **Phá thử** (ghi vào results "chưa chạy, Claude chạy", kèm đúng dòng): bỏ dòng `occurrence` ⇒ test không-sinh-lại và chạy-hai-lần đỏ; đổi `<` thành `<=` ở mốc 17:00 ⇒ test biên đỏ; bỏ kiểm "cùng tuần đã có survey" ⇒ test phiếu thủ công đỏ; bỏ `is_active` ⇒ test tắt đỏ; bỏ lọc `starts_on_week` ⇒ test tương lai đỏ; policy `ManageWorkOrders` → `ReadWorkOrders` ở POST ⇒ test 403 đỏ; bỏ unique ⇒ test 409 đỏ.
4. **Tài liệu:** `docs/contract-drift.md` dòng SURVEY-SCHED-1 (SELF-SIGNED 09/11/2026): bảng, endpoint, luật mốc chốt + ba ví dụ + ghi chú ví dụ "14/11" của spec sai, `first_occurrence_date`, PATCH chỉ `is_active` / `assigned_to` / `note`, bật lại tính lại, DELETE giữ phiếu, job sau 17:00 Thứ Năm + cửa sổ + chống trùng hai lớp + huỷ phiếu không sinh lại, gán người bỏ qua quota, **không hỗ trợ tạm dừng / dời từng tuần cụ thể — Quản lý huỷ hoặc đổi ngày chính phiếu đã sinh; muốn dừng một tuần chưa sinh thì tắt lịch rồi bật lại** (FE vẽ thẻ ảo từ `starts_on_week`), việc FE phải làm. `docs/agents/traps/faults-workorders.md` và `commune-scope.md` (cascade `occurrence`): bẫy ngắn. `scripts/copy_dev_to_supabase.py` `PLAN`. `tracking.html` thêm thẻ. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*`, **KHÔNG** tạo migration.
5. Ghi `.ai/results/SURVEY-RECURRING.md`: đã làm gì, lệnh Claude cần chạy (`dotnet ef migrations add` + tên cột / constraint / index bạn khai), điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới
- [ ] Test mục 2 có mặt; Claude sinh migration, chạy `dotnet test` + phá thử
- [ ] Endpoint mới nằm trong ma trận capability; `CapabilityPolicyCoverageTests` xanh; không thêm capability

## KHÔNG ĐƯỢC làm

- Không migration, không sửa snapshot, không sửa `src/LuxMap.Persistence/Migrations`.
- Không thêm capability, không thêm loại thông báo, không thêm giá trị enum đã publish, không đổi field đã publish.
- Không `ExecuteUpdate` / `ExecuteDelete`; không backdoor; không `*` scope trong job.
- Không bỏ qua T7/CN; không dùng giờ UTC để tính tuần / mốc chốt; không dùng `DateTime.UtcNow` (dùng `TimeProvider`).
- Không chép phần ghi của `CreateSurvey` / `EligibleUsers` / bộ chọn kỹ sư — tách dùng chung.
- Không làm "tạm dừng / dời từng tuần" (không bảng ngoại lệ); không thêm endpoint ngoài ba cái trên (GET / POST / PATCH / DELETE).
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*`.
- Không chạy git, `dotnet test`, swagger, `dotnet ef` (sandbox không có quyền).
- Không tự chọn phương án khác với các mục "Mỹ chốt"; điểm mơ hồ → ghi vào results thành câu hỏi, đừng đoán.
