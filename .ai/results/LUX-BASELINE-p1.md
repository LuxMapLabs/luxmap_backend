# LUX-BASELINE — Phase 1 (khảo sát, không sửa code)

Người làm: Claude, 25–26/11/2026, trên `origin/dev` `69560a0`. Mỹ chọn Claude tự làm (CLI ChatGPT từ chối `gpt-6.1-sol` từ 24/11).
DB: `luxmap_dev` (local) và VPS `luxmap` (**chỉ SELECT**, phiên `default_transaction_read_only = on`, Mỹ cho phép 26/11).
Cả hai đang ở migration `20261124080016_AddWorkOrderPhotoCopy`, khớp migration mới nhất của `dev`.

## Tóm tắt

| Câu | Kết luận | Ảnh hưởng D-item |
|---|---|---|
| 1 | Ảnh cột nhận ở **mọi** `task_kind`, không có cổng loại phiếu; DB đã có ảnh `confirm` ở phiếu `repair` | D-2 cần cổng **tường minh** |
| 2 | Ảnh **không** đi qua `/sync/push`; lux đến **sau** ảnh vì `photo_id` do server cấp; tạo lux **không khoá** hàng ảnh | D-10 phải sửa |
| 3 | `luminance_history` có PK `(sweep_id, pole_id)`, `sweep_id` / `run_id` NOT NULL, trigger bất biến cấp statement | D-7 **(B)** có bằng chứng |
| 4 | Chỗ cắm D-8 / D-9 là `ReviewedPolePhotoPublisher.PublishAsync`; không cần đổi port | D-8 / D-9 rõ chỗ |
| 5 | `FindingsAsync` gập ảnh bằng `PhotoStatus` (ON giữ nguyên); phiếu nối tiếp dựa vào nó | D-13 + sửa WSC-6 |
| 6 | Ngưỡng `dim` ở `system_setting`; `BaselineMinimumMembers` ở appsettings `SurveyReview` | D-5: khuyến nghị appsettings `LuxProtocol` |
| 7 | VPS: 13 số lux, **hai tên `meter_model` khác nhau**; 0 lux đến sau quyết định; 0 baseline | D-3 không dựa vào `meter_model`; D-15 mới |

## 1. Ảnh cột ở loại phiếu nào (D-2)

`WorkOrderPolePhotoService` không đọc `TaskKind` ở đâu cả:

```text
$ grep -c "TaskKind" src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoVideoUpload.cs
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoVideoUpload.cs:0
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:0
```

Upload chỉ kiểm cột thuộc `WorkOrderPoles.Targets(id)` (`WorkOrderPoles.cs:90`) và phiếu đang `in_progress` (`Writable`).
`Targets` gồm tuyến của phiếu, `work_order_pole_target`, và cột mang sự cố của phiếu. Phiếu `pole_gps_update` chỉ tính
`work_order_pole_target`. `TaskKind` hiện có **bốn** giá trị: `Inspection, Repair, Survey, PoleGpsUpdate` (`WorkOrder.cs:13`).
Phiếu sửa chữa thực tế đã có ảnh được quyết:

```text
luxmap_dev:  task_kind | decision | photos | with_lux
             repair    | confirm  |      1 |        0
```

→ D-2 (chỉ phiếu khảo sát được chấm dim / vào baseline) cần một cổng tường minh trong publisher: đọc `WorkOrder.TaskKind`
theo `input.WorkOrderId`. Survey đã reference WorkOrders, không cần port mới.

## 2. Thứ tự ảnh + lux (D-10)

- `SyncOpType` chỉ có `FaultReport, LuxReading, PoleNote, WorkOrderStart, WorkOrderComplete` (`SyncOperation.cs:4`). **Ảnh cột
  không đi qua `/sync/push`**: chúng upload multipart riêng. Câu "đẩy lux cùng lô sync ngay sau ảnh" trong ticket là **sai**.
- `photo_id` (`PPH-…`) do server cấp lúc upload ảnh, nên lux **chỉ gửi được sau khi upload ảnh thành công**. Giữa hai việc đó,
  Quản lý có thể quyết định ảnh.
- `LuxReadingService.CheckPhotoAsync` kiểm ảnh còn sống, cùng cột, chưa có lux. Nó **không** kiểm ảnh đã được quyết, và **không
  khoá** hàng ảnh. `DecideAsync` thì khoá `work_order → pole → work_order_pole_photo` (`WorkOrderPolePhotoService.cs:276–279`).
  Vì vậy chỉ thêm kiểm "đã quyết → 409" vẫn hở đua: lux kiểm lúc quyết định chưa commit, cả hai cùng thành công, số lux không
  được chấm.
- Mã lỗi đã có: **`POLE_PHOTO_ALREADY_DECIDED`** (409), dùng ở review / decide (`WorkOrderPolePhotoService.cs:229, 256, 283`).
  Ticket đặt tên mới `PHOTO_ALREADY_DECIDED` là thừa.
- Có sẵn cổng phía phiếu: survey `verify` trả 409 `POLE_PHOTOS_UNDECIDED` khi còn ảnh chưa quyết (#270).
- VPS: chưa xảy ra trường hợp lux đến sau quyết định.

```text
 lux_after_decision | photo_undecided | linked
                  0 |               3 |     12
```

**Đề xuất sửa D-10:** khi `photo_id` có mặt, `LuxReadingService.CreateAsync` khoá theo **cùng thứ tự** `work_order → pole →
work_order_pole_photo` (lấy `work_order_id` từ hàng ảnh), đọc lại `decision`; đã quyết → 409 **`POLE_PHOTO_ALREADY_DECIDED`**.
Áp cả đường `/sync/push` vì cùng service. App gửi lux ngay khi có `photo_id`. Web cảnh báo trước khi quyết định ảnh chưa có lux
ở phiếu khảo sát.

Phương án mạnh hơn, để sau: nhận lux **trong chính request upload ảnh**, không còn khe hở. Đổi bề mặt lớn hơn nên không đề xuất
cho ticket này.

## 3. Schema thật (D-7)

Trích `\d` trên `luxmap_dev` (đủ để quyết D-7):

```text
Table "public.luminance_history"
 sweep_id  | text   | not null
 run_id    | bigint | not null
 observation_id | bigint |          (nullable)
 baseline_id    | bigint |          (nullable)
Indexes:
    "pk_luminance_history" PRIMARY KEY, btree (sweep_id, pole_id)
Foreign-key constraints:
    "fk_luminance_history_pole_observation_observation_id_pole_id_c" FOREIGN KEY (observation_id, pole_id, commune_id, run_id) REFERENCES pole_observation(...)
    "fk_luminance_history_survey_processing_run_run_id_sweep_id" FOREIGN KEY (run_id, sweep_id) REFERENCES survey_processing_run(run_id, sweep_id)
Triggers:
    luminance_history_immutable BEFORE DELETE OR UPDATE OR TRUNCATE ON luminance_history FOR EACH STATEMENT EXECUTE FUNCTION luxmap_reject_processing_mutation()

Table "public.luminance_baseline"
 direction | text | not null
    "ck_luminance_baseline_direction" CHECK (direction = ANY (ARRAY['forward'::text, 'reverse'::text]))
    "ix_luminance_baseline_pole_id_version" UNIQUE, btree (pole_id, version)
Triggers:
    luminance_baseline_immutable BEFORE DELETE OR UPDATE OR TRUNCATE ... FOR EACH STATEMENT

Table "public.baseline_member"
 baseline_id    | bigint | not null
 observation_id | bigint | not null
    "pk_baseline_member" PRIMARY KEY, btree (baseline_id, observation_id)
    "fk_baseline_member_pole_observation_observation_id" FOREIGN KEY (observation_id) REFERENCES pole_observation(observation_id)

Table "public.lux_reading"
 photo_id | text | (nullable)
    "ux_lux_reading_photo_id" UNIQUE, btree (photo_id, pole_id, commune_id) WHERE photo_id IS NOT NULL
    "fk_lux_reading_pole_photo" FOREIGN KEY (photo_id, pole_id, commune_id) REFERENCES work_order_pole_photo(...)
    "ck_lux_reading_value_non_negative" CHECK (lux_value >= 0 AND lux_value <> 'NaN' AND lux_value <> 'Infinity')
 (không trigger bất biến)

Table "public.pole_current_status"
 last_sweep_id | text   | (nullable)
 last_run_id   | bigint | (nullable)
    "fk_pole_current_status_pole_pole_id" FOREIGN KEY (pole_id) REFERENCES pole(pole_id) ON DELETE CASCADE
```

Phương án (A) phải:
- đổi PK của `luminance_history` (ảnh không có `sweep_id`);
- cho `sweep_id` / `run_id` nullable, kéo theo FK ghép sang `survey_processing_run`;
- nới `ck_luminance_baseline_direction`;
- thêm cột loại trừ ở `baseline_member`, mà PK của bảng này chứa `observation_id` NOT NULL.

Cả ba bảng có trigger bất biến cấp statement, nên `AlterColumn` sinh `UPDATE` sẽ bị chặn (bẫy BE-34 trong `survey.md`). Ngoài
ra (A) còn mở lại bẫy FK `MATCH SIMPLE` với cột null (BE-15 P2c). **(B) không đụng bảng nào trong số này.**
`pole_current_status` dùng được cho ảnh mà không cần đổi: `last_sweep_id` / `last_run_id` đã nullable và luồng ảnh đang ghi
`null` vào đó.

**Khuyến nghị giữ (B).** Ghi thêm cho Phase 2: `lux_reading` **không** có trigger bất biến. Mẫu thô (D-4) và `lux_evaluation`
mới phải là `IImmutableRecord` + trigger như các bảng kết quả khác.

## 4. `DecideAsync` và chỗ cắm (D-8, D-9)

`WorkOrderPolePhotoService.DecideAsync` (`:271–309`), theo thứ tự:
1. Mở transaction, khoá `work_order` → đọc lại ảnh → khoá `pole` → khoá `work_order_pole_photo` → `Reload`.
2. Đã quyết → 409; `confirm` mà nhãn hiệu lực không phải on/off → 409 `POLE_PHOTO_INCONCLUSIVE`; clip chưa mở → 409.
3. Dựng `PolePhotoPublication` (có `PhotoId`, `WorkOrderId`, nguồn nhãn).
4. `confirm` → `IPolePhotoPublication.PublishAsync` (hiện thực: `Survey/Review/ReviewedPolePhotoPublisher.cs`).
5. Ghi quyết định lên hàng ảnh, audit `Confirmed` / `Rejected`, `SaveChanges`, commit.

`ReviewedPolePhotoPublisher.PublishAsync`:
- kiểm provenance → đọc cột → đọc `pole_current_status`;
- **`!IsNewer(captured_at, LastEvaluatedAt ?? LastSeenAt)` → trả `not_newer` sớm, không làm gì thêm**;
- `PhotoStatus` (ON giữ nguyên dim/normal) → `PoleStatusPublication.StageAsync` → audit `Applied` → `SaveChanges`;
- OFF và chưa có `lamp_out` mở → tạo sự cố `confirmed` + audit → `SaveChanges`.

Kết luận cho Phase 2:
- **Chỗ cắm:** trong `PublishAsync`. Survey sở hữu `LuxReading`, nên đọc lux theo `input.PhotoId` ngay tại đó. Bản ghi
  `PolePhotoPublication` và port `IPolePhotoPublication` **không cần đổi**. Các khoá đã được caller giữ.
- **Thứ tự mới:** ghi `lux_evaluation` và cập nhật baseline **trước** nhánh `not_newer`. Ảnh được quyết muộn vẫn là số đo
  hợp lệ của đêm đó, giống luật "late acceptance" của sweep (`SurveyBaselineLookup.cs`). Còn việc công bố trạng thái thì
  vẫn tuân `not_newer`.
- `lamp_dim` theo đúng khuôn `lamp_out` ở đây. Hằng severity đã có `SurveyReviewOptions.LampDimSeverity` (mặc định `Low`).
- Mỗi `SaveChanges` đúng một audit: cập nhật baseline + đánh giá cần một bước save riêng có audit, hoặc gộp vào audit `Applied`.
  Chốt ở Phase 2, không thêm save thiếu audit.

## 5. `FindingsAsync` và tóm tắt phiếu (D-13)

`Survey/Review/SurveyFindingLookup.cs`: lấy lịch sử sweep đã duyệt và ảnh `confirm` của phiếu, rồi `Merge` theo thời gian.
Với ảnh, kết quả là `SurveyPublicationRules.PhotoStatus(cv_state, status)`, tức **ON giữ nguyên** (WSC-6). Caller duy nhất:
`WorkOrderService.cs:330`, tạo phiếu nối tiếp từ phiếu khảo sát (WSC-2…WSC-4).

→ Phase 2: khi ảnh có `lux_evaluation`, `Merge` dùng `classified_as` của đánh giá thay cho `PhotoStatus`, để phiếu kiểm tra nối
tiếp mang được `lamp_dim`. Đây là thay đổi ngữ nghĩa của **WSC-6 và luật "ON giữ nguyên" (PP)**, phải ghi drift.
Câu "Không phát hiện cột mất sáng" trên web không có trường backend tương ứng (grep contracts WorkOrders không thấy). FE tự suy,
nên FE cần các trường của D-13.

## 6. Cấu hình (D-5, D-6)

- `dim_threshold_ratio`: bảng `system_setting`, định nghĩa ở `SystemSettingKeys.All` (mặc định 0.80, 0.50–0.95). CHECK của bảng
  **sinh từ danh sách khoá** (`SystemSetting.cs:94`): thêm khoá là thêm migration. Đọc qua `SystemSettingValues.ReadAsync`.
- `BaselineMinimumMembers`, `LampOutSeverity`, `LampDimSeverity`: `SurveyReviewOptions`, bind section `SurveyReview`,
  `ValidateOnStart` (`SurveyModule.cs:49–50`).

**Khuyến nghị D-5:** đặt các con số quy trình (`SensorHeightM`, `SensorHeightToleranceM`, `MinSamples`, `MaxWindowSpread`,
`MaxBaselineCv`) vào options **`LuxProtocol`** (appsettings, `ValidateOnStart`), theo khuôn `SurveyReview`.
- Không cần migration.
- Quy trình đo **không nên** sửa được giữa đợt thí điểm từ màn Quản trị: đổi độ cao là phải làm lại baseline (D-9).
- `dim_threshold_ratio` tiếp tục đọc từ `system_setting`, không chép.

Định nghĩa "đêm" (D-6) đặt một hàm trong `SurveyPublicationRules`.

## 7. Dữ liệu thật (D-3)

`luxmap_dev`: `lux_reading` 0 dòng, `luminance_baseline` 0 dòng. VPS (chỉ đọc):

```text
 meter_model  | data_source | n  | with_photo |         first_utc          |          last_utc          | min_lux | max_lux
 ESP32C3_LUX  | field       | 12 |         12 | 2026-11-24 04:37:51.179+00 | 2026-11-25 13:29:41.047+00 |     0.8 |    21.7
 LuxMap_ESP32 | field       |  1 |          0 | 2026-11-23 18:06:41.092+00 | 2026-11-23 18:06:41.092+00 |     0.8 |     0.8

 work_order_id | task_kind |  decision   | photos | with_lux
 WO-0036       | survey    | (undecided) |      4 |        3
 WO-0037       | survey    | confirm     |      9 |        9

 baselines
         0
```

- Hiện chưa có số nào mang quy trình (trường `protocol` chưa tồn tại), nên theo D-3 **cả 13 số là đối chứng**. Không có gì
  phải chuyển đổi.
- **`meter_model` không thống nhất:** 12 số mang `ESP32C3_LUX`, 1 số mang `LuxMap_ESP32`. Contract (LUX-PHOTO D-9) ghi
  `LuxMap_ESP32` và luật "chỉ so số đo cùng `meter_model`" (D-R15). Đây là lệch giữa app và Contract → **D-15**.
- WO-0036: 3 số đo lúc 04:37 UTC (11:37 giờ VN, ban ngày), trên ảnh chưa quyết. Đây là dữ liệu thử, không phải đêm khảo sát.

## 8. D-item cần chốt hoặc sửa

| Mã | Trạng thái sau Phase 1 | Đề xuất |
|---|---|---|
| D-1 | Giữ | Ảnh / clip đã quyết |
| D-2 | **Sửa** | Cổng tường minh `TaskKind == Survey` trong `ReviewedPolePhotoPublisher` khi chấm / dựng baseline; ảnh ở loại phiếu khác giữ hành vi cũ |
| D-3 | **Sửa** | Nhận ra số đo theo quy trình bằng **có `protocol`**, không bằng `meter_model` (xem D-15) |
| D-4 | Giữ | Mẫu thô lưu bảng con bất biến `lux_reading_sample(lux_id, seq, lux)` (khuyến nghị) thay `jsonb`, để CHECK NaN/∞ từng mẫu |
| D-5 | **Chốt chỗ** | Options `LuxProtocol` trong appsettings (mục 6) |
| D-6 | Giữ | Hàm đêm một chỗ trong `SurveyPublicationRules` |
| D-7 | **Có bằng chứng** | (B), mục 3 |
| D-8 | **Bổ sung** | Cắm trong `PublishAsync`; đánh giá + baseline chạy trước nhánh `not_newer`; công bố trạng thái vẫn theo `not_newer` |
| D-9 | Giữ | — |
| D-10 | **Sửa** | Khoá `work_order → pole → photo` trong `LuxReadingService.CreateAsync` khi có `photo_id`; đã quyết → 409 **`POLE_PHOTO_ALREADY_DECIDED`** (mã có sẵn); bỏ câu "cùng lô sync" |
| D-11 | Giữ | — |
| D-12 | Giữ | — |
| D-13 | **Bổ sung** | `SurveyFindingLookup.Merge` dùng `classified_as` của đánh giá; `PolePhotoItem` lấy qua `IPhotoLuxLookup` có sẵn (mở rộng) |
| D-14 | **Bổ sung** | Drift ghi thay đổi WSC-6 / luật "ON giữ nguyên" của PP khi có đánh giá lux |
| **D-15** (mới) | Cần quyết | `meter_model` trên VPS `ESP32C3_LUX` ≠ Contract `LuxMap_ESP32`. Hoặc sửa Contract theo app, hoặc báo WP6 đổi về `LuxMap_ESP32`. Ghi drift dù chọn hướng nào |
| **D-16** (mới) | Cần quyết | Số đo có `protocol` mà ảnh nằm ở phiếu không phải khảo sát: nhận và chỉ lưu (đối chứng), hay 400? Khuyến nghị **nhận, không chấm** (giữ đo đối chứng ở phiếu kiểm tra được) |

Phase 1 dừng ở đây. Không sửa code. Chờ Mỹ chốt D-1…D-16.
