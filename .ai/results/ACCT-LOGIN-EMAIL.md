# ACCT-LOGIN-EMAIL — kết quả (Codex viết, Claude kiểm chứng), 08/11/2026

## Đã làm

- `AuthService.LoginAsync(identifier, …)`: tra **username trước, rồi email**, `Trim()`, so không phân biệt hoa thường. Field JSON vẫn là `username`; mobile `/auth/login` và web `/auth/web/login` dùng chung. Mọi bước sau khi tra (mời chưa có mật khẩu, khoá kiểm sau mật khẩu, một lỗi `INVALID_CREDENTIALS`, rate limit, refresh chain) không đổi. Log không ghi chuỗi người dùng gõ.
- `EmailLoginTests`: 5 theory × mobile/web — email/username đúng, in hoa và có khoảng trắng; email lưu chữ hoa tìm được từ mọi kiểu chữ; thất bại giữ nguyên mã/thông điệp (kể cả mời và khoá); username thắng email của người khác; PATCH đổi email có hiệu lực ngay.
- Drift **ACCT-3** (SELF-SIGNED), trap `auth-accounts.md`, README, `tracking.html`.

## Đính chính so với ticket

Ticket giả định unique index của `email` phân biệt hoa thường nên cần "từ chối khi một email khớp nhiều hàng". **Sai**: `ix_app_user_email_lower` (và `ix_app_user_username_lower`) là UNIQUE trên `lower(...)` (migration `AddCaseInsensitiveIdentityIndexes`), nên không thể có hai hàng khớp. Nhánh fail-closed và test của nó (g) đã bỏ vì là mã không bao giờ chạy tới.

## Kiểm chứng (Claude)

```text
dotnet test --filter EmailLoginTests|Auth*|WebAuth*|AccountUsername*   Passed 76
sabotage a: bỏ nhánh tra email                                          6 red
sabotage b: tra email trước username                                    2 red (username thắng email)
sabotage d: so sánh email phân biệt hoa thường ở cột                    2 red (email lưu chữ hoa)
```

Phá thử (d) ban đầu **xanh** — test cũ chỉ lưu email chữ thường nên không bắt được lỗi so sánh phía cột; đã thêm `A_mixed_case_stored_email_is_found_from_any_casing`.
