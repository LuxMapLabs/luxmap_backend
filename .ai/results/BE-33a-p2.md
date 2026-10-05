# BE-33a — Phase 2: hiện thực (05/10/2026, claude)

Nhánh `feat/BE-33a-admin-users` từ `dev` @ `a1b15e9`. Theo D-1…D-14 Mỹ chốt (`BE-33a-p1.md` mục 5).

## Đã làm

- **API:** `POST/GET /admin/users`, `GET/PATCH /admin/users/{id}`, `POST /admin/users/{id}/lock|unlock|invite`
  (`ManageUsers`); `POST /auth/password/set`, `POST /auth/password/forgot` (ẩn danh, `[AllowAnonymous]` theo method,
  `forgot` có rate limit `account-mail`). Gỡ `POST /auth/register`, `RegisterRequest/Response`, `RegisterOutcome`,
  `AuthService.RegisterAsync/LowestRole`, `RegistrationTests`.
- **Lược đồ:** migration `AdminCreatedAccounts` (đọc trước khi apply; sửa tay: backfill `password_set_at` trước CHECK
  trong `Up()`, `Down()` điền hash rỗng cho tài khoản đã mời và đổi lý do thu hồi mới về `logout`).
- **Mail:** `EmailOptions` (env, fail-fast), `SmtpEmailSender` (MailKit 4.18.1; có login thì bắt buộc TLS),
  `AccountMailer` (không log link/token). Mailpit `v1.31.4@sha256:b68349e3…` (digest manifest list) trong compose.
- **Lệch so với phác thảo P1 §4:** `account_token → app_user` là **Cascade**, không Restrict (cùng loại
  `refresh_token`; teardown test xoá tài khoản sẽ gãy nếu Restrict); bỏ cột `created_by` (log đã ghi admin).
  `account_token` vào `SKIPPED` của script Supabase thay vì `PLAN`.
- **Thêm ngoài bảng D-item (an toàn, không đổi API):** đặt mật khẩu qua link **thu hồi mọi phiên cũ** (lý do
  `password_reset`); "quên mật khẩu" với tài khoản chưa đặt mật khẩu gửi **thư mời** thay vì link đặt lại.

## Bằng chứng

Migration trên `luxmap_test` — apply → rollback → apply:

```
== apply            Done.   (password_set_at null / tổng: 0|4; CHECK: ck_app_user_password_set_together,
                             ck_app_user_role, ck_app_user_system_wide_scope_matches_role; account_token có)
== rollback to 20261004153825_AttachPhotosToFaults   Done.   (account_token: rỗng; password_set_at: 0 cột; password_hash is_nullable: NO)
== reapply          Done.   account_token
```

`luxmap_dev` — trước / dọn / sau:

```
BEFORE  field_engineer f 1 | manager f 1 | superior f 1 | system_admin f 14 | system_admin t 1
CLEANUP NOTICE:  Deleted 14 leftover accounts.
AGAIN   NOTICE:  No BE-12a/BE-12b leftover accounts: nothing to do.
AFTER   admin system_admin t t | agency superior f t | engineer manager f t | crew field_engineer f t ; account_tokens 0
```

Test trên `luxmap_test`:

```
Passed!  - Failed: 0, Passed: 659, Total: 659 - LuxMap.Api.Tests.dll
Passed!  - Failed: 0, Passed: 337, Total: 337 - LuxMap.Shared.Tests.dll
Passed!  - Failed: 0, Passed:  44, Total:  44 - LuxMap.Persistence.Tests.dll
Passed!  - Failed: 0, Passed:  41, Total:  41 - LuxMap.Infrastructure.Storage.Tests.dll
```

Phá thử (mỗi lần một luật, chạy `AccountAdminTests`, rồi khôi phục):

```
1) link cũ còn dùng sau khi gửi link mới  → red: A_failed_mail_still_creates_the_account_and_a_resent_invitation_replaces_the_old_link
2) khoá được Quản trị cuối cùng            → red: The_last_active_system_admin_can_be_neither_demoted_nor_locked
3) tài khoản khoá vẫn nhận thư đặt lại     → red: Forgot_password_sends_an_invited_account_a_new_invitation_and_a_locked_account_nothing
```

End-to-end trên `luxmap_dev` qua Mailpit thật (tài khoản kiểm thử đã xoá sau đó):

```
POST /admin/users → 201 {"user":{...,"status":"invited","password_set_at":null},"invitation_sent":true}
Mailpit: "LuxMap — Lời mời tạo tài khoản", hết hạn 14:09 ngày 08/10/2026 giờ VN, link http://localhost:5173/set-password?token=<redacted>
set password 204 · dùng lại link 400 · đăng nhập 200 · /auth/me commune_ids ["COM-001"]
forgot 202 → "LuxMap — Đặt lại mật khẩu" · đặt lại 204 · mật khẩu cũ 401 · mật khẩu mới 200
raw tokens in logs: 0
```

OpenAPI: `wrote docs/openapi/luxmap-v1.5.json: paths=60 implemented_ops=76 not_implemented_ops=2`; redocly lint
"valid", 8 cảnh báo — trùng 8 cảnh báo của spec trên `dev`.

## Còn lại

- Báo WP5 (trang `/set-password`, màn quản trị, "quên mật khẩu") và WP6 (bỏ màn đăng ký) — drift BE-33a.
- SMTP thật cho môi trường deploy (Mỹ điền `.env`; không qua chat). Deploy sau proxy: forwarded headers trước rate limit.
- Audit tài khoản (A-7) và outbox gửi lại tự động (A-6, BE-26).
