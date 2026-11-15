# IOT-CONSOLIDATE-MODULE — kết quả phase 2-A

## Phạm vi và duyệt

Mỹ thông báo đã merge #257 và yêu cầu làm tiếp trong chat 15/11/2026.
Thực hiện bước A đã đề xuất trong `.ai/results/IOT-CONSOLIDATION-p1.md`:
**hợp nhất module trước, giữ hành vi/schema/API**, không suy ra mọi D-item bước B/C đã được duyệt.
Nền `origin/dev = 66e1c83` (merge #257), branch `refactor/IOT-consolidate-module`.
Task/cấm: `.ai/tasks/IOT-CONSOLIDATE-MODULE.md`.

## Thay đổi và lý do

- Chuyển 26 file C# của Telemetry vào IOT (entity/configuration/registry/lighting/MQTT/job/lookup), namespace sang `LuxMap.Modules.IOT.*`.
- `IotModule` chứa đăng ký cũ của cả hai module: job `node_offline`, hai firmware `VersionedResultColumn`, HTTP/MQTT,
  các port Assets và registry/Bridge. Giữ lifetime, options, tên logical registration, controller và khóa/fixed commune context.
- Gỡ `TelemetryModule`, project Telemetry và solution entry; cập nhật Api/Map/test-host catalogs và project references.
  Project IOT khai các dependency cũ của Telemetry trực tiếp; `MQTTnet` vẫn **5.2.0.1603**.
- Cập nhật tài liệu module và XML ownership comments; không thay method body của các implementation đã chuyển.
- Không tạo migration, đổi snapshot/designer lịch sử, bảng/cột/FK/index/sequence, payload/route/capability/enum/config hay firmware.
  Không thêm shim Telemetry: giữ shim sẽ để lại hai module đăng ký cùng trách nhiệm. Rollback bằng code cũ dùng cùng schema.

Các port `ICabinetDeviceLookup`, `IFeederControlLookup`, `IFeederPowerLookup` vẫn ở Assets; Assets không tham chiếu IOT.
Chiều phụ thuộc: IOT → Assets/Faults/Persistence/Shared; Map/Api → IOT; không có vòng.
Namespace cũ trong migration snapshot/designer là metadata lịch sử, không phải reference tới project Telemetry runtime.

## Bằng chứng build/model/API

```text
$ dotnet build LuxMap.slnx --disable-build-servers -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Đã build nền trước khi sửa và lưu SQL schema/spec vào thư mục evidence tạm; sau chuyển so với chính nền đó:

```text
$ dotnet ef dbcontext script --project src/LuxMap.Persistence --startup-project src/LuxMap.Api --no-build --output <before/after.sql>
schema exit: 0
$ dotnet ef migrations has-pending-model-changes --project src/LuxMap.Persistence --startup-project src/LuxMap.Api --no-build
pending exit: 0
$ Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output <after.json> src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
swagger exit: 0
schema byte equal: True
OpenAPI byte equal: True
$ git diff HEAD --numstat -- src/LuxMap.Persistence/Migrations docs/openapi
(no output)
$ python3 (so 26 moved files với git show HEAD, chuẩn hóa namespace, self-using và XML docs)
26 moved implementations preserved (namespace and XML docs excluded): True
```

Không cập nhật spec chỉ vì đổi namespace: raw spec hiện khớp từng byte với dev, consolidated spec cũng giữ nguyên.
Lint kết quả sinh sẵn vẫn hợp lệ (9 warning như nền dev, cùng CLI @redocly/cli@2.0.0); không tạo API mới để gọi là xong bước A.

## Regression và lỗi test nền

```text
$ dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-build --no-restore --filter 'FullyQualifiedName~CabinetMultiNodeMigrationTests|FullyQualifiedName~DeviceRegistryTests|FullyQualifiedName~LightingCommandTests|FullyQualifiedName~IotDiagnosticsTests|FullyQualifiedName~MqttChannelTests|FullyQualifiedName~BrokerAuthTests|FullyQualifiedName~ScheduledJobTests|FullyQualifiedName~ArtifactVersionTests|FullyQualifiedName~VersionedResultColumnTests'
Passed!  - Failed:     0, Passed:   121, Skipped:     0, Total:   121, Duration: 18 s - LuxMap.Api.Tests.dll (net10.0)
$ python3 -m unittest discover -s scripts/tests
Ran 20 tests in 1.253s
OK
```

Lượt full đầu (MQTT E2E bật qua EMQX riêng) có 1 lỗi, không được giấu bằng skip/filter:

```text
[xUnit.net 00:00:09.46]     LuxMap.Api.Tests.ProfileTests.Every_role_renames_itself_and_gets_the_me_body_back(role: SystemAdmin) [FAIL]
  Failed LuxMap.Api.Tests.ProfileTests.Every_role_renames_itself_and_gets_the_me_body_back(role: SystemAdmin) [32 ms]
  Error Message:
   Assert.Equal() Failure: Strings differ
                                  ↓ (pos 835)
Expected: ···"","name":"bbox-test-89e972c86371455e8072c"···
Actual:   ···"","name":"bbox-test-efe0d5b69e974ca7a5af5"···
                                  ↑ (pos 835)
  Stack Trace:
     at LuxMap.Api.Tests.ProfileTests.Every_role_renames_itself_and_gets_the_me_body_back(UserRole role) in /Users/nhm809/Documents/LuxMap/wt-cabinet-multi-dev/tests/LuxMap.Api.Tests/ProfileTests.cs:line 59
--- End of stack trace from previous location ---
```

Phân tích: dòng 59 so toàn bộ JSON giữa PATCH và GET kế tiếp. Admin hệ thống đọc **mọi xã** trong `AuthService.CommunesForAsync`;
`CommuneAdminTests` đồng thời tạo/xóa/đổi các xã `bbox-test-`. Hai snapshot không bảo đảm bằng nhau trong môi trường test song song.
Bốn file ProfileTests/CommuneAdminTests/AuthService/ProfileService đều byte-identical với dev; chưa sửa logic sản phẩm hoặc test ngoài scope.
Kiểm riêng cả hai class trong cùng lượt:

```text
$ dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-build --no-restore --filter 'FullyQualifiedName~ProfileTests|FullyQualifiedName~CommuneAdminTests'
Passed!  - Failed: 0, Passed: 35, Skipped: 0, Total: 35, Duration: 7 s - LuxMap.Api.Tests.dll (net10.0)
```

Lượt full thứ hai chạy nguyên bộ, không filter bỏ test và vẫn bật MQTT E2E qua EMQX riêng:

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ LUXMAP_MQTT_E2E=1 MQTT_CALLBACK_PORT=<isolated> MQTT_PORT=<isolated> dotnet test LuxMap.slnx --disable-build-servers --no-build --no-restore --logger 'console;verbosity=minimal'
Passed!  - Failed:     0, Passed:   427, Skipped:     0, Total:   427, Duration: 462 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 977 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    83, Skipped:     0, Total:    83, Duration: 5 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:  1762, Skipped:     5, Total:  1767, Duration: 5 m 35 s - LuxMap.Api.Tests.dll (net10.0)
Exit code: 0
```

**2317 pass, 0 fail, 5 skip**. Broker riêng đã được dừng sau test. Kết quả xanh ở lượt cuối không có nghĩa race
ProfileTests đã được sửa; lỗi đầu được giữ lại ở trên và ghi trong traps/testing + tracking.

Năm skip multi-commune ACCT-1/D-6b giữ nguyên từ dev, không thêm skip mới.
Không thêm test chỉ để assert tên project/namespace; các test hành vi hiện hữu kiểm đường gọi thật qua module mới.
Không có guard nghiệp vụ mới trong bước A nên không thêm mutation test lặp lại guard cũ.

## Kiểm HTTP ngoài test host và bản publish sạch

`dotnet publish src/LuxMap.Api --no-build --no-restore --configuration Debug --output <temp/publish>`;
chạy DLL publish qua TCP thật, PostGIS container riêng `iot_module_http`, migrate/seed trong DB tạm, đăng nhập Quản lý.
Kiểm cả file publish/deps manifest để không bị DLL Telemetry cũ trong bin dev che lỗi:

```json
{
  "database": "iot_module_http",
  "publish_contains_iot": true,
  "publish_contains_telemetry": false,
  "authenticated_routes": {
    "/api/v1/assets/iot-nodes": 200,
    "/api/v1/iot/nodes": 200,
    "/api/v1/lighting/commands": 200,
    "/api/v1/map/iot-nodes?bbox=106,10,107,11": 200
  },
  "device_without_credential": 401,
  "swagger_paths": 146
}```

Đã dừng API và PostGIS tạm; không chạy trên luxmap_dev/VPS. Registry/Bridge/lighting/map là controller đã chuyển vào cùng ApplicationPart IOT;
endpoint Device vẫn trả 401 khi chưa cấp credential.

## Review và giới hạn

Review độc lập: `.ai/reviews/IOT-CONSOLIDATE-MODULE-by-reviewer.md`; không còn actionable finding.
Reviewer kiểm source/DI/discovery/graph/migration preservation, tự so 26 implementation và XML cleanup; không lặp build/test của người thực thi.

✅ Verified: build, schema/spec so byte, không pending model changes, full regression 2317 pass/5 skip, focused regression/Python, publish sạch và HTTP thật trên DB tạm, review độc lập.
⚠️ Could not verify: firmware vật lý, FE/mobile/VPS ngoài repo; ProfileTests có race snapshot xã trong lượt tổng đầu.
📌 Remaining assumptions: bước A chưa gỡ relay, siết một feeder/node hoặc chuyển transport; D-1…D-8 cho B/C cần chốt trước thay đổi tương ứng.
VR-10…12, IOT-CONS-1 và backup bridge_data vẫn là việc tiếp theo, không được coi đã sửa bởi chuyển module.

Không sửa workspace gốc hoặc AGENTS.md symlink. Chưa push/deploy/apply DB dev; chuẩn bị PR A riêng sau khi hoàn tất kiểm chứng.
