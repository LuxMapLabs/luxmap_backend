# ACCT-GOOGLE — phase 2, 09/11/2026

Đã viết hai route Google, port/verifier Google.Apis.Auth, options/CSV/DI và log tắt tính năng lúc startup.
Dùng chung mở refresh chain với mật khẩu, tra email có sẵn, chặn email chưa verified/tài khoản đang mời;
web thay cookie cũ trong transaction. Chuyển AllowAnonymous của WebAuthController từ class xuống từng action.
Test mới ở `tests/LuxMap.Api.Tests/GoogleLoginTests.cs` dùng verifier giả qua DI, không gọi Google;
bao mobile/web, các nhánh lỗi, validation, cấu hình tắt/CSV, cookie cũ, metadata và Origin.
Không sửa test đăng nhập cũ, PackageReference, schema, Contract hay OpenAPI.
Đã cập nhật ACCT-4, auth traps, env/compose, README, hướng dẫn Google Console và tracking.

✅ Verified — lệnh build đã chạy (exit 0, lần cuối):

```text
dotnet build LuxMap.slnx --disable-build-servers -m:1 --no-restore
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.56
```

⚠️ Test/runtime/phá thử/Swagger chưa chạy theo yêu cầu sandbox; chưa stage/commit/push, không chạy restore/Git.
Claude cần chạy trên môi trường có Docker:

```bash
dotnet test LuxMap.slnx --disable-build-servers -m:1 --no-restore --filter 'FullyQualifiedName~GoogleLoginTests|FullyQualifiedName~GoogleLoginOptionsTests|FullyQualifiedName~AuthEndpointTests|FullyQualifiedName~WebAuthTests|FullyQualifiedName~EmailLoginTests|FullyQualifiedName~AuthRotationTests'
dotnet test LuxMap.slnx --disable-build-servers -m:1 --no-restore
```

Sau đó xuất Swagger → generator → lint theo `.ai/context/commands.md`; Codex không sinh spec.

Phá thử: **chưa chạy, Claude sẽ chạy**, tại `AuthService.GoogleSignInAsync`:

- (a) Dòng `if (identity is null || !identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))`:
  bỏ `!identity.EmailVerified ||` → `Invalid_identity_unmatched_email_and_invitation_share_password_failure_and_never_register` phải đỏ.
- (b) Dòng `if (user is null || user.PasswordHash is null)`: bỏ `|| user.PasswordHash is null`
  → cùng test trên phải đỏ ở tài khoản mời.
- (c) Bỏ nhánh `if (googleOptions.ClientIds.Length == 0)` (cả return trong nhánh)
  → `Empty_client_ids_disable_sign_in_without_calling_the_verifier` phải đỏ (fake đếm lời gọi).
- (d) Tạm thay nhánh từ chối email không khớp tại `if (user is null || user.PasswordHash is null)` bằng tạo/lưu tài khoản
  → test (a/b ở trên) phải đỏ nhờ `Assert.Equal(before, await factory.QueryAsync(db => db.Set<AppUser>().CountAsync()));`.
  Collection này tắt song song để đếm toàn bảng đáng tin. Khôi phục từng lần bằng cp/touch, build lại, không dùng bin cũ.

📌 Cần xác nhận khi review: body được chốt chỉ có `id_token`, nên web đang dùng `WebSession` như login không gửi
`remember_me`; ticket chưa nêu rõ Google có cần tuỳ chọn persistent hay không. Chưa thêm field mới.
Cấu hình CSV có mặt (kể cả rỗng) ưu tiên indexed settings; Google Console/audience thực tế và đăng nhập người dùng thật chưa kiểm chứng.

## Kiểm chứng (Claude, 09/11/2026)

```text
dotnet test --filter (GoogleLogin|Auth*|WebAuth*|EmailLogin|AuthRotation|Authentication)   Passed 96
sabotage a: bỏ kiểm email_verified                                      2 red
sabotage b: cho tài khoản đang mời đi qua                               2 red
sabotage c: bỏ nhánh "ClientIds rỗng"                                   2 red
sabotage d: tự tạo tài khoản khi email không khớp                       2 red
dotnet test (full)                                                      1458 + 398 + 45 + 80, exit 0 (7 skipped)
redocly lint luxmap-v1.5.json                                           valid, 10 background warnings; 147 operations
```

Lần chạy toàn suite đầu đỏ ở `AnonymousEndpointTests` (danh sách endpoint ẩn danh viết tay) và script sinh spec thiếu hai mô tả; cả hai đã bổ sung.
Chưa kiểm chứng với Google thật (cần OAuth Client ID). Web Google dùng phiên không ghi nhớ (không có `remember_me`).
