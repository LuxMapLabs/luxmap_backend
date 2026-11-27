# COMMUNE-BOUNDARY-GUARD — Phase 2: hiện thực (26/11/2026, codex)

Nhánh `feat/COMMUNE-BOUNDARY-guard`, dựng từ `50be400`. Không migration, không index, không mã lỗi mới, không sửa
`tracking.html` / `docs/openapi/*.json` / `docs/api-contract-v1.1.md` (theo "Phase 2 KHÔNG ĐƯỢC"). Đọc trước khi sửa:
`.ai/tasks/COMMUNE-BOUNDARY-GUARD.md`, `.ai/results/COMMUNE-BOUNDARY-GUARD-p1.md`, `docs/contract-drift.md` (CBD-1…CBD-6),
`docs/agents/traps/{assets,commune-scope,spatial-map,testing}.md`.

## D-item → chỗ hiện thực

| D-item | Hiện thực |
|---|---|
| D-1 (a), D-2 (a) | Luật chỉ chạy khi lần ghi **mang toạ độ**; `geom = null` không kiểm (kể cả xã chưa có ranh giới). Xã thiếu ranh giới → 400 `VALIDATION_FAILED` + `reason = COMMUNE_BOUNDARY_MISSING`; ngoài biên → `OUTSIDE_COMMUNE_BOUNDARY`. `details {field, reason, commune_id}`; thông điệp nêu tên + id xã, không nêu toạ độ polygon |
| D-3 (a) | `PoleRelocation.RequireInsideBoundaryAsync` gọi **sau** `IsOffSegment` ở cả `PUT /assets/poles/{id}/location` và GPS verify (`WorkOrderService.MovePolesAsync`); 409 `POLE_OFF_SEGMENT` giữ nguyên |
| D-4 | Mọi đường dùng một khái niệm "đổi toạ độ" (`CommuneBoundary.Moved`); sync push đi qua `WorkOrderService.Act` nên lỗi 400 rơi vào `rejected[]` sẵn có, không đụng `SyncPushService` |
| D-5 (a) | Import: `FOR SHARE` trên `administrative_unit` (một lần cho mỗi loại tài sản trong file; workbook dùng lại planner nên mỗi sheet một lần), trong **cùng transaction** với `pg_advisory_xact_lock`. Dời cột: `FOR SHARE` sau khoá `work_order`/`pole`. `UpdateBoundaryAsync`: `FOR UPDATE` trước khi đọc/gán |
| D-6 (b) | Một hàm thuần `CommuneBoundary.Verdict(MultiPolygon?, Point)` ở `LuxMap.Persistence`; CRUD đọc theo PK rồi gọi; import đọc nhiều xã bằng một truy vấn rồi gọi. Không `HasDbFunction` |
| D-7 | `AssetImportFixture.TestArea` + `AssetImportFixture.Rectangles`; cả hai xã fixture và hai xã `AssetWorkbookTestContext` mang `TestArea`; `SyncTests` / `WorkOrderTests` đặt ranh giới riêng; nhánh 180/90 của `AssetCoordinateRangeTests` viết lại thành "toạ độ thế giới nhưng ngoài xã → 400"; test mới có xã riêng |
| D-8 (a) | `scripts/data/commune-boundaries.geojson` thêm `COM-070 "Commune 01"`, chữ nhật `106.488–106.506 / 10.964–10.978`, `properties.mock = true`, ghi rõ không phải ranh giới thật; docstring `scripts/load_commune_boundaries.py` giải thích |
| D-9 | Không index, không migration. Comment `AdministrativeUnit.cs` (cả lớp và `Boundary`) + comment cấu hình EF nói rõ "đọc theo PK, so trong bộ nhớ" |
| D-10 | `CommuneBoundary.Moved` so `X`/`Y`; không `Point.Equals` (vì `Equals` tính cả SRID) |
| D-11 | Không API nào sửa `commune_id` của pole/cabinet; mọi kiểm dùng `commune_id` **hiện tại của bản ghi**. Drift CBG-11 ghi rõ |
| D-12 | Ranh giới đặt sau mọi 409 hiện có: feeder khác xã ở CRUD/import, hành lang tuyến ở relocation |

## File đã sửa / thêm

**Thêm**

- `src/LuxMap.Persistence/CommuneBoundary.cs` — `BoundaryVerdict {Inside, Outside, Missing}`, `Verdict`, `Moved`.
- `src/LuxMap.Modules.Assets/CommuneBoundaryGuard.cs` — đọc ranh giới theo PK, `LockAndReadAsync` (`FOR SHARE`, id sắp xếp,
  một truy vấn), `Refused` (400), `Explain` (một câu dùng chung CRUD + import), hằng `OUTSIDE_COMMUNE_BOUNDARY` /
  `COMMUNE_BOUNDARY_MISSING`.
- `tests/LuxMap.Api.Tests/CommuneBoundaryGuardTests.cs` — 10 test (danh sách dưới).

**Sửa (code)**

- `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs` — tủ điện tạo (sau trùng `external_ref`) / sửa (chỉ khi `Moved`);
  cột tạo (chỉ khi có `geom`) / sửa (chỉ khi `Moved`) / `SetPoleLocationAsync` (sau 409 hành lang); helper
  `RequireInsideBoundaryAsync`. `UpdatePoleAsync` được xếp lại: **mọi kiểm, kể cả ranh giới, chạy trước khi sửa** `ExternalRef` /
  `SegmentId` / feeder / `Geom`, để một thao tác bị từ chối (đặc biệt trong `sync push` dùng lại DbContext) không để lại thay đổi
  đang track.
- `src/LuxMap.Modules.Assets/Crud/PoleRelocation.cs` — `RequireInsideBoundaryAsync(IReadOnlyCollection<Measured>, field, ct)`;
  bỏ qua `Unchanged`; caller giữ transaction.
- `src/LuxMap.Modules.Assets/Import/AssetImportService.cs` — `LockAndReadAsync` một lần cho mỗi planner; kiểm sau lỗi dòng
  hiện có; lỗi dòng `geom_wkt` (ngoài) / `commune_id` (thiếu ranh giới).
- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs` — gọi kiểm ranh giới trong `MovePolesAsync`, sau hành lang, trước `Stage`.
- `src/LuxMap.Modules.Admin/Communes/CommuneAdminService.cs` — transaction tường minh + `SELECT 1 … FOR UPDATE` trước `FindAsync`;
  hình dạng request/response và quyền không đổi.
- XML doc: `src/LuxMap.Persistence/AdministrativeUnit.cs`, `AdminCommunesController.cs`, `CommuneAdminService.cs`.

**Sửa (test / fixture)**

- `AssetImportFixture.cs` — `TestArea` (8 chữ nhật) + `Rectangles(...)`, gán cho cả hai xã; `ManagerClientFor(params string[])`;
  `SetBoundaryAsync(communeId, MultiPolygon?)`.
- `AssetWorkbookTestContext.cs` — hai xã workbook mang `TestArea`.
- `SyncTests.cs` / `WorkOrderTests.cs` — xã tự tạo mang chữ nhật quanh cụm toạ độ của chúng.
- `AssetCoordinateRangeTests.cs` — 180/90 và −180/−90 rời khỏi lý thuyết "chấp nhận"; thêm lý thuyết "toạ độ hợp lệ thế giới nhưng
  ngoài xã → 400 `OUTSIDE_COMMUNE_BOUNDARY`".

**Sửa (docs / dữ liệu)**

- `docs/contract-drift.md` — entry mới **COMMUNE-BOUNDARY-GUARD** (CBG-1…CBG-11, map D-item, consumer WP5/WP6, SELF-SIGNED Mỹ
  26/11, ghi chú `COM-070` mock, "Phải báo" chưa báo); CBD-1 trỏ sang entry mới, giữ lịch sử; câu "Lược đồ" sửa lại.
- `docs/agents/traps/assets.md` (mục COMMUNE-BOUNDARY viết lại: luật ghi, một định nghĩa, chỉ kiểm khi đổi, thứ tự, khoá),
  `commune-scope.md` (§1c: đây là kiểm dữ liệu trong service, không phải filter/guard), `testing.md` (bẫy fixture + `FOR NO KEY UPDATE`).
- `docs/database/erd.md` — Q17: ranh giới là luật ghi cho `pole`/`electrical_cabinet`, vẫn **không** suy `commune_id`.
- `docs/openapi/tools/gen_consolidated_spec.py` — mô tả info + SUMMARY + response 409 cho verify. **Không** sinh lại JSON.
- `scripts/data/commune-boundaries.geojson`, `scripts/load_commune_boundaries.py` — COM-070 mock (D-8).

## Quyết định nhỏ đã tự lấy (ghi để review, không mở lại D-item)

1. **`Moved` nằm cạnh `Verdict` trong `LuxMap.Persistence`.** D-6 chỉ yêu cầu một hàm verdict; "đổi toạ độ" cũng là luật dùng
   chung của CRUD + import nên để cùng chỗ thay vì chép `X != X || Y != Y` ba lần.
2. **Import khoá theo loại tài sản, workbook theo sheet** — không phải "một lần cho cả file" theo nghĩa đen: mỗi loại có
   `pg_advisory_xact_lock` riêng và planner riêng, workbook xử lý từng sheet. Tất cả nằm trong **một** transaction, nên khoá giữ tới
   commit; số truy vấn là O(số loại), không O(số dòng). Đã ghi vào CBG-8 và trap `assets.md`.
3. **`UpdateBoundaryAsync` mở transaction tường minh.** `FOR UPDATE` ngoài transaction không có nghĩa (nhả ngay sau câu lệnh).
   Trả 200 kể cả khi gửi lại đúng polygon (như trước), chỉ khác là có thêm một transaction rỗng.
4. **`PoleRelocation` ném `InvalidOperationException` nếu cột trỏ tới xã không tồn tại** (FK + query filter làm trường hợp này không
   tới được qua API). Chọn vậy thay vì bịa 400/404 mới; không thêm mã lỗi.
5. **`RequireInsideBoundary` của import im lặng khi `commune_id` không có trong tập đã đọc** — dòng đó đã có lỗi `commune_id` từ
   `CommuneAsync` (không tồn tại / ngoài phạm vi), báo thêm lần nữa chỉ làm nhiễu. Điều kiện lọc của `LockAndReadAsync` dùng đúng
   `scopeAccessor.Scope.Allows` như `CommuneAsync`, nên không có khe hở.
6. **Kiểm ranh giới đặt sau `!reader.IsValid`**: giữ nguyên lỗi dòng cụ thể hơn (feeder, WKT, tham chiếu) thay vì thay bằng lỗi ranh giới.
7. **Fixture `TestArea`** chọn 8 chữ nhật theo cụm toạ độ đang có trong suite (đã quét mọi literal `POINT(...)` và cặp `lng, lat`
   của test). Các literal còn ngoài `TestArea` là ca **cố ý sai** (toạ độ trao đổi, ngoài ±180/±90, geometry sai kiểu) hoặc thuộc
   xã riêng của test mới. Không đụng toạ độ test nào để tránh sửa kỳ vọng không liên quan.
8. **Test khoá dùng cửa sổ 500 ms** (`Task.WhenAny` + `Task.Delay`): đủ để một request đã tới `FOR SHARE`/`FOR UPDATE` bị chặn; nếu
   máy CI chậm bất thường, đây là chỗ dễ báo đỏ giả nhất.

## Test mới

`tests/LuxMap.Api.Tests/CommuneBoundaryGuardTests.cs` (collection `AssetDatabaseCollection`, tự tạo xã riêng, teardown
audit → pole → `administrative_unit`):

1. `A_point_outside_the_outline_is_400_with_the_outside_reason` — `field`, `reason`, `commune_id`.
2. `A_commune_with_no_outline_refuses_a_position_with_the_missing_reason`.
3. `A_pole_without_a_position_is_created_even_where_no_outline_exists` (D-1).
4. `A_point_exactly_on_the_outline_counts_as_inside` (`Covers`, không `Contains`).
5. `Editing_an_old_pole_outside_the_outline_without_moving_it_is_accepted_but_moving_it_is_refused` (D-4/D-10): PUT gửi lại đúng
   toạ độ → 204; đổi X → 400.
6. `Importing_a_row_outside_the_outline_fails_that_row_and_keeps_the_inside_rows` (lỗi dòng `geom_wkt`, dòng trong vẫn vào).
7. `Importing_into_a_commune_with_no_outline_is_a_commune_id_row_error`.
8. `A_wrong_feeder_and_a_point_outside_the_outline_still_answers_409` (D-12, `CROSS_COMMUNE_REFERENCE`).
9. `An_import_waits_for_the_commune_row_lock_before_it_writes` (D-5: giữ `FOR NO KEY UPDATE`, import phải chờ rồi vẫn ghi được).
10. `Setting_the_boundary_waits_for_a_share_lock_on_the_commune` (D-5: `FOR UPDATE` phải chờ `FOR SHARE`).

Test cũ được chỉnh: `AssetCoordinateRangeTests` (2 lý thuyết), fixture/workbook/work-order/sync như trên. Không ghim ID literal —
mọi id lấy từ response hoặc DB.

## Kiểm chứng

✅ **Build:** `0 Warning(s) / 0 Error(s)`, exit 0, 30,39 s — cả 18 project + 5 test project.

```
DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1 \
dotnet build LuxMap.slnx --no-restore --no-incremental --disable-build-servers \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false -tl:off -v minimal
```

⚠️ **Hai giới hạn sandbox, không phải lỗi code:**

- `dotnet restore` bị chặn mạng (exit 1 sau ~5 phút, log rỗng). Đã dùng `obj/project.assets.json` + `project.nuget.cache` +
  `*.nuget.g.props/targets` của worktree anh em `wt-pg-accuracy` (cùng `50be400`, `PackageReference` không lệch) cho 23 project.
- Lệnh build **không** có `-p:UseSharedCompilation=false` treo đúng 5:00 rồi "Build FAILED. 0 Warning(s) 0 Error(s)" — Roslyn
  compiler server không chạy được dưới sandbox. Vì vậy lệnh trên là lệnh tái lập được, không phải thay đổi cấu hình repo.

✅ `python3` biên dịch `docs/openapi/tools/gen_consolidated_spec.py` (ra `/tmp`, không tạo `__pycache__` trong repo); `git diff --check`
sạch. `.ai/` không chứa build artifact.

⛔ **Chưa chạy `dotnet test` — sandbox không có Docker**, nên chưa dựng được PostGIS/Testcontainers. Vì vậy toàn bộ test mới và
fixture ở trên là **đọc mã + build**, chưa có kết quả xanh/đỏ thực tế. Cần chạy trên máy có Docker trước khi merge; chú ý
`CommuneBoundaryGuardTests` (khoá hàng) và các class dùng `AssetImportFixture` (ranh giới fixture).

📌 **Chưa commit** (theo yêu cầu). `tracking.html` không sửa (ticket cấm; tiến độ để Claude cập nhật).

## Còn lại

- Chạy `dotnet test` (Docker/PostGIS) và phá thử tối thiểu: bỏ `Verdict` ở CRUD cột, ở import, ở relocation → mỗi chỗ phải có test đỏ;
  bỏ `FOR SHARE` → 2 test khoá phải đỏ.
- Sinh lại `docs/openapi/luxmap-v1.json` → `gen_consolidated_spec.py` → `luxmap-v1.5.json`, rồi lint bằng Redocly (Claude làm theo
  ticket; **chưa làm ở đây**).
- Báo WP5 (form tài sản + màn import: 400 mới, `reason`, phải có ranh giới trước khi đặt điểm) và WP6 (GPS verify / sync push) —
  drift đang ghi "Chưa báo".
- Nạp ranh giới `COM-070` mock trên môi trường dev bằng `python3 scripts/load_commune_boundaries.py --apply` nếu muốn dữ liệu mock
  sửa được vị trí; seeder ghi thẳng SQL nên không tự đi qua luật.

## Review và kiểm chứng của Claude (26/11/2026)

- **Bỏ khoá `FOR UPDATE` tường minh ở `UpdateBoundaryAsync`** (cùng transaction tường minh). Phá thử: xoá `FOR UPDATE` thì
  `Setting_the_boundary_waits_for_a_share_lock_on_the_commune` **vẫn xanh** — câu `UPDATE administrative_unit SET boundary …`
  tự giữ `FOR NO KEY UPDATE` tới commit, đã xung đột với `FOR SHARE` của import / dời cột. Chốt không phá thử được là chốt thừa;
  doc comment, CBG-8, CBG-10, `traps/assets.md`, `traps/testing.md`, mô tả spec sửa theo. Test giữ nguyên (nó chứng minh PUT chờ).
- `PoleRelocation.RequireInsideBoundaryAsync`: `outlines[id] ?? throw` → `TryGetValue` (indexer ném `KeyNotFoundException`, nhánh `??` chết).
- **Test** (Docker + PostGIS, `.env` chép từ cây chính, không `ConnectionStrings__LuxMap`): Api 2095 pass / 7 skip có sẵn, Shared 454,
  Storage 90, Persistence 47, IotBridge 21 — **mã thoát 0**. Sau khi bỏ khoá: 67 test `CommuneBoundary*` / `CommuneAdmin*` /
  `AssetCoordinateRange*` xanh.
- **Phá thử:** `Verdict` luôn `Inside` → 5 đỏ (`CommuneBoundaryGuardTests` ×3, `AssetCoordinateRangeTests` ×2); bỏ `FOR SHARE` ở
  `LockAndReadAsync` → `An_import_waits_for_the_commune_row_lock_before_it_writes` đỏ. Khôi phục bằng `cp` + `touch`, chạy lại xanh.
- **OpenAPI:** xuất `luxmap-v1.json` (không đổi), sinh `luxmap-v1.5.json`, redocly lint hợp lệ, 10 cảnh báo như HEAD.
