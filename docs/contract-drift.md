# Chỗ lệch giữa code và Contract — log mới từ v1.4 (18/09/2026)

**Log cũ (v1.0 → v1.3, drift 1–43, quyết định A–F)** đã **archive** tại
[`docs/archive/contract-drift-v1.md`](archive/contract-drift-v1.md). Mọi mục trong đó đã được gộp vào
Contract v1.4 (xem changelog mục 10 của [`api-contract-v1.1.md`](api-contract-v1.1.md)) hoặc chuyển
thành Open item ở mục 9 của Contract. **Không sửa file archive.**

> 🗄️ **Mục đăng ký TRƯỚC 25/09/2026 là lịch sử (D-R18)** — chúng dùng tên vai trò cũ và nói "Quản trị"
> ghi tài sản, "Nhánh C". Không sửa lại; vai trò hiện hành ở mục *Registration v1.2* bên dưới và
> Contract v1.7 §2.

**Nguyên tắc vận hành quyết định (FW-00, 07/09/2026)** giữ nguyên hiệu lực — năm mục ở đầu file
archive: (1) ghi ngay, bốn trường Decision / Decision maker / Date / Scope; (2) policy là một vai trò
chính xác; (3) im lặng > 3 ngày làm việc = APPROVE nếu không chạm API, ESCALATE nếu chạm, fallback
signer Dylan → `SELF-SIGNED`; (4) không "bàn thêm" vô thời hạn — phải có owner + deadline; (5) ghi ở
đúng tầng (deviation → file này; luật liên ticket → Contract; ràng buộc kỹ thuật → `CLAUDE.md`; tiến độ
→ `tracking.html`).

---

## Quyết định đã đăng ký

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
| **D-R11** | Mô hình cuối: **chỉ Quản trị hệ thống tạo account**, không tự đăng ký. PR này: `LowestRole → field_engineer`, `POST /auth/register` **DEPRECATED** (Contract §4.1, README, `deprecated: true` trong spec) — **breaking đã báo trước**. **BE-33a** (ngay sau): `POST /api/v1/admin/users` (`ManageUsers`), gỡ `/auth/register` + `RegistrationTests`, `AnonymousEndpointTests` 7→6, báo WP6 bỏ màn đăng ký; Phase 1 của nó mở 3 D-item: mật khẩu tạm + đổi ở lần đầu, bắt buộc ≥1 xã cho 3 vai trò có phạm vi, khoá/mở khoá qua `is_locked` | **Có — breaking báo trước** |
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


> Ghi theo khuôn: Contract đang ghi gì · Code đang làm gì · Đề xuất · Ảnh hưởng. Chạm bề mặt API thì
> theo nguyên tắc 3 (ESCALATE khi im lặng), không tự coi là approve.
