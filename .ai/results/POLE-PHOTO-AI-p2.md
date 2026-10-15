# POLE-PHOTO-AI — kết quả PHA 2

Ngày: 15/10/2026 · Người thực hiện: Codex · Branch: `feat/POLE-PHOTO-AI` · HEAD gốc: `1ee993a`.

Thực hiện theo quyết định của Mỹ và Pha 2 trong `.ai/tasks/POLE-PHOTO-AI.md`, sau khi đọc P1, nguồn/commands,
quy ước repo, authorization guide và các đường upload/AI/publication hiện có. **SELF-SIGNED, nền tạm tới FW**;
WP5/WP6 **chưa báo**. **Hoàn tất Pha 2, chờ Claude review diff.** Build -warnaserror và full suite locale gốc đều exit0: **1.545 pass, 2 MQTT skip**; 9 chốt phá thử đỏ sau khi tăng độ mạnh bài dedup.

Không sửa Contract văn bản; không thêm NuGet, không commit/push/stage.
Không in/sửa/chép `.env`; chỉ host/test/tooling nạp cấu hình bình thường. Không chạy API hay MinIO thật ngoài test,
không chạy database update/psql vào `luxmap_dev`.

## 1. Đã hiện thực

- Sáu thao tác dưới `/api/v1/work-orders/{id}/pole-photos`: **POST upload**, **GET list**, **GET detail**, **GET thumbnail**,
  **GET original**, **POST decision**. Upload multipart `pole_id/photo/captured_at/lat/lng/client_op_id` bắt buộc;
  201 mới, 200 replay; read theo phiếu cha + xã ảnh, media proxy no-store. Capability upload `ExecuteWorkOrders`,
  read `ReadWorkOrders`, duyệt **`ReviewSurveys` chỉ Quản lý**.
- Nhãn riêng **`cv_state = on | off | inconclusive`**, null khi `ai_status=unavailable`; không dùng fixture_status làm
  nhãn AI, không trả dim từ ảnh. `ISingleLampPhotoHinter` ở Shared, adapter lazy ở AI, Citizen ủy quyền và giữ nguyên
  nhãn public + hash cq. Không sửa `/ai/detect`. Một nguồn luật daylight/single-lamp và cấu hình Citizen hiện có.
- Bảng `work_order_pole_photo`: prefix **PPH**, snapshot nguồn từ cột, ảnh nguyên byte + thumbnail sạch trong bucket evidence;
  ICommuneScoped/IAudited, unique user/UUID, hash metadata chuẩn hoá + bytes, bộ AI/quyết định có CHECK, FK đầy đủ.
  `fault.origin_pole_photo_id` nullable với FK ghép ảnh/pole/xã. Không biến Pole.CommuneId thành EF key.
- Tập cột upload và WO-12 dùng chung **`WorkOrderPoleService.Targets`**: tuyến phiếu ∪ cột mang fault, không phụ thuộc
  trang đầu, giữ luật liên xã. Chỉ assignee, ba loại phiếu, in_progress nhận mới; replay sau complete vẫn được khi còn quyền.
- Original bounded 16 MiB, body 17 MiB kể cả không Content-Length, Identify JPEG-only **40 MP trước decode** kể cả thiếu model;
  timestamp phải có Z/offset, số hữu hạn, từ chối form/trường server-owned lạ/lặp. Magic quyết JPEG, không tin tên/MIME.
  Caddy thêm guard 17 MiB riêng POST route mới để giữ 413 khi API từ chối sớm; có test container Caddy thật.
- Upload → AI **chưa đổi trạng thái**. Quản lý confirm/reject một lần; confirm inconclusive/unavailable →409.
  Confirm OFF mới → out; ON → normal khi out/unknown/chưa có row; ON giữ dim/normal + confidence cũ.
  Writer **`PoleStatusPublication.StageAsync` dùng chung với công bố sweep**; WorkOrders gọi qua port `IPolePhotoPublication`
  do Survey hiện thực, không thêm project reference WorkOrders → Survey/AI/Citizen.
- Chỉ áp capture mới hơn `LastEvaluatedAt ?? LastSeenAt`; cũ/bằng vẫn ghi confirm + audit nhưng false/not_newer và không sinh fault.
  Ảnh mới giữ classification vẫn tiến clock; sau áp ảnh LastSweepId/LastRunId=null. Không ghi history/baseline.
  OFF mới sinh tối đa một **lamp_out/confirmed**, CV, nguồn/xã/tuyến từ cột, severity cùng cấu hình survey, truy vết phiên bản + ảnh;
  kiểm effective type và **FaultStatusSets.Open** dưới cùng khoá pole với sweep. ON không đóng fault.
- Thứ tự khoá **phiếu → pole → photo**, storage/model ngoài transaction, recheck sau khoá. Upload hai save submitted(User),
  analyzed(Cv) trong một commit; decision/publication/fault mỗi save một event trong một transaction. Lỗi save thứ hai rollback
  cả ảnh + audit đầu; GET/replay không infer/audit lại.

Điểm vào để review:

| Phần | File chính |
|---|---|
| HTTP/quyền/giới hạn | `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotosController.cs`, `PolePhotoBodyLimitAttribute.cs` |
| Upload/replay/read/decision | `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:56`, `:162`, `:186` |
| Membership chung | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:78` |
| Writer chung / áp ảnh | `src/LuxMap.Modules.Survey/Review/PoleStatusPublication.cs:11`, `ReviewedPolePhotoPublisher.cs:20` |
| Luật AI + Citizen | `src/LuxMap.Modules.AI/Detection/YoloSingleLampPhotoHinter.cs:16`, `src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:16` |
| Schema | `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs`, migration `20261015153829_AddWorkOrderPolePhotos` |
| Kiểm ảnh trước decode | `src/LuxMap.Infrastructure.Storage/OriginalJpegReader.cs:12` |
| Proxy | `deploy/caddy/Caddyfile:30`, `tests/LuxMap.Api.Tests/PolePhotoProxyTests.cs` |

## 2. Migration, vận hành và tài liệu

Đã đọc Up/Down sinh ra trước lượt test apply, rà các phần chỉ thêm, rồi bổ sung FK/unique DB-only cho pole-commune.
Up thêm bảng/sequence/index/CHECK/FK và một cột nullable trên fault; không UPDATE/backfill dữ liệu cũ.
Down gỡ FK fault → ảnh trước khi drop ảnh, rồi cột/sequence/unique target. **Down mất metadata/kết quả/quyết định và nguồn ảnh
của fault**, không dọn bytes/audit. Môi trường có dữ liệu nên rollback ứng dụng và giữ schema; chưa apply vào môi trường thật.

`PolePhotoMigrationTests` dựng **container PostGIS riêng, DB trống**, apply toàn bộ → down về CommuneMapFrame → reapply,
assert catalog FK. Host tích hợp dùng cơ chế BE-36 (container riêng mỗi lượt, guard data source), không dùng DB dev.
Caddy test cũng container riêng với upstream không tồn tại, chỉ kiểm guard ở proxy; không gọi dịch vụ thật.

Đã thêm PLAN copy sau các cha, trước fault/audit, lọc ảnh theo xã + pole + phiếu + người tải/người duyệt được giữ;
fault có nguồn ảnh chỉ copy khi ảnh được giữ; work_order_fault, repair_evidence và citizen_report/submission/photo cũng theo tập fault được giữ. Chỉ import module để kiểm PLAN, **không chạy copy vào DB**. Bytes MinIO vẫn cần
chuyển riêng khi vận hành. Teardown test dọn fault → ảnh → phiếu/pole/user, audit trước actor/xã.

Drift **PP-1…PP-10** đăng ký Mỹ / 15-10 / SELF-SIGNED / nền tạm / consumer chưa báo. Bổ sung ràng buộc cuối CLAUDE,
ERD, hướng dẫn Caddy, thẻ đầu tracking; xuất lại hai spec và generator. Đã dùng skill `writing-for-agents` để rà phần CLAUDE.

## 3. Kiểm chứng hành vi thật

HTTP test chạy host .NET thật + PostgreSQL/PostGIS thật. Kho ảnh thay bằng **PhotoStore trong RAM chạy image pipeline thật**,
không coi đó là kiểm S3/MinIO thực. Có chạy model ONNX thật cho hồi quy version/adapter; có host thật với model path thiếu để
chứng minh upload vẫn 201/unavailable. Không khẳng định precision/recall hoặc độ chính xác bằng ảnh tổng hợp.

| Nhóm | Đã kiểm |
|---|---|
| Quyền | Bốn vai trò, vô danh, kỹ sư khác assignee, read/list/media cùng cha, ảnh khác parent, ảnh xã ngoài quyền dù còn thấy phiếu |
| Scope | Ba loại phiếu, chỉ in_progress nhận mới; pole ngoài tập/ngoài xã; cột qua fault, đường xã khác, hơn 200 cột; snapshot calibration_rig đúng xã cột |
| AI / publication | Giữ dim/normal, out/unknown/chưa row →normal, off→out; upload không đổi status/fault; không history/baseline; ON không đóng fault |
| Review | Inconclusive/null không confirm, reject được; ảnh cũ/bằng không ghi đè/sinh fault; lần hai 409, hai decision đồng thời 200/409 |
| Chống trùng | OFF nhiều ảnh chỉ một fault; replay bytes/metadata/key; đổi từng lat/lng/time/bytes/pole →409; concurrent một row/hai audit, GET/replay không infer; replay sau complete, giao lại thì 404 |
| Media | Magic, JPEG hỏng, original nguyên byte vòng tròn, no-store; file đúng 16 MiB nhận, vượt file/body trả413, có/không Content-Length; JPEG header 65535² bị chặn trước decode khi model vắng |
| Khoá/atomicity | Giao lại khi ghi object, đóng phiếu khi inference, không giữ lock qua các bước đó; decision chờ FOR NO KEY UPDATE trên pole; save phân tích lỗi rollback ảnh + audit |
| Audit/schema | IAudited từ chối 0/2 event, interceptor kiểm một event/save; User/Cv đúng; NaN/Infinity/null sai bộ AI/nhãn dim bị CHECK; FK pole-commune catalog + SQL sai xã bị từ chối |
| Hạ tầng/hồi quy | Apply/down/reapply container riêng, Caddy 413 thật, hash Citizen đúng `yolo-…+cq-561d1e0631ae`, OpenAPI required fields/labels/quyền, toàn suite hiện có |

**Lỗi phát hiện trong kiểm chứng, đã xử lý:**

1. Build đầu lộ EF1002 ở hai SQL cấp ID; sửa theo khuôn registry SQL tin cậy, không suppress analyzer.
2. Test mới gửi ghi chú complete quá ngắn rồi gửi `fault_outcomes=[]` cho phiếu không có fault: lỗi dữ liệu test, đã sửa.
3. Swagger 500 do FromForm trên IFormFile; bỏ attribute này ở file, giữ binding mặc định giống controller hiện có.
4. Scanner JPEG-only cần biến DecoderOptions rõ ràng; reader vẫn dùng JpegOnly, sửa về khuôn scanner kiểm được.
5. Full suite đầu còn lỗi WKT của **test hiện có**: locale dấu phẩy làm `108.8` thành `108,8`. Đã sửa đúng 8 biểu thức
   nội suy WKT sang `FormattableString.Invariant` trong CabinetTests/TopologyInferenceTests/PoleNoteTests theo quy ước culture
   ở CLAUDE; không sửa nghiệp vụ Assets. Có một lượt full xanh với en_US trước sửa; lượt nghiệm thu cuối dùng **locale gốc**.
6. Test proxy đầu dùng sai đích ResourceMapping (`…/Caddyfile` thay vì thư mục), rồi client gửi body khi proxy đã đóng sớm;
   sửa mapping thư mục và dùng Expect:100-continue để kiểm **từ chối Content-Length trước body** đúng mục tiêu test.

## 4. Phá thử thật

Mỗi lượt sửa tạm đúng một chốt, chạy test tương ứng trên container test mới, đọc mã thoát, khôi phục bằng ghi lại file
(đổi mtime, không dùng mv bản sao cũ), rồi build/test lại mã nguyên vẹn. **Đỏ dưới đây là test fail thật, không phải lỗi compile.**

| Mã | Phá | Test phát hiện / kết quả |
|---|---|---|
| M1 | Bỏ membership WO-12 | Membership… đỏ: mã POLE_NOT_IN_WORK_ORDER biến mất, exit1 |
| M2 | Cho ON ghi normal lên dim | Confirmation… nhánh Dim đỏ: expected Dim, actual Normal, exit1 |
| M3 | Bỏ kiểm mới hơn ở áp ảnh | Old_or_equal… cả hai nhánh đỏ: expected false, actual true, exit1 |
| M4 | Bỏ kiểm fault mở | Concurrent_OFF… đỏ: expected1, actual2 fault, exit1 |
| M5 | Bỏ khoá pole trước decision | Decision_waits… đỏ dưới FOR NO KEY UPDATE, exit1; FK KEY SHARE không che chốt thiếu |
| M6 | Bỏ recheck in_progress sau inference | Rechecks_state… đỏ: expected409, actual201, exit1 |
| M7 | Bỏ so hash đầy đủ khi replay | Replay… đỏ: expected409, actual200, exit1 |
| M8 | Đổi prefix hash cq thành pp | Citizen_hash… đỏ: expected yolo-…+cq-561d1e0631ae, actual …+pp-561d1e0631ae, exit1 |
| M9 | Bỏ audit submitted | Simultaneous_identical… đỏ: expected200/201, actual500/500 do guard, exit1 |

**Lượt M4 đầu SỐNG (exit0)**: hai ảnh đồng thời có thể để ảnh mới hơn thắng trước, ảnh cũ bị chronology chặn nên một fault dù
đã bỏ dedup. Đã bổ sung assert số fault sau **ảnh thứ ba chắc chắn mới hơn**; chạy phá lại đúng M4 cho expected1/actual2 đỏ.
Không báo lượt đầu là đã bắt được. Mã cuối đã khôi phục đủ chín chốt.

## 5. Bằng chứng terminal và mã thoát

Các khối dưới là output thật (rút gọn phần build liệt kê assembly), không suy từ chữ “Passed” khi chưa đọc exit.

```console
$ dotnet build LuxMap.slnx -warnaserror

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.02
EXIT_CODE=0
```

```console
$ ConnectionStrings__LuxMap='Host=127.0.0.1;Port=1;Database=pole_photo_design;Username=design;Password=design' Cors__AllowedOrigins__0=https://localhost:3000 dotnet ef migrations add AddWorkOrderPolePhotos -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations
Build started...
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
EXIT_CODE=0
```

```console
$ dotnet tool restore
Tool 'swashbuckle.aspnetcore.cli' (version '10.2.3') was restored. Available commands: swagger

Restore was successful.
EXIT_CODE=0
```

```console
$ Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 ConnectionStrings__LuxMap='Host=127.0.0.1;Port=1;Database=pole_photo_design;Username=design;Password=design' dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/wt-pole-photo/docs/openapi/luxmap-v1.json
EXIT_CODE=0
```

```console
$ python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=102 implemented_ops=132 not_implemented_ops=0 schemas=244
EXIT_CODE=0
```

```console
$ npx --yes @redocly/cli@2.0.0 lint docs/openapi/luxmap-v1.5.json

docs/openapi/luxmap-v1.5.json: validated in 80ms

Woohoo! Your API description is valid. 🎉
You have 8 warnings.
EXIT_CODE=0
```

```console
$ python3 -m unittest discover -s scripts/tests
...................
----------------------------------------------------------------------
Ran 19 tests in 1.118s

OK
EXIT_CODE=0
```

```console
$ dotnet test tests/LuxMap.Api.Tests --filter FullyQualifiedName~PolePhotoProxyTests
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-photo/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=0
```

Chuỗi giả của EF/Swagger là cấu hình tooling không có máy chủ lắng nghe, không trích từ `.env`; các CLI này không apply DB.

Output phá thử (tóm tắt nguyên dòng terminal):

```text
M1-membership
Expected: "POLE_NOT_IN_WORK_ORDER"
Actual:   null
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 937 ms - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M2-preserve-dim
Expected: Dim
Actual:   Normal
Failed!  - Failed:     1, Passed:     5, Skipped:     0, Total:     6, Duration: 4 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M3-chronology
Expected: False
Actual:   True
Expected: False
Actual:   True
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M4-dedup
Expected: 1
Actual:   2
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M5-pole-lock
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M6-recheck
Expected: 409
Actual:   201
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 941 ms - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M7-replay-hash
Expected: 409
Actual:   200
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M8-citizen-hash
Expected: "yolo-d8bd77cde3e14d64+cq-561d1e0631ae"
Actual:   "yolo-d8bd77cde3e14d64+pp-561d1e0631ae"
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 737 ms - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M9-audit
Expected: <generated>                          [200, 201]
Actual:   ImplicitlyStableOrderedIterator<int> [500, 500]
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 962 ms - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=1

M4-dedup — lượt đầu trước khi tăng độ mạnh test
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=0
```

Lượt nghiệm thu cuối **không đặt LC_ALL/LANG**, chạy locale gốc của máy sau khi sửa WKT test:

```console
$ dotnet test LuxMap.slnx --logger "trx;LogFileName=pole-photo-complete.trx"
Passed!  - Failed:     0, Passed:   384, Skipped:     0, Total:   384, Duration: 469 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 683 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    46, Skipped:     0, Total:    46, Duration: 3 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
  Skipped LuxMap.Api.Tests.MqttEndToEndTests.A_device_receives_executes_and_acknowledges_over_the_real_broker [1 ms]
  Skipped LuxMap.Api.Tests.MqttEndToEndTests.The_broker_enforces_identity_and_each_devices_own_topics [1 ms]
Passed!  - Failed:     0, Passed:  1070, Skipped:     2, Total:  1072, Duration: 2 m 3 s - LuxMap.Api.Tests.dll (net10.0)
EXIT_CODE=0
```

Tổng 1.547 ca = **1.545 pass, 0 fail, 2 skip**. TRX cuối nằm dưới `tests/*/TestResults/pole-photo-complete.trx`.
Trong đó **53 ca API mới** (photo/migration/proxy/OpenAPI) và 1 ca prefix mới đều pass. Hai skip là broker E2E hiện có,
chỉ bật khi `LUXMAP_MQTT_E2E=1` + EMQX; không giả báo hai ca đó đã chạy. Benchmark bị loại theo `luxmap.runsettings`
hiện có; không dùng filter để bỏ test thường nào. Không đổi cấu hình test để che lỗi culture.

Kiểm PLAN chỉ đọc module, không kết nối DB (output thật):

```text
PLAN: unique tables, parent → photo → fault → audit order: OK
```

Bổ sung sau review PLAN: `scripts/tests/test_pole_photo_copy_plan.py` thực thi **chính predicate PLAN** trên schema SQLite
trong RAM có qualifier public, với ảnh được giữ/bị loại, fault gốc ảnh/fault thường và từng lớp con. Chứng minh loại ảnh
không để work_order_fault/evidence/báo cáo công dân giữ FK tới fault bị loại; không viết lại predicate bằng Python,
không kết nối DB nguồn/đích. Bộ test script cuối có **20 pass** (thay cho 19 trước bổ sung).

```console
$ python3 -m unittest discover -s scripts/tests
....................
----------------------------------------------------------------------
Ran 20 tests in 1.051s

OK
EXIT_CODE=0
```

## 6. D-item phát sinh / chi tiết chọn theo quyền được giao

Đã chọn cách an toàn và đăng ký PP tương ứng, **không âm thầm mở rộng tính năng**:

| Mã | Chọn / căn cứ |
|---|---|
| D-P2-1 | Tên `cv_state`, `ai_status=available/unavailable`; ReviewSurveys hợp nghĩa duyệt quan sát CV, không mở cho kỹ sư. PP-2/9. |
| D-P2-2 | ON giữ dim/normal **và confidence cũ**, vẫn tiến mốc quan sát; `status_applied=true, classification_preserved` gồm thay clock. Tránh ảnh OFF cũ hơn ON mới ghi đè. PP-10. |
| D-P2-3 | Xoá liên kết current-status tới sweep/run khi áp ảnh (null), không giả nguồn sweep cho ảnh. History/baseline giữ nguyên, không backfill. PP-10. |
| D-P2-4 | Ảnh OFF cũ/bằng vẫn confirm nhưng **không sinh fault**; chặn tác dụng phụ từ quan sát đã cũ. Target đổi source/xã sau upload thì confirm409, reject vẫn được. PP-9/10. |
| D-P2-5 | Code sweep thật tạo **detected**, khác câu “confirmed theo cùng luật sweep” trong D-5d. Theo yêu cầu tường minh của Mỹ: fault từ ảnh tạo **confirmed**, có ConfirmedBy/At; không tự sửa sweep. Dùng chung dedup/effective type/open set/severity và khoá. PP-10. |
| D-P2-6 | in_progress chỉ giới hạn **nhận mới**; Quản lý có thể duyệt ảnh đã nhận sau khi phiếu complete. Quyết định một lần, không là hàng chờ sync/replay mới. PP-3/9. |
| D-P2-7 | Bổ sung guard Content-Length17 MiB vào Caddy vì bộ lọc mới từ chối sớm; test proxy riêng, không sửa stack đang chạy. Sửa 8 nội suy WKT test cũ theo quy ước culture để full suite chạy được locale gốc. Không đổi nghiệp vụ ngoài ảnh. |

✅ **Đã xác minh:** build toàn solution -warnaserror, full test thật và exit, HTTP/DB/model mất/atomicity/schema,
apply/down/reapply DB trống riêng, proxy Caddy thật, hash Citizen, 9 phá thử, spec/generator/lint, PLAN và diff.

⚠️ **Chưa xác minh / không nằm trong lượt này:** hai MQTT E2E bị skip; S3/MinIO thật, triển khai VPS/HTTPS thực,
413 chunked qua Caddy tới Kestrel thật (unknown-length đã kiểm ở host HTTP test; proxy thật kiểm declared-length với Expect:100-continue),
độ chính xác/latency model trên bộ ảnh thực WP4, chuyển dữ liệu/bytes lên môi trường đích. Object mồ côi sau DB/replay thua vẫn là nợ BE-35.

📌 **Còn lại:** Claude review diff rồi commit; Mỹ chuyển PP-1…PP-10 cho WP5/WP6 và FW xác nhận nền tạm.
Không gửi thông báo hay push trong lượt Codex. Không còn bước triển khai Pha 2 chờ tự chọn kiến trúc; các chi tiết trên được ghi để review.

## 7. Review của Claude (15/10/2026) — hai sửa sau Pha 2

- **500 chập chờn ở `POST …/decision`:** lượt chạy toàn bộ đầu tiên đỏ 1 test (`Concurrent_OFF_photos_and_concurrent_decisions_leave_one_open_fault`,
  correlation `573a7759-…`). Log server: `InvalidOperationException: No property found that maps to constructor parameter 'Note' for type
  'LuxMap.Modules.WorkOrders.PolePhotoDecisionRequest'`. Chạy riêng test đó xanh 10/10. Đổi `PolePhotoDecisionRequest` từ positional record
  sang `sealed class` + `init` (khuôn `WorkOrderRequests.cs`); ghi bẫy vào `CLAUDE.md`. Sau sửa: **2 lượt toàn bộ liên tiếp mã thoát 0**
  (Api 1070 + 2 skip MQTT, Storage 46, Persistence 45, Shared 384).
- **Audit áp trạng thái cột** dùng `AuditAction.Completed` trên `work_order` — đọc như phiếu đã hoàn thành. Đổi sang `AuditAction.Applied`
  (đã có trong `ck_audit_event_action`, không cần migration).
- Kiểm thêm: commit refactor port AI đứng riêng build sạch và 49 test Citizen xanh tại commit đó.
