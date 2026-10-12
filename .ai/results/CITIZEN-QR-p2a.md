# CITIZEN-QR — P2a: mã QR trên cột (Claude, 12/10/2026)

Nhánh `feat/CITIZEN-QR-p2a` (từ `origin/dev` @ `b478fa1`). Quyết định: D-1, D-3, D-11, D-16, CQ-1, CQ-10 (Mỹ chốt 12/10).
Drift **QR-1…QR-6** (SELF-SIGNED). Không có endpoint công khai nào — đó là P2b.

## Đã làm

| Phần | File |
|---|---|
| Module mới `LuxMap.Modules.Citizen` (tham chiếu Shared, Persistence, Assets, Identity; không gì tham chiếu ngược), đăng ký trong `Program.cs`, `LuxMap.slnx` | `src/LuxMap.Modules.Citizen/*` |
| Bảng `qr_code`: `qr_id` (`QR-0001`, sequence `qr_id_seq`) · `code` (CHECK `^[0-9A-Za-z]{16}$`, unique) · `pole_id` FK · `commune_id` (chép từ cột) · `created_by/at` · `revoked_by/at` (CHECK cùng có / cùng vắng, thu hồi ≥ tạo). Unique partial `ux_qr_code_pole_id_active` + index đầy đủ `ix_qr_code_pole_id`. Mọi FK `Restrict`. `ICommuneScoped`, `IAudited` | `Entities/QrCode.cs`, `Configurations/QrCodeConfiguration.cs`, migration `AddQrCodes` |
| `POST /qr-codes` (201, thu hồi mã đang dùng trong cùng giao dịch, khoá hàng `pole` sau khi đọc qua filter), `POST /qr-codes/{qr_id}/revoke` (409 lần hai), `GET /qr-codes` | `QrCodeService.cs`, `QrCodesController.cs` |
| Mã: `RandomNumberGenerator.GetString(base62, 16)`; `url` = `new Uri(WEB_APP_BASE_URL, "/r/" + code)` | `QrCodeService.cs` |
| Audit: `AuditEntityType.QrCode`, `AuditAction.Revoked`; payload **không** chứa `code` | `AuditEvent.cs` |
| Capability `ManageQrCodes` = Quản lý; hai test ma trận literal; tiền tố `QR` trong bảng tiền tố + test; mã lỗi `QR_CODE_NOT_FOUND`, `QR_CODE_ALREADY_REVOKED` | Shared, tests |
| `qr_code` vào `PLAN` của `copy_dev_to_supabase.py` (chỉ mã do tài khoản seed cấp / thu hồi); teardown `AssetImportFixture` xoá `qr_code` trước `pole` | scripts, tests |
| OpenAPI xuất lại + generator (tag `QrCodes`, summary, section); nhãn client `web` | `docs/openapi/` |
| `QrCodeTests` (10, PostGIS) | tests |

## Bằng chứng

```
dotnet build LuxMap.slnx -warnaserror      Build succeeded (kể cả NuGet audit)
Api 900 ✓ · 2 skip (MQTT E2E) · Storage 46 ✓ · Persistence 45 ✓ · Shared 380 ✓   (container BE-36, sau review)
Redocly: valid, 8 warnings (như trước)
```

Migration trên DB dùng một lần (`imresamu/postgis:17-3.5`):

```
apply:      qr_code | qr_id_seq | 7 index | ck_audit_event_entity_type có 'qr_code'
rollback→AddLightingCommands:  - | - | 0 | CHECK không còn 'qr_code'
reapply:    qr_code | qr_id_seq | 7 index | như lần đầu
```

Đọc migration trước khi apply: chỉ `CreateSequence`, `CreateTable`, `CreateIndex` ×6 và dựng lại hai CHECK của audit; không
`DropIndex` / `DropColumn` ngoài ý muốn; `Down()` đối xứng.

Phá thử: bỏ `SELECT 1 FROM pole … FOR UPDATE` → `Simultaneous_presses_leave_exactly_one_code_in_use` đỏ 3/3 lượt (5 lần bấm đồng
thời đụng `ux_qr_code_pole_id_active` → 500). Khôi phục → 7/7 xanh.

## Điều chỉnh so với Phase 1

- **`code` không còn là khoá chính** (P1 Q-1 ghi `code PK`). Thêm `qr_id` để mọi thao tác, log, audit dùng `qr_id`; `code` chỉ nằm
  trong response của Quản lý — đúng D-16 chặt hơn. Ghi ở drift QR-5.
- **Không FK ghép `(pole_id, commune_id)`** (review P1 mục 10 đề nghị khuôn O-7). `pole` không có alternate key đó; thêm vào sẽ biến
  `Pole.CommuneId` thành key property cho cả bảng `pole` (bẫy O-7) chỉ để bảo vệ một cột mà server tự chép. Dùng khuôn `fixture`:
  `commune_id` chép từ cột, không nhận từ client; cột không đổi xã (BE-12a quy tắc 1).

## Review

Codex (`.ai/reviews/CITIZEN-QR-p2a-by-codex.md`, ~76k token): 0 P1 / 1 P2 / 3 P3. P2 (race cấp mới ↔ thu hồi ghi đè lịch sử)
đã sửa — thu hồi khoá cùng hàng `pole`; hai P3 về test đã thêm, một P3 (tiêm lỗi save thứ hai) ghi nợ. Phá thử: bỏ khoá pole ở thu
hồi → đỏ; bỏ `ThenBy(QrId.Length)` → đỏ.

## Còn lại

- P2b (nhận công khai) — 🔴 cần forwarded headers trước; actor audit `Public`; bom pixel; `citizen_submission`.
- P2c (duyệt, tạo / gộp sự cố, AI).
- Báo WP5: màn Quản lý cấp / thu hồi / in QR (drift QR-1…QR-6), route công khai `/r/:code` (D-12).
