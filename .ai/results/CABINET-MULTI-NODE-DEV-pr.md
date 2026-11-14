## Summary

Cho phép một tủ chứa nhiều IoT node trên nền dev sau #256; giữ ảnh tài sản, feeder state, relay và Telemetry trong phase 1.

```diff
 cabinet
- 0..1 IoT node
+ 0..n IoT node
+ iot_node_ids[]; iot_node_id cũ = phần tử đầu hoặc null

 Bridge và relay cùng feeder
+ khác node → 409 IOT_REGISTRATION_CONFLICT
```

Guard áp cả hai thứ tự ghi, đọc power/control và gửi/giao lệnh; hai writer khóa node→feeder.
Kèm khảo sát phase 2 trong `.ai/results/IOT-CONSOLIDATION-p1.md`; chưa hiện thực bỏ relay hoặc hợp nhất module.

## Evidence

- **Before:** phá guard → 3 regression đỏ; bỏ khóa feeder → race test đỏ; bỏ kiểm rollback → migration test đỏ.
  **After:** khôi phục và build toàn bộ 0 warning/error; **2317 .NET pass, 0 fail, 5 skip nền dev**, gồm MQTT E2E qua EMQX riêng; **20 Python pass**.
- HTTP API thật trên PostGIS tạm: tạo hai node cùng tủ → **201/201**; mỗi node relay 1 trên feeder riêng → **204/204**;
  list/detail/map trả đủ hai node, giữ scalar và các trường ảnh/feeder. Migration apply/down/reapply, rollback nhiều node bị từ chối an toàn.
- Trang 1 và 200 feeder đều 6 SQL; OpenAPI giữ 146 paths, thêm array vào đúng hai schema, lint hợp lệ với 10 warning như dev.
  Review độc lập không còn finding; bằng chứng `.ai/results/CABINET-MULTI-NODE-DEV-p2.md`.

## Merge Danger

**Door:** two-way

Migration `20261114171551_AllowMultipleIotNodesPerCabinet` chỉ thay unique index cabinet bằng index thường, không xóa bảng/cột/dữ liệu.
Down chỉ được khi mỗi tủ còn tối đa một node; nếu đã có nhiều node thì cần giữ schema hoặc reassign rõ ràng trước rollback.
Không tự xóa/gộp node. Chưa apply dev/VPS hoặc deploy.

**Blast Radius:** IoT

API tủ thêm array và giữ scalar tương thích. Cấu hình Bridge/relay mâu thuẫn có thể khiến GET power/map, preview/poll hoặc command trả 409;
không âm thầm chọn một nguồn. Giữ capability/scope, MQTT payload, credential, ảnh và lịch sử hiện hữu.
Năm skip multi-commune đã có trên dev; chưa kiểm firmware vật lý/FE/mobile ngoài repo. Các khoảng trống Bridge last-report/firmware/backup
được ghi trong khảo sát phase 2 và drift, chưa được sửa trong PR này.
