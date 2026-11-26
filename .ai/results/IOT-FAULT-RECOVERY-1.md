# IOT-FAULT-RECOVERY-1 result

Implemented recovery and relapse audit/notifications without automatic rejection. Preserved detected during Inspection creation. IoT-only fault_absent rejection occurs on Manager verification. Existing fault_present confirmation and repair lifecycle remain intact.

New fault attachment uses the existing Faults port implemented in WorkOrders; explicit commune/node/feeder matching, one eligible inspection, open/assigned only, skip busy locks, audit and notices. Waiting lifecycle actions serialize with membership changes; review decisions retain xmin concurrency and the existing 409 contract.

Added nullable episode fault FK with composite commune constraint and Restrict delete. History reports the linked fault current status, not a guessed latest fault; old unlinked history remains null. Generated and reviewed migration 20261126093706_LinkIotDiagnosticEpisodesToFaults; model has no pending changes.

Generated both OpenAPI files and documented the lifecycle, nullable fields, rollout and maintenance. The operator script defaults to rollback, identifies only audited historical system rejections within COM-070/NODE-004/FDR-009/CAB-011 and three codes, preserves human decisions and FK references, backs up original rows and retains audits. Nothing executed against VPS.

Validation: solution build succeeded (0 warnings/errors). Final targeted API/PostGIS suite: 135 passed, 0 failed/skipped; covers recurrence, recovered inspection eligibility, history links, attachment states, Manager decisions, maintenance dry-run/apply/idempotency, OpenAPI and core WorkOrder/Fault review regressions. Broader earlier suite: 421 passed, 22 video failures due to missing ffmpeg, 2 test-string encoding failures subsequently fixed and passed in final suite. Existing concurrent verify/return regression was found, fixed without weakening its assertions, and passed in final suite.

No FE/firmware changed for this task. No commit, push, merge or deployment performed.