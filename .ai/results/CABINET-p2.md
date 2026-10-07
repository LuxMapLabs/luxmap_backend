# CABINET — Phase 2 (hiện thực) · 07/10/2026

Nhánh `feat/CABINET-electrical-cabinet`, đã rebase lên `dev` sau khi PR #110 (tài liệu quyết định) merge 07/10/2026. Thiết kế: `.ai/tasks/CABINET.md`; quyết định: `docs/contract-drift.md` mục CABINET (CAB-1…CAB-12).
⚠️ **Nền tạm:** CAB-1…CAB-12 là SELF-SIGNED, chạm API, chờ FW xác nhận.

## Đã làm

| Phần | File |
|---|---|
| Entity + cấu hình | `Assets/Entities/ElectricalCabinet.cs`, `CabinetConstraints.cs`, `Feeder.CabinetId`; `Telemetry/Entities/IotNode` (− `Geom`, + `CabinetId`, `CabinetDataSource`), `FeederControl.CabinetId` |
| Migration | `20261007115020_AddElectricalCabinet` — **viết lại thứ tự tay** (EF sinh `DropColumn geom` trước tiên + cột NOT NULL default `''`) |
| Port | `Assets/ICabinetDeviceLookup` ← `Telemetry/CabinetDeviceLookup` (khuôn `IActiveWorkOrderLookup`) |
| API | `/assets/cabinets` (GET list/detail, POST, PUT, DELETE), import `cabinets`, feeder `cabinet_id` / `cabinet`, `/map/cabinets`, `/map/iot-nodes` lấy toạ độ từ trụ |
| Script | `seed_mock_set.py` (CAB-001…003 cho NODE-001…003), `copy_dev_to_supabase.py` (`PLAN`) |
| Tài liệu | template `cabinets*.csv`, `feeders*.csv` + README; OpenAPI sinh lại; `CLAUDE.md`; ERD Q10; drift CAB-9…CAB-12 |

## Bằng chứng

**Mốc trước khi sửa** (`luxmap_test`, cùng worktree):

```
Passed!  - Failed:     0, Passed:   348 ... LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    44 ... LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    46 ... LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:   740 ... LuxMap.Api.Tests.dll
```

**Sau** (+1 prefix CAB, +22 `CabinetTests`, +1 plan test):

```
Passed!  - Failed:     0, Passed:   349 ... LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    44 ... LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    46 ... LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:   763 ... LuxMap.Api.Tests.dll
```

**Backfill trên `luxmap_test`** (trước: 3 node, 3 relay, 3 feeder):

```
 cabinet_id | cabinet_name | data_source |       st_astext       | node_id  | cabinet_data_source
 CAB-001    | Tủ NODE-001  | simulated   | POINT(106.49 10.97)   | NODE-001 | simulated
 CAB-002    | Tủ NODE-002  | simulated   | POINT(106.492 10.966) | NODE-002 | simulated
 CAB-003    | Tủ NODE-003  | simulated   | POINT(106.5 10.976)   | NODE-003 | simulated
 feeder FDR-001→CAB-001, FDR-002→CAB-002, FDR-003→CAB-003; feeder_control cùng trụ
```

**Rollback → apply lại** (`database update AddSyncOperation` rồi `database update`): node lấy lại đúng 3 điểm cũ,
`electrical_cabinet` biến mất, `ix_iot_node_geom` dựng lại, 0 cột `cabinet%`; apply lại ra 3 trụ và đủ 7 ràng buộc.

**Ràng buộc DB bằng SQL thô** (mỗi câu trong `BEGIN … ROLLBACK`):

```
UPDATE feeder SET cabinet_id = NULL (feeder có rơ-le)        → fk_feeder_control_feeder_same_cabinet
UPDATE feeder SET cabinet_id = 'CAB-002' (feeder có rơ-le)   → fk_feeder_control_feeder_same_cabinet
UPDATE feeder_control SET cabinet_id = 'CAB-002'              → fk_feeder_control_feeder_same_cabinet
UPDATE electrical_cabinet SET data_source = 'field' (có node) → ck_iot_node_cabinet_not_field (qua cascade)
UPDATE electrical_cabinet SET data_source = 'calibration_rig' → iot_node.cabinet_data_source = calibration_rig
INSERT iot_node thứ hai vào CAB-001                           → ux_iot_node_cabinet_id
UPDATE iot_node SET cabinet_data_source sai                    → fk_iot_node_cabinet_data_source
```

**Phá thử (sabotage) — test có đỏ khi luật bị gỡ:**

| Phá | Kết quả |
|---|---|
| `ReadCabinetId`: vắng = tháo | `Replacing_a_feeder_without_the_cabinet_key_keeps_the_cabinet` **đỏ** |
| Import: bỏ kiểm "feeder có rơ-le đổi trụ" | `Import_refuses_per_row_…` **đỏ** — import trả **500** (FK làm hỏng cả mẻ) |
| `IotNodeQuery`: `cabinet.Geom.Buffer(0).Intersects` | plan test **đỏ** (+ 5 test dữ liệu) |
| Gỡ `ThenBy(FeederId.Length)` ở `feeder_ids` (kiểm kê + bản đồ) | `Feeder_ids_are_in_id_order_…` **đỏ** |

`dotnet ef migrations has-pending-model-changes` → `No changes have been made to the model since the last migration.`

OpenAPI: so ngữ nghĩa với `HEAD` — +3 path, +9 schema, đổi đúng `CreateFeederRequest`/`UpdateFeederRequest` (`cabinet_id`),
`FeederListItem` (`cabinet`), POST feeder thêm 404/409. Redocly: hợp lệ, 8 cảnh báo (= trước khi sửa).

## Review

Codex (gpt-6.1-sol, high, chỉ đọc) — `.ai/reviews/CABINET-code-by-codex.md`: **0 P1 · 1 P2 · 1 P3**.

- **P2 — race ở import:** lượt tra thiết bị / rơ-le nằm trước transaction ghi; ai gắn thiết bị xen giữa thì DB từ chối ở bước
  ghi → cả mẻ rollback, 500. **Đúng, không sửa** — cùng lớp với hạn chế đã biết của upsert (BE-12a quy tắc 3: không hỏng dữ
  liệu, chỉ ra 500), và hôm nay writer duy nhất của `iot_node` / `feeder_control` là script seed. Ghi ở remarks của
  `PlanCabinetsAsync`; bản sửa đúng là khoá hàng trong transaction.
- **P3 — test thứ tự `feeder_ids` không bảo vệ tiebreaker độ dài:** **đúng, đã sửa** — hai feeder qua ngưỡng độ rộng trong một
  câu lệnh (trùng `created_at`), assert trên cả `/assets/cabinets/{id}` và `/map/cabinets`.

## Chưa kiểm được

- ⚠️ `copy_dev_to_supabase.py`: chỉ thêm `electrical_cabinet` vào `PLAN` (trước `feeder`); **chưa chạy** diễn tập (cần đích localhost).
- ⚠️ Template `cabinets.example.csv` / `feeders.example.csv` dùng `COM-070` — chưa nạp thử; định dạng giống hệt file test `CabinetTests`.
- `luxmap_dev` **chưa migrate** (theo quy ước: chỉ sau khi xanh hết và Mỹ đồng ý).
