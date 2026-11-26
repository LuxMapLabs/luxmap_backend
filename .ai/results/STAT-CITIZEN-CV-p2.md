# STAT-CITIZEN-CV — Phase 2

Claude, 26/11/2026, nhánh `feat/STAT-CITIZEN-AI`. Mỹ duyệt D-1…D-12 của `STAT-CITIZEN-CV-p1.md` nguyên văn ("Approve all").

## Đã làm

- `GET /api/v1/statistics/citizen-screening` (`StatisticsController.CitizenScreeningAsync`, `StatisticsService.CitizenScreeningAsync`,
  contracts `CitizenScreening*` + enum `CitizenClaim`). Hai truy vấn DB: một dòng mỗi báo cáo (đếm lượt gửi, ảnh theo nhãn, cờ claim /
  xa cột), một gom ảnh theo `(data_source, ai_artifact_version)`; kết luận và các khối gom trong bộ nhớ.
- Drift **ST-14** (ngay sau ST-13), `gen_consolidated_spec.py` (SUMMARY + SECTION), `luxmap-v1.json` + `luxmap-v1.5.json` sinh lại, tracking.

## Bằng chứng

```
$ dotnet test tests/LuxMap.Api.Tests --no-build --filter "FullyQualifiedName~StatisticsTests|FullyQualifiedName~Capability|FullyQualifiedName~OpenApi|FullyQualifiedName~Anonymous"
Passed!  - Failed:     0, Passed:   245, Skipped:     0, Total:   245, Duration: 27 s - LuxMap.Api.Tests.dll (net10.0)
exit=0
```

Phá thử (mỗi lần `cp` bản gốc về rồi `touch`, build lại):

| Phá | Test đỏ |
|---|---|
| (a) `cv_off` bỏ vế "0 ảnh sáng" | `Screening_verdicts_claims_and_contradictions_follow_the_photo_hints` |
| (b) đèn mờ + CV sáng tính là báo nhầm | `Screening_verdicts_claims_and_contradictions_follow_the_photo_hints` |
| (c) bỏ lọc `calibration_rig` mặc định | `Screening_keeps_sources_apart_and_filters_by_source_segment_and_night` |

Sau khôi phục: `StatisticsTests` 64/64 xanh, exit 0.

`npx @redocly/cli lint docs/openapi/luxmap-v1.5.json` → valid, **10 cảnh báo**, đúng bằng bản `origin/dev` (đã lint cùng lệnh).

## Chạy thật end-to-end (26/11, DB bỏ đi `luxmap_e2e`, API nhánh này ở cổng 5242)

Quản lý `engineer` cấp QR cho POLE-0001…0014 → 16 lượt gửi qua `POST /public/reports` với ảnh đêm thật (`img/`, `broken_light/photos`)
→ Quản lý mở 13/14 báo cáo (model YOLO thật chạy, phiên bản `yolo-6962e57cd7880f82+cq-561d1e0631ae`) → quyết 5 (1 accept, 1 merge, 3 reject).
`GET /statistics/citizen-screening` khớp truy vấn SQL độc lập trên `luxmap_e2e` ở mọi số:

| | API | SQL |
|---|---|---|
| reports / submissions / far | 14 / 16 / 1 | 14 / 16 / 1 |
| pending / accepted / merged / rejected | 9 / 1 / 1 / 3 | 9 / 1 / 1 / 3 |
| cv_off / cv_on / inconclusive / not_analyzed / no_photo | 1 / 3 / 7 / 1 / 2 | 1 / 3 / 7 / 1 / 2 |
| contradicted | 3 | 3 |
| off_upheld / off_rejected / on_upheld / on_rejected | 1 / 0 / 1 / 2 | 1 / 0 / 1 / 2 |
| agreement_rate / cv_off_upheld_rate / cv_on_rejected_rate | 0.75 / 1 / 0.6667 | (3/4, 1/1, 2/3) |

Ảnh đường phố nhiều bóng → `inconclusive` (luật một bóng, PC-8); ảnh cận một bóng sáng → `lamp_on`. Web (`feat/DASHBOARD-citizen-ai`) hiện đúng các số này trên Chrome.

## Lệch nhỏ so với đề xuất

- Test 400 cho `data_source` sai giá trị bỏ khỏi theory "naming the field": `WireEnum.ParseCsv` trả 400 không có `details.field`
  (hành vi chung, đã có test ở `Citizen_malformed_queries_are_validation_errors`).

## Còn lại

- Gợi ý vẫn chỉ chạy khi Quản lý mở báo cáo (D-5) — báo cáo chưa ai mở hiện `not_analyzed`.
- Prod 26/11: 0 báo cáo QR. WP5 chưa được báo về ST-14.
- Phát hiện ngoài phạm vi (chưa sửa): `POST /public/reports` đọc `lat`/`lng` theo culture của server. Máy này (`en_VN`) từ chối
  `10.979973` (400 `lat`) và nhận `10,979973`. Prod chạy container Linux, nhiều khả năng culture invariant — cần kiểm.
