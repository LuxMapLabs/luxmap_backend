# ACCT-LOGIN-EMAIL — phase 2, Codex, 08/11/2026

- Đã sửa `AuthService`: tham số `identifier`, helper tra username trước/email sau, Trim + so không phân biệt hoa thường. Email nhiều kết quả → InvalidCredentials, warning chỉ chứa các user_id (không chuỗi nhập). Giữ nguyên xử lý mật khẩu/khoá/token/rate limit và JSON `username`.
- Viết `EmailLoginTests`: 5 theory × mobile/web = 10 ca khai báo, phủ (a)–(h), assert literal mã/thông điệp lỗi và đúng JWT sub; tài khoản prefix `email-login-`, teardown riêng. Chưa chạy test.
- XML-doc hai DTO/hai action, README, drift **ACCT-3 SELF-SIGNED**, trap auth, tracking đã cập nhật. Không sửa Contract/OpenAPI, schema, UserAdminService hay AccountContracts; không stage/commit/push, không dùng git.
- `RequireFreeAsync` hiện đã chặn email trùng không phân biệt hoa thường khi tạo/PATCH. DB unique email phân biệt hoa thường nên vẫn cần fail closed cho dữ liệu cũ/chèn trực tiếp. Không phát sinh câu hỏi cần sửa RequireFreeAsync.

✅ Đã đối chiếu tĩnh code/controller/error mapping/fixture và phạm vi file sửa.
⚠️ Chưa xác minh biên dịch, warning hoặc hành vi runtime. Build thường không hoàn tất, đã yêu cầu huỷ; build đơn tiến trình với CLI_HOME tạm bị lỗi restore/network rồi timeout 55 giây (exit 124). Output thật:

```text
.../src/LuxMap.Api/LuxMap.Api.csproj : error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
.../src/LuxMap.Api/LuxMap.Api.csproj : error NU1301:   nodename nor servname provided, or not known (api.nuget.org:443)
Build timed out after 55 seconds (exit 124).
```

Lệnh Claude cần chạy ngoài sandbox (không dùng bin cũ):

```bash
dotnet build LuxMap.slnx --disable-build-servers
dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter 'FullyQualifiedName~EmailLoginTests'
dotnet test LuxMap.slnx --disable-build-servers
dotnet tool restore
dotnet build src/LuxMap.Api --disable-build-servers
Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py
npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
```

Phá thử: **chưa chạy, Claude sẽ chạy**. Mỗi lần chỉ phá một chốt trong `src/LuxMap.Modules.Identity/Auth/AuthService.cs`, build/test lại, khôi phục bằng `cp`/`touch`:

- (a) Dòng 95 `return user ?? await FindByEmailForSignInAsync(normalized, ct);` → `return user;`: test `Email_and_username_sign_in_identify_the_same_account` (a) phải đỏ.
- (b) Dòng 93–95: tra email trước username → `Username_wins_over_another_accounts_email_even_when_its_password_is_wrong` (f) phải đỏ.
- (c) Bỏ block dòng 105–110 `if (matches.Count > 1)` (dòng 112 SingleOrDefault sẽ ném), hoặc cho lấy hàng đầu để mô phỏng bỏ fail closed → `Ambiguous_email_fails_closed_for_either_accounts_correct_password` (g) phải đỏ.
- (d) Dòng 94/103: bỏ `.ToLower()` để so phân biệt hoa thường → `Email_and_username_sign_in_identify_the_same_account` (b) phải đỏ.

📌 Nền API tạm ACCT-3 SELF-SIGNED; chờ Claude build/test/phá thử/Swagger và review. Không có số test pass hay kết luận 0 warning.
