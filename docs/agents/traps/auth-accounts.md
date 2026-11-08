# Xác thực, tài khoản, phân quyền

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi auth, tài khoản, mời qua email, giới hạn đăng nhập, `[AllowAnonymous]`. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## `[AllowAnonymous]` cấp CLASS thắng `[Authorize]` cấp METHOD (19/09/2026)

Thêm một endpoint **cần token** vào một controller đang mang `[AllowAnonymous]` ở cấp class thì gắn
`[Authorize]` lên method **không cứu được**: ASP.NET Core cho opt-out ở xa hơn thắng. Compiler nói
thẳng điều đó qua **ASP0026**, và repo này để 0 warning nên nó nhìn thấy được, nhưng warning không
phải error.

Đã suýt phát sinh thật ở `GET /auth/me`: `AuthController` mang `[AllowAnonymous]` cấp class từ BE-07,
nên endpoint mới sẽ **mở cho người chưa đăng nhập**. Cách sửa đã áp: chuyển `[AllowAnonymous]` xuống
**từng** method cấp token (`login`, `refresh`, `logout`; không còn `register`), để mặc-định-đóng của BE-08 áp
cho mọi endpoint viết sau.

> 🔴 **Test theo HÀNH VI không bắt được cái này.** Với `[AllowAnonymous]` cấp class, request không
> token vào `/auth/me` **vẫn trả 401** — vì action chạy, không thấy claim `sub`, rồi tự ném. Cùng
> status, khác hoàn toàn lý do: endpoint đã **tới được**, và endpoint kế tiếp ai đó thêm vào
> controller sẽ thừa hưởng opt-out mà không ai đọc lại. Thứ bắt được là **assert trên metadata của
> route**: `endpoint.Metadata.GetMetadata<IAllowAnonymous>()` phải `null`
> (`CurrentUserTests.It_is_not_declared_anonymous_even_though_its_four_siblings_are`). Kiểm bằng phá
> hoại: trả `[AllowAnonymous]` về cấp class thì test hành vi **vẫn xanh**, test metadata **đỏ**.

**Mỗi tài khoản superior / manager / field_engineer thuộc đúng một xã** (ACCT-1, Contract v1.22 §2; thay D-R3). `system_admin` không gán xã, claim `["*"]`. `commune_ids` vẫn là mảng; kiểm số phần tử trước khi gộp trùng, UNIQUE `user_id` là chốt DB. Quyền vẫn theo capability + địa bàn.

## BE-33a — tài khoản do Quản trị tạo, mời qua email (05/10/2026)

**Phạm vi `*` ⇔ `system_admin` nay là CHECK của DB** (`ck_app_user_system_wide_scope_matches_role`). Test muốn chứng
minh lớp kiểm ở tầng phân quyền (`CommuneScopeConsistency`) phải **ký thẳng một JWT** lệch vai trò bằng
`AccessTokenIssuer`, không `UPDATE` cờ trên tài khoản (DB từ chối, và tài khoản seed là của chung). DB dev cũ có cặn
vi phạm thì chạy `scripts/cleanup_be12_leftover_accounts.sql` **trước** migration `AdminCreatedAccounts`.

**`password_hash` NULL ⇔ `password_set_at` NULL** (`ck_app_user_password_set_together`; NULL = đã mời, chưa đặt mật
khẩu). Mọi chỗ tạo `AppUser` có mật khẩu — seeder, fixture, test — phải đặt **cả hai**. Đăng nhập tài khoản chưa đặt
mật khẩu trả đúng `401 INVALID_CREDENTIALS` như sai mật khẩu.

**`user_id` rút từ sequence TRƯỚC khi chèn** (`UserAdminService.CreateAsync`, khuôn `FaultReportService.NextFaultId`),
để danh sách xã và link mời gắn mã thật trong cùng một `SaveChanges`. Khoá có tiền tố sinh bằng `DEFAULT` của DB, EF
không có giá trị tạm cho nó.

**Cấu hình mail bắt buộc lúc khởi động** (`SMTP_HOST`, `SMTP_PORT`, `SMTP_FROM_ADDRESS`, `SMTP_FROM_NAME`,
`WEB_APP_BASE_URL`; `SMTP_USERNAME`/`SMTP_PASSWORD` đi cùng nhau). Dev trỏ Mailpit (`docker compose up -d mailpit`,
hộp thư ở `http://localhost:8025`); CI ghi các biến này vào `.env` tạm; test thay `IEmailSender` bằng
`RecordingEmailSender`. Có login thì **luôn TLS** (465 SSL, còn lại STARTTLS bắt buộc), không bao giờ "STARTTLS nếu có".
**Không log link hay token thô** — chỉ băm SHA-256 nằm trong `account_token`.

**`account_token` cascade từ `app_user`** — ngoại lệ có chủ đích của luật "mọi FK `Restrict`": nó là thông tin đăng
nhập tạm như `refresh_token`, không thuộc xã nào nên vùng mù cascade của guard `SaveChanges` không áp. Nó nằm trong
`SKIPPED` của `copy_dev_to_supabase.py` (link gắn với `WEB_APP_BASE_URL` của nguồn).

**Kiểm tra thu hồi chứng chỉ SMTP giữ mặc định CHẶT của MailKit — không soft-fail, không tắt (05/10/2026).** Trên
macOS có thể gặp `RevocationStatusUnknown` khi kiểm chứng chỉ SMTP. Theo quyết định đã ghi: **Mac dùng Mailpit, Gmail chạy từ Linux**.
Không soft-fail: kẻ giữ khoá chứng chỉ đã thu hồi và chặn tải CRL có thể được cho qua, lộ mật khẩu ứng dụng/link đặt mật khẩu.
Đừng thêm `CheckCertificateRevocation = false` hay callback nới lỏng "cho chạy được".
Nếu máy chủ Linux đích cũng lỗi thì mới xét lại, và callback phải: chỉ dung thứ `RevocationStatusUnknown` /
`OfflineRevocation`, đòi `SslPolicyErrors` **bằng đúng** `RemoteCertificateChainErrors` (sai tên miền luôn bị từ chối),
kèm test cho từng cờ.

**Bộ giới hạn tần suất chia theo `RemoteIpAddress`.** Sau reverse proxy mọi client chung một địa chỉ (của proxy) ⇒
chung một xô 5 lần / 15 phút. Deploy sau proxy phải cấu hình forwarded headers **trước**.

**Đăng nhập có giới hạn tần suất (LOGIN-RATE, drift LG-1…LG-3), và mọi test đăng nhập từ cùng một địa chỉ.** `LuxMapTestHost`
nâng `RateLimits:Login:PermitLimit` lên 1.000.000 trong `CreateHost`; giá trị đặt ở hook đó **thắng `UseSetting`**, nên test muốn hạ
giới hạn phải override `LoginPermitLimit` (khuôn `LoginRateLimitTestFactory`), không `UseSetting` — làm vậy test vẫn chạy, chỉ không
bao giờ thấy 429. Endpoint đăng nhập mới phải gắn **cùng** policy `LuxMapRateLimits.Login`: policy riêng là xô riêng, kẻ dò mật khẩu
xen kẽ hai đường để được gấp đôi số lần.

Header lỗi theo `CLAUDE.md`, mục Bẫy chung; 429 tự ghi body bằng `RateLimitSetup.OnRejected` để giữ `Retry-After`.

**`FakeTimeProvider` không lùi được** (`Advance` âm ném ngoại lệ). Đẩy đồng hồ host lên rồi không lùi làm **mọi access
token cấp sau đó chưa tới hạn hiệu lực** → 401 hàng loạt ở các test sau, trông như lỗi phân quyền. Muốn test hết hạn
thì kiểm thời hạn đã cấp rồi dời `expires_at` của chính bản ghi đó về quá khứ (`AccountAdminTests.ExpireAsync`).

**Test luật "Quản trị cuối cùng" chạy trong transaction rồi rollback** (khoá các admin khác chỉ trong transaction đó).
Đổi trạng thái admin thật trên DB dùng chung sẽ đua với các class test khác đang tạo admin (`WorkOrderTests`).

## ACCT-USERNAME — khoá cấp hậu tố theo gốc (08/11/2026)

Tạo qua API không nhận username, kể cả null: giữ field `JsonElement` để `OptionalJson.Present` thấy key có mặt.
`UsernameGenerator.BaseName` là định nghĩa duy nhất của gốc. Lấy hậu tố max theo regex chính xác (không đếm hàng,
không lấy max chuỗi), so không phân biệt hoa thường; `mynh7` → `mynh8`, `MYNH3` cũng được tính.
`UserAdminService.CreateAsync` giữ `pg_advisory_xact_lock(hashtextextended("account-username:" + gốc, 0))`
trong transaction READ COMMITTED bao cả đọc max và SaveAsync/commit. Khoá theo gốc hoạt động cả khi chưa có hàng;
không thay bằng khoá trên hàng chưa tồn tại. Hash collision chỉ tuần tự hoá thêm, không gây trùng username.
Commit trước SMTP. Unique index vẫn là chốt cuối; không bỏ SaveAsync xử lý unique/email race.
Test API lấy username từ response, teardown dùng email có prefix riêng (username do server sinh).

## ACCT-3 — tra đăng nhập theo username rồi email (08/11/2026)

Field JSON vẫn là `username`; hai endpoint dùng chung `AuthService`. Username cũ có thể chứa `@` và trùng email
của người khác: luôn ưu tiên username, không thử email sau khi sai mật khẩu. Không cần xử lý "email khớp nhiều
hàng": `ix_app_user_email_lower` (và `ix_app_user_username_lower`) là UNIQUE trên `lower(...)`, đừng viết lại nhánh đó
dựa trên giả định index phân biệt hoa thường. Log đăng nhập không ghi chuỗi người dùng gõ (có thể là email). Nền API: ACCT-3 SELF-SIGNED.

## ACCT-4 — Google không phải đăng ký hay kích hoạt lời mời (08/11/2026)

Google chỉ chứng minh email: giữ kiểm `EmailVerified`, chỉ tra email vào tài khoản có sẵn, không tạo tài khoản,
không đổi tên/email và không cho `PasswordHash == null` đi qua. ClientIds rỗng phải trả 401 **trước** khi gọi verifier.
Không log ID token, email, Google `sub` hoặc exception của thư viện (có thể chứa credential).
Test thay `IGoogleIdTokenVerifier` qua DI, không gọi Google. Google và mật khẩu dùng chung hàm mở refresh chain;
chỉ thành công mới thu hồi cookie cũ. Web Google có body chỉ `id_token`, dùng `web_session` (không `remember_me`).
Nền API ACCT-4 SELF-SIGNED; ba giới hạn (liên kết sub, nonce, bật/tắt từng tài khoản) nằm ở drift.
