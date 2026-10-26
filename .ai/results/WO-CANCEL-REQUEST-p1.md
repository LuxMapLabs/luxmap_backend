# WO-CANCEL-REQUEST Phase 1 — kỹ sư xin hủy / từ chối nhận lịch (26/10/2026, Codex)

## Hướng chung

**Khảo sát đã xong; toàn bộ thiết kế dưới đây là ĐỀ XUẤT, chưa được Mỹ duyệt. Dừng ở Phase 1.**
Theo khuôn BE-27-p1: hướng chung → API → người nhận → lược đồ → kế hoạch Phase 2 → D-item.
Chỉ ghi file này theo yêu cầu riêng của ticket/người dùng; không cập nhật tracking, task, Contract, drift, code hoặc test; không commit.

Khuyến nghị để Mỹ cân nhắc: trạng thái phụ hiện tại trên `work_order`, lịch sử trong audit append-only; xin khi
`assigned`, áp cả ba task kind; dùng hai capability hiện có và thông báo polling. Không đổi `wo_status`.
Đây là phương án nhỏ nhất, nhưng chưa giải quyết nhu cầu lịch sử hiển thị như một tài nguyên riêng hay quyết định từ màn hình cũ.
Nếu hai nhu cầu đó bắt buộc, cần chốt lại mô hình trước Phase 2 (D-1, D-9).

Đã đọc các nguồn ticket yêu cầu: CLAUDE.md; sources/commands/README của .ai; các trap faults-workorders, migrations,
commune-scope, auth-accounts; toàn bộ WorkOrderService/Rules/Controller/Requests/Responses/Notices/entity/configuration;
Notifier/Notification/configuration; AuditEvent/configuration/guard; SyncContracts/PushService, đường bundle;
Contract §5.5, §5.8, §5.9, changelog; drift WO-*, FR-*, WSC, BE-27 N-*; BE-27-p1, BE-26-p1, BE-23-p1.
Đọc thêm ContractEnum, migration precedent và ERD Q6/Q7 để đánh giá bảng riêng và khảo sát liên xã.
BE-23-p1 có phần đầu bị chặn lịch sử; phần chốt bổ sung cuối file và code hiện tại mới giải thích nền đang chạy.
Các quyết định WO/FR/WSC và thông báo có phần SELF-SIGNED: nền tạm tới FW, không coi đề xuất FE là quyết định.

## Hiện trạng đã đối chiếu

| Chỗ | Kết quả từ code / tài liệu |
|---|---|
| WorkOrderRules | Kỹ sư được giao chỉ có start/complete; Manager có assign/unassign/edit/cancel ở open/assigned/in_progress, verify/return ở done. Không có bước xin hủy |
| Assign | Cùng người → 200 no-op; đổi người/bỏ giao đặt assigned/open, xoá started_at; không xoá ảnh, không đảo fault đã in_progress |
| PATCH | Chỉ đổi chi tiết; đổi scheduled_date/due_date được nhận diện riêng bằng rescheduled; đổi tên/materials không phải dời lịch |
| Act | start → in_progress; complete → done; return → in_progress; verify → verified; cancel → cancelled. Cancel/return cần note; repair complete cần ảnh after |
| Đồng thời | WorkOrder.Version map xmin; Save bắt DbUpdateConcurrencyException, rollback nếu có transaction, clear tracker rồi Find lại qua cả commune/assignee filter; trả 409 CONCURRENT_MODIFICATION hoặc 404 nếu mất quyền thấy |
| Audit | WorkOrder, WorkOrderFault, WorkOrderSegment là IAudited. AuditWriteGuard yêu cầu đúng một event cho một SaveChanges có entity được audit; không phải một event mỗi entity. Snapshot là danh sách trường tường minh |
| Thông báo | Stage trên context caller; cùng SaveChanges. Title/body cắt 200/1000 qua Clip, an toàn surrogate; không báo chính actor; mỗi người nhận một row |
| Quản lý | ManagersCoveringAsync lọc manager, !IsLocked, PasswordSetAt != null và đủ xã truyền vào. Truyền xã neo của WO theo mẫu Completed hiện tại |
| Offline | SyncOpType hiện chỉ có fault_report/lux_reading/pole_note/work_order_start/work_order_complete. Unknown op bị rejected; không tự được hỗ trợ khi thêm endpoint |
| Cấu trúc đọc | ItemsAsync dùng chung list/agenda; Detail dựng riêng; bundle gọi Detail và chỉ lấy phiếu assigned/in_progress của kỹ sư |
| CHECK | Snapshot có ck_notification_type, ck_audit_event_action, ck_sync_operation_op_type. Tên/giá trị thật ở bằng chứng bên dưới |
| Tức thời | Contract §5.9 và drift BE-27 N-1 dùng polling 30–60 giây khi app mở; chưa có đảm bảo push hoặc realtime |

## Mô hình lưu và lược đồ đề xuất

### Phương án A — năm cột trên work_order, audit giữ lịch sử (khuyến nghị D-1)

Các tên dưới đây là **tên mới FE đề xuất**, chưa tồn tại trong code/snapshot, chưa tạo migration:

| Cột đề xuất | Lưu trữ / ý nghĩa |
|---|---|
| cancellation_request_status | text NOT NULL, mặc định none, tập mới none/pending/rejected |
| cancellation_request_note | text nullable; lý do đã trim của lần xin hiện tại |
| cancellation_requested_at | timestamptz nullable; giờ server, UtcMicrosecondClock, UTC |
| cancellation_reject_note | text nullable; lý do bác của lần hiện tại |
| cancellation_rejected_at | timestamptz nullable; giờ server bác |

Entity enum riêng cho sub-state, không thêm vào WorkOrderStatus; HasContractEnum sinh CHECK theo quy ước
`ck_<table>_<column>` (tên dự kiến `ck_work_order_cancellation_request_status`, **chưa tồn tại**).
CHECK nghiệp vụ mới, tên sẽ chốt khi duyệt: none → cả bốn trường còn lại NULL; pending → note/at có, reject note/at NULL;
rejected → đủ cả bốn, rejected_at >= requested_at. Lý do phải không trắng và trong giới hạn; CHECK ràng buộc trạng thái phụ
với wo_status theo D-2/D-3. Viết điều kiện IS NOT NULL tường minh để NULL không lọt CHECK.
Nếu theo khuyến nghị assigned-only: pending/rejected chỉ tồn tại khi assigned; start sẽ reset về none.

Giá: migration thêm năm cột + default/backfill none cho dòng cũ + CHECK; sửa entity/configuration, snapshot EF;
không bảng, FK hay prefix mới. Mỗi xin/bác/reset thật sự đổi UpdatedAt để xmin đổi, stage đúng một audit chung.
Mở rộng WorkOrderSnapshot và hàm Snapshot với **đủ năm trường** trên mọi đường ghi, trước và sau reset.
Actor xin/bác giữ ở audit ActorUserId/Role, lý do đầy đủ ở Note và snapshot; năm cột không đủ tự trả requested_by/rejected_by.
Không hứa có API lịch sử: audit chưa có endpoint đọc. Reset xoá bản hiện tại nhưng audit trước đó vẫn giữ nguyên.

### Phương án B — bảng riêng, mỗi lần xin một hàng

Hợp nếu WP5/WP6 cần timeline nhiều lần, tác giả và quyết định truy vấn được trực tiếp.
Cần định danh lần xin, FK tới work_order cùng commune, người xin/người quyết định, lý do/mốc giờ,
trạng thái xử lý và chỉ một lần pending mỗi phiếu. Chưa đặt tên bảng/cột/constraint hay enum trạng thái lịch sử mới khi Mỹ chưa chọn.
Tập none/pending/rejected của FE là trạng thái **hiện tại**, chưa đủ diễn đạt lịch sử đã duyệt bằng giao lại/hủy hoặc tự hết hiệu lực:
phải chốt cách lưu kết quả đó, không âm thầm thêm enum.

Giá: migration bảng + FK Restrict + index đầy đủ/unique partial; PLAN copy_dev_to_supabase, teardown,
join/projection list/agenda/bundle. Nếu bảng được truy vấn gốc, mang ICommuneScoped, HasCommuneScope,
HasCommuneReference và commune lấy từ WO; FK ghép giữ đúng xã, quyền assignee vẫn phải neo qua WO.
Bảng quyết định nghiệp vụ nên IAudited, chụp Snapshot con trong event của WO; sửa cả WO để xmin phân xử với Assign/Act/PATCH
(nếu chỉ insert con thì xmin WO không bảo vệ race). Một SaveChanges vẫn đúng một audit chung.
HasContractEnum + CHECK cho trạng thái mới; nếu công bố endpoint lịch sử sẽ vượt hai endpoint và vượt phép đếm 141 bên dưới.
Không khuyến nghị vừa năm cột vừa bảng lịch sử trừ khi đo/chứng minh cần denormalize.

## Máy trạng thái phụ đề xuất (D-2…D-5)

Bảng sau là **phương án đề nghị duyệt**, không mô tả hành vi đang có.

| Sự kiện | none | pending | rejected | Quy tắc |
|---|---|---|---|---|
| Kỹ sư xin | → pending | 409 INVALID_STATE_TRANSITION | → pending | Chỉ người đang được giao; assigned theo D-3; reason mới bắt buộc; xin lại thay note/at, xoá reject note/at |
| Quản lý bác | 409 | → rejected | 409 | reject_reason bắt buộc; giữ nguyên wo_status/người/ngày; ghi mốc bác |
| Assign đổi người | → none | → none | → none | Reset đủ cả năm trường trong cùng audit Reassigned; giữ mọi hành vi gốc |
| Unassign thật | → none | → none | → none | Reset đủ trường trong audit Unassigned, WO về open theo code cũ |
| Assign cùng người | giữ | giữ | giữ | Giữ no-op hiện có; không dùng no-op để duyệt hoặc xoá yêu cầu |
| Cancel | → none | → none | → none | Duyệt hủy hẳn dùng cancel hiện có; reason/note vẫn bắt buộc; audit Cancelled, không thêm event thứ hai |
| PATCH ngày thật sự đổi (kể cả đặt/xoá NULL) | → none | → none | → none | Đề nghị coi là lịch mới; audit DetailsChanged ghi trước/sau; notice Rescheduled hiện có |
| PATCH chỉ title/materials hoặc ngày giống cũ | giữ | giữ | giữ | Không kết thúc yêu cầu; no-op vẫn no-op |
| Start | → none | → none | → none | Đề nghị vẫn cho bắt đầu và coi là tự rút yêu cầu; giữ luồng start cũ, không thêm trạng thái WO |
| Complete → done | → none | → none | → none | Không chặn hoàn thành; nếu D-3 cho in_progress thì đây là đường hết hiệu lực quan trọng; giữ yêu cầu report/after/outcomes gốc |
| Verify → verified | → none | → none | → none | Theo invariant done đã none; reset phòng thủ trong cùng audit Verified |
| Return done → in_progress | giữ none | không phải trạng thái hợp lệ | không phải trạng thái hợp lệ | Không đổi hành vi return; không hồi sinh yêu cầu cũ |
| Tạo WO/follow-up mới | none | — | — | Default none, không sao chép yêu cầu từ cha |

Ngoài ba self-loop no-op ở bảng, không có none → rejected hay rejected → rejected qua endpoint bác;
không có endpoint rút đơn hoặc approve mới. Các trạng thái open/done/verified/cancelled không được xin.
Reject chỉ thay sub-state, không tự dời ngày/bỏ giao. Reset là xoá dữ liệu hiện tại, không sửa audit cũ.
Unassign và dời lịch là hết hiệu lực theo đề xuất, không nên hiển thị như Manager đã “chấp thuận hủy hẳn”.

**Xin khi in_progress:** FE muốn cho phép, nhưng Assign vốn xoá started_at dù kỹ sư đã làm; ảnh before/after/observation
vẫn thuộc WO, vẫn có uploaded_by, không bị xoá; một ảnh after cũ vẫn thoả Any ở complete của lần giao sau.
Fault đã in_progress không được rollback bởi Assign/cancel. Phải chấp nhận rõ ý nghĩa bằng chứng của nhiều người/lần làm
nếu bật phạm vi này. Khuyến nghị chỉ assigned ở lượt đầu; nếu Mỹ chọn cả in_progress thì dùng các reset start/complete ở bảng,
giữ nguyên ảnh/fault/lịch sử và không sửa quy tắc hoàn thành ngoài phạm vi ticket.

**Survey:** đề nghị áp assigned như inspection/repair (D-4). Phiếu có thể có tuyến liên xã nhưng neo một xã;
Assign đã RequireSurveyAssignee kiểm tuyến. Xin hủy không huỷ sweep, không xoá video/lux/GPS/kết quả; các module khác không được
bị thêm project reference ngược. Nếu cho in_progress, phải chốt ảnh hưởng sweep uploading/processing và người quay sau giao lại;
SurveyIngest hiện yêu cầu WO in_progress. Không tự thêm thao tác xoá/huỷ sweep vào ticket.

### Xử lý đồng thời

Theo A, dùng xmin/Save hiện có (D-9), không cần tạo cơ chế khoá khác chỉ vì có hai action mới.
Request và reject phải Find qua filter, kiểm quyền assignee/state trong service, cập nhật WO thật và một SaveChanges với audit/notices.

- Manager cancel trước: request đọc sau thấy cancelled → 409; nếu đã đọc cũ thì Save thua → 409 CONCURRENT_MODIFICATION.
- Manager giao lại/bỏ giao trước: kỹ sư cũ đọc sau hoặc Find lại sau concurrency → 404 WORK_ORDER_NOT_FOUND.
- Request thắng trước: Manager đọc trạng thái mới và thao tác → reset none; nếu Manager đã đọc cũ thì 409, cần tải lại rồi quyết định.
- Hai request hoặc hai reject cùng đọc: một commit, một concurrency conflict; không để audit/thông báo của bên thua tồn tại.
- Request đua PATCH/start/complete: cùng hàng WO nên một bên thua khi cùng phiên bản; nếu reset chạy sau commit request thì kết thúc request.
- Rollback transaction trước mọi truy vấn lại; giữ cả commune + assignee filter, không trả nội dung lý do cho người đã mất quyền.
- Nếu chọn B, bắt buộc update WO hoặc một cơ chế đồng bộ chung được duyệt cho **mọi** writer; chỉ unique pending không đủ.

**Giới hạn cần Mỹ chốt:** xmin xử lý hai request đang chạy, không ngăn Manager bấm “bác” từ màn hình cũ sau vòng
pending → rejected → pending mới. Năm cột không có request ID/version của quyết định phía client.
Có thể bổ sung điều kiện mốc cancellation_requested_at mà client đã xem vào reject (tên request mới chưa chốt), hoặc chọn B
với ID lần xin; nhưng duyệt qua /assignee và /cancel hiện có cũng không mang định danh lần xin.
Không âm thầm đổi body/hành vi các endpoint đó ngoài reset. D-9 phải chốt chấp nhận semantics “thao tác lên WO hiện tại”
hay mở ticket riêng cho điều kiện chống màn hình cũ. Tải lại UI giảm rủi ro, không phải bảo đảm nguyên tử.

## API đề xuất và đọc danh sách (D-6…D-8)

Base /api/v1, snake_case; hai endpoint dưới đây do FE đề xuất, **chưa tồn tại**:

| Endpoint | Capability hiện có | Request | Response đề nghị |
|---|---|---|---|
| POST /work-orders/{id}/request-cancellation | ExecuteWorkOrders = field_engineer | {reason} | 200 WorkOrderDetail |
| POST /work-orders/{id}/reject-cancellation | ManageWorkOrders = manager | {reject_reason} | 200 WorkOrderDetail |

Không thêm capability nếu Mỹ đồng ý ma trận trên. Endpoint gắn policy riêng từng action; không AllowAnonymous.
Service kiểm lý do/quyền được giao/sub-state/wo_status vì có thể có caller khác controller.
DTO mới sealed class + init. Các trường hiện tại do server giữ, không cho Create/PATCH dùng để set cờ.

Đề nghị **trim 1..1000 UTF-16 code units** cho cả reason/reject_reason, kiểm trong service và tương thích DataAnnotations.
Đây là giới hạn mới cần WP5/WP6 xác nhận, không suy từ độ dài title 200.
Lưu lý do đầy đủ tới giới hạn đó trong WO/audit; notification body có mã/tên/nhãn cộng lý do nên sẽ bị Clip 1000,
title Clip 200. Không cắt lý do lưu DB để vừa thông báo. Không so số UTF-16 của .NET với char_length Postgres như cùng một đơn vị;
emoji biên phải có test. CHECK độ dài DB hỗ trợ chặn thô, validator giữ nghĩa API.
Whitespace-only/null/thiếu/quá dài → 400 VALIDATION_FAILED. Không dùng REQUIRED mới hay mã lỗi mới khi mã chung đủ.

- 401 khi chưa xác thực; 403 ROLE_FORBIDDEN khi sai capability; 404 WORK_ORDER_NOT_FOUND khi ngoài xã/không được giao/không tồn tại.
- 409 INVALID_STATE_TRANSITION nếu status/sub-state không cho phép; details gồm wo_status, cancellation_request_status và allowed_actions phù hợp caller.
- 409 CONCURRENT_MODIFICATION dùng Save hiện có; sau giao lại có thể là 404 theo bộ lọc.
- Xin lặp lúc pending và bác lặp sau rejected: đề nghị 409, không sinh thông báo/audit thêm. Retry HTTP mất response phải GET detail
  để biết trạng thái; không hứa idempotency xuyên nhiều vòng xin lại. Nếu cần client_op_id thì D-10 phải mở lại thiết kế.

**Response:** đề nghị thêm cả năm trường vào WorkOrderItem, nhờ đó list/detail/agenda đồng nhất; Detail dựng riêng phải điền đủ.
Lý do có trong list giúp banner không gọi N detail, đổi lại payload lớn và mọi vai trò ReadWorkOrders trong phạm vi đều đọc được
(superior/system_admin cũng chỉ đọc). Phương án ít dữ liệu hơn: item chỉ status/requested_at, lý do ở detail; D-6 chốt trước triển khai.
Không thêm requested_by/rejected_by trong API nếu chưa có nguồn lưu/query được duyệt.
Bundle nhận trường qua WorkOrderDetail; cache offline phải hiển thị “dữ liệu tại thời điểm generated_at”.

**Lọc:** thêm `GET /work-orders?cancellation_request_status=pending` (một giá trị, vắng = mọi trạng thái; sai → 400),
kết hợp AND với commune/task_kind/assigned_to/date/status/page hiện có, giữ sort và pagination.
Badge dùng total của truy vấn pending, không dùng unread_count: notification đã đọc không có nghĩa đã xử lý.
Không áp bộ lọc ngày lịch vào truy vấn đếm toàn bộ pending nếu banner phải nhắc cả lịch tương lai/không ngày.
Chưa cần endpoint count mới; hai POST vẫn là hai operation tăng thêm.

**allowed_actions:** chỉ ở detail hiện tại; đề nghị thêm `request_cancellation` cho kỹ sư đang giữ WO ở assigned
và sub-state none/rejected; thêm `reject_cancellation` cho Manager khi pending.
Manager vẫn dùng assign/unassign/edit/cancel như cũ; không thêm approve action. Kỹ sư pending vẫn có start theo D-2.
Superior/system_admin không có action ghi; rules và service phải dùng cùng điều kiện, không chỉ ẩn nút.
Không thêm AgendaFlag mới (enum đã publish).

## Thông báo và audit

Hai NotificationType mới **chỉ đề xuất**:
`WorkOrderCancellationRequested` → `work_order_cancellation_requested`;
`WorkOrderCancellationRejected` → `work_order_cancellation_rejected`.
EntityType vẫn WorkOrder, không thêm giá trị. WorkOrderNotices sở hữu câu chữ, dùng Notifier.Stage/Clip sẵn có.

| Sự kiện | Người nhận | Title/body tiếng Việt đề nghị |
|---|---|---|
| Xin | ManagersCoveringAsync(db, [wo.CommuneId], ct) | “Phiếu {id} có yêu cầu xin hủy” / “{id} — {title}. Kỹ sư xin hủy / từ chối nhận lịch. Lý do: {reason}” |
| Bác | wo.AssignedTo hiện tại | “Yêu cầu xin hủy phiếu {id} bị bác bỏ” / “{id} — {title}. Quản lý bác bỏ yêu cầu. Lý do: {reject_reason}” |
| Giao lại/hủy/dời lịch | Người nhận hiện có | Dùng Assigned/Unassigned/Cancelled/Rescheduled hiện có, không thêm notice “approved” |

Stage trong cùng SaveChanges với WO + audit; một now theo microsecond; loại actor và trùng recipient theo Notifier.
Nếu không có manager hoạt động phủ xã neo, Stage không tạo dòng; đề nghị vẫn lưu pending và audit để quản lý tương lai lọc thấy,
không báo đã “gửi tới quản lý”, không fallback sang system_admin/cấp giám sát/email. Đây là D-11, chưa quyết.
Với survey liên xã, đề nghị cùng người nhận Completed của WO (xã neo), không đánh đồng với notification duyệt sweep cần đủ mọi xã run.
Nếu FE muốn yêu cầu chỉ tới Manager có toàn bộ tuyến, phải chốt khác biệt ở D-4.

Hai AuditAction mới **chỉ đề xuất**: `CancellationRequested` / `CancellationRejected`
(wire lưu `cancellation_requested` / `cancellation_rejected`).
Các action Requested/Rejected/Submitted đã tồn tại nhưng không chỉ rõ nghiệp vụ xin hủy trong enum comments; có thể tái dùng
kèm entity/before/after, đổi lại người đọc phải suy nghĩa. Khuyến nghị hai action tường minh, Mỹ chọn ở D-12.
AuditActorKind.User, AuditEntityType.WorkOrder, actor/role thật, commune của WO, note là lý do đầy đủ.
Xin/bác không đổi fault và liên kết. Reset đi cùng event action gốc (Reassigned/Unassigned/Cancelled/DetailsChanged/Started/Completed/Verified),
không thêm event thứ hai vi phạm AuditWriteGuard.

### Hai thay đổi CHECK cần migration — dự kiến Up/Down, CHƯA viết/chạy

Mốc gốc lấy đúng snapshot tại bằng chứng E2, không chép CHECK cũ từ BE-27/BE-26 làm mất citizen_report_received/relocated.
“Hai migration CHECK” hiểu là hai thay đổi schema bắt buộc, có thể gộp cùng migration lưu năm cột cho một tính năng;
nếu Mỹ muốn tách hai migration vật lý, thứ tự và rollback vẫn như sau. Tên migration chưa chốt.

| Phần | Up dự kiến | Down dự kiến |
|---|---|---|
| notification | DropCheckConstraint ck_notification_type trên notification; AddCheckConstraint cùng tên với toàn bộ tập hiện tại E2 cộng work_order_cancellation_requested, work_order_cancellation_rejected | Drop cùng CHECK mở rộng; Add lại **đúng** tập hiện tại E2 (giữ citizen_report_received, work_order_overdue) |
| audit | DropCheckConstraint ck_audit_event_action trên audit_event; Add lại toàn bộ tập E2 cộng cancellation_requested, cancellation_rejected nếu D-12 chọn action mới | Drop CHECK mở rộng; Add lại **đúng** tập E2, giữ relocated cùng các action citizen/lighting |
| lưu A | Add năm cột, default none, CHECK enum và invariant mới; không thêm xmin vật lý, không DropIndex/AlterColumn ngoài yêu cầu | Bỏ CHECK/index mới nếu có, bỏ năm cột theo thứ tự phụ thuộc; mất bản hiện tại của yêu cầu, audit giữ nguyên |

Nếu D-12 chọn tái dùng audit action thì không cần nới CHECK audit; phải ghi lại phạm vi đã duyệt trước sinh migration.
Designer/model snapshot phải khớp runtime enum/configuration. Không đổi ck_work_order_wo_status hoặc ck_work_order_task_kind.
Không copy toàn Up/Down của AddScheduledJobs/AuditPoleRelocation vì chúng chứa bảng/cột/entity type ngoài phạm vi.

**Rollback:** Down thu hẹp CHECK sẽ thất bại nếu đã có notification/audit mới. Audit append-only không được xoá/sửa để ép rollback;
ưu tiên rollback ứng dụng nhưng giữ schema cộng thêm, hoặc forward fix. Apply/down/reapply chỉ kiểm trên DB test riêng,
Down khi không có dữ liệu enum mới; thêm ca có dữ liệu mới phải từ chối rollback nguyên tử.
Nếu chọn B, Down còn mất lịch sử bảng con: phải có chấp thuận và kế hoạch giữ dữ liệu trước triển khai.
Phase 1 không kết nối DB để xác nhận schema thực tế; snapshot là bằng chứng repo, không phải catalog của môi trường nào.

## Offline WP6 (D-10)

Thêm hai endpoint không làm hàng chờ hiện tại hiểu xin hủy. Nếu muốn sync tự động như start/complete,
cần một loại sync nghiệp vụ mới, parser/payload/PolicyFor/target work_order/server_state/replay và
nới **ck_sync_operation_op_type** qua migration; gọi đúng service gốc, stage sync_operation cùng SaveChanges.
Đây là thay đổi thêm tập enum/op_type đã công bố ngoài hai ngoại lệ của ticket.
**Không tự thêm hay coi đã được cho phép** trong Phase 2 hiện tại; ghi D-10 để Mỹ mở phạm vi/ticket riêng nếu cần.

Khuyến nghị trong phạm vi hiện nay: xin/bác chỉ online; WP6 có thể lưu nháp lý do, nhưng chưa có mạng thì chỉ ghi “chưa gửi”,
không đổi cache thành server pending, không cho vào /sync/push bằng op_type giả hoặc mượn work_order_start.
Người dùng vẫn được làm ca offline; thao tác xin quyết định quản lý chưa có hiệu lực cho tới server nhận.

BLOCKED_BY_EARLIER_OP chỉ là bộ nhớ trong **một batch**, áp sau Parse cho target đã biết và được đánh dấu khi Apply thất bại.
Unknown op/parse lỗi/capability bị từ chối đi continue sớm, không tự đánh dấu target blocked.
Do đó gửi op lạ “xin hủy” rồi start/complete không bảo đảm những bước sau bị chặn.
Gọi POST online ngoài batch cũng không tham gia blocked dictionary.

WP6 phải xử lý thứ tự: start đang chờ upload thì gửi start thành công trước khi đánh giá xin hủy; với assigned-only,
server đã in_progress sẽ từ chối xin. Không gửi xin trước rồi đẩy start một cách vô tình vì theo D-2 start reset pending.
Complete đã offline hoặc đang chờ sync: không khẳng định pending sẽ giữ phiếu; kết quả server/404/409 là chuẩn.
Nếu cần hàng chờ xin hủy thực thụ, D-10 phải chốt replay, thứ tự cùng WO, lỗi blocker, đồng bộ hai kênh và CHECK thứ ba,
không chỉ thêm một nhánh switch.

## Tác động Contract và bàn giao

- §5.5: hai endpoint/body/response; sub-state, reset/no-op, phạm vi assigned/in_progress/task kind;
  query lọc, trường list/detail/agenda, allowed_actions, cùng các giới hạn màn hình cũ.
- §5.9: hai type, trigger/recipient/nội dung và polling; fallback type lạ vẫn hiển thị body/title.
- §0: bảng lỗi và giới hạn lý do; §2: gắn endpoint với hai capability hiện có, không đổi ma trận vai trò.
- §5.8: bundle thêm trường detail và giới hạn xin online; chỉ thêm op_type nếu được duyệt mở phạm vi.
- §1 nếu thêm mô tả enum phụ mới; §10 changelog/version phải được duyệt, không tự coi v1.19 đã chốt.
  Lưu tên file OpenAPI hiện hành theo generator, không tự đổi filename.
- OpenAPI kiểm đếm thực: **139 → 141 operation** nếu chỉ thêm hai POST. Field/query/type mới không tăng operation;
  nếu thêm lịch sử/count/realtime endpoint thì phải tính lại và xin chốt phạm vi.
- Phase 2 sau duyệt: xuất luxmap-v1.json → generator hợp nhất (kiểm schema viết tay đè DTO) → lint.
  Cập nhật schema WorkOrderItem/Detail/AgendaWorkOrder/SyncBundle, request/error/NotificationType, mô tả reset.
- Drift **cần ghi sau duyệt**, chưa sửa file drift ở lượt này: đề nghị entry WO-CANCEL-REQUEST với CR-1 lưu trữ;
  CR-2 sub-state/reset/phạm vi; CR-3 API/filter/actions; CR-4 notices/audit; CR-5 offline;
  CR-6 realtime và stale decision. Mã CR-* chỉ là nhãn đề xuất, chưa đăng ký.
- WP5 xác nhận filter/total banner (pending khác unread), trường list hay detail, giới hạn lý do, no-op/giao lại/dời lịch,
  stale màn hình, thông báo polling đủ hay realtime bắt buộc.
- WP6 xác nhận các action/status/task kind, lý do/bác/retry/giờ server, quy tắc online và nháp,
  thứ tự với queue start/complete, refresh bundle khi giao lại, khả năng đọc NotificationType mới.
- Mỹ chốt kỹ thuật/nghiệp vụ và FW xác nhận bề mặt; im lặng với thay đổi API là ESCALATE.
  Không gửi tin cho WP5/WP6 trong lượt này, chỉ liệt kê nội dung bàn giao.

## Kế hoạch Phase 2 sau khi chốt — CHƯA viết/chạy test

Đọc trap testing trước triển khai; host tích hợp LuxMapTestHost, PostGIS riêng lượt chạy, dữ liệu/actor riêng,
AssetDatabaseCollection nếu fixture ghi tài sản; không dùng luxmap_dev. Dưới đây là **tên test dự kiến**, không phải test hiện có.

| Tên dự kiến | Ca cần chứng minh | Phá thử dự kiến (chỉ Phase 2) |
|---|---|---|
| Request_cancellation_requires_assignee_commune_and_execute_capability | 401, ba role sai 403; kỹ sư khác/xã khác 404; đúng người assigned thành công | Gỡ kiểm assignee; literal role/metadata vẫn phải bắt |
| Request_and_reject_validate_trimmed_reason_in_service | thiếu/null/trắng/quá 1000; 1/1000; gọi service trực tiếp; emoji tại biên | Bỏ validate service, HTTP DataAnnotations không được che test |
| Cancellation_substate_has_only_approved_edges | none→pending→rejected→pending; request pending/reject none hoặc rejected 409; các WO status bị cấm | Bỏ guard pending hoặc cho xin ở done |
| Request_scope_covers_each_approved_task_kind | inspection/repair/survey; khảo sát liên xã và điều kiện người nhận đã chốt | Loại survey hoặc nới in_progress trái D-3 |
| Reassignment_and_unassignment_reset_all_request_fields | cả pending/rejected; đúng audit trước/sau; người cũ 404; cùng người no-op giữ cờ | Bỏ reset ở Assign hoặc bỏ 1 trường, hoặc reset cùng người |
| Rescheduling_resets_only_when_dates_really_change | due/scheduled đặt/xoá/đổi; title/materials/no-op giữ; Rescheduled hiện hữu | Reset mọi PATCH hoặc quên due_date |
| Start_complete_cancel_verify_keep_existing_rules_and_clear_substate | reset; repair vẫn cần after; report/outcome; fault/released_at không đổi ngoài luật cũ; return không hồi sinh | Bỏ reset ở Act; hoặc bỏ guard after (test phải đỏ vì đúng chốt) |
| In_progress_reassignment_keeps_existing_evidence_if_approved | chỉ cần nếu D-3 mở rộng; before/after cũ, uploaded_by, started_at reset, fault in_progress giữ | Xoá ảnh khi reset hoặc âm thầm đảo fault |
| Cancellation_notices_have_correct_recipients_and_safe_text | active/locked/unset password/thiếu xã/không manager; actor loại trừ; nội dung tiếng Việt và emoji ≤200/1000 | Gỡ active filter hoặc Clip; assert số notice theo người, không chỉ tổng |
| Cancellation_audit_is_atomic_and_snapshots_every_field | đúng một event xin/bác; action/actor/Note/before/after/microseconds; reset trong action gốc | Bỏ trường snapshot hoặc stage event thứ hai |
| Concurrent_request_and_cancel_or_reassign_leave_only_winner_effects | giữ giao dịch có kiểm soát, hai request/reject; WO/audit/notice bên thua không còn; 404 sau mất assignee | Gỡ xmin hoặc tách SaveChanges notice; đọc mã thoát |
| Pending_filter_and_read_projections_agree | total ngoài trang, ngày NULL/tương lai/xã; list/detail/agenda/bundle đủ trường theo D-6 | Bỏ filter hoặc bỏ projection Detail/ItemsAsync |
| Unsupported_cancellation_sync_op_is_rejected_without_false_blocking | online-only, unknown op không biến thành applied; start/complete sau nó theo luật batch thực | Thêm nhánh giả áp op mà không gọi service hoặc đoán blocker không đúng code |
| Pending_request_and_offline_start_have_the_approved_ordering | queue start thắng/reset; conflict trả server detail/null; replay start không sinh notice trùng | Bỏ reset service khi sync gọi Act |
| Cancellation_schema_and_rollback_preserve_prior_enum_values | CHECK mới từ chối trạng thái/lý do sai; giữ toàn enum E2; default dòng cũ; không xmin vật lý/mất index | Bỏ CHECK mới hoặc dùng CHECK BE-27 cũ làm mất type đã thêm |
| Narrowing_checks_refuses_existing_new_audit_and_notice_values | down sạch được; dữ liệu enum mới khiến rollback từ chối nguyên tử; audit không bị sửa/xoá | Bỏ CHECK hẹp hoặc lách append-only |
| Stale_manager_decision_follows_the_approved_contract | vòng xin/bác/xin rồi bấm quyết định từ màn hình cũ, theo D-9 | Gỡ precondition nếu đã được duyệt, hoặc assert semantics WO hiện tại |

Kiểm bổ sung Phase 2: CapabilityPolicyCoverageTests, ma trận literal, JSON enum số bị từ chối, OpenAPI lint;
build --disable-build-servers; test chọn vùng phù hợp rồi kiểm regression theo phạm vi đã đổi.
Mỗi chốt mới có sabotage → test đỏ → khôi phục cp/touch → test xanh, lưu exit code/output thật.
Migration đọc Up/Down trước apply, chỉ thử catalog/apply/down/reapply trong DB test khi đã được phép.
Không phải kế hoạch chạy bất kỳ test/build/migration nào ở Phase 1 này.

## D-item cần Mỹ chốt

| # | Câu hỏi / nghi vấn | Đề xuất, chưa chốt |
|---|---|---|
| D-1 | Năm cột hay lịch sử bảng riêng? Có cần timeline/tác giả qua API? | A + audit giữ lịch sử; nếu cần truy vấn lịch sử độc lập thì B và thiết kế kết quả xử lý đầy đủ trước code |
| D-2 | Mọi cạnh/reset; pending có chặn start/complete không? | Theo bảng: start/complete cho tiếp tục rồi reset; giao/bỏ giao/cancel/ngày thật đổi reset; no-op/tên/materials giữ |
| D-3 | Xin chỉ assigned hay cả in_progress? Chấp nhận giữ ảnh after cũ qua giao lại? | Chỉ assigned cho lượt đầu; mở in_progress phải chấp nhận rõ started_at/ảnh/fault hiện có, không đổi hành vi gốc |
| D-4 | Survey và người quản lý tuyến liên xã? | Áp survey assigned; báo manager xã neo theo Completed; không huỷ sweep/ảnh/video/lux/GPS, giữ RequireSurveyAssignee |
| D-5 | rejected có xin lại được, có thời gian chờ/giới hạn số lần không? | Cho xin lại với lý do mới, không tự thêm cooldown; lịch sử audit; pending lặp 409 |
| D-6 | Trường response và riêng tư lý do? | Cả năm trường trên item/detail/agenda/bundle; mọi reader trong scope đọc được; nếu không chấp nhận thì item chỉ status/at, detail có lý do |
| D-7 | Endpoint/filter/action/error/capability/độ dài lý do? | Hai POST, filter một status, hai allowed_actions mới; reuse capability và mã 409/400; trim 1..1000 UTF-16, service validate |
| D-8 | “Tức thời” có bắt buộc push/realtime? | Polling 30–60 s theo Contract; badge theo total pending. Nếu cần realtime, đặc tả/kênh riêng trước mở phạm vi |
| D-9 | Race và quyết định từ màn hình cũ qua nhiều vòng xin? | xmin cho race đang chạy; Mỹ cần chốt semantics thao tác trên WO hiện tại hay yêu cầu precondition/request ID. Không tự thêm điều kiện vào assignee/cancel ngoài reset |
| D-10 | Xin có bắt buộc offline không? Ticket cấm mở enum khác nhưng sync cần op/CHECK thứ ba | Online-only trong phạm vi hiện tại; nháp WP6 chưa gửi; nếu cần sync thì Mỹ cho phép mở phạm vi/ticket riêng, chốt replay/order trước |
| D-11 | Không có Manager đang hoạt động phủ xã thì có nhận xin không? | Vẫn lưu pending/audit; không thông báo giả, không fallback role khác; quản lý có thể lọc sau |
| D-12 | AuditAction mới hay reuse Requested/Rejected? Hai migration CHECK tổ chức thế nào? | CancellationRequested/CancellationRejected tường minh; nới hai CHECK cùng thay đổi lưu A, không thay CHECK entity_type; Down hẹp không chạy khi có dữ liệu mới |
| D-13 | Ngày mới có thật sự giải quyết xin hủy hay cần giữ pending? | Reset khi due/scheduled thật đổi theo D-2, nhưng chỉ gọi là hết hiệu lực; WP5/WP6 cần đồng ý vì không phải hai cách “duyệt” FE nêu |
| D-14 | Ký Contract/drift và xử lý nền SELF-SIGNED trước Phase 2? | Mỹ chốt D-item rồi đăng ký đúng tầng, tăng version sau duyệt, WP5/WP6/FW xác nhận bề mặt; không coi bản khảo sát này là quyết định đã ký |

## Bằng chứng chạy thật

Các lệnh chỉ đọc file local/git; không HTTP/API, không kết nối DB, không psql/docker/dotnet.
Output dưới đây được chép nguyên từ kết quả tool, không phải output mô phỏng hay kết quả test.
E1 là trạng thái **trước khi ghi báo cáo**; task untracked đã có từ đầu, không do lượt này tạo.

### E1

Lệnh:

```sh
git branch --show-current; git status --short; git diff --stat
```

Output thật (exit code 0):

```text
docs/WO-CANCEL-REQUEST-p1
?? .ai/tasks/WO-CANCEL-REQUEST.md
```

### E2

Lệnh:

```sh
rg -n 'ck_notification_type|ck_audit_event_action|ck_work_order_task_kind|ck_work_order_wo_status|ck_sync_operation_op_type' src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs; rg -n 'HasColumnName\("(work_order_id|assigned_to|assigned_at|started_at|wo_status|scheduled_date|due_date|xmin)"\)' src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs
```

Output thật (exit code 0):

```text
1665:                            t.HasCheckConstraint("ck_notification_type", "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received', 'work_order_overdue')");
3026:                            t.HasCheckConstraint("ck_sync_operation_op_type", "\"op_type\" IN ('fault_report', 'lux_reading', 'pole_note', 'work_order_start', 'work_order_complete')");
3735:                            t.HasCheckConstraint("ck_work_order_task_kind", "\"task_kind\" IN ('inspection', 'repair', 'survey')");
3739:                            t.HasCheckConstraint("ck_work_order_wo_status", "\"wo_status\" IN ('open', 'assigned', 'in_progress', 'done', 'verified', 'cancelled')");
4221:                            t.HasCheckConstraint("ck_audit_event_action", "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");
1203:                        .HasColumnName("xmin");
1498:                        .HasColumnName("assigned_at")
2612:                        .HasColumnName("started_at");
2868:                        .HasColumnName("xmin");
2873:                        .HasColumnName("work_order_id");
3525:                        .HasColumnName("work_order_id");
3572:                        .HasColumnName("work_order_id")
3578:                        .HasColumnName("assigned_at");
3582:                        .HasColumnName("assigned_to");
3614:                        .HasColumnName("due_date");
3646:                        .HasColumnName("scheduled_date");
3654:                        .HasColumnName("started_at");
3676:                        .HasColumnName("xmin");
3681:                        .HasColumnName("wo_status");
3749:                        .HasColumnName("work_order_id");
3808:                        .HasColumnName("work_order_id");
3812:                        .HasColumnName("due_date");
3980:                        .HasColumnName("work_order_id");
4037:                        .HasColumnName("work_order_id");
```

### E3

Lệnh:

```sh
rg -n 'AllowedActions|FieldEngineer when|assign"|start"|complete"|verify"' src/LuxMap.Modules.WorkOrders/WorkOrderRules.cs; sed -n '331,400p' src/LuxMap.Modules.WorkOrders/WorkOrderService.cs; sed -n '527,593p' src/LuxMap.Modules.WorkOrders/WorkOrderService.cs
```

Output thật (exit code 0):

```text
13:        "assign" or "reassign" or "unassign" or "edit" or "cancel" =>
15:        "start" => status == WorkOrderStatus.Assigned,
16:        "complete" => status == WorkOrderStatus.InProgress,
17:        "verify" or "return" => status == WorkOrderStatus.Done,
30:    public static string[] AllowedActions(WorkOrderStatus status, UserRole? role, bool assigned, TaskKind kind)
35:                ? ["assign", "unassign", "edit", "verify", "return", "cancel", "follow_up"]
36:                : ["assign", "unassign", "edit", "verify", "return", "cancel"],
37:            UserRole.FieldEngineer when assigned => ["start", "complete"],
        var title = OptionalJson.Present(request.Title) ? ValidTitle(OptionalJson.Text(request.Title, "title", false)) : null;
        var due = OptionalJson.Date(request.DueDate, "due_date");
        var scheduled = OptionalJson.Date(request.ScheduledDate, "scheduled_date");
        var materials = Materials(OptionalJson.Text(request.MaterialsNote, "materials_note"));
        var wo = await Find(id, ct);
        RequireAction(wo, "edit");
        var before = Snapshot(wo);
        var nextDue = OptionalJson.Present(request.DueDate) ? due : wo.DueDate;
        var nextScheduled = OptionalJson.Present(request.ScheduledDate) ? scheduled : wo.ScheduledDate;
        var nextMaterials = OptionalJson.Present(request.MaterialsNote) ? materials : wo.MaterialsNote;
        Schedule(nextScheduled, nextDue);
        if ((title ?? wo.Title) == wo.Title && nextDue == wo.DueDate && nextScheduled == wo.ScheduledDate
            && nextMaterials == wo.MaterialsNote) return await Detail(id, ct);
        var rescheduled = nextDue != wo.DueDate || nextScheduled != wo.ScheduledDate;
        wo.Title = title ?? wo.Title;
        wo.DueDate = nextDue;
        wo.ScheduledDate = nextScheduled;
        wo.MaterialsNote = nextMaterials;
        var now = UtcMicrosecondClock.UtcNow();
        wo.UpdatedAt = now;
        Record(wo, AuditAction.DetailsChanged, before, Snapshot(wo), now);
        if (rescheduled) Notify(WorkOrderNotices.Rescheduled(wo), [wo.AssignedTo], now);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Assign(string id, AssignWorkOrderRequest request, CancellationToken ct)
    {
        if (!OptionalJson.Present(request.AssignedTo)) throw OptionalJson.Invalid("assigned_to");
        var assigned = OptionalJson.Text(request.AssignedTo, "assigned_to");
        var wo = await Find(id, ct);
        RequireAction(wo, assigned is null ? "unassign" : "assign");
        if (assigned is not null)
        {
            await RequireAssignee(assigned, wo.CommuneId, ct);
            if (wo.TaskKind == TaskKind.Survey)
            {
                var segments = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).Select(x => x.SegmentId).ToArrayAsync(ct);
                await RequireSurveyAssignee(assigned, segments, ct);
            }
        }
        if (wo.AssignedTo == assigned) return await Detail(id, ct);
        var before = Snapshot(wo);
        var action = assigned is null ? AuditAction.Unassigned : wo.AssignedTo is null ? AuditAction.Assigned : AuditAction.Reassigned;
        var now = UtcMicrosecondClock.UtcNow();
        var previous = wo.AssignedTo;
        wo.AssignedTo = assigned;
        wo.AssignedAt = assigned is null ? null : now;
        wo.WoStatus = assigned is null ? WorkOrderStatus.Open : WorkOrderStatus.Assigned;
        wo.StartedAt = null;
        wo.UpdatedAt = now;
        Record(wo, action, before, Snapshot(wo), now);
        Notify(WorkOrderNotices.Assigned(wo), [assigned], now);
        Notify(WorkOrderNotices.Unassigned(wo), [previous], now);
        await Save(id, [], ct);
        return await Detail(id, ct);
    }

    public async Task<WorkOrderDetail> Act(string id, string action, string? note,
        JsonElement outcomes, CancellationToken ct, string? materialsUsed = null, DateTime? performedAt = null)
    {
        note = note?.Trim();
        if ((action == "complete" && (note?.Length ?? 0) < 10)
            || ((action is "return" or "cancel") && string.IsNullOrEmpty(note))) throw OptionalJson.Invalid("note");
        var claimed = Performed(performedAt);
        var wo = await Find(id, ct);
        RequireAction(wo, action);
        // BE-43 D-6: the engineer's own clock may not put the step before the step it follows.
        var previous = action switch { "start" => wo.AssignedAt, "complete" => wo.StartedAt, _ => null };
        if (claimed is { } at && at < previous) throw OptionalJson.Invalid("performed_at");
    }

    private static LuxMapException LinkConflict(WorkOrderFault link) => Error("FAULT_ALREADY_IN_WORK_ORDER",
        HttpStatusCode.Conflict, ("fault_id", link.FaultId), ("work_order_id", link.WorkOrderId));

    private async Task Save(string id, string[] faultIds, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // A failed statement aborts an open transaction; roll it back before the lookup below.
            if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            // Reapply BOTH filters: reassignment during a race can make the WO invisible now.
            var current = await Find(id, ct);
            throw Error("CONCURRENT_MODIFICATION", HttpStatusCode.Conflict, ("wo_status", current.WoStatus));
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "ux_work_order_fault_fault_id_active" })
        {
            if (db.Database.CurrentTransaction is { } open) await open.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var link = await db.Set<WorkOrderFault>().FirstAsync(x => faultIds.Contains(x.FaultId) && x.ReleasedAt == null, ct);
            throw LinkConflict(link);
        }
    }

    /// <summary>BE-27 — staged with the change, in the same save; the actor is never told about their own action.</summary>
    private void Notify(NotificationMessage message, IEnumerable<string?> recipients, DateTime now)
        => Notifier.Stage(db, message, recipients, ActorId, now);

    private void Record(WorkOrder wo, AuditAction action, object? before, object after, DateTime now, string? note = null)
        => audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, wo.CommuneId,
            AuditEntityType.WorkOrder, wo.WorkOrderId, action, before, after, note));

    // Only explicit snapshots cross the audit boundary: never serialize an EF entity or AppUser.
    private static WorkOrderSnapshot Snapshot(WorkOrder wo, string[]? ids = null, FaultChange[]? changes = null,
        string[]? skipped = null, List<WorkOrderFault>? links = null)
        => new(wo.WorkOrderId, wo.Title, wo.TaskKind, wo.WoStatus, wo.AssignedTo, wo.AssignedAt,
            wo.DueDate, wo.ScheduledDate, wo.Note, wo.ReviewNote, wo.ReportNote, wo.MaterialsNote, wo.MaterialsUsed,
            wo.ParentWorkOrderId, wo.RootWorkOrderId,
            wo.StartedAt, wo.CompletedAt,
            wo.ClosedAt, wo.CreatedAt, wo.UpdatedAt, wo.CreatedBy, wo.CommuneId, wo.SegmentId, wo.ClusterId,
            ids, changes, skipped, links?.Select(x => new FaultOutcomeSnapshot(x.FaultId, x.InspectionOutcome, x.ReleasedAt)).ToArray());

    private sealed record FaultChange(string FaultId, FaultStatus From, FaultStatus To);
    private sealed record FaultOutcomeSnapshot(string FaultId, InspectionOutcome? InspectionOutcome, DateTime? ReleasedAt);
    private sealed record WorkOrderSnapshot(string WorkOrderId, string Title, TaskKind TaskKind, WorkOrderStatus WoStatus,
        string? AssignedTo, DateTime? AssignedAt, DateOnly? DueDate, DateOnly? ScheduledDate, string? Note,
        string? ReviewNote, string? ReportNote, string? MaterialsNote, string? MaterialsUsed,
        string? ParentWorkOrderId, string? RootWorkOrderId, DateTime? StartedAt, DateTime? CompletedAt, DateTime? ClosedAt,
        DateTime CreatedAt, DateTime UpdatedAt, string CreatedBy, string CommuneId, string? SegmentId, string? ClusterId,
        string[]? FaultIds, FaultChange[]? FaultChanges, string[]? FaultSkipped, FaultOutcomeSnapshot[]? FaultOutcomes);
}
```

### E4

Lệnh:

```sh
cat src/LuxMap.Persistence/Migrations/20261014094218_AuditPoleRelocation.cs; sed -n '120,155p' src/LuxMap.Persistence/Migrations/20261014065813_AddScheduledJobs.cs; rg -n 'HasContractEnum|HasCheckConstraint|char_length' src/LuxMap.Persistence/Conventions/ContractEnum.cs src/LuxMap.Modules.Notifications/Configurations/NotificationConfiguration.cs; rg -n 'Stage|Clip|PasswordSetAt|Manager|wanted|actorUserId' src/LuxMap.Modules.Notifications/Notifier.cs; rg -n 'ReadWorkOrders|ManageWorkOrders|ExecuteWorkOrders|SyncOffline' src/LuxMap.Shared/Authorization/LuxMapPolicies.cs
```

Output thật (exit code 0):

```text
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditPoleRelocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed', 'relocated')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report', 'pole')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event");

            migrationBuilder.DropCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_action",
                table: "audit_event",
                sql: "\"action\" IN ('created', 'assigned', 'reassigned', 'unassigned', 'started', 'completed', 'verified', 'returned', 'cancelled', 'details_changed', 'confirmed', 'rejected', 'submitted', 'requested', 'delivered', 'applied', 'failed', 'expired', 'superseded', 'reported', 'revoked', 'accepted', 'merged', 'analyzed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_audit_event_entity_type",
                table: "audit_event",
                sql: "\"entity_type\" IN ('work_order', 'fault', 'survey_sweep', 'lighting_request', 'lighting_command', 'qr_code', 'citizen_report')");
        }
    }
}
            migrationBuilder.DropTable(
                name: "scheduled_job");

            migrationBuilder.DropTable(
                name: "work_order_overdue_notice");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notification");

            migrationBuilder.DropIndex(
                name: "ix_fault_origin_node_id_commune_id",
                table: "fault");

            migrationBuilder.DropIndex(
                name: "ux_fault_origin_node_id_detected_at",
                table: "fault");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fault_node_source",
                table: "fault");

            migrationBuilder.DropColumn(
                name: "origin_node_id",
                table: "fault");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notification",
                sql: "\"type\" IN ('work_order_assigned', 'work_order_unassigned', 'work_order_rescheduled', 'work_order_returned', 'work_order_cancelled', 'work_order_completed', 'work_order_verified', 'survey_returned', 'survey_ready_for_review', 'survey_processing_failed', 'fault_reported', 'citizen_report_received')");
        }
    }
}
src/LuxMap.Persistence/Conventions/ContractEnum.cs:74:    public static EntityTypeBuilder<TEntity> HasContractEnum<TEntity, TEnum>(
src/LuxMap.Persistence/Conventions/ContractEnum.cs:91:        return entity.ToTable(table => table.HasCheckConstraint(
src/LuxMap.Persistence/Conventions/ContractEnum.cs:100:    public static EntityTypeBuilder<TEntity> HasContractEnum<TEntity, TEnum>(
src/LuxMap.Persistence/Conventions/ContractEnum.cs:117:        return entity.ToTable(table => table.HasCheckConstraint(
src/LuxMap.Modules.Notifications/Configurations/NotificationConfiguration.cs:16:            table.HasCheckConstraint("ck_notification_title_length", "char_length(title) BETWEEN 1 AND 200");
src/LuxMap.Modules.Notifications/Configurations/NotificationConfiguration.cs:17:            table.HasCheckConstraint("ck_notification_body_length", "char_length(body) BETWEEN 1 AND 1000");
src/LuxMap.Modules.Notifications/Configurations/NotificationConfiguration.cs:25:        builder.HasContractEnum(notice => notice.Type);
src/LuxMap.Modules.Notifications/Configurations/NotificationConfiguration.cs:26:        builder.HasContractEnum(notice => notice.EntityType);
14:/// Stages notices on the CALLER's context (BE-27, D-8). It never saves: the caller's own
29:    public static void Stage(DbContext db, NotificationMessage message, IEnumerable<string?> recipients, string? actorUserId, DateTime now)
33:        foreach (var recipient in recipients.OfType<string>().Distinct(StringComparer.Ordinal).Where(id => id != actorUserId))
38:                Title = Clip(message.Title, 200), Body = Clip(message.Body, 1000),
49:    public static async Task<List<string>> ManagersCoveringAsync(DbContext db, IReadOnlyCollection<string> communes, CancellationToken ct)
53:        var wanted = communes.Distinct(StringComparer.Ordinal).ToArray();
54:        if (wanted.Length == 0) return [];
55:        var covering = db.Set<AppUserCommune>().Where(link => wanted.Contains(link.CommuneId))
56:            .GroupBy(link => link.UserId).Where(group => group.Count() == wanted.Length).Select(group => group.Key);
58:            .Where(user => user.Role == UserRole.Manager && !user.IsLocked && user.PasswordSetAt != null && covering.Contains(user.UserId))
71:    public static string Clip(string text, int max)
90:    public const string ReadWorkOrders = "cap:read_work_orders";
91:    public const string ManageWorkOrders = "cap:manage_work_orders";
92:    public const string ExecuteWorkOrders = "cap:execute_work_orders";
98:    public const string SyncOffline = "cap:sync_offline";
150:            [ReadWorkOrders] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
151:            [ManageWorkOrders] = [UserRole.Manager],
152:            [ExecuteWorkOrders] = [UserRole.FieldEngineer],
153:            [SyncOffline] = [UserRole.FieldEngineer],
```

### E5

Lệnh:

```sh
rg -n 'BLOCKED_BY_EARLIER_OP|BlockedByEarlierOp|TryParseType|PolicyFor|work_order:|parsed.Target|SyncOpType.WorkOrder|ResetAsync' src/LuxMap.Modules.Sync/SyncPushService.cs; cat src/LuxMap.Modules.Sync/Configurations/SyncOperationConfiguration.cs; rg -n 'WorkOrder|enum|FaultReport|LuxReading|PoleNote' src/LuxMap.Modules.Sync/Entities/SyncOperation.cs; rg -n 'ItemsAsync|WorkOrderDetail|OpenForEngineer|MyOpenOrders' src/LuxMap.Modules.Sync/SyncBundleService.cs src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs
```

Output thật (exit code 0):

```text
41:/// <c>BLOCKED_BY_EARLIER_OP</c>: a <c>complete</c> after a failed <c>start</c> means nothing.
66:        // work_order:{id} / pole:{id} → the operation that failed on it earlier in this batch.
73:            await ResetAsync(ct);
75:            if (!TryParseType(wireType, out var type))
81:            if (!(await authorization.AuthorizeAsync(user, PolicyFor(type))).Succeeded)
99:            if (parsed.Target is { } target && blocked.TryGetValue(target, out var blocker))
101:                rejected.Add(new(key, wireType, new(ErrorCodes.BlockedByEarlierOp,
115:                await ResetAsync(ct);
138:                await ResetAsync(ct);
148:            if (parsed.Target is { } failed)
183:    private static bool TryParseType(string? wire, out SyncOpType type)
199:    private static string PolicyFor(SyncOpType type) => type switch
204:        SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => LuxMapPolicies.ExecuteWorkOrders,
263:            case SyncOpType.WorkOrderStart:
267:                return new(start, id, $"work_order:{id}");
270:            case SyncOpType.WorkOrderComplete:
274:                return new(complete, id, $"work_order:{id}");
427:                SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => await workOrders.Detail(id, ct),
441:    private async Task ResetAsync(CancellationToken ct)
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Sync.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Modules.Sync.Configurations;

public sealed class SyncOperationConfiguration : IEntityTypeConfiguration<SyncOperation>
{
    public void Configure(EntityTypeBuilder<SyncOperation> builder)
    {
        builder.ToTable("sync_operation");

        // The key IS the idempotency rule: one client_op_id per person. Two pushes racing on the same
        // operation meet here, and the loser's whole SaveChanges — the step included — rolls back.
        builder.HasKey(operation => new { operation.UserId, operation.ClientOpId });

        builder.Property(operation => operation.UserId).HasColumnType("text");
        builder.Property(operation => operation.EntityId).HasColumnType("text").IsRequired();
        builder.HasContractEnum(operation => operation.OpType);

        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(operation => operation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
4:public enum SyncOpType
6:    FaultReport,
7:    LuxReading,
8:    PoleNote,
9:    WorkOrderStart,
10:    WorkOrderComplete,
src/LuxMap.Modules.Sync/SyncBundleService.cs:37:    private static readonly WorkOrderStatus[] OpenForEngineer = [WorkOrderStatus.Assigned, WorkOrderStatus.InProgress];
src/LuxMap.Modules.Sync/SyncBundleService.cs:104:        var mine = MyOpenOrders();
src/LuxMap.Modules.Sync/SyncBundleService.cs:119:    private async Task<WorkOrderDetail[]> WorkOrdersAsync(IReadOnlyList<string> segmentIds, CancellationToken ct)
src/LuxMap.Modules.Sync/SyncBundleService.cs:124:        var ids = await MyOpenOrders()
src/LuxMap.Modules.Sync/SyncBundleService.cs:133:        var details = new WorkOrderDetail[ids.Count];
src/LuxMap.Modules.Sync/SyncBundleService.cs:143:    private IQueryable<WorkOrder> MyOpenOrders()
src/LuxMap.Modules.Sync/SyncBundleService.cs:146:        return db.Set<WorkOrder>().Where(wo => wo.AssignedTo == me && OpenForEngineer.Contains(wo.WoStatus));
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:202:        var items = (await WorkOrderService.ItemsAsync(db, tonight, ct)).ToDictionary(x => x.WorkOrderId);
```

### E6

Lệnh:

```sh
python3 - <<'PY'
import json
from pathlib import Path
p=Path('docs/openapi/luxmap-v1.5.json')
s=json.loads(p.read_text())
verbs={'get','post','put','patch','delete','head','options','trace'}
ops=[(v.upper(),p) for p,x in s['paths'].items() for v in x if v in verbs]
print('spec:',p)
print('info.version:',s['info']['version'])
print('operations:',len(ops))
print('with two proposed POST operations:',len(ops)+2)
for v,p in ops:
 if '/work-orders' in p or '/notifications' in p or '/sync/' in p: print(v,p)
print('cancellation occurrences:',sum('cancellation' in line.lower() for line in Path('docs/openapi/luxmap-v1.5.json').read_text().splitlines()))
PY
```

Output thật (exit code 0):

```text
spec: docs/openapi/luxmap-v1.5.json
info.version: 1.18
operations: 139
with two proposed POST operations: 141
GET /api/v1/notifications
GET /api/v1/notifications/unread-count
POST /api/v1/notifications/{notificationId}/read
POST /api/v1/notifications/read-all
GET /api/v1/sync/bundle
POST /api/v1/sync/push
POST /api/v1/work-orders/{id}/pole-photos
GET /api/v1/work-orders/{id}/pole-photos
GET /api/v1/work-orders/{id}/pole-photos/{photo_id}
GET /api/v1/work-orders/{id}/pole-photos/{photo_id}/thumbnail
GET /api/v1/work-orders/{id}/pole-photos/{photo_id}/original
PATCH /api/v1/work-orders/{id}/pole-photos/{photo_id}/review
POST /api/v1/work-orders/{id}/pole-photos/{photo_id}/decision
GET /api/v1/work-orders
POST /api/v1/work-orders
GET /api/v1/work-orders/agenda
GET /api/v1/work-orders/{id}
PATCH /api/v1/work-orders/{id}
GET /api/v1/work-orders/{id}/poles
POST /api/v1/work-orders/{id}/evidence
GET /api/v1/work-orders/{id}/evidence
GET /api/v1/work-orders/assignees
PUT /api/v1/work-orders/{id}/assignee
POST /api/v1/work-orders/{id}/start
POST /api/v1/work-orders/{id}/complete
POST /api/v1/work-orders/{id}/verify
POST /api/v1/work-orders/{id}/return
POST /api/v1/work-orders/{id}/follow-up
POST /api/v1/work-orders/{id}/cancel
cancellation occurrences: 0
```

### Giới hạn của bằng chứng

Một lệnh tra cứu ban đầu dùng nhầm tên file `src/LuxMap.Persistence/Conventions/ContractEnumExtensions.cs` trả:

```text
cat: src/LuxMap.Persistence/Conventions/ContractEnumExtensions.cs: No such file or directory
```

Đã tìm lại bằng `rg -n 'HasContractEnum|EnumCheck' src/LuxMap.Persistence/Conventions`, output thật:

```text
src/LuxMap.Persistence/Conventions/ContractEnum.cs:74:    public static EntityTypeBuilder<TEntity> HasContractEnum<TEntity, TEnum>(
src/LuxMap.Persistence/Conventions/ContractEnum.cs:100:    public static EntityTypeBuilder<TEntity> HasContractEnum<TEntity, TEnum>(
```

Đã đọc file thật ContractEnum.cs; báo cáo dựa trên file đó. E3 dùng hai đoạn sed riêng nên dấu ngoặc ở ranh giới
output không phải toàn bộ thân Act; đã đọc toàn bộ WorkOrderService.cs trước khi trích bằng chứng.

✅ Verified: đối chiếu code, snapshot, Contract/drift, đếm OpenAPI bằng JSON local; đã khảo sát đủ tám nhóm yêu cầu.

⚠️ Could not verify: hành vi runtime, catalog DB thực tế, build/test/migration/concurrency thực nghiệm — không chạy ở Phase 1.
Không có số test pass hoặc bằng chứng DB nào được tuyên bố.

📌 Remaining assumptions: D-1…D-14 chưa chốt; WP5/WP6/FW chưa xác nhận, chưa gửi thông báo.
**Dừng hẳn ở đây, chờ Mỹ quyết định; chưa triển khai Phase 2.**




## Quyết định D-1…D-14 — chốt 26/10/2026

Mỹ (Dylan) ủy quyền cho Claude chốt (26/10); Claude đã kiểm lại với code hai điểm của báo cáo (`DbUpdateConcurrencyException` → 409 `CONCURRENT_MODIFICATION`
ở `WorkOrderService.Save`; `ck_sync_operation_op_type` chỉ có 5 giá trị). **Theo đề xuất của Codex, trừ D-3 giữ có điều kiện, D-7 đổi độ dài lý do.**
Mọi thứ chạm bề mặt API là `SELF-SIGNED`, nền tạm tới FW kế tiếp; WP5 / WP6 chưa được báo.

| # | Chốt | Lý do / điều kiện |
|---|---|---|
| D-1 | **A** — năm cột trên `work_order`, lịch sử ở audit | Không có nhu cầu timeline độc lập; B thêm bảng + đồng bộ mọi writer. Cần timeline qua API thì mở ticket riêng |
| D-2 | Theo bảng máy trạng thái của báo cáo | `start` / `complete` khi `pending` cho đi tiếp và reset; giao lại / bỏ giao / `cancel` / ngày thật đổi reset; no-op, đổi title / vật tư giữ |
| D-3 | **Chỉ `assigned`** | Ca "xe hỏng trên đường tới" xảy ra trước `start` (ảnh `before` đòi `in_progress`, nên `start` bấm khi tới nơi). Mở thêm `in_progress` sau này chỉ là nới điều kiện, tương thích ngược; còn ảnh / `started_at` / sự cố đã `in_progress` thì chưa có luật cho người thứ hai |
| D-4 | Áp cả `survey`; báo Quản lý phủ xã neo của phiếu | Không huỷ sweep / video / lux / GPS |
| D-5 | Cho xin lại sau `rejected`, lý do mới, không cooldown | `pending` lặp → 409 |
| D-6 | Cả năm trường trên item, detail, agenda, bundle; mọi người đọc được phiếu đều đọc được | Lý do không nhạy cảm hơn `note` hiện có |
| D-7 | Hai POST, lọc `cancellation_request_status`, hai `allowed_actions`, dùng lại hai capability và mã 409 / 400; **lý do trim 1…500 ký tự**, kiểm trong service | 500 chứ không 1000: thân thông báo bị CHECK 1000 và mang thêm mã phiếu + tiêu đề, 1000 sẽ bị cắt mất lý do |
| D-8 | Polling 30–60 s như Contract §5.9; badge theo tổng `pending` | Không có hạ tầng realtime; không mở phạm vi |
| D-9 | Chấp nhận "thao tác lên phiếu hiện tại"; không thêm điều kiện vào `/assignee`, `/cancel`, `reject` | Màn hình cũ sau vòng xin → bác → xin là hẹp; ghi thành giới hạn đã biết trong drift. `xmin` lo race đang chạy |
| D-10 | **Online-only**; WP6 lưu nháp lý do, không đẩy vào `/sync/push` | Không mở `ck_sync_operation_op_type`. Cần offline thì ticket riêng |
| D-11 | Không có Quản lý phủ xã: vẫn lưu `pending` + audit, không thông báo giả, không fallback vai khác | Quản lý thấy qua bộ lọc |
| D-12 | `AuditAction.CancellationRequested` / `CancellationRejected`; hai CHECK (notification type, audit action) nới trong **cùng một migration** với cột mới, `Down()` đối xứng | Không động `ck_*entity_type` |
| D-13 | Dời lịch thật sự reset, ghi là "hết hiệu lực", không coi là duyệt hủy | WP5 / WP6 xác nhận ở FW kế tiếp |
| D-14 | Ghi drift `WO-CANCEL-REQUEST` (CR-1…CR-6) + Contract v1.19 `SELF-SIGNED` trong Phase 2, cùng PR với code | Im lặng 3 ngày làm việc = ESCALATE vì chạm API |

**Phase 2 phải làm đúng thứ tự:** đọc `Up()` / `Down()` của migration trước khi apply; không `DropIndex` / `DropColumn` / `AlterColumn` ngoài ý; CHECK mới
liệt kê trọn bộ giá trị cũ + mới; mỗi chốt mới có phá thử.
