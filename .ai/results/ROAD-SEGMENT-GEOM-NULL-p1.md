# ROAD-SEGMENT-GEOM-NULL — Phase 1: khảo sát (11/11/2026, claude)

Khảo sát trên `origin/dev` @ `970e300` (sau #225 / #227 / #228). **Không sửa code, không migration.** Cách tìm: `grep` mọi chỗ đọc
`RoadSegment.Geom` / `road_segment.geom` trong `src` (bỏ `Migrations`), rồi đọc từng chỗ. Chưa chạy `dotnet build` / `dotnet test` (không
cần ở Phase 1). `feat/SURVEY-segment-outage` (chưa merge) cũng sửa `SurveyReviewService` / `SurveyProcessor` — xem mục 7.

## 1. Điểm chặn hôm nay

| Chỗ | Hiện trạng |
|---|---|
| `Entities/RoadSegment.cs` (`Geom`) | `public required LineString Geom` |
| `Configurations/AssetConfigurations.cs:92` | `.HasColumnType(GeometryColumns.LineString).IsRequired()`; `:104` GIST `ix_road_segment_geom` |
| `Crud/AssetContracts.cs` `CreateSegmentRequest` / `UpdateSegmentRequest` | `[Required] string? GeomWkt` |
| `AssetCrudService.CreateSegmentAsync` (`:267`) | `Read<LineString>(request.GeomWkt)` — vắng ⇒ 400 |
| `AssetCrudService.UpdateSegmentAsync` (`:437–440`) | `Read<LineString>`, `segment.Geom.EqualsExact(geometry)` — hình cũ null sẽ NRE |
| `Import/AssetImportService.cs:99` | `geom_wkt` nằm trong `RequiredColumns(Segments)`; `:181`, `:193` gán `geometry!` |
| `AssetGeometry.TryReadWkt` | ô trống ⇒ "Geometry is required." |
| `Crud/AssetContracts.cs` `SegmentDetail.GeomWkt` | `required string` (không null) |

Không có CHECK nào trên `road_segment.geom` (chỉ `ck_road_segment_data_source`, `ck_road_segment_road_class`); không trigger, view, hàm
SQL nào đọc cột này. Index `ix_road_segment_geom` (GIST) nhận NULL bình thường (đúng như `ix_feeder_geom`, cột nullable duy nhất của module).
**`length_m` đã là giá trị KHAI BÁO** (`traps/spatial-map.md` bẫy 4; `UpdateSegmentAsync` không tính lại) nên không phụ thuộc hình.

## 2. Mọi chỗ đọc `road_segment.geom` — hành vi khi `geom IS NULL` và đề xuất

"500" = C# đọc `.Coordinates` / `.StartPoint` / `new LengthIndexedLine(null)` / `EqualsExact` của null, hoặc EF materialize
`ST_Distance(NULL, …)` vào `double` (ném lúc đọc). "Rơi" = SQL trả NULL nên hàng tự biến mất, không lỗi (cả hai dạng đã gặp ở
POLE-GEOM-NULL — `traps/assets.md` mục POLE-GEOM-NULL).

| # | Chỗ | Bề mặt | Nếu không sửa | Đề xuất |
|---|---|---|---|---|
| 1 | `Map/MapQueryService.SegmentsAsync` `:201` (`segment.Geom.Intersects(envelope)`) | `GET /map/segments` (bbox) | Rơi — `ST_Intersects(NULL, …)` | **Giữ rơi** (RG-4): tuyến chưa hình không có chỗ trên bản đồ. Test trồng tuyến null. Query vẫn đi qua `ix_road_segment_geom` |
| 2 | `Map/MapQueryService.ProjectSegmentsAsync` `:254` (`row.Geom.Coordinates`) | `GET /sync/bundle` qua `SegmentsByIdAsync` `:93` (không bbox) | **500** | Feature `geometry: null` (RG-6, khuôn GN-4); `Feature<T>.Geometry` đã nullable từ POLE-GEOM-NULL |
| 3 | `Assets/AssetCrudService.SegmentAsync` `:150–158` (`Wkt(extra.Geom)`) | `GET /assets/segments/{id}` | **500** | `geom_wkt: null`; `SegmentDetail.GeomWkt` thành `string?` (RG-5) |
| 4 | `Assets/AssetCrudService.SegmentRow` `:1173` | `GET /assets/segments` (danh sách) | Không đọc `Geom` — chạy được, nhưng FE **không phân biệt** tuyến có / chưa hình | Thêm `has_geometry` (RG-5; khuôn `FeederListItem.HasGeometry`, `:1202`) + bộ lọc `missing_geometry` |
| 5 | `Assets/AssetCrudService.UpdateSegmentAsync` `:439` | `PUT /assets/segments/{id}` | **500** khi tuyến chưa hình (`segment.Geom.EqualsExact`) | Luật RG-2; `rerouted = old is null \|\| !old.EqualsExact(new)` |
| 6 | `Assets/AssetCrudService.RequirePolesInCorridorAsync` `:460–470` | 409 `POLE_OFF_SEGMENT` khi đổi hình | Khi **gắn hình lần đầu**: chạy (rerouted = true) và đo mọi cột **có GPS** của tuyến | **Giữ** — đây là lưới an toàn của RG-11: cột được gắn GPS trước khi tuyến có hình sẽ bị bắt ở đây. Ghi nhận hệ quả cho FE |
| 7 | `Assets/AssetCrudService.SetPoleLocationAsync` `:817–819` (`DistanceMeters(segment.Geom, target)`) | `PUT /assets/poles/{id}/location` | **500** — `double` nhận NULL | Tuyến chưa hình ⇒ `offSegment = null`, **không đo**, 200 (RG-11). Audit `segment_distance_m: null` |
| 8 | `Assets/AssetCrudService.ReplaceSegmentPolesAsync` `:858` | `pole_ids` trên POST/PUT tuyến | Không đọc `Geom` | Gán được cột vào tuyến chưa hình, không đo (RG-13, khuôn GN-11) |
| 9 | `Assets/Import/AssetImportService.PlanSegmentsAsync` `:99`, `:181`, `:193` | import `segments.csv` / `.geojson` | Header thiếu `geom_wkt` ⇒ lỗi; ô trống ⇒ lỗi dòng | `geom_wkt` bỏ khỏi `RequiredColumns(Segments)`; ô trống ở tuyến MỚI ⇒ chưa hình, ở tuyến ĐÃ CÓ ⇒ giữ hình (RG-2); sai định dạng vẫn lỗi dòng. Dùng `IImportRow.GeometryIsEmpty` + `Geometry<T>` đã có |
| 10 | `Map/CabinetTopologyService` `:148`, `:168` (`new LengthIndexedLine(roads[…].Geom)`) | `GET /map/cabinets/{id}/topology` | **500** | Cột trên tuyến chưa hình **không xếp được thứ tự** ⇒ bỏ khỏi sơ đồ và đếm vào trường mới `unrouted_pole_count` (RG-10); khác `unlocated_pole_count` (cột chưa GPS) |
| 11 | `WorkOrders/WorkOrderPoles` `:133–134` (`new LengthIndexedLine(s.Geom)`), `:141`, `:154` | `GET /work-orders/{id}/poles` | **500** | Bỏ tuyến chưa hình khỏi `lines`; cột trên đó: `position: null`, vẫn hiện, `location` theo cột, xếp nhóm cuối của tuyến theo `created_at, length(pole_id), pole_id` (RG-8, khuôn GN-6). `WorkOrderPoleOrdering.Along` đã trả 0 khi không có `line` |
| 12 | `WorkOrders/AutoDispatchQueries` `:92–93` (`new LengthIndexedLine(s.Geom)`) | tự giao việc | **500** khi có sự cố trên cột thuộc tuyến chưa hình | Cùng cách #11: bỏ khỏi `lines`; `Along` ⇒ 0. Test trồng sự cố trên cột của tuyến null |
| 13 | `WorkOrders/WorkOrderAgenda` `:188`, `:191` (`DistanceMeters(s.Geom, origin)`), `:199`, `:223` (`StartPoint`, `Nearest`) | `GET /work-orders/agenda` | **500** (cả khoảng cách lẫn `StartPoint`) | Tuyến chưa hình: nhóm **vẫn có** `segment_name`; `location: null`, `distance_m: null`; nhóm xếp sau nhóm đo được khoảng cách (đã có: `OrderBy(DistanceM is null)`) (RG-9). Lọc `Geom != null` trước `DistanceMeters`, nhớ ép `Geom!` |
| 14 | `Survey/Ingest/SurveyIngestService.Create` `:66–97` | `POST /sweeps` (bắt đầu ghi) | Nhận sweep; về sau xử lý **lặng lẽ** bỏ tuyến (xem #15, #16) | **409 `SEGMENT_GEOMETRY_REQUIRED`**, `details {work_order_id, segment_ids}` (RG-7). Phiếu khảo sát vẫn TẠO / GIAO / LÊN LỊCH được với tuyến chưa hình |
| 15 | `Survey/Processing/SurveyChainageQuery.Gps` `:59–70` | khớp GPS xe với tuyến | Rơi — `ST_Intersects(…, NULL)` nên tuyến null không bao giờ là ứng viên; GPS thành "không gán tuyến" | **Giữ** (chốt chặn là #14). Không `COALESCE`. Test: sweep tạo được trước khi tuyến mất hình là không thể (RG-2) nên chỉ cần test xanh của #14 |
| 16 | `Survey/Processing/SurveyChainageQuery.Poles` `:87` (`JOIN … ST_Intersects(p.geom, ST_Expand(ST_Envelope(r.geom)…))`) | cột kỳ vọng trên tuyến | Rơi — cột của tuyến null không vào `poles_expected`, **coverage trông đủ** trong khi cả tuyến không được khảo sát | Cùng #14: tới được sweep chỉ nếu #14 hổng. Ghi vào bẫy: đây là chỗ "xanh giả" nếu ai bỏ #14 |
| 17 | `Survey/Review/SurveyReviewService.GapGeometriesAsync` `:146` (`ST_LineSubstring(ST_Transform(r.geom…))`) | `GET /sweeps/{id}/coverage` hình khoảng trống | NULL ⇒ `JsonDocument.Parse("")` ném | Không tới được nếu #14 giữ (khoảng trống chỉ có với tuyến đã khảo sát). Không thêm code; test duy nhất canh nó là test của #14 |
| 18 | `Survey/Processing/SurveyProcessor` `:147` | lấy `commune_id` của tuyến | Không đọc `Geom` | n-a |
| 19 | `Telemetry/Lighting/LightingCommandService` `:620`, `WorkOrders/SurveyRecurring*` `:50`, `:80`, `Jobs/SurveyRecurringGenerateJob` `:56`, `WorkOrders/WorkOrderService` `:191`, `:208` | lệnh đèn theo tuyến, lịch khảo sát hàng tuần, tạo phiếu | Chỉ đọc `commune_id` / tên / sự tồn tại | n-a — **đây chính là đường FE cần**: lịch công tác theo tuyến chạy không cần hình. Thêm test "tạo lịch hàng tuần + sinh phiếu cho tuyến chưa hình" để khoá lại |
| 20 | `Map/PoleDetailService` `:53` | `GET /map/poles/{id}` | Chỉ lấy `segment_name` | n-a |
| 21 | `Citizen/PublicReportService` `:82` | tên tuyến ở QR | Chỉ tên | n-a |
| 22 | `Faults`, `Statistics`, `Telemetry` còn lại | — | Không có truy vấn nào đọc `road_segment.geom`; `LengthM` chỉ ra ở `properties.length_m` của #1/#2 | n-a |

Không đọc hình tuyến: `scripts/seed_mock_set.py` (chỉ INSERT, có hình), `scripts/copy_dev_to_supabase.py` (không thêm bảng nên không phải sửa `PLAN`).

**Tổng:** 22 hàng; **8 chỗ sẽ 500** nếu không sửa (#2, #3, #5, #7, #10, #11, #12, #13; thêm #17 nếu chốt #14 hổng); **3 chỗ rơi lặng lẽ có chủ ý** (#1, #15, #16) trong đó #16 là chỗ nguy hiểm
nhất — coverage trông đủ khi cả tuyến không được khảo sát — nên phải có chốt chặn #14 ở trên và test canh nó.

## 3. Hai ràng buộc hiện có tương tác với tuyến chưa hình

1. **ASSET-SEG-2 (#224)** — đo hành lang chỉ khi hình **đổi**. Gắn hình lần đầu là "đổi" (null → có), nên **chạy** và đo mọi cột có GPS của tuyến;
   cột chưa GPS bị bỏ qua sẵn (`pole.Geom != null`). Hệ quả vận hành: **kỹ sư gắn GPS cột trước, vẽ tuyến sau** ⇒ nếu GPS lệch >25 m, lỗi
   nổi ở lúc vẽ tuyến (409 + danh sách `pole_ids`), không ở lúc gắn GPS (RG-11).
2. **POLE-GEOM-NULL G-2** — `PUT …/location` đo hành lang tới tuyến của cột. Với tuyến chưa hình **không có gì để đo**: RG-11 nói bỏ qua
   (khuôn D-5/G-2: cột chưa tuyến không đo). Phương án chặn (409 tới khi tuyến có hình) làm kẹt đúng quy trình thực địa FE mô tả.
3. Ghi chú ngoài phạm vi (đã có từ ASSET-SEGMENT-CORRIDOR, **không sửa ở đây**): `pole_ids` thêm cột vào tuyến **đã có hình** mà không đổi hình thì
   không đo hành lang (`rerouted = false`). Giữ nguyên.

## 4. Migration (đề xuất — chưa tạo)

`Up()`: một `AlterColumn<LineString>` trên `road_segment.geom`, `nullable: true` (đúng dạng `20261110135346_PoleGeomNullable`). Cần kiểm
migration sinh ra **không** có `DropIndex` / `CreateIndex` nào trên `ix_road_segment_geom`, và snapshot chỉ đổi cột này.
`Down()`: preflight `SELECT count(*) FROM road_segment WHERE geom IS NULL` ⇒ `RAISE EXCEPTION` có thông điệp (khuôn `PoleGeomNullable`), rồi
`ALTER TABLE road_segment ALTER COLUMN geom SET NOT NULL`. Không điền hình giả để cho rollback chạy.

## 5. Phương án thay thế đã cân nhắc (CLAUDE.md mục 9)

| Phương án | Vì sao không chọn |
|---|---|
| **Hình giả** (đường thẳng nối cột đầu–cuối, tâm xã) để giữ cột `NOT NULL` | Đúng loại dữ liệu bịa mà G-1 của POLE-GEOM-NULL cấm; thứ tự cột, hành lang 25 m, độ phủ khảo sát đều tính trên hình giả ⇒ số liệu nghiên cứu sai, không phát hiện được |
| **Bảng riêng** `road_segment_draft` cho tuyến chưa hình | Phiếu, lịch định kỳ, `pole.segment_id`, `work_order_segment` đều FK tới `road_segment` ⇒ phải đổi hết; thêm một chuyến "thăng cấp" dữ liệu |
| **Cột trạng thái** `has_geometry` lưu | Lưu một sự thật thứ hai cạnh `geom IS NULL` — hai nơi lệch nhau. Tính lúc đọc (như `FeederListItem.HasGeometry`) |
| **Cho phép đưa tuyến đã có hình về chưa hình** | Nhiều chốt chặn dựa vào "một chiều" (RG-2: coverage, hành lang, sweep đang chạy). Cần thì xoá tuyến tạo lại — hiếm |
| **`length_m` nullable** | Đổi `properties.length_m` đã publish trên bản đồ / bundle và mọi thống kê. Sổ sách vốn ghi chiều dài; giữ bắt buộc |

## 6. D-item cần Mỹ chốt trước Phase 2

Cột "Đề xuất" là đề xuất của Claude; **Mỹ duyệt toàn bộ RG-1…RG-15 đúng như đề xuất ngày 11/11/2026**. (Ba yêu cầu của FE — nới `geom_wkt` ở tạo / sửa / import, vẫn gán `pole_ids`,
bỏ qua tuyến chưa hình ở bản đồ / thứ tự / khảo sát — là đầu vào, RG-1…RG-13 là cách hiện thực.)

| Mã | Câu hỏi | Đề xuất |
|---|---|---|
| **RG-1** | Ý nghĩa của `road_segment.geom IS NULL`, và có hình giả không | Null = **tuyến mới có trong sổ sách, chưa có hình**. Không bao giờ điền hình giả / suy hình từ cột |
| **RG-2** | Tuyến đã có hình có quay về chưa hình được không; ngữ nghĩa vắng / `null` trên PUT và import | **Không** (khuôn GN-2). `PUT /assets/segments/{id}`: tuyến chưa hình gửi `geom_wkt` vắng / `null` ⇒ giữ chưa hình; gửi WKT ⇒ gắn hình lần đầu. Tuyến đã có hình gửi vắng / `null` ⇒ **400** như hôm nay (không lặng lẽ giữ hình, không xoá). `POST`: vắng / `null` ⇒ chưa hình; chuỗi trắng / WKT sai ⇒ 400. Import: ô trống ở tuyến MỚI ⇒ chưa hình; ở tuyến ĐÃ CÓ ⇒ giữ hình; giá trị sai vẫn lỗi dòng. **Hệ quả cho WP5:** form sửa tuyến đã vẽ phải luôn gửi lại `geom_wkt` |
| **RG-3** | `length_m` khi chưa có hình | **Vẫn bắt buộc, vẫn khai báo** (`≥ 1`); FE nhập chiều dài theo sổ sách. Khi tuyến nhận hình lần đầu, `length_m` **không** bị tính lại (bẫy 4) — FE gửi lại giá trị đo nếu muốn đổi |
| **RG-4** | Tuyến chưa hình có vào `GET /map/segments` không | **Không.** bbox không chọn được tuyến không có vị trí; FE lấy danh sách chờ vẽ từ `GET /assets/segments`. Cách kia (đặt `geometry: null` vào bbox-response bằng điều kiện "luôn trả") phá luật "bbox bắt buộc, quá 2000 → 413" |
| **RG-5** | FE nhận biết tuyến chưa hình bằng cách nào | `SegmentListItem.has_geometry` (bool, tính lúc đọc) + `SegmentDetail.geom_wkt` thành nullable + bộ lọc `GET /assets/segments?missing_geometry=true` (lọc trước tổng và phân trang; `false` = không lọc; khuôn `missing_location`) |
| **RG-6** | `GET /sync/bundle`: tuyến chưa hình của phiếu có vào gói không | **Có**, Feature `geometry: null` — kỹ sư cần thấy tuyến và cột của phiếu; WP6 phải xử lý `geometry: null` cho segment (cột đã xử lý từ GN-4) |
| **RG-7** | Khảo sát đêm với tuyến chưa hình | **Tạo / giao / lên lịch** phiếu khảo sát và lịch tuần cho tuyến chưa hình **được** (yêu cầu FE). **Bắt đầu ghi sweep** (`POST /sweeps`) trên phiếu có tuyến chưa hình ⇒ **409 `SEGMENT_GEOMETRY_REQUIRED`**, `details {work_order_id, segment_ids}`. Phiếu hỗn hợp (một tuyến có hình, một tuyến không) cũng bị chặn — Quản lý vẽ nốt tuyến hoặc tách phiếu. Cách kia (nhận sweep, chỉ xử lý tuyến có hình) tạo coverage đủ giả (#16) và bỏ lặng lẽ một tuyến |
| **RG-8** | `GET /work-orders/{id}/poles` với cột thuộc tuyến chưa hình | Vẫn hiện; `position: null`; `location` theo cột (có thể có GPS); xếp nhóm cuối của tuyến theo `created_at, length(pole_id), pole_id`. `position` là thứ tự dọc đường (contract) nên không bịa khi không có đường |
| **RG-9** | `GET /work-orders/agenda` với nhóm tuyến chưa hình | Nhóm giữ `segment_id` / `segment_name`; `location: null`, `distance_m: null`; xếp sau các nhóm đo được khoảng cách. Không đổi schema (các field đã nullable) |
| **RG-10** | Sơ đồ nhánh tủ điện gặp cột trên tuyến chưa hình | **Bỏ qua + đếm `unrouted_pole_count`** (trường MỚI, tách khỏi `unlocated_pole_count` = cột chưa GPS). Cách kia — gộp vào `unlocated_pole_count` — không đổi API nhưng FE sẽ hiện "cột chưa có GPS" sai sự thật. Không 409 (cùng lý do GN-5: trạng thái sổ sách kéo dài) |
| **RG-11** | Gắn GPS cột có tuyến chưa hình | **Không đo hành lang** (không có gì để đo), 200, audit `segment_distance_m: null`. Lưới an toàn: lúc tuyến nhận hình lần đầu, ASSET-SEG-2 đo mọi cột có GPS của tuyến (mục 3.1). Hệ quả cho FE: 409 `POLE_OFF_SEGMENT` có thể xuất hiện ở bước **vẽ tuyến**, kèm `pole_ids` |
| **RG-12** | Báo `has_geometry`/`segments` cho mobile ở các luồng khác | n-a — chỉ bundle (RG-6). Không đổi `GET /work-orders/{id}` |
| **RG-13** | Gắn `pole_ids` vào tuyến chưa hình | Cho phép, không đo hành lang (hành lang đo lúc có hình, RG-11) |
| **RG-14** | Rollback ứng dụng khi đã có tuyến chưa hình | `Down()` từ chối. **Cả binary cũ cũng không chạy được** với hàng `geom IS NULL` (`required LineString`): trước khi triển khai lại binary cũ phải vẽ hoặc xoá hết tuyến chưa hình. Ghi vào runbook |
| **RG-15** | Chia PR | **2a:** migration + `Geom` nullable + mọi chỗ đọc #2, #3, #5, #7, #10–#13 + chốt #14 + `has_geometry`; POST/PUT/import **vẫn bắt buộc** `geom_wkt` (khuôn POLE-GEOM-NULL — bảo vệ consumer trước). **2b:** bật writer (POST/PUT/import, RG-2), `missing_geometry`, OpenAPI sinh lại, template, trap, báo WP5/WP6. Không stack PR |

## 7. Rủi ro và phối hợp

- **WP5** (model nullable `geom_wkt`; `has_geometry`; `missing_geometry`; form sửa tuyến phải gửi lại hình; `unrouted_pole_count`; mã 409 mới; vẽ tuyến có thể 409 `POLE_OFF_SEGMENT`),
  **WP6** (bundle `geometry: null` cho **segment**; không bắt đầu sweep được trên tuyến chưa hình). WP4 không đổi. **Chưa báo.**
- `feat/SURVEY-segment-outage` (b4f864a, chưa merge) sửa `SurveyReviewService` / `SurveyProcessor`: cùng vùng với #14, #17; PR sau rebase lên `dev`, không stack.
- #229 (`docs/tracking-sync-1111`) cùng sửa `tracking.html`: xung đột văn bản dự kiến, giải khi merge.
- `luxmap-v1.json` đổi ở 2a/2b ⇒ cần CODEOWNERS.
- Phase 1 **không** chạm `luxmap-v1.json`, Contract, hay mock.

## 8. Kế hoạch kiểm thử (cho Phase 2)

Test trồng tuyến `geom IS NULL` thẳng vào DB (không qua API ở 2a), một test mỗi hàng 1, 2, 3, 4, 5, 7, 10, 11, 12, 13, 14, 16 của bảng mục 2; đọc mã thoát của `dotnet test`;
chạy trên Testcontainers (`LuxMapTestHost`), không bao giờ `luxmap_dev`. `EXPLAIN` của `GET /map/segments` vẫn đi qua `ix_road_segment_geom` (`MapQueryPlanTests`).
Phá thử: bỏ chốt #14 ⇒ test sweep đỏ; bỏ `Geom != null` ở #13 ⇒ agenda đỏ; ép `rerouted=false` khi hình cũ null ⇒ test hành lang lúc vẽ đầu đỏ; bỏ preflight `Down()` ⇒ test migration đỏ.
Class ghi tài sản nằm trong `AssetDatabaseCollection`; tiền tố riêng; teardown theo FK `Restrict`.
