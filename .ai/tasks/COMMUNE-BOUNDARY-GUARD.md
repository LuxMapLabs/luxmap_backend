---
ticket: COMMUNE-BOUNDARY-GUARD
title: Từ chối cột / tủ điện có toạ độ nằm ngoài ranh giới xã của nó
status: ready
phase: 2
owner: codex
branch: feat/COMMUNE-BOUNDARY-guard
contract_refs:
  - "contract-drift.md — COMMUNE-BOUNDARY CBD-1 (đang ghi: tài sản ngoài ranh giới không bị từ chối)"
  - "contract-drift.md — COMMUNE-BBOX CB-1 (bbox là khung nhìn — KHÔNG dùng ở ticket này)"
  - "docs/database/erd.md — Q17"
depends_on: []
---

## Bối cảnh

Mỹ thấy (26/11/2026): import vào xã `COM-070` "Commune 01" (vùng mock ở Củ Chi) nhận được cột có toạ độ ở Phường Long Bình.
Đúng như code hiện tại: import / CRUD chỉ kiểm `commune_id` trong phạm vi JWT và tồn tại (`AssetImportService.CommuneAsync`), toạ độ
không so với xã nào. Ranh giới polygon đã có từ COMMUNE-BOUNDARY (11b3a60: cột `administrative_unit.boundary
geometry(MultiPolygon,4326) NULL`, `PUT /admin/communes/{id}/boundary`, `scripts/load_commune_boundaries.py`) nhưng CBD-1 chốt "chỉ để vẽ".
Ticket này đổi CBD-1: ranh giới thành chốt chặn khi ghi tài sản dạng điểm. Consumer bị ảnh hưởng: WP5 (form tài sản, màn import),
WP6 nếu có đường ghi vị trí cột.

## Quyết định ĐÃ CHỐT (Mỹ, 26/11/2026 — không mở lại)

- **D1 — Dung sai 0 m.** Điểm phải nằm TRONG ranh giới xã của chính tài sản. Trên đường biên tính là trong (`ST_Covers`, không `ST_Contains`).
- **D2 — Xã chưa có ranh giới → TỪ CHỐI** ghi cột / tủ điện ở xã đó (không bỏ qua kiểm).
- **D3 — Ranh giới nạp một lần mỗi xã** qua endpoint có sẵn `PUT /admin/communes/{id}/boundary`; import tài sản KHÔNG nạp / sửa ranh giới.
- **D4 — Chỉ kiểm khi toạ độ hoặc `commune_id` đổi** (tạo mới luôn kiểm). Sửa ghi chú, bóng, mạch… của cột cũ đang lệch vẫn được.
- **Phạm vi: tài sản dạng điểm** — `pole`, `electrical_cabinet`. Tuyến và feeder KHÔNG kiểm (`inter_commune` được phép băng qua nhiều xã).

## Phải đọc trước

- `CLAUDE.md` (lõi), `docs/agents/traps/assets.md` (COMMUNE-BBOX, COMMUNE-BOUNDARY, POLE-LOCATION, BE-12a), `docs/agents/traps/spatial-map.md`,
  `docs/agents/traps/testing.md`
- `docs/contract-drift.md` mục COMMUNE-BOUNDARY (CBD-1…CBD-6)
- `src/LuxMap.Persistence/AdministrativeUnit.cs`, `src/LuxMap.Modules.Admin/Communes/*`
- `src/LuxMap.Modules.Assets/Import/AssetImportService.cs`, `Crud/AssetCrudService.cs`, `Crud/PoleRelocation.cs`

## Phase 1 — khảo sát (KHÔNG sửa code), ghi `.ai/results/COMMUNE-BOUNDARY-GUARD-p1.md`

1. **Liệt kê MỌI đường ghi** làm đổi `Pole.Geom`, `Pole.CommuneId`, `ElectricalCabinet.Geom`, `ElectricalCabinet.CommuneId` trên `origin/dev`
   (file:dòng): CRUD tạo / sửa, dời cột (`PoleRelocation`), import CSV / GeoJSON / workbook nhiều sheet, `POST /sync/push`, phiếu
   cập nhật GPS cột (`pole_gps_update`, POLE-GPS-PHOTO), seeder / mock / demo, bất kỳ job nào. Với từng đường: có đi qua service chung không,
   có thể đổi `commune_id` không.
2. **Đề xuất một chỗ định nghĩa duy nhất** cho phép kiểm (luật một định nghĩa) và cách gọi nó từ từng đường ở mục 1. Nêu: module nào sở hữu,
   có cần port không (ranh giới module), truy vấn cụ thể (`ST_Covers(boundary, geom)` chạy ở SQL — EF dịch được không?), cách gom cho import
   N dòng (một truy vấn, không N truy vấn), thứ tự so với `CommuneWriteGuard` và khoá hàng.
3. **Hình dạng lỗi** cho từng đường: CRUD (400 `VALIDATION_FAILED` + `details` theo trường, hay mã mới?), import (lỗi dòng, cột nào),
   relocation, sync push. Phân biệt hai lý do: "ngoài ranh giới" và "xã chưa có ranh giới". Đề xuất → D-item, không tự chọn.
4. **Tác động lên test**: test / fixture nào tạo cột hoặc tủ điện **qua API / service** ở xã chưa có ranh giới (sẽ đỏ theo D2). Đếm,
   liệt kê theo class, đề xuất cách sửa ít đụng nhất (helper fixture đặt ranh giới bao quanh?). Fixture ghi thẳng DbContext thì không đi qua chốt.
5. **Dữ liệu**: seeder / mock (`mocks/`, xã `COM-070` "Commune 01") — sau D2 chúng còn ghi được không? `COM-070` lấy ranh giới từ đâu
   (xã mock, không có relation OSM) → D-item.
6. **Đồng thời**: ranh giới bị sửa giữa lúc import (import giữ advisory lock theo loại tài sản, endpoint ranh giới thì không) — có cần gì không.
7. **Tài liệu phải sửa ở phase 2**: mục drift mới (sửa CBD-1), traps, OpenAPI (`gen_consolidated_spec.py` có schema viết tay đè không),
   `tracking.html`. Chỉ liệt kê, chưa sửa.

Cuối file: danh sách **D-item** còn mở (mỗi cái kèm phương án và khuyến nghị).

## KHÔNG ĐƯỢC làm

- Không sửa code, không sửa docs ở phase 1. Chỉ ghi file results.
- Không tạo migration (cột ranh giới đã có; nếu thấy cần migration thì ghi D-item).
- Không dùng `bbox` để kiểm (CB-1 vẫn đúng: bbox là khung nhìn).
- Không suy `commune_id` từ ranh giới (đó là phần "suy xã" của Q17 / BE-18, quyết định riêng).
- Không kiểm tuyến, feeder, sự cố, IoT node.
- Không đổi hành vi `PUT /admin/communes/{id}/boundary`.
- Không commit; không chạy `dotnet test` (sandbox không có Docker) — ghi rõ "chưa chạy".
- Không viết định danh cột / constraint từ trí nhớ: dán dòng code thật.

## Phase 1 dừng ở đâu

Xong 7 mục thì **dừng hẳn**, chờ Mỹ chốt D-item. Không viết code.

## Phase 2 — D-item đã chốt (Mỹ, 26/11/2026, trên `results/COMMUNE-BOUNDARY-GUARD-p1.md`)

- **D-1 (a):** luật chỉ áp cho lần ghi **mang toạ độ**. Cột tạo với `geom = null` vẫn được tạo ở xã chưa có ranh giới; lần đặt vị trí đầu tiên mới kiểm.
- **D-2 (a):** `400 VALIDATION_FAILED`, `details { field, reason, commune_id }`, `reason` ∈ `OUTSIDE_COMMUNE_BOUNDARY` | `COMMUNE_BOUNDARY_MISSING`
  (chuỗi ổn định). Không thêm mã vào `ErrorCodes`. Import: lỗi dòng (`geom_wkt` khi ngoài, `commune_id` khi xã thiếu ranh giới), workbook dùng lại planner.
  Thông điệp nêu tên/id xã + lý do, KHÔNG nêu toạ độ ranh giới.
- **D-3 (a):** dời cột / GPS verify: kiểm hành lang tuyến trước (giữ 409 `POLE_OFF_SEGMENT`), ranh giới sau → 400.
- **D-4:** sync push: lỗi ranh giới là 400 → `rejected[]` (không `conflicts`).
- **D-5 (a):** import: sau advisory lock, `SELECT 1 FROM administrative_unit WHERE commune_id = ANY(...) ORDER BY commune_id FOR SHARE`;
  dời cột: `FOR SHARE` xã **sau** khoá `work_order`/`pole` hiện có; `UpdateBoundaryAsync`: `FOR UPDATE` hàng xã trước khi gán. Có test chứng minh
  khoá (đọc `docs/agents/traps/testing.md` về `FOR NO KEY UPDATE` / `FOR SHARE`) và ghi trap.
- **D-6 (b):** một hàm thuần duy nhất `CommuneBoundary.Verdict` ở `LuxMap.Persistence` (Inside / Outside / Missing); CRUD có thể đọc `Boundary` theo PK rồi
  gọi hàm thuần; import đọc ranh giới các xã trong file bằng MỘT truy vấn rồi gọi hàm thuần. Không `HasDbFunction` mới.
- **D-7:** fixture `AssetImportFixture` + `AssetWorkbookTestContext` đặt ranh giới MultiPolygon gồm các chữ nhật quanh mọi cụm toạ độ test đang dùng;
  test D2 tự tạo xã riêng (không xoá ranh giới xã dùng chung); viết lại nhánh 180/90 của `AssetCoordinateRangeTests`. Test mới tối thiểu:
  ngoài ranh giới → 400 reason; xã thiếu ranh giới → 400 reason; điểm trên biên → nhận; cột cũ đang lệch sửa field khác (cùng toạ độ) → nhận;
  import dòng ngoài → lỗi dòng còn dòng trong vẫn vào; feeder sai + toạ độ ngoài → vẫn 409 (thứ tự); cột `geom = null` ở xã thiếu ranh giới → tạo được.
- **D-8 (a):** thêm feature mock `COM-070` (chữ nhật `106.488–106.506 / 10.964–10.978`, `properties.mock = true`, ghi rõ KHÔNG phải ranh giới thật)
  vào `scripts/data/commune-boundaries.geojson`; cập nhật docstring `scripts/load_commune_boundaries.py`.
- **D-9:** không index, không migration; sửa comment `AdministrativeUnit.cs` cho đúng lý do.
- **D-10:** "đổi toạ độ" = X hoặc Y khác (khuôn `PoleRelocation` `Unchanged`), không `Equals`.
- **D-11:** kiểm phòng thủ khi `CommuneId` đổi, ghi drift là hiện chỉ create chạm tới.
- **D-12:** kiểm ranh giới đặt SAU `RequireFeederInCommuneAsync` / các 409 hiện có ở mọi đường create/update.

### Phase 2 phải làm

1. Code theo D-1…D-12 ở mọi đường của p1 mục 1 (CRUD cột/tủ tạo+sửa, `PoleRelocation` — phủ cả `/location` và GPS verify, import CSV/GeoJSON/workbook).
2. Test như D-7; sửa fixture. Tên test mô tả hành vi.
3. Docs: entry drift mới **COMMUNE-BOUNDARY-GUARD** (CBG-1…) sửa CBD-1, ghi D-1…D-12 phần chạm bề mặt API, consumer WP5/WP6, SELF-SIGNED Mỹ 26/11;
   sửa câu CBD-1 trỏ sang entry mới (không xoá lịch sử); `docs/agents/traps/assets.md` (COMMUNE-BOUNDARY), `commune-scope.md`, `testing.md` (bẫy
   collection fixture); `docs/database/erd.md` Q17; XML doc ở `AdministrativeUnit.cs`, `CommuneAdminService.cs`, `AdminCommunesController.cs`;
   `docs/openapi/tools/gen_consolidated_spec.py` mô tả các endpoint liên quan (KHÔNG tự sinh lại JSON — Claude làm).
4. Ghi `.ai/results/COMMUNE-BOUNDARY-GUARD-p2.md`: file đã sửa, quyết định nhỏ đã tự lấy (nếu có), test mới. Không chạy `dotnet test`, không commit.

### Phase 2 KHÔNG ĐƯỢC

- Không migration, không đổi `PUT /admin/communes/{id}/boundary` ngoài phần khoá D-5, không mã lỗi mới, không kiểm tuyến/feeder/sự cố/IoT node,
  không dùng bbox, không suy `commune_id` từ ranh giới, không sửa `tracking.html` / `docs/openapi/*.json` / `docs/api-contract-v1.1.md`.
- Không `ExecuteUpdate` / `ExecuteDelete`. Không ghim ID literal trong test.
- Gặp chỗ mơ hồ mới: chọn phương án ít đổi hành vi nhất và GHI RÕ trong results, đừng dừng.
