# CITIZEN-QR — Phase 1: khảo sát (Claude, 12/10/2026)

Nền: `origin/dev` @ `2d424d8` (sau #131). Chỉ đọc `src/`, `tests/`, `docs/`. Đã chốt trước: **CQ-1** (mã ngẫu nhiên lưu
DB) và **CQ-10** (backend sinh mã, frontend vẽ ảnh QR). Phase 1 trả lời Q-1…Q-8 và biến CQ-2…CQ-9 thành D-item.

## Dữ kiện từ code (đọc thật)

| Chủ đề | Dữ kiện | Nguồn |
|---|---|---|
| Lược đồ đích | `erd.md` đã dự kiến `citizen_report {report_id, pole_id FK, status chờ/nhận/bác, fault_id FK}`, có `commune_id` + `ICommuneScoped`, `data_source`, FK `Restrict`, `IAudited` cho thao tác duyệt | `docs/database/erd.md:267,339,342,347,357` |
| Ảnh hiện có | `repair_evidence` thuộc module **WorkOrders**; cha là phiếu **hoặc** sự cố (`ck_repair_evidence_one_parent`); `uploaded_by` **bắt buộc** (người dùng), `lat`/`lng` **bắt buộc** | `RepairEvidence.cs:23-48`, `WorkOrderConfigurations.cs:101-107` |
| Kho ảnh | `StorageBucket` có `Survey`, `Evidence`, `Video`; cổng lưu **không có thao tác xoá** | `Shared/Storage/ObjectStorage.cs:12-18,129` |
| Đường tạo `field_report` | `FaultReportService`: `SourceChannel.FieldReport`, `ReportedBy = actor`, `data_source` lấy từ cột, `client_op_id`, audit + thông báo Quản lý trong cùng `SaveChanges`. Service **không** tự kiểm vai trò (controller kiểm `ReportFaults` = chỉ Kỹ sư hiện trường) | `FaultReportService.cs:79-160` |
| Endpoint ẩn danh | Đúng 8, đều phát / thu hồi token hoặc link mật khẩu. Thêm cái thứ 9 phải sửa danh sách literal | `AnonymousEndpointTests.cs:40-50` |
| Giới hạn tần suất | Chỉ một policy (`AccountMail`, 5 lần / 15 phút), chia theo `RemoteIpAddress` | `Api/Http/RateLimitSetup.cs` |
| Proxy | **Không có** `UseForwardedHeaders` / `ForwardedHeadersOptions` ở đâu trong `src/` ⇒ sau reverse proxy mọi client chung một IP | `grep` rỗng |
| AI | `YoloModel.DetectAsync(Stream jpeg)` → `YoloResult(Width, Height, Detections)`; có `SemaphoreSlim` giới hạn đồng thời (`Ai:MaxConcurrency`); `POST /ai/detect` chỉ `ReviewSurveys` (Quản lý), 16 MB | `YoloModel.cs:18,49,80,89`, `AiController.cs:27-41` |
| Giới hạn upload sẵn có | import 10 MB, ảnh phiếu 16 MB, AI 16 MB | 3 hằng `MaxUploadBytes` |
| Ma trận quyền | 19 capability; chưa có gì cho công dân / QR | `LuxMapPolicies.cs:94-115` |

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

## Sau review Codex (`.ai/reviews/CITIZEN-QR-p1-by-codex.md`, ~148k token) — đã kiểm lại từng P1

Đã xác minh bằng đọc file: request chưa đăng nhập ⇒ `CommuneScope.Empty` (`CommuneScopeAccessor.cs:29`), nên lookup qua filter không
thấy gì và guard từ chối mọi lượt ghi; `AuditActorKind` chỉ `User | Cv | Iot | System` (`AuditEvent.cs:25`), `User` bắt buộc tài khoản;
`ThumbnailFactory` `LoadAsync` toàn ảnh trước khi resize, không `Identify` / giới hạn pixel (AI thì có, `YoloModel.cs:95-116`); log request
ghi nguyên `RequestPath` (`SerilogSetup.cs:57`) ⇒ mã QR vào log. Phương án sửa theo đó:

1. **Phạm vi khi nhận (P1).** Tra `qr_code` **không lọc**, chỉ để biết mã còn hiệu lực + cột + xã; rồi mở context **đúng một xã đó**
   (khuôn `LightingScopeFactory`) cho mọi đọc / ghi tiếp theo. Không nới scope rỗng, không backdoor, không `*`, không lấy xã từ client.
2. **Actor audit khi nhận (P1).** Thêm `AuditActorKind.Public` — "người không có tài khoản, qua mã QR", **không** user, **không** role
   (CHECK cặp như `System`). Không giả Quản lý, không dùng backdoor để bỏ audit.
3. **Bom decode (P1).** Trước mọi decode: `IdentifyAsync` bằng cấu hình chỉ-JPEG, chặn số pixel (khuôn AI), giới hạn đồng thời; cả bước
   bỏ EXIF lẫn thumbnail đi qua cùng cổng đó.
4. **Mỗi lượt gửi là một hàng (P1, đổi D-6).** `citizen_submission` = từng lượt gửi (nội dung + ảnh + `client_op_id` gắn với **mã +
   băm nội dung**); `citizen_report` = **mục hàng chờ** gom các lượt cùng cột trong cửa sổ. Gửi lại đúng khoá + đúng nội dung → trả kết
   quả cũ; khoá trùng nhưng khác mã / nội dung → 409. Gộp không bao giờ làm mất nội dung hay ảnh của lượt nào; lượt có `kind` khác lượt
   đầu được đánh dấu "có thông tin mới" cho Quản lý.
5. **Chặn "chiếm quota" (P1).** Quá hạn mức theo mã thì vẫn **nhận nội dung chữ**, chỉ bỏ ảnh (cờ `over_quota`) — người giữ QR không
   chặn được dân thật báo cáo. Theo IP thì 429 như cũ.
6. **Ngân sách tài nguyên (P2).** Giới hạn body trước khi đọc; kiểm mã + hạn mức **trước** decode / ghi object; ngân sách byte ảnh
   công dân theo ngày cho cả hệ thống (vượt thì nhận chữ, bỏ ảnh). Hạn mức đếm trong DB (nguyên tử), không phải bộ nhớ một instance.
7. **GET và log (P2).** Giới hạn tần suất cả `GET /public/qr/{code}`; chuẩn hoá mã (độ dài, bảng chữ) trước khi chạm DB; mã sai và mã
   thu hồi cùng một nhánh, cùng 404. **Bỏ `has_open_report`.** Che mã trong log request (thay bằng `***`), không đưa mã vào audit.
8. **Duyệt và tạo sự cố (P2).** Một transaction ngoài; mỗi `SaveChanges` đúng một audit. `FaultReportService` đòi `client_op_id`,
   ghi chú ≥ 10 ký tự, chỉ `lamp_out | lamp_dim`, luôn `detected` ⇒ accept phải tự cấp khoá nội bộ, ghi chú mặc định, và **chỉ** cho
   chọn hai loại đó; loại "cột hỏng / khác" chỉ bác bỏ hoặc chuyển phiếu kiểm tra (D-17). Merge: đọc sự cố qua filter, khoá
   (`FaultLocks`), kiểm còn mở + cùng cột, rồi mới gắn.
9. **Quyền ảnh (P2).** Byte ảnh **chỉ Quản lý** (policy riêng); Cấp giám sát / Quản trị thấy metadata. Tra báo cáo qua filter rồi mới
   kiểm ảnh thuộc báo cáo đó.
10. **Lược đồ (P2).** FK ghép giữ báo cáo / mã / cột cùng xã (khuôn O-7); CHECK trạng thái ↔ `fault_id` / người quyết; `lat`/`lng`
    cùng có hoặc cùng vắng, hữu hạn; `ai_confidence` hữu hạn 0..1; `HasCommuneReference`; literal `kind`; giới hạn độ dài chữ. WP5 hiển
    thị nội dung của dân dạng chữ thuần (chống XSS lưu trữ).
11. **AI (P2, đổi D-9).** Giờ nhận **không** chứng minh ảnh chụp đêm. Nhãn chỉ là gợi ý, có giá trị "không kết luận" (không phát hiện
    gì / nhiều bóng / có thể là ảnh ngày); chạy lười khi Quản lý mở, có khoá chống chạy lặp; lưu kèm `artifact_version`.
12. **Thông báo, PLAN, dọn ảnh (P2).** Loại thông báo mới `citizen_report_received` (enum + CHECK + câu chữ ở module Citizen, Contract §5.9),
    stage trên **context đúng xã** khi tạo mục hàng chờ mới. Ba–bốn bảng vào `PLAN`. **Tự xoá ảnh sau 90 ngày chưa làm được**: cổng
    lưu không có delete, job nền là BE-26 ⇒ ghi nợ, không hứa.
13. **Test (P2).** Cả hai phép kiểm của `AnonymousEndpointTests` thêm đúng hai route; `[AllowAnonymous]` từng action. Kế hoạch test:
    không token, token kèm theo không mở rộng phạm vi, mã thu hồi / đua, gửi lại đổi mã / nội dung, spam gộp, bom pixel.

## D-item sau review (thay bảng trên ở các mục ghi "đổi")

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D-1 | Module | `LuxMap.Modules.Citizen` |
| D-2 | Ảnh | Bảng riêng + bucket `luxmap-citizen`; decode sau `Identify` + giới hạn pixel |
| D-3 | Mã QR | 16 ký tự base62 (CSPRNG), một mã hiệu lực / cột, mã cũ giữ lại cho lịch sử |
| D-4 *(đổi)* | Trang công khai trả gì | `pole_label`, `commune_name`; **bỏ** `has_open_report`; GET cũng giới hạn tần suất |
| D-5 | Sự cố từ báo cáo | `field_report`, `reported_by` = Quản lý duyệt, liên kết `fault_id`; một transaction ngoài |
| D-6 *(đổi)* | Gộp | `citizen_submission` mỗi lượt + `citizen_report` là mục hàng chờ; không mất nội dung lượt nào |
| D-7 | "Đêm" | `NightOf` chung (12:00 Asia/Ho_Chi_Minh) chỉ để **chia cửa sổ gộp** |
| D-8 *(đổi)* | Giới hạn | 10/giờ/IP (POST + GET), hạn mức theo mã (vượt → nhận chữ, bỏ ảnh), ngân sách byte/ngày, 3 ảnh × 8 MB; **forwarded headers trước khi bật** |
| D-9 *(đổi)* | AI | Gợi ý có "không kết luận"; chạy lười, chống chạy lặp |
| D-10 *(đổi)* | Riêng tư | Bỏ EXIF/XMP/IPTC ở ảnh lưu (ngoại lệ BE-11 cho ảnh công dân); byte ảnh chỉ Quản lý; tự xoá là **nợ** chờ BE-26 + thao tác delete |
| D-11 *(đổi)* | Capability | `ReadCitizenReports` (superior, manager, system_admin — metadata), `ReviewCitizenReports` (manager, gồm xem ảnh), `ManageQrCodes` (manager) |
| D-12 | Trang công khai | WP5 route `/r/:code`, chữ thuần, `Referrer-Policy: no-referrer` |
| D-13 | Phạm vi đợt đầu | Chỉ ảnh, không video, chưa CAPTCHA |
| D-14 *(mới)* | Phạm vi khi nhận | Tra mã không lọc → context đúng một xã (khuôn `LightingScopeFactory`) |
| D-15 *(mới)* | Actor audit khi nhận | `AuditActorKind.Public` (không user, không role) |
| D-16 *(mới)* | Mã trong log | Che mã trong log request; không ghi mã vào audit |
| D-17 *(mới)* | Loại báo cáo | `đèn tắt` → `lamp_out`, `đèn mờ / chập chờn` → `lamp_dim`; `cột hỏng / khác` chỉ bác bỏ hoặc mở phiếu kiểm tra |
| D-18 *(mới)* | Sự cố sau duyệt | Tạo `detected` (đi tiếp luồng duyệt sự cố thường) hay `confirmed` ngay (Quản lý đã xem) — đề xuất **`confirmed`**, cùng audit |
| D-19 *(mới)* | Chia Phase 2 | P2a mã QR + quản lý mã · P2b nhận công khai (sau forwarded headers) · P2c duyệt / tạo sự cố / AI |

Phase 1 dừng ở đây. Chờ Mỹ chốt D-item.
