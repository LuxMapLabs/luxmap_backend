---
ticket: POLE-SEGMENT-NULL
title: PR 2b — lớp bảo vệ nghiệp vụ cho cột chưa thuộc tuyến (phiếu, ảnh cột, dời cột, sự cố, khảo sát)
status: in_progress
phase: 2
owner: codex (viết) · claude (review + commit + chạy test)
branch: feat/POLE-SEGMENT-NULL-2b-guards
contract_refs:
  - "docs/contract-drift.md — mục POLE-SEGMENT-NULL (D-1…D-14, đã có từ PR 2a)"
depends_on:
  - "PR 2a đã merge (#198, 8d14caf): pole.segment_id đã nullable ở DB và entity; API tạo cột vẫn BẮT BUỘC tuyến"
---

## Bối cảnh

Sau 2a, một cột chưa tuyến chỉ tồn tại nếu ghi thẳng qua `DbContext`/SQL — chưa đường API nào tạo ra nó (nút bật là PR 2e). PR 2b làm cho
những chỗ NGHIỆP VỤ đã dùng `pole.SegmentId` xử lý đúng cột null **trước khi** nút bật tồn tại, theo D-1, D-5, D-7, D-8 của
`.ai/tasks/POLE-SEGMENT-NULL-p2.md` (đọc bảng quyết định ở đó, ĐỪNG mở lại). Phase 1 đã khảo sát đầy đủ: `.ai/results/POLE-SEGMENT-NULL-p1.md`
(§1 bảng consumer, §4 quy tắc, §6 kế hoạch test T04, T05, T09, T11); 2a liệt kê chỗ còn dùng `!` ở `.ai/results/POLE-SEGMENT-NULL-p2a.md` §4.

Rủi ro chính, đã xác minh bằng code: `WorkOrderPoleService.Targets` (`WorkOrderPoles.cs:79`) viết `w.SegmentId == p.SegmentId`. Với `WorkOrder.SegmentId`
nullable và `Pole.SegmentId` nullable, EF sinh so sánh coi `NULL = NULL` là đúng ⇒ một phiếu kiểm tra/sửa chữa không có tuyến sẽ kéo **mọi cột
chưa tuyến của xã** vào danh sách cột và vào quyền upload ảnh. Build và test vẫn xanh nếu không dựng cột null để kiểm.

## Phải đọc trước

`CLAUDE.md` (= `AGENTS.md`), `.ai/README.md`, `.ai/tasks/POLE-SEGMENT-NULL-p2.md` (bảng D-1…D-14 + nguyên tắc), `.ai/results/POLE-SEGMENT-NULL-p1.md`
§1, §4, §6, `docs/agents/traps/faults-workorders.md`, `survey.md`, `commune-scope.md`, `testing.md`, `assets.md` (mục dời cột),
`tests/LuxMap.Api.Tests/PoleSegmentNullableTests.cs` (cách dựng cột null qua `DbContext` + `EnterUnscopedSystemWriteBackdoor`).

## Yêu cầu — PR 2b (làm đúng các mục, không hơn)

1. **D-1 — `Targets` không còn `null == null`.** Sửa `WorkOrderPoleService.Targets` để hai nhánh theo tuyến chỉ khớp khi `p.SegmentId` **khác null**
   (`p.SegmentId != null && (…)`); nhánh theo sự cố (`WorkOrderFault`) giữ nguyên. Một predicate duy nhất, dùng chung cho danh sách cột và upload ảnh
   (`WorkOrderPolePhotoService.cs:45`). Không viết lại predicate ở chỗ khác.
2. **D-1 — `GET /work-orders/{id}/poles`.** Cột chưa tuyến chỉ xuất hiện khi là target qua sự cố của phiếu. Chúng có `segment_id: null`, `position: null`,
   xếp **nhóm cuối**, theo `created_at, length(pole_id), pole_id` (không `ORDER BY` ID trần). `position` của cột có tuyến giữ nguyên ý nghĩa
   (thứ tự dọc đường). `WorkOrderPole.SegmentId` thành `string?`, `Position` thành `int?`; sinh lại OpenAPI **chỉ** cho schema này
   (`WorkOrderSchemaFilter` có xử lý `segment_id`; kiểm xem nó có ép non-null không). Phân trang/tổng giữ đúng (`total` gồm cả nhóm chưa tuyến).
   Survey vẫn không nhận cột chưa tuyến.
3. **D-5 — dời cột chưa tuyến** (`AssetCrudService` quanh dòng 715–760; `.SingleAsync` trên tuyến của cột sẽ nổ 500 khi `SegmentId = null`).
   Cột chưa tuyến: cho dời, **không** đo hành lang, **không** `POLE_OFF_SEGMENT`; audit `Relocated` không dựng `segment_distance_m` giả (bỏ hẳn trường
   hoặc `null`, chọn một cách và test nó; `segment_id` trong audit là `null`). Cột có tuyến: hành vi y hệt cũ (giữ test hiện có).
4. **D-7 — sự cố / ảnh duyệt / citizen accept** với cột chưa tuyến: `Fault.SegmentId` (đã nullable) nhận `null` từ cột, giữ xã / vị trí / nguồn / audit
   như service gốc; không đòi client gửi tuyến hay vị trí khi cột có toạ độ. Gỡ `!` tạm của 2a ở những chỗ thuộc 2b và thay bằng xử lý thật. Kiểm các
   điểm: `FaultReportService.cs:143,207`, `ReviewedPolePhotoPublisher.cs:61`, `SurveyReviewService.cs:307`, `CitizenReviewService` (luồng accept ra fault).
   Gán tuyến về sau KHÔNG sửa fault/history/cluster/snapshot cũ (không viết gì để làm việc đó).
   *Không đổi DTO đọc của citizen/QR/`PoleMapDetail`… (thuộc 2c, D-6).*
5. **D-8 — khảo sát.** Xác nhận bằng test rằng `SurveyChainageQuery.Poles` (INNER JOIN `p.segment_id = r.segment_id`) không bao giờ trả cột chưa tuyến, kể cả cột
   đứng sát hành lang; phiếu khảo sát chỉ nhận `segment_ids`. Chỉ sửa code nếu test cho thấy lọt. **`fault_cluster` hiện KHÔNG có nơi nào ghi**
   (grep `FaultCluster`: chỉ PrefixedId, cấu hình, migration) ⇒ không có đường để "từ chối cluster nhận cột null"; ĐỪNG dựng writer. Ghi vào kết quả thành mục
   "để CV-15" và đề xuất câu chữ cho `traps/faults-workorders.md`.
6. **Các chỗ khác trong `WorkOrderService` / `WorkOrderAgenda` / `WorkOrderPolePhotoService` còn đọc `SegmentId` của cột:** rà từng hit trong bảng P1 §1
   (WorkOrders), sửa chỗ nào có `null == null`, `.Single()` trên tuyến, hoặc `Contains(segmentId)` làm cột null biến mất khỏi công việc được giao qua sự cố.
   Chỗ nào KHÔNG sửa (vì thuộc 2c–2e hoặc an toàn) thì ghi file:dòng + lý do.
7. **Mã lỗi `POLE_SEGMENT_REQUIRED` (D-14): CHỈ thêm nếu 2b có chỗ thật sự cần từ chối** vì cột chưa tuyến. Theo khảo sát hiện tại có thể KHÔNG có (D-1 hiện
   cột, D-5 cho dời, D-7 cho tạo fault, D-8 lọc). Đừng thêm hằng số/`ErrorCodes`/docs cho mã chưa có chỗ dùng. Nếu không dùng, ghi rõ trong kết quả
   rằng mã sẽ vào 2c (D-3 cabinet topology) — đó là PR đầu tiên có nơi dùng.
8. **Test (Testcontainers PostGIS qua `LuxMapTestHost`; không `luxmap_dev`)**, dựng cột null qua `DbContext`:
   - `Work_order_poles_excludes_unrelated_unassigned_poles_and_lists_fault_targets_last` — phiếu inspection KHÔNG tuyến + một cột null không liên quan trong
     cùng xã ⇒ KHÔNG thấy nó; cột null có sự cố của phiếu ⇒ thấy, `segment_id`/`position` null, xếp cuối, thứ tự `created_at, length(pole_id), pole_id` kiểm bằng
     ID hai bên ngưỡng độ rộng (`PrefixedIdOrderingTests` làm mẫu), tổng/phân trang đúng.
   - `Work_order_pole_photo_uses_the_same_unassigned_target_policy` — upload ảnh: cột null không liên quan bị từ chối như cột ngoài phiếu; cột null là target qua sự cố
     thì được; thay tuyến/cột trong lúc upload vẫn bị kiểm lại dưới khoá.
   - `Relocating_unassigned_pole_follows_explicit_policy` — dời không 500, không `POLE_OFF_SEGMENT`, audit không có khoảng cách tuyến; cột có tuyến vẫn bị
     `POLE_OFF_SEGMENT` khi quá hành lang (giữ test cũ).
   - `Fault_on_unassigned_pole_keeps_pole_location_commune_and_source` — `POST /faults` có `pole_id`, citizen accept, ảnh cột confirm: fault ra với `segment_id` null, xã/nguồn
     từ cột, replay `client_op_id`, audit đúng.
   - `Survey_never_targets_unassigned_poles` — cột null sát tuyến không vào snapshot/danh sách cột khảo sát; phiếu khảo sát giữ nguyên hành vi.
   - Test hiện có giữ kỳ vọng; chỉ đổi test nào ghim `segment_id`/`position` non-null của `WorkOrderPole`.
   - **Phá thử bắt buộc** cho từng chốt: bỏ `p.SegmentId != null` ⇒ test "unrelated" ĐỎ; ghép lại `SingleAsync` trên tuyến ⇒ test dời cột ĐỎ; `Position = 0` thay `null` ⇒ test vị trí ĐỎ;
     v.v. Khôi phục bằng `cp`/`touch`, đọc **mã thoát** của `dotnet test`. Test phải đỏ vì ĐÚNG lý do (đọc thông báo lỗi), không vì host không dựng được.
9. **Tài liệu:** `docs/contract-drift.md` mục POLE-SEGMENT-NULL: thêm dòng trạng thái "PR 2b đã làm: D-1, D-5, D-7, D-8 (khảo sát); D-14 chưa dùng" (không đổi bảng D-1…D-14);
   `docs/agents/traps/faults-workorders.md`: bẫy `null == null` trong `Targets` và quy tắc nhóm cuối; `survey.md`/`assets.md` chỉ khi có bẫy MỚI đã xác minh.
   OpenAPI: sinh lại bằng generator theo `.ai/context/commands.md`, KHÔNG sửa tay; lint.
10. Build / test: `MSBUILDDISABLENODEREUSE=1 dotnet build --disable-build-servers` rồi `dotnet test`. Nếu sandbox không chạy được Docker / test, nói thẳng trong kết quả
    — Claude sẽ chạy lại. Đừng báo xanh khi chưa chạy.

Tiêu chí xong PR 2b:

- [ ] Build 0 warning / 0 error
- [ ] `dotnet test` mã thoát 0 (hoặc báo rõ phần chưa chạy được)
- [ ] 5 test mới ở mục 8, mỗi test có bằng chứng phá thử đỏ đúng lý do (lệnh + output)
- [ ] `git diff origin/dev --stat` chỉ chạm: WorkOrders (Targets/Poles/Photo, và Agenda/Service nếu mục 6 yêu cầu), Assets (dời cột), Faults/Survey/Citizen (chỉ gỡ `!` và xử lý null), DTO `WorkOrderPole`, OpenAPI sinh, test, drift, traps, `.ai/results/POLE-SEGMENT-NULL-p2b.md`
- [ ] `results/POLE-SEGMENT-NULL-p2b.md`: từng hit rà ở mục 6 (sửa / không sửa + lý do), output build/test/phá thử thật, mục "để CV-15" (cluster), D-item mới nếu có

## KHÔNG ĐƯỢC làm

- Không mở API tạo cột chưa tuyến: POST/PUT/import vẫn BẮT BUỘC tuyến (nút bật là 2e). Không bộ lọc `unassigned_segment`.
- Không làm 2c–2e: không đổi DTO đọc của assets/map/topology/thống kê/lighting/citizen/QR/sync; không sync bundle; không `segment_name` null.
- Không migration, không đổi schema, không bảng/cột mới. Không `fault_cluster` writer.
- Không thêm mã lỗi chưa có chỗ dùng. Không tuyến giả, không suy tuyến gần nhất, không tự gán tuyến.
- Không sửa `docs/api-contract-v1.1.md`, không `tracking.html` (Claude cập nhật sau review). Không sửa tay `docs/openapi/*.json`.
- Không chạy trên `luxmap_dev`. Không `git commit` (sandbox không ghi `.git` của worktree — Claude commit sau review), không `git push`, không `git add -A`/`git add .`.
- Không `ExecuteUpdate`/`ExecuteDelete`; không project reference ngược chiều; không `ORDER BY` ID trần.
- Chỗ mơ hồ → D-item ở cuối file kết quả, không tự chọn. Không mở lại D-1…D-14.
