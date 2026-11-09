---
ticket: WO-AUTODISPATCH
title: Phân lịch kiểm tra tự động — POST /work-orders/auto-dispatch/preview (đề xuất, không ghi gì)
status: ready
phase: 2
owner: codex
branch: feat/WO-autodispatch
contract_refs:
  - "Chưa lên Contract — SELF-SIGNED. Mã drift: WO-AUTODISPATCH-1 (kiểm docs/contract-drift.md; dùng số kế tiếp nếu đã bị lấy)"
depends_on: []   # ghép với WO-BATCH (POST /work-orders/batch) ở phía FE; hai ticket KHÔNG phụ thuộc code của nhau
---

## Bối cảnh

FE (WP5) có nút "⚡ Phân lịch kiểm tra tự động" trên trang Lịch: Quản lý thấy bao nhiêu sự cố `detected` chưa có lịch, bấm nút, popup preview hiện các ca đề xuất
(người + ngày), Quản lý sửa rồi bấm "Duyệt tạo tất cả" (= `POST /work-orders/batch`, ticket `WO-BATCH`). **Thuật toán nằm ở backend** (một định nghĩa, test được; job lịch
khảo sát định kỳ ở ticket sau dùng chung hàm chọn kỹ sư). Mỹ chốt (09/11/2026) theo bản đặc tả của FE, **không phải D-item**:

**Endpoint:** `POST /api/v1/work-orders/auto-dispatch/preview` — body tuỳ chọn `{ "commune_id": "COM-001" }` (vắng / body rỗng = mọi xã trong phạm vi của người gọi). Capability **`ManageWorkOrders`** có sẵn (KHÔNG thêm capability). **Không ghi gì**: không phiếu, không audit, không thông báo, không khoá; chạy hai lần cùng trạng thái ⇒ cùng kết quả (xác định, không ngẫu nhiên). Dùng `POST` chỉ vì có body — ghi vào drift.

**Ứng viên:** sự cố `fault_status = detected` mà **chưa gắn phiếu mở** (không có `work_order_fault` với `released_at IS NULL`). Chỉ `detected` (không `confirmed`/`in_progress`). Đọc qua query filter (ngoài phạm vi xã ⇒ không thấy). Tối đa **2000** ứng viên (sắp theo mức nghiêm trọng, rồi `detected_at`); phần dư vào `skipped` với `reason = "LIMIT_EXCEEDED"`.

**Gom nhóm (A):**
- Nhóm theo **(`commune_id`, `segment_id`)**. Sự cố **không có `segment_id`** ⇒ không tự xếp, trả trong `skipped` (`reason = "NO_SEGMENT"`) để Quản lý xếp tay.
- Đơn vị đếm là **cột phân biệt**: các sự cố cùng `pole_id` luôn nằm chung một ca (một cột có thể có hai sự cố, ví dụ `lamp_out` + `lamp_dim`); sự cố cấp đoạn không có `pole_id` mỗi cái đếm như một đơn vị riêng. Mỗi ca **tối đa 8 đơn vị** (`MaxPolesPerOrder`), nhiều hơn thì tách theo thứ tự **dọc tuyến** (cột 1–8 → ca 1, 9–16 → ca 2, …): dùng đúng thứ tự `position` mà `GET /work-orders/{id}/poles` dùng (`WorkOrderPoleService`; **tái dùng hoặc tách hàm dùng chung**, không chép phép chiếu hình học); đơn vị không có cột xếp cuối theo `fault_id` (length, id).
- **Tiêu đề** `Kiểm tra {n} cột sự cố - {SegmentName}` với `n` = số đơn vị trong ca; khi một tuyến tách nhiều ca thì thêm hậu tố ` (ca {i}/{k})` (spec không có hậu tố nhưng hai ca trùng tên là vô dụng). `note` = `"Phân lịch tự động"`.

**Ngày (B) — CHẠY CẢ 7 NGÀY, KHÔNG nhảy T7/CN:** ngày khởi điểm = **đêm hiện tại + 2 ngày**, trong đó "đêm hiện tại" = `WorkOrderAgendaOptions.NightOf(now)` (cùng múi `Asia/Ho_Chi_Minh` và mốc 12:00 với agenda — `scheduled_date` là ngày buổi tối). `due_date = scheduled_date`. Không xử lý ngày lễ.

**Chọn kỹ sư và hạn ngạch (C):** kỹ sư đủ điều kiện = `field_engineer`, không khoá, thuộc xã của nhóm (đúng `EligibleUsers` của `WorkOrderService` — **tách thành hàm dùng chung**, đừng chép).
- **Tải** của một kỹ sư = số phiếu **không bị `cancelled`**, thuộc loại **bất kỳ** (inspection / repair / survey — một buổi tối của kỹ sư là một tài nguyên), có `assigned_to` = người đó và `scheduled_date` trong khoảng; cộng các ca đề xuất **đã xếp trong chính lượt này**. Phiếu không có `scheduled_date` không tính.
- Với mỗi ca (xét theo thứ tự ưu tiên ở dưới), duyệt ngày `d` từ ngày khởi điểm tăng dần; ở mỗi ngày, chỉ xét kỹ sư có **số ca trong ngày `d` < `QuotaPerDay` (mặc định 2)**; trong số đó chọn: **(1) ít ca nhất trong tuần chứa `d`** (tuần **thứ Hai–Chủ Nhật**), **(2) ít ca nhất trong ngày `d`**, **(3) `user_id` nhỏ hơn** (so sánh theo `length`, rồi chuỗi — không `ORDER BY` ID trần). Chưa ai còn chỗ ⇒ sang ngày `d + 1`. Đặt vòng lặp an toàn (ví dụ ≤ 366 ngày) và ghi rõ nếu chạm.
- **Thứ tự xếp ca khi quota hạn chế:** ca có sự cố **mức nghiêm trọng cao nhất** trước (`critical > high > medium > low`, hạng tường minh dạng `Expression`/hàm, KHÔNG sắp chữ cái của enum lưu dạng chữ), hoà thì `priority_score` lớn nhất (null = thấp nhất), hoà thì `detected_at` sớm nhất, hoà thì `segment_id` (length, id), hoà thì số ca. Ca nhẹ bị đẩy sang ngày kế tiếp khi hết quota.
- **Xã không có kỹ sư đủ điều kiện:** vẫn trả ca đề xuất với `assigned_to = null`, `scheduled_date` = ngày khởi điểm, và `unassigned_reason = "NO_ELIGIBLE_ENGINEER"` (Quản lý chọn tay). Không bỏ ca.
- **Quota và cân bằng tải chỉ là GỢI Ý**: `POST /work-orders/batch` không kiểm lại (Quản lý được đổi người, ngày). Đừng thêm kiểm tra quota vào `Create`.
- Cấu hình `WorkOrders:AutoDispatch` (`LeadDays` = 2, `QuotaPerDay` = 2, `MaxPolesPerOrder` = 8), validate lúc khởi động (> 0) như `WorkOrderAgendaOptions`; đọc bằng `appsettings.json` mặc định + test ghi đè bằng factory. **Không** đưa vào `system_setting`/DB.

**Response 200 (snake_case):**

```json
{
  "start_date": "2026-11-11",
  "fault_count": 7,
  "order_count": 2,
  "proposals": [
    {
      "task_kind": "inspection",
      "title": "Kiểm tra 4 cột sự cố - Tuyến A",
      "scheduled_date": "2026-11-11",
      "due_date": "2026-11-11",
      "assigned_to": "USR-0004",
      "fault_ids": ["FAULT-0001", "…"],
      "note": "Phân lịch tự động",
      "commune_id": "COM-001", "segment_id": "SEG-001", "segment_name": "Tuyến A",
      "assigned_to_name": "Nguyễn Văn A", "pole_ids": ["POLE-0001", "…"],
      "pole_count": 4, "max_severity": "high", "unassigned_reason": null
    }
  ],
  "skipped": [ { "fault_id": "FAULT-0009", "reason": "NO_SEGMENT" } ]
}
```

Sáu trường đầu (`task_kind … note`) có **đúng hình dạng một phần tử `items[]` của `POST /work-orders/batch`** — FE có thể gửi nguyên (kèm sửa người/ngày) hoặc chọn đúng sáu trường đó; phần còn lại chỉ để hiển thị. Thứ tự `proposals` = thứ tự ngày rồi thứ tự xếp. `fault_ids` trong ca sắp theo thứ tự dọc tuyến.

## Phải đọc trước

- `CLAUDE.md` (lõi) + `docs/agents/traps/faults-workorders.md` + `docs/agents/traps/spatial-map.md` (thứ tự dọc tuyến) + `docs/agents/traps/testing.md` + `docs/agents/traps/commune-scope.md`
- `src/LuxMap.Modules.WorkOrders/{WorkOrderService,WorkOrdersController,WorkOrderAgenda,WorkOrderPoles,WorkOrderRules,WorkOrdersModule}.cs`, `Entities/WorkOrder.cs`
- `src/LuxMap.Modules.Faults` (entity `Fault`, `FaultQueryService.SeverityRank`, `FaultStatusSets`) — mẫu hạng severity tường minh
- `tests/LuxMap.Api.Tests/WorkOrderTests.cs`, `AccountTestFactory.cs` (`FakeTimeProvider` để cố định "bây giờ"), `WorkOrderAgendaOptionsTests.cs`

## Yêu cầu

1. **Cấu trúc:** thuật toán là **lớp thuần** (không DB) nhận ứng viên + tải hiện có + danh sách kỹ sư, trả ca đề xuất — để test đơn vị đầy đủ, và để job định kỳ (ticket sau) dùng lại phần chọn kỹ sư / tải. Lớp truy vấn DB riêng đọc ứng viên, kỹ sư, tải. Đặt trong `LuxMap.Modules.WorkOrders` (file mới `AutoDispatch*.cs`), đăng ký trong `WorkOrdersModule`. Không đưa logic vào controller. Hàm chọn kỹ sư / bảng tải là **một lớp riêng, công khai trong module** (ví dụ `EngineerLoadBoard`) — job sau gọi lại, đừng viết nó thành chi tiết riêng của preview.
2. **Controller:** thêm action `[HttpPost("auto-dispatch/preview")]` trong `WorkOrdersController`, `[Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]`, `ProducesResponseType` 200/400/401/403. `commune_id` ngoài phạm vi ⇒ 403 `COMMUNE_FORBIDDEN` qua `CommuneFilter.Narrow` (như các endpoint khác); `commune_id` rỗng/`null` JSON ⇒ coi như vắng; kiểu sai ⇒ 400 `VALIDATION_FAILED`.
3. **Cố định thời gian:** dùng `TimeProvider` + `WorkOrderAgendaOptions.NightOf` (đừng `DateTime.UtcNow`).
4. **Test** — đơn vị cho lớp thuần (không cần DB, nhanh, nhiều ca) **và** tích hợp (`tests/LuxMap.Api.Tests/WorkOrderAutoDispatchTests.cs`, tiền tố xã/tài khoản riêng, `AccountTestFactory` hoặc cách tương đương để cố định đồng hồ; teardown theo `testing.md`). Assert **giá trị cụ thể**:
   - kịch bản đặc tả: 7 sự cố (Tuyến A 4 cột, Tuyến B 3 cột), 2 kỹ sư tải bằng nhau ⇒ 2 ca, mỗi ca một kỹ sư khác nhau, **cùng ngày khởi điểm** (= đêm hiện tại + 2), tiêu đề `Kiểm tra 4 cột sự cố - …` / `Kiểm tra 3 cột sự cố - …`;
   - tách ca: 8 cột ⇒ 1 ca; 9 ⇒ 2 ca (8 + 1, thứ tự dọc tuyến, hậu tố `(ca 1/2)`, `(ca 2/2)`); 16 ⇒ 8 + 8; 17 ⇒ 3 ca; **hai sự cố cùng cột đếm một đơn vị** và không bao giờ tách khỏi nhau (kể cả ở ranh giới 8/9);
   - **cuối tuần không bị bỏ qua:** đồng hồ giả để ngày khởi điểm rơi vào Thứ Bảy và Chủ Nhật ⇒ `start_date` đúng ngày đó, ca vẫn xếp trong cuối tuần;
   - hạn ngạch: 3 kỹ sư × 2 = 6 ca/ngày, ca thứ 7 sang ngày kế; **ca có `critical` được ngày sớm, ca `low` bị đẩy** khi quota thiếu (trồng giá trị theo thứ tự mà sắp chữ cái sẽ lộ sai); hoà severity thì theo `priority_score`;
   - tải hiện có: kỹ sư đã có 2 phiếu (kể cả loại `survey` / `repair`) hôm đó bị bỏ qua; kỹ sư có nhiều phiếu trong tuần hơn bị xếp sau; phiếu `cancelled` **không** tính; phiếu của tuần Thứ Hai–Chủ Nhật khác **không** tính; hoà ⇒ ít ca trong ngày ⇒ `user_id` (ID hai bên ngưỡng độ rộng, ví dụ `USR-9999` / `USR-10000` — tạo bằng sequence, không ghim literal);
   - lọc: chỉ `detected` (không `confirmed`/`rejected`/…); sự cố đã gắn phiếu mở bị loại, đã `released_at` thì lại là ứng viên; sự cố không `segment_id` ⇒ `skipped` `NO_SEGMENT`; kỹ sư khoá hoặc xã khác không được chọn; xã không có kỹ sư ⇒ ca có `assigned_to = null` + `NO_ELIGIBLE_ENGINEER`; nhiều xã (người gọi `*`) ⇒ ca theo từng xã, không trộn xã;
   - **không ghi gì**: sau preview, số dòng `work_order`, `work_order_fault`, `audit_event`, `notification` không đổi; hai lần gọi liên tiếp cho cùng kết quả (so toàn bộ JSON);
   - 401 không token; 403 `ROLE_FORBIDDEN` với `field_engineer` và `superior`; `manager` và `system_admin` được; `commune_id` ngoài phạm vi ⇒ 403 `COMMUNE_FORBIDDEN`; `commune_id` kiểu số ⇒ 400;
   - **liên thông với batch** (nếu `WO-BATCH` đã có trong cây; nếu chưa, bỏ qua và ghi vào results): lấy sáu trường đầu của từng ca đưa vào `/work-orders/batch` ⇒ 201.
5. **Phá thử** (ghi vào results "chưa chạy, Claude chạy", kèm đúng dòng): (a) bỏ chặn T7/CN ngược lại — thêm nhảy sang Thứ Hai ⇒ test cuối tuần đỏ; (b) đổi `QuotaPerDay` kiểm `<=` thay vì `<` ⇒ test 3×2 đỏ; (c) bỏ thứ tự severity (sắp chữ cái) ⇒ test critical/low đỏ; (d) bỏ lọc `cancelled` khỏi tải ⇒ test cancelled đỏ; (e) tuần tính Chủ Nhật–Thứ Bảy thay Thứ Hai–Chủ Nhật ⇒ test ranh giới tuần đỏ; (f) bỏ nhóm theo cột phân biệt (đếm theo sự cố) ⇒ test ranh giới 8/9 đỏ; (g) lọc `ReleasedAt == null` → bỏ ⇒ test gắn phiếu đỏ; (h) thêm ghi (ví dụ lưu gì đó) ⇒ test "không ghi gì" đỏ nếu có thể.
6. **Tài liệu:** `docs/contract-drift.md` dòng WO-AUTODISPATCH-1 (SELF-SIGNED 09/11/2026): endpoint + `POST` chỉ vì có body, ứng viên, gom nhóm, tách 8, **cả 7 ngày**, quota 2 (cấu hình), tuần Thứ Hai–Chủ Nhật, tải tính mọi loại phiếu trừ `cancelled`, thứ tự severity, hậu tố ca, `skipped`, `NO_ELIGIBLE_ENGINEER`, quota chỉ là gợi ý (batch không kiểm), hình dạng sáu trường khớp `items[]`; việc FE phải làm (hiện popup, gửi batch). `docs/agents/traps/faults-workorders.md`: bẫy ngắn. `appsettings.json` thêm mục `WorkOrders:AutoDispatch`. `tracking.html` thêm thẻ. **KHÔNG** sửa `docs/api-contract-v1.1.md`, **KHÔNG** sửa `docs/openapi/*`.
7. Ghi `.ai/results/WO-AUTODISPATCH.md` ngắn: đã làm gì, lệnh Claude cần chạy, điều chưa chắc.

Tiêu chí xong:

- [ ] `dotnet build LuxMap.slnx` không cảnh báo mới
- [ ] Test ở mục 4 có mặt (Codex không chạy được test; Claude chạy `dotnet test` + phá thử)
- [ ] Endpoint mới nằm trong ma trận `ManageWorkOrders`; `CapabilityPolicyCoverageTests` xanh; không thêm capability

## KHÔNG ĐƯỢC làm

- Không migration, không đổi schema, không thêm capability, không thêm giá trị enum, không đổi field đã publish.
- Không ghi DB, không phát thông báo, không audit trong preview. Không `ExecuteUpdate`.
- Không nhảy T7/CN. Không kiểm quota trong `Create` / batch. Không đọc `system_setting`.
- Không chép `EligibleUsers` hay phép chiếu thứ tự cột — tách dùng chung.
- Không đổi hành vi `GET /work-orders`, `POST /work-orders`, agenda (test hiện có không đổi).
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*`; không chạm project ngoài WorkOrders + test + tài liệu (cần sửa `Faults` / `Shared` thì ghi vào results và giữ ở mức nhỏ nhất).
- Không chạy git, `dotnet test`, swagger, `dotnet ef` (sandbox không có quyền).
- Không tự chọn phương án khác với các mục "Mỹ chốt"; điểm mơ hồ → ghi vào results thành câu hỏi, đừng đoán.
