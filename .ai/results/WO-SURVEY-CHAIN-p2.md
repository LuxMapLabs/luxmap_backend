# WO-SURVEY-CHAIN — Phase 2 (Claude)

Nền: `.ai/tasks/WO-SURVEY-CHAIN.md` (C-1…C-5) + thiết kế gộp `.ai/results/WO-SURVEY-CHAIN-p1.md`. Drift WSC-1…WSC-5.

## Thay đổi

| File | Việc |
|---|---|
| `src/LuxMap.Modules.WorkOrders/WorkOrderRules.cs` | `FollowUpKinds(Survey) = [Inspection]` |
| `src/LuxMap.Modules.WorkOrders/ISurveyFindingLookup.cs` | Port mới (`SurveyFinding(PoleId, FaultType, DataSource)`) |
| `src/LuxMap.Modules.Survey/Review/SurveyFindingLookup.cs` + `SurveyModule.cs` | Hiện thực: history của `accepted_run_id`, xã neo, mới nhất theo cột rồi `FaultFor` |
| `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs` | `FollowUp` nhánh khảo sát; `NewWorkOrder.Keep` → `Insert` lọc lại sau `FaultLocks`; `carried_fault_ids` sắp theo độ dài |
| `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs`, `gen_consolidated_spec.py`, `luxmap-v1.5.json` | Mô tả ngữ nghĩa (`luxmap-v1.json` không đổi: XML doc của request không vào spec) |
| `tests/LuxMap.Api.Tests/SurveyPublicationTests.cs` | 5 test + teardown xoá `work_order_fault` và phiếu con |
| `docs/contract-drift.md`, `CLAUDE.md`, `tracking.html` | WSC-1…5, ghi chú bẫy ở mục FR-2, thẻ tiến độ |

## Bằng chứng

```
$ dotnet build src/LuxMap.Api --disable-build-servers
    0 Error(s)
    0 Warning(s)

$ MSBUILDDISABLENODEREUSE=1 dotnet test --disable-build-servers
Passed!  - Failed:     0, Passed:   384, Skipped:     0, Total:   384 - LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45 - LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:  1078, Skipped:     2, Total:  1080 - LuxMap.Api.Tests.dll
exit=0

$ npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
Woohoo! Your API description is valid. 🎉
You have 9 warnings.
```

## Phá thử (`SurveyPublicationTests`, 33 test)

| Phá | Test đỏ |
|---|---|
| S1 bỏ `!busy.Contains` trong `Insert` | `A_fault_taken_by_another_order_while_waiting_for_its_lock_is_dropped_not_a_409` |
| S2 loại hiệu lực → `fault_type` trần | `A_fault_opened_before_the_survey_is_carried_by_effective_type_and_busy_rejected_or_other_type_ones_are_not` |
| S3 lọc out/dim trước khi chọn mới nhất | `The_latest_accepted_result_decides_so_an_older_out_followed_by_normal_carries_nothing` |
| S4 bỏ `Survey => [Inspection]` | 3 test (`…only_once_verified…`, `…anchor_communes_new_fault…`, `…latest_accepted_result…`) |

Khôi phục: 33/33 xanh.

⚠️ Lọc xã neo nằm ở **hai** chỗ (history trong port, fault trong `FollowUp`) nên phá riêng một chỗ không đỏ — phòng thủ hai lớp, có chủ ý.
Lọc `open` cũng vậy (truy vấn ứng viên, `Keep`, `Eligible` trong `Insert`).

## Bẫy gặp trong phiên

MSB3021 khi Codex build đồng thời trong worktree sandbox: `dotnet build` của cây chính dùng lại MSBuild node của Codex (chỉ ghi
được trong worktree của Codex). `--disable-build-servers` / `MSBUILDDISABLENODEREUSE=1` là đủ.

## Còn mở

- Báo WP5: nút "Tạo lịch kiểm tra" trên phiếu khảo sát `verified`; có thể nhận `NOTHING_TO_FOLLOW_UP`.
- Review chéo: Codex review nhánh này (chưa chạy).
