# CABINET-MULTI-NODE-DEV — kết quả tích hợp phase 1

Nền `origin/dev = df35665` (PR #256 đã merge), nhánh `feat/CABINET-multiple-iot-nodes-dev`.
Phạm vi/duyệt: [p1](CABINET-MULTI-NODE-DEV-p1.md), CAB-MN-1…5 ở `docs/contract-drift.md`.

## Thay đổi

- Một cabinet chứa nhiều node; giữ mảng có thứ tự và scalar tương thích ở list/detail/map.
- Giữ ảnh tài sản, feeder `power_state`, quyền/scope, relay, lịch sử, credential và MQTT của dev.
- Guard tạm 409 khi Bridge và relay gán hai node khác nhau cho một feeder; kiểm cả hai writer, đọc và gửi/giao lệnh.
- Nguồn tĩnh + DB trùng feeder trả lỗi rõ ràng. Hai writer khóa node→feeder, DB unique source-feeder của #256 giữ nguyên.
- Migration `20261114171551_AllowMultipleIotNodesPerCabinet` chỉ thay index cabinet unique→thường. Down khóa bảng và từ chối nếu tủ còn nhiều node; không xóa/gộp dữ liệu. Snapshot chỉ thay index này.

## Build và regression

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ LUXMAP_MQTT_E2E=1 MQTT_CALLBACK_PORT=<isolated> MQTT_PORT=<isolated> dotnet test LuxMap.slnx --disable-build-servers --no-build --no-restore --logger 'console;verbosity=minimal'
Passed!  - Failed:     0, Passed:   427, Skipped:     0, Total:   427, Duration: 473 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 1 s - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    83, Skipped:     0, Total:    83, Duration: 5 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:  1762, Skipped:     5, Total:  1767, Duration: 5 m 33 s - LuxMap.Api.Tests.dll (net10.0)
Exit code: 0
```

**2317 pass, 0 fail, 5 skip** (2322 total), MQTT E2E bật qua EMQX riêng; broker đã dừng sau test.

Năm test multi-commune đã bị skip ngay trên dev vì `ACCT-1 / D-6b: requires a multi-commune account; awaiting My review.`:
`WorkOrderPolePhotoReviewTests`, `WorkOrderPolePhotoTests`, `FaultReportTests`, `AssetReplaceAndDeleteTests`, `AssetImportTests`.
Không thay skip, không bỏ qua test lỗi để lấy kết quả xanh.

```text
$ python3 -m unittest discover -s scripts/tests
Ran 20 tests in 1.232s
OK
```

Lượt tổng đầu phát hiện assertion query cũ chỉ cho một query chạm `feeder_control`, trong khi guard thêm một query theo lô;
trang 1/200 đều 7 SQL. Đã gộp đọc owner relay vào candidate bằng LEFT JOIN (cả hai còn 6 SQL), giữ kiểm scope/null;
test yêu cầu đúng hai query bulk JOIN (state và guard) và tổng query trang 1 = trang 200. Review độc lập kiểm lại thay đổi cuối.

## Migration, lỗi và concurrency

Các ca regression chứng minh: hai node cùng tủ, thứ tự ID qua ngưỡng độ rộng, xóa tủ bị chặn khi còn node,
relay số 1 riêng từng node/feeder, credential và lệnh tách theo node, scope ngoài xã không lộ ID;
apply/down/reapply trên DB tạm giữ raw unique source-feeder. Rollback khi cabinet nhiều node phải từ chối mà giữ schema/dữ liệu.
Guard kiểm cả hai thứ tự source↔relay, nguồn tĩnh+DB, dữ liệu mâu thuẫn đã tồn tại, hai đường điều khiển và poll MQTT;
concurrency source↔relay đúng một writer thắng. Concurrency source↔source giữ backstop DB unique.

Ba lần phá thử (đã khôi phục bằng `cp`, build lại toàn bộ sau khôi phục):

```text
binding:
[xUnit.net 00:00:07.08]     LuxMap.Api.Tests.IotDiagnosticsTests.A_source_and_a_relay_cannot_assign_different_nodes_to_the_same_feeder(relayFirst: False) [FAIL]
[xUnit.net 00:00:07.34]     LuxMap.Api.Tests.IotDiagnosticsTests.A_source_and_a_relay_cannot_assign_different_nodes_to_the_same_feeder(relayFirst: True) [FAIL]
[xUnit.net 00:00:07.62]     LuxMap.Api.Tests.IotDiagnosticsTests.Configured_and_database_sources_for_one_feeder_return_a_clear_conflict_instead_of_500 [FAIL]
  Error Message:
  Error Message:
  Error Message:
Failed!  - Failed:     3, Passed:     0, Skipped:     0, Total:     3, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
```

```text
feeder-lock:
[xUnit.net 00:00:07.07]     LuxMap.Api.Tests.IotDiagnosticsTests.Concurrent_source_and_relay_writes_wait_for_the_feeder_and_leave_one_controller [FAIL]
  Error Message:
   The source write bypassed the feeder lock.
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 723 ms - LuxMap.Api.Tests.dll (net10.0)
```

```text
rollback:
[xUnit.net 00:00:09.21]     LuxMap.Api.Tests.CabinetMultiNodeMigrationTests.The_migration_preserves_devices_and_refuses_a_rollback_until_cabinets_have_at_most_one_node [FAIL]
  Error Message:
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 2 s - LuxMap.Api.Tests.dll (net10.0)
```

## HTTP độc lập và OpenAPI

API thật + PostGIS riêng `cabinet_dev_smoke`, migrate/seed trong DB tạm; sau kiểm đã dừng API/container:

```json
{
  "database": "cabinet_dev_smoke",
  "nodes": [
    "NODE-001",
    "NODE-002"
  ],
  "feeders": [
    "FDR-001",
    "FDR-002"
  ],
  "create_nodes": [
    201,
    201
  ],
  "wire_relays": [
    204,
    204
  ],
  "inventory_list_detail_map": "arrays and legacy scalar agree",
  "asset_photos": "preserved empty gallery/count/cover",
  "map_feeders": "both feeder entries retained; relay 1 each; power_state unknown without reports"
}```

```text
$ Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile ...
$ python3 docs/openapi/tools/gen_consolidated_spec.py
paths=146 implemented_ops=189 not_implemented_ops=0 schemas=328
$ npx --yes @redocly/cli@2.0.0 lint docs/openapi/luxmap-v1.5.json
valid; 10 warnings (dev baseline: 10)
```

Xuất lần nữa từ build cuối và `cmp` với raw spec đã sinh: exit 0. So sánh JSON hai spec: giữ toàn bộ 146 paths;
chỉ hai schema `CabinetListItem`/`CabinetProperties` thêm `iot_node_ids`, không mất field cũ (ảnh/feeders/power giữ nguyên).
Các response 409 và mô tả drift thêm theo CAB-MN-5.

## Review và giới hạn

Review độc lập: `.ai/reviews/CABINET-MULTI-NODE-DEV-by-reviewer.md`, không còn finding;
reviewer kiểm source/DI/scope/locks/migration và thay đổi query cuối, không tự chạy lại tests.
Các review DeepSeek/PR256 từ nhánh cũ là bằng chứng lịch sử, không thay thế review tích hợp này.

✅ Verified: nội dung trên bằng build/test/API thật trên hạ tầng tạm, `git diff --check`.
⚠️ Could not verify: chưa chạy với thiết bị vật lý, chưa apply migration/dev/VPS, chưa deploy; 5 skip nền của dev.
📌 Remaining assumptions: scalar chỉ để tương thích; phase 1 chưa ép mỗi node đúng một feeder. Phase 2 cần khảo sát/chốt riêng.

Không sửa workspace gốc (vẫn 3 tracked files dirty như đầu phiên), không push hoặc gửi thông báo ngoài repo.
Fetch cuối vẫn xác nhận `origin/dev = df35665`. Chờ xác nhận riêng trước push theo AGENTS.md §5.
