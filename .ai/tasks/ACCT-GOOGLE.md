---
ticket: ACCT-GOOGLE
title: Đăng nhập bằng Google — POST /auth/google và /auth/web/google (ID token → khớp tài khoản có sẵn theo email)
status: ready
phase: 2
owner: codex
branch: feat/ACCT-google-login
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: ACCT-4 (kiểm docs/contract-drift.md; ACCT-2 và ACCT-3 đã có)"
depends_on:
  - "ACCT-LOGIN-EMAIL (ACCT-3) đã merge — dùng lại tra theo email của AuthService"
---

## Bối cảnh

Mỹ chốt (08/11/2026): cho người dùng đăng nhập bằng "Sign in with Google". **Không tự đăng ký** (D-R11 giữ nguyên): Google
chỉ chứng minh người đó sở hữu một email; email phải khớp một tài khoản **đã do Quản trị tạo**. Không khớp → từ chối.

FE lấy **ID token** từ Google (Google Identity Services, nút Sign in with Google) và POST lên API; backend xác minh rồi cấp
token của hệ thống y như đăng nhập mật khẩu (cùng refresh chain). Backend KHÔNG dùng client secret, KHÔNG làm luồng
redirect / authorization code.

## Quyết định đã chốt (Mỹ, 08/11/2026) — không phải D-item

- **Hai endpoint**, đối xứng với login hiện có:
  - `POST /api/v1/auth/google` (mobile surface) → `AuthTokenResponse`, refresh token trong body như `/auth/login`.
  - `POST /api/v1/auth/web/google` (web) → `WebAuthTokenResponse` + cookie refresh như `/auth/web/login`.
  - Body: `{ "id_token": "<JWT của Google>" }`, `[Required]`, `[MaxLength(8192)]`. Lớp body là `sealed class` + `init` (luật CLAUDE.md).
  - Mỗi action gắn **`[AllowAnonymous]` riêng**, `[EnableRateLimiting(LuxMapRateLimits.Login)]`, `ClientSurface`, `RequireAllowedOrigin` (web) y hệt login. Mọi `[ProducesResponseType]` 200/400/401/403/429. Đọc `AuthController` / `WebAuthController` và test metadata `[AllowAnonymous]` của repo (docs/agents/traps/auth-accounts.md) — thêm action mới vào danh sách mà test đó canh nếu có.
- **Cổng (port) `IGoogleIdTokenVerifier`** trong module Identity: `Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct)`; `GoogleIdentity(string Subject, string Email, bool EmailVerified)`. Hiện thực thật bọc **`Google.Apis.Auth`** `GoogleJsonWebSignature.ValidateAsync(idToken, new ValidationSettings { Audience = clientIds })` (kiểm chữ ký, `iss`, `aud`, `exp`); lỗi xác minh → trả `null`, KHÔNG ném (và không log token). Test thay cổng này bằng bản giả qua DI — không test nào gọi mạng.
- **Gói NuGet `Google.Apis.Auth` đã được Claude thêm sẵn vào `LuxMap.Modules.Identity.csproj` và restore** (sandbox của bạn không có mạng). KHÔNG sửa dòng PackageReference; nếu build đòi gói khác thì dừng và ghi vào results.
- **Cấu hình:** options `GoogleLoginOptions` bind từ `Google:ClientIds` (danh sách, hỗ trợ nhiều ID: web + Android); biến môi trường `GOOGLE_CLIENT_IDS` là CSV → map vào `Google__ClientIds__N` theo cách repo đang làm cho `WebAuth` / `Cors` (đọc `WebAuthOptions.cs`, `.env.example`, `deploy/.env.example`, `deploy/compose.yml`, `docker-compose.yml`). **Rỗng = tính năng tắt:** hai endpoint luôn trả `401 INVALID_CREDENTIALS` (đúng mã / thông điệp đăng nhập sai) và KHÔNG gọi cổng xác minh; log một dòng Information lúc khởi động nói Google login đang tắt. Phần tử rỗng / có khoảng trắng bị bỏ; không cần `Required` (khác `WebAuth`, vì môi trường dev và CI không có Client ID).
- **Khớp tài khoản:** chỉ khi `EmailVerified == true`; tra bằng cùng hàm tra email của `AuthService` (ACCT-3, không phân biệt hoa thường — `ix_app_user_email_lower` là UNIQUE nên tối đa một). **Không bao giờ tạo tài khoản**, không đổi email / tên của tài khoản có sẵn.
- **Trạng thái tài khoản:**
  - đang mời (chưa đặt mật khẩu, `PasswordHash == null`) → **`401 INVALID_CREDENTIALS`** (v1 không cho Google kích hoạt tài khoản; người dùng phải đặt mật khẩu qua link mời trước);
  - khoá → **`403 ACCOUNT_LOCKED`** (khác đăng nhập mật khẩu: danh tính đã được Google chứng minh nên không lộ gì thêm);
  - email không khớp / chưa xác minh / token sai → **`401 INVALID_CREDENTIALS`**, cùng mã và thông điệp với đăng nhập sai (không phân biệt được).
- **Dùng chung phần cấp token:** tách phần đuôi của `AuthService.LoginAsync` (mở refresh chain, thu hồi phiên web cũ khi `supersededWebRefreshToken`, `BuildTokensAsync`) thành một hàm private dùng cho cả mật khẩu và Google; hành vi của đăng nhập mật khẩu **không đổi** (toàn bộ test đăng nhập hiện có phải xanh, không sửa chúng).
- **Ghi log:** `UserId` và `method=google` ở mức Information khi thành công; thất bại chỉ log lý do ở dạng chung (không email, không token, không `sub`).
- **Giới hạn đã biết (ghi vào drift, không xử lý ở v1):** (1) khớp theo email chứ chưa ràng `sub` của Google vào tài khoản — cần bảng liên kết = migration, để sau; (2) không kiểm `nonce` nên ID token bị lộ còn hiệu lực tới `exp` (~1 giờ) — nonce là việc của FE + một ticket sau; (3) quyền "đăng nhập bằng Google" không tắt riêng được từng tài khoản.
- Không migration, không đổi schema, không đổi CORS, không thêm vai trò / capability (endpoint anonymous như login).

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/auth-accounts.md` + `docs/agents/traps/testing.md`
- `src/LuxMap.Modules.Identity/Auth/{AuthService,AuthController,AuthContracts,AuthResults,AuthFailureErrors,WebAuthOptions}.cs`, `Auth/Web/{WebAuthController,WebAuthContracts}.cs`, `IdentityModule` (nơi đăng ký DI)
- `tests/LuxMap.Api.Tests/{AuthTestFactory,EmailLoginTests,AuthEndpointTests,WebAuthTests}.cs` (cách dựng host test, thay dịch vụ qua DI, giả mật khẩu / tài khoản mời / khoá)
- `docs/contract-drift.md` mục ACCT-3

## Yêu cầu

1. `IGoogleIdTokenVerifier` + hiện thực thật + `GoogleLoginOptions` + đăng ký DI. Hiện thực thật tách đủ mỏng để không cần mạng khi test.
2. `AuthService.GoogleSignInAsync(idToken, kind, supersededWebRefreshToken, ct)` → `AuthResult` (dùng chung đuôi cấp token), hai action controller.
3. Test (assert giá trị cụ thể, **cả mobile lẫn web** cho các ca (a)(b)(d)(e)):
   - (a) token hợp lệ + email khớp tài khoản đang hoạt động → 200, access token có `sub` = `user_id` đúng; web có `Set-Cookie` refresh, mobile có refresh trong body;
   - (b) email khớp nhưng viết hoa / khác kiểu chữ so với email lưu → 200 đúng tài khoản;
   - (c) `EmailVerified = false` → 401 `INVALID_CREDENTIALS`; verifier trả `null` → 401; email không khớp tài khoản nào → 401; **không có hàng `app_user` nào được thêm**;
   - (d) tài khoản đang mời → 401; tài khoản khoá → 403 `ACCOUNT_LOCKED`;
   - (e) thông điệp / mã của mọi nhánh 401 **giống hệt** đăng nhập mật khẩu sai;
   - (f) thiếu / rỗng `id_token` → 400; vượt 8192 ký tự → 400;
   - (g) `Google:ClientIds` rỗng → 401 và cổng xác minh **không được gọi** (đếm lời gọi của bản giả);
   - (h) đăng nhập mật khẩu không đổi (chạy lại `AuthEndpointTests`, `WebAuthTests`, `EmailLoginTests`, `AuthRotationTests` xanh — không sửa chúng);
   - (i) tham số `supersededWebRefreshToken` của web: đăng nhập Google khi đang có cookie cũ thu hồi phiên cũ giống đăng nhập mật khẩu (nếu test mật khẩu có ca này thì lặp lại một ca tương đương cho Google);
   - (j) test cấu hình: `GOOGLE_CLIENT_IDS="a, b,,c "` → ba ID `a`,`b`,`c`.
4. **Phá thử** (viết vào results là "chưa chạy, Claude sẽ chạy", kèm đúng dòng): (a) bỏ kiểm `EmailVerified` → test (c) đỏ; (b) bỏ kiểm tài khoản đang mời → (d) đỏ; (c) bỏ nhánh "ClientIds rỗng" → (g) đỏ; (d) cho phép tạo tài khoản khi không khớp → (c) đỏ (nhờ assert số hàng).
5. Tài liệu: `docs/contract-drift.md` dòng ACCT-4 (SELF-SIGNED 08/11/2026) gồm quyết định trên và ba giới hạn đã biết; `docs/agents/traps/auth-accounts.md` bẫy ngắn (không tạo tài khoản, không log token, đừng gỡ kiểm `email_verified`); `.env.example` + `deploy/.env.example` + `deploy/compose.yml` / `docker-compose.yml` biến `GOOGLE_CLIENT_IDS` (để trống, chú thích); **`docs/deploy/google-login.md`** mới, ngắn: việc phải làm ở Google Cloud Console (consent screen loại External và trạng thái In production, Web client với Authorized JavaScript origins = `https://<LUXMAP_DOMAIN>` và `http://localhost:5173`, Android client cần package name + SHA-1, scope chỉ `openid email profile`) và cách điền `GOOGLE_CLIENT_IDS` trên VPS. `tracking.html` thêm thẻ. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*` (Claude sinh lại).
6. Ghi `.ai/results/ACCT-GOOGLE.md` ngắn: đã làm gì, lệnh Claude cần chạy, điều chưa chắc. Không dán số test "đã xanh" nếu bạn không chạy được.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới (Claude xác minh)
- [ ] Test mục 3 có mặt; Claude chạy `dotnet test` xanh
- [ ] Không còn gọi mạng nào trong test; không có token / email thật trong repo

## KHÔNG ĐƯỢC làm

- Không tạo tài khoản, không liên kết `sub`, không migration, không đổi schema.
- Không sửa dòng `PackageReference`, không thêm gói khác.
- Không ghi `id_token`, email, `sub` vào log hay vào thông báo lỗi.
- Không đổi hành vi, mã lỗi, thông điệp của `POST /auth/login`, `/auth/web/login`, refresh, logout; không sửa test của chúng.
- Không làm luồng redirect / authorization code / client secret.
- Không tự cho tài khoản đang mời đi qua Google; không thêm capability hay vai trò.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json`.
- Không `git add` / commit / push (sandbox chặn git; Claude làm).
- Thấy chỗ mơ hồ thì ghi vào results như câu hỏi, đừng tự chọn kiến trúc.
