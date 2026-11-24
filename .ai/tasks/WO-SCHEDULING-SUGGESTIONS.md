# WO-SCHEDULING-SUGGESTIONS

User authorized implementation and River review on 19/11/2026. **No push or PR.**

Base: origin/dev 7d20bf3. The original checkout was stale: current dev already supports nullable pole geometry,
imports without GPS, `pole_gps_update` orders, manager verification, and offline complete. Reuse these features;
do not introduce a duplicate staging inventory or another task kind.

Implement manager-only scheduling suggestion summary and paginated details: fault inspection, fault repair,
missing GPS, survey needed, overdue work orders. Scope by commune, segment and data source. Keep warnings that
already have work orders visible with their existing order, but do not suggest another creation. Explain blockers.
Distinguish unique poles from fault counts. Unknown is a survey reason, never a fault. No stale-survey threshold.
Use existing create/batch, GPS, survey, audit, offline and overdue rules. No automatic writes on reads.

Definition of done: HTTP integration tests including scoping, role, source separation, paging, actual creation from
suggestion targets, re-read after assignment/completion, updated generated OpenAPI and consumer documentation,
River review addressed, tracking updated and local commits. No migration, dependency change, FE change, deployment,
push or PR. Existing endpoints and enums remain compatible.
