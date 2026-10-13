# SPEC-NULLABLE Phase 2 — Codex review (13/10/2026)

**Chưa nên merge: 1 P1 về ngữ nghĩa schema; D-4 vẫn cần bằng chứng WP5 đã sửa.** Review `git diff df598c1..26f2eb4` cả logic và nghiệp vụ; đọc task, P1/D-1…D-6, P1a, AGENTS (BE-14b). Hai lượt review song song theo skill `code-review`; chỉ file này được ghi, không build/test, commit hay cập nhật dashboard.

## Phát hiện

### 1. P1 — Wrapper có `nullable` nhưng `allOf` vẫn từ chối null

**`src/LuxMap.Api/OpenApi/NullableReferenceFilters.cs:44–45`; `docs/openapi/tools/gen_consolidated_spec.py:357–358`.** Cả 25 target vẫn có `type: object/string` không nullable; 14 target enum cũng không chứa null. Ví dụ `fixture: null` qua type của wrapper nhưng không qua `PoleMapFixture` trong `allOf`; `power_source: null` không qua `PowerSource` (`string`, enum `[grid]`). Theo [OAS 3.0.4 §4.7.24.2 và §4.7.24.2.1](https://spec.openapis.org/oas/v3.0.4.html#schema-object), nullable chỉ mở rộng type tại schema đó, các constraint khác vẫn áp dụng, và từng nhánh allOf được kiểm độc lập. Đây là suy luận trực tiếp từ chuẩn và schema, chưa chạy validator dữ liệu.

**Đề xuất:** xét lại **D-2** trước merge, chọn biểu diễn thực sự nhận null (ví dụ nhánh null riêng trong `anyOf`, hoặc component nullable riêng; enum nullable phải nhận cả null). Giữ component non-null dùng cho request. Thêm kiểm instance `null`, một giá trị hợp lệ và một giá trị sai; Redocly lint và assert hình dạng wrapper không chứng minh dữ liệu null hợp lệ. Implementation làm đúng encoding đã duyệt, nhưng encoding chưa đạt mục tiêu ticket.

### 2. P2 — Chốt test không phát hiện nullable mới bị bỏ sót hoặc generator đè mất nullable

**`tests/LuxMap.Api.Tests/OpenApiNullableTests.cs:16,82,124–128`; `src/LuxMap.Api/OpenApi/NullableReferenceFilters.cs:38–39,55–57`.** Test chỉ canh 25 literal và đếm 25 wrapper, thiếu coverage reflection độc lập đã đề xuất ở P1 §4. Filter chỉ sửa property bare `$ref`, không sửa `Items`/`AdditionalProperties`; ngoại lệ generic loại mọi `T`, gồm `Envelope<T>.Value`, không chỉ Feature. Response mới có `List<Foo?>`, `Dictionary<string,Foo?>`, `Enum?[]` hoặc generic nullable có thể lọt mà số wrapper vẫn 25. Chưa thấy các dạng container này trong response hiện tại.

**`docs/openapi/tools/gen_consolidated_spec.py:359–360`; `.github/workflows/ci.yml:125–134`.** Test chỉ đọc Swagger sống. Bỏ nullable của `lamp_watt/install_date` trong generator rồi sinh và commit spec sai vẫn có thể qua test này và kiểm stale-spec: CI so file với chính generator, đúng bẫy BE-14b.

**Đề xuất:** giữ literal, thêm coverage response bằng NRT độc lập và probe positional/rename/inherited/generic/container/request graph; kiểm nullable từ spec code sang bản hợp nhất. Không cố định số 25 cho DTO tương lai. Những sabotage trên được suy ra từ source, không thực thi trong lượt review.

### 3. P2 — WP5 chưa hiểu wrapper thật; đính chính kết luận “25 trường thành any”

**`../luxmap-web/scripts/generate-types.js:55–94,107–111` tại `origin/dev` (`b05d74e`).** Đã lấy đúng hai hàm thuần từ ref và chạy trong JS isolate với mẫu backend; không chạy npm, không đọc `.env` hay ghi types:

```ts
export interface Probe {
    fixture?: Record<string, any>
    source_channel?: string | null
}
```

Object rơi vào dòng **91** `return 'Record<string, any>'`; enum wrapper rơi vào dòng **75** `return 'string | null'`. Với encoding hiện tại: **11 object mất kiểu đích và null; 14 enum mất union giá trị**. P1/P1a nói tất cả thành `any` là sai vì mẫu khảo sát thiếu `type`.

**Đề xuất tối thiểu cho D-4:** nhận `allOf` một `$ref` **trước** nhánh `type`, lấy tên target và thêm `| null` khi `nullable: true`; không suy dấu `?` từ nullable. Sinh lại types, canh đúng `PoleMapFixture | null`, `SourceChannel | null`. Nếu đổi D-2 thì WP5 phải canh encoding cuối cùng. Ref dev đang có chưa đáp ứng; cần bằng chứng bản sửa WP5 trước merge, không suy rằng các nhánh khác chưa sửa.

### 4. P2 — WP6 còn DTO từ chối response hợp lệ (nợ có sẵn, D-5)

**`../mobile/app/src/main/java/com/luxmap/feature/map/data/dto/PoleDetailDto.kt:15,19,49`; `.../dto/PoleFeatureDto.kt:34–35` tại `origin/dev` (`0b46b7e`).** Năm trường cần nullable: `fixture`, `luminanceBaseline`, `sourceChannel`, `powerSource`, `fixtureType`. DTO viết tay nên không có bước regenerate. `iotNode` đã nullable (dòng 18); `WorkOrderDetailDto.inspectionOutcome` đã nullable (dòng 30), giữ nguyên.

**Đề xuất:** sửa DTO và mapping/domain/UI (`.../feature/map/data/PoleDetail.kt:69–77`) để biểu diễn thiếu bóng/chưa khảo sát. Hai scalar mới sửa ở bản hợp nhất cũng cần `Int?`/`String?` tại `PoleFeatureDto.kt:36–37`. Rà các scalar nullable cũ: warranty/last_seen/last_sweep, confidence/timestamps, baseline out-threshold, history ratio, frame distance/heading. `PoleDetailFixtureDto.supplier` bắt buộc ở dòng 40 nhưng BE không trả (`src/LuxMap.Modules.Map/Features/PoleMapDetail.cs:64`); bỏ giả định này. Không nới enum của **fixture object đang tồn tại**, chỉ nới enum ở properties khi không có bóng.

### 5. P3 — Lý do giữ 9 request bare `$ref` không đúng cho 5 trường

**`src/LuxMap.Api/OpenApi/NullableReferenceFilters.cs:61–62`; `tests/LuxMap.Api.Tests/OpenApiNullableTests.cs:46–48`.** Server chấp nhận null có nghĩa ở:

| Trường | Hành vi và bằng chứng |
|---|---|
| `UpdateUserRequest.role` | Giữ role cũ — `src/LuxMap.Modules.Identity/Accounts/UserAdminService.cs:122` |
| `ReportFaultRequest.location` | Hợp lệ khi có pole — `src/LuxMap.Modules.Faults/FaultReportService.cs:135–150` |
| `ReportFaultRequest.severity` | Default medium — cùng file, `:162` |
| `AcceptCitizenReportRequest.severity` | Default medium — `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:174` |
| `DeviceAckRequest.reported_mode` | Null hợp lệ khi failed; chỉ applied bắt buộc — `src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:355` |

Bốn trường còn lại (`Accept.fault_type`, `ReportFault.fault_type`, `CreateSweep.data_source`, `FaultOutcome.outcome`) null → 400; giữ bare ref hợp lý cho client. **Đề xuất:** sửa lý do/bản tin, ghi rõ 5 trường là nợ spec request tồn tại trước; chốt follow-up riêng, không tự mở rộng D-1. Không kết luận cả 9 trường chỉ nullable để validate “thiếu”.

### 6. P3 — Comment vẫn giữ số operation đã đính chính

**`src/LuxMap.Api/OpenApi/NullableReferenceFilters.cs:20–21`.** Còn ghi “all 121 operations changed”. **Đề xuất:** bỏ số operation sai do script so op với cả path item; giữ 88 schema đã đo đúng. Không dùng số này làm bằng chứng lựa chọn filter.

## Các điểm đã đối chiếu, không thấy lỗi hiện tại

- **Nghiệp vụ 25 response:** chưa thấy trường cần sửa C# thành non-null. Thiếu bóng giải thích `active_fixture`, `fixture`, enum nguồn/loại trên map/sync/work-order/warranty; enum chỉ có một giá trị vẫn có thể null khi không có bóng (`MapQueryService.cs:163`, `StatisticsService.cs:183`). Feeder/cabinet/source null khi chưa gán; agenda location null khi không còn điểm/tuyến nhìn thấy (`WorkOrderAgenda.cs:194,228`); coverage geometry thiếu khi tuyến bị scope hoặc không ra LineString (`SurveyReviewService.cs:129,147,154`). AI/luminance/run null khi chưa có kết quả; photos_dropped null khi không bỏ ảnh; control/reported mode null trước báo trạng thái/ACK.
- **Hai trường dễ hiểu sai:** `PoleMapDetail.iot_node` **luôn null**, đúng mô hình thiết bị ở tủ (`PoleDetailService.cs:92`, AGENTS/drift I-1/I-8); `inspection_outcome` chỉ bắt buộc khi hoàn tất phiếu inspection, null trước đó hoặc ở repair (`WorkOrderService.cs:378–388`). Không đổi null sang `unknown` cho `source_channel`; không có status thì chưa có nguồn (`PoleDetailService.cs:90`).
- **Reflection:** positional record có property; inherited `SyncPoleProperties` được phủ; nhánh `[JsonPropertyName]` đúng. `NullabilityInfoContext` và trạng thái graph tạo riêng mỗi call, không thấy race. Thay value trong dictionary giữ thứ tự; HashSet chỉ quyết thứ tự mutation, không dựng lại JSON.
- **Lọc request:** operation query/path được tính ở dòng 81; request thắng nếu schema dùng chung, đúng lựa chọn “chỉ-response”. Shared enum component hiện không có property để unwrap nhầm. Chưa đọc `path.Parameters` hay parameter `Content`; spec hiện không dùng hai dạng này. Bỏ nhánh operation-parameter chưa có probe canh.
- **Generator:** so mọi property nullable của code với bản hợp nhất hiện không còn trường nào mất nullable; schema viết tay còn `PoleProperties`, `SegmentProperties`, `LatLng` và ID, không tìm thêm nullable bị đè. Hai scalar đã sửa đúng.

✅ So JSON độc lập: code **25**, hợp nhất **27** property đổi, **0 operation đổi** ở cả hai; không thấy thay đổi ngoài sáu file trong commit. Kiểm source/service và consumer ref, chạy đúng hàm thuần WP5 trên mẫu wrapper.

⚠️ Không chạy dotnet/docker/npm, validator instance, deserialize Kotlin hay UI; số **1007/46/45/383**, build `-warnaserror`, Redocly valid và bốn sabotage đỏ là bằng chứng **người giao việc cung cấp**, không phải lượt review này chạy lại. Tổng **27 lệnh shell** (gồm hai reviewer), chỉ đọc; không fetch/checkout consumer.

📌 Kết luận theo hai trục: **logic/spec** cần giải quyết P1 và bổ sung chốt coverage; **nghiệp vụ** 25 response phù hợp, nhưng rationale request và handoff consumer cần đính chính. Ghi nhận **1 P1 / 3 P2 / 2 P3**; WP6 là nợ có sẵn, WP5 là điều kiện merge đã chốt.
