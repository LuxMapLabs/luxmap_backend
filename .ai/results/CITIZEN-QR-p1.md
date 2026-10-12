# CITIZEN-QR — Phase 1: khảo sát (Claude, 12/10/2026)

Nền: `origin/dev` @ `2d424d8` (sau #131). Chỉ đọc `src/`, `tests/`, `docs/`. Đã chốt trước: **CQ-1** (mã ngẫu nhiên lưu
DB) và **CQ-10** (backend sinh mã, frontend vẽ ảnh QR). Phase 1 trả lời Q-1…Q-8 và biến CQ-2…CQ-9 thành D-item.

## Dữ kiện từ code (đọc thật)

| Chủ đề | Dữ kiện | Nguồn |
|---|---|---|
| Lược đồ đích | `erd.md` đã dự kiến `citizen_report {report_id, pole_id FK, status chờ/nhận/bác, fault_id FK}`, có `commune_id` + `ICommuneScoped`, `data_source`, FK `Restrict`, `IAudited` cho thao tác duyệt | `docs/database/erd.md:267,339,342,347,357` |
| Ảnh hiện có | `repair_evidence` thuộc module **WorkOrders**; cha là phiếu **hoặc** sự cố (`ck_repair_evidence_one_parent`); `uploaded_by` **bắt buộc** (người dùng), `lat`/`lng` **bắt buộc** | `RepairEvidence.cs:23-48`, `WorkOrderConfigurations.cs:101-107` |
| Kho ảnh | `StorageBucket` chỉ có `Survey`, `Evidence` | `Shared/Storage/ObjectStorage.cs:12-18` |
| Đường tạo `field_report` | `FaultReportService`: `SourceChannel.FieldReport`, `ReportedBy = actor`, `data_source` lấy từ cột, `client_op_id`, audit + thông báo Quản lý trong cùng `SaveChanges`. Service **không** tự kiểm vai trò (controller kiểm `ReportFaults` = chỉ Kỹ sư hiện trường) | `FaultReportService.cs:79-160` |
| Endpoint ẩn danh | Đúng 8, đều phát / thu hồi token hoặc link mật khẩu. Thêm cái thứ 9 phải sửa danh sách literal | `AnonymousEndpointTests.cs:40-50` |
| Giới hạn tần suất | Chỉ một policy (`AccountMail`, 5 lần / 15 phút), chia theo `RemoteIpAddress` | `Api/Http/RateLimitSetup.cs` |
| Proxy | **Không có** `UseForwardedHeaders` / `ForwardedHeadersOptions` ở đâu trong `src/` ⇒ sau reverse proxy mọi client chung một IP | `grep` rỗng |
| AI | `YoloModel.DetectAsync(Stream jpeg)` → `YoloResult(Width, Height, Detections)`; có `SemaphoreSlim` giới hạn đồng thời (`Ai:MaxConcurrency`); `POST /ai/detect` chỉ `ReviewSurveys` (Quản lý), 16 MB | `YoloModel.cs:18,49,80,89`, `AiController.cs:27-41` |
| Giới hạn upload sẵn có | import 10 MB, ảnh phiếu 16 MB, AI 16 MB | 3 hằng `MaxUploadBytes` |
| Ma trận quyền | 20 capability; chưa có gì cho công dân / QR | `LuxMapPolicies.cs:97-115` |

## Trả lời Q-1…Q-8 (đề xuất)

**Q-1 — Lược đồ.** Module mới **`LuxMap.Modules.Citizen`** (tham chiếu Assets, Faults, Notifications, Storage; **không** tham
chiếu WorkOrders). Ba bảng:

```text
qr_code             code PK (text, 16 ký tự base62 ~ 95 bit) · pole_id FK · commune_id · active · created_by/at · revoked_by/at
                    unique partial (pole_id) WHERE active          ⇒ tối đa một mã hiệu lực / cột (CQ-10)
citizen_report      report_id (CRP-0001) · qr_code FK · pole_id FK · commune_id · data_source (= của cột)
                    kind (đèn tắt | đèn chập chờn | cột hỏng | khác) · description? · reporter_lat/lng? · far_from_pole
                    night_of · status (pending | accepted | merged | rejected) · fault_id FK? · decided_by/at · decision_note
                    duplicate_count · client_op_id · ai_label? · ai_confidence? · ai_artifact_version?
citizen_report_photo photo_id · report_id FK · object_key · thumbnail_key · byte_count · thumbnail_bytes · received_at
```

- **Không dùng lại `repair_evidence`**: `uploaded_by` và `lat/lng` bắt buộc, công dân không có tài khoản và GPS là tuỳ chọn; bảng nằm ở
  WorkOrders. Nới các cột đó là làm yếu ràng buộc của BE-24.
- Bucket mới `StorageBucket.Citizen` (`luxmap-citizen`) — tách khỏi bằng chứng của kỹ sư để chính sách giữ / xoá khác nhau (Q-7).
- Mọi FK `Restrict`; `citizen_report` và `qr_code` là `ICommuneScoped` (gốc truy vấn của màn duyệt / quản lý QR); ảnh đi qua báo cáo.
- `citizen_report`, `qr_code` là `IAudited` (duyệt, tạo / thu hồi mã) ⇒ thêm giá trị `AuditEntityType` + sinh lại CHECK.
- Ba bảng phải vào `PLAN` của `copy_dev_to_supabase.py` cùng PR.

**Q-2 — Endpoint ẩn danh (2 cái, thêm vào `AnonymousEndpointTests` kèm lý do).**

```text
GET  /api/v1/public/qr/{code}     → {pole_label, commune_name, has_open_report}  · mã sai / đã thu hồi → 404 PUBLIC_QR_NOT_FOUND
POST /api/v1/public/reports       multipart: code, kind, description?, lat?, lng?, client_op_id, photos[1..3] (JPEG)
                                  → 202 {report_ref}  (không trả report_id nội bộ, không trả trạng thái chi tiết)
```

- Không trả `pole_id`, toạ độ, trạng thái đèn hay sự cố nội bộ cho người lạ; `pole_label` là nhãn hiển thị (ví dụ "Cột 47 — Đường
  Long Phước") để dân biết mình quét đúng cột.
- 404 đồng nhất cho mã không tồn tại / đã thu hồi — không cho dò mã nào từng có.
- `client_op_id` gửi lại → 202 cùng `report_ref`, không tạo thêm (khuôn BE-41).

**Q-3 — Chống lạm dụng.** Hai policy giới hạn tần suất mới: theo IP (đề xuất 10 báo cáo / giờ) và theo mã QR (đề xuất 20 báo cáo /
đêm). 🔴 **Chặn triển khai:** chưa có forwarded headers ⇒ sau proxy cả xã chung một xô IP; phải cấu hình `ForwardedHeadersOptions`
với proxy tin cậy **trước** khi bật endpoint công khai. Ảnh: tối đa 3, mỗi ảnh ≤ 8 MB, JPEG magic bytes, decode qua
`ThumbnailFactory.JpegOnly`. Gộp trùng: cùng cột + cùng `night_of` + báo cáo `pending` đã có ⇒ không tạo mục mới, tăng
`duplicate_count`, giữ ảnh (tối đa N ảnh / mục). GPS cách cột > 200 m ⇒ `far_from_pole = true` (chỉ cờ).

**Q-4 — Luồng duyệt (capability mới, Quản lý).**

```text
GET  /api/v1/citizen-reports                 phân trang, lọc status / commune_id / pole_id; mặc định pending, mới nhất trước
GET  /api/v1/citizen-reports/{id}            kèm ảnh (proxy), nhãn AI, trạng thái khảo sát gần nhất + sự cố mở của cột
GET  /api/v1/citizen-reports/{id}/photos/{photo_id}/thumbnail|original
POST /api/v1/citizen-reports/{id}/accept     {fault_type, severity?, note?} → gọi FaultReportService (field_report, reported_by =
                                             Quản lý duyệt, data_source của cột) + liên kết fault_id; status accepted
POST /api/v1/citizen-reports/{id}/merge      {fault_id} → gắn vào sự cố đang MỞ của cùng cột; status merged; không sửa fault
POST /api/v1/citizen-reports/{id}/reject     {reason} → status rejected
```

Khoá hàng `citizen_report` trước khi quyết (khuôn `FaultLocks`); quyết định một lần, lần hai → 409. Audit trong cùng `SaveChanges`.
Thông báo Quản lý của xã **khi có mục hàng chờ mới** (không báo cho báo cáo trùng) qua `Notifier.ManagersCoveringAsync`.

**Q-5 — AI.** Không chạy đồng bộ trên endpoint công khai (người lạ sẽ chiếm hết `SemaphoreSlim` của model). Đề xuất **chạy lười khi
Quản lý mở chi tiết lần đầu**, lưu nhãn + `artifact_version` vào báo cáo. Ngày / đêm: không tin EXIF (client sửa được); dùng **giờ
server nhận** so với cửa sổ đêm theo `Asia/Ho_Chi_Minh`; nhãn AI chỉ hiện khi báo cáo nhận trong đêm, kèm chữ "gợi ý".

**Q-6 — QR.** `POST /api/v1/qr-codes {pole_id}` (thu hồi mã cũ nếu có, sinh mã mới), `POST /api/v1/qr-codes/{code}/revoke`,
`GET /api/v1/qr-codes?commune_id=&pole_id=&active=` → `{code, url, pole_id, active, …}`. WP5 vẽ ảnh từ `url`. In hàng loạt: WP5
ghép trang in từ danh sách (không thêm thư viện PDF vào backend).

**Q-7 — Riêng tư.** Ảnh gốc của dân **bỏ EXIF/GPS ngay khi nhận** (khác BE-11 quy tắc 4: ảnh khảo sát giữ nguyên byte vì metadata
phơi sáng là dữ liệu đo; ảnh của dân thì metadata chỉ là rủi ro). Chỉ Quản lý xem ảnh (proxy). Báo cáo `rejected` xoá ảnh sau 90 ngày —
cần job nền (BE-26), tạm ghi nợ. Không thu thông tin liên lạc.

**Q-8 — Capability mới** (literal trong `RoleCapabilityMatrixTests` + `CapabilityMatrixTests`):
`ReadCitizenReports` = superior, manager, system_admin · `ReviewCitizenReports` = manager · `ManageQrCodes` = manager.

## D-item

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D-1 | Module | **`LuxMap.Modules.Citizen`** mới; không đặt vào Faults / WorkOrders |
| D-2 | Ảnh | **Bảng riêng `citizen_report_photo`** + bucket `luxmap-citizen`; không nới `repair_evidence` |
| D-3 | Mã QR | 16 ký tự base62 từ `RandomNumberGenerator`; `active` + unique partial một mã hiệu lực / cột |
| D-4 | Trang công khai trả gì | Chỉ `pole_label`, `commune_name`, `has_open_report`; không `pole_id` / toạ độ / trạng thái |
| D-5 | Sự cố từ báo cáo | `field_report`, `reported_by` = **Quản lý duyệt**, `data_source` = của cột, liên kết `citizen_report.fault_id` (CQ-3) |
| D-6 | Gộp | Hai lớp: tự gộp **báo cáo trùng** (cùng cột + cùng đêm) khi nhận; Quản lý **merge** vào sự cố mở khi duyệt |
| D-7 | "Đêm" | Cùng `NightOf` của agenda (12:00 Asia/Ho_Chi_Minh) — cần đưa khái niệm này ra khỏi WorkOrders (hoặc đọc chung khoá cấu hình như W-2) |
| D-8 | Giới hạn | 10 / giờ / IP, 20 / đêm / mã, 3 ảnh × 8 MB; **bật endpoint chỉ sau khi cấu hình forwarded headers** |
| D-9 | AI | Chạy lười khi Quản lý mở; không dùng EXIF để biết ngày/đêm; nhãn chỉ là gợi ý (CQ-5) |
| D-10 | Riêng tư | Bỏ EXIF ở ảnh gốc khi nhận; chỉ Quản lý xem; xoá ảnh báo cáo bị bác sau 90 ngày (job BE-26) |
| D-11 | Capability | `ReadCitizenReports`, `ReviewCitizenReports`, `ManageQrCodes` như trên |
| D-12 | Trang công khai ở đâu | WP5 thêm route công khai `/r/:code` trong SPA (không đăng nhập); backend chỉ cấp API — cần báo WP5 |
| D-13 | Phạm vi đợt đầu | Chỉ ảnh; **không video** (CQ-4); không CAPTCHA (xét lại nếu thấy spam thật) |

Phase 1 dừng ở đây. Chờ Codex review và Mỹ chốt D-item.
