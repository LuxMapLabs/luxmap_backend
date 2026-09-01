# Chỗ lệch giữa code và Contract — nội dung mang vào FW-00

**Mục đích:** danh sách những chỗ code hiện tại và `api-contract-v1.1.md` / `tasks-backend.csv`
không khớp nhau, để cả nhóm quyết trong buổi review FW-00.

**Đây là tài liệu ĐỀ XUẤT, không phải quyết định.** Không sửa Contract, không sửa task list dựa
trên file này — mọi thay đổi phải được cả BE và FE duyệt rồi tăng version, đúng nguyên tắc ở đầu
Contract: *"Muốn đổi field/enum → mở issue, cả BE và FE cùng duyệt, tăng version. Không đổi ngầm."*

**Ai cần đọc:** WP5 (Web) và WP6 (Android) — nhiều mục dưới đây các bạn sẽ gặp ngay khi tích hợp.

**Trạng thái code:** BE-00 → BE-08 xong, 202 test xanh.

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
| `FRM-88213` | `FRM-000001` — 6 chữ số | `mock-pole-detail.json`, `frame_id` |
| `SWEEP-2026…` | `SWP-001` | `mock-poles.geojson`, `last_sweep_id` |
| `SUP-004` | không có trong bảng prefix | `mock-pole-detail.json`, `supplier` |

**Code đang làm gì:** theo Contract. BE-06 sinh `USR-001`…`USR-004` bằng sequence PostgreSQL.

**Vì sao phải chốt sớm:** **BE-39 phải seed lại đúng bộ mock đó** để demo khớp với những gì FE đã
dựng. Với `assigned_to: "USR-khang"` thì BE-39 **không map được sang user nào**.

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

## Việc cần quyết trong buổi FW-00

1. **Mục 1, 2, 3 phải chốt trước khi WP5/WP6 code sâu** — cả ba đều là thứ họ sẽ hardcode.
2. **Mục 6 (mock) phải chốt trước BE-39**, và ai sửa mock thì báo cho WP5/WP6.
3. Mục 4, 7, 8 chỉ cần ghi vào Contract cho rõ, không đổi code.
4. Mục 5 cần giao rõ phần tạo tài khoản cho BE-33.

Sau khi chốt, Contract tăng version và **cập nhật lại `docs/openapi/luxmap-v1.json`** bằng lệnh
export ở `README.md` để WP6 sinh lại DTO.
