# Bản tin cho WP5 / WP6 — CITIZEN-QR + BE-17 (nháp để Mỹ gửi, 13/10/2026)

Nguồn chuẩn cho từng dòng: `docs/contract-drift.md` (QR-1…QR-6, PB-1…PB-8, PC-1…PC-10, SC-1…SC-6) và spec
`docs/openapi/luxmap-v1.5.json` trên `dev`. Mọi thứ dưới đây **SELF-SIGNED — nền tạm tới FW kế tiếp**; xin xác nhận / phản hồi ở FW.
Đã được Codex soát từng khẳng định với spec, code và `origin/dev` của hai repo (`.ai/reviews/handoff-2026-10-13-by-codex.md`).

---

## Gửi WP5 (web)

**1. QR trên cột — chỉ Quản lý (CITIZEN-QR P2a, #133)**
- `POST /api/v1/qr-codes {pole_id}` → 201 `{qr_id, code, url, pole_id, commune_id, active, created_at, created_by, revoked_at, revoked_by}`;
  cột không tồn tại / ngoài phạm vi → 404 `POLE_NOT_FOUND`. Cấp mới thì mã cũ của cột tự thu hồi. `url` = `{WEB_APP_BASE_URL}/r/{code}` —
  **web vẽ ảnh QR từ `url`**, backend không sinh ảnh.
- `POST /api/v1/qr-codes/{qr_id}/revoke` → 200 cùng hình dạng; lần hai → 409 `QR_CODE_ALREADY_REVOKED`; không tồn tại → 404 `QR_CODE_NOT_FOUND`.
- `GET /api/v1/qr-codes?pole_id=&active=&commune_id=` (phân trang).
- Mọi thao tác dùng **`qr_id`**; `code` là thứ in lên cột — đừng đưa vào URL nội bộ, log hay analytics.

**2. Trang công khai `/r/:code` — KHÔNG đăng nhập (P2b, #135)**
- Route mới trong SPA, không token. Lấy `code` từ đường dẫn rồi gửi trong **body**:
  - `POST /api/v1/public/qr/lookup {code}` → `{segment_name, commune_name}`. Mã sai / thu hồi → 404 `PUBLIC_QR_NOT_FOUND`.
  - `POST /api/v1/public/reports` multipart: `code`, `kind` (`lamp_out | lamp_dim | pole_damaged | other`), `description?` (≤ 1000),
    `lat?`+`lng?` (cùng có / cùng vắng), `client_op_id` (UUID), `photos` (0–3 JPEG) → **202** `{client_op_id, received_at, photos_kept}`.
- Ảnh: ≤ 3, mỗi ảnh ≤ 8 MiB và ≤ 40 MP. Quá số ảnh hoặc quá pixel → 400 `VALIDATION_FAILED` (pixel: `details.max_pixels`); quá byte →
  413 `VALIDATION_FAILED` (`details.max_bytes`); không phải JPEG / không giải mã được → 415 `UNSUPPORTED_IMAGE_FORMAT`.
- **Thử lại:** cùng `client_op_id` phải giữ **cùng mã, cùng nội dung, cùng ảnh (byte và thứ tự)** → nhận lại biên nhận cũ; đổi nội dung thì dùng
  UUID mới, nếu không → 409 `IDEMPOTENCY_CONFLICT`.
- 429 `RATE_LIMITED`: chờ đúng số giây trong `Retry-After` (mặc định tra mã 30 lần / 10 phút, gửi 10 lần / giờ theo địa chỉ client).
  503 `SERVER_BUSY`: thử lại sau. Gửi cũng có thể 404 `PUBLIC_QR_NOT_FOUND` nếu mã bị thu hồi giữa chừng.
- `photos_kept` nhỏ hơn số ảnh gửi = vượt hạn mức ảnh, **chữ vẫn được nhận** — vẫn báo người dân là đã nhận.
- Đề nghị: `Referrer-Policy: no-referrer` cho route này; mô tả của người khác hiển thị **dạng chữ thuần**, không render markup.

**3. Hàng chờ duyệt (P2c, #136)**
- **Quyền:** Cấp giám sát, Quản lý, Quản trị **đọc** (metadata); **chỉ Quản lý** duyệt và mở ảnh. Báo cáo / ảnh không tồn tại hoặc ngoài
  phạm vi → 404 `CITIZEN_REPORT_NOT_FOUND` / `CITIZEN_PHOTO_NOT_FOUND`; lọc `commune_id` ngoài phạm vi → 403 `COMMUNE_FORBIDDEN`.
- `GET /api/v1/citizen-reports?status=&commune_id=&pole_id=` → phân trang `CitizenReportItem`; `status` CSV `pending | accepted | merged | rejected`,
  vắng = `pending`; mới nhất trước (`last_submitted_at`).
- `GET /api/v1/citizen-reports/{id}` (chỉ chi tiết mới có): `pole`, `open_faults`, `submissions[]`, **`allowed_actions`** (chỉ hiện nút có trong
  danh sách) và **`acceptable_fault_types`**.
- Ảnh ở `submissions[].photos[]` → `thumbnail_url` / `original_url` (proxy). Người không phải Quản lý thấy `photo_count` nhưng `photos = []`.
  `ai_hint` = `null` khi chưa chạy / không chạy được (không phải lỗi); có thì `{label, confidence, artifact_version, checked_at}`, `label` =
  `lamp_on | lamp_off | inconclusive`, `confidence = null` khi `inconclusive`. **Ghi rõ là "gợi ý".**
- Quyết định — **mọi body có `submission_count` (≥ 1)** = số lượt gửi đang hiển thị; cả ba trả **200 + chi tiết báo cáo** (không trả object sự cố):
  - `POST …/accept {submission_count, fault_type, severity?, note?}` — `fault_type` chọn trong `acceptable_fault_types` (`lamp_out` / `lamp_dim`),
    `severity` `low | medium | high | critical` (vắng = `medium`), `note` ≤ 1000. Tạo sự cố `field_report` ở trạng thái `confirmed`.
  - `POST …/merge {submission_count, fault_id, note?}` — `fault_id` từ `open_faults`; không tồn tại / ngoài phạm vi → 404 `FAULT_NOT_FOUND`.
  - `POST …/reject {submission_count, reason}` — `reason` 1–1000 ký tự sau khi bỏ khoảng trắng.
  - 400 `VALIDATION_FAILED` khi nhập sai. 409 `CITIZEN_REPORT_CHANGED` → người dân vừa gửi thêm: tải lại chi tiết rồi quyết. 409
    `CITIZEN_REPORT_ALREADY_DECIDED`, `CITIZEN_REPORT_NOT_ACCEPTABLE`, `OPEN_FAULT_EXISTS` (kèm `fault_ids` → gợi ý gộp), `FAULT_NOT_MERGEABLE`.
  - Báo cáo "cột hỏng" / "khác" không thành sự cố bằng `accept` — dùng luồng phiếu kiểm tra thường.
- **Thông báo:** thêm `citizen_report_received` vào `NotificationType` và `citizen_report` vào `NotificationEntityType`; mở bằng
  `GET /citizen-reports/{entity_id}`. Chỉ Quản lý của xã nhận, khi **mở mục chờ mới**; lượt gửi thêm vào mục đó không phát thông báo nữa.

**4. Khảo sát — lý do lỗi và độ phủ (BE-17, #140)**
- `GET /api/v1/sweeps/{id}` có thêm **`latest_run`**: `{run_id, attempt, result_state (succeeded | failed), stage, error_code, started_at,
  finished_at, coverage_pct, detection_coverage_pct, dim_coverage_pct, coverage_reason}` — lần xử lý **đã xong** gần nhất; `null` khi chưa
  xong lần nào; đang xử lý lại thì vẫn là lần trước (polling `processing_status`). `stage` / `error_code` là **chuỗi mở**, đừng khoá enum;
  thành công: `stage = complete`, `error_code = null`.
- `GET /api/v1/sweeps/{id}/coverage?run_id=` → `{sweep_id, run_id, coverage_pct, segments[]}`; mỗi segment: `segment_id, length_m,
  covered_ratio, passes[], gaps[], poles_expected, poles_observed, not_observed_pole_ids[]`. Vẽ `segments[].gaps[].geometry` (LineString
  [lng, lat]) khi khác `null`. `from_m` / `to_m` = **vị trí dọc tuyến** tính từ đỉnh đầu (nullable), không phải khoảng cách; lượt `reverse`
  có `from_m > to_m`.
- `coverage` chỉ đọc run **thành công**; vắng `run_id` = lần thành công mới nhất — có thể khác `latest_run` đang `failed`. Không có run thành
  công → 404 `RUN_NOT_FOUND`; không giữ đủ mọi xã của run và của phiếu → 404 `SWEEP_NOT_FOUND`.
- `processing_status` chốt 5 giá trị: `not_started | queued | processing | succeeded | failed` (types của web đã đúng).

**⚠️ Spec khai thiếu `null` (ticket SPEC-NULLABLE, đã chốt 13/10):** 25 thuộc tính response **dạng `$ref`** (11 object, 14 enum) có thể `null`
nhưng đang xuất thành `$ref` trần — danh sách ở `.ai/results/SPEC-NULLABLE-p1.md` §1 (vd. `PoleMapDetail.fixture`, `latest_run`,
`gaps[].geometry`, `PoleProperties.power_source`); thêm `PoleProperties.lamp_watt` / `install_date` ở bản hợp nhất. Đây **không** phải toàn
bộ trường nullable — đối chiếu thêm trường scalar trong spec. Backend sẽ khai lại dạng
`{"anyOf": [{"$ref": "…/PoleMapFixture"}, {"type": "object", "nullable": true, "enum": [null]}]}` (enum: `"type": "string"`) — "tham chiếu
HOẶC đúng `null`", đúng OAS 3.0.4 và validator chặt chấp nhận. **`scripts/generate-types.js` hiện chưa hiểu `anyOf`** — nhờ WP5 thêm một nhánh
**trước** nhánh `type`: `anyOf` gồm một `$ref` và một nhánh `enum: [null]` ⇒ sinh `<Tên> | null` (vd. `PoleMapFixture | null`, `SourceChannel
| null`); giữ dấu `?` theo `required` như cũ, không suy `?` từ nullable. Backend **chờ WP5 báo xong mới merge**. Trong lúc chờ: coi các trường trong danh sách là nullable; `GisDrawerPanel` nên hiện "chưa có bóng"
thay vì điền giá trị mặc định.

---

## Gửi WP6 (mobile)

**1. Độ phủ khảo sát của server (BE-17, #140)**
- F05 trên `origin/dev` hiện **chỉ xem lại video** trước khi nộp; kiểm độ phủ / chất lượng trước nộp vẫn là khoảng trống. **BE-17 là độ phủ
  server tính SAU khi xử lý**, từ vết GPS đã ghép tuyến: `GET /api/v1/sweeps/{id}/coverage` → các đoạn chưa đi qua (`segments[].gaps[]` kèm
  LineString, có thể `null`) và cột chưa quan sát. Có thể dùng để báo kỹ sư "cần quay lại đoạn X" sau khi phiên được xử lý. Lỗi:
  404 `RUN_NOT_FOUND` / `SWEEP_NOT_FOUND`.
- `GET /api/v1/sweeps/{id}` thêm `latest_run` (lý do phiên xử lý hỏng; `null` trước lần đầu). `processing_status` chốt 5 giá trị.

**2. Thông báo (dự phòng)** — API có loại mới `citizen_report_received` (cho Quản lý). App chưa có DTO thông báo trên `origin/dev`; khi làm,
nếu `type` là enum Kotlin, xin xử lý **giá trị lạ** (không crash) — backend sẽ còn thêm loại.

**3. Không đổi gì với luồng nộp phiên hiện có.** CITIZEN-QR là web + trang công khai, không đụng app.

**4. 🔴 DTO đang khai KHÔNG-null nhưng API có thể trả `null`** — lỗi thật, độc lập với spec; sẽ lộ khi chi tiết cột / bản đồ bỏ dữ liệu giả
và gọi API thật (deserialize thất bại, cả màn hỏng):
- `PoleDetailDto.fixture` — cột không có bóng đang dùng.
- `PoleDetailDto.luminanceBaseline` — chưa có baseline cho bóng đang dùng (cần đủ mẫu hợp lệ từ sweep đã duyệt theo chiều đi, mặc định ≥ 3).
- `PoleDetailCurrentStatusDto.sourceChannel` (`PoleDetailDto.currentStatus`) — khi cột chưa có hàng trạng thái.
- `PolePropertiesDto.powerSource`, `.fixtureType` (`PoleFeatureDto.properties`) — cột không có bóng.
- Scalar cũng nullable trong spec: `PolePropertiesDto.{lampWatt, installDate, warrantyExpiry, lastSeenAt, lastSweepId}`,
  `PoleDetailFixtureDto.warrantyExpiry`, `PoleDetailCurrentStatusDto.{statusConfidence, determinedAt}`,
  `PoleDetailLuminanceBaselineDto.outThresholdRatio`, `PoleDetailLuminancePointDto.{normalizedLuminance, baselineRatio}`.

Đề nghị thêm `?` (+ `= null`), cập nhật cả `toPoleDetail()`, `toPoleMarker()` và model / UI để nhận `null` (hiện "chưa có"), và **bỏ yêu cầu
`PoleDetailFixtureDto.supplier`** — API không trả trường đó. `iotNode` và `inspectionOutcome` đã đúng. DTO viết mới nên đối chiếu danh sách
nullable ở `.ai/results/SPEC-NULLABLE-p1.md` §1 **và** các trường scalar `nullable` trong spec.
