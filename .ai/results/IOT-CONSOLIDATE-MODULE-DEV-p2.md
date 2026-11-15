# IOT-CONSOLIDATE-MODULE-DEV — tích hợp dev #258 và publish bước A

Mỹ yêu cầu 15/11/2026: “gửi cho claude review đi xem push được k, đc thì mở pr, đợi ci xanh và merge bằng admin cho t”.
Được duyệt: giải conflict/kiểm bản ghép, Claude review, push, mở PR dev, chỉ merge admin sau CI xanh.
Không deploy, apply DB dev/VPS hoặc hiện thực B/C.

## Nền và conflict

- Latest dev: `839483eab3c110e153249b3b4227f36d3f7b7244`, merge PR #258 POLE-DELETE-QR.
- Source trước ghép: `61bcbea` sau bước A `9ec7815` và sửa 3 P3 Claude.
- `git merge --no-ff --no-commit origin/dev`: chỉ conflict `tracking.html` ở các dòng bảng completed tasks.
- Giữ cả hai dòng IOT implementation/review và dòng POLE-DELETE-QR, mỗi dòng đúng một `<tr>`.
  Card, contract drift và AssetCrudService tự merge; không chọn bỏ một bên.
- IPoleQrCodes/CitizenModule/QrCodeService và hai test QR/citizen giữ byte-identical với dev.
  AssetCrudService chỉ khác XML ownership comment; method body, new QR port, transaction/locks/audit giữ nguyên #258.

## Claude review lại bản đã ghép

Hai lượt Claude Code 2.1.325, **claude-opus-5-5 / high**, restricted/plan, Read/Grep/Glob.
Review frozen staged merge tree `901de2da25c2d13266f3047c1997219ca430a19c` so với dev839483e;
không dùng review9ec cũ để thay thế review tích hợp. Sau snapshot chỉ thêm/cập nhật evidence/tracking; code/tests/solution không khác tree đã review.

Standards: không có P0/P1/P2 hoặc vi phạm chuẩn; 2 P3 cập nhật evidence/base và authorization trên dashboard.
Spec: không có source blocker; 1 P3 trùng việc thay evidence cũ bằng lượt mới.
Đã cập nhật PR body theo base839483e/2320 pass, đánh dấu báo cáo cũ66e1c83 là lịch sử,
và ghi rõ Mỹ đã cho phép publish/admin merge sau CI xanh. Design observations B/C không hiện thực trong A.
Báo cáo nguyên văn: `.ai/reviews/IOT-CONSOLIDATE-MODULE-DEV-by-claude-opus-5-5.md`.

## Kiểm chứng bản ghép — output thật

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ LUXMAP_MQTT_E2E=1 MQTT_CALLBACK_PORT=<isolated> MQTT_PORT=<isolated> dotnet test LuxMap.slnx --disable-build-servers --no-build --no-restore --logger 'console;verbosity=minimal'
Passed!  - Failed:     0, Passed:   427, Skipped:     0, Total:   427, Duration: 575 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 1 s - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    83, Skipped:     0, Total:    83, Duration: 5 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:  1765, Skipped:     5, Total:  1770, Duration: 5 m 39 s - LuxMap.Api.Tests.dll (net10.0)
Full regression exit: 0
```

**2320 pass, 0 fail, 5 skip**; +3 test QR từ dev #258 so với lượt2317 trên base cũ.
Broker EMQX riêng đã dừng sau test; Testcontainers PostGIS của lượt chạy không chạm luxmap_dev.
5 skip ACCT-1/D-6b giữ nguyên. Lượt tích hợp này không gặp race ProfileTests; bug test nền đó chưa được sửa.

```text
$ python3 -m unittest discover -s scripts/tests
Ran 20 tests in 1.195s
OK
$ dotnet ef dbcontext script --project src/LuxMap.Persistence --startup-project src/LuxMap.Api --no-build --output <temp/module-schema.sql>
schema exit: 0
$ dotnet ef migrations has-pending-model-changes --project src/LuxMap.Persistence --startup-project src/LuxMap.Api --no-build
pending exit: 0
$ Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output <temp/module-luxmap-v1.json> src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
swagger exit: 0
Schema byte equal: True
OpenAPI byte equal to latest dev: True
$ git diff --exit-code 901de2da25c2d13266f3047c1997219ca430a19c -- src tests LuxMap.slnx
(no output; exit0)
$ git diff --name-only --diff-filter=U
(no output)
$ git diff --check
(no output)
```

Schema SQL so baseline chung đã xuất trước chuyển module; #258 không đổi EF model/config/migration.
Raw OpenAPI so trực tiếp file trên latest dev839483e; migration/snapshot/spec không có thay đổi của bước A.

## HTTP TCP và publish sạch trên DB tạm

Fresh Debug publish `--no-build --no-restore`, DLL publish chạy riêng cùng PostGIS `iot_module_http`, migrate/seed vào DB tạm.
Thêm GET assets/cabinets để kiểm constructor AssetCrudService cùng IPoleQrCodes/Citizen DI mới của #258.

```json
{
  "database": "iot_module_http",
  "publish_contains_iot": true,
  "publish_contains_telemetry": false,
  "authenticated_routes": {
    "/api/v1/assets/iot-nodes": 200,
    "/api/v1/assets/cabinets": 200,
    "/api/v1/iot/nodes": 200,
    "/api/v1/lighting/commands": 200,
    "/api/v1/map/iot-nodes?bbox=106,10,107,11": 200
  },
  "device_without_credential": 401,
  "swagger_paths": 146
}
```

Đã dừng API và PostGIS tạm. Không còn Telemetry DLL/deps trong bản publish; Device vẫn 401 nếu thiếu credential.

✅ Verified: source review Claude, giải conflict giữ cả hai bên, full regression/MQTT/Python, EF/model/spec và HTTP publish thật.
⚠️ Could not verify: firmware vật lý/FE/mobile/VPS ngoài repo; các gap Bridge đã ghi trước vẫn chưa sửa.
📌 Remaining assumptions: bước A giữ relay/hai transport; B/C chờ chốt; chỉ merge sau khi CI của đúng head xanh.

## Publish / CI / merge

Mỹ đã cấp quyền, không cần hỏi lại.

```text
$ git commit -m 'chore(iot): merge dev after pole QR deletion fix'
2b033d8 chore(iot): merge dev after pole QR deletion fix
$ git push -u origin refactor/IOT-consolidate-module
[new branch] refactor/IOT-consolidate-module -> refactor/IOT-consolidate-module
$ gh pr create --base dev --head refactor/IOT-consolidate-module --title 'refactor(iot): consolidate Telemetry into IOT module' --body-file .ai/results/IOT-CONSOLIDATE-MODULE-pr.md
https://github.com/LuxMapLabs/luxmap_backend/pull/259
$ gh pr view 259 --json state,baseRefOid,headRefOid,mergeable
state: OPEN
baseRefOid: 839483eab3c110e153249b3b4227f36d3f7b7244
headRefOid: 2b033d82ab04681878eb8471409ec72aa55948e5
mergeable: MERGEABLE
```

PR #259 đã được gắn vào chat bằng attach_artifact. CI có hai shard `Merge result / work-orders` và `Merge result / rest`.
Snapshot trên là thời điểm mở PR; commit docs ghi publish tiếp theo không đổi source. CI/head/merge cuối sẽ được xác minh
trực tiếp trước lệnh admin merge; không dùng snapshot pending này để kết luận đã xanh.
