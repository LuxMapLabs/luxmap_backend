---
ticket: LUX-PHOTO
title: Gắn số đo lux với ảnh cột — `photo_id` trên POST /lux-readings, lux trên PolePhotoItem
status: done
phase: 2
owner: claude
branch: feat/LUX-PHOTO-link
contract_refs:
  - "mục 5.7 — Số đo lux"
  - "mục 0 — bảng mã lỗi"
  - "mục 5.8 — sync push (loại lux_reading dùng chung body)"
depends_on: []
---

## Bối cảnh

WP6 (mobile) thêm bước đo lux tuỳ chọn ngay sau khi chụp ảnh / quay video cột trong phiếu, bằng máy đo cầm
tay ESP32 qua Bluetooth (`meter_model = "LuxMap_ESP32"`, `data_source = "field"`). Hiện `POST /lux-readings`
không có trường ảnh, nên chỉ ghép lux với ảnh được qua `pole_id` + thời gian — dễ nhầm khi một cột có nhiều ảnh.
Phase 1 (khảo sát) đã làm xong trong phiên Claude; **Mỹ chốt D-1…D-9 ngày 24/11/2026** — phase này hiện thực đúng
các quyết định đó, không thiết kế lại.

## Quyết định đã chốt (24/11/2026, Mỹ · SELF-SIGNED)

- **D-1** Trường mới `photo_id` (chuỗi, **không bắt buộc**, `MaxLength(32)`) trên `CreateLuxReadingRequest`. Là
  `photo_id` của `WorkOrderPolePhoto` (`PPH-000123`) — ảnh hoặc video cột. Không gửi → hành vi y như cũ.
- **D-2** Thứ tự kiểm tra trong `LuxReadingService.CreateAsync` (trong service, để `/sync/push` kiểm như nhau):
  trùng `client_op_id` → 200 bản ghi cũ (giữ nguyên, không so `photo_id`); cột → `404 POLE_NOT_FOUND`; rồi ảnh:
  - ảnh không có, **đã rút (withdrawn)**, hoặc ngoài phạm vi xã → **`404 POLE_PHOTO_NOT_FOUND`** (mã đã dùng ở
    `WorkOrderPolePhotoService`; đưa vào `ErrorCodes` nếu chưa có hằng);
  - ảnh thuộc cột khác `pole_id` → **`400 PHOTO_POLE_MISMATCH`** (mã mới, `ErrorCodes`);
  - ảnh đã có một lux khác gắn → **`409 PHOTO_LUX_ALREADY_LINKED`** (mã mới).
- **D-3** **Mỗi ảnh tối đa một lux.** Partial unique index `ux_lux_reading_photo_id` trên `photo_id WHERE photo_id IS NOT NULL`.
  Đua hai request cùng lúc: bắt `23505` theo tên constraint đó → 409 (giống cách bắt `ux_lux_reading_client_op_id`).
- **D-4** FK **ghép** `(photo_id, pole_id, commune_id)` → `work_order_pole_photo (photo_id, pole_id, commune_id)`,
  `Restrict`, không navigation — đúng khuôn `FaultOriginPolePhotoConfiguration` (alternate key phía principal đã có).
  DB tự chặn ảnh khác cột / khác xã. Khai trong module Survey (Survey đã reference WorkOrders).
- **D-5** `LuxReadingResponse` và `LuxReadingWithLuminanceResponse` thêm `photo_id` (string?, **luôn emit**, `null` khi
  không gắn). Đặt ngay sau `pole_id`.
- **D-6** `PolePhotoItem` thêm `lux_id`, `lux_value`, `lux_measured_at` (nullable, luôn emit). Đọc **live** (không
  snapshot) qua **port mới khai ở WorkOrders** (ví dụ `IPhotoLuxLookup`, một truy vấn theo lô cho nhiều `photo_id`, tránh
  N+1 ở list), **Survey hiện thực**, đăng ký trong `SurveyModule` — khuôn `ISurveyFindingLookup`. Chỉ lux gắn bằng
  `photo_id`; **không** đoán theo thời gian. Mọi chỗ trả `PolePhotoItem` (upload ảnh, upload video, list, detail, review,
  decide, replay) phải mang giá trị đúng.
- **D-7** Rút ảnh (withdraw) **không** bị chặn và **không** đụng lux: lux là ground truth RQ1, giữ nguyên `photo_id`.
- **D-8** Migration thêm cột `photo_id text NULL`, FK ghép, index unique partial. `Down()` đối xứng (xoá index, FK, cột).
  Không gì khác trong `Up()`.
- **D-9** Contract lên **v1.23** (dev đã ở v1.22; ticket viết nhầm v1.19) (SELF-SIGNED, chạm bề mặt API → chưa ổn định tới FW kế tiếp): §5.7 thêm hàng `photo_id`
  + 3 mã lỗi; mô tả `lux_value` = số đọc tương đối từ cảm biến điện thoại **hoặc máy đo cầm tay** (module ESP32 của
  nhóm, `meter_model = "LuxMap_ESP32"`); `meter_model` = model **thiết bị đo**; **chỉ so số đo cùng `meter_model`**
  (D-R15: cùng máy, cùng quy trình). Response §5.7 thêm `photo_id`. Mô tả `PolePhotoItem` (tìm mục ảnh cột trong phiếu)
  thêm 3 trường lux. Bảng mã lỗi mục 0 thêm 2 mã mới (+ `POLE_PHOTO_NOT_FOUND` nếu chưa có). Changelog dòng v1.19.
  Drift: một mục mới trong `docs/contract-drift.md` (tiền tố `LUX-PHOTO-1…`) ghi các quyết định trên, ghi rằng yêu cầu
  WP6 mở lại D-R27 (D-R27 từng nói không cần thêm trường cho `POST /lux-readings`), và ghi rõ: server **không** dùng
  `LuxReading` để phân loại `dim` (dim từ sweep, D-R20) — `photo_id` phục vụ ghép cặp cho đối chiếu RQ1 / CV-12.

## Phải đọc trước

- `CLAUDE.md` (= `AGENTS.md`) — đặc biệt mục Bẫy chung, Phạm vi xã, Test.
- `docs/agents/traps/survey.md` (bốn quy tắc BE-42), `migrations.md`, `testing.md`, `commune-scope.md`.
- `docs/api-contract-v1.1.md` §5.7, §5.8, bảng mã lỗi mục 0, changelog mục 10.
- Code: `src/LuxMap.Modules.Survey/LuxReadings/*`, `Entities/LuxReading.cs`, `Configurations/LuxReadingConfiguration.cs`,
  `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoContracts.cs`, `WorkOrderPolePhotoService.cs` (hàm `Item`, `Photos()`,
  `NotWithdrawn()`), `WorkOrderPolePhotoVideoUpload.cs`, `ISurveyFindingLookup.cs`, `Configurations/WorkOrderPolePhotoConfiguration.cs`,
  `src/LuxMap.Modules.Survey/SurveyModule.cs`, `src/LuxMap.Modules.Sync/SyncPushService.cs` (nhánh lux_reading),
  `src/LuxMap.Shared/Contracts/Errors/ErrorCodes.cs`.
- Test: `tests/LuxMap.Api.Tests/LuxReadingTests.cs`, `SyncTests.cs`, `OpenApiSpecTests.cs` (schema `PolePhotoItem`), test ảnh
  cột hiện có (grep `pole-photos`) để mượn fixture tạo phiếu + ảnh.

## Yêu cầu

1. Hiện thực D-1…D-8 (code + migration `AddLuxReadingPhotoLink` qua `dotnet ef migrations add`, đọc lại `Up()`/`Down()`).
2. Test tích hợp mới (trong class/collection đúng theo `testing.md`), tối thiểu:
   - gắn ảnh hợp lệ → 201, response có `photo_id`; `GET /lux-readings/poles/{id}` và `GET /lux-readings` trả `photo_id`;
   - không gửi `photo_id` → 201, `photo_id: null` (hành vi cũ);
   - ảnh không tồn tại → 404 `POLE_PHOTO_NOT_FOUND`; ảnh đã rút → 404; ảnh ở xã ngoài phạm vi → 404;
   - ảnh của cột khác (cùng xã) → 400 `PHOTO_POLE_MISMATCH`;
   - lux thứ hai cùng ảnh, `client_op_id` khác → 409 `PHOTO_LUX_ALREADY_LINKED`; cùng `client_op_id` → 200;
   - `PolePhotoItem` (detail và list) mang `lux_id/lux_value/lux_measured_at` sau khi gắn, `null` trước đó;
   - đường `/sync/push` loại `lux_reading` với `photo_id` sai cột bị từ chối cùng mã.
   - `OpenApiSpecTests`: assert `photo_id` trên schema lux và 3 trường lux trên `PolePhotoItem`.
3. D-9: sửa Contract + drift như mô tả. Rồi xuất OpenAPI theo `.ai/context/commands.md` (swagger tofile → generator).
   Nếu sandbox không chạy được lệnh nào thì ghi rõ lệnh nào chưa chạy.
4. Ghi `.ai/results/LUX-PHOTO-p2.md`: file đã đổi, output `dotnet build` thật, lệnh nào chưa chạy được.

Tiêu chí xong:

- [ ] `dotnet build --disable-build-servers` xanh, 0 warning mới.
- [ ] Migration chỉ chứa cột + FK + index của D-8; `Down()` đối xứng.
- [ ] Test mới viết xong (Claude sẽ chạy — sandbox không có Docker).

## KHÔNG ĐƯỢC làm

- **Không commit, không push**, không đổi branch.
- Không đổi trường / enum / mã lỗi đã có; không đổi tính bắt buộc của trường nào (kể cả `meter_model`).
- Không thêm ghép lux–ảnh theo thời gian; không dùng lux để phân loại `dim` hay đổi trạng thái cột.
- Không thêm project reference mới (WorkOrders **không** reference Survey — dùng port).
- Không `ExecuteUpdate` / `ExecuteDelete`; không chặn hay sửa luồng withdraw ảnh.
- Không ghim ID bằng literal trong test; không chạy test trên `luxmap_dev`.
- Không đụng `tracking.html` (Claude cập nhật).
- Gặp chỗ D-item ở trên không trả lời được → ghi vào results thành câu hỏi, chọn phương án ít thay đổi nhất, đừng tự mở rộng phạm vi.
