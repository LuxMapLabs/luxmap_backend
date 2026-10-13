# SPEC-NULLABLE — Phase 1 (Claude P1b + Codex P1a, 13/10/2026)

P1a (Codex, WP5/WP6 tiêu thụ thế nào): `.ai/results/SPEC-NULLABLE-p1a-by-codex.md`. Không sửa code, không xuất lại spec.

## 1. Đo trên spec (Claude, worktree tạm — đã xoá)

- Tuỳ chọn toàn cục `UseAllOfToExtendReferenceSchemas()` của Swashbuckle 10.2.3: **88 schema đổi** — bọc mọi `$ref`, kể cả ref không bao
  giờ null. **Loại.** ⚠️ *Đính chính (Phase 2, 13/10):* bản đầu ghi thêm "121 operation đổi" — sai, do script so sánh của Claude so một
  operation với cả path item; con số 88 schema vẫn đúng.
- Thuộc tính `$ref` mà C# khai nullable: **40**, chia theo nơi xuất hiện (tính bằng bao đóng `$ref` từ `requestBody` / `responses`):

| Nhóm | Số | Xử lý đề xuất |
|---|---|---|
| **Response** | **25** (11 object, 14 enum) | **Sửa** — đây là chỗ spec nói sai |
| Request | 9 — `AcceptCitizenReportRequest.{fault_type, severity}`, `ReportFaultRequest.{location, fault_type, severity}`, `CreateSweepRequest.data_source`, `DeviceAckRequest.reported_mode`, `FaultOutcomeRequest.outcome`, `UpdateUserRequest.role` | **Giữ** — nullable trong C# để server tự bắt "thiếu" và trả 400 có thông điệp; client không được gửi `null` |
| `*Feature.properties` | 6 (Pole, SyncPole, Segment, IotNode, Cabinet, CabinetTopologyEdge) | **Giữ** — `Feature<TProperties>` generic, NRT coi tham số generic là nullable, trên dây không bao giờ null. Luật loại: thuộc tính có kiểu khai báo là **tham số generic** |

25 thuộc tính response:

```
object : AgendaWorkOrder.location · WorkOrderAgendaGroup.location · CitizenPhotoItem.ai_hint · CoverageGap.geometry ·
         FeederListItem.cabinet · LuxReadingWithLuminanceResponse.nearest_luminance · PoleListItem.active_fixture ·
         PoleMapDetail.{fixture, iot_node, luminance_baseline} · SweepDetailResponse.latest_run
enum   : CitizenSubmissionItem.photos_dropped · IotNodeRelay.control_mode · LightingCommandItem.reported_mode ·
         PoleListItem.{feeder_source, cabinet_source} · PoleMapStatus.source_channel · PoleProperties.{power_source, fixture_type} ·
         SyncPoleProperties.{power_source, fixture_type} · TopologyPole.feeder_source · WarrantyItem.fixture_type ·
         WorkOrderFaultDetail.inspection_outcome · WorkOrderPole.fixture_type
```

**Thêm — bản hợp nhất làm mất `nullable` của scalar** (Codex soát bản tin): spec xuất từ code khai đúng, nhưng schema viết tay
`S["PoleProperties"]` trong `gen_consolidated_spec.py` bỏ `nullable` ở **`lamp_watt`** và **`install_date`** (bẫy BE-14b). Sửa cùng ticket.

## 2. Consumer (P1a, tóm tắt)

- **WP5** sinh types bằng **script tự viết** `scripts/generate-types.js` (không gọi `openapi-typescript` dù có trong `package.json`), đọc Swagger
  đang chạy. Script chỉ hiểu `$ref` / `enum` / `type`: gặp `allOf` hay `oneOf` → **`any`**. ⇒ **Backend sửa spec một mình làm types WP5 XẤU
  ĐI** (25 trường thành `any`) ở lần sinh lại kế tiếp. WP5 phải sửa script trước (hoặc cùng lúc).
- **WP6** viết DTO **bằng tay** (kotlinx.serialization). Ngoài 5 trường dưới, DTO còn **bắt buộc `PoleDetailFixtureDto.supplier`** mà API không
  trả (đã kiểm `PoleMapFixture`), và nhiều scalar nullable khai không-null — chi tiết cột hỏng khi nối API thật **kể cả cột có bóng**.
  **5 trường `$ref` response đang khai không-null** dù backend có thể trả `null`:
  `PoleDetailDto.fixture`, `.luminanceBaseline`, `currentStatus.sourceChannel`, `PoleFeatureDto.powerSource`, `.fixtureType`.
  🔴 **Đây là lỗi thật, không phụ thuộc spec**: cột không có bóng / chưa có baseline sẽ làm **deserialize thất bại** khi màn chi tiết cột nối
  API thật (hiện còn `FakePoleDetailRepository` nên chưa lộ). Báo WP6 ngay, không chờ ticket này.
- Không chỗ nào ở WP5 đọc thẳng object nullable mà thiếu guard; `GisDrawerPanel` điền giá trị giả khi thiếu bóng (UI nên hiện "chưa có bóng").

## 3. Cách mã hoá (OpenAPI 3.0.4)

OAS 3.0.4: `nullable` **chỉ có tác dụng khi có `type` trong cùng Schema Object**. Đề xuất cho mỗi thuộc tính trong 25:

```json
"fixture": { "type": "object", "nullable": true, "allOf": [ { "$ref": "#/components/schemas/PoleMapFixture" } ] }
"source_channel": { "type": "string", "nullable": true, "allOf": [ { "$ref": "#/components/schemas/SourceChannel" } ] }
```

Không lên OpenAPI 3.1 chỉ vì việc này: script WP5 cũng ra `any` với `oneOf`, WP6 không sinh code — đổi dialect không giúp ai.

## 4. Cơ chế đề xuất (Phase 2)

- `SchemaFilter` đọc **NRT bằng `NullabilityInfoContext`** cho từng thuộc tính (bỏ tham số generic), ghi tên thuộc tính nullable vào một
  extension tạm của schema; `DocumentFilter` chạy sau cùng, tính tập schema **chỉ nằm trong response**, viết lại đúng các thuộc tính đó theo §3,
  rồi xoá extension. Không danh sách chép tay — DTO mới tự được phủ.
- Test: duyệt mọi thuộc tính nullable (NRT) của các kiểu response, khẳng định trong spec xuất ra **không** còn `$ref` trần; và literal cho vài
  trường đã biết (`PoleMapDetail.fixture`, `SweepDetailResponse.latest_run`). Generator `gen_consolidated_spec.py` có schema viết tay đè
  (`S["PoleProperties"]`…) — phải sửa cùng (bẫy BE-14b).

## 5. D-item cần Mỹ chốt

| # | Câu hỏi | Đề xuất |
|---|---|---|
| D-1 | Phạm vi | **25 thuộc tính response** + 2 scalar của `PoleProperties` viết tay trong generator; không request, không `*Feature.properties` |
| D-2 | Mã hoá | `type` + `nullable: true` + `allOf: [$ref]` (đúng OAS 3.0.4); **không** lên 3.1 |
| D-3 | Cơ chế | Filter đọc NRT + lọc schema chỉ-response, không danh sách tay |
| D-4 | Thứ tự với FE | BE làm Phase 2 nhưng **chỉ merge khi WP5 đã sửa `generate-types.js`** (hiểu `allOf` + `nullable` → `T \| null`) — nếu không, lần sinh lại kế tiếp biến 25 trường thành `any` |
| D-5 | Lỗi DTO của WP6 | **Báo ngay** (5 trường thiếu `?`), độc lập với ticket — nằm trong bản tin `.ai/results/handoff-2026-10-13-wp5-wp6.md` |
| D-6 | Báo cáo FE | Gửi danh sách 25 trường + mẫu mã hoá ở §3 cho WP5 / WP6 cùng bản tin |

**Phase 1 dừng ở đây. Chờ Mỹ chốt D-1…D-6.**

## Quyết định — Mỹ, 13/10/2026

Chốt **D-1…D-6 theo cột "Đề xuất"**: 25 thuộc tính response + 2 scalar của `PoleProperties` viết tay; mã hoá `type` + `nullable: true` +
`allOf: [$ref]` (OAS 3.0.4, không lên 3.1); filter đọc NRT, không danh sách tay; Phase 2 **chỉ merge sau khi WP5 sửa `generate-types.js`**;
báo WP6 lỗi DTO ngay qua bản tin; gửi danh sách 25 trường + mẫu mã hoá cho WP5 / WP6. Phase 2 sẵn sàng (chạm spec — nền tạm tới FW).
