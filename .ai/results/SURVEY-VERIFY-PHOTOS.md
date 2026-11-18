# SURVEY-VERIFY-PHOTOS — kết quả phase 2 (17/11/2026)

## Đã triển khai

Theo quyết định Mỹ + WP5 trong ticket, implement trực tiếp; không commit, không push.

- `WorkOrderService.Act`: ngay sau `RequireAction`, trước mọi thay đổi, chỉ `verify` + `survey` kiểm ảnh/clip chưa hủy và `Decision == null`. Có ảnh → 409 `POLE_PHOTOS_UNDECIDED`, `details.photo_ids` theo `captured_at, uploaded_at, length(photo_id), photo_id`.
- `WorkOrderPolePhotoQueries.NotWithdrawn` là định nghĩa chung cho lọc ảnh chưa hủy; `Photos()` và cổng verify cùng gọi. `InCaptureOrder` dùng chung cho list PP-5 và danh sách lỗi. Giữ query filter xã, không thêm global filter, không đổi retention/replay.
- Ảnh confirm/reject/hủy không chặn; survey không ảnh nghiệm thu như cũ. Không đổi allowed_actions, hai cổng inconclusive/video, `/decision`, `/review`, hoặc verify của inspection/repair/pole_gps_update.
- Controller khai rõ 200 `WorkOrderDetail`, 409 `ApiErrorResponse`; xuất lại hai spec qua Swagger + generator. Không sửa Contract văn bản; không migration/dependency/schema change.
- Drift **SVP-1…SVP-3** ghi Mỹ + WP5 chốt, SELF-SIGNED, nền tạm; hướng dẫn WP5 return → review → nộp lại cho inconclusive, chính Quản lý mở clip trước confirm, reject ảnh không dùng.

## File đã sửa / thêm

- `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs`
- `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoQueries.cs` (mới)
- `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs`
- `tests/LuxMap.Api.Tests/WorkOrderSurveyVerifyPhotoTests.cs` (mới)
- `docs/contract-drift.md`
- `docs/openapi/tools/gen_consolidated_spec.py`
- `docs/openapi/luxmap-v1.json` (xuất từ code)
- `docs/openapi/luxmap-v1.5.json` (generator)
- `tracking.html`
- `.ai/results/SURVEY-VERIFY-PHOTOS.md` (mới)

`.ai/tasks/SURVEY-VERIFY-PHOTOS.md` đã untracked trước khi bắt đầu, không sửa. Giữ nguyên symlink AGENTS.md.

## Test mới — CHƯA CHẠY

4 phương thức / **5 ca tích hợp** trong partial `WorkOrderTests`, dùng host/fixture/teardown và `AssetDatabaseCollection` sẵn có:

1. `Survey_verify_lists_only_live_undecided_photos_and_clips_then_succeeds_after_decisions`: phiếu survey có ảnh + clip chưa quyết, ảnh reject, ảnh hủy; complete rồi verify 409, đúng hai ID theo captured_at (cố ý ngược thứ tự upload/ID), vẫn done, ClosedAt/ReviewNote null, CompletedAt không đổi, không audit Verified; allowed_actions vẫn verify. Confirm ảnh và reject clip rồi verify 200 verified.
2. `Survey_verify_without_pole_photos_succeeds`: survey done không ảnh → 200 verified.
3. `Non_survey_verify_still_accepts_undecided_pole_photos`: theory inspection/repair done có ảnh chưa quyết → 200 verified (2 ca).
4. `Survey_verify_checks_role_and_state_before_undecided_photos`: field engineer nhận 403 ROLE_FORBIDDEN; manager trên survey in_progress nhận 409 INVALID_STATE_TRANSITION, trước cổng ảnh mới.

Không ghim ID, không đổi sequence; tiêu chí thứ tự dùng captured_at theo lựa chọn ticket. Không tuyên bố đã kiểm runtime các nhánh tie-break uploaded_at/length/ordinal.

## Lệnh và output thật

### Build

Lệnh đầu `dotnet build --disable-build-servers` không có output trong sandbox; đã gửi Ctrl-C, log trả `Attempting to cancel the build...`.
Lượt `dotnet build --disable-build-servers -m:1` build thành công nhưng có NU1900 (không truy cập được NuGet vulnerability service) và CS8631 trong assert mới. Đã sửa nullability assert; restore lại từ cache local để không phụ thuộc mạng, không đổi dependency:

```bash
dotnet restore --source /Users/nhm809/.nuget/packages --disable-parallel --disable-build-servers -m:1 -p:NuGetAudit=false
```

Exit 0, mọi project restored. NuGetAudit=false chỉ áp lệnh restore offline này, không thay cấu hình repo.
Build cuối toàn solution, gồm test mới:

```text
$ dotnet build --disable-build-servers --no-restore -m:1 /p:UseSharedCompilation=false

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:15.77
```

Exit 0.

### Swagger / generator / lint

```text
$ dotnet tool restore
Tool 'swashbuckle.aspnetcore.cli' (version '10.2.3') was restored. Available commands: swagger

Restore was successful.
```

Không có `.env` trong worktree. Wrapper tạm `/tmp/survey-verify-photos-export.py` nạp tên biến từ `.env.example`, sinh giá trị ngẫu nhiên cho JWT/DB/MinIO secret, đặt `Swagger__Enabled=true`, `Cors__AllowedOrigins__0=https://localhost:3000`, `DOTNET_USE_POLLING_FILE_WATCHER=1`, `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`, rồi chạy đúng CLI dưới đây. Không lưu secrets vào repo; không kết nối DB:

```text
$ python3 /tmp/survey-verify-photos-export.py
# thực thi: dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/wt-survey-verify-photos/docs/openapi/luxmap-v1.json

$ python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=148 implemented_ops=191 not_implemented_ops=0 schemas=330

$ node /Users/nhm809/.npm/_npx/1738445911c9ab26/node_modules/@redocly/cli/bin/cli.js lint docs/openapi/luxmap-v1.5.json
docs/openapi/luxmap-v1.5.json: validated in 81ms

Woohoo! Your API description is valid. 🎉
You have 10 warnings.

```

Cả ba exit 0. Dùng Redocly CLI đã có sẵn để không cần npx tải qua mạng. 10 warning thuộc các mục có sẵn; không thêm dependency. Diff spec đã rà, chỉ route verify đổi.

Python đọc hai JSON, assert response 200/409 đúng schema, role literal manager, consolidated description có mã lỗi + details.photo_ids:

```text
luxmap-v1.json: verify 200 WorkOrderDetail, 409 ApiErrorResponse, manager only; OK
luxmap-v1.5.json: verify 200 WorkOrderDetail, 409 ApiErrorResponse, manager only; OK
```

Exit 0. `git diff --check`: không output, exit 0.

## Bàn giao kiểm chứng

- ✅ Verified: build toàn solution sạch, test mới compile; export/generator/lint OpenAPI thành công, schema response và role đúng; đã rà diff.
- ⚠️ Could not verify: **không chạy test tích hợp**, kiểm tay HTTP/DB hoặc phá thử vì sandbox không có Docker/Postgres (theo chỉ thị). Không tuyên bố các ca tích hợp pass.
- 📌 Remaining assumptions: không có giả định nghiệp vụ mới ngoài các quyết định ticket đã chốt. Cần Claude chạy runtime trên container PostGIS riêng; không dùng luxmap_dev.

Lệnh đề nghị Claude chạy sau build lại:

```bash
dotnet build --disable-build-servers
dotnet test --no-build --filter 'FullyQualifiedName~Survey_verify|FullyQualifiedName~Non_survey_verify|FullyQualifiedName~WorkOrderTests|FullyQualifiedName~OpenApiSpecTests'
```

Sau đó phá thử cổng verify (bỏ kiểm/đưa kiểm trước RequireAction/bỏ lọc withdrawal) để chứng minh test đỏ; khôi phục bằng cp + touch và build lại theo trap testing. Các test decision/video/withdrawal có sẵn cần tiếp tục xanh.

## D-item

**Không phát sinh D-item cần Mỹ chốt thêm.** Các quyết định đã chốt được ghi đúng tầng ở drift **SVP-1…SVP-3**. Chưa commit/push; commit dự kiến `feat(work-orders): block survey verify while pole photos are undecided`.

## Claude review (17/11/2026)

- Không sửa code. Chạy thật (PostGIS): 5/5 ca mới; `WorkOrder|PolePhoto|Withdraw|Capability|SurveyPublication` 541 pass / 2 skip.
- Phá thử: `undecided.Length > 0` → `> 999` → 1/5 đỏ; khôi phục bằng `cp` + `touch` → 5/5 xanh.
