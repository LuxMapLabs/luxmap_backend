---
ticket: CABINET
title: Trụ / tủ điện tổng thành tài sản riêng (`electrical_cabinet`); IoT node chỉ là thiết bị gắn ở trụ
status: draft
phase: 1
owner: claude
branch: (chưa tạo — feat/CABINET-electrical-cabinet)
contract_refs:
  - "§0.2 — bảng prefix ID (prefix mới)"
  - "§5.3 / §5.3.1 — nhóm /assets (tài nguyên mới, feeder thêm trường)"
  - "§5.6 — /map/iot-nodes (nguồn toạ độ đổi, hình dạng giữ)"
depends_on:
  - "FW xác nhận — chạm bề mặt API (FW-00 mục 3)"
---

## Bối cảnh

FE hỏi (07/10/2026) vì sao `GET /assets/feeders` trả `has_geometry` mà không trả toạ độ. Gốc rễ:
`feeder.geom` là **LineString tuyến cáp** (gần như luôn null), còn thứ người dùng muốn thấy là **vị trí
trụ / tủ điện tổng** — một điểm. Hiện toạ độ đó chỉ nằm ở `iot_node.geom`, nên **trụ không gắn IoT
không có chỗ lưu**. Nhóm đang đặt tên feeder theo tủ (`FDR-005 "Tủ A - Huỳnh Văn Cọ"`, `external_ref
CAB-HVC-A`) để bù.

Mỹ chốt 07/10/2026: không phải trụ nào cũng gắn IoT (IoT lắp theo yêu cầu của địa phương) ⇒ trụ là
**tài sản**, IoT node là **thiết bị gắn lên trụ**. Cùng quan hệ `Pole` / `Fixture` của BE-09. Đây là
**Q10** của `docs/database/erd.md`, với hai điểm sửa (D-2: `iot_node.cabinet_id` bắt buộc, không
nullable như Q10 ghi; `iot_node.geom` bỏ).

Phiếu v1.4 dòng 197/215/239/349 liệt kê *electrical cabinets* là tài sản được quản lý.

## Quyết định — Mỹ chốt 07/10/2026 (đồng ý 5 đề xuất mặc định)

| Mã | Quyết định |
|---|---|
| **D-1** | `electrical_cabinet.geom` **bắt buộc** — `geometry(Point,4326)`, GIST. Trụ là thứ nhìn thấy, đứng ra chụp được |
| **D-2** | **Bỏ `iot_node.geom`**; toạ độ thiết bị = toạ độ trụ, tính lúc đọc. `iot_node.cabinet_id` **bắt buộc**. `/map/iot-nodes` giữ nguyên hình dạng (điểm vẫn là điểm của trụ) |
| **D-3** | Một trụ tối đa **một** thiết bị (testbed: 1) |
| **D-4** | Rơ-le của thiết bị chỉ điều khiển feeder **thuộc cùng trụ** (`feeder.cabinet_id = iot_node.cabinet_id`) — **DB + service** (sửa sau review, xem dưới) |
| **D-6** | Thiết bị **không** gắn lên trụ `data_source = field` (D-R10) — **DB + service** (thêm sau review). ⚠️ **Mỹ chưa duyệt riêng** — Codex đề xuất, Claude áp |
| **D-5** | Prefix ID: đề xuất `CAB`, độ rộng 3 (`CAB-001`), sequence `cabinet_id_seq` |

## Lược đồ đề xuất

```
electrical_cabinet                       ← MỚI, module Assets
  cabinet_id     text PK  DEFAULT luxmap_format_id('CAB', nextval('cabinet_id_seq'), 3)
  cabinet_name   text NOT NULL
  commune_id     text NOT NULL  → administrative_unit (Restrict, HasCommuneReference), ICommuneScoped
  external_ref   text NULL      partial unique (commune_id, external_ref) — khuôn HasExternalRef
                                + HasIndex("CommuneId") tường minh (quy ước partial index)
  geom           geometry(Point,4326) NOT NULL, GIST
  data_source    text NOT NULL  (field | public_imagery | calibration_rig | simulated)
  created_at, updated_at, updated_by   IUpdateStamped (khuôn N-4, AssetStamp.Touch)
  ak (cabinet_id, commune_id)   ← đích FK ghép, trông thừa, KHÔNG thừa (khuôn O-7)

feeder.cabinet_id    text NULL   FK ghép (cabinet_id, commune_id) → electrical_cabinet, MATCH SIMPLE, Restrict
iot_node.cabinet_id  text NOT NULL FK ghép (cabinet_id, commune_id) → electrical_cabinet, Restrict
                     UNIQUE (cabinet_id)                          ← D-3
iot_node.geom        DROP (+ ix_iot_node_geom)                    ← D-2
```

Chuỗi sau thay đổi: `trụ → feeder → pole.feeder_id` và `trụ → iot_node → feeder_control(rơ-le) → feeder`.

**D-3 — vì sao UNIQUE đầy đủ, không partial như `ux_fixture_pole_id_active`:** `iot_node` hiện **không có**
khái niệm ngừng dùng (`removed_at`). Partial index cần một điều kiện "đang hoạt động" để lọc. Thay thiết bị
= ticket sau thêm `removed_at` rồi đổi sang partial.

**D-4 — thực thi ở HAI lớp (sửa sau review Codex 07/10, R-1).** Bản đầu ghi "`feeder.cabinet_id` nullable ⇒
MATCH SIMPLE bỏ qua, DB không chặn được" — **sai**: MATCH SIMPLE xét NULL ở **phía tham chiếu**. Đặt
`feeder_control.cabinet_id NOT NULL` thì một feeder có `cabinet_id` NULL không bao giờ khớp FK.

```
feeder_control.cabinet_id  text NOT NULL
  FK (feeder_id, cabinet_id) → feeder   UNIQUE (feeder_id, cabinet_id)    ← đích FK, trông thừa, KHÔNG thừa
  FK (node_id,   cabinet_id) → iot_node UNIQUE (node_id, cabinet_id)
  (giữ nguyên hai FK ghép theo xã hiện có, IotConfigurations.cs:68–76)
```

🔴 **Unique đích + FK này chỉ ở DB, KHÔNG khai `HasAlternateKey` / `HasPrincipalKey` trong EF.** Alternate key biến
`Feeder.CabinetId` / `IotNode.CabinetId` thành key property ⇒ EF cấm sửa trên entity đang track (cùng bẫy
`Feeder.CommuneId` của O-7), và EF không cho alternate key trên cột nullable. Viết bằng `migrationBuilder.Sql` kèm
`Down()` đối xứng; ghi vào `CLAUDE.md` vì snapshot EF sẽ không biết chúng tồn tại.

Service vẫn kiểm trước (409 có thông điệp) — kể cả lượt **đổi trụ của feeder / node** đang có `feeder_control`
(FK sẽ từ chối bằng 500 nếu service quên). Lý do cần lớp DB: hôm nay **writer duy nhất của `iot_node` /
`feeder_control` là SQL thô** (`seed_mock_set.py`), không qua service nào.

**D-6 — thiết bị không gắn lên trụ `field` (thêm sau review, R-2).** `ck_iot_node_data_source_not_field` chỉ kiểm
nguồn của **node**; trụ `field` + node `simulated` cùng xã vẫn lọt, tức D-R10 (không lắp thiết bị ngoài thực địa) bị
vượt qua bằng đường gắn trụ. Backstop DB cùng khuôn:

```
iot_node.cabinet_data_source  text NOT NULL  CHECK (cabinet_data_source <> 'field')
  FK (cabinet_id, cabinet_data_source) → electrical_cabinet   UNIQUE (cabinet_id, data_source)
```

Không ép `node.data_source = cabinet.data_source` — thiết bị `simulated` trên trụ testbed `calibration_rig` là hợp lệ.
Hệ quả: sửa `data_source` của trụ đang mang thiết bị sang `field` bị FK từ chối — service (CRUD + import trụ) phải
kiểm trước để ra 409/lỗi theo dòng. Cùng ràng buộc "chỉ DB, không EF key" như D-4.

## Bề mặt API đề xuất

| Endpoint | Ghi chú |
|---|---|
| `GET/POST /assets/cabinets`, `GET/PUT/DELETE /assets/cabinets/{id}` | Khuôn BE-12a/12b: `ManageAssets` ghi, `ReadNetwork` đọc; `commune_id` từ body + `Narrow`; PUT thay thế toàn phần, **không** đổi `commune_id`; DELETE để FK quyết (409 `ASSET_IN_USE`) |
| `POST /assets/import/cabinets` | Upsert theo `(commune_id, external_ref)`. Thứ tự nạp: **cabinets → feeders** (feeder tham chiếu `cabinet_external_ref`) |
| `GET /assets/feeders` item | Thêm `cabinet: {cabinet_id, cabinet_name, location{lat,lng}} \| null` — tiền lệ `active_fixture` trong list cột (§5.3.1: một cờ sẽ ép gọi thêm một request mỗi dòng). **Đây là câu trả lời cho câu hỏi của FE** |
| `POST/PUT /assets/feeders` | Thêm `cabinet_id?`. **PUT: vắng = GIỮ, `null` = tháo, chuỗi = đổi** — ngoại lệ thứ hai của thay thế toàn phần, cùng khuôn `note` của cột (R-3): form feeder hiện tại chưa có ô trụ, coi vắng là xoá thì đổi tên feeder sẽ tháo trụ (hoặc nhận 409 vì D-4). Đọc bằng `JsonElement` + thêm vào `JsonElementFieldSchemaFilter`. `cabinet` emit ở cả list lẫn detail qua cùng projection |
| `GET /map/cabinets` | `bbox` bắt buộc, `FeatureCollection<Point>`; properties `cabinet_id, cabinet_name, commune_id, feeder_ids[], node_id\|null` (tính lúc đọc). Trụ không IoT cũng hiện |
| `GET /map/iot-nodes` | Hình dạng **giữ nguyên**; geometry lấy từ trụ. Lọc bbox chạy trên `electrical_cabinet.geom` (GIST) rồi join |

## Migration

1. Tạo `cabinet_id_seq`, bảng `electrical_cabinet`.
2. **Backfill:** mỗi `iot_node` hiện có → một trụ (`geom` = `node.geom`, `commune_id`, `data_source` = của node,
   `cabinet_name` = `'Tủ ' || node_id`). Gán `iot_node.cabinet_id`; gán `feeder.cabinet_id` cho feeder đang được
   node đó điều khiển (qua `feeder_control`).
3. `iot_node.cabinet_id` SET NOT NULL, UNIQUE; `cabinet_data_source` backfill + NOT NULL; `feeder_control.cabinet_id`
   backfill từ node; dựng các unique đích + FK của D-4 / D-6; DROP `iot_node.geom` + `ix_iot_node_geom`.
4. `Down()` (R-4), đúng thứ tự: thêm `iot_node.geom` **nullable** → backfill từ trụ → SET NOT NULL → dựng lại GIST
   `ix_iot_node_geom` → bỏ FK/unique/cột của D-4, D-6 → bỏ `feeder.cabinet_id`, `iot_node.cabinet_id` → drop bảng +
   `cabinet_id_seq`. Rollback trả vị trí **hiện tại** của trụ, không phải vị trí trước `Up()`; trụ không có node **mất**.

**Phase 2 phải `EXPLAIN` truy vấn `/map/iot-nodes` mới** (`ST_Intersects(cabinet.geom, envelope)` sau join) bằng SQL EF
sinh ra, khuôn `MapQueryPlanTests` — review tĩnh không xác nhận được planner còn đi GIST. Giữ lọc `node.DataSource`
(`MapQueryService.cs:271`).

## Kéo theo (cùng PR)

`PrefixedIds` + `PrefixedIdOrderingTests` · `copy_dev_to_supabase.py` `PLAN` · teardown `AssetSchemaFixture` /
`AssetImportFixture` (xoá `iot_node` → `feeder` → `electrical_cabinet`, Restrict) · `seed_mock_set.py` sinh trụ cho
3 node mock · template import + `docs/templates/README.md` · `gen_consolidated_spec.py` · drift + Contract ·
helper test `IotNodeTests.cs:311/324/355` (tạo trụ trước, gán node + feeder cùng trụ) + test cho D-4, D-6 bằng **SQL thô**
(không qua service, để chứng minh lớp DB) · `docs/database/erd.md` Q10 sửa **ba** điểm (R-5): `iot_node.cabinet_id`
bắt buộc, `geom` của trụ bắt buộc (dòng 70), quan hệ trụ ↔ node `0..1` (dòng 268) · `CLAUDE.md`: unique/FK chỉ ở DB.

## Review

Codex (gpt-6.1-sol, 07/10/2026, đọc tĩnh, 98k token) — `.ai/reviews/CABINET-by-codex.md`. 5 phát hiện, Claude đã đối
chiếu code, **cả 5 đúng, đều đã áp** ở trên: R-1 (P1, D-4 hiểu sai MATCH SIMPLE), R-2 (D-6), R-3 (PUT feeder), R-4
(`Down()`), R-5 (kéo theo + ERD). D-1, D-2, D-3, D-5: đồng ý.

## KHÔNG ĐƯỢC làm

- Không viết code/migration trước khi FW xác nhận phần chạm API (hoặc Mỹ nói rõ làm trước trên nền tạm).
- Không bỏ `feeder.geom` (LineString) — vẫn hợp lệ, chỉ không ai dùng.
- Không thêm điều khiển ON/OFF qua trụ — điều khiển vẫn theo feeder / rơ-le (I-12/I-13).
- Không cho thiết bị IoT gắn vào trụ `field` (D-R10).
- Không đổi hình dạng `/map/iot-nodes`.
