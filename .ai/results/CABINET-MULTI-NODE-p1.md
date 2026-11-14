# CABINET-MULTI-NODE — khảo sát và quyết định

Mỹ yêu cầu phase 1: một cabinet chứa nhiều node; phase 2 bỏ relay để sau (chat 14/11/2026).
Mỹ chọn thêm `iot_node_ids[]`, giữ trường `iot_node_id` cũ để tương thích.
Đây là phê duyệt cho thay đổi schema / API trong phạm vi task, không phê duyệt apply DB dev hoặc push.

## Bằng chứng khảo sát

Lệnh: `rg -n 'ux_iot_node_cabinet_id|CabinetTaken' src/LuxMap.Modules.Telemetry` (bỏ file migration).
Các dòng đã đọc từ source:

```csharp
builder.HasIndex(node => node.CabinetId).IsUnique().HasDatabaseName("ux_iot_node_cabinet_id");
if (await db.Set<IotNode>().AnyAsync(node => node.CabinetId == cabinet.CabinetId, ct))
{
    throw CabinetTaken(cabinet.CabinetId);
}
```

`CabinetDeviceLookup.DevicesAsync` đang `ToDictionaryAsync(node => node.CabinetId, node => node.NodeId, ...)`:
nhiều node sẽ trùng khóa. `MapQueryService.CabinetsAsync` chỉ lấy `FirstOrDefault()` không sort.
`CabinetTests.A_cabinet_carries_at_most_one_device` và
`DeviceRegistryTests.A_cabinet_carries_one_device_and_a_foreign_cabinet_is_a_404` đang khẳng định luật cũ.

## Quyết định và rollback

- D-1: một cabinet có 0..n node; quan hệ cabinet / commune bắt buộc và FK Restrict giữ nguyên.
- D-2: giữ toàn bộ relay / feeder_control / giao thức lệnh trong phase 1.
- D-3: API thêm mảng ID có thứ tự; scalar cũ là phần tử đầu, không biểu thị quyền ưu tiên điều khiển.
- D-4: đổi index cabinet sang thường. Down dựng lại unique chỉ khi không có cabinet chứa nhiều node;
  nếu có phải từ chối rõ ràng và giữ nguyên dữ liệu / migration hiện hành.
- Thay thế CAB-3 phần cardinality trong drift; chưa hợp nhất Contract (nền CABINET SELF-SIGNED hiện có).

## Môi trường đã kiểm tra

```text
$ git branch --show-current (trước task)
docs/SRS-draft
$ dotnet --version
10.0.302
$ docker info --format '{{.ServerVersion}}'
29.6.1
```

Có thay đổi người dùng tại CLAUDE.md, docs/contract-drift.md, docs/registration/FA26SE222_v1.4.md
và nhiều file untracked; giữ nguyên, chỉ stage phần của task.
