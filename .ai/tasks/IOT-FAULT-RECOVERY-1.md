# IOT-FAULT-RECOVERY-1

Authorized by user on 2026-11-26 after FE clarified the original lifecycle.

Scope: preserve detected on recovery and inspection creation; reuse open node/feeder/code faults on relapse; attach new codes only to one open/assigned inspection; Manager verifies IoT fault_present -> confirmed and fault_absent -> rejected; inconclusive/cancel/return do not reject. Add durable nullable fault_id and current fault_status to feeder diagnostic history. Generate OpenAPI, docs and a dry-run-first legacy NODE-004 maintenance script.

Do not edit FE/firmware, push/merge, run production migration or cleanup, or repurpose rejected as a pending state. Keep non-IoT inspection decisions unchanged. Preserve commune scope, audit and concurrency behavior.