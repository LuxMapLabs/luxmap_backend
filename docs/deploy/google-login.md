# Bật đăng nhập bằng Google (ACCT-4)

Đây là nền tạm **SELF-SIGNED 08/11/2026** (ACCT-4 trong [contract-drift.md](../contract-drift.md)).
Google chỉ đăng nhập vào email của tài khoản Quản trị đã tạo và người dùng đã đặt mật khẩu qua link mời.

1. Trong Google Cloud Console → Google Auth Platform, chọn/tạo project. Cấu hình consent screen:
   Audience **External**, điền tên LuxMap, email hỗ trợ/liên hệ và domain của ứng dụng; chuyển publishing status
   sang **In production** trước khi phục vụ người dùng thật. Scope chỉ `openid email profile`.
2. Tạo OAuth client loại **Web application**. Authorized JavaScript origins:
   `https://<LUXMAP_DOMAIN>` (thay bằng domain thật) và `http://localhost:5173` cho Vite local, không thêm path.
   FE dùng Google Identity Services lấy ID token rồi POST `id_token` đến API.
   Xem [hướng dẫn setup của Google](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid).
3. Cho Android, tạo OAuth client loại **Android** với đúng package name và SHA-1 của chứng chỉ ký
   (debug/release có thể cần client riêng). Khi lấy ID token cho backend, Android dùng Web/server client ID
   làm audience; đưa đúng các audience thực tế vào allowlist backend.
   Xem [hướng dẫn Android của Google](https://codelabs.developers.google.com/sign-in-with-google-android).
4. Trên VPS, điền `GOOGLE_CLIENT_IDS` trong `deploy/.env` bằng CSV các client ID được phép, ví dụ:
   `GOOGLE_CLIENT_IDS=<web-client-id>,<android-client-id>`.
   API ánh xạ CSV sang mảng `Google:ClientIds` (dạng biến môi trường `Google__ClientIds__N`), bỏ khoảng trắng
   và phần tử rỗng. CSV có mặt được ưu tiên hơn cấu hình indexed. Không cần client secret.
   Sau khi triển khai bản API có ACCT-4, từ `deploy/` chạy `docker compose up -d --force-recreate api`
   để container nhận cấu hình mới. Ở local điền `.env` gốc rồi khởi động lại API; compose local chỉ chạy hạ tầng.

Kiểm tra bằng tài khoản đã nhận lời mời: Google web trả access token và cookie refresh, mobile trả refresh trong body.
Web Google mở `web_session` (không có `remember_me` trong request). Email không khớp, chưa xác minh hoặc tài khoản đang
mời trả 401; tài khoản khoá trả 403. Không copy token thật vào log, ticket hay repo.

Để tắt/rollback cấu hình, để `GOOGLE_CLIENT_IDS=` trống rồi recreate API: cả hai route Google trả
`401 INVALID_CREDENTIALS` mà không gọi Google; đăng nhập mật khẩu tiếp tục dùng được.
API không đổi CORS; Google Authorized JavaScript origins là cấu hình riêng tại Google.

Giới hạn v1: chưa liên kết Google subject, chưa kiểm nonce (token lộ dùng lại được tới hết hạn), chưa tắt Google
riêng cho từng tài khoản. Chi tiết quyết định và luồng lỗi ở ACCT-4.
