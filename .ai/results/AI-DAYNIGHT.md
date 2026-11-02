# AI-DAYNIGHT — implementation and validation

User authorized integration and explicitly confirmed on 02/11/2026:
day => JSON string, low confidence => unknown, night => unchanged YOLO response.
API decision is recorded in docs/contract-drift.md (AI-DAYNIGHT); no schema/auth changes.

Implemented separate DayNightOnnxService, singleton DI, Ai:DayNight options,
model output/publish copy, lazy YOLO resolution in existing endpoint, OpenAPI oneOf.
YoloModel, YoloOnOffDetector and photo/survey callers are unchanged.

Verified model: SHA256 2666c67fece9909cd1de012d027c5350b4642e07e4c4838220ab4bb688fbf04b;
float images[1,3,224,224] -> output0[1,2], names day/night; terminal Softmax.
Real ONNX Runtime 1.20.1 load/inference passed; no dependency upgrade needed.

Command:
`dotnet test tests/LuxMap.Api.Tests --no-restore --disable-build-servers -m:1 --filter FullyQualifiedName~DayNightTests`
Actual terminal output (exit 0):
```
Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 2 s - LuxMap.Api.Tests.dll (net10.0)
```
Includes boundary/tie rejection, no YOLO registrations on day/unknown branches,
night response, real model inference, JPEG rejection, pixel limit, cancellation,
and RGB/center-crop tensor checks. Tests use synthetic images, not accuracy validation.

Swagger exported from compiled API with temporary non-production service configuration.
Generator must use `python -X utf8` on this Windows environment; initial cp1252 run
failed and was rerun successfully (no truncated output remains):
```
wrote ...luxmap-v1.5.json: paths=110 implemented_ops=141 not_implemented_ops=0 schemas=270
```
`npx --yes @redocly/cli lint docs/openapi/luxmap-v1.5.json` exited 0:
```
Your API description is valid.
You have 9 warnings.
```
Warnings concern license, localhost servers, ref sibling and unused components;
none concern the new response schema. `git diff --check` passed.

PostGIS integration tests (including AiDetectionTests) not run: Docker engine is unavailable:
```
open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.
```
No DB/data migration or .env edit performed. daynight.onnx was user-provided/untracked
before this task and is left intact. No commit created.

Limit: default Ultralytics preprocessing was matched from supplied training call and
upstream classify_transforms; numeric parity with the original Pillow notebook and
accuracy on real photos have not been measured. See API drift entry for provenance.


## Final branch handoff (02/11/2026)

The evidence above describes the initial implementation, not the final model/response.
User replaced the model; final SHA256 is
14ae7b00ab20c1ab185dbe95b08b668d949aefac160e408ae49df94aeea4edc0.
Both DayNightOptions and appsettings pin the new artifact. Direct .NET inference on the
460x717 supplied daytime image returned day=0.9955745, night=0.0044255476, decision=day.
Python/Pillow reference returned day=0.995530903339386, night=0.004469101317226887.
Exact Ultralytics 8.4.198 preprocessing source was subsequently inspected from its wheel.

Final responses: day/unknown have classification and confidence_percent (max class score * 100,
rounded to two decimals); night has those fields plus all existing YOLO fields. Unknown is
backend abstention, not a trained class. API drift entries record each user-authorized change.

Final build: `dotnet build tests/LuxMap.Api.Tests --no-restore --disable-build-servers -m:1 -v:q`
exited 0: Build succeeded, 0 Warning(s), 0 Error(s). Updated tests compiled but were not rerun
following the user's rejection of the test command for the new model. Earlier 10-pass result
applies to the initial model/response only. Final Swagger export and UTF-8 generator succeeded
(110 paths, 141 implemented operations, 271 schemas); git diff --check passed.

PNG support remains unimplemented. JPEG-only API remains unchanged in that respect.
.env was configured later for local development; it is ignored and excluded from this branch.
User now explicitly requested a new branch and push. All model and response changes are included.
