# Sự cố, phiếu, audit, thông báo, job, sync

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi sự cố, phiếu công việc, audit, thông báo, job nền, đồng bộ offline. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Năm quy tắc chốt ở BE-18 — sự cố

**1. Cột trên fault chỉ giữ quyết định MỚI NHẤT; audit giữ chuỗi.**

`reported_by`, `confirmed_by/at`, `resolved_by/at` trả lời nhanh ai quyết gần nhất, không thay audit append-only
(BE-23a dưới đây). Chuỗi ví dụ hợp lệ: `detected → confirmed → in_progress → resolved`.
PATCH có cả `severity`; request/response xem `PatchFaultRequest` và Contract §2.5, không dựng `FaultHistory` riêng.

`reported_by` **NULL với fault do CV/IoT sinh**, và đó là câu trả lời đúng chứ không phải dữ liệu
thiếu — `source_channel` đã nói engine nào. **Không dựng user hệ thống giả**: nó làm mọi dòng trông
đồng đều trong khi ghi một việc chưa từng xảy ra, rồi lại hiện ra trong mọi thống kê "ai báo sự cố".

**2. `commune_id` có HAI nguồn, vì `pole_id` nullable.**

| Ca | Nguồn |
|---|---|
| Có `pole_id` | Server tra từ pole. Client gửi → **400** |
| `pole_id` NULL | Lấy từ **scope JWT**. Đúng một commune → lấy luôn. Nhiều hơn một → **400**, client phải chọn (và giá trị chọn phải trong scope) |

Không suy xã từ `lat/lng`: `AdministrativeUnit` chưa có polygon ranh giới; bbox là khung nhìn, không phải ranh giới.
Contract §2.8 đã có `commune_id` cho ca không pole (O-10 đã hợp nhất).

Ảnh báo tại chỗ gửi qua `POST /faults/{id}/photos` (EV-2), không có `photo_frame_id` trong POST fault;
`repair_evidence` có đúng một cha (phiếu **hoặc** sự cố). Xem `docs/contract-drift.md`, BE-24.

**3. `priority_score` là cột LƯU, nullable.**

`priority_score` là cột **lưu**, nullable, dành cho CV-16 (tiến trình ngoài API) ghi vào — sắp theo biểu thức tính lúc query thì không dùng được index và phân trang không ổn định; **sort mặc định là `-severity`** (P-3, `FaultSort.Default`).
Canh thứ tự mức bằng `FaultListTests.Default_order_is_severity_by_rank_not_alphabet_then_oldest_first`;
luật hạng enum ở `CLAUDE.md`, mục Enum. Khi chọn sort priority, fault chưa chấm nằm cuối. Không hard-code trọng số; đổi trọng số phải xét tính lại dữ liệu lưu
(theo P-1 / BE-33 / CV-16, không gán cho job BE-26).

> 🔴 **`ix_fault_priority_score` là `DESC` TRẦN, không phải `DESC NULLS LAST`** (`FaultConfigurations`). Postgres xếp NULL **lên đầu** với `DESC`, nên index
> này không phục vụ thứ tự NULL-cuối. `IsDescending()` của EF không sinh `NULLS LAST`; câu cũ ở đây
> và comment trong `FaultConfigurations` đã nói sai. **Truy vấn phải tự ép NULL xuống cuối**
> (`OrderBy(x => x.PriorityScore == null)` trước) — BE-40 làm vậy, canh bằng sabotage trong
> `FaultListTests`. Muốn index phục vụ được thì cần migration riêng (BE-32), đừng tưởng đã có.

**4. `fault_cluster` là BẢNG, không phải cột text trần.**

Contract mục 2.4 chỉ lọc theo `cluster_id` nên một cột text là đủ cho endpoint — nhưng nó sẽ là **ID
không ràng buộc thứ HAI**, cùng họ với `pole_current_status.last_sweep_id`, và không có gì phát hiện
một giá trị trỏ vào cụm không tồn tại. Bảng cũng cho lần gom cụm chỗ tự ghi lại:
`clustering_model_version` là thứ **BE-34** cần để trả lời *kết quả nào do model phiên bản nào sinh*.

Seed mock đã ghi `fault_cluster` (`scripts/seed_mock_set.py`); không đồng nhất dữ liệu seed với pipeline gom cụm CV-15.
Tiến độ topology/gom cụm xem `tracking.html`.

**5. Toàn bộ FK là `Restrict`. Fault là sự kiện đã xảy ra.**

Xoá cột không làm cho việc "đèn từng hỏng ngày 20/8" chưa từng xảy ra, và cột ghi vết trên chính dòng
đó **là** tiêu chí nghiệm thu của ticket. Cascade còn mở rộng **vùng mù** ở `docs/agents/traps/commune-scope.md`, mục 1c — guard
`SaveChanges` không thấy cascade do DB thực hiện.

**Định nghĩa "fault MỞ" — CHỐT, một chỗ duy nhất: `FaultStatusSets.Open`.**

Tập mở và cách gọi `FaultStatusSets.Open` / `IsOpen`: xem `CLAUDE.md`, mục Enum (Contract §1).

Ba trạng thái bị loại vì **ba lý do khác nhau** — đó là lý do tập này được viết ra tường minh thay vì
diễn đạt kiểu "chưa xong": `rejected` = kỹ sư kết luận nó chưa từng là sự cố (dương tính giả),
`resolved` = đã sửa, `verified` = đã nghiệm thu. Chỉ cái đầu là phán xét về **bản thân sự cố**.

**Hai test canh, và chúng bắt hai thứ KHÁC nhau** (`OpenFaultCountTests`, ở `LuxMap.Shared.Tests` nên
chạy được cả khi không có Docker):

| Test | Canh gì | Đỏ khi |
|---|---|---|
| `The_count_on_every_pole_matches_the_faults_that_are_actually_open` | **DỮ LIỆU** — `open_fault_count` của mọi cột trong `mock-poles.geojson` khớp số đếm thật từ `mock-faults.json` | ai đó thêm/sửa fault mà quên cập nhật count |
| `Only_detected_confirmed_and_in_progress_count_as_open` | **ĐỊNH NGHĨA** — dựng fault trong bộ nhớ phủ cả 6 trạng thái | ai đó đổi tập trong `FaultStatusSets.Open` |

⚠️ **Không gộp hai test làm một.** Mock chỉ có fault mở nên nới định nghĩa vẫn có thể giữ test dữ liệu xanh;
test định nghĩa phải phủ cả trạng thái đóng. Giữ test định nghĩa độc lập với dữ liệu mock.

**Hai test dùng CHUNG một hàm đếm** (`CountByPole`). Nếu tách thành hai đường thì test định nghĩa
không còn bảo vệ test dữ liệu — nó sẽ chỉ khẳng định một ý kiến riêng.

## BE-23a — audit chung, append-only và giới hạn thật (27/09/2026)

`LuxMap.Persistence/Audit/` chứa `AuditEvent`, `IAuditTrail` và `IAudited`.
`Record` chỉ stage event vào **cùng DbContext scoped**; service chụp actor, vai trò, thời điểm,
before/after rồi gọi đúng một `SaveChanges` cùng thay đổi nghiệp vụ. Không save riêng audit.
Guard ở cả sync/async từ chối sửa/xoá audit (kể cả backdoor); khi có thay đổi `IAudited`,
bắt buộc đúng một audit mới. Backdoor chỉ bỏ yêu cầu có event cho seed/fixture.
Guard đếm event, không chứng minh nội dung event khớp thao tác: service và test nghiệp vụ phải kiểm điều đó.
`WorkOrder`, `WorkOrderFault`, `WorkOrderSegment` và `Fault` đã implement `IAudited`; mọi writer phải stage audit.

Trigger DB chặn UPDATE/DELETE/TRUNCATE với SQLSTATE `55000`.
**Append-only chặn lỗi ứng dụng, KHÔNG chặn người có quyền SQL.**
`luxmap.audit_purge` là custom GUC, không phải quyền bảo mật; user có SQL có thể đặt nó,
và tài khoản `luxmap` local hiện là superuser. Không mô tả cơ chế này như chống giả mạo tuyệt đối.
Chỉ teardown test được `SET LOCAL luxmap.audit_purge = 'on'` trong transaction riêng,
rồi DELETE với WHERE theo dữ liệu của test. Không đặt ở mức session/connection pool.
`AuditGuardTests.Production_source_never_enables_the_test_purge_switch` quét `src/` (trừ migration)
để chặn đường bật GUC trong ứng dụng; giữ test này cùng `BannedBulkWriteApiTests`.
FK actor và commune đều RESTRICT: muốn dọn tài khoản/xã test phải dọn audit của test trước.
Rollback migration xoá bảng audit và mất dữ liệu audit; vòng apply/rollback/reapply chỉ chạy
trên DB test, khi bảng audit trống.

## BE-23 — xmin, tên partial index và quyền người được giao (28/09/2026)

`IsRowVersion()` trên uint dùng cột hệ thống xmin. **EF vẫn sinh AddColumn xmin trên fault và
DropColumn trong Down**, dù Npgsql xử lý đặc biệt lúc sinh SQL: đọc migration và bỏ thao tác
cột vật lý, giữ mapping trong model/Designer/snapshot. Không tạo một cột xmin riêng.

`HasIndex(..., "tên")` đặt tên model index, **chưa bảo đảm tên DB** khi convention snake_case
chạy. Đã sinh `ix_work_order_fault_fault_id1` thay vì `ux_work_order_fault_fault_id_active`;
phải thêm `HasDatabaseName` vì service nhận diện 23505 bằng tên constraint thật.

WorkOrder gộp IAssigneeScoped vào cùng lambda commune, tham chiếu DbContext. WorkOrderFault
chỉ scope commune; BE-24/BE-43 phải lookup WO cha trước. Sau concurrency cũng lookup lại có
filter: reassign vừa xảy ra có thể biến WO thành 404 với người được giao cũ.

Fault.CommuneId nay là alternate-key property để FK bảng nối bảo đảm cùng xã; giống Feeder,
EF không cho đổi commune trên entity đã track. Fault không chuyển xã.

**Timestamp app sinh rồi trả thẳng từ entity đang track phải cắt về micro giây trước khi lưu.**
Dùng `UtcMicrosecondClock.UtcNow()` (Shared), giữ cùng một `now` cho entity, fault và audit.
PostgreSQL `timestamptz` chỉ lưu µs; `DateTime.UtcNow` trên Linux có tick 100 ns nên response
có thể khác DB dù local macOS (đồng hồ µs) xanh. PR #54 đã lộ sai lệch completed_at/resolved_at.
Test phải cấp thời gian cố định có tick lẻ dưới µs qua TimeProvider, không chỉ thử đồng hồ thật.

## BE-40 — danh sách sự cố (29/09/2026)

**Faults KHÔNG tham chiếu được WorkOrders** — chiều ngược đã có (WorkOrders gọi `FaultTransitions`).
Câu hỏi "fault nào đang nằm trong phiếu nào" đi qua port **`IActiveWorkOrderLookup`** khai ở Faults,
WorkOrders hiện thực. **BE-19 dùng lại đúng port này** để trả 409 khi fault thuộc phiếu sửa chữa đang
chạy (WO-10) — đừng thêm project reference, và đừng chuyển endpoint fault sang WorkOrders.

`ActiveWorkOrdersAsync` chỉ đọc `WorkOrderFault` (scope xã), nên ID trả về cả với phiếu người gọi không mở được (F-3).
`ActiveRepairsAsync` còn JOIN `WorkOrder.IgnoreQueryFilters` để đọc `TaskKind`; scope vẫn neo từ bảng nối đã lọc xã,
không để assignee filter giấu phiếu sửa chữa của người khác (`ActiveWorkOrderLookup`).

`location` của fault = `lat/lng` của fault, thiếu thì điểm của cột — **cùng luật** với chi tiết WO.
CHECK `ck_fault_pole_or_location` cho phép fault có cột mà không toạ độ, nên chỉ đọc `fault.lat` là
trả `null` ở chỗ Contract bắt buộc có giá trị.

## FR-2 — chuỗi phiếu công việc (29/09/2026)

**`root_work_order_id` NULL ở lịch đầu, KHÔNG trỏ vào chính nó.** API tính `case_id = root ?? work_order_id`.
Trỏ vào chính nó thì mọi chỗ đang ghi phiếu (seed `seed_mock_set.py`, `Plant` trong test, fixture của
BE-40) phải biết cột này, và EF phải chèn một hàng tham chiếu chính nó. Lọc chuỗi bằng
`work_order_id = X OR root_work_order_id = X`. CHECK `ck_work_order_chain_complete` bắt cha và gốc
null **cùng nhau**; FK ghép `(…, commune_id)` giữ cả chuỗi trong một xã. Tính nhất quán
`root = cha.root ?? cha.id` **chỉ canh ở service** — không ràng buộc DB nào so hai hàng.

**Nghiệm thu kiểm tra ĐỔI trạng thái sự cố (FR-2a).** `fault_present` + `detected` → `confirmed`, làm
trong cùng `verify`, cùng audit. Đây là ngoại lệ thứ hai của "chỉ repair lan truyền" (WO-2). BE-19 phải
biết: sự cố `confirmed` có thể có `confirmed_by` là người nghiệm thu một phiếu, không phải người gọi
`PATCH /faults`.

**Khảo sát → kiểm tra (WO-SURVEY-CHAIN, drift WSC-1…WSC-5).** Phiếu khảo sát **không bao giờ** có `work_order_fault`: tập
mang sang tính lúc gọi `follow-up`, qua port **`ISurveyFindingLookup`** (Survey hiện thực — Survey tham chiếu WorkOrders, không
ngược lại). Chọn kết quả **mới nhất** của từng cột rồi mới xét `out`/`dim`; lọc ngược lại thì một `out` cũ sống lại sau `normal`.
Nhánh khảo sát truyền `NewWorkOrder.Keep`: `Insert` lọc lại **sau** `FaultLocks` và **bỏ** sự cố vừa bị phiếu khác lấy thay vì 409.
Đừng "sửa" thành tạo sự cố lúc `verify`: duyệt sweep đã sinh sự cố (`SurveyReviewService.Publish`).

## BE-19 — duyệt sự cố, audit và khoá hàng (29/09/2026)

**`Fault` là `IAudited`.** Mọi đường ghi sự cố (BE-41 báo tại chỗ, ingest CV/IoT, IOT-11) phải thêm
**đúng một** audit trong cùng `SaveChanges` — engine ghi actor `cv`/`iot`. Seeder và fixture đi qua
`EnterUnscopedSystemWriteBackdoor()`. ⚠️ Guard audit chạy **trước** guard xã (`LuxMapDbContext`): test
muốn chứng minh guard xã trên sự cố phải kèm một audit hợp lệ, nếu không nó đỏ vì lý do khác
(`FaultSchemaTests.WriteAsync`).

**Khoá hàng `fault` trước khi kiểm (`FaultLocks`).** Tạo phiếu **đọc** trạng thái sự cố rồi chèn liên
kết mà không ghi vào `fault`, nên `xmin` không thấy race với `PATCH /faults`. Cả hai phía mở transaction,
`SELECT … FOR UPDATE` theo thứ tự `fault_id` (thứ tự khoá, **không** phải thứ tự hiển thị), rồi mới đọc
lại. Hệ quả: hai lần tạo phiếu trên cùng sự cố nay **tuần tự**; unique index
`ux_work_order_fault_fault_id_active` còn là lớp chặn cuối, không còn là lớp trả lời. Trong transaction,
bắt lỗi xong phải **rollback trước** mọi truy vấn khác (`WorkOrderService.Save`).

**`fault_type` là loại kênh báo, không bao giờ sửa.** Phân loại lại đi vào `override_fault_type`; API dùng
`override ?? fault_type` cho cả trả về lẫn lọc. Lọc bằng hai nhánh, danh sách của nhánh override phải kiểu
`FaultType?` — EF không bind được một danh sách không-null vào cột nullable, cũng không dịch được
`Contains(override ?? type)`.

Rollback `AddFaultReview`: xem `docs/agents/traps/migrations.md`, mục Rollback dựng lại CHECK cũ.

**Test chuyển trạng thái phải thử chuyển HỢP LỆ ở §3.2 nhưng cấm ở `PATCH`** (`confirmed → in_progress`).
Chuyển mà máy trạng thái vốn cấm thì 409 dù có luật riêng của `PATCH` hay không — sabotage gỡ luật đó đã
xanh cho tới khi thêm cặp này.

## BE-27 — thông báo (05/10/2026)

**Thông báo được STAGE trên DbContext của người gọi, không bao giờ tự lưu** (`Notifier.Stage`, module
`LuxMap.Modules.Notifications`). Nó đi chung `SaveChanges` với thay đổi nghiệp vụ, nên thao tác thua tranh chấp hay
rollback không để lại thông báo (canh bằng `NotificationTests.The_losing_side_of_a_race_leaves_no_notice`). Đừng gọi
`SaveChanges` riêng cho thông báo, và đừng inject một service scoped giữ context của request: worker khảo sát là
singleton, tự mở context theo job và lưu theo từng xã — nó stage đúng một lần, ở lượt lưu của **xã neo**.

**FK `notification.recipient_user_id → app_user` là `Restrict`.** Mọi teardown xoá tài khoản test phải xoá
`notification` trước (theo `commune_id` của fixture là đủ, vì thông báo luôn mang xã của sự kiện). Quên là teardown
gãy, để lại tài khoản và xã — và `AccountAdminTests` (đọc `page_size=200` tài khoản đang hoạt động) đỏ ở lượt sau
vì DB đã quá 200 tài khoản. Test đổi luật người nhận mà thấy hàng loạt `23503 … fk_notification_app_user…` là đây.

**"Quản lý của xã" = `Notifier.ManagersCoveringAsync`** (đang hoạt động = không khoá **và** đã đặt mật khẩu; có
**đủ** mọi xã truyền vào). Sự kiện mới cần báo Quản lý thì gọi nó, đừng viết lại truy vấn — khảo sát liên xã mà báo
cho Quản lý thiếu một xã là báo cho người không duyệt được.

**Văn bản tự do vào thông báo (lý do trả phiếu, tên phiếu) phải qua `Notifier.Clip`, không cắt `Substring` tay.**
Cắt giữa một cặp surrogate (emoji) để lại nửa ký tự không mã hoá được UTF-8 — lượt ghi đó hỏng, kéo theo **cả thao tác
nghiệp vụ** đi cùng giao dịch (review Codex). **Đánh dấu đã đọc khoá hàng trước rồi mới đọc** (khuôn `FaultLocks`), để
hai thiết bị cùng bấm không ghi đè `read_at` đầu tiên.

**Câu chữ thuộc module sở hữu sự kiện** (`WorkOrderNotices`, `SurveyNotices`, `FaultNotices`) — Notifications không
tham chiếu ngược WorkOrders / Survey / Faults. Thêm `type` mới: thêm enum, câu chữ ở module đó, CHECK sinh lại qua
migration, Contract 5.9.

## BE-43 — đồng bộ offline (06/10/2026)

**Module `LuxMap.Modules.Sync` đứng trên đỉnh đồ thị phụ thuộc** (tham chiếu Map, Faults, WorkOrders, Survey, Assets) và
**không ghi dòng nghiệp vụ nào** — mỗi thao tác của hàng chờ gọi đúng service của endpoint gốc. Thêm `op_type` mới thì gọi service
của nó, đừng chép logic kiểm tra / ghi vào Sync: hai đường ghi sẽ lệch nhau ngày một trong hai đổi. Kiểm tra mà controller gốc làm
ngoài service (DataAnnotations, `RejectServerOwnedFields`) phải chuyển **vào service** hoặc gọi lại tường minh — `LuxReadingService.
RejectServerOwnedFields` đã chuyển vì lý do đó.

🔴 **Kiểm capability TỪNG thao tác**, ngoài `SyncOffline` ở cửa (`SyncPushService.PolicyFor` → `IAuthorizationService`, cùng tên
policy với endpoint gốc, không chép danh sách vai trò). Một endpoint gói năm loại ghi mà chỉ kiểm cửa là mở `ReportFaults` cho mọi vai
trò được sync. Test HTTP không phân biệt được hai lớp khi `SyncOffline` chỉ có `field_engineer`, nên lớp thứ hai được canh bằng một host
**từ chối riêng `ReportFaults`** (`Each_operation_is_checked_against_its_own_capability_not_only_the_door`).

**`sync_operation` được STAGE trước khi gọi service** và ghi bởi chính `SaveChanges` của service (khuôn `Notifier.Stage`): thao tác lỗi
hay thua tranh chấp không để lại dòng. Chỉ thao tác **đã áp** được ghi; conflict / rejected thì lần gửi lại đánh giá lại. Có **hai lớp**
khử trùng lặp, cả hai đều so `op_type` + `entity_id` trước khi báo "replayed": tra sớm (`RecordedAsync`) và bắt trùng
`pk_sync_operation` khi đua. Phá thử cho thấy tắt riêng một lớp vẫn xanh — cố ý; tắt cả hai thì
`A_client_op_id_reused_for_a_different_operation_is_rejected_and_writes_nothing` đỏ. Đừng "đơn giản hoá" đường bắt trùng thành
"luôn replayed": thao tác KHÁC dùng lại khoá sẽ bị báo đã áp dù không ghi gì.

**Tra chéo khoá giữa ba nơi giữ khoá** (`fault`, `lux_reading`, `sync_operation` — `SpentElsewhereAsync`) chỉ là best effort: không
nguyên tử với lượt ghi, nên hai yêu cầu đồng thời dùng chung khoá cho hai *loại* khác nhau có thể cùng qua. Chấp nhận vì mỗi nơi vẫn
áp thao tác của nó tối đa một lần. Đừng mô tả nó như ràng buộc chặt.

**`performed_at` không bao giờ để bước sau sớm hơn bước trước** (`WorkOrderService.Effective`): không gửi giờ mà giờ server còn sớm
hơn mốc trước thì lấy mốc trước. Canh bằng `A_completion_is_never_stored_before_its_start`.

**Giữa hai thao tác phải `ResetAsync`** (rollback giao dịch đang mở + `ChangeTracker.Clear()`): entity mà thao tác lỗi để lại sẽ bị
`SaveChanges` của thao tác sau ghi luôn.

**`SetPoleNoteAsync(…, expected)` khoá hàng `pole` (`SELECT 1 … FOR UPDATE`) rồi mới đọc qua query filter** — chỉ khi có `base_note`;
so theo **nội dung**, không theo `updated_at` (giờ đó đổi khi sửa bất cứ gì của cột).

**`PoleProperties` không còn `sealed`** — chỉ để `SyncPoleProperties` thêm `note` mà vẫn phẳng. Dựng bằng copy constructor của record
(`[SetsRequiredMembers]`); thêm trường vào `PoleProperties` thì gói tự có.

**Test dựng nhiều cột trong MỘT `SaveChanges` không được giả định ID theo thứ tự `AddRange`** — EF không giữ thứ tự khi DB sinh khoá
(cùng bẫy 7b). Kỳ vọng thứ tự phải sắp theo luật ID (`IdOrder` trong `SyncTests`).

**Generator spec:** `object?` C# xuất `nullable` không `type` → lint lỗi; vá tại phần "lint fix" của generator như `ApiError.details`.

## BE-26 — job nền theo lịch trên lease của DB (14/10/2026, drift JB-1…JB-8)

- **Không Hangfire.** `LuxMap.Persistence/Jobs`: bảng `scheduled_job` (một dòng mỗi job, runner tự `INSERT … ON CONFLICT DO NOTHING`
  lúc khởi động), `ScheduledJobRunner` (claim = **một** câu `UPDATE … WHERE job_name = (SELECT … FOR UPDATE SKIP LOCKED)`; mọi so sánh
  giờ dùng **`now()` của DB**), `ScheduledJobWorker` (`Jobs:Enabled`, `Jobs:PollSeconds`, `Jobs:LeaseSeconds`, `Jobs:RetrySeconds`).
  Job mới = một `IScheduledJob` đăng ký singleton trong module sở hữu việc đó; không sửa runner.
- 🔴 **Lease KHÔNG phải thứ chống trùng.** Một lượt có thể chạy quá lease, hai instance có thể cùng chạy: mọi thứ job ghi phải có
  **khoá tự nhiên + unique ở DB** (`pk_work_order_overdue_notice` = (phiếu, hạn); `ux_fault_origin_node_id_detected_at` = (thiết bị,
  lúc vượt ngưỡng)) và bắt đúng tên ràng buộc đó khi đua. Phá thử bỏ lượt bắt ⇒ test đồng thời đỏ. "Kiểm rồi chèn" một mình là sai.
- **Job quét theo TRẠNG THÁI, không theo khe giờ**: một lượt xử lý mọi thứ đang đến hạn, nên server tắt vài giờ thì lượt đầu bù hết.
  Mốc của sự kiện lấy từ **dữ liệu** (`last_report_at + OfflineAfter`), không từ lúc job chạy — mọi instance tính ra cùng giá trị.
- **Không principal ⇒ `JobContextFactory`**: `Unscoped()` (scope rỗng, chỉ đọc với `IgnoreQueryFilters` để tìm ứng viên — guard
  từ chối mọi lượt ghi ở đó) và `Open(xã)` cho mọi lượt ghi, đọc lại ứng viên **qua filter** trước khi ghi. Không backdoor, không `*`.
  Audit qua `JobContextFactory.AuditFor(db, correlation)`; correlation `job:<tên>:<lease token>`.
- **Một luật, một biểu thức.** Điều kiện của job dùng cho cả lượt quét lẫn lượt đọc lại (`WorkOrderOverdueJob.OverdueOn`). Bản đầu đọc
  lại bằng một bản sao viết tay — phá thử luật quét thì **không test nào đỏ** vì bản sao che đi. Luật im lặng của thiết bị phải viết
  lại trong SQL (EF không dịch `IotOptions.StatusAt`), nên test đối chiếu thẳng với `StatusAt` ở **hai phía** ngưỡng.
- 🔴 **Test chạy job với đồng hồ năm 2001 và trồng dữ liệu 2000–2001.** Job quét MỌI xã của DB dùng chung: chạy với giờ thật sẽ ghi dấu
  quá hạn cho phiếu của class khác, và teardown của class đó vấp FK `Restrict`. `LuxMapTestHost.CreateHost` **ép** `Jobs:Enabled=false`
  và từ chối host nào vẫn bật — đừng gỡ để "test worker thật"; test worker dựng `ScheduledJobWorker` trực tiếp với job giả.
- `fault.origin_node_id` cấu hình ở **Telemetry** (`FaultOriginNodeConfiguration`), như `origin_observation_id` ở Survey: Faults không
  tham chiếu Telemetry; Telemetry nay tham chiếu Faults. FK ghép `(origin_node_id, commune_id)` → `iot_node`; index FK khai tường minh
  cạnh unique partial (quy tắc partial index). CHECK `ck_fault_node_source`: có thiết bị ⇒ `iot` và không cột.
- Teardown xoá `fault` (giữ `iot_node`) và `work_order_overdue_notice` (giữ `work_order`) **trước** cha. Rollback `AddScheduledJobs`: xem `docs/agents/traps/migrations.md`, mục Rollback dựng lại CHECK cũ.
- 🔴 **Mọi lời gọi DB của worker nằm TRONG vòng retry, kể cả bước tạo dòng lúc khởi động.** Exception thoát khỏi
  `BackgroundService.ExecuteAsync` với mặc định `StopHost` **dừng cả API** — DB khởi động chậm hơn API là đủ (review Codex F-1). Canh bằng
  `A_database_unavailable_at_startup_does_not_end_the_worker` (khoá bảng + `Command Timeout` 1 s; phá thử → đỏ).
- Test worker đợi **dòng `scheduled_job`** đạt trạng thái cuối rồi mới `StopAsync`: tín hiệu từ job đến TRƯỚC lượt `CompleteAsync`, và dừng
  sớm sẽ hủy chính lượt ghi đó (F-2).
- Job dùng `UtcMicrosecondClock` một lần mỗi lượt; một lượt quá hạn xử lý tối đa `Batch` (500) phiếu mỗi vòng, lặp tới khi hết.
