# POLE-GEOM-NULL — Phase 1: khảo sát (10/11/2026, claude)

Khảo sát trên `origin/dev` @ `aa7f3fa`. **Không sửa code.** Cách tìm: `grep` mọi `Geom` của `Pole` trong `src`
(bỏ `Migrations`) và mọi SQL thô chạm bảng `pole`.

## 1. Điểm chặn hôm nay

| Chỗ | Hiện trạng |
|---|---|
| `src/LuxMap.Modules.Assets/Entities/Pole.cs:77` | `public required Point Geom` |
| `src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:201` | `.HasColumnType(GeometryColumns.Point).IsRequired()` |
| `AssetCrudService.CreatePoleAsync` / `UpdatePoleAsync` (`:554`) | `Read<Point>(request.GeomWkt)` — WKT vắng ⇒ 400 |
| `AssetImportService.cs:99` | `geom_wkt` nằm trong `RequiredColumns(Poles)`; ô trống ⇒ "Geometry is required." (`AssetGeometry.cs:28`) |
| `AssetCrudService.SetPoleLocationAsync` (`:770`) | đọc `pole.Geom.Y` làm `before`, đo `moved` từ `candidate.Geom` |

## 2. Mọi chỗ đọc vị trí cột — hành vi khi `geom IS NULL` và đề xuất

"500" = C# đọc `.X` / `.Y` / `.Coordinate` của null, hoặc EF materialize `ST_Y(NULL)` vào `double`. "Rơi" = SQL trả NULL nên
hàng tự biến mất, không lỗi.

| # | Chỗ | Bề mặt | Nếu không sửa | Đề xuất |
|---|---|---|---|---|
| 1 | `Map/MapQueryService.PoleQuery` `:464` | `GET /map/poles` (bbox) | Rơi — `ST_Intersects(NULL, …)` | Giữ: cột chưa GPS không lên bản đồ (G-7). Thêm test |
| 2 | `Map/MapQueryService.ProjectPolesAsync` `:153` | `GET /sync/bundle` (lọc theo tuyến, không bbox) | **500** | Feature `geometry: null` — GN-4 |
| 3 | `Map/PoleDetailService` `:81` | `GET /map/poles/{id}` | **500** | `location: null` (G-3) |
| 4 | `Map/CabinetTopologyService` `:140`, `:167`, `:180` | `GET /map/cabinets/{id}/topology` | **500** | Bỏ qua cột chưa GPS, đếm riêng — GN-5 |
| 5 | `Assets/AssetCrudService.PoleRow` `:1123` | `GET /assets/poles`, `/assets/poles/{id}` | **500** | `location: null`; `geom_wkt: null` ở chi tiết (G-3) |
| 6 | `Assets/AssetCrudService.TopologyPageAsync` `:1015` | `GET /assets/…/poles` (topology list) | **500** | `lat`, `lng` null (G-3) |
| 7 | `Assets/AssetCrudService.RequirePolesInCorridorAsync` `:450` | `PUT /assets/segments/{id}` đổi hình học | Rơi — không bị đếm là lệch | Giữ: cột chưa GPS không chặn việc sửa tuyến. Thêm test |
| 8 | `WorkOrders/WorkOrderPoles` `:135`, `:155` | `GET /work-orders/{id}/poles` | **500** | `location: null`, `position: null`, xếp nhóm cuối — GN-6 |
| 9 | `WorkOrders/AutoDispatchQueries` `:99` | tự giao việc (thứ tự dọc đường) | **500** | Không tới được nếu G-4 giữ (chỉ đọc cột mang sự cố); vẫn làm null-safe |
| 10 | `WorkOrders/WorkOrderService` `:108`, `WorkOrderAgenda` `:174`, `Faults/FaultQueryService` `:129` | `location` của sự cố = `fault.lat/lng ?? pole.geom` | **500** | Không tới được nếu G-4 giữ; vẫn làm null-safe cùng fallback hiện có |
| 11 | `Faults/FaultReportService.ReportAsync` `:140` | `POST /faults` có `pole_id`, `/sync/push` `fault_report` | Sự cố không toạ độ | **409** mã mới (G-4) — kiểm trong service (sync đi cùng đường) |
| 12 | `Faults/FaultReportService.StageReviewedReportAsync` `:202` | Quản lý duyệt báo cáo công dân → sự cố | Sự cố không toạ độ | 409 cùng mã — chốt chặn thứ hai sau #13 |
| 13 | `Citizen/PublicReportService.DistanceAsync` `:310` | `POST /public/…` báo cáo QR | **500** (`SingleAsync` của `double` nhận NULL) | **409** mã mới trước khi đo (G-4) |
| 14 | `Citizen/PublicReportService.LookupAsync` `:66` | `GET /public/…` thông tin QR | Không lỗi | Thêm cờ để FE hiện thông báo trước khi người dân điền form — GN-7 |
| 15 | `Citizen/CitizenReviewService` `:410` | chi tiết báo cáo ở hàng chờ | **500** | Không tới được nếu G-4 giữ; vẫn làm null-safe |
| 16 | `Survey/SurveyChainageQuery` `:81–95` (SQL thô) | khảo sát đêm: cột kỳ vọng trên tuyến | Rơi — JOIN có `ST_Intersects(p.geom, …)` | Giữ (G-7). Cột chưa GPS không tính vào `poles_expected`, không ra `unknown`. Thêm test |
| 17 | `Survey/ReviewedPolePhotoPublisher` `:61`, `SurveyReviewService` `:307` | ảnh cột được xác nhận / sweep duyệt → sự cố | Ảnh: sự cố không toạ độ. Sweep: không tới được (#16) | Ảnh cột — GN-3 |

Không đọc vị trí cột: `Statistics`, `Telemetry` (thiết bị dùng toạ độ tủ), `Sync/SyncBundleService` ngoài #2.

**Index:** `ix_pole_geom` (GIST) nhận NULL bình thường; không đổi index. `SpatialFunctions.DistanceMeters` ghi chú chỉ dùng
cho cột `NOT NULL` — phải cập nhật ghi chú đó và rà mọi lời gọi với `pole.Geom`.

## 3. Migration

- `Up()`: một `AlterColumn<Point>` trên `pole.geom`, `nullable: true`. Không có CHECK nào trên `geom` cần đổi.
- `Down()`: preflight `SELECT 1 FROM pole WHERE geom IS NULL` ⇒ từ chối có thông điệp (khuôn D-12 của
  POLE-SEGMENT-NULL), rồi `SET NOT NULL`. Không xoá, không gán toạ độ mặc định.
- Rollback khi đã có dữ liệu: gắn GPS hoặc xoá các cột chưa GPS trước.

## 4. D-item còn mở — cần Mỹ chốt trước Phase 2

| Mã | Câu hỏi | Đề xuất của Claude |
|---|---|---|
| **GN-1** | Tên mã lỗi 409 mới của G-4 | **`POLE_LOCATION_REQUIRED`**, `details {pole_id, operation}` với `operation` ∈ `fault_report` / `citizen_report` / `pole_photo` — cùng khuôn `POLE_SEGMENT_REQUIRED` |
| **GN-2** | Cột đã có GPS có quay về chưa GPS được không? `PUT /assets/poles/{id}` hôm nay bắt buộc `geom_wkt` | **Không.** PUT: cột chưa GPS gửi `geom_wkt` vắng / `null` ⇒ giữ chưa GPS; gửi WKT ⇒ gắn. Cột đã có GPS gửi vắng / `null` ⇒ **400** như hôm nay (không lặng lẽ giữ). Import: ô trống ở cột MỚI ⇒ chưa GPS; ở cột ĐÃ CÓ ⇒ giữ vị trí (khuôn D-2) |
| **GN-3** | Ảnh cột trong phiếu (`POLE-PHOTO-AI`): cột chưa GPS có nhận ảnh không? Ảnh xác nhận `off` sinh sự cố `lamp_out` theo cột | **Chặn lúc upload ảnh** bằng 409 `POLE_LOCATION_REQUIRED` — nhất quán G-4: gắn GPS trước rồi mới làm việc trên cột. Cách kia (nhận ảnh, công bố trạng thái nhưng không sinh sự cố) là âm thầm bỏ một phát hiện |
| **GN-4** | `GET /sync/bundle`: cột chưa GPS trên tuyến của phiếu có vào gói không? | **Có**, `geometry: null` — kỹ sư cần thấy cột để biết còn cột chờ gắn GPS. GeoJSON cho phép `geometry: null`; WP6 phải xử lý. Cách kia (bỏ khỏi gói) không vỡ client nhưng kỹ sư không thấy cột |
| **GN-5** | Sơ đồ nhánh tủ điện gặp cột chưa GPS: bỏ qua (G-7) hay 409 như cột chưa tuyến (D-3)? | **Bỏ qua + thêm `unlocated_pole_count`** vào response để FE biết sơ đồ thiếu cột. Khác D-3 có chủ ý: cột sổ sách là trạng thái bình thường kéo dài, 409 sẽ khoá sơ đồ của cả tủ cho tới khi gắn xong cột cuối |
| **GN-6** | `GET /work-orders/{id}/poles`: cột chưa GPS trên tuyến của phiếu | Vẫn hiện, `location: null`, `position: null`, xếp nhóm cuối của tuyến theo `created_at, length(pole_id), pole_id` (khuôn D-1). Phiếu khảo sát thì các cột này sẽ không có kết quả (#16) |
| **GN-7** | FE công dân biết cột chưa GPS bằng cách nào (G-5) | Thêm `can_report: false` + `reason: "pole_location_required"` vào `PublicQrInfo`? Hay chỉ dựa 409 lúc gửi. **Đề xuất: thêm trường** — người dân không phải điền form rồi mới bị từ chối |
| **GN-8** | Gắn GPS lần đầu **offline** | Ngoài phạm vi (PL-5 đã ghi dời offline là ticket sau). Hệ quả: kỹ sư cần mạng lúc gắn GPS. **Cần Mỹ xác nhận chấp nhận**, vì mobile vốn chạy trọn ca offline |
| **GN-9** | Response của `PUT …/location` lần đầu: `moved_m` | `moved_m: null` (không có vị trí cũ); audit `relocated` với `before: null`, `after.moved_m: null`. Không thêm `AuditAction` mới |
| **GN-10** | Bộ lọc cột chưa GPS | `GET /assets/poles?missing_location=true` (lọc trước tổng và phân trang; `false` = không lọc) — khuôn `unassigned_segment` |
| **GN-11** | Gắn tuyến cho cột chưa GPS qua `PUT /assets/segments/{id}` (`pole_ids`) và form cột | Cho phép, không đo hành lang (chưa có gì để đo); hành lang đo lúc gắn GPS (G-2) |

## 5. Ước lượng

- PR 2a: 1 migration, ~14 file `src`, test mới ở Assets / Map / WorkOrders / Faults / Citizen / Survey / Sync.
- PR 2b: `AssetCrudService`, `AssetContracts`, `AssetImportService`, `AssetsController`, OpenAPI, template, trap.
- Phải báo: **WP5** (model nullable, form, bộ lọc, mã lỗi, thông báo QR), **WP6** (model nullable, bundle `geometry: null`,
  màn gắn GPS, cần mạng — GN-8). **Chưa báo.**
