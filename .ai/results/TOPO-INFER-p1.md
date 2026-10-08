# TOPO-INFER — Phase 1: quan hệ cột → mạch có nhãn nguồn gốc + sơ đồ nhánh của trụ · 07/10/2026

**Phase 1 = khảo sát + đặc tả. Chưa sửa code, chưa migration.** Mỹ chốt D-item ở cuối rồi mới sang Phase 2.
Nền: phương án A (`.ai/results/FEEDER-SCOPE-impact.md`) + ý của nhóm: suy ra trụ → cột từ quan sát thực địa, **có gắn nhãn,
nói công khai** khi bảo vệ. Chạm schema + API ⇒ SELF-SIGNED, ghi drift khi chốt, nền tạm tới FW.

## 1. Mục tiêu

1. Mỗi quan hệ cột → mạch (`pole.feeder_id`) mang **nguồn gốc**: `verified` (xã / EVN xác nhận, hoặc kiểm bằng video chập
   tối — nhóm đèn bật cùng lúc) hay `inferred` (suy từ vị trí trụ / cột ngoài thực địa). Hai loại không bao giờ trộn khi
   hiển thị hay thống kê — cùng nguyên tắc với `data_source`.
2. FE vẽ được sơ đồ **rẽ nhánh** từ trụ ra các cột bằng **một request**, nối theo thứ tự dọc đường, nhãn suy luận / xác minh hiện ở tooltip
   (TI-7, không phân biệt nét) — **không tự suy gì phía client** (gỡ `useElectricalCascade` chia đều).

## 2. Khảo sát backend (`origin/dev` `8809eb7`)

**Đường GHI `pole.feeder_id` — đúng 4, cả 4 phải mang nhãn:**

| Đường | Chỗ |
|---|---|
| `POST /assets/poles` | `AssetCrudService.CreatePoleAsync` (`:275`) |
| `PUT /assets/poles/{id}` (thay thế toàn phần) | `UpdatePoleAsync` (`:444`) |
| `PUT /assets/poles/{id}/feeder` | `SetPoleFeederAsync` (`:593`) |
| `POST /assets/import/poles` | `AssetImportService.PlanPolesAsync` (`:397`, `:410`) |

Sync (BE-43) và Statistics (BE-28) **không** ghi / đọc `feeder_id`. Script `seed_mock_set.py` ghi `feeder_id` cho 103 cột mock.

**Đường ĐỌC phát `feeder_id`:** `PoleListItem` (`/assets/poles`, `:831`), `TopologyPole` (`/assets/feeders/{id}/poles`,
`/assets/feeders/poles?unassigned=true`, `:754`). Lớp `/map/poles` **không** phát (§5.1).

**Thứ tự dọc đường đã có sẵn:** `WorkOrderPoleService` (`WorkOrderPoles.cs`, WO-12) chiếu cột lên đường của tuyến bằng NTS
`LengthIndexedLine.Project` trên toạ độ lưu, chỉ để xếp thứ tự, không phát khoảng cách; đọc hình học tuyến **bỏ qua query
filter** vì tuyến `inter_commune` có thể thuộc xã khác (chỉ thứ tự rời khỏi hàm). Dùng lại đúng cách này.

**Dữ liệu hiện có (`luxmap_dev`):** 173 cột `field` — 0 có `feeder_id`; 103 cột mock `public_imagery` gắn 3 "Mạch tạm (demo,
chờ O-6)".

## 3. Đặc tả đề xuất

### 3.1 Lược đồ

```
pole.feeder_source  text NULL   CHECK IN ('verified','inferred')
  CHECK ck_pole_feeder_source_matches_feeder: (feeder_id IS NULL) = (feeder_source IS NULL)
```

- Nằm trên **`pole`**, vì quan hệ thuộc về cặp cột–mạch, không thuộc mạch (một mạch có thể có cột đã xác minh lẫn cột suy luận).
- Enum mới **`topology_source : verified | inferred`** — chuỗi thường trên dây, `HasContractEnum` (CHECK sinh tự động).
- Migration: thêm cột nullable → **backfill `inferred`** cho mọi hàng có `feeder_id` (103 cột mock — chưa ai xác minh, không
  bao giờ được tự nhận `verified`) → thêm CHECK. `Down()` drop cột (mất nhãn — chấp nhận, ghi rõ).

### 3.2 Ghi — luật chung cho cả 4 đường (D-3)

| Gửi | `feeder_id` không đổi | `feeder_id` đổi / gắn mới | `feeder_id` = null |
|---|---|---|---|
| `feeder_source` **vắng** | **giữ** nhãn cũ | **`inferred`** | null (tháo mạch luôn xoá nhãn) |
| `"verified"` / `"inferred"` | đặt theo giá trị | đặt theo giá trị | **400** (nhãn không có quan hệ) |
| `null` | **400** (muốn bỏ nhãn thì tháo mạch) | **400** | null |
| kiểu sai (số, object, chuỗi lạ) | **400** | **400** | **400** |

Import: ô trống = "vắng"; giá trị lạ = lỗi theo dòng; GeoJSON `properties.feeder_source` là object / mảng = lỗi theo dòng
(không được rơi về "vắng" như `ImportRow.cs:53` đang làm với ô lạ). `PUT /assets/poles/{id}/feeder` giữ luật hiện có: **khoá
`feeder_id` bắt buộc** (`AssetsController.cs:519`); `feeder_source` tuỳ chọn.

🔴 **Ghi THEO CẶP — sửa sau review Codex (P1).** Mọi đường ghi chạm `feeder_id` hoặc `feeder_source` phải ghi **cả hai cột** trong
cùng câu UPDATE (đánh dấu `IsModified` cho cả hai), kể cả khi một cột không đổi giá trị. Nếu không: A xác minh F1 (chỉ ghi
`feeder_source`), B đồng thời chuyển cột sang F2 (chỉ ghi `feeder_id`) — EF chỉ ghi cột đổi so với snapshot (`AssetStamp.cs:20`),
nên DB thành **`(F2, verified)`**: một cặp không ai từng khẳng định. Ghi theo cặp thì người ghi sau thắng **trọn cặp** — luôn là
một cặp có người khẳng định. (Mất cập nhật kiểu "PUT sau đè PUT trước" vẫn là đặc tính chung của thay thế toàn phần, không riêng
ticket này.) Canh bằng test xen hai lượt ghi.

- Mặc định **`inferred`**: không đường ghi nào được tự nhận `verified` — phải nói ra.
- Vắng mà mạch không đổi thì **giữ**: form cũ chưa có ô nhãn sẽ không hạ `verified` xuống `inferred` mỗi lần sửa cột
  (cùng họ bẫy `note` / `cabinet_id`). Đọc bằng `JsonElement` ở `PUT /assets/poles/{id}` (thay thế toàn phần).
- `PUT /assets/poles/{id}/feeder`: body thêm `feeder_source?` — `{feeder_id, feeder_source?}`.
- Import cột: cột tuỳ chọn `feeder_source`; ô trống theo bảng trên. Giá trị lạ = lỗi theo dòng.
- Quyền: như hiện tại (`ManageAssets` — Quản lý). Không capability mới (D-4).

### 3.3 Đọc

- `PoleListItem` (+ chi tiết) và `TopologyPole`: thêm `feeder_source` (`verified | inferred | null`).
- Lớp `/map/poles`: **không** thêm (§5.1 vẫn cấm `feeder_id` ở đó; quan hệ đi qua endpoint dưới).

### 3.4 `GET /api/v1/map/cabinets/{cabinetId}/topology` — sơ đồ nhánh (D-5, D-6)

`ReadNetwork`. Trụ không thấy được / ngoài phạm vi → `404 ASSET_NOT_FOUND`. Không `bbox` (một trụ, như `/map/poles/{id}`).

Trả **`FeatureCollection` của `LineString` — mỗi feature là MỘT cạnh** của sơ đồ, để FE thả thẳng vào một line layer MapLibre,
kiểu nét theo `feeder_source` bằng data-driven style:

```jsonc
{ "type": "FeatureCollection",
  "features": [
    { "type": "Feature",
      "geometry": { "type": "LineString", "coordinates": [[lngTrụ, latTrụ], [lngCột1, latCột1]] },
      "properties": { "feeder_id": "FDR-005", "segment_id": "SEG-003", "branch": 1, "order": 1,
                      "from_id": "CAB-001", "to_pole_id": "POLE-0101", "feeder_source": "inferred" } },
    { "geometry": { "coordinates": [[…Cột1], […Cột2]] },
      "properties": { …, "order": 2, "from_id": "POLE-0101", "to_pole_id": "POLE-0102", "feeder_source": "verified" } }
  ] }
```

**Thuật toán dựng nhánh** (trong bộ nhớ, sau khi đọc qua query filter):
1. Feeder của trụ (`feeder.cabinet_id`) → cột của từng feeder (`pole.feeder_id`), kèm `segment_id`, toạ độ, `feeder_source`.
2. Nhóm theo `(feeder_id, segment_id)`. Với mỗi nhóm: chiếu **cột và trụ** lên đường của tuyến (`LengthIndexedLine.Project`,
   hình học tuyến đọc bỏ filter như WO-12). Cột có vị trí `<` trụ → nhánh "về đầu tuyến" (sắp **giảm dần**), `≥` trụ → nhánh
   "về cuối tuyến" (sắp **tăng dần**). Mỗi nhánh không rỗng = một `branch`.
3. Mỗi nhánh thành chuỗi cạnh: trụ → **cột đầu tiên theo thứ tự từ điểm chiếu của trụ** → cột kế … `order` = 1, 2, …
   ⚠️ Không phải "cột gần trụ nhất" theo mét: phép chiếu `LengthIndexedLine` trên 4326 là phẳng theo độ, WO-12 chỉ cam kết
   **thứ tự** (`WorkOrderPoles.cs:70,123`); trụ xa tuyến vẫn được chiếu lên tuyến. Hoà vị trí → `length(pole_id), pole_id`
   (ordinal).
4. Nhãn của cạnh (`feeder_source`): cạnh cột → cột lấy nhãn của **cột ở đầu đến**; cạnh đầu (trụ → cột 1) lấy nhãn **thấp hơn**
   của `feeder.cabinet_source` và nhãn cột 1 (`inferred` nếu một trong hai suy luận — D-10). Nghĩa: nhãn cột = **cột đó thuộc
   mạch đó**; nó **không** chứng minh cạnh cột trước → cột này là đường dây thật (Codex P2).
5. **Thứ tự và đánh số tất định:** feeder theo `created_at, length(id), id`; trong feeder, segment theo `created_at, length(id),
   id`; trong segment, phía **về đầu tuyến trước**, phía **về cuối tuyến sau**. `branch` đánh số **toàn cục** 1, 2, … theo đúng
   thứ tự đó (không phụ thuộc thứ tự `GroupBy`); `order` đánh lại từ 1 trong mỗi branch. Mọi so sánh chuỗi ordinal.

Hệ quả cần biết (ghi vào XML doc): cột của một mạch nằm trên tuyến **khác** tuyến trụ đứng vẫn thành nhánh riêng — cạnh đầu
nối thẳng từ trụ tới cột đầu tiên theo thứ tự của tuyến đó, đúng nghĩa "sơ đồ logic, không phải tuyến cáp". Trụ không mạch / mạch không cột
→ `features: []`, 200.

**Không phát khoảng cách**, không phát hình học tuyến — chỉ toạ độ trụ và cột (4326) vốn đã công khai ở các lớp khác.

### 3.5 Tuỳ chọn — chặn trộn thực địa với mô hình (D-7)

Ý: cột `field` không được nằm dưới trụ không-`field` (và ngược lại). **Sửa sau review Codex (P1): kiểm lúc gắn cột là CHƯA ĐỦ.**
Bất biến bị phá qua ba đường khác nhau, và cả ba đều đang hợp lệ:

| Đường | Ví dụ |
|---|---|
| Gắn cột vào mạch | cột `field` → mạch thuộc trụ testbed |
| Gắn / chuyển **mạch** vào trụ (`PUT /assets/feeders/{id}`, import feeder) | cột `field` → mạch chưa có trụ → mạch gắn vào trụ testbed |
| Đổi `data_source` của **trụ** (`PUT /assets/cabinets/{id}`, import cabinet) | trụ `field` có cột `field` → đổi trụ sang `calibration_rig` |

Làm đúng nghĩa là kiểm trạng thái cuối ở **cả ba** (CRUD + import), trong transaction. Đây là luật **cột ↔ trụ**, không khôi phục
CAB-5 đã gỡ. "Không-`field`" gồm cả `public_imagery` (cột mock), không chỉ testbed. **Mỹ chốt: tách ticket riêng** — Phase 2
này không làm.

### 3.6 Nhãn cho quan hệ mạch → trụ (D-10)

`feeder.cabinet_source text NULL CHECK IN ('verified','inferred')`, CHECK `(cabinet_id IS NULL) = (cabinet_source IS NULL)`, cùng
enum `topology_source`. Cùng luật bảng 3.2 (thay `feeder_id` bằng `cabinet_id`) ở `POST` / `PUT /assets/feeders` (PUT: `cabinet_id`
vắng = giữ cả cặp, CAB-6) và import feeder (`cabinet_source` tuỳ chọn). Ghi theo cặp. Đọc: `FeederListItem.cabinet` thêm
`cabinet_source`. Backfill mọi feeder có `cabinet_id` → `inferred`.

## 4. Kéo theo (Phase 2)

Migration + `HasContractEnum`; 4 đường ghi; `JsonElementFieldSchemaFilter` (`UpdatePoleRequest.feeder_source`); template
`poles.csv` + README; `seed_mock_set.py` (ghi `inferred` cho cột mock); `gen_consolidated_spec.py` (SUMMARY endpoint mới);
OpenAPI; `CLAUDE.md`; drift; Contract sau khi FW duyệt; `tracking.html`; báo WP5 (thay `useElectricalCascade`) và WP6 (không đổi).

**Test (P3 của Codex):** bảng 3.2 đủ mọi ô kể cả `null` / kiểu sai / GeoJSON object; CHECK hai chiều bằng SQL thô; **ghi theo cặp**
qua hai lượt xen nhau; sabotage "vắng = hạ nhãn"; chuyển cột sang mạch khác cùng / khác trụ → `inferred`; thứ tự + đánh số
`branch` qua ngưỡng độ rộng ID và khi hoà vị trí; tuyến `inter_commune` của xã khác; 404 ngoài phạm vi; cạnh mang đúng nhãn (cạnh
đầu lấy nhãn thấp hơn).

**Fixture phải sửa** (CHECK mới làm chúng vỡ): mọi chỗ gán `Pole.FeederId` / `Feeder.CabinetId` trực tiếp phải đặt nhãn — ví dụ
`CabinetTests.cs:670`, `IotNodeTests`, `AssetReadShapeTests`, `TopologyQueryTests`; test đọc khoá chính xác
(`AssetReadShapeTests.cs:48`, `CabinetTests`). `JsonElementFieldSchemaFilter` cho `UpdatePoleRequest`, `SetPoleFeederRequest`,
`UpdateFeederRequest`. README template: cột nhãn trong CSV và GeoJSON.

## 5. KHÔNG làm

- Không tự sinh quan hệ suy luận trong backend (script đề xuất là ticket riêng, chỉ xuất danh sách để người duyệt — D-8).
- Không thêm `feeder_id` vào `/map/poles`.
- Không phát độ dài / khoảng cách theo đường.
- Không đổi `fixture_status` theo trạng thái trụ (đó là thứ `useElectricalCascade` đang làm sai).

## 6. D-item — ✅ Mỹ chốt 07/10/2026: đồng ý tất cả đề xuất

| Mã | Câu hỏi | Đề xuất (= quyết định) |
|---|---|---|
| **D-1** | Nhãn nằm ở đâu | Trên **`pole`** (`feeder_source`), không trên `feeder` |
| **D-2** | Giá trị | `verified \| inferred` — chỉ hai. Không thêm `unknown` (đã là `feeder_id = null`) |
| **D-3** | Luật ghi khi vắng nhãn | Bảng 3.2: mạch không đổi → giữ; đổi → `inferred`; không bao giờ tự `verified` |
| **D-4** | Ai được đặt `verified` | Quản lý (`ManageAssets`), như mọi ghi tài sản; không capability mới. ⚠️ `updated_by` / `updated_at` là người sửa **bất kỳ** trường nào, **không** phải người xác minh (Codex P2) — ticket này **không** lưu "ai xác minh, lúc nào" |
| **D-5** | Tên + hình dạng endpoint | `GET /map/cabinets/{id}/topology`, `FeatureCollection` cạnh (`LineString`) — thả thẳng vào MapLibre |
| **D-6** | Tách nhánh | Theo `(feeder, segment)`, hai phía của trụ trên tuyến; thứ tự dọc đường kiểu WO-12 |
| **D-7** | Chặn trộn `field` với mô hình | **Tách ticket riêng** — làm đúng phải kiểm ở 3 đường (3.5), ~1–1,5 ngày |
| **D-8** | Script đề xuất quan hệ suy luận | Ticket riêng, sau; chỉ xuất CSV để người duyệt rồi nạp bằng import |
| **D-9** | Backfill | **Mọi hàng có `feeder_id`** (và mọi feeder có `cabinet_id`) → `inferred` |
| **D-10** | Nhãn cho mạch → trụ | **Thêm `feeder.cabinet_source`** (3.6); cạnh đầu mỗi nhánh lấy nhãn thấp hơn |

**Review Codex** (gpt-6.1-sol, 07/10/2026, 89k token): 2 P1 + 5 P2 + 1 P3 — Claude đối chiếu code, **đều đúng, đã áp** ở trên:
ghi theo cặp; D-7 ba đường → tách ticket; nghĩa nhãn + D-10; `null` / kiểu sai; thứ tự tất định; "cột đầu theo thứ tự" thay
"gần nhất"; D-4 không phải audit xác minh; danh sách test / fixture.
