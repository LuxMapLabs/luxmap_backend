---
ticket: ASSET-SEGMENT-POLES
title: POST /assets/segments và PUT /assets/segments/{id} — nhận `pole_ids` (tập cột) để gán / bỏ pole.segment_id trong một transaction
status: ready
phase: 2
owner: codex
branch: feat/ASSET-segment-pole-ids
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: ASSET-SEG-1 (kiểm docs/contract-drift.md trước, dùng số kế tiếp nếu đã có)"
depends_on: []
---

## Bối cảnh

FE (WP5) xin: tạo / sửa tuyến kèm danh sách cột, thay vì gọi `PUT /assets/poles/{id}` từng cột. Mỹ chốt (09/11/2026) theo
thảo luận Claude–Codex, **bốn điểm** (không phải D-item):

- **B — thứ tự không lưu.** `pole_ids` là một **TẬP**; thứ tự trong mảng KHÔNG được lưu và KHÔNG được dùng. Thứ tự dọc tuyến vẫn chỉ
  do hình học tính (`WorkOrderPoles`, `position`). Không thêm cột, không migration. FE phải được báo rõ điều này (ghi vào drift).
- **D — khác xã.** Cột phải **thấy được** qua query filter (ngoài phạm vi → **404**, không đổi thành 409 để khỏi lộ tồn tại; không dùng
  `IgnoreQueryFilters`). Nếu `pole.CommuneId != segment.CommuneId`: chỉ cho khi `segment.RoadClass == InterCommune`, ngược lại **409
  `CROSS_COMMUNE_REFERENCE`** (mã hiện có, `details` nêu hai xã như `RequireFeederInCommuneAsync`). Chỉ kiểm cho cột **được gán mới**
  (cột đã thuộc tuyến này và vẫn nằm trong danh sách thì giữ nguyên, kể cả khi `road_class` vừa đổi trong cùng PUT).
- **Cột đang thuộc tuyến KHÁC → 409 `ASSET_IN_USE`** (mã hiện có), `details` có `pole_id` và `segment_id` hiện tại. Cột chưa thuộc tuyến nào
  (`segment_id = null`) hoặc đã thuộc chính tuyến này thì được.
- **Ngữ nghĩa PUT:** `pole_ids` **vắng = giữ nguyên** mọi cột của tuyến (FE cũ gửi PUT không có field này không được làm mất cột — cùng
  tinh thần CAB-6 / `note`); `pole_ids: []` = **bỏ hết** (mọi cột đang thuộc tuyến này, mà người gọi thấy được, → `segment_id = null`);
  `pole_ids: null` = **400**. Danh sách có mặt = thay thế đầy đủ: cột đang thuộc tuyến mà không có trong danh sách → `segment_id = null`.
  POST: vắng hoặc `[]` = tuyến không cột; `null` = 400.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/assets.md` + `docs/agents/traps/commune-scope.md` + `docs/agents/traps/testing.md`
- `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs`: `CreateSegmentAsync`, `UpdateSegmentAsync`, `CreatePoleAsync`, `UpdatePoleAsync`, `RequireFeederInCommuneAsync`, các chỗ đã dùng `SELECT 1 … FOR UPDATE` (dòng ~680, ~734), `Touch`, `RequireAsync`
- `src/LuxMap.Modules.Assets/Crud/AssetContracts.cs` (`UpdatePoleRequest.SegmentId` / `ReadSegmentId`, `UpdateFeederRequest.CabinetId` / `ReadCabinetId` — mẫu `JsonElement` + `OptionalJson`), `src/LuxMap.Shared/Http/OptionalJson.cs`
- `tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs` (có test cross-commune đang `Skip` chờ D-6b — **đừng bỏ Skip**), `AssetPermissionTests.cs`, `AssetUpdatedByTests.cs`

## Yêu cầu

1. `CreateSegmentRequest.PoleIds` và `UpdateSegmentRequest.PoleIds` kiểu `JsonElement` (giữ vắng / null / mảng phân biệt được), kèm hàm đọc ra `(bool Present, IReadOnlyList<string> Ids)`. Không phải mảng chuỗi, phần tử rỗng / không khớp `^POLE-[0-9]{4,}$` (`[0-9]`, không `\d`), trùng nhau (so ordinal), hoặc quá **2000** phần tử → **400 `VALIDATION_FAILED`** với `details.field = "pole_ids"`. Kiểm này chạy **trước** mọi truy vấn DB (400 không bị 404 / 409 che).
2. Một hàm dùng chung cho POST và PUT (đúng một chỗ, như `RequireFeederInCommuneAsync`): nhận segment (đã tạo trong ChangeTracker hoặc đã đọc) + tập id → đọc cột qua query filter (một truy vấn `Contains`), thiếu cột nào → **404** `NOT_FOUND` nêu id đầu tiên thiếu (cùng kiểu `RequireAsync` hiện có); áp luật D và luật "tuyến khác → 409"; gán `SegmentId`, `Touch(pole)` cho mỗi cột **thật sự đổi**; với PUT, bỏ cột đang thuộc tuyến mà không có trong danh sách. Cột không đổi thì **không** bị chạm (không `Touch`, không `UpdatedAt` mới, không audit thừa).
3. **Một `SaveChangesAsync` duy nhất** cho tuyến + mọi cột (một transaction). Lỗi giữa chừng → không cột nào đổi, tuyến không đổi / không được tạo.
4. **Chống đua:** hai yêu cầu đồng thời cùng gán một cột tự do cho hai tuyến khác nhau phải kết thúc với đúng một thành công và một **409**, cột nằm ở đúng một tuyến. Theo bẫy "Khoá hàng" của CLAUDE.md: đọc qua query filter trước, rồi `SELECT 1 FROM pole WHERE pole_id = ANY(...) ORDER BY pole_id FOR UPDATE` (thứ tự khoá cố định theo id), rồi đọc lại bằng LINQ; POST cần khoá **trước khi** insert segment hoặc trong cùng transaction rõ ràng — đọc `traps/assets.md` và các chỗ FOR UPDATE hiện có để theo đúng khuôn, và ghi quyết định vào results. Không giữ khoá qua thao tác chậm. Nếu cần transaction tường minh thì bắt lỗi xong rollback trước mọi truy vấn khác.
5. Đường **cũ không đổi hành vi**: `PUT /assets/poles/{id}` và `POST /assets/poles` vẫn đặt `segment_id` như trước (không thêm kiểm khác xã vào đó — nó nằm ngoài phạm vi; nếu thấy bất nhất giữa hai đường thì GHI vào results, đừng sửa).
6. Test (assert giá trị cụ thể; tiền tố riêng; teardown theo thứ tự FK; không ghim ID bằng literal; class ghi tài sản nằm trong `AssetDatabaseCollection`):
   - POST có `pole_ids` → 201, các cột đó có `segment_id` mới, cột khác không đổi; vắng / `[]` → không cột nào đổi;
   - PUT thay thế: A,B → B,C ⇒ A về null, B giữ, C gán; `updated_at` của B **không** đổi, của A và C có đổi;
   - PUT vắng `pole_ids` ⇒ mọi cột giữ nguyên; `[]` ⇒ mọi cột về null;
   - 400: `null`, không phải mảng, phần tử số, rỗng, sai định dạng, trùng, 2001 phần tử — mỗi ca một assert `details.field`; và 400 thắng 404 (tuyến không tồn tại + body sai ⇒ 400);
   - 404: cột không tồn tại; cột ngoài phạm vi xã của người gọi (dùng tài khoản một xã) — và **không có cột nào đổi** (atomic);
   - 409 `ASSET_IN_USE`: cột thuộc tuyến khác, `details` đúng; atomic (các cột hợp lệ cùng danh sách KHÔNG bị gán);
   - cột đã thuộc chính tuyến này + còn trong danh sách ⇒ OK, không đổi;
   - luật D: tuyến `inter_village` + cột khác xã ⇒ 409 `CROSS_COMMUNE_REFERENCE`; tuyến `inter_commune` + cột khác xã ⇒ OK. Cần người gọi thấy cả hai xã — dùng `system_admin` (`*`) hoặc tài khoản đa xã nếu fixture hiện có cho; nếu không có sẵn thì tạo trong test, **không** sửa tài khoản seed dùng chung;
   - quyền: các vai trò không có `ManageAssets` vẫn 403 như cũ (chỉ cần một test hồi quy nếu `AssetPermissionTests` chưa phủ);
   - đua: hai PUT / POST đồng thời cùng một cột ⇒ một thành công một 409 (lặp vòng nhỏ vài lần cho chắc; test phải xác định, không dựa vào sleep);
   - cột bị bỏ khỏi tuyến mà người gọi KHÔNG thấy (xã khác) ⇒ vẫn thuộc tuyến (không bị null), nếu dựng được; nếu không dựng được thì ghi vào results.
7. **Phá thử** (ghi vào results là "chưa chạy, Claude sẽ chạy", kèm đúng dòng): (a) bỏ `FOR UPDATE` ⇒ test đua đỏ; (b) bỏ nhánh `ASSET_IN_USE` ⇒ test 409 đỏ; (c) bỏ nhánh bỏ-cột-vắng ⇒ test thay thế đỏ; (d) bỏ kiểm `InterCommune` (luôn cho) ⇒ test luật D đỏ; (e) `Touch` cả cột không đổi ⇒ test `updated_at` của B đỏ; (f) tách hai `SaveChanges` ⇒ test atomic đỏ.
8. Tài liệu: `docs/contract-drift.md` dòng ASSET-SEG-1 (SELF-SIGNED 09/11/2026): hình dạng request, **thứ tự không lưu** (FE dùng `position` từ `GET /work-orders/{id}/poles` hoặc hình học), vắng = giữ / `[]` = bỏ hết / null = 400, các mã 400/404/409, luật khác xã, việc FE phải làm. `docs/agents/traps/assets.md` thêm bẫy ngắn (tập không thứ tự; vắng ≠ `[]`; hai đường ghi `segment_id`). `tracking.html` thêm thẻ. XML-doc hai request và hai action trong `AssetsController` (các `ProducesResponseType` 400/404/409). **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*` (Claude sinh lại).
9. Ghi `.ai/results/ASSET-SEGMENT-POLES.md` ngắn: đã làm gì, lệnh Claude cần chạy, điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới
- [ ] Test ở mục 6 có mặt; Claude chạy `dotnet test` xanh (Codex không chạy được test)
- [ ] `PUT/POST /assets/poles` không đổi hành vi; test cross-commune `Skip` cũ vẫn `Skip`

## KHÔNG ĐƯỢC làm

- Không migration, không thêm cột / index, không lưu thứ tự.
- Không `IgnoreQueryFilters`, không `EnterUnscopedSystemWriteBackdoor`, không `ExecuteUpdate` / `ExecuteDelete`.
- Không thêm mã lỗi mới, không đổi enum, không đổi field đã publish.
- Không tự thêm kiểm khác xã vào `CreatePole` / `UpdatePole`.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*`; không sửa tài khoản seed dùng chung trong test.
- Không chạy git, dotnet test, swagger (sandbox không có mạng / quyền) — Claude chạy.
