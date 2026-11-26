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

## Lệch nhỏ so với đề xuất

- Test 400 cho `data_source` sai giá trị bỏ khỏi theory "naming the field": `WireEnum.ParseCsv` trả 400 không có `details.field`
  (hành vi chung, đã có test ở `Citizen_malformed_queries_are_validation_errors`).

## Còn lại

- Gợi ý vẫn chỉ chạy khi Quản lý mở báo cáo (D-5) — báo cáo chưa ai mở hiện `not_analyzed`.
- Prod 26/11: 0 báo cáo QR. WP5 chưa được báo về ST-14.
