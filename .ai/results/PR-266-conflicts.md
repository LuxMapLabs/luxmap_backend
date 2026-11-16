# PR #266 — resolve conflicts with dev

Date: 16/11/2026. Worktree branch: `fix/PR-266-conflicts`.
PR head: `972a9270bd2eb9906bd357183190a5e85a73997c` (`iot`).
Merged base: `1e89827` (`origin/dev`, PR #265).

## Resolution

- Regenerated the consolidated spec from the merged raw spec, then re-exported the raw spec from the built API and regenerated again. The export matched the staged raw spec.
- Preserved the service-based lighting test helpers from dev and the optional bridge handler needed by PR #266.
- Adapted the new detach assertion to inspect the persisted command status, because dev removed the legacy `/lighting/commands` route and its history service.
- Preserved node detach, feeder electrical history, and 4 V diagnostics from PR #266; retained removal of the three legacy lighting HTTP routes from dev. No additional product behavior or migration introduced by this resolution.

## Verification evidence

```text
$ dotnet build LuxMap.slnx --disable-build-servers --no-restore -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ python3 -m unittest discover -s scripts/tests
Ran 20 tests in 1.416s
OK

$ python3 docs/openapi/tools/gen_consolidated_spec.py
wrote docs/openapi/luxmap-v1.5.json: paths=148 implemented_ops=190 not_implemented_ops=0 schemas=329

$ npx --yes @redocly/cli@2.0.0 lint docs/openapi/luxmap-v1.5.json
Your API description is valid.
You have 9 warnings.

$ git diff --exit-code -- docs/openapi/luxmap-v1.json docs/openapi/luxmap-v1.5.json
(no output; exit 0 after re-export and regeneration)
```

Structural checks on both specs: legacy `/api/v1/lighting` paths absent; DELETE `/api/v1/iot/nodes/{nodeId}` and GET `/api/v1/iot/feeders/{feederId}/electrical/history` present.

Full .NET regression suite used its disposable Testcontainers PostGIS database and a separate EMQX container/volume with MQTT E2E enabled:

```text
$ LUXMAP_MQTT_E2E=1 dotnet test LuxMap.slnx --no-build --no-restore --disable-build-servers --settings luxmap.runsettings --logger trx --results-directory /tmp/luxmap-pr266-test-results -m:1
Passed!  - Failed:     0, Passed:  1777, Skipped:     5, Total:  1782, Duration: 5 m 48 s - LuxMap.Api.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    87, Skipped:     0, Total:    87, Duration: 4 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 687 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   427, Skipped:     0, Total:   427, Duration: 285 ms - LuxMap.Shared.Tests.dll (net10.0)
Exit code: 0
```

Total: **2336 passed, 0 failed, 5 pre-existing skips**. TRX files are in `/tmp/luxmap-pr266-test-results/`.

Remote branches rechecked after resolution: dev `1e8982780b29d14f06cf2961565c5212c9cfbf80`, iot `972a9270bd2eb9906bd357183190a5e85a73997c`.
No push, PR merge, deployment, or application to a persistent database performed.
