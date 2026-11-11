# POLE-GEOM-NULL — PR 2b: bật API ghi cột chưa có GPS (11/11/2026, claude)

Nhánh `feat/ASSET-pole-geom-writers`, cắt từ `dev` sau khi PR #225 (2a) merge. Không có migration mới.

## Đã làm

| Quyết định | Kết quả |
|---|---|
| GN-2 `POST /assets/poles` | `geom_wkt` vắng / `null` ⇒ cột chưa có GPS. `""`, chuỗi trắng, WKT sai, kiểu không phải Point ⇒ 400 |
| GN-2 `PUT /assets/poles/{id}` | Cột đã có GPS gửi vắng / `null` ⇒ 400 (như cũ). Cột chưa có GPS: vắng / `null` ⇒ giữ chưa có; WKT ⇒ gắn vị trí đầu tiên (không đo hành lang — đó là việc của `PUT …/location`) |
| GN-2 import | `geom_wkt` bỏ khỏi cột bắt buộc của `poles.csv`. Ô trống / thiếu cột / GeoJSON `geometry` vắng hoặc `null` ⇒ cột MỚI chưa có GPS, cột ĐÃ CÓ giữ vị trí. `geometry` là chuỗi / mảng, WKT sai ⇒ lỗi dòng |
| GN-10 | `GET /assets/poles?missing_location=true`; AND với `unassigned_segment`; lọc trước tổng và phân trang |
| Template | `docs/templates/poles.example.csv` thêm dòng chưa GPS; `docs/templates/README.md` thêm mục *Cột chưa có GPS*, sửa dòng bảng cột |
| OpenAPI | `luxmap-v1.json` + `luxmap-v1.5.json` sinh lại; lint 10 cảnh báo (bằng trước) |

## Lỗi có sẵn, sửa kèm

`AssetGeometry.TryReadWkt` chỉ bắt `ArgumentException` / `FormatException` / `InvalidOperationException`; WKT sai số (`POINT(abc)`) làm NTS ném
`ParseException` ⇒ **500** ở POST/PUT/import trên `dev`. Chốt "sai ≠ rỗng" của ticket này dựa vào đường đó nên sửa luôn (3 test đỏ trước khi sửa).

## Kiểm chứng

- ✅ `dotnet build` 0 lỗi, 0 cảnh báo. `dotnet test` mã thoát 0: Api 1602 qua / 7 bỏ qua / 0 đỏ (+ Shared 421, Persistence 45, Storage 80).
- ✅ Test mới `PoleWithoutLocationWriteTests` (6): tạo, sửa, bộ lọc, import CSV, import GeoJSON, và luồng đầy đủ *tạo từ sổ sách → kỹ sư gắn GPS (60 m ngoài hành lang ⇒ 409, 5 m ⇒ 200, `moved_m: null`) → lên bản đồ, rời danh sách chờ*.
- ✅ Phá thử: tắt 400 của PUT cột đã có GPS + bắt import ghi đè vị trí bằng null ⇒ 3 test đỏ; khôi phục ⇒ 6/6 xanh.
- ⚠️ `luxmap_dev` local: đã có `PoleGeomNullable` (do phiên khác áp, cùng `AddFixtureSlot`), không chạm. **VPS dev chưa deploy / migrate.**
- 📌 WP5 / WP6 chưa báo.
