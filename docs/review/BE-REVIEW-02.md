# BE-REVIEW-02 — Review toàn diện backend + đề xuất + Contract hợp nhất (DRAFT)

**Ngày:** 18/09/2026 · **Người review:** reviewer độc lập (Claude) · **Phạm vi:** `origin/dev` @ `f8bb55b`
(working tree HEAD `0c0ead9` trên nhánh `feat/BE-12-pole-delete-and-feeder`, đã merge vào `origin/dev`
qua PR #38 — cây mã giống hệt, chỉ thiếu merge commit).

**Trạng thái:** PHASE 1 — chỉ đọc. Không sửa code, không chạy migration, không ghi DB. Ba file mới:

| File | Vai trò |
|---|---|
| `docs/review/BE-REVIEW-02.md` | Báo cáo này |
| `docs/contract/api-contract-v1.2.draft.md` | Contract hợp nhất (DRAFT) |
| `docs/openapi/luxmap-v1.2.draft.json` | OpenAPI khớp 1-1 với file md, lint sạch lỗi |

⛔ **HARD STOP sau báo cáo này.** Chờ Dylan duyệt findings và trả lời **mục 5** trước khi Phase 2.

**Giả định đã ghi (không im lặng):**

| A | Giả định | Vì sao |
|---|---|---|
| A-1 | Review trên cây mã HEAD = `origin/dev` (không checkout `dev` local — local `dev` @ `e278105` **đứng sau** origin 1 merge) | `git merge-base HEAD origin/dev` = HEAD; `git diff HEAD origin/dev --stat` rỗng |
| A-2 | OpenAPI draft đặt ở `docs/openapi/luxmap-v1.2.draft.json` (đề bài ghi `openapi/…` — thư mục đó không tồn tại; spec gốc nằm ở `docs/openapi/`) | Q-1 |
| A-3 | Thân contract draft giữ nguyên chữ của các quyết định `SELF-SIGNED` đã nằm trong v1.3 (A, C, D) kèm nhãn, và liệt kê nhóm `/assets/…` với nhãn `[PENDING DYLAN]` để OpenAPI khớp 1-1 với code | Rule "chỉ FOLD vào thân" xung đột với "OpenAPI khớp 1-1 với md" nếu bỏ nhóm đang chạy; đã nêu ở đầu draft |
| A-4 | `docs/tasks-backend.csv` cột status/% coi là dead field (theo đề bài) | — |

---

## 1. Baseline (output thật)

### 1.1 Git

```
$ git status
On branch feat/BE-12-pole-delete-and-feeder
Your branch is up to date with 'origin/feat/BE-12-pole-delete-and-feeder'.
Untracked files:
	AGENTS.md

$ git log --oneline -20 origin/dev
f8bb55b Merge pull request #38 from LuxMapLabs/feat/BE-12-pole-delete-and-feeder
0c0ead9 docs(BE-12): record what drift 43 still owes, and ban `git add -A`
cf75a2a fix(BE-12): give feeder_id a real type in the spec instead of an empty schema
3e54c76 fix(BE-12): close the cross-commune feeder hole on POST as well as PUT
3d9ba89 chore: untrack AGENTS.md, swept into this branch by mistake
c8190d9 docs(BE-12): register drift 43 for the two new pole endpoints and ASSET_IN_USE
fb3eddc test(BE-12): cover pole delete and feeder assignment against the real constraints
819fee8 feat(BE-12): set or clear a pole's feeder through a narrow PUT
bc7e963 feat(BE-12): delete a pole, and let the foreign keys decide whether it may go
54c41e0 feat(BE-12): add ASSET_IN_USE for a delete a foreign key refuses
56bbf39 Merge pull request #37 from LuxMapLabs/docs/BE-12a-template-example-split
5bba57a docs(BE-12a): split the import templates from their runnable examples, and register drift 42
19b6966 Merge pull request #36 from LuxMapLabs/fix/test-fixtures-self-cleanup
12e735d test: make the fixtures delete what they create, and stop hardcoding a seeded id
de1b535 Merge pull request #35 from LuxMapLabs/feat/BE-42-lux-readings-nested-route
2ff4867 feat(BE-42)!: move the per-pole lux series under /lux-readings
e278105 Merge pull request #34 from LuxMapLabs/feat/BE-CORS-dev-origin
4bea4c5 build(api): listen on 5141, the port the web client's env file already names
0a8959d feat(api): let the web SPA's http dev server reach the API in Development
b50867d Merge pull request #33 from LuxMapLabs/docs/tracking-beauth-and-registry

$ git rev-parse dev origin/dev HEAD
e278105c124a4183aa606be7cc3a1d2e87f87695   # dev (local) — cũ hơn origin 1 merge
f8bb55bb44c7704cc097c273e28c12c77e948e87   # origin/dev
0c0ead975c6299f31c75726179f0bf6fdc6cece3   # HEAD = merge-base(HEAD, origin/dev)
```

### 1.2 Build

```
$ dotnet build -c Release
  ... 15 project
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.39
```

Không có warning RS0030 (BannedApiAnalyzers) — quy tắc đặt ở mức **error** trong `.editorconfig`, nên
nếu có thì build đã đỏ.

### 1.3 Test (chạy trên DB dev `luxmap_dev` thật, container `luxmap_postgres`)

```
$ dotnet test -c Release --no-build
Passed!  - Failed: 0, Passed: 147, Skipped: 0, Total: 147 - LuxMap.Shared.Tests.dll
Passed!  - Failed: 0, Passed:  20, Skipped: 0, Total:  20 - LuxMap.Persistence.Tests.dll
Passed!  - Failed: 0, Passed:  18, Skipped: 0, Total:  18 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed: 0, Passed: 274, Skipped: 0, Total: 274 - LuxMap.Api.Tests.dll
```

**459 test xanh** (benchmark `Category=Benchmark` bị loại theo `luxmap.runsettings`). `tracking.html`
ghi 443 — số đã cũ.

### 1.4 Package

```
$ dotnet list package --vulnerable --include-transitive
The given project `<15 project>` has no vulnerable packages given the current sources.

$ dotnet list package --outdated   (rút gọn — chỉ top-level)
LuxMap.Api:            Microsoft.AspNetCore.Authentication.JwtBearer 10.0.11 → 10.0.12; Microsoft.EntityFrameworkCore.Design 10.0.11 → 10.0.12
LuxMap.Infrastructure.Storage: AWSSDK.S3 4.0.102.4 → 4.0.103.3; SixLabors.ImageSharp 3.1.12 → 4.1.2 (KHÔNG nâng — xem CLAUDE.md);
                       Microsoft.Extensions.{DependencyInjection,Logging}.Abstractions 10.0.1 → 10.0.12
LuxMap.Modules.Identity: Microsoft.IdentityModel.JsonWebTokens 8.15.0 → 8.22.0
LuxMap.Persistence:    Microsoft.EntityFrameworkCore{,.Relational} 10.0.11 → 10.0.12
Test projects:         coverlet.collector 6.0.4 → 10.0.1; Microsoft.NET.Test.Sdk 17.14.1 → 18.10.1; xunit.runner.visualstudio 3.1.4 → 4.0.0;
                       Microsoft.AspNetCore.Mvc.Testing 10.0.11 → 10.0.12; Microsoft.Extensions.TimeProvider.Testing 10.0.0 → 10.10.0
```

ImageSharp ghim **3.1.12** đúng ràng buộc (4.x đòi license key lúc build Release).

### 1.5 Migration và schema live

```
$ psql: select migration_id from __ef_migrations_history order by 1;
 20260829083507_InitialIdentity
 20260829093106_AddRefreshTokenChainTracking
 20260831061451_AddCaseInsensitiveIdentityIndexes
 20260901082917_AddAssetEntities
 20260901084931_FixPrefixedIdOverflow
 20260901091555_AddCommuneForeignKeys
 20260905030514_AddLuxReading
 20260905034539_RejectNonFiniteLuxValues
 20260905085759_AddFaultEntities
 20260905095851_AddAssetExternalRef
 20260911160551_AddRefreshTokenSessionKind
(11 rows) — khớp đủ 11 file trong src/LuxMap.Persistence/Migrations/
```

`\d+` năm bảng (trích phần quyết định; bản đầy đủ đã đọc lúc review):

```
road_segment: segment_id text DEFAULT luxmap_format_id('SEG', nextval('segment_id_seq'), 3)
  ix_road_segment_commune_id btree(commune_id) | ix_road_segment_geom gist(geom)
  ux_road_segment_commune_external_ref UNIQUE (commune_id, external_ref) WHERE external_ref IS NOT NULL
  CHECK ck_road_segment_data_source, ck_road_segment_road_class | FK commune_id → administrative_unit RESTRICT
feeder: feeder_id DEFAULT luxmap_format_id('FDR', …, 3); geom geometry(LineString,4326) NULLABLE; cùng bộ index/FK như trên
pole: pole_id DEFAULT luxmap_format_id('POLE', …, 4); feeder_id text NULL; geom geometry(Point,4326) NOT NULL
  ix_pole_commune_id | ix_pole_feeder_id | ix_pole_geom gist | ix_pole_segment_id | ux_pole_commune_external_ref (partial)
  FK feeder_id RESTRICT, segment_id RESTRICT, commune_id RESTRICT
  Referenced by: fault RESTRICT, lux_reading RESTRICT, fixture CASCADE, pole_current_status CASCADE
fixture: fixture_id DEFAULT luxmap_format_id('FIX', …, 4); install_date date NOT NULL; removed_date/warranty_expiry date NULL
  ix_fixture_pole_id_active btree(pole_id) WHERE removed_date IS NULL (KHÔNG unique) | FK pole_id CASCADE, commune_id RESTRICT
pole_current_status: pole_id PK/FK CASCADE; status_confidence double precision NULL
  CHECK ck_pole_current_status_confidence_matches_status ((status_confidence IS NULL) = (fixture_status = 'unknown'))
  CHECK ck_pole_current_status_fixture_status IN (normal,dim,out,unknown)
  ⚠ KHÔNG có CHECK 0..1 / NaN / Infinity cho status_confidence (drift 24 — vẫn mở)
```

Hàm sinh ID trên DB (chép từ `pg_get_functiondef`):

```sql
CREATE OR REPLACE FUNCTION public.luxmap_format_id(prefix text, value bigint, digits integer)
 RETURNS text LANGUAGE sql IMMUTABLE STRICT
AS $function$ SELECT prefix || '-' || lpad(value::text, greatest(digits, length(value::text)), '0') $function$
```

Số dòng thật (DB dev):

```
 pole 0 (feeder_id NULL: 0/0) | road_segment 0 | feeder 0 | fixture 0 | pole_current_status 0
 fault 0 | fault_cluster 0 | lux_reading 0 | administrative_unit 1 (COM-070 "Commune 01", seed_key study_site)
 app_user 4 | refresh_token 6168
 pole_id_seq.last_value = 396625 · commune_id_seq = 904 · user_id_seq = 1109
```

→ Câu hỏi "`pole.feeder_id` NULL bao nhiêu row": **0 trên 0 dòng** — DB dev không có tài sản; xem F-06
về lý do sequence ở 396 625.

### 1.6 Lint OpenAPI hiện hành (baseline)

```
$ npx @redocly/cli lint docs/openapi/luxmap-v1.json
❌ Validation failed with 23 errors and 38 warnings.
  21 error   operation-summary       Operation object should contain `summary` field.
   1 error   no-empty-servers        Servers must be present.
   1 error   nullable-type-sibling   The `type` field must be defined when the `nullable` field is used.  (ApiError.details.additionalProperties, dòng 1731)
  21 warning operation-operationId · 8 warning no-unused-components (8 enum chưa DTO nào dùng) · 5 warning tag-description
   3 warning operation-4xx-response (GET /assets/feeders, GET /assets/poles, GET /lux-readings) · 1 warning info-license
```

Không có `pattern` nào trong spec hiện hành (đúng như drift 40 ghi).

---

## 2. Findings

**Thang:** 🔴 High — dữ liệu sai/rò hoặc demo hỏng · 🟠 Medium — sai âm thầm trong điều kiện thực tế · 🟡 Low — lệch tài liệu, nợ nhỏ · ℹ️ Info.

| ID | Sev | Vùng | File:line | Mô tả | Bằng chứng | Đề xuất fix |
|---|---|---|---|---|---|---|
| **F-01** | 🟠 | Multi-tenancy / data integrity | `src/LuxMap.Modules.Assets/Import/AssetImportService.cs:246-264` | **Đường nạp file KHÔNG kiểm feeder/segment cùng xã với pole** — lỗ D-10 vừa vá ở POST/PUT vẫn mở ở import. `Resolve()` tra `*_external_ref` trên **mọi** xã caller thấy được, rồi gán thẳng `SegmentId`/`FeederId` (dòng 273-274, 286-287). `RequireFeederInCommuneAsync` chỉ được gọi từ `CreatePoleAsync` và `SetPoleFeederAsync` (`AssetCrudService.cs:83, 205`). | Drift 43 ghi "*một helper dùng chung chặn ở CẢ POST và PUT*" và không nhắc import. Quản trị viên hai xã nạp `poles.csv` có `feeder_external_ref` duy nhất nằm ở xã kia → cột xã A gắn mạch xã B, không lỗi, không log. Đây là bulk path BE-39 sẽ dùng để seed. | Ngắn hạn: gọi cùng helper (hoặc so `CommuneId`) trong `PlanPolesAsync` cho cả segment lẫn feeder, kèm test sabotage hai chiều. Dài hạn: FK ghép `(feeder_id, commune_id)` / `(segment_id, commune_id)` như drift 43 đã viết — ticket riêng. |
| **F-02** | 🟠 | Chất lượng chung / thời gian | `src/LuxMap.Modules.Survey/LuxReadings/LuxReadingService.cs:152,158` | `GET /lux-readings?from=&to=` **lệch múi giờ theo máy chủ** khi client bỏ hậu tố `Z`. Query binder trả `Kind=Unspecified`; `.ToUniversalTime()` coi đó là giờ **local** rồi dịch. | Probe (console .NET 10, mô phỏng `DateTimeModelBinder`: Invariant + `AllowWhiteSpaces \| AdjustToUniversal`), máy TZ `Asia/Saigon`: `2026-10-01T00:00:00` → service dùng **`2026-09-30T17:00:00Z`** (lệch 7 h); `…Z` và `…+07:00` đúng. Body JSON không bị (đi qua `UtcDateTimeConverter.ToUtc`). Không test nào gửi `from`/`to`. CV-12 kéo theo ngày sẽ mất/thừa 7 giờ số đo — **CONFIRMED** trên máy không phải UTC. | Chuẩn hoá bằng `UtcNormalization.ToUtc` (Unspecified = UTC, đúng Contract) hoặc trả 400 khi thiếu `Z`; thêm test `from=…` không `Z`. Quyết chọn cách nào: **D-12**. |
| **F-03** | 🟠 | Hiệu năng | `AssetImportService.cs:347-350` | **N+1** trong nạp bóng: mỗi dòng hợp lệ chạy một `SingleAsync` lấy `CommuneId` của cột, dù `ReferenceIndexAsync` (dòng 314) đã tải sẵn các `Pole` đó. `ReferenceIndexAsync` cũng tải nguyên entity (kể cả geometry) chỉ để lấy id. | 103 dòng mock → 103 round-trip thừa; tuyến tính theo file (≤ 10 MB). | Trả `(id, communeId)` từ `ReferenceIndexAsync` (projection), bỏ query trong vòng lặp. |
| **F-04** | 🟠 | Authz / Contract | `src/LuxMap.Api/Http/ApiConventionsSetup.cs:128` | **403 do sai VAI TRÒ trả `COMMUNE_FORBIDDEN`.** Mọi 403 "trần" (policy `Administrator` từ chối kỹ sư) được dựng lại thành mã có nghĩa §7 "commune ngoài phạm vi". | `README.md:336` và `authorization-guide.md` ghi đây là chủ ý; `AssetPermissionTests` chỉ assert status. Contract không có mã cho sai vai trò → WP5/WP6 sẽ hiện "ngoài địa bàn" cho một kỹ sư bấm nút tạo tài sản. | Thêm mã (vd `ROLE_FORBIDDEN`) cho nhánh policy-failure (`IAuthorizationMiddlewareResultHandler` phân biệt `Forbidden` do requirement nào) — **D-4**. |
| **F-05** | 🟠 | Contract text | `docs/api-contract-v1.1.md:175, 564` | Drift 10 và 12 ghi "*đã chốt lên v1.2*" nhưng chữ Contract **chưa đổi**: §1 vẫn "`data_source` gắn trên 5 entity" (code có 8: + `pole`, `fixture`, `road_segment` — `\d+` ở 1.5), §4 vẫn "70/9/17/7, 11 IoT node" (mock hiện 70/10/16/7, 12 node — `mocks/README.md`, đếm thật trong mock). | Đối chiếu trực tiếp file v1.3 với `\d+` và mock. | Đưa vào draft như CONFLICT → **O-5**, sửa chữ khi promote. |
| **F-06** | 🟠 | Test hygiene → **rủi ro demo** | `tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:44-46`, `CommuneWriteGuardCostTests.cs`, `AssetImportFixture.cs:132` | Test chạy trên **DB dev dùng chung** và để lại cặn: `pole_id_seq = 396 625` với **0** cột; `refresh_token` **6 168** dòng (engineer 3 879, admin 1 109, agency 677, crew 503 — `SeededClientAsync` đăng nhập mà không bao giờ xoá token của tài khoản seed); `commune_id_seq = 904`; `max(created_at) = 2026-09-30` (FakeTimeProvider). | SQL ở 1.5. Hệ quả thật: cột đầu tiên tạo trên DB này là **`POLE-396626`**, nên BE-39 **không thể** seed ra `POLE-0001…0103` khớp bộ mock FE nếu để sequence sinh ID. | (1) BE-36 Testcontainers — DB sạch mỗi lượt; (2) tạm: fixture xoá `refresh_token` của user seed nó đã login; (3) **D-6**: chiến lược seed BE-39 (INSERT ID tường minh + `setval`). |
| **F-07** | 🟡 | API | `src/LuxMap.Modules.Assets/Crud/AssetsController.cs:174-175` | Header `Location` của 201 trỏ `/api/v1/assets/{segments\|feeders\|poles\|fixtures}/{id}` — **không có route GET nào ở đó** (`GET …/poles/{id}` → 405 vì chỉ có DELETE/PUT; segments/feeders/fixtures → 404). | Danh sách route ở 4.1; `AssetPermissionTests:92` chỉ assert chuỗi. | Chấp nhận tới BE-12b (Q-5) hoặc bỏ `Location` tạm thời. |
| **F-08** | 🟡 | Tài liệu trong code ↔ code | nhiều | Chú thích trái với code: `AssetCrudService.cs:19` "**No delete**" trong khi `DeletePoleAsync` tồn tại (dòng 152); `FaultsModule.cs:9` "Empty shell… no entities" (có `Fault`, `FaultCluster`); `ApiHeaders.cs:6-7` "*never placed in the body*" trong khi `ExceptionHandlingMiddleware.cs:82-85` ghi `details.correlation_id`; `AssetSchemaFixture.cs:48-50` "LPAD defect… still entirely live" (đã sửa từ `8ea9930`). | grep + đọc. | Sửa ở Phase 2 cùng commit docs; CLAUDE.md xem mục 6. |
| **F-09** | 🟡 | Contract / mã lỗi | `AssetCrudService.cs:326-335` | Feeder **cùng phạm vi caller** nhưng khác xã với pole → `403 COMMUNE_FORBIDDEN`. Cả hai xã đều trong scope, nên "ngoài phạm vi" là sai nghĩa; đây là lỗi nhất quán dữ liệu (400/409). | `PoleWriteTests:283-295` khoá hành vi này. | **D-5**. |
| **F-10** | 🟡 | Quy tắc nghiệp vụ | `AssetImportService.cs:317, 331-337`; `AssetConfigurations.cs:186-197`; `AssetCrudService.cs:105` | Ba định nghĩa khác nhau về "bóng của cột": import từ chối cột **đã từng** có bóng (kể cả `removed_date` đã set — `OccupiedPolesAsync` không lọc), CRUD cho phép **nhiều** bóng active cùng lúc, còn BE-14 sẽ "flatten từ **bóng active**" (số ít). Không có ràng buộc DB "tối đa một bóng active/cột". | Đọc code; index `ix_fixture_pole_id_active` cố ý không unique. | **D-11** chốt cardinality trước BE-14. |
| **F-11** | 🟡 | Data integrity | `AssetCrudService.cs:213-220`; schema `fixture` | `PUT …/removal` nhận `removed_date < install_date` và ngừng-dùng-lại nhiều lần; DB không có CHECK `removed_date >= install_date`; `length_m`/`lamp_watt` chỉ chặn dương ở DTO (`[Range]`), DB không CHECK. | `\d+ fixture`, `\d+ road_segment`. | Thêm CHECK ở migration kế tiếp chạm hai bảng (BE-13). |
| **F-12** | 🟡 | Contract / registry | `src/LuxMap.Shared/Contracts/Errors/KnownError.cs:56-62`; `ApiConventionsSetup.cs:123-133` | `KnownErrors.All` tự nhận là "*registry of every error code*" nhưng chỉ có **12/20** hằng của `ErrorCodes`; thiếu `ASSET_NOT_FOUND`, `EXTERNAL_REF_TAKEN`, `ASSET_IN_USE`, `SERVER_OWNED_FIELD`, `UNSUPPORTED_IMAGE_FORMAT`, `UNSUPPORTED_MEDIA_TYPE`, `VALIDATION_FAILED`, `INTERNAL_ERROR`. Ba mã "trần" `NOT_FOUND`, `METHOD_NOT_ALLOWED`, `REQUEST_FAILED` là chuỗi rời, không có hằng. | So sánh bằng grep (mục 1.6 của phiên). | Một test "mọi hằng `ErrorCodes` có trong `KnownErrors` và trong bảng Contract" — enhancement N-4. |
| **F-13** | ℹ️ | Vận hành | `IdentityConfigurations.cs:89-90`, `AuthService.cs` | Index `ix_refresh_token_expires_at` khai "*cleanup (BE-07) scans on this column*" nhưng **không có job nào** dọn token hết hạn (110 expired, 612 revoked trong 6 168). | grep `ExpiresAt`: chỉ đọc khi refresh. | BE-26 (Hangfire) — ghi vào kế hoạch. |
| **F-14** | 🟡 | Data integrity (BE-19) | `FaultConfigurations.cs` | `fault.segment_id` "denormalised from pole" nhưng không có ràng buộc `= pole.segment_id`; `confirmed_by/at`, `resolved_by/at` không CHECK theo `fault_status` (fault `detected` có thể mang `resolved_at`). | `\d+ fault`. | BE-19 thêm CHECK khi viết máy trạng thái. |
| **F-15** | 🟡 | Data integrity | `pole_current_status.status_confidence` | Drift 24 **vẫn mở**: cột nhận NaN/Infinity/ngoài 0..1. | `\d+` ở 1.5 — chỉ có CHECK NULL⇔unknown. | Migration thuộc BE-15/BE-17 (chủ quyền ghi); đưa vào Must-have M-6. |
| **F-16** | ℹ️ | OpenAPI | `docs/openapi/luxmap-v1.json`; `SwaggerSetup.cs` | Spec sinh từ code **không qua lint** (23 lỗi ở 1.6). Nguyên nhân ở cấu hình Swashbuckle, không phải ở file: thiếu `servers`, không `IncludeXmlComments` → không `summary`/`operationId`, `ApiError.details` sinh `nullable` không `type`. | Lint output 1.6. | Sửa `SwaggerSetup` (N-6) rồi xuất lại; đưa lint vào CI. |

### Nghi vấn chưa xác minh (không đủ bằng chứng để là finding)

| ID | Nghi vấn | Vì sao chưa xác minh |
|---|---|---|
| S-1 | Query filter §7 (`commune_id = ANY(@ids)`) kết hợp `ST_Intersects` có giữ được GIST index khi bảng lớn nhiều xã? `authorization-guide.md` "Còn nợ" tự thừa nhận chưa đo; `SpatialIndexTests` đo với **một** xã. | DB dev 0 cột; test fixture 2500 cột một xã. BE-14 phải đo `EXPLAIN` với ≥ 2 xã. |
| S-2 | `CorsPolicy.IsOriginAllowed` so origin có phân biệt hoa/thường? Nếu không, `Origin: HTTPS://APP…` có thể qua. | Chưa chạy thử; ảnh hưởng thấp (đằng nào cũng phải là origin thật). |
| S-3 | `AmazonS3Client` singleton với `ForcePathStyle` — hành vi khi MinIO chưa sẵn sàng lúc request đầu (không có health check). | `S3ObjectStore` chưa có test tự động (CLAUDE.md thừa nhận); chưa có endpoint upload nào để bấm. |

---

## 3. Enhancements (chỉ đề xuất — không sửa)

### 3.1 Must-have trước khi bảo vệ

| # | Đề xuất | Giá trị cho đồ án | Effort | Rủi ro | Phụ thuộc |
|---|---|---|---|---|---|
| M-1 | **BE-36 sớm hơn W17**: Testcontainers (PostGIS + MinIO từ quay.io) → DB sạch mỗi lượt; hết cặn F-06, hết 13 pragma teardown | Hội đồng hỏi "test có tin được không?" — hiện test và demo dùng chung DB | M | Docker trên máy Windows của nhóm; image pin digest | BE1 |
| M-2 | **Chiến lược seed BE-39 với ID tường minh** (`INSERT … (pole_id, …)` + `setval` sau seed) — vì sequence dev đang ở 396 625 và FE hardcode `POLE-0047` | Demo khớp FE; không có nó demo hiện `POLE-396672` | S | Vi phạm tinh thần §0.4 "server sinh ID" → phải ghi rõ seeder là ngoại lệ hệ thống | **D-6** |
| M-3 | Vá **F-01** (import) + **F-02** (từ/đến) kèm test sabotage | Hai lỗi câm, dễ bị hỏi khi demo CV-12 / RQ2 | S | — | D-12 |
| M-4 | **FK ghép `(feeder_id, commune_id)` và `(segment_id, commune_id)`** — biến D-10 từ kiểm-ở-app thành ràng-buộc-không-thể-quên (đúng triết lý repo) | Bảo vệ được trước hội đồng: "cùng lớp với `CommuneWriteGuard`" | M (migration + unique index phụ + đọc migration sinh ra) | Migration làm rơi index `commune_id` (bẫy đã gặp ở BE-12a) | Ticket riêng, trước BE-13 |
| M-5 | **Rate limiting** cho `/auth/login`, `/auth/register`, `/auth/web/login` (`Microsoft.AspNetCore.RateLimiting`, fixed window theo IP + username) | Câu hỏi brute-force gần như chắc chắn; drift 5 tự nhận "nếu ra Internet phải xem lại" | S | Không có | — |
| M-6 | **CHECK cho `pole_current_status.status_confidence`** (0..1, `<> 'NaN'`, `<> 'Infinity'`) trước khi BE-15 ghi dòng đầu | Ground truth RQ1 sạch; drift 24 đã mở từ 05/09 | S | Chủ quyền ghi thuộc BE-15/17 — cần Dylan cho phép làm sớm | Dylan |
| M-7 | **Promote Contract hợp nhất** + xuất lại spec (sau N-6) + **CI**: build, test, `redocly lint`, và diff spec sinh ra vs file commit (fail nếu lệch) | Đóng drift 40/E thật sự; WP6 sinh DTO từ file đúng | M | GitHub Actions cần Docker cho test tích hợp (M-1) | Dylan (O-1), Thịnh/Ngọc |

### 3.2 Nên có

| # | Đề xuất | Giá trị | Effort | Ghi chú |
|---|---|---|---|---|
| N-1 | Health checks: `/health/live` (không phụ thuộc), `/health/ready` (Npgsql `SELECT 1` + `HeadBucket` MinIO) | Vận hành/demo: biết vì sao 500 trước khi mở log | S | Không mâu thuẫn "không chạm dịch vụ ngoài lúc **khởi động**" — đây là runtime |
| N-2 | **Không** chuyển sang ProblemDetails — Contract đã khoá `{error:{code,message,details}}` và FE đã bind. Thay vào đó: thêm mã cho 403-sai-vai-trò (F-04) và `RETRY_AFTER`/`RATE_LIMITED` khi có M-5 | Giữ hợp đồng | S | Trả lời trực tiếp mục "error model thống nhất" của đề bài |
| N-3 | API versioning: **đã có** (`Asp.Versioning`, URL segment `/api/v1`, `ReportApiVersions`) — chỉ cần ghi chính sách "v2 khi nào" vào Contract | — | XS | Đã ổn |
| N-4 | Test bất biến mới: (a) mọi hằng `ErrorCodes` nằm trong `KnownErrors` **và** trong bảng Contract md; (b) `from/to` không `Z` (F-02); (c) import feeder/segment khác xã bị từ chối (F-01); (d) `FaultStatusSets.Open` được **dùng** bởi BE-40/BE-28 khi có (hiện chỉ test dùng) | Biến quy ước thành ràng buộc | S | Cùng khuôn `BannedBulkWriteApiTests` |
| N-5 | Fixture test xoá `refresh_token` của user seed nó đã login; `AuthRotationTests` dọn chuỗi của mình | Chặn F-06 tới khi có M-1 | S | — |
| N-6 | `SwaggerSetup`: `servers`, `IncludeXmlComments` (bật `GenerateDocumentationFile` cho module), `operationId` = `{Controller}_{Action}`, map `details` thành `object` → lint 0 lỗi **từ code** | WP6 codegen sạch; hết drift E | S | Sau đó `luxmap-v1.json` sinh lại sẽ trùng phần implemented của draft |
| N-7 | Job Hangfire dọn `refresh_token` hết hạn/thu hồi > N ngày (BE-26) | Bảng không phình vô hạn | S | Index đã có |
| N-8 | `GET /assets/*` thêm filter `data_source` (đã có index) | Tách nguồn dữ liệu ngay ở tầng kiểm kê | XS | Không đổi hình dạng response |
| N-9 | Production: `UseHsts()`, `AllowedHosts` cụ thể, `Kestrel` limits ghi rõ trong `appsettings.Production.json` | Câu hỏi bảo mật triển khai | XS | — |
| N-10 | Structured logging: thêm `UserId`/`Role` vào `LogContext` sau auth (không log claim `commune_ids` dạng chuỗi dài) | Truy vết theo người trong log JSON | XS | Scrubber đã có |

### 3.3 Chiều sâu kỹ thuật (đóng góp cho RQ1/RQ2)

| # | Đề xuất | Đóng góp | Effort | Phụ thuộc |
|---|---|---|---|---|
| T-1 | **Topology feeder cho RQ2 (BE-13):** `GET /assets/feeders/{id}/poles` + view/query "cột trên feeder X" đi qua `ix_pole_feeder_id`; cụm `segment_outage` (CV-15) gom theo **feeder** rồi chiếu sang `segment` để phát `has_active_segment_fault` | Root-cause inference cần đúng cạnh điện; hiện `fault_cluster.segment_id` chỉ giữ đường | M | **Drift 23**: mock không có `feeder_id` → không có dữ liệu để chạy; D-7 |
| T-2 | **Pipeline detection → `pole_id` (CV-05):** truy vấn hai tầng đã chốt ở BE-10 (bbox 4326 qua GIST → `SpatialFunctions.DistanceMeters` 3405) đóng gói thành một repository method `NearestPoleAsync(point, radiusM, communeScope)` kèm test `EXPLAIN` theo khuôn `SpatialIndexTests`; ngưỡng bán kính là tham số cấu hình (BE-33), không hard-code | Bảo vệ được: "sai 109 290 lần nếu đo bằng độ" là câu chuyện kỹ thuật tốt cho hội đồng | M | BE-13, BE-15 |
| T-3 | **Tái lập kết quả (BE-34):** bảng `model_version` (id, tên, hash, ngày) với FK từ `fault.detection_model_version` và `fault_cluster.clustering_model_version` thay vì text tự do | "Kết quả nào do model nào sinh" thành ràng buộc, không phải quy ước | S | BE-34 |
| T-4 | **Ghép `nearest_luminance` (BE-17):** `LATERAL` join theo `(pole_id, observed_at)` ±48 h, index `(pole_id, observed_at)` trên `luminance_history`; thêm `x-luxmap` ghi rõ khoảng ghép là **đóng hay mở** ở biên | CV-12 và báo cáo dùng đúng một phép ghép | S | BE-15/17 |
| T-5 | **Tách nguồn dữ liệu ở thống kê (BE-28/30):** mọi query aggregate nhận `data_source` bắt buộc hoặc `GROUP BY data_source`; test "không có endpoint thống kê nào trả một con số gộp ba nguồn" | Tránh "lỗi nghiêm trọng" CLAUDE.md nêu | S | O-5 |
| T-6 | **Runtime phiên đêm (IOT/BE-20):** định nghĩa `night_of` = ngày bắt đầu phiên theo giờ địa phương Asia/Ho_Chi_Minh, lưu UTC, tính runtime trên khoảng `[dusk, dawn)` cắt qua nửa đêm — ghi thành luật Contract trước khi mock `runtime_history` thành API | Tránh tính theo ngày lịch (anti-pattern đã nêu) | S | O-11 |

---

## 4. Contract ↔ Drift ↔ Code

Liên kết: [`docs/contract/api-contract-v1.2.draft.md`](../contract/api-contract-v1.2.draft.md) ·
[`docs/openapi/luxmap-v1.2.draft.json`](../openapi/luxmap-v1.2.draft.json).

### 4.1 Bảng endpoint

| Endpoint | Contract v1.1 (file v1.3) | Drift liên quan | Code thực tế | Khớp? |
|---|---|---|---|---|
| `GET /poles` | §2.1 | 10 (mặc định loại `calibration_rig`?), 14 | **Không có** | `[NOT IMPLEMENTED]` BE-14 |
| `GET /poles/{id}` | §2.2 | 6 (`supplier`, `SWEEP-…`, `FRM-88213`) | Không có | `[NOT IMPLEMENTED]` BE-20 |
| `GET /segments` | §2.3 | 42 (tên tuyến) | Không có | `[NOT IMPLEMENTED]` BE-14 |
| `GET /faults` | §2.4 (+ `pole_id` A) | 27, 28, 35, 36, 38 | Không có `FaultsController` | `[NOT IMPLEMENTED]` BE-40 — **filter `pole_id` chưa có** |
| `PATCH /faults/{id}` | §2.5 | 26, 27 | Không có | `[NOT IMPLEMENTED]` BE-19 — **409 chuyển trạng thái chưa có** |
| `GET/POST/PATCH /work-orders`, `POST …/evidence` | §2.6 | 15, 38 | Không có (module rỗng) | `[NOT IMPLEMENTED]` BE-21..24 |
| `GET /iot-nodes`, `GET /sweeps`, `GET /frames/{id}/thumbnail` | §2.7 | 6, 15 | Không có; `IObjectStore` + thumbnail đã có (BE-11) | `[NOT IMPLEMENTED]` |
| `POST /faults` | §2.8 | 25 | Không có; entity + CHECK `ck_fault_pole_or_location` đã có | `[NOT IMPLEMENTED]` BE-41 |
| `POST /lux-readings` | §2.9 | 18, 19, 21 | `LuxReadingsController.CreateAsync` | ✅ khớp + drift (bắt buộc/không, `measured_by`, `SERVER_OWNED_FIELD`) |
| `GET /lux-readings/poles/{id}` | §2.9 (v1.3) | 20 | `ForPoleAsync` | ✅ khớp + drift (phân trang) |
| `GET /lux-readings` | §2.9 | 17 | `SearchAsync` | ✅ khớp + drift (`nearest_luminance` null) + **F-02** |
| `POST /auth/{login,register,refresh,logout}` | §2.10.1 | 1, 5 (F) | `AuthController` | ✅ |
| `POST /auth/web/{login,refresh,logout}` | §2.10.2–2.10.7 | 41 (F) | `WebAuthController` + `RequireAllowedOrigin` | ✅ |
| `GET /sync/bundle`, `POST /sync/push` | §3 | — | Không có | `[NOT IMPLEMENTED]` BE-43 |
| `GET /assets/{segments,feeders,poles}` | **không có** | 29, 34 | `AssetsController` (chỉ ID) | drift đã đăng ký |
| `POST /assets/{segments,feeders,poles,fixtures}` | không có | 29, 31, 33 | `AssetsController` | drift đã đăng ký |
| `DELETE /assets/poles/{id}`, `PUT /assets/poles/{id}/feeder` | không có | 43 | `AssetsController` | drift đã đăng ký (SELF-SIGNED 18/09) |
| `PUT /assets/fixtures/{id}/removal` | không có | 29 (ngầm) | `AssetsController` | drift 29 chỉ nói "CRUD" — nên nêu tên |
| `POST /assets/import/{kind}` | không có | 30 | `AssetImportController` | drift đã đăng ký |

**Định nghĩa "open fault":** `FaultStatusSets.Open = {detected, confirmed, in_progress}` — đúng đề
bài; `IsOpen()` chưa được gọi ở `src/` nào (chỉ test) vì chưa có endpoint đếm.

### 4.2 Bảng drift

| Drift | Nội dung | Decision maker | Date | SELF-SIGNED? | Đã implement? | Phân loại |
|---|---|---|---|---|---|---|
| A (35+37) | `pole_id` trên `GET /faults`; khuôn ID `[0-9]{n,}` | Dylan | 07/09/2026 | **Có** | Khuôn: trong Contract + `PrefixedIds`; filter: **chưa** | **CONFIRM** (đã trong v1.3) → O-3 |
| B (29,30,34) | Nhóm `/assets`, kết quả import 200, GET trả ID | Dylan (thông qua theo hạn) | 07/09/2026 | Có | Có | **CONFIRM** → O-4 |
| C (38) | `work_order_id` emit `null` | Dylan | 07/09/2026 | Có | Chưa (không có `GET /faults`) | **CONFIRM** → O-3 |
| D (39) | §0.4 `LPAD` → `luxmap_format_id` | Dylan | 07/09/2026 | Có | Có (`8ea9930`, `\d+`) | **FOLD** (sửa lỗi; code = DB = Contract) — vẫn liệt kê O-3 cho đủ thủ tục |
| E (40) | Không sửa tay spec | Dylan | 07/09/2026 | Có | Spec đã sinh lại (21 op) | **OBSOLETE** một phần: khoảng trống còn lại = endpoint chưa code; quy trình giữ (M-7) |
| F (1,5,41) | Auth web cookie; register | Dylan | 11/09/2026 | **Không** | Có | **FOLD** |
| 2 | 7 mã lỗi ngoài Contract | — | — | — | Có; 4 mã đã vào §2.10.6 | **CONFIRM** (`VALIDATION_FAILED`, `INTERNAL_ERROR`, `UNAUTHENTICATED`) → O-2 |
| 3 | Giá trị `user_role` | — | — | — | Có (CHECK `ck_app_user_role`, claim) | **CONFIRM** → O-6 |
| 4 | Correlation id header + body | — | — | — | Có | **CONFIRM** → O-2 |
| 6 | Mock lệch prefix | — | — | — | Mock **vẫn** `USR-khang`, `SWEEP-2026-…`, `FRM-88213`, `SUP-004`, `NODE-0047` (kiểm 18/09) | **CONFLICT** (mock ↔ §0.2) → O-9 |
| 7 | `page_size` kẹp im lặng | — | — | — | Có (`PageRequest.Create`) | **CONFIRM** → O-2 |
| 8 | Route sai → 401 chưa đăng nhập | — | — | — | Có | **CONFIRM** → O-2 |
| 9 | CLAUDE.md thiếu `data_source` | — | — | — | Đã có trong CLAUDE.md | **OBSOLETE** |
| 10 | §1 ↔ §2.9 về `data_source` | "đã chốt" (không tên) | — | — | Code có 8 entity; **chữ §1 chưa đổi** | **CONFLICT** → O-5 |
| 11 | FK `commune_id` | Dylan (đã làm) | 01/09 | — | Có | **OBSOLETE** cho Contract (nội bộ) |
| 12 | Mock `POLE-0047` | Dylan (đã sửa) | 01/09 | — | Mock đã sửa; **§4 Contract chưa** | **CONFLICT** (chữ §4) → O-5 |
| 13 | `LPAD` cắt ID | Dylan | 01/09 | — | Có | **FOLD** (qua D) |
| 14 | ID không cố định độ dài | — | — | — | Có | **CONFIRM** (đã trong §0.2/0.3 qua A) → O-3 |
| 15 | `UNSUPPORTED_IMAGE_FORMAT` | — | — | — | Có hằng, chưa endpoint nào phát | **CONFIRM** → O-2 |
| 16 | ImageSharp 3.x | — | — | — | Có (3.1.12) | **OBSOLETE** cho Contract (nội bộ, CLAUDE.md) |
| 17 | `nearest_luminance` luôn null | — | — | — | Có | **CONFIRM** → O-12 |
| 18 | §2.9 bảng bắt buộc | — | — | — | Có | **CONFIRM** → O-12 |
| 19 | `measured_by` | — | — | — | Có | **CONFIRM** → O-12 |
| 20 | Phân trang `/lux-readings/poles/{id}` | — | — | — | Có | **CONFIRM** → O-12 |
| 21 | `SERVER_OWNED_FIELD` | — | — | — | Có | **CONFIRM** → O-2/O-12 |
| 22 | `pole.external_ref` | — | — | — | Có | **OBSOLETE** (thay bằng 32) |
| 23 | Mock không có `feeder_id` | — | — | — | Mock vẫn không có (kiểm 18/09) | **CONFLICT/OPEN** → O-9 (chặn RQ2) |
| 24 | `status_confidence` hở | — | — | — | **Chưa sửa** (`\d+`) | OPEN nội bộ → M-6 (không phải Contract) |
| 25 | `commune_id` trong `POST /faults` | — | — | — | Entity sẵn; endpoint chưa | **CONFIRM** → O-10 |
| 26 | Không `FaultHistory` | Dylan (BE-18) | 05/09 | — | Có | **OBSOLETE** cho Contract (nội bộ) — nhưng CLAUDE.md mâu thuẫn (mục 6) |
| 27 | Định nghĩa fault MỞ | Dylan (BE-18, nội bộ) | 05/09 | — | Có (`FaultStatusSets`) | **CONFIRM** → O-7 |
| 28 | `mock-faults.json` lệch §2.4 | Dylan | 06/09 (`07ebe37`) | — | Mock khớp (kiểm khoá item 18/09) | **FOLD** |
| 29, 30 | xem B | | | | | **CONFIRM** → O-4 |
| 31, 31b | Vai trò ghi; EXACT-ROLE | — | — | — | Assets: admin-only, exact-role | **CONFIRM** → O-6 |
| 32 | `external_ref` 3 bảng | — | — | — | Có | **OBSOLETE** cho Contract (nội bộ, không emit) |
| 33 | `ASSET_NOT_FOUND`, `EXTERNAL_REF_TAKEN` | — | — | — | Có | **CONFIRM** → O-2 |
| 34 | xem B | | | | | **CONFIRM** (có hạn dùng BE-12b) → O-4 |
| 36 | `open_fault_count` mock | Dylan | — | — | Mock sửa + `OpenFaultCountTests` | **FOLD** |
| 38 | xem C | | | | | **CONFIRM** → O-3 |
| 39 | xem D | | | | | **FOLD** |
| 40 | xem E | | | | | **OBSOLETE** một phần |
| 41 | xem F | | | | | **FOLD** |
| 42 | Tên tuyến BE ≠ FE | — | — | — | Chưa quyết | **CONFLICT** → O-9 |
| 43 | `DELETE`, `PUT feeder`, `ASSET_IN_USE` | Dylan | 18/09/2026 | **Có** | Có (+ test) | **CONFIRM** → O-3 |

### 4.3 Undocumented drift (code lệch Contract, chưa có entry)

| U | Lệch | Ở đâu | Đề xuất |
|---|---|---|---|
| U-1 | 403 sai vai trò → `COMMUNE_FORBIDDEN` (F-04) | `ApiConventionsSetup.cs:128` | O-2 / D-4 |
| U-2 | Mã "trần" `NOT_FOUND`, `METHOD_NOT_ALLOWED`, `REQUEST_FAILED`, `UNSUPPORTED_MEDIA_TYPE` | `ApiConventionsSetup.cs:129-132` | O-2 |
| U-3 | `POST /assets/*` với `commune_id` **không tồn tại** → `400 VALIDATION_FAILED` (khác 403 khi ngoài phạm vi) | `AssetCrudService.cs:263-267` | O-4 ghi rõ |
| U-4 | Feeder khác xã (trong phạm vi) → `403 COMMUNE_FORBIDDEN` (F-09) | `AssetCrudService.cs:326` | D-5 |
| U-5 | `GET /lux-readings` `from`/`to`: biên đóng hai đầu; không `Z` → lệch múi giờ (F-02) | `LuxReadingService.cs:150-160` | D-12 |
| U-6 | `page`/`page_size` không phải số → im lặng dùng mặc định (mở rộng drift 7) | `PageQuery.cs:52-55` | O-2 |
| U-7 | `X-Correlation-Id` từ client được **chấp nhận** (≤128 ký tự, `[A-Za-z0-9-_.:]`), ngoài khuôn thì sinh mới (mở rộng drift 4) | `CorrelationIdMiddleware.cs:37-61` | O-2 |
| U-8 | `POST /lux-readings`: `lux_value > 200` chỉ log Warning, vẫn lưu | `LuxReadingService.cs:69-77` | Ghi vào §2.9 khi promote |
| U-9 | `POST /assets/import/{kind}` nhận cả `.json` như GeoJSON; kind sai → `400 VALIDATION_FAILED` kèm danh sách | `AssetImportController.cs:62-91` | O-4 ghi rõ |
| U-10 | `PUT /assets/fixtures/{id}/removal` không nằm tên trong drift 29/43 | `AssetsController.cs:156` | O-4 nêu tên |

### 4.4 Kiểm chứng draft

```
$ python3 gen_openapi_draft.py         (scratchpad; sinh draft từ luxmap-v1.json + Contract + mock)
wrote docs/openapi/luxmap-v1.2.draft.json: paths=30 implemented_ops=21 not_implemented_ops=15 schemas=85

$ npx @redocly/cli@latest lint docs/openapi/luxmap-v1.2.draft.json --format=stylish
  3:3      warning  info-license           Info object should contain `license` field.
  5308:14  warning  no-server-example.com  Server `url` should not point to example.com or localhost.
  5312:14  warning  no-server-example.com  Server `url` should not point to example.com or localhost.
Woohoo! Your API description is valid. 🎉
You have 3 warnings.

$ md <-> json 1-1 check (regex `METHOD /api/v1/...` trong md vs paths trong json)
json ops: 36  md ops: 36
in json, missing from md: none
in md, missing from json: none
implemented: 21  not_implemented: 15  total: 36

$ route attributes trong src/ (grep [HttpXxx]/[Route]) → 21 action, khớp 21 op `implemented`
```

Ba warning còn lại là cố ý: không bịa `license`; hai `servers` lấy từ `launchSettings.json` (Q-6 nếu
muốn thêm server staging). Script sinh draft nằm ở scratchpad phiên này — nếu Dylan muốn giữ để tái sinh
ở Phase 2, sẽ đưa vào `docs/openapi/tools/` (Q-7).

---

## 5. Danh sách cần Dylan quyết (mỗi mục trả lời bằng MỘT lựa chọn)

Gom toàn bộ D-item, Q-item và Open item `[PENDING DYLAN]` của draft. Số O-n trùng với draft mục 9.

| # | Câu hỏi | Lựa chọn | Đề xuất |
|---|---|---|---|
| **D-1 / O-1** | Số hiệu và tên file bản hợp nhất (file hiện hành đã là v1.3 bên trong) | (a) v1.4 giữ tên `api-contract-v1.1.md`; (b) v2.0; (c) v1.2 theo đề bài | **(a)** |
| **D-2 / O-14** | Số phận `contract-drift.md` | (a) archive `docs/archive/contract-drift-v1.md` + pointer sang changelog, mở log mới; (b) giữ, đánh dấu đóng từng mục | **(a)** |
| **D-3 / O-3** | Xác nhận 4 quyết định `SELF-SIGNED` (A, C, D, 43) | (a) cả bốn; (b) chỉ D + 43 | **(a)** |
| **D-4 / O-2** | 403 sai vai trò | (a) giữ `COMMUNE_FORBIDDEN`; (b) thêm `ROLE_FORBIDDEN`; (c) `FORBIDDEN` chung | **(b)** |
| **D-5** | Feeder khác xã với pole (cả hai trong phạm vi) | (a) giữ 403 `COMMUNE_FORBIDDEN`; (b) 400 `VALIDATION_FAILED` + details; (c) 409 mã mới `CROSS_COMMUNE_REFERENCE` | **(c)** — cùng họ với `ASSET_IN_USE`, và FK ghép (M-4) sẽ ném đúng nhánh này |
| **D-6** | Seed BE-39 khi sequence dev ở 396 625 | (a) seeder INSERT `pole_id` tường minh + `setval` (ngoại lệ hệ thống, ghi CLAUDE.md); (b) `docker compose down -v` trước demo + seed theo thứ tự mock; (c) FE bỏ hardcode ID | **(a)** — (b) chỉ đúng trên một máy, (c) đổi FE |
| **D-7 / O-9** | Drift 23 — `feeder_id` cho 103 cột mock | (a) thêm `feeder_id` vào mock (báo WP5/WP6); (b) file gán riêng `mocks/mock-pole-feeders.csv` nạp sau, ghi cách gán | **(b)** — không đổi hình dạng mock FE đang dùng |
| **D-8 / O-9** | Drift 42 — tên tuyến | (a) BE theo FE (tên đường thật); (b) FE theo BE | cần người biết địa bàn — **không đề xuất** |
| **D-9 / O-9** | Drift 6 — mock prefix (`USR-khang`, `SWEEP-…`, `FRM-88213`, `SUP-004`, NODE 4 chữ số) | (a) sửa mock theo §0.2; (b) đổi §0.2 NODE thành 4 chữ số, còn lại sửa mock | **(a)** |
| **D-10** | FK ghép `(feeder_id, commune_id)` / `(segment_id, commune_id)` | (a) ticket riêng trước BE-13; (b) gộp vào BE-13 | **(a)** |
| **D-11** | Cardinality bóng/cột (F-10) | (a) tối đa 1 bóng active/cột — unique partial index, import từ chối khi **đang có** bóng active; (b) nhiều bóng active, BE-14 flatten theo quy tắc nêu rõ | **(a)** — CV không tách được bóng, và mock có đúng 1 bóng/cột |
| **D-12** | `from`/`to` không `Z` (F-02) | (a) coi là UTC (`UtcNormalization.ToUtc`); (b) 400 `VALIDATION_FAILED` | **(a)** — nhất quán với body |
| **D-13 / O-5** | `data_source` 8 entity + mặc định loại `calibration_rig` ở `GET /poles` | (a) cả hai thành luật Contract; (b) chỉ sửa §1 | **(a)** |
| **D-14 / O-6** | Enum `user_role`, ma trận ghi, EXACT-ROLE vs HIERARCHY | (a) FOLD 4 giá trị + EXACT-ROLE + ghi=Quản trị cho assets; (b) FOLD giá trị, hoãn ma trận | **(a)** |
| **O-4** | Nhóm `/assets/…` thành Contract | (a) FOLD 5.3 (GET tạm); (b) chờ BE-12b | **(a)** |
| **O-7** | Fault MỞ = `detected \| confirmed \| in_progress` | (a) FOLD; (b) khác | **(a)** |
| **O-8** | Máy trạng thái `wo_status` | (a) đặc tả ở FW kế tiếp; (b) BE-22 đề xuất | **(a)** |
| **O-10** | `commune_id` trong `POST /faults` khi `pole_id` null | (a) thêm trường tuỳ chọn; (b) từ chối user nhiều xã | **(a)** |
| **O-11** | `processing_status` enum; thumbnail 320/q80 | (a) chốt ở FW kế tiếp; (b) BE-15/17 đề xuất | **(a)** |
| **O-12** | Năm drift BE-42 (17–21) | (a) FOLD cả năm; (b) khác | **(a)** |
| **O-13** | Hình dạng `sync/bundle`, `sync/push` | (a) chốt draft 5.8 ở FW kế tiếp; (b) BE-43 đề xuất W14 | **(a)** |
| **Q-1** | Vị trí draft OpenAPI: `docs/openapi/luxmap-v1.2.draft.json` (A-2) | (a) đúng; (b) chuyển | **(a)** |
| **Q-2** | Local `dev` đứng sau `origin/dev` 1 merge — cố ý? | (a) chỉ là chưa pull; (b) khác | (a) |
| **Q-3** | Import bóng từ chối cột **đã từng** có bóng (kể cả đã ngừng dùng) — cố ý hay hệ quả của D-11? | (a) hệ quả, sửa theo D-11; (b) cố ý | (a) |
| **Q-4** | `PUT …/removal` có cần kiểm `removed_date >= install_date` và chặn ngừng-dùng-lại? (F-11) | (a) có, CHECK ở DB; (b) không | (a) |
| **Q-5** | `Location` 201 trỏ route chưa có (F-07) — chấp nhận tới BE-12b? | (a) chấp nhận; (b) bỏ tạm | (a) |
| **Q-6** | Thêm `servers` staging/prod vào spec? | (a) chưa có → giữ localhost; (b) có URL | (a) |
| **Q-7** | Giữ script sinh draft trong repo (`docs/openapi/tools/`) cho Phase 2? | (a) có; (b) không | (a) |
| **Q-8** | M-6 (CHECK `status_confidence`) làm ngay hay chờ BE-15 (chủ quyền ghi)? | (a) ngay; (b) chờ | (a) |
| **Q-9** | Test residue trên DB dev (F-06): chấp nhận tới BE-36 hay vá tạm N-5? | (a) N-5 ngay; (b) chờ M-1 | (a) |

---

## 6. CLAUDE.md drift (đối chiếu với code ngày 18/09/2026)

| Dòng | CLAUDE.md nói | Thực tế | Sửa |
|---|---|---|---|
| 13, 34 | Contract là `api-contract-v1.1.md` "bản hợp nhất, chốt 24/08" | File tự ghi **v1.3** (v1.2 11/09, v1.3 15/09) | Cập nhật số hiệu (sau O-1) |
| 92 | Regex FE `^POLE-\d{4,}$` | Contract §0.2 chốt **`[0-9]`**, không `\d` (Unicode digits) | Đổi thành `[0-9]{4,}` |
| 189 | BE-07 "Endpoint đăng ký / đăng nhập / refresh" trong bảng **chưa có đặc tả** | Đã đặc tả §2.10 (v1.2) | Gạch dòng |
| 211 | Domain model liệt kê `FaultHistory` | BE-18 quyết **không có** `FaultHistory` (dòng 835 chính CLAUDE.md) | Bỏ / ghi "chỉ khi BE-19 cần" |
| 945 | "Mọi quyết định của kỹ sư vào `FaultHistory` (BE-18)" | Mâu thuẫn với mục BE-18 cùng file | Sửa thành "ghi vết trên bảng `fault`; chuỗi đầy đủ là việc BE-19" |
| 547 | BE-12a: "**KHÔNG có DELETE.**" | `DELETE /assets/poles/{id}` đã có (BE-12, drift 43) | Sửa thành "chỉ pole có DELETE; fixture không" |
| 528 | BE-42 "Không gắn policy vai trò" (đã có chú thích BE-12a) | Đúng — `LuxReadingsController` vẫn không policy | Giữ, nhưng khi O-6 chốt thì ghi rõ lux là ai ghi |
| 1044 | "Thứ tự hiện tại: W1 nền tảng…" | Đang W2–W3, BE-12 xong | Cập nhật |
| mục 1c | "Cửa sau dùng ở `IdentitySeeder`" | `IdentitySeeder` **không** gọi backdoor (chỉ ghi entity không `ICommuneScoped`); backdoor chỉ dùng trong test | Sửa câu cho đúng (hoặc giữ như dự kiến BE-39) |
| mục BE-11 | "Hai bucket do sidecar tạo… `S3ObjectStore` chưa có test" | Đúng | Giữ |
| mục "Chi phí guard" | Không có gì mới | — | — |
| (thiếu) | Không ghi F-01/F-02/F-06 | Ràng buộc kỹ thuật mới: (1) mọi đường ghi `pole.feeder_id`/`segment_id` phải kiểm cùng xã tới khi có FK ghép; (2) query-string `DateTime` không đi qua `UtcDateTimeConverter`; (3) DB dev bị test đẩy sequence | Thêm sau Phase 2 |

---

## 7. Đã check và ổn (ngắn)

- **Build/test/package:** 0 warning, 459 test xanh, không package có lỗ hổng; ImageSharp ghim 3.1.12.
- **Migration:** 11 file `Up()/Down()` đối xứng (đọc từng file): `FixPrefixedIdOverflow` tạo hàm **trước**
  khi 6 `AlterColumn` dùng nó, `Down()` `DROP FUNCTION IF EXISTS luxmap_format_id(text, bigint, int)`;
  `AddRefreshTokenChainTracking`/`AddRefreshTokenSessionKind` gỡ `DEFAULT` backfill; `AddAssetExternalRef`
  chỉ `AddColumn` + 3 index partial, **không** `DropIndex` (bẫy BE-12a đã tránh — `ix_*_commune_id` vẫn
  còn trên schema live).
- **CHECK live:** `status_confidence IS NULL ⇔ unknown` ✓; NaN/Infinity chặn trên `lux_value`,
  `fault.priority_score`, `fault.status_confidence`, `fault.lat/lng` bằng `<> 'NaN'::float8` — **đúng**;
  idiom `x = x` mà đề bài nêu **không** dùng được trên PostgreSQL (NaN = NaN là true) và repo đã ghi rõ.
  JSON cũng chặn bằng `FiniteDoubleConverter`.
- **`luxmap_format_id`:** không chỗ nào giả định độ dài cố định; `PrefixedIdSpec.Format` dùng `PadLeft`;
  danh sách sắp theo `created_at` rồi id (`AssetCrudService.ListAsync`), không `ORDER BY pole_id`;
  `PrefixedIdOverflowTests` phủ ngưỡng 10000.
- **Spatial:** chỉ `SpatialFunctions.DistanceMeters` (dịch sang `ST_Transform(…,3405)` trong cây SQL);
  4 API trả độ bị cấm RS0030 + `BannedDistanceApiTests` quét văn bản; **không** raw SQL nào trong `src/`
  (`FromSql|ExecuteSql|SqlQuery` = 0), không `IgnoreQueryFilters` trong `src/`; không API nào trả
  geometry 3405.
- **Multi-tenancy:** `HasQueryFilter` tham chiếu `DbContext` (không bắt singleton); `CommuneScopeAccessor`
  singleton fail-closed; `CommuneWriteGuard` trong `SaveChanges` ném **trước** base (403, không 500),
  kiểm Added/Modified (cả hai chiều)/Deleted; `ExecuteUpdate/Delete` bị cấm — 4 ngoại lệ ở `AuthService`
  đều trên `RefreshToken` (không `ICommuneScoped`) trong `#pragma`, 13 ngoại lệ teardown test; ba test
  `BannedBulkWriteApiTests` canh; `CommuneScopeConsistencyHandler` bắt `*` lệch vai trò; policy là
  exact-role, GET không gắn policy, `SetFallbackPolicy` đóng mặc định; `LuxReading.commune_id` và
  `Fixture.commune_id` lấy từ pole, client gửi → 400.
- **Storage/deps:** bucket `luxmap-survey` / `luxmap-evidence` do sidecar `minio-mc` tạo (`restart: "no"`);
  **không** presigned URL (grep 0); JPEG quyết bằng magic bytes + `Configuration` chỉ JPEG; thumbnail bỏ
  EXIF/XMP/IPTC tường minh; MinIO/Postgres/Redis bind `127.0.0.1`; image MinIO từ quay.io pin index digest.
- **Secrets/logging:** `.env` bị ignore, không secret trong `appsettings*`; JWT key ≥ 32 byte, HS256 cố
  định, `ClockSkew` 30 s, `MapInboundClaims=false`; mật khẩu PBKDF2 (`PasswordHasher`), refresh token chỉ
  lưu SHA-256; `SensitivePropertyScrubber`; `LuxMapException` không log stack; 500 không lộ chi tiết ngoài
  Development.
- **Auth:** xoay vòng token bằng UPDATE có điều kiện + row count (đúng cho race), cửa sổ ân hạn 30 s, thu
  hồi theo chuỗi, nhóm mobile/web tách bằng `session_kind` trong predicate SQL, cookie `__Secure-` với
  `Path` hẹp, `Origin` guard tách khỏi CORS, CORS validate origin lúc khởi động.
- **Chất lượng chung:** mọi service/controller async đều nhận `CancellationToken` (grep heuristic chỉ
  còn middleware/binder); không sync-over-async trong `src/`; không `TODO/FIXME`; không `Console.Write`;
  không `.Find()`; JSON pipeline cấu hình cả MVC lẫn minimal API; `PageQuery` binder tránh bẫy prefix.
- **API versioning:** đã có (`Asp.Versioning`, `/api/v{version}` → `/api/v1` trong spec, test canh).

---

## 8. PHASE 2 — đã áp dụng (18/09/2026, Dylan duyệt toàn bộ đề xuất mục 5)

Nhánh `docs/BE-REVIEW-02`, mỗi concern một commit, chưa push.

| Commit | Concern | Test canh (sabotage cả hai chiều) |
|---|---|---|
| `c1d73cc` | **D-5 / F-09** — mạch khác xã → 409 `CROSS_COMMUNE_REFERENCE` | `PoleWriteTests` (2 test đổi kỳ vọng) |
| `8edccbc` | **F-01** — import kiểm mạch cùng xã (tuyến KHÔNG kiểm: `inter_commune` hợp lệ theo CLAUDE.md) | `A_pole_whose_feeder_sits_in_another_commune…` — vô hiệu kiểm → **đỏ**; khôi phục → xanh; cùng test chứng minh mạch cùng xã đi qua |
| `3886112` | **F-03** — bỏ N+1 ở nạp bóng, index tham chiếu chỉ project id + commune | `AssetImportTests` (15) |
| `c5c2267` | **F-02 / D-12** — `from`/`to` không `Z` = UTC | `A_from_or_to_bound_without_a_Z_suffix…` — code cũ trên máy `+07` → **đỏ** (`Expected 1, Actual 0`); code mới → xanh |
| `d169853` | **D-4 / F-04** — `ROLE_FORBIDDEN` qua `ForbiddenCodeResultHandler` | `A_maintenance_engineer_may_NOT_create_an_asset` — bỏ nhánh đọc lý do → **đỏ**; `Wildcard_claim…` vẫn `COMMUNE_FORBIDDEN` |
| `de3665c` | **D-11 / F-10** — `ux_fixture_pole_id_active` UNIQUE; 409 `POLE_HAS_ACTIVE_FIXTURE`; import chỉ chặn bóng đang dùng | `FixtureCardinalityTests` (5) — hạ index xuống không-unique trên DB → **đỏ**; khôi phục → xanh; bóng đã ngừng dùng vẫn ghi được |
| `1a77e99` | **Q-4 / F-11** — `ck_fixture_removed_after_install`; PUT removal 400 khi trước install hoặc lặp | `FixtureRetirementTests` (5) — drop CHECK → **đỏ**; khôi phục → xanh; ngừng dùng đúng ngày lắp vẫn hợp lệ |
| `a22fa44` | **M-6 / F-15** — `ck_pole_current_status_confidence_range` (đóng drift 24) | `AssetSchemaTests` +7 (42.5, −0.1, NaN, +∞ đỏ; 0 / 0.81 / 1 xanh) — drop CHECK → **4 đỏ**; khôi phục → xanh |
| `01ed1cf` | **N-5 / F-06** — fixture dọn token của tài khoản seed nó đăng nhập | Đếm `refresh_token` trước/sau một lượt: 6216 → 6216 (trước đó mỗi lượt +7) |
| `f353911` | **F-08** — 4 chú thích sai | — |
| `026110b` | **D-9** — mock đổi ID theo §0.2 (`NODE-0nn`, `SWP-001..030`, `FRM-088213`, `USR-004`, bỏ `supplier`) | `OpenFaultCountTests`, `AssetImportMockSetTests` xanh |
| `2fab05e` | Spec xuất lại từ code + khai 409/400 mới trên controller | — |
| `5903252` | **D-1, D-2, D-3, D-13, D-14, O-4..O-13** — Contract v1.4 (`api-contract-v1.1.md`), `luxmap-v1.4.json`, `docs/openapi/tools/gen_consolidated_spec.py`, drift archive + log mới, CLAUDE.md (8 ràng buộc mới, 8 chỗ stale sửa), README | md ↔ json 36/36 |
| `98b8654` | Probe BE-08 `Wrong_role_returns_403…` đổi kỳ vọng sang `ROLE_FORBIDDEN` (suite đầy đủ bắt được — lượt chạy theo class ở commit D-4 không gồm nó) | — |

**Ba migration, đọc trước khi apply, mỗi cái đúng một thao tác + `Down()` đối xứng:**
`OneActiveFixturePerPole` (DropIndex `ix_fixture_pole_id_active` → CreateIndex UNIQUE
`ux_fixture_pole_id_active`; `ix_fixture_commune_id` còn nguyên), `FixtureRemovedAfterInstall`
(AddCheckConstraint), `PoleCurrentStatusConfidenceRange` (AddCheckConstraint). Đã apply lên DB dev.

**Không làm, và vì sao:** D-6 (seeder BE-39) và D-7 (`mocks/mock-pole-feeders.csv`) là quyết định về
việc chưa tới lượt / cần dữ liệu từ người biết địa bàn — đã ghi vào Contract (1.2, O-6) và drift log;
D-8 không có đề xuất; D-10 là ticket riêng (O-7); N-5 chỉ phủ `AssetImportFixture` — các class dùng
`AuthTestFactory` vẫn để lại token cho tới BE-36.

### Output cuối (thật)

```
$ dotnet build -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet test -c Release --no-build
Passed!  - Failed: 0, Passed: 147, Skipped: 0, Total: 147 - LuxMap.Shared.Tests.dll
Passed!  - Failed: 0, Passed:  20, Skipped: 0, Total:  20 - LuxMap.Persistence.Tests.dll
Passed!  - Failed: 0, Passed:  18, Skipped: 0, Total:  18 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed: 0, Passed: 293, Skipped: 0, Total: 293 - LuxMap.Api.Tests.dll
→ 478 test xanh (Phase 1: 459)

$ npx @redocly/cli@latest lint docs/openapi/luxmap-v1.4.json
  3:3      warning  info-license           Info object should contain `license` field.
  5412:14  warning  no-server-example.com  Server `url` should not point to example.com or localhost.
  5416:14  warning  no-server-example.com  Server `url` should not point to example.com or localhost.
Woohoo! Your API description is valid. 🎉  You have 3 warnings.

$ md <-> json 1-1: json ops 36, md ops 36, missing none/none; implemented 21, not_implemented 15
```

**Còn nợ sau Phase 2 (đã ghi ở Contract mục 9 / drift log):** O-1 tên tuyến; O-2 ma trận ghi cho
sweep/fault/work order; O-3 `wo_status`; O-4 `processing_status` + thumbnail; O-5 hình dạng sync;
O-6 `feeder_id` mock; O-7 FK ghép; M-1 BE-36; M-5 rate limit; M-7 CI lint spec; N-6 `SwaggerSetup`
(spec sinh từ code vẫn 23 lỗi lint — bản hợp nhất thì sạch).
