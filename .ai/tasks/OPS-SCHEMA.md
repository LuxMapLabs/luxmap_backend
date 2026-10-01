---
ticket: OPS-SCHEMA
title: Rà lược đồ DB đích trước khi lên Supabase — bảng nào thêm / sửa / xoá, ERD đích, theo Phiếu v1.4
status: ready
phase: 1
owner: codex
branch: docs/db-schema-review
contract_refs:
  - "mục 1 — enum; mục 2 — capability; mục 3 — luồng trạng thái, audit, lưu trữ; mục 5 — endpoint"
depends_on: []
---

## Bối cảnh

Mỹ muốn **hoàn thiện lược đồ và ERD** trước khi đưa DB lên Supabase (OPS-SUPABASE). Nguồn chuẩn cho
phạm vi là **Phiếu đăng ký FA26SE222 v1.4** (`docs/registration/FA26SE222_v1.4.md`, 27/09/2026) — nó đổi
kênh đo sáng (CV chỉ ON/OFF, độ sáng đo bằng BH1750 trên xe), khảo sát bằng **video** chứ không phải ảnh
JPEG, và nhiều D-item còn treo. Lược đồ hiện có 17 bảng (BE-09…BE-23, BE-14b, BE-19, BE-40, FR-2/FR-3).

Câu hỏi của ticket: **để phủ đủ chức năng của Phiếu v1.4, lược đồ còn thiếu bảng nào, bảng nào cần sửa,
bảng hay cột nào thừa / mang nền cũ cần bỏ** — và việc nào **phải** chốt trước khi chép dữ liệu lên Supabase,
việc nào làm sau được (migration vẫn chạy bình thường trên Supabase sau khi lên).

Đây là Phase 1: **khảo sát và đề xuất, không sửa code, không tạo migration.**

## Phải đọc

Theo thứ tự ưu tiên của repo (Contract > tasks-backend.csv > AGENTS.md > .ai):

- `docs/registration/FA26SE222_v1.4.md` — **toàn bộ**, nhất là chức năng theo vai trò, NFR, deliverable,
  Survey Data Association, Fixture Condition, testbed IoT, báo cáo/thống kê, QR công dân.
- `docs/api-contract-v1.1.md` (v1.7), `docs/contract-drift.md` (D-R1…D-R28, I-*, WO-*, FR-1…FR-5, R-*, P-*).
- `docs/tasks-backend.csv` — danh sách task. ⚠️ Cột trạng thái trong CSV **không được cập nhật**; trạng thái
  thật ở `tracking.html` và ở code.
- `AGENTS.md` — đặc biệt mục Domain model, "Quy tắc dễ sai âm thầm", quy ước `double precision`, partial index,
  `ICommuneScoped`, `data_source`, audit append-only (BE-23a), BE-14b IoT ở tủ điện.
- Lược đồ hiện tại: `src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs` + các entity trong `src/`.
- **Quyết định nằm trên nhánh chưa merge** — đọc bằng `git show <nhánh>:<đường dẫn>`, chúng có hiệu lực:
  - `docs/BE-15-survey-flow:.ai/tasks/BE-15.md` — flow phiên khảo sát video đã thảo luận 30/09 (giao tuyến,
    server tách lượt, chiều chuẩn từng cột, 3 mức độ phủ, 9 câu hỏi mở).
  - `docs/BE-15-survey-flow:docs/contract-drift.md` — mục EV-1 / EV-2 (ảnh bằng chứng kiểm tra, ảnh báo sự cố).
  - `feat/photos-to-poles:docs/contract-drift.md` — mục FX-1 (đèn thật là sodium, enum chỉ có LED; giá trị
    `lamp_watt`/`install_date` tạm).
  - `chore/supabase-deploy:.ai/results/OPS-SUPABASE-p1.md` — số đo dữ liệu thật trong `luxmap_dev`.

## Yêu cầu — Phase 1

1. **Kiểm kê lược đồ hiện tại** từ snapshot: 17 bảng, khoá, FK, `ICommuneScoped`, `data_source`, audit.
2. **Bảng đối chiếu chức năng Phiếu v1.4 → lược đồ.** Mỗi chức năng (theo vai trò + NFR + deliverable): bảng nào
   phục vụ, đang **có / thiếu / cần sửa / thừa**. Trích dòng Phiếu hoặc file:dòng làm bằng chứng.
3. **Đề xuất thay đổi**, mỗi mục có: tên bảng/cột, khoá, FK (`Restrict` hay không, vì sao), có `commune_id` +
   `ICommuneScoped` không (theo quy tắc BE-09), có `data_source` không, có cần `IAudited` không, CHECK cần có
   (kể cả quy ước NaN/Infinity cho số thực đo được), task/ticket sở hữu, và **mức chắc chắn: CHẮC / KHÔNG CHẮC**.
   Tối thiểu phải xét (không giới hạn ở đây):
   - Phiên khảo sát video (D-R21/D-R24/D-R28, BE-15/16/17): phiên, clip video, GPS track, mẫu lux BLE, cấu hình
     quay, lượt đi (pass), quan sát theo cột theo sweep, chuỗi luminance, baseline theo cột, liên kết phiếu
     khảo sát (FR-1 `task_kind = survey`, tuyến của phiếu có thứ tự). Tên `SurveySweep`/`SurveyFrame` cũ có
     còn hợp không.
   - CV: detection / kết quả ON/OFF, phiên bản model & firmware (BE-34), độ tin cậy, dataset gán nhãn.
   - IoT testbed (BE-14b đã có `iot_node`, `feeder_control`): telemetry, lệnh điều khiển ON/OFF/AUTO (D-R7),
     `runtime_decline`, `node_offline` (IOT-11).
   - Ảnh/video bằng chứng (BE-24, EV-1, EV-2, D-R14), ảnh khảo sát (BE-11 đã có code lưu trữ, chưa có bảng?).
   - Báo cáo QR của công dân (D-R1): hàng chờ riêng, Quản lý duyệt.
   - Notification (BE-27), đơn vị ngoài (ExternalUnit, BE-23), cấu hình ngưỡng / trọng số (BE-33), quản trị
     danh mục, ground truth / field verification cho `dim` và `out` (D-R23), sync offline (BE-43).
   - Cột / bảng hiện có mang **nền cũ**: `pole_current_status` (ai ghi, các cột còn đúng ở v1.4 không),
     `lux_reading` (đo thủ công), `fixture` (FX-1), `fault.priority_score` (P-1), `iot_node` (I-*), `external_ref`
     (D-R10), `administrative_unit` không geometry.
4. **ERD đích** dạng Mermaid `erDiagram`: bảng hiện có + bảng đề xuất, đánh dấu rõ cái nào mới / sửa.
5. **Phân loại thời điểm:** (A) phải chốt **trước** khi chép dữ liệu lên Supabase, vì đụng dữ liệu đang có hoặc
   khó đổi sau; (B) làm sau được bằng migration thường. Giải thích vì sao từng mục thuộc nhóm nào.
6. **Danh sách KHÔNG CHẮC**: với mỗi mục, các phương án, hệ quả, và đề xuất của Codex.

Kết quả ghi vào `.ai/results/OPS-SCHEMA-p1.md`. Viết tiếng Việt như các file results khác.

## KHÔNG ĐƯỢC làm

- Không sửa `src/`, migration, `docs/api-contract-v1.1.md`, `docs/openapi/*`, mock, CSV.
- Không tạo migration, không chạy `dotnet ef`, không chạy `dotnet test`.
- Không đọc `.env`; không kết nối DB hay Supabase; không chạy Docker.
- Không commit, không push, không `git add -A`. Không checkout nhánh khác (đọc bằng `git show`).
- Không tự chốt quyết định chạm Contract — ghi KHÔNG CHẮC hoặc D-item.
- Không thiết kế lại thứ đã chốt và đã hiện thực (fault, work_order, audit, phân quyền) trừ khi Phiếu v1.4 bắt
  buộc — khi đó nêu rõ dòng Phiếu.

## Phase 1 dừng ở đâu

Ghi xong `results/OPS-SCHEMA-p1.md` thì dừng hẳn. Claude review độc lập, chốt các mục cả hai cùng chắc; Mỹ chỉ
quyết những mục cả hai đều không chắc.
