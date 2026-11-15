## Summary

Registry, lighting, firmware và job offline đang ở Telemetry, còn Bridge diagnostics ở IOT. Hợp nhất trách nhiệm backend vào một IotModule để chuẩn bị bước một node–một feeder.

```diff
 Api / Map / test catalogs
- Modules.Telemetry → Assets / Faults / Persistence
- Modules.IOT → Telemetry
+ Modules.IOT → Assets / Faults / Persistence

+ IotModule: registry + lighting + HTTP/MQTT + firmware + offline job + Bridge
 Assets-owned lookup ports giữ nguyên
```

Chuyển 26 implementation, giữ method body; gỡ project/module Telemetry, chuyển MQTTnet 5.2.0.1603 sang IOT và cập nhật references/docs.
Bước A giữ hai đường điều khiển và relay hiện tại; binding một node–một feeder, cutover Bridge và gỡ legacy là bước B/C riêng.

## Evidence

- **Before:** runtime cần cả TelemetryModule và IotModule để có đủ entity/configuration, controllers, job và version registration.
  **After:** chỉ IotModule; fresh publish không có DLL/deps Telemetry. API TCP thật trên PostGIS tạm trả 200 cho registry/Bridge/lighting/map, Device thiếu credential vẫn 401, Swagger đủ 146 paths.
- SQL schema và raw OpenAPI **byte-identical** với dev **839483e (sau PR #258)**; EF `has-pending-model-changes` exit 0; migration/snapshot/spec không có diff.
- Build 0 warning/error; full regression **2320 pass, 0 fail, 5 skip nền dev** với MQTT E2E; 20 Python pass. Kết quả bản ghép mới tại `.ai/results/IOT-CONSOLIDATE-MODULE-DEV-p2.md`; kết quả2317/121 trước đó được giữ dưới nhãn lịch sử ở báo cáo cũ.
  Claude Opus 5.5/high review lại đúng staged merge với #258, không có source blocker; P3 về evidence/authorization đã cập nhật. Review cũ cũng không còn finding; 26 implementation khớp sau chuẩn hóa namespace/import/XML docs. Claude Opus 5.5/high kiểm hai trục: Spec 0 finding; Standards 3 P3 chú thích/docs đã sửa và được Claude follow-up xác nhận, không có P0–P2.
- Lượt full trên **nền cũ66e1c83** từng có một ProfileTests/SystemAdmin race ở so JSON chứa danh sách xã đang được test khác sửa; các file đó không đổi so với dev.
  Kiểm riêng ProfileTests + CommuneAdminTests: 35 pass; lượt tích hợp839483e có2320 pass, không gặp lại lỗi. Giữ bằng chứng lỗi lịch sử; không thêm skip hoặc sửa test ngoài phạm vi.

Chỉ conflict `tracking.html` khi ghép #258, đã giữ đủ các dòng hai bên. QR deletion/DI/locks/audit và tests của #258 giữ nguyên; không có conflict code.

## Merge Danger

**Door:** two-way

Chỉ chuyển code/registration/dependency cùng phiên bản. Không có migration hoặc thao tác chuyển/xóa dữ liệu; code cũ dùng được cùng schema.
Giữ lịch sử migration, credential, FK/index/sequence/job names, scope, audit, API và firmware protocol.

**Blast Radius:** Backend

Composition root, Map, các test-host catalogs và nơi đăng ký HTTP/MQTT/job/version chuyển sang IOT.
Các khoảng trống Bridge firmware/last-report/backup và quyết định B/C vẫn còn; việc gỡ project không xóa dữ liệu telemetry hoặc relay.
Chưa apply DB dev/VPS, deploy hoặc kiểm firmware vật lý/FE/mobile ngoài repo.
