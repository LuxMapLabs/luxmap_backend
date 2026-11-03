# POLE-SEGMENT-NULL — PR 2b, bàn giao Codex (03/11/2026)

## 1. Phạm vi và thay đổi

Nền `feat/POLE-SEGMENT-NULL-2b-guards`, PR 2a đã có. Chỉ làm D-1, D-5, D-7, D-8 của 2b; nền API **SELF-SIGNED** tạm tới FW.
Không stage/commit/push. Không migration; không mở POST/PUT/import nullable; không sửa tracking.html theo yêu cầu ticket.
Không dùng `luxmap_dev`; các test mới dùng fixture `AssetImportFixture` → `LuxMapTestHost` → Testcontainers PostGIS.

- `Targets`: hai nhánh theo tuyến chỉ xét khi pole có tuyến; nhánh fault giữ nguyên. Upload vẫn dùng chung predicate trước/sau khoá.
- `WorkOrderPole`: segment/position nullable; cột chưa tuyến cuối theo created_at, length(id), id; đánh số cột có tuyến trước phân trang.
- Dời cột: bỏ truy vấn tuyến khi null; audit chọn **segment_id: null, segment_distance_m: null**. Cột có tuyến giữ corridor và lỗi cũ.
- FaultReport / StageReviewedReport / ReviewedPolePhotoPublisher / SurveyReviewService đã gán nullable trực tiếp, không cần sửa production.
  Dấu `!` ở CitizenReviewService:410 là **DTO đọc thuộc 2c**, không gỡ trong 2b; accept gọi StageReviewedReportAsync an toàn.
- Không thêm `POLE_SEGMENT_REQUIRED`: chưa có thao tác 2b cần từ chối vì thiếu tuyến; nơi dùng đầu tiên là topology cabinet ở **2c (D-3)**.

## 2. Rà từng hit WorkOrders (mục 6)

Mỗi dòng dưới tương ứng **một số dòng hit trong P1 §1**, không gộp bỏ hit PK, comment hay DTO.
Cột P1 là số dòng inventory gốc; cột hiện tại là nơi đã đọc trong worktree này (khi code 2b thêm dòng, có thể lệch).
Không sửa Service/Agenda/Photo vì không phát hiện null==null hoặc mất cột fault target ngoài predicate đã sửa.

| Hit P1 | Hiện tại | Sửa / không sửa và lý do |
|---|---|---|
| `WorkOrderService.cs:70` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:70` | Giữ: filter tuyến chỉ chạy khi client có tuyến cụ thể; không lọc mặc định. |
| `WorkOrderService.cs:92` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:92` | Giữ: response phiếu đã nullable, không đọc pole. |
| `WorkOrderService.cs:118` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:118` | Giữ: detail phiếu đã nullable. |
| `WorkOrderService.cs:124` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:124` | Giữ: WorkOrderSegment required, danh sách tuyến khảo sát thật. |
| `WorkOrderService.cs:132` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:132` | Giữ: WorkOrderFaultDetail.SegmentId vốn nullable; lịch sử từ fault. |
| `WorkOrderService.cs:146` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:146` | Giữ: inspection/repair không nhận segment_ids. |
| `WorkOrderService.cs:149` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:149` | Giữ: không nhận đồng thời fault_ids và segment_id; không bắt pole phải có tuyến. |
| `WorkOrderService.cs:150` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:150` | Giữ: chỉ phiếu inspection không fault mới bắt segment_id. |
| `WorkOrderService.cs:151` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:151` | Giữ: lỗi validation của request phiếu, không phải thiếu tuyến pole. |
| `WorkOrderService.cs:157` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:157` | Giữ: lookup road chỉ nhánh không fault, segment_id đã validate. |
| `WorkOrderService.cs:166` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:166` | Giữ: survey lấy segment_ids, không nhận pole. |
| `WorkOrderService.cs:169` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:169` | Giữ: survey chỉ tuyến thật, không nhận fault. |
| `WorkOrderService.cs:170` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:170` | Giữ: lỗi survey request sai; không nới target. |
| `WorkOrderService.cs:173` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:173` | Giữ: kiểm đủ RoadSegment được giao, PK non-null. |
| `WorkOrderService.cs:193` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:193` | Giữ: lưu WorkOrderSegment required. |
| `WorkOrderService.cs:194` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:194` | Giữ: audit danh sách tuyến survey, không phải pole. |
| `WorkOrderService.cs:203` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:203` | Giữ: danh sách road required từ segment_ids. |
| `WorkOrderService.cs:206` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:206` | Giữ: Contains loại pole null đúng phạm vi survey; không dùng cho inspection/repair qua fault. |
| `WorkOrderService.cs:289` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:289` | Giữ: chọn anchor từ fault có tuyến; không lọc tập fault của phiếu. |
| `WorkOrderService.cs:290` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:290` | Giữ: FirstOrDefault()?.SegmentId trả null cho phiếu chỉ có fault null tuyến. |
| `WorkOrderService.cs:301` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:301` | Giữ: nhánh road-only đã kiểm road tồn tại. |
| `WorkOrderService.cs:311` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:311` | Giữ: WorkOrder.SegmentId nullable; fault targets vẫn được link. |
| `WorkOrderService.cs:335` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:335` | Giữ: patch từ chối trường server-owned, không target pole. |
| `WorkOrderService.cs:381` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:381` | Giữ: chỉ RequireSurveyAssignee khi task_kind survey. |
| `WorkOrderService.cs:647` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:647` | Giữ: audit snapshot phiếu nullable. |
| `WorkOrderService.cs:658` | `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:658` | Giữ: record snapshot string? đã đúng. |
| `WorkOrderAgenda.cs:82` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:82` | Giữ: mảng ID tuyến thật, không chèn null. |
| `WorkOrderAgenda.cs:93` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:93` | Giữ: comment bucket theo xã khi không tuyến. |
| `WorkOrderAgenda.cs:96` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:96` | Giữ: group.SegmentId đã nullable. |
| `WorkOrderAgenda.cs:138` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:138` | Giữ: mô tả chọn tuyến đầu, không quyết định membership. |
| `WorkOrderAgenda.cs:164` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:164` | Giữ: surveyRoads từ WorkOrderSegment required. |
| `WorkOrderAgenda.cs:165` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:165` | Giữ: lookup tuyến survey, không lọc phiếu inspection. |
| `WorkOrderAgenda.cs:173` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:173` | Giữ: LEFT JOIN pole; pole tồn tại nhưng null tuyến trả null, không fallback về tuyến fault cũ. |
| `WorkOrderAgenda.cs:180` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:180` | Giữ: RoadsOf bỏ null ID nhưng không bỏ phiếu; fault/location vẫn trong lookup. |
| `WorkOrderAgenda.cs:187` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:187` | Giữ: Contains trên RoadSegment PK, không lọc pole hay phiếu. |
| `WorkOrderAgenda.cs:188` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:188` | Giữ: dictionary tuyến thật, không key null. |
| `WorkOrderAgenda.cs:190` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:190` | Giữ: đo tuyến thật theo firstRoads; không loại phiếu. |
| `WorkOrderAgenda.cs:191` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:191` | Giữ: khoảng cách của road, không phải pole nullable. |
| `WorkOrderAgenda.cs:192` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:192` | Giữ: dictionary road required. |
| `WorkOrderAgenda.cs:205` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:205` | Giữ: mảng SegmentIds rỗng hợp lệ; entries vẫn đủ tonight. |
| `WorkOrderAgenda.cs:211` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:211` | Giữ: bucket xã khi SegmentIds rỗng, không mất phiếu. |
| `WorkOrderAgenda.cs:217` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:217` | Giữ: segmentId null hợp lệ khi không tuyến. |
| `WorkOrderAgenda.cs:222` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:222` | Giữ: nhánh có road đã kiểm segmentId non-null. |
| `WorkOrderAgenda.cs:231` | `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:231` | Giữ: nhánh bucket xã và location từ fault/pole, segmentId nullable. |
| `WorkOrderPolePhotoService.cs:125` | `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:125` | Giữ: so sánh nullable string an toàn; Target gọi lại Targets sau khoá, đổi null→tuyến bị TARGET_CHANGED. |
| `WorkOrderPoles.cs:23` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:23` | Sửa: string? SegmentId; Position int? (dòng 26). |
| `WorkOrderPoles.cs:61` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:61` | Giữ: mô tả union theo tuyến và fault. |
| `WorkOrderPoles.cs:80` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:80` | Sửa: chặn p.SegmentId != null trước cả hai nhánh tuyến. |
| `WorkOrderPoles.cs:81` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:81` | Sửa: WorkOrderSegment nằm trong cùng guard; fault branch giữ nguyên. |
| `WorkOrderPoles.cs:88` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:88` | Giữ: đọc anchor phiếu nullable. |
| `WorkOrderPoles.cs:92` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:92` | Giữ: danh sách tuyến survey required. |
| `WorkOrderPoles.cs:93` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:93` | Giữ: chỉ thêm own khi non-null. |
| `WorkOrderPoles.cs:108` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:108` | Sửa: bỏ !, đọc CreatedAt để xếp nhóm chưa tuyến. |
| `WorkOrderPoles.cs:121` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:121` | Sửa: OfType<string> bỏ null chỉ khỏi tập road đọc geometry, không bỏ rows. |
| `WorkOrderPoles.cs:126` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:126` | Giữ: đọc geometry road PK qua IgnoreQueryFilters cho tuyến liên xã. |
| `WorkOrderPoles.cs:127` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:127` | Giữ: projection road required. |
| `WorkOrderPoles.cs:128` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:128` | Giữ: dictionary chỉ gồm ID tuyến thật. |
| `WorkOrderPoles.cs:134` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:134` | Sửa: chỉ IndexOf khi SegmentId non-null. |
| `WorkOrderPoles.cs:135` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:135` | Sửa: chỉ TryGetValue khi key non-null. |
| `WorkOrderPoles.cs:137` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:137` | Sửa: nhóm null cuối, created_at trước length(pole_id), pole_id trong nhóm đó. |
| `WorkOrderPoles.cs:144` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:146` | Sửa: không truy cập positions[null], trả int? null. |
| `WorkOrderPoles.cs:149` | `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:153` | Sửa: DTO nhận SegmentId nullable trực tiếp. |
| `WorkOrderRequests.cs:12` | `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:12` | Giữ: SegmentId request phiếu nullable, không phải writer Pole. |
| `WorkOrderRequests.cs:14` | `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:14` | Giữ: SegmentIds là tập tuyến survey. |
| `WorkOrderRequests.cs:38` | `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:38` | Giữ: presence-aware patch server-owned. |
| `WorkOrderRequests.cs:39` | `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:39` | Giữ: patch SegmentIds bị từ chối, không liên quan pole null. |
| `WorkOrderResponses.cs:13` | `src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:13` | Giữ: SegmentId phiếu vốn nullable. |
| `WorkOrderResponses.cs:37` | `src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:37` | Giữ: SegmentIds chỉ tuyến thật. |
| `WorkOrderResponses.cs:54` | `src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:54` | Giữ: WorkOrderFaultDetail.SegmentId vốn nullable. |
| `WorkOrdersController.cs:27` | `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:27` | Giữ: filter tuyến được client chỉ định, không lọc ngầm null. |
| `Entities/WorkOrder.cs:19` | `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:19` | Giữ: WorkOrder.SegmentId vốn nullable. |
| `Entities/WorkOrder.cs:77` | `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:77` | Giữ: WorkOrderSegment.SegmentId required; survey không nới. |
| `Configurations/WorkOrderConfigurations.cs:18` | `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:18` | Giữ: CHECK survey không dùng single SegmentId; không migration. |
| `Configurations/WorkOrderConfigurations.cs:54` | `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:54` | Giữ: FK anchor road nullable Restrict. |
| `Configurations/WorkOrderConfigurations.cs:63` | `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:63` | Giữ: index phiếu, không đọc Pole. |
| `Configurations/WorkOrderConfigurations.cs:97` | `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:97` | Giữ: unique WorkOrderSegment required. |
| `Configurations/WorkOrderConfigurations.cs:103` | `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:103` | Giữ: FK tuyến survey required Restrict. |

| `OpenApi/WorkOrderSchemaFilter.cs:35` | `src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:35` | Giữ: mô tả request inspection/repair; không ghi schema WorkOrderPole. |
| `OpenApi/WorkOrderSchemaFilter.cs:36` | `src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:36` | Giữ: survey vẫn cần segment_ids; không ép cột của phiếu non-null. |
| `OpenApi/WorkOrderSchemaFilter.cs:40` | `src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:40` | Giữ: chỉ bỏ server-owned fields khỏi PatchWorkOrderRequest; không áp vào WorkOrderPole. |

Các call site ngoài inventory nhưng đã đọc trực tiếp: PhotoService:45 (Target→Targets), :124 (Target sau khoá),
Poles:146–148 (vị trí nullable), Agenda:196–200 (location fallback), Service:275–318 (Insert giữ toàn bộ fault).
`WorkOrderSchemaFilter`: không có rule ép `WorkOrderPole.segment_id` non-null; rule `segment_id` hiện có chỉ bỏ trường khỏi PatchWorkOrderRequest.

## 3. Test đã viết và những gì CHƯA được kiểm runtime

Tất cả test mới **biên dịch được**; chưa có test nào được tuyên bố xanh nếu runner không tới assertion.

| Test | Nội dung |
|---|---|
| Work_order_poles_excludes_unrelated_unassigned_poles_and_lists_fault_targets_last | Inspection anchor null; null unrelated cùng xã bị loại; fault targets null được giữ; cột có tuyến trước; created_at ưu tiên; POLE-9999/10000 cùng transaction, assert free và timestamp chung; total/page đúng; agenda giữ phiếu |
| Work_order_pole_photo_uses_the_same_unassigned_target_policy | Unrelated bị 409 trước store; fault target được upload; NO KEY UPDATE chứng minh chờ pole lock; đổi tuyến trong store → TARGET_CHANGED; xoá link khi store → POLE_NOT_IN_WORK_ORDER; không thêm row |
| Relocating_unassigned_pole_follows_explicit_policy | Dời 400 m không corridor/500; audit segment_id và segment_distance_m null; kiểm toạ độ lưu. Test cũ giữ POLE_OFF_SEGMENT cho cột có tuyến |
| Fault_on_unassigned_pole_keeps_pole_location_commune_and_source | POST lamp_dim không location/tuyến; replay client_op_id; photo OFF confirm tạo lamp_out nullable, xã/nguồn/audit đúng; gán tuyến sau không sửa fault cũ |
| Survey_never_targets_unassigned_poles | Hai cột cùng toạ độ: chỉ cột có tuyến vào list survey và Snapshot→Poles INNER JOIN; survey không nhận fault_ids thay cho tuyến |
| Citizen_accept_on_unassigned_pole_keeps_location_commune_source_and_audits | Báo cáo đã gửi rồi tháo tuyến cột trước accept; fault nullable confirmed, location lấy từ pole, xã/nguồn/audit fault + citizen đúng. Không kiểm/đổi DTO đọc QR thuộc 2c |
| Survey_publication_after_unassigning_a_pole_keeps_the_old_snapshot_and_nullable_fault | Snapshot đã có rồi tháo tuyến trước accept; fault nhận null; gán tuyến sau giữ fault null, snapshot và history cũ |
| Work_order_pole_segment_and_position_allow_explicit_null | Assert schema Swagger runtime nullable string/integer |

## 4. Bằng chứng terminal thật

Build thường (không `-m:1`) không trả output trong sandbox; dùng CLI home trong `/private/tmp`, restore cache cục bộ,
không thay dependency của repo. `--disable-build-servers` + `MSBUILDDISABLENODEREUSE=1` giữ nguyên; serial build tránh MSBuild node/socket.
Lượt `--no-restore` đầu tiên báo NETSDK1004 vì worktree mới chưa có assets; đã restore rồi build lại thành công.

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2b-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false MSBUILDDISABLENODEREUSE=1 dotnet restore --source /Users/nhm809/.nuget/packages -p:NuGetAudit=false --disable-parallel --disable-build-servers -m:1 -p:BuildInParallel=false
```

```text
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Infrastructure.Storage/LuxMap.Infrastructure.Storage.csproj (in 6 ms).
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Telemetry/LuxMap.Modules.Telemetry.csproj (in 2 ms).
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Notifications/LuxMap.Modules.Notifications.csproj (in 2 ms).
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Map/LuxMap.Modules.Map.csproj (in 3 ms).
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Citizen/LuxMap.Modules.Citizen.csproj (in 3 ms).
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.AI/LuxMap.Modules.AI.csproj (in 3 ms).
  Restored /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Admin/LuxMap.Modules.Admin.csproj (in 3 ms).

[exit=0]
```

```bash
DOTNET_CLI_HOME=/private/tmp/luxmap-pole-2b-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false MSBUILDDISABLENODEREUSE=1 dotnet build --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false
```

```text
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.36

[exit=0]
```

```bash
DOTNET_CLI_HOME=/tmp/luxmap-pole-2b-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false MSBUILDDISABLENODEREUSE=1 dotnet test --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false
```

```text
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.

[exit=1]
```

Toàn solution: runner abort vì socket, **không có số test pass**. Sau khi thêm test schema/publication, chạy lại filter nhóm mới:

```bash
DOTNET_CLI_HOME=/private/tmp/luxmap-pole-2b-cli NUGET_PACKAGES=/Users/nhm809/.nuget/packages MSBuildEnableWorkloadResolver=false MSBUILDDISABLENODEREUSE=1 dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~unassigned|FullyQualifiedName~Relocating_unassigned|FullyQualifiedName~Survey_publication_after_unassigning|FullyQualifiedName~Work_order_pole_segment_and_position'
```

```text
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-pole-segment-null-2b/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.

[exit=1]
```

Sau lượt filter, chỉ tăng độ nhạy test thứ tự (ID older=10001) và đợi inference trước kiểm khoá; build cuối ở trên đã biên dịch lại. Không chạy lặp test vì blocker socket chưa đổi.

```bash
docker info --format '{{.ServerVersion}}'
```

```text

permission denied while trying to connect to the docker API at unix:///Users/nhm809/.docker/run/docker.sock

[exit=1]
```

### OpenAPI sinh từ code, không sửa tay JSON

`dotnet tool restore --ignore-failed-sources` thành công. CLI thường `Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1` treo không có output.
Thử trực tiếp CLI có timeout 45 giây, và `_tofile` có timeout 40 giây: đều timeout (exit 124), không sinh file.

Fallback **chỉ trong /private/tmp**: gọi `AddLuxMapJsonConventions`, `AddLuxMapApiConventions`, `AddLuxMapSwagger` thật và MVC application parts
của 13 module thật; dùng `ISwaggerProvider.GetSwagger("v1")` / `SerializeAsV3`. Không đăng ký DB, không Start host, không gọi `Program` của API.
Không sửa generator/config repo. Dưới đây là source exporter đầy đủ để tái lập nếu CLI vẫn treo; csproj console net10.0 có FrameworkReference
Microsoft.AspNetCore.App và ProjectReference tuyệt đối tới `src/LuxMap.Api/LuxMap.Api.csproj` của worktree này.

```csharp
using LuxMap.Api.OpenApi;
using LuxMap.Api.Http;
using LuxMap.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
var services = new ServiceCollection();
services.AddLogging();
var environment = new ExportEnvironment();
services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(environment);
services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
Console.WriteLine("ServiceCollection ready");
services.AddLuxMapJsonConventions();
services.AddLuxMapApiConventions();
services.AddLuxMapSwagger();
var controllers = services.AddControllers();
foreach (var assembly in new[] {
    typeof(LuxMap.Modules.Identity.IdentityModule).Assembly,
    typeof(LuxMap.Modules.Assets.AssetsModule).Assembly,
    typeof(LuxMap.Modules.Map.MapModule).Assembly,
    typeof(LuxMap.Modules.Survey.SurveyModule).Assembly,
    typeof(LuxMap.Modules.Faults.FaultsModule).Assembly,
    typeof(LuxMap.Modules.WorkOrders.WorkOrdersModule).Assembly,
    typeof(LuxMap.Modules.Telemetry.TelemetryModule).Assembly,
    typeof(LuxMap.Modules.Admin.AdminModule).Assembly,
    typeof(LuxMap.Modules.Notifications.NotificationsModule).Assembly,
    typeof(LuxMap.Modules.Sync.SyncModule).Assembly,
    typeof(LuxMap.Modules.Statistics.StatisticsModule).Assembly,
    typeof(LuxMap.Modules.AI.AIModule).Assembly,
    typeof(LuxMap.Modules.Citizen.CitizenModule).Assembly,
}) controllers.AddApplicationPart(assembly);
using var provider = services.BuildServiceProvider();
Console.WriteLine("Swagger provider ready");
var doc = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
using var writer = new StringWriter();
doc.SerializeAsV3(new OpenApiJsonWriter(writer));
File.WriteAllText(args[0], writer.ToString());
Console.WriteLine("Exported from actual MVC controllers and SwaggerSetup; no database registered, host not started.");

sealed class ExportEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "LuxMap.Api";
    public string EnvironmentName { get; set; } = "Production";
    public string WebRootPath { get; set; } = "";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}
```

Build exporter với các cờ serial/no-restore như build chính, sau restore cache cục bộ; output cuối:

```bash
DOTNET_CLI_HOME=/private/tmp/luxmap-pole-2b-cli dotnet /private/tmp/luxmap-p2b-export/bin/Debug/net10.0/export.dll /private/tmp/luxmap-p2b-export/generated.json
```

```text
ServiceCollection ready
Swagger provider ready
Exported from actual MVC controllers and SwaggerSetup; no database registered, host not started.

[exit=0]
```

So sánh JSON exporter với raw HEAD: chỉ `WorkOrderPole/properties/{segment_id,position}/nullable` được thêm `true`. Sau đó:

```bash
cp /private/tmp/luxmap-p2b-export/generated.json docs/openapi/luxmap-v1.json
python3 docs/openapi/tools/gen_consolidated_spec.py
```

```text
wrote docs/openapi/luxmap-v1.5.json: paths=110 implemented_ops=141 not_implemented_ops=0 schemas=270

[exit=0]
```

So cả raw và consolidated với HEAD theo object JSON (không chỉ đếm dòng), output thật:

```text
docs/openapi/luxmap-v1.json [('/components/schemas/WorkOrderPole/properties/position/nullable', None, True), ('/components/schemas/WorkOrderPole/properties/segment_id/nullable', None, True)]
PASS: nullable segment_id/position; no other schema or operation changes.
docs/openapi/luxmap-v1.5.json [('/components/schemas/WorkOrderPole/properties/position/nullable', None, True), ('/components/schemas/WorkOrderPole/properties/segment_id/nullable', None, True)]
PASS: nullable segment_id/position; no other schema or operation changes.
```

`npx --offline @redocly/cli lint …` báo ENOTCACHED; dùng executable Redocly **2.71.2 đã có trong npm cache** (không cài dependency):

```bash
node /Users/nhm809/.npm/_npx/1738445911c9ab26/node_modules/@redocly/cli/bin/cli.js lint docs/openapi/luxmap-v1.5.json
```

```text


docs/openapi/luxmap-v1.5.json: validated in 65ms

Woohoo! Your API description is valid. 🎉
You have 9 warnings.


[exit=0]
```

```bash
git show HEAD:docs/openapi/luxmap-v1.5.json > /tmp/p2b-baseline-spec.json
node /Users/nhm809/.npm/_npx/1738445911c9ab26/node_modules/@redocly/cli/bin/cli.js lint /tmp/p2b-baseline-spec.json
```

```text


../../../../../tmp/p2b-baseline-spec.json: validated in 64ms

Woohoo! Your API description is valid. 🎉
You have 9 warnings.


[exit=0]
```

Cùng 9 warning ở cùng JSON pointers trên HEAD và bản mới: info-license; hai server URL; warranty_status $ref sibling;
5 component thừa (FaultId, SweepId, FrameId, WorkOrderId, LatLng). Không đổi các phần đó của 2c.

## 5. Kế hoạch phá thử — CHƯA chạy được, không nhận exit 1 do host là bằng chứng đỏ

**Chưa có bằng chứng phá thử đỏ đúng assertion.** Cả VSTest và Docker bị sandbox chặn; không phá code rồi ghi nhận lỗi host là bắt được regression.
Claude cần chạy baseline (full suite + nhóm mới) trước, rồi từng mutation bên dưới riêng lẻ. Mỗi lượt lưu bản gốc bằng cp;
khôi phục trong cả trường hợp test lỗi bằng cp/touch, build/test lại, đọc **exit thật và thông báo assertion**.

| Chốt / test | Mutation độc lập | Đỏ đúng lý do dự kiến |
|---|---|---|
| Targets / Work_order_poles_excludes_unrelated_unassigned_poles_and_lists_fault_targets_last | Bỏ `p.SegmentId != null &&` trong Targets | Total/IDs xuất hiện cột unrelated; không chấp nhận NullReference/500 là lỗi mong muốn |
| Position / cùng test | Đổi `Position = position` thành `Position = position ?? 0` | JsonValueKind.Null của position nhận Number |
| Nhóm cuối / cùng test | Bỏ OrderBy nhóm null và đưa null lên đầu | Chuỗi ID expected bắt đầu onRoad bị sai |
| created_at / cùng test | Bỏ ThenBy created_at riêng nhóm null | POLE-10001 (cũ hơn) bị đẩy sau 9999/10000 |
| ID overflow / cùng test | Bỏ ThenBy PoleId.Length (giữ ThenBy PoleId) | 10000 đứng trước 9999 trong timestamp chung |
| Dời cột / Relocating_unassigned_pole_follows_explicit_policy | Thay ternary `pole.SegmentId is null ? null : await …SingleAsync` bằng `await …SingleAsync` | HTTP 500 thay vì 200 vì không có tuyến |
| Audit dời / cùng test | Đổi audit segment_distance_m null thành 0 | JsonValueKind.Null nhận Number |
| Corridor cũ / A_position_beyond_the_survey_corridor_is_409_and_writes_nothing | Tạm bỏ check khoảng cách | HTTP 200 thay vì 409 POLE_OFF_SEGMENT; DB/audit bị đổi |
| Ảnh / Work_order_pole_photo_uses_the_same_unassigned_target_policy | Cho Target bỏ kiểm membership từ Targets | Unrelated upload thành 201 thay vì 409 |
| Ảnh recheck / cùng test | Bỏ so fresh.SegmentId với target.SegmentId | Sau store đổi tuyến, nhận 201 thay vì TARGET_CHANGED |
| Ảnh membership sau khoá / cùng test | Dùng target cũ thay `fresh = await Target(…)` (giữ kiểm trước store) | Xoá link khi store vẫn ghi ảnh, thiếu POLE_NOT_IN_WORK_ORDER |
| Pole lock / cùng test | Bỏ SQL FOR UPDATE trên pole | Sau khi inference đã báo tới, upload kết thúc trong khi NO KEY UPDATE vẫn giữ; assertion bypass lock đỏ |
| Fault / Fault_on_unassigned_pole_keeps_pole_location_commune_and_source | Đổi nguồn copy từ pole thành DataSource.Field ở ReportAsync | Expected Simulated nhận Field (không lỗi FK) |
| Photo fault / cùng test | Đổi nguồn copy từ pole thành DataSource.Field ở ReviewedPolePhotoPublisher | Fault sinh từ ảnh có DataSource sai |
| Citizen / Citizen_accept_on_unassigned_pole_keeps_location_commune_source_and_audits | Đổi nguồn ở StageReviewedReportAsync thành Simulated | Expected Field nhận Simulated |
| Survey membership / Survey_never_targets_unassigned_poles | Đổi INNER JOIN điều kiện `p.segment_id = r.segment_id` thành `true` | Snapshot.Poles có thêm pole null cùng toạ độ; Assert.Single/DoesNotContain đỏ |
| Survey publish / Survey_publication_after_unassigning_a_pole_keeps_the_old_snapshot_and_nullable_fault | Tạm gán Fault.SegmentId từ SurveyPass của run thay vì pole hiện tại | Assert.Null(fault.SegmentId) đỏ, snapshot giữ nguyên |
| OpenAPI / Work_order_pole_segment_and_position_allow_explicit_null | Tạm thêm rule WorkOrderSchemaFilter cho WorkOrderPole ép segment_id string và position integer không JsonSchemaType.Null | Assert nullable của schema đỏ; không sửa tay JSON |

Khuôn chạy một mutation (thay file/tên test theo bảng; thực hiện trên môi trường chạy được Testcontainers):

```bash
cp src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs /tmp/p2b-WorkOrderPoles.cs.saved
# Áp ĐÚNG MỘT mutation trong bảng.
MSBUILDDISABLENODEREUSE=1 dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter 'FullyQualifiedName~Work_order_poles_excludes_unrelated_unassigned_poles_and_lists_fault_targets_last' > /tmp/p2b-sabotage.log 2>&1
sabotage_exit=$?
cat /tmp/p2b-sabotage.log
printf 'test exit=%s\n' "$sabotage_exit"
cp /tmp/p2b-WorkOrderPoles.cs.saved src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs
touch src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs
MSBUILDDISABLENODEREUSE=1 dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter 'FullyQualifiedName~Work_order_poles_excludes_unrelated_unassigned_poles_and_lists_fault_targets_last'
# Chỉ nhận bằng chứng khi mutation fail đúng assertion, bản khôi phục exit 0.
```

## 6. Để CV-15 (de CV-15)

`fault_cluster` chưa có writer nghiệp vụ trong `src/`. Không dựng writer hay nới schema trong 2b; vì không có luồng ghi,
không có chỗ gắn `POLE_SEGMENT_REQUIRED`/guard từ chối cluster nhận pole null để phá thử ở PR này.
Grep rộng còn thấy mô tả module, VersionedResultColumn và FK ở WorkOrders (ngoài PrefixedId/config/migration); đều không phải writer.
Seed `scripts/seed_mock_set.py` có INSERT cụm, **không** phải pipeline CV-15.

```bash
rg -n 'FaultCluster|fault_cluster' src --glob '!**/Migrations/**'
```

```text
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:536:    /// here, <c>fault.segment_id</c> and <c>fault_cluster.segment_id</c> in Faults. So a segment that
src/LuxMap.Modules.Assets/Crud/AssetsController.cs:279:    /// <c>fault_cluster</c> hold it with RESTRICT — two of those in another module — so the database
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs:26:public class FaultCluster : ICommuneScoped
src/LuxMap.Modules.Faults/FaultsModule.cs:9:/// Faults module — Fault, FaultCluster, status workflow and clustering (BE-18..BE-20, BE-40, BE-41).
src/LuxMap.Modules.Faults/FaultsModule.cs:10:/// As of BE-18 it owns the <c>fault</c> and <c>fault_cluster</c> schema (no <c>FaultHistory</c>: the
src/LuxMap.Modules.Faults/FaultsModule.cs:28:        services.AddSingleton(new VersionedResultColumn("fault_cluster", "fault_cluster", "clustering_model_version", VersionReference.Text));
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:26:public sealed class FaultClusterConfiguration : IEntityTypeConfiguration<FaultCluster>
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:28:    public void Configure(EntityTypeBuilder<FaultCluster> builder)
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:30:        builder.ToTable("fault_cluster");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:33:        builder.Property(cluster => cluster.ClusterId).HasPrefixedId(PrefixedIds.FaultCluster);
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:48:            .HasDatabaseName("ix_fault_cluster_segment_id");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:145:        builder.HasOne<FaultCluster>().WithMany()
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:167:        builder.HasIndex(fault => fault.ClusterId).HasDatabaseName("ix_fault_cluster_id");
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:55:        builder.HasOne<FaultCluster>().WithMany().HasForeignKey(x => x.ClusterId).OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Shared/Contracts/PrefixedId.cs:70:    public static readonly PrefixedIdSpec FaultCluster = new("CLS", 3, "cluster_id_seq");
src/LuxMap.Shared/Contracts/PrefixedId.cs:100:        SurveyFrame, Detection, LuxReading, WorkOrder, RepairEvidence, ExternalUnit, AppUser, FaultCluster,

[exit=0]
```

```bash
rg -n 'new FaultCluster|INSERT INTO fault_cluster' src --glob '!**/Migrations/**'
```

```text
[không có output; exit=1]
```

Câu chữ đề xuất (đã ghi đúng tầng vào `docs/agents/traps/faults-workorders.md`):

> Khi viết writer fault_cluster, phải từ chối cột chưa tuyến theo D-8, giữ schema cụm bắt buộc tuyến; không suy tuyến gần nhất,
> không viết lại cụm/snapshot cũ khi gán tuyến cột. Seed mock không phải pipeline CV-15.

## 7. Soát phạm vi và trạng thái bàn giao

`git diff origin/dev HEAD --stat` đã có task PR 2b từ nền (không phải file sửa lượt này):

```text
 .ai/tasks/POLE-SEGMENT-NULL-p2b.md | 96 ++++++++++++++++++++++++++++++++++++++
 1 file changed, 96 insertions(+)
[exit=0]
```

Untracked result/test không hiện trong `git diff --stat`; cần xem cả `git status --short`. Không stage để làm đẹp diff.
✅ Build 0 warning / 0 error; sinh schema từ code; generator; lint hợp lệ (9 warning như HEAD); diff --check.
⚠️ Không kiểm runtime PostGIS/HTTP/lock hoặc sabotage do VSTest socket và Docker bị chặn. Không tuyên bố test xanh.
📌 Giữ nguyên D-1…D-14; không có thay đổi chức năng 2c–2e. POST/PUT/import vẫn yêu cầu segment. tracking.html để Claude sau review.

```bash
git diff origin/dev --stat
```

```text
 .ai/tasks/POLE-SEGMENT-NULL-p2b.md                 | 96 ++++++++++++++++++++++
 docs/agents/traps/faults-workorders.md             | 10 +++
 docs/contract-drift.md                             |  3 +
 docs/openapi/luxmap-v1.5.json                      |  6 +-
 docs/openapi/luxmap-v1.json                        |  6 +-
 src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs |  8 +-
 src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs    | 28 ++++---
 tests/LuxMap.Api.Tests/CitizenReviewTests.cs       | 32 ++++++++
 tests/LuxMap.Api.Tests/OpenApiSpecTests.cs         | 10 +++
 tests/LuxMap.Api.Tests/PoleLocationTests.cs        | 25 ++++++
 tests/LuxMap.Api.Tests/SurveyPublicationTests.cs   | 37 +++++++++
 11 files changed, 241 insertions(+), 20 deletions(-)
[exit=0]
```

```bash
git status --short
```

```text
 M docs/agents/traps/faults-workorders.md
 M docs/contract-drift.md
 M docs/openapi/luxmap-v1.5.json
 M docs/openapi/luxmap-v1.json
 M src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs
 M src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs
 M tests/LuxMap.Api.Tests/CitizenReviewTests.cs
 M tests/LuxMap.Api.Tests/OpenApiSpecTests.cs
 M tests/LuxMap.Api.Tests/PoleLocationTests.cs
 M tests/LuxMap.Api.Tests/SurveyPublicationTests.cs
?? .ai/results/POLE-SEGMENT-NULL-p2b.md
?? tests/LuxMap.Api.Tests/WorkOrderUnassignedPoleTests.cs
[exit=0]
```

```bash
git diff --check
git diff --cached --name-only
git diff HEAD --name-only -- src/LuxMap.Persistence/Migrations src/LuxMap.Modules.Assets/Crud/AssetContracts.cs src/LuxMap.Modules.Assets/Import docs/api-contract-v1.1.md tracking.html
```

```text
[không có output; cả ba lệnh exit=0]
```

## 8. D-item / việc còn phải xác nhận

- **D-17 — bằng chứng runtime 2b chưa đủ do sandbox:** Claude chạy full suite, 8 test mới và các mutation riêng ở §5;
  lưu thông báo assertion + exit code. Chưa xác nhận test xanh, xử lý transaction/lock, teardown hay fault publication runtime.
  Đây là blocker nghiệm thu, không đề nghị mở lại D-1…D-14 hay lách sandbox.
- Không có mơ hồ nghiệp vụ mới cần chọn kiến trúc. Exporter fallback chỉ sinh lại đúng hai nullable flags; Claude có thể chạy CLI chuẩn
  và đối chiếu lại trước commit. Không cần sửa DTO/schema nào khác.
