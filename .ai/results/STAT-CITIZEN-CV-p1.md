# STAT-CITIZEN-CV — Phase 1 (khảo sát, chưa sửa code)

Claude, 26/11/2026, nhánh `feat/STAT-CITIZEN-AI` (tách từ `origin/dev` `ff67208`).

## Đã có gì

| Nguồn | Có | Thiếu cho dashboard |
|---|---|---|
| `GET /statistics/citizen-feedback` (ST-13) | Đếm `CitizenReport` theo `pending/accepted/merged/rejected`, nhóm `data_source` (+ xã / tuyến), lọc `night_of` | Không có gì về CV, xu hướng theo đêm, cột bị báo nhiều |
| `GET /citizen-reports` (PC-2) | `kinds[]`, `photo_count`, `far_from_pole`, trạng thái | Không có gợi ý CV |
| `GET /citizen-reports/{id}` (PC-3/PC-4/PC-8) | `ai_hint {label, confidence, artifact_version}` từng ảnh | **Chạy model khi người có `ReviewCitizenReports` mở** (tối đa 6 ảnh / lượt) — web gom từ đây là N request + chạy model hàng loạt |

Lưu ở DB (`\d citizen_photo`, luxmap_dev 26/11): `ai_hint text` CHECK `lamp_on | lamp_off | inconclusive`, `ai_confidence`,
`ai_artifact_version`, `ai_checked_at` — bốn cột cùng null hoặc cùng có (`ck_citizen_photo_ai_together`). `citizen_submission.kind`
CHECK `lamp_out | lamp_dim | pole_damaged | other`, `far_from_pole boolean not null`.

⇒ **Cần endpoint mới**, chỉ đọc `ai_hint` đã lưu. Statistics đã tham chiếu Citizen (ST-13) — không thêm project reference.

**Dữ liệu thật hôm nay:** luxmap.com.vn, tài khoản `engineer` (manager), 26/11 02:24 UTC: `citizen-feedback` (đủ 4 nguồn) →
`rows: []`; `citizen-reports?status=pending,accepted,merged,rejected` → `total: 0`. luxmap_dev: `citizen_report` 0 dòng,
`citizen_photo` 0 dòng. ⇒ Dashboard trên prod sẽ hiện toàn 0 cho tới khi có báo cáo thật / demo (D-12).

## D-item — đề xuất của Claude, chờ Mỹ chốt

| Mã | Câu hỏi | Đề xuất |
|---|---|---|
| **D-1** | Endpoint | `GET /api/v1/statistics/citizen-screening`, capability `ReadStatistics` (ST-8). Query như ST-13: `commune_id` lặp (`CommuneFilter.Narrow`), `segment_id` (qua `pole.segment_id`), `data_source` CSV mặc định loại `calibration_rig`, `from`/`to` theo `night_of` (bao hai đầu, cùng `ValidateDateBounds`, không cửa sổ mặc định). Endpoint riêng, không mở rộng `citizen-feedback` (hình dạng khác) |
| **D-2** | Kết luận CV **cấp báo cáo** từ gợi ý từng ảnh (pole một bóng) | Trên các ảnh còn giữ của mọi lượt gửi: `cv_off` = ≥1 ảnh `lamp_off` và 0 ảnh `lamp_on`; `cv_on` = ≥1 `lamp_on` và 0 `lamp_off`; `inconclusive` = có ảnh đã chạy nhưng toàn `inconclusive`, **hoặc** vừa sáng vừa tắt; `not_analyzed` = có ảnh nhưng chưa ảnh nào chạy model; `no_photo` = không giữ ảnh nào. Năm nhóm rời nhau, cộng = số báo cáo. Ảnh chưa chạy không làm đổi kết luận của ảnh đã chạy |
| **D-3** | Người dân "nói gì" | `claim` = `lamp_out` nếu có lượt gửi chọn "đèn tắt"; ngược lại `lamp_dim` nếu có "đèn mờ"; ngược lại `other` (cột hỏng / khác — CV không liên quan). **Nghi báo nhầm** (`contradicted`) = `claim = lamp_out` **và** `cv_on`. `lamp_dim` + `cv_on` **không** là mâu thuẫn (đèn mờ vẫn sáng — model không đo độ sáng) |
| **D-4** | Mức khớp CV ↔ Quản lý | Chỉ trên báo cáo **đã quyết**, `claim = lamp_out`, kết luận `cv_off` / `cv_on`. "Giữ" = `accepted` ∪ `merged`. Bảng 2×2 `{cv_off, cv_on} × {upheld, rejected}` + `agreement_rate = (off∧upheld + on∧rejected) / tổng`, `null` khi 0; **kèm tỉ lệ riêng từng lớp** (`cv_off`: upheld / n, `cv_on`: rejected / n — luật "báo riêng từng lớp"). Gọi là **mức khớp**, không phải precision / recall: Quản lý không phải nhãn chuẩn. Tỉ lệ tính ở backend, 4 chữ số, `AwayFromZero` như ST-13 |
| **D-5** | Gợi ý chạy lười (chỉ khi mở) ⇒ báo cáo chưa ai mở là `not_analyzed` | **Giữ nguyên** ở ticket này, hiện `not_analyzed` rõ trên dashboard. Chạy gợi ý ngay lúc nhận (job nền) để hàng chờ được sàng lọc trước khi Quản lý mở là **ticket riêng** — chạm Citizen + job, PC-8 |
| **D-6** | `data_source` (ST-1: không cộng hai nguồn) | Response `{as_of, from, to, sources[]}`, mỗi phần tử một `data_source` với đủ các khối của D-7…D-9; chỉ nguồn có dữ liệu, theo thứ tự enum. Web chọn / hiện từng nguồn, mặc định nguồn `field` |
| **D-7** | Khối tổng | Mỗi nguồn: `reports`, `submissions`, `far_from_pole` (báo cáo có lượt gửi ở xa cột — tín hiệu spam thứ hai, sẵn có), `by_status {pending, accepted, merged, rejected}`, `by_verdict {cv_off, cv_on, inconclusive, not_analyzed, no_photo}`, `by_claim[]` (mỗi `claim` một dòng kèm `by_verdict`), `contradicted`, `agreement` (D-4) |
| **D-8** | Xu hướng | `by_night[]`: chỉ đêm có dữ liệu, tăng dần; `night_of, reports, cv_off, cv_on, inconclusive, not_analyzed, no_photo, contradicted`. Web tự lấp đêm trống |
| **D-9** | Cột bị báo nhiều | `top_poles[]`: 10 cột, sắp `reports` giảm, `submissions` giảm, rồi `length(pole_id)`, `pole_id` (không sắp ID trần); `pole_id, segment_id, reports, submissions, contradicted, last_night_of`. Số cố định 10, không tham số |
| **D-10** | Phiên bản model (BE-34) | Mỗi nguồn kèm `artifact_versions[] {artifact_version, photos}` — các phiên bản gợi ý có mặt trong cửa sổ. Kết luận không tách theo phiên bản; web cảnh báo khi > 1 phiên bản |
| **D-11** | CSV (W-5) | **Không** nhận `format=csv` (hình dạng lồng nhau) → 400 `VALIDATION_FAILED`, `details.field = format`. Ai cần CSV dùng `citizen-feedback` |
| **D-12** | Web + dữ liệu demo | Khối mới trên `ManagerDashboardPage`: KPI (tổng / chờ duyệt / CV xác nhận tắt / nghi báo nhầm / mức khớp), biểu đồ 30 đêm (`from = đêm nay − 29`), donut kết luận CV, bảng mức khớp theo lớp, bảng cột bị báo nhiều; số hàng chờ lấy từ endpoint mới. Prod đang 0 báo cáo: tạo dữ liệu demo (gửi form QR thật với ảnh đêm, rồi mở / quyết vài báo cáo) là bước riêng, cần Mỹ cho phép vì ghi vào prod |

Tính toán (thuộc D-1): **một** truy vấn DB chiếu mỗi báo cáo một dòng (đếm ảnh theo nhãn, cờ `claim`, `far_from_pole`, số lượt gửi,
`segment_id` của cột), gom các khối trong bộ nhớ. Báo cáo công dân ít (mỗi cột tối đa một hạng mục chờ / đêm), nên kéo một dòng
nhỏ mỗi báo cáo là chấp nhận được — khác ST-13 (gom hẳn ở DB) vì D-2 cần kết luận theo từng báo cáo trước khi đếm.

## Dừng

Phase 1 dừng ở đây. Chưa sửa code.
