# ROAD-SEGMENT-GEOM-NULL — PR 2a: schema + mọi chỗ đọc (11/11/2026, claude)

Nhánh `feat/ROAD-SEGMENT-GEOM-NULL-2a` (từ `origin/dev` @ 517e739). **Chưa bật writer:** `POST` / `PUT /assets/segments` và import tuyến vẫn bắt buộc
`geom_wkt` (PR 2b). Tuyến chưa hình chỉ tồn tại nếu ghi thẳng vào DB — test của PR này làm đúng vậy. Hai commit đầu của nhánh là tài liệu Phase 1
(`66466e4`, `f0db0f9` cherry-pick) để có chỗ ghi kết quả; chúng vào `dev` bằng PR docs riêng — xem "Chia PR" ở cuối.

## Đã làm

| Mục khảo sát (P1) | Kết quả |
|---|---|
| Migration `20261111135148_RoadSegmentGeomNullable` | `Up()`: một `AlterColumn road_segment.geom` nullable, không Drop/CreateIndex. `Down()`: từ chối khi còn `geom IS NULL`, rồi `SET NOT NULL`. Snapshot chỉ đổi cột này |
| `RoadSegment.Geom` | `LineString?`; bỏ `IsRequired()`; giữ GIST `ix_road_segment_geom` |
| #1 `GET /map/segments` | Giữ rơi (RG-4), có test; `MapQueryPlanTests` vẫn qua index |
| #2 `GET /sync/bundle` | Feature `geometry: null` (RG-6) |
| #3 `GET /assets/segments/{id}` | `SegmentDetail.GeomWkt` thành `string?` (RG-5) |
| #4 `GET /assets/segments` | THÊM `SegmentListItem.has_geometry`, tính lúc đọc (RG-5). `?missing_geometry=` để 2b |
| #5 `PUT /assets/segments/{id}` | `rerouted = old is null \|\| !old.EqualsExact(new)`; vẫn đòi `geom_wkt`. Vẽ lần đầu ⇒ đo hành lang mọi cột có GPS |
| #6, #8 | Không đổi (n-a ở 2a) |
| #7 `PUT /assets/poles/{id}/location` | Tuyến chưa hình ⇒ không đo, 200, audit `segment_distance_m: null` (RG-11) |
| #9 import | Không đổi — vẫn bắt buộc `geom_wkt` (2b) |
| #10 sơ đồ nhánh tủ | Bỏ cột có GPS trên tuyến chưa hình, THÊM `unrouted_pole_count` (RG-10) |
| #11 `GET /work-orders/{id}/poles` | `position: null`, nhóm cuối của tuyến (RG-8) |
| #12 tự giao việc | Bỏ tuyến chưa hình khỏi `lines` |
| #13 `GET /work-orders/agenda` | Tên giữ, `location` / `distance_m` null, xếp sau; lọc `Geom != null` trước `DistanceMeters` (RG-9) |
| #14 `POST /sweeps` | 409 `SEGMENT_GEOMETRY_REQUIRED`, `details {work_order_id, segment_ids}` (RG-7) — `ErrorCodes.SegmentGeometryRequired` |
| #15, #16 `SurveyChainageQuery` | Cố ý không sửa, không `COALESCE`; có test (xem phát hiện 1) |
| #17–#22 | n-a như P1. `SpatialFunctions` ghi chú `segment.geom` nay nullable; mọi `DistanceMeters(segment.Geom, …)` đã rà (3 chỗ: #5 qua `RequirePolesInCorridorAsync` — gọi sau khi vừa lưu hình, #7, #13) |

## Kiểm chứng

- `dotnet build`: 0 lỗi, 0 cảnh báo (hai cảnh báo CS8602 ở test cũ do `Geom` nullable đã sửa).
- Test mới (đều trồng `geom IS NULL` thẳng DB): `SegmentGeomNullableTests` (5: hàng 1/3/4, hàng 5 + hành lang, hàng 7, hàng 10, migration), `WorkOrderUndrawnSegmentTests` (4: hàng 11, 12, 13, lịch tuần + sinh phiếu),
  `SyncTests` (hàng 2), `SurveyIngestTests` (hàng 14, hai ca: chỉ tuyến chưa hình và phiếu hỗn hợp), `SurveyProcessingTests` (hàng 15/16).
- **Phá thử** (mỗi chốt bỏ / ép sai ⇒ test tương ứng đỏ, khôi phục bằng `cp` + `touch`): 10 lần, 10 đỏ —
  bỏ `RequireDrawnSegmentsAsync` (#14) ⇒ 2 test sweep đỏ; bỏ `Geom != null` ở agenda (#13) ⇒ test agenda đỏ; ép `rerouted = old is not null && …` (#5) ⇒ test hành lang lúc vẽ đầu đỏ; bỏ preflight `Down()` ⇒ test migration đỏ;
  bỏ bộ lọc `Geom is not null` ở topology (#10), `lines` của tự giao việc (#12), `lines` của cột trong phiếu (#11) ⇒ mỗi test đỏ; ném lỗi khi segment bundle null (#2) ⇒ test sync đỏ; `has_geometry = true` cứng (#4) ⇒ đỏ.
  Riêng #7: chỉ bỏ `Geom != null` thì test vẫn xanh vì phép ép `(double?)` + `SingleOrDefault` đã chịu được NULL — hai lớp bảo vệ chồng nhau, giữ cả hai; trả `offSegment` về dạng gốc (`SingleAsync` trên `double`) ⇒ test đỏ.
- OpenAPI: xuất lại `luxmap-v1.json` → `gen_consolidated_spec.py` → `luxmap-v1.5.json`; lint hợp lệ, 10 cảnh báo (bằng trước khi sửa). Spec đổi: `CabinetTopology.unrouted_pole_count` (bắt buộc), `SegmentListItem.has_geometry` (bắt buộc), `SegmentDetail.geom_wkt` nullable — cần CODEOWNERS.
- Lượt chạy đầy đủ cuối (`dotnet test`, đọc mã thoát): Shared 421, Persistence 45, Storage 80, Api 1632 qua / 7 bỏ qua / 0 đỏ (mã thoát 0, Testcontainers; `MapQueryPlanTests` vẫn qua `ix_road_segment_geom`).

## Chỗ lệch / phát hiện

1. **P1 dự đoán sai #15.** `SurveyChainageQuery.Gps` trên tuyến chưa hình đứng một mình **không** cho "không gán tuyến": khoảng cách NULL lọt qua bộ lọc ứng viên, mọi mẫu GPS bị gán `segment_id` của tuyến đó với
  `route_distance_m` null, chainage 0. Đứng cạnh tuyến có hình thì NULL xếp cuối. Không đổi quyết định (chốt chặn vẫn là #14) nhưng cơ chế hỏng khác với ghi ở P1; đã sửa ở `survey.md` và drift.
2. `SetPoleLocationAsync` đổi `.SingleAsync()` thành `.SingleOrDefaultAsync()` trên truy vấn đo hành lang: khi lọc bỏ tuyến chưa hình thì không có hàng nào, và `SingleAsync` sẽ ném.
3. **`PrefixedIdOrderingTests` đỏ ở lượt đầy đủ đầu tiên** (lượt chạy riêng thì xanh): nó ghim ngưỡng độ rộng thật `SEG-999` / `SEG-1000`, và `segment_id_seq` của cả bộ test vừa vượt 999 vì PR này thêm
  ~35 tuyến. Bom hẹn giờ có sẵn, không phải lỗi của 2a — đã dời test sang ngưỡng 99999 / 100000 (cùng luật, ngoài tầm sequence), ghi ở `traps/testing.md`. Commit riêng.
4. Không gặp test chập chờn khác.

## Chia PR

Nhánh chứa 2 commit tài liệu Phase 1 ở đáy (cherry-pick từ `docs/ROAD-SEGMENT-GEOM-NULL-p1`) vì các file docs mà 2a phải sửa (`docs/contract-drift.md`, `tracking.html`, `.ai/tasks`, `.ai/results`) chỉ tồn tại ở đó.
Cách gọn: merge PR docs Phase 1 vào `dev` trước, rồi `git rebase origin/dev` nhánh này — hai commit giống hệt sẽ tự rớt, PR 2a còn đúng code + docs 2a. Không stack PR (CLAUDE.md).

## Còn lại (PR 2b — session khác, sau khi 2a merge)

`POST` / `PUT /assets/segments` và import nhận tuyến chưa hình (RG-2), `?missing_geometry=true`, template import, báo WP5 / WP6. `luxmap_dev` / VPS chưa migrate.
