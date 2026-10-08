# Chỗ lệch giữa code và Contract — log mới từ v1.4 (18/09/2026)

**Log cũ (v1.0 → v1.3, drift 1–43, quyết định A–F)** đã **archive** tại
[`docs/archive/contract-drift-v1.md`](archive/contract-drift-v1.md). Mọi mục trong đó đã được gộp vào
Contract v1.4 (xem changelog mục 10 của [`api-contract-v1.1.md`](api-contract-v1.1.md)) hoặc chuyển
thành Open item ở mục 9 của Contract. **Không sửa file archive.**

> 🗄️ **Mục đăng ký TRƯỚC 25/09/2026 là lịch sử (D-R18)** — chúng dùng tên vai trò cũ và nói "Quản trị"
> ghi tài sản, "Nhánh C". Không sửa lại; vai trò hiện hành ở mục *Registration v1.2* bên dưới và
> Contract v1.7 §2.
>
> 📄 **Nguồn phiếu hiện hành: v1.4 (27/09/2026)** — mục *Registration v1.4* bên dưới. Vai trò không đổi so
> với v1.2; D-R21, D-R24 **đã chốt hướng (SELF-SIGNED)**; D-R20, D-R22, D-R23 **chốt 07/10/2026**; D-R25…D-R28 **còn chờ quyết**.

**Nguyên tắc vận hành quyết định (FW-00, 07/09/2026)** giữ nguyên hiệu lực — năm mục ở đầu file
archive: (1) ghi ngay, bốn trường Decision / Decision maker / Date / Scope; (2) policy là một vai trò
chính xác; (3) im lặng > 3 ngày làm việc = APPROVE nếu không chạm API, ESCALATE nếu chạm, fallback
signer Dylan → `SELF-SIGNED`; (4) không "bàn thêm" vô thời hạn — phải có owner + deadline; (5) ghi ở
đúng tầng (deviation → file này; luật liên ticket → Contract; ràng buộc kỹ thuật → `CLAUDE.md`; tiến độ
→ `tracking.html`).

---

## Quyết định đã đăng ký

### JSON-ENUM — enum trong body JSON chỉ nhận chuỗi (04/10/2026)

| | |
|---|---|
| **Decision** | Sửa converter toàn cục về đúng Contract §0/§3.1: từ chối enum dạng số JSON (`2`, `999`) và chuỗi chữ số (`"2"`, `"999"`), gồm enum nullable. Trước converter nhận các giá trị này, có thể đổi nghĩa hoặc gây 500 ở CHECK DB; nay body bind enum sai trả **400 `VALIDATION_FAILED`** qua cơ chế lỗi hiện có. Chuỗi enum hợp lệ vẫn serialize snake_case; tên enum và hình dạng response giữ nguyên. |
| **Decision maker** | **Mỹ**, duyệt sửa toàn cục trong `.ai/tasks/JSON-ENUM.md` |
| **Date** | 04/10/2026 |
| **Scope** | `LuxMapJsonOptions`, cả MVC/HTTP JSON và các lượt deserialize thủ công dùng options chung. Query string và kiểm tra cục bộ giữ nguyên. Client gửi số phải chuyển sang tên enum trong Contract; không cần migration. |

Đây là thay đổi quan sát được với client gửi sai Contract. `audit_event.before_state/after_state` hiện được ghi bằng
options chung (enum có tên ra chuỗi); rà mã không thấy đường đọc lại audit thành enum. Chưa kiểm dữ liệu đã lưu trên DB
theo giới hạn ticket. HTTP regression trên `POST /work-orders` chỉ biên dịch ở lượt thực thi; Claude chạy tích hợp sau.
Rollback code: trả converter về cấu hình trước, nhưng sẽ mở lại việc nhận enum số; không có đổi schema/package.

### BE-REVIEW-02 — 18/09/2026

| | |
|---|---|
| **Decision** | Duyệt **toàn bộ phương án đề xuất** ở mục 5 của `docs/review/BE-REVIEW-02.md` (D-1 … D-14, O-4 … O-13, Q-1 … Q-9) và cho chạy Phase 2 |
| **Decision maker** | **Dylan** — trực tiếp, **không** `SELF-SIGNED` |
| **Date** | 18/09/2026 |
| **Scope** | Contract v1.4 (file `api-contract-v1.1.md`), `docs/openapi/luxmap-v1.4.json` (đổi tên theo version, nay là `luxmap-v1.5.json`), log này, `CLAUDE.md`, code trên nhánh `docs/BE-REVIEW-02` |

Các quyết định con, để tra nhanh (chi tiết và lý do ở BE-REVIEW-02 mục 5):

| Mã | Chốt |
|---|---|
| D-1 | Bản hợp nhất là **v1.4**, giữ tên file `api-contract-v1.1.md` |
| D-2 | Archive log cũ sang `docs/archive/contract-drift-v1.md`; log này mở mới |
| D-3 | Xác nhận bốn quyết định `SELF-SIGNED`: A (`pole_id` filter + khuôn ID), C (`work_order_id` null), D (`LPAD` → hàm), 43 (`DELETE` / `PUT feeder` / `ASSET_IN_USE`) |
| D-4 | 403 sai vai trò → **`ROLE_FORBIDDEN`**; `COMMUNE_FORBIDDEN` chỉ cho địa bàn |
| D-5 | Mạch khác xã với cột → **409 `CROSS_COMMUNE_REFERENCE`** (trước là 403) |
| D-6 | Seeder BE-39 được INSERT ID hiển thị tường minh + `setval` — ngoại lệ hệ thống, vì sequence DB dev đã ở 396 625 và FE hardcode `POLE-0047` |
| D-7 | `feeder_id` cho mock: file gán riêng `mocks/mock-pole-feeders.csv` nạp sau (chưa có dữ liệu — O-6 của Contract) |
| D-8 | Tên tuyến BE ≠ FE: **chưa quyết** (O-1 của Contract) |
| D-9 | Mock đổi ID theo §0.2: `NODE-0nn`, `SWP-001..030`, `FRM-088213`, `USR-004`, bỏ `supplier` — **đã làm**, WP5/WP6 phải kéo lại |
| D-10 | FK ghép `(feeder_id, commune_id)`: ticket riêng trước BE-13 (O-7 của Contract) |
| D-11 | Một bóng đang dùng/cột — `ux_fixture_pole_id_active` UNIQUE, 409 `POLE_HAS_ACTIVE_FIXTURE` |
| D-12 | `from`/`to` không `Z` đọc là UTC |
| D-13 | `data_source` trên 8 entity; `GET /poles`, `GET /segments` mặc định loại `calibration_rig` |
| D-14 | Enum `user_role` vào Contract; EXACT-ROLE; ghi `/assets/*` = Quản trị |
| O-4 cũ | Nhóm `/assets/…` vào Contract (mục 5.3), GET tạm trả ID tới BE-12b |
| O-7 cũ | Fault MỞ = `detected \| confirmed \| in_progress` |
| O-8, O-11, O-13 cũ | Chốt `wo_status`, `processing_status`/thumbnail, hình dạng sync **ở FW kế tiếp** (O-3, O-4, O-5 của Contract) |
| O-10 cũ | `commune_id` tuỳ chọn trong `POST /faults` khi `pole_id` null |
| O-12 cũ | Năm drift BE-42 (17–21) gộp vào 5.7 |
| Q-1..Q-9 | Draft OpenAPI ở `docs/openapi/`; DB dev cặn test → vá tạm N-5, BE-36 là gốc; import bóng theo D-11; CHECK `removed_date >= install_date` + ngừng dùng một lần; `Location` 201 chấp nhận tới BE-12b; `servers` localhost; giữ script sinh spec hợp nhất trong repo; CHECK `status_confidence` làm ngay |

**Việc còn nợ người khác sau quyết định này:**

- **WP5/WP6:** kéo lại `mocks/` (D-9); đọc mục 1.4 (mã lỗi mới) và mục 2 (`ROLE_FORBIDDEN`, `user_role`)
  của Contract v1.4; regex ID dùng `[0-9]{n,}`.
- **Thịnh/Ngọc:** O-1 (tên tuyến), review PR chạm `api-contract-v1.1.md` và `luxmap-v1.json` (CODEOWNERS).
- **BE1:** ~~O-7 FK ghép trước BE-13~~ — **xong 21/09/2026**, xem drift 45; BE-36 sớm (M-1);
  rate limit auth (M-5); CI lint spec (M-7).

### Registration v1.2 — vai trò, phân quyền, phạm vi (25/09/2026)

| | |
|---|---|
| **Decision** | Đồng bộ backend với **Phiếu đăng ký FA26SE222 v1.2** (`docs/registration/FA26SE222_v1.2.md`): D-R1 … D-R18 theo bảng dưới. Contract lên **v1.7** |
| **Decision maker** | **Mỹ (Dylan)** · `SELF-SIGNED` — mọi mục chạm bề mặt API phải được xác nhận lại ở FW kế tiếp (FW-00 mục 3), tới lúc đó **nền là tạm** |
| **Date** | 25/09/2026 |
| **Scope** | `api-contract-v1.1.md` (v1.7: §1.4, §1.6, §2, §3.1, §3.3, §4.1, §4.5, §4.7, §5.3–§5.7, §7, §9, §10); `docs/openapi/luxmap-v1.json` + `luxmap-v1.5.json`; migration `RenameUserRolesToRegistrationV12`; `LuxMapPolicies`, `AuthorizationSetup`, 4 controller; seeder; `CLAUDE.md`, `README.md`, `tracking.html`; nhánh `chore/registration-v1.2-roles-docs`. Khảo sát: `.ai/results/registration-v1.2-phase1.md`, thực thi: `…-phase2.md` |

| Mã | Chốt | Chạm API |
|---|---|---|
| **D-R1** | **Citizen không có account, không có role**; `user_role` vẫn 4 giá trị. Báo sự cố qua QR trên cột → **hàng chờ riêng**, Quản lý duyệt rồi mới thành `fault`; **không** thêm giá trị vào `source_channel`. Chưa có endpoint; `AnonymousEndpointTests` giữ 7 endpoint. Quyết định 20/09/2026 *"bỏ actor guest"* (chỉ ghi trong comment `AnonymousEndpointTests`) **bị thay thế một phần**: không có guest đọc dữ liệu, nhưng có Citizen báo sự cố ẩn danh | Không (chưa có endpoint) |
| **D-R2** | Giữ `text` + CHECK sinh từ `HasContractEnum`. Migration: `DROP CONSTRAINT` → 4 `UPDATE` → `ADD CONSTRAINT`; `Down()` đối xứng, không mất dữ liệu | — |
| **D-R3** | Phạm vi Superior = **tập xã** qua `app_user_commune`. Không schema huyện, không đổi guard/filter, không cấp `*` | — |
| **D-R4** | Vai trò cố định, ma trận quyền trong code. Quản trị hệ thống chỉ **gán** vai trò và xã (API: BE-33) | — |
| **D-R5** | **Sửa D-14** (v1.4): "một vai trò chính xác cho mỗi policy" → **"danh sách vai trò chính xác cho mỗi capability"**, vẫn cấm thứ bậc. Lý do: phiếu có vai trò chỉ-đọc (Superior) và việc chia giữa Quản lý / Kỹ sư hiện trường; một-vai-trò chỉ diễn đạt được bằng endpoint trần — đúng cái đã để `POST /lux-readings` mở cho cả Superior. Ma trận ở `LuxMapPolicies.Matrix` (nguồn duy nhất), Contract §2. Không endpoint nghiệp vụ nào dựa vào fallback (ngoại lệ tên riêng: `GET /auth/me`), canh bằng `CapabilityPolicyCoverageTests` | **Có** |
| **D-R6** | Song ánh `management_agency→superior`, `maintenance_engineer→manager`, `field_crew→field_engineer`, `administrator→system_admin`. 14 tài khoản `administrator` cặn test trên DB dev map máy móc → `system_admin` (migration không biết username; dọn ở BE-36). Tài khoản seed đổi `role` + `full_name` tại chỗ; **giữ** username `admin/agency/engineer/crew`, `USR-001..004`, biến `SEED_*_PASSWORD`. Tên hiển thị: Cấp giám sát / Quản lý / Kỹ sư hiện trường / Quản trị hệ thống | **Có — BREAKING** (giá trị claim `role`) |
| **D-R7** | Điều khiển ON/OFF/AUTO: chỉ **Quản lý**; chỉ thiết bị `supports_remote_control = true` (testbed LED tự dựng) — thiết bị ngoài thực địa luôn `false`; mọi lệnh ghi audit; docs viết *"supported lighting devices (testbed demo)"*. PR này chỉ khai capability `ControlLighting`. Chốt trước khảo sát (Mỹ, 25/09/2026) | Không (chưa có endpoint) |
| **D-R8, D-R9** | Mapping Member 1–5 → tên, và timeline 09/2026–03/2027 so với kế hoạch 13 tuần: **Mỹ xử lý riêng, KHÔNG thuộc PR này** — `CLAUDE.md`, `tracking.html`, `tasks-backend.csv` giữ nguyên mốc W0–W21 | — |
| **D-R10** | **Phiếu v1.2 thay FO-01 (24/08/2026) — FO-01 `SUPERSEDED`.** Ngoài thực địa (xã đối tác): quay video đêm đèn thật, đo sáng tương đối bằng điện thoại, kiểm tra bằng mắt ban đêm (ground truth lớp out); **không** lắp thiết bị, **không** thao tác lưới xã. Testbed tự dựng: toàn bộ IoT, demo ON/OFF/AUTO, Controlled Reference Capture Set. AI khởi động bằng ảnh công khai + controlled reference. Nhãn "Nhánh C" bỏ khỏi văn hiện hành; **luật tách `data_source` giữ nguyên**. Dữ liệu thực địa mang `field`; dữ liệu testbed **không bao giờ** mang `field`. Bốn quyết định dựa trên Nhánh C thành follow-up "nền đã đổi, cần xét lại" (xem `tracking.html`) — `external_ref` **ưu tiên cao** | **Có** (§1.6 câu về `field`; O-9 mới) |
| **D-R11** | ✅ **Hiện thực ở BE-33a (05/10/2026)** — email mời thay mật khẩu tạm, xem mục *BE-33a* cuối file. Mô hình cuối: **chỉ Quản trị hệ thống tạo account**, không tự đăng ký. PR này: `LowestRole → field_engineer`, `POST /auth/register` **DEPRECATED** (Contract §4.1, README, `deprecated: true` trong spec) — **breaking đã báo trước**. **BE-33a** (ngay sau): `POST /api/v1/admin/users` (`ManageUsers`), gỡ `/auth/register` + `RegistrationTests`, `AnonymousEndpointTests` 7→6, báo WP6 bỏ màn đăng ký; Phase 1 của nó mở 3 D-item: mật khẩu tạm + đổi ở lần đầu, bắt buộc ≥1 xã cho 3 vai trò có phạm vi, khoá/mở khoá qua `is_locked` | **Có — breaking báo trước** |
| **D-R12** | (1) Ghi tài sản + import → **Quản lý**; (2) Quản trị hệ thống vẫn **đọc** qua `*`; (3) Quản trị hệ thống **không ghi** nghiệp vụ — nạp đầu kỳ qua seeder / `EnterUnscopedSystemWriteBackdoor` như BE-39 | **Có** (13 endpoint ghi đổi người được ghi) |
| **D-R13** | Audit trail **bắt buộc**, không còn "nếu BE-19 cần": một bảng audit **append-only dùng chung** cho quyết định fault, lệnh điều khiển, review survey session — không `FaultHistory` riêng từng loại. Thiết kế ở Phase 1 của BE-19. PR này không tạo bảng | Không (chưa có bảng) |
| **D-R14** | Video (khảo sát + bằng chứng): ngoài phạm vi, **planned**. BE-11 chỉ nhận JPEG tới khi có ticket video. Hướng đề xuất (chốt cùng Thịnh/mobile): upload thẳng MinIO bằng presigned URL, sync khi có mạng; tách frame ở server hay máy chốt ở ticket đó. ⚠️ Presigned **ngược** BE-11 quy tắc 1 — ticket video phải giải quyết chuyện phân quyền đó, không mặc nhiên | Không (planned) |
| **D-R15** | `lux_value` = số đọc cảm biến ánh sáng **điện thoại**, quy trình cố định (cùng máy, app, tư thế); **tương đối**, chỉ so giữa cột và giữa đêm; không tuyên bố lux tuyệt đối, không đánh giá đạt chuẩn. `meter_model` = model điện thoại. Ngưỡng 200 giữ nhưng ghi lại là kiểm tra hợp lệ dữ liệu thô. Giữ tên cột | Diễn đạt (§5.7), không đổi hình dạng |
| **D-R16** | Đóng mục mở "lưu ảnh dài hạn": ảnh, telemetry, fault giữ **tối thiểu qua hết bảo hành**; hết bảo hành **không** xoá / archive. Tài sản hết bảo hành chỉ mang **cảnh báo**, tính từ ngày hết hạn so với hôm nay, **không lưu cột trạng thái**; hiện trên bản đồ, danh sách tài sản, fault / work order. Hiện thực: BE-31 / BE-35. Schema hiện có: `fixture.install_date` (NOT NULL), `fixture.warranty_expiry` (NULL được), `fixture.removed_date` — **trên `fixture`, không trên `pole`** | Có (§7, §3.3; chưa có trường) |
| **D-R17** | Contract **v1.7**, BREAKING. Giữ tên file spec `luxmap-v1.5.json`; spec sinh lại từ code, không sửa tay. Người ký: Mỹ, `SELF-SIGNED` (CODEOWNERS đã bỏ từ 14/09) | — |
| **D-R18** | Tài liệu lịch sử giữ nguyên + banner (`backend-report.md`, `docs/review/*`, `docs/archive/*`, log này, mục đã xong trong tracking). Khối v1.6 trong `CLAUDE.md` giữ. Chỉ xoá chỗ sót viết ở thì hiện tại (`docs/templates/README.md` hai dòng solar, `CLAUDE.md` `POLE-0047` là cột solar). Không đụng bản sao untracked `.ai/context/tracking.html` | — |

⚠️ **Đính chính khảo sát:** Phase 1 ghi "15 chỗ gắn `Administrator`". Đếm lại lúc hiện thực: **13 attribute**
(12 trong `AssetsController` + 1 cấp class `AssetImportController`); dòng thứ 14 của grep là XML doc.
Ma trận §2 dùng con số 13.

**Việc còn nợ người khác sau quyết định này:**

- **WP5:** đổi giá trị `role` (bốn giá trị mới); màn quản lý tài sản nay là của **Quản lý**, Quản trị hệ
  thống bị `403 ROLE_FORBIDDEN`; đọc `x-luxmap-roles` trong spec để ẩn nút.
- **WP6:** đổi giá trị `role`; **bỏ màn đăng ký** (`/auth/register` deprecated); `POST /lux-readings` chỉ
  Kỹ sư hiện trường.
- **Thịnh/Ngọc:** xác nhận hoặc lật D-R5, D-R6, D-R10, D-R11, D-R12 ở FW kế tiếp.

### Registration v1.4 — CV chỉ ON/OFF, độ sáng đo bằng BH1750 trên xe (27/09/2026)

| | |
|---|---|
| **Decision** | Nguồn chuẩn cho scope / actor / FR / NFR / deliverable chuyển sang **Phiếu FA26SE222 v1.4** (`docs/registration/FA26SE222_v1.4.md`); `FA26SE222_v1.2.md` thành lịch sử. **Chỉ tầng tài liệu** — D-R20 … D-R28 bên dưới: D-R21, D-R24 **chốt hướng (SELF-SIGNED)**, còn lại **đề xuất, CHƯA CHỐT**; chưa đổi Contract, chưa đổi code |
| **Decision maker** | Nguồn phiếu: nhóm (nội dung phiếu). Các D-item: **chưa có người ký** — mục chạm bề mặt API **không** được im lặng thành approve (FW-00 mục 3) → **ESCALATE** ở FW kế tiếp |
| **Date** | 27/09/2026 |
| **Scope** | Tài liệu: `docs/registration/FA26SE222_v1.4.md` (mới), banner ở `FA26SE222_v1.2.md`, mục này, `CLAUDE.md` (con trỏ nguồn + cảnh báo nền đã đổi ở BE-42 / phạm vi), `tracking.html` (bảng follow-up). **Không** đụng `api-contract-v1.1.md`, `docs/openapi/*`, migration, `src/`. Khảo sát tác động: `.ai/results/registration-v1.4-phase1.md` |

**v1.3 bị bỏ qua có chủ đích.** Phiếu v1.3 (đã nộp) chuyển CV sang chấm **mặt đường theo đoạn**
(Insufficient / Adequate / Excessive). Repo chưa từng áp dụng v1.3; v1.4 quay lại **từng bóng**, nên đi
thẳng v1.2 → v1.4. Enum Contract mục 1 **không phải lật** — đó là lý do chính hướng v1.4 rẻ cho backend.

**Điều thay đổi về bản chất (v1.2 → v1.4):**

| | v1.2 | v1.4 |
|---|---|---|
| CV quyết gì | Phát hiện bóng **và chấm** normal / dim / out từ độ sáng ảnh đã chuẩn hoá phơi sáng + khoảng cách | **Chỉ ON/OFF**, và xác nhận đỉnh lux là của chính bóng đó (không phải đèn pha, biển hiệu) |
| Tín hiệu Dim | Độ sáng ảnh so baseline cột | **Đỉnh lux BH1750 lúc xe ngang cột** so baseline cột |
| Đo sáng | Người đứng dưới cột, cảm biến **điện thoại**, từng lần (`LuxReading`, D-R15) | Module **BH1750FVI + vi điều khiển** gắn **đầu xe máy** (xác nhận 03/10/2026), hướng lên, **lấy mẫu liên tục suốt sweep**, đồng bộ đồng hồ với điện thoại đầu phiên |
| Vai trò của lux | **Ground truth** chấm CV (CV-12, RQ1) | **Đầu vào** của phân loại Dim |
| Ground truth `dim` | Lux điện thoại + Controlled Reference Capture Set | **Kiểm tra thực địa** (field verification) — phương pháp chưa định |
| Controlled Reference Capture Set | Deliverable | **Không còn** trong deliverable |

| Mã | Đề xuất | Chạm API | Trạng thái |
|---|---|---|---|
| **D-R19** | Phiếu v1.4 thay v1.2 làm nguồn chuẩn. Vai trò, ma trận capability, Citizen/QR, điều khiển testbed, audit trail (D-R1…D-R18) **giữ nguyên** — v1.4 không đổi actor | Không | ✅ Tầng tài liệu, 27/09 |
| **D-R20** | `fixture_status` **giữ** `normal \| dim \| out \| unknown`. Quy tắc: CV `OFF` → `out`; CV `ON` + `baseline_ratio` < `dim_threshold_ratio` → `dim`; còn lại `normal`; không có quan sát → `unknown`. `lamp_out` / `lamp_dim` vẫn `source_channel = cv` (kênh = sweep khảo sát) — **cần xác nhận**, vì `lamp_dim` nay do CV **và** cảm biến cùng quyết | Có (ngữ nghĩa, không đổi hình dạng) | **Chốt 07/10 (Mỹ): giữ `cv`**, không thêm giá trị enum — kênh là phiên khảo sát, cảm biến lux là một phần của phiên. Không đổi Contract, mock, FE |
| **D-R21** | **Mô hình dữ liệu một phiên khảo sát — hướng (B), chốt 27/09.** App quay riêng (WP6) sinh **bốn file thô, không ai nhập tay**: (1) **video**; (2) **log lux** — module ESP32 + BH1750 gửi **từng mẫu** qua **BLE notify** (`seq`, `module_ms`, `lux`), app gắn `phone_elapsed_ns` lúc nhận; (3) **GPS track** theo thời gian (không phải một toạ độ cho cả file); (4) **cấu hình quay** thực tế. **Cả ba luồng video / GPS / lux dùng CHUNG một đồng hồ** `SystemClock.elapsedRealtimeNanos()` — Camera2 gắn `SENSOR_TIMESTAMP` cùng hệ thời gian khi `SENSOR_INFO_TIMESTAMP_SOURCE = REALTIME` (**phải kiểm trên máy quay thật**); quy đổi UTC **một lần** mỗi phiên, nên **không cần mốc flash**. Jitter BLE ~10–50 ms (≈0,4 m ở 30 km/h) — server khớp tuyến tính `(module_ms, phone_elapsed_ns)` trên cả phiên để khử. **Điện thoại không xử lý gì**: server lưu nguyên byte bốn file, parse log lux thành bảng mẫu theo sweep (không gắn cột), tìm đỉnh, ghép cột bằng GPS track + vị trí cột GIS, lấy ON/OFF của CV, rồi ghi **một dòng cho mỗi cột mỗi sweep** (`peak_at, peak_lux, cv_state, baseline_ratio, classified_as, association_confidence`) vào chuỗi luminance. Khoảng `seq` bị hổng trùng lúc ngang cột → `unknown`, không đoán. `LuxReading` (BE-42) **giữ** cho số đo **thủ công** lúc kiểm tra thực địa — ứng viên ground truth của D-R23. Người quyết: **Mỹ (Dylan)** · `SELF-SIGNED` | **Có** — BE-15 mới, §5.7 diễn đạt lại | **Chốt hướng, SELF-SIGNED** — nền tạm tới FW kế tiếp; **đặc tả BE-15 chưa viết** |
| **D-R22** | `luminance_history` / `luminance_baseline` **giữ hình dạng** (`baseline_ratio`, `classified_as`, `dim_threshold_ratio` 0.80) nhưng **nguồn đổi**: đỉnh lux theo cột, không phải độ sáng ảnh. `out_threshold_ratio` (0.15) **mất vai trò phân loại** vì Out nay do CV quyết — giữ làm kiểm tra nhất quán (CV nói ON mà lux gần 0), hay bỏ? Tên `luminance_*` giữ dù đại lượng là độ rọi (đổi tên = BREAKING không mua được gì) | **Có** — §5.2 detail, BE-33 | **Chốt 07/10 (Mỹ): giữ trường, luôn `null`, ghi Contract "không còn dùng phân loại"** (hướng b) — xem mục *Quyết định khảo sát 07/10/2026* |
| **D-R23** | **Lux không còn là ground truth của `dim`** — nó là đầu vào. Quy tắc BE-42 số 1 trong `CLAUDE.md` (*"ghi lux vào chuỗi luminance là để CV tự chấm chính mình"*) **đảo nghĩa**: ở v1.4 chuỗi luminance **chính là** lux. Cần chốt ground truth mới cho `dim` (kiểm tra thực địa bằng gì, ai làm, trên tập con nào) và cho độ lặp lại của phép đo (nhiều lượt qua cùng cột). `out` vẫn = kiểm tra bằng mắt ban đêm | Không (nghiên cứu) — nhưng đổi tiêu chí CV-12 | **Chốt 07/10 (Mỹ): B (tấm lọc ND) trước, A (lux kế tay) sau** — xem mục *Quyết định khảo sát 07/10/2026* |
| **D-R24** | **Cấu hình quay CỐ ĐỊNH, kiểm ở cấp PHIÊN thay cho kiểm từng JPEG (BE-16).** Camera2 (hoặc CameraX + Camera2Interop): tắt AE, cố định ISO + shutter; lấy nét cố định vô cực; WB cố định; độ phân giải + fps cố định; **tắt EIS / HDR / night mode** (crop, bóp méo, ghép frame). App ghi cấu hình **thực tế** vào file cấu hình phiên; server so với **một profile đã chốt** và **từ chối** phiên lệch, kèm lý do — cùng tinh thần "không nhận, không suy đoán" của BE-16. Con số ISO / shutter chốt sau buổi quay thử. Metadata phiên thêm: làn, chiều đi, tần số lấy mẫu module, phiên bản firmware module (BE-34). Người quyết: **Mỹ (Dylan)** · `SELF-SIGNED` | **Có** — BE-15/16 chưa đặc tả | **Chốt hướng, SELF-SIGNED**; chờ profile từ buổi quay thử |
| **D-R25** | Controlled Reference Capture Set **rời deliverable**. Giá trị `calibration_rig` **giữ trong enum** (xoá giá trị enum là BREAKING, và testbed IoT D-R10 vẫn cần một nhãn không-phải-`field` — Contract O-9). Không seed / không dựng thêm dữ liệu cho nó tới khi có quyết định | Không | Chờ quyết |
| **D-R26** | Trạng thái **đoạn** suy ra từ cột (phiếu 3.2.b: các cột Out/Dim liền nhau). **Tương thích** với CV-15 (`segment_outage`, `has_active_segment_fault`) — không thêm trường | Không | Ghi nhận |
| **D-R27** | Mốc thời gian: `LuxReading` bị đẩy sớm cho **FO-14 đo lux điện thoại W5**. Với BH1750, FO-14 đổi thành đo bằng module trên xe → việc BE thực sự cần sớm là **ingest mẫu theo sweep (D-R21)**, không phải thêm trường cho `POST /lux-readings`. `tasks-backend.csv` giữ nguyên tới khi D-R21 chốt | Không | Chờ quyết cùng D-R21 |
| **D-R28** | **Video là dữ liệu CHÍNH → ticket video (D-R14) thành việc chặn đường.** Dữ kiện (27/09): **mỗi video dưới 1 phút** → 1080p30 ở bitrate điện thoại (~12–20 Mbps) cỡ **~100–150 MB/clip**; một sweep gồm **nhiều clip**. Cỡ này **giữ được BE-11 quy tắc 1** (mọi byte qua API, không presigned) nếu endpoint video **stream thẳng** sang MinIO (S3 multipart), **không** buffer vào RAM, **không** qua `IFormFile` 10 MB; nâng giới hạn body **riêng cho endpoint này** (ví dụ 300 MB). Upload lại cả clip khi lỗi là chấp nhận được; resumable chưa cần. **Đề xuất: (A) proxy qua API** — không phải giải lại phân quyền địa bàn. (B) presigned PUT chỉ xét lại nếu clip dài ra. Hệ quả mô hình: log lux + GPS track là **một luồng liên tục cho cả sweep** (chung đồng hồ), video là **nhiều clip** có `start_elapsed_ns` riêng; cột bị đi ngang **giữa hai clip** → `unknown`, không đoán. Còn chốt: cắt frame ở server hay engine CV, giữ video gốc bao lâu (D-R16) | **Có** — endpoint upload mới | **Đề xuất (A), chờ quyết — chặn BE-15** |

**Việc còn nợ người khác sau mục này:**

- **WP4 (AI):** mô hình đổi đích — phát hiện + phân lớp ON/OFF; CV-12 không còn "chấm CV bằng lux" (D-R23).
- **WP6 (mobile):** **app quay riêng** (D-R21, D-R24): Camera2 khoá cấu hình, GPS track, nhận BLE từng mẫu, mọi luồng dùng
  chung `elapsedRealtimeNanos`; kiểm `SENSOR_INFO_TIMESTAMP_SOURCE` trên máy quay thật. Upload chờ D-R28.
- **IoT (Member 2):** firmware module BH1750 (phiếu 3.2.g) — gói BLE `seq, module_ms, lux`, notify từng mẫu; chốt tần số lấy mẫu với BE.
- **Người ký:** D-R21, D-R24 đã SELF-SIGNED (Dylan, 27/09) → xác nhận ở FW kế tiếp; D-R20, D-R22, D-R28 chạm bề mặt API →
  ESCALATE; D-R23 cần cả WP4.

### Từ v1.5: quyết định đi cùng Contract thì ghi Ở CONTRACT

Changelog Contract (mục 10) nay có cột **Người quyết**. Một thay đổi mà Contract và code cùng vào một
lần thì **không tạo ra chỗ lệch nào**, nên nó không có gì để ghi ở file này; bốn trường FW-00 mục 1
nằm trọn trong dòng changelog cộng phần mô tả.

File này giữ đúng một nghĩa: **nơi code và Contract bất đồng**. Đó cũng là lý do nó cố ý không có
CODEOWNERS, trong khi `api-contract-v1.1.md` và `luxmap-v1.json` thì có.

**Ví dụ đầu tiên đi theo lối này:** `GET /api/v1/auth/me`, Contract v1.5, Dylan, 19/09/2026. Endpoint
mới, hiện thực và đặc tả trong cùng một PR, **không có mục drift**.

---

## Chỗ lệch mới (đăng ký sau 18/09/2026)

| # | Chỗ lệch | Mức | Ai bị ảnh hưởng | Trạng thái |
|---|---|---|---|---|
| 44 | Năm endpoint sửa/xoá tài sản không có trong Contract | Trung bình | WP5 (màn quản trị tài sản) | **Chờ duyệt** — nêu ở FW kế tiếp |
| 45 | Contract mục 9 còn ghi O-7 là việc đang mở; FK ghép đã có trong lược đồ | Thấp | Không ai — thuần nội bộ backend | **Chờ đóng ở v1.6** |
| 46 | Không có endpoint topology nào trong Contract; BE-13 đã hiện thực trên nền TẠM | Cao | WP4 (CV-05, CV-15) | **Đã hiện thực, CHỜ DUYỆT** — đề xuất ở `review/BE-13-topology-shape.md` |
| 47 | `GET /poles` và `GET /segments` trả đúng HÌNH DẠNG mục 5.1–5.2 nhưng ba trường chưa có nguồn | Cao | WP5, WP6 (bản đồ trông sẽ trống) | **Mở** — gỡ dần theo BE-15/17 và bảng IoT |
| 34 | ~~`GET /assets/*` trả danh sách ID~~ | — | — | ✅ **ĐÓNG 22/09/2026** — BE-12b hiện thực, Contract v1.7 mục 5.3.1 |

### 44 — `PUT` và `DELETE` cho tuyến đường, tủ điện, cột đèn (20/09/2026)

**Contract đang ghi gì.** Mục 2.1–2.3 chỉ đặc tả phần ĐỌC cho bản đồ. Nhóm `/assets/…` vốn đã nằm
ngoài Contract (BE-12a), và drift 43 mới chỉ xác nhận `DELETE /assets/poles/{id}` cùng
`PUT /assets/poles/{id}/feeder`. Sửa tài sản và xoá tuyến/tủ điện **chưa có ở đâu cả**.

**Code đang làm gì.** BE-12 thêm năm endpoint, tất cả `[Authorize(Administrator)]`, tất cả trả `204`
không body:

```
PUT    /api/v1/assets/segments/{id}      PUT    /api/v1/assets/feeders/{id}
PUT    /api/v1/assets/poles/{id}
DELETE /api/v1/assets/segments/{id}      DELETE /api/v1/assets/feeders/{id}
```

Bốn quyết định đi kèm, đều là **nội bộ backend** nhưng chạm bề mặt API nên phải nêu:

1. **`PUT` là THAY THẾ TOÀN PHẦN, không phải patch.** Thiếu field nghĩa là field đó rỗng — nên body
   không có `feeder_id` sẽ **xoá mạch điện của cột**. Ai chỉ muốn đổi mạch thì dùng
   `PUT /assets/poles/{id}/feeder`, endpoint đó phân biệt được "không gửi" với "gửi null".
2. **`commune_id` KHÔNG sửa được.** Chuyển tài sản sang xã khác không phải sửa, mà là chuyển giao —
   đổi luôn ai nhìn thấy dòng đó, và phải kiểm scope ở **cả hai** phía. Sai xã thì xoá rồi tạo lại.
3. **`data_source` SỬA ĐƯỢC.** Đây là chỗ đánh đổi: nó là trường provenance của Nhánh C, ghi đè nó là
   ghi đè nguồn gốc dữ liệu. Giữ cho sửa vì một lần import sai không còn đường nào khác sau khi có
   `fault` trỏ vào, và endpoint là Quản trị-only. **Nếu FW thấy rủi ro lớn hơn tiện lợi thì bỏ field
   này khỏi cả ba request là đủ** — không ảnh hưởng gì khác.
4. **Xoá tuyến/tủ điện do KHOÁ NGOẠI quyết định**, không kiểm trong code: `pole`, `fault`,
   `fault_cluster` giữ tuyến bằng `Restrict`; `pole.feeder_id` giữ tủ điện bằng `Restrict` (và
   nullable — nên xoá tủ điện **không** âm thầm gỡ mạch của cột). Vi phạm → `409 ASSET_IN_USE` kèm
   tên constraint trong `details`.

**Đề xuất.** Ghi vào Contract mục 5.3.1 ở lần tăng version kế tiếp (**v1.6**), cùng lúc với
**BE-12b** (hình dạng response khi đọc) — hai thứ này thuộc cùng một màn hình của WP5, tách ra duyệt
hai lần là bắt Thịnh/Ngọc đọc cùng một ngữ cảnh hai lần.

📄 **Đề xuất BE-12b đã soạn sẵn để duyệt chung: [`docs/review/BE-12b-read-shape.md`](review/BE-12b-read-shape.md).**
Ba câu hỏi cần chữ ký, cả ba đều là *"mục 5.1 cấm emit `data_source` / `external_ref` / `feeder_id` —
lệnh cấm đó có áp cho endpoint kiểm kê không"*.

> 🔴 **Điểm 3 ở trên (`data_source` sửa được) BUỘC phải quyết cùng Q2 của tài liệu đó.** Nếu Q2 trả
> lời **không emit** thì phải **bỏ `data_source` khỏi cả ba request `PUT`**: một trường ghi được mà
> không đọc lại được là thiết kế không ai bảo vệ được, và với trường provenance của Nhánh C thì nó là
> đúng điều kiện để trộn nhầm nguồn dữ liệu mà không ai thấy. Đừng duyệt lệch hai câu này.

**Ảnh hưởng.** WP5 chưa code màn quản trị tài sản nên chưa ai bị chặn. Nếu quyết khác ở điểm 1 (đổi
sang `PATCH`) thì phải sửa trước khi WP5 bắt đầu — sau đó là breaking change.

### 45 — O-7 đã xong: FK ghép `(feeder_id, commune_id)` (21/09/2026)

**Contract đang ghi gì.** Mục 9 liệt kê **O-7** là Open item của BE1, hạn *"trước BE-13"*, kèm câu
*"tới lúc đó mọi đường ghi phải gọi kiểm cùng xã"*. Câu đó **nay đã sai**.

**Code đang làm gì.** `pole` mang khoá ngoại ghép tới `feeder` trên **cả hai** cột:

```
fk_pole_feeder_feeder_id_commune_id
    FOREIGN KEY (feeder_id, commune_id) REFERENCES feeder (feeder_id, commune_id) ON DELETE RESTRICT
```

Migration `FeederCommuneCompositeFk`. FK đơn cột `fk_pole_feeder_feeder_id` bị **thay thế**, không
phải thêm chồng — nên `details.constraint` của `409 ASSET_IN_USE` vẫn xác định một giá trị.

Bốn điểm đáng nêu:

1. **Bề mặt API KHÔNG đổi.** `RequireFeederInCommuneAsync` vẫn chạy trước và vẫn là thứ **trả lời**:
   409 `CROSS_COMMUNE_REFERENCE` nêu đúng hai xã. FK chỉ là lớp chặn phía dưới — nếu nó là thứ nổ thì
   người dùng nhận `DbUpdateException` trần và **500**, nên hai lớp đều cần, không thay nhau. Cùng
   hình dạng `CommuneFilter.Narrow` chồng lên `CommuneWriteGuard`.
2. **Khoá ngoại bắt được một ca mà tầng app CHƯA BAO GIỜ phủ:** đổi `commune_id` của **chính cột**
   trong khi nó đang đấu vào một tủ điện. `RequireFeederInCommuneAsync` đọc xã của cột làm vế cố
   định nên không có gì kích hoạt. Hôm nay không request nào hỏi được điều đó — `commune_id` vắng mặt
   ở cả ba body update — nhưng đó là tính chất của controller tuần này, không phải của dữ liệu.
3. **Cột không có mạch vẫn hợp lệ**, nhờ `MATCH SIMPLE` (mặc định của Postgres): một hàng có bất kỳ
   cột FK nào null thì bỏ qua lượt kiểm. `MATCH FULL` sẽ từ chối **mọi** cột không có feeder.
4. **`segment_id` KHÔNG có khoá tương tự, và đó là cố ý** — `road_class = inter_commune` nghĩa là
   đường chạy giữa các xã (BE-REVIEW-02 ràng buộc 1).

**Đề xuất.** Ở **v1.6**, xoá dòng O-7 khỏi mục 9 và đổi câu *"mọi đường ghi phải gọi kiểm cùng xã"*
thành ghi chú rằng lược đồ đã thực thi. Gộp chung lần duyệt với drift 44 và BE-12b — không đáng một
lượt duyệt riêng.

**Ảnh hưởng.** Không ai. Không endpoint nào đổi, không mã lỗi nào đổi, không trường nào đổi. Mục drift
này tồn tại **chỉ vì Contract đang nói một việc đã xong là chưa xong** — và một Open item quá hạn mà
thật ra đã đóng sẽ ngốn thời gian FW-00 y như một cái còn mở thật.


### 46 — BE-13 cần endpoint topology, Contract không có cái nào (21/09/2026)

**Contract đang ghi gì.** Không gì cả. Đã tra toàn văn: không có endpoint nào nhóm cột theo mạch
điện hay theo tuyến. Mục 5.3 phủ CRUD `/assets/…`; mục 2.1 phủ `GET /poles` theo `bbox`, là endpoint
**bản đồ** của BE-14.

**Code đang làm gì.** Đã hiện thực ba endpoint (21/09), **trên nền chưa được duyệt**:

```
GET /api/v1/assets/feeders/{feederId}/poles
GET /api/v1/assets/segments/{segmentId}/poles
GET /api/v1/assets/feeders/poles?unassigned=true
```

> 🔴 **NỀN LÀ TẠM.** Theo nguyên tắc 3 của FW-00: quyết định chạm bề mặt API **chưa ổn định cho tới
> khi FW kế tiếp xác nhận**, và ticket xây lên trên nó **phải ghi rõ nền là tạm**. Mục này là chỗ ghi
> đó. **CV-05 và CV-15 phải biết** trước khi bind theo hình dạng này.

**Vì sao hiện thực trước khi duyệt.** Cùng lối BE-12a đã đi: nhóm `/assets/…` cũng ra đời ngoài
Contract rồi đăng ký drift sau. Điều kiện để lối đó chấp nhận được là **bề mặt MỚI, chưa ai code
theo** — đúng trường hợp này: người dùng là CV-05/CV-15, engine nội bộ của WP4, chưa dựng gì lên
trên. Khác hẳn BE-12b, thứ đang đổi hình dạng một phong bì **đã publish**.

Hạn BE-13 là W4 (28/09–04/10) và hai ticket WP4 chặn sau nó, trong khi chờ duyệt ở nhóm này có
thành tích không tốt: BE-12b treo từ 17/09, drift 29/30/34 đi qua bằng **hết hạn im lặng** chứ không
phải có người đọc.

**Cái gì phải làm lại nếu bị lật:** route + DTO. Truy vấn ở service và toàn bộ test phạm vi địa bàn
**không đổi** dù hình dạng nào được chốt — đó là phần đắt, và nó không nằm trong vùng rủi ro.

**Phần GÁN của BE-13 thì đã xong** từ BE-12a/BE-12: `PUT /assets/poles/{id}/feeder` gán mạch,
`PUT /assets/poles/{id}` gán tuyến, import gán theo lô. Phần thiếu đúng là phần **đọc**.

**Đề xuất.** Chi tiết và lý lẽ ở
📄 [`docs/review/BE-13-topology-shape.md`](review/BE-13-topology-shape.md) — tài liệu **giữ nguyên**
và vẫn đi duyệt; code không thay chữ ký.

Phân trang JSON kèm `{lat, lng}` — **không** GeoJSON, theo đúng tiền lệ `GET /faults` ở mục 2.4.
Vào Contract mục 5.4 ở **v1.6**.

> 🔴 **Phải duyệt CÙNG LÚC với BE-12b Q3.** Câu đó hỏi `GET /assets/poles/{id}` có emit `feeder_id`
> không. Nếu trả lời **không emit**, thì một hệ thống mà `feeder_id` không đọc được ở đâu cả nhưng
> endpoint topology lại **nhóm theo nó** là mâu thuẫn — người dùng thấy kết quả gom nhóm mà không tra
> được cột nào thuộc nhóm nào. Tài liệu BE-12b đã nêu BE-13 như lý do cho Q3 khi BE-13 còn là giả
> định; nay nó có thật.

**Ảnh hưởng.** **WP4 bị chặn** — CV-05 và CV-15 đều đợi. Khác với drift 44 và 45 (WP5 chưa code nên
chưa ai bị chặn), mục này **có người đang đợi thật**, hạn W4.

> ⚠️ **Duyệt xong vẫn chưa chạy được — O-6 chặn phần dữ liệu.** Bộ mock **không có một tủ điện nào**
> và cả 103 cột đều `feeder_id = NULL`. BE-13 giao *khả năng truy vấn*; O-6 giao *dữ liệu để truy
> vấn*. Gộp hai thứ sẽ dẫn tới W4 báo BE-13 xong rồi W5 CV-15 phát hiện không có gì để gom.
>
> **Và O-6 như mục 9 đang ghi là thiếu một nửa:** nó chỉ nhắc `mock-pole-feeders.csv`, nhưng không
> gán cột vào tủ điện chưa tồn tại được — cần `mock-feeders.csv` trước. Khuôn cho cả hai đã dựng sẵn
> ở `mocks/`, chỉ **58 trên 103 dòng** cần điền (45 cột solar để trống là đúng). Đề nghị sửa câu chữ
> của O-6 ở v1.6.


### 47 — BE-14 trả đủ hình dạng, nhưng ba trường chưa có nguồn dữ liệu (22/09/2026)

**Contract đang ghi gì.** Mục 5.1 liệt kê 15 `properties` cho `GET /poles`, mục 5.2 liệt kê 7 cho
`GET /segments`. Không mục nào nói trường nào có thể rỗng vì bảng chưa tồn tại.

**Code đang làm gì.** Cả hai endpoint đã hiện thực **đúng đặc tả** — đủ 15 và 7 khoá, `FeatureCollection`,
`[lng, lat]`, không `feature.id`, `bbox` bắt buộc, quá 2000 cột → 413. Nhưng ba trường **chưa có
nguồn**:

| Trường | Trả về hôm nay | Vì sao | Ai gỡ |
|---|---|---|---|
| `fixture_status` | `unknown` với **mọi** cột | `pole_current_status` rỗng | **BE-15/BE-17** |
| `status_confidence` | `null` với mọi cột | như trên (mục 5.1: null ⟺ `unknown`) | **BE-15/BE-17** |
| `last_seen_at`, `last_sweep_id` | `null` | như trên | **BE-15/BE-17** |
| `has_iot_node` | `false` với mọi cột | **không có bảng `iot_node`** | ticket IoT |
| `controller_node_id` | `null` với mọi tuyến | như trên | ticket IoT |

> 🔴 **`unknown` ở đây KHÔNG phải giá trị tạm.** Mục 3.1 định nghĩa `unknown` là *"sweep gần nhất
> không phủ được cột đó"* — đúng trạng thái của một cột chưa ai phân loại. Nên hình dạng đúng **và**
> giá trị đúng; chỉ là dữ liệu chưa về. **Không được gộp vào `out`** ở bất kỳ thống kê nào.

**Khoá vẫn được emit, không bỏ khỏi JSON** — tiền lệ `nearest_luminance` của BE-42: WP5/WP6 bind
hình dạng cuối ngay bây giờ, và ngày bảng IoT ra đời không response nào đổi hình.

**Ảnh hưởng — đây là phần WP5 cần biết trước khi mở bản đồ.** Bộ mock `mock-poles.geojson` mang
70 `normal` / 10 `dim` / 16 `out` / 7 `unknown`, nhưng API sẽ trả **103 `unknown`** cho tới khi
BE-15/BE-17 ghi `pole_current_status`. Bản đồ chạy đúng, chỉ là một màu. Nếu WP5 demo theo bộ mock mà
đọc từ API thì con số sẽ không khớp — **đó là dữ liệu chưa có, không phải endpoint sai**.

**`GET /iot-nodes` KHÔNG nằm trong PR này.** Mô tả BE-14 ở `tasks-backend.csv` có nó, nhưng bảng
`iot_node` chưa tồn tại và mục 5.6 đánh dấu cả nhóm IoT là `[NOT IMPLEMENTED]`. Tạo bảng đó không
phải việc của BE-14; đây là **thu hẹp phạm vi do lược đồ, không phải lựa chọn**.

**Không có gì cần duyệt.** Hình dạng đã nằm trong Contract và code khớp. Mục này tồn tại để không ai
đọc một bản đồ toàn `unknown` rồi kết luận BE-14 hỏng.


### 34 — ĐÓNG: `GET /assets/*` nay trả hình dạng kiểm kê đầy đủ (22/09/2026)

Mục drift này mở từ BE-12a với hạn dùng rõ ràng: *"xoá khi BE-12b xong"*. BE-12b xong.

`GET /api/v1/assets/{segments|feeders|poles}` trả `PagedResult<T>` với dòng kiểm kê đầy đủ, và ba
endpoint `GET /assets/{kind}/{id}` được thêm. Đặc tả ở **Contract mục 5.3.1 (v1.7)**, nên từ đây
Contract và code không còn lệch — không còn gì để ghi ở file này.

**Ba câu treo từ 17/09 trả lời CÓ cả ba.** Quyết ngày 22/09 sau một lượt review độc lập đối chiếu
**repo WP5 thật** (`luxmap-web`), không phải suy đoán. Điều quyết định là phát hiện rằng màn quản trị
tài sản của WP5 **đọc từ mock local**, chỉ modal import gọi API — nên đổi hình dạng vẫn còn miễn phí,
và `AssetPoleItem` của họ cho thấy bảng hiện **công suất và loại đèn theo từng dòng**, thứ khiến
`has_active_fixture` kiểu boolean trở thành N+1.

⚠️ **`SELF-SIGNED`** — ký một mình, không qua Thịnh/Ngọc. Chạm bề mặt API nên **chưa ổn định cho tới
khi FW kế tiếp xác nhận**; ticket xây lên trên nó phải ghi rõ nền là tạm.

> 🔴 **Việc lớn hơn tìm thấy trong cùng lượt review, và nó KHÔNG ở repo này.**
> `luxmap-web/src/pages/assets/components/ImportAssetModal.tsx` bắt mọi exception rồi dựng
> `inserted: <số dòng>, failed: 0, total_errors: 0` và gọi `onImportSuccess`. Backend 500, 401 hay
> mất mạng đều hiện ra như một lượt nạp hoàn hảo, và `|| 10` khiến file rỗng cũng báo "đã nạp 10".
> **Nhìn giao diện không phân biệt được FE↔BE đã thông hay đã hỏng.** Cần WP5 sửa; tiêu chí nghiệm
> thu nên là *import → đọc lại từ server → sửa → tải lại trang*.


> Ghi theo khuôn: Contract đang ghi gì · Code đang làm gì · Đề xuất · Ảnh hưởng. Chạm bề mặt API thì
> theo nguyên tắc 3 (ESCALATE khi im lặng), không tự coi là approve.


### BE-23 — audit trước và loại việc của mock (27/09/2026)

| | |
|---|---|
| **Decision** | D1/D9: tách hạ tầng audit D-R13 (trước hẹn BE-19) thành BE-23a, merge trước BE-23. D10: WO-0001 và WO-0003 là `inspection`; repair chỉ nhận fault `confirmed` hoặc `in_progress`. |
| **Decision maker** | Mỹ — chốt bổ sung trực tiếp 27/09/2026; thiết kế chi tiết trong `.ai/results/BE-23-claude-decisions.md` theo uỷ quyền |
| **Date** | 27/09/2026 |
| **Scope** | BE-23a chỉ thêm lưu trữ audit nội bộ, không có API đọc audit. D10 chạm ngữ nghĩa mock/API; chưa gộp vào Contract v1.7, cần xác nhận FW. |

**Hiện thực:** BE-23a có bảng audit, guard EF, trigger append-only và lối dọn test đã duyệt.
Phần work order và đổi mock **chưa hiện thực**, đợi BE-23a merge theo D9.
**Phải báo WP5/WP6:** WO-0001/WO-0003 chuyển thành inspection, có `task_kind`, giữ fault detected
cho kiểm tra; không coi đây là repair hợp lệ. Việc thông báo chưa thực hiện; không suy ra họ đã nhận
thay đổi chỉ từ việc ghi log này. Các drift bề mặt API còn lại đăng ký khi triển khai BE-23.

## BE-23 — work orders (28/09/2026)

| | |
|---|---|
| **Decision** | Hiện thực inspection/repair, bảng nối giữ lịch sử, capability, giới hạn người được giao, máy trạng thái và audit theo D2–D11; D3 phương án B |
| **Decision maker** | **Mỹ chốt bổ sung 27/09/2026 + thiết kế Claude theo uỷ quyền người dùng 27/09/2026**; D-WO-P2-01 được Claude giải theo lệnh Mỹ 28/09/2026 |
| **Date** | 28/09/2026 |
| **Scope** | BE-23; Contract §1.4, §2, §3.1–3.3, §5.4–5.5. Không sửa Contract trong ticket này. Mọi mục chạm API là **nền tạm tới FW kế tiếp xác nhận**; im lặng là ESCALATE, không phải approve |

| Mã | Deviation / quyết định áp dụng | Chạm API |
|---|---|---|
| WO-1 | `ReadWorkOrders` = cả bốn vai trò; `ManageWorkOrders` = manager; `ExecuteWorkOrders` = field_engineer. FE chỉ thấy WO được giao cho mình **và** trong xã; WO không thấy → 404 `WORK_ORDER_NOT_FOUND`. `assigned_to=me` hỗ trợ mọi vai trò; FE lọc người khác → trang rỗng. System Admin chỉ đọc | Có |
| WO-2 | Máy trạng thái bên dưới đóng O-3 trên nền tạm. Chỉ repair lan truyền fault qua service FaultTransitions; mỗi thao tác một audit chung. **Sửa bởi FR-2a (29/09/2026):** verify inspection cũng chuyển `fault_present` `detected → confirmed` | Có |
| WO-3 | Enum mới `task_kind: inspection \| repair`; `inspection_outcome: fault_present \| fault_absent \| inconclusive`. Task kind và thành viên fault bất biến. Inspection nhận `FaultStatusSets.Open`; repair nhận Open trừ detected | Có |
| WO-4 | Thêm GET detail, GET assignees, PUT assignee, POST start/complete/verify/return/cancel. PATCH chỉ sửa title/due_date/scheduled_date; không đổi wo_status hoặc người được giao bằng PATCH | Có |
| WO-5 | **BREAKING**: POST bắt buộc `task_kind`. Repair cần 1..200 fault; inspection cần fault hoặc một segment, không cả hai. Server tra commune; POST cấm work_order_id/commune_id/wo_status/cluster_id/priority_score. Item thêm task_kind/commune_id/scheduled_date/updated_at; detail thêm các trường mô tả bên dưới | Có |
| WO-6 | Segment chụp từ fault có priority cao nhất, null cuối; hoà theo created_at/length(id)/id; bỏ fault không segment. Cluster chụp khi các cluster khác null có đúng một giá trị. Priority đọc sống = max trên toàn bộ thành viên, kể cả đã release. Mock lần lượt 98.0/74.2/66.4, API ra 92.9/96.3/72.5 | Có |
| WO-7 | Nguồn `fault.work_order_id` cho BE-40 là bảng nối có released_at NULL, tối đa một do unique partial index. Câu “luôn null tới BE-21” hết nền; **đã hiện thực ở BE-40 (29/09/2026), xem F-3** | Có |
| WO-8 | Thêm `WORK_ORDER_NOT_FOUND` (404), `FAULT_NOT_FOUND` (404), `INVALID_STATE_TRANSITION`, `FAULT_ALREADY_IN_WORK_ORDER`, `FAULT_STATUS_NOT_ELIGIBLE`, `ASSIGNEE_NOT_ELIGIBLE`, `CONCURRENT_MODIFICATION` (409). Bảng §11 của decisions gọi “sáu” nhưng §8 thực tế liệt kê **bảy** mã mới | Có |
| WO-9 | Không đổi mock-work-orders.json. File loại riêng gán WO-0001/0003 inspection, WO-0002 repair. Seed FAULT-0003/0007/0011 thành in_progress để khớp WO-0002; assigned_at và started_at suy từ created_at. Không audit seed; có audit work_order thì seed từ chối. **Cần báo WP5/WP6, chưa gửi thông báo trong phiên này** | Có |
| WO-10 | BE-19 phải từ chối sửa fault đang thuộc repair hoạt động bằng 409 (mã do BE-19 chốt). Fault trong inspection vẫn được review. Đây là ràng buộc cho ticket sau, không thêm endpoint fault ở BE-23 | Có, tương lai |
| WO-11 | Thiết kế audit chung được kéo từ BE-19 lên BE-23a (đã merge PR #53); không endpoint đọc audit. WorkOrder và WorkOrderFault implement IAudited; Fault đợi BE-19 | Không |
| WO-12 | **Thêm** `GET /work-orders/{id}/poles` (`ReadWorkOrders`, phân trang): khi giao việc, kỹ sư thấy **các cột trên đoạn được giao cùng trạng thái đèn lần khảo sát đã duyệt gần nhất — TRƯỚC khi đi**, không phải sau khi khảo sát xong. Cột = cột trên các segment của phiếu (`segment_ids`, hoặc `segment_id` của inspection/repair) **hợp** cột mang fault của phiếu. Mỗi item: `pole_id, segment_id, position` (thứ tự dọc đường, 1-based, tính lại cho từng segment; segment theo thứ tự trên phiếu), `location{lat,lng}`, `fixture_status` (`unknown` khi chưa có sweep nào công bố — `status_confidence`, `last_seen_at` khi đó `null`), `open_fault_count`, `near_sensitive_poi`, `fixture_type`, `lamp_watt` (bóng đang dùng, null nếu không có), `work_order_fault_ids[]` (fault của chính phiếu trên cột). Quyền xem = quyền xem phiếu: phiếu ngoài phạm vi / không phải người được giao ⇒ 404 `WORK_ORDER_NOT_FOUND`; cột của xã ngoài phạm vi trên đường liên xã **không** hiện. Trạng thái chỉ đổi khi Quản lý chấp nhận một sweep (BE-15 P2c). `position` là thứ tự, không phải khoảng cách. **Đã báo WP5 và WP6 ngày 04/10/2026** (Mỹ gửi; PR #85 đã merge); chưa có trong `GET /sync/bundle` (BE-43) | Có (endpoint mới, SELF-SIGNED) |

### Endpoint và hình dạng BE-23 (nền tạm)

Base `/api/v1/work-orders`:

| Endpoint | Capability | Request |
|---|---|---|
| GET base | ReadWorkOrders | wo_status CSV, task_kind, assigned_to (ID hoặc me), segment_id, commune_id lặp, scheduled_from/to (date, đóng hai đầu), page/page_size |
| GET /{id} | ReadWorkOrders | — |
| GET /assignees | ManageWorkOrders | commune_id bắt buộc đúng một; page (page_size cố định 50). Trả trang `{user_id, full_name}` đủ điều kiện |
| POST base | ManageWorkOrders | task_kind, title (trim 1..200), fault_ids? hoặc segment_id?, assigned_to?, due_date?, scheduled_date?, note? → 201 detail + Location |
| PATCH /{id} | ManageWorkOrders | title?, due_date?, scheduled_date? → 200 detail |
| PUT /{id}/assignee | ManageWorkOrders | assigned_to bắt buộc có khoá, ID hoặc null → 200 detail |
| POST /{id}/start | ExecuteWorkOrders | không body → 200 detail |
| POST /{id}/complete | ExecuteWorkOrders | report_note trim ≥10; inspection có fault bắt buộc fault_outcomes[{fault_id,outcome}] phủ đúng một lần mọi thành viên; repair/inspection theo tuyến cấm khoá này |
| POST /{id}/verify | ManageWorkOrders | {note?} |
| POST /{id}/return hoặc /cancel | ManageWorkOrders | {note} bắt buộc không rỗng |

PATCH: thiếu khoá giữ nguyên; null chỉ xoá ngày; title null và body rỗng → 400.
No-op PATCH/assignee trả 200, không audit, không đổi updated_at. `scheduled_date <= due_date`
khi cả hai có; scheduled_date là ngày bắt đầu ca đêm. Listing mới nhất trước:
`created_at DESC, length(work_order_id) DESC, work_order_id DESC`; fault_ids theo chiều tăng.

Detail = item + `note, review_note, report_note, created_by, assigned_at, started_at, completed_at,
closed_at, assignee_eligible, allowed_actions[], faults[]`. Fault detail gồm `fault_id, pole_id,
segment_id, location{lat,lng}, fault_type, fault_status, severity, inspection_outcome`.
Không trả entity EF hoặc thông tin tài khoản nhạy cảm vào response/audit.

Người được giao phải là field_engineer, không khoá, có xã WO trong **DB** app_user_commune.
Mọi lý do từ chối dùng cùng `ASSIGNEE_NOT_ELIGIBLE`, details chỉ assigned_to (ngoài correlation_id).
Không FK ghép tới bảng gán xã: quản trị vẫn gỡ xã được. Không tự gỡ WO khi quyền user đổi;
detail tính assignee_eligible lúc đọc. Role/commune trong JWT vẫn tuân vòng đời token hiện hữu.

### Máy trạng thái và audit

| Trạng thái | Hành động hợp lệ |
|---|---|
| open | assign → assigned; unassign no-op; edit; cancel → cancelled |
| assigned | reassign → assigned (cùng người no-op); unassign → open; edit; start → in_progress; cancel |
| in_progress | reassign → assigned (cùng người no-op); unassign → open; edit; complete → done; cancel |
| done | verify → verified; return → in_progress |
| verified / cancelled | không hành động ghi |

Manager làm assign/unassign/edit/verify/return/cancel; chỉ FE được giao làm start/complete.
Sai capability → 403 không phụ thuộc ID có tồn tại; sau capability và validate, lookup áp cả
commune và assignee → 404; sai trạng thái → 409. Lỗi concurrency cũng lookup lại bằng filter,
không lộ WO nếu vừa bị đổi người giao.

Repair start: confirmed → in_progress, đã in_progress giữ. Complete **chưa** resolved.
Verify: in_progress → resolved → verified trong cùng SaveChanges; resolved_by là người được giao,
resolved_at là completed_at. Fault không đúng trạng thái nguồn bỏ qua, ghi fault_skipped trong
audit. Inspection chỉ ghi outcome, không sửa fault_status. Verify/cancel release mọi dòng nối;
done còn giữ fault. Cancel repair không lùi trạng thái fault.

Đúng một audit/thao tác làm thay đổi, 0 cho no-op/lỗi. Audit chụp DTO riêng, actor và vai trò,
correlation ID, thời điểm chung với nghiệp vụ, before/after và fault_changes/fault_skipped.
Unique index bắt hai create cùng fault; xmin bắt sửa WO/fault đồng thời; transaction rollback cả
nghiệp vụ và audit của bên thua. Không BE-24 evidence, BE-27 notification, SLA, ExternalUnit,
gom địa lý hay khảo sát trong phạm vi này.


### Yêu cầu của FE về phiếu công việc (29/09/2026)

| | |
|---|---|
| **Decision** | Trả lời năm đề nghị của WP5 về `WorkOrder` (survey, mã sự vụ, vật tư, gửi cấp trên, minh chứng) |
| **Decision maker** | **Mỹ chốt 29/09/2026** cho FR-1, FR-2, FR-2a (C), FR-3, FR-5; FR-4 **chờ quyết**. Chạm API, chưa qua FW → `SELF-SIGNED` |
| **Date** | 29/09/2026 |
| **Scope** | BE-15, BE-23 (phiếu công việc), BE-24. Chưa đổi code, chưa migration. Mọi mục chạm API là **nền tạm tới FW kế tiếp** |

| Mã | Đề nghị của FE | Quyết định | Chạm API |
|---|---|---|---|
| **FR-1** | Thêm `survey` vào `task_kind` | **Nhu cầu đúng** (Phiếu v1.4: Quản lý *"create and assign survey routes"*), nhưng **quyết ở Phase 1 của BE-15**, không thêm lẻ: hoàn thành khảo sát là nộp **phiên khảo sát** (D-R21), phiên được duyệt hoặc bác và khảo sát lại. Hướng nghiêng: `task_kind = survey` dùng chung lịch, giao việc, audit của phiếu, gắn với tuyến và phiên khảo sát. Thêm giá trị = đổi CHECK DB (migration) | Có |
| **FR-2** | `case_code` / `parent_id` nối khảo sát → kiểm tra → sửa chữa | **Nhận và ĐÃ HIỆN THỰC 29/09/2026** (Mỹ chốt; đổi từ đề xuất "không làm" — Quản lý làm việc theo từng lịch nối tiếp). Item và chi tiết phiếu thêm `parent_work_order_id` (null ở lịch đầu) và `case_id` (= ID lịch đầu tiên của chuỗi, không prefix mới). `GET /work-orders?case_id=` trả cả chuỗi. **`POST /work-orders/{id}/follow-up`** (`ManageWorkOrders`) body `{task_kind, title?, fault_ids?, assigned_to?, due_date?, scheduled_date?, note?, materials_note?}` → 201 chi tiết: server mang sang các sự cố `fault_present` của lịch cha; `title` mặc định = của cha; `fault_ids` chỉ để **tách** (tập con của các sự cố mang sang, sai → 400 kèm `carried_fault_ids`); nhiều lịch con cùng cha = cây chung `case_id`. Lịch cha phải `verified` (khác → 409 `INVALID_STATE_TRANSITION`); cặp hợp lệ hiện chỉ **kiểm tra → sửa chữa** (khảo sát thêm ở BE-15), sai cặp → 409 `INVALID_STATE_TRANSITION` kèm `allowed_follow_up_kinds`; không có sự cố `fault_present` → **409 `NOTHING_TO_FOLLOW_UP`** (mã mới). `allowed_actions` của Quản lý có `follow_up` ở phiếu kiểm tra `verified`. DB: `parent_work_order_id`, `root_work_order_id` (null ở lịch đầu, API tính `case_id = root ?? chính nó`), FK ghép cùng xã, CHECK `ck_work_order_chain_complete` | Có |
| **FR-2a** | (phát sinh từ FR-2) sự cố `fault_present` vẫn `detected` sau khi nghiệm thu kiểm tra, mà sửa chữa chỉ nhận `confirmed`/`in_progress` | **Chốt C và ĐÃ HIỆN THỰC 29/09/2026:** `verify` một phiếu **kiểm tra** chuyển các sự cố `fault_present` từ `detected` → `confirmed` (`confirmed_by` = Quản lý nghiệm thu, `confirmed_at` = lúc nghiệm thu), ghi vào `fault_changes[]` của audit phiếu; sự cố không ở `detected` → `fault_skipped[]`. `fault_absent` và `inconclusive` **không đổi** — bác bỏ vẫn qua BE-19. **Sửa WO-2** ("chỉ repair lan truyền fault"): nay inspection cũng lan truyền, đúng một chuyển này | Có (ngữ nghĩa) |
| **FR-3** | `materials_note: string?` trên tạo phiếu và hoàn thành | **Nhận, tách HAI trường** vì là hai sự thật khác nhau: `materials_note` (Quản lý, ở `POST` và `PATCH` phiếu — cần mang gì) và `materials_used` (Kỹ sư được giao, ở `complete` — đã dùng gì). Văn bản tự do, không bảng vật tư. Một trường chung thì báo cáo của Kỹ sư ghi đè kế hoạch của Quản lý. **Đã hiện thực 29/09/2026** (migration `AddWorkOrderMaterials`): cắt khoảng trắng, chuỗi rỗng lưu `null` (CHECK `ck_work_order_materials_*_not_blank`); `PATCH` thiếu khoá giữ nguyên, `null` xoá; mỗi lần `complete` ghi lại `materials_used` (kể cả thành `null`) như `report_note`; hai trường chỉ có ở **chi tiết** phiếu, không ở item danh sách; có trong audit snapshot | Có |
| **FR-4** | `POST /work-orders/{id}/submit-superior` | **Chờ quyết.** Đề xuất của BE1: không làm — Cấp giám sát **chỉ đọc** (Phiếu v1.4, Contract §2) và đã đọc được mọi phiếu `verified` trong xã qua `ReadWorkOrders`; báo cáo gửi lên là việc của BE-28…BE-31. Lưu ý FE: nghiệm thu chuyển sang `verified` (không phải `closed`); nhận xét của Quản lý đi qua `note` của `verify` → `review_note`; `report_note` của Kỹ sư không sửa được | Có |
| **FR-5** | `evidence_urls: string[]` khi hoàn thành | **Không dùng URL.** Minh chứng đi qua `POST /work-orders/{id}/evidence` (Contract §5.5, multipart, JPEG theo magic bytes) — **BE-24**; byte phải qua API để canh phạm vi xã (BE-11 quy tắc 1). Video minh chứng vẫn **planned** (D-R14). Việc chốt ở Phase 1 BE-24: repair có **bắt buộc** ≥1 ảnh `after` trước `complete` không | Có |

**Phải báo:** WP5 (người gửi đề nghị) — FR-1, FR-5 đã chốt; FR-2, FR-2a, FR-3 đã chốt **và hiện thực**; FR-4 chờ. Đặc biệt phải báo: nghiệm thu kiểm tra nay **đổi trạng thái sự cố** (FR-2a), và mã lỗi mới `NOTHING_TO_FOLLOW_UP`. WP6 cũng cần `materials_used` ở màn hoàn thành phiếu. **Chưa báo.**

### BE-14 / IoT — chỉ còn thiết bị đo điện ở tủ điện tổng (28/09/2026)

| | |
|---|---|
| **Decision** | Mô hình thiết bị IoT theo phạm vi thật của đồ án: **một loại** thiết bị cố định, gắn ở **trụ điện / tủ điện tổng**, đo dòng điện và trạng thái nguồn, nhận lệnh ON/OFF/AUTO. **Không có IoT trên từng cột đèn.** Module BH1750 trên xe **không** phải `iot_node` (thuộc phiên khảo sát, D-R21 / BE-15) |
| **Decision maker** | **Mỹ (Dylan)** — trực tiếp trong phiên 28/09/2026, trên đề xuất của Claude |
| **Date** | 28/09/2026 |
| **Scope** | Contract §3.1 (`node_role`), §5.1 (`has_iot_node`), §5.2 (`controller_node_id`), §5.6 (`GET /iot-nodes`), mock `mock-iot-nodes.geojson` + chuỗi `POLE-0047`/`NODE-047`; chặn thiết kế IOT-10 / BE-14 `/iot-nodes`. **Chưa đổi Contract, mock hay code** |

| Mã | Chốt | Chạm API |
|---|---|---|
| **I-1** | **Bỏ `sampled_fixture`** — không có thiết bị IoT gắn trên từng cột. 9 node `sampled_fixture` của mock không còn đại diện cho gì | **Có — BREAKING** (enum §3.1, mock) |
| **I-2** | **Bỏ `battery_pct`** khỏi properties của `GET /iot-nodes` (thiết bị ở tủ điện dùng điện lưới; đèn solar đã bỏ từ v1.6) | **Có — BREAKING** (§5.6, mock) |
| **I-3** | `node_status` **tính lúc đọc**, không lưu cột: chưa từng báo → `never_reported`; `last_report_at` quá ngưỡng im lặng → `offline`; còn lại `online`. Ngưỡng cấu hình được (BE-33), cùng ngưỡng IOT-11 dùng để sinh `node_offline` | Không (ngữ nghĩa, giữ enum) |
| **I-4** | Thiết bị có **toạ độ riêng** (vị trí tủ điện, `Point` 4326) và gắn với **mạch điện** (`feeder_id`), không gắn với cột. Tuyến (`segment_id`, `controller_node_id` của `/segments`) **suy ra** từ các cột trên mạch | **Có** (§5.6 thêm `feeder_id`; nguồn của `segment_id` đổi) |
| **I-5** | `data_source` của thiết bị và telemetry = **`calibration_rig`** — theo D-R10 nhóm không lắp thiết bị ngoài thực địa, thiết bị chỉ có trên testbed. Đóng Contract **O-9** mà không thêm giá trị enum | Không (dùng giá trị có sẵn) |
| **I-6** | Bảng thiết bị mang `supports_remote_control` và **chế độ hiện tại** do thiết bị báo về (`on \| off \| auto`). **Lệnh** đã gửi (ai, lúc nào, kết quả) ở bảng lệnh riêng + audit (D-R7, D-R13) — thuộc ticket điều khiển đèn, **không** thuộc BE-14 | **Có** (enum chế độ mới, trường mới trên `/iot-nodes`) |

**Bổ sung cùng ngày — kênh và điều khiển theo segment (Mỹ, 28/09/2026):**

| Mã | Chốt | Chạm API |
|---|---|---|
| **I-12** | ~~Thiết bị có 1..n kênh; mỗi kênh cấp đúng một mạch.~~ **Sửa cùng ngày (Mỹ, 28/09/2026): "kênh" = một RƠ-LE của thiết bị, và KHÔNG phải đối tượng riêng.** Vì mỗi rơ-le cấp đúng một mạch, rơ-le được **gộp vào feeder**: một feeder có **0 hoặc 1** thiết bị điều khiển, mô tả bằng `node_id` + `relay_no` + chế độ hiện tại `on \| off \| auto` (sửa I-6: chế độ thuộc **feeder được điều khiển**, không thuộc cả thiết bị). Lưu ở **bảng riêng khoá theo `feeder_id`**, không thêm cột vào `feeder` — chế độ do thiết bị báo liên tục còn `feeder` do Quản lý sửa qua `/assets` (`PUT` thay thế toàn phần sẽ xoá mất chế độ); cùng lý do `pole_current_status` tách khỏi `pole` (BE-09 quy tắc 2). **UNIQUE `(node_id, relay_no)`**: hai feeder không được khai cùng một rơ-le. Chuỗi: thiết bị → (rơ-le) → `feeder` → `pole.feeder_id` → `pole.segment_id`. Không có bảng thiết bị ↔ cột song song. Giao diện không hiện chữ "kênh"/"rơ-le" — chỉ "feeder và thiết bị điều khiển nó" | **Có** (hình dạng thiết bị) |
| **I-13** | ~~Trên testbed, đi dây mỗi kênh = một segment.~~ **Sửa 28/09/2026 (Mỹ):** testbed thật có **1 thiết bị, 2 feeder — đèn LẺ và đèn CHẴN** — khoảng 10 đèn xen kẽ trên **cùng một segment**, nên một segment có hai feeder. Lệnh điều khiển phải cho chọn **theo segment HOẶC theo feeder**: chọn segment đổi cả hai rơ-le; chọn feeder "đèn lẻ" chỉ đổi rơ-le đó. Ngoài thực địa thiết bị chỉ **đo và báo**, không nhận lệnh (D-R7). Thuộc ticket điều khiển đèn | Có (ticket điều khiển) |
| **I-14** | Lệnh ghi **theo kênh**; giao diện cho chọn **theo segment**, server tra ra các kênh cấp điện cho segment đó. Mạch cấp cho **nhiều segment** → **cảnh báo** (liệt kê segment bị kéo theo) **rồi vẫn cho gửi**. Cột không thuộc kênh nào → báo "N cột không điều khiển được". Mỗi lệnh ghi audit | **Có** — thuộc ticket điều khiển đèn, không thuộc BE-14 |
| **I-15** | Trên bản đồ: thiết bị là **một điểm riêng** tại vị trí tủ; chọn thiết bị → highlight các cột và segment nó cấp điện (suy ra, không nhập tay) | **Có** (§5.6) |
| **I-17** | **Cho IOT-09 (Đạt):** thiết bị không biết mã feeder của hệ thống, chỉ biết số rơ-le. Telemetry dòng điện và phản hồi lệnh gửi lên mang **`node_id` + `relay_no`**; backend tra bảng điều khiển feeder (I-12) để ra `feeder_id` | Có (payload ingest, IOT-07/09) |

**Chốt tiếp cùng ngày (Mỹ, 28/09/2026) — đồng ý đề xuất của Claude:**

| Mã | Chốt | Chạm API |
|---|---|---|
| **I-7** | `node_role` **giữ tên `segment_controller`**, còn đúng một giá trị (tiền lệ `power_source = grid` v1.6). Tên nói "tuyến" trong khi thiết bị gắn mạch — chấp nhận để FE không phải sửa | Có (enum thu hẹp — đi cùng I-1) |
| **I-8** | `has_iot_node` trên `GET /poles` **giữ khoá, luôn `false`** — không xoá để FE không vỡ | Không (hình dạng giữ nguyên) |
| **I-9** | Fault `runtime_decline` (đo theo mạch) có `pole_id` **null**, `location` = **toạ độ tủ điện** của thiết bị. Chưa thêm `feeder_id` vào `fault` — xét lại khi có ticket cần | Có (ngữ nghĩa `runtime_decline`) |
| **I-10** | Demo `POLE-0047` / `NODE-047` **dựng lại ở mức mạch**; phải hỏi WP5 còn dùng màn biểu đồ runtime theo cột không trước khi đổi mock | Có (mock) |
| **I-16** | Dữ liệu "tủ nào cấp cho cột nào": **ngoài thực địa** nhóm thực địa hỏi xã; **testbed** nhóm tự khai theo sơ đồ đi dây | Không |
| **I-7b** | `controller_node_id` của `GET /segments` (§5.2) **đổi thành `controller_node_ids[]`** (Mỹ, 28/09/2026). Danh sách **tính lúc đọc**, không lưu: segment → cột (`pole.segment_id`) → `feeder` (`pole.feeder_id`) → bảng điều khiển feeder (I-12) → thiết bị; không trùng lặp, sắp theo khuôn `created_at, length(id), id`. Rỗng `[]` khi không thiết bị nào điều khiển — **không** `null`. Dữ liệu nhập thêm hay đi dây lại có hiệu lực ở request kế tiếp. Hôm nay mọi segment ra `[]` (0/103 cột có `feeder_id`, bảng điều khiển chưa có). Cùng kiểu `pole_count` / `has_active_segment_fault` (tính lúc đọc) | **Có — BREAKING** (đổi tên + kiểu; hôm nay trường luôn `null` nên FE chưa có logic dựa vào giá trị) |

⚠️ I-12…I-15 **cần O-6** (`feeder_id` của cột) có dữ liệu: hôm nay `luxmap_dev` có **0/103** cột mang `feeder_id`. Chưa có O-6 thì thiết bị không nối được với đèn nào.

**Hệ quả còn phải quyết — ghi lại để không ai coi là đã xong:**

| # | Câu hỏi | Vì sao phải quyết |
|---|---|---|

**BE-14b — hiện thực (Mỹ chốt 28/09/2026, BE1 tự làm):**

| Mã | Chốt / hiện thực | Chạm API |
|---|---|---|
| **I-11** | **BE-14b làm ngay, do BE1.** Mỹ quyết BE1 dựng `iot_node` + `feeder_control` (phần tương ứng của IOT-10); bảng telemetry và ingest **vẫn của Đạt** (IOT-09/10). Ngưỡng offline **1 giờ**, cấu hình `Iot:OfflineAfter` (≤ 0 → không khởi động). Bộ mock có **mạch TẠM** mỗi segment một feeder (`FDR-001..003`, `external_ref = DEMO-SEG-00n`, 103/103 cột gán) và 3 thiết bị dùng toạ độ `mock-iot-nodes.geojson`; **không** phải dữ liệu O-6. Testbed thật (1 thiết bị, 2 feeder lẻ/chẵn, ~10 đèn) seed khi có toạ độ mô hình | Có |
| **I-18** | `GET /iot-nodes` (capability `ReadNetwork`, `bbox` bắt buộc, `FeatureCollection` điểm). `properties` **đúng 8 khoá**: `node_id, node_role, node_status, pole_id, segment_ids, feeder_ids, supports_remote_control, last_report_at`. `pole_id` **luôn null** (giữ khoá); `battery_pct` bỏ; `segment_id` → `segment_ids[]`; `feeder_ids[]` theo **thứ tự rơ-le**, `segment_ids[]` theo khuôn `created_at, length(id), id`. ⚠️ MapLibre lưu mảng trong `properties` thành chuỗi JSON — FE phải parse khi dựng popup. `data_source` **mặc định loại `calibration_rig`** như `/poles`, `/segments` (§1.6) — testbed chỉ hiện khi hỏi tên (`data_source=calibration_rig`, hoặc `calibration_rig,simulated` để thấy cả hai). **Mỹ xác nhận giữ mặc định ẩn, 29/09/2026**; FE cần nút bật/tắt "Hiện testbed" thì chỉ thêm tham số, backend không đổi | **Có — BREAKING** so với §5.6 |
| **I-19** | Thiết bị **mock** mang `data_source = simulated`, `supports_remote_control = false` (dữ liệu demo, không phải phần cứng). Thiết bị testbed: `calibration_rig`, `true`. DB **siết hơn enum**: `ck_iot_node_data_source_not_field` chỉ nhận `calibration_rig \| simulated` (I-5, D-R10) | Không (giá trị có sẵn) |

**Mock đã đổi theo I-1, I-2, I-7b, I-8** (28/09/2026): `mock-iot-nodes.geojson` còn 3 node với hình dạng I-18; `mock-segments.geojson` `controller_node_id` → `controller_node_ids[]`; `mock-poles.geojson` 9 cột `has_iot_node` true → false. `mock-pole-detail.json` (`iot_node: NODE-047`) **chưa sửa** — chờ I-10 hỏi WP5.

**Phải báo:** WP5 (lớp IoT mới + parse mảng, `battery_pct` bỏ, `has_iot_node` luôn false, `controller_node_ids[]`, demo `POLE-0047`, 4 file mock đổi), WP6 (cùng các trường qua sync bundle sau này), và **BE2 – Đạt** (BE1 đã dựng `iot_node` + `feeder_control` theo I-1…I-6, I-12; telemetry vẫn của Đạt, payload mang `relay_no` theo I-17; `last_report_at` là cột Đạt sẽ ghi). **Chưa báo ai.**

## BE-40 — `GET /faults` (29/09/2026)

| | |
|---|---|
| **Decision** | Hiện thực `GET /api/v1/faults` theo §5.4, với sáu điểm Contract không nói (F-1…F-6) |
| **Decision maker** | **Mỹ chốt 29/09/2026** — chọn đúng các đề xuất D-1…D-9 ở `.ai/results/BE-40-p1.md`. Chưa qua FW → `SELF-SIGNED` |
| **Date** | 29/09/2026 |
| **Scope** | BE-40; Contract §2, §5.4. Không sửa Contract trong ticket này. Mọi mục chạm API là **nền tạm tới FW kế tiếp xác nhận**; im lặng là ESCALATE, không phải approve |

| Mã | Deviation / quyết định áp dụng | Chạm API |
|---|---|---|
| **F-1** | Capability mới `ReadFaults` (`cap:read_faults`) = cả bốn vai trò. Tách khỏi `ReadNetwork` để siết quyền đọc sự cố về sau mà không đụng bản đồ (O-2) | Có |
| **F-2** | Kỹ sư hiện trường thấy **mọi** fault trong xã của mình, không chỉ fault thuộc phiếu được giao — để khỏi báo trùng (BE-41). WO-1 chỉ giới hạn *phiếu* | Có |
| **F-3** | `work_order_id` = phiếu đang giữ fault (`released_at IS NULL`, WO-7), **kể cả** khi người gọi không mở được phiếu đó (WO-1 trả 404). `null` nghĩa là thật sự chưa có phiếu. §5.4 ghi "luôn null tới BE-21" — **hết hiệu lực** | Có |
| **F-4** | `location` = `fault.lat/lng`, thiếu thì lấy điểm của cột (cùng luật với chi tiết WO). `bbox` **tuỳ chọn** ở đây, lọc khoảng số bao hai đầu trên chính điểm đó; không cột geometry, không GIST | Có |
| **F-5** | `data_source` mặc định **ẩn `calibration_rig`** như `/poles`, `/segments`, `/iot-nodes` (§1.6); muốn thấy testbed thì truyền tên | Có |
| **F-6** | `sort` ∈ `priority_score`, `detected_at`, `updated_at`, mỗi cái có thể thêm `-`; `priority_score` null **luôn cuối** cả hai chiều; hoà thì `created_at, length(fault_id), fault_id` **tăng dần** bất kể chiều khoá (khớp mock). `status`, `severity`, `fault_type`, `source_channel`, `data_source` nhận CSV theo tên wire; `pole_id`/`segment_id`/`cluster_id` một giá trị; thêm `commune_id` lặp được (thu hẹp, ngoài phạm vi → 403). Giá trị sai → 400 `VALIDATION_FAILED` kèm `details.allowed` | Có |

**Đối chiếu mock (luxmap_test, 29/09/2026):** 28/28 fault, thứ tự mặc định khớp, 18 khoá khớp từng
trường, 11/11 `work_order_id` khớp. Lệch duy nhất: `fault_status` của FAULT-0003/0007/0011 là
`in_progress` (mock ghi `confirmed`) — đã ghi ở **WO-9**, không phải lệch mới.

**Phải báo:** WP5 (F-1…F-6; `work_order_id` nay có giá trị thật), WP6 (cùng item qua sync bundle sau
này). **Chưa báo ai.**

## Ưu tiên sự cố — Quản lý đặt, hoãn chấm điểm tự động (29/09/2026)

| | |
|---|---|
| **Decision** | Quản lý tự đặt mức ưu tiên của sự cố; hoãn chấm điểm tự động (CV-16). P-2, P-3 đã hiện thực; P-1 chờ nhóm |
| **Decision maker** | Mỹ chốt 29/09/2026 cho P-2 và P-3 (`SELF-SIGNED`). P-1 và cả P-3 chạm việc của WP4/WP5/WP6 → **vẫn nêu ở FW kế tiếp** |
| **Date** | 29/09/2026 |
| **Scope** | Contract §5.4 (thứ tự mặc định, `PATCH /faults`), §5.5; BE-19, BE-33; CV-16, FW-25, FM-25 |

**Căn cứ.** Phiếu v1.4 chỉ nêu ưu tiên ở **phần vấn đề** (dòng 41: *"Repairs are unprioritized… no basis
for treating an unlit school approach or bridge as more urgent"*) và ở tài liệu tham khảo (dòng 335);
phần giải pháp, chức năng, vai trò Quản lý và NFR **không** có chấm điểm ưu tiên. "Cơ sở" để Quản lý
phân biệt đã có trên bản đồ (trạng thái đèn, cụm cả đoạn, `near_sensitive_poi`, `road_class`). Công
thức CV-16 hiện **chưa được viết** ở đâu; các tiêu chí đều là mức chữ nên điểm 0–100 chỉ có tối đa ~16
giá trị khác nhau và trọng số không có đối tác xác nhận (ghi chú CV-16 v2.0).

| Mã | Đề xuất | Ai bị ảnh hưởng | Chạm API |
|---|---|---|---|
| **P-1** | Hoãn chấm điểm tự động. `fault.priority_score` **giữ cột, nullable**, không xoá — CV-16 làm sau được mà không migration | Thịnh (CV-16), Khang (FW-25 tab trọng số), BE-33 (phần trọng số) | Không (giữ hình dạng) |
| **P-2** | Quản lý đặt mức ưu tiên bằng **`severity`** (`low\|medium\|high\|critical`, enum có sẵn) qua `PATCH /faults` — làm ở **BE-19**, có audit | WP5, WP6 | **Có** (§5.4 body PATCH thêm `severity`) |
| **P-3** | Thứ tự mặc định `GET /faults` đổi từ `-priority_score` sang **`-severity`** (hạng `critical > high > medium > low`), cùng mức thì **sự cố cũ trước** (`detected_at` tăng), rồi `created_at, length(fault_id), fault_id`. `sort` nhận thêm `severity` / `-severity`; `-priority_score` vẫn dùng được. **Mỹ chốt 29/09/2026, ĐÃ HIỆN THỰC** — `SELF-SIGNED`, nền tạm: vẫn phải nêu ở FW vì chạm việc của Thịnh, Khang, FM-25. Danh sách **phiếu công việc** giữ nguyên (`created_at` giảm dần) — chưa đổi. ⚠️ `mock-faults.json` đang xếp theo `priority_score`, thứ tự API mặc định nay khác mock | **Có** (§5.4) |

**Làm sau được không:** được. Không bước nào xoá dữ liệu hay cột; bật lại CV-16 = ghi `priority_score`
và trả thứ tự mặc định về như cũ. `severity` do Quản lý đặt vẫn là một tiêu chí dùng được cho công thức.

**Phải báo / đưa ra FW kế tiếp:** Thịnh, Khang, WP6 (FM-25). **Chưa báo.**

## BE-19 — `PATCH /faults/{id}`: Quản lý duyệt sự cố (29/09/2026)

| | |
|---|---|
| **Decision** | Hiện thực `PATCH /faults/{id}` theo §5.4, với các điểm Contract không nói (R-1…R-9) |
| **Decision maker** | **Mỹ chốt 29/09/2026** — theo đề xuất D-1…D-10 ở `.ai/results/BE-19-p1.md`, **đã chỉnh sau review độc lập của Codex CLI** (xem mục "Chốt sau review" trong file đó). Chưa qua FW → `SELF-SIGNED` |
| **Date** | 29/09/2026 |
| **Scope** | BE-19; Contract §2, §3.2, §5.4. Không sửa Contract. Mọi mục chạm API là **nền tạm tới FW kế tiếp** |

| Mã | Quyết định | Chạm API |
|---|---|---|
| **R-1** | Capability mới `ReviewFaults` (`cap:review_faults`) = **chỉ Quản lý** | Có |
| **R-2** | `PATCH` **chỉ** làm `detected → confirmed \| rejected`. `confirmed → in_progress → resolved → verified` hợp lệ ở §3.2 nhưng do **phiếu sửa chữa** lái (WO-2) → ở đây 409 `INVALID_STATE_TRANSITION` kèm `allowed_actions` | Có (thu hẹp §5.4) |
| **R-3** | Sự cố đang bị **phiếu sửa chữa** giữ (liên kết chưa giải phóng, **bất kể** trạng thái phiếu: open/assigned/in_progress/done) → **409 `FAULT_IN_ACTIVE_REPAIR`** (mã mới) kèm `work_order_id`. Phiếu **kiểm tra** không chặn (WO-10) | Có |
| **R-4** | `override_fault_type`: chỉ `lamp_out ↔ lamp_dim`; lưu ở cột **`fault.override_fault_type`**, `fault.fault_type` giữ **nguyên loại kênh báo** (không phải ground truth — v1.4 `dim` = CV + lux). API trả và lọc `fault_type` = `override ?? fault_type`. Đặt về đúng loại gốc = xoá override; `null` = xoá. Loại khác hoặc sự cố không phải lamp → 400 | Có |
| **R-5** | Body thêm **`severity?`** (P-2) — Quản lý đặt mức ưu tiên. `null` → 400 | Có |
| **R-6** | `note` của PATCH lưu vào cột mới **`review_note`** (không ghi đè `note` của người báo); cắt khoảng trắng; rỗng/`null` = xoá; thiếu khoá = giữ | Có |
| **R-7** | Item `GET /faults` và response PATCH thêm **`review_note`** và **`allowed_actions[]`** → **20 khoá**. `allowed_actions` ∈ `confirm, reject, reclassify, set_severity, edit_note`; **rỗng** với mọi vai trò khác Quản lý, với sự cố đóng, và với sự cố bị phiếu sửa chữa giữ. Chỉ mang tính gợi ý: race vẫn có thể ra 409 | Có |
| **R-8** | `fault_status` bắt buộc; gửi **đúng trạng thái hiện tại** = không chuyển, vẫn áp `severity`/`override_fault_type`/`note`. Không đổi gì → 200, **không** audit, **không** đổi `updated_at`. Sửa khi sự cố đã đóng → 409. Chặn "phiếu sửa chữa" áp **trước** mọi thứ khác, kể cả no-op | Có |
| **R-9** | Thứ tự lỗi: 403 capability → 400 body → 404 `FAULT_NOT_FOUND` (không tồn tại **hoặc** ngoài phạm vi) → 409 `FAULT_IN_ACTIVE_REPAIR` → 409 `INVALID_STATE_TRANSITION` → 409 `CONCURRENT_MODIFICATION` | Có |

**Ngoài API (không chạm hình dạng):** `Fault` nay là `IAudited` — **mọi** lần ghi sự cố phải kèm đúng một
audit trong cùng `SaveChanges` (D-R13): quyết định của Quản lý ghi `entity_type = fault`, `action =
confirmed | rejected | details_changed`. Tạo phiếu công việc và `PATCH` sự cố **khoá hàng `fault`**
(`FOR UPDATE`) rồi mới đọc trạng thái để kiểm — chặn race "bác bỏ trong lúc đang tạo phiếu" mà `xmin`
không thấy (Codex phát hiện). Phiếu tạo tiếp (FR-2) vẫn **không** tự lọc sự cố đã bị bác bỏ sau khi
kiểm tra: mặc định mang cả nhóm `fault_present`, gặp sự cố không đủ điều kiện → 409
`FAULT_STATUS_NOT_ELIGIBLE`; Quản lý gửi `fault_ids` để chọn.

**Phải báo:** WP5 (FW-12: 20 khoá, `allowed_actions`, `review_note`, 2 mã lỗi mới, `fault_type` là loại
đã quyết). **Chưa báo.**


## Ảnh bằng chứng — hai chỗ Contract còn hở (30/09/2026)

| | |
|---|---|
| **Decision** | **Chỉ đăng ký, chưa quyết.** Hai chỗ Contract không cho gửi ảnh ở tình huống nó tự nêu. Phát hiện khi rà các tình huống dùng ảnh trong app (thảo luận 30/09/2026) |
| **Decision maker** | **Chưa có.** Cả hai chạm bề mặt API → **ESCALATE ở FW kế tiếp**, không tự chốt, im lặng không phải approve |
| **Date** | 30/09/2026 |
| **Scope** | Contract §5.4 (`POST /faults` — `photo_frame_id`), §5.5 (`POST /work-orders/{id}/evidence` — `kind`); BE-24, BE-41; FM-18, FM-19. Chưa đổi Contract, chưa đổi code |

| Mã | Chỗ hở | Hệ quả | Hướng đề xuất (chưa chốt) | Chạm API |
|---|---|---|---|---|
| **EV-1** | Ảnh của **phiếu kiểm tra** không có `kind` đúng nghĩa. Evidence chỉ nhận `kind=before\|after`; kiểm tra không sửa gì nên không có "trước/sau", chỉ có "đã thấy thế này". `task_kind = inspection` và kết quả `fault_present \| fault_absent \| inconclusive` đã có từ BE-23 (nền tạm), Contract chưa nhắc | App phải gửi ảnh kiểm tra dưới nhãn `before` hoặc `after` — sai nghĩa, và không lọc ra được. Ảnh kiểm tra ban đêm là ứng viên **ground truth** cho lớp `out` và có thể cho `dim` (D-R23) — loại ảnh quan trọng nhất về nghiên cứu lại không có chỗ đúng | Thêm giá trị `kind = observation` (hoặc tên khác), chỉ nhận ở phiếu `inspection`; `before`/`after` chỉ nhận ở phiếu `repair`. Chốt ở Phase 1 **BE-24** | **Có** (§5.5 enum `kind`) |
| **EV-2** | **Báo sự cố tại chỗ không upload được ảnh.** `POST /faults` có `photo_frame_id` — *"ảnh upload trước qua luồng evidence"* — nhưng luồng evidence nằm dưới `/work-orders/{id}/`, còn báo tại chỗ (FM-19) **chưa có phiếu nào**. Theo đặc tả hiện tại không có cách lấy `photo_frame_id` | FM-19 không gửi được ảnh; BE-41 sẽ vấp khi hiện thực. Tên trường còn gây nhầm: `frame` là ảnh **khảo sát** (`SurveyFrame`), không phải ảnh bằng chứng — hai luồng ảnh tách riêng từ BE-11 | Hai hướng: (a) endpoint upload ảnh **không gắn phiếu**, trả ID rồi gửi kèm `POST /faults`; (b) `POST /faults` nhận multipart kèm ảnh. Đổi tên trường cho khỏi lẫn với `SurveyFrame`. Chốt ở Phase 1 **BE-41** | **Có** (§5.4, có thể thêm endpoint) |

**Chung cho cả hai:** ảnh bằng chứng là cho **người xem**, **không** vào dữ liệu chấm của CV — ảnh chụp tự
chỉnh phơi sáng (mẫu thực địa 28/09 tự nhảy ISO 8000), không so sánh được; trạng thái đèn lấy từ video khoá
phơi sáng (D-R24). Byte ảnh vẫn qua API (BE-11 quy tắc 1).

**Phải báo / đưa ra FW kế tiếp:** WP6 (FM-18, FM-19), WP5 (màn chi tiết phiếu). **Chưa báo.**

**EV-1 đã chốt (Mỹ, 04/10/2026) và hiện thực ở BE-24:** thêm `kind = observation`. Phiếu **sửa chữa** nhận `before`
(đèn lúc tới nơi) và `after` (sau khi sửa); phiếu **kiểm tra** chỉ nhận `observation` (đã thấy thế này — kiểm tra không sửa
gì nên không có trước/sau); phiếu khảo sát không nhận ảnh. Sai nhãn → `400 EVIDENCE_KIND_NOT_ALLOWED`.

**EV-2 đã chốt (Mỹ, 04/10/2026) và hiện thực ở BE-41:** *tạo sự cố rồi gắn ảnh*. `POST /faults` giữ JSON như Contract; ảnh gửi
sau qua `POST /faults/{fault_id}/photos`. Trên app vẫn là **một form** (chụp ảnh ngay trong form, bấm Gửi một lần); app tự gửi
sự cố trước rồi tải ảnh — mất sóng chỉ làm chậm ảnh, không làm mất báo cáo. Trường `photo_frame_id` **bỏ**: gửi lên → 400.

### BE-24 — ảnh của phiếu công việc (04/10/2026)

**SELF-SIGNED, nền tạm tới FW** (quyết định của Mỹ cùng ngày: thêm `observation`, thêm đường xem ảnh, bắt buộc ảnh `after`).

| Endpoint | Quyền | Ghi chú |
|---|---|---|
| `POST /work-orders/{id}/evidence` | `ExecuteWorkOrders`, **chỉ người được giao**, phiếu **`in_progress`** | multipart: `file` (JPEG theo magic bytes, ≤ 16 MiB), `kind`, `captured_at` (ISO 8601, có offset thì quy về UTC), `lat`, `lng`, **`client_op_id` (tuỳ chọn, mới)** — gửi lại cùng khoá trả **200** cùng ảnh, khác phiếu/nhãn → `409 IDEMPOTENCY_CONFLICT`. Mới tạo **201** |
| `GET /work-orders/{id}/evidence` | `ReadWorkOrders` | **Mới.** Phân trang, sắp theo `captured_at`; item `evidence_id, work_order_id, kind, captured_at, lat, lng, uploaded_by, uploaded_at, thumbnail_url, original_url` |
| `GET /evidence/{evidence_id}/thumbnail`, `…/original` | `ReadWorkOrders` | **Mới.** JPEG qua API, không presigned; ảnh gốc **nguyên byte**. Quyền theo phiếu cha (xã **và** người được giao) → ngoài quyền `404 EVIDENCE_NOT_FOUND`; file mất trong kho `503 STORAGE_OBJECT_MISSING` |

Mã lỗi mới: `400 EVIDENCE_KIND_NOT_ALLOWED`, `409 WORK_ORDER_NOT_IN_PROGRESS`, **`409 AFTER_EVIDENCE_REQUIRED`** — `POST
/work-orders/{id}/complete` của phiếu **sửa chữa** bị từ chối khi chưa có ảnh `after` (ảnh `before` không tính). Phiếu kiểm
tra không bắt buộc ảnh. `415 UNSUPPORTED_IMAGE_FORMAT`, `404 WORK_ORDER_NOT_FOUND` giữ như Contract.
`415 UNSUPPORTED_IMAGE_FORMAT` gồm cả file **bắt đầu như JPEG nhưng không giải mã được** (hỏng, bị cắt ngang) — trước đây ra
500, nay quyết ở `ImagePipeline` nên ảnh khảo sát cũng hưởng. `captured_at` phải là **ngày-giờ ISO 8601 đầy đủ**
(`2026-10-04T13:30:00Z`, có thể kèm offset); thiếu ngày hoặc thiếu giờ → `400 VALIDATION_FAILED`.

⚠️ `allowed_actions` của phiếu vẫn liệt kê `complete` khi chưa có ảnh `after`: FE nên tự khoá nút theo danh sách ảnh (hoặc
xử lý 409). Ảnh bằng chứng **không** vào dữ liệu chấm CV (xem "Chung cho cả hai" ở trên). Bảng `repair_evidence` không
có endpoint sửa/xoá: ảnh là bản ghi của điều đã thấy.

**Phải báo:** WP6 (FM-18: upload, nhãn theo loại phiếu, `client_op_id`, ảnh `after` trước khi báo hoàn thành), WP5 (màn
chi tiết phiếu: danh sách và xem ảnh để nghiệm thu). **Đã báo WP5 và WP6 ngày 04/10/2026** (Mỹ gửi; PR #87 đã merge,
`luxmap_dev` đã migrate). Chờ xác nhận ở FW kế tiếp.

## OPS-SCHEMA — lược đồ đích theo Phiếu v1.4 (01/10/2026)

| | |
|---|---|
| **Decision** | ERD đích và Q1–Q17 ở [`docs/database/erd.md`](database/erd.md). Codex khảo sát (`.ai/results/OPS-SCHEMA-p1.md`, 21 D-item), Claude review độc lập và chốt các mục hai bên cùng chắc; Mỹ quyết mục cả hai không chắc (FX-1) |
| **Decision maker** | Mỹ (FX-1) + Claude theo uỷ quyền (còn lại), 01/10/2026 · `SELF-SIGNED` — mục chạm API **ESCALATE ở FW kế tiếp** |
| **Date** | 01/10/2026 |
| **Scope** | Chỉ lược đồ đích và tài liệu. **Chưa có migration, chưa đổi Contract.** Mỗi bảng mới vào qua Phase 1 của ticket sở hữu |

Các mục chạm bề mặt API — chưa sửa Contract, phải đưa ra FW:

| Mã | Thay đổi | Chạm |
|---|---|---|
| **S-Q12 (FX-1)** | `fixture_type`, `lamp_watt`, `install_date` **nullable khi chưa xác minh**; enum `fixture_type` thêm giá trị cho đèn cao áp sodium (tên giá trị chốt ở ticket). 114 bóng thực địa về NULL. Chọn bởi Mỹ | Contract §1 enum, §5.1 / §5.3.1 hình dạng, FE hiển thị "chưa rõ" |
| **S-Q6** | `work_order.task_kind` thêm `survey`; tuyến của phiếu là danh sách có thứ tự (`work_order_segment`) | §5.5 (đóng FR-1 về hướng) |
| **S-Q13** | Bỏ `ExternalUnit`/SLA khỏi domain model | §3.3 còn nhắc — sửa khi Contract lên version |
| **S-Q10** | Tài nguyên mới `electrical_cabinet` (prefix chốt ở ticket) | Endpoint tài sản mới |
| **S-Q7** | Phiếu khảo sát neo một xã nhưng chứa được tuyến/cột của xã khác trong phạm vi người tạo; kết quả mang xã của cột | Phân quyền đọc kết quả khảo sát (BE-15) |

**Phải báo:** WP5 (FX-1 hiển thị NULL, task_kind survey, tủ điện), WP6 (survey, FX-1), WP4 (bảng khảo sát / detection / registry phiên bản). **Chưa báo.**

## BE-15 Phase 1 — chốt (02/10/2026)

| | |
|---|---|
| **Decision** | D-01…D-15 ở `.ai/results/BE-15-p1.md` §11. Trong đó **D-R28 = A** (proxy streaming, cap tạm 300 MiB/clip, MP4 H.264/H.265, giữ gốc theo D-R16); `lamp_dim` giữ `source_channel = cv` (D-R20 đề xuất); capability mới `ReadSurveys` / `SubmitSurveys` / `ReviewSurveys`; enum `processing_status` (O-4) |
| **Decision maker** | Mỹ chốt 02/10: backend chạy model, duyệt ffmpeg, backend làm ghép cột. Còn lại Claude theo uỷ quyền sau khi đối chiếu Codex · `SELF-SIGNED` |
| **Date** | 02/10/2026 |
| **Scope** | BE-15/16/17; Contract §2 (capability), §5.6 (`GET /sweeps`, O-4), endpoint upload video mới. **Chưa đổi Contract** — ESCALATE ở FW kế tiếp |

**Phải báo:** WP6 (định dạng file thô, sidecar frame, upload clip), WP4 (detector theo frame, CV-05 chuyển sang backend), WP5 (màn
duyệt phiên, coverage). **Đã báo một phần (02/10/2026):** WP5/WP6 nhận phần P2a (xem mục dưới). **WP4 chưa báo** phần frame;
màn duyệt phiên và baseline/phân loại đã báo cả ba bên ngày 04/10/2026 (mục P2c bên dưới).

**Sửa D-05 (02/10/2026, Mỹ chốt, SELF-SIGNED):** đèn CV thấy **ON** mà cột **chưa có baseline** → `fixture_status = normal`,
`luminance_history.baseline_ratio = null` (FE hiển thị "chưa đủ dữ liệu để đánh giá độ sáng"), loại khỏi precision/recall của
`dim` và báo riêng. Thay cho "→ `unknown`": `unknown` giữ đúng nghĩa §3.1 (không được quét tới). Không thêm field, không đổi
enum, nhưng **đổi ý nghĩa giá trị trả về** nên đưa FW. Chi tiết: `.ai/results/BE-15-p1.md` §11 "Sửa D-05".

### BE-15 P2a — bề mặt hiện thực để FW đối chiếu (02/10/2026)

Nền SELF-SIGNED ở mục trên giữ nguyên; chi tiết payload tạm tại
[`survey-ingest-p2a.md`](survey-ingest-p2a.md). Ticket P2a dùng **cột/trường `commune_id` hiện có** làm xã neo
cho `task_kind=survey`, không thêm `anchor_commune_id`; inspection/repair vẫn cấm client gửi commune.
Thêm `segment_ids` ở chi tiết phiếu; sáu operation ingest/read tại `/sweeps`, ba capability đã ghi ở Phase 1.
`processing_status` tách khỏi `status`; `coverage_pct=null`, `frame_count=0` cho tới P2b.
PUT clip/raw trả **200** cho lưu mới và retry; raw cap **10 MiB/file** là giới hạn vận hành tạm của P2a.
Chưa tạo profile/registry hay kiểm codec; cấu hình và mapping frame giữ nguyên file thô để P2b kiểm.
OpenAPI đã xuất lại (PR #69); **văn bản Contract chưa sửa**, sửa khi lên version. **Đã báo WP5 và WP6 ngày 02/10/2026**
(Mỹ gửi): WP5 — phiếu `survey`, `GET /sweeps`, nhãn Swagger; WP6 — luồng thu phiên, giới hạn upload, ns dạng chuỗi, nhãn Swagger,
và xin phiên mẫu thật từ thiết bị (D-06). Chờ phản hồi ở FW kế tiếp.

### BE-15 P2c — duyệt, công bố và baseline (04/10/2026)

**SELF-SIGNED, nền tạm tới FW**, theo task P2c/D-03/05/08/10/12/14 đã duyệt; không sửa Contract hay enum.
Bề mặt mới: `GET /sweeps/{id}/results` (`ReadSurveys`, run thành công mới nhất hoặc `run_id`, trang ≤200),
`POST /sweeps/{id}/review` (`ReviewSurveys`, accept/return, `client_op_id`, `run_id`, `expected_version`,
return bắt buộc note). GET sweep/list thêm `version`. Thumbnail `GET /frames/{frame_id}/thumbnail` hiện thực
đúng JPEG proxy của Contract, kiểm cả parent/assignee/tập xã trước mở object; stub generator đã bỏ,
OpenAPI đã xuất lại sau review. Payload/mã lỗi tại [`survey-ingest-p2a.md`](survey-ingest-p2a.md).

Công bố một transaction, mỗi xã một batch history/status/baseline kèm audit; mỗi fault mới một SaveChanges
và audit `cv` riêng. Sweep cũ hoặc bằng timestamp không đè status và không tạo fault hiện tại (vẫn history).
Gộp lượt theo chất lượng, ON/OFF mâu thuẫn → unknown; không tự đóng sự cố/hoàn thành phiếu.
Baseline tách nguồn/chiều, trung vị, tối thiểu 3 member (cấu hình `SurveyReview:BaselineMinimumMembers`),
mỗi run accepted tối đa một member/chiều; lookup loại mọi baseline chứa member của chính sweep hoặc
không trước thời điểm bắt đầu sweep. Profile/protocol tương thích đầy đủ còn chờ D-05/D-06 + BE-33/34;
worker hiện vẫn chỉ nhận simulated. Không dùng kết quả mô phỏng làm chứng cứ chất lượng thực địa.

**Severity tạm cần WP4/FW xác nhận:** lamp_out Medium, lamp_dim Low; near_sensitive_poi tăng một bậc,
trần High. Cấu hình `SurveyReview:LampOutSeverity` / `LampDimSeverity` (Low/Medium/High).
`priority_score` vẫn null, chờ CV-16; nguồn dim vẫn `cv` theo D-14. Không đặt đây là quy tắc photometric.
**Đã báo WP5, WP6 và WP4 ngày 04/10/2026** (Mỹ gửi; PR #80 và #81 đã merge, `luxmap_dev` đã migrate). Chờ xác nhận ở FW
kế tiếp. Nội dung từng bên:
- **WP5:** màn duyệt (`results`, `published_as`, dòng `not_observed`, trường nullable), body và mã lỗi của `review`,
  `version` trên sweep, thumbnail chạy thật (404/503), hệ quả bản đồ khi chấp nhận, mức nghiêm trọng tạm.
- **WP6:** `version` trên sweep (sinh lại DTO), vòng `awaiting_review → accepted | returned` kèm `note`, phiên đã nộp bất biến
  (khảo sát lại = phiên mới), 503 `STORAGE_OBJECT_MISSING` không retry, đi hết tuyến, baseline cần ≥ 3 lần quét mỗi chiều.
- **WP4:** định nghĩa baseline (≥ 3 member `normal`, theo chiều, theo bóng đang lắp, không tự chấm), ngưỡng `dim` 0,80 và hai
  ca `normal` + `baseline_ratio = null` loại khỏi P/R của `dim`, mức nghiêm trọng tạm, truy vết `origin_observation_id` /
  `detection_model_version`, hạn chế *đèn mờ từ đầu*, và nhắc JSON model (D-07).
Test PostGIS và apply/rollback migration: Claude đã chạy (xem `.ai/results/BE-15-p2c.md`).

**Review P2c vòng 2 (04/10):** R1 chỉ nhận normal làm member; R2 thêm fixture_id FK Restrict trên baseline,
member từ ngày lắp UTC của bóng đang dùng, lookup chỉ dùng baseline cùng bóng hiện tại; không có bóng/ngày
lắp thì không lọc thời gian. R3 adapter đọc object thiếu →503 `STORAGE_OBJECT_MISSING`. R4 results thêm
`published_as`, `is_representative` tính trên mọi lượt của cột trong run, độc lập trang. Không có schema viết
tay cho SurveyResultItem trong generator. Nền SELF-SIGNED giữ nguyên.

**Review P2c vòng 3 (04/10):** phiên công bố `unknown` (`reason_codes = ["not_observed"]`) cho **mọi cột dự kiến**
của run mà xe không đi qua — đúng nghĩa `unknown` của Contract mục 1; trước đó các cột này giữ trạng thái cũ trên bản
đồ. **Hệ quả cho WP5/WP6:** chấp nhận một phiên phủ nửa tuyến làm nửa còn lại thành `unknown`. `GET /sweeps/{id}/results`
liệt kê cả các cột này (đứng sau mọi quan sát); trong `SurveyResultItem`, `observation_id`, `pass_id`, `direction`,
`association_confidence` thành **nullable**. Người duyệt phải có xã của cả cột không quan sát. Trả lại không còn bị
chặn khi cột đã được sửa sau xử lý (`SURVEY_SCOPE_CHANGED` chỉ cho accept). OpenAPI đã xuất lại.

## BE-41 — kỹ sư báo sự cố tại chỗ `POST /faults` (04/10/2026)

| | |
|---|---|
| **Decision** | Hiện thực Contract §5.4 (`POST /faults`) kèm luồng ảnh EV-2. Lệch Contract ở các chỗ dưới đây. **SELF-SIGNED, nền tạm tới FW** |
| **Decision maker** | Mỹ chốt EV-2 (04/10/2026); Claude hiện thực. Chạm bề mặt API → **ESCALATE ở FW kế tiếp** |
| **Date** | 04/10/2026 |
| **Scope** | Contract §2 (capability mới), §5.4 (`POST /faults`), §5.5 (ảnh); FM-19 |

| Mã | Chỗ lệch | Hướng đã làm | Chạm API |
|---|---|---|---|
| **R-1** | Contract §2 không có capability cho việc báo sự cố | **Thêm `ReportFaults` = chỉ `field_engineer`** (CLAUDE.md: sự cố do engine sinh hoặc do Kỹ sư hiện trường báo tại chỗ). Quản lý không báo — họ duyệt | **Có** (ma trận §2) |
| **R-2** | Contract áp cứng `data_source = field` | Có `pole_id` → **lấy `data_source` của cột**; không có cột → `field`. Áp cứng `field` sẽ đếm báo cáo trên cột testbed thành dữ liệu thực địa — đúng lỗi luật tách `data_source` sinh ra để chặn | **Có** (ngữ nghĩa) |
| **R-3** | `photo_frame_id` | **Bỏ** (EV-2): gửi lên → `400 VALIDATION_FAILED`. Ảnh: `POST /faults/{fault_id}/photos` (multipart `file`, `captured_at`, `lat`, `lng`, `client_op_id?`; luôn `observation`; chỉ **người báo**, chỉ khi sự cố còn **mở** — ngoài ra `404 FAULT_NOT_FOUND` / `409 FAULT_NOT_OPEN`), xem: `GET /faults/{fault_id}/photos` (`ReadFaults`), ảnh qua `GET /evidence/{id}/thumbnail\|original` như BE-24 | **Có** |
| **R-4** | Báo cáo gửi từ hàng chờ offline tới muộn | **Thêm `detected_at` tuỳ chọn** (lúc nhìn thấy; mặc định giờ server; tương lai quá 5 phút → 400). Không có nó, giờ nhận bị coi là giờ phát hiện | **Có** (trường mới) |
| **R-5** | `client_op_id` trùng | Cùng người báo → **200** trả sự cố đã tạo (`DUPLICATE_OP` là replay, không phải lỗi); người khác dùng lại khoá → `409 IDEMPOTENCY_CONFLICT` | Không |
| **R-6** | Còn lại theo Contract | `fault_status = detected`, `source_channel = field_report`, `reported_by` = JWT, `severity` mặc định `medium`, chỉ `lamp_out`/`lamp_dim` (`400 FAULT_TYPE_NOT_REPORTABLE`), `note` ≥ 10 ký tự, có cột thì xã/tuyến lấy từ cột (gửi `commune_id` kèm → 400; cột ngoài phạm vi → `404 POLE_NOT_FOUND`), không cột thì bắt buộc `location` (`400 LOCATION_REQUIRED`) và xã lấy từ scope (nhiều xã → phải gửi `commune_id` trong scope). `fixture_id` phải là bóng đang dùng của cột. Response 201 = item như `GET /faults` + `client_op_id` | Không |

Bảng ảnh `repair_evidence` nay nhận **một trong hai cha**: phiếu (`work_order_id`) **hoặc** sự cố (`fault_id`, FK ghép cùng xã);
CHECK một cha và ảnh sự cố luôn `observation`. `EvidenceItem` thêm `fault_id`, `work_order_id` thành nullable. Migration
`AttachPhotosToFaults` — `Down()` từ chối khi đã có ảnh sự cố.

**Phải báo:** WP6 (FM-19: `POST /faults`, luồng một form → gửi sự cố rồi tải ảnh, `detected_at` cho hàng chờ offline,
`photo_frame_id` bỏ), WP5 (sự cố `field_report` có ảnh; `GET /faults/{id}/photos`). **Đã báo WP5 và WP6 ngày 05/10/2026**
(Mỹ gửi; PR #91 đã merge, `luxmap_dev` đã migrate). Chờ xác nhận ở FW kế tiếp.

## BE-20 — chi tiết cột `GET /map/poles/{pole_id}` (04/10/2026)

| | |
|---|---|
| **Decision** | Hiện thực màn chi tiết cột theo Contract §5.1 + `mock-pole-detail.json`, **lệch ở chín chỗ dưới đây** vì nền đã đổi (IoT ở tủ điện, đo sáng bằng lux, baseline theo chiều). **SELF-SIGNED, nền tạm tới FW**; không sửa văn bản Contract hay enum |
| **Decision maker** | Claude hiện thực theo yêu cầu của Mỹ (04/10/2026); chạm bề mặt API → **ESCALATE ở FW kế tiếp** |
| **Date** | 04/10/2026 |
| **Scope** | Contract §5.1 (`GET /poles/{pole_id}`, đã dời `/map/…` ở MAP-1); mock `mock-pole-detail.json`; schema OpenAPI `PoleMapDetail*` (tên tránh trùng `PoleDetail` của BE-12b) |

| Mã | Chỗ lệch | Hướng đã làm | Chạm API |
|---|---|---|---|
| **D-1** | `iot_node` theo cột (drift I-1, I-8) | **Luôn `null`**; khoá giữ. Thiết bị ở tủ điện, không gắn cột | Không (hình dạng giữ) |
| **D-2** | `runtime_history[]` theo cột (I-9: runtime đo theo mạch) | **Luôn `[]`**; khoá giữ. Mock `POLE-0047` vẫn có chuỗi 18 đêm — câu hỏi I-10 cho WP5 còn mở | Không |
| **D-3** | Contract có **một** `luminance_baseline`; P2c dựng baseline **theo chiều đi** | `luminance_baseline` = baseline **mới nhất của bóng đang dùng**; **thêm** `luminance_baselines[]` (mỗi chiều một phần tử, có `direction`). `baseline_value` là **trung vị đỉnh lux tương đối** (mock ghi 1.0); `baseline_window_nights` = số phiên đã duyệt tạo nên giá trị (`member_count`, một phiên ≈ một đêm); `computed_at` = lúc tạo. Baseline dựng trên bóng đã thay **không hiện** | **Có** (trường mới, nghĩa số đổi) |
| **D-4** | `out_threshold_ratio` (mặc định 0.15) | **`null`**: từ Phiếu v1.4 `out` do CV ON/OFF quyết, không phải tỉ lệ lux. `dim_threshold_ratio` lấy từ cấu hình khảo sát (0.80) — BE-33 sẽ quản trị | **Có** (nullable) |
| **D-5** | `luminance_history[]` | Tối đa **30** điểm đã công bố mới nhất, **cũ → mới**; gồm cả điểm `unknown` (cột không quan sát, `baseline_ratio` null). `normalized_luminance` = `baseline_ratio` (mock có baseline 1.0 nên hai số trùng); **thêm** `peak_lux`, `reason_codes[]`. `baseline_ratio` nullable khi chưa có baseline | **Có** (nullable + trường mới) |
| **D-6** | `current_status` / `fixture` luôn là object | Chưa có sweep nào công bố cho cột ⇒ `fixture_status = unknown`, `status_confidence`, `determined_at`, `source_channel` đều `null`; `source_channel` là `cv` khi có dòng trạng thái (chỉ sweep được duyệt mới ghi). `determined_at` = lúc **khảo sát** (không phải lúc duyệt). `fixture = null` khi cột không có bóng đang dùng | **Có** (nullable) |
| **D-7** | `open_faults[]` | Chỉ sự cố mở (`FaultStatusSets.Open`), nặng nhất trước (hạng severity, rồi `priority_score` giảm dần, chưa chấm xuống cuối), tối đa 50. `fault_type` là loại **hiệu lực** (`override ?? fault_type`) | Không |
| **D-8** | `recent_frames[]` | Tối đa **10** frame đại diện của các lượt đã công bố, mới nhất trước, **chỉ frame người gọi mở được** ở `/frames/{id}/thumbnail` (cùng quy tắc quyền, `SurveyMediaAccess`) — URL đã liệt kê thì không 404. **`distance_m` và `heading_deg` luôn `null`**: bảng frame không lưu | **Có** (nullable) |
| **D-9** | Phạm vi | Đọc cột trước: ngoài phạm vi xã ⇒ **404 `POLE_NOT_FOUND`**, giống cột không tồn tại (Contract §7). Không trả `data_source`, `external_ref`, `feeder_id` | Không |

**Phải báo:** WP5 (FW-12/FW-13: biểu đồ lịch sử, đường ngưỡng — `out_threshold_ratio` null nên chỉ vẽ đường dim; baseline theo chiều;
`iot_node`/`runtime_history` rỗng — màn biểu đồ runtime theo cột không còn nguồn), WP6 (FM-17: cùng hình dạng; frame chỉ khi mở được). **Đã báo WP5 và WP6 ngày 04/10/2026** (Mỹ gửi; PR #84 đã merge). Chờ xác nhận ở FW kế tiếp; câu hỏi I-10 (màn runtime theo cột) vẫn chờ WP5 trả lời.

## Dữ liệu thực địa đầu tiên — đèn thật không phải LED (01/10/2026)

| | |
|---|---|
| **Decision** | **Chỉ đăng ký, chưa quyết.** Nạp 114 cột khảo sát ảnh 28/09/2026 vào `luxmap_dev` (COM-001 Phường Long Phước, COM-002 Phường Long Bình, `data_source = field`), bóng đèn gán **giá trị tạm** vì enum không biểu diễn được đèn thật |
| **Decision maker** | Nạp dữ liệu tạm: Mỹ chốt 01/10/2026. FX-1 chạm enum (bề mặt API) → **ESCALATE ở FW kế tiếp** |
| **Date** | 01/10/2026 |
| **Scope** | Contract §1 (`fixture_type`, v1.6 thu hẹp còn `led_road_lamp`), `fixture` trong `luxmap_dev`; nguồn và giá trị tạm ghi ở `img_osm/NGUON.txt` (không vào git) |

| Mã | Chỗ lệch | Hệ quả | Hướng (chưa chốt) | Chạm API |
|---|---|---|---|---|
| **FX-1** | Ảnh thực địa cho thấy **đa số đèn ánh cam — cao áp sodium (HPS)**, một số LED. TP.HCM (trừ Thủ Đức) còn 103.374 bộ HPS năm 2024; Thủ Đức đặt mục tiêu LED toàn bộ đến **2030**. Enum `fixture_type` chỉ còn `led_road_lamp` | 114 bóng đang mang `led_road_lamp` **sai sự thật** cho phần lớn; `lamp_watt = 150` và `install_date = 2020-01-01` là **giá trị bịa để qua NOT NULL**, trông y như số thật trên mọi màn hình | (a) thêm lại giá trị cho đèn cao áp (`hps_road_lamp`?) — đảo một phần quyết định v1.6; (b) giữ enum, đánh dấu bóng "chưa xác minh" bằng cột riêng; (c) cho `lamp_watt` / `install_date` nullable khi chưa biết. Cần chốt **trước khi** FE hiển thị số liệu bóng của xã thật | **Có** (§1 enum, §5.1 `fixture_type` / `lamp_watt`) |

**Phải báo / đưa ra FW kế tiếp:** WP5 (màn tài sản sẽ hiện bóng LED 150 W ngày lắp 2020-01-01 cho cả 114 cột), WP4 (CV ON/OFF trên đèn HPS ánh cam khác LED). **Chưa báo.**

## MAP-1 — endpoint bản đồ dời xuống `/api/v1/map/` (02/10/2026)

| | |
|---|---|
| **Contract v1.7** | §5.1 `GET /poles`, `GET /poles/{pole_id}`; §5.2 `GET /segments`; §5.6 `GET /iot-nodes` |
| **Code** | `GET /map/poles`, `GET /map/poles/{pole_id}` (BE-20, chưa hiện thực), `GET /map/segments`, `GET /map/iot-nodes` |
| **Vì sao** | Gom bốn lớp bản đồ dưới một tiền tố, tách hẳn khỏi `/assets/poles` (kiểm kê, BE-12a); `/poles` trần dễ bị đọc như tài nguyên "cột" chung |
| **Query, response** | Không đổi: vẫn `bbox` bắt buộc, `FeatureCollection`, 413 `BBOX_TOO_LARGE`, cùng `properties` |
| **Ai bị ảnh hưởng** | Không client nào gọi đường cũ: đã soát 11 nhánh `luxmap-web` (02/10/2026) — web còn đọc mock; repo mobile chưa có code gọi API. Không giữ alias đường cũ |
| **Quyết định** | Mỹ yêu cầu, **SELF-SIGNED** — chạm bề mặt API nên chưa ổn định tới FW kế tiếp. Contract text chưa sửa; sửa khi lên version |

**Phải báo:** WP5 (FW-08 bản đồ), WP6 (FM-15). **Đã báo ngày 02/10/2026** (Mỹ gửi, PR #70 đã merge). Chờ xác nhận ở FW kế tiếp.

## `external_ref` — chốt (03/10/2026)

| | |
|---|---|
| **Quyết định** | `external_ref` là **mã vĩnh viễn do nhóm quản lý**: cột thực địa giữ `KS-<ngày>-<giờ chụp>`, tuyến giữ `osm-…`, bộ mock giữ mã mock. Không đổi tên, không ghi đè sau khi đã nạp |
| **Mã của xã** | Nếu xã giao mã kiểm kê (số sơn trên cột…) thì lưu ở cột **riêng** `inventory_code` (nullable, duy nhất trong xã), thêm khi có dữ liệu thật; **không** thay `external_ref`, **không** làm khoá import |
| **Ảnh hưởng FO** | File import cột/tuyến **giữ nguyên `external_ref` cũ** cho cột đã có; cột mới mới được mã mới. Khảo sát ảnh lại phải dùng lại mã cũ (công cụ sẽ được sửa) — nếu không import sẽ tạo cột trùng |
| **Ai ký** | Mỹ (BE1), đóng follow-up D-R10. Không chạm bề mặt API hiện có |


## BE-33a — tài khoản do Quản trị tạo, mời qua email (05/10/2026)

| | |
|---|---|
| **Decision** | Hiện thực D-R11: Quản trị hệ thống tạo tài khoản **không mật khẩu**, hệ thống **gửi email mời** chứa link đặt mật khẩu; gỡ `POST /auth/register`. Đã viết vào **Contract v1.8** (§4.8, §4.9, bảng lỗi, ma trận §2). **SELF-SIGNED, nền tạm tới FW** |
| **Decision maker** | Mỹ chọn hướng email (05/10/2026) và chốt D-1…D-14 (`.ai/results/BE-33a-p1.md`); Claude hiện thực. Chạm bề mặt API → **ESCALATE ở FW kế tiếp** |
| **Date** | 05/10/2026 |
| **Scope** | Contract §2, §4 (bảng lỗi, 4.1, 4.8, 4.9); FW (màn quản trị tài khoản, trang đặt mật khẩu); FM-05 (bỏ màn đăng ký) |

| Mã | Điểm | Hướng đã làm | Chạm API |
|---|---|---|---|
| **A-1** | `POST /auth/register` (DEPRECATED từ v1.7) | **Gỡ.** Gọi vào → `401` như route không tồn tại | **Có — BREAKING đã báo trước** |
| **A-2** | Cách giao mật khẩu cho người mới | **Email mời**, không mật khẩu tạm: link `{WEB_APP_BASE_URL}/set-password?token=…`, 72 giờ, dùng một lần; gửi lại thì link cũ hết hiệu lực | **Có** (endpoint mới) |
| **A-3** | Quên mật khẩu | `POST /auth/password/forgot` luôn `202` cùng body; tài khoản chưa đặt mật khẩu nhận **thư mời mới**, tài khoản khoá không nhận gì; giới hạn 5/15 phút/IP → `429 RATE_LIMITED` | **Có** |
| **A-4** | Đặt mật khẩu | `POST /auth/password/set` → `204`; **thu hồi mọi phiên cũ** (lý do mới `password_reset`) | **Có** |
| **A-5** | Khoá | Thu hồi mọi refresh token (lý do mới `account_locked`); không tự khoá, không khoá / hạ vai trò Quản trị cuối còn đăng nhập được | **Có** (ngữ nghĩa) |
| **A-6** | Gửi mail lỗi | Tài khoản vẫn tạo, `invitation_sent: false`; chưa có outbox / thử lại tự động tới BE-26 (Hangfire) | Không |
| **A-7** | Audit | Thao tác quản trị tài khoản **chỉ ghi log Serilog**, chưa vào `audit_event` (bảng đó bắt buộc `commune_id`). Follow-up cùng capability "system log" của Phiếu | Không |

**Lược đồ:** migration `AdminCreatedAccounts` — `app_user.password_hash` thành nullable, thêm `password_set_at`, CHECK
`ck_app_user_system_wide_scope_matches_role` (phạm vi `*` ⇔ `system_admin`) và `ck_app_user_password_set_together`;
bảng `account_token` (chỉ lưu băm). `luxmap_dev` phải chạy `scripts/cleanup_be12_leftover_accounts.sql` **trước** migration
(14 tài khoản test cũ vi phạm CHECK mới). Hạ tầng: MailKit (NuGet mới), Mailpit trong compose cho dev; biến `SMTP_*`,
`WEB_APP_BASE_URL` bắt buộc lúc khởi động.

**Phải báo:** WP5 (trang `/set-password` đọc `token` từ query rồi gọi `POST /auth/password/set`, gửi
`Referrer-Policy: no-referrer`; màn quản trị tài khoản theo §4.9; nút "quên mật khẩu"), WP6 (bỏ màn đăng ký; nút "quên
mật khẩu" gọi `/auth/password/forgot`, link mở trên trình duyệt). **Đã báo WP5 và WP6 ngày 05/10/2026** (Mỹ gửi; PR #93
đã merge, `luxmap_dev` đã migrate). Chờ xác nhận Contract v1.8 ở FW kế tiếp.

## POLE-NOTE — ghi chú của kỹ sư trên cột (05/10/2026)

| | |
|---|---|
| **Decision** | Thêm một ghi chú tự do cho **mỗi cột** (vị trí), để kỹ sư ghi nơi nhạy cảm gần đó hay lưu ý khác. Đã viết vào **Contract v1.9** (mục 2, 5.3, 5.3.1). **SELF-SIGNED, nền tạm tới FW** |
| **Decision maker** | Mỹ chốt 05/10/2026: Kỹ sư hiện trường + Quản lý ghi; một ghi chú, ghi đè; hiện ở kiểm kê, chi tiết cột trên bản đồ, danh sách cột của phiếu. Chạm bề mặt API → **ESCALATE ở FW kế tiếp** |
| **Scope** | Contract §2 (capability `EditPoleNotes`), §5.3 (endpoint), §5.3.1 (trường `note`); drift BE-20 (`GET /map/poles/{pole_id}` thêm khoá `note`) và WO-12 (`GET /work-orders/{id}/poles` thêm `note`) |

| Mã | Điểm | Hướng đã làm | Chạm API |
|---|---|---|---|
| **N-1** | Ai ghi | Capability mới **`EditPoleNotes`** = Quản lý + Kỹ sư hiện trường, chỉ cho ghi chú; mọi ghi tài sản khác vẫn `ManageAssets` | **Có** (ma trận §2) |
| **N-2** | Đường ghi | Ba đường: `PUT /assets/poles/{poleId}/note` (khoá `note` bắt buộc, `null`/rỗng = xoá, ≤ 1000 ký tự → `200 {pole_id, note}`); form tạo `POST /assets/poles` (`note?`); form sửa `PUT /assets/poles/{poleId}` (`note?`, **vắng = giữ nguyên** dù `PUT` là thay thế toàn phần; gửi lại cùng nội dung không đổi tác giả). ~~Import không đụng~~ → xem N-4 | **Có** (endpoint mới + trường mới ở 2 request) |
| **N-3** | Hình dạng đọc | ~~`note: {text, updated_at, updated_by, updated_by_name} \| null`~~ → **chuỗi \| null** từ N-4, giống nhau ở 3 nơi; **không** đưa vào lớp bản đồ bbox | **Có** (thêm khoá) |
| **N-4** | Người sửa cuối + import (06/10/2026, Mỹ chốt) | Ghi chú là **thuộc tính của tài sản** (đặc điểm khu vực nhạy cảm, lưu ý về cột), và người dùng chủ yếu sửa bằng **import**. Nên: (a) ghi chú **mất tác giả riêng** — `pole.note_updated_by` **đổi tên** thành `updated_by` (giữ giá trị), `note_updated_at` **bị xoá**; (b) **`updated_by`** trên `pole`, `road_segment`, `feeder`, `fixture` = người sửa **gần nhất** bằng form, endpoint ghi chú hay import, cạnh `updated_at`; chỉ đổi khi một giá trị **thật sự đổi** (nạp lại file y hệt hay lưu form không sửa → không đổi; kết quả import thêm **`unchanged`**); (c) cột **`note` tuỳ chọn** trong file `poles`: ô có chữ **ghi đè**, ô trống / thiếu cột **giữ**, import không bao giờ xoá; thay ghi chú **khác nội dung** → `warnings[]` (trường mới, kèm `total_warnings`) **trích nguyên văn ghi chú cũ**; (d) **BREAKING** — `note` đọc ra là **chuỗi \| null** ở 3 nơi (thay object N-3), response `PUT …/note` = `{pole_id, note, updated_at, updated_by, updated_by_name}`, kiểm kê thêm `updated_by` + `updated_by_name` cho cột / tuyến / tủ. Chỉ người gần nhất, **không** lịch sử (lịch sử lượt nạp = bảng `asset_import`, chưa làm). Migration `AssetUpdatedBy`. Contract **v1.12**. Cờ `near_sensitive_poi` vẫn phải bật — hệ thống không đọc chữ trong ghi chú | **Có** (BREAKING `note`; trường mới) |

**Lược đồ:** migration `AddPoleNote` — `pole.note`, `note_updated_by` (FK `app_user`, `Restrict`), `note_updated_at`;
CHECK `ck_pole_note_length` (≤ 1000) và `ck_pole_note_stamp_together`. Module Assets nay tham chiếu Identity (cùng khuôn Faults).

**Phải báo:** WP5 (màn kiểm kê, chi tiết cột: hiện và sửa ghi chú), WP6 (danh sách cột của phiếu: hiện ghi chú; màn sửa ghi chú
cho kỹ sư; đưa vào hàng chờ offline khi làm BE-43). **Chưa báo.** N-4: WP5 (`note` thành chuỗi ở kiểm kê và chi tiết cột; cột "người sửa cuối" từ `updated_by_name`; modal import hiện `unchanged` + `warnings[]`; template `poles.csv` có cột `note`), WP6 (`note` thành chuỗi ở cột của phiếu). **Chưa báo.**

## BE-27 — thông báo trong ứng dụng (05/10/2026)

| | |
|---|---|
| **Decision** | Bảng `notification` + 4 endpoint `/notifications` đọc bằng **polling**; 11 `type`. Đã viết vào **Contract v1.10** (mục 1.2, 2, 5.9). **SELF-SIGNED, nền tạm tới FW** |
| **Decision maker** | Mỹ chốt 05/10/2026: phạm vi (b) + 3 sự kiện phụ (đổi lịch, nghiệm thu, xử lý khảo sát lỗi); server soạn nội dung; D-3…D-8 theo đề xuất `.ai/results/BE-27-p1.md` (đã qua review Codex). Chạm bề mặt API → **ESCALATE ở FW kế tiếp** |
| **Task list** | BE-27 ghi *"chốt tên bảng/entity cùng FE2-Ngọc trước W16"* — tên `notification` và hình dạng item **chưa** được FE2 xác nhận |

| Mã | Điểm | Hướng đã làm | Chạm API |
|---|---|---|---|
| **N-1** | Kênh | Polling (`unread-count` mỗi 30–60 s). Push (Firebase) thêm sau, đọc cùng bảng, API giữ nguyên | **Có** (endpoint mới) |
| **N-2** | Nội dung | Server soạn `title`/`body` tiếng Việt, **chụp lúc xảy ra**; client hiển thị nguyên văn, dùng `type` + `entity_*` để điều hướng | **Có** |
| **N-3** | Người nhận | Không báo người gây ra; "Quản lý của xã" = `manager` đang hoạt động có **đủ mọi xã** của sự kiện; khảo sát trả về báo người **đang** giữ phiếu (+ người quay nếu khác) | Không (luật server) |
| **N-4** | Phạm vi | `notification` mang `commune_id` + `ICommuneScoped`; ai bị chuyển khỏi xã thôi thấy thông báo của xã đó (theo luật 60 phút của token) | **Có** (hành vi) |
| **N-5** | ID | Prefix mới `NTF`, tối thiểu 6 chữ số | **Có** (bảng prefix) |
| **N-6** | Mở sự cố | ~~Không có `GET /faults/{id}`~~ → **ĐÓNG 06/10/2026**: thêm `GET /faults/{fault_id}` (Contract v1.11 §5.4), cùng hình dạng item danh sách, ngoài phạm vi = 404 `FAULT_NOT_FOUND`. SELF-SIGNED, báo WP5/WP6 cùng tin BE-27 | **Có** (endpoint mới) |

**Hoãn (cần BE-26 Hangfire):** `work_order_overdue`, `node_offline`. Sự cố CV sinh lúc Quản lý chấp nhận khảo sát
**không** báo — người duy nhất cần biết chính là người vừa duyệt. Thêm `type` sau là **thêm giá trị enum**: Contract
5.9 đã yêu cầu client không lỗi khi gặp giá trị lạ.

**Phải báo:** WP5 (chuông + danh sách thông báo, polling), WP6 (FM-21: chuông, danh sách, polling khi app mở; push sau).
**Chưa báo.**

## BE-43 — đồng bộ offline `/sync/bundle` + `/sync/push` (06/10/2026)

| | |
|---|---|
| **Decision** | Hiện thực mục 5.8 theo 8 đề xuất D-1…D-8 ở `.ai/results/BE-43-p1.md`, đã viết vào **Contract v1.13** (mục 1.4, 2, 5.8; đóng O-5). **SELF-SIGNED, nền tạm tới FW** |
| **Decision maker** | Mỹ chốt 06/10/2026 "theo 8 đề xuất"; hai câu để ngỏ lấy mặc định: `SyncOffline` = **chỉ `field_engineer`**, thao tác bị bỏ qua **gộp vào `rejected`** (`BLOCKED_BY_EARLIER_OP`). Chạm bề mặt API → **ESCALATE ở FW kế tiếp** |
| **Task list** | BE-43 ghi phụ thuộc **BE-25** (chưa làm). D-2 làm luôn phần "việc của tôi" (gói không `segment_id` = tuyến của phiếu mở của người gọi) — phần gom theo địa lý / lịch trong ngày của BE-25 vẫn còn |

| Mã | Điểm | Hướng đã làm | Chạm API |
|---|---|---|---|
| **S-1** | `since` (D-1) | **Bỏ** — luôn bản chụp đầy đủ theo tuyến; `since` → 400. Delta cần tombstone + tính lại thuộc tính dẫn xuất từ 4 bảng; tuyến lớn nhất trên dev 46 cột | **Có** (bỏ tham số đề xuất) |
| **S-2** | Phạm vi gói (D-2) | `segment_id` lặp ≤ 20; vắng = tuyến của phiếu `assigned`/`in_progress` của người gọi | **Có** |
| **S-3** | Nội dung (D-3) | cột = properties bản đồ + `note`; phiếu dạng **chi tiết** (đề xuất cũ: item); thêm `segment_ids[]`; không lồng cột theo phiếu (WO-12) | **Có** |
| **S-4** | `op_type` (D-4) | 5 loại; ảnh/clip **không** qua push | **Có** |
| **S-5** | Khử trùng lặp (D-5) | `fault_report` / `lux_reading` dùng cột `client_op_id` sẵn có (**chung khoá** với endpoint gốc); 3 loại còn lại → bảng mới **`sync_operation`** (PK `(user_id, client_op_id)`, CASCADE từ `app_user`, chỉ ghi thao tác ĐÃ áp, cùng `SaveChanges` với thao tác) | Không (lược đồ) |
| **S-6** | Giờ thao tác (D-6) | `performed_at?` ở push **và** ở `POST /work-orders/{id}/start` (body tuỳ chọn mới) + `…/complete` | **Có** (trường mới) |
| **S-7** | Kết quả (D-7) | `applied[]{client_op_id, op_type, id, replayed}` / `conflicts[]{…, reason, message, server_state}` / `rejected[]{…, error}` — đề xuất v1.4 chỉ có hai nhóm; 404 tính là conflict; `IDEMPOTENCY_CONFLICT` là rejected | **Có** |
| **S-8** | Ghi chú xung đột (D-8) | `base_note` so theo **nội dung** (ghi chú không còn giờ riêng từ N-4) → `409 NOTE_CHANGED` | **Có** (trường + mã mới) |

**Lược đồ:** migration `AddSyncOperation` (chỉ `CreateTable`; apply → rollback → apply trên `luxmap_test`). Bảng nằm trong `SKIPPED`
của `copy_dev_to_supabase.py`.

**Review Codex (06/10/2026, một lượt + một lượt xác nhận):** hai phát hiện, đã sửa ở commit riêng — (1) `client_op_id` dùng lại giữa hai
*loại* thao tác không bị bắt → nay tra chéo ba nơi giữ khoá; (2) bắt đầu với `performed_at` sớm hơn giờ server rồi báo xong không
`performed_at` lưu được `completed_at < started_at` → nay lấy mốc trước. **Hạn chế đã biết, không sửa:** hai yêu cầu **đồng thời** dùng
chung khoá cho hai loại khác nhau vẫn có thể cùng được áp (lượt tra chéo không nguyên tử với lượt ghi). Mỗi loại vẫn chỉ áp một lần,
nên đây là lỗi client không được báo, không phải ghi đôi; đóng hẳn thì cần một bảng khoá chung cho cả `fault` / `lux_reading`.

**Phải báo:** WP6 (FM-20: toàn bộ mục 5.8; tải ảnh **trước** khi đẩy hàng chờ; `performed_at`; `base_note` lấy từ `poles[].note` của gói;
xử lý ba nhóm kết quả). **Chưa báo.**

## BE-25 — việc đêm nay của kỹ sư, gom theo tuyến (06/10/2026)

| | |
|---|---|
| **Decision** | Hiện thực theo 8 đề xuất A của `.ai/results/BE-25-p1.md` (D-1…D-8), đã viết vào **Contract v1.14** (mục 5.5). **SELF-SIGNED, nền tạm tới FW** |
| **Decision maker** | Mỹ chốt 06/10/2026 "theo bạn đề xuất". Chạm bề mặt API (endpoint mới) → **ESCALATE ở FW kế tiếp** |
| **Task list** | `tasks-backend.csv:28` (W11–W12): *"Mở app thấy đúng việc được gán, gom theo địa lý"* |

| Mã | Điểm | Hướng đã làm | Chạm API |
|---|---|---|---|
| **A-1** | Bề mặt (D-1) | Endpoint mới `GET /work-orders/agenda`, cấu trúc đã gom nhóm, không phân trang; listing giữ nguyên | **Có** (endpoint mới) |
| **A-2** | "Đêm nay" (D-2) | `night_of?`; vắng = giờ `Asia/Ho_Chi_Minh`, **trước 12:00 là đêm hôm trước**. Mốc 12:00 là **tạm** — cấu hình `WorkOrders:Agenda:NightStartsAt` / `TimeZone`, múi giờ sai thì dừng khởi động. FO chốt giờ ca | **Có** |
| **A-3** | Phiếu nào (D-3) | `assigned` + `in_progress` của người đó; `in_progress` luôn có, còn lại trừ phiếu lịch **sau** đêm này (chỉ đếm `upcoming_count`). Cờ `in_progress`, `scheduled_tonight` / `carried_over` / `unscheduled` (tối đa một), `overdue` | **Có** |
| **A-4** | Gom (D-4) | Theo tuyến; `near=lat,lng` → nhóm gần nhất trước, `distance_m` qua `SpatialFunctions.DistanceMeters` (EPSG:3405); không `near` → tuyến theo thứ tự tạo. Vị trí nhóm: điểm tuyến gần `near` nhất / điểm đầu tuyến — **gợi ý dẫn đường, không phải phép đo** (chiếu trên toạ độ đã lưu như WO-12) | **Có** |
| **A-5** | Phiếu nhiều tuyến (D-5) | Xuất hiện **một lần** dưới tuyến đầu (khảo sát: tuyến đầu; còn lại: `segment_id`, rồi tuyến của **cột** mang sự cố theo thứ tự ID — cùng thứ tự WO-12); kèm `segment_ids[]` đầy đủ. Không tuyến nào → nhóm `segment_id = null` theo xã, vị trí của sự cố | **Có** |
| **A-6** | Ai xem (D-6) | `ReadWorkOrders`. Kỹ sư: của mình (`me` / vắng / chính mình); tên người khác → `404 USER_NOT_FOUND`. Vai trò khác: `assigned_to` **bắt buộc**, phải là kỹ sư hiện trường có ≥ 1 xã trong phạm vi người gọi, nếu không `404 USER_NOT_FOUND` | **Có** (dùng thêm mã có sẵn) |
| **A-7** | Chiều đi tuyến khảo sát (D-7) | **Tách ticket riêng** (migration `work_order_segment.direction` + đổi `POST /work-orders`) — trùng P2 của file đề xuất sửa Phiếu và D-FS-01; chốt cùng lúc | Không (ở ticket này) |
| **A-8** | Offline (D-8) | **Không** thêm vào `/sync/bundle`; app gom từ gói theo đúng luật A-3…A-5 | Không |

**Tuyến của xã ngoài phạm vi** (đường liên xã): nhóm giữ `segment_id` (đã có trên sự cố) nhưng `segment_name = null`, `commune_id` và
vị trí lấy từ phiếu — không lộ tên hay hình học tuyến của xã khác (cùng nguyên tắc WO-12).

**Code:** `WorkOrderItem` / `WorkOrderDetail` đổi từ `class` sang `record` để mục agenda mở rộng item bằng copy constructor (khuôn
`SyncPoleProperties` của BE-43) — hình dạng JSON không đổi. Phần dựng item của listing tách thành `WorkOrderService.ItemsAsync`, dùng
chung với agenda. Không migration.

**Review (06/10/2026, Claude tự review sau khi hiện thực):** `night_of=06/10/2026` được trả **200** với `night_of = 2026-06-10` — binder
của framework đọc ngày theo văn hoá bất biến (kiểu Mỹ tháng/ngày). **Cùng lỗi có từ trước** ở `scheduled_from` / `scheduled_to` (BE-23,
đã thử: lọc `06/10/2026` trả phiếu ngày 10/06) và `from` / `to` của lux (BE-42). Sửa chung: `IsoDateQueryModelBinderProvider` chỉ kiểm
**hình dạng** ISO rồi giao lại binder cũ (giữ nguyên nghĩa `DateTime` không `Z` = UTC); sai → `400 VALIDATION_FAILED`. Ghi vào Contract mục 0
(v1.14). Ba điểm còn lại ghi thành câu trong Contract mục 5.5: Quản lý phạm vi một phần thấy agenda thiếu mà không có dấu hiệu;
`distance_m` tới tuyến chứ không tới chỗ cần làm; vị trí phiếu `null` khi tuyến thuộc xã ngoài phạm vi.

**Phải báo:** WP6 (FM màn mở app: endpoint + luật gom để gom offline giống server; `night_of` mặc định theo mốc 12:00 tạm; **mọi** ngày trên
query string phải là ISO), WP5 (lịch của Quản lý: `assigned_to` bắt buộc; lọc `scheduled_from/to` phải gửi `YYYY-MM-DD`). **Chưa báo.**

## BE-15 D-06 — ánh xạ video trên máy thật, dung sai một tick (06/10/2026)

| | |
|---|---|
| **Decision** | Worker nhận PTS đầu/cuối khai lệch PTS thực trong file **tối đa một tick** time base (so số nguyên chính xác `\|lệch\| × den ≤ 10⁹ × num`), thay vì bằng tuyệt đối. Kiểm nhất quán NỘI BỘ của số khai (`Δpts == Δsensor`, `VideoClockMapping.Parse`) **giữ nguyên chặt** |
| **Decision maker** | Mỹ chốt 06/10/2026 ("ok" sau khi Claude báo lỗi lúc soi mẫu WP6). Không chạm bề mặt API — chỉ nới một kiểm tra nội bộ |
| **Lý do** | Mẫu thật đầu tiên của WP6 (`Mobile_Report`, SM-A075F, 02/10): điện thoại ghi PTS encoder theo µs, MP4 lưu tick 1/90000 s ⇒ PTS cuối khai 29 908 732 000 ns, trong file 29 908 733 333 ns. Bằng tuyệt đối thì **mọi** phiên thật hỏng `CLOCK_VIDEO_MAPPING` dù mobile làm đúng |

**Bằng chứng trên clip thật** (chạy một lần bằng test tạm, không đưa file 30 MB vào repo): 9 frame trích trong cửa sổ 14–16 s, ảnh
1080×1920 (cờ xoay −90° được áp), thời điểm điện thoại của từng frame so với `sensor_timestamp_ns` của sidecar lệch −4 556…+4 111 ns.
Ánh xạ affine độ dốc 1 đúng trên máy thật ⇒ **phần ánh xạ của D-06 đã chứng minh**. Cổng `VIDEO_DEVICE_MAPPING_PENDING` **vẫn giữ**: còn D-07
(model thật của WP4) và package mobile theo schema v1.

**Phải báo:** WP6 — `*_pts_ns` khai = `(video_pts_us − video_pts_us frame đầu) × 1000`, lệch trong một tick là hợp lệ; `time_base` khai phải đúng
time base của file (`1/90000`). **Chưa báo** (gộp vào tin phản hồi package đã soạn).

## Quyết định khảo sát 07/10/2026 — sau khi soi mẫu WP6 và mô phỏng khả thi

| | |
|---|---|
| **Decision maker** | Mỹ (Dylan), 07/10/2026, theo đề xuất của Claude. Bằng chứng: `.ai/results/feasibility/` (Pha 1–2, S1/S2), mẫu `Mobile_Report` |
| **Chạm API** | Không — trừ D-R22 (câu chữ Contract, không đổi hình dạng) |

| Mã | Quyết định | Việc kéo theo |
|---|---|---|
| **D-FS-20** | Firmware module lux: **H-Resolution Mode 2, ~8 mẫu/giây, `module_ms` = `millis()` lúc đo, `seq` từng mẫu** (hướng a). Firmware cũ 1 Hz / `module_ms` theo giây: log thật bị `FitClocks` từ chối `CLOCK_QUALITY`, mô phỏng S1 cho **0 đỉnh** ở mọi kịch bản | **Đã báo nhóm IoT (Đạt) 07/10.** `docs/field/BE-15-pilot-drive.md` v3 |
| **D-R22** | `out_threshold_ratio` **giữ trường, luôn `null`**; Contract ghi "không còn dùng để phân loại — Out do CV quyết" (hướng b) | Sửa câu Contract §5.2 ở lần lên version kế tiếp |
| **D-R23** | Ground truth `dim`: **B trước** (tấm lọc ND độ truyền 0,9 / 0,8 / 0,7 trên cột đã có baseline), **A sau** (lux kế tay cùng đêm). B chỉ chứng minh độ nhạy cảm biến + thuật toán, không chứng minh bóng mờ thật — báo cáo phải nói thẳng | **BE phải làm TRƯỚC buổi quay B:** trường khai tấm lọc trong `capture_config` (ví dụ `optical_filter_transmittance`), loại lượt có tấm lọc khỏi baseline, xuất tỉ lệ kỳ vọng. A về sau cần nối `nearest_luminance` (drift 17). A không làm bù được cho đêm đã qua |
| **D-7 / P2 / D-FS-01** | **Không** thêm trường chiều đi khi giao tuyến. Quy trình: **mỗi đêm đi–về trọn tuyến trong một phiên**, camera cố định một bên; ngoại lệ đi một chiều thì chọn chiều để cột cùng phía camera. Lý do: `camera_side` khai cho cả phiên, cột khác phía camera ra `unknown` (`DetectionAssociation.cs:17`); 11/27 tuyến thực địa có cột hai bên | `docs/field/BE-15-pilot-drive.md` v3 (quy trình + lượt 14 đi–về). Câu Phiếu P2 gợi ý: *"the actual travel direction of each pass is recorded; each night the route is surveyed in both directions so that fixtures on both sides of the road are captured"*. Gợi ý chiều tự tính (phía đa số cột) chỉ làm khi có tuyến buộc đi một chiều |
| **D-05** | Baseline **N = 3, trung vị**. Mô phỏng: N 3 → 5 chỉ giảm báo nhầm "mờ" 3,9% → 3,3% (sai số mỗi lượt 10%); độ lặp lại của phép đo mới là yếu tố quyết định | Câu Phiếu gợi ý: *"at least three approved surveys on different nights per pole (pilot default, configurable), so Dim is evaluated from the fourth night"*. ⚠️ **Code hiện đếm theo PHIÊN, không theo ĐÊM** (`SurveyPublicationRules.Members` → `GroupBy(RunId)`): ba phiên trong một đêm đủ baseline. **Duyệt 07/10 (Mỹ):** mỗi đêm tối đa một thành viên (mỗi cột / chiều), baseline chỉ từ đêm trước — hiện thực ở ticket BE-15 follow-up |
| **BE-26** | Hàng đợi job bằng **DB** (khuôn lease của BE-15, D-09), **không** thêm Hangfire | Phase 1 của BE-26 |

**File đề xuất sửa Phiếu** (`docs/registration/FA26SE222_v1.4-change-proposals.md`, untracked) — **ba câu lệch code (D-FS-19) đã sửa
07/10**: P2 không còn nói BE loại lượt sai chiều (BE chỉ so cùng chiều); P5 ghi đúng cấu hình `SurveyReview:BaselineMinimumMembers` và đếm
theo đêm; P6 ghi đúng là BE **không** có sửa ghép cột nào — duyệt chỉ `accept` / `return`.

**Phải báo:** WP6 + FO (quy trình đi–về, `camera_side`, lượt 14), WP4 (D-R23 B→A, N = 3 trung vị). **Chưa báo.**

## BE-28 — thống kê cho dashboard (07/10/2026)

| | |
|---|---|
| **Decision maker** | Mỹ (Dylan), 07/10/2026 — duyệt D-1…D-14 theo đề xuất ở `.ai/results/BE-28-p1.md` |
| **Chạm API** | **Có** — nhóm endpoint mới, Contract chưa có đặc tả. **SELF-SIGNED**, nền tạm tới FW kế tiếp; chưa lên Contract |
| **Consumer** | WP5 — **chưa có màn dashboard nào** (khảo sát `luxmap-web` 07/10), phải dựng màn mới. **Chưa báo** |

| Mã | Quyết định |
|---|---|
| ST-1 | `GET /api/v1/statistics/fixture-status` — đếm cột theo `fixture_status`, mỗi dòng một nhóm. **`data_source` luôn là một chiều nhóm**, không có tổng liên nguồn. Mặc định **loại `calibration_rig`** như bản đồ (§1.6); hỏi đích danh mới có |
| ST-2 | Cột **chưa có dòng `pole_current_status`** đếm vào `unknown` (khớp bản đồ, `MapQueryService`), **và** báo riêng `never_surveyed` — tập con của `unknown`, không phải bucket thứ năm. `normal + dim + out + unknown = pole_count` |
| ST-3 | Mẫu số = mọi cột trong phạm vi, không theo bóng (trạng thái thuộc vị trí cột, BE-09). Chỉ ảnh chụp hiện tại; xu hướng theo thời gian là ticket riêng |
| ST-4 | `GET /api/v1/statistics/repair-timeliness` — chỉ phiếu **`repair`**. "Xong" = `completed_at` (lần `complete` cuối; `return` xoá nó), trạng thái `done` hoặc `verified`. Không đợi `verify` |
| ST-5 | Đúng hạn ⇔ **đêm** của `completed_at` ≤ `due_date` (`WorkOrderAgendaOptions.NightOf`, 12:00 Asia/Ho_Chi_Minh). `from`/`to` cũng là đêm, phải trong 2000-01-01…2099-12-31 (ngoài dải → 400). Mặc định 30 đêm tới đêm nay. `due_date` hiện tại — không sửa được sau khi phiếu xong (`WorkOrderRules.Allows("edit")`) |
| ST-6 | Phiếu không hạn → `no_due_date`, ngoài mẫu số. `on_time_rate = on_time / (on_time + late)`, **tính ở backend**, `null` khi mẫu số 0. `open_overdue` = phiếu `repair` chưa xong có `due_date` < đêm nay |
| ST-7 | Đếm theo **phiếu**, không theo `case_id` (FR-2). **Không tách theo `data_source`** (Mỹ ký): phiếu không mang trường này, thời gian sửa là số vận hành chứ không phải số đo |
| ST-8 | Capability mới **`ReadStatistics`** = `superior`, `manager`, `system_admin`. Không `field_engineer`: `work_order` lọc theo người được giao nên số của họ sẽ khác số thật |
| ST-9 | `group_by`: `commune`, `segment` (fixture-status); `commune` (repair-timeliness). Khoá dòng **luôn có mặt**, `null` khi không nhóm theo chiều đó. Chỉ trả nhóm có dữ liệu. BE-30 thêm giá trị `group_by` mà không đổi hình dạng |
| ST-10 | Xuất CSV để **BE-31**, chung cho mọi báo cáo |

## CABINET — trụ / tủ điện tổng thành tài sản riêng (07/10/2026)

| | |
|---|---|
| **Decision** | Trụ / tủ điện tổng là **tài sản** (`electrical_cabinet`, có toạ độ điểm, luôn tồn tại); IoT node là **thiết bị gắn lên trụ**, lắp theo yêu cầu của địa phương nên không trụ nào bắt buộc có. Cùng quan hệ `Pole` / `Fixture` (BE-09). Hiện thực **Q10** của `docs/database/erd.md`, sửa ba điểm của Q10 |
| **Decision maker** | **Mỹ (Dylan)**, 07/10/2026 — đồng ý D-1…D-5 trên đề xuất của Claude; Codex review, cả 5 phát hiện đã áp. D-6 do review thêm, **Mỹ chưa duyệt riêng** |
| **Nguồn** | FE hỏi vì sao `GET /assets/feeders` trả `has_geometry` thay vì toạ độ — `feeder.geom` là LineString **tuyến cáp**, toạ độ tủ chỉ có ở `iot_node.geom` nên trụ không IoT không có chỗ lưu |
| **Chạm API** | **Có** — tài nguyên mới + trường mới trên feeder. **SELF-SIGNED**, nền tạm tới FW kế tiếp; chưa lên Contract, **chưa có code** |
| **Chi tiết** | `.ai/tasks/CABINET.md` (lược đồ, migration, kéo theo) · review `.ai/reviews/CABINET-by-codex.md` |

| Mã | Quyết định | Chạm API |
|---|---|---|
| **CAB-1** | Bảng `electrical_cabinet`: `cabinet_id` prefix **`CAB`**, độ rộng tối thiểu 3 (`CAB-001`), `cabinet_name`, `commune_id`, `external_ref` (partial unique theo xã), **`geom` Point 4326 bắt buộc**, `data_source`. `external_ref` dạng `CAB-HVC-A` không xung đột với ID — không parse nó như ID | Có (§0.2 prefix mới) |
| **CAB-2** | `feeder.cabinet_id` nullable (trụ nào cấp mạch này). `feeder.geom` (LineString) **giữ**, không bỏ | Có |
| **CAB-3** | `iot_node.cabinet_id` **bắt buộc**, tối đa **một** thiết bị / trụ (UNIQUE đầy đủ — node chưa có trạng thái ngừng dùng). **`iot_node.geom` bỏ**, toạ độ thiết bị = toạ độ trụ. `GET /map/iot-nodes` **giữ nguyên hình dạng** | Không (hình dạng giữ) |
| **CAB-4** | Rơ-le chỉ điều khiển feeder **cùng trụ** với thiết bị. DB (FK ghép qua `feeder_control.cabinet_id NOT NULL`) + service | Không |
| **CAB-5** | ~~Thiết bị **không** gắn lên trụ `data_source = field` (D-R10). DB (FK ghép tới `(cabinet_id, data_source)` + CHECK) + service~~ **GỠ 07/10/2026 (Mỹ): không cần luật này.** Migration `DropFieldCabinetRule` bỏ ba ràng buộc + cột `iot_node.cabinet_data_source`; trụ mang thiết bị đổi sang `field` được. Thiết bị tự nó vẫn không bao giờ `field` (`ck_iot_node_data_source_not_field`) | Không |
| **CAB-6** | `GET /assets/feeders` (list + detail) thêm `cabinet: {cabinet_id, cabinet_name, location{lat,lng}} \| null` — tiền lệ `active_fixture`. `PUT /assets/feeders/{id}`: `cabinet_id` **vắng = giữ**, `null` = tháo — ngoại lệ thứ hai của thay thế toàn phần, khuôn `note` | Có (§5.3.1) |
| **CAB-7** | Nhóm `/assets/cabinets` (CRUD + `POST /assets/import/cabinets`, nạp trụ **trước** feeder, feeder tham chiếu `cabinet_external_ref`); `GET /map/cabinets` (bbox bắt buộc, trụ không IoT cũng hiện) | Có (endpoint mới) |
| **CAB-8** | Hình dạng đọc (Mỹ, 07/10/2026). `GET /assets/cabinets` item: `cabinet_id, external_ref, cabinet_name, commune_id, data_source, location{lat,lng}, feeder_ids[], iot_node_id\|null, updated_at, updated_by, updated_by_name`; chi tiết `{cabinet, geom_wkt, created_at}`; `/map/cabinets` properties `cabinet_id, cabinet_name, commune_id, feeder_ids[], iot_node_id\|null`. `iot_node_id` **trần** — trạng thái thiết bị chỉ ở `/map/iot-nodes` (không hai nguồn sự thật). `feeder_ids[]` tính lúc đọc, **trong phạm vi xã của người gọi**, `[]` khi rỗng | Có (§5.3.1, endpoint mới) |

**Hiện thực 07/10/2026** (nhánh `feat/CABINET-electrical-cabinet`, migration `AddElectricalCabinet`; chi tiết + bằng chứng ở
`.ai/results/CABINET-p2.md`). Chốt thêm lúc hiện thực — đều chạm API, cùng nền SELF-SIGNED:

| Mã | Quyết định | Chạm API |
|---|---|---|
| **CAB-9** | Lỗi: feeder đang được thiết bị điều khiển mà đổi / tháo trụ → **409 `ASSET_IN_USE`**, `details` gồm `iot_node_id` + `constraint` (vế "trụ mang thiết bị đổi sang `field`" bỏ cùng CAB-5). Trụ của mạch khác xã với mạch → **409 `CROSS_COMMUNE_REFERENCE`** (`feeder_commune_id`, `cabinet_commune_id`). Không thêm mã lỗi mới | Có (mã lỗi, `details`) |
| **CAB-10** | `POST /assets/feeders` nhận `cabinet_id?` (404 khi trụ không thấy được, 409 khi khác xã). Import: `kind = cabinets`, thứ tự nạp **segments → cabinets → feeders → poles → fixtures**; cột `cabinet_external_ref` tuỳ chọn ở `feeders.csv` | Có |
| **CAB-11** | `GET /map/cabinets` theo đúng luật các lớp bản đồ khác: `bbox` bắt buộc, `commune_id` ngoài phạm vi → 403, `data_source` mặc định **loại `calibration_rig`** (§1.6) | Có |
| **CAB-12** | Migration sinh **một trụ cho mỗi thiết bị đang có** tại đúng toạ độ cũ (`cabinet_name = 'Tủ NODE-00n'`, cùng `data_source`), gắn mạch đang được nó điều khiển vào trụ đó. Bộ mock: `CAB-001…003` ↔ `NODE-001…003` (`external_ref` `DEMO-CAB-00n`). `GET /map/iot-nodes` trả **y hệt** trước | Không (hình dạng giữ) |

**Phải báo:** WP5 (trả lời câu hỏi `has_geometry`: toạ độ tủ đến qua `cabinet.location`; màn quản lý trụ mới; lớp bản đồ trụ;
`PUT` feeder vắng `cabinet_id` = giữ), IOT-09 / Đạt (testbed: tạo trụ trước, thiết bị gắn vào trụ, rơ-le mang `cabinet_id`).
**Chưa báo.**

## TOPO-INFER — nhãn nguồn gốc cho topology + sơ đồ nhánh của trụ (07/10/2026)

| | |
|---|---|
| **Decision** | Topology ngoài thực địa được **suy luận công khai, có gắn nhãn** — không giấu, không đoán ngầm. Mỗi quan hệ cột → mạch và mạch → trụ mang `verified \| inferred`; bản đồ vẽ sơ đồ nhánh từ dữ liệu backend; nhãn hiện ở tooltip (TI-7). Nền: phương án A của FEEDER-SCOPE |
| **Decision maker** | **Mỹ (Dylan)**, 07/10/2026 — đồng ý D-1…D-10 của `.ai/results/TOPO-INFER-p1.md` (đã qua review Codex, 8/8 phát hiện áp) |
| **Chạm API** | **Có.** SELF-SIGNED, nền tạm tới FW; chưa lên Contract |

| Mã | Quyết định | Chạm API |
|---|---|---|
| **TI-1** | Enum mới **`topology_source : verified \| inferred`**. `pole.feeder_source` (cặp với `feeder_id`) và `feeder.cabinet_source` (cặp với `cabinet_id`); CHECK: nhãn null ⇔ quan hệ null. Backfill mọi quan hệ đang có → `inferred` | Có (enum mới) |
| **TI-2** | Luật ghi: nhãn vắng + quan hệ không đổi → **giữ**; quan hệ đổi / gắn mới → **`inferred`**; tháo quan hệ → xoá nhãn (gửi kèm `null` cũng được); `null` **khi quan hệ còn** hay sai kiểu → **400**; nhãn có giá trị kèm quan hệ null → **400**. Không đường nào tự nhận `verified`. Ghi **theo cặp** (cả hai cột trong cùng UPDATE) | Có |
| **TI-3** | `feeder_source` trên `POST/PUT /assets/poles`, `PUT /assets/poles/{id}/feeder`, import cột; `cabinet_source` trên `POST/PUT /assets/feeders`, import feeder. Đọc: `PoleListItem`, `TopologyPole` (+`feeder_source`), `FeederListItem.cabinet` (+`cabinet_source`). **Không** thêm vào `/map/poles` | Có |
| **TI-4** | `GET /api/v1/map/cabinets/{cabinetId}/topology` — `FeatureCollection` của `LineString`, **mỗi feature một cạnh**: `feeder_id, segment_id, branch, order, from_id, to_pole_id, feeder_source`. Nhánh theo (mạch, tuyến) và hai phía trụ, thứ tự dọc đường kiểu WO-12; cạnh đầu lấy nhãn thấp hơn của hai quan hệ. Không phát khoảng cách | Có (endpoint mới) |
| **TI-5** | Quản lý (`ManageAssets`) đặt nhãn; không lưu "ai xác minh, lúc nào" (`updated_by` không phải dấu xác minh) | Không |
| **TI-6** | Chặn trộn cột `field` với trụ không-`field` (D-7) và script đề xuất quan hệ suy luận (D-8): **ticket riêng** | — |
| **TI-7** | **Hiển thị (Mỹ, 08/10/2026):** mọi cạnh vẽ **cùng một kiểu nét** — không phân biệt nét đứt / nét liền. Nhãn `feeder_source` **giữ trong dữ liệu** và FE hiện ở **tooltip** của cạnh (*"Quan hệ suy luận từ khảo sát, chưa xác minh"* / *"Đã xác minh"*), kèm dòng chú giải *"Sơ đồ logic, không phải tuyến cáp"*. Backend không đổi | Không (chỉ cách FE trình bày) |

**Phải báo:** WP5 (vẽ từ `/map/cabinets/{id}/topology`, bỏ chia đều trong `useElectricalCascade`; nhãn trên form cột / mạch). **Chưa báo.**
