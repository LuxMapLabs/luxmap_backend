---
ticket: ACCT-LOGIN-EMAIL
title: POST /auth/login và /auth/web/login — nhận username HOẶC email ở field `username`
status: ready
phase: 2
owner: codex
branch: feat/ACCT-login-by-email
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: ACCT-3 (kiểm docs/contract-drift.md trước; ACCT-2 thuộc PR ACCT-USERNAME đang mở — KHÔNG dùng số 2, KHÔNG sửa dòng của nó nếu nó đã có trong tree của bạn)"
depends_on: []
---

## Bối cảnh

Username sẽ được server tự sinh (`mynh1`, ticket ACCT-USERNAME) nên người dùng khó nhớ. Mỹ chốt (08/11/2026): cho đăng nhập
bằng **email hoặc username**. Hai endpoint đăng nhập (mobile `POST /auth/login`, web `POST /auth/web/login`) cùng gọi
`AuthService.LoginAsync`; thay đổi nằm ở đó. Đây cũng là nền cho ACCT-4 (đăng nhập Google khớp theo email).

## Quyết định đã chốt (Mỹ, 08/11/2026) — không phải D-item

- **Không đổi tên field**: body vẫn là `{ "username": "...", "password": "..." }` để FE web / mobile đang chạy không gãy. Field `username` nhận username **hoặc** email. Chỉ đổi XML-doc / mô tả.
- Thứ tự tra: **username trước**, không tìm thấy mới tra **email**; cả hai so **không phân biệt hoa thường**, sau `Trim()`. Lý do thứ tự: username tài khoản cũ do client nhập tự do (không cấm `@`), về lý thuyết có thể trùng email người khác; username sinh mới chỉ gồm `a-z0-9` nên không bao giờ có `@`.
- **Đính chính (Claude, 08/11/2026):** ticket ban đầu giả định unique index của `email` phân biệt hoa thường nên cần "fail closed khi nhiều hàng". Sai: `ix_app_user_email_lower` là UNIQUE trên `lower(email)` (migration `AddCaseInsensitiveIdentityIndexes`), nên không có ca mơ hồ; nhánh đó và test (g) đã bỏ.
- Mọi thứ sau bước tra GIỮ NGUYÊN: tài khoản đang mời (chưa có mật khẩu) thất bại giống sai mật khẩu; khoá kiểm SAU khi xác minh mật khẩu; một lỗi chung `InvalidCredentials`; rate limit `LuxMapRateLimits.Login` hiện có; một chuỗi refresh mới mỗi lần đăng nhập. Không thêm mã lỗi mới, không để lộ tài khoản có tồn tại hay không (kể cả log mức Warning hiện tại — không in chuỗi người dùng gõ).
- Không migration, không thêm index (bảng nhỏ; truy vấn `ToLower()` giống hiện tại).
- Đổi email (`PATCH /admin/users/{id}`) có hiệu lực ngay cho đăng nhập: email cũ ngừng dùng được, email mới dùng được. Chỉ cần test, không đổi code PATCH.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/auth-accounts.md` + `docs/agents/traps/testing.md`
- `src/LuxMap.Modules.Identity/Auth/{AuthService,AuthController,AuthContracts}.cs`, `Auth/Web/WebAuthController.cs`
- `src/LuxMap.Modules.Identity/Accounts/UserAdminService.cs` (`RequireFreeAsync`, `UpdateAsync` đổi email)
- `tests/LuxMap.Api.Tests/AuthEndpointTests.cs`, `AuthenticationTests.cs`, `WebAuthTests.cs`, `AccountTestFactory.cs`, `AccountAdminTests.cs`

## Yêu cầu

1. `AuthService.LoginAsync`: tra username → rồi email như trên; một hàm riêng gọn (vd `FindForSignInAsync`) để ACCT-4 dùng lại cho tra theo email. Đổi tên tham số `username` → `identifier` ở service (không đổi DTO / JSON).
2. XML-doc `LoginRequest.Username`, hai action login (mobile và web) và `<remarks>`: "username hoặc email".
3. Test (assert giá trị cụ thể, trên cả đường mobile `/auth/login` lẫn web `/auth/web/login` — ít nhất các ca (a)(b)(e) cho cả hai):
   - (a) đăng nhập bằng email đúng mật khẩu → 200, access token có `sub` đúng tài khoản;
   - (b) email viết HOA / có khoảng trắng đầu cuối → 200;
   - (c) email đúng, mật khẩu sai → 401 `INVALID_CREDENTIALS` y hệt (cùng mã, cùng thông điệp) trường hợp username sai mật khẩu và trường hợp email lạ;
   - (d) tài khoản đang mời (chưa đặt mật khẩu) đăng nhập bằng email → 401 giống sai mật khẩu; tài khoản khoá + mật khẩu đúng bằng email → 403 `ACCOUNT_LOCKED` (mã hiện có — đọc `AuthFailureErrors.cs`), mật khẩu sai → 401 (không lộ);
   - (e) username vẫn đăng nhập được như cũ (hồi quy);
   - (f) ưu tiên username: tài khoản A có username `b@x.test` (chèn trực tiếp qua DbContext, vì API không còn cho đặt), tài khoản B có email `b@x.test`; mật khẩu của A → đăng nhập ra A; mật khẩu của B với `b@x.test` → 401;
   - (h) Quản trị đổi email qua PATCH → email cũ 401, email mới 200.
   Tiền tố tài khoản riêng mỗi class test; teardown theo thứ tự FK (CLAUDE.md mục Test).
4. **Phá thử** (viết vào results là "chưa chạy, Claude sẽ chạy", kèm đúng dòng): (a) bỏ nhánh tra email → test (a) đỏ; (b) đổi thứ tự (email trước username) → test (f) đỏ; (c) đổi `ToLower` thành so sánh phân biệt hoa thường → test (b) đỏ.
5. Tài liệu: `docs/contract-drift.md` dòng ACCT-3 (SELF-SIGNED 08/11/2026): field `username` nhận username hoặc email, thứ tự tra, fail-closed, hệ quả đổi email, việc FE phải đổi (nhãn ô nhập). `docs/agents/traps/auth-accounts.md` thêm bẫy ngắn nếu có. `tracking.html` thêm thẻ. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*` (Claude sinh lại).
6. Ghi `.ai/results/ACCT-LOGIN-EMAIL.md` ngắn: đã làm gì, lệnh Claude cần chạy, điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới
- [ ] Test ở mục 3 có mặt; Claude chạy `dotnet test` xanh (Codex không chạy được test)
- [ ] Thông điệp / mã lỗi đăng nhập thất bại không đổi so với hiện tại ở mọi nhánh

## KHÔNG ĐƯỢC làm

- Không migration, không thêm index, không đổi schema.
- Không đổi tên field `username` trong JSON, không thêm field `email` vào `LoginRequest`.
- Không thêm mã lỗi mới; không phân biệt được "email không tồn tại" với "sai mật khẩu" từ phía client.
- Không ghi chuỗi người dùng gõ (username / email) vào log.
- Không đụng `UserAdminService.CreateAsync` / `AccountContracts.cs` (ACCT-USERNAME đang sửa) — chỉ ĐỌC `RequireFreeAsync`; nếu thấy cần sửa `RequireFreeAsync`, ghi vào results như câu hỏi, đừng sửa.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json`.
- Không `git add` / commit / push (sandbox chặn git; Claude làm).
