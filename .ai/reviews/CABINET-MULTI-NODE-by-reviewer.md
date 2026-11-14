# CABINET-MULTI-NODE — independent review

Reviewed 14/11/2026 against `.ai/tasks/CABINET-MULTI-NODE.md` and the user-approved phase 1 scope.
Reviewer did not author or modify implementation files.

## Result

No actionable correctness, authorization, migration, or compatibility findings in the reviewed change.

- `IotNodeConfiguration` and `AllowMultipleIotNodesPerCabinet.Up` remove only cabinet-level uniqueness;
  required cabinet, commune filter/guard, composite commune FK, same-cabinet relay FKs, and Restrict remain.
- Registry removes the duplicate-cabinet check and its obsolete exception translation; concurrent registrations
  retain independent database-generated node IDs.
- `CabinetDeviceLookup` loads all scoped nodes once per page, sorts before grouping, and returns lists per cabinet.
  Assets keeps its dependency boundary through the existing port. Map reads all mounted nodes with the same ordering.
- Inventory and map DTOs retain nullable `iot_node_id`, computed from `iot_node_ids.FirstOrDefault()`;
  empty arrays and the legacy null value agree. Both exported OpenAPI files contain the array and preserve the scalar type.
- `Down` takes an ACCESS EXCLUSIVE lock before checking duplicates and restoring the unique index. It refuses a lossy
  downgrade without deleting or merging nodes; the migration test exercises preservation, down/up, rejection, and recovery.
- Command planning, delivery, ACK, MQTT ACL, credentials, and offline-job deduplication still use node identity.
  No phase 2 node-to-single-feeder restriction or relay/protocol removal was introduced.
- Regression coverage includes concurrent registration, raw database writes, inventory/list/map reads, creation-time and
  width-boundary ordering, cabinet movement, relay/credential isolation, and command delivery/ACK isolation in a shared cabinet.

## Verification by reviewer

```text
$ git diff --check
(no output; exit 0)
```

Read the migration Up/Down, snapshot diff, existing raw cabinet constraints, registry, lookup, map queries,
command service, broker authentication, node-offline job, new/updated tests, generated schema entries, and relevant traps.
Build/test executions are owned by the implementing agent; no duplicate full test run was started by the reviewer.
No dev/VPS migration, push, or deployment performed.

Pre-existing user edits in CLAUDE.md, registration v1.4 and earlier contract-drift sections were excluded from this review.
