# AVATAR-PNG — implementation evidence (19/11/2026)

Caller selection: optional `AllowedImageFormats` argument on `IImageSanitizer.SanitizeJpegAsync`, default `JpegOnly`.
AvatarService explicitly selects `JpegOrPng`; PublicReportService explicitly selects `JpegOnly`.
The sanitizer preserves gate → bounded read → magic → Identify/pixel cap → decode → orientation → resize → white background → metadata strip → JPEG encode.
Only the restricted JPEG+PNG configuration is added; keys, limits, content types and packages are unchanged.

Commands and terminal results:

```text
dotnet build --disable-build-servers -m:1
Build succeeded.
    0 Warning(s)
    0 Error(s)

dotnet test tests/LuxMap.Persistence.Tests --disable-build-servers --filter JpegOnlyDecodeTests
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2

dotnet test tests/LuxMap.Infrastructure.Storage.Tests --disable-build-servers
Passed!  - Failed: 0, Passed: 90, Skipped: 0, Total: 90
```

The original redirected build/test attempts hit MSBuild named-pipe sandbox errors and stalled.
Direct retries of both requested test commands passed; single-process (`-m:1`) build and both test suites also passed.
NuGet restore succeeded. No API integration tests were run: Docker/Postgres unavailable by task constraint.
The API test assembly compiled in the solution build.

Mutation: temporarily changed ThumbnailFactory's decode options to `Configuration.Default`.
A subprocess attempt aborted on VSTest's socket server (sandbox); direct retry produced:

```text
dotnet test tests/LuxMap.Persistence.Tests --disable-build-servers --filter JpegOnlyDecodeTests -m:1
Every_image_decode_in_src_uses_an_allowed_restricted_configuration [FAIL]
Assert.Empty() Failure: Collection was not empty
Failed! - Failed: 1, Passed: 1, Skipped: 0, Total: 2
exit code 1
```

Restored source with `cp`; same command passed 2/2, exit code 0.

Added API coverage: PNG persisted as JPEG (opaque and transparent), white pixels in original and thumbnail,
PNG IHDR over pixel cap, GIF rejection; expanded corrupt PNG cases and strengthened citizen rejection to use a valid PNG.
Added storage unit coverage for exact/partial signatures, explicit opt-in and rewind, and transparent PNG sanitizing.
Added guard coverage for the exact codec registrations.

No commit or git write commands, as requested. Progress and remaining verification are in tracking.html.

Final storage rerun after removing duplicate signature assertions: one attempt aborted with sandbox SocketException (13) in VSTest before executing tests; retried directly: 90 passed, exit code 0.


## Kiểm chứng ngoài sandbox (Claude, 19/11/2026)

- Thêm `JpegOnlyDecodeTests.Only_the_avatar_service_opts_into_png`; phá thử: `PublicReportService` chọn `JpegOrPng` → đỏ; bỏ `BackgroundColor(Color.White)` → `Transparent_png_is_sanitized_to_white_jpeg` đỏ (pixel `0,0,0`). Khôi phục → xanh.
- Storage 90/90, Persistence 47/47, Shared 446/446, IotBridge 10/10.
- Api.Tests: 1907 pass, 7 skip, 1 fail chập chờn `SurveyProcessingTests.Expired_lease_is_reclaimed_with_a_new_attempt` (ngoài vùng ảnh); chạy riêng cùng `AvatarTests` + `A_valid_png_is_still_415_for_citizen_photos`: 25/25 xanh.
