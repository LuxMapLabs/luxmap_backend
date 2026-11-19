# REPAIR-STATUS — implementation handoff

Branch: `feat/REPAIR-STATUS-verify-normal`. User explicitly requested implementation without commit/push/migrations or API shape/enum changes.
Decision: final REPAIR-STATUS section in `docs/contract-drift.md`, RS-1…RS-6; SELF-SIGNED pending FW confirmation.

## Review entry points

- `WorkOrderService.Act`: repair verification transaction, work-order then ordinal pole locks, candidates from actual verified transitions,
  scoped pole lookup, stage through the new WorkOrders-owned port, outcomes on the existing single verify audit, save then commit.
- `Survey/Review/RepairStatusPublisher`: scoped pole reads, other open effective lamp faults excluding only newly verified IDs,
  normal publication through `PoleStatusPublication.StageAsync` using `CompletedAt`, null confidence/sweep/run; no save/audit of its own.
- `WorkOrderRepairStatusTests.cs`: partial `WorkOrderTests`, reuses isolated fixtures, evidence upload and teardown. Twelve cases cover
  normal publication, effective override, blockers inside/outside the order, timestamp precedence/fallback, duplicate candidates,
  pole-less non-lamp faults, complete/return, and an out-of-scope pole reference.
- `SurveyPublicationTests.Orders`: direct constructor updated. DI and Sync callers compile.

## Actual verification output

The requested `dotnet build --disable-build-servers` stalled without output. A diagnostic retry also stalled during parallel project evaluation;
both received cancellation. Single-node build completed from the repo root:

```text
$ dotnet build --disable-build-servers -m:1 > /private/tmp/luxmap-repair-status-build.log 2>&1
exit_code: 0
    46 Warning(s)
    0 Error(s)

Time Elapsed 00:00:07.62
```

All warnings were NU1900: unable to load `https://api.nuget.org/v3/index.json` for package vulnerability data.
`git diff --check`: exit 0, no output. Integration tests/manual API behavior not run: sandbox has no Docker; user will run them.
No tests or mutation/sabotage runs were attempted, per the build-only instruction.

The pre-existing edit in `docs/contract-drift.md` was preserved. No commit/push performed.

## Integration tests (Claude, outside the sandbox)

- `dotnet test tests/LuxMap.Api.Tests --filter` WorkOrderTests | SurveyPublicationTests | WorkOrderPolePhoto* | WorkOrderPoleGps* |
  WorkOrderCancellationTests | SyncTests | FaultReview*: **429 passed, 2 skipped, 0 failed**, exit 0.
- Break-tests, each restored with `cp` + `touch` and re-run green (13/13 `Repair_*`):
  1. drop `!verifiedFaultIds.Contains` + use `now` instead of `CompletedAt` → 7 red;
  2. disable the other-open-lamp-fault check → 2 red (`…another_lamp_fault_is_open`).
