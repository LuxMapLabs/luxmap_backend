# WSC-PHOTO — kết quả phase 2

Ngày 17/11/2026. Hiện thực theo quyết định Mỹ trong ticket; **không commit, không push**.
Không migration, endpoint, mã lỗi, project reference, request/response mới; không sửa Contract/OpenAPI,
`WorkOrderService.FollowUp` hay luồng `/decision`. PP-9/PP-14/PP-15 vẫn là nền tạm SELF-SIGNED tới FW kế tiếp.

## File đã sửa / thêm

- `src/LuxMap.Modules.Survey/Review/SurveyFindingLookup.cs`
- `src/LuxMap.Modules.Survey/Review/SurveyPublicationRules.cs`
- `src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs`
- `src/LuxMap.Modules.WorkOrders/ISurveyFindingLookup.cs`
- `tests/LuxMap.Shared.Tests/SurveyFindingTests.cs`
- `tests/LuxMap.Api.Tests/SurveyPublicationTests.cs`
- `docs/contract-drift.md`
- `docs/agents/traps/faults-workorders.md`
- `tracking.html`
- `.ai/results/WSC-PHOTO.md`

`.ai/tasks/WSC-PHOTO.md` đã untracked từ đầu phiên, giữ nguyên. `AGENTS.md` vẫn là symlink.

## Luật hiện thực

- Lookup đọc accepted-run history của các sweep đã duyệt và ảnh/clip của chính phiếu, đúng xã neo,
  `decision=confirm`; không lọc `status_applied`. Nhãn hiệu lực `final_cv_state ?? cv_state`.
- `SurveyFindingLookup.Merge` là hàm thuần, gộp riêng mỗi cột từ quan sát cũ tới mới.
  Sweep đặt trạng thái theo `classified_as`; ảnh OFF → out; ON giữ dim/normal, còn lại → normal.
  Luật ảnh nằm duy nhất ở `SurveyPublicationRules.PhotoStatus`, publisher và lookup cùng gọi.
  data_source lấy từ quan sát cuối cùng đổi trạng thái (Claude sửa khi review: ảnh on trên dim / off trên out giữ nguồn trước); cuối cùng mới `FaultFor` để lấy finding out/dim.
- Hoà giờ ảnh–sweep: sweep cuối cùng/thắng. Hai sweep giữ thứ tự `published_at`, độ dài ID, ordinal;
  hai ảnh theo `decided_at`, độ dài ID, ordinal (tăng dần). Không ORDER BY ID trần.
- Đã thêm WSC-6 (17/11/2026, Mỹ chốt, WP5 đã báo lỗi, không chạm API), cập nhật comment port/lookup,
  hướng dẫn nội bộ và tracking. Không sinh sự cố ở follow-up.

## Test mới

`tests/LuxMap.Shared.Tests/SurveyFindingTests.cs`: 7 method / **11 ca**:

- `Later_photo_folds_the_previous_sweep_classification` (5 ca: out/unknown/normal/dim + ON, normal + OFF).
- `Photo_only_and_engineer_label_are_used`.
- `Capture_order_wins_over_input_and_publication_order_and_all_steps_are_folded` (cả dim → off → on;
  chỉ lấy ảnh mới nhất rồi map sẽ sai).
- `Equal_capture_time_sweep_wins_even_when_photo_is_decided_later`.
- `Equal_photos_use_decision_time_then_id_length_then_ordinal_in_both_input_orders`.
- `Equal_sweeps_keep_publication_time_then_id_length_then_ordinal_tiebreak`.
- `Each_pole_has_its_own_state_and_final_observation_source`.

`tests/LuxMap.Api.Tests/SurveyPublicationTests.cs`: cùng class và `AssetDatabaseCollection` của WSC,
7 method / **9 ca tích hợp mới — CHƯA CHẠY**, vì sandbox không có Docker/Postgres:

- `Photo_only_survey_carries_the_confirmed_photo_fault` (AI off; AI on + final off), assert fault có origin ảnh,
  confirmed, nguồn cv/field_report và follow-up 201 mang đúng fault.
- `Undecided_or_rejected_photo_cannot_supply_a_follow_up_finding` (null/reject, vẫn có open fault để phát hiện lọt filter).
- `Confirmed_photo_findings_stay_in_their_order_and_anchor_commune`.
- `Confirmed_photo_not_applied_globally_still_counts_for_its_own_order`.
- `Sweep_out_then_newer_photo_on_carries_nothing`.
- `Sweep_dim_then_newer_photo_on_still_carries_lamp_dim`.
- `Older_photo_off_cannot_outlive_a_newer_normal_sweep`.

Fixture tạo ID qua DB, dùng publisher thật qua service quyết định, dọn fault trước ảnh rồi phiếu/cột/user.
Ca thành công gọi controller trực tiếp để assert CreatedResult 201 và dùng service/DB thật; chưa kiểm HTTP/auth pipeline.

## Kiểm chứng và output thật

SDK `dotnet --version`: `10.0.302`.

Lệnh build thường `dotnet build --disable-build-servers` đã thử nhưng không có output trong sandbox;
đổi CLI home sang `/tmp` và tắt workload resolver, chạy một node để tránh MSBuild node IPC.
Lượt restore online (cùng flags, chưa chỉ cache) exit 1 do sandbox DNS NuGet:

```text
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
error NU1301:   nodename nor servname provided, or not known (api.nuget.org:443)
    0 Warning(s)
    22 Error(s)
```

Dùng cache đã có sẵn, không thay dependency hay cấu hình repo:

```sh
MSBUILDENABLEWORKLOADRESOLVER=false DOTNET_CLI_HOME=/tmp/wsc-photo-dotnet \
DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
dotnet build --disable-build-servers -m:1 --ignore-failed-sources -p:NuGetAudit=false
```

Exit 0, output cuối thật:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:28.12
```

Đã thử VSTest (exit 1 trước khi chạy bất kỳ test nào):

```sh
MSBUILDENABLEWORKLOADRESOLVER=false DOTNET_CLI_HOME=/tmp/wsc-photo-dotnet \
DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
dotnet test tests/LuxMap.Shared.Tests --no-build --no-restore --disable-build-servers -m:1 \
  --filter 'FullyQualifiedName~SurveyFindingTests|FullyQualifiedName~SurveyPublicationTests'
```

```text
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint)
Test Run Aborted.
```

Fallback: harness tạm `/tmp/wsc-photo-harness/Program.cs` (không thêm vào repo) tham chiếu các DLL vừa build,
phản chiếu gọi mọi Fact/InlineData của hai class thuần `LuxMap.Shared.Tests.SurveyFindingTests` và
`LuxMap.Shared.Tests.SurveyPublicationTests`; assertion vẫn là assertion xUnit thật. Không thay thế
việc chạy suite qua VSTest hoặc test tích hợp. Lệnh chạy:

```sh
dotnet /tmp/wsc-photo-harness/bin/Debug/net10.0/WscPhotoHarness.dll
```

Output thật, exit 0:

```text
In-process unit assertions: Passed 39, Failed 0 (SurveyFindingTests + SurveyPublicationTests)
```

Phá thử trong source thật (mỗi lần build module Survey, copy DLL mới sang harness, chạy assertion rồi khôi phục source bằng ghi lại file):

```text
on_preserves_dim: exit 1; In-process unit assertions: Passed 37, Failed 2 (SurveyFindingTests + SurveyPublicationTests)
sweep_wins_ties: exit 1; In-process unit assertions: Passed 38, Failed 1 (SurveyFindingTests + SurveyPublicationTests)
```

Sau khôi phục, build lại toàn solution (cùng env như trên):

```sh
dotnet build --disable-build-servers -m:1 --no-restore
```

Exit 0, output thật:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.69
```

Copy DLL đã khôi phục sang harness và chạy lại: 39 pass, 0 fail (output phía trên).
`git diff --check`: exit 0, không output. Không chạy test tích hợp theo chỉ dẫn ticket.

✅ Verified: toàn solution và test project compile 0 warning/error; 39 ca assertion thuần qua harness;
2 phá thử đỏ, khôi phục xanh; diff không lỗi whitespace.

⚠️ Could not verify: VSTest (socket bị chặn); 9 ca tích hợp mới và suite API hiện có (thiếu Docker/Postgres);
HTTP end-to-end/manual trên API thật. Claude cần chạy `dotnet test --filter FullyQualifiedName~SurveyPublicationTests`
và các test ảnh cột/publisher liên quan ở môi trường có Docker.

📌 Remaining assumptions: không thêm giả định nghiệp vụ ngoài quyết định ticket. Lượt build offline không chạy NuGet audit.

## D-item

Không phát sinh D-item mới; WSC-6 ghi quyết định đã được Mỹ chốt, không cần quyết định kiến trúc/API bổ sung.

Commit message dự kiến (chưa commit):
`fix(work-orders): carry confirmed pole-photo faults into survey follow-up`.
