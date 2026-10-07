# TOPO-INFER — Phase 2 (hiện thực) · 07/10/2026

Nhánh `feat/TOPO-INFER` (Phase 1 + Phase 2 chung một nhánh, không PR chồng). Đặc tả + quyết định:
`.ai/results/TOPO-INFER-p1.md` (D-1…D-10), drift TI-1…TI-6. ⚠️ **Nền tạm:** SELF-SIGNED, chạm API, chờ FW.

## Đã làm

| Phần | File |
|---|---|
| Enum | `TopologySource { Verified, Inferred }` — `DomainEnums.cs`; ghim ở `DomainEnumSerializationTests` |
| Cột + CHECK | `Pole.FeederSource`, `Feeder.CabinetSource`; `ck_pole_feeder_source_matches_feeder`, `ck_feeder_cabinet_source_matches_cabinet` + CHECK enum |
| Migration | `20261007143426_AddTopologySource` — thêm cột → **backfill `inferred`** → CHECK |
| Luật ghi | `Assets/Crud/TopologyLink.cs` — `Read`, `Resolve` / `ResolveOrThrow`, `ApplyPoleFeeder`, `ApplyFeederCabinet` (ghi theo cặp) |
| 6 đường ghi | `POST/PUT /assets/poles`, `PUT /assets/poles/{id}/feeder`, `POST/PUT /assets/feeders`, import cột, import feeder |
| Import | `IImportRow.IsNonScalar`, `ImportRowReader.Label` — GeoJSON object / mảng ở ô nhãn = lỗi dòng |
| Đọc | `PoleListItem.feeder_source`, `TopologyPole.feeder_source`, `FeederCabinet.cabinet_source` |
| Endpoint | `GET /map/cabinets/{cabinetId}/topology` — `Map/Features/CabinetTopologyService.cs` |
| Kéo theo | `seed_mock_set.py` (nhãn `inferred`), template + README, `JsonElementFieldSchemaFilter` (5 trường), OpenAPI, `CLAUDE.md`, `tracking.html` |

## Bằng chứng

**Migration trên `luxmap_test`** (trước: 103 cột có feeder, 3 feeder có trụ):

```
pole|inferred|103
feeder|inferred|3
```

Rollback về `DropFieldCabinetRule` → 0 cột nhãn; apply lại sạch. `has-pending-model-changes` → không đổi.
Migration chỉ có `AddColumn` + `AddCheckConstraint` (+ khối backfill) — không `Drop*` nào ngoài `Down()`.

**Test:** trước 1202 (sau #112) → sau **1219/1219 xanh** (+2 enum, +16 `TopologyInferenceTests`, −1 không đổi):

```
Passed!  - Failed:     0, Passed:   351 ... LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    44 ... LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    46 ... LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:   778 ... LuxMap.Api.Tests.dll
```

Release `--no-incremental`: 0 warning, 0 error. 26 test cũ đỏ giữa chừng đều là fixture gán quan hệ không nhãn / danh sách khoá
literal — đã sửa theo luật mới (`TopologyQueryTests`, `IotNodeTests`, `FeederCommuneConstraintTests`, `CabinetTests`,
`AssetReplaceAndDeleteTests`, `AssetReadShapeTests`).

**Phá thử — mỗi luật đỏ đúng test:**

| Phá | Test đỏ |
|---|---|
| Vắng nhãn + quan hệ không đổi → `inferred` (hạ nhãn) | `A_full_replacement_keeps_…`, `The_narrow_feeder_endpoint_…`, `Import_keeps_a_label_…` |
| Bỏ ghi theo cặp (`IsModified`) | `Interleaved_writers_never_leave_a_pair_nobody_asserted` |
| Cạnh đầu không lấy nhãn thấp hơn | `An_edge_is_only_as_verified_as_its_weakest_relation` |
| Đọc hình học tuyến qua query filter | `A_foreign_road_still_orders_the_branch_and_ties_follow_the_id_rule` |

**OpenAPI** so ngữ nghĩa với `HEAD`: +1 path, +4 schema (`TopologySource`, `CabinetTopologyEdge*`), 8 schema thêm đúng một trường
nhãn; không xoá / đổi gì. Redocly hợp lệ, 8 cảnh báo (= trước).

## Chưa kiểm được / còn lại

- `luxmap_dev` chưa migrate (`AddElectricalCabinet`, `DropFieldCabinetRule`, `AddTopologySource`).
- Codex review code (Phase 2) — chưa chạy lúc viết file này.
- Báo WP5: thay `useElectricalCascade` bằng `/map/cabinets/{id}/topology`; ô nhãn trên form cột / mạch.
- Ticket riêng (D-7, D-8): chặn trộn `field` / mô hình ở ba đường; script đề xuất quan hệ suy luận.
