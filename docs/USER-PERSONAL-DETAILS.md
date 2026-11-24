# Bổ sung hồ sơ người dùng

Admin (`ManageUsers`, system_admin) tạo/sửa qua POST `/api/v1/admin/users` và PATCH `/api/v1/admin/users/{id}`.
Các trường hiện tại giữ nguyên; sáu trường mới đều tùy chọn, nullable:

| Trường | Dữ liệu | Quy tắc |
|---|---|---|
| date_of_birth | YYYY-MM-DD | Không được ở tương lai, kiểm theo ngày UTC của server |
| gender | male / female / other | null nếu chưa cung cấp |
| address | chuỗi | Tối đa 500 ký tự; địa chỉ cá nhân, không thay phạm vi xã |
| phone_number | chuỗi | 7–15 chữ số, có thể bắt đầu +; bỏ dấu cách, dấu chấm, ngoặc và gạch ngang; không unique |
| identity_type | citizen_id / identity_card / passport / other | CCCD / chứng minh thư / hộ chiếu / khác |
| identity_number | chuỗi | Tối đa 64 ký tự, trim và viết hoa; giữ số 0 đầu |

Ví dụ POST (không gửi username, server tự sinh):
```json
{
  "email": "new-user@example.com",
  "full_name": "Nguyễn Văn A",
  "role": "manager",
  "commune_ids": ["COM-070"],
  "date_of_birth": "2000-01-15",
  "gender": "male",
  "address": "Địa chỉ cá nhân",
  "phone_number": "0912345678",
  "identity_type": "citizen_id",
  "identity_number": "001234567890"
}
```

PATCH dùng cùng sáu trường. Bỏ trường giữ nguyên giá trị. null xóa trường; muốn xóa giấy tờ thì gửi cả identity_type và identity_number bằng null. Cặp loại và mã giấy tờ không được trùng tài khoản khác (409 IDENTIFIER_TAKEN); sai dữ liệu trả 400 VALIDATION_FAILED.

GET `/api/v1/admin/users/{id}` trả đủ thông tin cho Admin; GET `/api/v1/admin/users` che identity_number, chỉ giữ bốn ký tự cuối (mã ngắn được che toàn bộ). Các trường còn lại có trong cả hai response.
GET `/api/v1/auth/me` trả đủ hồ sơ của chính tài khoản đăng nhập. Không thêm dữ liệu cá nhân vào JWT hoặc log thay đổi tài khoản.

**PATCH `/api/v1/auth/me` không thay đổi**: chỉ nhận full_name, không sửa sáu trường mới.
Không sửa FE. Khi deploy, áp migration `AddUserPersonalDetails` trước khi chạy bản API mới; không backfill thông tin giả cho tài khoản cũ.


## Đổi email bằng API riêng (Admin)

PATCH hồ sơ `/api/v1/admin/users/{id}` không còn nhận email. Nếu gửi trường email, kể cả null, trả 400 VALIDATION_FAILED với details.field=email, không cập nhật các trường khác trong request đó. POST tạo tài khoản vẫn yêu cầu email.

**PATCH https://luxmap.com.vn/api/v1/admin/users/USR-2090/email**

Authorization: Bearer token Admin (ManageUsers). Body raw JSON:
```json
{
  "email": "email-moi@example.com"
}
```
Thành công 200:
```json
{
  "user": {
    "user_id": "USR-2090",
    "username": "tienn1",
    "email": "email-moi@example.com",
    "full_name": "Nguyễn Tiến",
    "role": "manager",
    "commune_ids": ["COM-070"],
    "status": "active",
    "created_at": "2026-11-14T10:00:00Z",
    "password_set_at": "2026-11-14T10:05:00Z",
    "avatar_url": null,
    "date_of_birth": null,
    "gender": null,
    "address": null,
    "phone_number": null,
    "identity_number": null,
    "identity_type": null
  },
  "notification_sent": true
}
```
Thông tin user ở ví dụ chỉ minh họa; response trả user thực tế. Giữ nguyên username, password_hash, password_set_at và phiên đăng nhập. Có thể đăng nhập bằng username hoặc email mới với mật khẩu cũ; email cũ không còn dùng để đăng nhập (trừ trường hợp nó chính là username vốn có).

Email gửi **chỉ đến địa chỉ mới** qua SMTP hiện có (Gmail nếu cấu hình SMTP Gmail). Chủ đề `LuxMap — Đổi email thành công`. Nội dung gồm tên, username, email mới, thông báo dùng mật khẩu cũ và nút Truy cập LuxMap trỏ WEB_APP_BASE_URL. Dùng chung khung HTML AccountMailTemplate (navy/amber, bảng và CSS inline) với lời mời/reset; có bản text, encode toàn bộ dữ liệu người dùng. Tài khoản chưa đặt mật khẩu nhận hướng dẫn liên hệ Admin để gửi lại lời mời, không nhận thông báo sai rằng đã có mật khẩu.

notification_sent=true nghĩa SMTP đã nhận gửi, không bảo đảm thư đã vào hộp thư đến. Nếu SMTP lỗi: email vẫn được lưu, trả 200 với notification_sent=false; gọi lại cùng địa chỉ để thử gửi thông báo. Không rollback email sau khi đã lưu. Không gửi mật khẩu hay reset token trong thông báo.

Link mời/reset chưa sử dụng gửi tới email cũ được vô hiệu hóa khi đổi email; với tài khoản invited, Admin gửi lại lời mời qua POST `/api/v1/admin/users/{id}/invite`.

Lỗi: 400 dữ liệu email không hợp lệ/thiếu; 401 chưa đăng nhập; 403 không phải Admin; 404 user không tồn tại; 409 IDENTIFIER_TAKEN nếu email trùng tài khoản khác (không phân biệt hoa thường). Không đổi PATCH /auth/me; không sửa FE. API mới cần deploy BE trước khi thử trên VPS.


## Người đăng nhập tự đổi mật khẩu

**POST https://luxmap.com.vn/api/v1/auth/password/change**

Authorization: Bearer token của chính tài khoản cần đổi mật khẩu; tất cả bốn vai trò đều được dùng. Không nhận user_id/username/email và không cho Admin đổi thay người khác qua API này.
Body raw JSON:
```json
{
  "old_password": "mat-khau-hien-tai",
  "new_password": "mat-khau-moi-tu-12-ky-tu"
}
```

Thành công: **204 No Content**, không có JSON response. Đăng nhập lại bằng mật khẩu mới.

Validation giống POST /auth/password/set: new_password bắt buộc, 12–1024 ký tự, không thêm quy tắc chữ hoa/ký tự đặc biệt. old_password bắt buộc, tối đa 1024 ký tự; không áp minimum 12 lên mật khẩu cũ để tài khoản legacy vẫn đổi được. Không trim mật khẩu. Các trường ngoài hai trường trên bị từ chối.

Sai mật khẩu cũ: 400, không đổi hash hay thu hồi phiên/link:
```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "The current password is incorrect.",
    "details": {
      "field": "old_password",
      "correlation_id": "ma-do-server-sinh"
    }
  }
}
```

Thiếu/sai validation: 400 VALIDATION_FAILED. Chưa đăng nhập, tài khoản không còn tồn tại/bị khóa/chưa có mật khẩu: 401 UNAUTHENTICATED. Giới hạn tần suất dùng chung policy Login, quá giới hạn trả 429 kèm Retry-After.

Sau khi đổi: hash bằng PasswordHasher hiện có; cập nhật password_set_at/updated_at; thu hồi mọi refresh session (reason password_reset, dùng enum hiện có) và vô hiệu các link invite/reset chưa dùng. Access token đã cấp vẫn có thể dùng đến hết hạn theo cơ chế JWT hiện tại; FE xóa token và đưa người dùng về đăng nhập. Không trả/log mật khẩu, không thay email hay username, không gửi mail trong luồng này.

Các yêu cầu đổi đồng thời và reset từ email cùng khóa tài khoản trước link để không dùng lại mật khẩu/link cũ. Không sửa PATCH /auth/me và không sửa FE. Cần deploy BE mới trước khi gọi URL VPS.
