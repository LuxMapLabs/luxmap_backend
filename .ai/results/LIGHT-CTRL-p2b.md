# LIGHT-CTRL — Phase 2b (lệnh ON / OFF / AUTO + kênh thiết bị) · 08/10/2026

Nhánh `feat/LIGHT-CTRL-2b` (làm trong bản clone ngoài Documents — repo chính đang bị macOS chặn đọc). Đặc tả
`.ai/results/LIGHT-CTRL-p1.md` §3.3–3.7, drift LC-1…LC-11. ⚠️ **Nền tạm:** SELF-SIGNED, chạm API.

## Đã làm

| Phần | File |
|---|---|
| Bảng | `lighting_request`, `lighting_command` (`CMD` ≥ 6 chữ số, `seq` identity), `feeder_control.mode_seq` — migration `AddLightingCommands` |
| Audit | actor `system`; entity `lighting_request`, `lighting_command`; action `requested`, `delivered`, `applied`, `failed`, `expired`, `superseded`, `reported` |
| Dịch vụ | `Telemetry/Lighting/LightingCommandService.cs` — độc lập kênh: bấm, xem trước, liệt kê, poll, ACK, hết hạn, thay |
| API | `LightingController` (`/lighting/preview`, `POST/GET /lighting/commands`), `DeviceCommandsController` (`/device/commands`, `…/{id}/ack`) |
| Nối với 2a | tháo / nối rơ-le → lệnh đang mở của rơ-le đó `superseded` (actor Quản lý) |
| Cấu hình | `Lighting:CommandTtl` (mặc định 60 s) |
| Kéo theo | spec: scheme `Device` + `DeviceSecurityOperationFilter`; `CapabilityPolicyCoverageTests` (ngoại lệ có tên), `OpenApiSpecTests`, `RoleCapabilityMatrixTests` (ControlLighting → endpoint thật); teardown `AssetImportFixture`; `copy_dev_to_supabase.py` `PLAN`; ERD; `CLAUDE.md`; drift LC-11 |

## Bằng chứng

- Migration: chỉ thêm bảng / cột / sequence / CHECK, sinh lại 3 CHECK enum của `audit_event`; không `DropIndex`. Apply `luxmap_test` sạch.
- **1265/1265 xanh** (352 + 44 + 46 + 823; +23 `LightingCommandTests`, +1 test ngoại lệ thiết bị). Release 0 lỗi.
- **Phá thử — đỏ đúng test:**

| Phá | Test đỏ |
|---|---|
| Bỏ khoá thiết bị khi bấm | `Two_presses_at_once_leave_exactly_one_open_command_the_newest` |
| Bỏ chặn theo `seq` (D-10) | `A_newer_press_supersedes_and_a_late_report_never_overwrites_a_newer_mode` |
| ACK không lọc theo `node_id` của thiết bị | `A_device_never_sees_or_acknowledges_another_devices_command_in_the_same_commune` |
| Poll ghi audit mỗi lần giao | `The_device_fetches…`, `Two_polls_at_once_deliver_the_command_once` |
| Poll không lưu hết hạn | `An_expired_command_reads_expired…` |
| Nối rơ-le không thay lệnh | `Unwiring_a_relay_supersedes_its_open_command…` |

- OpenAPI: +4 path, +15 schema, + scheme `Device`; không đổi gì sẵn có; Redocly hợp lệ, 8 cảnh báo (= trước).

## Lệch đặc tả (ghi ở LC-11)

`IDEMPOTENCY_CONFLICT` thay `DUPLICATE_OP` cho 409; poll trả object có `server_time`; 5 mã lỗi mới; action audit `reported`;
bị thay do lần bấm mới ghi trong event của lần bấm.

## Sửa sau Codex review code (0 P1 · 3 P2 · 1 P3 — đối chiếu code, cả 4 đúng)

| # | Sửa | Canh bằng (phá thử → đỏ) |
|---|---|---|
| P2 | Tháo + nối lại rơ-le reset `mode_seq` ⇒ ACK trễ của lệnh cũ đè chế độ mới. Nay còn chặn khi **đã có lệnh mới hơn được giao** cho `(node, relay)` — lịch sử lệnh không bị tháo rơ-le xoá | `Rewiring_a_relay_does_not_let_a_late_report…` |
| P2 | Poll vẫn giao lệnh khi `supports_remote_control = false`. Nay registry tắt cờ ⇒ thay mọi lệnh mở (actor Quản lý); poll của thiết bị đã tắt cờ ⇒ thay (actor `system`), không giao | `A_device_no_longer_remote_controlled…` (2 phá thử) |
| P2 | CHECK cho `applied` thiếu `reported_mode` (NULL = x là NULL). Thêm `reported_mode IS NOT NULL`; migration sinh lại (chưa merge) | `The_database_refuses_an_applied_command…` |
| P3 | Test khoá dựa vào may rủi. Thêm test tất định: giữ khoá từ kết nối khác, request phải chờ ≥ 500 ms. Lần đầu dùng `FOR UPDATE` thì test **vẫn xanh khi bỏ khoá** (FK KEY SHARE / UPDATE tự chờ) — đổi sang `FOR NO KEY UPDATE` | `A_press_and_a_poll_wait_for_the_device_lock` (bỏ khoá bấm / poll → đỏ) |

Gộp hai đoạn "đọc qua filter → khoá" (registry + dịch vụ lệnh) thành `LightingCommandService.LockDeviceAsync`.
Sau sửa: **1269/1269 xanh**, Release 0 lỗi, spec OpenAPI không đổi.

## Chưa làm / còn lại

- `luxmap_dev` chưa migrate `AddDeviceCredential` + `AddLightingCommands`.
- Báo Đạt (hợp đồng firmware: `seq` lưu NVS, ACK sau khi thực thi, `server_time`) và WP5 (thay nút ON/OFF mô phỏng bằng preview → bấm → lịch sử).
