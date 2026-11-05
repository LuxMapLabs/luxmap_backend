# PR #197 — gate the manual /ai/detect endpoint with a day/night classifier

Review revision completed 04/11/2026; API decision dated 03/11/2026. Prepared on `codex/ai-daynight-classification`; publication branch `codex/ai-daynight-review-fixes`.
After verification, the user explicitly requested a new branch and push on 04/11/2026.

## Scope and decisions

Authoritative decisions: `docs/contract-drift.md`, AI-DAYNIGHT (Mỹ, 03/11/2026, SELF-SIGNED until FW).
This revision changes the HTTP 200 response schema; it does not change authorization or database schema.
Day/unknown: classification + confidence_percent + daynight_model_version only.
Night: those fields plus existing YOLO model_version/width/height/count/detections.
Unknown is confidence abstention, not a model class or an OOD detector. Gate only the manual endpoint;
survey, Citizen, pole-photo hints and YOLO logic remain unchanged. No artifact registry/database writes.

Implemented enum classification, metadata-hashed MediaArtifact, threshold (0,1) and 64-hex pin validation,
clean error text, action-scoped OpenAPI oneOf with disjoint enums and web consumer. Generated both specs;
no Contract edits, info.version bump or migration edits. Consolidated drift and updated region traps.

## Preparation

Merged origin/dev without rebase/conflicts: `ad3dd6c`. Docker Desktop 28.4.0 accessible with elevation.
Baseline `MSBUILDDISABLENODEREUSE=1 dotnet build --disable-build-servers` exited 0, 0 warnings/errors.
Existing ignored .env retained; no test runs against luxmap_dev. Testcontainers creates a separate PostGIS DB.

## Fixtures and D-items

D-1 resolved: user explicitly approved using their daytime image as a fixture in this chat on 03/11/2026.
`tests/LuxMap.Api.Tests/Fixtures/AI/day.jpg` is an unchanged copy of
`E:/LuxMap_Dataset/images/Screenshot 2026-11-02 143206.jpeg`: 62,227 bytes, 460x717,
SHA256 `e79115c015d728b66dfc28a2f89fe638c5bc3774ef3ba7ed2b0ed326ea7b1d21`.

`night.jpg`: locally generated ImageSharp JPEG, 2,479 bytes, 320x240,
SHA256 `97822f627e85540e7acad01ce2d3d33eb92e52b889f96e487bcc44c8cdfe1fad`.
Recipe: sky RGB(5,8,18) for y<155, otherwise ground RGB(12,12,14);
pole x>205 && x<210 && y>55 RGB(30,30,30);
lamp x>180 && x<210 && y>53 && y<59 RGB(250,240,190); SaveAsJpeg defaults.
No external source for synthetic night. Model score night=0.9989017. Both fixtures under 100 KB.
A previous synthetic daytime candidate scored night=0.8094924 and was rejected, not relabeled.
Current pinned model SHA256 `14ae7b00ab20c1ab185dbe95b08b668d949aefac160e408ae49df94aeea4edc0`.
Real-model tests assert exact Day and Night labels at default 0.7, not one-of-three/conditional assertions.
The real photo diagnostic (before this review) gave day=0.9955745. These two regression fixtures are not an accuracy benchmark.

## Commands and verification

PowerShell setup for test runs:
```powershell
$env:MSBUILDDISABLENODEREUSE='1'
$env:PATH='C:\msys64\mingw64\bin;'+$env:PATH
$env:PYTHONUTF8='1'
$filter='FullyQualifiedName~DayNightTests|FullyQualifiedName~A_manager_gets_the_exact_classification_union|FullyQualifiedName~Manual_ai_detection_has_a_disjoint_web_union'
dotnet test tests/LuxMap.Api.Tests --disable-build-servers -m:1 --filter $filter
```
Initial compile caught a wrong namespace import for MediaArtifact; corrected to Processing.Frames.
Focused run after correction:
```
Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23, Duration: 37 s
EXIT_CODE=0
```

### Mutation run A (all new tests exercised)

Backed up the four files in `.ai/results/.daynight-mutation/` with copy, then applied these literal mutations:
- DayNightOnnxService: swap Day and Night in Classify return; replace metadata confidence_threshold with constant 0.7f.
- DayNightOptions: remove validation body.
- AiController: replace both `dayNight.Artifact.Version` response assignments with `"broken"`.
- AiDetectOperationFilter: replace `OneOf =` with `AnyOf =`.
Ran the same focused command above, captured actual dotnet exit code before printing log:
```
Failed! - Failed: 17, Passed: 6, Skipped: 0, Total: 23, Duration: 23 s
EXIT_CODE=1
```
Red tests: class probability gate (3 cases), real fixture class order (2), real-model load exact Day (1),
invalid threshold (3), invalid pin (3), Artifact_tracks_model_and_settings (1),
Manual_ai_detection_has_a_disjoint_web_union (1), exact HTTP union (day/unknown/night: 3).
Assertions reported Day/Night reversal, Assert.Throws failures, unchanged artifact version,
anyOf presence, and expected daynight-stub versus actual broken. These are assertion failures, not compilation failures.
Restore command pattern (all four files):
```powershell
Copy-Item -LiteralPath .ai/results/.daynight-mutation/DayNightOnnxService.cs -Destination src/LuxMap.Modules.AI/Detection/DayNightOnnxService.cs
# Same Copy-Item for DayNightOptions, AiController and AiDetectOperationFilter.
# Touch restored files too: Windows Copy-Item may retain old LastWriteTime.
(Get-Item src/LuxMap.Modules.AI/Detection/DayNightOnnxService.cs).LastWriteTime=Get-Date
```

### Mutation run B — response shape

Force day/unknown through YOLO by replacing the gate predicate with `classification.Classification == (DayNightClassification)(-1)`;
hide AiDetectResponse.DaynightModelVersion using JsonIgnore. Initial attempt kept C# required and hit serializer 500;
that run (5 failed, exit1) is NOT evidence of key assertions. Repeated with the mutated property non-required to test JSON keys.
Command:
```powershell
dotnet test tests/LuxMap.Api.Tests --disable-build-servers -m:1 --filter 'FullyQualifiedName~A_manager_gets_the_exact_classification_union|FullyQualifiedName~Rejection_never_resolves_yolo'
```
Final run B output:
```
Failed! - Failed: 5, Passed: 0, Skipped: 0, Total: 5, Duration: 24 s
EXIT_CODE=1
```
All three HTTP cases failed `Assert.Equal() Failure: Collections differ` (HTTP 200 reached), e.g.
```
Expected: ["classification", "confidence_percent", "daynight_model_version"]
Actual:   ["classification", "confidence_percent", "count", "detections", "height", ...]
```
The two Rejection_never_resolves_yolo cases failed because YoloModel was resolved from an intentionally empty provider.
Restored AiController and AiDetectResponse using Copy-Item; explicitly touched all five restored files before full build/test.
First full run (03/11/2026): exit1. API 1268 passed / 19 failed / 7 skipped; Storage 39 passed / 15 failed;
Persistence45 passed; Shared386 passed. First survey setup failed FFMPEG_NOT_INSTALLED, leaving fixture rows and
causing subsequent administrative_unit unique-name failures. Storage media tests also lacked ffmpeg.
This is retained in local ignored `AI-DAYNIGHT-full-missing-ffmpeg.log`; it is not reported as a green run.

Resolved environment prerequisite using FFmpeg9.0.2 essentials portable from
[gyan.dev](https://www.gyan.dev/ffmpeg/builds/), linked by [FFmpeg](https://ffmpeg.org/download.html).
Downloaded archive SHA256 matched provider checksum:
`60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`.
Extracted outside repo; no machine-wide PATH change. Retry (04/11/2026), new Testcontainers DB:
```powershell
$env:PATH='C:\Users\Nguyen Tien Dat\.codex\visualizations\2026\09\22\01a0c730-76ad-7201-bff0-36858375a52b\ffmpeg\ffmpeg-9.0.2-essentials_build\bin;C:\msys64\mingw64\bin;'+$env:PATH
$env:MSBUILDDISABLENODEREUSE='1'
$env:PYTHONUTF8='1'
dotnet test --disable-build-servers -m:1
```
First retry on 04/11: exit1; Docker Desktop had stopped since the previous day. API 82 passed/1205 failed/7 skipped;
Storage52 passed/2 failed; Persistence45 and Shared386 passed. Preserved in `AI-DAYNIGHT-full-docker-off.log`.
Reopened Docker Desktop (28.4.0 confirmed before the next run).

D-2 (environment limitation, outside AI review code): FfmpegFrameExtractor.Resolve tests File.Exists on literal
ffmpeg/ffprobe names; on Windows it does not append .exe/PATHEXT. The two DescribeAsync media tests still failed with
only .exe files on PATH. For local verification only, copied the same verified binaries to extensionless names in
the portable directory (outside repo). No production/test source or assertion changed, no machine-wide PATH change:
```powershell
Copy-Item -LiteralPath "$ffmpegBin/ffmpeg.exe" -Destination "$ffmpegBin/ffmpeg"
Copy-Item -LiteralPath "$ffmpegBin/ffprobe.exe" -Destination "$ffmpegBin/ffprobe"
```
Windows resolver support remains a separate survey follow-up; this run uses the documented environment workaround.
Targeted verification of both previously failing DescribeAsync tests after this workaround:
`dotnet test tests/LuxMap.Infrastructure.Storage.Tests --no-build --filter FullyQualifiedName~Generated_vfr_frame_codes_and_rotation_match_actual_pts`
→ Passed2 / Failed0 / Skipped0, duration3s, exit0. Same source/binaries as the preceding full build.
Full run with corrected local environment (before the final additionalProperties assertion): exit0.
API1287 passed/7 skipped; Storage54 passed; Persistence45 passed; Shared386 passed.
Total1772 passed,7 skipped,0 failed (1779 tests). API duration4m42s; other projects6s/1s/459ms.
Seven existing skips: five multi-commune scenarios awaiting ACCT-1/D-6b review and two MQTT real-broker E2E tests.
Final schema correction verified by the full run below.

## OpenAPI

```powershell
$env:Swagger__Enabled='true'
$env:Cors__AllowedOrigins__0='https://localhost:3000'
dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
C:/msys64/mingw64/bin/python.exe -X utf8 docs/openapi/tools/gen_consolidated_spec.py
npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
```
Both export and generator exited0. Generator: paths=110, implemented_ops=141, not_implemented_ops=0, schemas=272.
Lint exited0: `Your API description is valid`, 10 warnings (license, 2 localhost servers, ref sibling,
6 unused components including new DayNightClassification, whose response enums are inline and disjoint).
The initially rejected lint execution was retried only after the user's explicit “ok chạy đi”.
Repeat export + generator from restored assembly exited0; SHA-256 of BOTH generated files identical before/after. No regeneration diff.

## Remaining (out of scope)

Follow-up only: bounded waiting queue/SERVER_BUSY, startup fail-fast, THIRD-PARTY-NOTICES,
model LFS/MinIO, and any survey pipeline gate. Notify WP5 about the SELF-SIGNED union before integration;
WP6/WP4 are not consumers of this endpoint. No message sent on user's behalf.
PR #197 title updated successfully via authenticated GitHub API (no push): `feat(ai): gate the manual /ai/detect endpoint with a day/night classifier`.
No open decision blocker after D-1 approval; D-2 is a documented local Windows environment workaround. Final self-review and full-suite evidence below.

Final schema inspection found Swashbuckle inherits additionalProperties:false for DTO objects.
The operation filter now explicitly allows additional properties, keeping the union disjoint through classification only.
OpenAPI test also asserts neither branch is closed. This edit initially hit CS0200 (IOpenApiSchema setter is read-only); corrected by using the generated concrete OpenApiSchema.
Mutation C: set `schema.AdditionalPropertiesAllowed = false`, then run:
```powershell
dotnet test tests/LuxMap.Api.Tests --disable-build-servers -m:1 --filter FullyQualifiedName~Manual_ai_detection_has_a_disjoint_web_union
```
Actual output:
```
Assert.False() Failure
Expected: False
Actual: True
at OpenApiSpecTests.cs:line 47
Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 10 s
EXIT_CODE=1
```
Restored filter using Copy-Item from scratch backup and set LastWriteTime=Get-Date before final full test.
Final generated specs and full-suite verification include this correction.


## Final verification — 04/11/2026

After restoring mutation C, rebuilt and ran the whole solution using the documented environment:
```
Passed! - Failed: 0, Passed: 1287, Skipped: 7, Total: 1294, Duration: 6 m 55 s - LuxMap.Api.Tests.dll
Passed! - Failed: 0, Passed: 54, Skipped: 0, Total: 54, Duration: 7 s - LuxMap.Infrastructure.Storage.Tests.dll
Passed! - Failed: 0, Passed: 45, Skipped: 0, Total: 45, Duration: 13 s - LuxMap.Persistence.Tests.dll
Passed! - Failed: 0, Passed: 386, Skipped: 0, Total: 386, Duration: 1 s - LuxMap.Shared.Tests.dll
EXIT_CODE=0
```
Total **1772 passed, 7 skipped, 0 failed**. Build phase succeeded; no source edits after this run except documentation.
Raw local logs are ignored; commands and actual summaries are retained here. No test is disabled by this revision.
Final OpenAPI export/generator ran twice from the restored assembly: both files byte-identical; lint exit0,10 warnings.
Both response schemas omit additionalProperties; enums are disjoint and no discriminator was added.

## Self-review against origin/dev

- [x] Three response branches: HTTP theory covers literal day/unknown/night, exact key sets; direct day/unknown tests do not register YOLO.
- [x] All three include daynight_model_version equal to classifier Artifact.Version; night asserts YOLO Artifact.Version separately.
- [x] oneOf, disjoint enum values, web consumer, confidence0..100 and no closed response objects: OpenAPI regression test and both generated specs.
- [x] All requested tests exist; mutations A/B/C caused assertion failures, restored by copy+touch, full solution exited0 afterwards.
- [x] Exactly one AI-DAYNIGHT drift definition; obsolete JSON-string decisions removed. AI-1 response explicitly superseded; AI-3 covers both pins.
- [x] Scope review: no migration, Contract, survey pipeline or YOLO logic changes; .env, downloads and raw logs not staged.
- [x] `git diff --check` clean; `rg` scan for the damaged UTF-8 marker across src/docs/.ai found no matches. No production mutation remains.

Commit/PR title: `feat(ai): gate the manual /ai/detect endpoint with a day/night classifier`.
Publication is authorized only on the new `codex/ai-daynight-review-fixes` branch; the PR #197 source branch is not pushed. D-1 resolved by user; D-2 documents the FFmpeg Windows prerequisite/workaround,
not a change to the survey pipeline. Follow-ups listed above remain outside this PR.
