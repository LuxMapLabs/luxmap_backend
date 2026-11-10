---
ticket: POLE-GEOM-NULL
title: Cột (pole) được phép chưa có GPS — `pole.geom` nullable, gắn GPS lần đầu ở thực địa
status: ready
phase: 1
owner: claude (P1) · chưa giao (P2)
branch: feat/ASSET-pole-without-gps
contract_refs:
  - "docs/api-contract-v1.1.md — POST / PUT /assets/poles (geom_wkt), PoleListItem.location, import poles, PUT /assets/poles/{id}/location, GET /map/poles/{id}, GET /work-orders/{id}/poles, GET /sync/bundle, POST /faults, /public/…"
  - "docs/openapi/luxmap-v1.5.json (file SINH)"
depends_on:
  - "POLE-SEGMENT-NULL (đã merge 03/11/2026) — cột chưa tuyến"
  - "POLE-LOCATION (drift PL-1…PL-5) — endpoint dời cột"
---

## Bối cảnh

Cột đèn của xã lúc đầu thường chỉ có trong sổ sách: biết mã, biết thuộc tuyến nào, chưa ai biết toạ độ. Xã cần nhập
cột trước, kỹ sư ra thực địa sau mới lấy GPS và gắn cho cột. Có cột thì biết GPS ngay từ đầu. Hôm nay `Pole.Geom` là
`required Point` (`geometry(Point,4326) NOT NULL`), `POST /assets/poles` và `poles.csv` bắt buộc `geom_wkt`, nên cột
chưa có toạ độ không vào được hệ thống. Yêu cầu từ FE, 10/11/2026.

Vị trí cột là đầu vào của bản đồ, thứ tự cột trên mạch, thứ tự dọc đường của phiếu, khảo sát đêm, khoảng cách báo cáo QR
và `location` của sự cố. Cột `geom` null sẽ **lỗi 500** (đọc `.X` / `.Y` của null ở C#) hoặc **lặng lẽ rơi mất** (toán tử
không gian trả NULL ở SQL) nếu không xử lý tường minh từng chỗ — đó là rủi ro chính, không phải việc đổi cột.

Consumer bị ảnh hưởng: WP5 (web), WP6 (Android, `GET /sync/bundle`) — qua hình dạng response. WP4 không đổi gì
(cột chưa GPS không vào khảo sát).

## Quyết định đã chốt (Mỹ, 10/11/2026) — KHÔNG mở lại

| Mã | Quyết định |
|---|---|
| G-1 | `pole.geom` nullable = cột chưa có GPS. Không dùng toạ độ giả (tâm xã, đầu tuyến) |
| G-2 | Gắn GPS lần đầu dùng `PUT /assets/poles/{poleId}/location` (capability `EditPoleLocation` sẵn có). **Cột có tuyến: giữ kiểm tra hành lang 25 m** (`POLE_OFF_SEGMENT`) — chặn quét nhầm cột / GPS lệch, và giữ cột trong tầm khảo sát. **Cột chưa tuyến: không đo** (như D-5 của POLE-SEGMENT-NULL) |
| G-3 | `location` của cột là `null` trên các response đang có khi cột chưa GPS; ghi drift, báo WP5 / WP6. Không ẩn cột khỏi danh sách |
| G-4 | Cột chưa GPS **không nhận sự cố theo cột**: `POST /faults` có `pole_id` và báo cáo QR của công dân trả **409 với mã lỗi riêng**. Sự cố giữ cam kết luôn có `location{lat,lng}` |
| G-5 | **QR vẫn cấp** cho cột chưa GPS (kỹ sư quét để gắn GPS). FE công dân hiển thị "cột chưa được cập nhật vị trí, vui lòng báo cho đơn vị quản lý" |
| G-6 | Cột chưa GPS **vẫn gán được tuyến** (sổ sách thường ghi sẵn) |
| G-7 | Bản đồ, thứ tự cột trên mạch, khảo sát đêm **bỏ qua** cột chưa GPS |

## Phải đọc trước

- `.ai/results/POLE-GEOM-NULL-p1.md` — bảng khảo sát từng chỗ đọc vị trí cột và các D-item còn mở.
- `docs/contract-drift.md` mục *POLE-GEOM-NULL*, *POLE-SEGMENT-NULL*, *POLE-LOCATION*.
- `docs/agents/traps/assets.md` (POLE-LOCATION, POLE-SEGMENT-NULL PR 2e), `spatial-map.md`, `migrations.md`,
  `citizen.md`, `faults-workorders.md`.

## Yêu cầu (Phase 2 — sau khi Mỹ chốt D-item còn mở)

Tuần tự như POLE-SEGMENT-NULL: **bảo vệ consumer trước, bật writer sau.** Không stack PR — PR sau chờ PR trước merge.

1. **PR 2a — schema + mọi chỗ đọc.** Migration bỏ `NOT NULL` của `pole.geom`; `Pole.Geom` thành `Point?`; sửa mọi chỗ
   đọc ở bảng khảo sát; chặn sự cố theo cột (G-4). POST / PUT / import **vẫn bắt buộc** `geom_wkt`. Test trồng cột
   `geom IS NULL` thẳng vào DB.
2. **PR 2b — bật writer.** `POST /assets/poles`, `PUT /assets/poles/{id}`, import nhận cột chưa GPS; `PUT …/location`
   gắn GPS lần đầu; bộ lọc cột chưa GPS; OpenAPI, template import, trap, báo WP5 / WP6.

Tiêu chí xong — mỗi dòng kiểm chứng được bằng một lệnh:

- [ ] `dotnet test` xanh (đọc mã thoát), có test cho từng dòng của bảng khảo sát, trồng cột chưa GPS.
- [ ] Migration: `Up()` chỉ `AlterColumn pole.geom` nullable; `Down()` từ chối khi còn `geom IS NULL`.
- [ ] `EXPLAIN` của `GET /map/poles` vẫn đi qua `ix_pole_geom`.
- [ ] Phá thử mỗi chốt chặn mới (đỏ → khôi phục).
- [ ] `luxmap-v1.json` xuất lại → generator → lint.

## KHÔNG ĐƯỢC làm

- Không toạ độ giả; không tự suy GPS từ tuyến hay cột lân cận.
- Không cho cột **đã có GPS** quay về chưa GPS (D-item GN-2).
- Không thêm / đổi giá trị enum đã publish; không `git push`; không `git add -A` / `git add .`.
- Không sửa `docs/api-contract-v1.1.md` (hợp nhất Contract là việc sau khi FW xác nhận).
- Không nới `fault.lat/lng` hay hình dạng `location` của sự cố.
- Không làm dời cột / gắn GPS **offline** qua `/sync/push` (PL-5: ticket sau) — xem GN-8.
- Không viết định danh cột / constraint từ trí nhớ; chép từ migration / snapshot.

## Phase 1 dừng ở đâu

Khảo sát xong thì **dừng hẳn**, commit ticket + kết quả + mục drift (stage theo đường dẫn), chờ Mỹ chốt D-item còn mở.
Không viết code ở phase 1.
