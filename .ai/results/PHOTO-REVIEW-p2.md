# PHOTO-REVIEW — Phase 2 (Codex, 20/10/2026)

✅ **Verified:** đã hiện thực R-1…R-3 và D-1…D-5; build **0 warnings / 0 errors**, **229/229 test pass, exit 0** trên PostGIS container của Testcontainers; migration apply/down/reapply, OpenAPI export/generator/lint và test copy đều chạy thật.

⚠️ **Could not verify:** không đánh giá độ chính xác CV trên ảnh thực địa hay chạy copy lên đích. HTTP integration dùng model/object-store stub hiện có, DB là PostGIS thật. Không chạy `--apply` script copy, không migrate DB dev/production.

📌 **Remaining assumptions:** không phát sinh D-item cần Mỹ chốt thêm. PP-11…PP-15 vẫn SELF-SIGNED 20/10/2026, nền tạm chờ FW; WP5/WP6 chưa được thông báo. Theo D-5, không If-Match: Quản lý công bố review mới nhất sau khóa, có thể khác snapshot vừa xem trên UI.

## Hiện thực

- Thêm PATCH review, policy `ExecuteWorkOrders`, chỉ người được giao; kiểm quyền cha/xã ảnh, `in_progress`, chưa decision; note trim, bắt buộc khi khác AI kể cả AI null/inconclusive. Body class + init. Bốn trường review nullable trên mọi `PolePhotoItem`; không backfill/reset-null.
- Sửa nhiều lần trước decision; cùng label/note/reviewer là no-op, không stamp/audit. Mỗi thay đổi một audit `details_changed` có before/after. Review không inference/storage/publication/fault. Khóa `work_order → pole → photo`, kiểm lại quyền/trạng thái và đọc lại ảnh dưới khóa.
- Giữ `/decision`, `ReviewSurveys`, bốn trường decision cũ và đường `IPolePhotoPublication → PoleStatusPublication.StageAsync`. Công bố `FinalCvState ?? CvState`; provenance tách nhãn hiệu lực/người sửa/bộ AI gốc, giữ nguyên AI snapshot trên ảnh. Nhãn người đổi không mượn confidence/model AI.
- D-3: known current-status có thể null score; unknown vẫn buộc null, range hữu hạn giữ nguyên. **Không sửa CHECK history/observation.** ON giữ dim/normal và score cũ, tiến clock; ảnh cũ/bằng không apply/sinh fault; không ghi history/baseline, không đóng fault, không thay evidence after.
- D-4: human OFF tạo confirmed lamp_out nguồn `field_report`, `reported_by=reviewed_by`, confidence/model null; `confirmed_by` là Quản lý. Nhãn đúng AI giữ `cv` và score/artifact AI; chống trùng effective open lamp_out không phân biệt source.
- Copy PLAN lọc reviewer; reviewer không được copy thì loại ảnh và fault phụ thuộc. Test chạy chính predicate SQL bằng SQLite in-memory, không mở DB nguồn/đích.
- Drift PP-11…PP-15 tại `docs/contract-drift.md`; thêm ghi chú kỹ thuật trong mục POLE-PHOTO-AI của `CLAUDE.md`. Bẫy sửa được: PostgreSQL `E'\v'` là chữ `v`; CHECK dùng `chr(11)` cho vertical tab, test cả whitespace và note hợp lệ `v`.

## Migration — sinh từ API host, đọc trước apply

Lượt đầu EF thiếu `project.assets.json`; đã restore. Restore đầu bị chặn ghi cache NuGet ngoài worktree; restore lại với cache trong `obj/` thành công, không tắt vulnerability audit. EF sau build treo ở bước tìm host; chạy lại với polling watcher và tắt reload cấu hình cho CLI thành công. Các lần treo đã hủy (exit 130), không apply migration nào.

Lệnh sinh thực tế (connection là **dummy chỉ cho migrations add**, như đã được cho phép):

```sh
NUGET_HTTP_CACHE_PATH="$PWD/obj/nuget-http-cache" dotnet restore --force
DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false \
ConnectionStrings__LuxMap='Host=localhost;Port=1;Database=design_only;Username=design_only;Password=design_only;Timeout=1' \
NUGET_HTTP_CACHE_PATH="$PWD/obj/nuget-http-cache" \
dotnet ef migrations add AddPolePhotoReviews -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations --no-build --verbose
```

Trích output thật, **exit 0**:

```text
Using application service provider from Microsoft.Extensions.Hosting.
Using context 'LuxMapDbContext'.
Writing migration to '/Users/nhm809/Documents/LuxMap/luxmap_backend-photo-review/src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs'.
Writing model snapshot to '/Users/nhm809/Documents/LuxMap/luxmap_backend-photo-review/src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs'.
Done. To undo this action, use 'ef migrations remove'
```

Đã đọc `Up()`/`Down()` trước khi test áp dụng. Bốn cột nullable không default/backfill, một FK reviewer Restrict và index không partial; ba CHECK review mới, thay CHECK decision/current-status/photo-fault. Hai constraint DB-only `ak_pole_photo_target`/`fk_pole_photo_same_commune` giữ nguyên, có test catalog. Không có xmin hoặc DropIndex/DropColumn lạ ở Up. Down chỉ bỏ cột/index mới và phục hồi CHECK cũ sau preflight: khóa bảng rồi từ chối nếu có review, known-status không score hoặc photo-fault nguồn mới; không tự xóa review/bịa score/đổi source.

Trích kiểm tra cấu trúc migration, **exit 0**:

```text
Up: AddCheckConstraint=6, AddColumn=4, AddForeignKey=1, CreateIndex=1, DropCheckConstraint=3
Down: AddCheckConstraint=3, DropCheckConstraint=6, DropColumn=4, DropForeignKey=1, DropIndex=1, Sql=1
No unrelated drops or xmin; DB-only constraints untouched; Down preflight before drops: OK
```

`PolePhotoMigrationTests` dùng container riêng: latest → migration ảnh cũ → latest; chứng minh Down từ chối dữ liệu không tương thích và giữ migration đã áp; sau đó rollback ảnh cũ rồi reapply, kiểm FK DB-only còn nguyên và `HasPendingModelChanges()==false`.

## Build và test — output cuối sau khi khôi phục sabotage

```sh
NUGET_HTTP_CACHE_PATH="$PWD/obj/nuget-http-cache" dotnet build
DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false \
dotnet test tests/LuxMap.Api.Tests --no-build \
  --filter 'FullyQualifiedName~WorkOrderTests|FullyQualifiedName~PolePhotoMigrationTests|FullyQualifiedName~OpenApiSpecTests|FullyQualifiedName~AssetSchemaTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~SurveyPublicationTests' \
  --logger 'console;verbosity=normal'
python3 -m unittest scripts.tests.test_pole_photo_copy_plan
```

**Đã đọc exit code của cả ba lệnh: 0, 0, 0.** `WorkOrderPolePhotoTests.cs` và file review mới là partial của **`WorkOrderTests`**, nên filter theo class này chạy cả test ảnh cũ và mới; không dùng filter tên file rồi vô tình khớp 0 test. Docker truy cập được (`docker info --format '{{.ServerVersion}}'`: `29.6.1`, exit 0), không có hạn chế Docker cần dừng ở build.

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.29

  Passed LuxMap.Api.Tests.OpenApiSpecTests.Pole_photo_review_is_additive_assignee_only_and_has_literal_wire_fields [< 1 ms]
  Passed LuxMap.Api.Tests.WorkOrderTests.Review_lock_commands_precede_the_tracked_photo_read_in_order [127 ms]
  Passed LuxMap.Api.Tests.WorkOrderTests.Review_and_decision_serialize_and_publish_the_latest_locked_review(reviewFirst: True) [484 ms]
  Passed LuxMap.Api.Tests.WorkOrderTests.Review_and_decision_serialize_and_publish_the_latest_locked_review(reviewFirst: False) [384 ms]
  Passed LuxMap.Api.Tests.PolePhotoMigrationTests.Photo_schema_applies_rolls_back_and_reapplies_on_its_own_empty_container [1 s]
  Passed LuxMap.Api.Tests.SurveyPublicationTests.Check_rejects_known_history_without_observation [4 ms]

Test Run Successful.
Total tests: 229
     Passed: 229
 Total time: 1,3552 Minutes

.
----------------------------------------------------------------------
Ran 1 test in 0.017s

OK
```

Test phủ quyền literal bốn role/anonymous/kỹ sư khác; xã cha và xã ảnh; mọi trạng thái phiếu; ảnh confirm/reject đóng review; AI on/off/inconclusive/null × nhãn on/off, note/null/trắng/quá dài và body sai; no-op/audit/review về AI/replay; bộ AI bất biến; publication human/AI, hai actor, giữ dim, ảnh cũ, chống trùng source; explicit khóa/fresh read/đua hai chiều và kiểm lại sau khóa; lỗi save rollback toàn transaction; CHECK/FK thật ở PostgreSQL; schema roundtrip; hồi quy sweep/history. Mỗi save liên quan ảnh/publication/fault có đúng một audit.

Lượt kiểm tra trước có lỗi **test mới**, đã sửa: literal capability thực là `cap:execute_work_orders`; không đổi EF key `WorkOrder.CommuneId` trong helper; sau thu hồi xã phải đăng nhập lại để token phản ánh scope mới. Mã cuối không nới auth để làm test qua.

## Sabotage — chạy mutation thật rồi khôi phục

Mỗi mutation C# được build riêng (đều **exit 0**) rồi chạy test tương ứng trên container mới; Python chạy unittest thật. Kết quả đọc từ `subprocess.returncode`, không đoán từ dòng summary. Không tính compile failure là test bắt được lỗi.

| Mutation | Build exit | Test exit | Output rút từ terminal |
|---|---:|---:|---|
| `role_policy` — Bỏ capability trên PATCH review | 0 | 1 | 3 test đỏ |
| `assignee` — Bỏ kiểm tra own tường minh (filter cha vẫn còn) | 0 | 0 | 6 pass; filter cha vẫn chặn |
| `state_after_lock` — Bỏ Writable sau khóa cha | 0 | 1 | 1 test đỏ |
| `decided_guard` — Bỏ guard ảnh đã có decision | 0 | 1 | 2 test đỏ |
| `note_guard` — Bỏ guard note khi khác AI/null AI | 0 | 1 | 4 test đỏ |
| `noop` — Luôn ghi lại review giống hệt | 0 | 1 | 1 test đỏ |
| `effective_label` — Bỏ fallback nhãn review lúc công bố | 0 | 1 | 6 test đỏ |
| `borrow_AI_score` — Gán score AI cho nhãn người sửa | 0 | 1 | 2 test đỏ |
| `fault_source` — Ép fault người sửa thành cv | 0 | 1 | 3 test đỏ |
| `reporter_actor` — Gán reported_by thành Quản lý | 0 | 1 | 3 test đỏ |
| `photo_lock` — Bỏ explicit photo lock của review | 0 | 1 | 1 test đỏ |
| `stale_decision_read` — Decision dùng snapshot trước khóa | 0 | 1 | 1 test đỏ |
| `review_audit` — Bỏ audit review | 0 | 1 | 1 test đỏ |
| `DB_review_complete` — Nới CHECK bộ review thành true | 0 | 1 | 1 test đỏ |
| `DB_note` — Nới CHECK note thành true | 0 | 1 | 1 test đỏ |
| `DB_note_required` — Nới CHECK note bắt buộc thành true | 0 | 1 | 1 test đỏ |
| `DB_reviewer_FK` — Bỏ FK reviewed_by | 0 | 1 | 1 test đỏ |
| `DB_fault_provenance` — Nới CHECK photo-origin fault thành true | 0 | 1 | 1 test đỏ |
| `copy_reviewer` — Bỏ predicate reviewer khỏi PLAN copy | — | 1 | 2 subtest đỏ |
| `assignee_both_layers` — Bỏ cả own và query filter cha | 0 | 1 | 1 test đỏ |

**19/20 mutation làm test đỏ.** Một survivor có lý do: bỏ `Order(..., own:true)` vẫn bị query filter `IAssigneeScoped` của cha chặn. Đây là lớp phòng thủ còn hoạt động, không phải đã chứng minh riêng guard tường minh; mutation bổ sung bỏ cả hai lớp thì test kỹ sư `b` đỏ (expected 404). Không thêm test phản chiếu implementation để ép guard dư phải đỏ.

Trích output thật:

```text
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Api.Tests.dll (net10.0)
Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 2 s - LuxMap.Api.Tests.dll (net10.0)
RESTORED 19 mutants; 18 killed
RESTORED assignee_both_layers
```

Sau mọi mutation, source đã được khôi phục; build 0 warnings và 229 test pass ở trên là lượt cuối **sau khôi phục**, bao gồm hồi quy survey publication.

## OpenAPI export → generator → lint

```sh
DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false \
Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 \
dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py
npm_config_cache="$PWD/obj/npm-cache" npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
```

**Exit code: 0 / 0 / 0.** Output thật:

```text
Swagger JSON/YAML successfully written to /Users/nhm809/Documents/LuxMap/luxmap_backend-photo-review/docs/openapi/luxmap-v1.json
wrote docs/openapi/luxmap-v1.5.json: paths=103 implemented_ops=133 not_implemented_ops=0 schemas=245
docs/openapi/luxmap-v1.5.json: validated in 75ms

Woohoo! Your API description is valid. 🎉
You have 9 warnings.
```

Có 9 cảnh báo lint: thiếu license (1), hai URL localhost (2), description cạnh $ref của warranty_status (1), năm component chưa dùng (`FaultId`, `SweepId`, `FrameId`, `WorkOrderId`, `LatLng`). Đã lint cả bản HEAD lưu tạm trong `obj/`: **cũng 9 warnings, exit 0**. Không sửa các phần ngoài phạm vi chỉ để làm sạch cảnh báo có sẵn.

Kiểm tra JSON xuất ra (exit 0): cả hai spec có PATCH đúng capability/role, required final_cv_state enum on/off, note maxLength 2000, tám trường nullable review/decision, và `/decision` vẫn ReviewSurveys. Generator đã sửa mô tả `status_confidence` để không còn khẳng định null chỉ khi unknown.

## Files thay đổi

Không tính hai file đầu vào P1/task đã untracked từ đầu phiên. Danh sách source/doc/test/schema/result của thay đổi này:

```text
.ai/results/PHOTO-REVIEW-p2.md
CLAUDE.md
docs/contract-drift.md
docs/openapi/luxmap-v1.5.json
docs/openapi/luxmap-v1.json
docs/openapi/tools/gen_consolidated_spec.py
scripts/copy_dev_to_supabase.py
scripts/tests/test_pole_photo_copy_plan.py
src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs
src/LuxMap.Modules.Assets/Entities/PoleCurrentStatus.cs
src/LuxMap.Modules.Survey/Review/PoleStatusPublication.cs
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderPolePhotoConfiguration.cs
src/LuxMap.Modules.WorkOrders/Entities/WorkOrderPolePhoto.cs
src/LuxMap.Modules.WorkOrders/IPolePhotoPublication.cs
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoContracts.cs
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotosController.cs
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.Designer.cs
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs
src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs
tests/LuxMap.Api.Tests/AssetSchemaTests.cs
tests/LuxMap.Api.Tests/OpenApiSpecTests.cs
tests/LuxMap.Api.Tests/PolePhotoMigrationTests.cs
tests/LuxMap.Api.Tests/WorkOrderPolePhotoReviewTests.cs
tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs
```

`git diff --check` exit 0. `git diff --exit-code -- tracking.html` exit 0. Không commit, push, sửa tracking.html, chạy copy --apply, hay chuyển branch/worktree. Cache và script phá thử tạm nằm trong `obj/`; log terminal nằm trong `.ai/results/*.log` (gitignored), bằng chứng cần review đã trích ngay trong file này.
