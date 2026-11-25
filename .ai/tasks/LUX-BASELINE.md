---
ticket: LUX-BASELINE
title: Nối số lux đo đứng yên (gắn ảnh cột trong phiếu khảo sát) vào baseline và phân loại dim
status: ready
phase: 1
owner: unassigned
branch: feat/LUX-BASELINE
contract_refs:
  - "mục 5.7 — Số đo lux"
  - "mục 5.2 — chi tiết cột (luminance_history, luminance_baselines)"
  - "mục ảnh cột trong phiếu — PolePhotoItem, POST .../pole-photos/{photo_id}/decision"
  - "mục 5.8 — sync push (loại lux_reading)"
  - "mục 0 — bảng mã lỗi"
depends_on:
  - "D-R29 (docs/contract-drift.md, 25/11/2026) — đo lux đứng yên ngay dưới đèn"
  - "D-1 … D-14 của ticket này — Mỹ chốt sau Phase 1"
---

## Bối cảnh

D-R29 (25/11/2026) chuyển kênh đo độ sáng sang **thiết bị đứng yên, ngay dưới đầu đèn, đo từng cột**. Phần thu thập đã chạy
trên app và web: trong phiếu khảo sát, kỹ sư chụp ảnh / quay clip cột, đo lux và gắn số đo vào ảnh bằng `photo_id` (LUX-PHOTO).
AI cho ON/OFF, kỹ sư review, Quản lý quyết định (`POST /work-orders/{id}/pole-photos/{photo_id}/decision`). Ví dụ: màn
WO-0037, 25/11.

Nhưng số lux **chưa được dùng để kết luận gì**:

- `LuxReading` chỉ được lưu lại, không tạo baseline, không ra `normal` / `dim`. `nearest_luminance` luôn `null`.
- Ảnh được quyết ON thì **giữ nguyên** `dim` / `normal` cũ (`ReviewedPolePhotoPublisher`, `SurveyPublicationRules.PhotoStatus`).
  Không ghi lịch sử, không ghi baseline. Vì vậy phiếu chỉ phát hiện được đèn **tắt**, không bao giờ phát hiện được đèn **mờ**.
- Baseline hiện có (`luminance_baseline`, `baseline_member`, `luminance_history`) gắn cứng vào sweep: `sweep_id` và `run_id`
  bắt buộc, `baseline_member.observation_id` → `pole_observation`, `direction IN ('forward','reverse')`.

Ticket này nối số lux đo theo quy trình vào baseline của chính cột và chấm `dim` lúc Quản lý quyết định ảnh.
Bên chờ: **WP6** (trường quy trình trên số đo, mẫu thô, màn đo lại tại chỗ), **WP5** (hiện tỉ lệ / kết luận trên ảnh, chi tiết
cột, tóm tắt phiếu), **FO** (buổi đo B bằng tấm lọc ND, D-R23).

## Luồng đích

```text
Kỹ sư (phiếu khảo sát, tại cột)
  chụp ảnh / clip cột                    → POST .../pole-photos            (có sẵn)
  đo lux trên sào, cửa sổ vài giây       → POST /lux-readings
      photo_id + protocol{samples, sensor_height_m, module_id, firmware…}  (MỚI: protocol)
      server: kiểm profile, tính trung vị + độ dao động cửa sổ
              lệch profile / không ổn định → 400, app đo lại tại chỗ
  review nhãn ON/OFF                     → PATCH .../review                 (có sẵn)
Quản lý
  quyết định ảnh                         → POST .../decision                (có sẵn, đổi hành vi)
      OFF → out, lamp_out                                                   (như cũ)
      ON + có lux theo quy trình:
          baseline của cột (chỉ gồm đêm TRƯỚC) có  → tỉ lệ → normal | dim  (MỚI)
              dim → sự cố lamp_dim                                          (MỚI)
          chưa có baseline → giữ hành vi cũ, lý do baseline_missing
      lux hợp lệ (ON, normal hoặc chưa có baseline, không tấm lọc)
          → thành viên baseline (mỗi cột mỗi đêm một)                       (MỚI)
          → đủ BaselineMinimumMembers → baseline phiên bản mới = trung vị   (MỚI)
```

## Đề xuất thiết kế — D-item, Mỹ chốt sau Phase 1

Mỗi mục ghi **khuyến nghị**. Agent thực thi không tự chọn khác. Phase 1 kiểm các giả định ghi trong từng mục.

- **D-1 · Nguồn ON/OFF** (D-R29 O-1): lấy từ **ảnh / clip cột đã được Quản lý quyết định**, không lấy từ video sweep.
- **D-2 · Đơn vị duyệt** (D-R29 O-2): quyết định ảnh của Quản lý **là** bước duyệt số lux gắn với ảnh đó. Không thêm endpoint
  "phiên đo" mới. Chỉ ảnh thuộc **phiếu khảo sát** (`task_kind = survey`) mới được chấm dim và vào baseline. Ảnh ở phiếu
  kiểm tra / sửa chữa: số lux chỉ là đối chứng. *Phase 1: kiểm ảnh cột có ở những loại phiếu nào.*
- **D-3 · Hai loại số đo:** số lux **có `protocol`** (D-4) là số đo theo quy trình, mới được chấm và vào baseline. Không có
  `protocol` (lux kế cầm tay, số cũ, số đo ngày 25/11 trên WO-0037) là **đối chứng**, giữ nguyên hành vi hiện tại. Không thêm enum.
- **D-4 · Trường mới trên `POST /lux-readings`** (tuỳ chọn, chỉ cộng thêm): object `protocol` gồm:
  - `samples` (mảng số lux của cửa sổ đo, tối thiểu `LuxProtocol:MinSamples`);
  - `sensor_height_m`;
  - `module_id` (mã thiết bị do nhóm dán);
  - `module_firmware_version_id` (registry BE-34, `lux_module_firmware`);
  - `optical_filter_transmittance` (`null` = không tấm lọc; 0 < x ≤ 1).

  Server tính trung vị và độ dao động `(max − min) / trung vị` của `samples`. `lux_value` client gửi phải bằng trung vị
  (sai số 1%), nếu không thì 400 `LUX_SAMPLES_MISMATCH`. Mẫu thô lưu **nguyên văn** (bảng con hoặc `jsonb`, chốt ở Phase 1).
  Mọi số thực có CHECK chặn NaN / ±Infinity (`migrations.md`).
- **D-5 · Profile quy trình và từ chối:** cấu hình `LuxProtocol` gồm `SensorHeightM`, `SensorHeightToleranceM`, `MinSamples`,
  `MaxWindowSpread` (đề xuất 0,05). Theo nguyên tắc "không nhận, không suy đoán":
  - độ cao ngoài dung sai → 400 `LUX_PROTOCOL_MISMATCH`;
  - thiếu mẫu hoặc dao động quá ngưỡng → 400 `LUX_WINDOW_UNSTABLE`.

  Lỗi trả ngay để app **đo lại tại chỗ**. Kiểm tra nằm trong **service**, để `/sync/push` kiểm như nhau. Cấu hình nằm ở
  `system_setting` (qua `SystemSettingValues`) hay appsettings: chốt ở Phase 1 theo luật một định nghĩa.
- **D-6 · "Đêm khảo sát":** ngày địa phương (UTC+7) của `measured_at` **lùi 12 giờ**, vì phiên đêm cắt qua nửa đêm (không tính
  theo ngày lịch). Mỗi cột mỗi đêm tối đa **một** thành viên baseline: nếu có nhiều, lấy số đo **được quyết định trước**.
  Định nghĩa đặt **một chỗ** và gọi lại ở mọi nơi dùng.
- **D-7 · Lưu baseline ở đâu** (mục kiến trúc chính):
  - **(A)** Mở rộng `luminance_baseline` / `baseline_member` / `luminance_history`: thêm `method`, cho `direction`, `sweep_id`,
    `run_id` nullable, thêm `lux_id` loại trừ với `observation_id`. Đụng bảng bất biến, FK ghép và nhiều CHECK
    (bẫy FK `MATCH SIMPLE` với cột null, `survey.md` BE-15 P2c).
  - **(B)** Bảng **song song** cho kênh đứng yên:
    - `stationary_baseline` (cột, xã, bóng, phiên bản, giá trị, số thành viên, `member_cv`, dấu vân tay quy trình,
      phiên bản thuật toán);
    - `stationary_baseline_member` (`lux_id`);
    - `lux_evaluation` (một dòng cho mỗi số đo được chấm: tỉ lệ, `classified_as`, `baseline_id`, `dim_threshold_ratio`,
      `reason_codes`, phiên bản thuật toán).

  **Khuyến nghị (B)**. Đỉnh lux khi chạy xe và số đo đứng yên **không được trộn** vào cùng một baseline; (B) bảo đảm điều đó
  ngay từ cấu trúc bảng, và không đụng bất biến của pipeline sweep (D-R29 O-5 còn mở). Công thức trung vị và điều kiện hợp lệ
  là **hàm dùng chung**, không chép lại.
- **D-8 · Chấm lúc Quản lý quyết định ảnh** (thay luật "ON giữ nguyên" **chỉ khi** có lux theo quy trình):
  - nhãn hiệu lực **OFF** → `out`, `lamp_out`, như cũ;
  - **ON** + lux theo quy trình + có baseline hợp lệ (cùng bóng, cùng dấu vân tay quy trình, mọi thành viên thuộc **đêm trước**)
    → `tỉ lệ = lux / baseline`, nhỏ hơn `dim_threshold_ratio` → `dim`, ngược lại → `normal`. Công bố qua
    `PoleStatusPublication`, kể cả khi đổi `dim` thành `normal`: số đo có tỉ lệ phản bác được dim, ảnh ON trần thì không;
  - **ON** + chưa có baseline → hành vi cũ, lý do `baseline_missing`;
  - `dim` → sự cố `lamp_dim`, `confirmed`. `source_channel`: `cv` khi nhãn từ AI, `field_report` khi kỹ sư đổi nhãn (theo khuôn
    `lamp_out` từ ảnh). Severity lấy từ `SeverityFor(LampDim)`. Chống trùng theo loại hiệu lực + `FaultStatusSets.Open`, dưới khoá
    cột. **Không** tự đóng sự cố khi số đo trở lại `normal`.

  Thứ tự khoá **work_order → pole → photo** và một audit mỗi `SaveChanges` như hiện tại.
- **D-9 · Dựng baseline:** ngay sau khi chấm, số đo là thành viên hợp lệ khi đủ các điều kiện:
  - có `protocol`, nhãn ON;
  - kết luận `normal` hoặc `baseline_missing`;
  - `optical_filter_transmittance` là `null`;
  - đo sau ngày lắp bóng hiện tại;
  - cùng `data_source` với cột.

  Tính lại trung vị trên **mọi** thành viên của bóng + dấu vân tay quy trình (`module_id` + `sensor_height_m`). Đủ
  `SurveyReview:BaselineMinimumMembers` (mặc định 3) thì ghi phiên bản mới, có danh sách thành viên. Đổi bóng hoặc đổi dấu vân
  tay thì baseline bắt đầu lại.
- **D-10 · Lux đến sau khi ảnh đã được quyết định:** gắn `photo_id` của ảnh đã quyết → 409 `PHOTO_ALREADY_DECIDED`, để số đo không
  âm thầm nằm ngoài baseline. App đẩy lux **cùng lô** `/sync/push` ngay sau ảnh. Web cảnh báo ảnh trong phiếu khảo sát chưa có lux
  trước khi Quản lý quyết định. *Phase 1: kiểm thứ tự xử lý trong một lô sync.*
- **D-11 · Kiểm độ lặp lại (NFR CV ≤ 10%):** baseline lưu `member_cv`. Nếu `member_cv > LuxProtocol:MaxBaselineCv` (0,10) thì
  không chấm dim (`dim_evaluation_eligible = false`, lý do `baseline_unstable`), kết luận giữ như khi chưa có baseline.
- **D-12 · Lượt thử tấm lọc ND (D-R23 B):** số đo có `optical_filter_transmittance` vẫn được chấm (ghi `lux_evaluation` kèm tỉ lệ
  kỳ vọng = độ truyền) nhưng **không** công bố trạng thái cột, **không** sinh sự cố, **không** vào baseline.
  `data_source` vẫn `field`; lượt thử nhận ra bằng trường tấm lọc, không bằng `data_source` (D-R25 còn chờ).
- **D-13 · Bề mặt API — chỉ cộng thêm, không đổi trường đã publish:**
  - response lux (`§5.7`) thêm `protocol` (đọc lại) và `evaluation` (`null` tới khi ảnh được quyết);
  - `PolePhotoItem` thêm `lux_baseline_ratio`, `lux_classified_as`, `lux_reason_codes`;
  - chi tiết cột thêm **khoá riêng** `stationary_luminance_history[]` và `stationary_luminance_baseline`. **Không** cho
    `PoleMapHistoryPoint.sweep_id` thành nullable;
  - `ISurveyFindingLookup.FindingsAsync` phát lại được kết luận `dim` từ ảnh, để tóm tắt phiếu và phiếu nối tiếp mang
    `lamp_dim`.
- **D-14 · Contract và drift:** Contract lên version kế tiếp (SELF-SIGNED, chạm bề mặt API, ổn định ở FW kế tiếp): §5.7, chi tiết
  cột, `PolePhotoItem`, các mã lỗi mới. Drift `LUX-BASELINE-1…` ghi các quyết định trên, đóng D-R29 O-1 / O-2 / O-4, và ghi rõ
  `nearest_luminance` vẫn `null` (đối chứng A là việc khác).

## Phải đọc trước

- `CLAUDE.md` (= `AGENTS.md`): Quy ước API, Enum, Phạm vi xã, Bẫy chung, Test, Quy tắc dễ sai âm thầm.
- `docs/contract-drift.md`: **D-R29** (đầu file), D-R20, D-R22, D-R23, Quyết định khảo sát 07/10 (D-05), LUX-PHOTO-1…5, PP-1…PP-15.
- `docs/agents/traps/survey.md` (bốn quy tắc BE-42, BE-15 P2c FK ghép, POLE-PHOTO-AI, PHOTO-REVIEW, BE-34),
  `migrations.md`, `commune-scope.md`, `faults-workorders.md`, `testing.md`.
- `docs/api-contract-v1.1.md` §5.7, §5.2 (chi tiết cột), mục ảnh cột trong phiếu, §5.8, mục 0.
- Code:
  - `src/LuxMap.Modules.Survey/LuxReadings/*`, `Entities/LuxReading.cs`, `Configurations/LuxReadingConfiguration.cs`;
  - `Review/ReviewedPolePhotoPublisher.cs`, `Review/SurveyPublicationRules.cs` (`PhotoStatus`, `Eligible`, `Members`,
    `Median`, `SeverityFor`), `Review/PoleStatusPublication.cs`, `Review/SurveyBaselineLookup.cs`,
    `Processing/Frames/FrameClassification.cs`, `Entities/SurveyPublication.cs`, `Configurations/SurveyPublicationConfigurations.cs`;
  - `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs` (`DecideAsync`, khoá), `IPolePhotoPublication.cs`,
    `ISurveyFindingLookup.cs`;
  - `src/LuxMap.Modules.Map/Features/PoleDetailService.cs`, `PoleMapDetail.cs`;
  - `src/LuxMap.Modules.Sync/SyncPushService.cs` (nhánh `lux_reading`);
  - `src/LuxMap.Persistence/Versions/ArtifactVersion.cs` (`ArtifactRegistry`, `LuxModuleFirmware`),
    `src/LuxMap.Shared/Versions/VersionedResultColumn.cs`, `src/LuxMap.Persistence/Settings/SystemSetting.cs` (`SystemSettingValues`);
  - `scripts/copy_dev_to_supabase.py` (`PLAN`).
- Test: `LuxReadingTests.cs`, `WorkOrderPolePhotoLuxTests.cs`, `WorkOrderPolePhotoReviewTests.cs`, `SurveyPublicationTests.cs`
  (Api + Shared), `SyncTests.cs`, `OpenApiSpecTests.cs`, `VersionedResultColumnTests`.

## Phase 1 — khảo sát (không sửa code)

Ghi `.ai/results/LUX-BASELINE-p1.md`, dán output thật:

1. Ảnh cột có ở những `task_kind` nào. Quyết định ảnh có kiểm loại phiếu không (D-2).
2. Thứ tự xử lý một lô `/sync/push` có ảnh + lux, và lux có đến được trước quyết định không (D-10).
3. Schema thật của `lux_reading`, `luminance_baseline`, `baseline_member`, `luminance_history`, `pole_current_status`
   (`\d+`), để chốt D-7 (A) hay (B) bằng bằng chứng, không bằng trí nhớ.
4. `DecideAsync`: thứ tự khoá, transaction, chỗ gọi `IPolePhotoPublication`, các save và audit. Chỉ ra chính xác chỗ cắm D-8 / D-9.
5. `FindingsAsync` và tóm tắt phiếu ("Không phát hiện cột mất sáng") lấy kết luận từ đâu (D-13).
6. `dim_threshold_ratio` và `BaselineMinimumMembers` đang đọc ở đâu. Đề xuất chỗ cho `LuxProtocol:*` (D-5).
7. Đếm trên `luxmap_dev` (và VPS nếu Mỹ cho, chỉ đọc): số `lux_reading` có `photo_id`, `meter_model` của chúng. Xác nhận chưa có
   số nào đủ điều kiện quy trình (D-3).
8. Danh sách D-item còn mơ hồ hoặc mâu thuẫn với code. **Dừng hẳn, chờ Mỹ chốt.**

## Phase 2 — yêu cầu (sau khi chốt)

1. Hiện thực D-1…D-13 đã chốt: entity, configuration, migration (đọc `Up()` / `Down()` trước khi apply, `Down()` đối xứng), service,
   response, port.
2. Bảng mới: `ICommuneScoped` + `HasCommuneReference()`, FK `Restrict`, `IImmutableRecord` cho baseline / thành viên / đánh giá,
   `IAudited`, khai `VersionedResultColumn`, thêm vào `PLAN` của `scripts/copy_dev_to_supabase.py`.
3. D-14: Contract + drift. Xuất OpenAPI → generator → lint theo `.ai/context/commands.md`.
4. Ghi `.ai/results/LUX-BASELINE-p2.md`.

Tiêu chí xong — mỗi dòng kiểm bằng lệnh:

- [ ] `dotnet build --disable-build-servers` xanh, không warning mới.
- [ ] `dotnet test` xanh (đọc **mã thoát**). Test mới, tối thiểu:
  - đêm 1–3 ON + lux theo quy trình → `normal`, lý do `baseline_missing`; sau quyết định đêm 3 có baseline phiên bản 1 = trung vị;
  - đêm 4 lux = 0,7 × baseline → `dim`, sự cố `lamp_dim` `confirmed`, `pole_current_status = dim`; đêm 5 lux bình thường →
    `normal`, sự cố **không** tự đóng;
  - hai số đo cùng một đêm (kể cả hai phía nửa đêm, ví dụ 23:50 và 00:20 giờ VN) → chỉ **một** thành viên;
  - số đo không `protocol` → không vào baseline, không đổi trạng thái (hành vi cũ);
  - độ cao lệch → 400 `LUX_PROTOCOL_MISMATCH`; dao động cửa sổ vượt ngưỡng → 400 `LUX_WINDOW_UNSTABLE`; `lux_value` ≠ trung vị
    → 400 `LUX_SAMPLES_MISMATCH`; cả ba **cũng qua `/sync/push`**;
  - tấm lọc ND → có `evaluation`, không đổi trạng thái, không sự cố, không vào baseline;
  - đổi bóng hoặc đổi `module_id` / độ cao → baseline bắt đầu lại;
  - `member_cv > 0,10` → không chấm dim, lý do `baseline_unstable`;
  - gắn lux vào ảnh đã quyết → 409 `PHOTO_ALREADY_DECIDED`;
  - baseline dùng để chấm **không** chứa số đo của chính đêm đang chấm;
  - ảnh xã ngoài phạm vi → 404; ghi `commune_id` sai cha bị guard chặn;
  - `OpenApiSpecTests` assert các trường mới.
- [ ] Phá thử từng chốt chặn (bỏ luật một-đêm-một-thành-viên, bỏ loại tấm lọc, bỏ lọc "đêm trước"): mỗi lần đúng một test đỏ, khôi
      phục bằng `cp` / `touch`.
- [ ] Migration chỉ chứa phần của D-7 đã chốt; `EXPLAIN` truy vấn baseline theo cột đi qua index.

## KHÔNG ĐƯỢC làm

- Không viết code ở Phase 1, kể cả khi đã rõ phải sửa gì.
- Không tự chọn phương án D-7 hay bất kỳ D-item nào khác Mỹ đã chốt; chỗ mơ hồ ghi thành D-item mới.
- **Không trộn** baseline của đỉnh lux khi chạy xe với số đo đứng yên. Không sửa pipeline sweep, `luminance_baseline`,
  `baseline_member`, `luminance_history` nếu D-7 chốt (B).
- Không thêm hay đổi giá trị enum Contract mục 1. Không đổi kiểu, độ nullable hay tên trường đã publish (kể cả
  `PoleMapHistoryPoint.sweep_id`, `lux_value`, `meter_model`).
- Không đưa số đo **không có `protocol`** vào baseline hay phân loại (lux kế cầm tay là đối chứng, D-R23 A).
- Không so lux với chuẩn chiếu sáng, không dùng lux tuyệt đối, không so giữa hai cột. Chỉ tỉ lệ với baseline của chính cột.
- Không gộp `unknown` vào `out`. Không tự đóng sự cố.
- Không `ExecuteUpdate` / `ExecuteDelete`. Không giữ khoá qua upload hay chạy model. Không cascade giữa bảng có thể khác xã.
- Không viết lại tập trạng thái mở (`FaultStatusSets`), ngưỡng `dim` (`SystemSettingValues`), định nghĩa "đêm" ở nhiều chỗ.
- Không viết định danh cột / constraint từ trí nhớ; đọc schema thật rồi dán output.
- Không chạy test trên `luxmap_dev`. Không ghi gì lên VPS.
