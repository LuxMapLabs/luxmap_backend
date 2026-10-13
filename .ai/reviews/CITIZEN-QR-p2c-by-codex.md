# CITIZEN-QR P2c — review Codex

13/10/2026 · `git diff 33378be..HEAD` · Đọc Phase 2c, phản biện kế hoạch, AGENTS.md và các luồng liên quan. **0 P1, 3 P2, 1 P3.** Chỉ ghi file này; không chạy Docker/dotnet/generator hoặc phá thử.

## Spec — 2 P2

1. **[P2] `allowed_actions` chứa thao tác chắc chắn bị từ chối.** `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:416`: báo cáo chỉ có `lamp_out`, cột đã có sự cố mở cùng loại, vẫn trả `accept`; POST luôn 409 `OPEN_FAULT_EXISTS`. Trái nghĩa “can do now” ở `CitizenReviewContracts.cs:56`. `tests/LuxMap.Api.Tests/CitizenReviewTests.cs:125` còn ghim hành vi này. **Đề xuất:** chỉ thêm `accept` khi còn ít nhất một loại có căn cứ chưa bị trùng loại hiệu lực; test cả override và báo cáo hỗn hợp chỉ trùng một loại.

2. **[P2] Spec dẫn tới quyết định chưa được ghi; accept lệch HTTP đã mô tả.** `docs/openapi/tools/gen_consolidated_spec.py:272` viện dẫn **PC-1…PC-10**, nhưng `docs/contract-drift.md:1290` kết thúc ở PB-8, không có PC-items. `CitizenReportsController.cs:75` xuất **200**, trong khi `.ai/tasks/CITIZEN-QR.md:77` yêu cầu **201**; các bổ sung sau phản biện như `submission_count` cũng chưa được mô tả ở tầng drift. **Đề xuất:** ghi các PC-items thực tế, chốt HTTP/response/body/lỗi rồi đồng bộ task và spec; tránh để WP5 dựa vào một tham chiếu không tồn tại.

## Standards — 1 P2, 1 P3

3. **[P2] Ảnh lỗi chiếm hết ngân sách AI, ảnh sau không bao giờ được xét.** `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:303`: mỗi lần mở lấy `HintsPerRequest` ảnh null cũ nhất; lỗi đọc object hoặc hinter trả null tại dòng 320–328 vẫn để ảnh trong tập đó. Nếu sáu ảnh đầu lỗi riêng, mọi lần mở tiếp tục thử chúng, ảnh hợp lệ phía sau luôn bị bỏ qua. **Đề xuất:** cơ chế retry có cooldown hoặc tiến tiếp có giới hạn, không đánh dấu lỗi thành kết luận AI; test đủ một batch ảnh lỗi trước ảnh hợp lệ.

4. **[P3] Test chưa canh thời điểm sớm nhất, ngân sách AI và rollback giữa hai save.** `tests/LuxMap.Api.Tests/CitizenReviewTests.cs:68` chỉ so `detected_at` với `report.CreatedAt` trên một lượt; dòng 262 chỉ dùng một ảnh; dòng 42 chỉ kiểm accept thành công. Đổi `sends.Min(ReceivedAt)` thành `report.CreatedAt`, bỏ `Take(HintsPerRequest)`, hoặc để fault commit trước khi save report lỗi đều không được các ca này phân biệt. **Đề xuất:** lượt gửi nhận trước nhưng hoàn tất sau rồi accept; nhiều ảnh vượt quota một GET; ép save thứ hai lỗi và kiểm fault/report/hai audit cùng rollback. Đây là suy luận tĩnh về chốt test còn thiếu, chưa chạy sabotage.

✅ **Đã đối chiếu tĩnh:** filter xã ở report/submission/fault; Read không có link ảnh/AI/actions; proxy buộc ảnh thuộc đúng report; DTO bỏ GPS/key/code; lỗi sự cố lấy qua scope. Quyết định khóa **pole → report → fault**; public lấy pole trước report; PATCH/tạo phiếu không lấy pole sau fault — chưa thấy vòng deadlock trong các đường này. `CitizenPhoto : IAudited` không làm gãy gửi ảnh công khai: ảnh và lượt gửi đã dùng chung event `Submitted`; AI dùng `Analyzed/cv`; accept hai save trong một transaction. StageReviewed lấy nguồn từ cột, xác nhận qua transition. Teardown đặt report trước fault đúng FK. So JSON spec: chỉ thêm bảy route, không đổi route cũ.

⚠️ **Không xác minh runtime.** Kết quả test/build/migration và bảy sabotage xanh/đỏ là bằng chứng Claude do người dùng cung cấp, không phải lượt chạy này. 📌 Kiểm trùng chỉ tại lúc accept theo quyết định đã chốt; không yêu cầu bất biến toàn hệ thống. Nhãn AI là gợi ý; chưa đánh giá độ chính xác thực địa.

## Đã xử lý (Claude, 13/10/2026)

| # | Xử lý | Canh bằng (phá thử → đỏ) |
|---|---|---|
| 1 `allowed_actions` mời bấm thao tác chắc 409 | `acceptable_fault_types` bỏ loại đã có sự cố mở trên cột; `accept` chỉ hiện khi còn loại | `With_an_open_fault_of_that_type…` (nay `["merge","reject"]`), `A_mixed_report_offers_only_the_type_not_already_open_on_the_pole` — bỏ lọc → 2 đỏ |
| 2 PC-items không tồn tại; 200 vs 201 | Drift PC-1…PC-10 commit **sau** snapshot của lượt review (`7dfd5fc`); 200 + chi tiết báo cáo là hiện thực — ticket sửa cho khớp, PC-6 ghi 200 | — |
| 3 ảnh lỗi chiếm suất AI | `CitizenHintBackoff`: ảnh không gợi ý được chờ 10 phút (trong bộ nhớ, theo instance); lỗi không bao giờ lưu thành gợi ý | `A_photo_that_could_not_be_hinted_does_not_starve_the_photos_after_it` — bỏ backoff → đỏ |
| 4 test thiếu | `detected_at` = lượt nhận sớm nhất; giới hạn `HintsPerRequest` | `The_fault_is_detected_at_the_earliest_send…`, `One_opening_runs_the_model_on_at_most_HintsPerRequest_photos` — đổi về `report.CreatedAt` / bỏ `Take` → đỏ. **Còn nợ:** tiêm lỗi save thứ hai để chứng minh rollback cả hai (cùng nợ P2a) |
