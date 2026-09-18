# Chỗ lệch giữa code và Contract — log mới từ v1.4 (18/09/2026)

**Log cũ (v1.0 → v1.3, drift 1–43, quyết định A–F)** đã **archive** tại
[`docs/archive/contract-drift-v1.md`](archive/contract-drift-v1.md). Mọi mục trong đó đã được gộp vào
Contract v1.4 (xem changelog mục 10 của [`api-contract-v1.1.md`](api-contract-v1.1.md)) hoặc chuyển
thành Open item ở mục 9 của Contract. **Không sửa file archive.**

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
| **Scope** | Contract v1.4 (file `api-contract-v1.1.md`), `docs/openapi/luxmap-v1.4.json`, log này, `CLAUDE.md`, code trên nhánh `docs/BE-REVIEW-02` |

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
- **BE1:** O-7 FK ghép trước BE-13; BE-36 sớm (M-1); rate limit auth (M-5); CI lint spec (M-7).

---

## Chỗ lệch mới (đăng ký sau 18/09/2026)

| # | Chỗ lệch | Mức | Ai bị ảnh hưởng | Trạng thái |
|---|---|---|---|---|
| — | *(chưa có)* | | | |

> Ghi theo khuôn: Contract đang ghi gì · Code đang làm gì · Đề xuất · Ảnh hưởng. Chạm bề mặt API thì
> theo nguyên tắc 3 (ESCALATE khi im lặng), không tự coi là approve.
