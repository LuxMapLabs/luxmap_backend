# BE-23: chốt D1–D11 (Phase 1, không sửa file)

**Ai chốt:** Claude (Claude Code CLI) chốt theo ủy quyền của người dùng trong phiên 27/09/2026. Đây **không** phải chữ ký của Mỹ, cũng không phải quyết định của FW.

Mọi mục ghi "Chạm API: có" chỉ là **nền tạm**. Ở Phase 2, khi ghi vào `docs/contract-drift.md`, Decision maker phải ghi là *"Claude theo ủy quyền người dùng, 27/09/2026"*. Mỹ cần ký lại, rồi FW kế tiếp xác nhận. Theo FW-00 mục 3 (`docs/contract-drift.md:17`), mục chạm API mà không ai lên tiếng thì phải **ESCALATE**, không được coi là đã duyệt.

Tôi không triển khai Phase 2 và không sửa file nào.

---

## 0. Điều kiện chặn và cách đọc

- **Registration v1.4 chưa merge, Phase 2 vẫn dừng.**
  - Bằng chứng: `.ai/results/BE-23-p1.md:41` ghi `fatal: path 'docs/registration/FA26SE222_v1.4.md' exists on disk, but not in 'dev'`.
  - Ảnh chụp git lúc mở phiên cũng cho thấy file đó còn untracked, còn `CLAUDE.md` và `docs/contract-drift.md` đang sửa dở.
  - Tôi không giả định nhánh đã merge. Điều kiện ở `.ai/tasks/BE-23.md:61-63` vẫn giữ nguyên.
- **Tôi đọc working tree.** Tức là đọc cả phần chưa commit của `CLAUDE.md` và `contract-drift.md`. Nếu lúc merge các đoạn tôi dẫn bị đổi, phải đối chiếu lại.
- **Trong phiên này tôi không chạy psql hay git.**
  - Các output `\d …` và phép đếm `fault_status` là của người ra đề (`BE-23.md:84-159`). Tôi dùng chúng làm dữ kiện nhưng chưa kiểm lại.
  - Yêu cầu 1 của ticket (chạy lại và dán output thật) vẫn là việc của Phase 2.
- **Vì sao D1–D11 không chờ các D-R đang treo.** Phiếu v1.4 không đổi actor (D-R19, `contract-drift.md:135`). Các khoản đang treo (D-R20–28) chỉ nói về khảo sát và đo sáng, còn ticket này không đụng tới phần đó. Blocker ở đây chỉ mang tính hình thức: nhánh v1.4 phải vào `dev` trước.

## 1. Tóm tắt

| D | Chốt | Chạm API |
|---|---|---|
| D1 | Một bảng `audit_event` dùng chung, đặt ở `LuxMap.Persistence`. ID là `bigint` identity, không lộ ra API. Bảng là `ICommuneScoped`. Service gọi ghi audit tường minh, ghi **trong cùng `SaveChanges`** với thay đổi nghiệp vụ. **Đúng 1 dòng cho mỗi thao tác.** Append-only có hai lớp: guard EF và trigger DB, cộng một lối dọn chỉ dành cho teardown test. Chưa có endpoint đọc audit. | Không |
| D2 | Dùng **bảng nối `work_order_fault`**, không thêm cột `fault.work_order_id`. Mỗi fault có tối đa **một** phiếu công việc (WO) đang hoạt động, thực thi bằng unique partial index. Khoá ngoại ghép bảo đảm cùng xã. | Có |
| D3 | Inspection nhận fault thuộc tập `Open`. Repair nhận `Open` trừ `detected`. Kiểm ở service, sai trả **409**. | Có |
| D4 | Máy trạng thái ở mục 6. Có đường trả lại `done → in_progress`. Chỉ repair mới kéo `fault_status` theo. | Có |
| D5 | Ba capability: `ReadWorkOrders`, `ManageWorkOrders`, `ExecuteWorkOrders`. | Có |
| D6 | Thêm `IAssigneeScoped`, **gộp vào đúng một** query filter với commune scope. Truy cập WO không phải của mình trả 404. | Có (hành vi) |
| D7 | Kiểm điều kiện người được giao ở service. Không dùng FK ghép tới `app_user_commune`. Thêm endpoint liệt kê Kỹ sư hiện trường giao được. | Có |
| D8 | Thêm cột `scheduled_date` trên WO. "Lịch" chỉ là bộ lọc của listing, không có bảng lịch riêng. | Có |
| D9 | Tách **hai PR tuần tự**: BE-23a (audit, không chạm API) rồi BE-23 (WO). Mỗi chuyển trạng thái là một endpoint hành động riêng. | Có |
| D10 | `task_kind: inspection \| repair`, một bảng. Inspection có thể trỏ vào một đoạn tuyến thay vì fault. Kết quả kiểm tra ghi **theo từng fault** trên dòng nối. | Có (enum mới) |
| D11 | `segment_id`/`cluster_id` chụp lại theo luật lúc tạo. `priority_score` tính sống. Seed 3 WO qua `seed_mock_set.py` kèm một file loại việc riêng. Cảnh báo hết bảo hành để BE-31/35. | Có |

---

## 2. Từng D-item

### D1 — `audit_event` (D-R13)

**Hiện trạng**
- Contract §3.3 (`api-contract-v1.1.md:305-307`) và D-R13 (`contract-drift.md:90`) hẹn thiết kế audit ở BE-19. `Fault.cs:16-26` còn ghi *"audit trail lives on this row"*, trái với D-R13.
- Guard ghi là override của `SaveChanges` (`LuxMapDbContext.cs:74-86`), chạy **trước** lời gọi base (`:96-104`). Backdoor tắt guard nằm ở `:63-67`.
- Mọi cột `commune_id` bắt buộc có FK tới `administrative_unit` (`CommuneReferenceBuilderExtensions.cs:49` và `:81-120`).
- App và migration dùng cùng role `luxmap`, chính là `POSTGRES_USER` (`docker-compose.yml:17`).
- Teardown test đang xoá hàng loạt `AppUser`, `AdministrativeUnit`, `Fault`, `Pole` (`AuthEndpointTests.cs:242`, `ScopeTestFixture.cs:96-97`, `FaultSchemaTests.cs:43-47`…).
- `AppUser` nằm ở module Identity, mà Identity tham chiếu Persistence chứ không ngược lại.

**Phương án**

| | Hệ quả |
|---|---|
| A. `SaveChanges` interceptor tự sinh audit từ ChangeTracker | Không biết *vì sao* (ghi chú, hành động ngữ nghĩa như *reassign*). Sinh N dòng cho mỗi entity thay đổi, nên phá luật 1 dòng/thao tác. Không phân biệt được "đổi người" với "đổi hạn". |
| **B. Service ghi tường minh `IAuditTrail.Record(...)` trước `SaveChanges`, cộng guard bắt buộc có audit** | Có đủ ngữ nghĩa. Ghi cùng một `SaveChanges` nên cùng transaction ngầm. Nếu quên ghi thì guard bắt được. |
| ID prefix (`AUD-…`) | Phải thêm prefix vào §1.2, tức chạm Contract, cho một ID chưa ai đọc. |
| **ID `bigint` identity, không lộ ra ngoài** | Không chạm API. Sắp theo số nguyên nên không dính bẫy sắp chuỗi. |

**CHỐT**

1. **Vị trí code.**
   - `LuxMap.Persistence/Audit/` chứa `AuditEvent`, `AuditEventConfiguration`, các enum nội bộ (`AuditActorKind`, `AuditEntityType`, `AuditAction`), marker `IAudited`, cặp `IAuditTrail`/`AuditTrail` (scoped).
   - FK từ actor tới `app_user` khai ở **phía principal**: thêm `HasMany<AuditEvent>().WithOne().HasForeignKey(a => a.ActorUserId).OnDelete(Restrict)` vào `AppUserConfiguration` (`IdentityConfigurations.cs:10-33`). Như vậy không sinh phụ thuộc vòng.
   - Enum audit **không** đặt trong `DomainEnums.cs`: file đó là enum Contract bị đóng băng (`:3-4`).
2. **Schema:** xem mục 3.1.
3. **`commune_id NOT NULL`, bảng là `ICommuneScoped`.** `HasCommuneScope()` lo phía đọc, còn guard ghi chặn việc ghi audit cho xã ngoài phạm vi. Sự kiện không thuộc xã nào (quản trị tài khoản ở BE-33) **chưa vào bảng này**; BE-33 tự quyết khi làm, và việc đó không chặn ticket này.
4. **Ghi.** Mỗi thao tác đổi trạng thái:
   - gọi `IAuditTrail.Record(...)` đúng **một** lần;
   - rồi gọi `SaveChanges` đúng **một** lần.

   `occurred_at` lấy **đúng** giá trị `now` mà service ghi vào các timestamp nghiệp vụ. `correlation_id` lấy từ `ICorrelationIdAccessor`.
5. **Guard ở `LuxMapDbContext`, chạy trước `EnforceCommuneWriteScope`.**
   - (a) Nếu có entry `AuditEvent` ở trạng thái `Modified` hoặc `Deleted`: ném `InvalidOperationException` (ra 500). Luật này áp **kể cả trong backdoor**.
   - (b) Nếu có ít nhất một entry `IAudited` ở `Added`, `Modified` hoặc `Deleted`: bắt buộc có **đúng 1** entry `AuditEvent` ở `Added`, sai thì ném `InvalidOperationException`. Luật này được bỏ qua khi đang trong backdoor, vì seeder và fixture không phải quyết định của ai.
   - Ticket này gắn `IAudited` cho `WorkOrder` và `WorkOrderFault`. `Fault` sẽ được gắn `IAudited` ở **BE-19**, không phải ở đây: hiện chưa có đường ghi fault nào trong `src/`, và gắn sớm sẽ làm vỡ các test đang tạo fault.
6. **Append-only ở DB (migration BE-23a).**
   - Hàm `luxmap_audit_event_append_only()` (tên đề xuất): `RAISE EXCEPTION` trừ khi `current_setting('luxmap.audit_purge', true) = 'on'`.
   - Hai trigger: `BEFORE UPDATE OR DELETE … FOR EACH ROW` và `BEFORE TRUNCATE … FOR EACH STATEMENT`.
   - `CREATE FUNCTION` phải đứng trước `CREATE TRIGGER` trong cùng migration.
   - `Down()` drop trigger, rồi hàm, rồi bảng. Thao tác này **mất toàn bộ dữ liệu audit**, nên theo §8 chạy `Down()` là việc phải xin phép.
7. **Không dùng `REVOKE`.**
   - Role `luxmap` vừa là owner vừa (nhiều khả năng) là superuser, vì image Postgres chính thức tạo `POSTGRES_USER` là superuser. Phase 2 phải kiểm lại bằng `\du`; tôi chưa chạy.
   - Owner hoặc superuser tự `GRANT` lại được, và còn `ALTER TABLE … DISABLE TRIGGER` hoặc `SET session_replication_role = replica` được.
   - Nói thẳng ra: append-only ở đây **chặn tai nạn, không chặn DBA**. Tách role cho app là việc của ticket triển khai.
8. **Lối dọn cho teardown.** Test dọn trong một transaction tường minh:
   - `SET LOCAL luxmap.audit_purge = 'on'`;
   - rồi `DELETE FROM audit_event WHERE commune_id = ANY(@communes)` bằng SQL thô. Không dùng `ExecuteDelete`.

   Một test quét mã nguồn bảo đảm chuỗi `luxmap.audit_purge` **không xuất hiện trong `src/`**, ngoài thư mục `Migrations/`. BE-36 (mỗi lượt chạy một DB sạch) sẽ xoá hẳn nhu cầu này.
9. **Chưa có endpoint đọc audit.** Màn "lịch sử bảo trì" (phiếu v1.4, `FA26SE222_v1.4.md:87`) là follow-up riêng. Vết trên dòng `fault` (`confirmed_by/at`, `resolved_by/at`) vẫn giữ nguyên như BE-18.

**Tác động API:** không có. Thêm một drift nội dung: Contract §3.3 ghi "thiết kế ở BE-19", nay thiết kế nằm ở BE-23a.

### D2 — Quan hệ WO ↔ fault

**Hiện trạng**
- Bảng `fault` chưa có `work_order_id` (`BE-23.md:117`).
- §5.4 item có `work_order_id|null`, và câu "luôn null tới BE-21" (`api-contract-v1.1.md:575-580`).
- Mọi FK của fault đều `Restrict` (`FaultConfigurations.cs:123-144`).
- Tiền lệ O-7 dùng FK ghép `(feeder_id, commune_id)` (CLAUDE.md, mục O-7).

**Phương án**

| | Hệ quả |
|---|---|
| A. Cột `fault.work_order_id` | Fault chỉ giữ được **WO cuối cùng**. Khi fault chuyển sang WO mới (inspection xong thì repair), `fault_ids[]` của WO cũ đã `verified` **bị đổi ngược về quá khứ**. Lịch sử của một WO đã đóng không được phép biến đổi. |
| **B. Bảng nối `work_order_fault`, có `released_at`** | Thành viên của WO bất biến vĩnh viễn. "Fault đang thuộc WO nào" là dòng nối có `released_at IS NULL`. Hai quản lý giao trùng cùng lúc thì unique partial index chặn, không cần thêm một lượt kiểm. |

**CHỐT: phương án B.**
- `released_at` được gán cho mọi dòng nối của WO khi WO sang `verified` hoặc `cancelled`, trong cùng thao tác đó. WO ở `done` **vẫn giữ** fault, vì còn có thể bị trả lại làm tiếp.
- Một fault đã nằm trong WO đang hoạt động (`open`, `assigned`, `in_progress`, `done`) thì không vào WO khác được: trả **409 `FAULT_ALREADY_IN_WORK_ORDER`**. Khi WO cũ đóng thì fault được giải phóng.
- `fault_status` **không** đổi khi giải phóng. Lan truyền trạng thái xem ở D4.
- `work_order_id` trong §5.4 là WO của dòng nối chưa giải phóng. Index bảo đảm tối đa một dòng như vậy, nên giá trị là `null` hoặc đúng một WO.
- Chiều FK:
  - `work_order_fault (work_order_id, commune_id)` trỏ tới `work_order (work_order_id, commune_id)`, `Restrict`: chặn xoá WO khi còn dòng nối. WO vốn cũng không có endpoint DELETE.
  - `work_order_fault (fault_id, commune_id)` trỏ tới `fault (fault_id, commune_id)`, `Restrict`: chặn xoá một fault từng nằm trong WO.
- **Quyền sở hữu:** module WorkOrders sở hữu bảng nối. `fault_status` chỉ được đổi qua **một** service của module Faults (`FaultTransitions`, tên đề xuất). WorkOrders gọi service đó, còn BE-19 dùng lại. Service này không tự gọi `SaveChanges`. Như vậy có đúng một chỗ biết luồng của `fault_status`.
- **Hệ quả của alternate key trên `fault`:** `Fault.CommuneId` trở thành thuộc tính khoá, nên EF sẽ từ chối sửa nó trên entity đang track (cùng bẫy đã ghi ở mục O-7, ra 500). Chấp nhận được: fault không bao giờ đổi xã.

**Tác động API:** có. `work_order_id` ở §5.4 bắt đầu có giá trị thật khi BE-40 hiện thực.

### D3 — Điều kiện tạo theo loại việc

**Hiện trạng**
- `FaultStatusSets.Open` = `{detected, confirmed, in_progress}` (`FaultStatusSets.cs:28-31`).
- §3.2 (`api-contract-v1.1.md:269-272`) chỉ có `confirmed → in_progress`, **không** có `detected → in_progress`.
- Trên DB dev theo ticket: `COM-070` có 7 `confirmed` và 21 `detected`.
- Trong mock, WO-0001 gồm 6 fault `detected` và 1 `confirmed`; WO-0003 có FAULT-0027 `detected`.

**Phương án**

| | Hệ quả |
|---|---|
| A. Repair nhận cả fault `detected` | Khi bắt đầu làm, lan truyền sẽ phải đi `detected → in_progress`, tức vi phạm §3.2. Ngoài ra còn bỏ qua bước duyệt của Quản lý. |
| **B. Repair chỉ nhận `confirmed` hoặc `in_progress`; inspection nhận mọi fault trong `Open`** | Khớp §3.2. Cả 3 WO của mock đều hợp lệ **nếu** WO-0001 và WO-0003 là inspection (xem D10). Không phải nới luật, không phải chờ BE-19. |

**CHỐT: phương án B.**
- Chấp nhận `in_progress` cho repair vì trường hợp repair bị huỷ giữa chừng để lại fault ở `in_progress` mà không còn WO nào.
- Viết `FaultStatusSets.IsOpen(s)` cho inspection, và `FaultStatusSets.IsOpen(s) && s != FaultStatus.Detected` cho repair. Luật này đặt trong `WorkOrderEligibility` (tên đề xuất), **không** chép tay lại tập trạng thái.
- Fault đã đóng (`rejected`, `resolved`, `verified`) không vào WO nào được.
- Thực thi ở **service**. Đây là luật liên bảng nên CHECK không diễn đạt được, và không dùng trigger.
- Mã lỗi: **409 `FAULT_STATUS_NOT_ELIGIBLE`**, `details = {task_kind, accepted_statuses[], faults[]{fault_id, fault_status}}` liệt kê **mọi** fault vi phạm.
- Fault không tồn tại hoặc ngoài phạm vi: **404 `FAULT_NOT_FOUND`** kèm `details.fault_ids[]`, theo tiền lệ POST cột với tuyến không tìm thấy trả 404.
- Hệ quả cho demo: đường repair chạy được trên WO-0002 và trên các fault đã `confirmed` chưa thuộc WO nào trong seed (FAULT-0001, 0010, 0013). Đưa fault sang `confirmed` qua API vẫn phải chờ BE-19.

**Tác động API:** có (mã lỗi mới, luật mới).

### D4 — Luồng `wo_status`

**Hiện trạng**
- Enum `WorkOrderStatus` có sẵn (`DomainEnums.cs:98-107`).
- O-3 còn mở (`api-contract-v1.1.md:280` và `:712`).
- Chú thích `Fault.ResolvedBy` là *"Who marked the work done"* (`Fault.cs:144`).
- Chưa có concurrency token nào trong repo (grep không thấy `xmin` hay `IsConcurrencyToken`).

**Phương án cho phần lan truyền sang fault**

| | Hệ quả |
|---|---|
| A. WO `done` → fault `resolved`; WO `verified` → fault `verified` | Khi Quản lý trả lại (`done → in_progress`), fault phải đi `resolved → in_progress`. Chuyển đó **không có trong §3.2**, nên phải sửa máy trạng thái fault. |
| **B. WO `done` giữ fault ở `in_progress`; WO `verified` đẩy fault `in_progress → resolved → verified` trong một thao tác** | Chỉ dùng các chuyển §3.2 đã có. Lúc WO đang chờ nghiệm thu, fault vẫn tính là mở, và điều đó đúng sự thật: sửa chưa được nhận. |

**CHỐT: phương án B.**
- Bảng chuyển trạng thái đầy đủ ở mục 6. Luồng giống nhau cho inspection và repair; chỉ khác phần body lúc `complete` và phần lan truyền sang fault.
- Chuyển sai luồng trả **409 `INVALID_STATE_TRANSITION`**. Chuyển về chính trạng thái hiện tại (ví dụ `start` khi đã `in_progress`) cũng 409. Mobile đọc `details.wo_status` để nhận ra rằng lần gửi lại của mình thực ra đã được áp dụng trước đó.
- Bảng `work_order` dùng `xmin` làm concurrency token (xem mục 10).

**Tác động API:** có (đóng O-3, thêm mã lỗi).

### D5 — Capability (O-2)

**Hiện trạng**
- Ma trận ở `LuxMapPolicies.cs:53-62`.
- Host từ chối khởi động nếu có capability rỗng (`AuthorizationSetup.cs:55-60`).
- Hai bảng kỳ vọng viết literal: `RoleCapabilityMatrixTests.cs:43-51` và `CapabilityMatrixTests.cs:18-26`.
- Phiếu v1.4: Superior *"View and export … maintenance information"* (`FA26SE222_v1.4.md:75`); Field Engineer *"View assigned …"*, *"Update the status of assigned tasks"* (`:133`, `:143`).

**Phương án:** tách mịn "giao việc" và "nghiệm thu" thành hai capability, **hay** gộp. Cả hai việc đều của đúng một vai trò (Quản lý), nên tách ra không diễn đạt thêm được quyền nào mà chỉ làm dài ma trận.

**CHỐT:**

| Hằng | Chuỗi | Vai trò |
|---|---|---|
| `ReadWorkOrders` | `cap:read_work_orders` | superior, manager, field_engineer, system_admin |
| `ManageWorkOrders` | `cap:manage_work_orders` | **manager** |
| `ExecuteWorkOrders` | `cap:execute_work_orders` | **field_engineer** |

- Kỹ sư hiện trường có capability đọc, nhưng lớp D6 giới hạn họ chỉ thấy việc được giao cho mình.
- Quản trị hệ thống chỉ đọc (D-R12). Guard ghi cho claim `*` đi qua (`CommuneWriteGuard.cs:45-48`), nên **chỉ có policy** đứng giữa họ và các endpoint ghi.
- Ba capability này thêm vào `Matrix`, bảng §2 của Contract (qua drift) và cả hai bảng literal **trong cùng một diff**.

**Tác động API:** có.

### D6 — Lớp "chỉ người được giao"

**Hiện trạng**
- Query filter hiện là **một** lời gọi `HasQueryFilter` cho mỗi entity (`CommuneScopeBuilderExtensions.cs:91-97`). Gọi thêm một `HasQueryFilter` không đặt tên sẽ **ghi đè** filter commune, và không báo lỗi gì.
- Accessor phải là singleton; filter phải tham chiếu `DbContext` (`CommuneScopeAccessor.cs:13-21`, `authorization-guide.md:143-151`).
- Hằng `sub` (`AuthClaims.Subject`) nằm ở module Identity; cách đọc hiện có ở `LuxReadingsController.cs:122`.
- Repo đang dùng EF Core 10.0.11 (`LuxMap.Persistence.csproj:9`).

**Phương án**

| | Có thể quên không? |
|---|---|
| A. Service tự gọi `WhereVisibleTo(caller)` | **Quên được.** BE-24 (evidence) và BE-43 (sync) sẽ phải nhớ gọi lại. |
| B. `IAuthorizationHandler` theo resource | Chỉ chạy khi có code gọi. Listing không đi qua nó. |
| **C. `IAssigneeScoped` gộp vào filter commune** | **Không quên được.** Mọi truy vấn `Set<WorkOrder>()`, kể cả của BE-24 và BE-43, đều tự bị giới hạn. |

**CHỐT: phương án C.**
- `LuxMap.Shared/Authorization/IAssigneeScoped` có `string? AssignedTo`.
- `ICurrentActorAccessor` (singleton, hiện thực ở `LuxMap.Api/Authorization`) đọc `sub` và `role`.
- `LuxMapDbContext` lộ ra `CurrentAssigneeRestriction`. Khi role là `field_engineer`, giá trị là `sub`; thiếu `sub` thì là chuỗi rỗng, không khớp gì, tức đóng lại khi nghi ngờ.
- `ApplyFilter` dựng **một lambda duy nhất**: `(commune…) && (restriction == null || e.AssignedTo == restriction)`. Không dựa vào named filter.
- Lúc khởi động: entity khai `IAssigneeScoped` mà không `ICommuneScoped` thì app **không chạy**.
- Kỹ sư A mở WO của kỹ sư B cùng xã: **404 `WORK_ORDER_NOT_FOUND`**. Item fault vẫn cho A biết `work_order_id`, tức A biết WO tồn tại, nhưng không thấy nội dung. 404 cũng là thứ filter tự sinh ra, nên nhất quán.
- Listing với `assigned_to=<id người khác>` trả **200 rỗng**, theo tiền lệ `pole_id` ở §5.4. Có thêm bí danh `assigned_to=me` cho mọi vai trò.
- Nhiều người cùng làm một WO nằm ngoài phạm vi ticket. Khi cần, chỉ phải đổi biểu thức filter sang `Any()` trên một bảng phân công.
- Tài liệu `authorization-guide.md` thêm "Trường hợp 4".

**Tác động API:** có (hành vi).

### D7 — Điều kiện người được giao

**Hiện trạng**
- `app_user_commune` là `Cascade` từ `app_user` và `Restrict` tới xã (`IdentityConfigurations.cs:46-58`).
- Tài khoản `crew` (USR-004) có vai trò `field_engineer` (`IdentitySeeder.cs:163`).
- Hiện không có endpoint user nào ngoài `/auth/me`.

**Phương án**

| | Hệ quả |
|---|---|
| A. FK ghép `(assigned_to, commune_id)` → `app_user_commune` | Quản trị sẽ **không bao giờ gỡ được xã** của một người từng được giao việc, kể cả WO đã `verified` từ lâu. Không dùng được cho BE-33. |
| **B. Kiểm ở service lúc giao; FK đơn tới `app_user`, `Restrict`** | Kiểm theo thời điểm giao; lịch sử không khoá thao tác quản trị. |

**CHỐT: phương án B.**
- Người được giao hợp lệ khi đồng thời: `role = field_engineer`, `is_locked = false`, và `(user_id, wo.commune_id)` có trong `app_user_commune`. Đọc từ **DB**, không đọc từ claim, vì claim có thể cũ tới 60 phút.
- Sai thì trả **409 `ASSIGNEE_NOT_ELIGIBLE`**, `details` chỉ lặp lại `assigned_to`. Một mã cho mọi lý do (không tồn tại, sai vai trò, bị khoá, khác xã), để không thành kênh dò tài khoản.
- `commune_id` của WO: tra từ các fault, và mọi fault phải cùng một xã, khác xã trả **409 `CROSS_COMMUNE_REFERENCE`**. Với inspection theo đoạn tuyến, xã lấy từ `road_segment.commune_id`. Client gửi `commune_id` trả **400 `SERVER_OWNED_FIELD`**.
- Kỹ sư bị khoá, bị gỡ xã hoặc đổi vai trò sau khi đã nhận việc:
  - **Không tự gỡ giao.** Không có actor hệ thống giả, cũng không có Hangfire.
  - Chi tiết WO có `assignee_eligible` (tính lúc đọc) để Quản lý biết mà giao lại.
  - Phía kỹ sư: bị gỡ xã thì filter commune trả 404; đổi vai trò thì `ROLE_FORBIDDEN`; bị khoá thì token còn sống tối đa 60 phút, cùng luật với `/auth/me`.
- Quản lý không đổi được vai trò hay xã của ai (D-R4).
- **Có** endpoint `GET /work-orders/assignees?commune_id=` (`ManageWorkOrders`) cho dropdown của WP5. Nó chỉ trả `{user_id, full_name}` của người đủ điều kiện. Đây không phải API quản lý user của BE-33.

**Tác động API:** có.

### D8 — Lịch làm việc

**CHỐT**
- Thêm `scheduled_date date NULL`: ngày bắt đầu ca. Ca đêm cắt qua nửa đêm thì lấy **ngày của buổi tối**, cùng quy ước với `night_of`.
- CHECK `scheduled_date <= due_date` khi cả hai cùng có.
- "Lịch" là `GET /work-orders?assigned_to=&scheduled_from=&scheduled_to=`, hai đầu khoảng đều đóng, kiểu `DateOnly` dạng `YYYY-MM-DD`. Vì là `DateOnly` nên không có vấn đề múi giờ; `UtcNormalization` chỉ áp cho `DateTime` (ràng buộc BE-REVIEW-02 số 2).
- Không có bảng ca và không có endpoint lịch riêng. Bảng ca chỉ đáng làm khi có yêu cầu nhiều ca trong một ngày, mà phiếu không nêu.

**Tác động API:** có (trường và query param mới).

### D9 — Chia lát, và hình dạng endpoint

**Phương án chia PR**

| | Rủi ro |
|---|---|
| A. Một PR gộp audit, entity, luồng và giao việc | Diff ước khoảng 4–6k dòng (tính cả file Designer của migration). Review khó. Và **toàn bộ** PR chạm API nên phải ESCALATE ở FW, tức phần audit không chạm API cũng bị giữ lại theo. |
| **B. BE-23a (audit) rồi BE-23 (WO), tuần tự** | BE-23a ước khoảng 1–1,5k dòng và **không chạm API**, nên theo FW-00 mục 3 nếu im lặng thì được duyệt. BE-19 và D-R7 dùng lại được ngay. |

**CHỐT: phương án B.**
- Nhánh `feat/BE-23a-audit-event` rồi `feat/BE-23-work-orders`. Không xếp chồng PR: BE-23 chỉ rebase lên `dev` **sau khi** BE-23a đã merge. Cả hai chỉ tạo từ `dev` sau khi v1.4 đã merge.
- **Endpoint hành động, không dùng `PATCH wo_status` chung.** Một `PATCH` cho cả Quản lý lẫn Kỹ sư sẽ phải mang một capability gồm hai vai trò, cộng một lớp kiểm vai trò viết tay bên trong. Ma trận capability khi đó không còn nói được ai được làm gì. Mỗi nút bấm một endpoint thì:
  - một endpoint ứng với một capability và một hành động audit;
  - khớp với `op_type` của `sync/push` (BE-43).
- Chi tiết endpoint ở mục 4. `PATCH` chỉ còn sửa phần chi tiết của WO.

**Tác động API:** có. §5.5 đổi ngữ nghĩa của `PATCH`.

### D10 — `task_kind` và đối tượng của việc

**CHỐT**
- **Enum `task_kind: inspection | repair`**, cột `task_kind text NOT NULL`, CHECK sinh bởi `HasContractEnum`. Giá trị **bất biến** sau khi tạo. Một bảng `work_order` cho cả hai loại, dùng chung prefix `WO`.
  - Tách hai bảng sẽ làm listing, `work_orders[]` của sync và bảng prefix đều phải đổi, mà không mua được gì.
- **Đối tượng của việc:**
  - repair: bắt buộc `fault_ids[]` có ít nhất 1 phần tử;
  - inspection: **hoặc** `fault_ids[]` có ít nhất 1, **hoặc** đúng một `segment_id` (không có fault), không được cả hai.
  - Chưa có đối tượng là một cột đơn lẻ không kèm fault; khi D-R23 cần thì thêm vào (thay đổi cộng thêm).
  - `fault_ids` **bất biến**: muốn đổi thì huỷ WO rồi tạo lại.
- **Kết quả kiểm tra ghi theo từng fault** trên dòng nối: `inspection_outcome: fault_present | fault_absent | inconclusive`. Một kết quả chung cho cả WO-0001 (7 fault) sẽ mất thông tin.
  - Inspection theo đoạn tuyến không có outcome. Kỹ sư mô tả trong `report_note` và báo lỗi mới qua `POST /faults` (BE-41).
- Kết quả kiểm tra **không** tự đề xuất hay tự tạo repair, vì như thế cần một actor hệ thống. Nó chỉ là căn cứ để Quản lý confirm hoặc reject (BE-19) rồi tự tạo repair.
- **Chỉ nêu, không mở rộng:** `fault_present` trên `lamp_out` là quan sát bằng mắt ngoài thực địa, tức ứng viên ground truth lớp `out` (`data_source = field`). Ticket này không ghi quan sát đó thành dữ liệu RQ1; việc đó treo theo D-R23.
- **Mock:** không sửa file `mock-work-orders.json`. Loại việc của mock nằm ở một file gán riêng, theo tiền lệ D-7 (`mock-pole-feeders.csv`):

  `mocks/mock-work-order-kinds.csv` = `WO-0001,inspection` · `WO-0002,repair` · `WO-0003,inspection`

  - WO-0001 có tiêu đề "Sự cố cả đoạn" nhưng đa số fault còn `detected`, nên là kiểm tra xác minh nguyên nhân cả đoạn.
  - WO-0003 có tiêu đề "Kiểm tra", khớp.

**Tác động API:** có (§3.1 thêm hai enum, BREAKING với body của `POST` vì `task_kind` là trường bắt buộc mới).

### D11 — Trường dẫn xuất, seed, cảnh báo bảo hành

**Hiện trạng**
- Trong mock, `priority_score` của WO **không** bằng max của các fault: WO-0001 là 98.0 trong khi max 92.9; WO-0002 là 74.2 trong khi max 96.3; WO-0003 là 66.4 trong khi max 72.5 (`mock-work-orders.json:22,38,53` đối chiếu `mock-faults.json`).
- WO-0002 gồm SEG-001 và SEG-002 nhưng ghi `SEG-001`.
- FAULT-0024 trong WO-0001 có `cluster_id: null` (`mock-faults.json:91`). Ticket ghi "0018–0024 CLS-001" là chưa chính xác.
- Seed hiện chưa nạp WO (`seed_mock_set.py:28-31`); lệnh `DELETE` không có điều kiện (`:92-93`).

**CHỐT**

| Trường | Cách có | Luật |
|---|---|---|
| `segment_id` | **Chụp lúc tạo** (lưu cột) | Tuyến của fault có `priority_score` cao nhất (null xếp cuối). Hoà thì theo `created_at, length(fault_id), fault_id`. Bỏ qua fault không có tuyến; null nếu không fault nào có. Inspection theo đoạn thì lấy chính đoạn đó. Tái tạo đúng cả 3 WO của mock. |
| `cluster_id` | **Chụp lúc tạo** | Nếu tập các `cluster_id` khác null của các fault có **đúng 1** phần tử thì lấy phần tử đó, ngược lại null. Ra WO-0001 = CLS-001, WO-0002/0003 = null, khớp mock. Chụp lại vì mục đích WO không đổi khi CV-15 gom cụm lại. |
| `priority_score` | **Tính lúc đọc** | Max của `fault.priority_score` trên **mọi** dòng nối (kể cả đã giải phóng); null nếu không có. Khi BE-33 đổi trọng số, WO tự đúng theo, không thêm nợ tính lại. **Lệch giá trị mock**, cần đăng ký drift. |

- **Seed:** mở rộng `seed_mock_set.py` (chi tiết ở mục 11), **không** đi qua API.
- **Cảnh báo hết bảo hành trên WO:** để BE-31/BE-35 (D-R16, Contract §3.3). Ticket này không có trường đó.

**Tác động API:** có (ngữ nghĩa, và lệch giá trị mock).

---

## 3. Schema

Tên cột và tên ràng buộc dưới đây là **đề xuất**. Tên FK và index mà EF tự sinh phải **đọc trong migration sinh ra**, đừng dùng tên trong báo cáo này. Mọi FK là `Restrict`.

### 3.1 `audit_event` (BE-23a)

| Cột | Kiểu | Null | Ràng buộc |
|---|---|---|---|
| `audit_id` | bigint | NOT NULL | PK, `GENERATED ALWAYS AS IDENTITY` |
| `occurred_at` | timestamptz | NOT NULL | default `now()`, service luôn gán tường minh |
| `actor_kind` | text | NOT NULL | CHECK enum `user \| cv \| iot` |
| `actor_user_id` | text | NULL | FK `app_user` (khai ở `AppUserConfiguration`) |
| `actor_role` | text | NULL | CHECK enum `user_role`; là ảnh chụp vai trò lúc hành động, vì vai trò từng đổi tên (D-R6) |
| `commune_id` | text | NOT NULL | `HasCommuneReference`, `ICommuneScoped` |
| `entity_type` | text | NOT NULL | CHECK enum, BE-23a chỉ có `work_order`; BE-19 thêm `fault` |
| `entity_id` | text | NOT NULL | ID hiển thị, **không FK** (đa hình) |
| `action` | text | NOT NULL | CHECK enum `created \| assigned \| reassigned \| unassigned \| started \| completed \| verified \| returned \| cancelled \| details_changed` |
| `before_state`, `after_state` | jsonb | NULL | serialize bằng `LuxMapJsonOptions` (snake_case, enum dạng chuỗi, giờ UTC hậu tố Z); ánh xạ vào thuộc tính `string` để tránh bẫy dispose `JsonDocument` |
| `note` | text | NULL | lý do, báo cáo |
| `correlation_id` | text | NOT NULL | |

- **CHECK:**
  - `ck_audit_event_actor`: `(actor_kind = 'user') = (actor_user_id IS NOT NULL)`, và `(actor_user_id IS NULL) = (actor_role IS NULL)`;
  - `ck_audit_event_has_state`: `before_state IS NOT NULL OR after_state IS NOT NULL`;
  - `jsonb_typeof(x) = 'object'` cho từng cột jsonb khi khác null.
- **Index:** `ix_audit_event_entity` trên `(entity_type, entity_id, audit_id)`, và khai tường minh `ix_audit_event_commune_id`, `ix_audit_event_actor_user_id`.
- **Trigger:** xem D1 mục 6.

### 3.2 `work_order` (BE-23)

| Cột | Kiểu | Null | Ràng buộc |
|---|---|---|---|
| `work_order_id` | text | NOT NULL | PK, `HasPrefixedId(PrefixedIds.WorkOrder)` (`PrefixedId.cs:66`) |
| `commune_id` | text | NOT NULL | `HasCommuneReference`, `ICommuneScoped` |
| `task_kind` | text | NOT NULL | CHECK enum |
| `title` | text | NOT NULL | `ck_work_order_title_not_blank` = `btrim(title) <> ''`; API giới hạn 1..200 ký tự |
| `wo_status` | text | NOT NULL | CHECK enum (`HasContractEnum`) |
| `segment_id` | text | NULL | FK `road_segment` |
| `cluster_id` | text | NULL | FK `fault_cluster` |
| `assigned_to` | text | NULL | FK `app_user` |
| `assigned_at` | timestamptz | NULL | |
| `created_by` | text | NOT NULL | FK `app_user` |
| `due_date`, `scheduled_date` | date | NULL | |
| `note` | text | NULL | chỉ dẫn của Quản lý |
| `review_note` | text | NULL | ghi chú gần nhất lúc nghiệm thu, trả lại hoặc huỷ |
| `report_note` | text | NULL | báo cáo của Kỹ sư |
| `started_at`, `completed_at`, `closed_at` | timestamptz | NULL | |
| `created_at`, `updated_at` | timestamptz | NOT NULL | default `now()` |
| (không phải cột) `xmin` | — | — | concurrency token: thuộc tính `uint`, `IsRowVersion()` |

- **CHECK:**
  - `ck_work_order_assignee_matches_status`:
    `(wo_status = 'open' AND assigned_to IS NULL) OR (wo_status IN ('assigned','in_progress','done','verified') AND assigned_to IS NOT NULL) OR wo_status = 'cancelled'`
  - `ck_work_order_assigned_at_matches`: `(assigned_to IS NULL) = (assigned_at IS NULL)`
  - `ck_work_order_started`: `wo_status NOT IN ('in_progress','done','verified') OR started_at IS NOT NULL`
  - `ck_work_order_completed`: `wo_status NOT IN ('done','verified') OR (completed_at IS NOT NULL AND report_note IS NOT NULL)`
  - `ck_work_order_closed`: `(wo_status IN ('verified','cancelled')) = (closed_at IS NOT NULL)`
  - `ck_work_order_schedule_before_due`: `scheduled_date IS NULL OR due_date IS NULL OR scheduled_date <= due_date`
  - `ck_work_order_segment_target`: `task_kind = 'inspection' OR segment_id IS NOT NULL OR true`. Luật "có fault hoặc có đoạn" là luật liên bảng, nên **thực thi ở service**; CHECK này **không khai**. Ghi ra đây để người triển khai khỏi thêm một CHECK rỗng nghĩa.
- **Alternate key:** `ak_work_order_work_order_id_commune_id`. Đó là đích của FK ghép; đừng xoá vì tưởng thừa (cùng bẫy số 2 ở mục O-7).
- **Index** (khai tường minh): `ix_work_order_commune_id`, `ix_work_order_assigned_to`, `ix_work_order_created_by`, `ix_work_order_segment_id`, `ix_work_order_cluster_id`, `ix_work_order_wo_status`.
- **Không có DELETE.** Muốn bỏ một WO thì huỷ nó.

### 3.3 `work_order_fault`

| Cột | Kiểu | Null | Ràng buộc |
|---|---|---|---|
| `work_order_id` | text | NOT NULL | PK ghép với `fault_id` |
| `fault_id` | text | NOT NULL | |
| `commune_id` | text | NOT NULL | `HasCommuneReference`, `ICommuneScoped` (bảng này có thể thành gốc truy vấn, theo quy tắc BE-09) |
| `linked_at` | timestamptz | NOT NULL | default `now()` |
| `released_at` | timestamptz | NULL | CHECK `released_at IS NULL OR released_at >= linked_at` |
| `inspection_outcome` | text | NULL | CHECK enum; luật "chỉ khi WO là inspection" thực thi ở service |

- **FK ghép:**
  - `(work_order_id, commune_id)` trỏ tới `work_order`;
  - `(fault_id, commune_id)` trỏ tới `fault`. Cái này cần **alternate key mới trên `fault`** (`ak_fault_fault_id_commune_id`), nên migration có thêm một unique constraint trên bảng `fault`.
- **Index:**
  - `ux_work_order_fault_fault_id_active`: UNIQUE `(fault_id) WHERE released_at IS NULL`;
  - **khai tường minh** `ix_work_order_fault_fault_id` (index đầy đủ). Theo quy ước partial index, convention của EF sẽ bỏ index FK khi đã có một partial index dẫn đầu bằng cùng cột;
  - `ix_work_order_fault_commune_id` tường minh.
- **Khi đọc migration sinh ra, soi kỹ ba thứ:**
  - không có `DropIndex` nào mình không yêu cầu, **đặc biệt** là `ix_fault_*`;
  - không có `AddColumn xmin`;
  - `Down()` đối xứng với `Up()`.

### 3.4 `fault` (thay đổi tối thiểu)

- Thêm alternate key ở trên.
- Thêm `xmin` làm concurrency token (không thêm cột).
- **Không** thêm cột `work_order_id`.
- Chú thích ở `Fault.cs:16-26` phải sửa theo D-R13.

---

## 4. Endpoint, capability, người được giao

| Method + path | Capability | Body / query | Trả về |
|---|---|---|---|
| `GET /api/v1/work-orders` | Read | `wo_status` (CSV), `task_kind`, `assigned_to` (`USR-…` hoặc `me`), `segment_id`, `commune_id[]` (`Narrow`, ngoài phạm vi trả 403), `scheduled_from`, `scheduled_to`, `page`, `page_size` | 200 `PagedResult<WorkOrderItem>` |
| `GET /api/v1/work-orders/{id}` | Read | — | 200 `WorkOrderDetail`, hoặc 404 |
| `GET /api/v1/work-orders/assignees` | Manage | `commune_id` (**bắt buộc**, đúng 1 giá trị, `Narrow`), `page` | 200 `PagedResult<{user_id, full_name}>` |
| `POST /api/v1/work-orders` | Manage | `{task_kind, title, fault_ids[]?, segment_id?, assigned_to?, due_date?, scheduled_date?, note?}` | 201, header `Location`, body là detail |
| `PATCH /api/v1/work-orders/{id}` | Manage | `{title?, due_date?, scheduled_date?}` | 200 detail |
| `PUT /api/v1/work-orders/{id}/assignee` | Manage | `{"assigned_to": "USR-…" \| null}`, khoá bắt buộc | 200 detail |
| `POST …/{id}/start` | **Execute** | — | 200 detail |
| `POST …/{id}/complete` | **Execute** | `{report_note (bắt buộc, ≥10 ký tự sau trim), fault_outcomes[]{fault_id, outcome}?}` | 200 detail |
| `POST …/{id}/verify` | Manage | `{note?}` | 200 detail |
| `POST …/{id}/return` | Manage | `{note}` bắt buộc, không rỗng | 200 detail |
| `POST …/{id}/cancel` | Manage | `{note}` bắt buộc, không rỗng | 200 detail |

- **Thứ tự kiểm:** 401 → 403 `ROLE_FORBIDDEN` → 400 (validate model) → 404 (filter commune và người được giao) → 409.
  - Endpoint `complete` validate body **trước** khi tra WO. Nhờ vậy nó làm được probe cho `ExecuteWorkOrders` trong `RoleCapabilityMatrixTests`: body rỗng cho ra 400 nếu vai trò được vào, 403 nếu không.
- **`wo_status` và `task_kind` trên query string:** so với tên trên dây bằng `JsonNamingPolicy.SnakeCaseLower`, **không** dùng `Enum.TryParse`. `in_progress` là giá trị nhiều từ nên dính bẫy số 1 của BE-14.
- **Thứ tự listing:** `created_at DESC, length(work_order_id) DESC, work_order_id DESC`. Thứ tự `fault_ids[]`: `fault.created_at, length(fault_id), fault_id`.
- **POST:**
  - `fault_ids` không được trùng và tối đa 200 phần tử;
  - các trường `work_order_id`, `commune_id`, `wo_status`, `cluster_id`, `priority_score` bị cấm, trả 400 `SERVER_OWNED_FIELD`; giữ các khoá này dưới dạng `JsonElement` để phát hiện chúng có được gửi hay không;
  - gửi `segment_id` kèm fault, hoặc với repair: 400 `VALIDATION_FAILED`;
  - có `assigned_to` thì WO vào thẳng `assigned`, audit chỉ **1** dòng `created`.
- **ID của WO sinh trước khi insert:** `SELECT {PrefixedIds.WorkOrder.DefaultValueSql}`, vẫn cùng sequence và cùng hàm format.
  - Lý do: dòng audit và các dòng nối cần ID ngay trong **cùng một** `SaveChanges`.
  - Hỏng giữa chừng thì dãy số có khoảng trống, điều đã được chấp nhận ở §1.2.
- **`WorkOrderItem`:** các trường của §5.5 cộng thêm `task_kind`, `commune_id`, `scheduled_date`, `updated_at`.
- **`WorkOrderDetail`:** item cộng thêm:
  - `note`, `review_note`, `report_note`, `created_by`;
  - `assigned_at`, `started_at`, `completed_at`, `closed_at`;
  - `assignee_eligible` (bool, hoặc null khi chưa giao);
  - `allowed_actions[]` (tính theo người gọi, trong tập `assign`, `unassign`, `edit`, `start`, `complete`, `verify`, `return`, `cancel`);
  - `faults[]{fault_id, pole_id, segment_id, location{lat,lng}, fault_type, fault_status, severity, inspection_outcome}`. `location` lấy từ `lat`/`lng` của fault, hoặc từ geometry 4326 của cột. Mobile cần cái này để dẫn đường, vì BE-40 chưa có.
- **Lát mỏng của BE-25:** chỉ là `GET /work-orders?assigned_to=me` cộng với filter D6. Gom việc theo địa lý nằm ngoài ticket.
- `POST …/evidence` thuộc BE-24, không làm ở đây. Đề xuất cho BE-24 dùng capability `ExecuteWorkOrders`.

## 5. Ngữ nghĩa null của `PATCH` / `PUT`

- **`PATCH`:**
  - mỗi trường giữ dưới dạng `JsonElement`: **thiếu khoá** thì giữ nguyên; **`null`** thì xoá giá trị (chỉ cho `due_date` và `scheduled_date`); `title: null` thì 400;
  - các khoá `wo_status`, `assigned_to`, `fault_ids`, `task_kind`, `segment_id` trả 400 `VALIDATION_FAILED` kèm `details` chỉ endpoint đúng; `commune_id` trả 400 `SERVER_OWNED_FIELD`;
  - body `{}` trả 400;
  - chỉ cho phép ở `open`, `assigned`, `in_progress`; trạng thái khác trả 409 `INVALID_STATE_TRANSITION`;
  - giá trị mới trùng giá trị cũ: **200, không ghi audit, không đổi `updated_at`**.
- **`PUT assignee`:** theo khuôn `SetPoleFeederRequest` (`AssetsController.cs:434-454`). Thiếu khoá thì 400. Giao lại đúng người đang được giao, hoặc `null` khi WO vốn chưa ai nhận, thì là no-op: 200, không ghi audit.
- Dùng một helper chung trong `LuxMap.Shared.Http` để đọc "vắng / null / có giá trị", tránh viết lại logic đó bốn lần. `SetPoleFeederRequest` **để nguyên**, không refactor.

## 6. Bảng chuyển trạng thái: `wo_status` × hành động × vai trò

Ký hiệu: **M** = Quản lý (`ManageWorkOrders`); **FE\*** = Kỹ sư hiện trường **đang được giao chính WO đó** (`ExecuteWorkOrders`). Kỹ sư khác nhận 404. Superior và Quản trị gọi bất kỳ hành động nào ở đây đều nhận 403 `ROLE_FORBIDDEN`.

| Hiện tại | assign / reassign (M) | unassign (M) | edit (M) | start (FE\*) | complete (FE\*) | verify (M) | return (M) | cancel (M) |
|---|---|---|---|---|---|---|---|---|
| `open` | → `assigned` | no-op 200 | ✓ | 409 | 409 | 409 | 409 | → `cancelled` |
| `assigned` | người khác → `assigned` (`reassigned`); cùng người: no-op | → `open` | ✓ | → `in_progress` | 409 | 409 | 409 | → `cancelled` |
| `in_progress` | người khác → **`assigned`**; cùng người: no-op | → `open` | ✓ | 409 | → `done` | 409 | 409 | → `cancelled` |
| `done` | 409 | 409 | 409 | 409 | 409 | → `verified` | → `in_progress` | 409 |
| `verified`, `cancelled` | 409 | 409 | 409 | 409 | 409 | 409 | 409 | 409 |

- Luồng giống nhau cho inspection và repair. Riêng `complete` của inspection **có fault** thì `fault_outcomes[]` phải phủ **đúng một lần** mọi fault đã nối; repair và inspection theo đoạn thì không được gửi trường này. Sai trả 400.
- **Lan truyền sang fault** đi qua `FaultTransitions`, được ghi vào `after_state.fault_changes[]{fault_id, from, to}` của **chính** dòng audit của WO.

| Hành động | inspection | repair |
|---|---|---|
| `start` | không đổi fault | `confirmed → in_progress`; fault đã `in_progress` thì giữ nguyên |
| `complete` | ghi `inspection_outcome` lên dòng nối; `fault_status` không đổi | không đổi fault |
| `return` | không đổi | không đổi |
| `verify` | giải phóng dòng nối | `in_progress → resolved → verified`; `resolved_by = assigned_to`, `resolved_at = completed_at`; giải phóng dòng nối |
| `cancel` | giải phóng dòng nối | giải phóng; fault `in_progress` **vẫn giữ** (vẫn tính là mở, repair sau nhận lại được) |
| đổi người giao | không đổi | không đổi |

- Fault không ở đúng trạng thái xuất phát thì **bỏ qua**, liệt kê trong `after_state.fault_skipped[]`.
- **Ràng buộc truyền cho BE-19:** fault đang nằm trong một **repair** đang hoạt động thì `PATCH /faults` phải trả 409; mã lỗi do BE-19 chốt. Fault nằm trong inspection thì BE-19 vẫn confirm hoặc reject bình thường.

## 7. Audit: đúng 1 dòng, cùng transaction, append-only, teardown

- **Mỗi thao tác 2xx làm đổi trạng thái ghi đúng 1 dòng.** No-op 200 ghi 0 dòng. Mọi phản hồi 4xx hoặc 5xx ghi 0 dòng.
- `created` ghi `after_state` là ảnh chụp đầy đủ, gồm cả `fault_ids`. Các hành động khác ghi `before_state`/`after_state` gồm các trường đổi, cộng `wo_status`.
- **Cùng transaction:** một lần `SaveChanges` là một transaction ngầm của EF. Thua race `23505` (unique index) hay `DbUpdateConcurrencyException` thì EF rollback cả WO, dòng nối, fault lẫn dòng audit. Sau đó gọi `ChangeTracker.Clear()` rồi mới ném 409. Mẫu này đã có ở `AssetCrudService.cs:431-444` và `LuxReadingService.cs:84-94`.
- **Các lớp append-only**

  | Lớp | Chặn được | Không chặn được |
  |---|---|---|
  | Guard EF (D1 mục 5a) | Sửa hoặc xoá qua EF, kể cả trong backdoor | SQL thô |
  | Trigger DB | `UPDATE`, `DELETE`, `TRUNCATE` bằng SQL thô, từ app lẫn từ test | superuser `DISABLE TRIGGER` hoặc `session_replication_role` |
  | Test quét `luxmap.audit_purge` trong `src/` | Code thật mở lối dọn | — |
  | `ExecuteUpdate`/`ExecuteDelete` | Đã bị cấm (RS0030 và `BannedBulkWriteApiTests`) | — |

- **Thứ tự teardown** của các test WO, trong `AssetDatabaseCollection`: một transaction có `SET LOCAL luxmap.audit_purge='on'` để xoá `audit_event` → `work_order_fault` → `work_order` → `fault` → `fault_cluster` → `lux_reading` → `fixture` → `pole` → tài khoản throwaway (có dọn `refresh_token`) → xã.
  - Chỉ dùng tài khoản throwaway. **Không bao giờ** khoá hay đổi `admin`, `agency`, `engineer`, `crew` (bẫy test REG-v1.2).
  - Test phải thêm assembly WorkOrders vào `ScopedEntityNames` (`BannedBulkWriteApiTests.cs:248-257`).

## 8. Mã lỗi (🆕 = mới, cần đăng ký drift vào §1.4)

| Mã | HTTP | Khi nào |
|---|---|---|
| 🆕 `WORK_ORDER_NOT_FOUND` | 404 | WO không tồn tại, ngoài phạm vi xã, hoặc Kỹ sư không phải người được giao |
| 🆕 `FAULT_NOT_FOUND` | 404 | `fault_ids` không tồn tại hoặc ngoài phạm vi; `details.fault_ids[]` |
| `ASSET_NOT_FOUND` | 404 | `segment_id` của inspection theo đoạn |
| 🆕 `INVALID_STATE_TRANSITION` | 409 | Hành động sai luồng hoặc sửa WO ở trạng thái không cho phép; `details{work_order_id, wo_status, action, allowed_actions[]}`. Đặt tên chung để BE-19 dùng lại |
| 🆕 `FAULT_ALREADY_IN_WORK_ORDER` | 409 | Fault đang ở một WO đang hoạt động; `details{fault_id, work_order_id}` |
| 🆕 `FAULT_STATUS_NOT_ELIGIBLE` | 409 | Xem D3 |
| 🆕 `ASSIGNEE_NOT_ELIGIBLE` | 409 | Xem D7 |
| 🆕 `CONCURRENT_MODIFICATION` | 409 | Xung đột `xmin`; `details{wo_status}` hiện tại; thử lại có thể thành công |
| `CROSS_COMMUNE_REFERENCE` | 409 | Các fault thuộc nhiều xã |
| `VALIDATION_FAILED` / `SERVER_OWNED_FIELD` | 400 | Như mục 4 và 5 |
| `ROLE_FORBIDDEN` / `COMMUNE_FORBIDDEN` | 403 | Vai trò / tham số `commune_id` |

## 9. Concurrency

| Ca | Cơ chế | Kết quả |
|---|---|---|
| Hai POST cùng lúc nối cùng một fault | `ux_work_order_fault_fault_id_active` | Đúng một 201, một 409 `FAULT_ALREADY_IN_WORK_ORDER`; bên thua không để lại WO hay dòng audit nào |
| Hai hành động cùng lúc trên một WO (verify với return; start với reassign) | `xmin` của `work_order` | Một 200, một 409 `CONCURRENT_MODIFICATION` |
| Lan truyền sang fault cùng lúc với BE-19 sửa fault | `xmin` của `fault` (thêm ở ticket này, không cần migration) | Bên sau nhận 409 |

⚠️ Tôi đã kiểm lại tài liệu provider Npgsql: một thuộc tính `uint` khai `IsRowVersion()` được **tự** ánh xạ sang cột hệ thống `xmin`. Riêng việc migration sinh ra có chứa `AddColumn` hay không thì **chưa kiểm được** khi chưa chạy `migrations add`; Phase 2 phải đọc migration sinh ra để xác nhận.

## 10. Seed và các xung đột (mở rộng `seed_mock_set.py` trong PR BE-23)

1. **Guard mới.** `RAISE` nếu `audit_event` có bất kỳ dòng nào với `entity_type = 'work_order'`. Lý do:
   - seed xoá WO không điều kiện, và ID được nạp lại (`WO-0001`) sẽ nhận nhầm lịch sử audit cũ;
   - cùng lập trường với guard của `lux_reading` (`:84-89`).
2. **Thứ tự `DELETE`:** `work_order_fault` → `work_order` → `fault` → …
3. **Chèn WO với ID tường minh** (ngoại lệ D-6 đã có):
   - `created_by` tra theo username `engineer`;
   - `assigned_to` tra theo username `crew`. `RAISE` nếu `crew` không phải `field_engineer`, bị khoá, hoặc không có xã `study_site`;
   - `task_kind` đọc từ `mocks/mock-work-order-kinds.csv`;
   - `segment_id` và `cluster_id` lấy theo mock (khớp luật D11);
   - `assigned_at` = `created_at` cho WO-0001 và WO-0002; `started_at` = `created_at` cho WO-0002. Đây là **lệch**: mock không có hai giá trị này;
   - `scheduled_date` để NULL.
4. **Dòng nối:** `released_at` và `inspection_outcome` để NULL.
5. **🔴 Xung đột dữ liệu:** WO-0002 là repair đang `in_progress`, nên theo D4 ba fault FAULT-0003, 0007, 0011 phải ở `in_progress`, trong khi `mock-faults.json` ghi `confirmed`.
   - **Chốt:** seed ghi chúng là `in_progress`. Nếu để `confirmed`, lượt verify sau này sẽ bỏ qua cả ba fault.
   - `OpenFaultCountTests` chỉ so hai file mock nên không bị ảnh hưởng.
   - Hệ quả trên DB, suy ra từ số liệu của ticket chứ **chưa đo**: `COM-070` còn 4 `confirmed`, 3 `in_progress`, 21 `detected`.
6. **Không ghi audit cho dữ liệu seed.** Seed không phải quyết định của ai, nên lịch sử của các WO seed bắt đầu rỗng.
7. **`setval`** `work_order_id_seq` vượt qua giá trị lớn nhất đã dùng.

## 11. Drift cần đăng ký ở Phase 2, vào `contract-drift.md`, sau khi Mỹ ký lại

| # (đề xuất) | Nội dung | Chạm API |
|---|---|---|
| WO-1 | Ba capability vào §2 (đóng phần work order của O-2) | Có |
| WO-2 | Máy trạng thái `wo_status` (đóng O-3) và lan truyền trạng thái sang fault | Có |
| WO-3 | Enum mới `task_kind`, `inspection_outcome` vào §3.1 | Có |
| WO-4 | Endpoint mới: `GET /{id}`, năm endpoint hành động, `PUT /assignee`, `GET /assignees`; `PATCH` thu hẹp so với §5.5 | Có |
| WO-5 | `POST` có trường bắt buộc mới `task_kind` (**BREAKING** so với body ở §5.5); item và detail thêm trường | Có |
| WO-6 | Ngữ nghĩa trường dẫn xuất; `priority_score` của mock lệch (98.0/74.2/66.4 so với 92.9/96.3/72.5) | Có |
| WO-7 | §5.4 bỏ câu "`work_order_id` luôn null tới BE-21" | Có |
| WO-8 | Sáu mã lỗi mới vào §1.4 | Có |
| WO-9 | Mock thiếu `task_kind` (có file gán riêng); seed ghi FAULT-0003/0007/0011 là `in_progress` | Có (dữ liệu FE nhìn thấy) |
| WO-10 | BE-19 phải 409 khi sửa fault đang nằm trong repair hoạt động | Có (hành vi tương lai) |
| WO-11 | §3.3: thiết kế audit chuyển sang BE-23a; bảng nội bộ, không có endpoint | Không |

## 12. Chỗ lệch với `tasks-backend.csv`: ghi vào `tracking.html`, Mỹ quyết có sửa task list hay không

- **BE-21** (`:24`): *"xác nhận sự cố là sinh ngay work order kèm due_at"* → **bỏ.** Không có actor hệ thống, Quản lý tự tạo WO. SLA và `ExternalUnit` ra khỏi phạm vi; phiếu v1.4 không có actor đơn vị ngoài.
- **BE-22** (`:25`): wording "Mới / Đã đóng" → chuyển sang dùng `wo_status`.
- **BE-23** (`:26`): "tổ sửa chữa / đơn vị bên ngoài" → **chỉ Kỹ sư hiện trường**.
- **BE-25** (`:28`): phần gom theo địa lý vẫn là việc còn lại.
- **BE-19** (`:22`) và Contract §3.3: thiết kế audit **kéo lên BE-23a**.
- Mốc thời gian W9–W12 trong csv so với hôm nay (đang W3) là việc riêng của Mỹ (D-R8/D-R9). Tôi không đụng.

## 13. Test cho Phase 2 và cách phá hoại để thấy test đỏ

| Test | Phủ | Phá hoại làm test ĐỎ |
|---|---|---|
| `CapabilityMatrixTests` và `RoleCapabilityMatrixTests` (thêm 3 dòng literal; probe là GET list, POST `{}`, POST `/complete` `{}`) | 4 vai trò × 3 capability | Thêm `superior` vào `ManageWorkOrders` |
| `WorkOrderStateMachineTests` (Shared, không cần Docker) | Bảng literal 6 trạng thái × 9 hành động | Cho phép `cancel` từ `done` |
| `WorkOrderTransitionHttpTests` | Cùng bảng đó qua HTTP, dựng trạng thái bằng backdoor | Bỏ kiểm `done` trong endpoint `return` |
| `WorkOrderActorTests` | Kỹ sư được giao ✓ / kỹ sư khác 404 / Quản lý gọi start 403 / Superior và Quản trị: đọc 200, ghi 403 | Gỡ `IAssigneeScoped` khỏi `WorkOrder` |
| `AssigneeFilterTests` | Listing chỉ có việc của mình, `me`, `assigned_to=` người khác ra rỗng, khởi động thất bại khi `IAssigneeScoped` mà không `ICommuneScoped` | Gọi `HasQueryFilter` lần thứ hai (ghi đè filter commune) |
| `WorkOrderScopeTests` | WO ngoài xã 404; fault ngoài xã 404 `FAULT_NOT_FOUND`; fault hai xã 409; `commune_id` trong body 400; `?commune_id=` ngoài phạm vi 403 | Lấy `commune_id` từ body |
| `WorkOrderAssigneeTests` | Không phải FE, bị khoá, khác xã, không tồn tại: cùng 409 và `details` giống hệt; endpoint `/assignees`; thiếu khoá 400; reassign lúc `in_progress` về `assigned` | Kiểm xã theo claim thay vì theo DB |
| `FaultEligibilityTests` (có bản Shared literal 6 `fault_status` × 2 loại việc) | D3 | Thêm `Rejected` vào `Open` |
| `FaultLinkTests` | 409 khi fault đang ở WO khác; nối lại được sau verify hoặc cancel; **race** hai POST song song | Bỏ `WHERE` khỏi partial index, hoặc đổi thành index không unique |
| `FaultPropagationTests` | Repair start/verify/cancel; inspection không bao giờ đổi `fault_status`; `resolved_by/at` đúng | Lan truyền `done → resolved` |
| `AuditTrailTests` | Mỗi endpoint 2xx: đúng 1 dòng theo `X-Correlation-Id`; no-op: 0; 4xx: 0; race thua: 0 (rollback) | Gọi `SaveChanges` hai lần trong một thao tác |
| `AuditRequiredTests` | Sửa `WorkOrder` mà không có audit thì ném lỗi; có 2 dòng cũng ném; trong backdoor thì cho qua | Bỏ luật 5b |
| `AuditAppendOnlyTests` | SQL `UPDATE`/`DELETE`/`TRUNCATE` bị trigger chặn; EF `Modified` ném trước SQL kể cả trong backdoor; purge có GUC chỉ chạy được trong transaction | Drop trigger |
| `AuditPurgeScanTests` (Persistence, không Docker) | `luxmap.audit_purge` không có trong `src/` ngoài `Migrations/` | Đặt lệnh purge vào service |
| `BannedBulkWriteApiTests` | Thêm assembly WorkOrders; `WorkOrder`, `WorkOrderFault`, `AuditEvent` là entity có scope | — |
| `WorkOrderConcurrencyTests` | verify ∥ return: một 200, một 409, 1 dòng audit | Bỏ `IsRowVersion()` |
| `WorkOrderListingTests` | `wo_status=in_progress`; thứ tự **có thứ tự** với ID hai bên ngưỡng `WO-9999`/`WO-10000`, ghi trong cùng một transaction; `setval` lùi xong **trả lại sequence** (bẫy số 4 của BE-14) | Sắp theo `work_order_id` trần |
| `DerivedFieldTests` | Tái tạo `segment_id` và `cluster_id` của 3 WO mock; `priority_score` đổi khi fault đổi | Lấy `segment_id` của fault đầu tiên |
| `ScheduleFilterTests` | Khoảng `DateOnly` đóng hai đầu; `scheduled > due` trả 400 ở API và 23514 ở DB | Bỏ CHECK |
| `MockWorkOrderKindsTests` (Shared, không Docker) | Mọi WO mock hợp lệ theo D3/D10 khi đối chiếu `mock-faults.json` và file loại việc | Đổi WO-0001 sang `repair` |

Kiểm tay: đọc `Up()`/`Down()` của cả hai migration trước khi apply; chạy `\du` và `\d` trên `luxmap_test`; migrate `luxmap_dev` chỉ khi toàn bộ test đã xanh.

---

## Kết quả

✅ **Đã kiểm:** các `file:line` dẫn ở trên đều đọc trực tiếp trong working tree. Các con số mock (priority, segment, cluster, FAULT-0024) đều đọc thẳng từ file.

⚠️ **Chưa kiểm được:**
- mọi output psql (`\d`, phép đếm, `\du`);
- trạng thái git hiện tại, vì phiên này không có shell;
- migration EF có sinh `AddColumn xmin` hay không (tài liệu nói là không);
- role `luxmap` có phải superuser không.

📌 **Giả định còn lại:**
- v1.4 merge mà không đổi các đoạn tôi dẫn;
- BE-33 chấp nhận để sự kiện không thuộc xã nào ra ngoài `audit_event`.

📌 **Ba phát hiện bên lề, không sửa:**
- `FaultConfigurations.cs:157-162` ghi chú là "DESC NULLS LAST", nhưng `\d` của ticket chỉ in `priority_score DESC`, mà Postgres mặc định `NULLS FIRST` khi DESC. Nên kiểm lại ở BE-40.
- `authorization-guide.md:174` ghi mã `NOT_FOUND` cho tài nguyên ngoài phạm vi, trong khi Contract dùng mã theo từng loại tài nguyên.
- `Fault.cs:16-26` đã lỗi thời so với D-R13.

**Việc tiếp theo:** Mỹ ký lại các mục chạm API → merge v1.4 → Codex làm Phase 2 của BE-23a → merge → Phase 2 của BE-23.
