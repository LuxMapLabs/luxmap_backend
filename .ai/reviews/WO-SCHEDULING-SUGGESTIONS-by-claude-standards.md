# Claude review — standards

Date: 21/11/2026. Reviewer: Claude Code, model `claude-sonnet-5-5` (configured default).
Pinned diff: `git diff 7d20bf3...96797a7`; reviewed feature commit: `96797a7`.
Independent read-only session; only Read/Grep/Glob tools were allowed. No source changes, test runs, push or PR.

Original response follows verbatim. Some line citations refer to captured diff offsets rather than source line
numbers; the aggregate report corrects source locations and verifies the optional observations against existing code.

**Kết luận (trục STANDARDS): không có vi phạm cứng nào của chuẩn đã ghi trong repo.** Tôi chỉ đọc code và tài liệu, không chạy lệnh hay test nào.

**Đã đối chiếu và thấy đúng chuẩn**
- **Phân quyền:** cả hai action gắn `[Authorize(Policy = ManageWorkOrders)]` riêng (`SchedulingSuggestionsController.cs:493`, `:504`), không cấp class. Không thêm capability mới, nên không phải sửa ma trận literal.
- **Phạm vi xã:** `CommuneFilter.Narrow` gọi trước khi truy vấn (`SchedulingSuggestions.cs:356`). Tên và hình tuyến lấy qua `db.Set<RoadSegment>()` nên vẫn qua query filter (`:314`). Đường GET không ghi, không audit, không notification.
- **Luật một định nghĩa:** dùng lại `FaultStatusSets.Open`, `WorkOrderRules.Eligible` và `WorkOrderOverdueJob.OverdueOn` (`:380`, `:215`). Giờ đêm lấy từ `agenda.NightOf` (`:61`, `:90`), không tính theo ngày lịch.
- **Null trong SQL:** không có `NOT IN` trên cột nullable. Các so sánh `p.SegmentId == w.SegmentId` đều có `w.SegmentId != null` hoặc so với cột không nullable, và `p.Geom == null` viết đúng.
- **Thứ tự ID và phân trang:** sắp `CreatedAt, Id.Length, Id` (`:99`). Test phân trang ghim `POLE-999999` / `POLE-1000000`, nằm ngoài tầm `pole_id_seq`, nên hợp `testing.md` mục 2.
- **Tách nguồn:** mặc định `field`. Overdue lọc theo đối tượng của phiếu, không cộng các lượt lọc.
- **Test isolation:** nằm trong `AssetDatabaseCollection`, teardown xoá theo `home` / `foreign`.

**Gợi ý tuỳ chọn (không chặn merge)**
1. **P3, Duplicated Code — `SchedulingSuggestions.cs:177`.** Hạng severity viết thêm lần thứ ba, bên cạnh `FaultQueryService.cs:218` (private) và `AutoDispatchPlanner.cs:80`. Luật enum của `CLAUDE.md` yêu cầu hạng dạng `Expression` để EF sinh `CASE`. Nên đưa một `Expression` dùng chung vào Faults hoặc Shared và gọi lại, vì thêm mức severity mới sẽ phải sửa ba chỗ.
2. **P3, định nghĩa hẹp hơn Eligible — `SchedulingSuggestions.cs:380-381`.** Clause `kind != Inspection || x == Detected` làm nhóm `fault_inspection` chỉ có fault `detected`. `WorkOrderRules.Eligible` (`WorkOrderRules.cs:9-11`) cho inspection nhận cả `confirmed` / `in_progress`. Spec ghi "một fault detected" nên đây là chủ ý, không phải lỗi. Nhưng fault `confirmed` chưa có phiếu kiểm tra sẽ không hiện ở nhóm nào trong hai nhóm fault_*. Nên ghi rõ trong SS-2 để FE và Quản lý không hiểu nhầm là sót.
3. **P3, chưa chắc là lỗi — `SchedulingSuggestions.cs:99-100`.** Postgres xếp NULL cuối khi `ASC`. Với `survey_needed`, cột chưa từng quan sát (`ObservedAt` null) nằm sau cột `unknown` đã có mốc. Spec ghi "mốc cũ trước" mà không nói về NULL. Nếu muốn cột chưa từng khảo sát lên trước thì cần `OrderBy(x => x.ObservedAt != null)` trước. Cần Mỹ chốt, tôi không coi là lỗi.
4. **P3, nhỏ — `SchedulingSuggestionsController.cs:514`, `:516`.** Mẹo `ContainsKey ? "" : null` để phân biệt tham số rỗng với vắng mặt, lặp ở hai nơi. Có thể gom vào một helper, nhưng chạy đúng.

📌 **Chưa kiểm chứng:** tôi không xác minh bản dịch SQL của EF cho `GroupBy(x => 1)` kèm subquery, và không đối chiếu `luxmap-v1.5.json` với code. Hai việc này dựa vào bằng chứng test lịch sử mà bạn cung cấp.
