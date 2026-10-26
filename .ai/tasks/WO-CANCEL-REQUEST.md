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
