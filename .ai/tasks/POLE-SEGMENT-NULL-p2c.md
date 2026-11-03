---
ticket: POLE-SEGMENT-NULL
title: PR 2c — các bề mặt ĐỌC xử lý cột chưa tuyến (assets, map, topology cabinet, thống kê, lighting, citizen/QR) + mã lỗi POLE_SEGMENT_REQUIRED
status: in_progress
phase: 2
owner: codex (viết) · claude (review + commit + chạy test)
branch: feat/POLE-SEGMENT-NULL-2c-reads
contract_refs:
  - "docs/contract-drift.md — mục POLE-SEGMENT-NULL (D-3, D-6, D-9, D-10, D-14)"
  - ".ai/results/SPEC-NULLABLE-p1.md — cơ chế biểu diễn nullable trong OpenAPI (đọc TRƯỚC khi sinh spec)"
depends_on:
  - "PR 2a merged (#198). PR 2b (#199) mở song song — KHÔNG phụ thuộc code của 2b; sẽ rebase lên dev sau khi 2b merge"
---

## Bối cảnh

Sau 2a/2b, cột chưa tuyến chưa thể tạo qua API (nút bật là 2e) nhưng ĐÃ có thể tồn tại trong DB. 2c làm cho mọi bề mặt ĐỌC hiển thị đúng thay vì
nổ 500 hoặc ra chuỗi rỗng giả: D-3 (sơ đồ nhánh cabinet), D-6 (`segment_name` null), D-9 (thống kê), D-10 (lighting). Bảng quyết định đầy đủ:
`.ai/tasks/POLE-SEGMENT-NULL-p2.md`. Danh sách chỗ phải xử lý: `.ai/results/POLE-SEGMENT-NULL-p2a.md` §4 (các dòng "2c") và
`.ai/results/POLE-SEGMENT-NULL-p1.md` §1, §3 (hình dạng API), §4, §6 (T03, T06, T08, T10). Mọi dòng `SegmentId!` do 2a thêm ở các file thuộc 2c phải được thay bằng xử lý thật.

## Phải đọc trước

`CLAUDE.md` (= `AGENTS.md`), `.ai/README.md`, `.ai/tasks/POLE-SEGMENT-NULL-p2.md`, `.ai/results/POLE-SEGMENT-NULL-p1.md` §1/§3/§4/§6, `.ai/results/SPEC-NULLABLE-p1.md`,
`docs/agents/traps/spatial-map.md`, `assets.md`, `faults-workorders.md`, `citizen.md`, `iot-lighting.md`, `testing.md`, `tests/LuxMap.Api.Tests/PoleSegmentNullableTests.cs`
(cách dựng cột null qua `DbContext` + `EnterUnscopedSystemWriteBackdoor`).

## Yêu cầu — PR 2c

1. **D-6 — `segment_name` / `segment_id` null ở bề mặt đọc, KHÔNG chuỗi giả.** Giữ key, giá trị `null`:
   - `GET /assets/poles[/{id}]` (`PoleListItem`, `PoleDetail.pole`): `segment_id`, `segment_name` nullable (hiện fallback `""` ở `AssetCrudService` quanh dòng 125/879/966).
   - `GET /assets/feeders/{id}/poles`, `/assets/feeders/poles?unassigned=true` (`TopologyPole.segment_id` nullable). Ý nghĩa `unassigned=true` ở đây vẫn là **chưa gán feeder** — không đổi.
     `GET /assets/segments/{id}/poles` giữ nguyên (chỉ cột của tuyến đó).
   - `GET /map/poles` (`PoleProperties.segment_id` nullable, vẫn flat, vẫn bbox/413/scope/data_source, không `feature.id`), `GET /map/poles/{id}` (`PoleMapDetail.segment_id`, `segment_name` nullable).
   - `GET /statistics/warranty` (`WarrantyItem.segment_id` nullable; JSON null, CSV ô trống theo đường xuất chung).
   - Citizen: `PublicQrInfo.segment_name` null, `CitizenReportPole.segment_id` null (`CitizenReviewService.cs:410`, `PublicReportService.cs:31,71` — lookup tên tuyến đang dùng scalar `First` không fallback). QR của cột chưa tuyến KHÔNG được coi là invalid.
   - `SyncPoleProperties` kế thừa `PoleProperties`: nullable chảy theo; KHÔNG sửa logic `SyncBundleService` (2d). Ghi hiệu ứng spec vào kết quả.
2. **D-3 — `GET /map/cabinets/{cabinetId}/topology`:** nếu có BẤT KỲ cột (thuộc các mạch của tủ) chưa tuyến ⇒ từ chối, không bịa cạnh.
   **409 `POLE_SEGMENT_REQUIRED`** (D-14), `details = { operation: "cabinet_topology", pole_ids: [tối đa 100 ID đầu, thứ tự created_at, length(pole_id), pole_id], pole_count: <tổng> }`.
   Mã lỗi mới: thêm vào `ErrorCodes`, thêm `[ProducesResponseType(409)]` cho endpoint (nhớ: hai attribute cùng mã thì cái sau thay cái trước), liệt kê ở nơi repo liệt kê mã lỗi
   (grep `POLE_OFF_SEGMENT` làm mẫu: `ErrorCodes.cs`, drift, OpenAPI summary). `CabinetTopologyService` hiện dùng `ContainsKey(null)`/`SegmentId!` (dòng ~90–110) — thay bằng kiểm rõ ràng ở ĐẦU truy vấn, không để rơi 500.
3. **D-9 — thống kê** (`StatisticsService`):
   - `GET /statistics/fixture-status`: mỗi `FixtureStatusRow` thêm `unassigned_segment_pole_count` (int, ≥ 0) = số cột của nhóm đó chưa tuyến. Khi `group_by=segment`, nhóm `segment_id: null` là nhóm chưa tuyến (thứ tự hàng: nhóm null **cuối cùng** trong cùng `data_source`/`commune`,
     không dùng ID trần). Khi không group theo segment, hàng vẫn đếm cả cột chưa tuyến vào `pole_count` và cho `unassigned_segment_pole_count`. KHÔNG gộp nhóm null vào tuyến khác hay vào `out`; `unknown`/`never_surveyed` giữ nghĩa cũ; không thêm giá trị enum.
     Lọc `?segment_id=` giữ so sánh bằng (không khớp cột null).
   - `warranty`: xem mục 1. Nếu có CSV của fixture-status, thêm cột mới ở CUỐI.
4. **D-10 — lighting** (`LightingCommandService.PlanAsync` ~dòng 619–669):
   - Lọc `null` khỏi `affected_segment_ids` (hiện `Select(pole => pole.SegmentId!).Distinct()`); mảng không bao giờ chứa `null`.
   - Thêm `unassigned_segment_pole_count` (int) vào **preview** và vào response của **POST /lighting/commands** = số cột chưa tuyến bị ảnh hưởng (cột nằm trong các mạch được điều khiển). `uncontrollable_pole_count` KHÔNG tăng chỉ vì chưa tuyến.
   - ⚠️ Snapshot của lệnh (`AffectedSegmentIds`, `UncontrollablePoleCount`) được LƯU trong DB (`LightingEntities.cs`). **KHÔNG thêm cột, KHÔNG migration.** Trường mới chỉ tính lúc lập kế hoạch; khi dựng response từ bản lưu (đọc lại lệnh/request cũ) nó là `null` ("không ghi lại"), nên kiểu là `int?` ở record dùng chung.
     Ghi vào kết quả thành D-item: có cần lưu bền không (cần migration + duyệt).
   - Đích theo tuyến (`segment_id=`): cột chưa tuyến không thuộc tuyến nào nên không nằm trong phạm vi tuyến; không đổi.
5. **OpenAPI:** sinh lại bằng lệnh chuẩn (`.ai/context/commands.md`: `dotnet swagger tofile` + `gen_consolidated_spec.py` + lint), KHÔNG sửa tay file sinh. `gen_consolidated_spec.py` có override viết tay cho `PoleProperties.segment_id` (`$ref: SegmentId`, ~dòng 376–383):
   phải cho phép null theo cơ chế của `.ai/results/SPEC-NULLABLE-p1.md` (OpenAPI 3.0: `$ref` không mang `nullable` bên cạnh — đọc quyết định D-1…D-6 ở đó; nếu chúng chưa được hiện thực, dùng cách hẹp nhất đã được duyệt và ghi D-item).
   Chỉ nullable hoá đúng thuộc tính của **quan hệ cột → tuyến** (liệt kê ở mục 1); KHÔNG đổi `SegmentId` PK dùng chung, `SegmentProperties`, `SegmentListItem`, `SegmentCoverage`, phần tử `segment_ids[]`.
6. **Test (Testcontainers PostGIS qua `LuxMapTestHost`; không `luxmap_dev`)**, dựng cột null qua `DbContext`:
   - `Unassigned_pole_remains_in_inventory_bbox_detail_and_feeder_lists` — giữ hàng/ID/toạ độ/trạng thái; key `segment_id`/`segment_name` hiện `null` (kiểm `JsonValueKind.Null`, không `""`); trộn null + có tuyến; scope xã; phân trang/`total` đúng; tập key cũ của test shape (`AssetReadShapeTests`, `TopologyQueryTests`) không đổi ngoài giá trị null.
   - `Cabinet_topology_refuses_with_unassigned_poles_and_lists_them` — 409 `POLE_SEGMENT_REQUIRED`, `details.pole_ids` đúng thứ tự (ID hai bên ngưỡng độ rộng), `pole_count`; tủ không có cột null vẫn trả sơ đồ như cũ; 404 không bị biến thành 409.
   - `Statistics_count_unassigned_separately_without_losing_unknown` — nhóm null cuối cùng, `unassigned_segment_pole_count`, `never_surveyed`/`unknown` giữ nghĩa, không gộp vào `out`, warranty JSON null + CSV ô trống, `calibration_rig` mặc định loại như cũ.
   - `Qr_lookup_and_citizen_review_work_for_unassigned_pole` — QR lookup 200 với `segment_name: null` (không 500, không "QR invalid"); citizen detail `segment_id: null`.
   - `Lighting_handles_unassigned_poles_without_null_affected_ids` — feeder có cả cột có tuyến lẫn chưa tuyến; chỉ chưa tuyến; cột chưa nối rơ-le: `affected_segment_ids` không có `null`, không trùng; `unassigned_segment_pole_count` đúng; `uncontrollable_pole_count` không tăng vì chưa tuyến; đọc lại lệnh cũ ⇒ trường mới `null`.
   - `OpenApiSpecTests`: các thuộc tính ở mục 1 nullable đúng; `SegmentId` PK/`SegmentProperties`/`segment_ids[]` KHÔNG nullable; `POLE_SEGMENT_REQUIRED` có ở 409 của endpoint cabinet topology.
   - Test hiện có giữ kỳ vọng; chỉ sửa test nào ghim `segment_id`/`segment_name` non-null của các DTO ở mục 1.
   - **Phá thử bắt buộc** cho từng chốt (ví dụ: trả `""` thay `null` cho `segment_name`; bỏ kiểm cabinet ở đầu ⇒ 500 hoặc sơ đồ thiếu cột; bỏ lọc null khỏi `affected_segment_ids`; cho `uncontrollable` đếm cả cột chưa tuyến; gộp nhóm null vào hàng khác; thêm `INNER JOIN road_segment` làm cột biến mất khỏi list).
     Mỗi lần: test ĐỎ vì ĐÚNG lý do (đọc thông báo lỗi), khôi phục bằng `cp`/`touch`, đọc **mã thoát** `dotnet test`.
7. **Tài liệu:** `docs/contract-drift.md` mục POLE-SEGMENT-NULL: dòng trạng thái "PR 2c đã làm: D-3, D-6, D-9, D-10, D-14" (không đổi bảng D-1…D-14) + tên trường mới (`unassigned_segment_pole_count`) + mã `POLE_SEGMENT_REQUIRED` 409 + hiệu ứng lên `SyncPoleProperties`;
   `docs/agents/traps/spatial-map.md` (hàng map `segment_id: null`, bẫy `ContainsKey(null)` / INNER JOIN road làm cột biến mất, tổng cột toàn xã ≠ tổng đếm theo tuyến), `iot-lighting.md` (affected IDs không null; trường mới không lưu bền), `citizen.md` (QR của cột chưa tuyến hợp lệ); `assets.md` chỉ khi có bẫy MỚI đã xác minh.
8. Build / test: `MSBUILDDISABLENODEREUSE=1 dotnet build --disable-build-servers` rồi `dotnet test`. Nếu sandbox không chạy được Docker / test, nói thẳng trong kết quả — Claude sẽ chạy lại. Đừng báo xanh khi chưa chạy.

Tiêu chí xong PR 2c:

- [ ] Build 0 warning / 0 error; không còn `SegmentId!` do 2a thêm ở các file thuộc 2c (liệt kê các `!` còn lại, nếu có, kèm lý do)
- [ ] `dotnet test` mã thoát 0 (hoặc báo rõ phần chưa chạy được)
- [ ] 6 test mới ở mục 6 + mỗi chốt có bằng chứng phá thử đỏ đúng lý do (lệnh + output)
- [ ] OpenAPI sinh bằng lệnh chuẩn; chạy lại sinh lần nữa ⇒ `git diff` rỗng; lint sạch
- [ ] `git diff origin/dev --stat` chỉ chạm: Assets read, Map, Statistics, Telemetry/Lighting, Citizen, ErrorCodes, DTO liên quan, OpenAPI sinh + `gen_consolidated_spec.py`, test, drift, traps, `.ai/results/POLE-SEGMENT-NULL-p2c.md`
- [ ] `results/POLE-SEGMENT-NULL-p2c.md`: từng hit 2c của p2a §4 (sửa / không sửa + lý do), output build/test/phá thử thật, D-item mới

## KHÔNG ĐƯỢC làm

- Không mở API tạo cột chưa tuyến: POST/PUT/import vẫn BẮT BUỘC tuyến; không bộ lọc `unassigned_segment` (2e). Không đổi nghĩa `unassigned=true` của feeder.
- Không động vào logic `SyncBundleService` (2d), không `WorkOrders`/dời cột/sự cố (đã ở 2b).
- Không migration, không đổi schema, không cột lưu mới (kể cả cho lighting). Không tuyến giả, không chuỗi `"Chưa phân tuyến"` ở API (FE tự hiển thị).
- Không thêm giá trị enum nào; không đổi `fixture_status`; không gộp `unknown` vào `out`.
- Không sửa `docs/api-contract-v1.1.md`, không `tracking.html` (Claude cập nhật sau review). Không sửa tay `docs/openapi/*.json`.
- Không chạy trên `luxmap_dev`. Không `git commit` (sandbox không ghi `.git` của worktree — Claude commit sau review), không `git push`, không `git add -A`/`git add .`.
- Không `ExecuteUpdate`/`ExecuteDelete`; không project reference ngược chiều; không `ORDER BY` ID trần; không so sánh `null == null` theo cách làm hai cột null khớp nhau.
- Chỗ mơ hồ → D-item ở cuối file kết quả, không tự chọn. Không mở lại D-1…D-14.
