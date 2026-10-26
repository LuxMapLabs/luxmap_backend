---
ticket: WO-CANCEL-REQUEST
title: Kỹ sư xin hủy / từ chối nhận lịch, Quản lý duyệt hoặc bác bỏ
status: ready
phase: 2
owner: codex
branch: docs/WO-CANCEL-REQUEST-p1
contract_refs:
  - "Contract mục 5.5 (work orders) và 5.9 (notifications) — chưa có bước xin hủy"
depends_on:
  - "Đề xuất của FE (WP5) 26/10/2026 — nội dung ở mục Bối cảnh; chưa được Mỹ duyệt"
---

## Bối cảnh

FE đề xuất để kỹ sư xin hủy hoặc từ chối nhận một phiếu đã giao, kèm lý do bắt buộc, mà **không thêm giá trị vào enum `wo_status`** (khoá cứng) —
dùng một sub-state riêng. Luồng mong muốn:

- **Kỹ sư (Mobile):** mở chi tiết lịch → "Xin hủy / Từ chối nhận lịch" → lý do bắt buộc → gửi.
- **Quản lý (Web):** nhận thông báo tức thời (badge / banner trên Lịch điều phối), xem lý do, rồi quyết định:
  - **Duyệt, 1a:** giao lại cho kỹ sư khác cùng xã → đã có `PUT /work-orders/{id}/assignee`.
  - **Duyệt, 1b:** hủy hẳn → đã có `POST /work-orders/{id}/cancel`.
  - **Bác bỏ:** phiếu giữ nguyên cho kỹ sư đó; Quản lý **bắt buộc** nhập lý do; hệ thống gửi thông báo về Mobile.

Đề xuất kỹ thuật của FE (CHƯA duyệt, phải đánh giá chứ không mặc nhiên làm theo):

- 5 cột mới trên `work_order`: `cancellation_request_status` (`none|pending|rejected`, mặc định `none`), `cancellation_request_note`,
  `cancellation_requested_at`, `cancellation_reject_note`, `cancellation_rejected_at`.
- `POST /work-orders/{id}/request-cancellation` (`ExecuteWorkOrders`; phiếu `assigned` hoặc `in_progress`; body `{reason}`; báo mọi Quản lý của xã
  qua `Notifier.ManagersCoveringAsync`).
- `POST /work-orders/{id}/reject-cancellation` (`ManageWorkOrders`; đang `pending`; body `{reject_reason}`; báo kỹ sư).
- Hai `NotificationType` mới: `WorkOrderCancellationRequested`, `WorkOrderCancellationRejected`.
- Duyệt = Quản lý gọi `PUT /assignee` hoặc `POST /cancel` sẵn có, backend tự reset cờ về `none`.

Consumer: WP5 (Web), WP6 (Android). Bên review của Claude đã kiểm sơ bộ (chưa phải kết luận, Codex phải tự xác minh lại từ code):
`ck_notification_type` và `ck_audit_event_action` là CHECK liệt kê giá trị ở DB nên thêm type / action mới cần migration; `AuditAction` chưa có giá trị hợp nghĩa;
`WorkOrderRules.AllowedActions` cứng `["start","complete"]` cho kỹ sư; `Assign` đặt lại `wo_status = assigned` và xoá `started_at`; `PATCH` dời lịch không đi qua
`/assignee` hay `/cancel`; `SyncOpType` chỉ có `WorkOrderStart` / `WorkOrderComplete`; thân thông báo bị CHECK ≤ 1000 ký tự, tiêu đề ≤ 200.

## Phải đọc trước

- `CLAUDE.md` + `docs/agents/traps/faults-workorders.md` (audit, thông báo, đồng bộ offline), `migrations.md`, `commune-scope.md`, `auth-accounts.md` (capability)
- `src/LuxMap.Modules.WorkOrders/` — `WorkOrderService.cs`, `WorkOrderRules.cs`, `WorkOrdersController.cs`, `WorkOrderNotices.cs`, `WorkOrderResponses.cs`, `Entities/WorkOrder.cs`
- `src/LuxMap.Modules.Notifications/` (`Notifier.cs`, `Entities/Notification.cs`), `src/LuxMap.Persistence/Audit/AuditEvent.cs`
- `src/LuxMap.Modules.Sync/` (đường offline của kỹ sư), `docs/api-contract-v1.1.md` mục 5.5, 5.9 và changelog, `docs/contract-drift.md` (luật FR-*, BE-27 N-*)
- `.ai/results/BE-27-p1.md`, `BE-26-p1.md` (precedent thêm `NotificationType` + migration CHECK), `BE-23-p1.md` (máy trạng thái phiếu)

## Yêu cầu (Phase 1)

Khảo sát, đánh giá đề xuất FE, đề xuất thiết kế, ghi D-item. Kết quả: `.ai/results/WO-CANCEL-REQUEST-p1.md`. Phải trả lời:

1. **Mô hình lưu:** 5 cột trên `work_order` hay bảng riêng giữ lịch sử nhiều lần xin? Nêu giá từng phương án (migration, `IAudited`, `Snapshot`, quy ước `HasContractEnum` + CHECK).
2. **Máy trạng thái của sub-state:** mọi cạnh `none / pending / rejected`, gồm: xin lại sau `rejected`; những đường phải reset cờ (`/assignee` đổi người hoặc bỏ giao, `/cancel`,
   `PATCH` dời `scheduled_date` / `due_date`, kỹ sư `start` / `complete` khi đang `pending`, phiếu sang `done` / `verified`); xử lý đồng thời (Quản lý huỷ / giao lại cùng lúc kỹ sư xin).
3. **Phạm vi trạng thái:** chỉ `assigned`, hay cả `in_progress` (giao lại sẽ xoá `started_at`; ảnh `before` đã chụp thì sao)? Phiếu `survey` có áp dụng không?
4. **Bề mặt API:** hai endpoint FE đề xuất, thêm hoặc đổi field nào trên item / detail của work order, bộ lọc `GET /work-orders` cho banner Lịch điều phối, `allowed_actions` mới,
   mã lỗi (409 `INVALID_STATE_TRANSITION` hay mã mới), giới hạn độ dài lý do (so với CHECK 1000 / 200 của thông báo, `Notifier.Clip`), capability (có cần mới không).
5. **Thông báo + audit:** hai `NotificationType` mới, văn bản tiếng Việt qua `WorkOrderNotices`, trường hợp không có Quản lý nào phủ xã; giá trị `AuditAction` mới;
   hai migration CHECK (liệt kê `Up()` / `Down()` dự kiến, chưa viết).
6. **Offline (WP6):** có cần sync op mới cho "xin hủy" không; hậu quả nếu chỉ online (đối chiếu `BLOCKED_BY_EARLIER_OP` của sync).
7. **Tác động Contract:** mục nào thêm, spec OpenAPI 139 → bao nhiêu op, drift entry cần ghi, phần nào WP5 / WP6 phải xác nhận.
8. Kế hoạch test Phase 2 (tên test, trường hợp, phá thử) — chưa viết test.

## KHÔNG ĐƯỢC làm

- Phase 1: không sửa code `src/`, `tests/`, không migration, không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json` / `docs/contract-drift.md`.
- Không thêm giá trị vào `wo_status` hay bất kỳ enum đã publish nào ngoài `NotificationType` / `AuditAction` mà thiết kế bắt buộc phải thêm — và với hai enum này chỉ **đề xuất**, ghi D-item.
- Không đổi hành vi `/assignee`, `/cancel`, `/return` hiện có ngoài việc reset cờ đề xuất.
- Không tự chọn phương án kiến trúc — ghi thành D-item cho Mỹ; không coi đề xuất FE là đã duyệt.
- Không viết tên cột / constraint / tên endpoint từ trí nhớ; đọc code / snapshot thật rồi dán output.
- Không chạy `dotnet test` toàn suite, không chạm `luxmap_dev`, không kết nối DB nào.
- Không sửa `tracking.html`, `docs/tasks-backend.csv`, `.ai/tasks/current.md`.

## Phase 1 dừng ở đâu

Khảo sát xong thì **dừng hẳn**, ghi `.ai/results/WO-CANCEL-REQUEST-p1.md`, chờ Mỹ chốt D-item. Không viết code, kể cả khi đã rõ phải sửa gì.

---

## Phase 2 — hiện thực (26/10/2026)

Nguồn quyết định: **mục "Quyết định D-1…D-14" ở cuối `.ai/results/WO-CANCEL-REQUEST-p1.md`** (đã chốt). Phần "KHÔNG ĐƯỢC làm" bên trên về code / migration / Contract
**hết hiệu lực cho Phase 2**, nhưng mọi ràng buộc còn lại giữ nguyên. Branch: `feat/WO-CANCEL-REQUEST` (worktree riêng, đã tạo từ `origin/dev`).

### Làm

1. **Entity + migration (một migration duy nhất).** Năm cột trên `work_order` theo D-1 (`cancellation_request_status` mặc định `none` đi qua `HasContractEnum` + CHECK; note / at / reject_note / rejected_at nullable),
   nới `ck_notification_type` và `ck_audit_event_action` bằng **toàn bộ giá trị cũ + giá trị mới** (D-12). **Đọc `Up()` / `Down()` do EF sinh TRƯỚC khi apply**; bỏ mọi `DropIndex` / `DropColumn` /
   `AlterColumn` không có chủ đích; `IsRowVersion` sinh `AddColumn xmin` thì bỏ; `Down()` đối xứng và từ chối hẹp CHECK khi đã có dữ liệu mới (trap `migrations.md`). Bảng không mới nên không đụng `copy_dev_to_supabase.py` — kiểm lại.
2. **Hai endpoint** trong `WorkOrdersController` + `WorkOrderService`: `POST /work-orders/{id}/request-cancellation` (`ExecuteWorkOrders`, body `{reason}`) và `POST /work-orders/{id}/reject-cancellation`
   (`ManageWorkOrders`, body `{reject_reason}`). Body là `sealed class` + `init`. Lý do **trim, 1…500 ký tự, kiểm trong service** (D-7). Sai trạng thái → 409 `INVALID_STATE_TRANSITION`; người không được giao / ngoài xã → 404 (query filter có sẵn).
3. **Máy trạng thái D-2 / D-5 / D-13:** chỉ `assigned` được xin; `none|rejected → pending`, `pending` lặp → 409; reject chỉ khi `pending`. Reset đủ năm trường, **trong cùng audit event của hành động gốc**, ở: `Assign` đổi người và bỏ giao
   (không reset khi cùng người — no-op giữ), `cancel`, `start`, `complete`, `verify`, `PATCH` khi `scheduled_date` / `due_date` **thật sự đổi**. `PATCH` chỉ đổi title / vật tư thì giữ. Tạo phiếu / follow-up mới: `none`, không sao chép từ cha.
4. **Đọc (D-6, D-7):** năm trường vào item, detail, agenda, bundle; `GET /work-orders?cancellation_request_status=` (WireEnum.ParseCsv); `allowed_actions` thêm `request_cancellation` (kỹ sư được giao, `assigned`, chưa `pending`) và `reject_cancellation` (Quản lý, đang `pending`).
5. **Thông báo + audit (D-4, D-11, D-12):** `NotificationType.WorkOrderCancellationRequested` / `WorkOrderCancellationRejected` (wire `work_order_cancellation_requested` / `…_rejected`), văn bản tiếng Việt trong `WorkOrderNotices`, qua `Notifier.Stage` cùng `SaveChanges`,
   `Notifier.Clip`; yêu cầu → `Notifier.ManagersCoveringAsync(db, [wo.CommuneId])`, bác → kỹ sư được giao; không Quản lý nào phủ xã thì vẫn lưu, không fallback. `AuditAction.CancellationRequested` / `CancellationRejected`, đúng **một** `AuditEvent` mỗi `SaveChanges`, `Snapshot` có đủ năm trường.
6. **Không làm:** sync op mới / `ck_sync_operation_op_type` (D-10, online-only); `in_progress` (D-3); cooldown (D-5); realtime (D-8); điều kiện chống màn hình cũ (D-9).
7. **Contract / drift / spec / docs (D-14), cùng PR:** Contract **v1.19** `SELF-SIGNED` (mục 5.5, 5.9, 0, changelog — giữ giọng các bản trước), drift entry `WO-CANCEL-REQUEST` CR-1…CR-6 (kể cả giới hạn màn hình cũ D-9 và online-only D-10), xuất `luxmap-v1.json` → `gen_consolidated_spec.py` → lint
   (`.ai/context/commands.md`), spec 139 → 141 op; cập nhật trap `faults-workorders.md` nếu có bẫy mới; README / docs nếu đổi bề mặt. Không đụng `tracking.html` (Claude cập nhật sau).
8. **Test (khuôn ở mục "Kế hoạch Phase 2" của p1):** `LuxMapTestHost` (container PostGIS riêng lượt chạy; không `luxmap_dev`); không ghim ID bằng literal; tiền tố tài khoản riêng, teardown theo thứ tự FK; **`RoleCapabilityMatrixTests` / `CapabilityPolicyCoverageTests` đỏ nếu thiếu — kiểm lại**;
   mỗi chốt mới **phá thử**: phá → test đỏ → khôi phục bằng `cp` / `touch` (không `mv`) → xanh; đọc **mã thoát** của `dotnet test`.

### Cách chạy

- `dotnet build --disable-build-servers` (hoặc `MSBUILDDISABLENODEREUSE=1`) với `-warnaserror` như repo; `dotnet test --disable-build-servers`. **Chỉ một lượt `dotnet test` tại một thời điểm.** Docker đang chạy; nếu sandbox chặn Docker thì dừng và ghi rõ, đừng giả vờ test xanh.
- Ghi `.ai/results/WO-CANCEL-REQUEST-p2.md`: đã làm gì, output thật (migration `Up()` / `Down()`, `dotnet test` mã thoát và số pass / skip, mỗi lần phá thử), D-item phát sinh (nếu có thì **dừng ở chỗ đó và ghi**, đừng tự chọn).
- Commit theo Conventional Commits (`feat(WO-CANCEL-REQUEST): …`), một thay đổi logic một commit, stage theo đường dẫn tường minh (không `git add -A`), **không push**, không thêm trailer `Co-Authored-By`.
