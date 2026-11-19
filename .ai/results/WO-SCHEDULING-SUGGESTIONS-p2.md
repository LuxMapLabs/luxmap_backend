# WO-SCHEDULING-SUGGESTIONS — implementation evidence

Base: `7d20bf3`, branch `feat/WO-SCHEDULING-suggestions`. No push, PR, deployment, schema or dependency change.
No changes to the original worktree. Reuses nullable pole geometry/import, PG GPS orders with manager verification,
existing offline commands, survey orders and shared overdue rules.

## Interface

- GET /api/v1/work-orders/suggestions: all five summary groups, including zero groups.
- GET /api/v1/work-orders/suggestions/items?kind=…: SQL paging and optional state filter.
- ManageWorkOrders: manager only. Commune scope and data-source filters precede aggregates and pagination.
- Already-planned warnings include the actual order/title/assignee/status/dates. Missing routes are explained.
- Undrawn roads remain schedulable per RG; segment_has_geometry is informational.
- Reads use RepeatableRead, do not write or emit audit/notifications. Commands retain their own validation.
- Contract drift SS-1…SS-5 and consumer guide recorded; source and consolidated OpenAPI generated.

## Build and focused regression — actual terminal output

```
dotnet build --disable-build-servers -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```
dotnet test tests/LuxMap.Api.Tests --no-build --disable-build-servers \
  --filter 'FullyQualifiedName~Scheduling_|FullyQualifiedName~A_pole_gps_order|FullyQualifiedName~Complete_keeps_the_report_on_the_order|FullyQualifiedName~SyncTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~RoleCapabilityMatrixTests|FullyQualifiedName~OpenApiSpecTests' \
  --logger 'console;verbosity=normal'
Test Run Successful.
Total tests: 191
     Passed: 191
 Total time: 20,1793 Seconds
```

13 new tests: 12 live HTTP/DB cases and one generated OpenAPI interface case. Verify real creation from suggestion
targets, assignment visibility, GPS done retaining the target, manager verify removing GPS warning and adding survey
warning, cancellation/release, source isolation, actual denied roles, unknown vs out, shared overdue rules,
multi-route source membership, finite-width ID ordering, real query pagination, and prerequisite information.

## Six mutation checks — actual exit codes

Each run modified one guard, ran its targeted test, observed the intended assertion failure, then restored the file
with copy and touch (no mv). Final build and focused regression above ran on restored production code.

```
default-source exit=1 expected_test_failed=True
unique-poles exit=1 expected_test_failed=True
id-width exit=1 expected_test_failed=True
unrelated-source exit=1 expected_test_failed=True
manager-policy exit=1 expected_test_failed=True
held-gps exit=1 expected_test_failed=True
```

## Full suite and independent baseline reproduction

This full run preceded the final metadata/OpenAPI adjustments and additional reviewer-requested assertions;
final production source was rebuilt warning-free and the focused 191-test regression above was rerun afterwards.

```
dotnet test --disable-build-servers -warnaserror --logger 'console;verbosity=minimal'
Passed! - Failed: 0, Passed: 12,  Skipped: 0 - LuxMap.IotBridge.Tests.dll
Passed! - Failed: 0, Passed: 47,  Skipped: 0 - LuxMap.Persistence.Tests.dll
Passed! - Failed: 0, Passed: 90,  Skipped: 0 - LuxMap.Infrastructure.Storage.Tests.dll
Passed! - Failed: 0, Passed: 447, Skipped: 0 - LuxMap.Shared.Tests.dll
Failed! - Failed: 1, Passed: 1942, Skipped: 7, Total: 1950, Duration: 17 m - LuxMap.Api.Tests.dll
exit code: 1
```

The only failure is IotDiagnosticsTests.Admin_registers_two_feeders_on_one_gateway_and_retries_without_creating_another_node,
line 43, registering an IoT server: expected 201 Created, actual 503 ServiceUnavailable. This is NOT reported as a passing full suite.
Reproduced independently in `/tmp/luxmap-scheduling-baseline`, a pristine detached worktree at `7d20bf3`, with the same
`.env.example` configuration and no feature changes:

```
dotnet test tests/LuxMap.Api.Tests --disable-build-servers \
  --filter 'FullyQualifiedName~IotDiagnosticsTests.Admin_registers_two_feeders_on_one_gateway_and_retries_without_creating_another_node' \
  --logger 'console;verbosity=normal'
Expected: Created
Actual:   ServiceUnavailable
Test Run Failed.
Total tests: 1
     Failed: 1
 Total time: 9,2560 Seconds
exit code: 1
```

## Scripts and generated API

```
python3 -m unittest discover -s scripts/tests
Ran 20 tests in 1.239s
OK
```

```
Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile \
  --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=158 implemented_ops=206 not_implemented_ops=0 schemas=340
npx --yes @redocly/cli lint docs/openapi/luxmap-v1.5.json
Your API description is valid.
You have 10 warnings.
```

Baseline generated spec at 7d20bf3 also has 10 warnings. A second export/generate was byte-for-byte identical.
No manual OpenAPI JSON edits. Endpoint paths use static `/suggestions/items` to avoid ambiguity with order child routes.

River reviews run via configured `river` provider, `deepseek-ai/DeepSeek-V4.1-Flash-1017`, separate read-only Standards
and Spec sessions. Their final reports and any resolutions are kept under `.ai/reviews/`.
