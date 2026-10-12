# BE-31 — Review Phase 2, Codex (12/10/2026)

Phạm vi: `git diff 7a9c1f9..HEAD`, 3 commit `5f56cdf`, `150510b`, `ab2bcc4`; đối chiếu task, quyết định cuối P1, W-1…W-8 và AGENTS.md. Không thấy P1. Cần sửa 3 P2; 4 P3 về độ bảo vệ của test.

## Spec

- **P2 — OpenAPI mô tả sai phân trang.** `docs/openapi/luxmap-v1.json:6751`, `docs/openapi/luxmap-v1.5.json:8086` khai một query `page` kiểu object `PageQuery`, thiếu tham số độc lập `page_size`. Runtime (`StatisticsController.cs:104`, `PageQuery.cs:39`) đọc hai số `page`/`page_size`; SDK sinh từ schema sẽ có kiểu/tham số sai. **Sửa:** operation filter tách thành hai integer query đúng §0, xuất lại cả hai spec; assert ngữ nghĩa hai tham số và hai media type của 200. Hiện CSV filter giữ JSON đúng, nhưng test chưa canh regression này.

- **P2 — Vẫn nhận được khoá múi giờ thứ hai.** `src/LuxMap.Modules.Assets/AssetsModule.cs:28`: binder nhận cả `Assets:Warranty:TimeZone`; dòng 29 giữ giá trị đó khi thiếu `WorkOrders:Agenda:TimeZone`. Ví dụ khoá đầu = `UTC`, khoá sau vắng: bảo hành dùng UTC, agenda dùng mặc định VN, lệch ngày/trạng thái lúc 18:00 UTC. Trái W-2. **Sửa:** chỉ bind ngưỡng; luôn lấy zone từ khoá agenda hoặc mặc định chuẩn, không fallback về thuộc tính đã bind. Thêm test cấu hình này.

- **P2 — Guard CSV BE-28 chạy sau tải dữ liệu.** `src/LuxMap.Modules.Statistics/StatisticsController.cs:49` (tương tự dòng 80) gọi `CsvExport.Write` sau service. `StatisticsService.cs:83` đã tải hết nhóm, dòng 124 tải hết phiếu hoàn tất; chỉ báo cáo warranty kiểm trước tải ở dòng 207. Với `fixture-status?group_by=segment` có 10 001 nhóm, toàn bộ nhóm vào RAM rồi mới 413. **Sửa:** truyền chế độ export vào service, đếm số dòng báo cáo trước materialize; repair cần gom nhóm ở DB nếu muốn giới hạn bộ nhớ. Giới hạn tính trên hàng xuất, không trên số phiếu nguồn. Test helper hiện không chứng minh guard nằm trước truy vấn tải.

- **P3 — Test mép SQL chỉ dùng N=90.** `tests/LuxMap.Api.Tests/WarrantyTests.cs:141`: đổi riêng SQL khi N=0 thành `expiry > today` vẫn xanh; unit N=0 không chạm SQL. **Sửa:** integration filter từng trạng thái với N=0, hạn −1/0/+1, cột thiếu bóng và cột chỉ còn bóng đã tháo; đối chiếu ID/kết quả với `Classify`.

- **P3 — Chưa canh thứ tự hoà và phân trang.** `tests/LuxMap.Api.Tests/WarrantyTests.cs:127`, helper dòng 307 tạo từng cột riêng; bỏ `ThenBy(PoleId.Length)` ở `StatisticsService.cs:211` vẫn xanh. **Sửa:** hai ID `POLE-9999`/`POLE-10000` cùng transaction/hạn, assert thứ tự qua hai trang; thêm ca `created_at` khác nhau để canh thứ tự ưu tiên.

- **P3 — Test ngày vẫn phụ thuộc giờ chạy.** `tests/LuxMap.Api.Tests/WarrantyTests.cs:28` đóng băng `UtcNow`; đổi calendar sang UTC hoặc `NightOf` có thể xanh khi các ngày trùng nhau. **Sửa:** ghim `2026-10-11T18:00:00Z`, assert hôm nay VN là `2026-10-12`, phân biệt cả UTC lẫn đêm khảo sát.

## Standards

- **P3 — Thiếu hai đầu vào chống công thức.** `tests/LuxMap.Shared.Tests/CsvExportTests.cs:44`: xoá nhánh tab/CR trong `Defuse` vẫn xanh. **Sửa:** thêm chuỗi bắt đầu `\t`/`\r`, assert dấu nháy đơn và quote CR đúng RFC 4180.

## Kiểm chứng và giới hạn

✅ Đọc tĩnh: các bề mặt dùng `WarrantyWindow.Classify`; SQL dùng chính bounds của window, đúng cả N=0/null/bóng đã tháo. `Pole` và `Fixture` đều có query filter xã, join không bỏ filter; capability đúng `ReadStatistics`. Warranty đếm trước tải, null cuối, hoà `created_at,length(id),id`. CSV hiện đúng BOM/CRLF/quote/defuse và giữ số âm; `MakeReadOnly` chưa thấy rủi ro thực với cách dùng hiện tại, không có caller sửa options chung sau đó. So sánh JSON hai spec với baseline: không mất schema; 200 giữ JSON + CSV; `PoleProperties` viết tay đã có `warranty_status`.

⚠️ Không chạy Docker, build, `dotnet test` hay sabotage. Các nhận định sabotage là suy luận từ code/test; số 874/46/45/378 xanh, 2 skip MQTT do người dùng cung cấp, không xác minh lại.

Tổng: Spec **3 P2 + 3 P3** (đáng chú ý: schema phân trang); Standards **1 P3** (test tab/CR). Chỉ ghi file review, không cập nhật dashboard hay commit theo yêu cầu CHỈ ĐỌC.

## Đã xử lý (Claude, 12/10/2026)

| Phát hiện | Xử lý |
|---|---|
| P2 phân trang trong spec | **Có sẵn từ trước, không riêng BE-31**: mọi endpoint dùng `PageQuery` (`/assets/poles`, `/faults`, `/work-orders`…) xuất cùng một tham số object `page`. Theo OAS 3, object query `style: form, explode: true` tuần tự hoá thành `page=…&page_size=…` nên đúng trên dây, nhưng generator client có thể sinh kiểu lạ. Tách thành việc riêng ở `tracking.html` (Known issues), sửa một lần cho mọi endpoint |
| P2 khoá múi giờ thứ hai | **Sửa**: `WarrantyOptions.From` chỉ đọc `ExpiringWithinDays` từ `Assets:Warranty`; múi giờ luôn từ `WorkOrders:Agenda:TimeZone` hoặc mặc định. Test `A_zone_under_the_warranty_section_is_ignored_the_agenda_key_is_the_only_one` |
| P2 guard CSV BE-28 sau khi tải | **Giữ**, có lý do: output của `fixture-status` / `repair-timeliness` là số đã gộp, bị chặn bởi số nhóm (xã × tuyến × nguồn); 10 001 nhóm vào RAM là vài MB. Giới hạn 10 000 là giới hạn **file**, không phải bảo vệ bộ nhớ của truy vấn — truy vấn đó chạy y hệt ở đường JSON từ BE-28 |
| P3 mép SQL với N=0 | **Thêm** `With_a_zero_day_window_the_filter_still_agrees_and_a_retired_lamp_is_no_lamp` (−1/0/+1/null + cột chỉ còn bóng đã tháo). Phá thử `expiry > today` → 2 test đỏ |
| P3 thứ tự hoà / phân trang | **Thêm** `Ties_are_broken_by_id_length_then_id_and_paging_neither_repeats_nor_drops` (hai ID 9xxxx / 1xxxxx cùng transaction, qua hai trang). Lượt đầu chọn số sai nên phá thử vẫn xanh — đã sửa; phá thử bỏ `ThenBy(PoleId.Length)` → đỏ |
| P3 test ngày phụ thuộc giờ chạy | **Thêm** `WarrantyCalendarTests` (không host): ghim `2026-10-11T18:00Z` → VN `2026-10-12` (khác UTC) và `2026-10-12T04:00Z` → `2026-10-12` (khác "đêm" khảo sát). Host HTTP vẫn dùng "now" đóng băng vì JWT phải còn hiệu lực |
| P3 tab / CR | **Thêm** `A_cell_starting_with_a_tab_or_a_carriage_return_is_defused_and_the_cr_quoted` |

Sau khi sửa: 884 / 46 / 45 / 379 xanh, 2 skip MQTT, trên DB sạch.
