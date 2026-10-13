# SPEC-NULLABLE — P1a (Codex, 13/10/2026)

Chỉ khảo sát; WP5/WP6 đọc **ref `origin/dev` đang có tại máy**, không fetch, checkout hay đọc working tree/node_modules. Dòng dẫn dưới đây thuộc ref đó; đường Kotlin rút gọn từ `app/src/main/java/com/luxmap/`. Không chạy npm/Gradle/dotnet/Docker; chỉ tạo file này, không commit hay cập nhật dashboard theo giới hạn của lượt giao việc.

## 1. WP5 sinh types bằng gì, đọc spec nào?

- `package.json:14–15`: `generate:types` và `generate:types:remote` gọi **`node scripts/generate-types.js`**, bản remote thêm `--remote`. Script không đọc tham số này.
- Có dependency `openapi-typescript: ^7.13.0` (`package.json:57`), lock chốt **7.13.0** (`package-lock.json:6927–6929`), nhưng script **không import/gọi thư viện**; chỉ dùng các module Node `fs/path/http/https/url` (`scripts/generate-types.js:1–5`). Không có cấu hình Redocly/OpenAPI riêng trong cây file đã kiểm.
- Input là **HTTP Swagger**, không trực tiếp `luxmap-v1.json` hay `luxmap-v1.5.json`: đọc `.env`, ưu tiên `VITE_API_URL`, rồi `API_URL`, mặc định `http://localhost:5141`; URL có đuôi `.json` dùng nguyên, còn lại thêm `/swagger/v1/swagger.json` (`scripts/generate-types.js:13–28,165–167`; `.env.example:2`). Không biết biến môi trường thực tế của người chạy; không đọc `.env` ngoài ref. Có thể cấu hình URL trỏ bản hợp nhất được phục vụ qua HTTP, nhưng hiện không có bằng chứng đang làm vậy.
- Output là cây `src/types/<domain>/<resource>.ts`; lọc operation mobile bằng `x-luxmap-client`/summary `[Mobile]` (`scripts/generate-types.js:123–128`).

**Kiểu sinh ra — suy ra từ chính source, không chạy generator:**

| Thuộc tính schema | Script WP5 hiện tại | `openapi-typescript` 7.13.0 nếu thực sự dùng |
|---|---|---|
| `$ref: T` | `T` | `components["schemas"]["T"]` |
| 3.0: `allOf: [{$ref: T}], nullable: true` | **`any`** | `components["schemas"]["T"] \| null` |
| 3.1: `oneOf` hoặc `anyOf: [{$ref: T}, {type: "null"}]` | **`any`** | `components["schemas"]["T"] \| null` |

Script `openApiTypeToTs` (`scripts/generate-types.js:55–94`) chỉ xét `$ref`, enum và `type`; không xử lý composition/nullable/null, cuối hàm trả `any`. `generateInterface:99–113` sinh dấu `?` theo `required`, **độc lập** với null. Ví dụ types hiện tại: `src/types/map/poles.ts:23,25–26` là `fixture?: PoleMapFixture`, `iot_node?: PoleMapIotNode`, `luminance_baseline?: PoleMapBaseline`, **không có `| null`**. `source_channel?: SourceChannel` ở dòng 93 cũng vậy.

Đối chứng thư viện từ [source chính thức tại tag 7.13.0](https://github.com/openapi-ts/openapi-typescript/blob/openapi-typescript%407.13.0/packages/openapi-typescript/src/transform/schema-object.ts#L254): dựng intersection/union rồi thêm null (254–299), nhận `type: "null"` (349–350); [hàm `tsNullable`](https://github.com/openapi-ts/openapi-typescript/blob/openapi-typescript%407.13.0/packages/openapi-typescript/src/lib/ts.ts#L495) dựng union. Kết quả này áp cho wrapper chỉ chứa cấu trúc được hỏi; **không phải behavior script đang dùng**.

## 2. WP6 / FM-04: DTO viết tay, nullable đến đâu?

Không thấy OpenAPI/Swagger generator, task codegen hay plugin tương ứng trong cây `origin/dev` và các Gradle/config. DTO là `@Serializable data class` viết tay: `feature/auth/data/AuthDtos.kt:7–9` ghi đối chiếu C#; `feature/workorder/data/dto/WorkOrderItemDto.kt:6–8` tương tự. Kế hoạch upload `docs/superpowers/plans/2026-10-08-survey-session-upload.md:818–829` viết DTO từ spec `luxmap-v1.5.json`, không sinh tự động. Không có marker `FM-04` trong Markdown/Kotlin được tìm; không suy ra trạng thái nghiệm thu FM-04 từ đó.

Stack: Retrofit **2.11.0**, kotlinx.serialization JSON **1.7.3** (`gradle/libs.versions.toml:12–14`); `core/network/ApiClient.kt:17` chỉ bật `ignoreUnknownKeys = true`, không biến thuộc tính khai non-null thành nullable.

| Thuộc tính BE → DTO tương ứng | Kotlin ở WP6 `origin/dev` |
|---|---|
| `PoleMapDetail.fixture` | **Thiếu `?`**: `PoleDetailDto.kt:15`, `PoleDetailFixtureDto` |
| `PoleMapDetail.iot_node` | **Đã nullable**: `PoleDetailDto.kt:18`, `PoleDetailIotNodeDto? = null` |
| `PoleMapDetail.luminance_baseline` | **Thiếu `?`**: `PoleDetailDto.kt:19`, `PoleDetailLuminanceBaselineDto` |
| `PoleMapStatus.source_channel` | **Thiếu `?`**: `PoleDetailDto.kt:49`, `String` |
| `PoleProperties.power_source`, `.fixture_type` | **Thiếu `?`**: `PoleFeatureDto.kt:34–35`, `String` |
| `WorkOrderFaultDetail.inspection_outcome` | **Đã nullable**: `WorkOrderDetailDto.kt:30`, `String? = null`; model `WorkOrderDetail.kt:29` cũng nullable |
| `CreateSweepRequest.data_source` (request) | `core/network/dto/SweepDtos.kt:14`: `String`, không `?`; phù hợp client phải gửi giá trị hợp lệ |
| `FaultOutcomeRequest.outcome` (request) | `WorkOrderCompletionDto.kt:16`: `String`, không `?`; không cần nới nếu null chỉ phục vụ validation server |
| `*Feature.properties` | DTO cột/tuyến khai non-null (`PoleFeatureDto.kt:18`, `RoadSegmentFeatureDto.kt:18`); **giữ vậy**, properties không null trên dây |
| `SweepDetailResponse.latest_run`, `CoverageGap.geometry` | **Chưa có DTO tương ứng**; `SweepResponseDto` chỉ nhận `sweep_id` (`SweepDtos.kt:19–21`), không phải DTO detail/run/coverage |
| `PoleListItem/PoleDetail.active_fixture`, `.feeder_source`, `.cabinet_source`; `TopologyPole.feeder_source`; `FeederListItem/FeederDetail.cabinet` | **Chưa có DTO tài sản/topology tương ứng** |
| `AgendaWorkOrder/WorkOrderAgendaGroup.location`; `WorkOrderPole.fixture_type`; `WarrantyItem.fixture_type` | **Chưa có DTO tương ứng**; location của `WorkOrderFaultDetailDto` là trường khác, không được đánh đồng |
| `CitizenPhotoItem.ai_hint`, `CitizenSubmissionItem.photos_dropped`; `LuxReadingWithLuminanceResponse.nearest_luminance`; `IotNodeRelay.control_mode`; `LightingCommandItem.reported_mode` | **Chưa có DTO tương ứng** |
| Request còn lại được ticket nêu: `AcceptCitizenReportRequest.{fault_type,severity}`, `ReportFaultRequest.{location,fault_type,severity}`, `DeviceAckRequest.{result,reported_mode}`, `UpdateUserRequest.role` | **Chưa có DTO tương ứng** |

Đây là đối chiếu các DTO hiện có và các nhóm chưa có, **không tuyên bố 40/40 đã nullable**. Ticket chỉ đưa tổng 40, không liệt kê từng schema/property. Quét tĩnh rộng C# nullable + `$ref` của spec hiện có tìm **57 ứng viên** (gồm `[Required]` trên request; chưa phải kiểm reflection/NRT), và thấy **6** schema `*Feature.properties`, có thêm `SyncPolePropertiesFeature`. Vì vậy P1b cần xuất danh sách chính xác và quy tắc đếm; không dùng bảng P1a làm whitelist cho filter.

## 3. FE đang giả định không-null ở đâu?

**WP5: chưa thấy truy cập object nullable không có guard trong các chỗ tìm được.** Các dòng đọc thẳng cần phân biệt:

| File:dòng WP5 | Truy cập / đánh giá |
|---|---|
| `src/hooks/assets/useAssetData.ts:107–112` | `item.active_fixture.x`; **đã guard** bằng `if (item.active_fixture && item.pole_id)` ở 104 |
| `src/pages/gis-map/components/GisDrawerPanel.tsx:108–111` | `poleDetail.iot_node.x`; **đã guard** `if (poleDetail?.iot_node)` ở 106 |
| `src/pages/gis-map/components/PoleDetailDrawer.tsx:218,221,225` | `pole.iot_node.x`; **đã guard** `pole.iot_node &&` ở 213 |
| `src/pages/gis-map/components/PoleDetailDrawer.tsx:23,35,37,42` | Interface viết tay dùng `fixture?`, `iot_node?`, `luminance_baseline?` và `source_channel: string`, **thiếu null trong type**; truy cập fixture ở 181/201/206 dùng `?.` |
| `src/pages/gis-map/components/GisDrawerPanel.tsx:386–405` | Fixture dùng `?.`; null không gây dereference nhưng UI điền giá trị giả (100W/ngày lắp/hạn bảo hành). Cần trạng thái “chưa có bóng/chưa biết”, không coi fallback là dữ liệu thật |

Không tìm thấy consumer `.latest_run`, `.nearest_luminance`, `.ai_hint`, `.reported_mode`, `.control_mode`, `.inspection_outcome` trong source WP5 ngoài types/mock/test ở lượt tìm này. Không suy ra chúng an toàn khi được hiện thực về sau.

**WP6 có giả định non-null rõ:** `feature/map/data/PoleDetail.kt:69–73` đọc `fixture.fixtureType/powerSource/lampWatt/installDate/warrantyExpiry`; dòng 77 gán `currentStatus.sourceChannel` vào model `String` (28). DTO fixture/baseline non-null có thể từ chối JSON null ngay lúc deserialize, trước bước mapping. Repo detail hiện có `FakePoleDetailRepository` (`feature/map/data/FakePoleDetailRepository.kt:13`), nên không coi việc mock chạy được là xác nhận response backend hợp lệ. `iotNode` đã được mapping bằng `?.` (`PoleDetail.kt:78–80`).

## 4. Nếu sửa spec hẹp, mỗi bên cần làm gì?

- **WP5:** sửa `openApiTypeToTs` để wrapper đã chọn sinh `T | null`, giữ quy tắc `required` riêng; hoặc chuyển sang thư viện 7.13.0 nhưng đó là thay đổi pipeline lớn hơn. Sau đó sinh lại `src/types/**` từ Swagger đã sửa (hoặc URL spec hợp nhất được công bố), rà interface viết tay của drawer, xử lý UI thiếu fixture/baseline/enum. Chỉ đổi spec rồi chạy script cũ sẽ **mất type thành `any`**, không sửa được vấn đề. Cần kiểm type/UI bằng mẫu null sau khi được phép thực thi.
- **WP6:** **không có bước regenerate DTO** hiện tại. Sửa tay 5 trường response đang thiếu `?` trong bảng (fixture, baseline, source_channel, power_source, fixture_type), cập nhật mapping/domain/UI cho trạng thái thiếu dữ liệu; giữ 2 trường đã nullable. DTO chưa có phải khai đúng null khi xây tính năng. Kiểm deserialize mẫu null + UI; không nới request chỉ vì C# dùng nullable để trả 400.
- **BE:** công bố danh sách schema/property đổi và cả Swagger xuất từ code lẫn bản hợp nhất. Không sửa enum chung thành nullable vì sẽ ảnh hưởng mọi consumer của enum; sửa tại thuộc tính response. Giữ `Feature.properties` non-null theo luật generic/shape được P1b xác minh.

**Lưu ý số lượng:** “40 − 5 = ~35” là bỏ Feature, **chưa bỏ request**. Sáu request family ticket nêu chứa 10 thuộc tính; nếu tập 40 đúng thì còn **25 response**, trước khi xét response kế thừa và request có null mang nghĩa riêng. Chốt whitelist/cơ chế ở P1b, không gọi mặc định 35 trường đều là response.

## Kết luận cho backend

Khuyến nghị **chưa nâng toàn bộ OpenAPI lên 3.1 chỉ cho ticket này**: script WP5 hiện tại đều không hiểu hai dạng; WP6 viết tay nên đổi dialect không tự sửa DTO. Giữ thay đổi theo từng thuộc tính và phối hợp sửa WP5 generator/WP6 DTO trước khi kết luận client tương thích.

`allOf + nullable` được openapi-typescript 7.13.0 sinh đúng union, nhưng không đủ chứng minh mọi validator 3.0 chấp nhận null: [OAS 3.0.4, Schema Object](https://spec.openapis.org/oas/v3.0.4.html#schema-object) yêu cầu `type` cùng Schema Object để nullable có tác dụng và vẫn giữ các constraint khác. P1b cần phân biệt mục tiêu sinh types với tính hợp lệ schema; cách encoding cuối cùng là D-item chờ Mỹ chốt.

✅ Đã xác minh bằng source/ref và source công cụ phiên bản cố định. ⚠️ Chưa chạy generator, deserialize hay UI/build; chưa kiểm reflection để tái xác nhận con số 40. 📌 Kết quả runtime của wrapper là suy luận từ source, không phải output thực thi.

### Bằng chứng terminal tiêu biểu

```text
$ git -C ../luxmap-web grep -n -i -E 'openapi|swagger|generate:|luxmap-v1' origin/dev -- package.json '*lock*' '*config*' 'scripts/*' README.md .ai
origin/dev:package.json:14:    "generate:types": "node scripts/generate-types.js",
origin/dev:package-lock.json:6929:      "resolved": "https://registry.npmjs.org/openapi-typescript/-/openapi-typescript-7.13.0.tgz",
origin/dev:scripts/generate-types.js:28:const swaggerUrl = baseUrl.endsWith('.json') ? baseUrl : `${baseUrl.replace(/\/$/, '')}/swagger/v1/swagger.json`

$ git -C ../mobile grep -n -E '^' origin/dev -- app/src/main/java/com/luxmap/feature/map/data/dto/PoleDetailDto.kt
origin/dev:app/src/main/java/com/luxmap/feature/map/data/dto/PoleDetailDto.kt:15:    val fixture: PoleDetailFixtureDto,
origin/dev:app/src/main/java/com/luxmap/feature/map/data/dto/PoleDetailDto.kt:18:    @SerialName("iot_node") val iotNode: PoleDetailIotNodeDto? = null,
origin/dev:app/src/main/java/com/luxmap/feature/map/data/dto/PoleDetailDto.kt:19:    @SerialName("luminance_baseline") val luminanceBaseline: PoleDetailLuminanceBaselineDto,
```

Các dòng trên trích từ lượt grep thật (lượt mobile đọc gộp nhiều DTO); khảo sát và kiểm đầu ra dùng 27 lượt shell, chỉ ghi file kết quả qua patch.
