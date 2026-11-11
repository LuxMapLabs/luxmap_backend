---
ticket: ROAD-SEGMENT-GEOM-NULL
title: Tuyến đường (road_segment) được phép chưa có hình — `road_segment.geom` nullable, vẽ hình sau khi có GPS
status: phase2a_done
phase: 2
owner: claude (P1, PR 2a; PR 2b chưa bắt đầu)
branch: docs/ROAD-SEGMENT-GEOM-NULL-p1
contract_refs:
  - "docs/api-contract-v1.1.md — POST / PUT /assets/segments (geom_wkt), SegmentDetail.geom_wkt, import segments, GET /map/segments, GET /map/cabinets/{id}/topology, GET /work-orders/{id}/poles, GET /work-orders/agenda, GET /sync/bundle, POST /sweeps"
  - "docs/openapi/luxmap-v1.5.json (file SINH)"
depends_on:
  - "POLE-SEGMENT-NULL (đã merge 03/11/2026) — cột chưa tuyến"
  - "POLE-GEOM-NULL (đã merge #225 / #227, 10–11/11/2026) — cột chưa GPS; ticket này đi theo đúng khuôn đó"
  - "ASSET-SEGMENT-POLES (#219) — pole_ids trên segment; ASSET-SEGMENT-CORRIDOR (#224, drift ASSET-SEG-2) — đổi hình không bỏ rơi cột"
  - "SURVEY-RECURRING (#223) — lịch khảo sát hàng tuần theo tuyến"
---

## Bối cảnh

FE (WP5) xin, 11/11/2026: tạo được tuyến đường khi **chưa biết GPS**. Thực tế: xã có tuyến mới và cột mới chỉ có trong sổ sách; họ nhập
tuyến + cột trước, **tạo lịch công tác theo tuyến cho kỹ sư**, kỹ sư ra thực địa mới lấy GPS. Cột đã tạo được khi chưa có GPS
(POLE-GEOM-NULL). Tuyến thì chưa: `RoadSegment.Geom` là `required LineString` (`geometry(LineString,4326) NOT NULL`), `geom_wkt` của
`POST` / `PUT /assets/segments` là `[Required]`, và import tuyến bắt `geom_wkt`.

Ba yêu cầu của FE (đầu vào, chưa phải quyết định):

1. `geom_wkt` của tuyến được null khi **tạo, sửa và import**. Vẫn gán được `pole_ids`.
2. Khi cột có GPS thì cập nhật tuyến bằng `geom_wkt` như hiện tại.
3. Mọi chỗ dùng hình tuyến (bản đồ, thứ tự mạch, khảo sát) **bỏ qua** tuyến chưa hình. Phiếu khảo sát / giao việc theo `segment_ids`, và
   lịch hàng tuần, **phải vẫn hoạt động** với tuyến chưa hình.

Rủi ro chính không phải việc đổi cột mà là **mọi chỗ đọc hình**: C# đọc `.Coordinates` / `.StartPoint` / `LengthIndexedLine(null)` là **500**;
toán tử không gian ở SQL trả NULL nên hàng **lặng lẽ rơi** (`traps/assets.md` mục POLE-GEOM-NULL). Chỗ nguy hiểm nhất: khảo sát đêm trên tuyến
chưa hình sẽ cho coverage trông đủ trong khi cả tuyến không được khảo sát — phải có chốt chặn tường minh (RG-7).

Consumer bị ảnh hưởng: WP5 (model nullable, `has_geometry`, bộ lọc, mã lỗi, form sửa tuyến), WP6 (`GET /sync/bundle` có segment `geometry: null`,
không bắt đầu sweep được trên tuyến chưa hình). WP4 không đổi.

## Quyết định đã chốt (Mỹ, 11/11/2026) — KHÔNG mở lại

Mỹ duyệt **toàn bộ RG-1…RG-15 đúng như đề xuất** ở `.ai/results/ROAD-SEGMENT-GEOM-NULL-p1.md` mục 6 (nguyên văn từng mục ở đó; tóm tắt ở
`docs/contract-drift.md` mục *ROAD-SEGMENT-GEOM-NULL*). Điểm cần nhớ khi hiện thực: RG-2 (một chiều; PUT tuyến đã có hình gửi vắng / `null` ⇒ 400), RG-3 (`length_m`
vẫn bắt buộc), RG-4 (không vào `/map/segments`), RG-6 (bundle `geometry: null`), RG-7 (409 `SEGMENT_GEOMETRY_REQUIRED` ở `POST /sweeps`), RG-10
(`unrouted_pole_count` tách khỏi `unlocated_pole_count`), RG-11 (gắn GPS cột không đo khi tuyến chưa hình), RG-15 (2a rồi 2b, không stack).

## Phải đọc trước

- `.ai/results/ROAD-SEGMENT-GEOM-NULL-p1.md` — bảng 22 chỗ đọc hình tuyến, phương án thay thế, kế hoạch rollback.
- `.ai/tasks/POLE-GEOM-NULL.md`, `.ai/results/POLE-GEOM-NULL-p1.md`, `-p2a.md`, `-p2b.md` (khuôn).
- `docs/contract-drift.md` mục *POLE-GEOM-NULL*, *ASSET-SEG-1*, *ASSET-SEG-2*.
- `docs/agents/traps/assets.md` (POLE-GEOM-NULL, ASSET-SEGMENT-POLES), `spatial-map.md` (bẫy 4: `length_m` khai báo), `migrations.md`,
  `survey.md`, `faults-workorders.md`.

## Yêu cầu (Phase 2)

Tuần tự như POLE-GEOM-NULL: **bảo vệ consumer trước, bật writer sau.** Không stack PR — PR sau chờ PR trước merge.

1. **PR 2a — schema + mọi chỗ đọc.** Migration bỏ `NOT NULL` của `road_segment.geom` (`Up()` chỉ `AlterColumn`; `Down()` từ chối khi còn
   `geom IS NULL`); `RoadSegment.Geom` thành `LineString?`; sửa mọi chỗ đọc ở bảng khảo sát (mục 2 của kết quả P1); chốt chặn sweep (RG-7);
   `has_geometry`; `unrouted_pole_count`. POST / PUT / import **vẫn bắt buộc** `geom_wkt`. Test trồng tuyến `geom IS NULL` thẳng vào DB.
2. **PR 2b — bật writer.** `POST` / `PUT /assets/segments` và import nhận tuyến chưa hình theo RG-2; `?missing_geometry=`; OpenAPI sinh lại; template
   import; trap; báo WP5 / WP6.

Tiêu chí xong — mỗi dòng kiểm chứng được bằng một lệnh:

- [ ] `dotnet test` xanh (đọc mã thoát), chạy trên Testcontainers, có test cho từng dòng của bảng khảo sát, trồng tuyến chưa hình.
- [ ] Migration: đọc `Up()` / `Down()` TRƯỚC khi apply; `Up()` chỉ `AlterColumn road_segment.geom` nullable, không `DropIndex` / `DropColumn`; `Down()` từ chối khi còn `geom IS NULL`.
- [ ] `EXPLAIN` của `GET /map/segments` vẫn đi qua `ix_road_segment_geom` (`MapQueryPlanTests`).
- [ ] Tạo lịch tuần + sinh phiếu khảo sát cho tuyến chưa hình chạy được; `POST /sweeps` trên phiếu đó → 409 `SEGMENT_GEOMETRY_REQUIRED` (nếu RG-7 chốt).
- [ ] Phá thử mỗi chốt chặn mới (đỏ → khôi phục bằng `cp` / `touch`).
- [ ] `luxmap-v1.json` xuất lại → generator → lint.

## KHÔNG ĐƯỢC làm

- Không hình giả: không nối đường thẳng giữa các cột, không tâm xã, không suy hình từ cột lân cận.
- Không cho tuyến **đã có hình** quay về chưa hình (RG-2, nếu chốt theo đề xuất).
- Không tính lại `length_m` từ hình, không đổi `length_m` thành nullable (RG-3).
- Không thêm / đổi giá trị enum đã publish; không đổi / bỏ field đã publish (chỉ THÊM `has_geometry`, `unrouted_pole_count`, mã lỗi, và nới nullable).
- Không `ExecuteUpdate` / `ExecuteDelete`; mọi FK `Restrict`; không `COALESCE` thay cho chỗ cố ý để rơi (RG-4, #15, #16).
- Không `git push`; không `git add -A` / `git add .`; không sửa `docs/api-contract-v1.1.md` (hợp nhất Contract là việc sau khi FW xác nhận).
- Không chạy test trên `luxmap_dev`.
- Không sửa việc `pole_ids` thêm cột vào tuyến **đã có hình** mà không đo hành lang (ghi nhận ở P1 mục 3.3; ngoài phạm vi).
- Không viết tên index / constraint / cột từ trí nhớ; chép từ migration / snapshot.

## Trạng thái

Phase 1 xong (66466e4), Mỹ duyệt 11/11/2026. **PR 2a code xong** (nhánh `feat/ROAD-SEGMENT-GEOM-NULL-2a`, chưa push; kết quả `.ai/results/ROAD-SEGMENT-GEOM-NULL-p2a.md`). PR 2b (bật writer) chờ 2a merge. Không push khi chưa hỏi.
