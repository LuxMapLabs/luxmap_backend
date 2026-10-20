# Công dân báo qua QR

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi mã QR, endpoint `/public/…`, hàng chờ duyệt báo cáo công dân. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## CITIZEN-QR P2a — mã QR trên cột (12/10/2026, drift QR-1…QR-6)

- 🔴 **`code` là chìa khoá gửi báo cáo, không phải định danh.** Mọi thao tác của Quản lý đi theo **`qr_id`** (`QR-0001`); `code`
  không bao giờ nằm trong đường dẫn (log request ghi nguyên `RequestPath`), trong audit hay trong log (D-16). Audit `qr_code` chỉ ghi
  `qr_id`, `pole_id`, `replaces` / `replaced_by`. Canh bằng `Issuing_again_revokes_the_code_in_use_and_the_audit_never_holds_a_code`.
- **Một mã đang dùng mỗi cột**: `IssueAsync` đọc cột qua filter TRƯỚC (ngoài phạm vi là 404, Quản lý xã khác không chờ khoá), rồi
  `FOR UPDATE` hàng `pole`, rồi mới tìm mã đang dùng; `ux_qr_code_pole_id_active` là lớp chặn cuối. Phá thử bỏ khoá →
  `Simultaneous_presses_leave_exactly_one_code_in_use` đỏ 3/3. Thu hồi + cấp mới là **hai** `SaveChanges` trong một giao dịch (mỗi lần
  đúng một audit, BE-23a).
- **Hai `HasIndex` trên CÙNG cột phải có tên model** (`HasIndex(x => x.PoleId, "tên")`) — không thì EF coi lần gọi thứ hai là lần đầu
  và **đè** nó. Ở đây cần cả unique partial `ux_qr_code_pole_id_active` lẫn `ix_qr_code_pole_id` đầy đủ (partial không phục vụ hàng
  đã thu hồi — quy tắc partial index).
- **Thu hồi khoá CÙNG hàng `pole`, cùng thứ tự (pole → mã), như cấp mới** (review Codex P2a). Chỉ khoá hàng `qr_code` thì một lượt
  cấp mới đọc mã là "đang dùng" trước khi thu hồi commit, rồi ghi đè người / thời điểm thu hồi kèm audit `revoked` thứ hai (bảng
  không có row version). Canh bằng `Issue_and_revoke_both_wait_for_the_pole_lock` (giữ khoá `FOR NO KEY UPDATE`; phá thử → đỏ).
- `commune_id` của mã **chép từ cột** ở server, như `fixture`; client không gửi được.
- Teardown xoá `qr_code` **trước** `pole` và tài khoản (FK `Restrict` tới cả hai). Rollback `AddQrCodes` gãy khi `audit_event` đã có
  `entity_type = 'qr_code'` hoặc `action = 'revoked'` (cùng họ BE-19).

## CITIZEN-QR P2b — form công khai, không token (13/10/2026, drift PB-1…PB-8)

- 🔴 **Endpoint công khai KHÔNG dùng `LuxMapDbContext` của request.** Request ẩn danh có phạm vi xã **rỗng**: thấy không hàng nào,
  ghi không được gì. `CitizenScopeFactory.Unscoped()` chỉ cho hai việc — mã → cột / xã, và tra gửi lại theo `client_op_id`; mọi đọc / ghi
  còn lại qua `CitizenScopeFactory.Open(<xã của cột>)`, nơi query filter, `CommuneWriteGuard` và guard audit chạy bình thường. Không
  backdoor, không `*`, không lấy xã từ client hay từ token tình cờ gửi kèm.
- **`[AllowAnonymous]` trên TỪNG action của `PublicController`**, không cấp class. Ba nơi nêu đích danh hai route và phải sửa cùng diff
  khi đổi: `AnonymousEndpointTests` (cả hai phép kiểm), `OpenApiSpecTests` (ngoại lệ `/api/v1/public/`), generator spec (`is_anonymous`).
- **Mã đi trong BODY, không bao giờ trong đường dẫn** — log request ghi `RequestPath` (D-16). Endpoint mới của phía công khai giữ luật này.
- **Ảnh công dân qua `IImageSanitizer` TRƯỚC `StoreImageAsync`** (ngoại lệ BE-11 quy tắc 4, chỉ cho ảnh công dân): đọc có giới hạn byte →
  magic bytes → `IdentifyAsync` + giới hạn pixel (chỉ đọc header) → decode trong cổng `SemaphoreSlim` → `AutoOrient` → bỏ EXIF/XMP/IPTC/ICC →
  mã hoá lại. Decode luôn qua `ThumbnailFactory.JpegOnly` (tiền đề tắt advisory ImageSharp). Phá thử: bỏ giới hạn pixel → test bom pixel đỏ;
  giữ EXIF → test GPS đỏ.
- **Thứ tự của `SubmitAsync` là thiết kế, đừng đảo** (review P2b #2, #4–#7): kiểm → tra mã → băm nội dung (ảnh đọc dạng luồng) → gửi lại
  sớm → hạn mức sớm (không khoá) → **làm sạch MỌI ảnh** → khoá `pole` → **đọc lại mã còn hiệu lực** (thu hồi giữa chừng thắng) → **gửi lại
  lần nữa** → **hạn mức lần nữa, cộng byte thật của ảnh sắp ghi** → ghi object → ghi dòng. Object ghi **dưới khoá** để lượt thua hạn mức hay
  thua đua gửi lại không để lại ảnh mồ côi; sau làm sạch mỗi ảnh vài trăm KB nên khoá chỉ giữ vài giây (khác clip BE-15). Quá hạn mức thì
  **vẫn nhận chữ**, bỏ ảnh. `last_submitted_at` lấy **max** (lượt chậm đến sau lượt nhanh không được ghi lùi — CHECK sẽ ra 500). Ngân sách byte
  toàn hệ thống **không** tuần tự hoá giữa các xã: hai lượt cùng lúc ở hai xã có thể vượt bằng ảnh của chính chúng — đừng mô tả là chặt.
- **Cổng làm sạch lấy chỗ TRƯỚC khi đọc ảnh vào RAM**, hàng chờ có hạn (quá → 503 `SERVER_BUSY`), ảnh sạch thu nhỏ về `PhotoMaxLongEdge`
  (2560) — nên bước tạo thumbnail (decode lần hai, ngoài cổng) chỉ gặp ảnh nhỏ. `MaxPhotos` tối đa **3** cũng vì CHECK của `citizen_photo`.
- **Khoá ngoại ghép giữ một lượt gửi cùng cột + xã với báo cáo và mã của nó** (`ak_citizen_report_…`, `ak_qr_code_qr_id_pole_id_commune_id` —
  trông thừa, là đích FK). `report → pole` vẫn là FK đơn (pole không có khoá `(pole_id, commune_id)`; server chép xã từ mã).
- ⚠️ **Postgres cắt tên định danh ở 63 ký tự, im lặng.** Tên FK ghép EF sinh ra có thể dài hơn (`fk_citizen_submission_citizen_report_report_id_pole_id_commune_id`
  = 66) và trên DB thành tên khác — code nhận diện lỗi theo `ConstraintName` hay test so tên đầy đủ sẽ trượt. Đặt `HasConstraintName` ngắn
  nếu cần bắt theo tên.
- 🔴 **Forwarded headers: danh sách RỖNG = TIN MỌI PEER, không phải "không tin ai".** ASP.NET Core bỏ hẳn bước kiểm IP khi cả
  `KnownProxies` lẫn `KnownIPNetworks` trống (bản đầu của P2b xoá mặc định loopback rồi để trống — Codex review bắt, P1). Nên **không
  cấu hình proxy ⇒ middleware KHÔNG nằm trong pipeline** (`TrustedProxies.Any`); có cấu hình ⇒ đúng các proxy đó, loopback mặc định bị
  xoá, middleware đứng **đầu** pipeline. Triển khai sau reverse proxy phải khai proxy, nếu không cả xã chung một xô giới hạn. Canh bằng
  `With_no_proxy_configured_a_forwarded_address_is_ignored_from_every_peer` và `Forwarded_addresses_count_only_from_a_trusted_proxy`
  (giả địa chỉ peer bằng `IStartupFilter`).
- Gửi lại: khoá `client_op_id` gắn với **mã + băm nội dung** (kể cả ảnh), tra ở cả bước sớm lẫn khi bắt trùng `ux_citizen_submission_client_op_id`.
- Teardown: `citizen_photo` (theo submission) → `citizen_submission` → `citizen_report` → `qr_code` → `pole`. Rollback `AddCitizenReports` gãy
  khi `audit_event` đã có actor `public` hoặc entity `citizen_report` (cùng họ BE-19).

## CITIZEN-QR P2c — hàng chờ duyệt (13/10/2026, drift PC-1…PC-10)

- 🔴 **Mọi quyết định (accept / merge / reject) khoá hàng `pole` TRƯỚC, rồi `citizen_report`, rồi (merge) `fault`** — cùng khoá pole mà
  lượt gửi công khai giữ khi nộp vào báo cáo. Bỏ khoá pole thì một lượt gửi đang chạy có thể gắn vào báo cáo vừa được quyết và không bao
  giờ tới tay Quản lý. Đọc lần đầu là **projection không tracking**, sau khoá mới nạp entity — đọc tracking trước khoá thì lần đọc sau trả
  lại chính entity cũ. Canh bằng `A_decision_waits_for_the_pole_lock_a_public_send_holds` (giữ khoá `FOR NO KEY UPDATE`). **Đừng** thêm
  khoá pole vào `PATCH /faults` hay tạo phiếu để "chặn trùng cho chặt": các đường đó khoá fault trước, sẽ thành vòng fault → pole.
- **`submission_count` là phiên bản của báo cáo** vì lượt gửi **chỉ được thêm**. Tính năng nào xoá / gộp lượt gửi sau này phải thay bằng
  dấu phiên bản thật, nếu không 409 `CITIZEN_REPORT_CHANGED` sẽ để lọt quyết định trên nội dung chưa đọc.
- **Citizen không bao giờ `new Fault`.** Chấp nhận đi qua `FaultReportService.StageReviewedReportAsync` (Faults): đọc cột qua filter, lấy
  xã / tuyến / `data_source` của cột, xác nhận qua `FaultTransitions.Apply`, stage đúng một audit `created` — **không** save. Người gọi
  save trong transaction của mình rồi mới đổi báo cáo (save thứ hai, audit `accepted`).
- 🔴 **`CitizenPhoto` là `IAudited` từ P2c** (gợi ý AI là phát hiện tự động, D-R13). Mọi đường ghi ảnh phải có đúng một audit trong cùng
  `SaveChanges`: lượt gửi công khai (`submitted`, actor `public`) và gợi ý (`analyzed`, actor `cv`, entity `citizen_report`).
- **Không inject `YoloModel` vào service trên đường request.** Constructor của nó ném khi thiếu file model ⇒ DI làm cả màn chi tiết 500.
  `YoloCitizenPhotoHinter` resolve lười trong vùng bắt lỗi; gợi ý là tuỳ chọn, hàng chờ thì không. Model chạy **ngoài** transaction và
  khoá; chỉ lượt ghi khoá hàng ảnh và chỉ ghi ảnh **còn `ai_hint` null** — hai lượt mở cùng lúc có thể cùng chạy model nhưng chỉ một
  ghi, một event (`The_hint_is_an_audited_automated_finding_written_once`, phá thử đỏ 3/3).
- `artifact_version` của gợi ý = phiên bản detector **+** băm luật gợi ý (một bóng, ngưỡng ảnh ban ngày). Đổi `Citizen:DaylightMeanLuma`
  là đổi phiên bản — đừng tách luật ra khỏi chuỗi đó.
- Test model thật **không** khẳng định model thấy gì: nó gọi khung đen có một điểm sáng là một bóng `out`. Chỉ canh hợp đồng (nhãn hợp lệ,
  confidence đúng khi kết luận, dạng phiên bản) — cùng quy ước AI-1.
- Teardown: `citizen_photo` → `citizen_submission` → `citizen_report` **trước `fault`** — báo cáo đã chấp nhận / gộp giữ sự cố bằng
  `Restrict`. Rollback `AddCitizenReview` gãy khi audit đã có `accepted` / `merged` / `analyzed` (cùng họ BE-19).
