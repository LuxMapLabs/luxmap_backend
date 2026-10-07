---
ticket: CABINET
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-07
scope: code — git diff fe432d6..df7be19+8ddedf0, trừ docs/openapi/*.json; đọc tĩnh, không chạy test / DB
tokens: 249257
---

> Nguyên văn câu trả lời cuối của Codex (link tuyệt đối đổi thành đường dẫn trong repo). Claude đối chiếu code:
> **P2 đúng** — cùng lớp với hạn chế đã biết của import (BE-12a quy tắc 3), **ghi lại, không sửa** (remarks của
> `PlanCabinetsAsync`); **P3 đúng — đã sửa** (`Feeder_ids_are_in_id_order_across_a_width_boundary_on_both_surfaces`,
> phá thử gỡ `ThenBy(length)` → đỏ).

**P1: Không tìm thấy.**

- **P2 — Race CABINET trong import vẫn làm cả mẻ trả 500.** [AssetImportService.cs:207](src/LuxMap.Modules.Assets/Import/AssetImportService.cs) đọc thiết bị; dòng 271 đọc rơ-le, trước transaction ở dòng 139. Nếu writer khác gắn node/rơ-le sau lookup, đổi trụ sang `field` hoặc chuyển feeder sẽ bị CHECK/FK từ chối tại dòng 140; không có catch nên toàn bộ mẻ rollback và trả 500. Test [CabinetTests.cs:350](tests/LuxMap.Api.Tests/CabinetTests.cs) chỉ kiểm quan hệ tồn tại trước import, chưa bắt race này. **Sửa:** mở transaction trước planning, khóa các cabinet/feeder cập nhật bằng `FOR UPDATE` theo thứ tự ổn định, rồi đọc lại quan hệ trước khi quyết định lỗi theo dòng; thêm test chủ động xen lượt gắn node/rơ-le giữa lookup và save.

- **P3 — Test thứ tự `cabinet.feeder_ids` vẫn xanh nếu bỏ tiebreaker độ dài ID.** [CabinetTests.cs:115](tests/LuxMap.Api.Tests/CabinetTests.cs) tạo hai feeder qua các `SaveChanges` riêng (helper dòng 612–618), nên `created_at` đã phân biệt thứ tự. Test không bảo vệ `ThenBy(length)` tại [AssetCrudService.cs:939](src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs) và [MapQueryService.cs:378](src/LuxMap.Modules.Map/Features/MapQueryService.cs). **Sửa:** tạo `FDR-999` và `FDR-1000` cùng transaction để trùng `created_at`, assert thứ tự trên cả inventory và map.

Không thấy lỗi khác: migration backfill trước khi bỏ geometry, `Down()` phục hồi đúng thứ tự; Designer và snapshot giống nhau, không thấy lệch khiến migration kế tiếp tự sửa schema. CRUD giữ/tháo/chuyển đúng, catch đúng constraint; scoping, join map và thứ tự teardown/seed/copy phù hợp. Test GIST chứng minh **khả năng dùng index** với cấu hình cưỡng chế, chưa chứng minh planner thực tế chọn index.

✅ Đã đối chiếu tĩnh. ⚠️ Không chạy build/test, migration hay truy cập database; không sửa file.
