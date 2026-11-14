---
ticket: EVIDENCE-POLE
title: Ảnh của phiếu (`POST /work-orders/{id}/evidence`) gắn được với một cột — thêm `pole_id`
status: in_progress
phase: 2
owner: codex
branch: feat/EVIDENCE-POLE
contract_refs:
  - "§5.5 — POST /work-orders/{id}/evidence (Contract văn bản cũ; bản đang chạy ở contract-drift.md mục BE-24 + EV-1 / EV-3)"
depends_on:
  - "EV-3 (contract-drift.md, 14/11/2026): mọi phiếu không sửa chữa nhận observation — đã merge #251"
---

## Bối cảnh

Mỹ (14/11/2026, nguyên văn): *"bạn không có poleid à, việc này khiến cho up ảnh không biết của cột nào"*.
`POST /work-orders/{id}/evidence` chỉ gắn ảnh vào **phiếu**; `repair_evidence` không có `pole_id`, nên ảnh trên một phiếu nhiều
cột (khảo sát cả tuyến, kiểm tra / sửa chữa nhiều sự cố, cập nhật GPS cột) không biết là của cột nào. WP6 (app kỹ sư, FM-18)
upload ảnh này; WP5 hiện ảnh ở màn chi tiết phiếu để nghiệm thu. Đã có `POST /work-orders/{id}/pole-photos` gắn `pole_id`, nhưng
đó là luồng ảnh **có AI + Quản lý quyết + công bố trạng thái cột** (POLE-PHOTO), khác nghĩa với ảnh bằng chứng thường.

Ticket chạm **schema + bề mặt API** ⇒ theo `.ai/README.md` bắt buộc hai pha. **Đây là PHA 1: khảo sát và đề xuất, KHÔNG sửa code.**

## Phải đọc trước

- `AGENTS.md` (= `CLAUDE.md`) — đặc biệt: Nguồn sự thật, Phạm vi xã, Mô hình dữ liệu, Bẫy chung (migration, ảnh, khoá hàng,
  `client_op_id`), Test.
- `docs/agents/traps/faults-workorders.md`, `docs/agents/traps/media-storage.md`, `docs/agents/traps/migrations.md`,
  `docs/agents/traps/commune-scope.md`, `docs/agents/traps/deploy-ops.md` (`scripts/copy_dev_to_supabase.py` PLAN).
- `docs/contract-drift.md` mục EV-1 / EV-2 / EV-3, BE-24, WO-12, POLE-PHOTO (PP-*), R-3 (ảnh sự cố).
- `docs/database/erd.md` (quyết định Q-x liên quan `repair_evidence`).
- Code: `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs`, `WorkOrdersController.cs` (UploadEvidence / Evidence),
  `FaultPhotosController.cs`, `WorkOrderPoles.cs` (`WorkOrderPoleService`, tập cột của phiếu WO-12),
  `WorkOrderPolePhotoService.cs` (cách `/pole-photos` kiểm `pole_id`: `Target(...)`, mã lỗi), `Entities/RepairEvidence.cs`,
  `Configurations/WorkOrderConfigurations.cs` (CHECK của `repair_evidence`), `src/LuxMap.Modules.Sync/**` (bundle / push có
  chạm evidence không), luồng xoá / dời cột ở `src/LuxMap.Modules.Assets/**` (FK mới tới `pole` ảnh hưởng gì).
- Test: `tests/LuxMap.Api.Tests/WorkOrderTests.cs` (khối evidence), `FaultReportTests.cs` (ảnh sự cố, CHECK).

## Pha 1 — phải trả lời (ghi vào `.ai/results/EVIDENCE-POLE-p1.md`, tiếng Việt)

Trích file:dòng cho mọi khẳng định; schema thật thì dán output (đọc migration / snapshot), không viết từ trí nhớ.

1. **Hiện trạng:** `repair_evidence` có những cột, FK, CHECK, index nào? Ảnh evidence đang được đọc ở đâu (list, thumbnail,
   original, sync bundle, follow-up, báo cáo)? Có chỗ nào đang suy ra cột từ ảnh không?
2. **`pole_id` bắt buộc hay tuỳ chọn?** Ảnh nào hợp lý là *không* của cột nào (tổng quan tuyến, sự cố không có cột — `POST /faults`
   cho phép sự cố chỉ có `location`)? App cũ (WP6) không gửi `pole_id` thì sao? Đề xuất + lý do; đây là **D-item**.
3. **Cột nào hợp lệ:** tái dùng đúng tập cột của phiếu (`WorkOrderPoleService`, WO-12) như `/pole-photos`? Cột ngoài xã, cột
   không tồn tại, cột không thuộc phiếu ⇒ mã lỗi nào (đề xuất **dùng lại** mã của `/pole-photos`, không đặt mã mới)? Cột chưa có
   hình (`geom` null) có được nhận không (ảnh bằng chứng không công bố trạng thái)?
4. **Kiểm lại sau khi ghi object:** `StoreAsync` khoá phiếu và kiểm lại sau khi ghi ảnh — cần kiểm lại cột còn thuộc phiếu không?
   Thứ tự khoá (phiếu → cột?) theo luật "Khoá hàng" của AGENTS.md.
5. **`client_op_id`:** replay hiện so phiếu + nhãn. Gửi lại cùng khoá nhưng **khác cột** ⇒ 409 `IDEMPOTENCY_CONFLICT`? Đề xuất.
6. **Schema:** cột `pole_id` (kiểu, nullable), FK tới `pole` (`Restrict` theo mặc định repo), index, CHECK (vd. ảnh sự cố
   `fault_id IS NOT NULL` thì `pole_id` NULL?), `commune_id` của ảnh vẫn suy từ phiếu — có cần kiểm cột cùng xã ở DB không
   (CommuneWriteGuard không kiểm "khớp cha")? Migration `Up()` / `Down()` đối xứng; có cần backfill không (đề xuất: không —
   ảnh cũ để `NULL`, không đoán).
7. **FK `Restrict` tới `pole` chặn gì:** xoá cột / gộp cột / import có đường nào xoá `pole` không? Nếu có, hiện trả gì (409 sạch
   hay 500)? So với `work_order_pole_photo` đã có FK tới `pole`.
8. **Bề mặt API:** form field `pole_id`; `EvidenceItem` thêm `pole_id` (nullable); `GET /work-orders/{id}/evidence` có cần lọc
   `?pole_id=` không (WP5 / WP6 hiện ảnh theo cột)? Đề xuất tối thiểu.
9. **Ảnh sự cố `POST /faults/{id}/photos`:** dùng chung service — để nguyên (sự cố đã có `pole_id`) hay cũng nhận? Đề xuất.
10. **Sync offline:** bundle / push có cần mang `pole_id` của ảnh không? (Ảnh không đi qua `/sync/push` — xác nhận.)
11. **Chỗ phải sửa cùng:** OpenAPI (`luxmap-v1.json` là CODEOWNERS), `gen_consolidated_spec.py`, drift (mục mới **EV-4**, SELF-SIGNED
    sau khi Mỹ chốt), `docs/database/erd.md`, `scripts/copy_dev_to_supabase.py` (cột mới có cần gì không), `tracking.html`.
12. **Kế hoạch test pha 2:** liệt kê test (tên + ca) và chốt chặn nào cần phá thử.

Cuối file: danh sách **D-item** (`D-1`, `D-2`, …) mỗi cái có phương án đề xuất + phương án thay thế + hệ quả. Mỹ chốt rồi mới có pha 2.

## KHÔNG ĐƯỢC làm

- **Pha 1 không sửa bất kỳ file nào** ngoài việc trả báo cáo; không tạo migration, không build, không chạy test.
- Không sửa `docs/api-contract-v1.1.md`, `docs/openapi/*.json`.
- Không đổi `/pole-photos` (ảnh có AI, quyết định, công bố trạng thái cột) và không gộp hai luồng ảnh — CLAUDE.md: "`SurveyFrame` và
  `RepairEvidence` là hai luồng ảnh riêng"; nếu thấy nên gộp thì ghi thành D-item, không tự làm.
- Không đổi luật hoàn thành (chỉ sửa chữa bắt ảnh `after`), không đổi tập nhãn theo loại phiếu (EV-3).
- Không suy `pole_id` từ toạ độ ảnh (lat/lng) — nếu thấy cần thì ghi D-item.
- Không tự chọn phương án kiến trúc — ghi thành D-item.
- Không push, không mở PR.

## Pha 1 dừng ở đâu

Khảo sát xong thì **dừng hẳn**: trả báo cáo pha 1, chờ Mỹ chốt D-item. Không viết code ở pha 1, kể cả khi đã rõ phải sửa gì.

---

## PHA 2 — Mỹ chốt D-item (14/11/2026) ⇒ implement

Báo cáo pha 1: `.ai/results/EVIDENCE-POLE-p1.md`. Mỹ chốt **theo đề xuất**, riêng **D-3 chọn phương án A (cùng xã)**:

| D | Quyết định |
|---|---|
| D-1 | `pole_id` **tuỳ chọn** ở form, DB, DTO. Thiếu ⇒ lưu / trả `null`. Không backfill, không suy từ lat/lng |
| D-2 | Cột phải thuộc **toàn bộ** `WorkOrderPoleService.Targets(id)` (gọi trực tiếp, không chép predicate). Cột không tồn tại / ngoài scope ⇒ **404 `POLE_NOT_FOUND`**; thấy được nhưng ngoài tập ⇒ **409 `POLE_NOT_IN_WORK_ORDER`** (dùng lại mã của `/pole-photos`, không mã mới). **Nhận cột `geom` NULL** (không tái dùng chốt geometry của `/pole-photos`). Kiểm quyền phiếu trước (404 `WORK_ORDER_NOT_FOUND` giữ nguyên) |
| D-3 | **Phương án A: cột phải cùng xã với phiếu.** Service: cột khác `commune_id` của phiếu ⇒ **409 `POLE_NOT_IN_WORK_ORDER`** (không mã mới). DB: FK ghép **DB-only** `(pole_id, commune_id)` → `pole(pole_id, commune_id)` tái dùng `ak_pole_photo_target` (`20261015153829_AddWorkOrderPolePhotos.cs:137`), `ON DELETE RESTRICT`; **không** thêm alternate key EF làm khoá `Pole.CommuneId`. `commune_id` của ảnh vẫn suy từ phiếu. Không cần kiểm scope cột riêng ở list / media / replay (ảnh và cột cùng xã) |
| D-4 | Kiểm cột **trước** khi ghi object và **kiểm lại** sau khi ghi, trong transaction: đọc qua filter trước → khoá **phiếu → cột** (`SELECT 1 … FOR UPDATE` qua `ExecuteSql`) → đọc lại bằng LINQ. Không giữ khoá qua upload / decode. Replay vẫn chạy trước các kiểm dành cho ảnh mới |
| D-5 | Replay so thêm `pole_id` **chính xác, kể cả NULL** ở **mọi** đường replay (sớm, sau storage, nhánh bắt unique violation): khác cột, `NULL→cột`, `cột→NULL` ⇒ **409 `IDEMPOTENCY_CONFLICT`** |
| D-6 | `GET /work-orders/{id}/evidence?pole_id=` tuỳ chọn, lọc **trước** count / phân trang, giữ thứ tự hiện tại; không khớp ⇒ trang rỗng (không tra cột) |
| D-7 | `POST /faults/{id}/photos` **không** nhận `pole_id`; row ảnh sự cố giữ `pole_id` NULL; CHECK mới `fault_id IS NULL OR pole_id IS NULL` (tên do pha 2 đặt, khai `HasCheckConstraint`) |
| D-8 | Migration: thêm cột `pole_id text NULL`, FK (D-3), index dẫn đầu `pole_id`, CHECK (D-7). Không backfill. `Down()` đối xứng nhưng **từ chối** (RAISE) khi còn `pole_id IS NOT NULL`, theo tiền lệ `20261004153825_AttachPhotosToFaults.cs:54`. Đọc `Up()` / `Down()` trước khi xong: không `DropIndex` / `DropColumn` / `AlterColumn` ngoài yêu cầu, không `xmin` |

### Phải làm (pha 2)

1. Entity `RepairEvidence.PoleId`, cấu hình, migration + snapshot (`dotnet ef migrations add AddEvidencePole …`), FK ghép bằng `migrationBuilder.Sql` như tiền lệ.
2. Controller: form field `pole_id` tuỳ chọn ở POST evidence; query `pole_id` tuỳ chọn ở GET evidence. `EvidenceItem` thêm `pole_id` (nullable), mapping chung cho POST / replay / GET / ảnh sự cố.
3. Service theo D-2…D-6. Kiểm nằm trong service (không dựa DataAnnotations).
4. Test (trong `WorkOrderTests` / `FaultReportTests`, giữ `AssetDatabaseCollection`, không ghim ID literal, teardown xoá evidence trước pole): theo kế hoạch mục 12 của báo cáo pha 1, bỏ các ca liên xã (D-3 A) và thay bằng: cột khác xã ⇒ 409 + raw SQL chứng minh FK ghép chặn. Ca bắt buộc: không gửi `pole_id` (app cũ) vẫn 201 + `pole_id: null`; cột thuộc phiếu ⇒ 201, trả / liệt kê / lọc đúng; 404 / 409 các ca D-2; geom NULL nhận; đổi tập cột trong lúc ghi object (`DuringWrite`) ⇒ không row; replay D-5 cả ba chiều; filter D-6 đếm trước phân trang; ảnh sự cố `pole_id` null + CHECK chặn; xoá cột đang có ảnh ⇒ 409 `ASSET_IN_USE`; migration Down từ chối khi còn liên kết.
5. OpenAPI: build `src/LuxMap.Api`, xuất `docs/openapi/luxmap-v1.json`, chạy `docs/openapi/tools/gen_consolidated_spec.py` (lệnh ở `.ai/context/commands.md`), cập nhật summary POST / GET evidence trong generator. Không viết tay schema đè schema sinh.
6. Drift: mục mới **EV-4** ngay sau EV-3 trong `docs/contract-drift.md` (Mỹ 14/11/2026, SELF-SIGNED, chạm API ⇒ nền tạm tới FW, WP5 / WP6 chưa báo) + cập nhật dòng `POST /work-orders/{id}/evidence` / `GET` trong bảng BE-24. `docs/database/erd.md`: thêm cột + quan hệ. `tracking.html`: card mới ngay dưới `<h1>`.
7. `.ai/results/EVIDENCE-POLE-p2.md`: đã làm gì, file nào, output build / test **chép từ terminal**, Up / Down của migration, và những gì chưa chạy được.

### Lệnh và giới hạn môi trường

- Build / test: `dotnet build --disable-build-servers`, `dotnet test … --disable-build-servers`. Test cần Docker (Testcontainers). Nếu sandbox không chạy được Docker / test thì **ghi rõ** trong p2 là chưa chạy — Claude sẽ chạy và phá thử.
- **Không commit, không push, không tạo branch** — để nguyên thay đổi trong cây làm việc; Claude review rồi commit.
- Giữ nguyên mục KHÔNG ĐƯỢC làm ở trên (trừ dòng "pha 1 không sửa file"); thêm: không đổi `/pole-photos`, không đổi luật hoàn thành, không sửa `docs/api-contract-v1.1.md`.
