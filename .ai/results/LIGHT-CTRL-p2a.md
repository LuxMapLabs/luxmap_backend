# LIGHT-CTRL — Phase 2a (đăng ký thiết bị + rơ-le + bí mật + scheme `Device`) · 08/10/2026

Nhánh `feat/LIGHT-CTRL-2a` (mang theo đặc tả + quyết định Phase 1, một PR). Đặc tả `.ai/results/LIGHT-CTRL-p1.md`, drift LC-1…LC-10.
⚠️ **Nền tạm:** SELF-SIGNED, chạm API. Lệnh ON / OFF / AUTO là **2b**.

## Đã làm

| Phần | File |
|---|---|
| Bí mật + hằng | `LuxMap.Shared/Authorization/DeviceAuth.cs` (`DeviceAuth`, `DeviceSecret`) |
| Scheme + policy | `LuxMap.Api/Authorization/DeviceAuthenticationHandler.cs`; `AuthorizationSetup` (scheme không mặc định, policy `DeviceOnly`) |
| Cột | `iot_node.credential_hash`, `credential_set_at` + `ck_iot_node_credential_set_together` — migration `AddDeviceCredential` |
| API | `Telemetry/Registry/` — `IotNodeRegistryController`, `IotNodeRegistryService`, `IotNodeContracts` (7 thao tác) |
| Mã lỗi | `FEEDER_NOT_IN_CABINET` |
| Test | `DeviceRegistryTests` (14), `DeviceProbeController` + `DeviceProbeFactory` (endpoint thiết bị chỉ trong test) |
| Kéo theo | `JsonElementFieldSchemaFilter`, OpenAPI + generator, `CLAUDE.md`, drift LC-10, `tracking.html` |

## Bằng chứng

- Migration chỉ `AddColumn` ×2 + `AddCheckConstraint`; apply trên `luxmap_test` sạch.
- **1238/1238 xanh** (351 + 44 + 46 + 797; +14 test mới).
- **Phá thử — đỏ đúng test:**

| Phá | Test đỏ |
|---|---|
| So bí mật luôn đúng | `Every_wrong_credential_is_the_same_401_and_a_rotated_secret_stops_working` |
| Policy `DeviceOnly` không ghim scheme `Device` | `Every_wrong_credential…`, `A_device_authenticates…`, `A_bearer_token_cannot_reach_a_device_endpoint…` |
| Bỏ kiểm "mạch cùng trụ" | `Relays_wire_only_within_the_cabinet…` (FK DB trả 500) |

- OpenAPI so với `origin/dev`: +4 path, +8 schema, không đổi gì sẵn có; Redocly hợp lệ, 8 cảnh báo (= trước).

## Chưa làm / còn lại

- 2b: `lighting_request`, `lighting_command`, endpoint lệnh (người dùng + thiết bị), `seq`, audit actor `System`.
- `luxmap_dev` chưa migrate `AddDeviceCredential`.
- `copy_dev_to_supabase.py` chép nguyên `iot_node.credential_hash` (chỉ là băm; bí mật dev vẫn dùng được với bản chép) — chấp nhận
  cho pilot, xét lại khi có thiết bị thật.
