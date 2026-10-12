# CITIZEN-QR P2a — Codex review, 12/10/2026

Phạm vi: `git diff b478fa1..HEAD` (`0c9b6e6`, `592ee89`), theo task, quyết định D-1…D-19, drift QR-1…QR-6 và AGENTS.md. **Chỉ đọc**, chỉ ghi file này. Không Docker/dotnet, không commit.

## Standards

Không phát hiện lỗi EF/migration/PLAN: unique `code`; unique partial `ux_qr_code_pole_id_active` và index đầy đủ `ix_qr_code_pole_id` có tên model riêng; CHECK format/cặp thu hồi/thời gian; bốn FK `Restrict`; snapshot khớp. PLAN đứng sau bảng cha, lọc cả người cấp/thu hồi, cơ chế sequence nhận `qr_id_seq`. Giới hạn rollback khi đã có audit QR đã được ghi trong AGENTS.md, không phải phát hiện mới.

## Spec

**P2 — Cấp lại đua với thu hồi, ghi đè lịch sử.** `src/LuxMap.Modules.Citizen/QrCodeService.cs:52` và `:82`. QR-1 chỉ thu hồi “mã đang dùng”; QR-2 yêu cầu lần thu hồi thứ hai → 409. Revoke khoá QR, Issue khoá pole rồi SELECT QR thường. Lịch hợp lệ: Revoke giữ khoá QR chưa commit → Issue đọc `revoked_at = null` qua MVCC → UPDATE của Issue chờ → Revoke commit → Issue UPDATE đè `revoked_by/at`, thêm audit `Revoked` với before-state null sai. **Đề xuất:** Revoke đọc scope lấy thêm `pole_id`, rồi khoá **pole → QR**, đọc lại trạng thái sau khoá. Thêm test điều phối Issue↔Revoke với hai Quản lý, assert giữ người/thời điểm thu hồi và chỉ một event `Revoked`. Test hiện tại `tests/LuxMap.Api.Tests/QrCodeTests.cs:97` chỉ đua Issue↔Issue.

**P3 — Test chưa canh rollback toàn bộ khi cấp mới thất bại.** `tests/LuxMap.Api.Tests/QrCodeTests.cs:49`. Bỏ transaction ngoài, giữ hai SaveChanges riêng vẫn vượt các ca thành công hiện có; nếu save cấp mới lỗi, mã cũ và audit thu hồi đã commit. **Đề xuất:** gây lỗi ở save thứ hai sau khi mã cũ đã save; kiểm mã cũ vẫn active, không có audit thu hồi/cấp mới bị lưu dở.

**P3 — “Audit never holds a code” chỉ kiểm AfterState.** `tests/LuxMap.Api.Tests/QrCodeTests.cs:65`. Đưa code vào BeforeState ở thao tác thu hồi vẫn xanh; test cũng chưa bắt rò qua Note/log. **Đề xuất:** kiểm cả BeforeState, AfterState, Note và thu log trong test; thử phá bằng cách đưa code vào BeforeState/log.

**P3 — Thiếu test bảo vệ schema và thứ tự QR qua ngưỡng.** `tests/LuxMap.Api.Tests/QrCodeTests.cs:53`, `:102`. Bỏ unique partial vẫn xanh khi pole lock còn; bỏ CHECK hoặc đổi FK sang cascade không bị dữ liệu hợp lệ hiện tại phát hiện. Bỏ `ThenBy(QrId.Length)` vẫn xanh vì hai mã được cấp khác thời điểm. **Đề xuất:** kiểm catalog/ghi SQL vi phạm cho index, CHECK, FK; seed QR-9999/QR-10000 cùng `created_at`, assert thứ tự qua endpoint. Test `PrefixedIdOrderingTests` hiện chỉ kiểm RoadSegment.

## Kiểm chứng

- ✅ Kiểm tra tĩnh: scope được đọc trước khoá; `ManageQrCodes` chỉ manager; đối tượng ngoài xã → 404, query xã ngoài phạm vi → 403; mỗi SaveChanges của service kèm một audit. Code không vào payload audit/đường dẫn quản trị; EF không bật sensitive parameter logging.
- ✅ Generator chạy **trong bộ nhớ, bỏ bước ghi** khớp JSON đã commit. Hai spec chỉ thêm hai path/ba schema; không xoá hay đổi path/schema cũ. Ba operation đúng capability/role/web. `git diff --check` sạch.
- ⚠️ Không chạy runtime/sabotage. Các trường hợp “vẫn xanh” trên là suy luận từ assertion; số test 897/46/45/380, hai skip MQTT và migration/sabotage pole-lock là bằng chứng do người dùng cung cấp, không phải lượt chạy của Codex.
- 📌 Nền API SELF-SIGNED chờ FW; phía công khai/log mã khi quét thuộc P2b.

Tổng: Standards 0; Spec 1 P2 + 3 P3. Cần sửa race trước khi chốt P2a.

## Đã xử lý (Claude, 12/10/2026)

| Phát hiện | Xử lý |
|---|---|
| P2 race cấp mới ↔ thu hồi | **Sửa**: `RevokeAsync` đọc `pole_id` qua filter rồi khoá `pole` → `qr_code` (cùng thứ tự với `IssueAsync`). Test `Issue_and_revoke_both_wait_for_the_pole_lock` giữ khoá `FOR NO KEY UPDATE` và thấy cả hai request chờ; phá thử bỏ khoá ở thu hồi → đỏ ("never took the lock") |
| P3 rollback khi save thứ hai lỗi | **Chưa thêm test** — cần tiêm lỗi vào lượt `SaveChanges` thứ hai (interceptor), chi phí cao so với rủi ro; cả hai save nằm trong một `BeginTransactionAsync` có `await using` (không commit thì rollback). Ghi nợ |
| P3 audit chỉ kiểm `after_state` | **Sửa**: kiểm cả `before_state`, `after_state`, `note` |
| P3 schema + thứ tự qua ngưỡng | **Thêm** `The_schema_enforces_one_active_code_per_pole_and_never_cascades` (đọc `pg_constraint` / `pg_indexes`: hai index pole_id đúng dạng, ba CHECK, bốn FK `RESTRICT`) và `The_list_breaks_ties_by_id_length_then_id` (`QR-9xxxx` / `QR-1xxxxx` cùng `created_at`); phá thử bỏ `ThenBy(QrId.Length)` → đỏ |

Sau khi sửa: 900 / 46 / 45 / 380 xanh, 2 skip MQTT; spec không đổi.
