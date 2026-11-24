# WO-SCHEDULING-SUGGESTIONS — phase 1

User: implement the discussed backend suggestions; send to provider River for review; no PR without permission.

Verified checkout evidence (19/11/2026):
```
git show -s --format='%h %cs %s' origin/dev
7d20bf3 2026-11-19 Merge pull request #288 from LuxMapLabs/feat/iot-current-drop-deployment
docker info --format '{{.ServerVersion}}'
29.6.1
dotnet --version
10.0.302
```

D-1: reuse existing nullable Pole.Geom / imports and pole_gps_update workflow (PG-1…PG-13), not the redundant staging
table suggested while reading the older checkout. No new schema or write workflow is necessary.
D-2: expose two additive manager-only reads, sharing ManageWorkOrders: summary and paginated category details.
D-3: warnings remain visible in three states: ready, in_work_order, blocked. Scope and source filtering apply before
counts and paging. Default source is field; explicit CSV permits calibration_rig/simulated/public_imagery.
D-4: survey_needed means a located pole with absent/unknown latest published status; missing GPS is separate.
Missing route / invisible route / GPS route-commune mismatch block creation with a reason. Undrawn roads remain schedulable (RG); segment_has_geometry is informational.
D-5: no automatic scheduling, notification, write or persistent warning table. Existing commands revalidate on creation.
All decisions implement the user's authorized flow while preserving current dev's established data model.

API definition and exact semantics are recorded before implementation in docs/work-order-scheduling-suggestions.md
and docs/contract-drift.md. SELF-SIGNED foundation pending WP5/WP6 review remains explicit.
