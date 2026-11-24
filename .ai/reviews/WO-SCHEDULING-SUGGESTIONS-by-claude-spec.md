# Claude review — spec

Date: 21/11/2026. Reviewer: Claude Code, model `claude-sonnet-5-5` (configured default).
Pinned diff: `git diff 7d20bf3...96797a7`; reviewed feature commit: `96797a7`.
Independent read-only session; only Read/Grep/Glob tools were allowed. No source changes, test runs, push or PR.

Original response follows verbatim. Some line citations refer to captured diff offsets rather than source line
numbers; the aggregate report corrects source locations and verifies the optional observations against existing code.

**Kết luận (trục spec/đúng đắn): không có lỗi P0–P2 được xác nhận.** Tôi đã đối chiếu `SchedulingSuggestions.cs` với `docs/work-order-scheduling-suggestions.md` và với các lệnh tạo phiếu hiện có. Tôi không chạy lệnh nào và không đánh giá lại kết quả test cũ.

**Đã kiểm tra, khớp spec**
- **Fault inspection/repair:** `SchedulingSuggestions.cs:380` dùng đúng `FaultStatusSets.Open` và `WorkOrderRules.Eligible`. Inspection chỉ nhận `detected`, repair nhận `confirmed` và `in_progress`, khớp `InsertCore` (`WorkOrderService.cs:399`). Loại sự cố hiệu lực là `OverrideFaultType ?? FaultType`.
- **Trạng thái giữ chỗ:** `released_at IS NULL` khớp việc `CreatePoleGpsCore` (`:259`) và `InsertCore` (`:403`) từ chối khi mục tiêu đã bị giữ. Phiếu done chờ duyệt vẫn giữ mục tiêu. Hủy hoặc verify gọi `ClosedAt` rồi thả link (`:680-684`), nên mục tiêu quay lại `ready`.
- **Điều kiện chặn GPS:** `POLE_SEGMENT_REQUIRED` khớp `segment_id` bắt buộc. `SEGMENT_NOT_ACCESSIBLE` khớp `ASSET_NOT_FOUND`. `CROSS_COMMUNE_REFERENCE` khớp `:258`.
- **Khảo sát:** không đòi tuyến phải có hình học, khớp `CreateSurveyCore`. `unknown` hoặc chưa có trạng thái chỉ thành `survey_needed`, không thành `lamp_out`.
- **Overdue:** dùng đúng `WorkOrderOverdueJob.OverdueOn(night)`. Đêm tính qua `agenda.NightOf`, cùng định nghĩa với job BE-26.
- **Nguồn dữ liệu và ID:** mặc định `field`. Thứ tự dùng `Id.Length` rồi `Id`, đúng luật ID tối thiểu N chữ số.
- **Kiểu dữ liệu:** `ObservedAt` là `DateTime?` và đi qua `UtcDateTimeConverter` (`UtcDateTimeConverter.cs:11`). System.Text.Json tự bọc converter cho kiểu nullable, nên xuất ra hậu tố `Z`.
- **Phạm vi nới thêm:** không có endpoint ghi, migration hay enum mới.

**Nghi ngờ chưa xác nhận (P3)**
- `SchedulingSuggestions.cs:310`: `.ThenBy(x => x.ObservedAt)` sắp tăng dần, và Postgres đặt NULL cuối. Với `survey_needed`, cột chưa từng quan sát (`ObservedAt = null`, kể cả `unknown`) sẽ xếp sau cột có mốc quan sát cũ. Spec ghi "mốc cũ trước" nhưng không nói rõ cách xếp NULL. Cột chưa từng quan sát thường là việc cần khảo sát nhất. Nếu muốn chúng đứng trước, thêm khóa `ObservedAt == null` giảm dần trước `ThenBy(ObservedAt)`. Chưa đủ căn cứ để coi là lỗi, nên cần chốt với spec.

**Cải tiến tùy chọn**
- `SchedulingSuggestions.cs:430-433`: lọc nguồn của Overdue dùng cả link `WorkOrderFault` và `WorkOrderPoleTarget` đã thả (`ReleasedAt != null`). Điều này vô hại, vì phiếu overdue luôn đang mở và target chưa bị thả. Chỉ cần lưu ý nếu sau này có chức năng thả riêng từng target.
- Fault không có cột (`node_offline`, `runtime_decline`) vẫn nằm trong nhóm inspection/repair với `pole_count` = 0. Việc này hợp lý theo spec ("một fault") và nên giữ.

📌 Tôi chưa xác nhận bằng chạy thực tế việc EF dịch `Distinct().Count()` trong `GroupBy(x => 1)` (`:276-284`) sang `COUNT(DISTINCT …)`. Phần này dựa vào bằng chứng test cũ mà bạn cung cấp, tôi không tự chạy lại.
