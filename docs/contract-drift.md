# Chỗ lệch giữa code và Contract — nội dung mang vào FW-00

**Mục đích:** danh sách những chỗ code hiện tại và `api-contract-v1.1.md` / `tasks-backend.csv`
không khớp nhau, để cả nhóm quyết trong buổi review FW-00.

**Đây là tài liệu ĐỀ XUẤT, không phải quyết định.** Không sửa Contract, không sửa task list dựa
trên file này — mọi thay đổi phải được cả BE và FE duyệt rồi tăng version, đúng nguyên tắc ở đầu
Contract: *"Muốn đổi field/enum → mở issue, cả BE và FE cùng duyệt, tăng version. Không đổi ngầm."*

**Ai cần đọc:** WP5 (Web) và WP6 (Android) — nhiều mục dưới đây các bạn sẽ gặp ngay khi tích hợp.

**Trạng thái code:** BE-00 → BE-09 xong, 252 test xanh.

---

## Tóm tắt

| # | Chỗ lệch | Mức | Ai bị ảnh hưởng | Đề xuất sửa bên nào |
|---|---|---|---|---|
| 1 | Nhóm endpoint `/auth` chưa có trong Contract | 🔴 Cao | WP5, WP6 | Contract — thêm mục 2.10 |
| 2 | 7 mã lỗi chưa có trong Contract | 🔴 Cao | WP5, WP6 | Contract — thêm vào mục 0 |
| 3 | Giá trị `user_role` chưa có trong Contract mục 1 | 🔴 Cao | WP5, WP6 | Contract — thêm vào mục 1 |
| 4 | Correlation id nằm ở cả header lẫn body | 🟡 Vừa | WP5, WP6 | Contract — ghi rõ |
| 5 | **Endpoint đăng ký — đã ĐẢO NGƯỢC quyết định cũ** | 🔴 Cao | WP5, WP6 | Contract — thêm vào mục 2.10 |
| 6 | Bộ mock FO-26 lệch bảng prefix mục 0.2 | 🔴 Cao | WP5, WP6, BE-39 | Mock — sửa 6 chỗ |
| 7 | `page_size` vượt 200 bị kẹp im lặng | 🟡 Vừa | WP5, WP6 | Contract — ghi rõ hành vi |
| 8 | Route không tồn tại trả 401 khi chưa đăng nhập | 🟢 Thấp | WP5, WP6 | Contract — ghi rõ |
| 9 | `CLAUDE.md` thiếu `data_source` trong khối enum | 🟢 Thấp | Nội bộ BE | `CLAUDE.md` |
| 10 | **Contract mục 1 ↔ mục 2.9 mâu thuẫn về `data_source`** | 🔴 Cao | Nội bộ BE, CV-11/CV-18 | Contract — **đã chốt lên v1.2** |
| 11 | ~~`commune_id` trên 5 bảng Assets chưa có khoá ngoại~~ | 🟡 Vừa | Nội bộ BE, BE-12 | **ĐÃ CHỐT** — `AdministrativeUnit` sang `Persistence` |
| 14 | **ID không còn cố định độ dài — FE/mobile phải sửa regex** | 🔴 Cao | WP5, WP6 | Contract — ghi rõ ở mục 0.3 |
| 12 | Mock: `POLE-0047` mâu thuẫn giữa 3 file | 🔴 Cao | WP5, WP6, BE-39 | Mock — **đã sửa** |
| 13 | ~~`LPAD` cắt bớt ID khi vượt độ rộng~~ | 🔴 Cao | Toàn bộ 16 entity | Code BE-06 — **ĐÃ SỬA** |
| 15 | **Mã lỗi thứ 8 ngoài Contract: `UNSUPPORTED_IMAGE_FORMAT`** | 🟡 Vừa | WP5, WP6 | Contract — gộp vào mục 2 |
| 16 | **Ràng buộc phiên bản: ImageSharp phải ở 3.x** | 🟡 Vừa | Nội bộ BE, CI | Không phải Contract — quyết định công cụ |
| 17 | **`nearest_luminance` luôn `null` cho tới BE-17** | 🔴 Cao | CV-12 | Không sửa Contract — nợ hiện thực, BE-15/BE-17 nối nguồn |
| 18 | **Mục 2.9 thiếu bảng bắt buộc/không** — `pole_id` đã chốt BẮT BUỘC | 🟡 Vừa | WP6, CV-12 | Contract — thêm bảng như mục 2.8 |
| 19 | **`measured_by` có ở task list, KHÔNG có ở Contract mục 2.9** | 🟡 Vừa | WP6 | Contract — theo khuôn `reported_by` mục 2.8 |
| 20 | **`GET /poles/{id}/lux-readings` có phân trang** (mục 2.9 không nhắc) | 🟢 Thấp | WP5, WP6 | Contract — ghi rõ |
| 21 | **Mã lỗi thứ 9 ngoài Contract: `SERVER_OWNED_FIELD`** | 🟡 Vừa | WP5, WP6 | Contract — gộp vào mục 2 |
| 22 | **`pole.external_ref`** — cột mới, LƯU và TRA được nhưng KHÔNG emit ra API | 🟡 Vừa | Nội bộ BE, tổ sửa chữa | Không phải Contract — quyết định lược đồ, migration thuộc BE-12 |
| 23 | **Bộ mock FO-26 không mang `feeder_id`** — nạp xong 103 cột chưa gắn mạch điện | 🔴 Cao | CV-15, BE-13, RQ2 | Mock — bổ sung, hoặc gán thủ công có ghi chép |
| 24 | **`pole_current_status.status_confidence` nhận `NaN`, `Infinity` và giá trị ngoài `0..1`** | 🔴 Cao | CV-11, BE-28, RQ1 | Code BE-15/BE-17 — chưa sửa |
| 25 | **`commune_id` suy từ scope JWT khi `pole_id` NULL** | 🟡 Vừa | WP6, FM-19 | Contract — thêm vào mục 2.8 |
| 26 | **Ghi vết trên bảng `fault`, không có `FaultHistory`** | 🟡 Vừa | Nội bộ BE, BE-19 | Không phải Contract — quyết định lược đồ |
| 27 | **Contract không định nghĩa "fault MỞ"** cho `open_fault_count` | 🟡 Vừa | WP5, BE-28, BE-40 | Contract — ghi rõ ba trạng thái |
| 28 | **`mock-faults.json` lệch mục 2.4 mười chỗ** (thiếu 7 trường, thừa 2, `CLU` thay `CLS`) | 🔴 Cao | WP5, WP6, BE-39 | Mock — sửa; mục 6 mới ghi file work-orders |
| 29 | **Nhóm endpoint `/api/v1/assets/…`** — CRUD tài sản + import, không có trong Contract | 🟡 Vừa | WP5, WP6 | Contract — thêm mục mới, KHÔNG gộp vào 2.1 |
| 30 | **Hình dạng kết quả import** `{inserted, updated, failed, total_errors, truncated, rows[]}`, trả **200** khi có dòng hỏng | 🟡 Vừa | WP5 | Contract — thêm; 207 đã cân nhắc và loại |
| 31 | **Vai trò nào được GHI tài sản** — mục 7 chỉ nói phạm vi địa bàn | 🔴 Cao | WP5, WP6, BE-33 | Contract — ghi rõ; BE-12a chốt Quản trị |
| 32 | **`external_ref` trên `road_segment`, `feeder`, `pole`** — LƯU và upsert theo, KHÔNG emit | 🟡 Vừa | Nội bộ BE, BE-39 | Không phải Contract — mở rộng mục 22 từ 1 bảng lên 3 |
| 33 | **Hai mã lỗi mới: `ASSET_NOT_FOUND`, `EXTERNAL_REF_TAKEN`** | 🟡 Vừa | WP5, WP6 | Contract — gộp vào mục 2 |
| 34 | **`GET /assets/*` trả danh sách ID, không phải entity** — chỗ giữ chỗ cho BE-12b | 🟡 Vừa | WP5 | Tạm thời — BE-12b thay khi Thịnh/Ngọc duyệt |

---

## 1. Nhóm endpoint `/auth` chưa có trong Contract 🔴

**Contract đang ghi gì:** không có gì. Mục *"Contract phủ tới đâu"* liệt kê BE-07 là
*"Endpoint đăng ký / đăng nhập / refresh"* nằm trong nhóm **chưa có đặc tả**.

**Code đang làm gì:** ba endpoint đã chạy, 202 test phủ.

```
POST /api/v1/auth/login     { "username": "...", "password": "..." }
POST /api/v1/auth/refresh   { "refresh_token": "..." }
POST /api/v1/auth/logout    { "refresh_token": "..." }
```

Response của `login` và `refresh` — **đúng bốn trường, không hơn**:

```json
{ "access_token": "...", "refresh_token": "...", "token_type": "Bearer", "expires_in": 3600 }
```

`expires_in` là lifetime của **access** token, tính bằng giây. `logout` luôn trả `204`, kể cả khi
token đã thu hồi hoặc không tồn tại.

Claim trong access token:

| Claim | Kiểu | Ví dụ |
|---|---|---|
| `sub` | chuỗi | `USR-001` |
| `role` | **chuỗi đơn**, không phải mảng | `maintenance_engineer` |
| `commune_ids` | **luôn là mảng** | `["COM-001"]` · Quản trị: `["*"]` |
| `iss` / `aud` | chuỗi | `luxmap-api` / `luxmap-clients` |

**Đề xuất:** bổ sung thành **mục 2.10 (Auth)** của Contract, giữ nguyên hình dạng đang có — WP6 đã
sinh DTO từ `docs/openapi/luxmap-v1.json`, đổi bây giờ là breaking change.

**Ảnh hưởng:** WP5 và WP6 cần biết chính xác tên trường và tên claim. FM-05 phụ thuộc trực tiếp.

---

## 2. Bảy mã lỗi chưa có trong Contract 🔴

**Contract đang ghi gì:** mục 0 chốt hình dạng `{ error: { code, message, details } }` và các mục
2.1, 2.8, 7 nêu đích danh 6 mã: `BBOX_TOO_LARGE`, `POLE_NOT_FOUND`, `LOCATION_REQUIRED`,
`FAULT_TYPE_NOT_REPORTABLE`, `DUPLICATE_OP`, `COMMUNE_FORBIDDEN`.

**Code đang làm gì:** phải thêm 7 mã nữa để mọi API cùng một hình dạng lỗi:

| Mã | HTTP | Khi nào | Thuộc |
|---|---|---|---|
| `VALIDATION_FAILED` | 400 | Body sai định dạng hoặc thiếu trường. Chi tiết từng field nằm trong `details` | BE-04 |
| `INTERNAL_ERROR` | 500 | Lỗi chưa xử lý. Thông điệp cố ý chung chung | BE-04 |
| `INVALID_CREDENTIALS` | 401 | Sai tài khoản **hoặc** sai mật khẩu — cố ý dùng chung một mã | BE-07 |
| `ACCOUNT_LOCKED` | 403 | Đúng mật khẩu nhưng tài khoản bị khoá | BE-07 |
| `INVALID_REFRESH_TOKEN` | 401 | Refresh token sai / hết hạn / đã thu hồi / bị dùng lại — chung một mã | BE-07 |
| `UNAUTHENTICATED` | 401 | Access token thiếu / sai chữ ký / hết hạn / sai `iss` / sai `aud` — chung một mã | BE-08 |
| `IDENTIFIER_TAKEN` | 409 | Đăng ký trùng username hoặc email | BE-07 (bổ sung) |

**Vì sao gộp mã:** phân biệt "sai tài khoản" với "sai mật khẩu" là nói cho kẻ tấn công biết tài
khoản nào tồn tại. Tương tự với refresh token và access token. **Đây là chủ ý, đừng tách ra.**

**Đề xuất:** thêm cả 7 vào mục 0 của Contract.

**Ảnh hưởng:** WP5 và WP6 đang phải tự đoán 7 mã này.

---

## 3. Giá trị vai trò chưa có trong Contract mục 1 🔴

**Contract đang ghi gì:** mục 7 liệt kê 4 vai trò **bằng tiếng Việt** (Kỹ sư bảo trì, Tổ khảo sát /
sửa chữa, Cơ quan quản lý, Quản trị) nhưng **không chốt giá trị enum trên dây**. Mục 1 không có
`user_role`.

**Code đang làm gì:** BE-06 đặt 4 chuỗi, đã nằm trong CHECK constraint của DB và trong claim `role`
của JWT:

```
management_agency · maintenance_engineer · field_crew · administrator
```

**Đề xuất:** thêm `user_role` vào **mục 1** với đúng 4 giá trị trên, và ghi rõ ánh xạ sang tên
tiếng Việt ở mục 7.

**Ảnh hưởng:** WP5 và WP6 sẽ hardcode 4 chuỗi này để hiện giao diện theo vai trò. **Chốt trước khi
họ code, đổi sau là breaking change với cả hai.**

---

## 4. Correlation id nằm ở cả header lẫn body 🟡

**Contract đang ghi gì:** mục 0 chốt body lỗi đúng ba khoá `{ code, message, details }` rồi ghi
thêm *"+ correlation id"* — **không nói nằm ở đâu**.

**Quyết định ban đầu (BE-00):** chỉ ở header `X-Correlation-Id`, để body giữ đúng ba khoá đã publish.

**Code hiện tại (BE-04 trở đi):** ở **cả hai** — header `X-Correlation-Id` trên mọi response, và
`error.details.correlation_id` trong body lỗi.

Không phá hình dạng ba khoá vì `details` là bag tự do. Nhưng đây là **thay đổi so với quyết định
ban đầu** nên cần thống nhất lại.

**Đề xuất:** giữ nguyên hành vi hiện tại và ghi rõ vào Contract mục 0 — FE lấy từ body tiện hơn khi
báo lỗi, còn header phục vụ trường hợp response thành công.

---

## 5. Endpoint đăng ký — ĐÃ ĐẢO NGƯỢC quyết định trước đó 🔴

> ⚠️ **Mục này thay thế hoàn toàn nội dung cũ.** Trước đây mục 5 ghi *"đã bỏ endpoint đăng ký"*.
> Quyết định đó **đã bị đảo**. Phần dưới giải thích vì sao đảo được mà vẫn an toàn — người đọc ở
> FW-00 cần hiểu cơ chế thay thế, không chỉ biết là đã đảo.

**Task list đang ghi gì:** dòng BE-07 — *"API đăng ký / đăng nhập / refresh token"*.

**Contract đang ghi gì:** không có gì. Cả nhóm `/auth` chưa được đặc tả (xem mục 1).

**Code đang làm gì:** có `POST /api/v1/auth/register`, **mở, không cần mã mời, không cần duyệt trước**.

### Vì sao trước đây bỏ

Lập luận cũ: LuxMap không có vai trò Người dân, cả 4 vai trò đều là cán bộ nội bộ, nên **không có
cách hợp lý nào để người tự đăng ký nhận vai trò và địa bàn**. Lập luận đó vẫn đúng — chỉ là nó
chứng minh sai kết luận.

### Vì sao giờ mở lại được

Vì tách hai thứ vốn bị gộp làm một:

> **Đăng ký tạo ra DANH TÍNH. Đăng ký KHÔNG tạo ra QUYỀN.**

Tài khoản mới nhận:

| | |
|---|---|
| `role` | `field_crew` — vai trò hẹp nhất trong bốn |
| `commune_ids` | **RỖNG** — không xã nào |
| trạng thái | mở, không khoá |

Người dùng **đăng nhập được ngay**, nhưng **không thấy một bản ghi nào** cho tới khi quản trị gán
địa bàn. Việc gán thuộc **BE-33**.

### Cơ chế an toàn — dựa trên thứ đã có sẵn, không phải thứ mới

An toàn KHÔNG đến từ endpoint đăng ký. Nó đến từ cơ chế lọc của BE-08, vốn đã tồn tại và đã có test
trước khi endpoint này ra đời:

- BE-07 phát `commune_ids: []` cho tài khoản không có xã — đã đo bằng token thật
- BE-08 `CommuneScopeAccessor` fail đóng: 0 phần tử → `CommuneScope.Empty`
- Query filter của BE-08 không cho một hàng nào lọt qua
- Tra cứu trực tiếp theo ID → **404**, không phải 403, nên không lộ cả sự tồn tại

Ba đường leo thang đặc quyền hiển nhiên nhất đã có test riêng: gửi kèm `role`, `commune_ids: ["*"]`,
`commune_ids: ["COM-001"]` trong body — **cả ba bị bỏ qua hoàn toàn**, vì DTO không có property nào
để nhận chúng.

### Chọn `field_crew` làm vai trò thấp nhất — vì sao

| Vai trò | Nếu bị gán nhầm địa bàn thì thiệt hại |
|---|---|
| `field_crew` | Báo sự cố nhiễu — nhưng kỹ sư vẫn phải duyệt |
| `maintenance_engineer` | **Bác bỏ được sự cố thật** → che mất hỏng hóc |
| `management_agency` | Nhìn xuyên nhiều xã |

CLAUDE.md: *"kỹ sư duyệt chứ không tạo"*. `field_crew` là vai trò tác nghiệp đầu vào, quyền hẹp nhất.

Đã cân nhắc thêm vai trò thứ năm kiểu `pending` — bỏ, vì an toàn đến từ `commune_ids` rỗng chứ không
từ tên vai trò, mà thêm giá trị enum thì WP5/WP6 phải hardcode thêm và BE-08 phải thêm policy.

### Chống dò tài khoản — chọn trả lỗi rõ

Đăng ký mở tạo ra một lỗ rò không có ở hệ thống nội bộ: thử đăng ký để biết username nào đã tồn tại.
**Chọn (a) trả lỗi rõ** kèm `409 IDENTIFIER_TAKEN`.

Lý do trong bối cảnh cụ thể: hệ thống nội bộ, không có API công khai cho người dân, chạy trong mạng
cơ quan, khoảng chục tài khoản với username đoán được sẵn (`admin`, `engineer`, `crew`). Trả cùng
một response cho cả thành công lẫn trùng sẽ khiến người đăng ký thật nhận "thành công" rồi không
đăng nhập được — một lỗi dùng hàng ngày, đổi lấy việc chống một mối đe doạ gần như không áp dụng.

⚠️ **Nếu hệ thống ra Internet thì phải xem lại** — và cách đúng lúc đó là rate limit cả `/login` lẫn
`/register`, không phải làm mờ response.

### Đề xuất cho Contract

Bổ sung vào **mục 2.10 (Auth)** cùng với login/refresh/logout:

```
POST /api/v1/auth/register
{ "username": "...", "email": "...", "full_name": "...", "password": "..." }

201 Created
{ "user_id": "USR-005", "username": "...", "email": "...", "full_name": "...",
  "role": "field_crew", "commune_ids": [],
  "message": "Account created. An administrator must assign communes before any data becomes visible." }
```

Ghi rõ trong Contract: **server áp cứng `role` và `commune_ids`, client không set được**; mật khẩu
tối thiểu **12 ký tự**, không ràng buộc thành phần (NIST SP 800-63B: độ dài hơn quy tắc thành phần);
trùng định danh → **409 `IDENTIFIER_TAKEN`**; **không trả token** — gọi `/auth/login` riêng.

**Ảnh hưởng:** WP5 cần màn đăng ký và phải hiển thị được thông điệp "chưa được gán địa bàn". WP6
tương tự nếu mobile cho đăng ký. **Cả hai phải biết tài khoản mới sẽ thấy danh sách rỗng — đó là
đúng, không phải lỗi API.**

**Còn nợ:** chưa có UI gán vai trò/địa bàn cho tới BE-33. Trong lúc chờ, quản trị gán bằng SQL —
xem `docs/authorization-guide.md`.

---

## 6. Bộ mock FO-26 lệch bảng prefix Contract mục 0.2 🔴

**Contract đang ghi gì:** mục 0.2 chốt bảng prefix, mục 0.1 chốt số chữ số pad.

**Mock đang ghi gì:**

| Trong mock | Contract quy định | Ở file nào |
|---|---|---|
| `USR-khang` | `USR-001` — 3 chữ số | `mock-work-orders.json`, trường `assigned_to`, 2 chỗ |
| `CLU-0001` | `CLS-001` | `mock-work-orders.json`, `cluster_id` |
| `NODE-0020` … | `NODE-001` — 3 chữ số | `mock-iot-nodes.geojson`, `mock-pole-detail.json` |
| ~~`NODE-001-CTRL`~~ | Sai **cấu trúc** mục 0.1, không chỉ sai số chữ số | ~~`mock-segments.geojson`, `mock-iot-nodes.geojson`~~ — **đã sửa ở BE-09** |
| `FRM-88213` | `FRM-000001` — 6 chữ số | `mock-pole-detail.json`, `frame_id` |
| `SWEEP-2026…` | `SWP-001` | `mock-poles.geojson`, `last_sweep_id` |
| `SUP-004` | không có trong bảng prefix | `mock-pole-detail.json`, `supplier` |

**Code đang làm gì:** theo Contract. BE-06 sinh `USR-001`…`USR-004` bằng sequence PostgreSQL.

**Vì sao phải chốt sớm:** **BE-39 phải seed lại đúng bộ mock đó** để demo khớp với những gì FE đã
dựng. Với `assigned_to: "USR-khang"` thì BE-39 **không map được sang user nào**.

> ✅ **Đã xử lý một phần ở BE-09.** Hậu tố `-CTRL` sai cấu trúc mục 0.1 (`<PREFIX>-<số đã pad>` không
> có phần đuôi chữ) nên đã đổi thành `NODE-0001` / `NODE-0002` / `NODE-0003` ở **cả hai** file
> `mock-segments.geojson` và `mock-iot-nodes.geojson`. Giữ **4 chữ số** cho khớp với các node còn
> lại trong bộ mock — câu 3-hay-4 chữ số vẫn còn mở, và phải sửa **một lượt cho toàn bộ node ID**
> chứ không sửa lắt nhắt. Năm chỗ lệch còn lại chưa đụng tới.

**Đề xuất:** sửa **mock** cho khớp Contract, vì Contract là hợp đồng còn mock là dữ liệu minh hoạ.
Nhưng WP5/WP6 đang code theo mock nên phải báo trước — đây chính là việc mà `mocks/README.md` đã dặn:
*"Sửa file ở đây thì phải báo WP5 và WP6."*

Riêng `SUP-004`: cần quyết `supplier` là entity có ID hiển thị (thì phải thêm prefix vào mục 0.2)
hay chỉ là chuỗi tự do.

---

## 7. `page_size` vượt 200 bị kẹp im lặng 🟡

**Contract đang ghi gì:** mục 0 — *"`page_size` tối đa 200"*. Không nói xử lý thế nào khi vượt.

**Code đang làm gì:** kẹp im lặng về 200, trả HTTP 200 với `page_size: 200` trong response. Không
báo lỗi.

**Hệ quả cho FE:** phải đọc `page_size` **trong response** để tính số trang, không được giả định
bằng giá trị đã gửi lên. Gửi `page_size=500` mà tính trang theo 500 là sai.

**Đề xuất:** ghi rõ hành vi kẹp vào Contract mục 0.

---

## 8. Route không tồn tại trả 401 khi chưa đăng nhập 🟢

**Contract đang ghi gì:** không đề cập.

**Code đang làm gì:** BE-08 đặt mặc định toàn ứng dụng là phải xác thực, và ràng buộc đó áp cả cho
request tới route không tồn tại. Nên:

- chưa đăng nhập + route sai → **401 `UNAUTHENTICATED`**
- đã đăng nhập + route sai → **404 `NOT_FOUND`**

**Đây là chủ ý:** người lạ không dò được route nào có thật.

**Đề xuất:** ghi một dòng vào Contract để FE không nhầm 401 này là "token hỏng" khi thật ra là gõ
sai URL.

---

## 9. `CLAUDE.md` thiếu `data_source` trong khối enum 🟢

Khối *"Enum — khoá cứng, Contract mục 1"* trong `CLAUDE.md` liệt kê 12 enum nhưng **thiếu
`data_source`**, dù chính file đó mô tả kỹ `data_source` ở phần Nhánh C.

Contract mục 1 có đủ. Code theo Contract — đã hiện thực đủ cả `data_source`.

**Đề xuất:** thêm một dòng vào `CLAUDE.md`. Không ảnh hưởng code, nhưng người mới đọc `CLAUDE.md`
sẽ tưởng chỉ có 11 enum.

---

## 10. Contract mục 1 và mục 2.9 mâu thuẫn về `data_source` 🔴

**Mục 1 ghi gì:** *"`data_source` gắn trên `SurveySweep`, `SurveyFrame`, `Fault`,
`TelemetryReading`, `LuxReading`."* — **`Pole` và `Fixture` không có trong danh sách.**

**Mục 2.9 ghi gì:** *"Đăng ký bộ hiệu chuẩn FO-07 như một `RoadSegment` bình thường với các `Pole`
và `Fixture` tương ứng, **`data_source = calibration_rig`**"* — **yêu cầu `Pole` và `Fixture` phải
có trường này.**

Hai mục của cùng một tài liệu nói ngược nhau. `CLAUDE.md` chép lại danh sách của mục 1 nên cũng
thiếu (xem mục 9).

**Vì sao không thể tránh:** không có `data_source` ở tầng tài sản thì không cách nào tách cột hiệu
chuẩn khỏi cột thật khi thống kê — mà CV-11, CV-18, IOT-16 đều yêu cầu báo cáo tách riêng, và
`CLAUDE.md` gọi việc trộn ba nguồn dữ liệu là *"lỗi nghiêm trọng, không phải chi tiết trình bày"*.

**Đã chốt:** mục 2.9 thắng (mới hơn, cụ thể hơn). BE-09 thêm `data_source` vào **`pole`**,
**`fixture`** và **`road_segment`**. Không thêm vào `feeder` — mạch điện không mang nguồn dữ liệu.
Contract sẽ **lên v1.2** để bổ sung ba entity này vào danh sách mục 1.

**Cột lưu nhưng KHÔNG emit ra `properties`.** Bộ mock là nguồn chuẩn cho hình dạng response và không
có trường này, nên FE không phải sửa gì. Xử lý y hệt `road_segment.commune_id`: dùng để **lọc**,
không xuất hiện trong response. `GET /poles` mặc định loại `data_source = calibration_rig`; muốn xem
thì thêm query param tường minh.

---

## 11. `commune_id` trên 5 bảng Assets chưa có khoá ngoại 🟡 ✅ ĐÃ CHỐT

**Đã quyết và đã làm.** `AdministrativeUnit` chuyển từ `LuxMap.Modules.Identity` sang
**`LuxMap.Persistence`**, và cả 5 bảng khai FK thật.

**Vì sao `Persistence` chứ không phải `Shared`:** `Shared` đang là thư viện nhẹ (quy ước
serialization, contract, enum) và có chỗ dùng nó mà không nên biết gì về EF. Đặt một entity EF vào
đó thì hoặc `Shared` phải kéo theo EF Core, hoặc entity và cấu hình bị tách hai nơi — cả hai đều tệ
hơn hiện trạng. `Persistence` đã sở hữu chính cơ chế cần bảng này (`ICommuneScoped`,
`HasCommuneScope()`, chốt chặn khởi động), và **mọi module đều đã tham chiếu `Persistence`**.

`AdministrativeUnit` không phải khái niệm của Identity — nó là **mốc neo phạm vi** cho 15/16 entity
toàn hệ. Đây là di chuyển namespace, **không đổi schema**.

**FK khai thật, KHÔNG có navigation property:**

```csharp
builder.HasCommuneReference(pole => pole.CommuneId);
// → HasOne<AdministrativeUnit>().WithMany().HasForeignKey(...).OnDelete(Restrict)
```

Toàn vẹn ở tầng database mà không mở đường cho `pole.Commune.Name` rải khắp module — coupling giữa
module vẫn chỉ là chuỗi ID. `Restrict`, không bao giờ cascade một đơn vị hành chính.

**Chốt chặn mở rộng:** vế cũ hỏi *"entity implement `ICommuneScoped` đã gọi `HasCommuneScope()`
chưa"*. Vế mới quét **theo cột**: mọi cột tên `commune_id` không có FK tới `administrative_unit` thì
**chặn khởi động**. Quét cột chứ không quét interface — bịt luôn lỗ hổng mà XML doc của
`ICommuneScoped` tự thừa nhận. Đã kiểm bằng cách tạm gỡ FK khỏi `Pole`:

```
System.InvalidOperationException : Entities have a 'commune_id' column with no foreign key to
'administrative_unit': Pole. …
```

`AdministrativeUnit` **không** implement `ICommuneScoped` — lọc chính bảng neo bằng filter toàn cục
sẽ tạo vòng lặp ngữ nghĩa. Đã ghi vào `CLAUDE.md` để không ai "sửa cho nhất quán".

**Kèm theo — `seed_key` text NULL UNIQUE:** `IdentitySeeder` idempotent theo **tên**, mà tên chính là
thứ BE-39 sẽ sửa; upsert theo tên sẽ sinh commune **thứ hai** trong khi commune đầu vẫn giữ toàn bộ
cột. Giờ upsert theo **vai trò**: `study_site` và `calibration_site`. Không thêm `official_code` —
chưa chọn được địa bàn thì chưa có mã thật, thêm vào là bịa. Đã kiểm: đổi tên thành "Xa Tan Hiep" rồi
seed lại → tìm thấy theo `seed_key`, không sinh dòng thứ hai.

> BE-39 seed dòng `calibration_site`. Key đã đặt sẵn, chưa tạo dòng vì chưa biết xã nào.

---

## 12. Mock `POLE-0047` mâu thuẫn giữa ba file 🔴 — đã sửa

**Vấn đề:** cùng một cột, ba file nói ba điều khác nhau:

| File | Nói gì |
|---|---|
| `mock-poles.geojson` | `fixture_status = out`, `confidence 0.91`, `has_iot_node = false` |
| `mock-pole-detail.json` | `fixture_status = dim`, `confidence 0.81`, **có** `iot_node = NODE-0047` |
| `mock-iot-nodes.geojson` | **không tồn tại `NODE-0047`** |

`POLE-0047` chính là cột `mocks/README.md` chỉ định làm case demo runtime suy giảm — BE-39 sẽ không
seed nổi.

**Đã chốt và đã sửa:** pin yếu làm đèn **mờ dần**, không tắt phụt, nên `dim` là trạng thái đúng.

- `mock-poles.geojson` → `dim` / `0.81` / `has_iot_node = true`
- `mock-iot-nodes.geojson` → thêm `NODE-0047` (`sampled_fixture`, `SEG-002`, `pole_id: POLE-0047`)
- Phân bố đổi thành **70 `normal` · 10 `dim` · 16 `out` · 7 `unknown`**, số node thành **12**
- `mocks/README.md` đã cập nhật theo

Sửa kèm: `SEG-002` có `segment_name` ghi *"duong lien ap"* (liên ấp) nhưng `road_class` là
`inter_commune` (liên xã) — đổi **tên** cho khớp enum, vì `road_class` đang được FE dùng để lọc và
vẽ legend, sửa enum đắt hơn nhiều.

> ⚠️ **Contract mục 4 cũng mang những con số này** (*"70 normal / 9 dim / 17 out / 7 unknown"*,
> *"11 IoT node"*) và giờ đã cũ. Không sửa Contract ở BE-09 — đưa vào cùng lần lên **v1.2**.

**Bất biến vẫn giữ nguyên sau khi sửa:** `status_confidence` null đúng ở 7 cột `unknown`, không lệch
dòng nào. Bất biến này đã được khoá thành CHECK constraint ở BE-09.

---

## 13. `LPAD` cắt bớt ID khi vượt độ rộng — vi phạm Contract mục 0.3 🔴 ✅ ĐÃ SỬA

**Phát hiện khi làm BE-09, đã sửa trong cùng nhánh.** Lỗi nằm ở helper sinh ID của BE-06, không phải BE-09.

**Contract mục 0.3 ghi gì:**

> *"Khi vượt ngưỡng chữ số, ID dài ra tự nhiên — cột thứ 10000 là `POLE-10000`. Không có cắt bớt,
> không có tràn số."*

**Code đang làm gì:** `PrefixedIdSpec.DefaultValueSql` sinh ra
`'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')`.

**Vấn đề:** `lpad(string, length, fill)` của PostgreSQL **CẮT BỚT** khi chuỗi đã dài hơn `length`.
Nó không trả về chuỗi dài hơn như comment trong code đang khẳng định.

```
lpad('9999',  4, '0') = '9999'   → POLE-9999
lpad('10000', 4, '0') = '1000'   → POLE-1000   ← đụng cột thứ 1000
lpad('10005', 4, '0') = '1000'   → POLE-1000   ← và mọi giá trị 5 chữ số khác
```

Đo thật trên PostgreSQL 17 trong container BE-02:

| `nextval` | Hiện tại | `to_char(v,'FM0000')` | `LPAD(v, GREATEST(4, LENGTH(v)), '0')` |
|---|---|---|---|
| 1 | `POLE-0001` | `POLE-0001` | `POLE-0001` |
| 9999 | `POLE-9999` | `POLE-9999` | `POLE-9999` |
| 10000 | **`POLE-1000`** ❌ | `POLE-####` ❌ | `POLE-10000` ✅ |
| 123456 | **`POLE-1234`** ❌ | `POLE-####` ❌ | `POLE-123456` ✅ |

**Ngưỡng vỡ theo từng entity:**

| Độ rộng | Entity | Vỡ ở hàng thứ |
|---|---|---|
| 3 | `SEG` `FDR` `COM` `NODE` `SWP` `EXT` `USR` `CLS` | **1000** |
| 4 | `POLE` `FIX` `FAULT` `LUX` `WO` `EVD` | **10000** |
| 6 | `FRM` `DET` | **1000000** |

**`SurveyFrame` là chỗ dễ chạm nhất** — một đợt quét sinh vài trăm khung hình mỗi đêm, 1 triệu frame
không xa. `SEG` và `FDR` ở 1000 cũng không phải không thể với một dự án mở rộng địa bàn.

**Hư hại:** không ghi dữ liệu sai — lỗi là `23505 duplicate key`, insert hỏng hẳn. Nhưng trong
transaction thì **một statement lỗi abort cả transaction** (đúng cảnh báo ở `CLAUDE.md`), nên một đợt
import BE-12 sẽ hỏng trọn gói chứ không hỏng một dòng.

**Vì sao chưa lộ ra:** cả 4 bảng Identity của BE-06 đều chưa tới 1000 hàng. BE-09 chạm phải vì test
sinh 2500 pole tổng hợp trên một database dev đã có sequence chạy cao.

**Cách sửa — một biểu thức:**

```sql
LPAD(v::text, GREATEST(<digits>, LENGTH(v::text)), '0')
```

Cần bọc trong một function SQL nhỏ để `nextval` vẫn chỉ được gọi **đúng một lần** (biểu thức trên
tham chiếu `v` ba lần, và PostgreSQL không cho subquery trong `DEFAULT`):

```sql
CREATE FUNCTION luxmap_format_id(prefix text, value bigint, digits int)
RETURNS text LANGUAGE sql IMMUTABLE AS
$$ SELECT prefix || '-' || lpad(value::text, greatest(digits, length(value::text)), '0') $$;
```

`DEFAULT` thành `luxmap_format_id('POLE', nextval('pole_id_seq'), 4)`.

**Phạm vi sửa:** `PrefixedIdSpec.DefaultValueSql` (một dòng) + một migration đổi `DEFAULT` của **cả
16 cột ID**. Không đụng dữ liệu đã có — mọi ID hiện tại đều dưới ngưỡng nên không cần backfill.

### Đã sửa — migration `FixPrefixedIdOverflow`

`PrefixedIdSpec.DefaultValueSql` giờ sinh ra `luxmap_format_id('POLE', nextval('pole_id_seq'), 4)`.
Migration tạo function rồi đổi `DEFAULT` của **cả 6 cột ID đang tồn tại** (`COM` `USR` `SEG` `FDR`
`POLE` `FIX`); 10 entity chưa tạo sẽ dùng biểu thức mới ngay từ migration đầu của chúng. **Không
backfill dòng nào** — mọi ID hiện có đều dưới ngưỡng.

Kiểm chứng trên database dựng **thuần từ migration**, không vá tay:

```
setval('pole_id_seq', 9998); INSERT … ×4
  POLE-9999
  POLE-10000     ← trước đây là POLE-1000 → 23505
  POLE-10001
  POLE-10002
```

`PrefixedIdOverflowTests` không còn test nào bị `Skip`: chèn thật qua `DEFAULT` ở đúng ngưỡng vỡ,
kiểm cột thứ 30000 không đụng cột thứ 3000, và đối chiếu **cả 16 prefix** giữa `PrefixedIdSpec.Format`
của C# và function của database ở cả hai phía ngưỡng. `Format` dùng `PadLeft` nên phía C# vốn đã
đúng — chỉ phía SQL lệch.

> Comment cũ trong `PrefixedId.cs` khẳng định ngược lại sự thật — *"LPAD simply returns the longer
> number ... no truncation and no overflow"* — và chính nó làm người đọc tin là đã an toàn. Đã viết
> lại kèm lý do vì sao phải dùng function chứ không phải biểu thức thẳng.

---

## 14. ID không còn cố định độ dài — WP5 và WP6 phải sửa 🔴

**Hệ quả của bản sửa ở mục 13**, và là thứ duy nhất trong đó chạm tới FE và mobile.

Contract mục 0.3 vốn đã nói *"ID dài ra tự nhiên"*, nhưng trước đây database làm sai nên trên thực
tế mọi ID đều đúng N chữ số và không ai va phải. Giờ nó đúng như đặc tả — nghĩa là **`POLE-0001` và
`POLE-10000` cùng tồn tại**, độ dài khác nhau.

**Ba việc:**

| | Việc | Ai |
|---|---|---|
| 1 | **Không bao giờ `ORDER BY pole_id`.** So chuỗi thì `POLE-10000 < POLE-9999`. Sắp theo `created_at` hoặc theo sequence. Lọc theo khoảng trên text cũng sai từ cột thứ 10000 | BE — **kiểm lại khi làm BE-14** |
| 2 | Regex/validator ID phải nhận độ dài thay đổi: `^POLE-\d{4,}$`, **không phải** `\d{4}` | WP5, WP6 |
| 3 | Biết rằng định dạng ID là **tối thiểu N chữ số, không phải đúng N** | WP5, WP6 |

Việc 2 và 3 chỉ chạm tới nếu FE/mobile đang validate hoặc parse ID — mà Contract mục 0.3 vốn đã cấm
parse (*"Client không được phân tích cú pháp ID"*). Nên đây phần lớn là xác nhận lại, không phải
breaking change. Nhưng **cần báo Ngọc và Khang một dòng** để không ai viết `\d{4}` từ đây về sau.

Việc 1 là của backend và có thật: chưa có code nào sắp theo ID, nhưng BE-14 rất dễ viết vào.

---

## Việc cần quyết trong buổi FW-00

1. **Mục 1, 2, 3 phải chốt trước khi WP5/WP6 code sâu** — cả ba đều là thứ họ sẽ hardcode.
2. **Mục 6 (mock) phải chốt trước BE-39**, và ai sửa mock thì báo cho WP5/WP6.
3. Mục 4, 7, 8 chỉ cần ghi vào Contract cho rõ, không đổi code.
4. Mục 5 cần giao rõ phần tạo tài khoản cho BE-33.
5. ~~Mục 11~~ — **đã chốt và đã làm**, `AdministrativeUnit` sang `Persistence`, FK khai thật trên cả 5 bảng.
6. **Mục 10 và 12 gộp vào lần lên Contract v1.2**, rồi báo WP5/WP6 vì bộ mock đã đổi.
7. ~~Mục 13~~ — **đã sửa**, chỉ cần báo để cả nhóm biết `DEFAULT` của cột ID đã đổi dạng.
8. **Mục 14 phải báo WP5 và WP6** — một dòng thôi, nhưng phải nói trước khi ai đó hardcode `\d{4}`.
9. **Mục 15** — BE-11 thêm `UNSUPPORTED_IMAGE_FORMAT` (415). Mục 2 đang đếm 7 mã ngoài Contract, giờ là **8**. Chỉ cần ghi vào Contract, không đổi code.
10. **Mục 17 — CV-12 PHẢI biết trước.** `GET /lux-readings` trả `nearest_luminance` là `null` cho **mọi** bản ghi cho tới khi BE-15/BE-17 tạo `luminance_history`. Đây **KHÁC** với ngữ nghĩa Contract định nghĩa (*"null nếu không có điểm nào trong ±48 giờ"*): hiện tại null nghĩa là **bảng nguồn chưa tồn tại**, không phải "đã tìm và không thấy". CV-12 không phân biệt được hai ca này qua API. **Nợ có chủ: BE-15/BE-17** nối nguồn thật rồi xoá mục này.
11. **Mục 18** — mục 2.9 không có bảng `| Trường | Bắt buộc | Ghi chú |` như mục 2.8, nên `pole_id`, `meter_model`, `note`, `client_op_id` đều không rõ. BE-42 chốt `pole_id` và `client_op_id` **bắt buộc**, `meter_model` và `note` tuỳ chọn. Cần ghi vào Contract.
12. **Mục 19** — `tasks-backend.csv` yêu cầu lưu "người đo", Contract mục 2.9 không có trường nào. BE-42 lấy từ JWT theo đúng khuôn `reported_by` ở mục 2.8 dòng 283 (server áp cứng, client không set được), lưu FK tới `app_user`, **không emit ra response**. Nhiều khả năng Contract thiếu sót chứ không phải cố ý.
13. **Mục 20** — `GET /poles/{id}/lux-readings` được phân trang dù mục 2.9 chỉ nói "sắp theo `measured_at` tăng dần". Lý do: hiệu chuẩn đo gần hàng ngày, một cột rig sẽ tích hàng trăm điểm và response không giới hạn sẽ phình theo thời gian.
14. **Mục 21** — `SERVER_OWNED_FIELD` (400) khi client gửi `lux_id` hoặc `commune_id`. Mục 2 đang đếm 8 mã ngoài Contract, giờ là **9**.
15. **Mục 22** — `pole.external_ref` (`text NULL`, `UNIQUE (commune_id, external_ref) WHERE external_ref IS NOT NULL`). Lưu mã kiểm kê của đơn vị quản lý. Ba lý do: nối lại được giữa các đợt nhập; là **khoá tự nhiên duy nhất** khiến import idempotent (lược đồ hiện tại không có cái nào, nên nạp lại cùng file sinh trùng toàn bộ); và tổ sửa chữa ngoài hiện trường đọc mã sơn trên cột chứ không đọc `POLE-0042`. **Không emit ra API** — thêm vào response là đổi hình dạng đã publish. Migration thuộc **BE-12**.
16. **Mục 23 — chặn RQ2, cần quyết sớm.** `mock-poles.geojson` không có `feeder_id`, nên nạp bộ mock FO-26 cho ra **103 cột không gắn tuyến điện nào**. CV-15 gom cụm theo **mạch điện** (một cầu chì nhảy lan theo feeder, không lan theo đường), và BE-13 tồn tại để trả lời *"mọi cột trên feeder X"* — cả hai đều trả rỗng. Hai đường: bổ sung `feeder_id` vào mock (phải báo WP5/WP6 vì FE đã code theo bộ mock), hoặc gán thủ công sau khi nạp và **ghi lại cách gán** để kết quả gom cụm tái lập được. Cột `solar_all_in_one` đúng là không có feeder — đừng gán bừa cho đủ.
17. **Mục 24 — cột thứ hai cùng lỗi `NaN`, CHƯA sửa.** `pole_current_status.status_confidence` nhận `NaN`, `Infinity`, và **cả giá trị ngoài `0..1`** — đã chèn thật `42.5` và nó vào bảng. CHECK duy nhất trên cột đó chỉ ràng buộc NULL-hay-không so với `fixture_status`; XML doc ghi *"0..1"* mà **không có gì thực thi**. Quyền ghi bảng thuộc **BE-15/BE-17** nên bản sửa thuộc về đó. `fault.status_confidence` của BE-18 **không lặp lại lỗi này** — đã có CHECK đủ.
18. **Mục 25** — `POST /faults` cho `pole_id` null, nhưng `Fault` phải `ICommuneScoped` nên `commune_id` NOT NULL. `administrative_unit` **không có cột geometry** (cố ý — nhánh C không có ranh giới thật), nên không suy được từ `location`. BE-18 chốt: có pole thì tra từ pole; không có pole thì lấy từ scope JWT, user nhiều commune phải chọn. Cần ghi vào mục 2.8.
19. **Mục 26** — không tạo `FaultHistory`; ghi vết là các cột `reported_by`/`confirmed_by`+`at`/`resolved_by`+`at` trên chính bảng `fault`. Chỉ giữ quyết định **mới nhất**, không giữ chuỗi. **Nếu BE-19 cần đủ chuỗi thì đó là việc của BE-19.**
20. **Mục 27** — Contract liệt kê 6 `fault_status` và không nói cái nào là "mở", trong khi `open_fault_count` (mục 2.1), BE-28 và BE-40 đều cần. BE-18 chốt `detected | confirmed | in_progress`, đặt ở `FaultStatusSets.Open`. Cần ghi vào Contract.
21. **Mục 28 — `mock-faults.json` lệch mục 2.4 mười chỗ.** Thiếu `fixture_id`, `data_source`, `status_confidence`, `updated_at`, `work_order_id`, `note`, `reported_by`; thừa `confirmed_by`, `confirmed_at`; và `cluster_id` dùng **`CLU-0001`** trong khi mục 0.2 chốt **`CLS`**. Mục 6 mới ghi lỗi `CLU` ở `mock-work-orders.json`, **thiếu file này**. FE đã code theo mock — phải báo WP5/WP6.
22. **Mục 29 — vì sao KHÔNG dùng `/poles`.** Contract mục 2.1 đã đặc tả `GET /poles` là endpoint bản đồ: `bbox` bắt buộc, `FeatureCollection`, 413 quá 2000 cột. Đó là của **BE-14**. Danh sách kiểm kê trả lời cùng đường dẫn sẽ chiếm mất chỗ, nên BE-12a đi đường riêng `/assets/…`. Cần một mục Contract mới, **đừng gộp vào 2.1**.
23. **Mục 30 — vì sao 200 chứ không phải 4xx.** Kiểm toàn bộ file trước rồi ghi tập hợp lệ trong một transaction, nên "10 hỏng trong 500, 490 đã ghi" là **thành công một phần thật**, không phải lỗi của request; bọc trong `{error:…}` là nói sai về 490 dòng kia. Tiền lệ có sẵn: `POST /lux-readings` trùng `client_op_id` trả **200** và mục 5.8 gọi đó là hành vi bình thường. **207 đã cân nhắc và loại** — không có trong Contract, không có trong repo. `rows[]` là **mảng** vì dictionary không cam kết thứ tự và khoá số dạng chuỗi cho `"10"` đứng trước `"9"`; cắt ở 100, `total_errors` vẫn đủ.
24. **Mục 31 — chỗ hở nghiêm trọng nhất của nhóm này.** Mục 7 có bảng phạm vi địa bàn của 4 vai trò và 5 quy tắc lọc, **không một chữ nào** về vai trò nào được ghi. BE-12a chốt: **Quản trị** ghi, ba vai trò kia chỉ đọc. Đây là lần đầu 4 policy của BE-08 chạm dòng sản xuất, nên nó **tạo tiền lệ** cho BE-15, BE-18, BE-21, BE-24. Phải chốt cả nhóm ở FW-00, đừng để mỗi ticket tự quyết. ⚠️ Kèm một cái bẫy phải ghi vào Contract luôn: **policy là MỘT vai trò chính xác, không phải một bậc** — gắn `maintenance_engineer` lên endpoint đọc sẽ chặn luôn Quản trị và Cơ quan quản lý.
25. **Mục 32** — mục 22 trước đây chỉ đăng ký `pole.external_ref`. BE-12a mở lên ba bảng, vì `poles.csv` phải trỏ tuyến và mạch điện bằng mã của đơn vị quản lý: `SEG-001` do DB sinh lúc INSERT nên người soạn file không biết trước, và nếu template đòi mã đó thì bộ bốn file không nạp được liền mạch. `fixture` cố ý KHÔNG có — một cột mang nhiều bóng, không mã nào chỉ đúng một lần lắp đặt, nên nhập bóng là insert-only.
26. **Mục 33** — `ASSET_NOT_FOUND` (404, gộp cả "không tồn tại" lẫn "ngoài phạm vi" đúng như mục 7 đòi) và `EXTERNAL_REF_TAKEN` (409). Mục 2 nay đếm **11** mã ngoài Contract.
27. **Mục 34 — có hạn dùng.** `GET /assets/{segments,feeders,poles}` trả `PagedResult<string>` chỉ gồm ID. Đó là **chỗ giữ chỗ**, không phải thiết kế: BE-12a sở hữu request và phân quyền, còn hình dạng khi đọc là **BE-12b** đang chờ Thịnh/Ngọc. Công bố một hình dạng đoán bây giờ thì FE sẽ bám vào, và gỡ ra khó hơn nhiều so với công bố muộn.
28. **Mục 16 — ràng buộc phiên bản, KHÔNG phải chi tiết triển khai.** ImageSharp bị ghim ở 3.x vì từ 4.x task validate lúc build đòi `SixLaborsLicenseKey`, và `ContinueOnError` chỉ bật ở Debug → **mọi build Release, CI và deploy sẽ GÃY**. Điều khoản Split License không đổi, chỉ khác cái cổng kiểm key. Ai nâng cấp phải **xin key TRƯỚC**, không phải sau khi thấy build đỏ.

Sau khi chốt, Contract tăng version và **cập nhật lại `docs/openapi/luxmap-v1.json`** bằng lệnh
export ở `README.md` để WP6 sinh lại DTO.
