# POLE-SEGMENT-NULL — Phase 1: khảo sát cột chưa phân tuyến · 02/11/2026

**Chỉ khảo sát; chưa hiện thực, chưa tạo migration.** Nền đọc: branch `docs/POLE-SEGMENT-NULL-p1`, HEAD `89c4ce218faf79d90fa09a81ad7b197c3349b525`; working tree sạch lúc bắt đầu, `AGENTS.md -> CLAUDE.md` (E00). Đã đọc `AGENTS.md` → `.ai/README.md` → ticket theo thứ tự người giao yêu cầu; đọc thêm sources/commands, các trap được ticket chỉ định và các đoạn liên quan của Contract/ERD/drift. Không dùng tài liệu bên ngoài.

Bốn quyết định của Mỹ trong ticket được giữ nguyên: chỉ nới `pole.segment_id`; null = **chưa phân tuyến**, không được giao khảo sát; Quản lý lọc được và thống kê đếm riêng; đường không chấp nhận phải báo lý do. **Mọi “đề xuất” dưới đây chờ Mỹ chốt**, không phải quyết định mới.

**Cách đọc bằng chứng:** E00–E23 ở phụ lục ghi lệnh đã chạy + stdout thật. E01 là grep bao phủ bắt buộc (chỉ thêm `--exclude-dir` đúng ba thư mục ticket cho loại); E02 bổ sung tests; E03 bổ sung `segment_external_ref` và join; E18 đọc cấu trúc JSON bằng Python chỉ đọc. `File:dòng` trong bảng đối chiếu trực tiếp được với output đó. Cột “nếu null” là **phân tích tĩnh giả định đã nới NOT NULL**, không phải kết quả HTTP/SQL chạy thật; hiện schema không cho lưu ca ấy (E04). Không khẳng định EF sẽ sinh kiểu JOIN nào khi chưa có SQL thực tế.

## 1. Bản kiểm kê consumer

### 1.1 Toàn bộ hit bắt buộc `src/`, `scripts/`, `mocks/`

Đối chiếu E22: **331 hit / 64 file** của E01; **240 hit / 52 file** test E02 đều có hàng ánh xạ. Mỗi hàng liệt kê **tất cả số dòng match của file**, kể cả hit gián tiếp/PK/comment; `n-a` giải thích tại hàng thay vì bỏ khỏi kiểm kê. Các cột test dùng mã kế hoạch ở mục 6. Nguồn: E01; đọc thêm ngữ cảnh E03–E16.

| File:dòng | Làm gì với segment | Hành vi HIỆN TẠI nếu segment null (phân tích tĩnh) | Đề xuất (chờ duyệt) | Cần test |
|---|---|---|---|---|
| `mocks/mock-faults.json:10,33,56,79,102,125,148,171,194,217,240,263,286,309,332,355,378,401,424,447,470,493,516,539,562,585,608,631` | Ví dụ segment_id của fault | Bộ ví dụ gán tuyến; không có ca null trong các hit | show_unassigned thêm ví dụ sau duyệt D-11 | T04/T12 |
| `mocks/mock-pole-detail.json:3` | Ví dụ pole detail segment_id | Ví dụ non-null; chưa mô tả cột chưa phân tuyến | show_unassigned D-11 | T03/T12 |
| `mocks/mock-work-orders.json:9,29,45` | Ví dụ tuyến của phiếu | Không phải writer pole; giữ ví dụ tuyến hiện có | n-a; thêm ca phiếu null theo D-1/D-11 | T05/T12 |
| `scripts/copy_dev_to_supabase.py:90` | PLAN road_segment key segment_id | Hit là PK; pole nằm PLAN lọc commune (E15), không join bắt buộc tuyến tại PLAN | n-a; kiểm copy null ở P2 nếu có môi trường riêng | T12 |
| `scripts/seed_mock_set.py:93,94,97,140,142,146,148,158,160,181,186,199,205,243,245,259` | INSERT pole từ properties segment_id; map feeder theo tuyến; seed fault/cluster/WO và sequence | quote(p[segment_id]) nhận null được (E21) nhưng demo_feeder[p[segment_id]] không có key null; cụm seed yêu cầu đúng một tuyến | refuse mock đầu vào sai; fixture unassigned riêng D-11 | T12 |
| `src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:35,36,40` | Schema theo task_kind, xoá server-owned SegmentId ở patch | Schema phiếu, không phải PoleListItem; thông điệp survey vẫn segment_ids | n-a (giữ ngữ nghĩa survey) | T12 |
| `src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:86,88,198,226,246` | Pole.SegmentId required; FK Restrict; PK RoadSegment | Không ghi được null; FK tuyến không ghép commune | show_unassigned (chỉ quan hệ Pole) | T01 |
| `src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:109,314,388,490,636` | Create/Update [Required]; TopologyPole/PoleListItem required string; SegmentListItem PK | Binding từ chối null đầu vào; DTO đọc chưa khai nullable | show_unassigned; PK SegmentListItem n-a | T01/T02/T03 |
| `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:42,111,135,259,294,304,391,477,486,535,536,547,728,742,749,828,830,879,966,1008,1020,1172` | POST/PUT kiểm tuyến và gán; list/detail chiếu trực tiếp; topology theo tuyến/mạch; relocation đo tuyến; count tuyến | POST/PUT null bị chặn; detail tên rỗng qua ??; list không join Segment; relocation SingleAsync không có hàng; DTO topology vẫn non-null | show_unassigned (list/detail/feeder); refuse hay cho dời: D-5; exclude (list theo tuyến) | T01/T02/T03/T09 |
| `src/LuxMap.Modules.Assets/Entities/Pole.cs:34` | Entity + navigation Segment bắt buộc | DB/model chặn null; đổi property mà giữ navigation required chưa đủ | show_unassigned | T01 |
| `src/LuxMap.Modules.Assets/Entities/RoadSegment.cs:20,30` | PK tuyến và chú thích đếm cột theo tuyến | PK không nullable; count theo tuyến không đếm null | n-a (PK); exclude (count theo tuyến) | T06 |
| `src/LuxMap.Modules.Assets/Import/AssetImportService.cs:424,437` | Upsert gán segment!.Id ở cả nhánh cập nhật và tạo | Resolve required:true + RequiredColumns chặn null trước ghi (E06) | show_unassigned; tham chiếu không khớp vẫn refuse theo dòng | T02 |
| `src/LuxMap.Modules.Assets/Import/ImportContracts.cs:19` | Chú thích thứ tự nạp vì pole.segment_id NOT NULL | Chú thích sẽ sai sau thay đổi; không phải một writer khác | n-a (sửa mô tả ở P2) | T02 |
| `src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs:72` | CitizenReportPole.SegmentId required string | Response chưa mô tả null | show_unassigned | T08 |
| `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:366,410` | Chiếu Pole.SegmentId sang chi tiết báo cáo | Không join tuyến; truyền null vào DTO non-null nếu có dữ liệu ấy | show_unassigned | T08 |
| `src/LuxMap.Modules.Citizen/PublicReportService.cs:72` | QR lookup tra tên tuyến bằng scalar First, IgnoreQueryFilters | Không Include Segment; scalar thiếu hàng không có fallback; chưa chạy nên chưa kết luận null hay exception ở materialization | show_unassigned; tên tuyến D-6 | T08 |
| `src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:34,44,47,48,64,143,166` | fault_cluster required + FK/index; fault SegmentId optional + FK/index | Fault cho null, cluster không cho; không có FK tuyến ghép với pole | n-a (không tự đổi schema); cluster refuse theo D-8 | T04/T05 |
| `src/LuxMap.Modules.Faults/Entities/Fault.cs:46` | SegmentId nullable trên sự kiện | Đã nhận null về kiểu; không cần nới cột vì ticket này | show_unassigned | T04 |
| `src/LuxMap.Modules.Faults/Entities/FaultCluster.cs:39` | SegmentId required của cụm | Không thể lưu null theo cấu hình/migration | refuse (D-8); schema n-a | T05 |
| `src/LuxMap.Modules.Faults/FaultItem.cs:18` | Response segment_id nullable | Đã mô tả null | show_unassigned | T04 |
| `src/LuxMap.Modules.Faults/FaultListQuery.cs:21` | Bộ lọc segment_id optional | Vắng = không lọc; có ID = equality, không chọn null | exclude (khi yêu cầu tuyến cụ thể) | T04 |
| `src/LuxMap.Modules.Faults/FaultQueryService.cs:61,63,102,104,144` | GET filter, open-on-segments cho sync; chiếu fault.SegmentId | GET chung giữ fault null; sync theo tuyến loại null nếu cả fault và pole đều không tuyến; Locate LEFT JOIN pole, không join road (E11) | show_unassigned (GET); sync D-4 | T04/T07 |
| `src/LuxMap.Modules.Faults/FaultReportService.cs:143,161,207,260` | Report có pole + citizen accept copy segment từ pole; replay response | Copy được null vào Fault; commune/source vẫn lấy từ pole; không cần client tự chọn tuyến | show_unassigned; lịch sử khi gán tuyến D-7 | T04/T08 |
| `src/LuxMap.Modules.Faults/FaultsController.cs:55,71` | Query segment_id chuyển vào FaultListQuery | Không có bộ lọc chưa phân tuyến riêng trong các hit | exclude (ID cụ thể) | T04 |
| `src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:28,90,93,95,96,98,108,109,150` | Group theo feeder/segment, đọc road IgnoreQueryFilters rồi roads.ContainsKey | Cột null đi vào ContainsKey(null) → rủi ro ArgumentNullException, không phải tự động LEFT JOIN; đường thiếu hợp lệ khác bị Where bỏ | refuse hoặc show_unassigned riêng: D-3; không tự vẽ cạnh | T05 |
| `src/LuxMap.Modules.Map/Features/MapController.cs:68,79,129` | Query segment_id; ghi chú node segment_ids | Vắng segment filter thì không lọc tuyến; node lists chỉ tuyến có thật | show_unassigned (pole); exclude (node segment_ids) | T03/T10 |
| `src/LuxMap.Modules.Map/Features/MapProperties.cs:27,93,135,136,161` | PoleProperties required string; SegmentProperties PK; node SegmentIds | Pole response chưa nullable; PK tuyến không đổi; node chỉ danh sách ID tuyến | show_unassigned (pole); n-a (PK/list ID) | T03/T10 |
| `src/LuxMap.Modules.Map/Features/MapQueries.cs:19` | PoleMapQuery.SegmentId optional | Không có selector null tuyến | show_unassigned qua bộ lọc D-2 | T03 |
| `src/LuxMap.Modules.Map/Features/MapQueryService.cs:87,94,96,105,155,210,216,221,232,249,286,288,292,293,294,315,471,473` | BBox poles; poles-on-segments; pole_count; active fault; controllers và node segment_ids | ProjectPoles không join road, status/lamp scalar optional; null được giữ khi không lọc tuyến; Contains/equality theo tuyến không lấy null; node query từ RoadSegment chỉ ra ID thật | show_unassigned (bbox); exclude (tuyến cụ thể/node IDs); sync D-4 | T03/T06/T07/T10 |
| `src/LuxMap.Modules.Map/Features/PoleDetailService.cs:52,53,78` | Đọc pole, scalar FirstOrDefault tên tuyến rồi ?? string.Empty | Giữ cột; ID có thể truyền null nhưng DTO non-null, tên hiện rỗng | show_unassigned; tên D-6 | T03 |
| `src/LuxMap.Modules.Map/Features/PoleMapDetail.cs:17` | Response segment_id non-null | Chưa mô tả null trên wire | show_unassigned | T03 |
| `src/LuxMap.Modules.Statistics/StatisticsContracts.cs:35,104` | FixtureStatusRow string?; WarrantyItem string bắt buộc | Nhóm status cho null sẵn; warranty DTO chưa nullable | show_unassigned | T06 |
| `src/LuxMap.Modules.Statistics/StatisticsController.cs:36,100` | Query segment_id cho status/warranty | Vắng = không lọc; chưa có filter chưa phân tuyến | show_unassigned/filter D-2 | T06 |
| `src/LuxMap.Modules.Statistics/StatisticsService.cs:20,26,56,67,70,75,93,182,187,224,241,248` | Group status, warranty projection, filter poles | LEFT JOIN status và active lamp, không join road; group_by segment tạo nhóm null; khi không group segment, null vốn nghĩa tổng hợp | show_unassigned; phân biệt nhóm/tổng D-9 | T06 |
| `src/LuxMap.Modules.Survey/Configurations/SurveyProcessingConfigurations.cs:57` | FK SurveyPass tới tuyến Restrict | Pass vẫn phải có tuyến, không phải FK của Pole | n-a; refuse đầu vào survey không hợp lệ | T05 |
| `src/LuxMap.Modules.Survey/Entities/SurveyProcessing.cs:41` | SurveyPass.SegmentId required | Không đại diện cột chưa phân tuyến | n-a | T05 |
| `src/LuxMap.Modules.Survey/Ingest/SurveyContracts.cs:19,27,30` | SegmentIds của response sweep | Tập tuyến được giao, không phải nullable pole field | n-a | T05 |
| `src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:31,47,49,243,252,269` | Visible/LockOrder kiểm tuyến của phiếu; list/detail sweeps | Không nhận pole_id khi giao khảo sát; kiểm tuyến không phát hiện cột null gần tuyến | refuse target không hợp lệ, exclude ngoài tập tuyến có giải thích D-8 | T05 |
| `src/LuxMap.Modules.Survey/Ingest/SweepsController.cs:71` | Bộ lọc segment_id của sweep | Chọn theo WorkOrderSegment, không đọc pole trực tiếp | n-a | T05 |
| `src/LuxMap.Modules.Survey/Processing/Frames/SurveyFramePipeline.cs:142` | Single projected pole khớp pole_id + pass.Segment | Chỉ thấy tập đã qua INNER JOIN ở ChainageQuery; không nhận null ở đây | refuse sai ghép; exclude có chẩn đoán ở upstream D-8 | T05 |
| `src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:8,21,49,57,59,65,69,72,80,87,93,96` | SqlQuery GPS LEFT JOIN LATERAL road; poles INNER JOIN road + bbox/corridor | GPS thiếu tuyến vẫn giữ mẫu; pole.segment_id null không khớp JOIN nên biến mất khỏi projected poles | exclude ngoài phạm vi đã giao; phải tường minh/chẩn đoán D-8 | T05 |
| `src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:79,92,95,98,147,149,256` | Tuyến phiếu, nhóm pass, GIS snapshot; SQL UNION scope JOIN road/pole qua segment_id | Cột null không vào scope theo tuyến và không vào snapshot từ ChainageQuery; không có từ chối riêng trong các hit | refuse/chẩn đoán D-8; giữ snapshot lịch sử D-7 | T05 |
| `src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:61` | Fault từ ảnh được xác nhận copy pole.SegmentId | Fault nullable nên không bắt buộc có tuyến để copy | show_unassigned; kiểm Targets ở WorkOrders D-1 | T04/T11 |
| `src/LuxMap.Modules.Survey/Review/SurveyMediaAccess.cs:30` | Kiểm tuyến phiếu có nhìn thấy không | Không join pole→road ở hit này; null pole không tự đổi quyền run | n-a; giữ commune/assignee guard | T05 |
| `src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:105,111,112,117,147,148,307` | Độ phủ từ GIS snapshot; gap JOIN road; Publish fault copy tuyến hiện tại của pole | Không phải mọi join đều đọc pole: gap join theo đoạn; fault có thể null nếu pole tháo tuyến sau snapshot | show_unassigned fault; xử lý đổi tuyến giữa run D-7/D-8 | T04/T05 |
| `src/LuxMap.Modules.Survey/Review/SweepCoverage.cs:13` | SegmentCoverage response | Tuyến của coverage, không cho nhóm cột null thành tuyến | n-a | T05 |
| `src/LuxMap.Modules.Sync/SyncBundleService.cs:51,58,83,84,87,92,99,109,110,111,112,113,114,121,122,125,126` | Chọn tuyến từ phiếu mở → map poles/notes/faults/orders theo tuyến | Cột null không vào Contains; phiếu chỉ có fault/pole null tuyến có thể mất toàn bộ trong bundle mặc định | show_unassigned theo phiếu hay refuse: D-4 | T07 |
| `src/LuxMap.Modules.Sync/SyncContracts.cs:24,26` | SegmentIds bundle; SyncPoleProperties kế thừa PoleProperties (E10) | Wire hiện mô tả segment_id non-null; scope/cache là theo tuyến | show_unassigned theo D-4; không thêm op tự phát | T07 |
| `src/LuxMap.Modules.Sync/SyncController.cs:23,33` | Query segment_id lặp cho bundle | Chỉ ID tuyến, không selector cột null; SyncOffline capability | n-a đầu vào tuyến; D-4 cho nội dung | T07 |
| `src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:51,72,79,100,158,603,619,621,666,710,771` | Plan feeder hoặc segment; đếm uncontrollable; affected segments DISTINCT rồi OrderBy(id.Length) | Feeder scope vẫn đếm pole null tuyến; segment scope không chọn nó; affected có null → id.Length có thể ném lỗi | exclude null khỏi affected IDs + thông tin minh bạch D-10 | T10 |
| `src/LuxMap.Modules.Telemetry/Lighting/LightingConfigurations.cs:24` | AffectedSegmentIds text[] required | Mảng snapshot lệnh, không phải quan hệ Pole | n-a; không lưu phần tử null | T10 |
| `src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:48,54,61,92` | DTO affected IDs + input segment_id xor feeder_id | Mảng IReadOnlyList<string> không mô tả phần tử null; request vẫn một tuyến/mạch thật | n-a input; show_unassigned thông tin pole theo D-10 | T10 |
| `src/LuxMap.Modules.Telemetry/Lighting/LightingController.cs:32` | Query preview feeder_id/segment_id | Không có target pole; tuyến vẫn phải là ID thật | n-a | T10 |
| `src/LuxMap.Modules.Telemetry/Lighting/LightingEntities.cs:39` | Snapshot AffectedSegmentIds string[] | Không phải cột FK pole, không tự đổi nullable | n-a | T10 |
| `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:18,54,63,97,103` | work_order.segment_id nullable, survey CHECK; work_order_segment required/FK/index | Không phải pole.segment_id; survey giữ danh sách tuyến thật | n-a; không nới schema khác | T05 |
| `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:19,77` | SegmentId nullable phiếu; SegmentId WorkOrderSegment required | Phiếu sửa/kiểm tra từ fault không tuyến vẫn có thể có SegmentId null | n-a; hành vi poles theo D-1 | T05 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:82,93,96,138,164,165,173,180,187,188,190,191,192,205,211,217,222,231` | LEFT JOIN fault→pole, RoadsOf bỏ null; bucket không tuyến theo xã | Không làm mất phiếu chỉ vì không tuyến; pole tồn tại mà SegmentId null thì không fallback sang fault.SegmentId (ternary theo pole != null) | show_unassigned (bucket xã); lịch sử D-7 | T05/T07 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:125` | Kiểm lại target sau khoá: commune/source/segment | So sánh string null an toàn; tập Targets dùng chung vẫn có rủi ro D-1; đổi tuyến trong upload bị coi target changed | n-a compare; show_unassigned/refuse theo D-1 | T11 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:23,61,80,81,88,92,93,108,121,126,127,128,134,135,137,144,149` | Targets union tuyến + fault; đọc road IgnoreQueryFilters; dictionary/order/position theo segment | Fault-bound null pole vẫn lọt Targets; TryGetValue(null), .Length và positions[null] không an toàn; equality nullable WO với pole cần kiểm SQL sau đổi model | show_unassigned hoặc refuse D-1; không silently exclude fault target | T05/T11 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:12,14,38,39` | Create/Patch SegmentId, SegmentIds của phiếu | Không phải chỗ gán tuyến cho pole; survey yêu cầu segment_ids | n-a | T05 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:13,37,54` | WorkOrderItem/Detail + WorkOrderFaultDetail nullable SegmentId | Đã nullable cho phiếu/fault; không tự bảo đảm WorkOrderPole an toàn | n-a | T05 |
| `src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:70,92,118,124,132,146,149,150,151,157,166,169,170,173,193,194,203,206,289,290,301,311,335,381,647,658` | List/detail/filter, CreateSurvey theo tuyến; assignee communes từ poles-on-segments; Insert chọn fault segment non-null | Repair/inspection có fault null tuyến vẫn tạo được phiếu không tuyến; survey không nhận pole; filter theo segment loại null theo phạm vi | show_unassigned cho repair/inspection D-1; refuse survey sai target | T05 |
| `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:27` | Query filter segment_id | Không ghi Pole.SegmentId | n-a | T05 |
| `src/LuxMap.Shared/Contracts/PrefixedId.cs:57` | Spec ID SEG và sequence | PK tuyến, không liên quan nới FK nullable | n-a | Không đổi |

### 1.2 Join / navigation: các điểm phải phân biệt

- **Không thấy truy cập `pole.Segment.…` hay `Include(p => p.Segment)`** trong phạm vi scan liên quan; hit `.Segment` thực thể Pole là khai quan hệ ở cấu hình. `Include` khảo sát là `o.Pass`/`o.Run`, không phải navigation road (E03, E16). Kết luận chỉ trong tập lệnh đó, không suy rộng mọi tên biến trên repo.
- **INNER JOIN làm mất cột null:** `SurveyChainageQuery.cs:87` (`road_segment r JOIN pole p ON p.segment_id = r.segment_id`), thêm bbox/corridor; `SurveyProcessor.cs:149` (`work_order_segment JOIN pole USING(segment_id)`) làm mất xã của cột ấy trong phép tính scope. Cả hai không có nhánh thông báo cột chưa phân tuyến trong output đã đọc (E09). `SurveyReviewService.cs:148` JOIN road cho **geometry khoảng trống**, không phải join đọc pole; GPS `LEFT JOIN LATERAL` vẫn giữ mẫu không gán tuyến (E09).
- **Không join road, vẫn giữ pole:** inventory list chiếu trực tiếp; inventory detail / map detail dùng scalar `FirstOrDefault` rồi tên rỗng; bbox map dùng scalar status/lamp, statistics LEFT JOIN status/lamp, fault location LEFT JOIN pole; agenda LEFT JOIN pole (E05/E08/E11/E12/E07). Đổi nullability DTO vẫn cần làm, dù hàng không biến mất.
- **Đã materialize rồi mới lỗi:** topology `roads.ContainsKey(pole.SegmentId)`; WO poles `lines.TryGetValue`, `.SegmentId.Length`, dictionary `positions`; lighting `OrderBy(id => id.Length)` trên affected IDs (E07/E08/E12). Không mô tả nhầm các chỗ này thành “INNER JOIN loại null”.
- **Loại theo phạm vi tuyến được yêu cầu:** `MapQueryService.PolesOnSegmentsAsync`, notes/faults/orders của bundle và topology list theo segment đều dùng equality/Contains (E05/E08/E10/E11). Riêng bundle mặc định mất phiếu chỉ mang cột chưa phân tuyến là điểm phải quyết, không coi là exclude hợp lệ sẵn (D-4).

### 1.3 Bổ sung ngoài grep bắt buộc

| File:dòng / nguồn | Làm gì | Nếu chưa phân tuyến | Đề xuất | Test |
|---|---|---|---|---|
| `AssetImportService.cs:47,93–100,363,383`; `ImportRow.cs:102–128` (E03/E06) | Header bắt buộc và Resolve reference | Header thiếu bị từ chối trước lập kế hoạch; ô trống lỗi dòng | show_unassigned; missing header D-2; sai reference vẫn refuse | T02 |
| `AssetImportController.cs:17–19` (E21) | Hướng dẫn nhập tuyến trước | Comment giả định mọi pole phải có tuyến | n-a, cập nhật mô tả P2 | T02 |
| `AssetsController.cs:149–166,190–199,260–273` (E05) | Route + policy + phân trang poles | Không có filter chưa phân tuyến trong chữ ký GET đã đọc | show_unassigned/filter D-2 | T01/T03 |
| `SyncPushService.cs:198–207,239–278,346–390` (E10) | 5 op gọi service gốc; không có op cập nhật tuyến pole | fault_report copy null qua FaultReportService; note/lux không tự cần tuyến; targets của work order vẫn phụ thuộc D-1 | show_unassigned; không tự thêm op | T04/T07/T11 |
| `scripts/photos_to_poles.py:21,112,647,749,797` (E03/E15) | Xuất CSV, review ca không có tuyến, cảnh báo ô bắt buộc | Đã xuất trống nhưng cảnh báo coi trống tuyến là lỗi phải sửa | n-a thuật toán; chỉ sửa hướng dẫn/cảnh báo theo quy tắc được duyệt | T02/T12 |
| `mocks/mock-pole-feeders.csv:1` (E03) | Header segment_external_ref | File ánh xạ có header; không phải importer backend | n-a; kiểm tài liệu mẫu D-11 | T12 |
| `mocks/mock-poles.geojson`, `mock-segments.geojson`, `mock-iot-nodes.geojson` (E17/E18) | Mẫu lớp GIS; E18 đọc properties phần tử đầu và số features | Mẫu pole đầu tiên có tuyến, node là danh sách tuyến; chưa chứng minh phủ ca null | show_unassigned ví dụ pole D-11; PK tuyến n-a | T03/T12 |
| `mocks/README.md:24` (E21) | Ghi đổi segment_id thành segment_ids của node | Tài liệu node, không phải pole nullable | n-a; giữ mô hình IoT ở tủ | T10 |
| `docs/openapi/luxmap-v1.5.json` (E18, số dòng từng property ở stdout) | Schema response/request; xem mục 3 | Các schema pole quan trọng hiện non-null | show_unassigned; schema PK/segment scope n-a | T12 |
| `docs/openapi/tools/gen_consolidated_spec.py:376–383,421` (E13) | Ghi đè PoleProperties viết tay, segment_id là `$ref` | Chỉ sửa C# rồi sinh lại vẫn có thể bị ghi đè về non-null | show_unassigned theo schema đã duyệt | T12 |

### 1.4 Tests — phủ mọi hit E02

Các hàng dưới đây phân loại **mọi hit test**, không coi mọi fixture có `SegmentId` là một invariant phải bỏ. Test đang dựng dữ liệu có tuyến vẫn là regression cần giữ; thêm dữ liệu null riêng theo T01–T12, không sửa hàng loạt fixture sang null. Output đầy đủ E02 là căn cứ dòng; tên test liên quan có ở E14.

| File:dòng | Làm gì với segment | Hành vi hiện tại với null / phạm vi | Đề xuất | Cần test |
|---|---|---|---|---|
| `tests/LuxMap.Api.Tests/AssetImportMockSetTests.cs:23,65,82,158,166` | Header/ref import, fixture và kỳ vọng dữ liệu có tuyến | Ca blank chưa có ở các hit; unknown-ref vẫn phải lỗi | show_unassigned (thêm ca); giữ regression | T02 |
| `tests/LuxMap.Api.Tests/AssetImportTests.cs:21,40` | Header/ref import, fixture và kỳ vọng dữ liệu có tuyến | Ca blank chưa có ở các hit; unknown-ref vẫn phải lỗi | show_unassigned (thêm ca); giữ regression | T02 |
| `tests/LuxMap.Api.Tests/AssetPermissionTests.cs:189,195,207` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:49,82,113,240,264,319,451,489` | Shape/list/detail và dữ liệu có tuyến | Các hit kỳ vọng ID/keys, chưa phủ nullable pole riêng | show_unassigned (thêm ca); giữ keys | T03 |
| `tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:181,199,318,350,383,391,393,407,460,500,531` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:26,35,71,75,80` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/AssetSchemaTests.cs:21,24,36,48,100,105,350` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/AssetUpdatedByTests.cs:38,50,82` | Header/ref import, fixture và kỳ vọng dữ liệu có tuyến | Ca blank chưa có ở các hit; unknown-ref vẫn phải lỗi | show_unassigned (thêm ca); giữ regression | T02 |
| `tests/LuxMap.Api.Tests/CitizenReportTests.cs:452` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/CitizenReviewTests.cs:584` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/CommuneReferenceTests.cs:67` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/CommuneWriteGuardCostTests.cs:302,323,346` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/CommuneWriteScopeTests.cs:131` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/FaultListTests.cs:75,76,124,349,372,388` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/FaultReportTests.cs:58,60,61,142` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/FaultReviewTests.cs:73,74,308,331` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/FaultSchemaTests.cs:75,301` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/FeederCommuneConstraintTests.cs:208,224,351,356` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/FixtureCardinalityTests.cs:208` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/FixtureLampCountTests.cs:185` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/FixtureRetirementTests.cs:169` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/IotNodeTests.cs:50,129,294,374,382` | Wiring và derived segment_ids | Fixture có tuyến nên chưa lộ id.Length(null) | exclude null IDs; D-10 | T10 |
| `tests/LuxMap.Api.Tests/LightingCommandTests.cs:73,81,83,142` | Wiring và derived segment_ids | Fixture có tuyến nên chưa lộ id.Length(null) | exclude null IDs; D-10 | T10 |
| `tests/LuxMap.Api.Tests/LightingTestRig.cs:183,191` | Wiring và derived segment_ids | Fixture có tuyến nên chưa lộ id.Length(null) | exclude null IDs; D-10 | T10 |
| `tests/LuxMap.Api.Tests/LuxReadingTests.cs:153` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/LuxValueFinitenessTests.cs:55` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/MapEndpointTests.cs:85,377,386,421,423,476,489,519,531,578` | Shape/list/detail và dữ liệu có tuyến | Các hit kỳ vọng ID/keys, chưa phủ nullable pole riêng | show_unassigned (thêm ca); giữ keys | T03 |
| `tests/LuxMap.Api.Tests/NotificationTests.cs:67,262,273` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/PoleDetailEndpointTests.cs:63,204,239` | Shape/list/detail và dữ liệu có tuyến | Các hit kỳ vọng ID/keys, chưa phủ nullable pole riêng | show_unassigned (thêm ca); giữ keys | T03 |
| `tests/LuxMap.Api.Tests/PoleLocationTests.cs:177` | Dời cột trong corridor của tuyến | Đo tuyến giả định luôn tồn tại | refuse hay cho dời D-5 | T09 |
| `tests/LuxMap.Api.Tests/PoleNoteTests.cs:162,165,263,284,330` | Header/ref import, fixture và kỳ vọng dữ liệu có tuyến | Ca blank chưa có ở các hit; unknown-ref vẫn phải lỗi | show_unassigned (thêm ca); giữ regression | T02 |
| `tests/LuxMap.Api.Tests/PolePhotoMigrationTests.cs:50` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/PoleWriteTests.cs:391,415,459` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:27,54,61,62,70,77,84,85,92,93,110` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/PrefixedIdOverflowTests.cs:305` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/QrCodeTests.cs:273` | Fault/QR, fixture tham chiếu pole/tuyến | Một số fault vốn null; chưa chứng minh pole null được phục vụ | show_unassigned | T04/T08 |
| `tests/LuxMap.Api.Tests/ScheduledJobTests.cs:353` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Api.Tests/StatisticsTests.cs:62,109,110,136,139,312,337` | Group/filter, warranty JSON/CSV | Đang dựng pole có tuyến; CSV null tổng hợp đã có | show_unassigned nhóm null riêng | T06 |
| `tests/LuxMap.Api.Tests/SurveyIngestTests.cs:62,73,75,76` | Tuyến pass/phiếu, snapshot/coverage/publication | Không đồng nhất nullable GPS với pole null; fixture hiện có tuyến | n-a fixture; thêm refuse/chẩn đoán | T05 |
| `tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:74,75,76,82,120,121,123,124,263,369,373,374,376,384,388,442,484` | Tuyến pass/phiếu, snapshot/coverage/publication | Không đồng nhất nullable GPS với pole null; fixture hiện có tuyến | n-a fixture; thêm refuse/chẩn đoán | T05 |
| `tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:70,71,72,77,105,685,698,855` | Tuyến pass/phiếu, snapshot/coverage/publication | Không đồng nhất nullable GPS với pole null; fixture hiện có tuyến | n-a fixture; thêm refuse/chẩn đoán | T05 |
| `tests/LuxMap.Api.Tests/SweepCoverageTests.cs:114,118,119,133,134,135,187,219,372,388` | Tuyến pass/phiếu, snapshot/coverage/publication | Không đồng nhất nullable GPS với pole null; fixture hiện có tuyến | n-a fixture; thêm refuse/chẩn đoán | T05 |
| `tests/LuxMap.Api.Tests/SyncTests.cs:71,76,130,132,148,149,153,154,155,404,417` | Bundle theo tuyến, op replay/validation | Đang kiểm segment_ids và dữ liệu theo tuyến | show_unassigned/refuse D-4; giữ replay | T07 |
| `tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:97,248,278,293,405,450,536,545,644,683` | Label feeder/cabinet, topology cạnh và import | Không phải nhãn nguồn segment; fixture có tuyến | n-a nhãn cũ; topology D-3 | T02/T05 |
| `tests/LuxMap.Api.Tests/TopologyQueryTests.cs:53,270,346,370` | Shape/list/detail và dữ liệu có tuyến | Các hit kỳ vọng ID/keys, chưa phủ nullable pole riêng | show_unassigned (thêm ca); giữ keys | T03 |
| `tests/LuxMap.Api.Tests/WarrantyTests.cs:79,133,204,295,308,370` | Group/filter, warranty JSON/CSV | Đang dựng pole có tuyến; CSV null tổng hợp đã có | show_unassigned nhóm null riêng | T06 |
| `tests/LuxMap.Api.Tests/WorkOrderCancellationTests.cs:170,191` | Phiếu theo tuyến/fault; poles/ảnh | Cần thêm null target, không sửa toàn bộ phiếu sang null | show_unassigned/refuse D-1 | T05/T11 |
| `tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:134,135,175` | Phiếu theo tuyến/fault; poles/ảnh | Cần thêm null target, không sửa toàn bộ phiếu sang null | show_unassigned/refuse D-1 | T05/T11 |
| `tests/LuxMap.Api.Tests/WorkOrderTests.cs:67,161,170,181,268,337,634,650,655,743,761,795,823,828,848,1184,1206,1232,1245,1257,1261,1265,1268,1321,1328,1343` | Phiếu theo tuyến/fault; poles/ảnh | Cần thêm null target, không sửa toàn bộ phiếu sang null | show_unassigned/refuse D-1 | T05/T11 |
| `tests/LuxMap.Shared.Tests/JsonConventionTests.cs:14` | Fixture/PK/scope/thứ tự/JSON với dữ liệu có tuyến | Không có nghĩa phải đổi PK/fixture hiện hữu thành null | n-a regression; thêm ca scope/nullable riêng | T01/T12 |
| `tests/LuxMap.Shared.Tests/SurveyObservationIdentityTests.cs:29,30,75` | Tuyến pass/phiếu, snapshot/coverage/publication | Không đồng nhất nullable GPS với pole null; fixture hiện có tuyến | n-a fixture; thêm refuse/chẩn đoán | T05 |
| `tests/LuxMap.Shared.Tests/SurveyPublicationTests.cs:25` | Tuyến pass/phiếu, snapshot/coverage/publication | Không đồng nhất nullable GPS với pole null; fixture hiện có tuyến | n-a fixture; thêm refuse/chẩn đoán | T05 |

## 2. Schema từ migrations + model snapshot — không đọc DB

**Đây là schema mô tả trong repo, không phải xác nhận catalog của một database đang chạy.** E04 và E19:

| Thành phần | Bằng chứng thật | Tác động cần có ở P2 |
|---|---|---|
| `pole.segment_id text NOT NULL` | `20260901082917_AddAssetEntities.cs:69`; snapshot `Property<string>("SegmentId").IsRequired().HasColumnName("segment_id")`; config `AssetConfigurations.cs:198` | Cho null trên property FK và navigation `Pole.Segment`; cấu hình quan hệ optional nhất quán |
| FK đơn `fk_pole_road_segment_segment_id` → `road_segment(segment_id)`, `Restrict` | Migration `:89–94`; snapshot `:4554–4560`; config `:224–228` | Giữ tên/đích/Restrict; null không có cha, non-null vẫn phải là tuyến tồn tại |
| Index `ix_pole_segment_id` | Migration `:191–193`; snapshot `:512–513` | Giữ index; không tự thêm partial/composite vì chưa có đo plan |
| Commune vẫn bắt buộc, `HasCommuneScope()` + `HasCommuneReference(pole => pole.CommuneId)` | Config E04; snapshot FK `fk_pole_administrative_unit_commune_id`, `Restrict` | Không suy xã từ tuyến; không nới scope khi tuyến null |
| Không có FK ghép segment/commune | Cấu hình ghi rõ cố ý `:246`; snapshot FK đơn (E04); trap O-7 đã đọc | Giữ tuyến liên xã, không thêm FK cho “đối xứng” |
| CHECK của **pole** trong snapshot | `ck_pole_data_source`; `ck_pole_feeder_source`; `ck_pole_feeder_source_matches_feeder` = `(feeder_id IS NULL) = (feeder_source IS NULL)`; `ck_pole_note_length` = `char_length(note) <= 1000` (E04) | Không CHECK nào trong nhóm này yêu cầu segment; giữ nguyên, không đổi nhãn feeder |
| Các index pole khác | Snapshot E04: `ix_pole_commune_id`, `ix_pole_geom` GIST, `ix_pole_updated_by`, `ux_pole_commune_external_ref` partial, `ix_pole_feeder_id_commune_id` | Không drop/recreate ngoài yêu cầu |
| `fault.segment_id` đã nullable; `fault_cluster.segment_id` NOT NULL | `20260905085759_AddFaultEntities.cs:25,56` (E04); entity/config E11 | **Không cần migration nới fault** chỉ để copy null. Không tự nới cluster; D-8 |

**Mô tả Up cần có, chưa tạo migration:** nới duy nhất `pole.segment_id` text sang nullable (bỏ NOT NULL, không backfill, không default tuyến giả), cập nhật model/snapshot để FK optional và navigation nullable. Nếu EF sinh drop/re-add FK do đổi required → optional, phải review tên/Restrict và tránh thay đổi thừa; không tự coi đó là thay đổi nghiệp vụ mới. Không đổi PK `road_segment.segment_id` hay những bảng khác. Schema đích đang vẽ `road_segment ||--o{ pole` (`erd.md:300`, E13): P2 cần sửa cardinality cha của pole thành optional theo quyết định đã chốt.

**Mô tả Down cần có:** trước khi SET NOT NULL, kiểm có `pole.segment_id IS NULL` hay không. Có → dừng với thông điệp yêu cầu gán tuyến có kiểm chứng trước hoặc rollback ứng dụng và giữ schema. Không xoá pole, không tự gán tuyến, không dùng `defaultValue: ""` để EF backfill. Không có null → siết NOT NULL trở lại, giữ FK/index/CHECK. Đề nghị preflight + `ALTER ... SET NOT NULL` trong transaction migration; SET NOT NULL vẫn là chốt cuối nếu ghi null xen vào. Mẫu preflight từ chối mất dữ liệu có thật ở `AddPolePhotoReviews.Down` (E20); cách khoá/preflight cụ thể chờ D-12. P1 **không thực thi** Up/Down hay truy vấn kiểm dữ liệu.

## 3. Hình dạng API và tương thích consumer

Căn cứ Contract E13; DTO/source E05–E12; schema JSON thực tế E18. `optional` = có thể vắng key; `nullable` = key có thể mang null. Không đánh đồng hai nghĩa.

| Endpoint / DTO | Hiện tại | Thay đổi cần xét ở P2 (chờ D-item khi có) | Additive / breaking |
|---|---|---|---|
| `POST /assets/poles`, `CreatePoleRequest` | `segment_id` `[Required]`, max 32; service Require tuyến | Cho null; đề nghị bỏ bắt buộc key ở POST (D-2). Nếu có ID vẫn kiểm qua query filter, không hợp lệ/không thấy vẫn 404 `ASSET_NOT_FOUND`; không coi chuỗi rác là null | Nới tập request hợp lệ: additive cho client cũ; response đọc có null là breaking |
| `PUT /assets/poles/{id}`, `UpdatePoleRequest` | `[Required]`; service gán SegmentId; PUT thay thế toàn phần (Contract E13) | ID/null/vắng phải chốt riêng D-2; không tự để form cũ vô tình tháo tuyến. Null explicit phải có nghĩa tường minh; ID sai vẫn lỗi | Cho explicit null là mở rộng request; vắng key có thể đổi hành vi nên phải thông báo |
| `GET /assets/poles[/{id}]`, `PoleListItem`, `PoleDetail.pole` | `segment_id` required non-null; detail `segment_name` required, code fallback `""` | Giữ key `segment_id`, đổi thành `string|null`; bộ lọc D-2. `segment_name` D-6. `cabinet_*` vẫn suy feeder, không phụ thuộc segment (CAB-13, E13) | Nullable hoá response là **breaking** với model non-null WP5/WP6 |
| Import `poles.csv` / GeoJSON | Header + ô `segment_external_ref` bắt buộc; upsert cả hai nhánh `segment!.Id` | Cho ô trống; thiếu header, ô trống trên dòng đã có tuyến, chuỗi trắng và ô GeoJSON sai kiểu: D-2. Chuỗi tham chiếu không khớp **vẫn lỗi dòng**, không fallback null. `ImportResult` không cần hình dạng mới chỉ vì trường này | Nới đầu vào; ngữ nghĩa upsert blank có thể phá liên kết nếu chọn xoá |
| `GET /map/poles`, `PoleProperties` | `properties.segment_id` non-null; bbox query không join road | Giữ feature và flat properties, `segment_id:null`; vẫn bbox/413/scope/data_source như cũ. Filter chưa gán tuyến D-2 | Breaking kiểu giá trị; filter mới additive |
| `GET /map/poles/{id}`, `PoleMapDetail` | `segment_id`, `segment_name` non-null; đây là object detail, không FeatureCollection | `segment_id:null`; tên D-6; giữ current status/history/fault của pole | Breaking; không gọi nhầm detail là `properties` |
| GET topology inventory: `/assets/feeders/{id}/poles`, `/assets/feeders/poles?unassigned=true`, `/assets/segments/{id}/poles` | `TopologyPole.segment_id` non-null; unassigned hiện là **chưa gán feeder** (E05) | Feeder list giữ cột chưa tuyến với nullable segment; list segment chỉ cột của tuyến đó. Không tái sử dụng cờ feeder-unassigned để mang nghĩa chưa tuyến | Breaking DTO; bộ lọc tuyến mới là additive |
| `GET /map/cabinets/{id}/topology`, `CabinetTopologyEdgeProperties` | Cạnh bắt buộc segment để chiếu thứ tự đường | D-3: refuse rõ khi có cột chưa tuyến, hoặc trả thông tin riêng; chưa quyết nullable hoá cạnh | Phụ thuộc phương án; phát lỗi mới cũng đổi hành vi client |
| `GET /work-orders/{id}/poles`, `WorkOrderPole` | `segment_id` non-null; `position` int theo tuyến; union cả pole của fault | D-1: cho inspection/repair hiện nhóm chưa tuyến và `position` không giả làm thứ tự đường, hoặc refuse rõ. Survey không được nhận cột ấy | Nếu nullable hoá segment/position là breaking WP5/WP6; nếu refuse phải công bố mã/điều kiện |
| `GET /sync/bundle`, `SyncPoleProperties` | Kế thừa `PoleProperties`; schema hiện non-null; toàn bộ cache scope theo segment | D-4 quyết scope cột/fault/phiếu chưa tuyến và quy tắc thay cache; không thêm null vào `segment_ids[]` | Nullable response + đổi cache scope ảnh hưởng WP6; không coi chỉ là additive |
| `POST /sync/push` | 5 op, kiểm capability từng op, gọi service gốc (E10) | Không op gán tuyến pole hiện hữu. `fault_report` theo quy tắc POST fault; note/lux giữ service; start/complete theo phiếu. Không cần field segment từ client | Không cần đổi envelope; quyết định mới phải phản ánh trong rejected/conflicts của từng op |
| `POST /faults` có cột; citizen accept; ảnh cột confirm; sweep publish | Copy segment từ pole vào `Fault.SegmentId` nullable (E09/E11) | Có thể copy null không đổi schema/response. Không yêu cầu client bịa tuyến/location khi đã có pole. Gán tuyến về sau có cập nhật fault cũ không: D-7 | Shape fault vốn nullable; mở thêm trường hợp dữ liệu là thay đổi ngữ nghĩa cần báo |
| `GET /faults`, `FaultItem`, `WorkOrderFaultDetail` | `segment_id` vốn nullable; location LEFT JOIN pole fallback | Hiển thị null là chưa phân tuyến khi có pole; không join road để lọc mất. Query ID cụ thể giữ phạm vi, không dùng literal `null` như sentinel | Không đổi kiểu field đã nullable; thêm filter nếu duyệt là additive |
| Statistics fixture-status / warranty + CSV | `FixtureStatusRow.segment_id` required nullable; `WarrantyItem.segment_id` non-null; LEFT JOIN status/lamp | Nhóm null riêng khi group theo segment; aggregate không group segment cần D-9 để biết riêng số chưa tuyến. Warranty nullable, CSV ô trống theo đường xuất chung (trap đã đọc) | Status kiểu vốn nhận null; thêm count nếu duyệt additive; warranty nullable là breaking |
| QR `/public/…`, `PublicQrInfo`, `CitizenReportPole` | Lookup chỉ có `segment_name` required; citizen detail pole.segment_id required | D-6 cho tên/nhãn chưa tuyến; citizen detail nullable segment. Không thay code QR hay tạo vai trò công dân | Nullable field là breaking; giữ string với nhãn thì đổi ngữ nghĩa |
| Lighting preview/commands/result; map node | `affected_segment_ids` là string list; `uncontrollable_pole_count` int; node `segment_ids` derived từ road | Loại null khỏi danh sách **ID tuyến thật**, không đồng nhất “chưa tuyến” với “không điều khiển được”; D-10 cho thông tin số cột chưa tuyến | Không cần nullable array items; thêm thông tin nếu duyệt additive |

**Mã lỗi:** `VALIDATION_FAILED`, `ASSET_NOT_FOUND`, `POLE_OFF_SEGMENT` có thật (E17). Chưa có căn cứ để công bố một mã “pole chưa tuyến” mới. D-1/D-3/D-5/D-8 đề nghị một mã nghiệp vụ thống nhất, ví dụ **`POLE_SEGMENT_REQUIRED` (tên đề xuất, chưa tồn tại/chưa chốt)** với `details.pole_ids` + thao tác bị chặn; Mỹ cần chốt HTTP 409 hay dùng 400 `VALIDATION_FAILED` ở biên input. Import tiếp tục `rows[].column=segment_external_ref`, không biến lỗi dòng thành mã HTTP cả mẻ (E06).

**Spec sinh:** không sửa tay `luxmap-v1.5.json`. Sau quyết định được ghi và hiện thực P2: xuất `luxmap-v1.json` từ code → generator → lint như `.ai/context/commands.md`. Đặc biệt generator `:376–383` ghi đè `PoleProperties.segment_id` bằng `$ref: SegmentId` (E13), nên chỉ nới DTO không đủ. E18 liệt kê mọi schema có `segment_id`: chỉ đổi các schema mang **quan hệ của pole**, không nullable hoá `SegmentId` PK dùng chung, `SegmentProperties`, `SegmentListItem`, `SegmentCoverage` hay phần tử `segment_ids`.

**SPEC-NULLABLE:** `PoleMapDetail.fixture` / `luminance_baseline` thực tế là `$ref` trần dù source nullable; `PoleProperties.segment_id` hiện cũng `$ref` trần, còn `SyncPoleProperties.segment_id` là `{type:string}` riêng (E18). Dùng `$ref` trần hoặc thêm `nullable` sai chỗ có thể khiến client sinh tiếp tục non-null; phải kiểm schema hợp nhất theo ngữ nghĩa và model client sinh ra, không chỉ lint. Bẫy chung được ghi ở `spatial-map.md` và drift có nhắc SPEC-NULLABLE (E13); không tự mở ticket sửa toàn bộ nullable object trong P1.

## 4. Quy tắc theo chức năng — đề xuất để duyệt

Các dòng “cần” dưới đây triển khai bốn quyết định đã chốt; chỗ chọn cơ chế/shape chuyển sang D-item. Không có luật nào được coi đã áp vào hệ thống ở P1.

| Luồng / consumer | Hiện trạng có chứng cứ | Quy tắc cần làm rõ / không được làm mất dữ liệu |
|---|---|---|
| Khảo sát giao theo tuyến | CreateSurvey chỉ nhận segment_ids, RequireSurveyAssignee lấy commune theo pole-on-segments (E07) | Cột null không là target survey. Không tạo tuyến giả/không suy tuyến gần nhất. Cần quy định lời giải thích cho cột null gần hành lang và race tháo tuyến sau giao phiếu (D-8) |
| Hành lang / processor / coverage | INNER JOIN Chainage + scope UNION bỏ null; coverage dùng GIS snapshot (E09) | Không gọi cột chưa tuyến là `unknown/not_observed` của một tuyến mà nó chưa thuộc. Snapshot cũ không viết lại; kiểm thay đổi tuyến trước publish D-7/D-8 |
| WO poles inspection/repair | Targets hợp pole của fault, ordering dùng tuyến (E07) | Không bỏ mất pole/fault được giao. D-1 quyết hiển thị không có thứ tự dọc đường hay từ chối có lý do; survey vẫn chỉ các tuyến được giao |
| Ảnh cột trong phiếu | Upload dùng Targets; sau khoá so commune/source/segment (E07); publish fault copy segment (E01/E09) | Không nới scope/assignee khi tuyến null. D-1 phải áp chung Targets cho list và ảnh, tránh list thấy mà upload không được hoặc target sai do null==null |
| Fault report có pole / citizen accept / photo confirm | Fault nullable và copy commune/source/segment từ pole (E11/E09) | Giữ khả năng báo hỏng cột chưa tuyến; không yêu cầu location do client nếu pole có toạ độ. Nguồn và audit như service gốc. Fault cũ không tự dịch tuyến trước D-7 |
| Fault cluster | Entity/config/migration yêu cầu một segment (E04/E11); seed kiểm một tuyến (E15) | Không nhét cột null vào cluster nào, không nới cluster schema. Nếu luồng sinh cụm nhận cột ấy thì refuse/diagnostic D-8, không drop im lặng. Không suy diễn script seed là engine CV-15 |
| Thống kê | Fixture status group có null; warranty không join tuyến (E12) | Đếm nhóm chưa tuyến riêng theo data_source/scope, không gộp vào tuyến khác, không gộp vào out. D-9 phân biệt null nhóm với null chiều bị bỏ trong aggregate |
| Bản đồ cột / detail / kiểm kê | Không cần join road để hiện pole (E05/E08) | Hiện đầy đủ bbox/phân trang/scope; label chưa tuyến qua dữ liệu null, tên D-6. Segment layer chỉ đếm thành viên của tuyến thật; tổng pole toàn xã không lấy bằng tổng segment counts |
| Topology feeder/cabinet | List feeder không cần đường; cabinet diagram cần đường để order (E05/E08) | List feeder giữ pole; diagram không bịa hình học/thứ tự; lỗi rõ hoặc thông tin riêng theo D-3 |
| Sync | Bundle lựa tuyến trước rồi kéo 4 tập nội dung (E10) | Không để phiếu sửa/kiểm tra chỉ có cột chưa tuyến mất khỏi công việc offline. D-4 phải bao đủ pole + note + fault + WO và cách cache xoá/giữ khi tái gán tuyến; không chỉ sửa projection |
| Citizen QR | Resolve QR tới pole; lookup tên road First; review detail chiếu segment (E11) | QR vẫn neo pole/commune; chưa tuyến không phải QR invalid. UI có cách hiện “chưa phân tuyến”, shape tên D-6; accept đi service fault |
| IoT-lighting | Feeder scope theo feeder; count uncontrollable theo thiếu feeder/wiring; affected IDs từ toàn bộ feeder switched (E12) | Null tuyến không tự khiến đèn không điều khiển được. Danh sách affected chỉ ID thật; số cột chưa tuyến bị ảnh hưởng cần minh bạch D-10. Segment target không đại diện tất cả cột trong feeder; không thay phạm vi vật lý rơ-le |
| Scripts / mocks | seed index demo_feeder theo segment; photos_to_poles cảnh báo bắt buộc; copy PLAN lọc commune (E15) | Không chạy scripts/DB P1. Sau duyệt chỉ thêm fixture unassigned phù hợp, không phá ID mock cũ; không chỉnh thuật toán tự gán tuyến/OSM (D-11) |

## 5. Gán tuyến về sau và bộ lọc chưa phân tuyến

**Đường đã có (E05/E06):** `PUT /assets/poles/{id}` gán lại `pole.SegmentId`; import poles tìm dòng theo `(commune_id, external_ref)` rồi gán `segment!.Id`. Hai đường đó có thể gán từ null sang tuyến sau khi nới validation. Chưa thấy endpoint chuyên thay segment trong chữ ký/grep liên quan đã đọc; không tự phát minh route mới. Endpoint chuyên feeder/note/location không phải đường gán segment (E05/E20).

**Đề xuất tối thiểu, chưa quyết:** dùng PUT/import đã có để gán tuyến; chỉ thêm endpoint riêng nếu Mỹ muốn tách thao tác này khỏi PUT thay thế toàn phần (D-2). Vẫn `ManageAssets` = Quản lý, `ReadNetwork` = bốn vai trò, scope xã của pole không đổi; quyền đã có trong matrix E20. Tuyến supplied kiểm qua query filter như hiện tại, không dùng IgnoreQueryFilters để mở quyền ghi (E05). Ngoại lệ inter_commune không bị biến thành same-commune FK (E04).

**Bộ lọc:** GET inventory hiện chỉ `commune_id[]` + `PageQuery` (E05), map/status/warranty có `segment_id` mà chưa có selector chưa tuyến (E01/E12). D-2 đề nghị query `unassigned_segment=true` trên `/assets/poles` (tên **chưa chốt**); có thể mở sang map/thống kê nếu scope UI cần. Không lấy `unassigned=true` trên `/assets/feeders/poles` để đổi nghĩa cũ (E05). Phải lọc **trước Count/Skip/Take**, giữ `page/page_size/total/items`, thứ tự hiện có và Narrow/scope; xác định xung đột với `segment_id` (nếu có) bằng lỗi rõ, không im lặng ưu tiên một cờ.

**Nhãn nguồn `segment_source`:** TOPO-INFER thật đang có label theo **cặp quan hệ**, CAB-13 chỉ đọc cabinet qua feeder (E13). Chưa có luật label cho segment trong Pole/config/snapshot E04. Thêm nhãn là thay schema/API vượt quyết định nullable, nên D-13: có cần không, không tự thêm hoặc tái dùng nhãn `feeder_source`.

## 6. Kế hoạch test — chưa chạy, chưa sửa test

Các tên dưới đây là **tên dự kiến**, không khẳng định đã tồn tại hoặc pass. Căn cứ rủi ro là E04–E12; test cũ/fixture E02/E14/E20; quy tắc phá thử ở trap testing (E14). Ca phụ thuộc D-item chỉ được chốt assertion sau khi Mỹ chọn.

| Mã | Tên dự kiến / trường hợp | Chốt phải phá thử ở P2 |
|---|---|---|
| T01 | `Pole_without_segment_round_trips_and_stays_commune_scoped`: EF/SQL null hợp lệ; POST vắng/null theo D-2; ID hợp lệ vẫn gán, ID không thấy 404; non-manager 403; pole xã ngoài 404; guard direct ngoài scope vẫn chặn; FK non-null sai vẫn chặn | Khôi phục `.IsRequired()` làm ca null đỏ; bỏ check supplied ID làm ca ID sai đỏ; không đánh đồng lỗi audit với scope |
| T02 | `Blank_segment_reference_imports_unassigned_but_unknown_reference_fails_the_row`: CSV/GeoJSON trống, header vắng theo D-2; whitespace; value sai kiểu; **ref không khớp vẫn lỗi dòng, không ghi null thay**; ref trùng nhiều xã vẫn lỗi; mẻ một dòng hợp lệ + một dòng sai; reimport null→assigned, assigned→blank, unchanged + updated_by đúng | Cho Resolve unknown trả null không Fail → test phải đỏ; bật lại required-cell → ca trống đỏ; thay blank semantics → ca upsert đỏ |
| T03 | `Unassigned_pole_remains_in_inventory_bbox_detail_and_feeder_lists`: giữ ID/toạ độ/status/fixture/note; nullable key hiện đúng; filter counts/phân trang trước khi skip; mixed null/assigned, scope; segment filter cụ thể chỉ tuyến ấy | Thêm INNER JOIN road hoặc bỏ null trước total → test mất hàng/count đỏ; giữ DTO/schema non-null → schema test đỏ |
| T04 | `Fault_on_unassigned_pole_keeps_pole_location_commune_and_source`: POST fault, citizen accept, confirmed photo, GET fault; không cần client location/segment; replay client_op_id; audit đúng; fault có segment cũ khác tuyến pole theo D-7 | Buộc có segment hoặc chọn nguồn/xã sai → đỏ; bỏ fallback location → đỏ |
| T05 | `Survey_never_targets_unassigned_poles_and_reports_the_reason`: cột null gần tuyến cũng không thành target; sai input target refuse; chẩn đoán theo D-8; giữ GIS snapshot/coverage cũ; tháo/đổi tuyến giữa snapshot/publish; cluster null không được lưu; WO poles fault-target null và null unrelated pole theo D-1 | Bỏ điều kiện segment-not-null của Targets (nếu thêm) phải làm **cột null không liên quan** lọt vào và đỏ; bỏ diagnostic/refusal → đỏ; cố nhận null cluster → đỏ |
| T06 | `Statistics_count_unassigned_separately_without_losing_unknown`: tổng và nhóm segment/commune/data_source, never_surveyed; không có fixture; phân biệt aggregate null và nhóm null D-9; warranty JSON/CSV null; calibration_rig mặc định loại như cũ | Lọc mất null trước group hoặc gộp vào một tuyến → đếm đỏ; gộp unknown vào out → đỏ |
| T07 | `Offline_bundle_keeps_unassigned_fault_targets_without_other_engineers_data`: phiếu mở chỉ có null targets; mixed assigned/null; đầy đủ notes/faults/orders; cache chuyển null↔assigned theo D-4; explicit segment bundle giữ phạm vi; push 5 op + replay/capability từng op | Trả bundle cũ chỉ theo Contains(segment) → đỏ; mở rộng mọi pole xã thay vì targets của người gọi → lộ dữ liệu và đỏ; bỏ check capability riêng phải đỏ |
| T08 | `Qr_lookup_and_citizen_review_work_for_unassigned_pole`: QR lookup không rơi thành lỗi/QR invalid; tên theo D-6; submit/accept giữ dữ liệu của pole; ngoài xã/kênh ảnh không mở quyền mới | Giữ scalar First không fallback khi thiếu road → ca null phải phát hiện; thêm requirement segment ở accept → đỏ |
| T09 | `Relocating_unassigned_pole_follows_explicit_policy`: D-5; giữ no-op, audit, scope và có-tuyến corridor; không gọi SingleAsync lên tuyến null | Bỏ nhánh null/refusal đã duyệt → đỏ; bỏ corridor cho pole có tuyến → test cũ đỏ |
| T10 | `Lighting_handles_unassigned_poles_without_null_affected_ids`: feeder điều khiển có cả assigned/null, chỉ null, cột chưa wiring; preview + send; affected ID thật unique; uncontrollable không đếm nhầm chỉ vì null tuyến; node segment_ids không có null; thông tin D-10 | Bỏ lọc null trước `.Length` → ca feeder chỉ null phải đỏ; đổi uncontrollable thành segment-null → đếm đỏ |
| T11 | `Work_order_pole_photo_uses_the_same_unassigned_target_policy`: list/upload cùng Targets; cột null unrelated không được upload; thay segment trong lúc upload bị kiểm lại; ảnh confirm sinh fault nullable không sửa history | Tách predicate list/ảnh hoặc bỏ recheck dưới khoá → đỏ |
| T12 | `Nullable_pole_segment_schema_and_rollback_are_explicit`: raw+consolidated schemas đúng required/nullable; không đổi SegmentId PK; client sinh WP5/WP6 kiểm model; index/FK/CHECK/scope giữ; Up/Down trên **container riêng** (chỉ P2), Down có null từ chối và giữ nguyên hàng, Down sau gán tuyến thành công; script/mocks không chạy DB | Bỏ preflight hoặc thêm default trống làm Down đổi dữ liệu → đỏ; generator override non-null → schema assertion đỏ; drop index/FK ngoài yêu cầu → catalog assertion đỏ |

**Test hiện có phải rà kỳ vọng, không xoá hàng loạt:**

- `AssetImportTests.A_reference_that_matches_nothing_is_caught_in_validation_and_nothing_is_written` (`:27`, lỗi cột `:40`, E14/E02) **giữ nguyên kỳ vọng lỗi**. Header-missing test hiện gọi segments (`:170–175`, đã đọc); không đổi nó sang cho thiếu trường của segments. Thêm test header poles riêng theo D-2.
- `AssetReadShapeTests` và `TopologyQueryTests` đang ghim bộ keys; giữ keys nếu chỉ nullable hoá, thêm assert `JsonValueKind.Null` cho ca mới. Không tự thêm operational properties vào inventory (E14/E21).
- `WorkOrderTests` survey positions `:837`, repair fault-only ordering `:841–864`, `Paging_keeps_road_positions_and_the_total :884–894` (E20): **giữ kỳ vọng cột có tuyến**. Bổ sung nhóm chưa tuyến/position theo D-1, cập nhật kỳ vọng tổng khi fixture mới đưa vào đúng phiếu. Không thay ID order bằng chuỗi ID trần.
- `StatisticsTests.Grouping_by_commune_and_segment_gives_one_row_per_group_in_id_order` (E14), `WarrantyTests` CSV header/row (E02) cần thêm dòng/field **chỉ nếu D-9 thêm count**, không tự thay kỳ vọng tổng mọi test do fixture chung. Null segment của aggregate hiện có không chứng minh nhóm chưa tuyến đúng.
- `SyncTests.Without_segments_the_bundle_packs_the_roads_of_my_open_orders_and_only_my_orders` (E14) giữ tính độc quyền người được giao; nếu D-4 mở scope thì thêm/sửa assertion ca phiếu null riêng, giữ explicit-segments/replay.
- `PoleLocationTests`, `TopologyInferenceTests`, `IotNodeTests`, `LightingCommandTests`, Citizen/QR tests: giữ ca có tuyến và thêm ca null tương ứng; E02 liệt kê mọi vị trí fixture đang gán SegmentId, không có lý do bắt buộc sửa hết chúng.

**Môi trường test P2:** tài sản ghi trong `AssetDatabaseCollection`; host `LuxMapTestHost` dựng PostGIS riêng, không DB dev; test không DB dùng `NoDatabaseTestHost`; tài khoản/xã riêng và teardown con trước cha. Mỗi guard mới phải phá thử → đỏ → khôi phục bằng `cp`/`touch`, đọc exit code thật. Thứ tự phải có ID hai bên ngưỡng độ rộng, cùng transaction (AGENTS/trap testing đã đọc; E14). **P1 không chạy bất kỳ test nào**, kể cả các bước phá thử trên.

## 7. Tài liệu & thông báo — danh sách bàn giao P2, chưa sửa/chưa gửi

| Nơi / người nhận | Cần ghi / cần thông báo sau duyệt | Căn cứ |
|---|---|---|
| `docs/contract-drift.md` | Mục POLE-SEGMENT-NULL: 4 trường Decision/Decision maker/Date/Scope; bốn quyết định đã chốt, D-item sau khi Mỹ chọn, các nullable response và lỗi, cache/offline. Mỹ chốt một mình chạm API thì **SELF-SIGNED, nền tạm tới FW**; cần WP5/WP6/WP4 xác nhận, không coi im lặng là approve API | FW-00 `:15–20`; TOPO-INFER `:1191–1211` (E13) |
| `docs/api-contract-v1.1.md` | Sau duyệt và tăng version: §5.1 map/detail, §5.3 POST/PUT/read/filter/import, §5.4 faults, §5.5 WO poles theo WO-12/drift, §5.8 sync; thống kê/QR/topology/lighting đang ở drift liên quan cũng phải mô tả đúng. Không coi ticket đủ quyền âm thầm sửa Contract P1 | E13/E18 |
| `docs/openapi/luxmap-v1.json`, `luxmap-v1.5.json`, generator | Sinh lại, nullable key đúng; cập nhật schema override PoleProperties; kiểm consumer model; không sửa file sinh bằng tay | E13/E18; trap assets |
| `docs/database/erd.md` | Quan hệ optional road→pole, không đổi fault_cluster hay tuyến của survey | E13, E04 |
| `docs/agents/traps/assets.md` | Nhập cột không cần tuyến; PUT/import vắng/null; topology/dời cột khi null; filter phân biệt chưa feeder/chưa segment; giữ same-commune feeder và inter_commune segment | E05/E06; trap đã đọc |
| `spatial-map.md`, `survey.md`, `faults-workorders.md` | Giữ hàng map null, ordering không đường; Targets shared/ảnh; survey chẩn đoán/snapshot/đổi tuyến; fault nullable và bundle scope/cache sau D-item | E07–E12 |
| `migrations.md`, `commune-scope.md`, `testing.md` | Chỉ bổ sung bẫy **mới** được P2 xác minh: Down refusal null, scope không theo tuyến, chốt null equality và test. Không chép quy tắc lặp nếu file đã có | E04/E14/E20 |
| `docs/templates/README.md`, `poles.csv`, `poles-example.csv` | `segment_external_ref` optional/nullable và mẫu ô trống; ref sai vẫn lỗi; thứ tự nhập chỉ bắt buộc cho quan hệ thực sự supplied; upsert blank theo D-2. Hướng dẫn/cảnh báo `photos_to_poles.py` liên quan, không đổi tự gán tuyến | E03/E06/E15 |
| `mocks/`, script seed/copy | Thêm ca chưa tuyến nếu D-11 duyệt, kiểm seed không index map bằng null; giữ bộ ID đã bàn giao. Copy PLAN không cần bảng mới vì không thêm bảng | E15/E18 |
| `tracking.html` | Chỉ cập nhật tiến độ sau P2 hoàn tất theo quy định repo; P1 bị người giao cấm sửa, không làm dashboard mới | Ticket + yêu cầu người giao |
| WP5 | Form tạo/sửa null/vắng, bộ lọc, nhãn chưa phân tuyến, map/kiểm kê/QR/diagram, nhóm thống kê, mã lỗi, model nullable. Đừng suy null là pole lỗi hoặc bị xoá | E05/E08/E12/E18 |
| WP6 | Model nullable, WO `position` D-1, bundle/cache D-4, ảnh cột/QR, lỗi/retry từng sync op. Phải phối hợp trước khi cho response null vào client non-null | E07/E10/E18 |
| WP4 | Cột chưa tuyến không nằm trong target survey/cluster, không suy tuyến từ gần nhất; provenance/snapshot/đổi tuyến D-7/D-8; không mất mẫu nghiên cứu im lặng | E09/E11 |

**Kiểm chứng P1:** ✅ Đã khảo sát tĩnh bằng lệnh chỉ đọc và đối chiếu các hit với bảng; kiểm tra tài liệu E22 xác nhận đủ 8 mục, 14 D-item và diff HEAD các đường dẫn bị cấm rỗng. ⚠️ Không xác minh runtime HTTP/SQL EF, build, test, migration hay catalog DB vì P1 chỉ khảo sát và người giao cấm test/EF/DB. 📌 Cơ chế và shape còn mơ hồ để ở D-item cuối file; không có thay đổi kiến trúc nào được chọn thay Mỹ.

**⚠️ Bàn giao chưa commit:** thao tác stage theo đúng đường dẫn bị sandbox từ chối tạo Git `index.lock` ở metadata worktree ngoài vùng ghi. File kết quả còn untracked, index không có file staged, HEAD vẫn như đầu phiên (E23). Không chạy được bước commit yêu cầu `docs(ai): POLE-SEGMENT-NULL phase 1 survey`; không push và không tự chuyển P2. Đây là giới hạn môi trường, không phải D-item kiến trúc.

### Phụ lục bằng chứng chạy thật

Các khối dưới đây là stdout thu từ subprocess của lệnh ghi kèm; không phải ví dụ dự kiến. Lệnh Python E18 chỉ đọc JSON; E19 chỉ tìm trong migrations. Không chạy nội dung SQL/C# xuất hiện trong stdout. Các đường dẫn `/tmp/…` là bộ thu bằng chứng tạm, không phải artifact bàn giao hay dependency để đọc báo cáo.

<details>
<summary>E00 — lệnh và output thật</summary>

```bash
pwd; git branch --show-current; git rev-parse HEAD; git status --short; ls -l AGENTS.md
```

```text
/Users/nhm809/Documents/LuxMap/wt-pole-segment-null
docs/POLE-SEGMENT-NULL-p1
89c4ce218faf79d90fa09a81ad7b197c3349b525
lrwxr-xr-x@ 1 nhm809  staff  9 Nov  2 15:46 AGENTS.md -> CLAUDE.md

[exit=0]
```

</details>

<details>
<summary>E01 — lệnh và output thật</summary>

```bash
grep -rIn "SegmentId\|segment_id" src scripts mocks --include='*.cs' --include='*.sql' --include='*.py' --include='*.json' --exclude-dir=Migrations --exclude-dir=bin --exclude-dir=obj
```

```text
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:86:        builder.HasKey(segment => segment.SegmentId);
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:88:        builder.Property(segment => segment.SegmentId).HasPrefixedId(PrefixedIds.RoadSegment);
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:198:        builder.Property(pole => pole.SegmentId).HasColumnType("text").IsRequired();
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:226:            .HasForeignKey(pole => pole.SegmentId)
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:246:        // Note segment_id deliberately gets NO such key: road_class = inter_commune means the road
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:109:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:314:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:388:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:490:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:636:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:42:        => ListAsync(communes, page, (RoadSegment segment) => segment.SegmentId, SegmentRow, ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:111:                    .Where(segment => segment.SegmentId == pole.SegmentId)
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:135:            .Where(segment => segment.SegmentId == segmentId);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:259:        return segment.SegmentId;
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:294:        await RequireAsync<RoadSegment>(segment => segment.SegmentId == request.SegmentId, "road segment", ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:304:            SegmentId = request.SegmentId!,
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:391:        var segment = await RequireAsync<RoadSegment>(candidate => candidate.SegmentId == segmentId, "road segment", ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:477:        await RequireAsync<RoadSegment>(segment => segment.SegmentId == request.SegmentId, "road segment", ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:486:        pole.SegmentId = request.SegmentId!;
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:535:    /// Three tables hold a segment with RESTRICT and they are in TWO modules: <c>pole.segment_id</c>
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:536:    /// here, <c>fault.segment_id</c> and <c>fault_cluster.segment_id</c> in Faults. So a segment that
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:547:            candidate => candidate.SegmentId == segmentId,
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:728:            .Where(segment => segment.SegmentId == pole.SegmentId)
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:742:                        ["pole_id"] = pole.PoleId, ["segment_id"] = pole.SegmentId,
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:749:                new { lat, lng, moved_m = Math.Round(moved, 2), pole.SegmentId, segment_distance_m = Math.Round(offSegment, 2) }));
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:828:        await RequireAsync<RoadSegment>(segment => segment.SegmentId == segmentId, "road segment", ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:830:        return await TopologyPageAsync(pole => pole.SegmentId == segmentId, page, ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:879:                SegmentId = pole.SegmentId,
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:966:            SegmentId = pole.SegmentId,
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:1008:            SegmentId = segment.SegmentId,
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:1020:            PoleCount = dbContext.Set<Pole>().Count(pole => pole.SegmentId == segment.SegmentId),
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:1172:    /// <c>segment_id</c> gets NO such key, and that is not an omission: <c>road_class =
src/LuxMap.Modules.Assets/Import/ImportContracts.cs:19:    /// <c>feeder.cabinet_id</c> must exist if filled, <c>pole.segment_id</c> is NOT NULL,
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:424:                pole.SegmentId = segment!.Id;
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:437:                SegmentId = segment!.Id,
src/LuxMap.Modules.Assets/Entities/Pole.cs:34:    public required string SegmentId { get; set; }
src/LuxMap.Modules.Assets/Entities/RoadSegment.cs:20:/// <item><c>pole_count</c> — <c>COUNT(pole WHERE segment_id = ...)</c>.</item>
src/LuxMap.Modules.Assets/Entities/RoadSegment.cs:30:    public string SegmentId { get; set; } = null!;
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:51:            AffectedSegmentIds = plan.AffectedSegmentIds,
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:72:            new { actorId, request.FeederId, request.SegmentId, mode }, LuxMapJsonOptions.Default)));
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:79:        var plan = await PlanAsync(request.FeederId, request.SegmentId, ct);
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:100:        var locked = await PlanAsync(request.FeederId, request.SegmentId, ct);
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:158:            AffectedSegmentIds = [.. plan.AffectedSegmentIds],
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:603:            throw Invalid("feeder_id", "Send exactly one of feeder_id or segment_id.");
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:619:            communeId = await db.Set<RoadSegment>().Where(segment => segment.SegmentId == segmentId)
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:621:            scope = db.Set<Pole>().Where(pole => pole.SegmentId == segmentId);
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:666:                .Select(pole => pole.SegmentId).Distinct().ToListAsync(ct))
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:710:            AffectedSegmentIds = request.AffectedSegmentIds,
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:771:        IReadOnlyList<string> AffectedSegmentIds,
src/LuxMap.Modules.Telemetry/Lighting/LightingController.cs:32:        [FromQuery(Name = "feeder_id")] string? feederId, [FromQuery(Name = "segment_id")] string? segmentId, CancellationToken ct)
src/LuxMap.Modules.Telemetry/Lighting/LightingEntities.cs:39:    public required string[] AffectedSegmentIds { get; set; }
src/LuxMap.Modules.Telemetry/Lighting/LightingConfigurations.cs:24:        builder.Property(request => request.AffectedSegmentIds).HasColumnType("text[]").IsRequired();
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:48:    public required IReadOnlyList<string> AffectedSegmentIds { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:54:/// <summary><c>POST /lighting/commands</c>: exactly one of <c>feeder_id</c> / <c>segment_id</c>.</summary>
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:61:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:92:    public required IReadOnlyList<string> AffectedSegmentIds { get; init; }
src/LuxMap.Shared/Contracts/PrefixedId.cs:57:    public static readonly PrefixedIdSpec RoadSegment = new("SEG", 3, "segment_id_seq");
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:366:            .Select(p => new { p.PoleId, p.SegmentId, p.Geom }).SingleAsync(ct);
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:410:                PoleId = pole.PoleId, SegmentId = pole.SegmentId, Location = new ReportPoint(pole.Geom.Y, pole.Geom.X),
src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs:72:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Citizen/PublicReportService.cs:72:                    .Where(s => s.SegmentId == p.SegmentId).Select(s => s.SegmentName).First(),
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:18:            table.HasCheckConstraint("ck_work_order_survey_target", "task_kind <> 'survey' OR (segment_id IS NULL AND cluster_id IS NULL)");
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:54:        builder.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:63:        foreach (var property in new[] { "CommuneId", "AssignedTo", "CreatedBy", "SegmentId", "ClusterId", "WoStatus" })
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:97:        builder.HasIndex(x => new { x.WorkOrderId, x.SegmentId }).IsUnique();
src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:103:        builder.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:70:        if (segment is not null) query = query.Where(x => x.SegmentId == segment);
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:92:            SegmentId = wo.SegmentId, ClusterId = wo.ClusterId, WoStatus = wo.WoStatus, AssignedTo = wo.AssignedTo,
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:118:            SegmentId = wo.SegmentId, ClusterId = wo.ClusterId, WoStatus = wo.WoStatus, AssignedTo = wo.AssignedTo,
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:124:            SegmentIds = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).OrderBy(x => x.Position).Select(x => x.SegmentId).ToArrayAsync(ct),
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:132:            Faults = members.Select(x => new WorkOrderFaultDetail(x.Fault.FaultId, x.Fault.PoleId, x.Fault.SegmentId,
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:146:        if (request.SegmentIds is not null) throw OptionalJson.Invalid("segment_ids");
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:149:            || (ids.Length > 0 && request.SegmentId is not null)
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:150:            || (ids.Length == 0 && (kind != TaskKind.Inspection || string.IsNullOrWhiteSpace(request.SegmentId))))
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:151:            throw OptionalJson.Invalid("fault_ids / segment_id");
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:157:            : await db.Set<RoadSegment>().FirstOrDefaultAsync(x => x.SegmentId == request.SegmentId, ct)
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:166:        var ids = request.SegmentIds ?? [];
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:169:            || ids.Distinct().Count() != ids.Length || request.SegmentId is not null || request.FaultIds is { Length: > 0 })
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:170:            throw OptionalJson.Invalid("segment_ids / commune_id / fault_ids");
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:173:            || await db.Set<RoadSegment>().CountAsync(x => ids.Contains(x.SegmentId), ct) != ids.Length)
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:193:            db.Add(new WorkOrderSegment { WorkOrderId = id, CommuneId = commune, Position = position, SegmentId = ids[position] });
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:194:        Record(wo, AuditAction.Created, null, new { work_order = Snapshot(wo, []), segment_ids = ids }, now);
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:203:        var roads = await db.Set<RoadSegment>().Where(x => ids.Contains(x.SegmentId)).Select(x => x.CommuneId).ToArrayAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:206:        var communes = roads.Concat(await db.Set<Pole>().Where(x => ids.Contains(x.SegmentId)).Select(x => x.CommuneId).Distinct().ToArrayAsync(ct)).Distinct();
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:289:            segment = faults.Where(x => x.SegmentId is not null).OrderByDescending(x => x.PriorityScore)
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:290:                .ThenBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).FirstOrDefault()?.SegmentId;
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:301:            segment = order.Road.SegmentId;
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:311:            WorkOrderId = id, CommuneId = commune, TaskKind = order.Kind, Title = order.Title, SegmentId = segment,
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:335:        if (new[] { request.WoStatus, request.AssignedTo, request.FaultIds, request.TaskKind, request.SegmentId, request.SegmentIds }.Any(OptionalJson.Present))
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:381:                var segments = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).Select(x => x.SegmentId).ToArrayAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:647:            wo.ClosedAt, wo.CreatedAt, wo.UpdatedAt, wo.CreatedBy, wo.CommuneId, wo.SegmentId, wo.ClusterId,
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:658:        DateTime CreatedAt, DateTime UpdatedAt, string CreatedBy, string CommuneId, string? SegmentId, string? ClusterId,
src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:13:    public string? SegmentId { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:37:    public string[] SegmentIds { get; init; } = [];
src/LuxMap.Modules.WorkOrders/WorkOrderResponses.cs:54:public sealed record WorkOrderFaultDetail(string FaultId, string? PoleId, string? SegmentId,
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:23:    public required string SegmentId { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:61:/// <c>segment_id</c> of inspection and repair orders) UNION the poles carrying this order's faults, so a
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:80:        db.Set<WorkOrder>().Any(w => w.WorkOrderId == id && w.SegmentId == p.SegmentId)
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:81:        || db.Set<WorkOrderSegment>().Any(s => s.WorkOrderId == id && s.SegmentId == p.SegmentId)
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:88:            .Select(x => new { x.SegmentId }).FirstOrDefaultAsync(ct)
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:92:            .OrderBy(x => x.Position).Select(x => x.SegmentId).ToListAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:93:        if (order.SegmentId is { } own && !segmentIds.Contains(own)) segmentIds.Add(own);
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:108:                p.PoleId, p.SegmentId, p.Geom, p.NearSensitivePoi,
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:121:        var roadIds = rows.Select(r => r.SegmentId).Distinct().ToArray();
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:126:        var lines = (await db.Set<RoadSegment>().AsNoTracking().IgnoreQueryFilters().Where(s => roadIds.Contains(s.SegmentId))
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:127:            .Select(s => new { s.SegmentId, s.Geom }).ToListAsync(ct))
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:128:            .ToDictionary(s => s.SegmentId, s => new LengthIndexedLine(s.Geom));
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:134:                SegmentOrder = segmentIds.IndexOf(r.SegmentId) is var at and >= 0 ? at : int.MaxValue,
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:135:                Along = lines.TryGetValue(r.SegmentId, out var line) ? line.Project(r.Geom.Coordinate) : 0d,
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:137:            .OrderBy(x => x.SegmentOrder).ThenBy(x => x.Row.SegmentId.Length).ThenBy(x => x.Row.SegmentId, StringComparer.Ordinal)
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:144:            var position = positions[x.Row.SegmentId] = positions.GetValueOrDefault(x.Row.SegmentId) + 1;
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:149:                SegmentId = r.SegmentId,
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:82:    public string[] SegmentIds { get; init; } = [];
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:93:/// <summary>The orders under one road — or, for orders with no road at all, under their commune (<c>segment_id</c> null).</summary>
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:96:    public string? SegmentId { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:138:/// Each order appears ONCE, under its first road: the first survey segment, else its own <c>segment_id</c>, else
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:164:            .OrderBy(x => x.Position).Select(x => new { x.WorkOrderId, x.SegmentId }).ToListAsync(ct))
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:165:            .ToLookup(x => x.WorkOrderId, x => x.SegmentId);
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:173:                             select new { link.WorkOrderId, SegmentId = pole != null ? pole.SegmentId : fault.SegmentId,
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:180:            : new[] { wo.SegmentId }.Concat(faults[wo.WorkOrderId].Select(x => x.SegmentId)
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:187:        var roads = await db.Set<RoadSegment>().AsNoTracking().Where(s => firstRoads.Contains(s.SegmentId))
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:188:            .Select(s => new { s.SegmentId, s.SegmentName, s.CommuneId, s.CreatedAt, s.Geom }).ToDictionaryAsync(s => s.SegmentId, ct);
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:190:            .Where(s => firstRoads.Contains(s.SegmentId))
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:191:            .Select(s => new { s.SegmentId, Metres = SpatialFunctions.DistanceMeters(s.Geom, origin) })
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:192:            .ToDictionaryAsync(s => s.SegmentId, s => s.Metres, ct);
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:205:            SegmentIds = roadsOf[wo.WorkOrderId],
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:211:        foreach (var bucket in entries.GroupBy(x => x.SegmentIds is [var road, ..] ? road : "\0" + x.CommuneId))
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:217:            var segmentId = orders[0].SegmentIds is [var key, ..] ? key : null;
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:222:                    SegmentId = segmentId, SegmentName = road.SegmentName, CommuneId = road.CommuneId,
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:231:                SegmentId = segmentId, CommuneId = orders[0].CommuneId, Location = location,
src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:12:    public string? SegmentId { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:14:    public string[]? SegmentIds { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:38:    public JsonElement SegmentId { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrderRequests.cs:39:    public JsonElement SegmentIds { get; init; }
src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:27:        [FromQuery(Name = "segment_id")] string? segment,
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:125:        if (fresh.CommuneId != target.CommuneId || fresh.DataSource != target.DataSource || fresh.SegmentId != target.SegmentId)
src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:19:    public string? SegmentId { get; set; }
src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:77:    public required string SegmentId { get; set; }
src/LuxMap.Modules.Survey/Configurations/SurveyProcessingConfigurations.cs:57:        b.HasOne<RoadSegment>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Modules.Survey/Processing/Frames/SurveyFramePipeline.cs:142:            var pole = poles.Single(x => x.PoleId == p.PoleId && x.SegmentId == pass.Segment);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:79:                .OrderBy(x => x.Position).Select(x => x.SegmentId).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:92:            foreach (var route in gps.Where(x => x.SegmentId is not null && !x.RouteAmbiguous && x.RouteDistanceM <= o.RouteCorridorM).GroupBy(x => x.SegmentId!))
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:95:                var expected = poles.Where(x => x.SegmentId == route.Key).Select(x => new PolePosition(x.PoleId, x.CommuneId, x.ChainageM)).ToArray();
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:98:                    x.SegmentId != route.Key && !x.RouteAmbiguous && x.RouteDistanceM <= o.RouteCorridorM)).ToArray();
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:147:            UNION SELECT r.commune_id FROM work_order_segment w JOIN road_segment r USING (segment_id)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:149:            UNION SELECT p.commune_id FROM work_order_segment w JOIN pole p USING (segment_id)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:256:                Run = run, SegmentId = result.Segment, PassNo = number++,
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:8:    public string? SegmentId { get; set; }
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:21:    public required string SegmentId { get; set; }
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:49:        SELECT nearest.segment_id AS segment_id, g.phone_elapsed_ns AS time_ns,
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:57:            SELECT candidate.*, lead(distance_m) OVER (ORDER BY distance_m, segment_id) AS second_distance_m
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:59:                SELECT r.segment_id, ST_AsGeoJSON(r.geom) AS route_geometry_json,
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:65:                WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:69:                        WHERE bounded.segment_id = ANY({segments}) AND bounded.commune_id = ANY({communes})
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:72:            ORDER BY distance_m, segment_id LIMIT 1
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:80:        SELECT r.segment_id AS segment_id, p.pole_id AS pole_id, p.commune_id AS commune_id,
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:87:        FROM road_segment r JOIN pole p ON p.segment_id = r.segment_id
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:93:        WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:96:        ORDER BY r.segment_id, chainage_m, length(p.pole_id), p.pole_id
src/LuxMap.Modules.Survey/Ingest/SweepsController.cs:71:        [FromQuery(Name = "segment_id")] string? segment,
src/LuxMap.Modules.Survey/Ingest/SurveyContracts.cs:19:    string[] SegmentIds, int FrameCount, double? CoveragePct, SweepProcessingStatus ProcessingStatus,
src/LuxMap.Modules.Survey/Ingest/SurveyContracts.cs:27:    string[] SegmentIds, int FrameCount, double? CoveragePct, SweepProcessingStatus ProcessingStatus,
src/LuxMap.Modules.Survey/Ingest/SurveyContracts.cs:30:    : SweepResponse(SweepId, WorkOrderId, StartedAt, EndedAt, SegmentIds, FrameCount, CoveragePct, ProcessingStatus, Status, DataSource,
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:31:            && !db.Set<RoadSegment>().Any(road => road.SegmentId == link.SegmentId))));
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:47:        var ids = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).Select(x => x.SegmentId).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:49:            || await db.Set<RoadSegment>().CountAsync(x => ids.Contains(x.SegmentId), ct) != ids.Length)
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:243:        return new SweepDetailResponse(s.SweepId, s.WorkOrderId, s.StartedAt, s.EndedAt, s.SegmentIds, s.FrameCount, s.CoveragePct,
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:252:        if (segment is not null) query = query.Where(x => db.Set<WorkOrderSegment>().Any(w => w.WorkOrderId == x.WorkOrderId && w.SegmentId == segment));
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:269:            .Select(x => new { x.WorkOrderId, x.SegmentId }).ToArrayAsync(ct)).ToLookup(x => x.WorkOrderId, x => x.SegmentId);
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:61:                FaultId = id, PoleId = pole.PoleId, CommuneId = pole.CommuneId, SegmentId = pole.SegmentId,
src/LuxMap.Modules.Survey/Review/SweepCoverage.cs:13:    string SegmentId, double? LengthM, double CoveredRatio, PassCoverage[] Passes, CoverageGap[] Gaps,
src/LuxMap.Modules.Survey/Review/SurveyMediaAccess.cs:30:            && !db.Set<RoadSegment>().Any(r => r.SegmentId == link.SegmentId))));
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:105:            ? poles.EnumerateArray().Select(p => (Segment: p.GetProperty("segment_id").GetString()!, Pole: p.GetProperty("pole_id").GetString()!))
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:111:        var lengths = await db.Set<RoadSegment>().AsNoTracking().Where(r => segmentIds.Contains(r.SegmentId))
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:112:            .ToDictionaryAsync(r => r.SegmentId, r => (double?)r.LengthM, ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:117:            var driven = passes.Where(p => p.SegmentId == segment).ToArray();
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:147:            FROM unnest({segments}::text[], {froms}::float8[], {tos}::float8[]) WITH ORDINALITY AS g(segment_id, from_fraction, to_fraction, ordinal)
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:148:            JOIN road_segment r ON r.segment_id = g.segment_id AND r.commune_id = ANY({communes})
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:307:                var fault = new Fault { FaultId = faultId, PoleId = o.PoleId, CommuneId = o.CommuneId, SegmentId = pole.SegmentId,
src/LuxMap.Modules.Survey/Entities/SurveyProcessing.cs:41:    public required string SegmentId { get; set; }
src/LuxMap.Modules.Sync/SyncBundleService.cs:51:            .Where(pole => segmentIds.Contains(pole.SegmentId))
src/LuxMap.Modules.Sync/SyncBundleService.cs:58:            SegmentIds = segmentIds,
src/LuxMap.Modules.Sync/SyncBundleService.cs:83:                $"segment_id names between 1 and {MaxSegments} segments.",
src/LuxMap.Modules.Sync/SyncBundleService.cs:84:                new Dictionary<string, object?> { ["field"] = "segment_id", ["max"] = MaxSegments });
src/LuxMap.Modules.Sync/SyncBundleService.cs:87:        var visible = await db.Set<RoadSegment>().Where(segment => ids.Contains(segment.SegmentId)).Select(segment => segment.SegmentId).ToListAsync(ct);
src/LuxMap.Modules.Sync/SyncBundleService.cs:92:                new Dictionary<string, object?> { ["segment_id"] = missing });
src/LuxMap.Modules.Sync/SyncBundleService.cs:99:    /// D-2 — no <c>segment_id</c>: "tonight's work". Every segment of the caller's open work orders — the order's own
src/LuxMap.Modules.Sync/SyncBundleService.cs:109:            .Where(segment => mine.Any(wo => wo.SegmentId == segment.SegmentId)
src/LuxMap.Modules.Sync/SyncBundleService.cs:110:                || db.Set<WorkOrderSegment>().Any(s => s.SegmentId == segment.SegmentId && mine.Any(wo => wo.WorkOrderId == s.WorkOrderId))
src/LuxMap.Modules.Sync/SyncBundleService.cs:111:                || held.Any(fault => fault.SegmentId == segment.SegmentId)
src/LuxMap.Modules.Sync/SyncBundleService.cs:112:                || db.Set<Pole>().Any(pole => pole.SegmentId == segment.SegmentId && held.Any(fault => fault.PoleId == pole.PoleId)))
src/LuxMap.Modules.Sync/SyncBundleService.cs:113:            .OrderBy(segment => segment.CreatedAt).ThenBy(segment => segment.SegmentId.Length).ThenBy(segment => segment.SegmentId)
src/LuxMap.Modules.Sync/SyncBundleService.cs:114:            .Select(segment => segment.SegmentId)
src/LuxMap.Modules.Sync/SyncBundleService.cs:121:        var onSegments = db.Set<Fault>().Where(fault => (fault.SegmentId != null && segmentIds.Contains(fault.SegmentId))
src/LuxMap.Modules.Sync/SyncBundleService.cs:122:            || db.Set<Pole>().Any(pole => pole.PoleId == fault.PoleId && segmentIds.Contains(pole.SegmentId)));
src/LuxMap.Modules.Sync/SyncBundleService.cs:125:            .Where(wo => (wo.SegmentId != null && segmentIds.Contains(wo.SegmentId))
src/LuxMap.Modules.Sync/SyncBundleService.cs:126:                || db.Set<WorkOrderSegment>().Any(s => s.WorkOrderId == wo.WorkOrderId && segmentIds.Contains(s.SegmentId))
src/LuxMap.Modules.Sync/SyncController.cs:23:    /// <c>segment_id</c> repeats for several roads (at most 20); without it, the roads of the caller's open work
src/LuxMap.Modules.Sync/SyncController.cs:33:        [FromQuery(Name = "segment_id")] string[]? segmentIds,
src/LuxMap.Modules.Sync/SyncContracts.cs:24:    public required IReadOnlyList<string> SegmentIds { get; init; }
src/LuxMap.Modules.Sync/SyncContracts.cs:26:    /// <summary>Same properties as <c>GET /map/segments</c>, in the order of <see cref="SegmentIds"/>.</summary>
src/LuxMap.Modules.Map/Features/PoleMapDetail.cs:17:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/MapController.cs:68:        [FromQuery(Name = "segment_id")] string? segmentId,
src/LuxMap.Modules.Map/Features/MapController.cs:79:                SegmentId = segmentId,
src/LuxMap.Modules.Map/Features/MapController.cs:129:    /// <c>segment_ids</c> and <c>feeder_ids</c> as derived lists, <c>pole_id</c> always null.
src/LuxMap.Modules.Map/Features/MapProperties.cs:27:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/MapProperties.cs:93:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/MapProperties.cs:135:/// Replaces the shape of Contract section 5.6: <c>battery_pct</c> is gone (I-2), <c>segment_id</c>
src/LuxMap.Modules.Map/Features/MapProperties.cs:136:/// became <see cref="SegmentIds"/>, and <see cref="FeederIds"/> and
src/LuxMap.Modules.Map/Features/MapProperties.cs:161:    public required IReadOnlyList<string> SegmentIds { get; init; }
src/LuxMap.Modules.Map/Features/MapQueryService.cs:87:            .Where(pole => segmentIds.Contains(pole.SegmentId))
src/LuxMap.Modules.Map/Features/MapQueryService.cs:94:            .Where(segment => segmentIds.Contains(segment.SegmentId)), ct);
src/LuxMap.Modules.Map/Features/MapQueryService.cs:96:        return new FeatureCollection<SegmentProperties> { Features = [.. layer.Features.OrderBy(f => order[f.Properties.SegmentId])] };
src/LuxMap.Modules.Map/Features/MapQueryService.cs:105:                pole.SegmentId,
src/LuxMap.Modules.Map/Features/MapQueryService.cs:155:                    SegmentId = row.SegmentId,
src/LuxMap.Modules.Map/Features/MapQueryService.cs:210:                segment.SegmentId,
src/LuxMap.Modules.Map/Features/MapQueryService.cs:216:                PoleCount = dbContext.Set<Pole>().Count(pole => pole.SegmentId == segment.SegmentId),
src/LuxMap.Modules.Map/Features/MapQueryService.cs:221:                    .Any(fault => fault.SegmentId == segment.SegmentId
src/LuxMap.Modules.Map/Features/MapQueryService.cs:232:                            pole.SegmentId == segment.SegmentId && pole.FeederId == control.FeederId)))
src/LuxMap.Modules.Map/Features/MapQueryService.cs:249:                    SegmentId = row.SegmentId,
src/LuxMap.Modules.Map/Features/MapQueryService.cs:286:                SegmentIds = dbContext.Set<RoadSegment>()
src/LuxMap.Modules.Map/Features/MapQueryService.cs:288:                        pole.SegmentId == segment.SegmentId
src/LuxMap.Modules.Map/Features/MapQueryService.cs:292:                    .ThenBy(segment => segment.SegmentId.Length)
src/LuxMap.Modules.Map/Features/MapQueryService.cs:293:                    .ThenBy(segment => segment.SegmentId)
src/LuxMap.Modules.Map/Features/MapQueryService.cs:294:                    .Select(segment => segment.SegmentId)
src/LuxMap.Modules.Map/Features/MapQueryService.cs:315:                    SegmentIds = row.SegmentIds,
src/LuxMap.Modules.Map/Features/MapQueryService.cs:471:        if (query.SegmentId is { Length: > 0 } segmentId)
src/LuxMap.Modules.Map/Features/MapQueryService.cs:473:            poles = poles.Where(pole => pole.SegmentId == segmentId);
src/LuxMap.Modules.Map/Features/PoleDetailService.cs:52:                p.PoleId, p.SegmentId, p.CommuneId, p.Geom,
src/LuxMap.Modules.Map/Features/PoleDetailService.cs:53:                SegmentName = db.Set<RoadSegment>().Where(s => s.SegmentId == p.SegmentId)
src/LuxMap.Modules.Map/Features/PoleDetailService.cs:78:            SegmentId = pole.SegmentId,
src/LuxMap.Modules.Map/Features/MapQueries.cs:19:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:28:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:90:            .Select(pole => new { pole.PoleId, FeederId = pole.FeederId!, pole.SegmentId, pole.Geom, pole.FeederSource })
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:93:        var segmentIds = poles.Select(pole => pole.SegmentId).Distinct().ToArray();
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:95:                .Where(segment => segmentIds.Contains(segment.SegmentId))
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:96:                .Select(segment => new { segment.SegmentId, segment.CreatedAt, segment.Geom })
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:98:            .ToDictionary(segment => segment.SegmentId, StringComparer.Ordinal);
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:108:                .Where(pole => pole.FeederId == feeder.FeederId && roads.ContainsKey(pole.SegmentId))
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:109:                .GroupBy(pole => pole.SegmentId, StringComparer.Ordinal)
src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs:150:                                SegmentId = group.Key,
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:34:        builder.Property(cluster => cluster.SegmentId).HasColumnType("text").IsRequired();
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:44:            .HasForeignKey(cluster => cluster.SegmentId)
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:47:        builder.HasIndex(cluster => cluster.SegmentId)
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:48:            .HasDatabaseName("ix_fault_cluster_segment_id");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:64:        foreach (var text in new[] { "ClientOpId", "PoleId", "FixtureId", "SegmentId", "ClusterId",
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:143:            .HasForeignKey(fault => fault.SegmentId).OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:166:        builder.HasIndex(fault => fault.SegmentId).HasDatabaseName("ix_fault_segment_id");
src/LuxMap.Modules.Faults/FaultReportService.cs:143:            communeId = pole.CommuneId; segmentId = pole.SegmentId; source = pole.DataSource;
src/LuxMap.Modules.Faults/FaultReportService.cs:161:            SegmentId = segmentId, CommuneId = communeId, Lat = request.Location?.Lat, Lng = request.Location?.Lng,
src/LuxMap.Modules.Faults/FaultReportService.cs:207:            FaultId = await NextFaultId(ct), PoleId = pole.PoleId, SegmentId = pole.SegmentId, CommuneId = pole.CommuneId,
src/LuxMap.Modules.Faults/FaultReportService.cs:260:            FaultId = item.FaultId, PoleId = item.PoleId, FixtureId = item.FixtureId, SegmentId = item.SegmentId,
src/LuxMap.Modules.Faults/FaultListQuery.cs:21:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Faults/FaultItem.cs:18:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Faults/FaultQueryService.cs:61:        if (query.SegmentId is { } segment)
src/LuxMap.Modules.Faults/FaultQueryService.cs:63:            faults = faults.Where(fault => fault.SegmentId == segment);
src/LuxMap.Modules.Faults/FaultQueryService.cs:102:        var poles = db.Set<Pole>().Where(pole => segmentIds.Contains(pole.SegmentId)).Select(pole => pole.PoleId);
src/LuxMap.Modules.Faults/FaultQueryService.cs:104:            && ((fault.SegmentId != null && segmentIds.Contains(fault.SegmentId)) || (fault.PoleId != null && poles.Contains(fault.PoleId))));
src/LuxMap.Modules.Faults/FaultQueryService.cs:144:            SegmentId = row.Fault.SegmentId,
src/LuxMap.Modules.Faults/FaultsController.cs:55:        [FromQuery(Name = "segment_id")] string? segmentId,
src/LuxMap.Modules.Faults/FaultsController.cs:71:                SegmentId = Single(segmentId),
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs:39:    public required string SegmentId { get; set; }
src/LuxMap.Modules.Faults/Entities/Fault.cs:46:    public string? SegmentId { get; set; }
src/LuxMap.Modules.Statistics/StatisticsContracts.cs:35:    public required string? SegmentId { get; init; }
src/LuxMap.Modules.Statistics/StatisticsContracts.cs:104:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Statistics/StatisticsService.cs:20:    string? SegmentId,
src/LuxMap.Modules.Statistics/StatisticsService.cs:26:    string? SegmentId,
src/LuxMap.Modules.Statistics/StatisticsService.cs:56:        var poles = Poles(query.CommuneIds, query.SegmentId, query.DataSources);
src/LuxMap.Modules.Statistics/StatisticsService.cs:67:                    SegmentId = bySegment ? p.SegmentId : null,
src/LuxMap.Modules.Statistics/StatisticsService.cs:70:            .GroupBy(x => new { x.DataSource, x.CommuneId, x.SegmentId })
src/LuxMap.Modules.Statistics/StatisticsService.cs:75:                SegmentId = g.Key.SegmentId,
src/LuxMap.Modules.Statistics/StatisticsService.cs:93:                .ThenBy(r => r.SegmentId?.Length).ThenBy(r => r.SegmentId, StringComparer.Ordinal)
src/LuxMap.Modules.Statistics/StatisticsService.cs:182:            from p in Poles(query.CommuneIds, query.SegmentId, query.DataSources)
src/LuxMap.Modules.Statistics/StatisticsService.cs:187:                p.PoleId, p.SegmentId, p.CommuneId, p.DataSource, p.CreatedAt,
src/LuxMap.Modules.Statistics/StatisticsService.cs:224:                PoleId = r.PoleId, SegmentId = r.SegmentId, CommuneId = r.CommuneId, DataSource = r.DataSource,
src/LuxMap.Modules.Statistics/StatisticsService.cs:241:        if (segmentId is not null) poles = poles.Where(p => p.SegmentId == segmentId);
src/LuxMap.Modules.Statistics/StatisticsService.cs:248:                .Select(d => d == StatisticsDimension.Commune ? "commune_id" : "segment_id"))
src/LuxMap.Modules.Statistics/StatisticsController.cs:36:        [FromQuery(Name = "segment_id")] string? segmentId,
src/LuxMap.Modules.Statistics/StatisticsController.cs:100:        [FromQuery(Name = "segment_id")] string? segmentId,
src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:35:            concrete.Description = "WO-5 (provisional): repair requires fault_ids; inspection requires fault_ids OR segment_id; "
src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:36:                + "survey (BE-15, provisional) requires commune_id and an ordered segment_ids. Server-owned fields are rejected.";
src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs:40:            foreach (var name in new[] { "commune_id", "wo_status", "assigned_to", "fault_ids", "task_kind", "segment_id", "segment_ids" }) concrete.Properties.Remove(name);
scripts/seed_mock_set.py:93:    segment_ids = sorted(f["properties"]["segment_id"] for f in segments)
scripts/seed_mock_set.py:94:    demo_feeder = {sid: f"FDR-{i:03d}" for i, sid in enumerate(segment_ids, start=1)}
scripts/seed_mock_set.py:97:        wanted = [demo_feeder[sid] for sid in p["segment_ids"]]
scripts/seed_mock_set.py:140:            "INSERT INTO road_segment (segment_id, segment_name, road_class, length_m, geom, "
scripts/seed_mock_set.py:142:            f"{quote(p['segment_id'])}, {quote(p['segment_name'])}, {quote(p['road_class'])}, "
scripts/seed_mock_set.py:146:            f"{quote(p['segment_id'])});")
scripts/seed_mock_set.py:148:    for sid in segment_ids:
scripts/seed_mock_set.py:158:            "INSERT INTO pole (pole_id, segment_id, feeder_id, feeder_source, commune_id, geom, "
scripts/seed_mock_set.py:160:            f"{quote(p['pole_id'])}, {quote(p['segment_id'])}, {quote(demo_feeder[p['segment_id']])}, 'inferred', {COMMUNE}, "
scripts/seed_mock_set.py:181:        segment = sorted({f["segment_id"] for f in members})
scripts/seed_mock_set.py:186:            "INSERT INTO fault_cluster (cluster_id, segment_id, commune_id, clustered_at, "
scripts/seed_mock_set.py:199:            "INSERT INTO fault (fault_id, client_op_id, pole_id, fixture_id, segment_id, commune_id, "
scripts/seed_mock_set.py:205:            f"{quote(f['segment_id'])}, {COMMUNE}, {location['lat']}, {location['lng']}, "
scripts/seed_mock_set.py:243:            "segment_id, cluster_id, assigned_to, assigned_at, created_by, due_date, started_at, created_at, updated_at) VALUES ("
scripts/seed_mock_set.py:245:            f"{quote(wo['wo_status'])}, {quote(wo['segment_id'])}, {quote(wo['cluster_id'])}, {assigned}, "
scripts/seed_mock_set.py:259:        ("segment_id_seq", "segment_id", "road_segment", 5),
scripts/copy_dev_to_supabase.py:90:    ("road_segment", IN_COMMUNES, "segment_id"),
mocks/mock-pole-detail.json:3: "segment_id": "SEG-002",
mocks/mock-faults.json:10:   "segment_id": "SEG-001",
mocks/mock-faults.json:33:   "segment_id": "SEG-001",
mocks/mock-faults.json:56:   "segment_id": "SEG-001",
mocks/mock-faults.json:79:   "segment_id": "SEG-003",
mocks/mock-faults.json:102:   "segment_id": "SEG-001",
mocks/mock-faults.json:125:   "segment_id": "SEG-002",
mocks/mock-faults.json:148:   "segment_id": "SEG-001",
mocks/mock-faults.json:171:   "segment_id": "SEG-001",
mocks/mock-faults.json:194:   "segment_id": "SEG-002",
mocks/mock-faults.json:217:   "segment_id": "SEG-002",
mocks/mock-faults.json:240:   "segment_id": "SEG-001",
mocks/mock-faults.json:263:   "segment_id": "SEG-003",
mocks/mock-faults.json:286:   "segment_id": "SEG-002",
mocks/mock-faults.json:309:   "segment_id": "SEG-002",
mocks/mock-faults.json:332:   "segment_id": "SEG-001",
mocks/mock-faults.json:355:   "segment_id": "SEG-003",
mocks/mock-faults.json:378:   "segment_id": "SEG-002",
mocks/mock-faults.json:401:   "segment_id": "SEG-001",
mocks/mock-faults.json:424:   "segment_id": "SEG-003",
mocks/mock-faults.json:447:   "segment_id": "SEG-002",
mocks/mock-faults.json:470:   "segment_id": "SEG-003",
mocks/mock-faults.json:493:   "segment_id": "SEG-003",
mocks/mock-faults.json:516:   "segment_id": "SEG-003",
mocks/mock-faults.json:539:   "segment_id": "SEG-003",
mocks/mock-faults.json:562:   "segment_id": "SEG-003",
mocks/mock-faults.json:585:   "segment_id": "SEG-001",
mocks/mock-faults.json:608:   "segment_id": "SEG-003",
mocks/mock-faults.json:631:   "segment_id": "SEG-002",
mocks/mock-work-orders.json:9:   "segment_id": "SEG-003",
mocks/mock-work-orders.json:29:   "segment_id": "SEG-001",
mocks/mock-work-orders.json:45:   "segment_id": "SEG-002",

[exit=0]
```

</details>

<details>
<summary>E02 — lệnh và output thật</summary>

```bash
rg -n 'SegmentId|segment_id|segment_external_ref' tests --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**'
```

```text
tests/LuxMap.Api.Tests/LuxValueFinitenessTests.cs:55:                SegmentId = fixture.SegmentId,
tests/LuxMap.Shared.Tests/SurveyObservationIdentityTests.cs:29:        var forward = new SurveyPass { PassNo = 0, Run = run, SegmentId = "segment", Direction = "forward", QualityFlags = "{}" };
tests/LuxMap.Shared.Tests/SurveyObservationIdentityTests.cs:30:        var reverse = new SurveyPass { PassNo = 1, Run = run, SegmentId = "segment", Direction = "reverse", QualityFlags = "{}" };
tests/LuxMap.Shared.Tests/SurveyObservationIdentityTests.cs:75:        PoleId = id, CommuneId = "commune", SegmentId = "segment", SideOfRoute = 1,
tests/LuxMap.Api.Tests/SurveyIngestTests.cs:62:            db.AddRange(targets); await db.SaveChangesAsync(); roads = targets.Select(x => x.SegmentId).Reverse().ToArray(); return 0;
tests/LuxMap.Api.Tests/SurveyIngestTests.cs:73:            commune_id = commune, segment_ids = roads, assigned_to = users["owner"].UserId }), 201);
tests/LuxMap.Api.Tests/SurveyIngestTests.cs:75:        Assert.Equal(roads, created.GetProperty("segment_ids").EnumerateArray().Select(x => x.GetString()));
tests/LuxMap.Api.Tests/SurveyIngestTests.cs:76:        Assert.Equal(JsonValueKind.Null, created.GetProperty("segment_id").ValueKind);
tests/LuxMap.Shared.Tests/JsonConventionTests.cs:14:        string SegmentId,
tests/LuxMap.Shared.Tests/SurveyPublicationTests.cs:25:        Pass = new() { SegmentId = "SEG-001", Direction = direction, QualityFlags = "{}" },
tests/LuxMap.Api.Tests/FixtureLampCountTests.cs:185:                    SegmentId = segment.SegmentId,
tests/LuxMap.Api.Tests/QrCodeTests.cs:273:            var pole = new Pole { CommuneId = commune, SegmentId = road.SegmentId, DataSource = DataSource.Field, Geom = new Point(107.405, 11.305) { SRID = 4326 } };
tests/LuxMap.Api.Tests/PoleNoteTests.cs:162:        var segmentId = (await Json(await manager.GetAsync($"/api/v1/assets/poles/{existing}"))).GetProperty("pole").GetProperty("segment_id").GetString();
tests/LuxMap.Api.Tests/PoleNoteTests.cs:165:            segment_id = segmentId, commune_id = fixture.CommuneId, geom_wkt = FormattableString.Invariant($"POINT ({Lng} {Lat})"), data_source = "public_imagery", note,
tests/LuxMap.Api.Tests/PoleNoteTests.cs:263:            "external_ref,segment_external_ref,commune_id,geom_wkt,data_source,note"
tests/LuxMap.Api.Tests/PoleNoteTests.cs:284:            ["segment_id"] = detail.GetProperty("pole").GetProperty("segment_id").GetString(),
tests/LuxMap.Api.Tests/PoleNoteTests.cs:330:                    SegmentId = segment.SegmentId, CommuneId = communeId,
tests/LuxMap.Api.Tests/IotNodeTests.cs:50:            "node_id", "node_role", "node_status", "pole_id", "segment_ids", "feeder_ids",
tests/LuxMap.Api.Tests/IotNodeTests.cs:129:        Assert.Equal([first, second], Strings(properties.GetProperty("segment_ids")));
tests/LuxMap.Api.Tests/IotNodeTests.cs:294:        => Strings(Find(body, segment, "segment_id").GetProperty("properties").GetProperty("controller_node_ids"));
tests/LuxMap.Api.Tests/IotNodeTests.cs:374:            return segment.SegmentId;
tests/LuxMap.Api.Tests/IotNodeTests.cs:382:                SegmentId = segmentId,
tests/LuxMap.Api.Tests/NotificationTests.cs:67:            segment = road.SegmentId;
tests/LuxMap.Api.Tests/NotificationTests.cs:262:            task_kind = "inspection", title = "Notification test order", segment_id = segment, assigned_to = assignee, due_date = "2026-10-12",
tests/LuxMap.Api.Tests/NotificationTests.cs:273:            CommuneId = home, TaskKind = TaskKind.Inspection, Title = "Race order", SegmentId = segment, CreatedBy = users["manager"].UserId,
tests/LuxMap.Api.Tests/CitizenReportTests.cs:452:            var planted = new Pole { CommuneId = factory.CommuneId, SegmentId = road.SegmentId, DataSource = DataSource.Field, Geom = new Point(107.505, 11.405) { SRID = 4326 } };
tests/LuxMap.Api.Tests/ScheduledJobTests.cs:353:        Assert.Null(fault.SegmentId);
tests/LuxMap.Api.Tests/FaultSchemaTests.cs:75:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/FaultSchemaTests.cs:301:            SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:181:        Assert.Equal(0, await CountAsync<RoadSegment>(s => s.SegmentId == segmentId));
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:199:        Assert.Equal(1, await CountAsync<RoadSegment>(s => s.SegmentId == segmentId));
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:318:            segment_id = segmentId,
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:350:            segment_id = segmentId,
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:383:            segment_id = foreignSegment,
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:391:            .Where(p => p.PoleId == poleId).Select(p => new { p.SegmentId, p.CommuneId }).SingleAsync());
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:393:        Assert.Equal(foreignSegment, stored.SegmentId);
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:407:            segment_id = invisible,
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:460:            .SingleAsync(s => s.SegmentId == segmentId));
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:500:                return segment.SegmentId;
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:531:                    SegmentId = segmentId,
tests/LuxMap.Api.Tests/PoleWriteTests.cs:391:        segment_id = segmentId,
tests/LuxMap.Api.Tests/PoleWriteTests.cs:415:                return segment.SegmentId;
tests/LuxMap.Api.Tests/PoleWriteTests.cs:459:                    SegmentId = segment.SegmentId,
tests/LuxMap.Api.Tests/WarrantyTests.cs:79:            road = roads[0].SegmentId; foreignRoad = roads[1].SegmentId;
tests/LuxMap.Api.Tests/WarrantyTests.cs:133:                "pole_id", "segment_id", "warranty_expiry", "warranty_status" },
tests/LuxMap.Api.Tests/WarrantyTests.cs:204:                db.Add(new Pole { PoleId = id, CommuneId = home, SegmentId = road, DataSource = DataSource.Field, Geom = new Point(107.315, 11.275) { SRID = 4326 } });
tests/LuxMap.Api.Tests/WarrantyTests.cs:295:        Assert.Equal("pole_id,segment_id,commune_id,data_source,fixture_id,fixture_type,lamp_watt,lamp_count,install_date,warranty_expiry,warranty_status,days_remaining", lines[0]);
tests/LuxMap.Api.Tests/WarrantyTests.cs:308:        Assert.Equal(["data_source,commune_id,segment_id,pole_count,normal,dim,out,unknown,never_surveyed", "field,,,1,0,0,0,1,1"], status);
tests/LuxMap.Api.Tests/WarrantyTests.cs:370:            var pole = new Pole { CommuneId = commune, SegmentId = segment, DataSource = source, Geom = new Point(107.315, 11.275) { SRID = 4326 } };
tests/LuxMap.Api.Tests/FixtureCardinalityTests.cs:208:                    SegmentId = segment.SegmentId,
tests/LuxMap.Api.Tests/CommuneWriteGuardCostTests.cs:302:                SegmentId = segmentId,
tests/LuxMap.Api.Tests/CommuneWriteGuardCostTests.cs:323:                .Select(segment => segment.SegmentId)
tests/LuxMap.Api.Tests/CommuneWriteGuardCostTests.cs:346:                return segment.SegmentId;
tests/LuxMap.Api.Tests/PrefixedIdOverflowTests.cs:305:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/FaultReviewTests.cs:73:            segment = roads[0].SegmentId;
tests/LuxMap.Api.Tests/FaultReviewTests.cs:74:            foreignSegment = roads[1].SegmentId;
tests/LuxMap.Api.Tests/FaultReviewTests.cs:308:            CommuneId = commune, SegmentId = road, Lat = 16.001, Lng = 108.001, FaultStatus = status, FaultType = type,
tests/LuxMap.Api.Tests/FaultReviewTests.cs:331:                WoStatus = WorkOrderStatus.Assigned, SegmentId = segment, AssignedTo = users["a"].UserId, AssignedAt = now,
tests/LuxMap.Api.Tests/AssetImportTests.cs:21:        "external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source";
tests/LuxMap.Api.Tests/AssetImportTests.cs:40:        Assert.Equal("segment_external_ref", error.GetProperty("column").GetString());
tests/LuxMap.Api.Tests/AssetUpdatedByTests.cs:38:            segment_id = segmentId, feeder_id = feederId, commune_id = fixture.CommuneId,
tests/LuxMap.Api.Tests/AssetUpdatedByTests.cs:50:            await db.Set<RoadSegment>().IgnoreQueryFilters().Where(x => x.SegmentId == segmentId).Select(x => x.UpdatedBy).SingleAsync(),
tests/LuxMap.Api.Tests/AssetUpdatedByTests.cs:82:        var poles = "external_ref,segment_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source"
tests/LuxMap.Api.Tests/LuxReadingTests.cs:153:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/WorkOrderCancellationTests.cs:170:        var bundle = await clients["a"].GetFromJsonAsync<JsonElement>($"/api/v1/sync/bundle?segment_id={segment}");
tests/LuxMap.Api.Tests/WorkOrderCancellationTests.cs:191:            segment_ids = new[] { segment }, assigned_to = users["a"].UserId }, 201)).GetProperty("work_order_id").GetString()!;
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:97:        body["segment_id"] = JsonSerializer.SerializeToElement(segment);
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:248:        const string Header = "external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,data_source,feeder_source";
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:278:            await db.Set<RoadSegment>().IgnoreQueryFilters().Where(s => s.SegmentId == segment).Select(s => s.ExternalRef).SingleAsync(),
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:293:                        ["segment_external_ref"] = segmentRef!,
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:405:            ["branch", "feeder_id", "feeder_source", "from_id", "order", "segment_id", "to_pole_id"],
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:450:                INSERT INTO pole (pole_id, segment_id, feeder_id, feeder_source, commune_id, geom, data_source) VALUES
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:536:        body["segment_id"] = segment;
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:545:        body["segment_id"] = segment;
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:644:            return segment.SegmentId;
tests/LuxMap.Api.Tests/TopologyInferenceTests.cs:683:                SegmentId = segmentId,
tests/LuxMap.Api.Tests/FeederCommuneConstraintTests.cs:208:    /// is somebody adding a composite key to <c>segment_id</c> "for symmetry" with the one O-7 just
tests/LuxMap.Api.Tests/FeederCommuneConstraintTests.cs:224:        Assert.Equal(1, await CountAsync(pole => pole.PoleId == poleId && pole.SegmentId == segmentId));
tests/LuxMap.Api.Tests/FeederCommuneConstraintTests.cs:351:        return segment.SegmentId;
tests/LuxMap.Api.Tests/FeederCommuneConstraintTests.cs:356:        SegmentId = segmentId,
tests/LuxMap.Api.Tests/SyncTests.cs:71:            (road1, road2, foreignRoad) = (roads[0].SegmentId, roads[1].SegmentId, roads[2].SegmentId);
tests/LuxMap.Api.Tests/SyncTests.cs:76:                SegmentId = p.Item1, CommuneId = p.Item2, DataSource = DataSource.Simulated, Geom = new Point(p.Item3, 16) { SRID = 4326 },
tests/LuxMap.Api.Tests/SyncTests.cs:130:        Assert.Equal([road1], Strings(bundle.GetProperty("segment_ids")));
tests/LuxMap.Api.Tests/SyncTests.cs:132:            .Select(f => f.GetProperty("properties").GetProperty("segment_id").GetString()!));
tests/LuxMap.Api.Tests/SyncTests.cs:148:        var bundle = await GetAsync("a", $"{Bundle}?segment_id={road2}&segment_id={road1}&segment_id={road2}");
tests/LuxMap.Api.Tests/SyncTests.cs:149:        Assert.Equal([road2, road1], Strings(bundle.GetProperty("segment_ids")));
tests/LuxMap.Api.Tests/SyncTests.cs:153:        await ExpectErrorAsync("a", $"{Bundle}?segment_id={foreignRoad}", HttpStatusCode.NotFound, "ASSET_NOT_FOUND");
tests/LuxMap.Api.Tests/SyncTests.cs:154:        await ExpectErrorAsync("a", $"{Bundle}?segment_id={road1}&since=2026-10-01T00:00:00Z", HttpStatusCode.BadRequest, "VALIDATION_FAILED");
tests/LuxMap.Api.Tests/SyncTests.cs:155:        var tooMany = string.Join('&', Enumerable.Range(0, 21).Select(i => $"segment_id=SEG-{i}"));
tests/LuxMap.Api.Tests/SyncTests.cs:404:            new { task_kind = kind, title = "Sync test order", fault_ids = faults, segment_id = road, assigned_to = assignee.UserId }, HttpStatusCode.Created);
tests/LuxMap.Api.Tests/SyncTests.cs:417:                CommuneId = home, PoleId = poleId, SegmentId = road1, FaultStatus = status, FaultType = FaultType.LampOut,
tests/LuxMap.Api.Tests/CommuneReferenceTests.cs:67:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:53:        Assert.Equal(segmentId, item.GetProperty("segment_id").GetString());
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:270:        string[] expected = ["pole_id", "segment_id", "feeder_id", "feeder_source", "lat", "lng"];
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:346:                return segment.SegmentId;
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:370:                    SegmentId = segmentId,
tests/LuxMap.Api.Tests/LightingCommandTests.cs:73:        var preview = await Rigs.JsonAsync(await manager.GetAsync($"{Preview}?segment_id={rig.Segment}"));
tests/LuxMap.Api.Tests/LightingCommandTests.cs:81:        Assert.Equal(Rigs.Ordered(rig.Segment, other), preview.GetProperty("affected_segment_ids").EnumerateArray().Select(s => s.GetString()!).ToArray());
tests/LuxMap.Api.Tests/LightingCommandTests.cs:83:        var press = await manager.PostAsJsonAsync(Commands, new { segment_id = rig.Segment, mode = "off", client_op_id = Guid.NewGuid() });
tests/LuxMap.Api.Tests/LightingCommandTests.cs:142:    [InlineData("""{ "feeder_id": "FDR-001", "segment_id": "SEG-001", "mode": "on", "client_op_id": "8f0e3b8e-1f2a-4c55-9d3b-2b1f0d6a7c11" }""")]
tests/LuxMap.Api.Tests/StatisticsTests.cs:62:            road = roads[0].SegmentId; otherRoad = roads[1].SegmentId; foreignRoad = roads[2].SegmentId;
tests/LuxMap.Api.Tests/StatisticsTests.cs:109:        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("segment_id").ValueKind);
tests/LuxMap.Api.Tests/StatisticsTests.cs:110:        Assert.Equal(new[] { "commune_id", "data_source", "dim", "never_surveyed", "normal", "out", "pole_count", "segment_id", "unknown" },
tests/LuxMap.Api.Tests/StatisticsTests.cs:136:        Assert.Equal(["data_source", "commune_id", "segment_id"], Strings(body.GetProperty("group_by")));
tests/LuxMap.Api.Tests/StatisticsTests.cs:139:            .Select(r => (r.GetProperty("commune_id").GetString(), r.GetProperty("segment_id").GetString(), r.GetProperty("pole_count").GetInt32()))
tests/LuxMap.Api.Tests/StatisticsTests.cs:312:                var pole = new Pole { CommuneId = commune, SegmentId = segment, DataSource = source, Geom = new Point(108, 16) { SRID = 4326 } };
tests/LuxMap.Api.Tests/StatisticsTests.cs:337:                SegmentId = kind == TaskKind.Survey ? null : (commune == home ? road : foreignRoad),
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:70:        db.Add(segment); await db.SaveChangesAsync(); road = segment.SegmentId;
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:71:        var assets = new[] { new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, NearSensitivePoi = true, Geom = new Point(108,16) { SRID = 4326 } },
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:72:            new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.001,16) { SRID = 4326 } } };
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:77:        db.Add(new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = road });
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:105:        var pass = new SurveyPass { Run = run, SegmentId = road, Direction = direction, FromFraction = direction == "forward" ? 0 : 1,
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:685:        var fault = new Fault { CommuneId = a, SegmentId = road, PoleId = poles[0], FaultType = type, OverrideFaultType = overridden,
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:698:            WoStatus = WorkOrderStatus.Assigned, Title = "Already running", SegmentId = road, CreatedAt = now, UpdatedAt = now };
tests/LuxMap.Api.Tests/SurveyPublicationTests.cs:855:        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {road}");
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:189:                return segment.SegmentId;
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:195:            SegmentId = segmentId,
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:207:            .CountAsync(pole => pole.SegmentId == segmentId));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:67:            db.AddRange(roads); await db.SaveChangesAsync(); segment = roads[0].SegmentId; foreignSegment = roads[1].SegmentId;
tests/LuxMap.Api.Tests/WorkOrderTests.cs:161:            var f = new Fault { CommuneId = commune ?? home, SegmentId = road ?? segment, Lat = 16, Lng = 108,
tests/LuxMap.Api.Tests/WorkOrderTests.cs:170:            segment_id = faults is null ? segment : null, assigned_to = assigned }, 201);
tests/LuxMap.Api.Tests/WorkOrderTests.cs:181:                SegmentId = commune == foreign ? foreignSegment : segment,
tests/LuxMap.Api.Tests/WorkOrderTests.cs:268:        await Send("manager", "POST", "", new { task_kind = "inspection", title = "Own field", segment_id = segment, commune_id = home }, 400, "SERVER_OWNED_FIELD");
tests/LuxMap.Api.Tests/WorkOrderTests.cs:337:            task_kind = "inspection", title = "Rejected assignment", segment_id = segment, assigned_to = target,
tests/LuxMap.Api.Tests/WorkOrderTests.cs:634:                TaskKind = TaskKind.Inspection, SegmentId = segment, CreatedAt = now, UpdatedAt = now });
tests/LuxMap.Api.Tests/WorkOrderTests.cs:650:        Assert.Equal(foreignSegment, detail.GetProperty("segment_id").GetString());
tests/LuxMap.Api.Tests/WorkOrderTests.cs:655:        Assert.Equal(foreignSegment, detail.GetProperty("segment_id").GetString());
tests/LuxMap.Api.Tests/WorkOrderTests.cs:743:            var pole = new Pole { CommuneId = owner, SegmentId = road ?? segment, DataSource = DataSource.Simulated,
tests/LuxMap.Api.Tests/WorkOrderTests.cs:761:            var fault = new Fault { CommuneId = pole.CommuneId, SegmentId = reportedOn ?? pole.SegmentId, PoleId = poleId, Lat = 16, Lng = 108,
tests/LuxMap.Api.Tests/WorkOrderTests.cs:795:        Assert.All(items, x => Assert.Equal(segment, x.GetProperty("segment_id").GetString()));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:823:            db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
tests/LuxMap.Api.Tests/WorkOrderTests.cs:828:            segment_ids = new[] { other, segment }, assigned_to = users["a"].UserId }, 201);
tests/LuxMap.Api.Tests/WorkOrderTests.cs:848:            db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1184:        db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1206:        $"SELECT ST_Distance(ST_Transform(geom, 3405), ST_Transform(ST_SetSRID(ST_MakePoint({lng}, {lat}), 4326), 3405)) AS \"Value\" FROM road_segment WHERE segment_id = {road}").SingleAsync());
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1232:        Assert.Equal(segment, group.GetProperty("segment_id").GetString());
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1245:        Assert.Equal([segment], Strings(orders[0], "segment_ids"));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1257:            segment_ids = new[] { nearRoad, segment }, assigned_to = users["a"].UserId }, 201)).GetProperty("work_order_id").GetString()!;
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1261:        Assert.Equal([segment, nearRoad], Groups(await AgendaOf("a", "?night_of=2026-10-06")).Select(x => x.GetProperty("segment_id").GetString()!));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1265:        Assert.Equal([nearRoad, segment], groups.Select(x => x.GetProperty("segment_id").GetString()!));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1268:        Assert.Equal([nearRoad, segment], Strings(groups[0].GetProperty("work_orders")[0], "segment_ids"));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1321:        Assert.Equal(JsonValueKind.Null, group.GetProperty("segment_id").ValueKind);
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1328:        Assert.Empty(Strings(item, "segment_ids"));
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1343:        Assert.Equal(foreignSegment, engineer.GetProperty("segment_id").GetString());
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:74:        db.Add(route); await db.SaveChangesAsync(); road = route.SegmentId;
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:75:        var assets = new[] { new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.00025,16.00004) { SRID = 4326 } },
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:76:            new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.00075,15.99998) { SRID = 4326 } } };
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:82:        db.Add(new WorkOrderSegment { WorkOrderId = order.WorkOrderId, CommuneId = a, SegmentId = road });
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:120:            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE segment_id = {parallelRoad}");
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:121:            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {parallelRoad}");
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:123:        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE segment_id = {road}");
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:124:        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {road}");
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:263:            db.Add(new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated,
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:369:            var original = await db.Set<RoadSegment>().SingleAsync(x => x.SegmentId == road);
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:373:            db.Add(other); await db.SaveChangesAsync(); parallelRoad = other.SegmentId;
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:374:            db.Add(new Pole { CommuneId = a, SegmentId = parallelRoad, DataSource = DataSource.Simulated,
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:376:            db.Add(new WorkOrderSegment { WorkOrderId = orderId, CommuneId = a, SegmentId = parallelRoad, Position = 1 });
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:384:        if (!ambiguous) Assert.All(projected, x => Assert.Equal(road, x.SegmentId));
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:388:        Assert.DoesNotContain(passes, x => x.SegmentId == parallelRoad);
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:442:            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE road_segment SET geom = ST_Translate(geom,0.0004,0) WHERE segment_id = {road}");
tests/LuxMap.Api.Tests/SurveyProcessingTests.cs:484:            INSERT INTO pole (segment_id, commune_id, geom, data_source, near_sensitive_poi)
tests/LuxMap.Api.Tests/AssetImportMockSetTests.cs:23:/// <c>pole_id</c> / <c>segment_id</c>, which is what a real migration would do: the mock ids ARE the
tests/LuxMap.Api.Tests/AssetImportMockSetTests.cs:65:            .Select(pole => new { pole.ExternalRef, pole.Geom, pole.SegmentId, pole.FeederId })
tests/LuxMap.Api.Tests/AssetImportMockSetTests.cs:82:            $"Loaded {stored.Count} poles across {stored.Select(p => p.SegmentId).Distinct().Count()} segments. "
tests/LuxMap.Api.Tests/AssetImportMockSetTests.cs:158:        properties["external_ref"] = $"{tag}-{properties["segment_id"]!.GetValue<string>()}";
tests/LuxMap.Api.Tests/AssetImportMockSetTests.cs:166:        properties["segment_external_ref"] = $"{tag}-{properties["segment_id"]!.GetValue<string>()}";
tests/LuxMap.Api.Tests/FaultListTests.cs:75:            segment = roads[0].SegmentId;
tests/LuxMap.Api.Tests/FaultListTests.cs:76:            foreignSegment = roads[1].SegmentId;
tests/LuxMap.Api.Tests/FaultListTests.cs:124:            ["fault_id", "pole_id", "fixture_id", "segment_id", "location", "fault_type", "fault_status",
tests/LuxMap.Api.Tests/FaultListTests.cs:349:                    CommuneId = plan.Commune ?? home, SegmentId = plan.Segment ?? segment, PoleId = plan.Pole,
tests/LuxMap.Api.Tests/FaultListTests.cs:372:                CommuneId = commune, SegmentId = road ?? segment, DataSource = DataSource.Simulated,
tests/LuxMap.Api.Tests/FaultListTests.cs:388:                TaskKind = TaskKind.Inspection, WoStatus = WorkOrderStatus.Assigned, SegmentId = segment,
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:49:            "pole_id", "external_ref", "segment_id", "feeder_id", "commune_id", "commune_name", "data_source",
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:82:        foreach (var (url, key, id) in new[] { (Poles, "pole_id", poleId), (Segments, "segment_id", segmentId), (Feeders, "feeder_id", feederId) })
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:113:            (await FindAsync(client, $"{Segments}?page_size=200", "segment_id", segmentId))
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:240:        var row = await FindAsync(client, $"{Segments}?page_size=200", "segment_id", segmentId);
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:264:        var row = await FindAsync(client, $"{Segments}?page_size=200", "segment_id", segmentId);
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:319:            segment_id = segmentId,
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:451:                return segment.SegmentId;
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:489:                    SegmentId = segmentId,
tests/LuxMap.Api.Tests/FixtureRetirementTests.cs:169:                    SegmentId = segment.SegmentId,
tests/LuxMap.Api.Tests/PolePhotoMigrationTests.cs:50:            var pole = new Pole { CommuneId = commune.CommuneId, SegmentId = road.SegmentId, DataSource = DataSource.Simulated,
tests/LuxMap.Api.Tests/MapEndpointTests.cs:85:            "pole_id", "segment_id", "fixture_status", "status_confidence", "power_source",
tests/LuxMap.Api.Tests/MapEndpointTests.cs:377:        var feature = Find(await GetAsync(client, Segments + Box), segmentId, "segment_id");
tests/LuxMap.Api.Tests/MapEndpointTests.cs:386:            "segment_id", "segment_name", "road_class", "length_m", "pole_count",
tests/LuxMap.Api.Tests/MapEndpointTests.cs:421:        Assert.False(Find(body, withLampFault, "segment_id")
tests/LuxMap.Api.Tests/MapEndpointTests.cs:423:        Assert.True(Find(body, withOutage, "segment_id")
tests/LuxMap.Api.Tests/MapEndpointTests.cs:476:            INSERT INTO pole (pole_id, segment_id, commune_id, geom, near_sensitive_poi, data_source)
tests/LuxMap.Api.Tests/MapEndpointTests.cs:489:            $"DELETE FROM pole WHERE segment_id = '{segmentId}' "
tests/LuxMap.Api.Tests/MapEndpointTests.cs:519:                return segment.SegmentId;
tests/LuxMap.Api.Tests/MapEndpointTests.cs:531:                    SegmentId = segmentId,
tests/LuxMap.Api.Tests/MapEndpointTests.cs:578:                    SegmentId = segmentId,
tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:134:                order.SegmentId = null;
tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:135:                db.Add(new WorkOrderSegment { WorkOrderId = id, CommuneId = home, SegmentId = segment, Position = 0 });
tests/LuxMap.Api.Tests/WorkOrderPolePhotoTests.cs:175:            for (var n = 0; n < 205; n++) db.Add(new Pole { CommuneId = home, SegmentId = segment, DataSource = DataSource.Simulated,
tests/LuxMap.Api.Tests/CitizenReviewTests.cs:584:        var pole = new Pole { CommuneId = commune, SegmentId = road.SegmentId, DataSource = DataSource.Field, Geom = new Point(107.505, 11.405) { SRID = 4326 } };
tests/LuxMap.Api.Tests/CommuneWriteScopeTests.cs:131:        SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/LightingTestRig.cs:183:            return segment.SegmentId;
tests/LuxMap.Api.Tests/LightingTestRig.cs:191:                SegmentId = segment,
tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:26:        INSERT INTO pole (segment_id, commune_id, geom, near_sensitive_poi, data_source)
tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:35:    public string SegmentId { get; private set; } = null!;
tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:71:        SegmentId = await ScalarAsync(db, """
tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:75:            RETURNING segment_id;
tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:80:            ("segment", SegmentId), ("commune", CommuneId));
tests/LuxMap.Api.Tests/PoleDetailEndpointTests.cs:63:            "pole_id", "segment_id", "segment_name", "commune_id", "location", "fixture", "current_status",
tests/LuxMap.Api.Tests/PoleDetailEndpointTests.cs:204:                    SegmentId = segment.SegmentId, CommuneId = communeId,
tests/LuxMap.Api.Tests/PoleDetailEndpointTests.cs:239:                    CommuneId = pole.CommuneId, SegmentId = pole.SegmentId, PoleId = poleId, Lat = Lat, Lng = Lng,
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:27:/// <c>segment_id_seq</c> past live rows can cause collisions (docs/agents/traps/testing.md),
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:54:                .Where(segment => Straddling.Contains(segment.SegmentId))
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:61:                .ThenBy(segment => segment.SegmentId.Length)
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:62:                .ThenBy(segment => segment.SegmentId));
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:70:                .ThenBy(segment => segment.SegmentId));
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:77:                "DELETE FROM road_segment WHERE segment_id IN ('SEG-999','SEG-1000','SEG-1001','SEG-1002');");
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:84:                .Where(segment => Straddling.Contains(segment.SegmentId)))
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:85:            .Select(segment => segment.SegmentId)
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:92:            .Where(segment => Straddling.Contains(segment.SegmentId))
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:93:            .Select(segment => segment.SegmentId)
tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:110:                (segment_id, segment_name, road_class, length_m, geom, commune_id, data_source)
tests/LuxMap.Api.Tests/PoleLocationTests.cs:177:                    SegmentId = segment.SegmentId, CommuneId = poleCommune,
tests/LuxMap.Api.Tests/FaultReportTests.cs:58:            db.AddRange(segment, far); await db.SaveChangesAsync(); road = segment.SegmentId;
tests/LuxMap.Api.Tests/FaultReportTests.cs:60:            var mine = new Pole { CommuneId = home, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.305, 16.305) { SRID = 4326 } };
tests/LuxMap.Api.Tests/FaultReportTests.cs:61:            var theirs = new Pole { CommuneId = outside, SegmentId = far.SegmentId, DataSource = DataSource.Field, Geom = new Point(108.405, 16.405) { SRID = 4326 } };
tests/LuxMap.Api.Tests/FaultReportTests.cs:142:        Assert.Equal(road, body.GetProperty("segment_id").GetString());
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:21:    public async Task Pole_id_and_segment_id_are_generated_by_the_database_in_contract_format()
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:24:        Assert.StartsWith("SEG-", fixture.SegmentId, StringComparison.Ordinal);
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:36:        Assert.Matches(@"^SEG-\d{3,}$", fixture.SegmentId);
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:48:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:100:            return segment.SegmentId;
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:105:            .Where(segment => segment.SegmentId == segmentId)
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:350:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:114:        (road, bare, bent) = (segments[0].SegmentId, segments[1].SegmentId, segments[2].SegmentId);
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:118:            new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.0002, 16) { SRID = 4326 } },
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:119:            new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.0009, 16) { SRID = 4326 } },
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:133:        db.AddRange(new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = road, Position = 0 },
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:134:            new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = bare, Position = 1 },
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:135:            new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = bent, Position = 2 });
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:187:        Assert.Equal([road, bare, bent], coverage.Segments.Select(s => s.SegmentId));
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:219:        var gap = (await Review(db).Coverage(sweep.Id, null, default)).Segments.Single(s => s.SegmentId == bent).Gaps.Single();
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:372:            Run = run, SegmentId = segment, PassNo = i, Direction = p.Direction, FromFraction = p.From, ToFraction = p.To,
tests/LuxMap.Api.Tests/SweepCoverageTests.cs:388:                poles = poles.Select((id, i) => new { segment_id = road, pole_id = id, commune_id = i == 0 ? a : b }),

[exit=0]
```

</details>

<details>
<summary>E03 — lệnh và output thật</summary>

```bash
rg -n 'segment_external_ref|\.Segment\.|Include\([^\n]*Segment|JOIN road_segment|join .*RoadSegment' src scripts mocks --glob '!**/Migrations/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '*.cs' --glob '*.py' --glob '*.sql' --glob '*.csv'
```

```text
scripts/photos_to_poles.py:21:                     `data_source` is always `field`. `segment_external_ref`,
scripts/photos_to_poles.py:112:POLES_HEADER = ["external_ref", "segment_external_ref", "feeder_external_ref", "commune_id",
scripts/photos_to_poles.py:647:    segment.add_argument("--segment-ref", default="", help="điền sẵn segment_external_ref (một thư mục = một tuyến)")
scripts/photos_to_poles.py:749:                                f"Không tuyến nào trong {args.review_m:g} m — segment_external_ref để trống."])
scripts/photos_to_poles.py:797:        print("⚠️  poles.csv còn ô bắt buộc để trống (external_ref / segment_external_ref / commune_id) "
mocks/mock-pole-feeders.csv:1:pole_external_ref,segment_external_ref,lng,lat,feeder_external_ref
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:147:            UNION SELECT r.commune_id FROM work_order_segment w JOIN road_segment r USING (segment_id)
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:148:            JOIN road_segment r ON r.segment_id = g.segment_id AND r.commune_id = ANY({communes})
src/LuxMap.Modules.Assets/Import/AssetImportController.cs:19:/// segments fail every row with a message naming the <c>segment_external_ref</c> that matched nothing.
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:37:/// every row with a clear message naming the missing <c>segment_external_ref</c>.
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:98:        ImportKind.Poles => ["external_ref", "segment_external_ref", "commune_id", "geom_wkt", "data_source"],
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:363:        var segments = await ReferenceIndexAsync<RoadSegment>(readers, "segment_external_ref", cancellationToken);
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:383:            var segment = Resolve(reader, segments, "segment_external_ref", required: true);

[exit=0]
```

</details>

<details>
<summary>E04 — lệnh và output thật</summary>

```bash
rg -n -C 2 'SegmentId|HasCommuneReference|HasCommuneScope|ck_pole' src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs; rg -n 'SegmentId|Segment \{' src/LuxMap.Modules.Assets/Entities/Pole.cs; nl -ba src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs | sed -n '434,538p;4546,4561p'; nl -ba src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs | sed -n '65,97p;190,194p'; rg -n 'segment_id|fk_pole_administrative' src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs
```

```text
84-    {
85-        builder.ToTable("road_segment");
86:        builder.HasKey(segment => segment.SegmentId);
87-
88:        builder.Property(segment => segment.SegmentId).HasPrefixedId(PrefixedIds.RoadSegment);
89-        builder.Property(segment => segment.SegmentName).HasColumnType("text").IsRequired();
90-        builder.HasExternalRef("road_segment");
--
98-        builder.HasContractEnum(segment => segment.DataSource);
99-
100:        builder.HasCommuneScope();
101-
102-        // Contract section 5.3: every geometry column carries a GIST index, and the bbox queries must
--
104-        builder.HasIndex(segment => segment.Geom).HasMethod(GeometryColumns.GistMethod);
105-
106:        builder.HasCommuneReference(segment => segment.CommuneId);
107-    }
108-}
--
124-        builder.HasUpdatedBy();
125-
126:        builder.HasCommuneScope();
127-
128-        // Still indexed although the column is nullable: PostgreSQL simply leaves NULL rows out.
--
137-        builder.HasAlternateKey(feeder => new { feeder.FeederId, feeder.CommuneId });
138-
139:        builder.HasCommuneReference(feeder => feeder.CommuneId);
140-
141-        // CAB-2 — the cabinet a circuit leaves from, in the feeder's own commune (the O-7 shape). Nullable,
--
175-        builder.HasContractEnum(cabinet => cabinet.DataSource);
176-
177:        builder.HasCommuneScope();
178-
179-        // GET /map/cabinets and GET /map/iot-nodes both answer a bbox from this column (CAB-3).
--
184-        builder.HasAlternateKey(cabinet => new { cabinet.CabinetId, cabinet.CommuneId });
185-
186:        builder.HasCommuneReference(cabinet => cabinet.CommuneId);
187-    }
188-}
--
196-
197-        builder.Property(pole => pole.PoleId).HasPrefixedId(PrefixedIds.Pole);
198:        builder.Property(pole => pole.SegmentId).HasColumnType("text").IsRequired();
199-        builder.HasExternalRef("pole");
200-        builder.Property(pole => pole.FeederId).HasColumnType("text");
--
211-        builder.HasContractEnum(pole => pole.FeederSource);
212-        builder.ToTable(table => table.HasCheckConstraint(
213:            "ck_pole_feeder_source_matches_feeder", "(feeder_id IS NULL) = (feeder_source IS NULL)"));
214-
215-        // The API trims and checks the same limit and answers 400; this stops every other writer.
216:        builder.ToTable(table => table.HasCheckConstraint("ck_pole_note_length", "char_length(note) <= 1000"));
217-
218:        builder.HasCommuneScope();
219-
220-        // The index the whole task turns on: GET /poles must answer a bbox in under 500 ms at 2000
--
224-        builder.HasOne(pole => pole.Segment)
225-            .WithMany(segment => segment.Poles)
226:            .HasForeignKey(pole => pole.SegmentId)
227-            // Deleting a segment that still carries poles would orphan the assets.
228-            .OnDelete(DeleteBehavior.Restrict);
--
253-            .OnDelete(DeleteBehavior.Restrict);
254-
255:        builder.HasCommuneReference(pole => pole.CommuneId);
256-    }
257-}
--
286-        builder.HasContractEnum(fixture => fixture.DataSource);
287-
288:        builder.HasCommuneScope();
289-
290-        builder.HasOne(fixture => fixture.Pole)
--
293-            .OnDelete(DeleteBehavior.Cascade);
294-
295:        builder.HasCommuneReference(fixture => fixture.CommuneId);
296-
297-        // "The lamp currently in service on this pole" is the lookup BE-14 makes for every pole in a
--
339-        builder.HasContractEnum(status => status.FixtureStatus);
340-
341:        builder.HasCommuneScope();
342-
343-        builder.HasOne(status => status.Pole)
--
346-            .OnDelete(DeleteBehavior.Cascade);
347-
348:        builder.HasCommuneReference(status => status.CommuneId);
349-
350-        // PHOTO-REVIEW: a human classification has no AI score; unknown still cannot carry one.
--
352-        {
353-            table.HasCheckConstraint(
354:                "ck_pole_current_status_confidence_matches_status",
355-                "fixture_status <> 'unknown' OR status_confidence IS NULL");
356-
--
363-            // the constraint is in place before the first row lands.
364-            table.HasCheckConstraint(
365:                "ck_pole_current_status_confidence_range",
366-                "status_confidence IS NULL OR (status_confidence >= 0 AND status_confidence <= 1 "
367-                + "AND status_confidence <> 'NaN'::float8 AND status_confidence <> 'Infinity'::float8 "
34:    public required string SegmentId { get; set; }
117:    public RoadSegment Segment { get; set; } = null!;
   434	            modelBuilder.Entity("LuxMap.Modules.Assets.Entities.Pole", b =>
   435	                {
   436	                    b.Property<string>("PoleId")
   437	                        .ValueGeneratedOnAdd()
   438	                        .HasColumnType("text")
   439	                        .HasColumnName("pole_id")
   440	                        .HasDefaultValueSql("luxmap_format_id('POLE', nextval('pole_id_seq'), 4)")
   441	                        .HasAnnotation("LuxMap:PrefixedIdSequence", "pole_id_seq");
   442	
   443	                    b.Property<string>("CommuneId")
   444	                        .IsRequired()
   445	                        .HasColumnType("text")
   446	                        .HasColumnName("commune_id");
   447	
   448	                    b.Property<DateTime>("CreatedAt")
   449	                        .ValueGeneratedOnAdd()
   450	                        .HasColumnType("timestamp with time zone")
   451	                        .HasColumnName("created_at")
   452	                        .HasDefaultValueSql("now()");
   453	
   454	                    b.Property<string>("DataSource")
   455	                        .IsRequired()
   456	                        .HasColumnType("text")
   457	                        .HasColumnName("data_source");
   458	
   459	                    b.Property<string>("ExternalRef")
   460	                        .HasColumnType("text")
   461	                        .HasColumnName("external_ref");
   462	
   463	                    b.Property<string>("FeederId")
   464	                        .HasColumnType("text")
   465	                        .HasColumnName("feeder_id");
   466	
   467	                    b.Property<string>("FeederSource")
   468	                        .HasColumnType("text")
   469	                        .HasColumnName("feeder_source");
   470	
   471	                    b.Property<Point>("Geom")
   472	                        .IsRequired()
   473	                        .HasColumnType("geometry(Point,4326)")
   474	                        .HasColumnName("geom");
   475	
   476	                    b.Property<bool>("NearSensitivePoi")
   477	                        .ValueGeneratedOnAdd()
   478	                        .HasColumnType("boolean")
   479	                        .HasDefaultValue(false)
   480	                        .HasColumnName("near_sensitive_poi");
   481	
   482	                    b.Property<string>("Note")
   483	                        .HasColumnType("text")
   484	                        .HasColumnName("note");
   485	
   486	                    b.Property<string>("SegmentId")
   487	                        .IsRequired()
   488	                        .HasColumnType("text")
   489	                        .HasColumnName("segment_id");
   490	
   491	                    b.Property<DateTime>("UpdatedAt")
   492	                        .ValueGeneratedOnAdd()
   493	                        .HasColumnType("timestamp with time zone")
   494	                        .HasColumnName("updated_at")
   495	                        .HasDefaultValueSql("now()");
   496	
   497	                    b.Property<string>("UpdatedBy")
   498	                        .HasColumnType("text")
   499	                        .HasColumnName("updated_by");
   500	
   501	                    b.HasKey("PoleId")
   502	                        .HasName("pk_pole");
   503	
   504	                    b.HasIndex("CommuneId")
   505	                        .HasDatabaseName("ix_pole_commune_id");
   506	
   507	                    b.HasIndex("Geom")
   508	                        .HasDatabaseName("ix_pole_geom");
   509	
   510	                    NpgsqlIndexBuilderExtensions.HasMethod(b.HasIndex("Geom"), "gist");
   511	
   512	                    b.HasIndex("SegmentId")
   513	                        .HasDatabaseName("ix_pole_segment_id");
   514	
   515	                    b.HasIndex("UpdatedBy")
   516	                        .HasDatabaseName("ix_pole_updated_by");
   517	
   518	                    b.HasIndex("CommuneId", "ExternalRef")
   519	                        .IsUnique()
   520	                        .HasDatabaseName("ux_pole_commune_external_ref")
   521	                        .HasFilter("external_ref IS NOT NULL");
   522	
   523	                    b.HasIndex("FeederId", "CommuneId")
   524	                        .HasDatabaseName("ix_pole_feeder_id_commune_id");
   525	
   526	                    b.ToTable("pole", null, t =>
   527	                        {
   528	                            t.HasCheckConstraint("ck_pole_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
   529	
   530	                            t.HasCheckConstraint("ck_pole_feeder_source", "\"feeder_source\" IS NULL OR \"feeder_source\" IN ('verified', 'inferred')");
   531	
   532	                            t.HasCheckConstraint("ck_pole_feeder_source_matches_feeder", "(feeder_id IS NULL) = (feeder_source IS NULL)");
   533	
   534	                            t.HasCheckConstraint("ck_pole_note_length", "char_length(note) <= 1000");
   535	                        });
   536	
   537	                    b.HasAnnotation("LuxMap:CommuneScopeApplied", true);
   538	                });
  4546	            modelBuilder.Entity("LuxMap.Modules.Assets.Entities.Pole", b =>
  4547	                {
  4548	                    b.HasOne("LuxMap.Persistence.AdministrativeUnit", null)
  4549	                        .WithMany()
  4550	                        .HasForeignKey("CommuneId")
  4551	                        .OnDelete(DeleteBehavior.Restrict)
  4552	                        .IsRequired()
  4553	                        .HasConstraintName("fk_pole_administrative_unit_commune_id");
  4554	
  4555	                    b.HasOne("LuxMap.Modules.Assets.Entities.RoadSegment", "Segment")
  4556	                        .WithMany("Poles")
  4557	                        .HasForeignKey("SegmentId")
  4558	                        .OnDelete(DeleteBehavior.Restrict)
  4559	                        .IsRequired()
  4560	                        .HasConstraintName("fk_pole_road_segment_segment_id");
  4561	
    65	                name: "pole",
    66	                columns: table => new
    67	                {
    68	                    pole_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')"),
    69	                    segment_id = table.Column<string>(type: "text", nullable: false),
    70	                    feeder_id = table.Column<string>(type: "text", nullable: true),
    71	                    commune_id = table.Column<string>(type: "text", nullable: false),
    72	                    geom = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
    73	                    near_sensitive_poi = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
    74	                    data_source = table.Column<string>(type: "text", nullable: false),
    75	                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
    76	                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
    77	                },
    78	                constraints: table =>
    79	                {
    80	                    table.PrimaryKey("pk_pole", x => x.pole_id);
    81	                    table.CheckConstraint("ck_pole_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
    82	                    table.ForeignKey(
    83	                        name: "fk_pole_feeder_feeder_id",
    84	                        column: x => x.feeder_id,
    85	                        principalTable: "feeder",
    86	                        principalColumn: "feeder_id",
    87	                        onDelete: ReferentialAction.Restrict);
    88	                    table.ForeignKey(
    89	                        name: "fk_pole_road_segment_segment_id",
    90	                        column: x => x.segment_id,
    91	                        principalTable: "road_segment",
    92	                        principalColumn: "segment_id",
    93	                        onDelete: ReferentialAction.Restrict);
    94	                });
    95	
    96	            migrationBuilder.CreateTable(
    97	                name: "fixture",
   190	            migrationBuilder.CreateIndex(
   191	                name: "ix_pole_segment_id",
   192	                table: "pole",
   193	                column: "segment_id");
   194	
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:54:                name: "fk_pole_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:90:                name: "fk_pole_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:25:                    segment_id = table.Column<string>(type: "text", nullable: false),
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:41:                        name: "fk_fault_cluster_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:42:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:44:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:56:                    segment_id = table.Column<string>(type: "text", nullable: true),
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:134:                        name: "fk_fault_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:135:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:137:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:188:                name: "ix_fault_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:190:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:205:                name: "ix_fault_cluster_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:207:                column: "segment_id");

[exit=0]
```

</details>

<details>
<summary>E05 — lệnh và output thật</summary>

```bash
rg -n -C 2 'SegmentId|ListPolesAsync|ListPolesWithoutFeederAsync|FeederId == null' src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs; rg -n -B 3 'public .*SegmentId' src/LuxMap.Modules.Assets/Crud/AssetContracts.cs; nl -ba src/LuxMap.Modules.Assets/Crud/AssetsController.cs | sed -n '149,166p;190,199p;260,273p;531,557p'
```

```text
40-    public Task<PagedResult<SegmentListItem>> ListSegmentsAsync(
41-        IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
42:        => ListAsync(communes, page, (RoadSegment segment) => segment.SegmentId, SegmentRow, ct);
43-
44-    public async Task<PagedResult<FeederListItem>> ListFeedersAsync(
--
57-    }
58-
59:    public async Task<PagedResult<PoleListItem>> ListPolesAsync(
60-        IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
61-    {
--
109-                pole.CreatedAt,
110-                SegmentName = dbContext.Set<RoadSegment>()
111:                    .Where(segment => segment.SegmentId == pole.SegmentId)
112-                    .Select(segment => segment.SegmentName)
113-                    .FirstOrDefault(),
--
133-    {
134-        var segments = dbContext.Set<RoadSegment>().AsNoTracking()
135:            .Where(segment => segment.SegmentId == segmentId);
136-
137-        var item = await segments.Select(SegmentRow).FirstOrDefaultAsync(ct)
--
257-        Touch(segment);
258-        await dbContext.SaveChangesAsync(ct);
259:        return segment.SegmentId;
260-    }
261-
--
292-        // Read through the query filter: a segment outside the caller's scope is simply not there,
293-        // which is the 404 Contract section 7 asks for rather than a 403 that would confirm it exists.
294:        await RequireAsync<RoadSegment>(segment => segment.SegmentId == request.SegmentId, "road segment", ct);
295-
296-        await RequireFeederInCommuneAsync(request.FeederId, communeId, ct);
--
302-        {
303-            ExternalRef = request.ExternalRef,
304:            SegmentId = request.SegmentId!,
305-            FeederId = request.FeederId,
306-            FeederSource = feederSource,
--
389-    public async Task UpdateSegmentAsync(string segmentId, UpdateSegmentRequest request, CancellationToken ct)
390-    {
391:        var segment = await RequireAsync<RoadSegment>(candidate => candidate.SegmentId == segmentId, "road segment", ct);
392-
393-        await RejectDuplicateRefAsync<RoadSegment>(segment.CommuneId, request.ExternalRef, ct, segment.ExternalRef);
--
475-
476-        // Through the query filter: a segment the caller cannot see is not there, so this is a 404.
477:        await RequireAsync<RoadSegment>(segment => segment.SegmentId == request.SegmentId, "road segment", ct);
478-
479-        await RequireFeederInCommuneAsync(request.FeederId, pole.CommuneId, ct);
--
484-
485-        pole.ExternalRef = request.ExternalRef;
486:        pole.SegmentId = request.SegmentId!;
487-        TopologyLink.ApplyPoleFeeder(dbContext, pole, request.FeederId, feederSource);
488-        pole.Geom = Read<Point>(request.GeomWkt);
--
545-    public Task DeleteSegmentAsync(string segmentId, CancellationToken ct)
546-        => DeleteAsync<RoadSegment>(
547:            candidate => candidate.SegmentId == segmentId,
548-            "road segment",
549-            "That road segment still has poles, faults or fault clusters pointing at it, so it cannot be deleted.",
--
726-        // constraint 1). Only a distance leaves the query, never the segment.
727-        var offSegment = await dbContext.Set<RoadSegment>().IgnoreQueryFilters()
728:            .Where(segment => segment.SegmentId == pole.SegmentId)
729-            .Select(segment => SpatialFunctions.DistanceMeters(segment.Geom, target)).SingleAsync(ct);
730-
--
740-                    new Dictionary<string, object?>
741-                    {
742:                        ["pole_id"] = pole.PoleId, ["segment_id"] = pole.SegmentId,
743-                        ["distance_m"] = Math.Round(offSegment, 1), ["corridor_m"] = corridor.RouteCorridorM,
744-                    });
--
747-            audit.Record(new AuditChange(pole.UpdatedAt, AuditActorKind.User, actor.UserId, actor.Role, pole.CommuneId,
748-                AuditEntityType.Pole, pole.PoleId, AuditAction.Relocated, before,
749:                new { lat, lng, moved_m = Math.Round(moved, 2), pole.SegmentId, segment_distance_m = Math.Round(offSegment, 2) }));
750-        }
751-        else
--
826-        string segmentId, PageRequest page, CancellationToken ct)
827-    {
828:        await RequireAsync<RoadSegment>(segment => segment.SegmentId == segmentId, "road segment", ct);
829-
830:        return await TopologyPageAsync(pole => pole.SegmentId == segmentId, page, ct);
831-    }
832-
--
838-    /// still to do, and the field was removed rather than left returning one constant.
839-    /// </remarks>
840:    public Task<PagedResult<TopologyPole>> ListPolesWithoutFeederAsync(
841-        IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
842-    {
843-        var scoped = communes is null
844:            ? (System.Linq.Expressions.Expression<Func<Pole, bool>>)(pole => pole.FeederId == null)
845:            : pole => pole.FeederId == null && communes.Contains(pole.CommuneId);
846-
847-        return TopologyPageAsync(scoped, page, ct);
--
877-            {
878-                PoleId = pole.PoleId,
879:                SegmentId = pole.SegmentId,
880-                FeederId = pole.FeederId,
881-                FeederSource = pole.FeederSource,
--
964-            PoleId = pole.PoleId,
965-            ExternalRef = pole.ExternalRef,
966:            SegmentId = pole.SegmentId,
967-            FeederId = pole.FeederId,
968-            FeederSource = pole.FeederSource,
--
1006-        segment => new SegmentListItem
1007-        {
1008:            SegmentId = segment.SegmentId,
1009-            ExternalRef = segment.ExternalRef,
1010-            SegmentName = segment.SegmentName,
--
1018-            // On an inter_commune road that is not the whole road, and it must not be: reporting a
1019-            // count the caller cannot then list would be a way to probe another commune's data.
1020:            PoleCount = dbContext.Set<Pole>().Count(pole => pole.SegmentId == segment.SegmentId),
1021-            UpdatedAt = segment.UpdatedAt,
1022-            UpdatedBy = segment.UpdatedBy,
106-
107-    [Required]
108-    [MaxLength(32)]
109:    public string? SegmentId { get; init; }
--
311-
312-    [Required]
313-    [MaxLength(32)]
314:    public string? SegmentId { get; init; }
--
385-{
386-    public required string PoleId { get; init; }
387-
388:    public required string SegmentId { get; init; }
--
487-
488-    public string? ExternalRef { get; init; }
489-
490:    public required string SegmentId { get; init; }
--
633-/// <summary>One road segment in the inventory list.</summary>
634-public sealed record SegmentListItem
635-{
636:    public required string SegmentId { get; init; }
   149	    [HttpGet("poles")]
   150	    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
   151	    [ProducesResponseType<PagedResult<PoleListItem>>(StatusCodes.Status200OK)]
   152	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
   153	    public async Task<ActionResult<PagedResult<PoleListItem>>> ListPolesAsync(
   154	        [FromQuery(Name = "commune_id")] string[]? communeId, PageQuery page, CancellationToken ct)
   155	        => Ok(await service.ListPolesAsync(Narrow(communeId), page.ToPageRequest(), ct));
   156	
   157	    /// <summary>One pole, with its geometry as WKT and its segment's name resolved.</summary>
   158	    /// <remarks>
   159	    /// Outside the caller's commune this is <b>404</b>, not 403 — Contract section 7 wants absence
   160	    /// rather than a refusal that confirms the id exists somewhere else.
   161	    /// </remarks>
   162	    [HttpGet("poles/{poleId}")]
   163	    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
   164	    [ProducesResponseType<PoleDetail>(StatusCodes.Status200OK)]
   165	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
   166	    public async Task<ActionResult<PoleDetail>> PoleAsync(string poleId, CancellationToken ct)
   190	    [HttpPost("poles")]
   191	    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
   192	    [ProducesResponseType(StatusCodes.Status201Created)]
   193	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
   194	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
   195	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
   196	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
   197	    public async Task<IActionResult> CreatePoleAsync(
   198	        [FromBody] CreatePoleRequest request, CancellationToken ct)
   199	        => CreatedAsset("poles", await service.CreatePoleAsync(request, ct));
   260	    [HttpPut("poles/{poleId}")]
   261	    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
   262	    [ProducesResponseType(StatusCodes.Status204NoContent)]
   263	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
   264	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
   265	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
   266	    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
   267	    public async Task<IActionResult> UpdatePoleAsync(
   268	        string poleId, [FromBody] UpdatePoleRequest request, CancellationToken ct)
   269	    {
   270	        await service.UpdatePoleAsync(poleId, request, ct);
   271	        return NoContent();
   272	    }
   273	
   531	/// </para>
   532	/// </remarks>
   533	public sealed record SetPoleFeederRequest
   534	{
   535	    public JsonElement FeederId { get; init; }
   536	
   537	    /// <summary>
   538	    /// Provenance of the relation (TOPO-INFER TI-2): <c>verified</c> | <c>inferred</c>. Optional — absent means <c>inferred</c>
   539	    /// for a new or moved feeder, KEEP when the feeder stays; <c>null</c> with a feeder, or a label with <c>feeder_id: null</c>, is 400.
   540	    /// </summary>
   541	    public JsonElement FeederSource { get; init; }
   542	
   543	    /// <summary>The feeder id, or <c>null</c> to clear. Throws when the key was not sent at all.</summary>
   544	    public string? ReadFeederId() => FeederId.ValueKind switch
   545	    {
   546	        JsonValueKind.String => FeederId.GetString(),
   547	        JsonValueKind.Null => null,
   548	        JsonValueKind.Undefined => throw new LuxMapException(
   549	            ErrorCodes.ValidationFailed,
   550	            HttpStatusCode.BadRequest,
   551	            "feeder_id is required. Send null to record that the pole is on no circuit.",
   552	            new Dictionary<string, object?> { ["feeder_id"] = "missing" }),
   553	        _ => throw new LuxMapException(
   554	            ErrorCodes.ValidationFailed,
   555	            HttpStatusCode.BadRequest,
   556	            "feeder_id must be a string or null.",
   557	            new Dictionary<string, object?> { ["feeder_id"] = FeederId.ValueKind.ToString().ToLowerInvariant() }),

[exit=0]
```

</details>

<details>
<summary>E06 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Assets/Import/AssetImportService.cs | sed -n '93,100p;359,385p;416,448p;686,708p'; nl -ba src/LuxMap.Modules.Assets/Import/ImportRow.cs | sed -n '102,128p'; rg -n 'segment_external_ref|poles.csv|segments →' docs/templates/README.md; cat docs/templates/poles*.csv
```

```text
    93	    private static IReadOnlyList<string> RequiredColumns(ImportKind kind) => kind switch
    94	    {
    95	        ImportKind.Segments => ["external_ref", "segment_name", "road_class", "length_m", "geom_wkt", "commune_id", "data_source"],
    96	        ImportKind.Cabinets => ["external_ref", "cabinet_name", "commune_id", "geom_wkt", "data_source"],
    97	        ImportKind.Feeders => ["external_ref", "feeder_name", "commune_id"],
    98	        ImportKind.Poles => ["external_ref", "segment_external_ref", "commune_id", "geom_wkt", "data_source"],
    99	        ImportKind.Fixtures => ["pole_external_ref", "fixture_type", "power_source", "lamp_watt", "install_date", "data_source"],
   100	        _ => [],
   359	    private async Task<WritePlan> PlanPolesAsync(
   360	        List<ImportRowReader> readers, List<ImportRowError> warnings, CancellationToken cancellationToken)
   361	    {
   362	        var existing = await ExistingByRefAsync<Pole>(readers, pole => pole.ExternalRef, cancellationToken);
   363	        var segments = await ReferenceIndexAsync<RoadSegment>(readers, "segment_external_ref", cancellationToken);
   364	        var feeders = await ReferenceIndexAsync<Feeder>(readers, "feeder_external_ref", cancellationToken);
   365	
   366	        int inserted = 0, updated = 0, unchanged = 0;
   367	
   368	        foreach (var reader in readers)
   369	        {
   370	            var externalRef = reader.Required("external_ref");
   371	            var communeId = await CommuneAsync(reader, cancellationToken);
   372	            var dataSource = reader.RequiredEnum<DataSource>("data_source");
   373	            var nearPoi = reader.Flag("near_sensitive_poi", fallback: false);
   374	            var geometry = reader.Geometry<Point>(CsvImportRow.GeometryColumn);
   375	
   376	            // Optional, and never required in the header. Already trimmed, blank = no note.
   377	            var note = reader.Optional("note");
   378	            if (note is { Length: > PoleNoteInput.MaxLength })
   379	            {
   380	                reader.Fail("note", $"A note is at most {PoleNoteInput.MaxLength} characters; this one has {note.Length}.");
   381	            }
   382	
   383	            var segment = Resolve(reader, segments, "segment_external_ref", required: true);
   384	
   385	            // Nullable on purpose: a solar_all_in_one pole is connected to no circuit at all.
   416	
   417	            if (!reader.IsValid)
   418	            {
   419	                continue;
   420	            }
   421	
   422	            if (pole is not null)
   423	            {
   424	                pole.SegmentId = segment!.Id;
   425	                TopologyLink.ApplyPoleFeeder(dbContext, pole, feeder?.Id, feederSource);
   426	                pole.Geom = geometry!;
   427	                pole.NearSensitivePoi = nearPoi;
   428	                pole.DataSource = dataSource;
   429	                ImportNote(reader, pole, note, warnings);
   430	                Count(pole, ref updated, ref unchanged);
   431	                continue;
   432	            }
   433	
   434	            var created = new Pole
   435	            {
   436	                ExternalRef = externalRef,
   437	                SegmentId = segment!.Id,
   438	                FeederId = feeder?.Id,
   439	                FeederSource = feederSource,
   440	                CommuneId = communeId!,
   441	                Geom = geometry!,
   442	                NearSensitivePoi = nearPoi,
   443	                DataSource = dataSource,
   444	                Note = note,
   445	            };
   446	
   447	            dbContext.Set<Pole>().Add(created);
   448	            Touch(created);
   686	    private static ReferencedAsset? Resolve(
   687	        ImportRowReader reader, Dictionary<string, List<ReferencedAsset>> index, string column, bool required)
   688	    {
   689	        var value = required ? reader.Required(column) : reader.Optional(column);
   690	        if (value is null)
   691	        {
   692	            return null;
   693	        }
   694	
   695	        if (!index.TryGetValue(value, out var matches) || matches.Count == 0)
   696	        {
   697	            reader.Fail(column, $"'{value}' matches nothing. Import that file first — see docs/templates/README.md.");
   698	            return null;
   699	        }
   700	
   701	        if (matches.Count > 1)
   702	        {
   703	            reader.Fail(column, $"'{value}' exists in more than one commune you can see; it does not identify one row.");
   704	            return null;
   705	        }
   706	
   707	        return matches[0];
   708	    }
   102	internal sealed class ImportRowReader(IImportRow row, List<ImportRowError> errors)
   103	{
   104	    public IImportRow Row => row;
   105	
   106	    /// <summary>True while this row has produced no error — i.e. it is still eligible to be written.</summary>
   107	    public bool IsValid { get; private set; } = true;
   108	
   109	    public void Fail(string? column, string message)
   110	    {
   111	        IsValid = false;
   112	        errors.Add(new ImportRowError(row.Row, column, message));
   113	    }
   114	
   115	    public string? Optional(string column) => row[column];
   116	
   117	    public string? Required(string column)
   118	    {
   119	        var value = row[column];
   120	        if (value is null)
   121	        {
   122	            Fail(column, "Required, but the cell is empty.");
   123	        }
   124	
   125	        return value;
   126	    }
   127	
   128	    public int RequiredInt(string column)
26:| `segments.csv` · `cabinets.csv` · `feeders.csv` · `poles.csv` · `fixtures.csv` | **Chỉ dòng header** | Phát cho đơn vị điền dữ liệu thật |
54:## Bản v2 — hai cột đã bị BỎ khỏi `poles.csv`
56:`segment_name` và `feeder_name` **không còn** trong `poles.csv`. Chúng từng có mặt "chỉ để đọc",
60:Cũng ở bản v2: `poles.csv` tham chiếu bằng `segment_external_ref` / `feeder_external_ref` thay cho
63:> ⚠️ **Nếu ai đó đã điền file theo bản cũ**: bỏ hai cột thừa đi là đủ với `poles.csv` — trình nhập
65:> `segment_external_ref`/`feeder_external_ref` và điền mã của đơn vị thay cho `SEG-001`/`FDR-001`.
73:1. segments.csv  →  2. cabinets.csv  →  3. feeders.csv  →  4. poles.csv  →  5. fixtures.csv
76:**Mỗi lần gọi nạp đúng một loại file**, nên khi nạp `poles.csv` thì các tuyến đã nằm sẵn trong DB
78:báo `segment_external_ref` không khớp gì cả, và không dòng nào được ghi.
122:`poles.csv` từng có `segment_name` / `feeder_name` "chỉ để đọc". Đã **bỏ hẳn**: một cái tên gõ sai
131:`road_segment.segment_name`; điều vừa bỏ là bản sao chỉ-để-đọc của nó trong `poles.csv`.
147:### `note` trong `poles.csv` — tuỳ chọn; ô có chữ ghi đè, ô trống giữ nguyên
171:Ba file `segments.csv`, `feeders.csv`, `poles.csv` đều có cột `external_ref` **bắt buộc**;
172:`fixtures.csv` trỏ về cột bằng `pole_external_ref`, còn `poles.csv` trỏ về tuyến và mạch điện bằng
173:`segment_external_ref` và `feeder_external_ref`.
182:sinh lúc INSERT, **người soạn file không thể biết trước**. Nếu `poles.csv` đòi `segment_id` thì bộ
230:| `external_ref` | **Có** | text | Mã tuyến của đơn vị quản lý. `poles.csv` trỏ về bằng `segment_external_ref`. Trùng trong cùng xã → **UPDATE** |
259:| `external_ref` | **Có** | text | Mã mạch điện của đơn vị quản lý. `poles.csv` trỏ về bằng `feeder_external_ref` |
269:## `poles.csv`
274:| `segment_external_ref` | **Có** | text | Khớp `external_ref` trong `segments.csv`. Không khớp → lỗi theo dòng |
286:| `pole_external_ref` | **Có** | text | Khớp `external_ref` trong `poles.csv`. **Cột đã có bóng → lỗi theo dòng** (insert-only) |
315:| `segment_id` | ✅ `poles.csv` → `segment_external_ref` (dùng `segment_id` của mock làm mã tuyến) |
316:| `commune_id` | ✅ `poles.csv` → `commune_id` |
317:| `near_sensitive_poi` | ✅ `poles.csv` → `near_sensitive_poi` |
331:**Kết luận: 15/15 trường đều có chỗ.** 9 trường nạp được từ template (4 ở `poles.csv`,
384:trong `poles.csv` và trong lược đồ. Nhưng nó phải được lấp trước khi CV-15 có thể chạy, bằng
external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source,note,feeder_source
external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source,note,feeder_source
CTA-001,TUYEN-A,FDR-A,COM-070,POINT(106.4900 10.9700),true,public_imagery,"Cạnh cổng trường TH Long Phước, tan học 17h",

[exit=0]
```

</details>

<details>
<summary>E07 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs | sed -n '20,27p;78,94p;104,151p'; nl -ba src/LuxMap.Modules.WorkOrders/WorkOrderService.cs | sed -n '138,175p;200,209p;283,303p'; nl -ba src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs | sed -n '166,184p;211,233p'; nl -ba src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs | sed -n '113,129p'
```

```text
    20	public sealed record WorkOrderPole
    21	{
    22	    public required string PoleId { get; init; }
    23	    public required string SegmentId { get; init; }
    24	
    25	    /// <summary>1-based place along its segment, in the direction the segment's line is drawn.</summary>
    26	    public int Position { get; init; }
    27	
    78	
    79	    internal IQueryable<Pole> Targets(string id) => db.Set<Pole>().Where(p =>
    80	        db.Set<WorkOrder>().Any(w => w.WorkOrderId == id && w.SegmentId == p.SegmentId)
    81	        || db.Set<WorkOrderSegment>().Any(s => s.WorkOrderId == id && s.SegmentId == p.SegmentId)
    82	        || (from link in db.Set<WorkOrderFault>() join fault in db.Set<Fault>() on link.FaultId equals fault.FaultId
    83	            where link.WorkOrderId == id && fault.PoleId == p.PoleId select link).Any());
    84	
    85	    public async Task<PagedResult<WorkOrderPole>> PolesAsync(string id, PageRequest page, CancellationToken ct)
    86	    {
    87	        var order = await db.Set<WorkOrder>().AsNoTracking().Where(x => x.WorkOrderId == id)
    88	            .Select(x => new { x.SegmentId }).FirstOrDefaultAsync(ct)
    89	            ?? throw new LuxMapException("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound, "WORK ORDER NOT FOUND");
    90	
    91	        var segmentIds = await db.Set<WorkOrderSegment>().AsNoTracking().Where(x => x.WorkOrderId == id)
    92	            .OrderBy(x => x.Position).Select(x => x.SegmentId).ToListAsync(ct);
    93	        if (order.SegmentId is { } own && !segmentIds.Contains(own)) segmentIds.Add(own);
    94	
   104	
   105	        var rows = await Targets(id).AsNoTracking()
   106	            .Select(p => new
   107	            {
   108	                p.PoleId, p.SegmentId, p.Geom, p.NearSensitivePoi,
   109	                p.Note,
   110	                Status = db.Set<PoleCurrentStatus>().Where(s => s.PoleId == p.PoleId)
   111	                    .Select(s => new { s.FixtureStatus, s.StatusConfidence, s.LastSeenAt }).FirstOrDefault(),
   112	                Lamp = db.Set<Fixture>().Where(f => f.PoleId == p.PoleId && f.RemovedDate == null)
   113	                    .Select(f => new { f.FixtureType, f.LampWatt, f.LampCount }).FirstOrDefault(),
   114	                // FaultStatusSets.Open is the one definition of an open fault (BE-18); never a copied list.
   115	                OpenFaults = db.Set<Fault>().Count(f => f.PoleId == p.PoleId && OpenStatuses.Contains(f.FaultStatus)),
   116	            })
   117	            .ToListAsync(ct);
   118	
   119	        // Geometry for every road a returned pole stands on — including roads reached only through the
   120	        // order's faults, whose poles would otherwise all project to 0 and sort by ID.
   121	        var roadIds = rows.Select(r => r.SegmentId).Distinct().ToArray();
   122	        // 🔴 Unfiltered ON PURPOSE, and only for ordering. An inter_commune road belongs to another commune,
   123	        // so the commune filter hides it from this caller even though THEIR poles stand on it; with the
   124	        // geometry missing every pole would project to 0 and sort by id. The poles above already passed the
   125	        // filter, and nothing of the road except the order along it leaves this method.
   126	        var lines = (await db.Set<RoadSegment>().AsNoTracking().IgnoreQueryFilters().Where(s => roadIds.Contains(s.SegmentId))
   127	            .Select(s => new { s.SegmentId, s.Geom }).ToListAsync(ct))
   128	            .ToDictionary(s => s.SegmentId, s => new LengthIndexedLine(s.Geom));
   129	
   130	        var ordered = rows
   131	            .Select(r => new
   132	            {
   133	                Row = r,
   134	                SegmentOrder = segmentIds.IndexOf(r.SegmentId) is var at and >= 0 ? at : int.MaxValue,
   135	                Along = lines.TryGetValue(r.SegmentId, out var line) ? line.Project(r.Geom.Coordinate) : 0d,
   136	            })
   137	            .OrderBy(x => x.SegmentOrder).ThenBy(x => x.Row.SegmentId.Length).ThenBy(x => x.Row.SegmentId, StringComparer.Ordinal)
   138	            .ThenBy(x => x.Along).ThenBy(x => x.Row.PoleId.Length).ThenBy(x => x.Row.PoleId, StringComparer.Ordinal)
   139	            .ToList();
   140	
   141	        var positions = new Dictionary<string, int>();
   142	        var all = ordered.Select(x =>
   143	        {
   144	            var position = positions[x.Row.SegmentId] = positions.GetValueOrDefault(x.Row.SegmentId) + 1;
   145	            var r = x.Row;
   146	            return new WorkOrderPole
   147	            {
   148	                PoleId = r.PoleId,
   149	                SegmentId = r.SegmentId,
   150	                Position = position,
   151	                Location = new WorkOrderLocation(r.Geom.Y, r.Geom.X),
   138	    {
   139	        if (new[] { request.WorkOrderId, request.WoStatus, request.ClusterId, request.PriorityScore }.Any(OptionalJson.Present))
   140	            throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
   141	        var title = ValidTitle(request.Title);
   142	        Schedule(request.ScheduledDate, request.DueDate);
   143	        var kind = request.TaskKind ?? throw OptionalJson.Invalid("task_kind");
   144	        if (kind == TaskKind.Survey) return await CreateSurvey(request, title, ct);
   145	        if (OptionalJson.Present(request.CommuneId)) throw Error("SERVER_OWNED_FIELD", HttpStatusCode.BadRequest);
   146	        if (request.SegmentIds is not null) throw OptionalJson.Invalid("segment_ids");
   147	        var ids = request.FaultIds ?? [];
   148	        if (ids.Length > 200 || ids.Distinct().Count() != ids.Length || ids.Any(string.IsNullOrWhiteSpace)
   149	            || (ids.Length > 0 && request.SegmentId is not null)
   150	            || (ids.Length == 0 && (kind != TaskKind.Inspection || string.IsNullOrWhiteSpace(request.SegmentId))))
   151	            throw OptionalJson.Invalid("fault_ids / segment_id");
   152	        var faults = await db.Set<Fault>().Where(x => ids.Contains(x.FaultId))
   153	            .OrderBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).ToListAsync(ct);
   154	        var missing = ids.Except(faults.Select(x => x.FaultId)).ToArray();
   155	        if (missing.Length != 0) throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound, ("fault_ids", missing));
   156	        var road = faults.Count > 0 ? (RoadSegment?)null
   157	            : await db.Set<RoadSegment>().FirstOrDefaultAsync(x => x.SegmentId == request.SegmentId, ct)
   158	                ?? throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
   159	        return await Insert(new(kind, title, faults, road, request.AssignedTo, request.DueDate, request.ScheduledDate,
   160	            request.Note, Materials(request.MaterialsNote), null), ct);
   161	    }
   162	
   163	    // SELF-SIGNED BE-15 P2a: uses the existing commune_id as the explicit anchor.
   164	    private async Task<WorkOrderDetail> CreateSurvey(CreateWorkOrderRequest request, string title, CancellationToken ct)
   165	    {
   166	        var ids = request.SegmentIds ?? [];
   167	        var commune = OptionalJson.Text(request.CommuneId, "commune_id", false);
   168	        if (string.IsNullOrWhiteSpace(commune) || ids.Length is 0 or > 200 || ids.Any(string.IsNullOrWhiteSpace)
   169	            || ids.Distinct().Count() != ids.Length || request.SegmentId is not null || request.FaultIds is { Length: > 0 })
   170	            throw OptionalJson.Invalid("segment_ids / commune_id / fault_ids");
   171	        if (!db.CurrentCommuneScope.Allows(commune)
   172	            || !await db.Set<AdministrativeUnit>().AnyAsync(x => x.CommuneId == commune, ct)
   173	            || await db.Set<RoadSegment>().CountAsync(x => ids.Contains(x.SegmentId), ct) != ids.Length)
   174	            throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
   175	        if (request.AssignedTo is not null)
   200	
   201	    private async Task RequireSurveyAssignee(string assigned, string[] ids, CancellationToken ct)
   202	    {
   203	        var roads = await db.Set<RoadSegment>().Where(x => ids.Contains(x.SegmentId)).Select(x => x.CommuneId).ToArrayAsync(ct);
   204	        if (roads.Length != ids.Length) throw Error("ASSET_NOT_FOUND", HttpStatusCode.NotFound);
   205	        // Q7: the eligible pole set is restricted to the assigning manager's scope.
   206	        var communes = roads.Concat(await db.Set<Pole>().Where(x => ids.Contains(x.SegmentId)).Select(x => x.CommuneId).Distinct().ToArrayAsync(ct)).Distinct();
   207	        foreach (var commune in communes) await RequireAssignee(assigned, commune, ct);
   208	    }
   209	
   283	        string? segment;
   284	        if (faults.Count > 0)
   285	        {
   286	            if (faults.Select(x => x.CommuneId).Distinct().Count() != 1)
   287	                throw Error("CROSS_COMMUNE_REFERENCE", HttpStatusCode.Conflict);
   288	            commune = faults[0].CommuneId;
   289	            segment = faults.Where(x => x.SegmentId is not null).OrderByDescending(x => x.PriorityScore)
   290	                .ThenBy(x => x.CreatedAt).ThenBy(x => x.FaultId.Length).ThenBy(x => x.FaultId).FirstOrDefault()?.SegmentId;
   291	            var invalid = faults.Where(x => !WorkOrderRules.Eligible(order.Kind, x.FaultStatus)).ToArray();
   292	            if (invalid.Length > 0) throw Error("FAULT_STATUS_NOT_ELIGIBLE", HttpStatusCode.Conflict,
   293	                ("task_kind", order.Kind), ("accepted_statuses", FaultStatusSets.Open.Where(x => WorkOrderRules.Eligible(order.Kind, x)).ToArray()),
   294	                ("faults", invalid.Select(x => new { x.FaultId, x.FaultStatus }).ToArray()));
   295	            var conflict = await db.Set<WorkOrderFault>().FirstOrDefaultAsync(x => ids.Contains(x.FaultId) && x.ReleasedAt == null, ct);
   296	            if (conflict is not null) throw LinkConflict(conflict);
   297	        }
   298	        else
   299	        {
   300	            commune = order.Road!.CommuneId;
   301	            segment = order.Road.SegmentId;
   302	        }
   303	        if (order.AssignedTo is not null) await RequireAssignee(order.AssignedTo, commune, ct);
   166	        var faults = (await (from link in db.Set<WorkOrderFault>().AsNoTracking()
   167	                             join fault in db.Set<Fault>().AsNoTracking() on link.FaultId equals fault.FaultId
   168	                             join pole in db.Set<Pole>().AsNoTracking() on fault.PoleId equals pole.PoleId into poles
   169	                             from pole in poles.DefaultIfEmpty()
   170	                             where ids.Contains(link.WorkOrderId)
   171	                             orderby fault.CreatedAt, fault.FaultId.Length, fault.FaultId
   172	                             // The pole's road is where the lamp physically stands (WO-12 orders by it too).
   173	                             select new { link.WorkOrderId, SegmentId = pole != null ? pole.SegmentId : fault.SegmentId,
   174	                                 Lat = fault.Lat ?? (pole == null ? (double?)null : pole.Geom.Y),
   175	                                 Lng = fault.Lng ?? (pole == null ? (double?)null : pole.Geom.X) }).ToListAsync(ct))
   176	            .ToLookup(x => x.WorkOrderId);
   177	
   178	        string[] RoadsOf(WorkOrder wo) => surveyRoads.Contains(wo.WorkOrderId)
   179	            ? surveyRoads[wo.WorkOrderId].ToArray()
   180	            : new[] { wo.SegmentId }.Concat(faults[wo.WorkOrderId].Select(x => x.SegmentId)
   181	                    .OfType<string>().OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal))
   182	                .OfType<string>().Distinct().ToArray();
   183	        var roadsOf = tonight.ToDictionary(x => x.WorkOrderId, RoadsOf);
   184	
   211	        foreach (var bucket in entries.GroupBy(x => x.SegmentIds is [var road, ..] ? road : "\0" + x.CommuneId))
   212	        {
   213	            var orders = bucket.OrderByDescending(x => x.WoStatus == WorkOrderStatus.InProgress)
   214	                .ThenByDescending(x => x.DueDate < night).ThenBy(x => x.DueDate is null).ThenBy(x => x.DueDate)
   215	                .ThenBy(x => x.CreatedAt).ThenBy(x => x.WorkOrderId.Length).ThenBy(x => x.WorkOrderId, StringComparer.Ordinal)
   216	                .ToArray();
   217	            var segmentId = orders[0].SegmentIds is [var key, ..] ? key : null;
   218	            if (segmentId is not null && roads.TryGetValue(segmentId, out var road))
   219	            {
   220	                groups.Add((new WorkOrderAgendaGroup
   221	                {
   222	                    SegmentId = segmentId, SegmentName = road.SegmentName, CommuneId = road.CommuneId,
   223	                    Location = origin is null ? new(road.Geom.StartPoint.Y, road.Geom.StartPoint.X) : Nearest(road.Geom, origin),
   224	                    DistanceM = origin is null ? null : roadDistance[segmentId], WorkOrders = orders,
   225	                }, [0, road.CreatedAt, segmentId.Length, segmentId]));
   226	                continue;
   227	            }
   228	            var location = orders.Select(x => x.Location).FirstOrDefault(x => x is not null);
   229	            groups.Add((new WorkOrderAgendaGroup
   230	            {
   231	                SegmentId = segmentId, CommuneId = orders[0].CommuneId, Location = location,
   232	                DistanceM = origin is null || location is null ? null : await PointDistanceAsync(location, origin, orders[0].WorkOrderId, ct),
   233	                WorkOrders = orders,
   113	            || hint.Confidence is { } c && (!double.IsFinite(c) || c is < 0 or > 1)
   114	            || string.IsNullOrWhiteSpace(hint.ModelVersion) || string.IsNullOrWhiteSpace(hint.ArtifactVersion)))
   115	            throw new InvalidOperationException("Invalid single-lamp hint.");
   116	
   117	        // No locks across storage, image decode or model inference. Global order: order → pole → photo.
   118	        await using var tx = await db.Database.BeginTransactionAsync(ct);
   119	        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
   120	        await Order(id, true, ct);
   121	        if (await Replay() is { } overlap) return (overlap, false);
   122	        Writable(await Order(id, true, ct));
   123	        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} AND commune_id = {target.CommuneId} FOR UPDATE", ct);
   124	        var fresh = await Target(id, poleId, ct);
   125	        if (fresh.CommuneId != target.CommuneId || fresh.DataSource != target.DataSource || fresh.SegmentId != target.SegmentId)
   126	            throw Error("POLE_PHOTO_TARGET_CHANGED", HttpStatusCode.Conflict);
   127	        var now = UtcMicrosecondClock.UtcNow();
   128	        var row = new WorkOrderPolePhoto
   129	        {

[exit=0]
```

</details>

<details>
<summary>E08 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Map/Features/CabinetTopologyService.cs | sed -n '86,120p'; nl -ba src/LuxMap.Modules.Map/Features/MapQueryService.cs | sed -n '84,141p;210,235p;286,296p;455,475p'; nl -ba src/LuxMap.Modules.Map/Features/PoleDetailService.cs | sed -n '47,59p;75,84p'; rg -n -B 2 'SegmentId|SegmentName' src/LuxMap.Modules.Map/Features/PoleMapDetail.cs src/LuxMap.Modules.Map/Features/MapProperties.cs
```

```text
    86	
    87	        var feederIds = feeders.Select(feeder => feeder.FeederId).ToArray();
    88	        var poles = await db.Set<Pole>().AsNoTracking()
    89	            .Where(pole => pole.FeederId != null && feederIds.Contains(pole.FeederId))
    90	            .Select(pole => new { pole.PoleId, FeederId = pole.FeederId!, pole.SegmentId, pole.Geom, pole.FeederSource })
    91	            .ToListAsync(ct);
    92	
    93	        var segmentIds = poles.Select(pole => pole.SegmentId).Distinct().ToArray();
    94	        var roads = (await db.Set<RoadSegment>().AsNoTracking().IgnoreQueryFilters()
    95	                .Where(segment => segmentIds.Contains(segment.SegmentId))
    96	                .Select(segment => new { segment.SegmentId, segment.CreatedAt, segment.Geom })
    97	                .ToListAsync(ct))
    98	            .ToDictionary(segment => segment.SegmentId, StringComparer.Ordinal);
    99	
   100	        var features = new List<Feature<CabinetTopologyEdgeProperties>>();
   101	        var branch = 0;
   102	
   103	        foreach (var feeder in feeders)
   104	        {
   105	            // A road missing here was deleted after its poles were read (a pole cannot outlive its segment — RESTRICT), so
   106	            // its poles are gone too: skip them rather than answer 500 on a concurrent delete (Codex review P2).
   107	            var bySegment = poles
   108	                .Where(pole => pole.FeederId == feeder.FeederId && roads.ContainsKey(pole.SegmentId))
   109	                .GroupBy(pole => pole.SegmentId, StringComparer.Ordinal)
   110	                .OrderBy(group => roads[group.Key].CreatedAt)
   111	                .ThenBy(group => group.Key.Length)
   112	                .ThenBy(group => group.Key, StringComparer.Ordinal);
   113	
   114	            foreach (var group in bySegment)
   115	            {
   116	                var line = new LengthIndexedLine(roads[group.Key].Geom);
   117	                var cabinetAt = line.Project(cabinet.Geom.Coordinate);
   118	                var placed = group
   119	                    .Select(pole => new { Pole = pole, At = line.Project(pole.Geom.Coordinate) })
   120	                    .ToList();
    84	    /// </summary>
    85	    public Task<FeatureCollection<PoleProperties>> PolesOnSegmentsAsync(IReadOnlyCollection<string> segmentIds, CancellationToken ct)
    86	        => ProjectPolesAsync(dbContext.Set<Pole>().AsNoTracking()
    87	            .Where(pole => segmentIds.Contains(pole.SegmentId))
    88	            .OrderBy(pole => pole.CreatedAt).ThenBy(pole => pole.PoleId.Length).ThenBy(pole => pole.PoleId), ct);
    89	
    90	    /// <summary>The given segments, in the order asked — the offline bundle's segment layer (BE-43).</summary>
    91	    public async Task<FeatureCollection<SegmentProperties>> SegmentsByIdAsync(IReadOnlyList<string> segmentIds, CancellationToken ct)
    92	    {
    93	        var layer = await ProjectSegmentsAsync(dbContext.Set<RoadSegment>().AsNoTracking()
    94	            .Where(segment => segmentIds.Contains(segment.SegmentId)), ct);
    95	        var order = segmentIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
    96	        return new FeatureCollection<SegmentProperties> { Features = [.. layer.Features.OrderBy(f => order[f.Properties.SegmentId])] };
    97	    }
    98	
    99	    private async Task<FeatureCollection<PoleProperties>> ProjectPolesAsync(IQueryable<Pole> poles, CancellationToken ct)
   100	    {
   101	        var rows = await poles
   102	            .Select(pole => new
   103	            {
   104	                pole.PoleId,
   105	                pole.SegmentId,
   106	                pole.CommuneId,
   107	                pole.NearSensitivePoi,
   108	                pole.Geom,
   109	
   110	                // Left joins, all of them. A pole with no status row, no lamp and no fault is a
   111	                // perfectly ordinary pole — most of the mock set is exactly that.
   112	                Status = dbContext.Set<PoleCurrentStatus>()
   113	                    .Where(status => status.PoleId == pole.PoleId)
   114	                    .Select(status => new
   115	                    {
   116	                        status.FixtureStatus,
   117	                        status.StatusConfidence,
   118	                        status.LastSeenAt,
   119	                        status.LastSweepId,
   120	                    })
   121	                    .FirstOrDefault(),
   122	
   123	                // The lamp IN SERVICE — unique per pole since BE-REVIEW-02 constraint 3.
   124	                Lamp = pole.Fixtures
   125	                    .Where(lamp => lamp.RemovedDate == null)
   126	                    .Select(lamp => new
   127	                    {
   128	                        lamp.PowerSource,
   129	                        lamp.FixtureType,
   130	                        lamp.LampWatt,
   131	                        lamp.LampCount,
   132	                        lamp.InstallDate,
   133	                        lamp.WarrantyExpiry,
   134	                    })
   135	                    .FirstOrDefault(),
   136	
   137	                // FaultStatusSets.Open is the ONE definition of an open fault (BE-18). Never a
   138	                // hand-written status list here: a second copy would be right the day it was
   139	                // written and wrong the day the definition moved, with nothing to detect it.
   140	                OpenFaultCount = dbContext.Set<Fault>()
   141	                    .Count(fault => fault.PoleId == pole.PoleId
   210	                segment.SegmentId,
   211	                segment.SegmentName,
   212	                segment.RoadClass,
   213	                segment.LengthM,
   214	                segment.Geom,
   215	
   216	                PoleCount = dbContext.Set<Pole>().Count(pole => pole.SegmentId == segment.SegmentId),
   217	
   218	                // ONE cause for the whole road, not N lamp faults — the output of CV-15's spatial
   219	                // clustering. Nothing writes segment_outage yet, so this is false everywhere today.
   220	                HasActiveSegmentFault = dbContext.Set<Fault>()
   221	                    .Any(fault => fault.SegmentId == segment.SegmentId
   222	                        && fault.FaultType == FaultType.SegmentOutage
   223	                        && OpenFaultStatuses.Contains(fault.FaultStatus)),
   224	
   225	                // I-7b: derived on read through segment → pole → feeder → feeder_control → device.
   226	                // The commune filter applies to every set here, so a device of another commune
   227	                // never appears even on an inter_commune road.
   228	                ControllerNodeIds = dbContext.Set<IotNode>()
   229	                    .Where(node => dbContext.Set<FeederControl>().Any(control =>
   230	                        control.NodeId == node.NodeId
   231	                        && dbContext.Set<Pole>().Any(pole =>
   232	                            pole.SegmentId == segment.SegmentId && pole.FeederId == control.FeederId)))
   233	                    .OrderBy(node => node.CreatedAt)
   234	                    .ThenBy(node => node.NodeId.Length)
   235	                    .ThenBy(node => node.NodeId)
   286	                SegmentIds = dbContext.Set<RoadSegment>()
   287	                    .Where(segment => dbContext.Set<Pole>().Any(pole =>
   288	                        pole.SegmentId == segment.SegmentId
   289	                        && dbContext.Set<FeederControl>().Any(control =>
   290	                            control.NodeId == placed.Node.NodeId && control.FeederId == pole.FeederId)))
   291	                    .OrderBy(segment => segment.CreatedAt)
   292	                    .ThenBy(segment => segment.SegmentId.Length)
   293	                    .ThenBy(segment => segment.SegmentId)
   294	                    .Select(segment => segment.SegmentId)
   295	                    .ToList(),
   296	            })
   455	    /// </remarks>
   456	    public IQueryable<Pole> PoleQuery(PoleMapQuery query)
   457	    {
   458	        var envelope = Envelope(query.Bbox);
   459	
   460	        // ⚠️ Intersects(envelope), not X/Y comparisons: this is the form that reaches ix_pole_geom.
   461	        var poles = dbContext.Set<Pole>().AsNoTracking()
   462	            .Where(pole => pole.Geom.Intersects(envelope));
   463	
   464	        poles = WithDataSource(poles, query.DataSource, pole => pole.DataSource);
   465	
   466	        if (query.CommuneIds is { Count: > 0 } communes)
   467	        {
   468	            poles = poles.Where(pole => communes.Contains(pole.CommuneId));
   469	        }
   470	
   471	        if (query.SegmentId is { Length: > 0 } segmentId)
   472	        {
   473	            poles = poles.Where(pole => pole.SegmentId == segmentId);
   474	        }
   475	
    47	    {
    48	        var pole = await db.Set<Pole>().AsNoTracking()
    49	            .Where(p => p.PoleId == poleId)
    50	            .Select(p => new
    51	            {
    52	                p.PoleId, p.SegmentId, p.CommuneId, p.Geom,
    53	                SegmentName = db.Set<RoadSegment>().Where(s => s.SegmentId == p.SegmentId)
    54	                    .Select(s => s.SegmentName).FirstOrDefault(),
    55	                p.Note,
    56	            })
    57	            .SingleOrDefaultAsync(ct)
    58	            ?? throw new LuxMapException(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound,
    59	                "That pole does not exist, or it is outside your permitted commune scope.");
    75	        return new PoleMapDetail
    76	        {
    77	            PoleId = pole.PoleId,
    78	            SegmentId = pole.SegmentId,
    79	            SegmentName = pole.SegmentName ?? string.Empty,
    80	            CommuneId = pole.CommuneId,
    81	            Location = new PoleMapLocation(pole.Geom.Y, pole.Geom.X),
    82	            Fixture = fixture?.Detail is { } lamp
    83	                ? lamp with { WarrantyStatus = warranty.Window().Classify(lamp.WarrantyExpiry) }
    84	                : null,
src/LuxMap.Modules.Map/Features/PoleMapDetail.cs-15-{
src/LuxMap.Modules.Map/Features/PoleMapDetail.cs-16-    public required string PoleId { get; init; }
src/LuxMap.Modules.Map/Features/PoleMapDetail.cs:17:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/PoleMapDetail.cs:18:    public required string SegmentName { get; init; }
--
src/LuxMap.Modules.Map/Features/MapProperties.cs-25-    public required string PoleId { get; init; }
src/LuxMap.Modules.Map/Features/MapProperties.cs-26-
src/LuxMap.Modules.Map/Features/MapProperties.cs:27:    public required string SegmentId { get; init; }
--
src/LuxMap.Modules.Map/Features/MapProperties.cs-91-public sealed record SegmentProperties
src/LuxMap.Modules.Map/Features/MapProperties.cs-92-{
src/LuxMap.Modules.Map/Features/MapProperties.cs:93:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Map/Features/MapProperties.cs-94-
src/LuxMap.Modules.Map/Features/MapProperties.cs:95:    public required string SegmentName { get; init; }
--
src/LuxMap.Modules.Map/Features/MapProperties.cs-134-/// <para>
src/LuxMap.Modules.Map/Features/MapProperties.cs-135-/// Replaces the shape of Contract section 5.6: <c>battery_pct</c> is gone (I-2), <c>segment_id</c>
src/LuxMap.Modules.Map/Features/MapProperties.cs:136:/// became <see cref="SegmentIds"/>, and <see cref="FeederIds"/> and
--
src/LuxMap.Modules.Map/Features/MapProperties.cs-159-
src/LuxMap.Modules.Map/Features/MapProperties.cs-160-    /// <summary>Segments carrying a pole on a feeder this device switches, in id order.</summary>
src/LuxMap.Modules.Map/Features/MapProperties.cs:161:    public required IReadOnlyList<string> SegmentIds { get; init; }

[exit=0]
```

</details>

<details>
<summary>E09 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs | sed -n '46,98p'; rg -n -C 2 'SegmentId|JOIN pole|JOIN road_segment' src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs
```

```text
    46	    // routes as a diagnostic fallback; the outer LEFT JOIN still preserves an unassigned sample.
    47	    public static Task<ProjectedGps[]> Gps(LuxMapDbContext db, string sweepId, string[] segments,
    48	        string[] communes, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedGps>($"""
    49	        SELECT nearest.segment_id AS segment_id, g.phone_elapsed_ns AS time_ns,
    50	            COALESCE(nearest.chainage_m, 0) AS chainage_m,
    51	            COALESCE(nearest.length_m, 0) AS length_m,
    52	            nearest.distance_m AS route_distance_m, nearest.route_geometry_json AS route_geometry_json,
    53	            COALESCE(nearest.second_distance_m - nearest.distance_m < {o.RouteAmbiguityM}, false) AS route_ambiguous,
    54	            g.accuracy_m AS accuracy_m, g.speed_mps AS speed_mps
    55	        FROM survey_gps_sample g
    56	        LEFT JOIN LATERAL (
    57	            SELECT candidate.*, lead(distance_m) OVER (ORDER BY distance_m, segment_id) AS second_distance_m
    58	            FROM (
    59	                SELECT r.segment_id, ST_AsGeoJSON(r.geom) AS route_geometry_json,
    60	                    ST_Distance(ST_Transform(r.geom,3405), ST_Transform(g.geom,3405)) AS distance_m,
    61	                    ST_LineLocatePoint(ST_Transform(r.geom,3405), ST_Transform(g.geom,3405))
    62	                        * ST_Length(ST_Transform(r.geom,3405)) AS chainage_m,
    63	                    ST_Length(ST_Transform(r.geom,3405)) AS length_m
    64	                FROM road_segment r
    65	                WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
    66	                  AND (ST_Intersects(g.geom, ST_Expand(ST_Envelope(r.geom), {o.BboxPaddingDegrees}))
    67	                    OR NOT EXISTS (
    68	                        SELECT 1 FROM road_segment bounded
    69	                        WHERE bounded.segment_id = ANY({segments}) AND bounded.commune_id = ANY({communes})
    70	                          AND ST_Intersects(g.geom, ST_Expand(ST_Envelope(bounded.geom), {o.BboxPaddingDegrees}))))
    71	            ) candidate
    72	            ORDER BY distance_m, segment_id LIMIT 1
    73	        ) nearest ON true
    74	        WHERE g.sweep_id = {sweepId}
    75	        ORDER BY g.phone_elapsed_ns, g.sample_no
    76	        """).ToArrayAsync(ct);
    77	
    78	    public static Task<ProjectedPole[]> Poles(LuxMapDbContext db, string[] segments, string[] communes,
    79	        string source, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedPole>($"""
    80	        SELECT r.segment_id AS segment_id, p.pole_id AS pole_id, p.commune_id AS commune_id,
    81	            ST_AsGeoJSON(p.geom) AS geometry_json, ST_AsGeoJSON(r.geom) AS route_geometry_json,
    82	            ((ST_X(tangent.b) - ST_X(tangent.a)) * (ST_Y(projected.pole) - ST_Y(tangent.a))
    83	             - (ST_Y(tangent.b) - ST_Y(tangent.a)) * (ST_X(projected.pole) - ST_X(tangent.a))) AS side_of_route,
    84	            ST_LineLocatePoint(ST_Transform(r.geom,3405), ST_Transform(p.geom,3405))
    85	                * ST_Length(ST_Transform(r.geom,3405)) AS chainage_m,
    86	            ST_Length(ST_Transform(r.geom,3405)) AS length_m
    87	        FROM road_segment r JOIN pole p ON p.segment_id = r.segment_id
    88	          AND ST_Intersects(p.geom, ST_Expand(ST_Envelope(r.geom), {o.BboxPaddingDegrees}))
    89	        CROSS JOIN LATERAL (SELECT ST_Transform(r.geom,3405) AS route, ST_Transform(p.geom,3405) AS pole) projected
    90	        CROSS JOIN LATERAL (SELECT ST_LineLocatePoint(projected.route, projected.pole) AS fraction) position
    91	        CROSS JOIN LATERAL (SELECT ST_LineInterpolatePoint(projected.route, greatest(0, position.fraction - 0.00001)) AS a,
    92	            ST_LineInterpolatePoint(projected.route, least(1, position.fraction + 0.00001)) AS b) tangent
    93	        WHERE r.segment_id = ANY({segments}) AND r.commune_id = ANY({communes})
    94	          AND p.commune_id = ANY({communes}) AND p.data_source = {source}
    95	          AND ST_Distance(ST_Transform(r.geom,3405), ST_Transform(p.geom,3405)) <= {o.RouteCorridorM}
    96	        ORDER BY r.segment_id, chainage_m, length(p.pole_id), p.pole_id
    97	        """).ToArrayAsync(ct);
    98	}
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs-59-            var fault = new Fault
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs-60-            {
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:61:                FaultId = id, PoleId = pole.PoleId, CommuneId = pole.CommuneId, SegmentId = pole.SegmentId,
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs-62-                FaultType = FaultType.LampOut, FaultStatus = FaultStatus.Confirmed,
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs-63-                SourceChannel = human ? SourceChannel.FieldReport : SourceChannel.Cv, DataSource = pole.DataSource, OriginPolePhotoId = input.PhotoId,
--
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-109-            .Select(o => o.PoleId).Distinct().ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-110-        var passes = await db.Set<SurveyPass>().AsNoTracking().Where(p => p.RunId == run.RunId).OrderBy(p => p.PassNo).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:111:        var lengths = await db.Set<RoadSegment>().AsNoTracking().Where(r => segmentIds.Contains(r.SegmentId))
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:112:            .ToDictionaryAsync(r => r.SegmentId, r => (double?)r.LengthM, ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-113-
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-114-        var plans = segmentIds.Select(segment =>
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-115-        {
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-116-            var length = lengths.GetValueOrDefault(segment);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:117:            var driven = passes.Where(p => p.SegmentId == segment).ToArray();
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-118-            var gaps = SweepCoverage.Gaps(SweepCoverage.Union(driven.Select(p => (p.FromFraction, p.ToFraction))), length);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-119-            return (Segment: segment, Length: length, Passes: driven, Gaps: gaps);
--
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-146-                ST_AsGeoJSON(ST_Transform(ST_LineSubstring(ST_Transform(r.geom, 3405), g.from_fraction, g.to_fraction), 4326)) AS geo_json
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-147-            FROM unnest({segments}::text[], {froms}::float8[], {tos}::float8[]) WITH ORDINALITY AS g(segment_id, from_fraction, to_fraction, ordinal)
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:148:            JOIN road_segment r ON r.segment_id = g.segment_id AND r.commune_id = ANY({communes})
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-149-            """).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-150-        var result = new Dictionary<(string, double, double), LuxMap.Shared.Contracts.GeoJson.Geometry>();
--
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-305-                var faultId = await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-306-                var pole = poles[o.PoleId];
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:307:                var fault = new Fault { FaultId = faultId, PoleId = o.PoleId, CommuneId = o.CommuneId, SegmentId = pole.SegmentId,
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-308-                    FaultType = type, FaultStatus = FaultStatus.Detected, SourceChannel = SourceChannel.Cv,
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs-309-                    DataSource = sweep.DataSource, OriginObservationId = o.ObservationId, DetectionModelVersion = modelVersion,
--
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-77-            await ValidateAssignee(db, sweep, job.Communes, ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-78-            var segments = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == sweep.WorkOrderId)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:79:                .OrderBy(x => x.Position).Select(x => x.SegmentId).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-80-            await Heartbeat(db, sweep, job, o, ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-81-            // One PostgreSQL snapshot includes the geometry used by both projections.
--
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-90-            run.ClockFit = Json(new { epochs = fits });
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-91-            var intervals = SurveyAlgorithms.AlignLux(raw, fits, o);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:92:            foreach (var route in gps.Where(x => x.SegmentId is not null && !x.RouteAmbiguous && x.RouteDistanceM <= o.RouteCorridorM).GroupBy(x => x.SegmentId!))
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-93-            {
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-94-                await Heartbeat(db, sweep, job, o, ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:95:                var expected = poles.Where(x => x.SegmentId == route.Key).Select(x => new PolePosition(x.PoleId, x.CommuneId, x.ChainageM)).ToArray();
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-96-                var track = gps.Select(x => new TrackPoint(x.TimeNs, x.ChainageM, x.AccuracyM, x.SpeedMps,
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-97-                    x.RouteDistanceM is null || x.RouteDistanceM > o.RouteCorridorM, x.RouteAmbiguous,
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:98:                    x.SegmentId != route.Key && !x.RouteAmbiguous && x.RouteDistanceM <= o.RouteCorridorM)).ToArray();
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-99-                foreach (var pass in SurveyAlgorithms.ProcessRoute(track, expected, intervals, o))
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-100-                    results.Add((route.Key, route.First().LengthM, pass));
--
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-145-        var communes = await db.Database.SqlQuery<string>($"""
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-146-            SELECT {selected.CommuneId} AS "Value"
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:147:            UNION SELECT r.commune_id FROM work_order_segment w JOIN road_segment r USING (segment_id)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-148-              WHERE w.work_order_id = {selected.WorkOrderId}
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:149:            UNION SELECT p.commune_id FROM work_order_segment w JOIN pole p USING (segment_id)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-150-              JOIN survey_sweep s ON s.sweep_id = {selected.SweepId} AND p.data_source = s.data_source
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-151-              WHERE w.work_order_id = {selected.WorkOrderId}
--
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-254-            var pass = new SurveyPass
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-255-            {
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:256:                Run = run, SegmentId = result.Segment, PassNo = number++,
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-257-                Direction = result.Pass.Direction,
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs-258-                FromFraction = result.Pass.Track[0].ChainageM / result.Length, ToFraction = result.Pass.Track[^1].ChainageM / result.Length,
--
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-29-    private IQueryable<SurveySweep> Visible() => db.Set<SurveySweep>().Where(s => db.Set<WorkOrder>().Any(w =>
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-30-        w.WorkOrderId == s.WorkOrderId && !db.Set<WorkOrderSegment>().Any(link => link.WorkOrderId == w.WorkOrderId
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:31:            && !db.Set<RoadSegment>().Any(road => road.SegmentId == link.SegmentId))));
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-32-
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-33-    private async Task<SurveySweep> Find(string id, CancellationToken ct)
--
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-45-            ?? throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-46-        if (write && wo.AssignedTo != ActorId) throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:47:        var ids = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).Select(x => x.SegmentId).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-48-        if (wo.TaskKind != TaskKind.Survey || ids.Length == 0
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:49:            || await db.Set<RoadSegment>().CountAsync(x => ids.Contains(x.SegmentId), ct) != ids.Length)
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-50-            throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-51-        return wo;
--
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-241-                r.CoveragePct, r.DetectionCoveragePct, r.DimCoveragePct, r.CoverageReason))
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-242-            .FirstOrDefaultAsync(ct);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:243:        return new SweepDetailResponse(s.SweepId, s.WorkOrderId, s.StartedAt, s.EndedAt, s.SegmentIds, s.FrameCount, s.CoveragePct,
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-244-            s.ProcessingStatus, s.Status, s.DataSource, s.SubmittedAt, s.Clips, s.RawFiles, s.Version, latest);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-245-    }
--
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-250-        var query = Visible().AsNoTracking();
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-251-        if (workOrder is not null) query = query.Where(x => x.WorkOrderId == workOrder);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:252:        if (segment is not null) query = query.Where(x => db.Set<WorkOrderSegment>().Any(w => w.WorkOrderId == x.WorkOrderId && w.SegmentId == segment));
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-253-        if (processing is not null) query = query.Where(x => x.ProcessingStatus == processing);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-254-        if (source is not null) query = query.Where(x => x.DataSource == source);
--
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-267-        var ids = sweeps.Select(x => x.SweepId).ToArray();
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-268-        var segments = (await db.Set<WorkOrderSegment>().Where(x => orders.Contains(x.WorkOrderId)).OrderBy(x => x.Position)
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:269:            .Select(x => new { x.WorkOrderId, x.SegmentId }).ToArrayAsync(ct)).ToLookup(x => x.WorkOrderId, x => x.SegmentId);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-270-        var clips = (await db.Set<SurveyVideoClip>().Where(x => ids.Contains(x.SweepId)).OrderBy(x => x.ClipNo).ToArrayAsync(ct))
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs-271-            .ToLookup(x => x.SweepId, x => new SurveyClipResponse(x.ClipNo, x.Sha256, x.ByteCount, x.ContentType));

[exit=0]
```

</details>

<details>
<summary>E10 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Sync/SyncBundleService.cs | sed -n '43,68p;99,140p'; nl -ba src/LuxMap.Modules.Sync/SyncPushService.cs | sed -n '198,207p;239,278p;346,358p;377,390p'; nl -ba src/LuxMap.Modules.Sync/SyncContracts.cs | sed -n '13,54p'
```

```text
    43	    {
    44	        // One snapshot: the layers, the faults and the orders must describe the same moment.
    45	        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    46	        var generatedAt = UtcMicrosecondClock.UtcNow();
    47	
    48	        var segmentIds = requested is { Count: > 0 } ? await NamedSegmentsAsync(requested, ct) : await MySegmentsAsync(ct);
    49	        var poles = await map.PolesOnSegmentsAsync(segmentIds, ct);
    50	        var notes = await db.Set<Pole>().AsNoTracking()
    51	            .Where(pole => segmentIds.Contains(pole.SegmentId))
    52	            .Select(pole => new { pole.PoleId, pole.Note })
    53	            .ToDictionaryAsync(pole => pole.PoleId, pole => pole.Note, StringComparer.Ordinal, ct);
    54	
    55	        var bundle = new SyncBundle
    56	        {
    57	            GeneratedAt = generatedAt,
    58	            SegmentIds = segmentIds,
    59	            Segments = await map.SegmentsByIdAsync(segmentIds, ct),
    60	            Poles = new FeatureCollection<SyncPoleProperties>
    61	            {
    62	                Features = [.. poles.Features.Select(feature => new Feature<SyncPoleProperties>
    63	                {
    64	                    Geometry = feature.Geometry,
    65	                    Properties = new SyncPoleProperties(feature.Properties, notes.GetValueOrDefault(feature.Properties.PoleId)),
    66	                })],
    67	            },
    68	            OpenFaults = await faults.OpenOnSegmentsAsync(segmentIds, ct),
    99	    /// D-2 — no <c>segment_id</c>: "tonight's work". Every segment of the caller's open work orders — the order's own
   100	    /// segment, its survey segments, and the segments of the faults it holds and of their poles — in id order.
   101	    /// </summary>
   102	    private async Task<IReadOnlyList<string>> MySegmentsAsync(CancellationToken ct)
   103	    {
   104	        var mine = MyOpenOrders();
   105	        var links = db.Set<WorkOrderFault>().Where(link => link.ReleasedAt == null && mine.Any(wo => wo.WorkOrderId == link.WorkOrderId));
   106	        var held = db.Set<Fault>().Where(fault => links.Any(link => link.FaultId == fault.FaultId));
   107	
   108	        return await db.Set<RoadSegment>()
   109	            .Where(segment => mine.Any(wo => wo.SegmentId == segment.SegmentId)
   110	                || db.Set<WorkOrderSegment>().Any(s => s.SegmentId == segment.SegmentId && mine.Any(wo => wo.WorkOrderId == s.WorkOrderId))
   111	                || held.Any(fault => fault.SegmentId == segment.SegmentId)
   112	                || db.Set<Pole>().Any(pole => pole.SegmentId == segment.SegmentId && held.Any(fault => fault.PoleId == pole.PoleId)))
   113	            .OrderBy(segment => segment.CreatedAt).ThenBy(segment => segment.SegmentId.Length).ThenBy(segment => segment.SegmentId)
   114	            .Select(segment => segment.SegmentId)
   115	            .ToListAsync(ct);
   116	    }
   117	
   118	    /// <summary>The caller's open orders touching these segments, oldest first, each read as its own detail.</summary>
   119	    private async Task<WorkOrderDetail[]> WorkOrdersAsync(IReadOnlyList<string> segmentIds, CancellationToken ct)
   120	    {
   121	        var onSegments = db.Set<Fault>().Where(fault => (fault.SegmentId != null && segmentIds.Contains(fault.SegmentId))
   122	            || db.Set<Pole>().Any(pole => pole.PoleId == fault.PoleId && segmentIds.Contains(pole.SegmentId)));
   123	
   124	        var ids = await MyOpenOrders()
   125	            .Where(wo => (wo.SegmentId != null && segmentIds.Contains(wo.SegmentId))
   126	                || db.Set<WorkOrderSegment>().Any(s => s.WorkOrderId == wo.WorkOrderId && segmentIds.Contains(s.SegmentId))
   127	                || db.Set<WorkOrderFault>().Any(link => link.WorkOrderId == wo.WorkOrderId && link.ReleasedAt == null
   128	                    && onSegments.Any(fault => fault.FaultId == link.FaultId)))
   129	            .OrderBy(wo => wo.CreatedAt).ThenBy(wo => wo.WorkOrderId.Length).ThenBy(wo => wo.WorkOrderId)
   130	            .Select(wo => wo.WorkOrderId)
   131	            .ToListAsync(ct);
   132	
   133	        var details = new WorkOrderDetail[ids.Count];
   134	        for (var i = 0; i < ids.Count; i++)
   135	        {
   136	            details[i] = await workOrders.Detail(ids[i], ct);
   137	        }
   138	
   139	        return details;
   140	    }
   198	    /// <summary>The capability of the operation's own endpoint — the same policy, not a copy of its role list.</summary>
   199	    private static string PolicyFor(SyncOpType type) => type switch
   200	    {
   201	        SyncOpType.FaultReport => LuxMapPolicies.ReportFaults,
   202	        SyncOpType.LuxReading => LuxMapPolicies.RecordLuxReading,
   203	        SyncOpType.PoleNote => LuxMapPolicies.EditPoleNotes,
   204	        SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => LuxMapPolicies.ExecuteWorkOrders,
   205	        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
   206	    };
   207	
   239	        switch (type)
   240	        {
   241	            case SyncOpType.FaultReport:
   242	                return new(Read<ReportFaultRequest>(WithKey(body, operation)), null, null);
   243	
   244	            case SyncOpType.LuxReading:
   245	            {
   246	                var reading = Read<CreateLuxReadingRequest>(WithKey(body, operation));
   247	                Validate(reading);
   248	                LuxReadingService.RejectServerOwnedFields(reading);
   249	                return new(reading, null, null);
   250	            }
   251	
   252	            case SyncOpType.PoleNote:
   253	            {
   254	                var element = JsonSerializer.SerializeToElement(body);
   255	                var poleId = OptionalJson.Text(element.TryGetProperty("pole_id", out var p) ? p : default, "pole_id");
   256	                if (string.IsNullOrWhiteSpace(poleId)) throw OptionalJson.Invalid("pole_id");
   257	                var (sent, note) = PoleNoteInput.Read(element.TryGetProperty("note", out var n) ? n : default);
   258	                if (!sent) throw OptionalJson.Invalid("note");
   259	                var (seen, text) = PoleNoteInput.Read(element.TryGetProperty("base_note", out var b) ? b : default);
   260	                return new(new NotePayload(poleId, note, seen ? new ExpectedNote(text) : null), poleId, $"pole:{poleId}");
   261	            }
   262	
   263	            case SyncOpType.WorkOrderStart:
   264	            {
   265	                var start = Read<StartPayload>(body);
   266	                var id = RequiredId(start.WorkOrderId);
   267	                return new(start, id, $"work_order:{id}");
   268	            }
   269	
   270	            case SyncOpType.WorkOrderComplete:
   271	            {
   272	                var complete = Read<CompletePayload>(body);
   273	                var id = RequiredId(complete.WorkOrderId);
   274	                return new(complete, id, $"work_order:{id}");
   275	            }
   276	
   277	            default:
   278	                throw new ArgumentOutOfRangeException(nameof(type), type, null);
   346	        switch (parsed.Body)
   347	        {
   348	            case ReportFaultRequest report:
   349	            {
   350	                var (fault, created) = await faults.ReportAsync(report, ct);
   351	                return (fault.FaultId, !created);
   352	            }
   353	
   354	            case CreateLuxReadingRequest reading:
   355	            {
   356	                var (created, stored) = await luxReadings.CreateAsync(reading, ActorId, ct);
   357	                return (stored.LuxId, !created);
   358	            }
   377	        {
   378	            case NotePayload note:
   379	                await assets.SetPoleNoteAsync(note.PoleId, note.Note, ct, note.Expected);
   380	                break;
   381	            case StartPayload start:
   382	                await workOrders.Act(parsed.EntityId!, "start", null, default, ct, performedAt: start.PerformedAt);
   383	                break;
   384	            case CompletePayload complete:
   385	                await workOrders.Act(parsed.EntityId!, "complete", complete.ReportNote, complete.FaultOutcomes, ct,
   386	                    complete.MaterialsUsed, complete.PerformedAt);
   387	                break;
   388	        }
   389	
   390	        return (parsed.EntityId!, false);
    13	/// <remarks>
    14	/// A full snapshot every time, never a delta: the client REPLACES its cache for these segments. A pole deleted,
    15	/// a work order handed to someone else, a commune taken away — each simply stops being in the next bundle.
    16	/// Read in one REPEATABLE READ transaction so the five parts describe the same moment.
    17	/// </remarks>
    18	public sealed record SyncBundle
    19	{
    20	    /// <summary>When the snapshot was taken — what a phone shows as "data as of".</summary>
    21	    public required DateTime GeneratedAt { get; init; }
    22	
    23	    /// <summary>The segments actually packed: the ones asked for, or those of the caller's open work orders.</summary>
    24	    public required IReadOnlyList<string> SegmentIds { get; init; }
    25	
    26	    /// <summary>Same properties as <c>GET /map/segments</c>, in the order of <see cref="SegmentIds"/>.</summary>
    27	    public required FeatureCollection<SegmentProperties> Segments { get; init; }
    28	
    29	    /// <summary>Same properties as <c>GET /map/poles</c> plus the pole's <c>note</c>, in id order.</summary>
    30	    public required FeatureCollection<SyncPoleProperties> Poles { get; init; }
    31	
    32	    /// <summary>Open faults on these segments or their poles, each an item of <c>GET /faults</c>, default order.</summary>
    33	    public required IReadOnlyList<FaultItem> OpenFaults { get; init; }
    34	
    35	    /// <summary>The caller's open work orders (<c>assigned</c>, <c>in_progress</c>) touching these segments, as details.</summary>
    36	    public required IReadOnlyList<WorkOrderDetail> WorkOrders { get; init; }
    37	}
    38	
    39	/// <summary>A pole of the bundle: the map layer's fifteen properties plus <c>note</c>, still flat (BE-43 D-3).</summary>
    40	public sealed record SyncPoleProperties : PoleProperties
    41	{
    42	    [SetsRequiredMembers]
    43	    public SyncPoleProperties(PoleProperties map, string? note) : base(map) => Note = note;
    44	
    45	    /// <summary>The free-text note on the pole (POLE-NOTE); <c>null</c> when none. What a <c>pole_note</c> push sends as <c>base_note</c>.</summary>
    46	    public string? Note { get; init; }
    47	}
    48	
    49	/// <summary><c>POST /sync/push</c> — the offline queue, applied in order (BE-43 D-7).</summary>
    50	public sealed record SyncPushRequest
    51	{
    52	    public IReadOnlyList<SyncOperationRequest>? Operations { get; init; }
    53	}
    54	

[exit=0]
```

</details>

<details>
<summary>E11 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Faults/FaultReportService.cs | sed -n '134,166p;199,211p'; nl -ba src/LuxMap.Modules.Faults/FaultQueryService.cs | sed -n '61,64p;99,105p;119,129p'; rg -n -C 2 'SegmentId|ck_fault_pole_or_location' src/LuxMap.Modules.Faults/Entities/Fault.cs src/LuxMap.Modules.Faults/Entities/FaultCluster.cs src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs; nl -ba src/LuxMap.Modules.Citizen/PublicReportService.cs | sed -n '31,36p;63,78p'; rg -n -C 2 'SegmentId' src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs src/LuxMap.Modules.Citizen/CitizenReviewService.cs
```

```text
   134	        string communeId; string? segmentId = null; DataSource source = DataSource.Field;
   135	        if (request.PoleId is { } poleId)
   136	        {
   137	            if (request.CommuneId is not null)
   138	                throw Invalid("commune_id", "commune_id is looked up from the pole; do not send it with pole_id.");
   139	            // Read the pole FIRST: the commune filter is in this WHERE, so out of scope is simply not found.
   140	            var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(p => p.PoleId == poleId, ct)
   141	                ?? throw new LuxMapException(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound,
   142	                    "That pole does not exist, or it is outside your permitted commune scope.");
   143	            communeId = pole.CommuneId; segmentId = pole.SegmentId; source = pole.DataSource;
   144	            if (request.FixtureId is { } fixtureId && !await db.Set<Fixture>().AnyAsync(
   145	                    f => f.FixtureId == fixtureId && f.PoleId == poleId && f.RemovedDate == null, ct))
   146	                throw Invalid("fixture_id", "fixture_id must be the lamp currently in use on that pole.");
   147	        }
   148	        else
   149	        {
   150	            if (request.Location is null)
   151	                throw new LuxMapException(ErrorCodes.LocationRequired, HttpStatusCode.BadRequest,
   152	                    "A report without pole_id needs location: the fault has to be somewhere on the map.");
   153	            if (request.FixtureId is not null) throw Invalid("fixture_id", "fixture_id needs the pole it is mounted on.");
   154	            communeId = await ReportCommune(request.CommuneId, ct);
   155	        }
   156	
   157	        var faultId = await NextFaultId(ct);
   158	        var fault = new Fault
   159	        {
   160	            FaultId = faultId, ClientOpId = key, PoleId = request.PoleId, FixtureId = request.FixtureId,
   161	            SegmentId = segmentId, CommuneId = communeId, Lat = request.Location?.Lat, Lng = request.Location?.Lng,
   162	            FaultType = type, FaultStatus = FaultStatus.Detected, Severity = request.Severity ?? Severity.Medium,
   163	            SourceChannel = SourceChannel.FieldReport, DataSource = source, DetectedAt = detected,
   164	            Note = note, ReportedBy = ActorId, CreatedAt = now, UpdatedAt = now,
   165	        };
   166	        db.Add(fault);
   199	            throw new LuxMapException(ErrorCodes.FaultTypeNotReportable, HttpStatusCode.BadRequest,
   200	                "Only lamp_out and lamp_dim can be reported; cluster and IoT faults come from the engines.");
   201	
   202	        var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(p => p.PoleId == report.PoleId, ct)
   203	            ?? throw new LuxMapException(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound,
   204	                "That pole does not exist, or it is outside your permitted commune scope.");
   205	        var fault = new Fault
   206	        {
   207	            FaultId = await NextFaultId(ct), PoleId = pole.PoleId, SegmentId = pole.SegmentId, CommuneId = pole.CommuneId,
   208	            FaultType = report.FaultType, FaultStatus = FaultStatus.Detected, Severity = report.Severity,
   209	            SourceChannel = SourceChannel.FieldReport, DataSource = pole.DataSource, DetectedAt = report.DetectedAt,
   210	            Note = report.Note, ReviewNote = report.ReviewNote, ReportedBy = ActorId, CreatedAt = now, UpdatedAt = now,
   211	        };
    61	        if (query.SegmentId is { } segment)
    62	        {
    63	            faults = faults.Where(fault => fault.SegmentId == segment);
    64	        }
    99	    public async Task<FaultItem[]> OpenOnSegmentsAsync(IReadOnlyCollection<string> segmentIds, CancellationToken ct)
   100	    {
   101	        FaultStatus[] open = [.. FaultStatusSets.Open];
   102	        var poles = db.Set<Pole>().Where(pole => segmentIds.Contains(pole.SegmentId)).Select(pole => pole.PoleId);
   103	        var faults = db.Set<Fault>().AsNoTracking().Where(fault => open.Contains(fault.FaultStatus)
   104	            && ((fault.SegmentId != null && segmentIds.Contains(fault.SegmentId)) || (fault.PoleId != null && poles.Contains(fault.PoleId))));
   105	        var rows = await Order(Locate(faults), FaultSort.Default).ToListAsync(ct);
   119	        =>
   120	            from fault in faults
   121	            join pole in db.Set<Pole>() on fault.PoleId equals pole.PoleId into poles
   122	            from pole in poles.DefaultIfEmpty()
   123	            select new LocatedFault
   124	            {
   125	                Fault = fault,
   126	                Lat = fault.Lat ?? (pole == null ? 0 : pole.Geom.Y),
   127	                Lng = fault.Lng ?? (pole == null ? 0 : pole.Geom.X),
   128	            };
   129	
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs-37-    /// highlights. The feeder is reachable through the poles.
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs-38-    /// </remarks>
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs:39:    public required string SegmentId { get; set; }
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs-40-
src/LuxMap.Modules.Faults/Entities/FaultCluster.cs-41-    /// <summary>Denormalised from the segment. Without it the cluster escapes Contract section 7 entirely.</summary>
--
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-32-
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-33-        builder.Property(cluster => cluster.ClusterId).HasPrefixedId(PrefixedIds.FaultCluster);
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:34:        builder.Property(cluster => cluster.SegmentId).HasColumnType("text").IsRequired();
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-35-        builder.Property(cluster => cluster.ClusteringModelVersion).HasColumnType("text");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-36-        builder.Property(cluster => cluster.ClusteredAt).IsRequired();
--
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-42-        builder.HasOne<RoadSegment>()
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-43-            .WithMany()
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:44:            .HasForeignKey(cluster => cluster.SegmentId)
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-45-            .OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-46-
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:47:        builder.HasIndex(cluster => cluster.SegmentId)
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-48-            .HasDatabaseName("ix_fault_cluster_segment_id");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-49-    }
--
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-62-        builder.Property(fault => fault.FaultId).HasPrefixedId(PrefixedIds.Fault);
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-63-
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:64:        foreach (var text in new[] { "ClientOpId", "PoleId", "FixtureId", "SegmentId", "ClusterId",
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-65-                                     "Note", "ReportedBy", "ConfirmedBy", "ResolvedBy",
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-66-                                     "DetectionModelVersion" })
--
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-99-            // import, sync push, a seeder) can create a fault nobody can go and find.
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-100-            table.HasCheckConstraint(
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:101:                "ck_fault_pole_or_location",
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-102-                "pole_id IS NOT NULL OR (lat IS NOT NULL AND lng IS NOT NULL)");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-103-
--
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-141-
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-142-        builder.HasOne<RoadSegment>().WithMany()
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:143:            .HasForeignKey(fault => fault.SegmentId).OnDelete(DeleteBehavior.Restrict);
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-144-
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-145-        builder.HasOne<FaultCluster>().WithMany()
--
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-164-
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-165-        builder.HasIndex(fault => fault.PoleId).HasDatabaseName("ix_fault_pole_id");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:166:        builder.HasIndex(fault => fault.SegmentId).HasDatabaseName("ix_fault_segment_id");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-167-        builder.HasIndex(fault => fault.ClusterId).HasDatabaseName("ix_fault_cluster_id");
src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs-168-        builder.HasIndex(fault => fault.FaultStatus).HasDatabaseName("ix_fault_fault_status");
--
src/LuxMap.Modules.Faults/Entities/Fault.cs-44-
src/LuxMap.Modules.Faults/Entities/Fault.cs-45-    /// <summary>Denormalised from the pole where there is one; set directly for a segment-level fault.</summary>
src/LuxMap.Modules.Faults/Entities/Fault.cs:46:    public string? SegmentId { get; set; }
src/LuxMap.Modules.Faults/Entities/Fault.cs-47-
src/LuxMap.Modules.Faults/Entities/Fault.cs-48-    /// <summary>
    31	public sealed record PublicQrInfo
    32	{
    33	    public required string SegmentName { get; init; }
    34	    public required string CommuneName { get; init; }
    35	}
    36	
    63	    public async Task<PublicQrInfo> LookupAsync(string? code, CancellationToken ct)
    64	    {
    65	        var target = await ResolveAsync(code, ct);
    66	        await using var db = scopes.Open(target.CommuneId);
    67	        return await db.Set<Pole>().AsNoTracking()
    68	            .Where(p => p.PoleId == target.PoleId)
    69	            .Select(p => new PublicQrInfo
    70	            {
    71	                SegmentName = db.Set<RoadSegment>().IgnoreQueryFilters()
    72	                    .Where(s => s.SegmentId == p.SegmentId).Select(s => s.SegmentName).First(),
    73	                CommuneName = db.Set<AdministrativeUnit>()
    74	                    .Where(u => u.CommuneId == p.CommuneId).Select(u => u.Name).First(),
    75	            })
    76	            .SingleOrDefaultAsync(ct) ?? throw NotFound();
    77	    }
    78	
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-364-
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-365-        var pole = await db.Set<Pole>().AsNoTracking().Where(p => p.PoleId == report.PoleId)
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:366:            .Select(p => new { p.PoleId, p.SegmentId, p.Geom }).SingleAsync(ct);
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-367-        var status = await db.Set<PoleCurrentStatus>().AsNoTracking().Where(s => s.PoleId == report.PoleId)
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-368-            .Select(s => new { s.FixtureStatus, s.LastSeenAt }).SingleOrDefaultAsync(ct);
--
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-408-            Pole = new CitizenReportPole
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-409-            {
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:410:                PoleId = pole.PoleId, SegmentId = pole.SegmentId, Location = new ReportPoint(pole.Geom.Y, pole.Geom.X),
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-411-                FixtureStatus = status?.FixtureStatus ?? FixtureStatus.Unknown, LastSeenAt = status?.LastSeenAt,
src/LuxMap.Modules.Citizen/CitizenReviewService.cs-412-            },
--
src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs-70-{
src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs-71-    public required string PoleId { get; init; }
src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs:72:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs-73-    public required ReportPoint Location { get; init; }
src/LuxMap.Modules.Citizen/CitizenReviewContracts.cs-74-

[exit=0]
```

</details>

<details>
<summary>E12 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Statistics/StatisticsService.cs | sed -n '52,96p;180,195p;222,243p'; nl -ba src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs | sed -n '600,625p;661,671p'; rg -n -C 2 'SegmentId|AffectedSegmentIds|UncontrollablePoleCount' src/LuxMap.Modules.Statistics/StatisticsContracts.cs src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs
```

```text
    52	    {
    53	        var byCommune = query.GroupBy.Contains(StatisticsDimension.Commune);
    54	        var bySegment = query.GroupBy.Contains(StatisticsDimension.Segment);
    55	
    56	        var poles = Poles(query.CommuneIds, query.SegmentId, query.DataSources);
    57	
    58	        // No status row means no sweep ever covered the pole; the map shows it as unknown (drift ST-2).
    59	        var groups = await (
    60	                from p in poles
    61	                join s in db.Set<PoleCurrentStatus>() on p.PoleId equals s.PoleId into statuses
    62	                from s in statuses.DefaultIfEmpty()
    63	                select new
    64	                {
    65	                    p.DataSource,
    66	                    CommuneId = byCommune ? p.CommuneId : null,
    67	                    SegmentId = bySegment ? p.SegmentId : null,
    68	                    Status = (FixtureStatus?)s.FixtureStatus,
    69	                })
    70	            .GroupBy(x => new { x.DataSource, x.CommuneId, x.SegmentId })
    71	            .Select(g => new FixtureStatusRow
    72	            {
    73	                DataSource = g.Key.DataSource,
    74	                CommuneId = g.Key.CommuneId,
    75	                SegmentId = g.Key.SegmentId,
    76	                PoleCount = g.Count(),
    77	                Normal = g.Count(x => x.Status == FixtureStatus.Normal),
    78	                Dim = g.Count(x => x.Status == FixtureStatus.Dim),
    79	                Out = g.Count(x => x.Status == FixtureStatus.Out),
    80	                Unknown = g.Count(x => x.Status == null || x.Status == FixtureStatus.Unknown),
    81	                NeverSurveyed = g.Count(x => x.Status == null),
    82	            })
    83	            .ToListAsync(ct);
    84	
    85	        return new FixtureStatusStatistics
    86	        {
    87	            AsOf = clock.GetUtcNow().UtcDateTime,
    88	            GroupBy = Dimensions(query.GroupBy, withDataSource: true),
    89	            // Few rows, sorted here: data_source is stored as text, so ORDER BY in SQL would be alphabetical.
    90	            Rows = groups
    91	                .OrderBy(r => r.DataSource)
    92	                .ThenBy(r => r.CommuneId?.Length).ThenBy(r => r.CommuneId, StringComparer.Ordinal)
    93	                .ThenBy(r => r.SegmentId?.Length).ThenBy(r => r.SegmentId, StringComparer.Ordinal)
    94	                .ToArray(),
    95	        };
    96	    }
   180	
   181	        var rows =
   182	            from p in Poles(query.CommuneIds, query.SegmentId, query.DataSources)
   183	            join f in db.Set<Fixture>().Where(f => f.RemovedDate == null) on p.PoleId equals f.PoleId into lamps
   184	            from f in lamps.DefaultIfEmpty()
   185	            select new
   186	            {
   187	                p.PoleId, p.SegmentId, p.CommuneId, p.DataSource, p.CreatedAt,
   188	                FixtureId = (string?)f.FixtureId,
   189	                FixtureType = (FixtureType?)f.FixtureType,
   190	                LampWatt = (int?)f.LampWatt,
   191	                LampCount = (int?)f.LampCount,
   192	                InstallDate = (DateOnly?)f.InstallDate,
   193	                Expiry = (DateOnly?)f.WarrantyExpiry,
   194	            };
   195	
   222	            Items = [.. found.Select(r => new WarrantyItem
   223	            {
   224	                PoleId = r.PoleId, SegmentId = r.SegmentId, CommuneId = r.CommuneId, DataSource = r.DataSource,
   225	                FixtureId = r.FixtureId, FixtureType = r.FixtureType, LampWatt = r.LampWatt, LampCount = r.LampCount, InstallDate = r.InstallDate,
   226	                WarrantyExpiry = r.Expiry,
   227	                WarrantyStatus = window.Classify(r.Expiry),
   228	                DaysRemaining = window.DaysRemaining(r.Expiry),
   229	            })],
   230	        };
   231	    }
   232	
   233	    /// <summary>The poles a pole statistic counts. Contract 1.6, as on the map: the calibration rig only when asked for by name.</summary>
   234	    private IQueryable<Pole> Poles(IReadOnlyList<string>? communes, string? segmentId, IReadOnlyList<DataSource>? dataSources)
   235	    {
   236	        var poles = db.Set<Pole>().AsQueryable();
   237	        poles = dataSources is { Count: > 0 } sources
   238	            ? poles.Where(p => sources.Contains(p.DataSource))
   239	            : poles.Where(p => p.DataSource != DataSource.CalibrationRig);
   240	        if (communes is not null) poles = poles.Where(p => communes.Contains(p.CommuneId));
   241	        if (segmentId is not null) poles = poles.Where(p => p.SegmentId == segmentId);
   242	        return poles;
   243	    }
   600	    {
   601	        if ((feederId is null) == (segmentId is null))
   602	        {
   603	            throw Invalid("feeder_id", "Send exactly one of feeder_id or segment_id.");
   604	        }
   605	
   606	        var kind = feederId is not null ? LightingTargetKind.Feeder : LightingTargetKind.Segment;
   607	        string communeId;
   608	        string[] feeders;
   609	        IQueryable<Pole> scope;
   610	        if (feederId is not null)
   611	        {
   612	            communeId = await db.Set<Feeder>().Where(feeder => feeder.FeederId == feederId)
   613	                .Select(feeder => feeder.CommuneId).FirstOrDefaultAsync(ct) ?? throw NotFound("feeder");
   614	            feeders = [feederId];
   615	            scope = db.Set<Pole>().Where(pole => pole.FeederId == feederId);
   616	        }
   617	        else
   618	        {
   619	            communeId = await db.Set<RoadSegment>().Where(segment => segment.SegmentId == segmentId)
   620	                .Select(segment => segment.CommuneId).FirstOrDefaultAsync(ct) ?? throw NotFound("segment");
   621	            scope = db.Set<Pole>().Where(pole => pole.SegmentId == segmentId);
   622	            feeders = [.. (await scope.Where(pole => pole.FeederId != null).Select(pole => pole.FeederId!).Distinct().ToListAsync(ct))
   623	                .OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal)];
   624	        }
   625	
   661	
   662	        var switched = relays.Select(relay => relay.FeederId).ToArray();
   663	        var left = excluded.Select(exclusion => exclusion.FeederId).ToArray();
   664	        var uncontrollable = await scope.CountAsync(pole => pole.FeederId == null || left.Contains(pole.FeederId), ct);
   665	        var affected = (await db.Set<Pole>().Where(pole => pole.FeederId != null && switched.Contains(pole.FeederId))
   666	                .Select(pole => pole.SegmentId).Distinct().ToListAsync(ct))
   667	            .OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal).ToArray();
   668	
   669	        return new Plan(kind, feederId ?? segmentId!, communeId, relays, excluded, affected, uncontrollable);
   670	    }
   671	
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-46-    /// switches poles of other segments (I-14). The UI must say so before the press.
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-47-    /// </summary>
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:48:    public required IReadOnlyList<string> AffectedSegmentIds { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-49-
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-50-    /// <summary>Poles of the target that no command reaches: no feeder, or a feeder that is excluded.</summary>
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:51:    public required int UncontrollablePoleCount { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-52-}
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-53-
--
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-59-
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-60-    [MaxLength(32)]
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:61:    public string? SegmentId { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-62-
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-63-    [Required]
--
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-90-    public required IReadOnlyList<LightingExclusion> Excluded { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-91-
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:92:    public required IReadOnlyList<string> AffectedSegmentIds { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-93-
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs:94:    public required int UncontrollablePoleCount { get; init; }
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-95-}
src/LuxMap.Modules.Telemetry/Lighting/LightingContracts.cs-96-
--
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-33-
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-34-    /// <summary><c>null</c> unless grouped by <c>segment</c>.</summary>
src/LuxMap.Modules.Statistics/StatisticsContracts.cs:35:    public required string? SegmentId { get; init; }
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-36-
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-37-    public required int PoleCount { get; init; }
--
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-102-{
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-103-    public required string PoleId { get; init; }
src/LuxMap.Modules.Statistics/StatisticsContracts.cs:104:    public required string SegmentId { get; init; }
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-105-    public required string CommuneId { get; init; }
src/LuxMap.Modules.Statistics/StatisticsContracts.cs-106-

[exit=0]
```

</details>

<details>
<summary>E13 — lệnh và output thật</summary>

```bash
rg -n 'segment_id|segment_external_ref|PoleListItem|sync/bundle|sync/push' docs/api-contract-v1.1.md; rg -n 'TOPO-INFER|CAB-13|SPEC-NULLABLE|WO-12|SELF-SIGNED|APPROVE' docs/contract-drift.md | head -35; nl -ba docs/contract-drift.md | sed -n '15,20p;1191,1211p'; rg -n 'Q6|Q7|text segment_id|road_segment .*pole' docs/database/erd.md; nl -ba docs/openapi/tools/gen_consolidated_spec.py | sed -n '374,387p;418,423p'
```

```text
15:**v1.13:** 06/10/2026 (BE-43 — đồng bộ offline `/sync/bundle` + `/sync/push`; `performed_at?` cho bước của phiếu) ·
164:| `BLOCKED_BY_EARLIER_OP` | — | Chỉ trong `rejected[]` của `POST /sync/push`: thao tác trước trên cùng phiếu / cột đã thất bại (v1.13, mục 5.8) |
228:  ngoài phạm vi → **404**, không 403 (403 sẽ lộ rằng nó tồn tại). `GET /sync/bundle` chỉ đóng gói
253:| `SyncOffline` | **field_engineer** | `GET /sync/bundle`, `POST /sync/push` (v1.13, mục 5.8) — mỗi thao tác trong hàng chờ **còn** kiểm capability của chính nó |
683:enum), `power_source`, `segment_id`, `commune_id`, `has_open_fault`, `data_source` (mặc định loại
688:pole_id, segment_id, fixture_status, status_confidence (0..1|null), power_source, fixture_type,
698:`pole_id, segment_id, segment_name, commune_id, location{lat,lng}, fixture{fixture_type, power_source,
717:`calibration_rig`). `FeatureCollection` của `LineString`; `properties`: `segment_id, segment_name,
733:| **`GET /api/v1/assets/poles`** | như trên | `PagedResult<PoleListItem>` |
737:| **`POST /api/v1/assets/poles`** | `{external_ref?, segment_id, feeder_id?, commune_id, geom_wkt, near_sensitive_poi?, data_source, note?}` — `note` từ v1.9, ≤ 1000 ký tự | `201` + `Location`; `404 ASSET_NOT_FOUND` tuyến/mạch; `409 CROSS_COMMUNE_REFERENCE` mạch khác xã |
738:| **`PUT /api/v1/assets/poles/{poleId}`** | `{external_ref?, segment_id, feeder_id?, geom_wkt, near_sensitive_poi?, data_source, note?}` — **thay thế toàn phần**: trường vắng bị xoá (vắng `feeder_id` = bỏ mạch), **trừ `note`** (v1.9): vắng = **giữ nguyên**, `null`/rỗng = xoá, chữ = ghi đè | `204`; như `POST` |
771:{ "pole_id": "POLE-0047", "external_ref": "TB-2024-047", "segment_id": "SEG-003",
783:{ "segment_id": "SEG-003", "external_ref": "DX-03", "segment_name": "Đường liên thôn 3",
842:`bbox, status, severity, fault_type, source_channel, data_source, pole_id, segment_id, cluster_id,
847:fault_id, pole_id|null, fixture_id|null, segment_id|null, location{lat,lng},
889:- **`GET /api/v1/work-orders`** — query `wo_status`, `assigned_to`, `segment_id`, phân trang; item theo
890:  `mock-work-orders.json`: `work_order_id, title, segment_id, cluster_id, fault_ids[], wo_status,
906:  Response `{night_of, assigned_to, upcoming_count, groups[]}`; group `{segment_id, segment_name, commune_id, location{lat,lng},
907:  distance_m, work_orders[]}`; mỗi phiếu = item của `GET /work-orders` cộng `segment_ids[]`, `flags[]`
911:  - **Mỗi phiếu xuất hiện đúng một lần**, dưới tuyến đầu của nó: tuyến khảo sát đầu tiên, rồi `segment_id` của phiếu, rồi tuyến
913:    không tuyến) → nhóm `segment_id = null` theo xã. `location` của phiếu: vị trí sự cố đầu tiên (của sự cố, thiếu thì của cột),
922:  - Offline: **không** có trong `/sync/bundle` (mục 5.8) — app gom từ gói theo đúng các luật trên.
956:- **Online-only:** không có sync op mới. WP6 có thể lưu nháp lý do, hiển thị chưa gửi; không đưa vào `/sync/push`.
969:  `mock-iot-nodes.geojson`: `node_id, node_role, node_status, pole_id, segment_id, battery_pct,
971:- **`GET /api/v1/sweeps`** — `sweep_id, started_at, ended_at, segment_ids[], frame_count, coverage_pct,
1030:**`GET /api/v1/sync/bundle`** — `?segment_id=…&segment_id=…` (lặp được, tối đa **20**). Vắng `segment_id` =
1039:  "segment_ids": ["SEG-003"],                 // phạm vi thật đã đóng gói
1049:**`POST /api/v1/sync/push`** — `{ "operations": [ { "client_op_id": "uuid", "op_type": "…", "payload": { … } } ] }`,
1157:`POST /lux-readings`, `POST /sync/push` — trùng trả **200**. 9. Bộ hiệu chuẩn là `RoadSegment` thật.
1185:| ~~O-5~~ | ~~Hình dạng `sync/bundle` / `sync/push`~~ → **hiện thực ở v1.13** (mục 5.8, BE-43), SELF-SIGNED — WP6 xác nhận ở FW kế tiếp | Dylan + WP6 | FW kế tiếp |
1204:| v1.13 | 06/10/2026 | **Mỹ (Dylan)** · `SELF-SIGNED` | **BE-43 — đồng bộ offline** (mục 5.8, drift BE-43; đóng O-5). `GET /sync/bundle`: bản chụp đầy đủ theo tuyến (`segment_id` lặp ≤ 20; vắng = tuyến của phiếu mở của tôi), **không** `since`; cột kèm `note`, phiếu dạng chi tiết. `POST /sync/push`: 5 `op_type` qua service của endpoint gốc, kiểm capability từng thao tác, kết quả `applied` / `conflicts` / `rejected`, `BLOCKED_BY_EARLIER_OP`; khử trùng lặp bằng cột sẵn có hoặc bảng mới `sync_operation`. Capability mới **`SyncOffline`** = `field_engineer`. Mã lỗi mới `NOTE_CHANGED`, `BLOCKED_BY_EARLIER_OP`. **Thêm** `performed_at?` cho `POST /work-orders/{id}/start` (body tuỳ chọn) và `…/complete`. Chỉ thêm. Chạm bề mặt API và ký một mình → **chưa ổn định cho tới FW kế tiếp xác nhận** |
13:> với v1.2; D-R21, D-R24 **đã chốt hướng (SELF-SIGNED)**; D-R20, D-R22, D-R23 **chốt 07/10/2026**; D-R28 = A **chốt 02/10** (BE-15 Phase 1); D-R26 ghi nhận; D-R25, D-R27 **còn chờ quyết**.
17:chính xác; (3) im lặng > 3 ngày làm việc = APPROVE nếu không chạm API, ESCALATE nếu chạm, fallback
18:signer Dylan → `SELF-SIGNED`; (4) không "bàn thêm" vô thời hạn — phải có owner + deadline; (5) ghi ở
59:| **Decision maker** | **Mỹ (Dylan), ủy quyền Claude chốt 28/10** · `SELF-SIGNED`, nền tạm tới FW kế tiếp; WP5/WP6/BE2 chưa xác nhận |
66:Client sinh từ spec coi `control` / `mode_reported_at` nullable (SPEC-NULLABLE, `$ref` trần).
96:| **Decision maker** | **Dylan** — trực tiếp, **không** `SELF-SIGNED` |
106:| D-3 | Xác nhận bốn quyết định `SELF-SIGNED`: A (`pole_id` filter + khuôn ID), C (`work_order_id` null), D (`LPAD` → hàm), 43 (`DELETE` / `PUT feeder` / `ASSET_IN_USE`) |
138:| **Decision maker** | **Mỹ (Dylan)** · `SELF-SIGNED` — mọi mục chạm bề mặt API phải được xác nhận lại ở FW kế tiếp (FW-00 mục 3), tới lúc đó **nền là tạm** |
159:| **D-R17** | Contract **v1.7**, BREAKING. Giữ tên file spec `luxmap-v1.5.json`; spec sinh lại từ code, không sửa tay. Người ký: Mỹ, `SELF-SIGNED` (CODEOWNERS đã bỏ từ 14/09) | — |
178:| **Decision** | Nguồn chuẩn cho scope / actor / FR / NFR / deliverable chuyển sang **Phiếu FA26SE222 v1.4** (`docs/registration/FA26SE222_v1.4.md`); `FA26SE222_v1.2.md` thành lịch sử. **Chỉ tầng tài liệu** — D-R20 … D-R28 bên dưới: D-R21, D-R24 **chốt hướng (SELF-SIGNED)**, còn lại **đề xuất, CHƯA CHỐT**; chưa đổi Contract, chưa đổi code |
202:| **D-R21** | **Mô hình dữ liệu một phiên khảo sát — hướng (B), chốt 27/09.** App quay riêng (WP6) sinh **bốn file thô, không ai nhập tay**: (1) **video**; (2) **log lux** — module ESP32 + BH1750 gửi **từng mẫu** qua **BLE notify** (`seq`, `module_ms`, `lux`), app gắn `phone_elapsed_ns` lúc nhận; (3) **GPS track** theo thời gian (không phải một toạ độ cho cả file); (4) **cấu hình quay** thực tế. **Cả ba luồng video / GPS / lux dùng CHUNG một đồng hồ** `SystemClock.elapsedRealtimeNanos()` — Camera2 gắn `SENSOR_TIMESTAMP` cùng hệ thời gian khi `SENSOR_INFO_TIMESTAMP_SOURCE = REALTIME` (**phải kiểm trên máy quay thật**); quy đổi UTC **một lần** mỗi phiên, nên **không cần mốc flash**. Jitter BLE ~10–50 ms (≈0,4 m ở 30 km/h) — server khớp tuyến tính `(module_ms, phone_elapsed_ns)` trên cả phiên để khử. **Điện thoại không xử lý gì**: server lưu nguyên byte bốn file, parse log lux thành bảng mẫu theo sweep (không gắn cột), tìm đỉnh, ghép cột bằng GPS track + vị trí cột GIS, lấy ON/OFF của CV, rồi ghi **một dòng cho mỗi cột mỗi sweep** (`peak_at, peak_lux, cv_state, baseline_ratio, classified_as, association_confidence`) vào chuỗi luminance. Khoảng `seq` bị hổng trùng lúc ngang cột → `unknown`, không đoán. `LuxReading` (BE-42) **giữ** cho số đo **thủ công** lúc kiểm tra thực địa — ứng viên ground truth của D-R23. Người quyết: **Mỹ (Dylan)** · `SELF-SIGNED` | **Có** — BE-15 mới, §5.7 diễn đạt lại | **Chốt hướng, SELF-SIGNED** — nền tạm tới FW kế tiếp; **đặc tả BE-15 chưa viết** |
205:| **D-R24** | **Cấu hình quay CỐ ĐỊNH, kiểm ở cấp PHIÊN thay cho kiểm từng JPEG (BE-16).** Camera2 (hoặc CameraX + Camera2Interop): tắt AE, cố định ISO + shutter; lấy nét cố định vô cực; WB cố định; độ phân giải + fps cố định; **tắt EIS / HDR / night mode** (crop, bóp méo, ghép frame). App ghi cấu hình **thực tế** vào file cấu hình phiên; server so với **một profile đã chốt** và **từ chối** phiên lệch, kèm lý do — cùng tinh thần "không nhận, không suy đoán" của BE-16. Con số ISO / shutter chốt sau buổi quay thử. Metadata phiên thêm: làn, chiều đi, tần số lấy mẫu module, phiên bản firmware module (BE-34). Người quyết: **Mỹ (Dylan)** · `SELF-SIGNED` | **Có** — BE-15/16 chưa đặc tả | **Chốt hướng, SELF-SIGNED**; chờ profile từ buổi quay thử |
217:- **Người ký:** D-R21, D-R24 đã SELF-SIGNED (Dylan, 27/09) → xác nhận ở FW kế tiếp; D-R20, D-R22, D-R28 chạm bề mặt API →
440:⚠️ **`SELF-SIGNED`** — ký một mình, không qua Thịnh/Ngọc. Chạm bề mặt API nên **chưa ổn định cho tới
492:| WO-12 | **Thêm** `GET /work-orders/{id}/poles` (`ReadWorkOrders`, phân trang): khi giao việc, kỹ sư thấy **các cột trên đoạn được giao cùng trạng thái đèn lần khảo sát đã duyệt gần nhất — TRƯỚC khi đi**, không phải sau khi khảo sát xong. Cột = cột trên các segment của phiếu (`segment_ids`, hoặc `segment_id` của inspection/repair) **hợp** cột mang fault của phiếu. Mỗi item: `pole_id, segment_id, position` (thứ tự dọc đường, 1-based, tính lại cho từng segment; segment theo thứ tự trên phiếu), `location{lat,lng}`, `fixture_status` (`unknown` khi chưa có sweep nào công bố — `status_confidence`, `last_seen_at` khi đó `null`), `open_fault_count`, `near_sensitive_poi`, `fixture_type`, `lamp_watt` (bóng đang dùng, null nếu không có), `work_order_fault_ids[]` (fault của chính phiếu trên cột). Quyền xem = quyền xem phiếu: phiếu ngoài phạm vi / không phải người được giao ⇒ 404 `WORK_ORDER_NOT_FOUND`; cột của xã ngoài phạm vi trên đường liên xã **không** hiện. Trạng thái chỉ đổi khi Quản lý chấp nhận một sweep (BE-15 P2c). `position` là thứ tự, không phải khoảng cách. **Đã báo WP5 và WP6 ngày 04/10/2026** (Mỹ gửi; PR #85 đã merge); chưa có trong `GET /sync/bundle` (BE-43) | Có (endpoint mới, SELF-SIGNED) |
559:| **Decision maker** | **Mỹ chốt 29/09/2026** cho FR-1, FR-2, FR-2a (C), FR-3, FR-5; FR-4 **chờ quyết**. Chạm API, chưa qua FW → `SELF-SIGNED` |
637:| **Decision maker** | **Mỹ chốt 29/09/2026** — chọn đúng các đề xuất D-1…D-9 ở `.ai/results/BE-40-p1.md`. Chưa qua FW → `SELF-SIGNED` |
662:| **Decision maker** | Mỹ chốt 29/09/2026 cho P-2 và P-3 (`SELF-SIGNED`). P-1 và cả P-3 chạm việc của WP4/WP5/WP6 → **vẫn nêu ở FW kế tiếp** |
677:| **P-3** | Thứ tự mặc định `GET /faults` đổi từ `-priority_score` sang **`-severity`** (hạng `critical > high > medium > low`), cùng mức thì **sự cố cũ trước** (`detected_at` tăng), rồi `created_at, length(fault_id), fault_id`. `sort` nhận thêm `severity` / `-severity`; `-priority_score` vẫn dùng được. **Mỹ chốt 29/09/2026, ĐÃ HIỆN THỰC** — `SELF-SIGNED`, nền tạm: vẫn phải nêu ở FW vì chạm việc của Thịnh, Khang, FM-25. Danh sách **phiếu công việc** giữ nguyên (`created_at` giảm dần) — chưa đổi. ⚠️ `mock-faults.json` đang xếp theo `priority_score`, thứ tự API mặc định nay khác mock | **Có** (§5.4) |
689:| **Decision maker** | **Mỹ chốt 29/09/2026** — theo đề xuất D-1…D-10 ở `.ai/results/BE-19-p1.md`, **đã chỉnh sau review độc lập của Codex CLI** (xem mục "Chốt sau review" trong file đó). Chưa qua FW → `SELF-SIGNED` |
747:**SELF-SIGNED, nền tạm tới FW** (quyết định của Mỹ cùng ngày: thêm `observation`, thêm đường xem ảnh, bắt buộc ảnh `after`).
775:| **Decision maker** | Mỹ (FX-1) + Claude theo uỷ quyền (còn lại), 01/10/2026 · `SELF-SIGNED` — mục chạm API **ESCALATE ở FW kế tiếp** |
796:| **Decision maker** | Mỹ chốt 02/10: backend chạy model, duyệt ffmpeg, backend làm ghép cột. Còn lại Claude theo uỷ quyền sau khi đối chiếu Codex · `SELF-SIGNED` |
804:**Sửa D-05 (02/10/2026, Mỹ chốt, SELF-SIGNED):** đèn CV thấy **ON** mà cột **chưa có baseline** → `fixture_status = normal`,
811:Nền SELF-SIGNED ở mục trên giữ nguyên; chi tiết payload tạm tại
824:**SELF-SIGNED, nền tạm tới FW**, theo task P2c/D-03/05/08/10/12/14 đã duyệt; không sửa Contract hay enum.
857:tay cho SurveyResultItem trong generator. Nền SELF-SIGNED giữ nguyên.
870:| **Decision** | Hiện thực Contract §5.4 (`POST /faults`) kèm luồng ảnh EV-2. Lệch Contract ở các chỗ dưới đây. **SELF-SIGNED, nền tạm tới FW** |
896:| **Decision** | Hiện thực màn chi tiết cột theo Contract §5.1 + `mock-pole-detail.json`, **lệch ở chín chỗ dưới đây** vì nền đã đổi (IoT ở tủ điện, đo sáng bằng lux, baseline theo chiều). **SELF-SIGNED, nền tạm tới FW**; không sửa văn bản Contract hay enum |
940:| **Quyết định** | Mỹ yêu cầu, **SELF-SIGNED** — chạm bề mặt API nên chưa ổn định tới FW kế tiếp. Contract text chưa sửa; sửa khi lên version |
958:| **Decision** | Hiện thực D-R11: Quản trị hệ thống tạo tài khoản **không mật khẩu**, hệ thống **gửi email mời** chứa link đặt mật khẩu; gỡ `POST /auth/register`. Đã viết vào **Contract v1.8** (§4.8, §4.9, bảng lỗi, ma trận §2). **SELF-SIGNED, nền tạm tới FW** |
988:| **Decision** | Thêm một ghi chú tự do cho **mỗi cột** (vị trí), để kỹ sư ghi nơi nhạy cảm gần đó hay lưu ý khác. Đã viết vào **Contract v1.9** (mục 2, 5.3, 5.3.1). **SELF-SIGNED, nền tạm tới FW** |
990:| **Scope** | Contract §2 (capability `EditPoleNotes`), §5.3 (endpoint), §5.3.1 (trường `note`); drift BE-20 (`GET /map/poles/{pole_id}` thêm khoá `note`) và WO-12 (`GET /work-orders/{id}/poles` thêm `note`) |
1009:| **Decision** | Bảng `notification` + 4 endpoint `/notifications` đọc bằng **polling**; 11 `type`. Đã viết vào **Contract v1.10** (mục 1.2, 2, 5.9). **SELF-SIGNED, nền tạm tới FW** |
1020:| **N-6** | Mở sự cố | ~~Không có `GET /faults/{id}`~~ → **ĐÓNG 06/10/2026**: thêm `GET /faults/{fault_id}` (Contract v1.11 §5.4), cùng hình dạng item danh sách, ngoài phạm vi = 404 `FAULT_NOT_FOUND`. SELF-SIGNED, báo WP5/WP6 cùng tin BE-27 | **Có** (endpoint mới) |
    15	**Nguyên tắc vận hành quyết định (FW-00, 07/09/2026)** giữ nguyên hiệu lực — năm mục ở đầu file
    16	archive: (1) ghi ngay, bốn trường Decision / Decision maker / Date / Scope; (2) policy là một vai trò
    17	chính xác; (3) im lặng > 3 ngày làm việc = APPROVE nếu không chạm API, ESCALATE nếu chạm, fallback
    18	signer Dylan → `SELF-SIGNED`; (4) không "bàn thêm" vô thời hạn — phải có owner + deadline; (5) ghi ở
    19	đúng tầng (deviation → file này; luật liên ticket → Contract; ràng buộc kỹ thuật → `CLAUDE.md`; tiến độ
    20	→ `tracking.html`).
  1191	## TOPO-INFER — nhãn nguồn gốc cho topology + sơ đồ nhánh của trụ (07/10/2026)
  1192	
  1193	| | |
  1194	|---|---|
  1195	| **Decision** | Topology ngoài thực địa được **suy luận công khai, có gắn nhãn** — không giấu, không đoán ngầm. Mỗi quan hệ cột → mạch và mạch → trụ mang `verified \| inferred`; bản đồ vẽ sơ đồ nhánh từ dữ liệu backend; nhãn hiện ở tooltip (TI-7). Nền: phương án A của FEEDER-SCOPE |
  1196	| **Decision maker** | **Mỹ (Dylan)**, 07/10/2026 — đồng ý D-1…D-10 của `.ai/results/TOPO-INFER-p1.md` (đã qua review Codex, 8/8 phát hiện áp) |
  1197	| **Chạm API** | **Có.** SELF-SIGNED, nền tạm tới FW; chưa lên Contract |
  1198	
  1199	| Mã | Quyết định | Chạm API |
  1200	|---|---|---|
  1201	| **TI-1** | Enum mới **`topology_source : verified \| inferred`**. `pole.feeder_source` (cặp với `feeder_id`) và `feeder.cabinet_source` (cặp với `cabinet_id`); CHECK: nhãn null ⇔ quan hệ null. Backfill mọi quan hệ đang có → `inferred` | Có (enum mới) |
  1202	| **TI-2** | Luật ghi: nhãn vắng + quan hệ không đổi → **giữ**; quan hệ đổi / gắn mới → **`inferred`**; tháo quan hệ → xoá nhãn (gửi kèm `null` cũng được); `null` **khi quan hệ còn** hay sai kiểu → **400**; nhãn có giá trị kèm quan hệ null → **400**. Không đường nào tự nhận `verified`. Ghi **theo cặp** (cả hai cột trong cùng UPDATE) | Có |
  1203	| **TI-3** | `feeder_source` trên `POST/PUT /assets/poles`, `PUT /assets/poles/{id}/feeder`, import cột; `cabinet_source` trên `POST/PUT /assets/feeders`, import feeder. Đọc: `PoleListItem`, `TopologyPole` (+`feeder_source`), `FeederListItem.cabinet` (+`cabinet_source`). **Không** thêm vào `/map/poles` | Có |
  1204	| **TI-4** | `GET /api/v1/map/cabinets/{cabinetId}/topology` — `FeatureCollection` của `LineString`, **mỗi feature một cạnh**: `feeder_id, segment_id, branch, order, from_id, to_pole_id, feeder_source`. Nhánh theo (mạch, tuyến) và hai phía trụ, thứ tự dọc đường kiểu WO-12; cạnh đầu lấy nhãn thấp hơn của hai quan hệ. Không phát khoảng cách | Có (endpoint mới) |
  1205	| **TI-5** | Quản lý (`ManageAssets`) đặt nhãn; không lưu "ai xác minh, lúc nào" (`updated_by` không phải dấu xác minh) | Không |
  1206	| **TI-6** | Chặn trộn cột `field` với trụ không-`field` (D-7) và script đề xuất quan hệ suy luận (D-8): **ticket riêng** | — |
  1207	| **TI-7** | **Hiển thị (Mỹ, 08/10/2026):** mọi cạnh vẽ **cùng một kiểu nét** — không phân biệt nét đứt / nét liền. Nhãn `feeder_source` **giữ trong dữ liệu** và FE hiện ở **tooltip** của cạnh (*"Quan hệ suy luận từ khảo sát, chưa xác minh"* / *"Đã xác minh"*), kèm dòng chú giải *"Sơ đồ logic, không phải tuyến cáp"*. Backend không đổi | Không (chỉ cách FE trình bày) |
  1208	
  1209	**Phải báo:** WP5 (vẽ từ `/map/cabinets/{id}/topology`, bỏ chia đều trong `useElectricalCascade`; nhãn trên form cột / mạch). **Chưa báo.**
  1210	
  1211	## LIGHT-CTRL — điều khiển ON / OFF / AUTO trên testbed + đăng ký thiết bị (08/10/2026)
29:| Q6 | Phiếu khảo sát = `work_order.task_kind = 'survey'` + **`work_order_segment` có thứ tự**; một phiếu nhiều sweep (khảo sát lại) | FR-1, BE-15 |
30:| Q7 | **Tuyến liên xã:** phiếu neo vào **một xã**, nhưng được chứa tuyến/cột của các xã khác **trong phạm vi của người tạo**; kết quả theo cột mang **xã của cột**. Cột ngoài phạm vi → không xử lý, báo rõ trong độ phủ. **ACCT-1 (29/10): tài khoản thường chỉ một xã, nên phạm vi cho phép chỉ còn xã của người giao; giữ mô hình/logic Q7 cho dữ liệu cũ. Giới hạn hiện thực: processor có thể từ chối cả job `JOB_SCOPE_INVALID` khi tuyến có cột cùng nguồn xã khác, xem ACCT-1** | Dữ liệu thật 28/09: Nguyễn Xiển & Phước Thiện chạy qua cả Long Phước và Long Bình. Claude quyết (Codex nghiêng "một phiếu một xã" — chia tuyến thực tế sẽ cắt ngang các đoạn đường) |
86:        text segment_id PK "HIỆN CÓ"
94:        text segment_id FK
131:        text work_order_id PK "MỚI (Q6)"
133:        text segment_id FK
176:        text segment_id FK
199:        text commune_id FK "xã của cột (Q7)"
300:    road_segment ||--o{ pole : ""
   374	                              "lng": {"type": "number", "format": "double", "minimum": -180, "maximum": 180}}}
   375	
   376	S["PoleProperties"] = {
   377	    "type": "object", "additionalProperties": False,
   378	    "description": "Contract §5.1 — 15 thuộc tính phẳng + warranty_status (BE-31 drift W-1) + lamp_count (LAMP-COUNT LMC-3). Không có data_source / external_ref / feeder_id (không emit).",
   379	    "required": ["pole_id", "segment_id", "fixture_status", "power_source", "fixture_type", "lamp_watt", "lamp_count",
   380	                 "install_date", "warranty_status", "commune_id", "open_fault_count", "has_iot_node", "near_sensitive_poi"],
   381	    "properties": OrderedDict([
   382	        ("pole_id", {"$ref": "#/components/schemas/PoleId"}),
   383	        ("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
   384	        ("fixture_status", {"$ref": "#/components/schemas/FixtureStatus"}),
   385	        ("status_confidence", {"type": "number", "format": "double", "minimum": 0, "maximum": 1, "nullable": True,
   386	                               "description": "null khi unknown hoặc kết luận người sửa không có score AI (PHOTO-REVIEW PP-14); trạng thái normal/dim giữ lại có thể giữ score cũ"}),
   387	        ("power_source", {"$ref": "#/components/schemas/PowerSource"}),
   418	    ])}
   419	# BE-43: PointGeometry / LineStringGeometry / PoleFeature(Collection) / SegmentFeature(Collection) were only
   420	# referenced by the hand-written SyncBundle stub; with it gone they were orphans. The map layers are exported
   421	# from the code; PoleProperties / SegmentProperties above still override those exported schemas.
   422	
   423	# BE-20: PoleDetail and its parts (PoleDetailFixture/Status/Baseline/HistoryPoint/OpenFault/Frame) now come

[exit=0]
```

</details>

<details>
<summary>E14 — lệnh và output thật</summary>

```bash
rg -n 'public (async )?Task|public void' tests/LuxMap.Api.Tests/{AssetImportTests,AssetReadShapeTests,AssetReplaceAndDeleteTests,StatisticsTests,SyncTests,PoleLocationTests,TopologyQueryTests,WorkOrderTests}.cs; rg -n 'segment_id|SegmentId|Required' tests/LuxMap.Api.Tests/AssetPermissionTests.cs tests/LuxMap.Api.Tests/AssetSchemaTests.cs; rg -n 'AssetDatabaseCollection|phá|cp|mv|NoDatabase|LuxMapTestHost|mã thoát|sequence' docs/agents/traps/testing.md
```

```text
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:37:    public async Task A_circuit_reports_the_poles_wired_to_it_and_nobody_elses()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:67:    public async Task Each_pole_carries_its_own_coordinates_in_4326()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:90:    public async Task A_circuit_in_another_commune_is_not_found_rather_than_forbidden()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:112:    public async Task A_shared_road_lists_only_poles_of_the_callers_commune()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:140:    public async Task A_pole_already_on_a_circuit_is_not_listed_as_unassigned()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:162:    public async Task The_unassigned_listing_refuses_to_answer_without_the_flag()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:192:    public async Task A_manager_may_read_the_topology()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:222:    public async Task The_listing_pages_without_repeating_or_dropping_a_pole()
tests/LuxMap.Api.Tests/TopologyQueryTests.cs:260:    public async Task The_item_carries_only_the_topology_fields_and_none_of_the_inventory_ones()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:37:    public async Task InitializeAsync()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:79:    public async Task DisposeAsync()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:124:    public async Task An_unknown_enum_name_is_a_400_that_names_no_internal_type()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:137:    public async Task Numeric_task_kind_is_a_validation_error_at_json_binding(string value)
tests/LuxMap.Api.Tests/WorkOrderTests.cs:193:    public async Task Transition_http_literal_six_by_nine_table()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:229:    public async Task Actor_and_commune_filters_are_combined_per_request_and_metadata_is_closed()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:255:    public async Task Eligibility_scope_and_server_owned_fields()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:273:    public async Task Assignee_eligibility_uses_database_and_has_identical_error_details()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:312:    public async Task Create_rejects_ineligible_assignee_without_order_or_audit(string reason)
tests/LuxMap.Api.Tests/WorkOrderTests.cs:349:    public async Task Repair_propagation_and_inspection_outcomes_and_release()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:381:    public async Task Patch_null_noop_audit_content_and_reassignment()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:416:    public async Task Materials_plan_and_materials_used_are_kept_apart()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:456:    public async Task The_table_refuses_blank_materials_text(string column, string constraint)
tests/LuxMap.Api.Tests/WorkOrderTests.cs:467:    public async Task A_verified_inspection_confirms_present_faults_and_follows_up_into_a_repair_on_one_case()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:511:    public async Task Follow_up_is_refused_until_there_is_a_verified_parent_with_something_to_carry()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:535:    public async Task The_table_refuses_a_half_linked_chain_and_a_parent_from_another_commune()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:567:    public async Task Concurrent_creates_on_one_fault_serialize_and_the_loser_leaves_nothing()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:585:    public async Task Creation_waits_for_the_fault_lock_and_rereads_the_status_it_validates()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:607:    public async Task Schedule_api_database_and_inclusive_filter()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:623:    public async Task Listing_orders_numeric_ids_and_parses_snake_case()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:645:    public async Task Derived_fields_use_highest_priority_and_live_max()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:658:    public async Task Audit_required_on_real_work_order_and_link_and_backdoor_is_explicit()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:682:    public async Task Partial_unique_index_rejects_active_duplicates_but_preserves_released_history()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:701:    public async Task Concurrency_verify_return_has_one_success_one_conflict_and_one_audit()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:718:    public async Task Fault_xmin_rejects_a_stale_write()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:771:    public async Task An_orders_poles_come_in_road_order_with_the_status_the_last_survey_left_before_any_visit()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:816:    public async Task A_survey_order_lists_its_segments_in_order_then_along_each_road()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:841:    public async Task A_repair_created_from_faults_lists_the_poles_carrying_them_with_the_fault_ids()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:868:    public async Task Road_order_survives_a_road_owned_by_a_commune_outside_the_callers_scope()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:884:    public async Task Paging_keeps_road_positions_and_the_total()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:898:    public async Task A_caller_sees_only_poles_of_their_own_communes_on_a_shared_road()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:913:    public async Task An_order_the_caller_cannot_see_is_404_exactly_like_one_that_does_not_exist()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:982:    public async Task A_repair_needs_an_after_photo_before_it_can_be_completed()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:996:    public async Task Photos_are_listed_in_capture_order_and_served_through_the_api_byte_for_byte()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1023:    public async Task Labels_follow_the_kind_of_order()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1037:    public async Task Only_the_assigned_engineer_uploads_and_only_while_the_order_is_in_progress()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1052:    public async Task Another_engineer_cannot_list_or_open_the_photos()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1066:    public async Task A_file_that_is_not_a_jpeg_is_refused_by_its_bytes_and_nothing_is_written(string what)
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1086:    public async Task Malformed_fields_are_400_before_anything_is_stored(string field, string value)
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1103:    public async Task An_order_changed_while_the_photo_uploads_is_checked_again_before_the_row_is_saved(string change)
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1124:    public async Task An_overlapping_retry_gets_the_photo_back_even_after_the_order_was_completed()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1153:    public async Task A_retry_with_the_same_client_op_id_returns_the_same_photo()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1211:    public async Task Tonights_agenda_lists_the_engineers_open_orders_once_with_why_and_only_counts_later_nights()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1253:    public async Task A_survey_appears_once_under_its_first_road_and_near_puts_the_nearest_road_first()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1278:    public async Task An_engineer_gets_only_their_own_agenda_and_everyone_else_must_name_an_engineer_of_their_communes()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1303:    public async Task Without_night_of_the_agenda_is_for_the_current_night_in_the_communes_time_zone()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1314:    public async Task An_order_on_a_fault_with_no_pole_and_no_road_is_grouped_by_commune_at_the_faults_spot()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1333:    public async Task A_road_of_a_commune_outside_the_callers_scope_lends_it_no_name_and_no_place()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1352:    public async Task A_date_that_is_not_yyyy_mm_dd_is_a_400_never_another_day()
tests/LuxMap.Api.Tests/WorkOrderTests.cs:1365:    public async Task A_position_that_is_not_lat_comma_lng_on_the_globe_is_a_400()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:38:    public async Task A_pole_row_carries_the_inventory_fields_and_none_of_the_operational_ones()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:73:    public async Task Every_inventory_row_names_its_commune_in_the_list_and_the_detail()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:100:    public async Task The_inventory_code_is_readable_on_all_three_resources()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:129:    public async Task Provenance_is_readable_where_it_exists_and_absent_where_it_does_not()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:152:    public async Task The_circuit_a_full_replacement_would_overwrite_is_readable()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:178:    public async Task The_lamp_in_service_travels_with_the_row_and_a_retired_one_does_not()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:198:    public async Task A_pole_with_no_lamp_reports_a_null_active_fixture_rather_than_an_empty_object()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:215:    public async Task A_pole_row_carries_a_plain_location_and_not_a_geojson_geometry()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:233:    public async Task A_segment_row_carries_its_declared_length_and_a_pole_count()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:257:    public async Task A_segments_pole_count_stops_at_the_edge_of_the_callers_scope()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:271:    public async Task A_feeder_row_reports_whether_a_cable_route_was_surveyed()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:286:    public async Task Reading_one_pole_adds_its_geometry_its_segment_name_and_when_it_was_created()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:309:    public async Task The_geometry_a_detail_read_returns_is_accepted_straight_back_by_a_replacement()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:328:    public async Task A_feeder_with_no_surveyed_route_reports_a_null_geometry_not_an_empty_string()
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:342:    public async Task An_asset_in_another_commune_is_not_found_rather_than_forbidden(string resource)
tests/LuxMap.Api.Tests/AssetReadShapeTests.cs:377:    public async Task The_literal_unassigned_route_still_wins_over_the_feeder_detail_route()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:33:    public async Task A_field_engineer_moves_a_pole_far_along_its_road_and_the_move_is_audited()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:64:    public async Task A_manager_may_move_it_off_the_road_inside_the_corridor()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:77:    public async Task A_position_beyond_the_survey_corridor_is_409_and_writes_nothing()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:98:    public async Task The_corridor_is_measured_from_a_segment_owned_by_another_commune()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:112:    public async Task Sending_the_position_it_already_has_changes_nothing()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:126:    public async Task A_missing_or_out_of_range_coordinate_is_400()
tests/LuxMap.Api.Tests/PoleLocationTests.cs:142:    public async Task A_pole_outside_the_callers_commune_is_404()
tests/LuxMap.Api.Tests/SyncTests.cs:39:    public async Task InitializeAsync()
tests/LuxMap.Api.Tests/SyncTests.cs:90:    public async Task DisposeAsync()
tests/LuxMap.Api.Tests/SyncTests.cs:119:    public async Task Without_segments_the_bundle_packs_the_roads_of_my_open_orders_and_only_my_orders()
tests/LuxMap.Api.Tests/SyncTests.cs:146:    public async Task Named_segments_come_back_in_the_order_asked_and_a_foreign_one_is_404()
tests/LuxMap.Api.Tests/SyncTests.cs:166:    public async Task Every_operation_kind_applies_once_and_a_resent_batch_is_a_replay()
tests/LuxMap.Api.Tests/SyncTests.cs:202:    public async Task A_conflict_carries_the_server_state_and_blocks_later_steps_on_the_same_order_only()
tests/LuxMap.Api.Tests/SyncTests.cs:223:    public async Task A_note_changed_since_the_engineer_saw_it_is_a_conflict_and_is_kept()
tests/LuxMap.Api.Tests/SyncTests.cs:240:    public async Task A_bad_operation_is_rejected_on_its_own_and_a_bad_envelope_is_400()
tests/LuxMap.Api.Tests/SyncTests.cs:264:    public async Task A_client_op_id_reused_for_a_different_operation_is_rejected_and_writes_nothing()
tests/LuxMap.Api.Tests/SyncTests.cs:291:    public async Task A_completion_is_never_stored_before_its_start()
tests/LuxMap.Api.Tests/SyncTests.cs:305:    public async Task A_step_dated_before_the_order_was_assigned_or_in_the_future_is_rejected()
tests/LuxMap.Api.Tests/SyncTests.cs:324:    public async Task Each_operation_is_checked_against_its_own_capability_not_only_the_door()
tests/LuxMap.Api.Tests/SyncTests.cs:347:    public async Task Only_a_field_engineer_may_sync()
tests/LuxMap.Api.Tests/SyncTests.cs:359:        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
tests/LuxMap.Api.Tests/SyncTests.cs:362:        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:42:    public async Task Replacing_a_segment_overwrites_every_writable_field()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:78:    public async Task The_declared_length_survives_a_geometry_that_disagrees_with_it()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:106:    public async Task A_segment_may_keep_the_inventory_code_it_already_holds()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:126:    public async Task A_segment_may_not_take_an_inventory_code_another_segment_in_the_commune_holds()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:152:    public async Task Replacing_a_segment_in_another_commune_is_404_rather_than_403()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:173:    public async Task Deleting_a_segment_nothing_references_succeeds()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:185:    public async Task A_segment_that_still_carries_a_pole_cannot_be_deleted()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:203:    public async Task Deleting_a_segment_that_does_not_exist_is_404()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:216:    public async Task Replacing_a_feeder_overwrites_its_name_and_cable_route()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:244:    public async Task Replacing_a_feeder_without_a_geometry_clears_the_cable_route()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:267:    public async Task A_feeder_with_poles_wired_to_it_cannot_be_deleted_and_they_stay_wired()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:284:    public async Task Deleting_a_feeder_nothing_is_wired_to_succeeds()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:307:    public async Task Replacing_a_pole_without_a_feeder_id_clears_its_circuit()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:341:    public async Task A_pole_may_not_be_moved_onto_a_feeder_in_another_commune()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:374:    public async Task A_pole_may_sit_on_a_segment_owned_by_another_commune()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:398:    public async Task A_pole_may_not_be_moved_onto_a_segment_the_caller_cannot_see()
tests/LuxMap.Api.Tests/AssetReplaceAndDeleteTests.cs:434:    public async Task A_system_admin_may_not_replace_or_delete_an_asset(string method, string route)
tests/LuxMap.Api.Tests/StatisticsTests.cs:33:    public async Task InitializeAsync()
tests/LuxMap.Api.Tests/StatisticsTests.cs:74:    public async Task DisposeAsync()
tests/LuxMap.Api.Tests/StatisticsTests.cs:94:    public async Task Every_status_is_counted_and_never_surveyed_is_part_of_unknown_not_a_fifth_bucket()
tests/LuxMap.Api.Tests/StatisticsTests.cs:115:    public async Task The_calibration_rig_is_left_out_unless_asked_for_by_name_as_on_the_map()
tests/LuxMap.Api.Tests/StatisticsTests.cs:128:    public async Task Grouping_by_commune_and_segment_gives_one_row_per_group_in_id_order()
tests/LuxMap.Api.Tests/StatisticsTests.cs:152:    public async Task A_manager_counts_only_their_communes_and_naming_another_one_is_403()
tests/LuxMap.Api.Tests/StatisticsTests.cs:176:    public async Task A_malformed_query_is_a_400(string query)
tests/LuxMap.Api.Tests/StatisticsTests.cs:190:    public async Task On_time_is_decided_by_the_night_of_completion_not_the_calendar_date()
tests/LuxMap.Api.Tests/StatisticsTests.cs:228:    public async Task A_repair_due_tonight_is_not_overdue_and_one_due_last_night_is()
tests/LuxMap.Api.Tests/StatisticsTests.cs:242:    public async Task Open_repairs_past_their_due_night_are_overdue_and_the_rate_is_null_with_nothing_due()
tests/LuxMap.Api.Tests/StatisticsTests.cs:264:    public async Task Without_bounds_the_window_is_the_last_thirty_nights_ending_tonight()
tests/LuxMap.Api.Tests/AssetImportTests.cs:27:    public async Task A_reference_that_matches_nothing_is_caught_in_validation_and_nothing_is_written()
tests/LuxMap.Api.Tests/AssetImportTests.cs:49:    public async Task Loading_the_same_file_twice_updates_instead_of_creating_a_second_copy()
tests/LuxMap.Api.Tests/AssetImportTests.cs:78:    public async Task A_second_fixtures_file_is_refused_per_row_rather_than_doubling_the_equipment_history()
tests/LuxMap.Api.Tests/AssetImportTests.cs:101:    public async Task Two_rows_in_ONE_file_claiming_the_same_pole_are_caught_before_the_write()
tests/LuxMap.Api.Tests/AssetImportTests.cs:119:    public async Task A_row_naming_a_commune_outside_the_scope_fails_that_row_and_leaves_the_rest_written()
tests/LuxMap.Api.Tests/AssetImportTests.cs:147:    public async Task More_than_a_hundred_errors_are_truncated_but_the_total_is_still_reported_in_full()
tests/LuxMap.Api.Tests/AssetImportTests.cs:170:    public async Task A_file_whose_header_is_missing_a_column_reports_it_once_against_line_one()
tests/LuxMap.Api.Tests/AssetImportTests.cs:184:    public async Task An_unsupported_file_extension_is_refused_with_415_before_anything_is_parsed()
tests/LuxMap.Api.Tests/AssetImportTests.cs:199:    public async Task An_unknown_import_kind_is_a_400_naming_the_four_that_exist()
tests/LuxMap.Api.Tests/AssetImportTests.cs:214:    public async Task A_semicolon_file_with_a_utf8_BOM_and_CRLF_still_imports()
tests/LuxMap.Api.Tests/AssetImportTests.cs:243:    public async Task A_row_in_an_unreachable_commune_does_not_block_reusing_its_inventory_code()
tests/LuxMap.Api.Tests/AssetImportTests.cs:310:    public async Task The_upsert_key_is_the_COMPOSITE_when_both_communes_are_inside_the_scope()
tests/LuxMap.Api.Tests/AssetImportTests.cs:367:    public async Task A_pole_whose_feeder_sits_in_another_commune_is_refused_per_row_and_the_same_commune_goes_through()
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:171:        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:189:                return segment.SegmentId;
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:195:            SegmentId = segmentId,
tests/LuxMap.Api.Tests/AssetPermissionTests.cs:207:            .CountAsync(pole => pole.SegmentId == segmentId));
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:21:    public async Task Pole_id_and_segment_id_are_generated_by_the_database_in_contract_format()
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:24:        Assert.StartsWith("SEG-", fixture.SegmentId, StringComparison.Ordinal);
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:36:        Assert.Matches(@"^SEG-\d{3,}$", fixture.SegmentId);
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:48:                SegmentId = fixture.SegmentId,
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:100:            return segment.SegmentId;
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:105:            .Where(segment => segment.SegmentId == segmentId)
tests/LuxMap.Api.Tests/AssetSchemaTests.cs:350:                SegmentId = fixture.SegmentId,
3:Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi viết / sửa test, fixture, teardown, sequence, test host. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.
5:## Quy ước: MỌI test ghi tài sản nằm trong `AssetDatabaseCollection`, và KHÔNG test nào ghim ID bằng literal
9:**1. Một collection cho mọi class ghi tài sản.** xUnit tuần tự các class trong `AssetDatabaseCollection`.
10:`PrefixedIdOverflowTests` lùi sequence để chạm ngưỡng độ rộng; người ghi song song có thể cướp ID hoặc nhận ID đã có,
13:**2. Chọn dải từ bảng LIVE, không ghim literal ID.** `AssetSchemaFixture` chèn từ chỗ sequence đang đứng, không từ 1.
14:Dùng `FreeFourDigitDecadeAsync`, assert tiền đề bằng `RequireFreeAsync`; chọn dải trống rồi **trả sequence về giá trị cũ
15:trong `try/finally`**. Dải đang ghi trống không chứng minh cả đoạn sequence sẽ đi qua đều trống.
36:**Mọi factory trong `LuxMap.Api.Tests` kế thừa `LuxMapTestHost`, không bao giờ kế thừa thẳng
37:`WebApplicationFactory<Program>`.** `LuxMapTestHost.CreateHost` chờ `TestDatabase` (một container
40:Canh bằng `TestHostCoverageTests` (phá thử: `DeviceProbeFactory` kế thừa thẳng → đỏ nêu đúng tên).
41:Ngoại lệ có tên: **`NoDatabaseTestHost`** cho test không chạm DB (CORS, correlation id, phân trang, lỗi khởi động) — chạy
44:`.env` (`NoDatabaseHostTests`).
61:⚠️ **Khôi phục file sau phá thử bằng `cp` hoặc `touch`, KHÔNG bằng `mv` bản sao lưu.** `mv` giữ thời điểm sửa CŨ của bản
62:sao, cũ hơn DLL vừa build từ bản đã phá ⇒ MSBuild coi module là chưa đổi và giữ DLL bản phá; test đỏ ở lượt chạy toàn bộ trong khi

[exit=0]
```

</details>

<details>
<summary>E15 — lệnh và output thật</summary>

```bash
nl -ba scripts/photos_to_poles.py | sed -n '110,114p;742,750p;795,799p'; nl -ba scripts/seed_mock_set.py | sed -n '153,163p;178,190p'; nl -ba scripts/copy_dev_to_supabase.py | sed -n '86,101p'; rg -n 'SegmentId|segment_id|pole' src/LuxMap.Api/OpenApi/WorkOrderSchemaFilter.cs
```

```text
   110	from pathlib import Path
   111	
   112	POLES_HEADER = ["external_ref", "segment_external_ref", "feeder_external_ref", "commune_id",
   113	                "geom_wkt", "near_sensitive_poi", "data_source"]
   114	JPEG_MAGIC = b"\xff\xd8\xff"
   742	            shutil.copy2(photo.path, out / "photos" / f"{group.name}_{photo.path.name}")
   743	        segment_ref, commune_id = args.segment_ref, args.commune_id
   744	        if segments:
   745	            d, nearest = min((distance_to_line_m(group.lat, group.lng, line), ref) for ref, line in segments)
   746	            segment_ref = nearest if d <= args.review_m else ""
   747	            if not segment_ref:
   748	                reviews.append(["khong_tuyen", group.name, nearest, f"{d:.1f}", "",
   749	                                f"Không tuyến nào trong {args.review_m:g} m — segment_external_ref để trống."])
   750	        if boundaries:
   795	          f"{len(suggestions)} gợi ý gộp · kết quả ở {out}")
   796	    if any(not row[0] or not row[1] or not row[3] for row in pole_rows):
   797	        print("⚠️  poles.csv còn ô bắt buộc để trống (external_ref / segment_external_ref / commune_id) "
   798	              "— điền trước khi import.")
   799	    return 0
   153	
   154	    for feature in poles:
   155	        p = feature["properties"]
   156	        sql.append(
   157	            # feeder_source: the demo circuit is nobody's verified claim (TOPO-INFER TI-1) — always 'inferred'.
   158	            "INSERT INTO pole (pole_id, segment_id, feeder_id, feeder_source, commune_id, geom, "
   159	            "near_sensitive_poi, data_source, external_ref) VALUES ("
   160	            f"{quote(p['pole_id'])}, {quote(p['segment_id'])}, {quote(demo_feeder[p['segment_id']])}, 'inferred', {COMMUNE}, "
   161	            f"{geometry(feature['geometry'])}, {str(p['near_sensitive_poi']).lower()}, "
   162	            f"{DATA_SOURCE}, {quote(p['pole_id'])});")
   163	
   178	    clustered = [f for f in faults if f["cluster_id"]]
   179	    for cluster_id in sorted({f["cluster_id"] for f in clustered}):
   180	        members = [f for f in clustered if f["cluster_id"] == cluster_id]
   181	        segment = sorted({f["segment_id"] for f in members})
   182	        if len(segment) != 1:
   183	            raise SystemExit(f"{cluster_id} spans {segment}; a cluster is one segment's outage.")
   184	
   185	        sql.append(
   186	            "INSERT INTO fault_cluster (cluster_id, segment_id, commune_id, clustered_at, "
   187	            "clustering_model_version) VALUES ("
   188	            f"{quote(cluster_id)}, {quote(segment[0])}, {COMMUNE}, "
   189	            # Not in the mock. The earliest detection in the cluster is the defensible reading, and
   190	            # it is recorded here rather than invented as "now" so re-seeding is reproducible.
    86	    # A global registry of versions, no commune: kept whole — every run, and since BE-34 every device's firmware, cites it — except
    87	    # what a system admin registered by hand when that admin is not copied (created_by is a RESTRICT key to app_user). Before
    88	    # iot_node, whose firmware_version_id points here.
    89	    ("artifact_version", f"created_by IS NULL OR created_by IN {COPIED_USERS}", "version_id"),
    90	    ("road_segment", IN_COMMUNES, "segment_id"),
    91	    # CABINET: feeder and iot_node point at the cabinet, so it comes before both.
    92	    ("electrical_cabinet", IN_COMMUNES, "cabinet_id"),
    93	    ("feeder", IN_COMMUNES, "feeder_id"),
    94	    ("pole", IN_COMMUNES, "pole_id"),
    95	    ("fixture", IN_COMMUNES, "fixture_id"),
    96	    ("iot_node", IN_COMMUNES, "node_id"),
    97	    # BE-34: a device's firmware changes, append-only; holds the device and the registry with RESTRICT.
    98	    ("iot_node_firmware", IN_COMMUNES, "change_id"),
    99	    ("feeder_control", IN_COMMUNES, "feeder_id"),
   100	    # LIGHT-CTRL 2b: requested_by is a RESTRICT key to app_user, so only the presses of the accounts copied above; a command
   101	    # follows its request. Newest-first is a read order — copy by seq so the identity column's order survives.
35:            concrete.Description = "WO-5 (provisional): repair requires fault_ids; inspection requires fault_ids OR segment_id; "
36:                + "survey (BE-15, provisional) requires commune_id and an ordered segment_ids. Server-owned fields are rejected.";
40:            foreach (var name in new[] { "commune_id", "wo_status", "assigned_to", "fault_ids", "task_kind", "segment_id", "segment_ids" }) concrete.Properties.Remove(name);

[exit=0]
```

</details>

<details>
<summary>E16 — lệnh và output thật</summary>

```bash
rg -n 'Include\(|\.Segment\b|join |JOIN |FromSql|ExecuteSql|SqlQuery' src/LuxMap.Modules.Assets src/LuxMap.Modules.Map src/LuxMap.Modules.Survey src/LuxMap.Modules.Statistics src/LuxMap.Modules.Sync src/LuxMap.Modules.WorkOrders src/LuxMap.Modules.Citizen src/LuxMap.Modules.Telemetry/Lighting --glob '*.cs'
```

```text
src/LuxMap.Modules.Statistics/StatisticsService.cs:54:        var bySegment = query.GroupBy.Contains(StatisticsDimension.Segment);
src/LuxMap.Modules.Statistics/StatisticsService.cs:61:                join s in db.Set<PoleCurrentStatus>() on p.PoleId equals s.PoleId into statuses
src/LuxMap.Modules.Statistics/StatisticsService.cs:100:        if (query.GroupBy.Contains(StatisticsDimension.Segment))
src/LuxMap.Modules.Statistics/StatisticsService.cs:183:            join f in db.Set<Fixture>().Where(f => f.RemovedDate == null) on p.PoleId equals f.PoleId into lamps
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:606:        var kind = feederId is not null ? LightingTargetKind.Feeder : LightingTargetKind.Segment;
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:629:                join node in db.Set<IotNode>() on control.NodeId equals node.NodeId
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:736:        => db.Database.ExecuteSqlRawAsync(
src/LuxMap.Modules.Telemetry/Lighting/LightingCommandService.cs:743:        return db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:105:        var photoId = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:119:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:123:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} AND commune_id = {target.CommuneId} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:194:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:196:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {initial.PoleId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:197:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order_pole_photo WHERE photo_id = {photoId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:221:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:223:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {initial.PoleId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotoService.cs:224:        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order_pole_photo WHERE photo_id = {photoId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
src/LuxMap.Modules.Map/Features/PoleDetailService.cs:200:            join o in db.Set<PoleObservation>().AsNoTracking() on h.ObservationId equals o.ObservationId
src/LuxMap.Modules.Map/Features/MapQueryService.cs:383:                           join control in dbContext.Set<FeederControl>() on feeder.FeederId equals control.FeederId into reports
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:46:    // routes as a diagnostic fallback; the outer LEFT JOIN still preserves an unassigned sample.
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:48:        string[] communes, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedGps>($"""
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:56:        LEFT JOIN LATERAL (
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:79:        string source, SurveyProcessingOptions o, CancellationToken ct) => db.Database.SqlQuery<ProjectedPole>($"""
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:87:        FROM road_segment r JOIN pole p ON p.segment_id = r.segment_id
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:89:        CROSS JOIN LATERAL (SELECT ST_Transform(r.geom,3405) AS route, ST_Transform(p.geom,3405) AS pole) projected
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:90:        CROSS JOIN LATERAL (SELECT ST_LineLocatePoint(projected.route, projected.pole) AS fraction) position
src/LuxMap.Modules.Survey/Processing/SurveyChainageQuery.cs:91:        CROSS JOIN LATERAL (SELECT ST_LineInterpolatePoint(projected.route, greatest(0, position.fraction - 0.00001)) AS a,
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:27:/// in flight could join a report that was decided a moment earlier and never reach a Manager. Order: pole, then report, then (merge)
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:246:        await db.Database.ExecuteSqlInterpolatedAsync(
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:248:        await db.Database.ExecuteSqlInterpolatedAsync(
src/LuxMap.Modules.Citizen/CitizenReviewService.cs:339:        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM citizen_photo WHERE photo_id = ANY({0}) ORDER BY photo_id FOR UPDATE", [ids], ct);
src/LuxMap.Modules.Survey/Review/SurveyFindingLookup.cs:18:                          join history in db.Set<LuminanceHistory>().AsNoTracking() on sweep.SweepId equals history.SweepId
src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:166:        var evidenceId = await db.Database.SqlQueryRaw<string>(nextId).SingleAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:175:        await db.Database.ExecuteSqlAsync(target.Lock, ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:133:        var candidates = await db.Database.SqlQuery<QueueRow>($"""
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:145:        var communes = await db.Database.SqlQuery<string>($"""
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:147:            UNION SELECT r.commune_id FROM work_order_segment w JOIN road_segment r USING (segment_id)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:149:            UNION SELECT p.commune_id FROM work_order_segment w JOIN pole p USING (segment_id)
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:150:              JOIN survey_sweep s ON s.sweep_id = {selected.SweepId} AND p.data_source = s.data_source
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:167:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {job.SweepId} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:201:        await db.Database.ExecuteSqlInterpolatedAsync($"""
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:214:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM work_order WHERE work_order_id = {sweep.WorkOrderId} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:215:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {job.SweepId} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Processing/SurveyProcessor.cs:256:                Run = run, SegmentId = result.Segment, PassNo = number++,
src/LuxMap.Modules.Citizen/PublicReportService.cs:116:        await db.Database.ExecuteSqlInterpolatedAsync(
src/LuxMap.Modules.Citizen/PublicReportService.cs:317:        return await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
src/LuxMap.Modules.WorkOrders/ActiveWorkOrderLookup.cs:31:    /// asks. Scope still holds — the join starts from the commune-scoped link rows of faults the
src/LuxMap.Modules.WorkOrders/ActiveWorkOrderLookup.cs:44:                join order in db.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking()
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:665:            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM pole WHERE pole_id = {0} FOR UPDATE", [poleId], ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:719:        await dbContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM pole WHERE pole_id = {0} FOR UPDATE", [poleId], ct);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:947:    /// a join would multiply the pole across its retired lamps before being collapsed again.
src/LuxMap.Modules.Citizen/QrCodeService.cs:48:        await db.Database.ExecuteSqlInterpolatedAsync(
src/LuxMap.Modules.Citizen/QrCodeService.cs:87:        await db.Database.ExecuteSqlInterpolatedAsync(
src/LuxMap.Modules.Citizen/QrCodeService.cs:89:        await db.Database.ExecuteSqlInterpolatedAsync(
src/LuxMap.Modules.Citizen/QrCodeService.cs:143:        return await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:224:        builder.HasOne(pole => pole.Segment)
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:167:                             join fault in db.Set<Fault>().AsNoTracking() on link.FaultId equals fault.FaultId
src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:168:                             join pole in db.Set<Pole>().AsNoTracking() on fault.PoleId equals pole.PoleId into poles
src/LuxMap.Modules.Assets/Entities/Pole.cs:25:/// scale those are a hash aggregate and a semi-join over a few thousand rows; storing them would buy
src/LuxMap.Modules.Assets/Entities/Pole.cs:85:    /// model has no POI entity and Branch C has no POI source, so there is nothing to join against.
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:59:        var rows = await query.Include(o => o.Pass).OrderBy(o => o.ObservedAt).ThenBy(o => o.PoleId.Length)
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:121:        var geometries = await GapGeometriesAsync(plans.SelectMany(p => p.Gaps.Select(g => (p.Segment, g.From, g.To))).ToArray(), communes, ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:126:            var mine = expected.Where(e => e.Segment == plan.Segment).Select(e => e.Pole).Distinct().ToArray();
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:128:            return new SegmentCoverage(plan.Segment, plan.Length, Math.Round(SweepCoverage.CoveredRatio(plan.Gaps), 4),
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:131:                    geometries.GetValueOrDefault((plan.Segment, g.From, g.To))))],
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:141:        var segments = gaps.Select(g => g.Segment).ToArray();
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:144:        var rows = await db.Database.SqlQuery<GapGeometryRow>($"""
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:148:            JOIN road_segment r ON r.segment_id = g.segment_id AND r.commune_id = ANY({communes})
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:209:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM work_order WHERE work_order_id = {initial.WorkOrderId} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:211:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {id} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:226:        var observations = await db.Set<PoleObservation>().Include(o => o.Pass).Where(o => o.RunId == run.RunId).ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:235:                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:305:                var faultId = await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:325:        var candidates = await db.Set<PoleObservation>().Include(o => o.Pass).Include(o => o.Run)
src/LuxMap.Modules.Assets/Crud/AssetContracts.cs:624:    /// <summary>Resolved for display. The list omits it: one join per row for a label.</summary>
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:82:        || (from link in db.Set<WorkOrderFault>() join fault in db.Set<Fault>() on link.FaultId equals fault.FaultId
src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:97:            join fault in db.Set<Fault>().AsNoTracking() on link.FaultId equals fault.FaultId
src/LuxMap.Modules.Survey/Processing/Frames/SurveyFramePipeline.cs:90:                            var id = await db.Database.SqlQuery<string>($"SELECT luxmap_format_id({spec.Prefix}, nextval({spec.SequenceName}::regclass), {spec.Digits}) AS \"Value\"").SingleAsync(ct);
src/LuxMap.Modules.Survey/Processing/Frames/SurveyFramePipeline.cs:142:            var pole = poles.Single(x => x.PoleId == p.PoleId && x.SegmentId == pass.Segment);
src/LuxMap.Modules.Survey/Processing/Frames/SurveyFramePipeline.cs:179:        await db.Database.ExecuteSqlInterpolatedAsync($"""
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:85:                             join fault in db.Set<Fault>() on link.FaultId equals fault.FaultId
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:107:                             join fault in db.Set<Fault>() on link.FaultId equals fault.FaultId
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:108:                             join pole in db.Set<Pole>() on fault.PoleId equals pole.PoleId into poles
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:183:        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:308:        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyMediaAccess.cs:39:        var communes = await db.Database.SqlQuery<CommuneRow>($"SELECT DISTINCT commune_id FROM pole_observation WHERE run_id = {run.RunId}").ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Review/SurveyMediaAccess.cs:42:        var current = await db.Database.SqlQuery<CommuneRow>($"SELECT DISTINCT commune_id FROM pole WHERE pole_id = ANY({ids})").ToArrayAsync(ct);
src/LuxMap.Modules.Survey/Review/ReviewedPolePhotoPublisher.cs:58:            var id = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:125:        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:38:        // Lock first, then read through LINQ — the FaultLocks pattern. A FromSql "SELECT *" cannot be the
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:43:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:62:        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {id} FOR UPDATE", ct);
src/LuxMap.Modules.Survey/Ingest/SurveyIngestService.cs:88:        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);

[exit=0]
```

</details>

<details>
<summary>E17 — lệnh và output thật</summary>

```bash
rg -n 'segment_id|SegmentId' mocks --glob '*.geojson' | head -12; rg -n 'DefaultIgnoreCondition|WhenWritingNull' src/LuxMap.Shared/Serialization/LuxMapJsonOptions.cs; rg -n 'PoleOffSegment|ValidationFailed|AssetNotFound' src/LuxMap.Shared/Contracts/Errors/ErrorCodes.cs
```

```text
mocks/mock-iot-nodes.geojson:19:    "segment_ids": [
mocks/mock-iot-nodes.geojson:43:    "segment_ids": [
mocks/mock-iot-nodes.geojson:67:    "segment_ids": [
mocks/mock-segments.geojson:85:    "segment_id": "SEG-001",
mocks/mock-segments.geojson:160:    "segment_id": "SEG-002",
mocks/mock-segments.geojson:223:    "segment_id": "SEG-003",
mocks/mock-poles.geojson:16:    "segment_id": "SEG-001",
mocks/mock-poles.geojson:44:    "segment_id": "SEG-001",
mocks/mock-poles.geojson:72:    "segment_id": "SEG-001",
mocks/mock-poles.geojson:100:    "segment_id": "SEG-001",
mocks/mock-poles.geojson:128:    "segment_id": "SEG-001",
mocks/mock-poles.geojson:156:    "segment_id": "SEG-001",
32:    public const string ValidationFailed = "VALIDATION_FAILED";
59:    public const string AssetNotFound = "ASSET_NOT_FOUND";
210:    public const string PoleOffSegment = "POLE_OFF_SEGMENT";

[exit=0]
```

</details>

<details>
<summary>E18 — lệnh và output thật</summary>

```bash
python3 - <<'PY'
import json
from pathlib import Path
p=Path('docs/openapi/luxmap-v1.5.json'); d=json.loads(p.read_text()); lines=p.read_text().splitlines()
for name,s in d['components']['schemas'].items():
 props=s.get('properties',{})
 if 'segment_id' in props:
  print(name+': required='+str('segment_id' in s.get('required',[]))+'; segment_id='+json.dumps(props['segment_id'],ensure_ascii=False))
for name in ['PoleMapDetail','PublicQrInfo','PoleDetail','WorkOrderPole']:
 s=d['components']['schemas'][name]
 for k,v in s.get('properties',{}).items():
  if k in ['segment_name','position','fixture','luminance_baseline']: print(name+'.'+k+'='+json.dumps(v))
for i,line in enumerate(lines,1):
 if '"segment_id": {' in line or '"segment_external_ref"' in line: print(str(p)+':'+str(i)+':'+line.strip())
for f in ['mocks/mock-poles.geojson','mocks/mock-segments.geojson','mocks/mock-iot-nodes.geojson']:
 d=json.loads(Path(f).read_text()); print(f+': features='+str(len(d['features']))+'; first.properties='+json.dumps(d['features'][0]['properties'],ensure_ascii=False))
PY
```

```text
AgendaWorkOrder: required=False; segment_id={"type": "string", "nullable": true}
CabinetTopologyEdgeProperties: required=True; segment_id={"type": "string"}
CitizenReportPole: required=True; segment_id={"type": "string"}
CreateLightingRequest: required=False; segment_id={"maxLength": 32, "type": "string", "nullable": true}
CreatePoleRequest: required=True; segment_id={"maxLength": 32, "minLength": 1, "type": "string"}
CreateWorkOrderRequest: required=False; segment_id={"type": "string", "nullable": true}
FaultItem: required=False; segment_id={"type": "string", "nullable": true}
FixtureStatusRow: required=True; segment_id={"type": "string", "nullable": true}
PoleListItem: required=True; segment_id={"type": "string"}
PoleMapDetail: required=True; segment_id={"type": "string"}
PoleProperties: required=True; segment_id={"$ref": "#/components/schemas/SegmentId"}
ReportedFault: required=False; segment_id={"type": "string", "nullable": true}
SegmentCoverage: required=False; segment_id={"type": "string"}
SegmentListItem: required=True; segment_id={"type": "string"}
SegmentProperties: required=True; segment_id={"$ref": "#/components/schemas/SegmentId"}
SyncPoleProperties: required=True; segment_id={"type": "string"}
TopologyPole: required=True; segment_id={"type": "string"}
UpdatePoleRequest: required=True; segment_id={"maxLength": 32, "minLength": 1, "type": "string"}
WarrantyItem: required=True; segment_id={"type": "string"}
WorkOrderAgendaGroup: required=False; segment_id={"type": "string", "nullable": true}
WorkOrderDetail: required=False; segment_id={"type": "string", "nullable": true}
WorkOrderFaultDetail: required=False; segment_id={"type": "string", "nullable": true}
WorkOrderItem: required=False; segment_id={"type": "string", "nullable": true}
WorkOrderPole: required=True; segment_id={"type": "string"}
PoleMapDetail.segment_name={"type": "string"}
PoleMapDetail.fixture={"$ref": "#/components/schemas/PoleMapFixture"}
PoleMapDetail.luminance_baseline={"$ref": "#/components/schemas/PoleMapBaseline"}
PublicQrInfo.segment_name={"type": "string"}
PoleDetail.segment_name={"type": "string"}
WorkOrderPole.position={"type": "integer", "format": "int32"}
docs/openapi/luxmap-v1.5.json:14724:"segment_id": {
docs/openapi/luxmap-v1.5.json:15328:"segment_id": {
docs/openapi/luxmap-v1.5.json:15757:"segment_id": {
docs/openapi/luxmap-v1.5.json:16162:"segment_id": {
docs/openapi/luxmap-v1.5.json:16246:"segment_id": {
docs/openapi/luxmap-v1.5.json:16436:"segment_id": {
docs/openapi/luxmap-v1.5.json:16748:"segment_id": {
docs/openapi/luxmap-v1.5.json:17088:"segment_id": {
docs/openapi/luxmap-v1.5.json:18360:"segment_id": {
docs/openapi/luxmap-v1.5.json:18530:"segment_id": {
docs/openapi/luxmap-v1.5.json:19072:"segment_id": {
docs/openapi/luxmap-v1.5.json:19567:"segment_id": {
docs/openapi/luxmap-v1.5.json:19771:"segment_id": {
docs/openapi/luxmap-v1.5.json:19848:"segment_id": {
docs/openapi/luxmap-v1.5.json:19932:"segment_id": {
docs/openapi/luxmap-v1.5.json:20862:"segment_id": {
docs/openapi/luxmap-v1.5.json:21079:"segment_id": {
docs/openapi/luxmap-v1.5.json:21247:"segment_id": {
docs/openapi/luxmap-v1.5.json:21458:"segment_id": {
docs/openapi/luxmap-v1.5.json:21631:"segment_id": {
docs/openapi/luxmap-v1.5.json:21720:"segment_id": {
docs/openapi/luxmap-v1.5.json:21881:"segment_id": {
docs/openapi/luxmap-v1.5.json:21925:"segment_id": {
docs/openapi/luxmap-v1.5.json:22056:"segment_id": {
mocks/mock-poles.geojson: features=103; first.properties={"pole_id": "POLE-0001", "segment_id": "SEG-001", "fixture_status": "normal", "status_confidence": 0.92, "power_source": "grid", "fixture_type": "led_road_lamp", "lamp_watt": 100, "lamp_count": 1, "install_date": "2022-03-24", "warranty_expiry": "2024-03-24", "commune_id": "COM-001", "last_seen_at": "2026-08-20T12:00:00Z", "last_sweep_id": "SWP-030", "open_fault_count": 0, "has_iot_node": false, "near_sensitive_poi": false}
mocks/mock-segments.geojson: features=3; first.properties={"segment_id": "SEG-001", "segment_name": "Tuyen A - duong lien xa", "road_class": "inter_commune", "length_m": 1600, "pole_count": 46, "controller_node_ids": ["NODE-001"], "has_active_segment_fault": false}
mocks/mock-iot-nodes.geojson: features=3; first.properties={"node_id": "NODE-001", "node_role": "segment_controller", "node_status": "online", "pole_id": null, "segment_ids": ["SEG-001"], "feeder_ids": ["FDR-001"], "supports_remote_control": false, "last_report_at": "2026-08-20T04:00:00Z"}

[exit=0]
```

</details>

<details>
<summary>E19 — lệnh và output thật</summary>

```bash
rg -n 'fk_pole|table: "pole"|ck_pole|segment_id' src/LuxMap.Persistence/Migrations --glob '*.cs' --glob '!*.Designer.cs' --glob '!**/*Snapshot.cs'
```

```text
src/LuxMap.Persistence/Migrations/20261008061230_AddLightingCommands.cs:49:                    affected_segment_ids = table.Column<string[]>(type: "text[]", nullable: false),
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:18:                name: "fk_pole_app_user_note_updated_by",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:19:                table: "pole");
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:22:                name: "ck_pole_note_stamp_together",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:23:                table: "pole");
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:27:                table: "pole");
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:31:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:36:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:89:                name: "fk_pole_app_user_updated_by",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:90:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:117:                name: "fk_pole_app_user_updated_by",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:118:                table: "pole");
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:150:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:155:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:160:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:169:                name: "ck_pole_note_stamp_together",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:170:                table: "pole",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:174:                name: "fk_pole_app_user_note_updated_by",
src/LuxMap.Persistence/Migrations/20261006021757_AssetUpdatedBy.cs:175:                table: "pole",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:25:                    segment_id = table.Column<string>(type: "text", nullable: false),
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:41:                        name: "fk_fault_cluster_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:42:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:44:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:56:                    segment_id = table.Column<string>(type: "text", nullable: true),
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:134:                        name: "fk_fault_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:135:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:137:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:188:                name: "ix_fault_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:190:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:205:                name: "ix_fault_cluster_segment_id",
src/LuxMap.Persistence/Migrations/20260905085759_AddFaultEntities.cs:207:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:61:                    table.CheckConstraint("ck_pole_photo_ai", "(cv_state IS NULL AND confidence IS NULL AND model_version IS NULL AND artifact_version IS NULL AND analyzed_at IS NULL) OR (cv_state IS NOT NULL AND cv_state IN ('on','off','inconclusive') AND model_version IS NOT NULL AND btrim(model_version) <> '' AND artifact_version IS NOT NULL AND btrim(artifact_version) <> '' AND analyzed_at IS NOT NULL AND ((cv_state = 'inconclusive' AND confidence IS NULL) OR (cv_state IN ('on','off') AND confidence IS NOT NULL AND confidence BETWEEN 0 AND 1 AND confidence <> 'NaN'::float8)))");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:62:                    table.CheckConstraint("ck_pole_photo_decision", "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (cv_state IS NOT NULL AND cv_state IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:63:                    table.CheckConstraint("ck_pole_photo_keys", "btrim(object_key) <> '' AND btrim(thumbnail_key) <> '' AND request_hash ~ '^[0-9a-f]{64}$' AND client_op_id <> '00000000-0000-0000-0000-000000000000'::uuid");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:64:                    table.CheckConstraint("ck_pole_photo_lat", "lat BETWEEN -90 AND 90 AND lat <> 'NaN'::float8");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:65:                    table.CheckConstraint("ck_pole_photo_lng", "lng BETWEEN -180 AND 180 AND lng <> 'NaN'::float8");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:66:                    table.CheckConstraint("ck_pole_photo_sizes", "byte_count BETWEEN 1 AND 16777216 AND thumbnail_bytes > 0 AND width > 0 AND height > 0 AND width::bigint * height <= 40000000");
src/LuxMap.Persistence/Migrations/20261015153829_AddWorkOrderPolePhotos.cs:138:            migrationBuilder.Sql("ALTER TABLE work_order_pole_photo ADD CONSTRAINT fk_pole_photo_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT");
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:54:                name: "fk_pole_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:55:                table: "pole",
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:62:                name: "fk_pole_current_status_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:90:                name: "fk_pole_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:91:                table: "pole");
src/LuxMap.Persistence/Migrations/20260901091555_AddCommuneForeignKeys.cs:94:                name: "fk_pole_current_status_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20260918155145_PoleCurrentStatusConfidenceRange.cs:14:                name: "ck_pole_current_status_confidence_range",
src/LuxMap.Persistence/Migrations/20260918155145_PoleCurrentStatusConfidenceRange.cs:23:                name: "ck_pole_current_status_confidence_range",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:132:                    segment_id = table.Column<string>(type: "text", nullable: false),
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:150:                        name: "fk_survey_pass_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:151:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:153:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:186:                    table.CheckConstraint("ck_pole_observation_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:187:                    table.CheckConstraint("ck_pole_observation_finite", "chainage_m >= 0 AND chainage_m < 'Infinity'::float8 AND speed_mps >= 0 AND speed_mps < 'Infinity'::float8 AND association_confidence BETWEEN 0 AND 1");
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:188:                    table.CheckConstraint("ck_pole_observation_peak", "(peak_lux IS NULL) = (peak_at_elapsed_ns IS NULL) AND (peak_lux IS NULL OR (peak_lux >= 0 AND peak_lux < 'Infinity'::float8))");
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:189:                    table.CheckConstraint("ck_pole_observation_quality", "jsonb_typeof(quality_flags) = 'array'");
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:190:                    table.CheckConstraint("ck_pole_observation_time", "observed_elapsed_ns >= 0 AND (peak_at_elapsed_ns IS NULL OR peak_at_elapsed_ns >= 0)");
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:192:                        name: "fk_pole_observation_administrative_unit_commune_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:198:                        name: "fk_pole_observation_pole_pole_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:204:                        name: "fk_pole_observation_survey_pass_pass_id_run_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:210:                        name: "fk_pole_observation_survey_processing_run_run_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:271:                name: "ix_survey_pass_segment_id",
src/LuxMap.Persistence/Migrations/20261002095348_AddSurveyProcessing.cs:273:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs:31:                    segment_id = table.Column<string>(type: "text", nullable: true),
src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs:85:                        name: "fk_work_order_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs:86:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs:88:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs:149:                name: "ix_work_order_segment_id",
src/LuxMap.Persistence/Migrations/20260928025712_AddWorkOrders.cs:151:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:90:                    segment_id = table.Column<string>(type: "text", nullable: false),
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:104:                        name: "fk_work_order_segment_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:105:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:107:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:229:                sql: "task_kind <> 'survey' OR (segment_id IS NULL AND cluster_id IS NULL)");
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:306:                name: "ix_work_order_segment_segment_id",
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:308:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:316:                name: "ix_work_order_segment_work_order_id_segment_id",
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:318:                columns: new[] { "work_order_id", "segment_id" },
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:322:                name: "fk_pole_current_status_survey_sweep_last_sweep_id",
src/LuxMap.Persistence/Migrations/20261002041141_AddSurveyIngest.cs:334:                name: "fk_pole_current_status_survey_sweep_last_sweep_id",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:19:                table: "pole",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:37:                name: "ck_pole_feeder_source",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:38:                table: "pole",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:42:                name: "ck_pole_feeder_source_matches_feeder",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:43:                table: "pole",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:61:                name: "ck_pole_feeder_source",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:62:                table: "pole");
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:65:                name: "ck_pole_feeder_source_matches_feeder",
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:66:                table: "pole");
src/LuxMap.Persistence/Migrations/20261007143426_AddTopologySource.cs:78:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:16:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:22:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:28:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:34:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:38:                name: "ck_pole_note_length",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:39:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:43:                name: "ck_pole_note_stamp_together",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:44:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:48:                name: "fk_pole_app_user_note_updated_by",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:49:                table: "pole",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:60:                name: "fk_pole_app_user_note_updated_by",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:61:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:65:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:68:                name: "ck_pole_note_length",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:69:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:72:                name: "ck_pole_note_stamp_together",
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:73:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:77:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:81:                table: "pole");
src/LuxMap.Persistence/Migrations/20261005153538_AddPoleNote.cs:85:                table: "pole");
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:15:                name: "ck_pole_photo_decision",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:19:                name: "ck_pole_current_status_confidence_matches_status",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:56:                name: "ck_pole_photo_decision",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:61:                name: "ck_pole_photo_review_complete",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:66:                name: "ck_pole_photo_review_note",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:71:                name: "ck_pole_photo_review_note_required",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:76:                name: "ck_pole_current_status_confidence_matches_status",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:86:                name: "fk_pole_photo_reviewed_by",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:111:                name: "fk_pole_photo_reviewed_by",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:119:                name: "ck_pole_photo_decision",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:123:                name: "ck_pole_photo_review_complete",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:127:                name: "ck_pole_photo_review_note",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:131:                name: "ck_pole_photo_review_note_required",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:135:                name: "ck_pole_current_status_confidence_matches_status",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:159:                name: "ck_pole_photo_decision",
src/LuxMap.Persistence/Migrations/20261020134307_AddPolePhotoReviews.cs:164:                name: "ck_pole_current_status_confidence_matches_status",
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:36:                name: "segment_id",
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:40:                defaultValueSql: "luxmap_format_id('SEG', nextval('segment_id_seq'), 3)",
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:43:                oldDefaultValueSql: "'SEG-' || LPAD(nextval('segment_id_seq')::text, 3, '0')");
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:47:                table: "pole",
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:100:                name: "segment_id",
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:104:                defaultValueSql: "'SEG-' || LPAD(nextval('segment_id_seq')::text, 3, '0')",
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:107:                oldDefaultValueSql: "luxmap_format_id('SEG', nextval('segment_id_seq'), 3)");
src/LuxMap.Persistence/Migrations/20260901084931_FixPrefixedIdOverflow.cs:111:                table: "pole",
src/LuxMap.Persistence/Migrations/20260905095851_AddAssetExternalRef.cs:21:                table: "pole",
src/LuxMap.Persistence/Migrations/20260905095851_AddAssetExternalRef.cs:40:                table: "pole",
src/LuxMap.Persistence/Migrations/20260905095851_AddAssetExternalRef.cs:62:                table: "pole");
src/LuxMap.Persistence/Migrations/20260905095851_AddAssetExternalRef.cs:74:                table: "pole");
src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs:258:                name: "ck_pole_observation_baseline_value",
src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs:352:                name: "fk_pole_current_status_survey_processing_run_last_run_id_last_",
src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs:360:                name: "fk_pole_observation_luminance_baseline_baseline_id_pole_id_com",
src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs:410:                name: "fk_pole_current_status_survey_processing_run_last_run_id_last_",
src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs:414:                name: "fk_pole_observation_luminance_baseline_baseline_id_pole_id_com",
src/LuxMap.Persistence/Migrations/20261003172809_AddSurveyPublication.cs:459:                name: "ck_pole_observation_baseline_value",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:25:                name: "segment_id_seq");
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:47:                    segment_id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'SEG-' || LPAD(nextval('segment_id_seq')::text, 3, '0')"),
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:59:                    table.PrimaryKey("pk_road_segment", x => x.segment_id);
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:69:                    segment_id = table.Column<string>(type: "text", nullable: false),
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:81:                    table.CheckConstraint("ck_pole_data_source", "\"data_source\" IN ('field', 'public_imagery', 'calibration_rig', 'simulated')");
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:83:                        name: "fk_pole_feeder_feeder_id",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:89:                        name: "fk_pole_road_segment_segment_id",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:90:                        column: x => x.segment_id,
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:92:                        principalColumn: "segment_id",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:142:                    table.CheckConstraint("ck_pole_current_status_confidence_matches_status", "(status_confidence IS NULL) = (fixture_status = 'unknown')");
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:143:                    table.CheckConstraint("ck_pole_current_status_fixture_status", "\"fixture_status\" IN ('normal', 'dim', 'out', 'unknown')");
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:145:                        name: "fk_pole_current_status_pole_pole_id",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:176:                table: "pole",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:181:                table: "pole",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:186:                table: "pole",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:191:                name: "ix_pole_segment_id",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:192:                table: "pole",
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:193:                column: "segment_id");
src/LuxMap.Persistence/Migrations/20260901082917_AddAssetEntities.cs:240:                name: "segment_id_seq");
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:241:                name: "ck_pole_observation_classification",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:246:                name: "ck_pole_observation_classified_as",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:251:                name: "ck_pole_observation_cv",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:303:                name: "fk_pole_observation_survey_frame_representative_frame_id",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:357:                name: "fk_pole_observation_survey_frame_representative_frame_id",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:411:                name: "ck_pole_observation_classification",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:415:                name: "ck_pole_observation_classified_as",
src/LuxMap.Persistence/Migrations/20261003071658_AddSurveyFrames.cs:419:                name: "ck_pole_observation_cv",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:25:                name: "fk_pole_feeder_feeder_id",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:26:                table: "pole");
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:30:                table: "pole");
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:39:                table: "pole",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:43:                name: "fk_pole_feeder_feeder_id_commune_id",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:44:                table: "pole",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:55:                name: "fk_pole_feeder_feeder_id_commune_id",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:56:                table: "pole");
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:60:                table: "pole");
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:68:                table: "pole",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:72:                name: "fk_pole_feeder_feeder_id",
src/LuxMap.Persistence/Migrations/20260921142601_FeederCommuneCompositeFk.cs:73:                table: "pole",

[exit=0]
```

</details>

<details>
<summary>E20 — lệnh và output thật</summary>

```bash
nl -ba src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs | sed -n '115,130p'; nl -ba src/LuxMap.Modules.Assets/Crud/AssetContracts.cs | sed -n '620,628p'; rg -n 'ReadNetwork.*UserRole|ManageAssets.*UserRole|ControlLighting.*UserRole|SyncOffline.*UserRole' src/LuxMap.Shared/Authorization/LuxMapPolicies.cs; nl -ba tests/LuxMap.Api.Tests/WorkOrderTests.cs | sed -n '817,840p;841,864p;884,895p'; rg -n 'nullable|DROP|IS NULL|RAISE|NOT NULL' src/LuxMap.Persistence/Migrations/*PolePhotoReviews.cs
```

```text
   115	            .FirstAsync(ct);
   116	
   117	        return new PoleDetail
   118	        {
   119	            Pole = item,
   120	
   121	            // A pole always has a segment — NOT NULL with a real foreign key — so a missing name
   122	            // would mean the segment sits in another commune and the filter hid it. That happens on
   123	            // an inter_commune road and is legitimate, so the id still identifies it; the label is
   124	            // simply not this caller's to see.
   125	            SegmentName = extra.SegmentName ?? string.Empty,
   126	            GeomWkt = Wkt(extra.Geom),
   127	            CreatedAt = extra.CreatedAt,
   128	        };
   129	    }
   130	
   620	public sealed record PoleDetail
   621	{
   622	    public required PoleListItem Pole { get; init; }
   623	
   624	    /// <summary>Resolved for display. The list omits it: one join per row for a label.</summary>
   625	    public required string SegmentName { get; init; }
   626	
   627	    /// <summary>WKT <c>POINT</c> in EPSG:4326, longitude first — what a <c>PUT</c> body wants back.</summary>
   628	    public required string GeomWkt { get; init; }
137:            [ReadNetwork] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
139:            [ManageAssets] = [UserRole.Manager],
143:            [ControlLighting] = [UserRole.Manager],
153:            [SyncOffline] = [UserRole.FieldEngineer],
   817	    {
   818	        var other = await Db(async db =>
   819	        {
   820	            using var system = db.EnterUnscopedSystemWriteBackdoor();
   821	            var road = new RoadSegment { CommuneId = home, SegmentName = "WO second road", RoadClass = RoadClass.InterVillage,
   822	                DataSource = DataSource.Simulated, LengthM = 100, Geom = new LineString([new Coordinate(108.1, 16.1), new Coordinate(108.11, 16.11)]) { SRID = 4326 } };
   823	            db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
   824	        });
   825	        var onFirst = await PlantPole(0.4);
   826	        var onOther = await PlantPole(0.1, road: other, origin: 0.1);
   827	        var response = await Send("manager", "POST", "", new { task_kind = "survey", title = "Survey the poles", commune_id = home,
   828	            segment_ids = new[] { other, segment }, assigned_to = users["a"].UserId }, 201);
   829	        var id = response.GetProperty("work_order_id").GetString()!;
   830	
   831	        // The engineer the order is assigned to sees it too — this is what they open on the phone.
   832	        var body = await PolesOf(id, who: "a");
   833	
   834	        var items = body.GetProperty("items").EnumerateArray().ToArray();
   835	        // `other` was listed first on the order, so its pole comes first even though it was planted second.
   836	        Assert.Equal([onOther, onFirst], items.Select(x => x.GetProperty("pole_id").GetString()!));
   837	        Assert.Equal([1, 1], items.Select(x => x.GetProperty("position").GetInt32())); // position restarts per segment
   838	    }
   839	
   840	    [Fact]
   841	    public async Task A_repair_created_from_faults_lists_the_poles_carrying_them_with_the_fault_ids()
   842	    {
   843	        var offRoad = await Db(async db =>
   844	        {
   845	            using var system = db.EnterUnscopedSystemWriteBackdoor();
   846	            var road = new RoadSegment { CommuneId = home, SegmentName = "WO fault road", RoadClass = RoadClass.InterVillage,
   847	                DataSource = DataSource.Simulated, LengthM = 100, Geom = new LineString([new Coordinate(108.2, 16.2), new Coordinate(108.21, 16.21)]) { SRID = 4326 } };
   848	            db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
   849	        });
   850	        // Planted in REVERSE road order on a road the order does not list: only the faults tie these poles to it.
   851	        var beyond = await PlantPole(0.7, road: offRoad, origin: 0.2);
   852	        var pole = await PlantPole(0.3, road: offRoad, status: FixtureStatus.Out, confidence: 0.9, origin: 0.2);
   853	        var fault = await PlantPoleFault(pole, FaultStatus.Confirmed, reportedOn: segment);
   854	        var beyondFault = await PlantPoleFault(beyond, FaultStatus.Confirmed, reportedOn: segment);
   855	        var response = await Send("manager", "POST", "", new { task_kind = "repair", title = "Repair those poles", fault_ids = new[] { fault, beyondFault } }, 201);
   856	        var id = response.GetProperty("work_order_id").GetString()!;
   857	
   858	        var items = (await PolesOf(id)).GetProperty("items").EnumerateArray().ToArray();
   859	
   860	        // Road order on the fault-only road too, not insert order or id order.
   861	        Assert.Equal([pole, beyond], items.Select(x => x.GetProperty("pole_id").GetString()!));
   862	        Assert.Equal([1, 2], items.Select(x => x.GetProperty("position").GetInt32()));
   863	        Assert.Equal([fault], items[0].GetProperty("work_order_fault_ids").EnumerateArray().Select(x => x.GetString()!));
   864	        Assert.Equal("out", items[0].GetProperty("fixture_status").GetString());
   884	    public async Task Paging_keeps_road_positions_and_the_total()
   885	    {
   886	        foreach (var along in new[] { 0.1, 0.2, 0.3 }) await PlantPole(along);
   887	        var id = await Create();
   888	
   889	        var first = await PolesOf(id, query: "?page=1&page_size=2");
   890	        var second = await PolesOf(id, query: "?page=2&page_size=2");
   891	
   892	        Assert.Equal(3, first.GetProperty("total").GetInt32());
   893	        Assert.Equal([1, 2], first.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("position").GetInt32()));
   894	        Assert.Equal([3], second.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("position").GetInt32()));
   895	    }
4:#nullable disable
30:                nullable: true);
36:                nullable: true);
42:                nullable: true);
48:                nullable: true);
58:                sql: "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (COALESCE(final_cv_state, cv_state) IS NOT NULL AND COALESCE(final_cv_state, cv_state) IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");
63:                sql: "(final_cv_state IS NULL AND review_note IS NULL AND reviewed_by IS NULL AND reviewed_at IS NULL) OR (final_cv_state IS NOT NULL AND final_cv_state IN ('on','off') AND reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)");
68:                sql: "review_note IS NULL OR (char_length(review_note) BETWEEN 1 AND 2000 AND btrim(review_note, E' \\t\\n\\r\\f' || chr(11)) <> '')");
73:                sql: "final_cv_state IS NULL OR final_cv_state IS NOT DISTINCT FROM cv_state OR review_note IS NOT NULL");
78:                sql: "fixture_status <> 'unknown' OR status_confidence IS NULL");
83:                sql: "origin_pole_photo_id IS NULL OR (source_channel IN ('cv','field_report') AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL AND (source_channel <> 'field_report' OR (reported_by IS NOT NULL AND status_confidence IS NULL AND detection_model_version IS NULL)))");
102:                    IF EXISTS (SELECT 1 FROM work_order_pole_photo WHERE final_cv_state IS NOT NULL)
103:                        OR EXISTS (SELECT 1 FROM pole_current_status WHERE fixture_status <> 'unknown' AND status_confidence IS NULL)
104:                        OR EXISTS (SELECT 1 FROM fault WHERE origin_pole_photo_id IS NOT NULL AND source_channel <> 'cv') THEN
105:                        RAISE EXCEPTION 'Cannot roll back AddPolePhotoReviews with review data; roll back the application and keep the schema.';
161:                sql: "(decision IS NULL AND decided_by IS NULL AND decided_at IS NULL AND decision_note IS NULL AND NOT status_applied AND status_reason IS NULL) OR (decision IS NOT NULL AND decision IN ('confirm','reject') AND decided_by IS NOT NULL AND decided_at IS NOT NULL AND status_reason IS NOT NULL AND (decision <> 'confirm' OR (cv_state IS NOT NULL AND cv_state IN ('on','off'))) AND (NOT status_applied OR decision = 'confirm'))");
166:                sql: "(status_confidence IS NULL) = (fixture_status = 'unknown')");
171:                sql: "origin_pole_photo_id IS NULL OR (source_channel = 'cv' AND fault_type = 'lamp_out' AND pole_id IS NOT NULL AND origin_observation_id IS NULL AND origin_node_id IS NULL)");

[exit=0]
```

</details>

<details>
<summary>E21 — lệnh và output thật</summary>

```bash
rg -n 'SegmentId|segment_id|segment_external_ref' scripts mocks --glob '!*.json' --glob '!*.py' --glob '!*.geojson'; nl -ba scripts/seed_mock_set.py | sed -n '72,76p'; nl -ba mocks/README.md | sed -n '20,27p'; nl -ba src/LuxMap.Modules.Assets/Import/AssetImportController.cs | sed -n '15,20p'; nl -ba tests/LuxMap.Api.Tests/AssetReadShapeTests.cs | sed -n '43,63p'
```

```text
mocks/mock-pole-feeders.csv:1:pole_external_ref,segment_external_ref,lng,lat,feeder_external_ref
mocks/README.md:24:  cột, nên 9 node đó bị bỏ (I-1), `battery_pct` bị bỏ (I-2), `segment_id` thành `segment_ids[]`, thêm
    72	def quote(value) -> str:
    73	    """A SQL literal, or NULL. Single quotes are doubled; nothing else is interpolated."""
    74	    return "NULL" if value is None else "'" + str(value).replace("'", "''") + "'"
    75	
    76	
    20	- **`SEG-003`** có một cụm lỗi cả đoạn — dùng test luồng highlight toàn tuyến
    21	  (`has_active_segment_fault = true`)
    22	- **3 IoT node**, cả ba `segment_controller`, đặt ở tủ điện tổng (28/09/2026, drift "BE-14 / IoT").
    23	  ⚠️ **Trước 28/09 có 12 node**, gồm 9 `sampled_fixture` trên từng cột: đồ án **không** gắn IoT lên
    24	  cột, nên 9 node đó bị bỏ (I-1), `battery_pct` bị bỏ (I-2), `segment_id` thành `segment_ids[]`, thêm
    25	  `feeder_ids[]` + `supports_remote_control`. `has_iot_node` của mọi cột nay là `false` (I-8), và
    26	  `controller_node_id` của `mock-segments.geojson` thành `controller_node_ids[]` (I-7b). **Phải báo
    27	  WP5/WP6.**
    15	/// </summary>
    16	/// <remarks>
    17	/// Load the five kinds IN ORDER: <c>segments</c>, <c>cabinets</c>, <c>feeders</c>, <c>poles</c>, <c>fixtures</c>.
    18	/// The order is foreign keys, not preference, and it enforces itself — poles uploaded before their
    19	/// segments fail every row with a message naming the <c>segment_external_ref</c> that matched nothing.
    20	/// </remarks>
    43	
    44	        var row = await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId);
    45	        var keys = row.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    46	
    47	        string[] expected =
    48	        [
    49	            "pole_id", "external_ref", "segment_id", "feeder_id", "commune_id", "commune_name", "data_source",
    50	            "near_sensitive_poi", "location", "active_fixture", "note", "updated_at", "updated_by", "updated_by_name",
    51	            "feeder_source", "cabinet_id", "cabinet_name", "cabinet_source",
    52	        ];
    53	
    54	        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. keys.Order(StringComparer.Ordinal)]);
    55	
    56	        // The operational view's fields, which belong to Contract section 5.1 and must never appear
    57	        // here. Listed by name rather than inferred, so adding one has to argue with a red test.
    58	        foreach (var operational in new[]
    59	                 {
    60	                     "fixture_status", "status_confidence", "open_fault_count", "last_seen_at",
    61	                     "last_sweep_id", "has_iot_node",
    62	                 })
    63	        {

[exit=0]
```

</details>

<details>
<summary>E22 — lệnh và output thật</summary>

```bash
python3 - <<'PY'
from pathlib import Path
import json,re,subprocess,collections
p=Path('.ai/results/POLE-SEGMENT-NULL-p1.md'); text=p.read_text(); es=json.loads(Path('/tmp/pole-survey-evidence.json').read_text())
for key in ['E01','E02']:
 e=next(x for x in es if x['id']==key); g=collections.defaultdict(list)
 for l in e['stdout'].splitlines():
  f,n,_=l.split(':',2);g[f].append(n)
 for f,nums in g.items():
  assert '| `'+f+':'+','.join(nums)+'` |' in text,(key,f)
 print(key+': '+str(sum(map(len,g.values())))+' hits / '+str(len(g))+' files; all file:line entries mapped')
headings=re.findall(r'^## ([1-8])\. ',text,re.M);assert headings==list('12345678'),headings
print('8 required sections: present; D-items in final section')
assert len(re.findall(r'\| \*\*D-\d+\*\*',text))==14
print('14 D-items: present')
r=subprocess.run(['git','diff','--exit-code','HEAD','--','src','tests','docs','scripts','mocks','tracking.html'],capture_output=True,text=True)
assert r.returncode==0,r.stdout+r.stderr
print('Protected paths: git diff HEAD empty (exit 0)')
r=subprocess.run(['git','status','--short'],capture_output=True,text=True);print('git status --short:\n'+r.stdout,end='')
assert r.stdout.splitlines()==['?? .ai/results/POLE-SEGMENT-NULL-p1.md'],r.stdout
PY
```

```text
E01: 331 hits / 64 files; all file:line entries mapped
E02: 240 hits / 52 files; all file:line entries mapped
8 required sections: present; D-items in final section
14 D-items: present
Protected paths: git diff HEAD empty (exit 0)
git status --short:
?? .ai/results/POLE-SEGMENT-NULL-p1.md

[exit=0]
```

</details>

<details>
<summary>E23 — stage bị sandbox từ chối; kiểm tra trạng thái sau lỗi</summary>

```bash
git add -- .ai/results/POLE-SEGMENT-NULL-p1.md
git status --short
git diff --cached --name-only
git rev-parse HEAD
```

```text
fatal: Unable to create '/Users/nhm809/Documents/LuxMap/luxmap_backend/.git/worktrees/wt-pole-segment-null/index.lock': Operation not permitted
[git add exit=128]
git status --short:
?? .ai/results/POLE-SEGMENT-NULL-p1.md
git diff --cached --name-only:
git rev-parse HEAD:
89c4ce218faf79d90fa09a81ad7b197c3349b525
```

Không tiếp tục commit vì bước stage đã thất bại. Các lệnh trạng thái sau đó chỉ đọc.

</details>

## 8. D-item — Mỹ chốt; tất cả đang chờ, không triển khai P1

Không mở lại bốn quyết định nullable/chưa phân tuyến/không giao survey/đếm riêng–báo rõ. Các phương án dưới đây chỉ chọn **cách thực thi** và hình dạng API. Khuyến nghị không phải quyết định.

| Mã | Chỗ chưa rõ + bằng chứng | Phương án | Khuyến nghị để Mỹ duyệt |
|---|---|---|---|
| **D-1** | WO inspection/repair có fault của pole chưa tuyến nhưng ordering/position buộc có road; `Targets` equality với `WorkOrder.SegmentId` vốn nullable (E07). Sau khi pole optional, EF có thể bù null==null làm chọn thêm pole không liên quan; **chưa có SQL chạy để xác nhận** | A: giữ các target fault, `segment_id:null`, `position:null`, nhóm chưa tuyến cuối; B: refuse có lý do khi không thể trả đầy đủ. Không có phương án bỏ im lặng fault targets | **A**, giữ vận hành sửa/kiểm tra; chỉ target qua fault thật, chặn equality segment-null ghép hai null. Chốt schema position và thứ tự nhóm null (đề nghị created_at, length(pole_id), pole_id), cùng predicate cho list/ảnh; survey vẫn không nhận null |
| **D-2** | POST/PUT required; import header/Resolve required; list chỉ commune/page; filter feeder-unassigned có nghĩa riêng (E05/E06) | POST vắng = null hay phải gửi explicit null? PUT vắng: lỗi / giữ / xoá? Import blank trên existing: giữ hay tháo? Header bắt buộc hay optional? Filter dùng tên nào và áp endpoint nào? | POST vắng/null = null; **PUT vẫn bắt buộc key nhưng chấp nhận explicit null** để tránh tháo ngầm. Import new blank = null; existing blank/missing giữ tuyến, tháo qua PUT explicit null; nếu chọn replacement blank→null thì cần cảnh báo và test tương ứng. Header optional. Đề nghị `unassigned_segment=true` ở inventory, scope ReadNetwork/phân trang cũ; map/statistics mở thêm khi UI cần, không tái nghĩa feeder-unassigned. Chốt cả whitespace/sai kiểu GeoJSON, xung đột filter nếu có |
| **D-3** | Cabinet diagram cần đường chiếu; `ContainsKey(null)` không an toàn (E08) | A: refuse cả diagram có cột chưa tuyến và nêu pole IDs; B: trả cạnh hợp lệ **kèm** tập/đếm chưa tuyến tường minh theo shape được duyệt | **A** tối thiểu, UI dẫn người quản lý sang inventory để gán tuyến; không bỏ im lặng, không bịa cạnh. B chỉ làm nếu WP5 thống nhất shape bổ sung |
| **D-4** | Bundle scope/cache theo tuyến làm mất null targets/phiếu chỉ có null targets (E10; Contract §5.8 E13) | A: bundle mặc định union target pole/fault/WO chưa tuyến của phiếu mở được giao, có metadata scope/cache rõ; explicit segment bundle vẫn chỉ tuyến được yêu cầu. B: từ chối rõ tải offline phiếu ấy cho tới khi gán tuyến, không để thành công nhưng thiếu việc | **A** để kỹ sư đi trọn ca offline, nhưng phải chốt metadata + eviction khi phiếu đổi người/đóng và pole chuyển null↔assigned với WP6. Không kéo tất cả pole chưa tuyến của xã vào gói. Không thêm `null` vào segment_ids |
| **D-5** | Dời cột luôn `SingleAsync` đo tuyến (E05); đây là đường riêng, không phải gán tuyến | A: cho dời pole null mà không đo corridor chưa tồn tại, giữ audit/scope; B: refuse cho tới khi gán tuyến | **A**, vì chưa có tuyến để đo. Giữ corridor cho pole có tuyến; audit cần biểu đạt không đo segment_distance, không dựng số 0 giả |
| **D-6** | Tên tuyến inventory/map fallback rỗng; PublicQrInfo bắt buộc tên và scalar First không fallback (E08/E11/E18/E20) | A: `segment_name:null` và FE render nhãn; B: giữ string, trả nhãn “Chưa phân tuyến”; C: giữ rỗng nhưng UI phải phân biệt bằng segment_id ở response có ID | **A** nhất quán null tường minh, chấp nhận breaking tên/đồng bộ client. PublicQrInfo không có segment_id nên C không đủ cho QR. Không đổi QR code hay đánh dấu QR invalid |
| **D-7** | Fault/sweep/photo publisher copy tuyến **lúc tạo sự kiện**, agenda dùng tuyến pole hiện tại; snapshot run chứa tuyến cũ (E07/E09/E11) | A: gán/tháo tuyến chỉ đổi pole, giữ fault/history/cluster/snapshot cũ; B: cập nhật fault đang mở theo rule được duyệt, không sửa bất biến. Đồng thời quyết xử lý tháo/đổi tuyến trong run đang xử lý/chờ duyệt | **A** giữ sự kiện và snapshot; cần chốt conflict/diagnostic khi publish từ run cũ khác tuyến pole hiện tại. Không tự bulk sửa fault; thông báo rõ GET fault segment có thể khác pole segment và sync phải union theo cả fault/target |
| **D-8** | Survey input là segment_ids, không có pole_id để trực tiếp refuse; INNER JOIN làm cột chưa tuyến gần đường không lọt snapshot. Cluster vẫn bắt buộc segment (E07/E09/E11) | A: null không thuộc tập giao survey, UI giải thích/diagnostic có scope khi chọn tài sản; nếu thao tác thật sự yêu cầu pole ấy thì refuse. B: thêm diagnostic “unassigned near corridor” vào snapshot/coverage theo đặc tả riêng. Không suy tuyến hoặc gộp vào unknown của tuyến | **A** tối thiểu, cần Mỹ chỉ rõ bề mặt hiển thị lý do để không “exclude im lặng”. Nếu tiêu chí yêu cầu B thì phải chốt bbox/corridor/count/shape và snapshot version trước code. Cluster nhận pole không tuyến thì refuse, không nới fault_cluster schema |
| **D-9** | `FixtureStatusRow.segment_id:null` hiện cũng nghĩa “không group theo segment”, không riêng chưa tuyến (E12/E18) | A: group_by segment có nhóm null, aggregate vẫn kèm count chưa tuyến riêng; B: buộc mọi thống kê hiển thị thêm chiều segment; C: thêm discriminator cho rows | **A**, chốt tên/vị trí count và JSON/CSV với WP5. Không thêm giá trị enum “unassigned”; không gộp null vào tuyến khác hay out. Warranty null segment phải được hiển thị |
| **D-10** | affected IDs `.Length` trên null; uncontrollable tính theo wiring chứ không theo tuyến (E12) | A: lọc null khỏi affected_segment_ids, thêm count/IDs cột chưa tuyến bị ảnh hưởng; B: refuse request có cột chưa tuyến | **A**, phản ánh đúng feeder/rơ-le; chốt field minh bạch trên preview + kết quả/snapshot nếu cần. Không tăng uncontrollable_pole_count chỉ vì chưa tuyến; không làm phần tử segment_id của mảng nullable |
| **D-11** | Mock seed map demo_feeder theo segment; ví dụ hiện có dùng tuyến; photos_to_poles có cảnh báo “bắt buộc” (E15/E18) | A: giữ bộ mock demo và thêm ví dụ/test fixture null riêng; B: thay một phần mock hiện hữu bằng null và sửa toàn bộ số đếm/references | **A**, hạn chế đổi ID/cụm đã bàn giao WP5/WP6. Sửa hướng dẫn/cảnh báo trống tuyến của photos_to_poles, không sửa thuật toán tự gán tuyến/OSM ngoài scope |
| **D-12** | Down phải không xoá/đổi dữ liệu; mẫu preflight refusal có thật (E20) | A: Down từ chối khi còn null, sau khi người quản lý gán tuyến thật mới siết; B: chỉ rollback ứng dụng, giữ schema nullable và không chạy Down trên dữ liệu mới | **A** cho migration với thông điệp rõ; **B** là phương án vận hành khi chưa gán được. Review preflight/lock/SET NOT NULL trong transaction, không EF default/backfill ngầm |
| **D-13** | TOPO-INFER gắn nhãn cặp feeder/cabinet; Pole/config chưa có segment_source (E04/E13) | A: chỉ nullable, chưa thêm nhãn; B: thêm provenance cho segment ở ticket riêng với luật theo cặp + API/migration | **A** cho scope ticket này; Mỹ quyết có cần B sau hay không. Không tái dùng feeder_source cho quan hệ khác |
| **D-14** | Các đường cần từ chối chưa có mã/HTTP thống nhất cho pole chưa tuyến; hiện có VALIDATION_FAILED/ASSET_NOT_FOUND/POLE_OFF_SEGMENT (E17) | A: một code nghiệp vụ mới cùng reason/details theo thao tác; B: VALIDATION_FAILED cho mọi ca; hoặc tách validation 400 và conflict trạng thái 409 | Đề nghị **400 VALIDATION_FAILED** cho input sai, **409 với code mới** cho trạng thái tài sản chưa tuyến; tên ví dụ `POLE_SEGMENT_REQUIRED` chỉ là đề xuất. Mỹ chốt tên/details và các endpoint áp, không trả 404 như thể pole không tồn tại |

**Dừng Phase 1 tại đây.** Chờ Claude review báo cáo và Mỹ chốt D-item theo ticket; không tự chuyển sang Phase 2.
