# BE-33a — Phase 1: khảo sát (05/10/2026, claude)

Nền: `dev` @ `a1b15e9`, nhánh `feat/BE-33a-admin-users`. Chưa sửa code. Mỹ đã chọn **email mời** (không mật khẩu tạm).

## 1. Hiện trạng (đọc code + DB, không suy luận)

**Không có hạ tầng email nào.** `grep -rliE "smtp|mailkit|sendgrid|EmailSender"` trên `src`, `.env.example`,
`docker-compose.yml`: không kết quả. Không thư viện, không biến cấu hình, không service compose.

**`app_user`** (`AppUser.cs`): `user_id, username, email (required), full_name, password_hash (required),
password_algorithm, role, is_locked, has_system_wide_scope, created_at, updated_at` + bảng nối `app_user_commune`.
Không có trạng thái "chưa đặt mật khẩu". Index trên DB:

```
ix_app_user_email_lower    | CREATE UNIQUE INDEX ... USING btree (lower(email))
ix_app_user_username_lower | CREATE UNIQUE INDEX ... USING btree (lower(username))
```

CHECK trên `app_user` / `refresh_token`:

```
ck_app_user_role                | CHECK ((role = ANY (ARRAY['superior','manager','field_engineer','system_admin'])))
ck_refresh_token_revoked_reason | CHECK (((revoked_reason IS NULL) OR (revoked_reason = ANY (ARRAY['rotation','logout','reuse_detected']))))
```

**Không có ràng buộc DB nối `has_system_wide_scope` với `role`.** `CommuneScopeConsistency.cs` nói thẳng: *"BE-06 has
no database constraint tying has_system_wide_scope to role … a defect in BE-33 is enough to make BE-07 hand ["*"] to
an ordinary account"*; nó chặn ở tầng authorization (403 `COMMUNE_FORBIDDEN`). `IdentitySeeder` đặt
`HasSystemWideScope = template.Role == UserRole.SystemAdmin`.

**Dữ liệu `luxmap_dev` hiện vi phạm bất biến đó:**

```
      role      | has_system_wide_scope | is_locked | count
 system_admin   | f                     | f         |    14
 system_admin   | t                     | f         |     1
```

14 hàng là cặn test `be12a-*` / `be12b-*` (tạo 21/09/2026) — đúng follow-up BE-36 trong `tracking.html` ("dọn 14 tài
khoản cặn"). Một CHECK mới sẽ **gãy migration** trên DB dev nếu không xử lý chúng trước.

**Khoá tài khoản** (`AuthService`): login kiểm `IsLocked` SAU khi kiểm mật khẩu (không lộ tài khoản tồn tại); refresh
từ chối tài khoản khoá; access token còn sống tới hết 60 phút (`GET /auth/me` cố ý không chặn). Khoá **không** thu hồi
refresh token nào hiện nay.

**Đổi vai trò / xã:** claim cố định trong access token 60 phút; refresh dựng lại claim từ DB
(`BuildTokensAsync` → `CommuneIdsForAsync`), nên thay đổi có hiệu lực ở lần refresh kế tiếp, chậm tối đa 60 phút.

**Audit** (`AuditEvent`): `ICommuneScoped`, `commune_id` **bắt buộc**, `entity_type ∈ {work_order, fault, survey_sweep}`.
Tài khoản không thuộc một xã nào → không vừa khuôn này.

**Rate limiter:** `grep AddRateLimiter|EnableRateLimiting` trên `src`: không có. Endpoint ẩn danh mới (quên mật khẩu)
sẽ là endpoint ẩn danh đầu tiên gửi email ra ngoài.

**Bảng trỏ vào `app_user`** (FK, tất cả là lý do không được xoá tài khoản): `app_user_commune, audit_event,
fault (reported_by, confirmed_by, resolved_by), luminance_baseline, luminance_history, lux_reading, refresh_token,
repair_evidence, survey_sweep (captured_by, reviewed_by), work_order (assigned_to, created_by)`.

**Khuôn cấu hình:** bí mật đọc từ biến môi trường UPPER_SNAKE và fail-fast lúc khởi động (`JwtOptions.SigningKeyEnvironmentVariable
= "JWT_SIGNING_KEY"`, `StorageOptions.EndpointVariable = "MINIO_ENDPOINT"`). FE dev origin: `http://localhost:5173`
(`appsettings.Development.json` → `Cors:AllowedOrigins`).

**Mailpit:** `docker manifest inspect axllent/mailpit:latest` → kéo được; tag mới nhất `v1.31.4` (03/10/2026).

**Test chạm tới:** `RegistrationTests.cs` (xoá), `AnonymousEndpointTests.cs` (danh sách endpoint ẩn danh đổi),
`CapabilityPolicyCoverageTests` / `RoleCapabilityMatrixTests` (endpoint mới phải nêu `ManageUsers`).

## 2. Luồng đề xuất

```
Quản trị: POST /admin/users {username,email,full_name,role,commune_ids}
  → app_user (chưa có mật khẩu, trạng thái "đã mời") + account_token(invite, băm SHA-256, hạn, dùng một lần)
  → commit → gửi email: {WEB_APP_BASE_URL}/set-password?token=<thô>
Người dùng: trang WP5 → POST /auth/password/set {token,new_password} (ẩn danh)
  → kiểm token (đúng mục đích, chưa dùng, chưa hết hạn) → băm mật khẩu bằng PasswordHasher hiện có → token used
  → đăng nhập bình thường (web hoặc Android)
```

Endpoint đề xuất: `POST/GET /admin/users`, `GET/PATCH /admin/users/{id}`, `POST /admin/users/{id}/lock|unlock|invite`
(ManageUsers) + ẩn danh `POST /auth/password/set`, `POST /auth/password/forgot`. Đặt trong module **Identity**
(sở hữu `AppUser`; module Admin rỗng mà tham chiếu Identity thì thêm một cạnh phụ thuộc không cần).

## 3. D-item — cần Mỹ chốt

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D-1 | Hạn token mời / đặt lại | Mời **72 giờ**, đặt lại **1 giờ**; dùng một lần; gửi lại lời mời vô hiệu token mời cũ |
| D-2 | Có làm **quên mật khẩu tự phục vụ** trong ticket này? | **Có**: `POST /auth/password/forgot {email}` luôn `202` cùng một body (không lộ email tồn tại), dùng chung bảng token; kèm rate limiter có sẵn của ASP.NET Core (không thêm package): 5 lần / 15 phút / IP |
| D-3 | Gửi mail lỗi (SMTP chết) | Tài khoản **vẫn tạo** (201), response có `invitation_sent: false`; Quản trị bấm gửi lại. Gửi đồng bộ sau commit; chưa có outbox tới BE-26 (Hangfire) |
| D-4 | Thư viện + dev | **MailKit** (NuGet mới) + **Mailpit** trong compose (pin digest manifest list). Phương án thay: `System.Net.Mail.SmtpClient` có sẵn nhưng Microsoft không khuyến nghị cho code mới. Rollback: gỡ package + service, không đụng dữ liệu |
| D-5 | Thiếu cấu hình SMTP lúc khởi động | **Fail-fast** như JWT/MinIO; máy dev trỏ Mailpit (`localhost:1025`) nên không cần bí mật thật |
| D-6 | Bất biến `*` ↔ `system_admin` | Thêm CHECK `ck_app_user_system_wide_scope_matches_role`: `has_system_wide_scope = (role = 'system_admin')`. **Trước đó phải xử lý 14 tài khoản cặn** — xem D-7 |
| D-7 | 14 tài khoản `be12a-*/be12b-*` trên `luxmap_dev` | Xoá bằng script dọn riêng (kèm hàng phụ thuộc của chúng) **trước** khi apply migration; migration **không** tự xoá dữ liệu. Phương án thay: migration đặt cờ cho chúng = true (cho cặn test quyền toàn hệ thống — không đề xuất) |
| D-8 | Xã bắt buộc | `superior` / `manager` / `field_engineer` **≥ 1 xã**, xã phải tồn tại; `system_admin` **không** nhận `commune_ids` (gửi → 400), cờ toàn hệ thống tự bật |
| D-9 | Khoá | Khoá **thu hồi mọi refresh token** của tài khoản (lý do mới `account_locked` → mở rộng CHECK); access token còn ≤ 60 phút như hiện nay. Không khoá được chính mình, không khoá Quản trị cuối cùng còn hoạt động |
| D-10 | Đổi vai trò / xã | **Không** thu hồi phiên; có hiệu lực ở lần refresh kế tiếp (≤ 60 phút). Hạ vai trò Quản trị cuối cùng → 409 |
| D-11 | Sửa gì được | `full_name`, `email`, `role`, `commune_ids`. **`username` bất biến** (định danh đăng nhập + log). Đổi email không gửi xác minh |
| D-12 | Audit | **Không** dùng `audit_event` (bắt buộc `commune_id`, khuôn theo xã). Ghi log Serilog có cấu trúc cho mọi thao tác quản trị; audit tài khoản thành follow-up cùng capability "system log" của Phiếu |
| D-13 | Trạng thái tài khoản | Cột mới `password_set_at timestamptz NULL` (NULL = đã mời, chưa đặt) thay cho cờ boolean; `password_hash` thành NULL được cho tài khoản chưa đặt; login với tài khoản chưa đặt → cùng `401 INVALID_CREDENTIALS` (không lộ trạng thái). API trả `status`: `invited` / `active` / `locked` (tính lúc đọc, không lưu) |
| D-14 | Contract | Lên **v1.8**: §4 thêm 2 endpoint ẩn danh, mục mới cho `/admin/users`, gỡ `register`. Chạm bề mặt API → `SELF-SIGNED`, **ESCALATE** ở FW kế tiếp; WP5 cần trang đặt mật khẩu + màn quản trị tài khoản |

## 4. Thay đổi lược đồ dự kiến (sau khi chốt)

- `app_user`: + `password_set_at`, `password_hash` nullable, + CHECK D-6, + CHECK `password_hash` NULL ⇔ `password_set_at` NULL.
- Bảng mới `account_token(token_id, user_id → app_user Restrict, purpose CHECK invite|reset, token_hash UNIQUE,
  expires_at, used_at NULL, created_at, created_by NULL)`. Không `commune_id` (không phải gốc truy vấn theo xã).
- `refresh_token.revoked_reason` CHECK thêm `account_locked`.
- Thêm `account_token` vào `PLAN` của `scripts/copy_dev_to_supabase.py` (CLAUDE.md: bảng mới vào cùng PR).

## 5. Quyết định

**Mỹ chốt 05/10/2026: đồng ý cả 14 đề xuất D-1 … D-14 như bảng mục 3.** Phase 2 theo đúng các đề xuất đó.
