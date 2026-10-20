---
ticket: WO-SURVEY-CHAIN
title: Tạo lịch kiểm tra tiếp từ phiếu khảo sát đã nghiệm thu — mang sang sự cố khảo sát phát hiện, chung case_id
status: review
phase: 2
owner: claude
branch: feat/WO-SURVEY-CHAIN
depends_on:
  - "FR-2 / FR-2a (docs/contract-drift.md): POST /work-orders/{id}/follow-up, case_id, verify inspection → confirmed"
  - "BE-15 / BE-17: duyệt sweep (accept) công bố luminance_history + tự sinh fault detected (SurveyReviewService.Publish)"
---

## Bối cảnh

Đề xuất của WP5 (20/10/2026): giữ một `case_id` cho chuỗi Khảo sát → Kiểm tra → Sửa chữa. Đề xuất gốc tạo sự cố lúc
`verify` phiếu khảo sát và ghi `WorkOrderFault` vào phiếu khảo sát — **đã bác** (review Claude + Codex 20/10): duyệt sweep
**đã** sinh fault `detected` (`SurveyReviewService.cs` ~291–316), tạo lần nữa là trùng; liên kết thêm lúc verify không được
`released_at` và chặn chính phiếu kiểm tra (`ux_work_order_fault_fault_id_active`).

## Mỹ đã chốt (20/10/2026)

- **C-1** `FollowUpKinds(Survey) = [Inspection]`. Lịch đầu (khảo sát) là gốc `case_id`.
- **C-2** Tập sự cố mang sang **tính lúc gọi follow-up**, không lưu trước: sự cố **còn mở** (`FaultStatusSets.Open`) trên cột mà
  lần khảo sát **đã duyệt** của phiếu thấy `out` / `dim`, **cùng loại hiệu lực** (`override ?? fault_type` = `FaultFor(classified_as)`),
  **gồm cả sự cố đã mở từ trước** (bị bỏ qua lúc publish vì chống trùng) — "Cách 2".
- **C-3** Loại sự cố đang nằm trong phiếu **đang chạy** khác (`released_at IS NULL`) — không để 409 cả request.
- **C-4** Khảo sát liên xã: chỉ lấy sự cố thuộc **xã chính** (`work_order.commune_id` của phiếu khảo sát).
- **C-5** Không migration, không ghi `WorkOrderFault` vào phiếu khảo sát, không tạo sự cố mới ở luồng này.

## KHÔNG ĐƯỢC làm

- Không thêm project reference WorkOrders → Survey (Survey đã tham chiếu WorkOrders): dùng **port** khai ở WorkOrders, Survey
  hiện thực (khuôn `IPolePhotoPublication`, `IActiveWorkOrderLookup`).
- Không viết lại ánh xạ `classified_as → fault_type` (một nguồn: `SurveyPublicationRules.FaultFor`), không viết lại tập "mở"
  (`FaultStatusSets`).
- Không đổi luồng duyệt sweep, không đổi FR-2 cho cặp kiểm tra → sửa chữa.
- Không `ORDER BY fault_id` trần (luật độ rộng tối thiểu, CLAUDE.md mục 0).

## Pha 1 — phải trả lời (`.ai/results/WO-SURVEY-CHAIN-p1.md`)

1. Nguồn "lần khảo sát đã duyệt" chính xác: sweep `Accepted` của phiếu + `AcceptedRunId` → `luminance_history.classified_as`?
   Phiếu có nhiều sweep đã duyệt, cùng một cột khác kết quả → lấy kết quả nào?
2. Hình dạng port (tên, chữ ký, trả về gì), hiện thực ở Survey, đăng ký DI.
3. Thay đổi ở `FollowUp`: `NOTHING_TO_FOLLOW_UP`, `fault_ids` (tập con), `carried_fault_ids` trong lỗi 400, khoá/đua
   (FaultLocks trong `Insert` đã đọc lại trạng thái — đủ chưa khi lọc C-3 trước khoá?).
4. `AllowedActions`: Quản lý thấy `follow_up` trên phiếu khảo sát `verified`. Ảnh hưởng FE.
5. Test cần có (literal, sabotage), drift cần ghi, OpenAPI có đổi không.
6. D-item còn mở.
