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
| 46 | Không có endpoint topology nào trong Contract; BE-13 cần một cái | Cao | WP4 (CV-05, CV-15) | **Chờ duyệt** — đề xuất ở `review/BE-13-topology-shape.md` |

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

**Code đang làm gì.** Cũng không gì cả — **chưa hiện thực dòng nào**, cố ý. Tiêu chí nghiệm thu của
BE-13 (`tasks-backend.csv` dòng 16) là *"Truy vấn được 'tất cả cột trên feeder X' phục vụ
clustering"*, mà truy vấn đó không có chỗ nào để gọi.

**Phần GÁN của BE-13 thì đã xong** từ BE-12a/BE-12: `PUT /assets/poles/{id}/feeder` gán mạch,
`PUT /assets/poles/{id}` gán tuyến, import gán theo lô. Phần thiếu đúng là phần **đọc**.

**Đề xuất.** Ba endpoint, chi tiết và lý lẽ ở
📄 [`docs/review/BE-13-topology-shape.md`](review/BE-13-topology-shape.md):

```
GET /api/v1/assets/feeders/{feederId}/poles
GET /api/v1/assets/segments/{segmentId}/poles
GET /api/v1/assets/feeders/poles?unassigned=true
```

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


> Ghi theo khuôn: Contract đang ghi gì · Code đang làm gì · Đề xuất · Ảnh hưởng. Chạm bề mặt API thì
> theo nguyên tắc 3 (ESCALATE khi im lặng), không tự coi là approve.
