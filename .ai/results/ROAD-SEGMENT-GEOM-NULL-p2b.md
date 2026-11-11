# ROAD-SEGMENT-GEOM-NULL — PR 2b: bật writer (11/11/2026, claude)

Nhánh `feat/ROAD-SEGMENT-GEOM-NULL-2b`, cắt từ `origin/dev` sau khi #230 (2a) merge (`8471bb8`). Không stack. **Migration `RoadSegmentGeomNullable` chưa áp lên `luxmap_dev` / VPS.**

## Đã làm

| Quyết định | Hiện thực |
|---|---|
| RG-2 `POST /assets/segments` | `CreateSegmentRequest.GeomWkt` bỏ `[Required]`; vắng / `null` ⇒ `Geom = null`; trắng, WKT hỏng, không phải LineString, `LINESTRING EMPTY` ⇒ 400 `VALIDATION_FAILED` (qua `Read<LineString>` / `AssetGeometry.TryReadWkt`, không đổi) |
| RG-2 `PUT /assets/segments/{id}` | `UpdateSegmentRequest.GeomWkt` bỏ `[Required]`; luật ở `UpdateSegmentAsync`: tuyến đã có hình gửi vắng / `null` ⇒ 400; tuyến chưa hình gửi vắng / `null` ⇒ giữ chưa hình; gửi WKT ⇒ vẽ lần đầu, `rerouted = true` ⇒ đo hành lang (ASSET-SEG-2). `Read` chạy trước mọi phép gán |
| RG-2 import | `geom_wkt` rời `RequiredColumns(Segments)`; `reader.OptionalGeometry<LineString>` (ô trống / `geometry` vắng hoặc null ⇒ null; sai vẫn lỗi dòng); tuyến mới ⇒ chưa hình, tuyến đã có ⇒ giữ hình (`if (geometry is not null) segment.Geom = geometry`) |
| RG-5 `?missing_geometry=` | `ListSegmentsAsync(…, bool missingGeometry, …)` → một `Expression` đưa vào `ListAsync` (lọc trước `CountAsync` và phân trang), khuôn `missing_location` |
| RG-13 `pole_ids` | Không đổi `ReplaceSegmentPolesAsync`; `rerouted` không bật khi không có hình mới nên không đo hành lang |
| Tài liệu | `docs/templates/segments.example.csv` (+ dòng tuyến chưa hình), `README.md` mục *Tuyến chưa có hình*, XML-doc controller, drift, `traps/assets.md`, `tracking.html` |
| OpenAPI | `luxmap-v1.json` → generator → `luxmap-v1.5.json`; lint hợp lệ, 10 cảnh báo (bằng trước). Đổi: `CreateSegmentRequest` / `UpdateSegmentRequest` `geom_wkt` nullable, rời `required`; thêm query `missing_geometry`. Cần CODEOWNERS |

## Kiểm chứng

- `dotnet build`: 0 lỗi, 0 cảnh báo.
- Test mới: `SegmentWithoutGeometryWriteTests` (7: tạo vắng/null/trắng/sai; `pole_ids` trên tuyến chưa hình ở POST và PUT; PUT không xoá hình + giữ chưa hình + 400; vẽ lần đầu 409 `POLE_OFF_SEGMENT` rồi 204; `missing_geometry`; import CSV; import GeoJSON) và `WorkOrderTests.A_road_created_through_the_api_without_a_line_takes_poles_a_weekly_schedule_and_a_survey_order_and_is_drawn_later` (luồng FE: tạo tuyến chưa hình + `pole_ids` → lịch tuần → phiếu khảo sát → liệt kê cột → vẽ sau).
- **Phá thử** (bỏ / ép sai chốt ⇒ test đỏ, khôi phục bằng `cp` + `touch`): 7 lần, 7 đỏ — bỏ nhánh 400 của PUT tuyến đã vẽ; `rerouted` không tính vẽ lần đầu; bỏ lọc `missing_geometry`; POST luôn đòi hình (5 đỏ); import ô trống ghi đè hình (2 đỏ); import đòi lại cột `geom_wkt`; import ô trống là lỗi dòng (2 đỏ).
- **Một vế KHÔNG có test bắt được:** `geometry is not null &&` trong `rerouted`. Bỏ nó thì tuyến chưa hình nhận `pole_ids` không kèm hình chạy `RequirePolesInCorridorAsync` với đường `null`; SQL trả NULL (không ném) nên đếm 0 — 8/8 test vẫn xanh (đã đo). Giữ vế này để không dựa vào NULL của SQL; ghi ở `traps/assets.md`.
- Lượt `dotnet test` đầy đủ (đọc mã thoát 0, Testcontainers): Shared 421, Persistence 45, Storage 80, Api 1640 qua / 7 bỏ qua / 0 đỏ.

## Chỗ lệch / phát hiện

1. Import **không đo hành lang** khi vẽ lần đầu (và không import nào đo khi đổi hình — chỉ `PUT`). Đây là hành vi hiện có, không phải lệch mới; ghi ở README template, drift và trap để FE không kỳ vọng 409 từ import. Nếu muốn import đo hành lang thì là ticket riêng.
2. Ô `geom_wkt` của `PUT` gửi **chuỗi trắng** ⇒ 400 cho cả tuyến đã có hình lẫn chưa có (blank là "giá trị sai", không phải "vắng").

## Còn lại

Mỹ quyết thời điểm báo WP5 (`geom_wkt` tuỳ chọn ở form tạo tuyến, form sửa tuyến đã vẽ **phải gửi lại** `geom_wkt`, `has_geometry`, `missing_geometry`, `unrouted_pole_count`, 409 `SEGMENT_GEOMETRY_REQUIRED`, 409 `POLE_OFF_SEGMENT` lúc vẽ tuyến) và WP6 (bundle `geometry: null`). Áp migration lên `luxmap_dev` / VPS khi triển khai.
