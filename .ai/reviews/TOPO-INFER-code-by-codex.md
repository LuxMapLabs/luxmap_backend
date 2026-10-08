---
ticket: TOPO-INFER
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-07
scope: code Phase 2 — git diff 46d19c6..22c0e21, trừ docs/openapi/*.json; đọc tĩnh
tokens: 78371
---

> Nguyên văn. Claude đối chiếu: cả 4 điểm đúng, đã sửa — P2 (bỏ qua nhóm có tuyến vừa bị xoá thay vì 500), P3 (test thứ tự branch theo feeder qua ngưỡng ID; test lỗi nhãn phía feeder: POST/PUT/import), lệch câu chữ null ở drift TI-2 + mô tả OpenAPI.

**P1:** Không phát hiện.

**P2:**

- [CabinetTopologyService.cs:107](src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs) — Có thể trả **500 khi xoá đồng thời**: sau khi đọc cột, request khác xoá cột cuối rồi xoá tuyến; truy vấn đường không còn tuyến đó, nên `roads[group.Key]` ném `KeyNotFoundException` (dòng 113 tương tự). FK không bảo vệ danh sách đã đọc. **Sửa:** đọc toàn bộ topology trong transaction `RepeatableRead`; thêm test xen lượt xoá.

**P3:**

- [TopologyInferenceTests.cs:358](tests/LuxMap.Api.Tests/TopologyInferenceTests.cs) — Test ngưỡng ID chỉ chứng minh thứ tự **cột**, chưa chứng minh thứ tự feeder/segment và `branch` toàn cục. **Sửa:** tạo nhiều feeder/tuyến có `created_at` trùng, ID qua ngưỡng độ rộng; assert toàn bộ chuỗi cạnh và số branch.
- [TopologyInferenceTests.cs:53](tests/LuxMap.Api.Tests/TopologyInferenceTests.cs) — Chưa phủ đủ bảng TI-2: lỗi nhãn chủ yếu thử `POST poles`; thiếu import feeder, negative cases feeder, array và null khi detach. Test xen hai writer chỉ kiểm cặp pole. **Sửa:** bổ sung ma trận cho cả hai quan hệ, kiểm lỗi field/dòng và dữ liệu không đổi khi bị từ chối.

✅ Rà tĩnh: cập nhật quan hệ đi qua `Resolve` + `Apply*`; tạo mới gán cặp sau `Resolve`, seed SQL ghi cặp với `inferred`; Sync không ghi quan hệ. `MarkPair` và thứ tự `Touch` hợp lý; không thấy lỗi CHECK mới lọt vào batch write. `Lower()` đúng, scoping đúng; migration backfill trước CHECK, `Down()` bỏ CHECK trước cột.

📌 **Spec có điểm lệch:** bảng §3.2 cho phép nhãn `null` khi quan hệ `null`—code làm đúng bảng; drift TI-2/OpenAPI lại mô tả `null → 400` không nêu ngoại lệ. Cần đồng bộ mô tả.

⚠️ Không build/test, truy cập DB hoặc sửa file.
