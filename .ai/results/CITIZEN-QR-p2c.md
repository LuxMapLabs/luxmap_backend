# CITIZEN-QR — P2c: hàng chờ duyệt của Quản lý (Claude + Codex, 13/10/2026)

Nhánh `feat/CITIZEN-QR-p2c` (từ `dev` @ `33378be`, sau merge #135). Quyết định: D-5, D-9, D-11, D-17, D-18 (Mỹ chốt 12/10); chi tiết
theo phản biện kế hoạch của Codex. Drift **PC-1…PC-10** (SELF-SIGNED). Với P2c, CITIZEN-QR xong phần backend của D-19.

## Cách làm cùng Codex

1. Claude viết kế hoạch P2c vào ticket → **Codex phản biện kế hoạch** (`.ai/reviews/CITIZEN-QR-p2c-plan-by-codex.md`, 3 P1 / 7 P2 / 1 P3).
   Áp: audit cho gợi ý AI, loại sự cố phải khớp loại dân báo, chỉ kết luận khi một bóng + ảnh không sáng như ban ngày, giới hạn và chống
   ghi trùng gợi ý, `submission_count` khi quyết, `detected_at` = lượt sớm nhất. Đã đúng sẵn: đọc lại sau khoá, CHECK AI, đường ảnh. Chọn
   "kiểm trùng tại lúc chấp nhận", không thêm khoá vào `PATCH /faults` (tránh vòng fault → pole).
2. Claude hiện thực + test + phá thử → **Codex review code** (`.ai/reviews/CITIZEN-QR-p2c-by-codex.md`, 0 P1 / 3 P2 / 1 P3) → Claude sửa,
   mỗi bản có test và phá thử đỏ (bảng cuối file review).

## Đã làm

| Phần | File |
|---|---|
| Capability `ReadCitizenReports` (superior, manager, system_admin), `ReviewCitizenReports` (manager); literal trong hai test ma trận | `LuxMapPolicies.cs`, `CapabilityMatrixTests`, `RoleCapabilityMatrixTests` |
| `GET /citizen-reports` (vắng `status` = pending, mới nhất trước), `GET /citizen-reports/{id}` (cột, sự cố mở, lượt gửi, `allowed_actions`, `acceptable_fault_types`), ảnh `…/photos/{id}/thumbnail\|original` (chỉ Quản lý, phải thuộc đúng báo cáo, `no-store`) | `CitizenReportsController.cs`, `CitizenReviewService.cs`, `CitizenReviewContracts.cs` |
| `accept` → sự cố `field_report` `confirmed` dựng ở Faults (`FaultReportService.StageReviewedReportAsync`); `merge` vào sự cố mở cùng cột; `reject` có lý do. Khoá pole → báo cáo → sự cố; `submission_count` bắt buộc | `CitizenReviewService.cs`, `FaultReportService.cs` |
| Gợi ý AI theo ảnh: YOLO của AI-1, một bóng + kiểm ảnh ban ngày, phiên bản = detector + băm luật; chạy lười ≤ 6 ảnh / lượt mở, ngoài khoá; ghi dưới khoá hàng ảnh, một audit `analyzed` (actor `cv`); ảnh lỗi chờ 10 phút | `CitizenPhotoHints.cs`, `CitizenHintBackoff.cs` |
| `CitizenPhoto` thành `IAudited`; audit thêm `accepted`, `merged`, `analyzed`; 4 cột AI + CHECK (migration `AddCitizenReview`) | `CitizenReport.cs`, `CitizenReportConfigurations.cs`, `AuditEvent.cs`, migration |
| Teardown: báo cáo **trước** sự cố (báo cáo đã chấp nhận giữ sự cố) | `AssetImportFixture.cs` |
| Spec xuất lại (chỉ thêm 7 operation, 16 schema), generator (tag `CitizenReports`, summary, section) | `docs/openapi/` |
| `CitizenReviewTests` (28) | tests |

## Bằng chứng

```
dotnet build LuxMap.slnx -warnaserror      Build succeeded
Api 956 ✓ · 2 skip (MQTT E2E) · Storage 46 ✓ · Persistence 45 ✓ · Shared 383 ✓   (container BE-36, sau review)
Redocly: valid, 8 warnings (như trước)
```

Migration `AddCitizenReview` đọc trước khi apply: 4 `AddColumn`, 4 CHECK mới, dựng lại CHECK `action`; `Down()` đối xứng; không
`DropIndex`. Apply → rollback → apply trên DB dùng một lần: 4 cột / 4 CHECK / `analyzed` có → 0 / 0 / không → như lần đầu.

Phá thử (khôi phục bằng `cp` + `touch`) — 11 chốt, đều đỏ: bỏ khoá pole · bỏ kiểm `pending` · bỏ kiểm `submission_count` · nới D-17 ·
ghi đè gợi ý (đỏ 3/3) · lộ ảnh cho người không duyệt · mở ảnh của báo cáo khác · bỏ backoff · bỏ giới hạn ảnh / lượt · `detected_at` theo
lúc mở báo cáo · mời `accept` khi đã có sự cố mở cùng loại.

## Giới hạn đã biết

- Kiểm "cột đã có sự cố mở cùng loại" chỉ tại lúc chấp nhận; báo tại chỗ / khảo sát / `PATCH /faults` vẫn có thể tạo hoặc đổi thành trùng.
- Backoff gợi ý theo instance, trong bộ nhớ.
- Chưa có test tiêm lỗi ở lần save thứ hai của `accept` (rollback cả sự cố lẫn audit) — cùng nợ với P2a.
- Model thật trên ảnh tổng hợp gọi khung đen là một bóng `out`: gợi ý chỉ là gợi ý; độ chính xác là việc của WP4.
- Chưa làm: mở phiếu kiểm tra thẳng từ báo cáo "cột hỏng" (D-17 vế sau), tự xoá ảnh (BE-26), báo kết quả cho người dân.
- WP5 chưa được báo (màn duyệt + trang `/r/:code`).
