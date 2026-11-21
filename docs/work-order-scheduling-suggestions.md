# Gợi ý phân lịch cho Quản lý

WO-SCHEDULING-SUGGESTIONS · 19/11/2026 · additive, SELF-SIGNED, chờ WP5/WP6 xác nhận.

## API

`GET /api/v1/work-orders/suggestions` trả `{generated_at, night_of, groups[]}`. Mỗi nhóm gồm
`kind, total, ready_count, in_work_order_count, blocked_count, pole_count, segment_count`.
Cả năm nhóm luôn có, kể cả khi rỗng. Số cột/tuyến là số ID khác nhau, không phải số sự cố.

`GET /api/v1/work-orders/suggestions/items?kind=…` trả phân trang chuẩn `{page,page_size,total,items[]}`.
`kind` (query bắt buộc): `fault_inspection`, `fault_repair`, `missing_gps`, `survey_needed`, `overdue`.
Mỗi item có `id, kind, state, commune_id, segment_id, segment_name, segment_has_geometry, pole_id, external_ref, note, fault_id,
fault_type, fault_status, severity, data_source, work_order_id, work_order_title, assigned_to, wo_status, recommended_task_kind, blocking_reason,
observed_at, scheduled_date, due_date`. Trường không áp dụng là null. `work_order_id` trỏ phiếu hiện có,
không phải một phiếu được tạo khi GET.
Thông tin phiếu được tải theo lô cho trang hiện tại để FE hiện người được giao/trạng thái mà không gọi từng phiếu.
Ở nhóm overdue, pole_count là 0 vì item là phiếu; segment_id là tuyến đại diện (tuyến đầu của survey),
hoặc tuyến đã chọn ở bộ lọc. Lọc segment_id vẫn xét mọi tuyến/target của phiếu, không chỉ tuyến đại diện.

Cả hai endpoint: capability `ManageWorkOrders` (chỉ manager). Bộ lọc `commune_id` lặp, `segment_id`,
`data_source` CSV; mặc định **field**. Xã ngoài quyền → 403 `COMMUNE_FORBIDDEN`; nguồn / kind sai →
400 `VALIDATION_FAILED`. `page/page_size` theo quy ước chung, page_size tối đa 200. Chi tiết thêm
`state=ready|in_work_order|blocked` (tuỳ chọn). Các bộ lọc không phụ thuộc tháng đang mở trên lịch.

## Nguồn và quy tắc

| kind | Một item / điều kiện | Việc gợi ý |
|---|---|---|
| fault_inspection | Một fault detected | inspection |
| fault_repair | Một fault còn mở, đủ điều kiện repair theo WorkOrderRules | repair |
| missing_gps | Một pole.geom null, kể cả chưa gán tuyến | pole_gps_update |
| survey_needed | Một cột đã có GPS, trạng thái công bố thiếu hoặc unknown | survey |
| overdue | Một phiếu thoả WorkOrderOverdueJob.OverdueOn(đêm hiện tại) | Mở phiếu để xử lý, không tạo phiếu mới |

Fault dùng loại hiệu lực (`override_fault_type ?? fault_type`). Một cột có thể có nhiều sự cố; warning đèn hỏng
phải đọc cả số sự cố và pole_count. Không biến unknown thành lamp_out, không suy thiếu quan sát là hỏng.
Nguồn các warning fault theo fault; GPS/survey theo cột. Overdue lọc nguồn theo đối tượng của phiếu:
fault nếu là inspection/repair có fault, pole target nếu GPS, cột trên tuyến (hoặc tuyến rỗng) nếu survey/inspection tuyến.
Một phiếu nhiều nguồn xuất hiện một lần trong mỗi lượt lọc; không cộng các lượt lọc để suy tổng.

`state=in_work_order` khi fault/pole đang được giữ (`released_at IS NULL`), hoặc tuyến của cột unknown đã có
phiếu survey cùng xã chưa verified/cancelled. Với survey, work_order_id là phiếu tạo sớm nhất trong các phiếu
phù hợp. Overdue luôn trỏ chính phiếu quá hạn. Phiếu done chờ duyệt vẫn giữ mục tiêu, tránh giao lại sớm.

Nếu chưa có phiếu giữ, `state=blocked` khi thiếu điều kiện; `blocking_reason`:
`POLE_SEGMENT_REQUIRED`, `SEGMENT_NOT_ACCESSIBLE`, `CROSS_COMMUNE_REFERENCE` (GPS phải cùng xã tuyến).
Các tên/hình học tuyến ngoài scope không được lộ. Tuyến chưa vẽ **vẫn giao khảo sát/GPS được** theo RG/PG;
`segment_has_geometry=false` giúp FE nhắc vẽ tuyến trước khi nộp sweep (ingest mới yêu cầu hình). Các mục còn lại là ready. Chỉ ready mới có recommended_task_kind.

Mỗi nhóm sắp mức nghiêm trọng giảm dần (fault), mốc cũ trước, rồi độ dài ID và ID. Overdue sắp hạn trước.
Mỗi response đọc cùng một snapshot DB; không ghi audit/notification. Gợi ý không giữ chỗ: POST vẫn kiểm lại và
có thể trả 409 nếu người khác đã nhận việc. Không thêm ngưỡng "lâu chưa khảo sát" chưa được chốt.
Fault/GPS có chốt giữ mục tiêu ở command hiện có. Survey vẫn cho nhiều phiếu cùng tuyến vào các đêm khác nhau;
`in_work_order` là thông tin để tránh giao lặp trên UI, không phải khoá độc quyền tuyến hay cơ chế đặt chỗ.

## Tích hợp Web và Mobile

Vào màn Phân lịch → tải summary → bấm nhóm → tải chi tiết (mặc định UI có thể chọn state=ready).
Giữ trang lịch và nút tạo thủ công. Form lấy targets từ gợi ý, để manager chọn title, kỹ sư và ngày:

- inspection/repair: `POST /work-orders` với task_kind và fault_ids (cùng xã, tối đa 200).
- pole_gps_update: cùng POST với segment_id và pole_ids (cùng tuyến/xã, tối đa 200).
- survey: cùng POST với commune_id và segment_ids (duy nhất, có thứ tự). Một nhóm nhiều cột cùng tuyến chỉ gửi tuyến một lần.
- overdue / in_work_order: mở work_order_id hiện có. blocked: hiển thị lý do và điều hướng bổ sung dữ liệu tài sản.

Sau tạo/duyệt/huỷ → tải lại summary và trang chi tiết. Không giới hạn cảnh báo theo tháng của calendar.
GPS thu thập từ giấy/Excel: import cột chưa GPS bằng luồng hiện có → giao pole_gps_update → kỹ sư complete với
pole_locations → manager verify mới ghi GIS. Offline tiếp tục qua sync bundle/push hiện có; API gợi ý chỉ dành cho manager online.
Chức năng mới không cần migration, không đổi task_kind, không thay đổi hình dạng các endpoint cũ.

## Handoff WP5 — đối chiếu FE ngày 21/11/2026

Đã đọc `luxmap-web` và đối chiếu các file phân lịch với `origin/dev b1ad915`.
FE **đã có** TaskKind `survey` / `pole_gps_update`, form thủ công, chọn cột GPS, PDF QR,
chọn sự cố, saga tạo/duyệt phiếu và mở chi tiết qua query `?work_order=<work_order_id>` (`NOTIFICATION_TARGET_PARAMS.workOrder`).
Chưa có consumer cho hai API gợi ý. Không cần thư viện, migration hay một workflow GPS khác.
Các đường dẫn trong bảng dưới thuộc repo **luxmap-web**, không phải backend.

| File | Việc cần làm |
|---|---|
| `scripts/generate-types.js` → `src/types/workorders/orders.ts`, `src/types/common/enums.ts` | Chạy generator từ spec của nhánh BE này để có `SchedulingSuggestionSummary`, `SchedulingSuggestionItem`, `SchedulingSuggestionGroup`, `SchedulingSuggestionItemPagedResult`, hai enum kind/state. Không sửa tay `src/types`; không thêm lại TaskKind GPS/survey đã có. Kiểm nullable ở runtime, nhất là `recommended_task_kind` chỉ có ở ready. |
| `src/feature/work-schedule/workScheduleAPI.ts` | Thêm hai hàm GET qua `apiClient`: `/work-orders/suggestions` và `/work-orders/suggestions/items`. Dùng kiểu sinh; query `commune_id` là key lặp (không `commune_id[]`), `data_source` là CSV; `kind` bắt buộc cho items, `state` chỉ cho items. Giữ POST auto-dispatch/preview hiện có. |
| `src/feature/work-schedule/workScheduleSlice.ts`, `workScheduleSaga.ts` | State/actions và worker cho summary, items, bộ lọc, trang, loading/error riêng. Đổi kind/state/xã/tuyến/nguồn thì reset trang và bỏ selection cũ; bỏ response cũ khi đổi lọc nhanh. Không lấy lỗi tải dữ liệu làm số 0. |
| `src/pages/work-schedule/WorkSchedulePage.tsx` | Vào màn tải summary theo địa bàn; render năm nhóm hoặc gộp hai nhóm fault thành một khối “Sự cố cần xử lý” có hai tab. Lấy số từ summary, bấm nhóm tải items phân trang, hiển thị ba trạng thái và CTA tương ứng. Gợi ý không gửi bộ lọc tháng của lịch. Reuse `prefillContext`, form hiện có và đường mở chi tiết bằng `?work_order=<work_order_id>`, kể cả phiếu ngoài tháng đang xem; phiếu chưa có scheduled_date cần fallback mở detail trực tiếp. |
| `src/pages/work-schedule/components/CreateManualModal.tsx`, `FaultSelector.tsx`, `GpsPoleSelector.tsx` | Prefill đúng IDs, giữ chúng qua lúc assets tải xong; không thu gọn nhiều fault cùng cột thành một fault. Tuyến prefill không có trong danh sách phải báo lỗi, không chọn tuyến đầu thay thế. Cho inspection dùng fault IDs hợp lệ kể cả không có pole; chỉ kiểm pole/segment khi đúng chế độ tạo theo cột/tuyến. Tách selection GPS theo cùng tuyến/xã; giữ giới hạn 200 targets và yêu cầu PDF QR của form hiện tại. |
| `src/feature/work-schedule/workScheduleSaga.ts` + callbacks thành công ở page | Sau tạo/follow-up/batch, cập nhật trạng thái, duyệt hoặc huỷ: refresh summary và trang items đang mở. Gặp 409: giữ form, báo lỗi API, tải lại gợi ý/targets để manager chọn lại; không tự gửi lại tạo phiếu. |

Từ repo FE có thể sinh kiểu bằng spec local đã kiểm:

```bash
npm run gen -- /Users/nhm809/Documents/LuxMap/wt-scheduling-suggestions/docs/openapi/luxmap-v1.5.json
```

Sau khi BE merge, thay đường dẫn bằng file spec của revision đã merge; FE trỏ API tới môi trường đã
**deploy** revision đó mới gọi được hai route. Mở PR đơn thuần chưa đưa API lên môi trường FE.
Không chạy generator lấy spec từ checkout BE cũ vì sẽ thiếu schema mới.

### Quy tắc nối form cần giữ

1. `ready` và `recommended_task_kind != null`: mới mở thao tác tạo. `in_work_order`/`overdue`:
   mở phiếu sẵn có. `blocked`: giải thích lý do, dẫn tới sửa dữ liệu tài sản; không gọi POST tạo.
2. Fault: lấy đủ `fault_ids` của các dòng đã chọn, cùng xã. Form hiện tại dùng một tuyến;
   UI có thể giới hạn selection theo tuyến để tái sử dụng nguyên form. Nếu hỗ trợ nhiều tuyến cùng xã,
   cần sửa form tương ứng; không bỏ bớt sự cố âm thầm. `PoleSelector` hiện ánh xạ một fault/cột và tính lại fault IDs khi toggle;
   `FaultSelector` reset selection khi mount/đổi tuyến và chỉ lấy trang đầu 50 fault. Cần giữ đúng
   IDs từ gợi ý và hiện các dòng đã chọn dù không nằm trong trang selector hiện tại.
   Một fault không có cột/tuyến vẫn có thể hợp lệ.
3. Với fault IoT không có `pole_id`, DTO gợi ý không mang cabinet/node/feeder. Nếu dùng UI đích IoT,
   lấy `faultAPI.getFaultDetail(fault_id)` ở `src/feature/faults/faultAPI.ts`, map
   `cabinet_id`, `origin_node_id`, `feeder_id`, `iot_anomaly_code` vào `initialIotTarget` hiện có.
   Không giả lập pole/tuyến để vượt điều kiện form. Fault không cột mà không phải IoT cũng phải
   được gửi bằng `fault_ids`, không ép vào form đích IoT.
4. GPS: một phiếu = một `segment_id` + `pole_ids` cùng tuyến/xã (≤ 200). Phiếu done chưa verified
   vẫn giữ cột, chưa giao lại. GPS thiếu tuyến là blocked, cần gán tuyến trước.
5. Survey: chọn cột chỉ để xác định tuyến; payload gửi `commune_id` của gợi ý và `segment_ids`
   duy nhất. Form hiện tại chỉ một tuyến, có thể tạo từng tuyến. Muốn gộp nhiều tuyến phải mở rộng
   props/payload/form; giữ thứ tự manager chọn và không gửi `pole_ids`.
   Không dùng xã đầu tiên của tài khoản nếu manager đang chọn gợi ý thuộc xã khác.
6. Chỉ khi `segment_has_geometry === false` mới nhắc vẽ tuyến trước khi nộp sweep, không disable giao khảo sát/GPS.
   Hình tuyến phải được xác nhận theo luồng tài sản. Saga GPS hiện tại có đoạn tự nối/cập nhật tuyến
   sau verify; WP5 cần đối chiếu quy tắc tuyến chưa hình trong `docs/agents/traps/assets.md` trước khi tái sử dụng (xem lưu ý bên dưới).
7. Nguồn mặc định field; demo simulated phải chọn nguồn rõ ràng. `unknown` là cần khảo sát, không
   gọi là hỏng. Import `initial_status=out/dim` của demo chỉ tạo trạng thái, **không tạo fault**;
   muốn kiểm thẻ sự cố phải có fault hợp lệ. Không thay bằng lọc màu cột trên bản đồ.
   Hai nhóm fault bao gồm cả IoT/sự cố tuyến: dùng nhãn “Sự cố cần xử lý”, không gọi toàn bộ tổng
   này là số bóng hỏng. API hiện chưa lọc `fault_type`; không lọc mỗi trang rồi báo là tổng số đèn hỏng.
8. Summary là nguồn số đếm cho khối cảnh báo. Badge hiện tại dùng `loadUnassignedFaults` chỉ đọc
   trang đầu 100 detected rồi đếm ở client; không tái sử dụng con số đó làm tổng backlog mới.
   Nếu đổi badge auto-dispatch sang số ready, nhãn phải nói rõ đây là số gợi ý, không phải số ca
   planner chắc chắn xếp được. Giữ logic planner quyết định kỹ sư/ngày/khả dụng qua preview hiện có.

**Lưu ý FE sẵn có, không phải thay đổi của BE này:** `verifyWorkOrderWorker` trong
`workScheduleSaga.ts` có bước tự dựng `LINESTRING` từ cột khi tuyến chưa hình, rồi gọi
`assetAPI.updateSegment`. Quy tắc tuyến chưa hình ở `docs/agents/traps/assets.md:477` ghi
“Không bao giờ nối cột đầu–cuối, tâm xã hay hình suy ra cho chạy được” (RG-1).
WP5 cần tách việc xác nhận hình tuyến khỏi nghiệm thu GPS trước khi dùng trọn luồng trên tuyến chưa hình;
BE suggestion vẫn cho giao việc đúng quy định. Chưa sửa FE hay chạy ứng dụng FE trong lượt review này.

Đã thử generator FE nguyên bản trong thư mục tạm với consolidated spec này: sinh đủ DTO/enum đúng
file; typecheck riêng `workorders/orders.ts` cùng enum bằng TypeScript hiện có pass. Không ghi file
vào repo FE. Đây không thay thế build/test hay nghiệm thu UI của FE. Kiểu sinh có nullable `$ref`
không biểu diễn đầy đủ giá trị null: luôn kiểm `state`, ID và task kind trước khi thao tác.

### Checklist nghiệm thu FE

- Manager thấy đủ nhóm/kể cả 0; vai trò khác không gọi API manager. Kiểm lỗi tải/retry và đổi địa bàn nhanh.
- Hơn 200 dòng: tổng đúng, phân trang đúng, không đếm theo rows hiện có. Một cột nhiều fault hiện đủ IDs.
- Tạo inspection detected, repair confirmed, inspection fault không pole; request khớp form và API.
- Tạo GPS theo gợi ý → complete offline/online → manager verify → warning thiếu GPS biến mất;
  done chưa verify vẫn hiện in_work_order. Không tự vẽ hình tuyến chưa có.
- Survey nhiều cột cùng tuyến chỉ gửi một segment; tuyến chưa hình vẫn giao được và cảnh báo ingest.
- blocked không tạo; in_work_order/overdue mở đúng phiếu ngoài tháng. 409 do manager khác nhận việc
  giữ form và refresh gợi ý. Tạo/huỷ/duyệt xong cập nhật cả summary lẫn items.
- Chuyển field/simulated đúng dữ liệu, unknown tách fault; demo initial_status không gây kỳ vọng fault giả.
- Chạy build/typecheck, lint và test FE theo repo (`npm run build`, `npm run lint`, `npm run test`).
  Các lệnh FE này **chưa chạy** vì lượt này chỉ review và lập handoff, không sửa FE.

## Triển khai và rollback

Đưa commit này lên cùng quy trình deploy API hiện có; không chạy migration hay nạp lại dữ liệu. FE chỉ gọi hai
route mới sau khi API đã được cập nhật. Rollback bằng revert commit / deploy binary trước: chỉ hai route mới
không còn; dữ liệu tài sản, phiếu và các API cũ giữ nguyên, không cần rollback database.
