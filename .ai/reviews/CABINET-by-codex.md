---
ticket: CABINET
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-07
scope: đọc tĩnh .ai/tasks/CABINET.md bản đầu; không chạy test, không chạm DB
tokens: 98267
---

> Nguyên văn câu trả lời cuối của Codex. Claude đã đối chiếu từng mục với code — cả 5 đúng, đã áp vào
> `.ai/tasks/CABINET.md` (R-1…R-5). Link tuyệt đối đã đổi thành đường dẫn tương đối trong repo.

Đề xuất nhìn chung hợp lý; **D-4 cần sửa lập luận và cơ chế bảo vệ trước khi triển khai**.

1. **P1 — D-4 hiểu sai `MATCH SIMPLE`; chỉ kiểm ở service chưa đủ.**  
   [CABINET.md:67](.ai/tasks/CABINET.md) cho rằng nullable ở feeder khiến FK bỏ qua. NULL được xét ở **phía tham chiếu**: đặt `feeder_control.cabinet_id NOT NULL` thì feeder chưa có tủ không thể khớp. **Sửa:** thêm hai FK `(feeder_id,cabinet_id)` / `(node_id,cabinet_id)` tới UNIQUE **đầy đủ**, giữ hai FK xã hiện có tại [IotConfigurations.cs:68](src/LuxMap.Modules.Telemetry/Configurations/IotConfigurations.cs). Service trả lỗi thân thiện, phủ cả đổi tủ của node; DB chặn race và writer khác. Đây đúng với [quy tắc PostgreSQL](https://www.postgresql.org/docs/16/ddl-constraints.html).  
   **Bẫy khi sửa:** tạo UNIQUE/FK bổ sung ở DB; tránh `HasAlternateKey/HasPrincipalKey` trên `Feeder.CabinetId` nullable, cần sửa. EF key bất biến — tiền lệ tại [AGENTS.md:358](AGENTS.md), cũng được [EF xác nhận](https://learn.microsoft.com/en-us/ef/core/modeling/keys).

2. **P2 — Luật “node không nằm trên tủ `field`” đúng mục đích nhưng chưa được schema bảo vệ.**  
   FK đề xuất tại [CABINET.md:56](.ai/tasks/CABINET.md) chỉ so xã; [CHECK hiện tại:20](src/LuxMap.Modules.Telemetry/Configurations/IotConfigurations.cs) chỉ kiểm nguồn **node**. Tủ `field` + node `simulated` cùng xã vẫn lọt. **Sửa:** kiểm cả gắn/chuyển node và sửa nguồn tủ qua CRUD/import. Backstop rẻ: `iot_node.cabinet_data_source NOT NULL CHECK <> 'field'`, FK `(cabinet_id,cabinet_data_source)` tới UNIQUE đầy đủ `(cabinet_id,data_source)` của tủ. Giữ riêng `node.data_source`; không ép hai nguồn bằng nhau.

3. **P2 — Thêm endpoint riêng chưa giải quyết bẫy feeder PUT.**  
   [CABINET.md:83](.ai/tasks/CABINET.md) vẫn cho PUT chính xoá tủ khi thiếu trường; cách thay thế hiện tại thể hiện tại [AssetCrudService.cs:302](src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs). Client cũ chỉ đổi tên sẽ vô tình tháo tủ, hoặc nhận 409 nếu D-4 được bảo vệ. **Sửa:** chốt ngoại lệ “vắng = giữ, `null` = tháo”, tương tự `note`; emit cabinet trong cả list/detail qua projection chung. Endpoint `/cabinet` chỉ hỗ trợ thao tác riêng, không tự bảo vệ PUT chính. Contract đã giải thích yêu cầu đọc được quan hệ tại [§5.3.1:631](docs/api-contract-v1.1.md).

4. **P2 — `Down()` chưa đặc tả khôi phục đủ schema cũ.**  
   [CABINET.md:94](.ai/tasks/CABINET.md) thiếu thứ tự và phục hồi index; schema cũ yêu cầu [Point NOT NULL:25](src/LuxMap.Persistence/Migrations/20260928100335_AddIotNodes.cs), [GIST:110](src/LuxMap.Persistence/Migrations/20260928100335_AddIotNodes.cs). **Sửa:** thêm geom nullable → backfill → SET NOT NULL → dựng lại GIST → bỏ FK/cột bổ sung → drop bảng/sequence. Rollback trả vị trí **hiện tại** của tủ; không phục hồi vị trí trước `Up()` nếu tủ đã di chuyển.

5. **P2 — “Kéo theo” thiếu helper test và sửa ERD.**  
   [IotNodeTests.cs:311](tests/LuxMap.Api.Tests/IotNodeTests.cs) còn khởi tạo `Geom`; [helper feeder:324](tests/LuxMap.Api.Tests/IotNodeTests.cs) chưa gán tủ. **Sửa:** tạo cabinet rồi gán node/feeder cùng tủ; bổ sung kiểm chứng các invariant mới. ERD cần sửa [Q10:33](docs/database/erd.md), [geom nullable:70](docs/database/erd.md), [quan hệ node 0..n:268](docs/database/erd.md) thành 0..1. Proposal thực tế đổi **ba** điểm Q10, không chỉ hai.

Verdict từng quyết định:

- **D-1: Đồng ý.** FK xã ghép, alternate key của cabinet, index xã đầy đủ và `Restrict` đúng. Nhớ `HasCommuneScope()`; `CommuneWriteGuard` chỉ bảo vệ phạm vi xã, không kiểm D-4/provenance ([guard:52](src/LuxMap.Persistence/CommuneWriteGuard.cs)). `Restrict` tránh mở thêm cascade mà guard không thấy.
- **D-2: Đồng ý có điều kiện.** Grep thấy reader geometry nghiệp vụ duy nhất là [MapQueryService.cs:269](src/LuxMap.Modules.Map/Features/MapQueryService.cs), projection/output cùng hàm; còn config, seed và helper test phải đổi. `ST_Intersects(cabinet.geom,envelope)` trên cột thô vẫn **có thể dùng GIST** sau join; giữ lọc `node.DataSource` tại dòng 271. Chưa thể xác nhận planner chọn index khi không chạy `EXPLAIN`.
- **D-3: Đồng ý.** `NOT NULL + UNIQUE(cabinet_id)` đủ ở DB; full unique hợp lý vì node chưa có trạng thái ngừng dùng.
- **D-4: Cần sửa** theo finding 1.
- **D-5: Đồng ý.** `CAB`, width tối thiểu 3, `cabinet_id_seq` khớp [PrefixedId.cs:30](src/LuxMap.Shared/Contracts/PrefixedId.cs). `CAB-HVC-A` là `external_ref`, không xung đột với ID `CAB-001`; không parse nó như ID. Giá trị cụ thể `FDR-005/CAB-HVC-A` chỉ thấy trong proposal, chưa xác minh được từ nguồn repo.

✅ Đã đối chiếu tĩnh. Không sửa file, chạy `dotnet test` hoặc truy cập database.
