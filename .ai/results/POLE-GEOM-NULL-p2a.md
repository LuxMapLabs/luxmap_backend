# POLE-GEOM-NULL — PR 2a: schema + mọi chỗ đọc (10/11/2026, claude)

Nhánh `feat/ASSET-pole-without-gps`. **Chưa bật writer:** `POST` / `PUT /assets/poles` và import vẫn bắt buộc `geom_wkt`
(PR 2b). Cột chưa GPS chỉ tồn tại nếu ghi thẳng vào DB — test của PR này làm đúng vậy.

## Đã làm

| Mục khảo sát (P1) | Kết quả |
|---|---|
| Migration `20261110135346_PoleGeomNullable` | `Up()`: một `AlterColumn pole.geom` nullable. `Down()`: từ chối khi còn `geom IS NULL`, rồi `SET NOT NULL` |
| #1 `GET /map/poles` (bbox) | Không đổi hành vi: cột chưa GPS không có trong kết quả. Có test |
| #2 `GET /sync/bundle` | Feature `geometry: null` (GN-4). `Feature<T>.Geometry` thành nullable ở C# |
| #3 `GET /map/poles/{id}` | `location: null` |
| #4 `GET /map/cabinets/{id}/topology` | Bỏ qua cột chưa GPS, thêm `unlocated_pole_count` (GN-5). Kiểu response mới `CabinetTopology` (trên dây vẫn là FeatureCollection + một trường) |
| #5, #6 inventory | `location: null`, `geom_wkt: null`; danh sách topology `lat` / `lng` null |
| #7 sửa hình học tuyến | Cột chưa GPS không bị tính là lệch hành lang (GN-11). Có test |
| #8 `GET /work-orders/{id}/poles` | `location: null`, `position: null`, đứng cuối tuyến của nó (GN-6) |
| #9, #10, #15 | Null-safe, giữ fallback cũ; không tới được vì G-4 |
| #11, #12 `POST /faults`, duyệt báo cáo công dân | 409 `POLE_LOCATION_REQUIRED` (`fault_report` / `citizen_report`) |
| #13 `POST /public/reports` | 409 `POLE_LOCATION_REQUIRED` (`citizen_report`), trước khi decode ảnh |
| #14 `POST /public/qr/lookup` | Thêm `can_report`, `reason` (GN-7) |
| #16 khảo sát đêm | Không đổi SQL: cột chưa GPS không thuộc tập cột kỳ vọng. Có test |
| #17 ảnh / clip cột trong phiếu | 409 `POLE_LOCATION_REQUIRED` (`pole_photo`) ở `WorkOrderPolePhotoService.Target` (GN-3) |

**Làm sớm hơn kế hoạch:** `PUT /assets/poles/{id}/location` đã gắn được GPS lần đầu (G-2, GN-9) — ticket xếp vào PR 2b, nhưng
chỉ vài dòng trong đúng hàm phải sửa cho null-safe, và chưa API nào tạo được cột chưa GPS nên không mở đường ghi mới.

## Kiểm chứng

- `dotnet build`: 0 lỗi, 0 cảnh báo.
- Test mới: `PoleGeomNullableTests` (4), `WorkOrderUnlocatedPoleTests` (2), hai test trong `PoleLocationTests`, một trong
  `FaultReportTests`, `SyncTests`, `SegmentPoleMembershipTests`; thêm một cột null vào `Chainage_is_projected_metres_and_its_actual_sql_uses_gist`.
- **Phá thử:** tắt lần lượt 5 chốt (409 của fault / public report / ảnh cột, bộ đếm topology, khoá sắp xếp phiếu) ⇒ đúng 5 test đỏ
  (public report thành 500 thật), khôi phục bằng `cp` + `touch`.
- OpenAPI: xuất lại `luxmap-v1.json` → generator → lint (10 cảnh báo, bằng trước khi sửa).
- Lượt chạy đầy đủ cuối (`dotnet test`, mã thoát 0): Shared 421, Persistence 45, Storage 80, Api 1596 qua / 7 bỏ qua / 0 đỏ.

## Chỗ lệch / phát hiện

- `CitizenReportTests.The_lookup_names_the_road_and_the_commune_and_no_internal_id` ghim danh sách khoá của `PublicQrInfo` bằng literal —
  đã sửa literal cùng diff (GN-7 thêm hai khoá).
- **Ngoài phạm vi, chưa sửa:** form công dân nhận `lat` / `lng` bằng `[FromForm] double?`; trên máy locale dấu phẩy thập phân (`en_VN`)
  `13.5` bị binder đọc sai → 400. Ghi ở `docs/agents/traps/citizen.md`.
- **Ngoài phạm vi, chập chờn:** `AvatarTests.Both_concurrent_uploads_succeed_and_leave_one_complete_set` đỏ 2 / 3 lượt ở nhánh này và đỏ
  cả trên worktree sạch của `origin/dev` @ `aa7f3fa` (lệch vài byte kích thước ảnh); lượt đầy đủ cuối cùng thì xanh.
- OpenAPI: `location` / `geometry` nullable là object nên vẫn xuất `$ref` trần (hạn chế chung đã biết).

## Còn lại (PR 2b)

`POST` / `PUT /assets/poles` và import nhận cột chưa GPS (GN-2), bộ lọc `missing_location=true` (GN-10), template import,
báo WP5 / WP6. `luxmap_dev` / VPS chưa migrate.
