---
ticket: LIGHT-CTRL
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-08
scope: đặc tả Phase 1 bản đầu + luxmap-web origin/dev; đọc tĩnh
tokens: 83046
---

> Nguyên văn. Claude đối chiếu code (AuditEvent.cs:25-27, AuditWriteGuard.cs:16, CommuneScopeAccessor.cs:21, các test coverage): cả 10 phát hiện đúng, đã áp vào đặc tả. Số dòng spec:N trỏ vào bản đầu.

**Chưa nên sang Phase 2:** còn điểm chặn về authentication, audit, FK lịch sử và concurrency. Review theo bản spec 145 dòng, đã bổ sung scope thiết bị tại dòng 50–55.

Các vị trí `spec:N` dưới đây thuộc [LIGHT-CTRL-p1.md](.ai/results/LIGHT-CTRL-p1.md).

**A — Backend**

- **P1 — Pipeline Device còn mơ hồ — spec:47,50.** Bootstrap bằng DB rồi cấp `commune_ids` là đúng hướng. Nhưng `[AllowAnonymous]` bỏ **toàn bộ authorization**; chỉ nêu scheme không bảo đảm từ chối authentication thất bại hoặc gán principal vào `HttpContext.User`. Accessor đọc principal đó tại `src/LuxMap.Api/Authorization/CommuneScopeAccessor.cs:21`; scope rỗng khiến filter không thấy dữ liệu và guard từ chối ghi (`src/LuxMap.Persistence/CommuneWriteGuard.cs:52`). **Sửa:** policy riêng chỉ dùng Device + `RequireAuthenticatedUser`, bỏ `AllowAnonymous`, giữ Bearer mặc định; bootstrap bỏ filter chỉ cho lookup credential, các query lệnh vẫn phải lọc thêm đúng `device_node_id`.

- **P1 — FK lịch sử chặn nối lại rơ-le — spec:68,115.** `feeder_control` là mapping hiện tại, PK theo feeder, unique node/relay (`src/LuxMap.Modules.Telemetry/Configurations/IotConfigurations.cs:62,86`). FK tới mapping này khiến cả lệnh terminal vẫn chặn tháo/chuyển relay; tuple FK đề xuất cũng chưa có unique đích. **Sửa:** FK tới danh tính node/feeder, lưu snapshot node/relay/feeder/cabinet; kiểm mapping lúc tạo/giao/ACK. Nếu cần FK mapping thì dùng phiên bản bất biến, không cascade sửa lịch sử.

- **P1 — Lifecycle chưa phân xử race — spec:85,91,92,107,115.** Hai poll có thể cùng giao; hai manager tạo hai pending; ACK delivered cũ ghi đè ACK mới; ACK quá TTL vẫn apply nếu chưa lượt đọc nào materialize expiry. Tháo relay chỉ hủy pending, bỏ sót delivered. **Sửa:** create/poll/ACK/expiry/rewire dùng chung khóa ổn định theo node/relay, transaction và đọc lại sau khóa; khóa nhóm theo thứ tự cố định. ACK tự kiểm TTL, trạng thái và phiên bản mapping; chốt xử lý delivered khi thay/tháo, cùng sequence/dedup phía thiết bị.

- **P1 — Dedup mâu thuẫn nhóm nhiều relay — spec:66,75,100.** Một POST có một `client_op_id` nhưng tạo nhiều row có UUID unique: dùng chung thì vi phạm unique, sinh riêng thì mất dedup lần bấm. **Sửa:** claim UUID ở request group/receipt, lưu actor + hash payload + kết quả nhóm; cùng UUID khác payload → 409. Receipt và commands phải commit nguyên tử.

- **P1 — Audit chưa tương thích — spec:80,107,128.** `src/LuxMap.Persistence/Audit/AuditEvent.cs:25` chưa có actor `System`, entity lighting command và action tương ứng. Guard đòi **đúng một event cho toàn SaveChanges** (`AuditWriteGuard.cs:16`), trong khi nhóm có nhiều lệnh và poll cũng sửa `IAudited`. **Sửa:** bổ sung enum/CHECK vào Phase 2; định nghĩa audit cho delivered. Có thể save từng thao tác relay + một audit trong cùng transaction nhóm. ACK dùng `Iot`, user/role null, snapshot chứa node; supersede do manager phải giữ actor manager.

- **P2 — Poll mất response sẽ mất đường nhận lại — spec:38,107.** Sau commit `delivered`, response rơi thì poll kế tiếp chỉ tìm pending, không lấy lại lệnh còn hạn như phần giới thiệu hứa. **Sửa:** chốt giao lại delivered còn hạn bằng cùng command ID, firmware dedup; không giao lại sau expiry.

- **P2 — Secret/replay chưa đủ rõ — spec:44,51.** Đã yêu cầu so thời gian cố định, nhưng thiếu encoding và hash input. Khuôn thực tế là 32 byte → base64url → SHA256 UTF-8 chuỗi → hex (`src/LuxMap.Modules.Identity/Auth/AccessTokenIssuer.cs:60`). **Sửa:** ghi format canonical, giới hạn chiều dài, so digest 32 byte bằng `FixedTimeEquals`. Header tĩnh vẫn là bearer có thể replay; chốt chấp nhận HTTPS + ACK idempotent, hoặc bổ sung thiết kế chữ ký/timestamp/nonce nếu cần chống replay tầng xác thực.

- **P2 — Coverage chưa bảo vệ ngoại lệ Device — spec:128.** `tests/LuxMap.Api.Tests/CapabilityPolicyCoverageTests.cs:38` bỏ qua anonymous, còn `:56` cấm policy ngoài matrix. `AnonymousEndpointTests.cs:69,84` cũng chưa được tính trong kế hoạch. **Sửa:** ngoại lệ đúng method/route, assert Device-only, authenticated, không anonymous; thêm cases thiếu/sai/rotated secret, Bearer, node khác cùng xã, khác xã và Device gọi endpoint người dùng.

- **P2 — ACK và response thiếu contract — spec:92,100,108,145.** Chưa chốt duplicate khác nội dung, `failed` thiếu mode, mode khác requested; POST chưa có trường liệt kê relay bị loại theo D-8. **Sửa:** validation theo result, duplicate khác kết quả → 409; báo trạng thái có thứ tự để ACK cũ không ghi đè mode mới; thêm `excluded_relays[]` và kết quả từng command cho partial failure.

- **P3 — Thiếu đồng bộ schema — spec:64.** `.ai/results/OPS-SCHEMA-p1.md:278` yêu cầu snapshot `data_source` từ node nhưng schema mới chưa có; `docs/database/erd.md:224` còn ID bigint. **Sửa:** bổ sung nguồn dữ liệu snapshot và đồng bộ ERD/Contract sau khi D-3 được duyệt.

**B — Web, chỉ `origin/dev` tại `03227aa`**

- **Có ON/OFF mô phỏng:** `src/pages/gis-map/components/GisDrawerPanel.tsx:740` gọi bằng `cabinet_id`; `src/hooks/gis-map/useElectricalCascade.ts:7,25,42` dùng mock + React state, đổi ngay `active↔fault`, điện áp `220↔0`, không gọi command API. **P2 khi tích hợp:** thay handler mock bằng preview/send/poll; báo đã thực thi sau ACK, không dùng `fault` để biểu diễn FORCE_OFF.
- Shape hiện tại là GeoJSON cabinet có `cabinet_id`, feeder/segment, status và số đo điện. CRUD “tủ điện” đang gọi `/assets/feeders` (`src/feature/assets/assetAPI.ts:45`), request `{external_ref, feeder_name, commune_id, geom_wkt}` (`src/types/assets/feeders.ts:7`).
- **Không tìm thấy AUTO, provisioning/rotate secret hoặc quản lý mapping relay.** IoT chỉ có type đọc map (`src/types/map/iot-nodes.ts:8`). Cần contract/UI mới theo feeder/segment, `relay_no`, mode và lifecycle command.

| D-item | Verdict |
|---|---|
| D-1 | Poll HTTPS hợp lý; chờ Đạt, sửa mất response. |
| D-2 | Chưa chốt: hoàn thiện pipeline, format và replay. |
| D-3 | CMD width 6 phù hợp; đồng bộ tài liệu sau duyệt. |
| D-4 | Chặn Phase 2: race, dedup, FK và ACK. |
| D-5 | Hợp lý; cập nhật heartbeat đơn điệu, không đồng nghĩa telemetry mới. |
| D-6 | Khớp `ReadNetwork`: bốn vai trò đọc. |
| D-7 | Khớp `ManageAssets`: chỉ Manager; sửa rewire với lệnh đã giao. |
| D-8 | Đúng hướng I-14; bổ sung excluded targets và partial results. |

✅ Đã đối chiếu tĩnh code/spec và web `origin/dev`. ⚠️ Không build/test, không truy cập DB, không sửa file.
