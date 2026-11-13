---
ticket: WO-POLE-GPS
title: Lịch "Cập nhật vị trí cột" — dán tem QR, lấy GPS, Quản lý duyệt rồi mới ghi vào GIS
status: done
phase: 2
owner: claude
branch: feat/WO-POLE-GPS-pole-gps-update
contract_refs:
  - "§5.5 work orders — task_kind, create, complete, verify, /poles"
  - "drift WO-POLE-GPS PG-1…PG-13 (SELF-SIGNED 13/11/2026)"
  - "drift POLE-LOCATION PL-3/PL-4 (luật dời cột dùng lại)"
depends_on: []
---

## Bối cảnh

Cột nhập từ sổ sách của xã chưa có GPS (POLE-GEOM-NULL). WP5 cần một loại lịch riêng: Quản lý chọn các cột trên một tuyến, gửi kèm
PDF tem QR; kỹ sư dán tem, đứng chân cột lấy toạ độ, nộp báo cáo; Quản lý xem trên bản đồ rồi duyệt, lúc đó toạ độ mới vào `pole.geom`.
Prototype: `work_schedule_gps_update_prototype.html` (WP5). Consumer: WP5 (web lịch + duyệt), WP6 (mobile làm offline).

## Phải đọc trước

- `docs/contract-drift.md` mục **WO-POLE-GPS** (đặc tả của ticket này) và **POLE-LOCATION**
- `docs/agents/traps/faults-workorders.md` (BE-23a audit, BE-43 sync, POLE-GEOM-NULL), `media-storage.md`, `migrations.md`, `testing.md`
- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`, `WorkOrderPoles.cs`, `WorkOrderEvidence.cs`
- `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs` — `SetPoleLocationAsync`

## Yêu cầu

1. `TaskKind.PoleGpsUpdate` (`pole_gps_update`), đơn lẻ, không sự cố (PG-1).
2. Mục tiêu `pole_ids` lưu ở `work_order_pole_target`, trả trên item / detail, lọc `/poles` (PG-2…PG-5).
3. PDF đính kèm: upload / tải / gỡ mềm, liệt kê ở detail (PG-6…PG-9).
4. `complete` nhận `pole_locations` (kể cả `/sync/push`), `verify` ghi vào `pole.geom` theo luật PL-3/PL-4, tất cả hoặc không (PG-10…PG-12).

Tiêu chí xong:

- [x] `dotnet build` không cảnh báo mới; `dotnet test` mã thoát 0, có test mới `WorkOrderPoleGpsTests.cs` (partial `WorkOrderTests`) phủ: tạo hợp lệ / cột lệch tuyến /
  cột trùng lệnh mở; `/poles` chỉ trả mục tiêu; follow-up bị chặn; PDF đúng / sai magic bytes / gỡ / tải ngoài phạm vi; complete thiếu /
  thừa / cột lạ; verify ghi geom + audit; verify có cột lệch hành lang không đổi gì
- [x] Phá thử: bỏ lọc `TaskKind` ở `Targets` → test `/poles` đỏ; bỏ kiểm magic bytes → test PDF đỏ; bỏ transaction / kiểm toàn bộ ở verify → test
  "tất cả hoặc không" đỏ
- [x] Migration đọc `Up()`/`Down()` trước khi apply; bảng mới vào `PLAN` của `scripts/copy_dev_to_supabase.py`
- [x] OpenAPI xuất lại + generator + lint

## KHÔNG ĐƯỢC làm

- Không đổi `EditPoleLocation` (đường vòng của kỹ sư — ghi drift, nhóm chốt sau).
- Không thêm xoá object vào `IObjectStore`.
- Không chép logic dời cột sang WorkOrders — gọi lõi của Assets.
- Không sửa `docs/api-contract-v1.1.md` (chưa hợp nhất, SELF-SIGNED).
- Không áp migration lên `luxmap_dev` / VPS.
