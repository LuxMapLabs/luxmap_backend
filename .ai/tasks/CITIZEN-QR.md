---
ticket: CITIZEN-QR
title: Công dân báo sự cố qua QR trên cột — hàng chờ riêng, Quản lý duyệt (D-R1)
status: in_progress
phase: 2
owner: claude (P1, P2) · codex (review)
branch: docs/citizen-qr-p1 (P1) · feat/CITIZEN-QR (P2)
contract_refs:
  - "Phiếu v1.4 mục 3.2.c — actor Citizen"
  - "docs/contract-drift.md D-R1; mục 'CITIZEN-QR' (CQ-1 chốt, CQ-2…CQ-10 đề xuất)"
  - "§1 enum source_channel (KHÔNG thêm giá trị); §7 phân quyền theo commune_id"
depends_on: [CQ-1]
---

## Bối cảnh

Phiếu v1.4 có actor Citizen: báo sự cố chiếu sáng qua QR dán trên mỗi cột đã đăng ký. D-R1: công dân không có tài
khoản, báo cáo vào hàng chờ riêng, Quản lý duyệt rồi mới thành `fault`. Mỹ đã chốt **CQ-1 — cách A**: QR chứa mã ngẫu
nhiên lưu trong DB (`qr_code: code → pole_id, active`), không chứa toạ độ, không chứa `pole_id` trần. Các mục CQ-2…CQ-10
là đề xuất — Phase 1 phải trả lời chúng thành D-item. Consumer: WP5 (trang công khai cho dân + màn duyệt cho Quản lý).

## Phải đọc trước

- `docs/contract-drift.md`: D-R1, D-R13 (audit), D-R14/D-R28 (video), D-R16 (lưu trữ), mục **CITIZEN-QR**
- `CLAUDE.md`: "Vai trò" (Citizen), BE-11 (ảnh: proxy, JPEG-only, thumbnail không GPS), BE-33a (rate limiter theo IP, forwarded
  headers), BE-41 (`POST /faults` — đường tạo `field_report`, `client_op_id`), BE-19 (audit + khoá hàng fault), AI-1 (model ON/OFF),
  BE-REVIEW-02 ràng buộc 6 (hai mã 403)
- `docs/authorization-guide.md` — trước khi mở endpoint ẩn danh
- Code: `AnonymousEndpointTests`, `RateLimitSetup`, `FaultReportService`, `RepairEvidence` / `IObjectStore`, `YoloOnOffDetector`

## Câu hỏi Phase 1

- Q-1. Lược đồ: `qr_code`, `citizen_report`, ảnh của báo cáo (bảng `repair_evidence` mở thêm một cha, hay bảng riêng?);
  `commune_id` lấy từ cột qua mã QR; FK `Restrict`; có cần `ICommuneScoped` (báo cáo là gốc truy vấn của màn duyệt → có).
- Q-2. Endpoint ẩn danh: hình dạng `GET /public/qr/{code}` (trả gì cho trang công khai — tối thiểu, không lộ dữ liệu nội bộ) và
  `POST /public/reports` (multipart). Mã sai / đã thu hồi → trả gì (404 chung, không phân biệt — tránh dò mã).
- Q-3. Chống lạm dụng: giới hạn theo IP + theo mã (CQ-7), giới hạn dung lượng / số ảnh, gộp trùng (CQ-6), cờ GPS (CQ-8).
- Q-4. Luồng duyệt của Quản lý: danh sách hàng chờ (phân trang, lọc theo xã/trạng thái), duyệt → tạo fault mới hoặc gộp vào
  fault đang mở (CQ-2, CQ-3), bác bỏ; audit; khoá hàng như `FaultLocks`.
- Q-5. AI (CQ-5): chạy đồng bộ lúc nhận hay nền; ảnh ngày/đêm phân biệt bằng gì (EXIF giờ chụp? độ sáng?); lưu nhãn ở đâu.
- Q-6. Phát hành QR (CQ-10): **đã chốt backend sinh mã, frontend vẽ ảnh QR**. Còn trả lời: endpoint tạo / thu hồi, ai được làm
  (capability), in hàng loạt cả xã (PDF do backend sinh hay WP5 tự ghép).
- Q-7. Riêng tư (CQ-9): thời hạn giữ ảnh; ai xem ảnh gốc.
- Q-8. Capability mới (vd. `ReviewCitizenReports`, `ManageQrCodes`) và ma trận vai trò — thêm literal vào hai test ma trận.

## KHÔNG ĐƯỢC làm

- Phase 1 không sửa code, không tạo migration, không sửa Contract / OpenAPI.
- **Không** để báo cáo của dân đi thẳng vào `fault`; **không** tạo tài khoản / vai trò cho công dân; **không** thêm giá trị vào
  `source_channel` (D-R1).
- **Không** đưa toạ độ hoặc `pole_id` trần vào QR (CQ-1 đã chốt); **không** để client sinh mã (CQ-10).
- **Không** nhận video ở đợt đầu (CQ-4) trừ khi Mỹ đổi quyết định.
- **Không** để AI tự tạo hoặc tự bác bỏ báo cáo (CQ-5).
- **Không** presigned URL cho ảnh (BE-11 quy tắc 1); **không** để EXIF/GPS trong thumbnail.
- **Không** mở endpoint ẩn danh mà không cập nhật `AnonymousEndpointTests` kèm lý do.
- Không tự chọn phương án kiến trúc — ghi D-item. Deviation về `docs/contract-drift.md`, không vào `.ai/`.

## Phase 1 dừng ở đâu

`.ai/results/CITIZEN-QR-p1.md`: lược đồ đề xuất, hình dạng endpoint, chống lạm dụng, luồng duyệt, D-item (trả lời CQ-2…CQ-10).
Codex review (chỉ đọc) → `.ai/reviews/CITIZEN-QR-p1-by-codex.md`. **Dừng**, chờ Mỹ chốt.

## Phase 2c — hàng chờ duyệt (kế hoạch, Claude 13/10/2026)

Nền: D-5, D-9, D-11, D-17, D-18 (Mỹ chốt 12/10). Lược đồ `citizen_report` đã có cột quyết định + CHECK từ P2b.

**Capability (literal trong hai test ma trận):** `ReadCitizenReports` = superior, manager, system_admin (metadata) ·
`ReviewCitizenReports` = manager (quyết định + byte ảnh).

**Endpoint** (module Citizen, `ClientSurface.Web`):

```text
GET  /citizen-reports?status=&commune_id=&pole_id=&page=&page_size=   Read · status CSV, vắng = pending · mới nhất trước:
                                                                       last_submitted_at DESC, length(report_id) DESC, report_id DESC
GET  /citizen-reports/{id}                                             Read · chi tiết + bối cảnh cột + lượt gửi + allowed_actions
GET  /citizen-reports/{id}/photos/{photo_id}/thumbnail|original        Review · proxy JPEG, ảnh phải thuộc báo cáo (tra qua filter)
POST /citizen-reports/{id}/accept   {fault_type, severity?, note?}     Review → sự cố mới, 201
POST /citizen-reports/{id}/merge    {fault_id, note?}                  Review → gắn vào sự cố đang mở của CÙNG cột
POST /citizen-reports/{id}/reject   {reason}                           Review
```

- **Ảnh chỉ được LIỆT KÊ cho người mở được** (luật BE-20): người không có `ReviewCitizenReports` thấy `photo_count`, `photos = []`.
- **Không trả toạ độ người báo** cho ai; chỉ cờ `far_from_pole` (tối thiểu dữ liệu cá nhân, D-10).
- **Bối cảnh cột** trong chi tiết: `pole {pole_id, segment_id, location, fixture_status (unknown nếu chưa có), last_seen_at}`,
  `open_faults[]` (định nghĩa `FaultStatusSets.Open`) — ứng viên để gộp.
- **accept:** `fault_type` ∈ {lamp_out, lamp_dim}; báo cáo phải có ít nhất một lượt `lamp_out` / `lamp_dim` (D-17: cột hỏng / khác chỉ
  bác bỏ) → 409 `CITIZEN_REPORT_NOT_ACCEPTABLE`; cột đã có sự cố MỞ cùng loại hiệu lực → 409 `OPEN_FAULT_EXISTS` kèm `fault_ids` (phải gộp,
  không nhân đôi sự cố). Sự cố: `field_report`, `data_source` của cột, `detected_at` = lượt gửi đầu, `reported_by` = Quản lý,
  **`confirmed`** ngay (D-18, qua `FaultTransitions.Apply` nên `confirmed_by/at` đúng luật), `note` = tóm tắt do server viết, ghi chú của
  Quản lý vào `review_note`. Dựng sự cố ở **Faults** (`FaultReportService.StageReviewedReportAsync`), Citizen không tự `new Fault`.
- **merge:** sự cố đọc qua filter, `FaultLocks`, phải mở và cùng `pole_id` → nếu không 409 `FAULT_NOT_MERGEABLE`. Không sửa sự cố.
- **reject:** `reason` bắt buộc, 1..1000 ký tự.
- **Khoá:** đọc báo cáo qua filter (404) → transaction → `FOR UPDATE` hàng `pole` (cùng khoá với lượt gửi công khai — một lượt gửi đang
  chạy không thể gắn vào mục vừa được quyết) → đọc lại → còn `pending` (không thì 409 `CITIZEN_REPORT_ALREADY_DECIDED`) → (merge) khoá
  sự cố. Thứ tự khoá: pole → fault.
- **Audit:** mỗi `SaveChanges` đúng một event. accept = hai lượt lưu trong một transaction (sự cố `created`, báo cáo `accepted`);
  merge / reject = một lượt (`merged` / `rejected`). Thêm `AuditAction.Accepted`, `Merged` (CHECK sinh lại).
- **AI (D-9):** gợi ý theo TỪNG ảnh, chạy lười khi người có `ReviewCitizenReports` mở chi tiết và ảnh chưa có gợi ý; `YoloModel` + phiên
  bản `YoloOnOffDetector.Artifact`. Nhãn `lamp_on` (≥ 1 phát hiện, toàn `normal`), `lamp_off` (toàn `out`), `inconclusive` (không có hoặc
  lẫn). Lưu `ai_hint`, `ai_confidence` (hữu hạn 0..1, null khi `inconclusive`), `ai_artifact_version`, `ai_checked_at` trên `citizen_photo`
  (cùng có / cùng vắng). Lỗi model → không có gợi ý, GET vẫn 200. Không bao giờ tự quyết (CQ-5).
- **Migration** `AddCitizenReview`: 4 cột AI + CHECK; CHECK `action` của audit.
- **Không làm ở P2c:** mở phiếu kiểm tra từ báo cáo "cột hỏng" (D-17 vế sau — Quản lý dùng luồng phiếu thường), tự xoá ảnh (BE-26),
  thông báo khi quyết định.
