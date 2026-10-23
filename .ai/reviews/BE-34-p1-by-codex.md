# BE-34 Phase 1 — review by Codex (`codex exec -p river`, DeepSeek-V4.1-Flash, read-only, 23/10/2026, ~237k tokens)

Verbatim output. Claude verified every P1 against the code before using it; corrections and the revised D-items are in `.ai/results/BE-34-p1.md` §5.

## Claims

- P3 CONFIRMED — 1.1 CHECK list: five components only, no firmware (`src/LuxMap.Modules.Survey/Configurations/SurveyProcessingConfigurations.cs:15`). Same CHECK also lacks `clustering_algorithm` and `profile`, which the survey does not flag.
- P3 CONFIRMED — 1.1 writers: `SurveyProcessor.Artifact()` hashes the Survey assembly and inserts (`src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:195-205`); `SurveyFramePipeline.RegisterArtifact()` writes `cv_model` / `frame_extractor` (`src/LuxMap.Modules.Survey/Processing/Frames/SurveyFramePipeline.cs:48,50,176-186`).
- P1 WRONG — 1.1 says “run, frame, detection, pass, baseline” carry version FKs. `SurveyPass` has only `run_id`, no artifact FK (`src/LuxMap.Modules.Survey/Entities/SurveyProcessing.cs:46-59`; `SurveyProcessingConfigurations.cs:73`); it inherits versions through the run.
- P3 CONFIRMED — 1.1 no registry API; the comment is at `src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:199`, and no controller exposes `artifact_version`.
- P3 CONFIRMED — 1.2 sweep fault stores `artifact_version.version` (`src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:272-273,302,308`).
- P3 CONFIRMED — 1.2 work-order pole photo stores both `model_version` and `artifact_version` (`src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:143-144`; `src/LuxMap.Modules.WorkOrders/Entities/WorkOrderPolePhoto.cs:30-31`).
- P1 WRONG — 1.2 says citizen photos store `model_version` + composite. They store only `ai_artifact_version` (composite); there is no `model_version` column (`src/LuxMap.Modules.Citizen/Entities/CitizenReport.cs:159-167`; `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:347-349`).
- P2 INCOMPLETE — 1.2 photo-origin fault stores the composite only for an AI label; a human label stores `null` (`src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:64`).
- P2 INCOMPLETE — 1.2 “same version string on both paths” holds only when the survey detector is `yolo` (`src/LuxMap.Modules.AI/AIModule.cs:26-28`; `src/LuxMap.Modules.Survey/SurveyModule.cs:43-51`); with `fake` the versions differ (`src/LuxMap.Modules.Survey/Processing/Frames/OnOffDetector.cs:54`).
- P2 WRONG — 1.2 “4 tables / 3 forms” undercounts: at least run, frame, detection, baseline, fault, work-order photo, citizen photo, fault cluster = 8 tables (`SurveyProcessingConfigurations.cs:50-54`; `SurveyFrameConfigurations.cs:26,46`; `SurveyPublicationConfigurations.cs:27`; `Fault.cs:172`; `WorkOrderPolePhoto.cs:30-31`; `CitizenReport.cs:165`; `FaultCluster.cs:54`).
- P3 — 1.2 dev counts (`0|33`, `1|1`) are not verifiable from source; the code is consistent with them only if no sweep/photo fault has been published.
- P3 CONFIRMED — 1.3 `iot_node` has no firmware column (`src/LuxMap.Modules.Telemetry/Entities/IotNode.cs:27-68`).
- P3 CONFIRMED — 1.3 MQTT heartbeat carries `FirmwareVersion` (`src/LuxMap.Modules.Telemetry/Mqtt/MqttMessages.cs:70`), but it is nullable and the handler accepts a null value.
- P3 CONFIRMED — 1.3 handler discards it and calls only `TouchAsync` (`src/LuxMap.Modules.Telemetry/Mqtt/MqttLightingHandler.cs:50-52`).
- P3 CONFIRMED — 1.3 HTTP poll/ACK have no firmware field (`src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:141,150-163`).
- P3 CONFIRMED — 1.3 parser requires `module_firmware_version_id` and `profile_id` (`src/LuxMap.Modules.Survey/Ingest/SurveyRawParser.cs:35,59`) and discards both.
- P3 CONFIRMED — 1.3 no `capture_profile` / `telemetry_reading` table and no firmware column on `survey_sweep` (`src/LuxMap.Modules.Survey/Entities/SurveySweep.cs:12-54`; no such tables in migrations).
- P3 — 1.3 `to_regclass` output is not source-verifiable; the code confirms the tables are absent.
- P3 CONFIRMED — 1.4 registry route and capabilities (`src/LuxMap.Modules.Telemetry/Registry/IotNodeRegistryController.cs:21,26,43,56,68,83,96`).
- P2 INCOMPLETE — 1.4 “admin API done”: registry writes are not audited (`IotNode` is not `IAudited`, `IotNode.cs:27`; `IotNodeRegistryService.cs:88,117,129`) and there is no firmware read.

## Missed

- P1 — `fault_cluster.clustering_model_version` exists for BE-34 but is never written (`src/LuxMap.Modules.Faults/Entities/FaultCluster.cs:46-54`; `FaultConfigurations.cs:35`; seed writes `NULL`, `scripts/seed_mock_set.py:186-195`; the trap says this is exactly BE-34’s job, `docs/agents/traps/faults-workorders.md:48`).
- P1 — The registry CHECK cannot hold a clustering algorithm or capture profile, despite ERD Q8 (`SurveyProcessingConfigurations.cs:15` vs `docs/database/erd.md:31`).
- P1 — `ck_artifact_version_hash` requires a 64-hex `artifact_hash` (`SurveyProcessingConfigurations.cs:16`; `SurveyProcessing.cs:12`). A device-reported firmware string has no such hash; the proposal does not say how to satisfy this.
- P1 — `artifact_version` is unique on `(component, version)` (`SurveyProcessingConfigurations.cs:23`); two different firmware binaries sharing a version string collide silently. `SurveyProcessor.Artifact` does not check hash conflicts (`SurveyProcessor.cs:200-205`), unlike `RegisterArtifact` (`SurveyFramePipeline.cs:184`).
- P1 — Making `IotNode` `IAudited` would require exactly one audit event on every `SaveChanges` touching it (`src/LuxMap.Persistence/Audit/AuditWriteGuard.cs:16-22`), but heartbeats save on every message (`LightingCommandService.cs:522-530`) and registry create/update/delete do not audit (`IotNodeRegistryService.cs:88,117,129`).
- P2 — `AuditEntityType` has no `IotNode` value (`src/LuxMap.Persistence/Audit/AuditEvent.cs:38`); adding one widens `ck_audit_event_entity_type` (`LuxMapDbContextModelSnapshot.cs:4209`), and narrowing it in `Down()` fails once such events exist.
- P1 — Telemetry does not reference Survey (`src/LuxMap.Modules.Telemetry/LuxMap.Modules.Telemetry.csproj:4-8`), but `ArtifactVersion` lives in Survey (`SurveyProcessing.cs:7`). The proposed `iot_node` FK and heartbeat insert cross a module boundary; a port or a registry move is required.
- P1 — Admin references Survey but not Faults, WorkOrders, Citizen or Telemetry (`src/LuxMap.Modules.Admin/LuxMap.Modules.Admin.csproj:4-7`); the proposed usage endpoint cannot count faults/photos without new references or ports.
- P1 — `telemetry_reading` (IOT-09) is still absent (`src/LuxMap.Modules.Telemetry/TelemetryModule.cs:10`); the MQTT `telemetry` topic is defined but unhandled (`MqttMessages.cs:31`; `MqttLightingHandler.cs:41-58`). Storing only the node’s current firmware cannot answer “which firmware produced this reading” after an upgrade.
- P2 — `app_version`, `phone_model`, `camera_id` are validated then discarded (`SurveyRawParser.cs:34`); `survey_sweep` has no columns for them (`SurveySweep.cs:12-54`).
- P2 — `LuxReading` records `MeterModel` but no firmware/version (`src/LuxMap.Modules.Survey/LuxReadings/LuxReadingContracts.cs:36-37`).
- P3 — `POST /ai/detect` returns `ModelVersion` but persists nothing (`src/LuxMap.Modules.AI/Controllers/AiController.cs:53-60`); it is a manual try, not a stored result.
- P2 — `fault.detection_model_version` is not exposed by `GET /faults` (`FaultItem.cs:13-42`) and has no index (`FaultConfigurations.cs:64-69`); the same is true of `citizen_photo.ai_artifact_version` (`CitizenReportConfigurations.cs:146`) and `work_order_pole_photo.model_version` (`WorkOrderPolePhotoConfiguration.cs:22`). The usage endpoint will scan.
- P2 — BE-15 P1 already put `module_firmware_version_id` on `survey_processing_run` (`.ai/results/BE-15-p1.md:125`) and `capture_profile_id` / `actual_capture_config` on `survey_sweep` (`.ai/results/BE-15-p1.md:112`); D-5 A proposes a different placement.
- P2 — The usage endpoint lists run/frame/detection/baseline/fault/photos but omits `pole_observation`, `luminance_history` and `survey_pass`, which are also results reachable through `run_id` (`SurveyProcessing.cs:46-59,61-88`; `SurveyPublication.cs:31-51`).

## Risks in the proposal

- P1 — D-2’s `IAudited` on `IotNode` is unsafe: it breaks every heartbeat and every registry write unless every save also adds an audit event (`AuditWriteGuard.cs:16-22`; `LightingCommandService.cs:522-530`; `IotNodeRegistryService.cs:88,117,129`). Use an explicit `IAuditTrail.Record` only on the firmware-change save instead.
- P1 — D-2’s FK `iot_node.firmware_version_id → artifact_version` crosses Telemetry → Survey (`Telemetry.csproj:4-8`; `SurveyProcessing.cs:7`). This violates the module-reference rule; use a port in Telemetry or move the registry.
- P1 — D-2’s “insert `node_firmware` like `SurveyProcessor.Artifact`” will fail the 64-hex hash CHECK (`SurveyProcessingConfigurations.cs:16`) and can silently alias different binaries under one version string (`:23`).
- P1 — D-3 writes firmware into `fault.detection_model_version` (`Fault.cs:161-172`; `NodeOfflineJob.cs:91-99`), the same column CV uses (`SurveyReviewService.cs:308`). The usage API can no longer distinguish “CV model X” from “firmware Y”; use a separate origin-firmware column.
- P1 — D-5 A reinterprets WP6’s integer as a server `artifact_version.version_id` (`SurveyRawParser.cs:35,59`; `SurveyProcessing.cs:9`). WP6 is offline-first and cannot know server-assigned IDs; rejecting a whole sweep with 409 because the firmware was not pre-registered loses field data.
- P2 — D-5 A also conflicts with BE-15 P1’s run-level `module_firmware_version_id` (`.ai/results/BE-15-p1.md:125`) and with the existing `capture_profile` design (`:112`).
- P1 — D-1’s scope omits the clustering result (`FaultCluster.cs:46-54`), telemetry readings (`TelemetryModule.cs:10`) and app/camera provenance (`SurveyRawParser.cs:34`), so the ticket cannot answer the task for those results.
- P2 — D-8 says `SELF-SIGNED` but omits the required “nền tạm tới FW” caveat and the WP4 / Đạt notifications (`docs/api-contract-v1.1.md:1063`).
- P2 — The migration plan only “widens” the component CHECK; it gives no safe `Down()` for the narrowed CHECK (`docs/agents/traps/migrations.md:105-108`). `Down()` must refuse or preserve firmware rows, not fail mid-rollback.
- P3 — D-6 uses `ManageSettings` for a read endpoint; that capability is `system_admin` only (`LuxMapPolicies.cs:159`), so it works, but it is a semantic stretch.
- P3 — `GET /admin/artifact-versions/{id}/usage` says “404 nếu không có”; distinguish “version not found” from “version exists with zero usage”, which should return zeros.
- P3 — `POST /admin/artifact-versions` inserts an `ArtifactVersion` that is not `IAudited` (`SurveyProcessing.cs:7`); a human registration decision would leave no audit trail.
- P3 — D-4’s “optional `firmware_version` in the poll” is a query parameter on a GET (`LightingContracts.cs:141`); it will land in access logs and is a weaker fit than a heartbeat body.
- P2 — D-7 says the usage API compares “the `model_version` part” of the photo string, but citizen photos have only the composite `ai_artifact_version` (`CitizenReport.cs:165`); the service must split on `+` there, and the survey does not define that rule once.