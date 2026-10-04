# JSON-ENUM — kết quả thực thi

- Agent: Codex; nhánh `fix/json-enum-strings-only`.
- Quyết định/ticket: 04/10/2026; hoàn tất lượt thực thi: 05/10/2026 (Asia/Ho_Chi_Minh).
- Đã đọc `AGENTS.md` (symlink tới `CLAUDE.md`), `.ai/README.md`, `.ai/context/sources.md`, `.ai/context/commands.md`, `.ai/tasks/JSON-ENUM.md` và các file code/test được ticket chỉ định. `.ai/tasks/current.md` không tồn tại trong worktree.
- Không đọc `.env`; không kết nối DB/Docker/MinIO; không chạy test tích hợp; không xuất OpenAPI; không thêm package, migration, commit, push hoặc stage.

## Thay đổi

1. `src/LuxMap.Shared/Serialization/LuxMapJsonOptions.cs`: đặt `allowIntegerValues: false`, kèm comment giải thích số JSON và chuỗi chữ số có thể lọt xuống CHECK DB. Giữ converter chuẩn và snake_case; không tự viết converter khác.
2. `tests/LuxMap.Shared.Tests/JsonEnumHandlingTests.cs`: 14 ca, mỗi ca một assert. Phủ số hợp lệ theo ordinal (`2`), số chưa định nghĩa (`999`), chuỗi chữ số tương ứng, tên sai, tên snake_case hợp lệ, enum nullable/null, ghi enum có tên thành chuỗi và từ chối ghi enum chưa định nghĩa thành số.
3. `tests/LuxMap.Shared.Tests/JsonPipelineConventionTests.cs`: thêm 4 ca từ chối số/chuỗi chữ số trên options lấy thật từ DI của MVC và HTTP JSON.
4. `tests/LuxMap.Api.Tests/WorkOrderTests.cs`: thêm theory 4 ca `Numeric_task_kind_is_a_validation_error_at_json_binding`, gửi `1`, `999`, `"1"`, `"999"` tới `POST /api/v1/work-orders`. Kiểm 400, `VALIDATION_FAILED`, đúng envelope `error.code/message/details`, correlation id, lỗi tại `$.task_kind`, không stack/System và không audit mới qua helper hiện có. **Chỉ biên dịch, chưa chạy.**
5. Bổ sung mục JSON-ENUM trong `docs/contract-drift.md`, quy tắc ở mục “Cả JSON cũng hở” trong `CLAUDE.md`; giữ nguyên symlink `AGENTS.md`. Cập nhật `tracking.html` với trạng thái chờ review/tích hợp.

Chọn endpoint WorkOrders vì `CreateWorkOrderRequest.TaskKind` là enum nullable được MVC bind trực tiếp. `PATCH /faults` dùng `JsonElement` rồi kiểm riêng, nên một test trên endpoint đó không chứng minh converter chung hoạt động. `ApiConventionsSetup.BuildValidationErrorResponse` đã có sẵn cơ chế đổi lỗi binding thành 400 đúng envelope; không sửa cơ chế lỗi hay guard cục bộ. Nhánh này chưa có `POST /faults` của BE-41; không gỡ/thay kiểm tra BE-41 khi merge.

## Rà các bên dùng JSON

Rà toàn bộ `src/` và `tests/` bằng `rg` theo `JsonSerializer`, `Deserialize<`, `JsonStringEnum`, `JsonConverter`, `JsonDocument`, `JsonElement`, `jsonb`, `Hangfire`, `Newtonsoft`, các hàm HTTP JSON và các trường enum. Đọc các call site liên quan; không chạy code seed hoặc truy vấn dữ liệu.

| Chỗ | Phát hiện và xử lý |
|---|---|
| `LuxMapJsonOptions` / `JsonConventionExtensions` | Một chỗ đăng ký enum converter chung; MVC và HTTP JSON cùng gọi Configure. Đây là chỗ sửa. Không thấy converter enum riêng khác trong src/tests. |
| `FaultReviewService`, `OptionalJson`, `WireEnum` | Body PATCH fault đọc JsonElement, bắt buộc chuỗi rồi so tên enum trên dây. Số và chuỗi chữ số đã bị từ chối; giữ nguyên. Query string không đi qua converter JSON, giữ nguyên. |
| `WorkOrderService.TransitionAsync` | `outcomes.Deserialize<FaultOutcomeRequest[]>(LuxMapJsonOptions.Default)` đọc enum nullable `InspectionOutcome` từ **body mới**, không phải snapshot DB. Trước có thể nhận số; nay bị JsonException, catch hiện có chuyển về `OptionalJson.Invalid("fault_outcomes")` → 400. Giữ catch và validation hiện có. |
| `AuditTrail.Serialize` | `before_state/after_state` ghi qua options chung; snapshot `FaultReviewService.FaultSnapshot`, `WorkOrderService.WorkOrderSnapshot`/`FaultChange`/`FaultOutcomeSnapshot` chứa enum có tên, vốn đã ghi thành chuỗi. Không đổi định dạng ghi enum hợp lệ. |
| `SurveyProcessor.Audit` | Đường ghi audit riêng dùng helper `Json(state)` → options chung. Không tìm thấy code src deserialize audit đã lưu thành DTO enum. Test audit/fault/work-order dùng JsonDocument/GetString hoặc snapshot `{}`; không dựa vào enum số. |
| `SurveyProcessor.ProcessOneAsync` | Helper Json dùng options chung. Deserialize `SurveyProcessingOptions` từ chuỗi vừa serialize từ `options.Value`, không phải đọc `SettingsSnapshot` cũ. `SurveyProcessingOptions` và `SurveyFrameOptions` không có property enum. |
| `SurveyProcessingRun` — settings/GIS/clock | Các cột jsonb ghi qua helper chung. `SurveyPublicationRules` và `SurveyMediaAccess` đọc GIS snapshot bằng JsonDocument lấy ID/chuỗi; không deserialize enum qua options chung. Không thấy đường đọc settings/clock cũ thành enum. |
| Các jsonb còn lại của Survey | `quality_flags`, `reason_codes` là cờ/chuỗi/mảng chuỗi; `Flags` deserialize `string[]`. `artifact_version.metadata` chứa metadata công cụ; `detection.raw_prediction` giữ JSON đầu ra detector. Không có đường đọc enum số bằng options chung. |
| `ManifestOnOffDetector`, fixture Shared/API, `FfmpegFrameExtractor` | Serialize/deserialize mặc định cho manifest, prediction và metadata; `FakeDetectionCase.Outcome`/`Prediction.Label` là **string**, không enum. Giữ nguyên vì không phụ thuộc cách đọc enum. |
| `SurveyIngestService`, `SurveyReviewService` | Hash request bằng options chung. Request hợp lệ có enum vẫn serialize cùng chuỗi nên không đổi hash vì sửa này; không thấy dữ liệu JSON cũ được deserialize thành enum. `SurveyRawParser` canonicalize JsonElement, không đọc enum. |
| Import CSV/GeoJSON: `AssetImportService`, `ImportRow` | GeoJSON có thể chuyển number thành text để dùng parser chung; `RequiredEnum` chỉ so với `ContractEnum.ToDbValue` của các thành viên định nghĩa, nên chữ số không khớp. Giữ nguyên cơ chế lỗi theo dòng của import. |
| Seeder và lưu enum dạng cột | `IdentitySeeder` ghi entity qua EF, không qua JSON. `ContractEnum` lưu enum dạng text bằng converter EF riêng, không bị đổi. Đọc bổ sung `scripts/seed_mock_set.py`: lấy tên enum từ mock/CSV và ghi SQL text; không dùng options JSON .NET. Không chạy script. |
| Các HTTP test/helper | `JsonContent.Create`, `PostAsJsonAsync`, `PutAsJsonAsync` dùng anonymous body với chuỗi enum. `VerifiedInspectionAsync` nhận `(string Fault, string Outcome)`, không gửi ordinal. Không thấy test/fixture cũ phụ thuộc enum JSON số. Các payload số mới là ca âm cố ý, giữ. |
| Test serialization/storage hiện có | `DomainEnumSerializationTests`, `JsonConventionTests`, `ContractEnumStorageTests`, `CapabilityMatrixTests`, `UserRoleTests` kỳ vọng chuỗi. Không sửa kỳ vọng. `OpenFaultCountTests` đọc mock bằng options chung; đã xanh trong lượt Shared. |
| Hangfire | Không tìm thấy đăng ký job/package/serializer Hangfire trong src/tests; chỉ thấy tên trong comment của `LuxMapJsonOptions`. Không có payload Hangfire cần chuyển đổi ở nhánh này. |

**Giới hạn dữ liệu cũ:** không kiểm DB nên không khẳng định mọi row lịch sử đều chứa enum chuỗi. Rà code không thấy reader đọc audit jsonb cũ thành enum bằng options này, cũng không thấy snapshot reader phụ thuộc ordinal. Vì vậy không đề xuất sửa/migrate dữ liệu. Nếu dữ liệu từng được ghi ngoài các writer đã rà, cần người review kiểm riêng trước khi thêm reader typed trong tương lai.

## Bằng chứng chạy thật

### Build

Worktree chưa có `project.assets.json`, nên lần `dotnet build --no-restore` đầu báo `NETSDK1004`. Build mặc định/compilation server không hoàn tất trong sandbox; đã gửi yêu cầu hủy. Chạy single-node, không dùng compilation server giải quyết được.

NuGet restore báo `NU1900` do không tải được vulnerability feed `https://api.nuget.org/v3/index.json`. Đã restore từ cache với **tham số lệnh** `-p:NuGetAudit=false`, không sửa project/thiết lập audit của repo. Chưa xác minh vulnerability feed. Lệnh build sau cùng:

```bash
dotnet build --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
```

Output cuối (chép từ `/tmp/json-enum-final-build.log`):

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.79
```

Build solution bao gồm `LuxMap.Api.Tests`, nên test HTTP mới đã biên dịch. Không chạy API host.

### Test Shared không cần DB

Lệnh chuẩn đã thử:

```bash
dotnet test tests/LuxMap.Shared.Tests --no-build --no-restore -m:1 -nr:false
```

VSTest dừng trước khi chạy test, output:

```text
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
Test Run Aborted.
```

Để kiểm hành vi trong giới hạn sandbox, dựng runner tạm ở `/tmp/json-enum-runner` (source `Program.cs`, project `Runner.csproj`), **không thêm package**: tham chiếu các DLL xUnit đã restore của repo, dùng `XunitTestFramework.GetDiscoverer/GetExecutor.RunTests` trong cùng tiến trình, đợi `ITestAssemblyFinished`, báo failed/skipped/error và trả exit code khác 0 khi fail. Vẫn dùng discovery/theory/lifecycle của xUnit thật, không gọi tay từng hàm test. Loại `Category=Benchmark` như runsettings.

Lượt đầu chạy runner từ `/tmp` có 2 test tìm thư mục mock thất bại do `AppContext.BaseDirectory`; đã đặt DLL runner tạm vào output Shared để các test tìm đúng repo. Không sửa/bỏ qua test. Lệnh sau cùng:

```bash
dotnet exec --runtimeconfig tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.runtimeconfig.json --depsfile tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.deps.json tests/LuxMap.Shared.Tests/bin/Debug/net10.0/JsonEnumRunner.dll /Users/nhm809/Documents/LuxMap/luxmap_enum/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
```

Output thật (`/tmp/json-enum-final-tests.log`, exit 0):

```text
Assembly: LuxMap.Shared.Tests; selected cases: 331
Total: 336; Passed: 336; Failed: 0; Skipped: 0; Errors: 0
```

331 case discovery thành 336 lượt test vì một số theory mở rộng dữ liệu lúc chạy. Chỉ assembly Shared được thực thi; không chạy assembly API/Persistence.

### Phá thử

Tạm đổi **duy nhất** `allowIntegerValues: false` → `true`; build Shared với `--no-restore -m:1 -nr:false -p:UseSharedCompilation=false`, rồi chạy lệnh runner trên với đối số cuối `JsonEnumHandlingTests`. Build phá thử cũng 0 warning/error. Output thật (`/tmp/json-enum-sabotage.log`, exit 1):

```text
Assembly: LuxMap.Shared.Tests; selected cases: 14
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Nullable_enums_also_reject_invalid_values(value: "\"2\""): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Nullable_enums_also_reject_invalid_values(value: "2"): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Nullable_enums_also_reject_invalid_values(value: "\"999\""): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Nullable_enums_also_reject_invalid_values(value: "999"): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Invalid_enum_values_are_rejected_at_the_json_boundary(value: "\"999\""): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Invalid_enum_values_are_rejected_at_the_json_boundary(value: "\"2\""): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Invalid_enum_values_are_rejected_at_the_json_boundary(value: "999"): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Invalid_enum_values_are_rejected_at_the_json_boundary(value: "2"): Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
FAIL LuxMap.Shared.Tests.JsonEnumHandlingTests.Writing_an_undefined_enum_cannot_emit_an_integer: Assert.Throws() Failure: No exception was thrown
Expected: typeof(System.Text.Json.JsonException)
Total: 14; Passed: 5; Failed: 9; Skipped: 0; Errors: 0
```

Đã phục hồi `false`, build lại toàn solution và chạy lại toàn Shared: chính là hai output cuối ở trên. Không để lại thay đổi phá thử trong source.

## Bàn giao và giới hạn

- ✅ Đã kiểm hành vi đọc/ghi enum bằng xUnit thật: Shared 336/336; build cuối 0 warning/error; rà diff và `git diff --check` sạch.
- ⚠️ Test HTTP chỉ biên dịch; Claude cần chạy `WorkOrderTests.Numeric_task_kind_is_a_validation_error_at_json_binding` và test tích hợp theo ticket. Không tuyên bố HTTP đã chạy xanh.
- ⚠️ Lệnh VSTest chuẩn bị sandbox chặn socket; NuGet vulnerability feed chưa kiểm được. Runner thay thế chỉ là artifact tạm, không thay CI của repo.
- 📌 Không có D-item kiến trúc mới. Dữ liệu jsonb lịch sử thực tế chưa xác minh; không suy từ code thành khẳng định dữ liệu sạch.
- Giữ nguyên enum, response, guard cục bộ, package, OpenAPI và schema. Không commit/push theo lệnh trực tiếp của Mỹ.

Dừng tại đây để Claude review và chạy tích hợp.
