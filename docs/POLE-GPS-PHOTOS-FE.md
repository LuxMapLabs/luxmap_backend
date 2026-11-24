# Ảnh trong lịch "Cập nhật vị trí cột" — nghiệp vụ cho FE (WP5 web, WP6 mobile)

Cập nhật 24/11/2026. Đặc tả gốc: `docs/contract-drift.md` mục **WO-POLE-GPS** (PG-1…PG-13), **EV-3 / EV-4** (mục *Ảnh bằng chứng*)
và **POLE-GPS-PHOTO** (PGP-1…PGP-5, Mỹ chốt 24/11). Tất cả đều **SELF-SIGNED**, chưa hợp nhất Contract. Tài liệu này là phần BE báo cho WP5 / WP6.

> **Đánh dấu 🆕 = đã chốt, BE chưa hiện thực** (ticket `POLE-GPS-PHOTO`). FE làm giao diện theo luật mới luôn; BE bật sau.

## 1. Một câu

Cột import chưa có toạ độ thì kỹ sư đi **gắn GPS và chụp ảnh cột cùng lúc**. Trong lịch `pole_gps_update`, **toạ độ** và **ảnh** là hai
trường riêng của cùng một thao tác:

- **Toạ độ** (`pole_locations` gửi kèm `complete`) là thứ **duy nhất** được ghi vào GIS khi Quản lý duyệt.
- **Ảnh** là **ảnh bằng chứng `observation`** của phiếu, gắn với cột (`pole_id`). 🆕 **Bắt buộc ≥ 1 ảnh cho mỗi cột có toạ độ báo.**
  Quản lý dùng ảnh để tin rằng kỹ sư đã đứng đúng chân cột, đã dán tem. 🆕 Khi duyệt, ảnh được **chép sang thư viện ảnh tài sản** của
  cột. Ảnh **không** quyết định toạ độ và **không** qua AI.

### GPS: trường riêng, không đọc EXIF (PGP-1)

- BE **không bao giờ** đọc GPS trong EXIF. Toạ độ cột = `pole_locations[]`; toạ độ ảnh = form `lat` / `lng`.
- App dùng **một lần đo GPS lúc bấm chụp** cho cả hai: cùng `lat`, `lng`; `accuracy_m` và `captured_at` của lần đo đó.
- Mobile **được** ghi thêm GPS vào EXIF nếu muốn (ảnh gốc giữ nguyên byte nên EXIF còn trong bản gốc; thumbnail luôn bỏ metadata). Đó chỉ
  là bản sao, BE không dùng.
- Vì sao không dùng EXIF: ảnh chọn từ thư viện trên Android 10+ bị xoá GPS khi app thiếu quyền `ACCESS_MEDIA_LOCATION`; EXIF hiếm khi có
  độ chính xác, mà `pole_locations` cần `accuracy_m`.

## 2. Ba kênh ảnh dễ nhầm — trong lịch GPS chỉ dùng kênh A

| | **A. Ảnh bằng chứng phiếu** ✅ | B. Ảnh cột cho AI | C. Ảnh tài sản cột |
|---|---|---|---|
| Endpoint | `POST /work-orders/{id}/evidence` | `POST /work-orders/{id}/pole-photos` | `POST /assets/poles/{id}/photos` |
| Mục đích | "Tôi đã ở đây, đã thấy thế này" | AI chấm đèn ON/OFF, Quản lý xác nhận → công bố trạng thái / sinh sự cố | Ảnh hồ sơ của cột (gallery, ảnh bìa) |
| Cột chưa GPS | **Nhận** | Từ chối `409 POLE_LOCATION_REQUIRED` | Nhận |
| Gắn phiếu | Có | Có | Không |
| Dùng trong lịch GPS | **Có** | **Không** — ảnh ban ngày, không phải để chấm đèn; cột chưa GPS cũng không nhận | Không gọi trực tiếp — 🆕 BE tự chép ảnh kênh A sang đây khi `verify` |

## 3. Luồng đầy đủ

```text
Quản lý (web)                         Kỹ sư (mobile)                                    Backend
──────────────                        ──────────────                                    ───────
1. Tạo lịch pole_gps_update      ──►                                                     open / assigned
   segment_id + pole_ids[]
2. Upload PDF tem QR (từng file) ──►                                                     attachments[]
                                      3. Tải bundle + PDF lúc có mạng
                                      4. start                                     ──►   in_progress
                                      5. Tại từng cột:
                                         a. dán tem QR
                                         b. bấm "Chụp ảnh cột" — một lần đo GPS:
                                            · toạ độ  → giữ trong máy (chưa gửi)
                                            · ảnh     → POST evidence              ──►   lưu ảnh (pole_id)
                                              kind=observation, pole_id=POLE-…
                                      6. Đẩy hết ảnh còn trong hàng chờ             ──►
                                      7. complete + pole_locations[]               ──►   🆕 thiếu ảnh cột → 409
                                                                                         done (GIS chưa đổi)
8. Mở báo cáo: mỗi cột
   GPS cũ │ GPS mới │ ảnh của cột
9a. verify                       ──►                                                     ghi pole.geom
                                                                                         🆕 chép ảnh → ảnh tài sản
                                                                                         verified
9b. return (kèm ghi chú)         ──►  quay lại bước 5                                    in_progress
```

Điểm mấu chốt:

- **Ảnh chỉ gửi được khi phiếu `in_progress`**, và chỉ bởi **người được giao**. Sau `complete` (phiếu `done`) ảnh mới bị từ chối
  `409 WORK_ORDER_NOT_IN_PROGRESS` → mobile **phải đẩy hết ảnh trước khi gửi `complete`** (bước 6 trước bước 7).
- 🆕 **Ảnh bắt buộc theo cột:** mỗi `pole_id` trong `pole_locations` phải có ≥ 1 ảnh `observation` **gắn đúng `pole_id` đó** trong lệnh
  này (ảnh vòng trước `return` vẫn tính; ảnh không gắn cột không tính). Thiếu → `409 POLE_EVIDENCE_REQUIRED`,
  `details {work_order_id, pole_ids: [...]}`. Cột kỹ sư không báo toạ độ thì không cần ảnh.
- `verify` chỉ đọc `pole_locations` để ghi GIS. Toạ độ `lat/lng` đi kèm ảnh **không bao giờ** được dùng làm toạ độ cột.
- 🆕 **`verify` chép ảnh:** mọi ảnh `observation` gắn cột (của các cột có toạ độ) thành ảnh tài sản của cột đó, trong cùng giao dịch
  với ghi GIS. Không có ảnh mới nào được tải lên — dùng chung file. `verify` bị `409 POLE_OFF_SEGMENT` thì không chép gì. Ảnh không ưng
  thì Quản lý ẩn trong gallery của cột (ẩn ở gallery không đụng ảnh của phiếu).

## 4. API

### 4.1 Gửi ảnh — `POST /api/v1/work-orders/{id}/evidence`

Quyền `ExecuteWorkOrders` (Kỹ sư hiện trường), `multipart/form-data`:

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `file` | ✔ | JPEG (kiểm theo magic bytes, không theo đuôi), ≤ 16 MiB; cả request ≤ 17 MiB |
| `kind` | ✔ | Luôn `observation` ở lịch GPS. `before` / `after` → `400 EVIDENCE_KIND_NOT_ALLOWED` |
| `captured_at` | ✔ | ISO 8601 đủ ngày giờ, nên gửi UTC `…Z` |
| `lat`, `lng` | ✔ | Vị trí điện thoại **lúc chụp** (±90 / ±180) |
| `pole_id` | tuỳ chọn ở API | Cột đang chụp. Phải nằm trong `pole_ids` của lịch. **Ở lịch GPS luôn gửi** — 🆕 ảnh không gắn cột không tính cho luật bắt buộc ảnh và không được chép sang ảnh tài sản |
| `client_op_id` | tuỳ chọn | UUID sinh ở máy. Gửi lại cùng khoá → `200` trả đúng ảnh cũ (chống trùng khi mạng chập chờn). **Mobile nên luôn gửi** |

Trả `201` (ảnh mới) hoặc `200` (gửi lại cùng `client_op_id`):

```json
{
  "evidence_id": "EVD-0123",
  "work_order_id": "WO-0042",
  "fault_id": null,
  "pole_id": "POLE-0007",
  "kind": "observation",
  "captured_at": "2026-11-24T02:15:30Z",
  "lat": 10.9651, "lng": 106.5042,
  "uploaded_by": "USR-0005",
  "uploaded_at": "2026-11-24T02:15:41.123456Z",
  "thumbnail_url": "/api/v1/evidence/EVD-0123/thumbnail",
  "original_url": "/api/v1/evidence/EVD-0123/original"
}
```

(ID ví dụ — đọc ID từ response, không tự đoán định dạng.)

### 4.2 Xem ảnh — `GET /api/v1/work-orders/{id}/evidence?pole_id=POLE-0007&page=1&page_size=50`

Quyền `ReadWorkOrders` (Quản lý, Cấp giám sát, kỹ sư được giao). Phân trang chuẩn, sắp theo `captured_at`. `pole_id` lọc tuỳ chọn;
không truyền thì ra mọi ảnh của phiếu, kể cả ảnh không gắn cột (`pole_id: null`).

Ảnh: `GET /api/v1/evidence/{evidence_id}/thumbnail` hoặc `/original` — **phải có Bearer**, nên `<img src>` trần không chạy:
`fetch` có header → `blob` → `URL.createObjectURL`. Không có presigned URL.

### 4.3 Dữ liệu để dựng màn duyệt

| Cần | Lấy ở |
|---|---|
| Danh sách cột của lịch, đúng thứ tự | `GET /work-orders/{id}` → `pole_ids[]` |
| **GPS cũ** (đang lưu trong GIS; `null` nếu cột chưa từng có GPS) | `GET /work-orders/{id}/poles` → `location` |
| **GPS mới** kỹ sư báo | `GET /work-orders/{id}` → `pole_locations[]` `{pole_id, lat, lng, accuracy_m, captured_at}`; cột kỹ sư không tìm được thì **không có** trong mảng |
| Ảnh của từng cột | `GET /work-orders/{id}/evidence?pole_id=…` |
| Ảnh chung (không gắn cột) | `GET /work-orders/{id}/evidence`, lọc `pole_id == null` ở FE |

## 5. Lỗi FE cần xử lý khi gửi ảnh

| Mã | Khi nào | FE làm gì |
|---|---|---|
| `400 VALIDATION_FAILED` | Thiếu / sai `file`, `kind`, `captured_at`, `lat`, `lng`, `client_op_id` | Lỗi lập trình — kiểm form |
| `400 EVIDENCE_KIND_NOT_ALLOWED` | Gửi `before` / `after` ở lịch GPS | Luôn gửi `observation` |
| `404 WORK_ORDER_NOT_FOUND` | Không phải người được giao, hoặc ngoài xã | Ẩn nút chụp |
| `404 POLE_NOT_FOUND` | `pole_id` không tồn tại / ngoài phạm vi | Bỏ ảnh khỏi hàng chờ, báo người dùng |
| `409 POLE_NOT_IN_WORK_ORDER` | Cột không nằm trong `pole_ids` của lịch | Chỉ cho chọn cột trong `pole_ids` |
| `409 WORK_ORDER_NOT_IN_PROGRESS` | Phiếu chưa `start`, đã `complete`, đã huỷ… | Chỉ hiện nút chụp khi `in_progress`; đẩy hàng chờ trước `complete` |
| `409 IDEMPOTENCY_CONFLICT` | Cùng `client_op_id` nhưng khác phiếu / nhãn / cột | Lỗi lập trình — mỗi ảnh một UUID riêng, không đổi `pole_id` khi gửi lại |
| `413 UPLOAD_TOO_LARGE` | Ảnh > 16 MiB | Nén lại trước khi gửi |
| `415 UNSUPPORTED_IMAGE_FORMAT` | Không phải JPEG, hoặc JPEG hỏng | Chụp lại / chuyển sang JPEG |

Và ở `complete`:

| Mã | Khi nào | FE làm gì |
|---|---|---|
| 🆕 `409 POLE_EVIDENCE_REQUIRED` | Cột trong `pole_locations` chưa có ảnh gắn cột | Mở lại các cột trong `details.pole_ids`, yêu cầu chụp; với `/sync/push` đây là kết quả lỗi của op `work_order_complete` |

## 6. WP6 — mobile

- **Màn từng cột** trong lịch GPS: một nút chính **"Chụp ảnh cột"** — mở camera, lúc bấm chụp lấy một lần đo GPS (nên chờ tới khi
  `accuracy` đủ tốt), lưu `lat, lng, accuracy_m, captured_at` làm toạ độ cột **và** dùng chính số đó cho `lat`, `lng`, `captured_at`
  của ảnh. Chụp thêm ảnh được; chụp lại thì hỏi có thay toạ độ cột bằng lần đo mới không. Ảnh luôn kèm `pole_id` của cột đang mở.
- Không lấy ảnh từ thư viện máy cho lịch này (GPS trong file có thể đã bị Android xoá; BE cũng không đọc).
- 🆕 Nút "Hoàn thành" chỉ bật khi mọi cột đã có toạ độ đều có ít nhất một ảnh (đã lên server hoặc đang trong hàng chờ).
- **Offline:** ảnh **không** đi qua `/sync/push`. Giữ một **hàng chờ multipart riêng**: mỗi ảnh lưu file + `pole_id` + `client_op_id`
  (sinh một lần lúc chụp, không đổi khi gửi lại). Có mạng thì đẩy.
- **Thứ tự bắt buộc khi kết thúc:** đẩy hết hàng chờ ảnh → rồi mới `complete` (trực tiếp hoặc op `work_order_complete` qua
  `/sync/push`). `complete` đến server trước thì ảnh còn lại bị `409 WORK_ORDER_NOT_IN_PROGRESS` (trừ ảnh đã lên server, gửi lại
  cùng `client_op_id` vẫn được `200`).
- **Bị `return`:** phiếu về `in_progress`, chụp thêm được. Ảnh cũ **vẫn còn** (không có API xoá ảnh bằng chứng). `complete` lần sau
  **thay toàn bộ** `pole_locations` — phải gửi lại cả những cột đã đúng ở lần trước.
- Cột chưa GPS vẫn chụp ảnh bằng chứng được. **Không** gọi `/pole-photos` trong lịch này.

## 7. WP5 — web, màn duyệt báo cáo

Bảng, mỗi dòng một cột theo thứ tự `pole_ids`:

| Cột | GPS cũ | GPS mới | Độ chính xác | Lúc lấy | Ảnh |
|---|---|---|---|---|---|
| `POLE-0007` | `location` hoặc "Chưa có" | `pole_locations[i]` hoặc "Kỹ sư không báo" | `accuracy_m` m | `captured_at` | thumbnail của `evidence?pole_id=` ; "Không có ảnh" nếu rỗng |

- Nút "Xem GIS": bản đồ vẽ **GPS mới** (điểm chính), **GPS cũ** (nếu có, nét mờ) và **vị trí chụp ảnh** `lat/lng` của từng ảnh — ba
  điểm gần nhau là dấu hiệu tốt. Vị trí chụp ảnh chỉ để tham khảo, không bao giờ là toạ độ cột.
- Mục "Ảnh chung của phiếu" cho ảnh `pole_id == null`.
- Ảnh từ nhiều vòng (`return` rồi `complete` lại) nằm chung; sắp theo `captured_at` như API trả.
- 🆕 Sau `verify` thành công, gallery của từng cột (`GET /assets/poles/{id}/photos`) có ảnh vừa duyệt; cột chưa có ảnh bìa thì
  ảnh bìa hiện theo luật bìa hiện có. Màn duyệt nên báo "Đã thêm N ảnh vào hồ sơ cột".
- **Duyệt:** `verify` — tất cả hoặc không. Có cột lệch hành lang tuyến → `409 POLE_OFF_SEGMENT`,
  `details.poles[] {pole_id, segment_id, distance_m, corridor_m}`: tô đỏ các dòng đó, không cột nào đổi, phiếu vẫn `done` → Quản lý
  `return` kèm ghi chú.
- Thiếu cột / ảnh không thuyết phục → `return` kèm ghi chú cho kỹ sư.

## 8. Quyết định và câu hỏi còn mở

| Mã | Câu hỏi | Trạng thái |
|---|---|---|
| Q-1 | Có **bắt buộc** ít nhất một ảnh cho mỗi cột có toạ độ khi `complete` không? | **Có** — Mỹ chốt 24/11 (PGP-2), BE chưa hiện thực |
| Q-2 | Sau `verify`, có **chép ảnh** sang thư viện ảnh tài sản của cột (kênh C) không? | **Có** — Mỹ chốt 24/11 (PGP-3), BE chưa hiện thực |
| Q-3 | Có **chặn** `/pole-photos` (kênh B) ở phiếu `pole_gps_update` không? | **Còn mở** — hiện chưa chặn (chỉ chặn cột chưa GPS); FE đừng gọi |
| Q-4 | GPS ghi vào EXIF hay gửi riêng? | **Gửi riêng** — Mỹ chốt 24/11 (PGP-1); EXIF tuỳ mobile, BE không đọc |
