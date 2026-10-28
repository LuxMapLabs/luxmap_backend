---
ticket: ASSET-PHOTOS
title: Ảnh cho tủ điện + cột, và trạng thái mạch điện (control + power_state)
status: ready
phase: 2
owner: codex
branch: docs/ASSET-PHOTOS-p1
contract_refs:
  - "Contract mục 5.1–5.6 (assets, map), 0 (quy ước), 1 (enum) — chưa có ảnh gắn tài sản, chưa có trạng thái mạch"
depends_on:
  - "Yêu cầu của FE (WP5) 27/10/2026 — nội dung ở mục Bối cảnh; chưa được Mỹ duyệt"
---

## Bối cảnh

FE (WP5) gửi Mỹ 27/10/2026, trong đó hai việc chạm bề mặt API (phải đặc tả trước khi hiện thực):

1. **"Làm vụ ảnh cho cabinets + poles"** — tủ điện và cột cần có ảnh gắn thẳng vào **tài sản** (không phải ảnh khảo sát, ảnh sự cố, ảnh before/after).
2. **"Trả thêm status của mạch điện"** — FE chỉ cần **một trạng thái để biết mạch đó có điện hay không**.

Hai việc còn lại trong tin của FE **đã có sẵn** (Claude đã xác minh trên `origin/dev` và sẽ báo FE, không phải việc của ticket này):
`GET /auth/me` đã trả `communes[] {commune_id, name, bbox, center}` và `map_bounds` (COMMUNE-BBOX, drift CB-3…CB-6, đã nằm trong build VPS `02aebc4`).
Rơ-le: một thiết bị có 1..32 rơ-le, mỗi rơ-le cấp đúng một mạch, một mạch có 0 hoặc 1 rơ-le (`feeder_control`, khoá chính `feeder_id`, UNIQUE `(node_id, relay_no)`, CAB-4).

### Hiện trạng đã kiểm sơ bộ (chưa phải kết luận — Codex phải tự xác minh lại từ code)

**Ảnh tài sản.** Không có endpoint nào nhận hay trả ảnh gắn tài sản; `AssetsController` (`/assets/…`) chỉ có CRUD; entity tài sản không có cột khoá ảnh. Ảnh đang có nhưng gắn nghiệp vụ:
`/work-orders/{id}/pole-photos` (`WorkOrderPolePhotosController`, có thumbnail / original / review / decision), `/faults/{id}/photos`, `/work-orders/{id}/evidence`, ảnh khảo sát (`frames`), ảnh công dân.
Hạ tầng dùng lại được: MinIO, `ThumbnailFactory.JpegOnly`, nhận JPEG theo magic bytes, mọi byte đi qua API (không presigned), ghi object trước / commit row sau. Contract nói có **4 bucket** (`survey / evidence / video / citizen`).
`deploy/backup.sh` (vòng lặp bốn bucket viết cứng) và `scripts/copy_dev_to_supabase.py` (`PLAN`) liệt kê bucket / bảng — bảng hoặc bucket mới phải vào đó.

**Trạng thái mạch.** `feeder` không có cột trạng thái nào. `FeederListItem` có `feeder_id, external_ref, feeder_name, commune_id, commune_name, has_geometry, pole_count, cabinet, updated_at, updated_by, updated_by_name`. `feeder_control.control_mode`
(`on | off | auto`, null cho tới khi thiết bị báo) đã có trong `GET /assets/iot-nodes/{id}` → `relays[]` (`IotNodeRelay`: `relay_no, feeder_id, control_mode, mode_reported_at`) nhưng chưa trả ở `GET /assets/feeders` và `GET /map/cabinets`
(`CabinetProperties` chỉ có `feeder_ids[]`). **BE chưa biết mạch có điện hay không**: trên `dev` chỉ có `/device/commands`, không có endpoint ingest telemetry, không bảng lưu dòng điện / điện áp (drift I-17 định nghĩa payload, phía nhận IOT-09/10 là của BE2 – Đạt, chưa vào `dev`).
`control_mode` là **chế độ** thiết bị báo lại, không phải trạng thái có điện: không dùng nó làm "có điện".

### Hướng Claude đề xuất (Mỹ đã đồng ý ý tưởng 27/10/2026, chi tiết còn phải chốt qua D-item)

- **`control` + `power_state` trên mạch:** `control = {node_id, relay_no, control_mode, mode_reported_at}` hoặc `null` khi mạch chưa nối rơ-le, thêm vào item / detail của `GET /assets/feeders`; trên `GET /map/cabinets` **thêm** `feeders[] {feeder_id, relay_no, control_mode, power_state}` và **giữ nguyên** `feeder_ids[]`.
  Không thêm vào `/map/poles`, `/map/segments` (tối đa 2000 cột mỗi lần).
- **`power_state` = `on | off | unknown`**, bước đầu **luôn `unknown`** (hình dạng đúng, dữ liệu chưa có — như `fixture_status` trước BE-15). Khi có telemetry: dòng điện mới nhất của rơ-le so với ngưỡng cấu hình qua `system_setting` → `on` / `off`; không có rơ-le hoặc thiết bị im lặng quá ngưỡng offline sẵn có (`NodeSilence`) → `unknown`. **`unknown` không gộp vào `off`.**
- **Ảnh tài sản (mặc định đề xuất):** mỗi tủ / cột nhiều ảnh (tối đa ~10); ảnh gốc + thumbnail đều qua API; Quản lý và kỹ sư tải lên, chỉ Quản lý xoá; có audit; **chưa** đưa vào sync offline ở bản đầu.

## Phải đọc trước

- `CLAUDE.md` + `docs/agents/traps/assets.md`, `media-storage.md`, `commune-scope.md`, `migrations.md`, `faults-workorders.md` (audit), `iot-lighting.md`, `deploy-ops.md`, `spatial-map.md`
- `src/LuxMap.Modules.Assets/` (`Crud/AssetsController.cs`, `Crud/AssetCrudService.cs`, `Entities/`), `src/LuxMap.Modules.Map/Features/` (`MapController.cs`, `MapProperties.cs`, `MapQueryService.cs`)
- Precedent ảnh: `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotosController.cs` + service, `FaultPhotosController.cs`, `src/LuxMap.Infrastructure.Storage/`, `ThumbnailFactory`, `.ai/results/BE-11-p1.md`, `BE-24-p*.md`, `BE-35-p1.md` (đếm theo bucket / `storage.buckets[]`)
- `src/LuxMap.Modules.Telemetry/` (`Entities/FeederControl.cs`, `Registry/IotNodeContracts.cs`, `IotNodeRegistryService.cs`, `NodeSilence`), `docs/contract-drift.md` (I-6, I-12, I-17, CAB-*, LC-*, COMMUNE-BBOX)
- `docs/api-contract-v1.1.md` (mục 5.1–5.6, 0, 1 và changelog), `docs/openapi/luxmap-v1.5.json`, `deploy/backup.sh`, `scripts/copy_dev_to_supabase.py`
- `LuxMapPolicies.Matrix` (`LuxMap.Shared`) và `RoleCapabilityMatrixTests` / `CapabilityMatrixTests` / `CapabilityPolicyCoverageTests` (capability mới)

## Yêu cầu (Phase 1)

Khảo sát, đánh giá, đề xuất thiết kế, ghi D-item. Kết quả: `.ai/results/ASSET-PHOTOS-p1.md`, hai phần A (ảnh tài sản) và B (trạng thái mạch). Phải trả lời:

### A. Ảnh cho tủ điện + cột

1. **Mô hình lưu:** một bảng chung (`asset_photo` với `asset_kind` + `asset_id`) hay hai bảng (`pole_photo`-kiểu / `cabinet_photo`)? Chú ý trùng tên với `pole_photo` **đã có** của phiếu công việc. Giá từng phương án: FK thật tới cột / tủ (`Restrict`), `commune_id` + `ICommuneScoped` + `HasCommuneReference`, `CommuneWriteGuard`, `commune_id` suy từ cha, `HasContractEnum` + CHECK, quy ước ID có prefix (`prefix` mới trong `PrefixedId.cs`, tối thiểu N chữ số, thứ tự `ORDER BY created_at, length(id), id`).
2. **Object store:** dùng lại bucket nào hay bucket mới? Hệ quả của bucket mới: tạo bucket trong `deploy/compose.yml` (`minio-mc`), `deploy/backup.sh`, `scripts/copy_dev_to_supabase.py`, `storage.buckets[]` ở `/admin/system/status` (BE-35), Contract "đủ 4 bucket". Quy ước khoá object (id hay hash), thumbnail 320 / q80, bỏ metadata EXIF tường minh.
3. **Bề mặt API:** endpoint đề xuất (upload multipart, danh sách, ảnh gốc, thumbnail, xoá, đặt ảnh bìa nếu cần), hình dạng response, giới hạn (số ảnh mỗi tài sản, dung lượng, chỉ JPEG theo magic bytes), mã lỗi, **capability** (có cần mới không, danh sách vai trò nêu tên, test kỳ vọng literal), phạm vi xã (ngoài xã → 404).
4. **Ảnh có hiện trong đâu:** `GET /assets/poles/{id}` và danh sách tủ / cột (`photo_count`, ảnh bìa?), `GET /map/poles/{id}` (đang trả frame khảo sát — ảnh tài sản có chen vào không, tách hay gộp?), `GET /map/cabinets`. Không làm phình `GET /map/poles` (2000 cột).
5. **Vòng đời:** D-R16 nói ảnh **không xoá** sau bảo hành — xoá ảnh tài sản có mâu thuẫn không (xoá mềm?); `DELETE /assets/poles/{id}` và `/cabinets/{id}` khi còn ảnh; dời cột; import tài sản (`AssetImportService`); ảnh `unknown` / đổi bóng. Kiểm audit: `IAudited`, **một** `AuditEvent` mỗi `SaveChanges`, `AuditAction` mới thì cần migration nới `ck_audit_event_action` (liệt kê `Up()` / `Down()` dự kiến, chưa viết).
6. **Offline (WP6):** có cần sync op cho ảnh tài sản không; hậu quả nếu chỉ online (đối chiếu `BLOCKED_BY_EARLIER_OP`); kỹ sư có tải ảnh tài sản ngoài vùng sóng được không (hỏi: đề xuất mặc định là chưa, ghi thành D-item).

### B. Trạng thái mạch điện

7. **Xác minh hiện trạng:** `feeder_control` / `IotNodeRelay` / `FeederListItem` / `CabinetProperties` đúng như mô tả trên không (dán dòng code thật); telemetry ingest còn thiếu không (có endpoint, bảng, job nào chạm dòng điện / điện áp — dán output `grep`).
8. **Bề mặt API:** hình dạng `control` và `power_state` trên `GET /assets/feeders` (item + detail) và `feeders[]` trên `GET /map/cabinets` (giữ `feeder_ids[]`); join `feeder_control` qua query filter xã **không N+1** (một truy vấn, kiểm bằng log SQL / `EXPLAIN`); enum `power_state` lưu dạng chữ (`HasContractEnum` + CHECK nếu có cột, hay tính lúc đọc?), `WireEnum`; `control_mode` đã có enum `FeederControlMode`.
9. **`power_state` bước đầu luôn `unknown`:** nêu rõ cách tránh trường hợp "luôn `unknown` mà FE coi là `off`"; ghi cách mở rộng khi telemetry vào (ngưỡng ở `system_setting`, `NodeSilence`) mà **không đổi hình dạng** response; câu hỏi cho nhóm firmware / testbed (thiết bị có đo dòng điện hoặc điện áp từng rơ-le không, payload và chu kỳ).
10. **Tác động nhóm:** BE2 – Đạt (IOT-09/10), WP5 (hiển thị `unknown`, `control_mode` kèm `mode_reported_at` vì đó là điều thiết bị báo lại), WP6 (qua sync bundle nếu cần).

### Chung

11. **Tác động Contract:** mục nào thêm, bản **v1.21** `SELF-SIGNED`, spec OpenAPI 141 → bao nhiêu op, drift entry cần ghi (Contract luật thắng chữ), phần nào WP5 / WP6 phải xác nhận; cả A và B **chỉ thêm**, client cũ không đổi.
12. **Kế hoạch Phase 2:** tách PR hay chung? (đề xuất: hai migration / hai PR nếu A có bảng + bucket còn B không migration) — thứ tự, tên test, trường hợp, phá thử (chưa viết test).

## KHÔNG ĐƯỢC làm

- Phase 1: không sửa code `src/`, `tests/`, không migration, không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json` / `docs/contract-drift.md`, `deploy/`, `scripts/`.
- Không thêm giá trị vào enum đã publish (`fixture_status`, `fault_type`, `wo_status`, …); `power_state` là enum **mới**, `AuditAction` chỉ **đề xuất** và ghi D-item.
- Không coi đề xuất FE hay "hướng Claude đề xuất" là đã duyệt; không tự chọn phương án kiến trúc — ghi D-item cho Mỹ.
- Không dùng `control_mode` để suy ra "có điện"; không thêm `power_state` nhận giá trị `on` / `off` khi không có dữ liệu đo.
- Không viết tên cột / constraint / endpoint / bucket từ trí nhớ; đọc code / snapshot thật rồi dán output.
- Không chạy `dotnet test` toàn suite, không chạm `luxmap_dev`, không kết nối DB nào.
- Không sửa `tracking.html`, `docs/tasks-backend.csv`, `.ai/tasks/current.md`.

## Phase 1 dừng ở đâu

Khảo sát xong thì **dừng hẳn**, ghi `.ai/results/ASSET-PHOTOS-p1.md` (D-item đánh số, mỗi D-item có phương án + đề xuất của Codex), chờ Mỹ chốt. Không viết code, kể cả khi đã rõ phải sửa gì.

---

## Phase 2-B — trạng thái mạch điện (28/10/2026)

Nguồn quyết định: **mục "Quyết định D-01…D-12" ở cuối `.ai/results/ASSET-PHOTOS-p1.md`** (đã chốt; phần B là D-07, D-08, D-09, D-10, D-11, D-12). Phần "KHÔNG ĐƯỢC làm" bên trên về code / Contract
**hết hiệu lực cho Phase 2-B**, nhưng mọi ràng buộc còn lại giữ nguyên. **Phần A (ảnh tài sản) KHÔNG nằm trong lượt này** — PR sau. Branch: `feat/ASSET-FEEDER-STATE` (worktree riêng, đã tạo từ `origin/dev`).

### Làm

1. **Không migration, không cột mới, không `HasContractEnum` / CHECK** (D-08: tính lúc đọc). Nếu thấy bắt buộc phải có migration thì **dừng và ghi D-item**, đừng tự thêm.
2. **Enum `FeederPowerState`** = `On | Off | Unknown`, JSON chuỗi `on|off|unknown` qua options chung, đặt ở chỗ mọi module cần dùng mà không tạo vòng tham chiếu. **Một hàm duy nhất** quyết định giá trị
   (luật một định nghĩa) — bước này nó trả `Unknown` vô điều kiện (kể cả mode `on` và node online); cả Assets và Map gọi nó, không chép. Không bao giờ suy `power_state` từ `control_mode`.
3. **`GET /assets/feeders` (item) và chi tiết mạch:** thêm `control` (`{node_id, relay_no, control_mode, mode_reported_at}` hoặc `null` khi mạch chưa nối rơ-le; có rơ-le chưa báo thì `control_mode` và `mode_reported_at` đều `null`;
   node offline vẫn giữ mode / time lịch sử) và `power_state`. Không đưa hai trường vào `PUT` / `POST` / import. Chi tiết dùng lại item (`feeder`).
4. **`GET /map/cabinets`:** **giữ nguyên `feeder_ids[]`**, thêm `feeders[] {feeder_id, relay_no, control_mode, mode_reported_at, power_state}` — **mọi feeder của tủ** (kể cả không rơ-le: `relay_no`, `control_mode`, `mode_reported_at` = `null`,
   `power_state` = `unknown`), cùng phạm vi xã và cùng thứ tự với `feeder_ids`; tủ không feeder → `[]` ở cả hai key. Lấy `feeder_ids` từ cùng tập kết quả để không lệch.
5. **Không đụng** `/map/poles`, `/map/segments`, sync bundle, `PUT /assets/iot-nodes/{id}/relays/{n}`, telemetry / ingest, capability, `system_setting`.
6. **Truy vấn (D-08, ngoại lệ đã chốt):** Assets **không** tham chiếu Telemetry. Assets: một **port do Assets khai** (theo `ICabinetDeviceLookup`), Telemetry hiện thực, **một truy vấn control theo lô cho cả page** — không N+1, query filter xã của `feeder_control` / `iot_node` giữ nguyên,
   không `IgnoreQueryFilters`, không SQL raw bỏ scope. Map đã tham chiếu Telemetry: LEFT JOIN trực tiếp, thiếu rơ-le vẫn giữ feeder (không INNER JOIN). Giữ `count + page` sẵn có.
   Kiểu `control_mode` ở port: **không tạo enum thứ hai trùng nghĩa** `FeederControlMode`; nếu phải dời kiểu về `LuxMap.Shared` thì giữ nguyên tên wire `on|off|auto` và ghi lý do vào p2.
7. **Contract / drift / spec / docs, cùng PR:**
   - Contract: nâng **heading và changelog lên v1.21 `SELF-SIGNED`** — heading hiện vẫn v1.19 và `info.version` của spec vẫn 1.19 dù changelog có v1.20 (sót của PR #190; sửa luôn, nêu rõ trong p2). Mục 5.3.1 (feeder), 5.6 (phân biệt `control_mode` = chế độ thiết bị **báo lại**, không phải đo điện), mô tả Map cabinets (CAB-8/11),
     changelog v1.21 ghi rõ chỉ phần B đã hiện thực, phần ảnh tài sản (A) chưa; `CLAUDE.md` dòng "bản hợp nhất v…" đồng bộ.
   - Drift: entry mới (mã do Codex chọn, **không dùng lại CAB / LC / CB / CR**; đủ Decision / Decision maker / Date / Scope; Mỹ **ủy quyền Claude chốt 28/10**), ghi: `power_state` luôn `unknown` tới khi có telemetry đo điện; "có dòng tải" ≠ "có điện áp";
     bốn câu hỏi firmware ở D-09 là việc còn mở; WP5 phải hiện nhãn "Chưa có dữ liệu điện", cấm `== 'on' ? 'Có điện' : 'Mất điện'`; `control_mode` kèm `mode_reported_at`; client sinh từ spec coi `control` / `mode_reported_at` là nullable (SPEC-NULLABLE, `$ref` trần).
   - Spec: `dotnet swagger tofile` → `gen_consolidated_spec.py` → redocly lint (`.ai/context/commands.md`); **141 op giữ nguyên**, lint baseline 9 warning không tăng; kiểm schema viết tay đè trong generator; không sửa JSON sinh bằng tay.
   - Trap `docs/agents/traps/iot-lighting.md` hoặc `assets.md` nếu có bẫy mới. Không đụng `tracking.html`, `docs/tasks-backend.csv`, `.ai/tasks/current.md` (Claude cập nhật sau).
8. **Test (khuôn D-12):** `LuxMapTestHost` (container PostGIS riêng lượt chạy; không `luxmap_dev`); không ghim ID bằng literal; tiền tố tài khoản riêng, teardown theo thứ tự FK `Restrict` (`feeder_control` / `iot_node` trước `feeder` và xã).
   - `FeederStateTests`: item / detail / `control` null, rơ-le chưa báo, `on` / `off` / `auto` đều `power_state = unknown`; map liệt kê mọi feeder kể cả không rơ-le, giữ `feeder_ids` và thứ tự; hai xã, cùng tủ; **không** thêm field vào `/map/poles`, `/map/segments`, bundle.
   - `FeederStateQueryTests`: số câu SQL **không tăng** từ 1 → 200 feeder (tìm kỹ thuật đếm SQL đã có trong repo, ví dụ interceptor; không có thì nêu cách khác); SQL của cabinet bbox vẫn dùng GIST 4326 (`EXPLAIN` từ SQL EF thật).
   - `OpenApiSpecTests` và ba test capability sẵn có: schema mới, enum chuỗi, nullable, additive, 141 op.
   - **Phá thử mỗi chốt:** ánh xạ `mode → power` (cho `on` thành `on`) → test đỏ; INNER JOIN làm mất feeder không rơ-le → đỏ; query trong `foreach` → số câu SQL đỏ; bỏ `feeder_ids` → đỏ. Khôi phục bằng `cp` / `touch` (không `mv`), đọc **mã thoát** của `dotnet test`.

### Cách chạy

- `dotnet build --disable-build-servers` với `-warnaserror` như repo (`dotnet restore LuxMap.slnx -warnaserror` phải qua; ImageSharp advisory đã được tắt ở #193); `dotnet test --disable-build-servers`. **Chỉ một lượt `dotnet test` tại một thời điểm.**
  Nếu sandbox chặn Docker / mạng / ghi ngoài worktree thì **dừng ở chỗ đó và ghi rõ** — đừng giả vờ test xanh; Claude sẽ tự chạy test độc lập.
- Ghi `.ai/results/ASSET-PHOTOS-p2b.md`: đã làm gì, output thật (SQL đếm, `dotnet test` mã thoát và số pass / skip, mỗi lần phá thử, số op spec trước / sau), D-item phát sinh (nếu có thì **dừng ở đó và ghi**, đừng tự chọn).
- Commit theo Conventional Commits (`feat(ASSET-PHOTOS): …`), một thay đổi logic một commit, stage theo đường dẫn tường minh (không `git add -A`), **không push**, không thêm trailer `Co-Authored-By`.
