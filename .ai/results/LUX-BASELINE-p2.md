# LUX-BASELINE — Phase 2 (hiện thực)

Người làm: Claude, 26/11/2026, nhánh `docs/LUX-BASELINE-spec` (PR #304, tách từ `dev` `69560a0`). D-1…D-16 chốt theo khuyến nghị
(ticket, mục *Quyết định đã chốt*). Kèm phần WP5 (web) và WP6 (mobile) theo yêu cầu của Mỹ, ở repo riêng.

## Backend — đã làm

| Phần | File chính |
|---|---|
| `protocol` trên `POST /lux-readings` (kiểm độ cao, số mẫu, dao động, trung vị → 400 có lý do), mẫu thô bất biến | `LuxReadingService`, `LuxReadingContracts`, `LuxReading` (+`LuxReadingSample`) |
| Khoá `work_order → pole → photo` khi gắn lux, ảnh đã quyết → 409 `POLE_PHOTO_ALREADY_DECIDED` (D-10) | `LuxReadingService.CheckPhotoAsync` |
| Chấm lúc Quản lý xác nhận ảnh (chỉ phiếu khảo sát, D-2), trước nhánh `not_newer`; `lamp_dim` `confirmed` | `StationaryLuxEvaluator`, `ReviewedPolePhotoPublisher` |
| Baseline đứng yên: trung vị cộng dồn, mỗi đêm một thành viên, chỉ dùng đêm trước, CV, dấu vân tay module + độ cao | `StationaryLuxRules`, `StationaryLuxEvaluator.RebuildAsync` |
| Bảng song song bất biến (D-7 B) + nới CHECK `fault` cho `lamp_dim` từ ảnh | migration `AddStationaryLuxBaseline`, `StationaryLuxConfigurations` |
| Đọc: `evaluation` ở lux, 3 trường `lux_*` ở `PolePhotoItem`, `stationary_luminance_*` ở chi tiết cột, `FindingsAsync` | `LuxEvaluationReads`, `PhotoLuxLookup`, `PoleDetailService`, `SurveyFindingLookup` |
| Phiên bản (BE-34): `classification_algorithm` băm assembly + ngưỡng; `VersionedResultColumn` cho 2 bảng kết quả | `StationaryLuxEvaluator.AlgorithmAsync`, `SurveyModule` |
| Contract v1.24, drift LUX-BASELINE-1…6, OpenAPI, script chép DB, trap survey | tài liệu |

## Bằng chứng chạy thật

`dotnet build --disable-build-servers`: `Build succeeded.`, 0 warning.

Bộ test toàn phần (trước khi sửa test khoá chi tiết cột):

```text
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21 - LuxMap.IotBridge.Tests.dll
Passed!  - Failed:     0, Passed:   449, Skipped:     0, Total:   449 - LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47 - LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90 - LuxMap.Infrastructure.Storage.Tests.dll
Failed!  - Failed:     1, Passed:  2035, Skipped:     7, Total:  2043 - LuxMap.Api.Tests.dll
  Failed LuxMap.Api.Tests.PoleDetailEndpointTests.The_keys_are_exactly_the_documented_ones_and_data_source_external_ref_feeder_id_never_appear
```

Test đỏ là danh sách khoá literal của chi tiết cột; thêm hai khoá mới (`stationary_luminance_baseline`, `stationary_luminance_history`)
vào danh sách, chạy lại các class bị ảnh hưởng:

```text
Passed!  - Failed:     0, Passed:    63, Skipped:     0, Total:    63 - LuxMap.Api.Tests.dll
         (PoleDetailEndpointTests | OpenApiSpecTests | StationaryLuxBaselineTests)
```

`StationaryLuxBaselineTests` — 15 test: ba đêm dựng baseline / đêm 4 mờ → `lamp_dim` confirmed / đêm 5 bình thường, sự cố không tự đóng;
hai số đo hai phía nửa đêm = một đêm; không chấm theo baseline chứa chính đêm đó; số đo đối chứng không được chấm; phiếu kiểm tra không
chấm; 4 lỗi quy trình; gắn vào ảnh đã quyết → 409; tấm lọc ND không đổi gì; baseline không ổn định; đổi module; `FindingsAsync` mang
`lamp_dim`; hai bảng mới không xoá được.

Phá thử (mỗi lần đúng một test đỏ, khôi phục bằng `cp` + `touch`, chạy lại xanh 15/15):

| Chốt | Test đỏ |
|---|---|
| Mỗi đêm một thành viên (`GroupBy(Night)` → `GroupBy(LuxId)`) | `Two_readings_either_side_of_midnight_are_one_survey_night` |
| Chỉ baseline của đêm trước (`FirstOrDefault()` trần) | `A_reading_is_never_judged_against_a_baseline_holding_its_own_night` |
| Ảnh đã quyết (`if (false && …)`) | `A_reading_cannot_be_linked_to_a_photo_already_decided` |
| Baseline phải ổn định mới chấm dim | `An_unstable_baseline_does_not_judge_dim` |

Migration trên container PostGIS trống, dùng xong bỏ: Up → Down (`AddWorkOrderPhotoCopy`) → Up.

```text
after Down:  lux_evaluation gone = t | lux_reading.sensor_height_m columns = 0 | fault CHECK has lamp_dim = f
after Up:    lux_evaluation present = t | 20261125173021_AddStationaryLuxBaseline | immutable triggers = 4
```

OpenAPI: xuất → generator (`info.version` 1.24) → redocly lint `valid`, 9 cảnh báo, **cùng 9 cảnh báo** như spec của `origin/dev`.

## Chưa làm / chưa kiểm

- **Chưa apply migration lên `luxmap_dev` và VPS.** Deploy cần backup trước. Down có preflight từ chối khi đã có dữ liệu đứng yên.
- **Chưa chạy lại bộ test toàn phần** sau khi sửa danh sách khoá (chỉ chạy 3 class bị ảnh hưởng).
- Preflight của `Down()` khi **có** dữ liệu chưa thử (chỉ thử trên DB trống).
- Con số `LuxProtocol` (1,0 m ± 0,05; ≥ 5 mẫu; dao động ≤ 0,05; CV ≤ 0,10) là đề xuất — D-R29 O-3 còn mở.
- Màn thử tấm lọc ND trên mobile chưa có (backend nhận `optical_filter_transmittance`; app chưa gửi).

## WP5 (web) — repo `luxmap-web`, worktree `wt-web-lux-baseline`, nhánh `feat/LUX-BASELINE-ui` (chưa commit theo quy tắc repo web)

- `src/types/` sinh lại từ `docs/openapi/luxmap-v1.json` (giữ phần lux; bỏ diff `iot/feeders.ts` không liên quan).
- `LuxVerdictBadge` + `utils/work-schedule/luxVerdict.ts`: hiện kết luận backend ("MỜ · 70% baseline", "Chưa đủ baseline (cần 3 đêm)", …);
  FE không tự tính tỉ lệ.
- `CaseDetailModal`, `FieldReportDetailModal`: "Kỹ sư: Đèn sáng bình thường" → "Kỹ sư: Đèn bật"; bỏ trung bình / min / max lux giữa
  các cột; tiêu đề phiếu thêm "· N cột mờ".
- `npx tsc --noEmit` 0 lỗi; `npm run lint` 0 lỗi (cảnh báo mới duy nhất đã sửa); `npm run build` xanh; `vitest` 6/6 test mới.

## WP6 (mobile) — repo `mobile`, worktree `wt-mobile-lux-baseline`, nhánh `feat/LUX-BASELINE-protocol`

- Gửi `protocol {samples, sensor_height_m = 1.0, module_id = địa chỉ BLE}`; `lux_value` = trung vị chính xác của mẫu.
- Ngưỡng "ổn định" của app = ngưỡng server (dao động ≤ 5%, bỏ sàn 0,5 lux), để server không từ chối số app đã nhận.
- Room v27 → v28 (3 cột mới ở `local_lux_reading`); số đo cũ gửi như đối chứng.
- 409 `POLE_PHOTO_ALREADY_DECIDED` → gửi lại không kèm `photo_id` (như các lỗi liên kết ảnh khác).
- Unit test các class liên quan: 65/65 xanh. Chưa chạy migration test trên máy thật (androidTest).
