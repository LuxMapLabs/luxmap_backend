# BE-15 Phase 2c — kết quả thực thi Codex

Ngày 04/10/2026 · worktree `luxmap_be15_p2c` · nhánh `feat/BE-15-survey-review`.
Nền API **SELF-SIGNED, tạm tới FW**, theo các quyết định ở Phase 1 và task P2c.
Không commit/push theo yêu cầu. Không đánh dấu đã nghiệm thu PostGIS từ kết quả unit test.

## 1. Phần đã hiện thực

- `Survey/Review/SurveyReviewService.cs` và controller: results theo run thành công (mặc định attempt mới nhất),
  phân trang tối đa 200; review accept/return, note bắt buộc khi return; idempotency có hash actor/body/ID,
  optimistic version, kiểm scope trước retry. GET sweep/list bổ sung `version` để client gửi expected_version.
- Quyền: capability có sẵn; scope/assignee của work order cha, tuyến được giao và sweep; đọc riêng tập xã của
  observation thuộc run đã xác thực để không công bố thiếu một phần run liên xã. Review thiếu xã →403;
  results/thumbnail ngoài quyền →404. Thumbnail stream JPEG qua API, kiểm xã GIS snapshot và các run trước
  mở object; không presigned URL, không gọi store trong các lượt kiểm thực thi ở đây.
- Công bố trong một transaction: khoá work order → sweep → pole theo ordinal ID; audit quyết định ở xã neo;
  mỗi xã một SaveChanges cho history/current/baseline cùng một audit chỉ chứa pole của xã đó; mỗi fault mới
  một SaveChanges + một audit actor `cv`. Không nới audit guard/backdoor. Không tự hoàn thành phiếu.
- Một history/cột/sweep. Luật chọn: known, CV confidence, association confidence, ít quality flags, thời gian,
  observation ID; không dùng độ lớn lux để xếp hạng. Mâu thuẫn ON/OFF →unknown, null confidence/ratio,
  không xét dim/member/fault. evaluated_at lấy observed_at của đại diện.
- D-08: chỉ thời điểm khảo sát mới hơn mới ghi current status và sinh fault; bằng/cũ hơn vẫn lưu history.
  Khi gặp current cũ chưa có last_evaluated_at, dùng last_seen_at nếu có làm mốc bảo thủ.
- Fault CV theo out/dim, mang data_source, observation nguồn, model version; priority_score null.
  Dưới khoá pole, tra `FaultStatusSets.Open` với loại hiệu lực override trước khi tạo. Không đóng fault cũ.
  Severity cấu hình tạm Medium/Low; cột nhạy cảm tăng một bậc tới High.
- Baseline version bất biến + member: accepted run, cùng cột/nguồn/chiều; tối đa một member chất lượng mỗi
  run/chiều; on, known, peak hữu hạn không âm, không shared/paired/gap/saturated/ambiguous. Đủ số member
  cấu hình (mặc định 3), trung vị dương mới tạo baseline. Peak 0 vẫn là member; baseline 0 không làm mẫu số.
- `SurveyBaselineLookup` thay Empty lookup; xét **từng member**, không chỉ count cache: accepted run,
  khác sweep đang chấm, xảy ra trước lúc bắt đầu sweep, cùng nguồn/chiều. Worker dùng chính DbContext
  với scope hữu hạn của job. Lưu baseline ID/value trên observation, không chấm lại lúc review.
- Schema: `luminance_baseline`, `baseline_member`, `luminance_history`; trường review trên sweep,
  last_evaluated_at/last_run_id trên current, origin_observation_id trên fault; baseline ID/value trên observation.
  FK Restrict; FK ghép giữ history/nguồn fault cùng pole/xã/run và accepted run thuộc sweep.
  Bảng gốc có ICommuneScoped, ba bảng mới IImmutableRecord + IAudited; CHECK số hữu hạn, confidence, enum.
  CHECK confidence 0..1 của current đã có sẵn, giữ nguyên.
- Xoá stub thumbnail và schema binary inline đi kèm khỏi generator, **không chạy generator/Swagger**.
  README/hướng dẫn survey/drift/tracking đã cập nhật. Không sửa Contract hoặc JSON OpenAPI.

## 2. Migration đã đọc

`20261003172809_AddSurveyPublication` (một migration, Designer + snapshot).
Sinh bằng EF CLI hiện có, host thật; working directory/contentRoot ở `/private/tmp/be15-p2c-design`,
Production, config reload tắt; connection string giả trỏ port 1 và các placeholder cấu hình chỉ cấp cho
lệnh design-time. Không IDesignTimeDbContextFactory, không sửa Program, không mở DB/MinIO.

Đã đọc toàn bộ Up/Down sinh ra; thêm ba trigger gọi `luxmap_reject_processing_mutation()` (hàm có từ P2b,
chứa cơ chế `luxmap.audit_purge` cho teardown); ghi tên migration vào danh sách miễn AuditGuardTests.
Down được bổ sung guard từ chối nếu có history/baseline hoặc quyết định review. Rollback ứng dụng nên
bảo toàn dữ liệu; không dùng Down để xoá dữ liệu khảo sát.

Kiểm đọc: Up không Drop/Alter ngoài phạm vi; giữ index commune; không cột xmin vật lý; không default enum
làm Normal thành Unknown; CHECK known đòi cv_state IS NOT NULL để tránh SQL NULL vượt CHECK.
Down bỏ FK trước bảng/AK, bỏ các cột/index/constraint tương ứng; chưa chạy SQL trên Postgres.

Output `/tmp/be15-p2c-migration.log`:

```text
Using application service provider from Microsoft.Extensions.Hosting.
Using context 'LuxMapDbContext'.
Writing migration to '/Users/nhm809/Documents/LuxMap/luxmap_be15_p2c/src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs'.
Writing model snapshot to '/Users/nhm809/Documents/LuxMap/luxmap_be15_p2c/src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs'.
Done. To undo this action, use 'ef migrations remove'
  exit 0
```

Kiểm model không DB (`python3 /private/tmp/be15-p2c-design/check-model.py`):

```text
Using application service provider from Microsoft.Extensions.Hosting.
Using context 'LuxMapDbContext'.
No changes have been made to the model since the last migration.
  exit 0
```

Script gọi `dotnet exec` với deps/runtimeconfig của Api trong worktree, `ef.dll` hiện có,
`migrations add AddSurveyPublication` hoặc `migrations has-pending-model-changes`, startup assembly Api,
assembly Persistence, `--working-dir /private/tmp/be15-p2c-design -- --contentRoot /private/tmp/be15-p2c-design`.
Log model: `/tmp/be15-p2c-model-check.log`.

## 3. Build và test đã chạy (không DB)

Worktree mới thiếu project.assets.json; lượt build --no-restore đầu báo NETSDK1004. Đã restore **chỉ từ
cache local** `/Users/nhm809/.nuget/packages`, không thêm hay đổi PackageReference. Lượt restore dùng
CLI home mặc định không tiến triển, đã ngắt; dùng CLI home tạm và giới hạn processor thì thành công.

```sh
DOTNET_CLI_HOME=/private/tmp/luxmap-dotnet NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
DOTNET_PROCESSOR_COUNT=2 dotnet restore --source /Users/nhm809/.nuget/packages \
  -p:NuGetAudit=false --disable-parallel --disable-build-servers -m:1

DOTNET_CLI_HOME=/private/tmp/luxmap-dotnet NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
DOTNET_PROCESSOR_COUNT=2 dotnet build --no-restore --disable-build-servers -m:1 -warnaserror
```

`/tmp/be15-p2c-build-final.log`:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.40
```

Dùng runner xUnit tạm cùng cách P2b, chỉ nạp Shared/Persistence/Storage tests, **không nạp Api.Tests**.
Runner gọi XunitTestFramework.RunAll, in failures và tổng từng assembly, trả exit khác 0 khi có failure;
không cần testhost socket, không dựng host API. Runner tham chiếu project hiện có, không package mới.

```sh
DOTNET_CLI_HOME=/private/tmp/luxmap-dotnet NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
DOTNET_PROCESSOR_COUNT=2 dotnet build /private/tmp/be15-p2c-runner/Runner.csproj \
  --no-restore --disable-build-servers -m:1 -warnaserror
DOTNET_CLI_HOME=/private/tmp/luxmap-dotnet DOTNET_PROCESSOR_COUNT=2 \
  dotnet tests/LuxMap.Shared.Tests/bin/Runner/net10.0/Runner.dll
```

Kết quả cuối `/tmp/be15-p2c-tests-final.log`: **387 passed, 0 failed, 0 skipped**.

```text
LuxMap.Shared.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null: Total=308 Failed=0 Skipped=0
LuxMap.Persistence.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null: Total=43 Failed=0 Skipped=0
LuxMap.Infrastructure.Storage.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null: Total=36 Failed=0 Skipped=0
```
Test mới không DB kiểm chọn đại diện, conflict ON/OFF, timestamp cũ/bằng, severity/config/trần,
loại member không hợp lệ, median lẻ/chẵn/tối thiểu/chiều/zero; guard bất biến của cả ba bảng trước DB.
Có chạy lại ffmpeg media tests thật trong Storage như P2b. Không coi đó là xác minh DB hoặc thiết bị thực địa.

Kiểm tĩnh thêm:

```text
Generator syntax: OK (not executed)
Migration Up unexpected Drop/Alter: False
Migration immutable triggers: 3
Migration Down data guard: True
```

`git diff --check`: exit 0, không output. Đã rà diff code/schema/docs; không debug print, package mới,
ExecuteUpdate/Delete, service ngoài, enum Contract mới hoặc commit.

## 4. Test tích hợp đã viết, CHƯA chạy

`tests/LuxMap.Api.Tests/SurveyPublicationTests.cs`: 11 ca PostGIS, tạo dữ liệu riêng và teardown theo
FK/immutable purge trong transaction; chỉ biên dịch trong build solution:

1. Accept công bố cả hai xã, status/history/fault/audit đúng; không hoàn thành WO; retry không nhân đôi.
2. Duyệt muộn hoặc bằng timestamp không đè status, không sinh fault quá khứ, vẫn thêm history.
3. Chống trùng theo effective type có override; normal không tự đóng fault.
4. Hai quyết định đồng thời chỉ một thắng.
5. Thiếu xã →403 khi review; thumbnail →404 trước mở object.
6. Return đòi note, không công bố.
7. Chuỗi ba capture simulated đã accepted tạo baseline median; loại tự chấm/sai chiều/future;
   capture kế tiếp qua lookup + FrameClassification thật sinh dim/normal đủ điều kiện đánh giá và fault dim.
8. Results paging và thumbnail proxy stream.
9. Run sai sweep, version cũ, retry đổi body →409.
10. UPDATE/DELETE ba bảng immutable bị trigger 55000.
11. Inject lỗi ở SaveChanges fault sau khi đã lưu batch history: rollback cả quyết định/current/history/fault.

**Claude cần chạy** các ca trên và suite hiện có trên DB test đã migrate, kiểm FK/SQL translation/audit/concurrency,
apply/rollback/reapply khi dữ liệu test trống, rồi xuất/lint OpenAPI. Chưa chạy bất kỳ integration test ở đây.

## 5. Giới hạn và bàn giao

- ✅ Đã kiểm: build 0 warning/0 error, tests không DB, model/snapshot, đọc migration, diff và syntax generator.
- ⚠️ Chưa kiểm: SQL trên PostGIS, HTTP end-to-end, khoá/race thật, rollback thực, object store thật, OpenAPI.
- 📌 Nền tạm: severity/quality ranking/tối thiểu member/median chờ pilot WP4/FW; các default đã ghi drift.
- 📌 P2a/P2b chưa có capture_profile/system_setting quản trị. Schema P2c hiện theo nền đang có:
  chưa thêm capture_profile_id/fixture_id và matching fixture/profile/geometry/protocol đầy đủ của bản đề xuất
  §2.5 Phase 1; việc này vẫn thuộc D-05/D-06 + BE-33/34 trước khi mở xử lý thực địa. Đã ghi ở drift và tài liệu,
  không tự tạo bảng profile hoặc thiết kế quyết định mới. Worker vẫn chỉ xử lý simulated, không nới sang field.
- History hiện ứng với tập pole có observation trong run (mỗi pole một đại diện); không chế observation cho pole
  ngoài các lượt đã đi. Phân trang results giữ các observation theo lượt để người duyệt thấy cả nguyên nhân conflict.
- Không sửa ghép cột trước/sau duyệt, không Roboflow, không tự đóng sự cố, không hoàn thành WO.

Tiến độ tại `tracking.html`; deviation/bề mặt tạm tại `docs/contract-drift.md` mục BE-15 P2c.
Không đọc `.env`; không DB/Docker/MinIO; không database update/test tích hợp/OpenAPI; không package/factory;
không git add/commit/push. Dừng sau bàn giao để Claude review.

## Vòng 2 — sửa R1–R4 theo review Claude (04/10/2026)

Giữ nguyên **R0**: đã so byte-for-byte toàn khối truy vấn chống trùng gồm `Open.ToArray()`,
`FaultType? overridden` và hai nhánh override/không override; không khôi phục phép coalesce trên enum.
Kết quả PostGIS 575/575 trong review là **Claude chạy ở vòng trước**, không phải kiểm chứng DB của lượt này.

### Thay đổi

- **R1:** `Eligible` chỉ nhận `normal`, gồm normal chưa có baseline; dim không thành member.
  Test mô phỏng khởi tạo baseline 100 rồi nhiều phiên lux 60/70: phân loại vẫn dim, member vẫn đúng ba
  phiên normal ban đầu. Có thêm ca PostGIS đi qua Accept thật (chỉ biên dịch).
- **R2:** thêm `luminance_baseline.fixture_id` nullable, FK Restrict tới fixture + index. BuildBaselines
  lấy bóng đang dùng (`removed_date IS NULL`), ghi fixture_id, lọc observed_at từ **00:00 UTC ngày lắp**;
  sweep cũ không có member sau ngày lắp không tạo baseline version mới. Lookup chỉ lấy baseline có
  fixture_id bằng bóng hiện tại; không bóng thì chỉ lấy fixture_id null, không tái dùng baseline bóng đã tháo.
  **Không có bóng hoặc ngày lắp ⇒ không lọc theo ngày**; `Fixture.InstallDate` trong model hiện tại là
  DateOnly **không nullable**, nên nhánh thiếu ngày hiện chỉ đến từ thiếu fixture (helper cũng nhận null
  để diễn đạt quy tắc). Không đổi schema Fixture/không bịa ngày lắp. Test offline kiểm biên đúng ngày,
  đủ ba member mới, fallback thiếu ngày và lọc fixture; thêm ca PostGIS thay bóng + tích luỹ lại (chưa chạy).
  Phần thiếu fixture_id của §2.5 ghi trong kết quả vòng trước nay đã được bổ sung; profile/protocol vẫn chờ.
- **R3:** dịch lỗi trong `S3ObjectStore.OpenAsync` (chỗ chung đã biết SDK): HTTP 404 hoặc NoSuchKey →
  LuxMapException 503 `STORAGE_OBJECT_MISSING`. Không đổi access denied thành missing; không đổi thứ tự
  phân quyền trước mở object ở Thumbnail. Ba test qua adapter thật và S3 client giả override GetObjectAsync,
  không tạo request mạng, không MinIO; hai dạng not-found và ca đối chứng 403.
- **R4:** mỗi results item thêm `published_as`, `is_representative`. Sau lấy trang, đọc mọi observation
  trong run của các pole trên trang rồi gọi **cùng Choose** dùng lúc công bố. Không gộp chỉ trong trang.
  Test một trang có ON, trang khác OFF: vẫn preview unknown; chỉ observation được chọn mang true.
  Tài liệu/README/drift cập nhật. Generator không có schema viết tay cho SurveyResultItem; không sửa JSON
  sinh sẵn, không chạy generator/Swagger/OpenAPI theo giới hạn task.

### Sinh lại và đọc migration

Đã sao lưu migration/Designer/snapshot vòng 1 vào `/private/tmp/be15-p2c-r2-migration-backup`;
trả snapshot tạm về HEAD trước migration chưa merge, build host rồi dùng EF CLI với cùng cấu hình giả
và cwd/contentRoot tạm như vòng trước. Không dùng `migrations remove` cần kiểm DB.
EF sinh lại `AddSurveyPublication`, sau đó giữ lại định danh gốc `20261003172809_AddSurveyPublication`
để vẫn là **chính một migration**, không thêm migration thứ hai. Giữ nguyên SQL trigger và Down guard viết tay.

Log `/tmp/be15-p2c-r2-migration.log`:

```text
Writing migration to '/Users/nhm809/Documents/LuxMap/luxmap_be15_p2c/src/LuxMap.Persistence/Migrations/20261003175157_AddSurveyPublication.cs'.
Writing model snapshot to '/Users/nhm809/Documents/LuxMap/luxmap_be15_p2c/src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs'.
Done. To undo this action, use 'ef migrations remove'
  exit 0
```

Đã đọc diff toàn migration và Down: **Up chỉ thêm fixture_id, FK Restrict, index so với vòng 1**;
Down không đổi (DropTable baseline tự gỡ cột/FK/index). Không Drop/Alter bất ngờ, không default/xmin mới.
So sánh tự động các khối viết tay và R0:

```text
R0 unchanged byte-for-byte: OK
Handwritten migration triggers/Down guard unchanged: OK
Down unchanged: OK; Up diff only fixture_id + FK Restrict + index
```

`python3 /private/tmp/be15-p2c-design/check-model.py`, log `/tmp/be15-p2c-r2-model-check.log`:

```text
Using application service provider from Microsoft.Extensions.Hosting.
Using context 'LuxMapDbContext'.
No changes have been made to the model since the last migration.
  exit 0
```

Claude cần kiểm lại bản migration đã sửa trên DB test thích hợp; lịch sử apply bản cũ cùng ID không tự
áp thêm cột fixture_id. Codex không thao tác DB để giải quyết việc đó.

### Phá thử và kiểm chứng cuối

`python3 /private/tmp/be15-p2c-r2-mutate.py` sao lưu bốn file production, đồng thời bỏ các chốt
R1/R2/R3/R4, build mutant thành công 0 warning, chạy runner không DB; finally khôi phục từng byte.
Log `/tmp/be15-p2c-r2-mutant.log`: đúng **6 test đỏ**, runner exit 1:

```text
FAIL ...Preview_uses_all_passes_even_when_the_conflict_is_on_another_page
FAIL ...Gradual_dimming_does_not_lower_its_own_baseline
FAIL ...Replacement_requires_new_members_and_lookup_rejects_the_old_fixture
FAIL ...Replacement_lookup_uses_only_the_active_fixture
FAIL ...Missing_thumbnail_becomes_storage_503(status: NotFound, code: null)
FAIL ...Missing_thumbnail_becomes_storage_503(status: BadRequest, code: "NoSuchKey")
```

Các mutation: dùng classified_as từng lượt cho preview; nhận lại dim; bỏ lọc ngày lắp;
bỏ lọc fixture_id; rethrow lỗi SDK thay vì dịch 503. Ca 403 đối chứng vẫn xanh.
Sau khôi phục, thêm chốt không tạo baseline từ sweep cũ trước ngày lắp rồi build/test bản cuối.

Build solution (cùng env CLI_HOME/NUGET_PACKAGES/PROCESSOR_COUNT và lệnh vòng trước),
`/tmp/be15-p2c-r2-final-build.log`:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:08.36
```

Runner vẫn chỉ nạp Shared/Persistence/Storage, không Api.Tests. Lệnh:

```sh
DOTNET_CLI_HOME=/private/tmp/luxmap-dotnet NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
DOTNET_PROCESSOR_COUNT=2 dotnet build /private/tmp/be15-p2c-runner/Runner.csproj \
  --no-restore --disable-build-servers -m:1 -warnaserror
DOTNET_CLI_HOME=/private/tmp/luxmap-dotnet DOTNET_PROCESSOR_COUNT=2 \
  dotnet tests/LuxMap.Shared.Tests/bin/Runner/net10.0/Runner.dll
```

Output cuối `/tmp/be15-p2c-r2-final-tests.log`: **394 passed, 0 failed, 0 skipped**.

```text
LuxMap.Shared.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null: Total=312 Failed=0 Skipped=0
LuxMap.Persistence.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null: Total=43 Failed=0 Skipped=0
LuxMap.Infrastructure.Storage.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null: Total=39 Failed=0 Skipped=0
```

✅ `git diff --check` exit 0, không output. ✅ R0/trigger/Down guard nguyên vẹn, model khớp snapshot.
⚠️ Chưa kiểm DB hoặc HTTP; 📌 ngày lắp dùng mốc UTC, không có fixture/ngày lắp thì không lọc ngày.
Không nhận bộ test offline là bằng chứng EF thực thi SQL, scope/khoá/HTTP hoặc MinIO đã chạy thật.
Test tích hợp mới chỉ biên dịch; phần cần Claude kiểm gồm vòng dim nhiều phiên, thay fixture, paging results,
503 qua HTTP và apply/rollback migration sửa lại. Không có package mới.

Không `.env`, DB/Docker/MinIO, database update, test tích hợp, OpenAPI export, commit/push.
Đã cập nhật tracking; dừng sau vòng sửa R1–R4 này.

## Vòng 3 — R5, R6 (04/10/2026)

Codex hết hạn mức giữa lượt R6 (sau khi đã sửa code, migration, test; chưa ghi mục này) — Claude hoàn tất kiểm và ghi.

- **R5** (review độc lập lần 1): kiểm tương thích cột chỉ chạy khi accept. Claude sửa, test
  `A_corrected_pole_blocks_accept_but_not_return`.
- **R6** (review độc lập lần 2): cột dự kiến không quan sát ⇒ history `unknown`/`not_observed`, `observation_id` null,
  `evaluated_at` = kết thúc phiên, current status theo D-08; không fault/baseline. Phạm vi người duyệt phủ xã của cột dự kiến
  và xã hiện tại của chúng. Results liệt kê cột không quan sát sau mọi quan sát. CHECK `ck_luminance_history_unobserved`.
  `SurveySweep.AtElapsed` dùng chung cho `ended_at` của listing và mốc công bố.
- Claude thêm `Expected_poles_read_the_snapshot_exactly_as_the_worker_writes_it` — snapshot test dựng tay không canh được
  định dạng `PolePosition` + `LuxMapJsonOptions` mà worker thật ghi.
- Kiểm trên PostGIS `luxmap_test`: migration apply → rollback → apply hai vòng, không có thay đổi model chưa sinh migration.
  Api 585, Persistence 44, Storage 39, Shared 318 (môi trường sạch). Không sót dữ liệu test.
- Phá thử (đã khôi phục): bỏ cột dự kiến khỏi tập công bố ⇒ 4 test DB + 2 test không DB đỏ; bỏ xã cột dự kiến khỏi kiểm phạm
  vi ⇒ 1 + 1 đỏ; gỡ CHECK trên DB ⇒ `Check_rejects_known_history_without_observation` đỏ (CHECK đã tạo lại đúng như migration).
- OpenAPI xuất lại, spec hợp nhất sinh lại, lint hợp lệ.
- ⚠️ Review độc lập lần 3 (Codex `exec review`) **chưa chạy** — hết hạn mức tới 10/10/2026.

## Sau merge (#80) — review độc lập lần 3

Codex `exec review` trên dev (chạy với `-c model="gpt-6-astra"`; `~/.codex/config.toml` đang trỏ `gpt-6.1-sol`, tài khoản
ChatGPT không dùng được): hàng `not_observed` không có FK nào tới `pole` (hai FK ghép bỏ qua vì null). Claude sửa ở
`fix/BE-15-history-pole-fk`: migration `LuminanceHistoryPoleFk` (chỉ `AddForeignKey`), test xoá cột; phá thử (gỡ FK) ⇒ đỏ.
