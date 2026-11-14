# EVIDENCE-POLE — Báo cáo pha 1

Ngày: 14/11/2026. **Chỉ khảo sát; mọi đề xuất dưới đây chờ Mỹ chốt.** Ticket xây trên EV-3 và POLE-PHOTO đang SELF-SIGNED, nền tạm tới FW (`docs/contract-drift.md:1036`, `docs/contract-drift.md:1848`).

✅ Đã đối chiếu mã nguồn, migration, snapshot và test hiện có.  
⚠️ Không kiểm chứng runtime/DB đang triển khai; không build, chạy test, tạo migration hay sửa file theo yêu cầu.  
📌 Báo cáo này là nội dung bàn giao cho `.ai/results/EVIDENCE-POLE-p1.md`; chưa ghi xuống đĩa. `git status --short --branch` trả `## feat/EVIDENCE-POLE...origin/dev [ahead 1]`; `git diff --stat` không có output.

## 1. Hiện trạng schema và các nơi đọc ảnh

Schema trong repo có **15 cột**, chỉ `work_order_id`, `fault_id`, `client_op_id` nullable; PK `evidence_id`, 4 FK Restrict, 7 CHECK, 5 index ngoài index của PK. Không có `pole_id`. Dưới đây là **output terminal thật**, trích bằng Python chỉ đọc snapshot và ghép khoảng trắng trong từng statement; số đầu dòng thuộc `src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs`:

```text
3823: b.Property<string>("EvidenceId") .ValueGeneratedOnAdd() .HasColumnType("text") .HasColumnName("evidence_id") .HasDefaultValueSql("luxmap_format_id('EVD', nextval('evidence_id_seq'), 4)") .HasAnnotation("LuxMap:PrefixedIdSequence", "evidence_id_seq");
3830: b.Property<long>("ByteCount") .HasColumnType("bigint") .HasColumnName("byte_count");
3834: b.Property<DateTime>("CapturedAt") .HasColumnType("timestamp with time zone") .HasColumnName("captured_at");
3838: b.Property<Guid?>("ClientOpId") .HasColumnType("uuid") .HasColumnName("client_op_id");
3842: b.Property<string>("CommuneId") .IsRequired() .HasColumnType("text") .HasColumnName("commune_id");
3847: b.Property<string>("FaultId") .HasColumnType("text") .HasColumnName("fault_id");
3851: b.Property<string>("Kind") .IsRequired() .HasColumnType("text") .HasColumnName("kind");
3856: b.Property<double>("Lat") .HasColumnType("double precision") .HasColumnName("lat");
3860: b.Property<double>("Lng") .HasColumnType("double precision") .HasColumnName("lng");
3864: b.Property<string>("ObjectKey") .IsRequired() .HasColumnType("text") .HasColumnName("object_key");
3869: b.Property<long>("ThumbnailBytes") .HasColumnType("bigint") .HasColumnName("thumbnail_bytes");
3873: b.Property<string>("ThumbnailKey") .IsRequired() .HasColumnType("text") .HasColumnName("thumbnail_key");
3878: b.Property<DateTime>("UploadedAt") .ValueGeneratedOnAdd() .HasColumnType("timestamp with time zone") .HasColumnName("uploaded_at") .HasDefaultValueSql("now()");
3884: b.Property<string>("UploadedBy") .IsRequired() .HasColumnType("text") .HasColumnName("uploaded_by");
3889: b.Property<string>("WorkOrderId") .HasColumnType("text") .HasColumnName("work_order_id");
3893: b.HasKey("EvidenceId") .HasName("pk_repair_evidence");
3896: b.HasIndex("CommuneId") .HasDatabaseName("ix_repair_evidence_commune_id");
3899: b.HasIndex("UploadedBy") .HasDatabaseName("ix_repair_evidence_uploaded_by");
3902: b.HasIndex("FaultId", "CommuneId") .HasDatabaseName("ix_repair_evidence_fault_id_commune_id");
3905: b.HasIndex("UploadedBy", "ClientOpId") .IsUnique() .HasDatabaseName("ux_repair_evidence_uploaded_by_client_op_id") .HasFilter("client_op_id IS NOT NULL");
3910: b.HasIndex("WorkOrderId", "CommuneId") .HasDatabaseName("ix_repair_evidence_work_order_id_commune_id");
3915: t.HasCheckConstraint("ck_repair_evidence_bytes", "byte_count > 0 AND thumbnail_bytes > 0");
3917: t.HasCheckConstraint("ck_repair_evidence_fault_observation", "fault_id IS NULL OR kind = 'observation'");
3919: t.HasCheckConstraint("ck_repair_evidence_keys_not_blank", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> ''");
3921: t.HasCheckConstraint("ck_repair_evidence_kind", "\"kind\" IN ('before', 'after', 'observation')");
3923: t.HasCheckConstraint("ck_repair_evidence_lat", "lat >= -90 AND lat <= 90");
3925: t.HasCheckConstraint("ck_repair_evidence_lng", "lng >= -180 AND lng <= 180");
3927: t.HasCheckConstraint("ck_repair_evidence_one_parent", "(work_order_id IS NULL) <> (fault_id IS NULL)");
6237: b.HasOne("LuxMap.Persistence.AdministrativeUnit", null) .WithMany() .HasForeignKey("CommuneId") .OnDelete(DeleteBehavior.Restrict) .IsRequired() .HasConstraintName("fk_repair_evidence_administrative_unit_commune_id");
6244: b.HasOne("LuxMap.Modules.Identity.Entities.AppUser", null) .WithMany() .HasForeignKey("UploadedBy") .OnDelete(DeleteBehavior.Restrict) .IsRequired() .HasConstraintName("fk_repair_evidence_app_user_uploaded_by");
6251: b.HasOne("LuxMap.Modules.Faults.Entities.Fault", null) .WithMany() .HasForeignKey("FaultId", "CommuneId") .HasPrincipalKey("FaultId", "CommuneId") .OnDelete(DeleteBehavior.Restrict) .HasConstraintName("fk_repair_evidence_fault_fault_id_commune_id");
6258: b.HasOne("LuxMap.Modules.WorkOrders.Entities.WorkOrder", null) .WithMany() .HasForeignKey("WorkOrderId", "CommuneId") .HasPrincipalKey("WorkOrderId", "CommuneId") .OnDelete(DeleteBehavior.Restrict) .HasConstraintName("fk_repair_evidence_work_order_work_order_id_commune_id");
```

Đã đối chiếu lịch sử: tạo bảng/sequence tại `src/LuxMap.Persistence/Migrations/20261004134151_AddRepairEvidence.cs:14`; migration sau cho `work_order_id` nullable, thêm `fault_id`, index/FK và hai CHECK cha tại `src/LuxMap.Persistence/Migrations/20261004153825_AttachPhotosToFaults.cs:13`.

Các nơi sử dụng hiện tại:

- **Danh sách:** theo phiếu hoặc sự cố; kiểm cha trước, phân trang theo `captured_at, uploaded_at, length(evidence_id), evidence_id` (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:225`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:238`).
- **Thumbnail/original:** lookup ảnh theo scope xã rồi kiểm quyền đọc cha; lấy bytes từ bucket Evidence (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:248`, `src/LuxMap.Modules.WorkOrders/EvidenceController.cs:17`).
- **Hoàn thành:** chỉ kiểm tồn tại ≥1 ảnh `after` của phiếu sửa chữa; không kiểm theo từng cột (`src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:583`).
- **Sync bundle:** chỉ chứa bản đồ, sự cố mở và chi tiết phiếu; chi tiết không chứa danh sách evidence (`src/LuxMap.Modules.Sync/SyncBundleService.cs:62`, `src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:38`).
- **Follow-up:** mang sự cố sang phiếu mới, không đọc/copy evidence (`src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:314`).
- **Báo cáo:** không tìm thấy consumer evidence trong module Statistics; thống kê sửa chữa/trạng thái phiếu đọc `WorkOrder`. Dung lượng/đối chiếu object có khai original và thumbnail của `repair_evidence` (`src/LuxMap.Modules.Statistics/StatisticsService.cs:196`, `src/LuxMap.Modules.Statistics/StatisticsService.cs:260`, `src/LuxMap.Modules.WorkOrders/WorkOrdersModule.cs:42`).

Không tìm thấy đường suy cột từ ảnh: entity chỉ lưu GPS chụp; ghi và DTO không lookup cột (`src/LuxMap.Modules.WorkOrders/Entities/RepairEvidence.cs:23`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:187`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:274`).

## 2. `pole_id` bắt buộc hay tuỳ chọn?

**Đề xuất D-1: tuỳ chọn**, thiếu thì lưu/trả `null`. Ảnh tổng quan tuyến, hiện trường chung hoặc sự cố chưa có cột là tình huống hợp lý; Contract cho phép fault chỉ có `location` (`docs/api-contract-v1.1.md:932`).

App WP6 cũ không gửi trường mới vẫn upload như hiện tại; ảnh cũ giữ `NULL`, không đoán từ GPS. Form hiện chưa có `pole_id` (`src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:105`). Mọi loại phiếu vẫn giữ tập nhãn EV-3, chỉ repair bắt ảnh `after` (`docs/contract-drift.md:1036`).

## 3. Cột nào hợp lệ, mã lỗi và `geom = null`?

**Đề xuất D-2:** gọi trực tiếp `WorkOrderPoleService.Targets(id)`, không đọc trang đầu của `/poles`, không chép lại predicate. Tập hiện tại gồm cột trên tuyến phiếu, mục tiêu `work_order_pole_target`, hoặc cột mang fault của phiếu; `pole_gps_update` bỏ nhánh cả tuyến (`src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:86`).

Dùng lại cách phân lỗi của `/pole-photos`: cột không tồn tại/ngoài scope → **404 `POLE_NOT_FOUND`**; cột nhìn thấy nhưng ngoài tập mục tiêu → **409 `POLE_NOT_IN_WORK_ORDER`**. Kiểm quyền phiếu trước để giữ **404 `WORK_ORDER_NOT_FOUND`** (`src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:37`, `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:45`).

**Đề xuất nhận cột chưa GPS.** `Targets` không yêu cầu geometry; `/pole-photos` chặn riêng vì ảnh được xác nhận OFF sẽ sinh fault cần vị trí. Evidence chỉ cho người xem, nên không tái dùng nguyên `Target(...)` có chốt geometry (`src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:51`, `src/LuxMap.Modules.WorkOrders/Entities/RepairEvidence.cs:12`). Ngoại lệ liên xã cần chốt D-3.

## 4. Kiểm lại sau khi ghi object và thứ tự khoá

Hiện `StoreAsync` kiểm quyền → replay → trạng thái → ghi object → mở transaction/khoá phiếu → kiểm lại quyền/replay/trạng thái → insert (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:163`).

**Đề xuất D-4:** với ảnh mới có cột, kiểm tồn tại/scope/thành viên trước storage và kiểm lại dưới khoá sau storage. Đọc qua filter trước khi chờ khoá, khoá **phiếu → cột**, rồi đọc LINQ không tracking lại; không giữ khoá qua upload/giải mã. Khuôn tương ứng đã có ở `/pole-photos` (`CLAUDE.md:260`, `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:140`).

Cột bị xoá/ra ngoài scope → 404; còn nhìn thấy nhưng rời tập phiếu → 409 `POLE_NOT_IN_WORK_ORDER`; không tạo row. Object đã ghi có thể thành orphan theo cơ chế hiện tại (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:55`). Không buộc segment/GPS phải bất biến nếu cột vẫn hợp lệ: evidence không công bố trạng thái. Replay phải chạy trước kiểm điều kiện dành cho ảnh mới, kể cả sau hoàn thành (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:182`).

## 5. `client_op_id`

Replay hiện tra theo `(uploaded_by, client_op_id)` và so **hai cha + kind**, chưa so GPS, thời gian hay bytes (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:214`).

**Đề xuất D-5:** thêm so `pole_id` chính xác, kể cả `NULL`: cùng khoá khác cột, `NULL → cột`, hoặc `cột → NULL` đều **409 `IDEMPOTENCY_CONFLICT`**. Áp dụng cả replay sớm, replay sau storage và nhánh bắt unique violation (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:166`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:184`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:201`).

Giữ replay cùng yêu cầu sau khi phiếu hoàn thành nếu còn quyền; ảnh cũ không được bổ sung cột bằng cách dùng lại khoá. Chưa đề xuất đưa hash toàn nội dung như `/pole-photos` vào ticket này (`src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:120`).

## 6. Schema, phạm vi xã và migration

**Đề xuất:** thêm `pole_id text NULL`, FK tới `pole(pole_id)` **Restrict**, index đầy đủ dẫn đầu bằng `pole_id`; giữ nguyên PK, sequence, index và CHECK hiện có. Nếu giữ ảnh fault không nhận cột riêng, thêm CHECK đề xuất `fault_id IS NULL OR pole_id IS NULL` — tên ràng buộc mới phải được chốt/kiểm trong pha 2. Khuôn Restrict và cấu hình evidence hiện tại ở `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:173`.

**D-3 là lựa chọn quan trọng:** `commune_id` evidence hiện lấy từ phiếu, FK ghép buộc khớp phiếu; nhưng WO-12/Q7 cho phép cột khác xã trong scope. `/pole-photos` giải quyết bằng xã của cột và FK phiếu đơn (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:100`, `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:194`, `docs/database/erd.md:30`, `docs/contract-drift.md:1857`).

**Khuyến nghị giữ toàn bộ tập WO-12 và xã evidence theo phiếu:** không thêm FK buộc evidence/cột cùng xã vì liên xã là hợp lệ; thêm kiểm scope cột độc lập cho list/media/replay khi `pole_id != null`. Chỉ kiểm scope lúc upload là chưa đủ: media hiện chỉ kiểm cha, nên có thể tiếp tục mở ảnh gắn cột ngoài scope sau khi phạm vi đổi (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:248`).

Nếu Mỹ chọn chỉ nhận cột cùng xã phiếu, cần kiểm service và FK ghép DB-only tới `pole(pole_id, commune_id)`, tái dùng `ak_pole_photo_target`; không thêm alternate key EF làm khoá `Pole.CommuneId`. Đây là **thu hẹp** WO-12 và phải ghi rõ; guard chỉ kiểm trong scope, không kiểm khớp cha (`CLAUDE.md:206`, `src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:136`).

**D-8:** `Up()` chỉ thêm cột/index/FK/CHECK đã duyệt; không backfill. `Down()` đảo các thao tác đó, giữ unique đích dùng chung; đề xuất từ chối khi còn `pole_id` khác NULL để tránh mất liên kết đã ghi. Rollback ứng dụng có thể giữ schema cộng thêm. Tiền lệ từ chối rollback gây mất quan hệ tại `src/LuxMap.Persistence/Migrations/20261004153825_AttachPhotosToFaults.cs:54`; yêu cầu đối xứng tại `docs/agents/traps/migrations.md:7`.

## 7. FK Restrict chặn gì khi xoá/gộp/import/dời cột?

`DELETE /assets/poles/{poleId}` xoá thật qua `DeletePoleAsync`; bắt mọi FK violation và trả **409 `ASSET_IN_USE`**, kèm thông tin constraint. Vì vậy FK evidence mới sẽ được xử lý sạch trên đường này theo mã nguồn, chưa kiểm runtime (`src/LuxMap.Modules.Assets/Crud/AssetsController.cs:349`, `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:667`, `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:723`).

`work_order_pole_photo` đã giữ cột bằng FK đơn Restrict và FK ghép DB-only; evidence sẽ thêm một loại lịch sử ngăn xoá (`src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs:42`, `src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:138`).

Không thấy endpoint gộp cột/xoá cột trong import Assets: `PlanPolesAsync` cập nhật hàng cũ hoặc thêm hàng mới. Công cụ `photos_to_poles.py --merge` gộp nhóm ảnh trước import, không phải merge hàng DB (`src/LuxMap.Modules.Assets/Import/AssetImportService.cs:445`, `src/LuxMap.Modules.Assets/Import/AssetImportService.cs:465`, `scripts/photos_to_poles.py:63`).

Dời GPS/đổi tuyến giữ nguyên ID nên FK đơn không chặn; đổi tuyến trong lúc upload phải được lượt kiểm lại phát hiện (`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:621`, `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:845`). Script seed có SQL xoá `pole`; đó là đường script, không có envelope HTTP 409, và có thể bị FK chặn. Không chạy script này (`scripts/seed_mock_set.py:132`).

## 8. Bề mặt API tối thiểu

**Đề xuất:** thêm form field tuỳ chọn `pole_id` cho POST evidence; thêm `EvidenceItem.PoleId` nullable và mapping dùng chung cho POST/replay/GET. Giữ status 201/200 và các field hiện có (`src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:105`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:21`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:274`).

**D-6: khuyến nghị thêm `GET /work-orders/{id}/evidence?pole_id=...`.** Lọc trước count/phân trang, giữ thứ tự hiện tại; không gửi filter thì gồm cả ảnh tổng quan NULL. Phiếu không nhìn thấy vẫn 404; pole filter không khớp ảnh nào trả trang rỗng, không lookup cột để phát lộ tồn tại. Khuôn này đã có ở list pole-photos (`src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:185`).

Đây là đề xuất hỗ trợ màn ảnh theo cột; repo backend chưa chứng minh WP5/WP6 đang cần hay đã xử lý field mới. Ticket xác định hai consumer, còn EV-3 chưa báo họ (`.ai/tasks/EVIDENCE-POLE.md:18`, `docs/contract-drift.md:1036`).

## 9. Ảnh sự cố `POST /faults/{id}/photos`

**Đề xuất D-7: không thêm input `pole_id`.** Ảnh vẫn gắn fault, luôn observation, chỉ người báo và fault mở; cột tra từ fault nếu có. Không sao chép `fault.pole_id` xuống row ảnh và không suy từ GPS (`src/LuxMap.Modules.WorkOrders/FaultPhotosController.cs:33`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:123`).

Do dùng chung DTO, response ảnh fault sẽ có trường mới `pole_id: null`; nghĩa là **ảnh không có liên kết cột trực tiếp**, không khẳng định fault không có cột. CHECK ở mục 6 ngăn ghi hai nguồn liên kết cột khác nhau. Fault không cột vẫn upload được ảnh (`docs/api-contract-v1.1.md:932`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:139`).

## 10. Sync offline

**Xác nhận ảnh không qua `/sync/push`:** controller nói rõ; enum/parser chỉ có `fault_report`, `lux_reading`, `pole_note`, `work_order_start`, `work_order_complete` (`src/LuxMap.Modules.Sync/SyncController.cs:47`, `src/LuxMap.Modules.Sync/Entities/SyncOperation.cs:4`, `src/LuxMap.Modules.Sync/SyncPushService.cs:239`).

Bundle không mang evidence nên không cần thêm `pole_id` ảnh vào bundle hiện tại (`src/LuxMap.Modules.Sync/SyncContracts.cs:18`). Đề xuất WP6 giữ `pole_id` cùng file và UUID trong hàng chờ multipart riêng: start → upload evidence → complete; repair phải chờ ảnh after thành công. Đây là cách tổ chức được đề xuất cho evidence, tương tự PP-6, không thêm op_type/base64 (`docs/contract-drift.md:1860`, `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:583`).

## 11. Những chỗ phải sửa cùng trong pha 2

- **Code:** entity/config/migration + snapshot; controller form/query; service validation, hai lượt kiểm, replay, mapping và scope đọc theo D-3. Các điểm hiện có: `src/LuxMap.Modules.WorkOrders/Entities/RepairEvidence.cs:21`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:96`, `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:105`.
- **OpenAPI:** xuất lại `docs/openapi/luxmap-v1.json`, sinh `luxmap-v1.5.json`; cập nhật summary POST/GET trong generator. Không tạo lại schema upload viết tay để đè schema sinh. `luxmap-v1.json` thuộc CODEOWNERS (`.github/CODEOWNERS:15`, `.ai/context/commands.md:71`, `docs/openapi/tools/gen_consolidated_spec.py:289`, `docs/openapi/tools/gen_consolidated_spec.py:579`).
- **Drift:** thêm EV-4 sau khi Mỹ chốt, ghi lựa chọn nullable/target/xã/replay/filter/fault-photo và trạng thái SELF-SIGNED, nền tạm tới FW; báo WP5/WP6. Không sửa Contract văn bản trong ticket này (`.ai/tasks/EVIDENCE-POLE.md:65`, `docs/contract-drift.md:1049`).
- **ERD:** bổ sung cột nullable và quan hệ pole → repair_evidence, diễn giải ảnh fault; giữ Q7 hoặc ghi rõ ngoại lệ đã duyệt (`docs/database/erd.md:30`, `docs/database/erd.md:292`, `docs/database/erd.md:355`).
- **Copy DB:** không thêm bảng PLAN; cột được đọc động và COPY có danh sách cột. Pole đã đứng trước evidence. Tuy nhiên nếu cho liên xã, phải thêm điều kiện giữ ảnh khi `pole_id IS NULL` hoặc pole được chép, tránh giữ ảnh trỏ tới pole bị lọc bỏ (`scripts/copy_dev_to_supabase.py:95`, `scripts/copy_dev_to_supabase.py:145`, `scripts/copy_dev_to_supabase.py:373`, `scripts/copy_dev_to_supabase.py:422`).
- **Tracking:** cập nhật sau thực thi/kiểm chứng pha 2; pha 1 giữ nguyên mọi file theo lệnh READ-ONLY (`.ai/tasks/EVIDENCE-POLE.md:66`, `.ai/tasks/EVIDENCE-POLE.md:71`).

## 12. Kế hoạch test pha 2 — chưa tạo/chạy

Tên dưới đây là **tên dự kiến**, điều chỉnh theo D-item đã chốt. Mở rộng fixture/helper hiện có, dùng PostGIS riêng và AssetDatabaseCollection; dọn evidence trước pole (`tests/LuxMap.Api.Tests/WorkOrderTests.cs:89`, `tests/LuxMap.Api.Tests/WorkOrderTests.cs:964`, `docs/agents/traps/testing.md:5`).

- `Evidence_without_a_pole_preserves_old_clients`: thiếu pole, ảnh tổng quan, dữ liệu trước migration → NULL; nhãn EV-3 và completion giữ nguyên.
- `Evidence_accepts_every_work_order_target`: tuyến survey nhiều trang (>200), tuyến inspection/repair, cột qua fault kể cả chưa tuyến; GPS-update chỉ nhận mục tiêu được nêu.
- `Evidence_rejects_missing_foreign_and_unrelated_poles`: 404 missing/ngoài xã, 409 ngoài phiếu; bị từ chối trước storage.
- `Evidence_accepts_an_unlocated_target_without_publication`: geom NULL hợp lệ; không gọi AI, không đổi status/fault.
- `Evidence_preserves_commune_rules_and_read_scope`: liên xã theo D-3; kiểm list/original/thumbnail/replay sau mất scope cột hoặc đổi assignee.
- `Evidence_rechecks_targets_after_storage`: dùng `DuringWrite` xoá cột/đổi tuyến/rút mục tiêu; không insert row; giữ các ca reassign/cancel hiện có (`tests/LuxMap.Api.Tests/WorkOrderTests.cs:1176`).
- `Evidence_holds_order_then_pole_locks`: kiểm thứ tự và sự chờ bằng connection độc lập với `FOR NO KEY UPDATE`, tránh FK KEY SHARE cho xanh giả (`tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:565`).
- `Evidence_replay_compares_nullable_pole`: cùng cột →200; khác cột và hai chiều NULL →409; ảnh cũ không được gắn cột qua retry; replay sau done.
- `Overlapping_evidence_retries_compare_pole`: kiểm replay sau storage và nhánh unique race khác phiếu, không chỉ replay sớm (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:201`, `tests/LuxMap.Api.Tests/WorkOrderTests.cs:1197`).
- `Evidence_filter_counts_before_pagination`: trộn hai cột + NULL, kiểm total/items/thứ tự và filter không khớp → trang rỗng.
- `Fault_photos_keep_their_parent_semantics`: fault có/không cột đều upload; DTO trực tiếp NULL; CHECK chặn fault-photo có pole; giữ one-parent/observation (`tests/LuxMap.Api.Tests/FaultReportTests.cs:286`, `tests/LuxMap.Api.Tests/FaultReportTests.cs:322`).
- `Evidence_fk_prevents_pole_deletion`: pole chỉ bị evidence giữ →409 ASSET_IN_USE; ảnh/pole còn nguyên; raw SQL chứng minh FK chặn.
- `Evidence_migration_preserves_rows_and_existing_constraints`: schema trước/sau, cột cũ NULL, index/FK/CHECK đầy đủ; Up/Down/reapply trên DB test; Down có liên kết phải từ chối nếu chốt D-8.
- `Evidence_spec_and_copy_plan_match_the_change`: spec form/response/filter nullable; bộ lọc copy không giữ ảnh mất pole; bundle/push giữ hình dạng hiện tại.

**Phải phá thử:** bỏ membership; bỏ lượt kiểm sau storage; bỏ scope đọc theo D-3; bỏ khoá cột; bỏ so pole ở từng đường replay; bỏ FK/CHECK mới; chuyển filter sau phân trang. Mỗi phá phải làm đúng test đỏ, rồi khôi phục và đọc exit code; kiểm nhãn/after/media bằng bộ hồi quy evidence hiện có (`tests/LuxMap.Api.Tests/WorkOrderTests.cs:990`, `tests/LuxMap.Api.Tests/WorkOrderTests.cs:1005`, `docs/agents/traps/testing.md:63`). Không thực hiện phá thử ở pha 1.

## D-item — chờ Mỹ chốt

- **D-1 — Nullable và ảnh cũ.** Đề xuất: input/DB/DTO nullable, thiếu giữ NULL, không backfill. Thay thế: bắt buộc cho ảnh mới. Hệ quả: phương án bắt buộc làm app cũ và ảnh tổng quan bị từ chối; không giải quyết hợp lý ảnh cũ (`src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:105`, `docs/api-contract-v1.1.md:932`).
- **D-2 — Tập cột và geometry.** Đề xuất: tái dùng toàn bộ `Targets`, nhận geom NULL, dùng lại 404/409 mục 3. Thay thế: chỉ cột mang fault hoặc áp chốt geometry của pole-photos. Hệ quả: loại cột survey/GPS hoặc cột chưa định vị dù bằng chứng không công bố trạng thái (`src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:90`, `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:51`).
- **D-3 — Liên xã và ràng buộc DB.** Đề xuất: xã ảnh theo phiếu, FK pole đơn, cho toàn bộ WO-12 trong scope; thêm scope cột độc lập khi list/media/replay. Thay thế A: chỉ cùng xã phiếu + FK ghép DB-only, thu hẹp WO-12; thay thế B: xã ảnh theo cột như PPH, phải đổi FK cha và ngữ nghĩa scope evidence. Hệ quả: không thể vừa giữ toàn bộ cột liên xã, vừa giữ xã phiếu, vừa ép ảnh/cột cùng xã (`docs/database/erd.md:30`, `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:194`, `docs/contract-drift.md:1858`).
- **D-4 — Tính hợp lệ sau upload.** Đề xuất: kiểm trước/sau storage, khoá phiếu → cột và đọc lại; membership chỉ bắt buộc lúc tạo, không dùng tập hiện tại để xoá lịch sử đọc. Thay thế: chỉ kiểm trước hoặc chỉ dựa FK. Hệ quả: FK chỉ chứng minh cột tồn tại, không chứng minh còn thuộc phiếu (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:176`, `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:140`).
- **D-5 — Idempotency.** Đề xuất: so thêm nullable pole ở mọi đường replay, khác →409. Thay thế A: giữ cha + nhãn, có thể trả nhầm ảnh cột; thay thế B: hash toàn nội dung, mở rộng semantics/chi phí và phạm vi ticket (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:214`, `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:120`).
- **D-6 — Filter list.** Đề xuất: `?pole_id=` trước phân trang, không khớp → trang rỗng. Thay thế: client tải đủ mọi trang rồi lọc. Hệ quả: bỏ filter giảm thay đổi API nhưng màn ảnh theo cột phải tải nhiều hơn và không thể lấy total theo cột từ một trang (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:238`, `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:185`).
- **D-7 — Ảnh fault.** Đề xuất: không nhận/lưu pole trực tiếp; DTO pole NULL và CHECK bảo vệ. Thay thế: sao chép từ fault hoặc cho client gửi. Hệ quả: phát sinh hai nguồn quan hệ và yêu cầu kiểm khớp fault/cột; mở rộng API fault ngoài nhu cầu chính (`src/LuxMap.Modules.WorkOrders/FaultPhotosController.cs:33`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:123`).
- **D-8 — Index và rollback.** Đề xuất: index pole đầy đủ, không backfill; Down đối xứng nhưng từ chối khi còn liên kết cột, rollback app giữ schema. Thay thế: index ghép phục vụ filter sau khi có nhu cầu/đo đạc; hoặc Down luôn drop cột. Hệ quả: thêm index làm tăng chi phí ghi; drop cột luôn sẽ mất thông tin ảnh thuộc cột nào (`docs/agents/traps/migrations.md:7`, `src/LuxMap.Persistence/Migrations/20261004153825_AttachPhotosToFaults.cs:54`).

**Dừng tại pha 1, chờ Mỹ chốt D-item; không chuyển sang hiện thực** (`.ai/tasks/EVIDENCE-POLE.md:82`).