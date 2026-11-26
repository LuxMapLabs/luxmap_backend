---
ticket: STAT-CITIZEN-CV
title: GET /statistics/citizen-screening — CV ON/OFF sàng lọc báo cáo QR của người dân, cho dashboard Quản lý
status: done
phase: 2
owner: claude
branch: feat/STAT-CITIZEN-AI
contract_refs:
  - "Chưa lên Contract — họ ST-* trong docs/contract-drift.md (SELF-SIGNED). Mã đề xuất: ST-14"
depends_on: []  # D-1…D-12 Mỹ duyệt 26/11/2026
---

## Bối cảnh

Buổi bảo vệ cần số hoá vai trò của CV trong luồng báo cáo QR: người dân quét QR trên cột, gửi ảnh; model ON/OFF gắn gợi ý
cho từng ảnh (PC-8, CQ-5) để Quản lý **không tin ngay** lựa chọn "đèn hư" của người dân — ảnh cho thấy đèn sáng thì nghi báo
nhầm / spam. Màn Tổng quan (`/dashboard`) của Quản lý ở WP5 chưa có khối nào về báo cáo QR.

`GET /statistics/citizen-feedback` (ST-13) chỉ đếm hàng chờ theo trạng thái. Gợi ý CV chỉ có ở `GET /citizen-reports/{id}`
(PC-3/PC-4) và chạy **khi Quản lý mở** báo cáo — web không thể tự gom: phải gọi chi tiết từng báo cáo, và mỗi lần gọi có thể
chạy model (tác dụng phụ). Cần một endpoint thống kê mới, chỉ đọc cột `citizen_photo.ai_hint` đã lưu.

Consumer: WP5 (dashboard Quản lý). Làm cùng phiên: nhánh web `feat/DASHBOARD-citizen-ai`.

## Phải đọc trước

- `CLAUDE.md` + `docs/agents/traps/citizen.md` (P2c: gợi ý lười, `artifact_version`) + `docs/agents/traps/commune-scope.md` + `testing.md`
- `docs/contract-drift.md` ST-1…ST-13, W-5, CITIZEN-QR CQ-5, PC-1…PC-10
- `src/LuxMap.Modules.Statistics/*`, `src/LuxMap.Modules.Citizen/Entities/CitizenReport.cs`, `CitizenReviewService.HintPhotosAsync`

## Yêu cầu (Phase 2 — sau khi Mỹ chốt D-item)

1. Endpoint + contracts + service theo D-item đã chốt; capability `ReadStatistics`.
2. Test tích hợp trong `StatisticsTests` (hoặc file cạnh nó), assert giá trị cụ thể: từng nhãn kết luận cấp báo cáo, ma trận
   mức khớp, `by_night`, `top_poles` (thứ tự, tie-break), lọc nguồn / xã / tuyến / ngày, scope 403, `field_engineer` 403.
3. Phá thử: (a) đổi luật kết luận (bỏ vế "không có ảnh sáng") → test `cv_off` đỏ; (b) đếm `lamp_dim` + CV sáng là báo nhầm →
   test đỏ; (c) bỏ lọc `calibration_rig` mặc định → test nguồn đỏ.
4. Drift ST-14, `gen_consolidated_spec.py` (SUMMARY/SECTION), xuất spec, lint; `tracking.html`.

Tiêu chí xong:

- [ ] `dotnet build --disable-build-servers` sạch; `dotnet test` (lọc Statistics + Capability) xanh, mã thoát 0
- [ ] Spec sinh lại + lint không lỗi mới
- [ ] Web: `src/types/statistics/citizen-screening.ts`, khối dashboard, `tsc`, lint, `vitest`, build xanh; xem trên trình duyệt

## KHÔNG ĐƯỢC làm

- Không migration, không đổi schema / enum (`CitizenPhotoHint`, `CitizenReportStatus`, `source_channel`).
- Không chạy model trong endpoint thống kê; không đổi thời điểm chạy gợi ý (D-5 — ticket riêng nếu Mỹ muốn).
- Không gọi mức khớp là precision / recall: quyết định của Quản lý không phải nhãn chuẩn.
- Không cộng hai `data_source` vào một số (ST-1). Không đổi `citizen-feedback` hay endpoint thống kê khác.
- Không sửa `docs/api-contract-v1.1.md`. Không push / mở PR / deploy khi Mỹ chưa bảo.

## Phase 1 dừng ở đâu

Khảo sát xong thì dừng, ghi `.ai/results/STAT-CITIZEN-CV-p1.md`, chờ Mỹ chốt D-1…D-12.
