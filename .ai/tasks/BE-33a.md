---
ticket: BE-33a
title: Quản trị hệ thống tạo tài khoản, mời qua email; gỡ tự đăng ký
status: done
phase: 2
owner: claude
branch: feat/BE-33a-admin-users
depends_on:
  - "D-R11 (docs/contract-drift.md): chỉ Quản trị hệ thống tạo account, POST /auth/register DEPRECATED từ Contract v1.7"
  - "Mỹ chọn 05/10/2026: tài khoản mới nhận lời mời QUA EMAIL (link đặt mật khẩu), không dùng mật khẩu tạm"
---

## Bối cảnh

Phiếu v1.2/v1.4 không có tự đăng ký: Quản trị hệ thống tạo tài khoản, gán vai trò và xã (D-R11).
`POST /auth/register` còn chạy (đánh dấu deprecated) để lúc nào cũng có cách tạo tài khoản; ticket này thay
nó bằng `POST /api/v1/admin/users` (capability `ManageUsers` = `system_admin`, đã khai sẵn trong
`LuxMapPolicies.Matrix`) rồi gỡ nó.

Mỹ chọn hướng **email mời**: tài khoản tạo ra chưa có mật khẩu; hệ thống gửi email chứa link đặt mật khẩu
(có hạn, dùng một lần); người dùng đặt mật khẩu trên trang web của WP5 rồi mới đăng nhập (web hoặc Android).

Đọc trước: `AuthService` (hasher, lock, `CommuneIdsForAsync`), `AppUser`, `IdentitySeeder`
(`HasSystemWideScope = role == SystemAdmin`), `CommuneScopeConsistency` (claim `*` chỉ cho system_admin),
`RefreshTokenGenerator` (sinh + băm token), Contract §2 (ma trận) và §4, `AnonymousEndpointTests`,
`RegistrationTests`, `JwtOptions`/`StorageOptions` (khuôn đọc biến môi trường + fail-fast).

## Yêu cầu (chốt chi tiết ở Phase 1)

1. Quản trị tạo tài khoản: username, email, họ tên, vai trò, danh sách xã. Không có mật khẩu lúc tạo.
2. Gửi email mời chứa link đặt mật khẩu; endpoint ẩn danh nhận token + mật khẩu mới.
3. Quản trị xem danh sách / chi tiết, sửa vai trò / xã / thông tin, khoá / mở khoá, gửi lại lời mời.
4. Gỡ `POST /auth/register`, `RegisterRequest/Response`, `RegistrationTests`; cập nhật `AnonymousEndpointTests`.
5. Dev và test không gửi mail thật (Mailpit trong docker-compose; test dùng sender giả).
6. Contract + OpenAPI sinh lại; drift; báo WP5 (trang đặt mật khẩu, màn quản trị) và WP6 (bỏ màn đăng ký).

## KHÔNG ĐƯỢC làm

- Không tự chọn phương án ở D-item: Phase 1 dừng cứng ở `.ai/results/BE-33a-p1.md`.
- Không commit bí mật SMTP; không dán mật khẩu ứng dụng vào code, test, log hay `.ai/`.
- Không trả token đặt mật khẩu trong response API (chỉ đi qua email); không log token thô.
- Không làm lộ email nào tồn tại qua endpoint quên mật khẩu (luôn cùng một response).
- Không cho `has_system_wide_scope` lệch vai trò: `*` chỉ cho `system_admin`.
- Không xoá tài khoản (khoá thay xoá: lịch sử sự cố, phiếu, audit trỏ vào `app_user` bằng `Restrict`).
- Không dùng `ExecuteUpdate/ExecuteDelete` ngoài ngoại lệ đã có của `RefreshToken` (BannedSymbols).
- Không đổi `role`/`commune_ids` của tài khoản seed trong test (CLAUDE.md: test tự tạo tài khoản riêng).
- Không `git add -A`; không push khi chưa được hỏi.
