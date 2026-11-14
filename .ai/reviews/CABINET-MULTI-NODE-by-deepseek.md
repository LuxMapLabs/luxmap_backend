# CABINET-MULTI-NODE — river / DeepSeek review

Requested by Mỹ in chat on 14/11/2026. Both axes ran independently through the configured `river` provider.
No production code, database, migration or uncommitted user work was modified by this review.

## Execution

- Harness: Codex CLI `0.164.0`.
- Provider: `river`.
- Model confirmed by both runner banners: `deepseek-ai/DeepSeek-V4.1-Flash-1017`.
- Reasoning effort: `high`; sandbox: `read-only`; approval: `never`; ephemeral sessions.
- Reviewed commit: `6c7c9793dbbd7ff417648b420a56756da2dbd22a`.
- Fixed base: `b280775b38666561e84d1bea504220e8c1b5b04a`.
- Diff: `git diff b280775b38666561e84d1bea504220e8c1b5b04a...6c7c9793dbbd7ff417648b420a56756da2dbd22a`.
- Both review processes exited `0`. Neither reran builds/tests or changed source.
- Spec sources: `.ai/tasks/CABINET-MULTI-NODE.md`, `.ai/results/CABINET-MULTI-NODE-p1.md`, drift CAB-MN-1…4.
- Standards sources: `AGENTS.md` / `CLAUDE.md`, relevant traps, `.ai/README.md`, plus the code-review skill smell baseline.
- Standards session: `01a1dacc-c088-76b1-a49b-f0fb4792fbcc`.
- Spec session: `01a1dacc-c088-7b33-b1a4-87eea24e422c`.

Invocation per axis (prompt supplied on stdin, final report captured outside the repository):

```text
codex --no-daemon -p river -a never exec --ephemeral -s read-only \
  -C /Users/nhm809/Documents/LuxMap/luxmap_backend --color never -o <temporary-report> -
```

Line numbers in the raw reports below refer to the reviewed commit, not the dirty working tree.
Reports are preserved separately; findings are not merged or reranked across axes.

## Standards

**AXIS: STANDARDS** — commit `6c7c979` vs base `b280775`

**Verdict:** no P0/P1/P2 defect in code, migration, or tests. Two P3 documentation findings. Builds/tests not re-run per instruction.

**F1 — P3 — `docs/contract-drift.md:1123` (CAB-8) still publishes the old read shape**
- Trigger: WP5 implements the cabinet screen from drift layer 2, reading CAB-8 literally: item fields listed as `… feeder_ids[], iot_node_id|null …`, with no `iot_node_ids[]`.
- Incorrect result: one device per cabinet rendered; a cabinet's second and later nodes silently missing, even though `CAB-MN-2` (`docs/contract-drift.md:1608`) and the shipped response add the array. The same commit amended CAB-3 (`:1118`) with a strikethrough pointer but left CAB-8 untouched, so the drift file now contradicts itself. `docs/openapi/tools/gen_consolidated_spec.py:167,306` also still label this surface `drift CAB-1…CAB-8`.
- Rule: project doc “Nguồn sự thật” item 2 — drift entries are the operative decision record; AGENTS.md §10 requires docs describing the changed thing to be updated. Fix is one CAB-8 amendment mirroring CAB-3.

**F2 — P3 — stale SRS/ERD inputs contradict the new cardinality**
- `docs/srs/inputs/business-rules.md:41` (BR-16: “mỗi tủ tối đa một thiết bị … CAB-1…CAB-8”), `docs/srs/inputs/physical-erd.md:194` (still names `ux_iot_node_cabinet_id` as the DB enforcement), `docs/srs/inputs/conceptual-erd.md:36` (R-07 `1 : 0..1`), `docs/srs/inputs/logical-erd.md:23`.
- Trigger: traceability review or SRS submission cites BR-16 → asserts a constraint the index no longer provides and an index name that no longer exists. Rule: AGENTS.md §10; BR-20/traps rule that a rule is enforced where it says it is.
- Caveat: these files are dated 27/10/2026 point-in-time extracts (`business-rules.md:3`) and were not in this commit. If the team treats them as frozen inputs, this is optional; otherwise update in the same change that moved the rule.

**Checked, no finding:** commune filtering on lookup/list/detail/map; `created_at, length(node_id), node_id` on all three surfaces; scalar legacy field = first element; composite FKs and `Restrict` intact; `Up` only swaps unique→ordinary index; `Down` preflight refuses instead of deleting; relay/MQTT/credential scope unchanged.

**Smell judgement:** the duplicated `IotNodeId => IotNodeIds.FirstOrDefault()` in `AssetContracts.cs` and `MapProperties.cs` is duplicated code, but extraction would cross the documented Assets/Map module boundary — not flagged. No other baseline smell matters to these hunks.

## Spec

**AXIS: SPEC**

No actionable findings.

The committed implementation matches `CABINET-MULTI-NODE` phase 1 and decisions D-1..D-4 / CAB-MN-1..4:

- Cabinet → node cardinality is now 0..n; the service-level “cabinet already taken” check is removed and the replacement index is non-unique.
- Nodes still require a same-commune cabinet through the unchanged composite FK and `Restrict`.
- Inventory list, cabinet detail, and `/map/cabinets` expose ordered `iot_node_ids[]`, empty when absent; legacy `iot_node_id` evaluates to the first item or `null`.
- Ordering is consistently `created_at, length(node_id), node_id`.
- Read paths remain commune-scoped; no cabinet-keyed leakage or per-node credential, relay, command, or MQTT mixup was found.
- The migration changes only the cabinet index; `Down` preserves data and explicitly refuses rollback when any cabinet has multiple nodes.
- Relay/feed and phase-2 behavior remains intentionally unchanged.

Review was limited to the specified committed diff and supporting committed/current source; no files were modified and tests were not rerun.

## Verification of reported documentation findings

- F1 confirmed: the CAB-8 historical row still lists only `iot_node_id`; CAB-MN-2 correctly documents the array and
  supersedes the old shape. This is a P3 clarity / cross-reference issue, not evidence of a response or runtime defect.
  The OpenAPI schema and operation summaries already expose `iot_node_ids[]`; the generator's contract reference metadata
  still points at CAB-1…CAB-8.
- F2 confirmed: the four cited SRS / ERD input files contain the old one-node cardinality / old unique index.
  They were collected on 27/10/2026 and are outside this commit. Keep the reviewer's caveat: treat this as a documentation
  follow-up if those drafts remain living inputs; historical validation figures should not be rewritten as fresh measurements.
- No fixes made as part of this review. No phase 2 work, dev/VPS migration, deployment, push or external team notification.

Summary: Standards **2 P3 documentation findings (F2 conditional)**; Spec **0 findings**.
Worst Standards severity: **P3**. No P0/P1/P2 code, migration or test defect reported. Worst Spec severity: **none**.
