# PHOTO-REVIEW — review bởi Claude (20/10/2026)

Đọc: migration `AddPolePhotoReviews` (Up/Down), `WorkOrderPolePhotoService.ReviewAsync`/`DecideAsync`, `ReviewedPolePhotoPublisher`,
`PoleStatusPublication`, port `IPolePhotoPublication`, drift PP-11…PP-15, CLAUDE.md.

**Kết luận: không có phát hiện P1/P2/P3. Đồng ý merge.**

- Migration: chỉ `AddColumn` ×4, FK + index reviewer, thay 3 CHECK; không `DropIndex`/`DropColumn` lạ, không `xmin`, hai constraint
  DB-only nguyên vẹn; `Down()` đối xứng, có preflight khoá bảng và từ chối khi đã có dữ liệu review.
- D-3: mọi chỗ đọc `status_confidence` (Map, WorkOrderPoles, Faults) là `double?` truyền thẳng; Contract vốn ghi `0..1|null` ⇒ không gãy client.
- Provenance: nhãn đúng AI giữ `cv` + bộ AI; nhãn đổi → `field_report`, `reported_by` = kỹ sư, `confirmed_by` = Quản lý, score/model null.
  Publisher tự kiểm bất biến của hai nhánh.
- Khoá `work_order → pole → photo`, đọc lại sau khoá; mỗi `SaveChanges` một audit; review giống hệt là no-op.

Kiểm lại độc lập trên worktree (`MSBUILDDISABLENODEREUSE=1 dotnet test --disable-build-servers`, toàn bộ):

```
Passed!  - Failed:     0, Passed:   384, Skipped:     0, Total:   384 - LuxMap.Shared.Tests.dll
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45 - LuxMap.Persistence.Tests.dll
Passed!  - Failed:     0, Passed:    54, Skipped:     0, Total:    54 - LuxMap.Infrastructure.Storage.Tests.dll
Passed!  - Failed:     0, Passed:  1112, Skipped:     2, Total:  1114 - LuxMap.Api.Tests.dll
exit=0
```

Ghi chú merge: nhánh này và `feat/WO-SURVEY-CHAIN` cùng thêm vào cuối `docs/contract-drift.md` và cùng chỗ trong `tracking.html` —
PR thứ hai rebase lên `dev` sẽ có xung đột chữ, giữ cả hai khối.
