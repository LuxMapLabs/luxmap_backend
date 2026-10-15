# POLE-PHOTO-AI — PHA 1: khảo sát và đề xuất

Ngày khảo sát: 15/10/2026. Người thực hiện: Codex. Branch thực tế: `docs/POLE-PHOTO-AI-p1`; HEAD: `9278eae`.

**Trạng thái: hoàn tất khảo sát, CHƯA có quyết định triển khai. Dừng tại PHA 1 để Mỹ chốt D-item.**

Phạm vi tuân theo `.ai/tasks/POLE-PHOTO-AI.md`: chỉ ghi file này; không sửa code, schema, Contract, drift, `CLAUDE.md`, dashboard; không commit/push; không đọc `.env`, gọi API thật, kết nối DB hay chạy migration/test. File task đang untracked từ trước được giữ nguyên. Đã đọc `.ai/context/sources.md` → task → `.ai/context/commands.md`, `.ai/README.md`, các mục hướng dẫn liên quan và mã nguồn dẫn dưới đây.

Các tên bảng, cột, route, port, mã lỗi mang nhãn **đề xuất** trong báo cáo chưa tồn tại/chưa được duyệt. Không được lấy báo cáo này làm quyết định API đã chốt. Contract §5.5 chủ động dẫn sang drift về hình dạng phiếu thực tế (`docs/api-contract-v1.1.md:707`); EV-1/EV-2, WO-12, AI-1, PC-8 là nền có ghi nhận, trong đó các quyết định SELF-SIGNED vẫn cần FW xác nhận (`docs/contract-drift.md:441`, `docs/contract-drift.md:686`, `docs/contract-drift.md:1188`, `docs/contract-drift.md:1296`). Không sửa các nguồn đó trong lượt này.

## 1. Đã có gì gần giống; mở rộng hay tách đường mới?

| Đường hiện có | Đã đáp ứng | Thiếu hoặc không phù hợp với đề xuất |
|---|---|---|
| `POST /work-orders/{id}/evidence` | Multipart JPEG, metadata chụp, khử trùng theo người gửi + `client_op_id`, lưu original/thumbnail; 201 mới, 200 replay. `ExecuteWorkOrders`; người được giao, `in_progress`. | `RepairEvidence` không có `pole_id`, `data_source`, kết quả AI. Kiểm tra chỉ `observation`, sửa chữa chỉ `before/after`, khảo sát không nhận. Nguồn: `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs:61`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:75`, `src/LuxMap.Modules.WorkOrders/Entities/RepairEvidence.cs:21`. |
| `POST /faults/{id}/photos` | Cùng service, bảng và kho evidence; chỉ người báo, fault mở, luôn `observation`. | Cha là fault, không phải đồng thời work order + pole; ép dùng đường này sẽ phải tạo fault trước, trái mục đích ảnh chỉ gợi ý. Nguồn: `src/LuxMap.Modules.WorkOrders/FaultPhotosController.cs:11`, `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:119`, `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:105`. |
| Gợi ý AI ảnh công dân | Lưu nhãn/confidence/phiên bản/thời điểm; đúng một bóng + ảnh đủ tối; model lỗi thì không làm hỏng request; audit `cv/analyzed`. | Thuộc `citizen_submission/report`, ảnh đã làm sạch, quyền ảnh là Quản lý. Không dùng entity/quyền công dân cho kỹ sư. Nguồn: `src/LuxMap.Modules.Citizen/Entities/CitizenReport.cs:133`, `src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:49`, `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:290`, `docs/contract-drift.md:1304`. |
| `pole_observation` / `survey_frame` | Observation có pole, nguồn dữ liệu, CV, peak lux/baseline; frame có clip/sweep/PTS/extractor; công bố sau review. | Không phải ảnh chụp rời. Observation cần run/pass; frame cần sweep/clip. Tạo run/pass giả để nhét ảnh sẽ phá nghĩa dữ liệu nghiên cứu. Nguồn: `src/LuxMap.Modules.Survey/Entities/SurveyProcessing.cs:61`, `src/LuxMap.Modules.Survey/Entities/SurveyFrames.cs:6`, `docs/database/erd.md:24`. |
| `POST /ai/detect` | Cùng YOLO, normal/out, bbox pixel, kiểm JPEG; không lưu. | Chỉ Quản lý (`ReviewSurveys`), không có parent/pole/idempotency. Không mở capability này cho kỹ sư để dùng tính năng mới. Nguồn: `src/LuxMap.Modules.AI/Controllers/AiController.cs:21`, `docs/contract-drift.md:1192`. |

**Phương án A — mở rộng evidence:** thêm `pole_id?`, `data_source?`, bộ trường AI; ảnh legacy vẫn null; có thể giữ route evidence hoặc thêm adapter `/pole-photos` nhưng vẫn một đường lưu ảnh. Ưu: ít hệ thống media mới, dùng lại phân quyền/xem ảnh/list. Nhược: chỉ thật sự gọn nếu giới hạn vào inspection; cho survey hoặc repair không có `kind` sẽ phải đổi EV-1 và quy tắc hoàn thành. Gắn `IAudited` vào toàn bộ `RepairEvidence` còn buộc sửa mọi writer/fixture evidence vốn chưa có marker đó (`src/LuxMap.Modules.WorkOrders/Entities/RepairEvidence.cs:21`, `src/LuxMap.Persistence/Audit/AuditWriteGuard.cs:16`). Biến thể bảng phụ 1–1 AI/pole trên evidence giữ nguyên bytes nhưng tăng hai cha/hai bước ghi, vẫn không giải quyết survey/kind.

**Phương án B — endpoint như yêu cầu + bảng hẹp `work_order_pole_photo` (đề xuất):** độc lập evidence before/after, lưu ảnh và gợi ý theo cặp phiếu/cột; tái dùng kho/codec, model và luật gợi ý. Ưu: không làm một ảnh AI vô tình thành ảnh `after`, hỗ trợ ba loại phiếu mà không đổi luồng survey. Nhược: thêm bảng/sequence, list/detail/media, teardown/PLAN và kiểm quyền riêng theo cha. Đây là chi phí thật, không coi endpoint mới là miễn phí.

**Khuyến nghị B nếu tính năng dành cho mọi loại phiếu như cách diễn đạt hiện tại; nếu Mỹ chỉ cần inspection thì A đáng chọn hơn.** Đây là D-1/D-4, chưa tự chốt. Các mục 4–9 mô tả phương án B để có thiết kế cụ thể mà review; không phải lệnh triển khai.

## 2. `classified_as` trả gì được thật?

Model chỉ có hai lớp theo thứ tự `normal`, `out` (`src/LuxMap.Modules.AI/Detection/YoloModel.cs:44`); adapter survey ánh xạ chúng thành `on`, `off` (`src/LuxMap.Modules.AI/Detection/YoloOnOffDetector.cs:28`). Phiếu v1.4 yêu cầu ON/OFF kết hợp BH1750 + baseline riêng từng cột để suy Normal/Dim/Out (`docs/registration/FA26SE222_v1.4.md:53`, `docs/registration/FA26SE222_v1.4.md:59`). **Ảnh đơn không được trả `dim`, cũng không chứng minh độ sáng đạt baseline.**

Luật hiện có, cần tái dùng nguyên nghĩa:

- YOLO lọc confidence mặc định **0.25**, NMS IoU **0.45**, sau đó mới đếm detection. Giá trị cấu hình có thể đổi; không thêm hằng confidence riêng cho pole-photo (`src/LuxMap.Modules.AI/AiOptions.cs:17`, `src/LuxMap.Modules.AI/Detection/YoloPostprocess.cs:28`, `src/LuxMap.Modules.AI/Detection/YoloPostprocess.cs:81`). Confidence đúng bằng ngưỡng không bị lọc (`src/LuxMap.Modules.AI/Detection/YoloPostprocess.cs:47`).
- Hint chỉ kết luận khi **đúng một detection sau hậu xử lý**, mean luma thumbnail **≤ 0.45** mặc định, score hữu hạn; lớp lạ/0 bóng/nhiều bóng/ảnh quá sáng → `inconclusive`, confidence null (`src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:76`, `src/LuxMap.Modules.Citizen/CitizenOptions.cs:27`). Tại luma = ngưỡng vẫn được xét; giờ chụp và EXIF không thay luật này.
- Đây là heuristic độ sáng trung bình ảnh, **không phải máy xác minh ban ngày**. Ảnh đêm quá sáng có thể không kết luận; ảnh ban ngày tối có thể lọt. Đúng một detection cũng không chứng minh đó là bóng của pole đã chọn. Các test chỉ canh hợp đồng/luật, không chứng minh độ chính xác; một ảnh đen tổng hợp từng bị model nhận là một bóng tắt (`tests/LuxMap.Api.Tests/CitizenReviewTests.cs:294`). WP4 cần mẫu thực để đánh giá.

**Ánh xạ API đề xuất (D-2):**

| Hint chung | `cv_state` | `classified_as` của ảnh | Confidence |
|---|---|---|---|
| `lamp_on` | `on` | `normal` **chỉ với nghĩa nhãn ON của model** | Score model, hữu hạn 0..1 |
| `lamp_off` | `off` | `out` | Score model, hữu hạn 0..1 |
| `inconclusive` | null | `unknown` | null |
| Không chạy được AI | null | null | null |

Khuyến nghị ánh xạ trên để giữ tên `classified_as` theo yêu cầu, nhưng bắt buộc response có `classification_scope = "on_off_hint"`, `dim_evaluation_eligible = false`; WP5/WP6 hiển thị **“đang bật”**, không hiển thị “độ sáng bình thường”. `unknown` ở đây chỉ là ảnh không kết luận, không ghi thành trạng thái unknown của cột. Phương án chặt nghĩa hơn là chỉ trả `on/off/inconclusive` trong một enum hint riêng và không gọi là fixture classification; Mỹ cần chốt D-2 trước khi đặt DTO/CHECK. Không thêm giá trị vào enum `fixture_status` (`docs/api-contract-v1.1.md:260`).

**Không viết luật thứ hai:** tách phần trung lập của `YoloCitizenPhotoHinter` sang adapter AI dùng chung; port/record trung lập trong Shared (tên đề xuất `ISingleLampPhotoHinter`), wrapper Citizen chỉ chuyển nhãn sang enum hiện có. WorkOrders chỉ phụ thuộc port, không gọi `AiController` và không inject `YoloModel`. Giữ lazy resolve + fallback null và giữ nguyên cách băm version khi kết quả không đổi (`src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:40`, `src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:54`, `src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:111`). Nơi đăng ký hiện nay là `src/LuxMap.Modules.Citizen/CitizenModule.cs:23`.

Lý do port cần thiết: AI → Survey → WorkOrders đã tồn tại; thêm WorkOrders → AI tạo vòng (`src/LuxMap.Modules.AI/LuxMap.Modules.AI.csproj:8`, `src/LuxMap.Modules.Survey/LuxMap.Modules.Survey.csproj:4`). Luật gợi ý chung cần một nguồn cấu hình daylight; khuyến nghị giữ tương thích khóa `Citizen:DaylightMeanLuma` khi tách, đọc một lần cho adapter chung, chưa đổi tên/copy thêm option cho pole-photo. Chuyển chủ sở hữu cấu hình là D-3; nếu đổi key phải có alias/migration cấu hình, không làm thay đổi gợi ý Citizen âm thầm.

## 3. Có đổi trạng thái cột, luminance hay tự sinh fault không?

**Đề xuất không.** Ảnh chỉ lưu gợi ý cho kỹ sư/Quản lý xem, không ghi `pole_observation`, `pole_current_status`, `luminance_history`, baseline, detection của survey; không tự tạo/đóng fault, không cập nhật kết quả inspection hay tự hoàn thành phiếu.

Căn cứ: Q5 chỉ công bố sau Quản lý chấp nhận sweep (`docs/database/erd.md:28`). Code review gọi `Publish` riêng khi accept (`src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:243`); `Publish` thêm history, cập nhật current status và baseline rồi tạo fault có `origin_observation_id`, model version, audit (`src/LuxMap.Modules.Survey/Review/SurveyReviewService.cs:268`). WO-12 cũng quy định trạng thái chỉ đổi qua sweep công bố (`docs/contract-drift.md:441`).

Pipeline hiện trả `normal` khi CV ON nhưng thiếu baseline/peak, kèm lý do và không đủ điều kiện đánh giá dim (`src/LuxMap.Modules.Survey/Processing/Frames/FrameClassification.cs:20`). Điều đó **không** cấp cho ảnh chụp rời quyền ghi trạng thái hay lấy ảnh làm số đo lux. Lux thủ công và chuỗi luminance vốn khác nhau (`CLAUDE.md:621`). Kết luận này là D-5 để Mỹ xác nhận, không phải mở thêm đường công bố.

## 4. Phiếu, capability, tập cột, vị trí

Đề xuất áp dụng cho `survey`, `inspection`, `repair`, **chỉ `in_progress`**, chỉ kỹ sư đang được giao. Lý do cho phép survey: đây là ảnh ghi nhận bổ sung, không thay video/log/đồng bộ sweep. Lý do cho phép repair: không có kind nên nó không thay bằng chứng before/after. Nếu Mỹ chỉ cần inspection, chuyển sang phương án A mục 1. Ba loại phiếu hiện có ở `src/LuxMap.Modules.WorkOrders/Entities/WorkOrder.cs:7`; evidence hiện chỉ cho hai loại ở `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:75`.

- **POST:** dùng capability sẵn `ExecuteWorkOrders`, không mở `ReviewSurveys`. Matrix hiện chỉ cho `field_engineer` thực thi (`src/LuxMap.Shared/Authorization/LuxMapPolicies.cs:138`). Không cần capability mới nếu Mỹ đồng ý đây là thao tác thực thi phiếu (D-6).
- **Đọc list/detail/bytes:** `ReadWorkOrders` + parent qua filter xã/người được giao + filter xã của ảnh/cột. Kỹ sư khác cùng xã vẫn 404, kể cả biết `photo_id`; khi phiếu giao lại, người cũ mất quyền đọc. Mẫu hiện có kiểm parent riêng ở `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:70` và `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:242`. Không lấy `uploaded_by == tôi` làm quyền đọc vĩnh viễn.
- **`pole_id` hợp lệ:** thuộc tập WO-12 = cột trên các tuyến của phiếu (bảng `work_order_segment` + `segment_id`) **hợp** cột mang fault của phiếu, sau filter xã (`src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:78`). Tách biểu thức membership dùng chung cho WO-12 và upload, không gọi list có phân trang rồi chỉ kiểm 50 cột đầu. Không thu hẹp riêng repair về cột fault nếu đã hứa “cùng WO-12”.
- **Đường liên xã:** tập trên có thể gồm cột thuộc xã khác xã chủ quản phiếu nếu người gọi có cả hai xã; WO-12 cố ý đọc geometry tuyến ngoài filter chỉ để sắp (`src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:99`, `src/LuxMap.Modules.WorkOrders/WorkOrderPoles.cs:117`). `commune_id` ảnh lấy **từ pole**, không lấy từ đường/phiếu. Không âm thầm ép cột cùng xã phiếu bằng FK ghép. D-7 cần chốt vì ảnh evidence hiện mang xã của phiếu, FK ghép theo phiếu (`src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:116`).
- **GPS:** bắt buộc số hữu hạn, lat ∈ [-90,90], lng ∈ [-180,180], UTC đầy đủ. Khuyến nghị đợt đầu chưa hard-reject theo khoảng cách: GPS và việc chọn đúng bóng trong ảnh là hai vấn đề khác nhau. Nếu Mỹ cần proximity, đề xuất cảnh báo xa trước, tính mét bằng `SpatialFunctions.DistanceMeters`, ngưỡng phải chốt riêng, không lấy bừa hành lang survey 25 m hay ngưỡng công dân 200 m làm quy tắc kỹ sư. Mẫu tính mét và cờ công dân: `src/LuxMap.Modules.Citizen/PublicReportService.cs:306`, `src/LuxMap.Modules.Citizen/CitizenOptions.cs:24`. D-8 chọn không kiểm/cảnh báo/từ chối + ngưỡng.

Thứ tự đề xuất: kiểm capability → parse/hash có giới hạn → parent qua filter + assignee → pole qua filter → replay → membership và điều kiện loại/trạng thái cho ảnh mới. Sau upload/suy luận, transaction ngắn khóa parent rồi pole, đọc lại quyền/membership/nguồn dữ liệu trước insert. Nếu metadata cột hoặc parent đổi giữa chừng, không ghi row sai phạm vi. Cần kiểm thứ tự khóa với các writer liên quan trước Pha 2; không khóa ảnh trước rồi quay lại parent. Replay vẫn phải qua quyền hiện tại, nhưng không bị chặn chỉ vì phiếu đã `done` hoặc tuyến của phiếu đổi sau lần nhận (cùng nguyên tắc evidence ở `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:160`).

## 5. Lưu trữ và audit — thiết kế để review, chưa tạo bảng

### 5.1 Bảng `work_order_pole_photo` đề xuất cho phương án B

Entity đề xuất `WorkOrderPolePhoto : ICommuneScoped, IAudited`. `ICommuneScoped` vì ảnh có thể là query root theo ID; marker audit vì AI là phát hiện tự động (quy ước `CLAUDE.md:1388`, guard `src/LuxMap.Persistence/Audit/AuditWriteGuard.cs:16`).

| Nhóm | Cột đề xuất |
|---|---|
| Danh tính / liên kết | `photo_id text PK` do DB sinh, đề xuất `PPH-000001` + `pole_photo_id_seq` (prefix mới phải được duyệt); `work_order_id text NOT NULL`, `pole_id text NOT NULL`, `commune_id text NOT NULL`, `data_source text NOT NULL` lấy từ pole khi nhận |
| Chụp / tải | `captured_at timestamptz`, `lat/lng double precision`, `uploaded_by text`, `uploaded_at timestamptz`; tất cả NOT NULL |
| Media | `object_key`, `thumbnail_key` NOT NULL; `byte_count`, `thumbnail_bytes bigint > 0`; `photo_sha256 text` của bytes gốc; `width`, `height int > 0` theo ảnh sau EXIF orientation |
| Replay | `client_op_id uuid NOT NULL` khác UUID rỗng; `request_hash text NOT NULL` |
| AI | `classified_as text NULL`, `confidence double precision NULL`, `model_version text NULL` (detector identity), `artifact_version text NULL` (identity detector + luật hint), `analyzed_at timestamptz NULL` |

Không lưu `cv_state`/scope/dim eligibility thành bản sao không cần thiết: response suy từ nhãn và loại tài nguyên. Chưa lưu bbox trong phương án tối thiểu; mục 8/D-13 nêu nhánh có bbox. `classified_as = NULL` là chưa có kết quả/model không dùng được; `unknown` là đã chạy và không kết luận. Không ghi giả model_version khi model chưa nạp được.

FK/index/CHECK đề xuất:

- FK `work_order_id → work_order`, `pole_id → pole`, `uploaded_by → app_user`, `commune_id → administrative_unit`, tất cả **RESTRICT**. Thêm ràng buộc DB `(pole_id, commune_id) → pole(pole_id, commune_id)` để không thể đặt ảnh vào sai xã. Pole hiện chưa khai alternate key đó trong cấu hình (`src/LuxMap.Modules.Assets/Configurations/AssetConfigurations.cs:190`); đây là **schema bổ sung**, không giả định có sẵn. Khuyến nghị unique target + FK ghép tại DB theo khuôn CAB-4 nếu cần giữ `CommuneId` không thành EF key; đọc bẫy ở `src/LuxMap.Modules.Assets/Entities/CabinetConstraints.cs:8`. Test catalog bắt buộc cho constraint ngoài snapshot. D-7 phải chốt cách ràng buộc và hệ quả khi đổi xã của pole.
- **Không** ghép `commune_id` pole vào FK work order nếu chọn hỗ trợ tập WO-12 liên xã. FK work order đơn + service kiểm parent qua filter; quyền ảnh luôn là giao giữa hai phạm vi. Membership theo tuyến/fault thay đổi được nên kiểm service dưới khóa; không hứa có FK chứng minh “pole thuộc phiếu”. Không ép FK equality `data_source`: đây là snapshot nguồn lúc nhận, service lấy từ pole và kiểm lại khi commit (D-9).
- Unique toàn bảng `(uploaded_by, client_op_id)` vì client_op_id bắt buộc; index FK đầy đủ, index list `(work_order_id, captured_at, uploaded_at)` và `(pole_id, captured_at)`; index `commune_id`. Unique replay đã có leading uploaded_by nên phủ FK đó; nếu Mỹ đổi client_op_id thành nullable/partial phải khai thêm FK index toàn phần theo mẫu `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs:124`.
- CHECK chuỗi keys không rỗng; SHA/hash `^[0-9a-f]{64}$`; enum data_source dùng cơ chế contract enum; size dương, dimensions dương; cặp lat/lng đúng giới hạn và hữu hạn. Confidence phải cùng null với `classified_as IS NULL OR classified_as = 'unknown'`; score hợp lệ 0..1; nhãn chỉ `normal/out/unknown` nếu D-2 được duyệt. Model/artifact/analyzed_at đi đủ bộ khi có classified_as, tất cả null khi chưa phân tích; thêm `IS NOT NULL` tường minh ở nhánh conclusive để CHECK không lọt qua SQL NULL.
- Mọi `double` có cả validate `double.IsFinite` ở biên/service và CHECK DB. Với miền có cận trên hữu hạn, dùng `BETWEEN ... AND ... AND x <> 'NaN'::float8`; ±Infinity đã bị cận trên/dưới chặn. Nếu thêm distance/luma/bbox, áp đúng miền và loại NaN/±Infinity; không dùng `x=x` để bắt NaN (`CLAUDE.md:966`). Không cho named floating-point literals lọt JSON/form. Với bbox tùy chọn cần đầy đủ tọa độ, không lưu width/height trùng dữ liệu suy ra.
- ID dùng `PrefixedIdSpec.DefaultValueSql` / `luxmap_format_id`, không LPAD và không UUID làm ID hiển thị; ID mới vào registry/test (`src/LuxMap.Shared/Contracts/PrefixedId.cs:30`, `src/LuxMap.Shared/Contracts/PrefixedId.cs:94`). Tên PPH/sequence ở đây chỉ là đề xuất D-9.

**Phiên bản:** giữ các chuỗi phiên bản như precedent Citizen, không tạo registry AI thứ hai. Detector version hiện băm model SHA, ngưỡng, resize, EXIF và mapping (`src/LuxMap.Modules.AI/Detection/YoloOnOffDetector.cs:39`); hint version cộng băm luật daylight/single-lamp (`src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:111`). Registry `artifact_version` hiện thuộc Survey (`src/LuxMap.Modules.Survey/Entities/SurveyProcessing.cs:7`, `src/LuxMap.Modules.Survey/Configurations/SurveyProcessingConfigurations.cs:9`). Nếu yêu cầu FK vào registry chung Q8 ngay đợt này, cần một port/adapter riêng, không thêm WorkOrders → Survey gây vòng; đây là lựa chọn D-10. Chuỗi phiên bản không tự bảo đảm còn bytes model cũ: WP4/BE-34 vẫn phải giữ artifact để tái lập.

### 5.2 Kho ảnh, original và thumbnail

Khuyến nghị dùng bucket **`luxmap-evidence`** có sẵn; keys `original/{photo_id}.jpg`, `thumb/{photo_id}.jpg`, prefix ID PPH tách EVD; không chứa commune. Không cần bucket hay presigned URL mới. Tên/key chuẩn hiện có ở `src/LuxMap.Shared/Storage/ObjectStorage.cs:51`; API kho ảnh ở `src/LuxMap.Shared/Storage/ObjectStorage.cs:136`.

Khuyến nghị **original nguyên byte** giống ảnh kỹ sư, thumbnail riêng bỏ EXIF/XMP/IPTC. Ngoại lệ sanitize original hiện được ghi riêng cho ảnh công dân (`CLAUDE.md:1884`), không tự mở rộng ngoại lệ. Việc infer AutoOrient trong RAM không thay bytes gốc (`src/LuxMap.Modules.AI/Detection/YoloModel.cs:131`). Thumbnail hiện 320/q80, strip metadata tường minh (`src/LuxMap.Infrastructure.Storage/ThumbnailFactory.cs:20`, `src/LuxMap.Infrastructure.Storage/ThumbnailFactory.cs:80`). Cần phân biệt chọn original nguyên byte với chọn làm sạch để giảm dữ liệu riêng tư; Mỹ chốt D-11.

**Bẫy phải giải ở Pha 2:** `ImagePipeline.PrepareAsync` buffer rồi tạo thumbnail ngay, chưa có bước Identify/giới hạn pixel (`src/LuxMap.Infrastructure.Storage/ImagePipeline.cs:55`); `ThumbnailFactory` decode ở `src/LuxMap.Infrastructure.Storage/ThumbnailFactory.cs:62`. Vì vậy chỉ gọi AI sau `StoreImageAsync` không bảo vệ decode thumbnail trước ảnh JPEG cực lớn. Luồng mới phải đọc có giới hạn byte, kiểm magic + Identify bằng JpegOnly và cap **40 MP trước mọi decode**, kể cả khi thiếu model; tái dùng helper thay vì viết codec khác. Không biến lỗi ảnh hỏng thành “AI unavailable”.

Object trước row sau, không giữ transaction/row lock qua stream hoặc decode/model. Lỗi ghi original/thumbnail thì không commit row; lỗi DB sau object có thể để object mồ côi (nợ BE-35), không hứa dọn đồng bộ đã có (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:164`, `CLAUDE.md:518`). Thua replay cùng ID thao tác không được ghi đè key của ảnh thắng: cấp photo_id riêng cho lượt mới, chấp nhận orphan khi đua.

Media endpoint đề xuất riêng dưới phiếu, proxy qua API, kiểm parent và scope ảnh trước khi mở object; `Cache-Control: no-store` đề xuất để thu hồi quyền có hiệu lực trên lượt tải tiếp. Không emit bucket/object key ra response. Đọc thiếu object dùng 503 `STORAGE_OBJECT_MISSING` như adapter hiện tại (`src/LuxMap.Infrastructure.Storage/S3ObjectStore.cs:55`). Thời hạn giữ ảnh kỹ sư theo D-R16 tối thiểu qua bảo hành, không tự xóa khi hết bảo hành (`docs/api-contract-v1.1.md:320`).

### 5.3 Audit

Khuyến nghị dùng **entity audit `work_order` có sẵn**; `entity_id` là phiếu, after_state nêu `photo_id`, `pole_id`, `data_source`, client_op_id/hash và metadata kết quả, không nhúng bytes/key ảnh. Không cần thêm AuditEntityType. `Submitted` và `Analyzed`, actor `User`/`Cv` đều đã có (`src/LuxMap.Persistence/Audit/AuditEvent.cs:25`). Commune audit theo pole/ảnh; parent phiếu vẫn được nêu trong state, bao gồm ca liên xã. D-12 cần chốt điều này.

- Ghi object và chạy AI **trước transaction**, kết quả tạm trong RAM; model lỗi thì giữ kết quả tạm null, vẫn đi tiếp tới nhận ảnh hợp lệ.
- Một transaction ngắn sau suy luận: khóa parent → pole, kiểm lại và kiểm replay đua; insert ảnh với AI null + **một** audit `user/submitted`, SaveChanges. Nếu có kết quả, cập nhật bộ AI + **một** audit `cv/analyzed`, SaveChanges lần hai; **commit chung**. Audit CV không giả danh kỹ sư; uploaded_by/correlation giữ dấu người tải.
- Hai save nhưng một commit để không có cửa sổ replay nhìn thấy ảnh AI null trong khi request gốc sắp ghi nhãn. Thua unique/khóa thì rollback rồi replay snapshot đã commit, không thêm analyzed. Không tạo hai event trong một SaveChanges. Tiền lệ suy luận ngoài khóa ở `src/LuxMap.Modules.Citizen/CitizenReviewService.cs:295`; yêu cầu một event/save ở `CLAUDE.md:1510`.
- Parent đổi người/đóng trong lúc infer: recheck từ chối ảnh mới; object có thể mồ côi nhưng không có row đã nhận sai quyền. Tải ảnh chỉ thành công khi transaction metadata + audit commit. Lỗi audit/DB vẫn là lỗi request, không giả báo thành công. D-12/D-14 cần chốt phương án này; không ngầm mở quyền cho request cũ.

### 5.4 Teardown và copy dữ liệu

Bảng mới phải được xóa theo dữ liệu test **trước work_order/pole/app_user/commune**, audit liên quan dọn trước theo cơ chế test. Các danh sách teardown hiện nằm ở `tests/LuxMap.Api.Tests/WorkOrderTests.cs:90`, `tests/LuxMap.Api.Tests/SyncTests.cs:102`, `tests/LuxMap.Api.Tests/FaultReportTests.cs:90`; fixture chung có cleanup Citizen ở `tests/LuxMap.Api.Tests/AssetImportFixture.cs:182`. Không xóa toàn bảng dùng chung hay dùng cùng prefix account với class khác.

Thêm bảng vào `scripts/copy_dev_to_supabase.py` **PLAN** sau cha đã copy và trước audit. Filter phải giao giữa xã ảnh, pole được giữ, work order được giữ và uploaded_by được giữ; chỉ `IN_COMMUNES` chưa đủ cho FK người gửi/phiếu liên xã. Thứ tự/hàng lọc hiện có ở `scripts/copy_dev_to_supabase.py:70`; bảng chưa có PLAN bị từ chối ở `scripts/copy_dev_to_supabase.py:318`. Script PLAN không thay việc chuyển bytes MinIO; cần ghi rõ công việc chuyển object nếu vận hành đích. Không chạy script trong PHA 1.

## 6. Đồng bộ trong request hay nền; lỗi model

**Khuyến nghị D-14:** đợt đầu suy luận đồng bộ **sau khi ghi object, trước transaction commit row**, rồi lưu ảnh và kết quả/audit bằng transaction ngắn mục 5.3. Không tạo worker/queue mới ngay; ưu điểm là mobile có gợi ý lập tức, replay nhận cùng snapshot hoàn chỉnh, ít trạng thái vận hành. Nhược điểm là thời gian chờ inference/semaphore, cold start và client ngắt kết nối. Chỉ khẳng định latency sau đo Pha 2, không hứa SLO từ comment.

Model singleton lazy, có semaphore giới hạn mặc định 2 cho cả decode/resize/inference (`src/LuxMap.Modules.AI/AIModule.cs:23`, `src/LuxMap.Modules.AI/Detection/YoloModel.cs:88`). Khởi tạo model thiếu file/sai SHA ném exception (`src/LuxMap.Modules.AI/Detection/YoloModel.cs:52`); vì thế service đường upload **không inject model trực tiếp**, dùng port lazy như Citizen. `OperationCanceledException` phải được truyền đúng nghĩa, không coi là thiếu model (`src/LuxMap.Modules.Citizen/CitizenPhotoHints.cs:65`).

Model không khả dụng/lỗi suy luận → vẫn commit ảnh hợp lệ và trả **201** với `ai_status = "unavailable"`, nhãn/confidence/version null. JPEG sai/hỏng/quá pixel phải bị từ chối trước ghi object, không fallback thành ảnh hợp lệ. Object store/DB/audit hỏng không được trả 201. Với request bị hủy trước commit có thể còn orphan object, retry được thử lại; nếu commit đã thành công nhưng client chưa nhận response, retry trả **200** cùng ảnh/kết quả đã ghi, không suy luận/lưu/audit lại.

Khuyến nghị tối thiểu chưa tự retry AI qua GET/replay: `ai_status` suy từ row (chưa có result = unavailable) và kết quả đầu tiên đã ghi là bất biến. Nếu muốn phục hồi ảnh chưa có AI khi model trở lại, chọn rõ một thao tác retry hoặc job bền vững; không fire-and-forget giữ scoped DbContext và không gọi nó là “queue” nếu không có lease/retry. Nhánh nền sẽ cần `pending/processing/failed`, lease, polling và audit hệ thống riêng — đưa vào D-14, không lén thêm.

**Chưa đo inference.** Không chạy API thật, không dựng harness hoặc sửa test/code để benchmark; không coi “vài trăm ms” trong comment `src/LuxMap.Modules.AI/AiOptions.cs:21` là số đo của máy này. Pha 2 nên đo cold model load, warm inference và tổng upload (ảnh thực + ảnh 40 MP, có tranh semaphore), báo phân vị và cấu hình máy; precision/recall cần mẫu WP4, khác latency.

## 7. Idempotency và offline

**Khác biệt cần chốt:** evidence chỉ so cùng user/key + parent/kind, **không so bytes, captured_at, lat/lng** (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:208`). Vì thế không được tuyên bố tái dùng replay evidence là có kiểm “cùng khóa khác nội dung”. Citizen có request hash gồm content và hash từng ảnh (`src/LuxMap.Modules.Citizen/PublicReportService.cs:288`) — phù hợp hơn cho yêu cầu mới.

Đề xuất D-15:

1. `client_op_id` bắt buộc UUID khác rỗng; lưu trên row ảnh, unique `(uploaded_by, client_op_id)`. Không dùng `sync_operation` cho multipart. Không hứa namespace UUID toàn hệ thống nguyên tử: BE-43 cũng ghi kiểm chéo khóa giữa các bảng chỉ best effort (`CLAUDE.md:1983`).
2. `request_hash = SHA256` của biểu diễn chuẩn `{work_order_id, pole_id, captured_at UTC đã chuẩn hóa microsecond, lat, lng, SHA256(bytes gốc)}`. Dùng số invariant, coi ±0 cùng một giá trị; bỏ filename/Content-Type/multipart boundary. Không đưa trạng thái cột, model version, commune/data_source hiện tại vào hash vì đó không phải nội dung client gửi; server đổi model không được đổi kết quả replay.
3. Kiểm quyền hiện tại trước replay; cùng key/hash → **200 cùng ảnh và kết quả đã lưu**, không thêm object/row/audit, không chạy AI lại. Loại/trạng thái phiếu đổi sau lần nhận không ngăn replay, nhưng thu hồi xã/giao lại phiếu vẫn 404. Với ảnh đã nhận, membership đã được xác nhận lúc commit; replay không xét lại tuyến mới để làm mất khả năng khử trùng.
4. Cùng key khác work order/pole/metadata/bytes → **409 IDEMPOTENCY_CONFLICT** sau kiểm visibility thích hợp, không tiết lộ tài nguyên ngoài scope. Hash phải đọc ảnh có bounded stream; dù replay cũng không được đọc vô hạn.
5. Đồng thời hai request: unique là backstop; chỉ bắt đúng constraint replay, rollback transaction lỗi, detach/clear đúng entity rồi đọc lại qua quyền và so hash. Không biến mọi unique violation thành replay như một shortcut. Hai request có thể cùng infer ngoài khóa; chỉ ảnh và bộ kết quả của lượt commit thắng được giữ, lượt thua trả snapshot đó. Lượt thua có thể để orphan object; không ghi đè ảnh thắng.

**Không cần `op_type` mới trong `/sync/push` ở đợt này.** Push hiện là JSON payload của 5 loại thao tác, không chứa ảnh (`src/LuxMap.Modules.Sync/SyncContracts.cs:49`); mỗi loại gọi service gốc và kiểm capability riêng (`src/LuxMap.Modules.Sync/SyncPushService.cs:24`, `src/LuxMap.Modules.Sync/SyncPushService.cs:198`). Không nhét JPEG/base64 vào JSON để có vẻ offline.

Trình tự mobile đề xuất D-16: giữ file gốc trên máy cùng UUID và metadata; đồng bộ `work_order_start` nếu cần → upload ảnh multipart với UUID cố định → xử lý 201/200 và lưu photo_id → gửi complete. **Ảnh cần upload trước complete** vì server chỉ nhận mới khi `in_progress`; đặc biệt ảnh `after` hiện vẫn đi evidence để vượt điều kiện hoàn thành (`docs/contract-drift.md:704`). Nếu upload lỗi hoặc 409 do server đóng phiếu/giao lại, giữ hàng chờ cho người dùng xử lý, không sửa UUID để lách conflict. Không có AI khi mất mạng; app hiển thị chờ phân tích, không gọi nhãn offline là kết quả server. Bundle chưa có media của tính năng mới; không âm thầm mở rộng bundle. Nếu cần phục hồi lịch sử ảnh sau cài lại app hoặc danh sách theo pole toàn hệ thống, tách quyết định API riêng.

## 8. Response và endpoint đọc lại đề xuất

Phương án B: multipart `pole_id`, **`photo`** (khác `file` evidence / `image` AI), `captured_at`, `lat`, `lng`, `client_op_id`, tất cả bắt buộc. Không nhận trường server-owned như `commune_id`, `data_source`, `classified_as`, `model_version`. D-17 chốt timestamp: khuyến nghị theo evidence nhận ISO đầy đủ có Z/offset, quy UTC; yêu cầu timestamp không timezone phải được ghi rõ nếu tiếp tục tương thích parser cũ (`src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:139`).

Response 201 mới / 200 replay cùng schema, snake_case; ví dụ minh họa, không phải output API đã chạy:

```json
{
  "photo_id": "PPH-000001",
  "work_order_id": "WO-0001",
  "pole_id": "POLE-0001",
  "data_source": "field",
  "captured_at": "2026-10-15T13:30:00Z",
  "lat": 10.8,
  "lng": 106.6,
  "uploaded_at": "2026-10-15T13:30:05Z",
  "uploaded_by": "USR-0001",
  "ai_status": "completed",
  "classification_scope": "on_off_hint",
  "cv_state": "on",
  "classified_as": "normal",
  "confidence": 0.87,
  "model_version": "yolo-<16 ký tự hash detector>",
  "artifact_version": "yolo-<hash detector>+cq-<hash luật>",
  "analyzed_at": "2026-10-15T13:30:06Z",
  "dim_evaluation_eligible": false,
  "width": 1920,
  "height": 1080,
  "thumbnail_url": "/api/v1/work-orders/WO-0001/pole-photos/PPH-000001/thumbnail",
  "original_url": "/api/v1/work-orders/WO-0001/pole-photos/PPH-000001/original"
}
```

Ảnh đã chạy nhưng không kết luận: `ai_status=completed`, `classified_as=unknown`, `cv_state/confidence=null`, vẫn có model/artifact/analyzed_at. Model lỗi/chưa có kết quả: `ai_status=unavailable`, toàn bộ trường AI nullable null; vẫn có photo_id/URL. Không dùng 0 thay confidence null. Không trả `count/detections[]` như raw `/ai/detect` để client tự quyết một luật mới.

**BBox:** khuyến nghị chưa emit/lưu ở bản tối thiểu vì UI cần nhãn, không cần chọn bóng. Nếu WP6 cần overlay, D-13 chọn thêm `bounding_box{x1,y1,x2,y2}` pixel của ảnh đã AutoOrient cùng width/height, chỉ của một detection kết luận; còn lại null. Không dùng bbox YOLO trước letterbox, không trộn normalized box survey. Tọa độ pixel/EXIF của raw model đã được định nghĩa ở `src/LuxMap.Modules.AI/Detection/YoloModel.cs:17` và `src/LuxMap.Modules.AI/Detection/YoloOnOffDetector.cs:64`. Trường bbox mới phải tham gia CHECK hữu hạn, biên ảnh và phiên bản luật khi làm thay đổi output.

| Endpoint đề xuất | Quyền / hành vi |
|---|---|
| `POST /api/v1/work-orders/{id}/pole-photos` | `ExecuteWorkOrders`, tạo 201 / replay 200 |
| `GET /api/v1/work-orders/{id}/pole-photos?pole_id=&page=&page_size=` | `ReadWorkOrders`, parent + commune ảnh; phân trang chuẩn, page_size ≤200; lọc pole trong đúng phiếu |
| `GET /api/v1/work-orders/{id}/pole-photos/{photo_id}` | `ReadWorkOrders`, đúng cả parent và photo; trả snapshot đã lưu, GET không infer |
| `GET /api/v1/work-orders/{id}/pole-photos/{photo_id}/thumbnail` và `/original` | `ReadWorkOrders`, cùng phép kiểm list/detail; proxy JPEG, no-store |

Thứ tự list: captured_at, uploaded_at, length(photo_id), photo_id; không sắp chuỗi ID trần (mẫu evidence tại `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs:233`). **Chưa thêm `/map/poles/{id}/pole-photos`**: quyền xem bản đồ không đủ thay quyền xem từng phiếu; nếu sau này liệt kê theo cột xuyên phiếu phải join từng parent và chỉ liệt kê URL người gọi mở được (D-13).

| HTTP | Mã / điều kiện đề xuất |
|---|---|
| 400 | `VALIDATION_FAILED`: thiếu photo/pole_id/metadata/UUID, UTC sai, số không hữu hạn, quá 40 MP, gửi trường server-owned; `POLE_PHOTO_TASK_KIND_NOT_ALLOWED` nếu Mỹ hạn chế loại phiếu |
| 401 | `UNAUTHENTICATED` theo cơ chế chung |
| 403 | `ROLE_FORBIDDEN` khi thiếu capability; không dùng 403 để báo tài nguyên xã khác tồn tại (`docs/api-contract-v1.1.md:213`, `docs/api-contract-v1.1.md:249`) |
| 404 | `WORK_ORDER_NOT_FOUND` ngoài scope/không được giao/không tồn tại; `POLE_NOT_FOUND` cho pole không thấy; `POLE_PHOTO_NOT_FOUND` cho ảnh không thấy hoặc không thuộc đúng parent |
| 409 | `WORK_ORDER_NOT_IN_PROGRESS`; `IDEMPOTENCY_CONFLICT`; **mới** `POLE_NOT_IN_WORK_ORDER` khi pole nhìn thấy nhưng không thuộc tập; **mới** `POLE_PHOTO_TARGET_CHANGED` khi phạm vi/data_source đổi trong lúc xử lý mà không thể nhận an toàn |
| 413 | `UPLOAD_TOO_LARGE` cho giới hạn byte/request của route mới, kể cả transfer không Content-Length; phải kiểm thực tế reverse proxy/host |
| 415 | `UNSUPPORTED_IMAGE_FORMAT`: magic bytes khác JPEG hoặc JPEG không decode được |
| 503 | lỗi storage / `STORAGE_OBJECT_MISSING` khi đọc media mất; lỗi model riêng không dùng 503 nếu ảnh đã lưu thành công |

**16 MiB phải định nghĩa đúng:** `AiController.MaxUploadBytes = 16*1024*1024` hiện gắn cả RequestSizeLimit và MultipartBodyLengthLimit (`src/LuxMap.Modules.AI/Controllers/AiController.cs:26`), nên không thể suy file đúng 16 MiB luôn vừa cả multipart body. Khuyến nghị D-17: file ≤16 MiB, request ≤17 MiB để chứa form overhead, bounded-read độc lập; đây là đề xuất route mới, không tự đổi controller cũ. Mã lỗi 413 và body chuẩn phải có HTTP test thực, không chỉ attribute. Cap 40 MP kiểm trước thumbnail cả khi model vắng như mục 5.2.

## 9. Kế hoạch Pha 2 sau khi được chốt

### File dự kiến, chưa tạo/sửa

| Nhóm | Thay đổi có điều kiện theo phương án B |
|---|---|
| Port / luật dùng chung | **Mới đề xuất** `src/LuxMap.Shared/AI/SingleLampPhotoHints.cs`; adapter **mới đề xuất** `src/LuxMap.Modules.AI/Detection/YoloSingleLampPhotoHinter.cs`; sửa `AIModule.cs`; Citizen `CitizenPhotoHints.cs`, `CitizenModule.cs`, có thể `CitizenOptions.cs` để ủy quyền luật/cấu hình và giữ hành vi/version cũ |
| WorkOrders | **Mới đề xuất** `Entities/WorkOrderPolePhoto.cs`, `Configurations/WorkOrderPolePhotoConfiguration.cs`, `WorkOrderPolePhotoService.cs`, `WorkOrderPolePhotosController.cs`, DTO đề xuất `WorkOrderPolePhotoContracts.cs`; sửa `WorkOrdersModule.cs` đăng ký service; sửa `WorkOrderPoles.cs` tách membership dùng chung, không đổi tập hay thứ tự WO-12 |
| Media validation | Sửa nhỏ `src/LuxMap.Infrastructure.Storage/ImagePipeline.cs` hoặc helper mới trong chính module đó để Identify + cap trước decode; giữ JpegOnly và original nguyên byte. Không thêm image package hay codec |
| ID / schema | `src/LuxMap.Shared/Contracts/PrefixedId.cs`; migration tên **đề xuất** `AddWorkOrderPolePhotos` trong `src/LuxMap.Persistence/Migrations/`, designer/snapshot tương ứng; constraint pole-commune DB nếu D-7 chốt; không thêm enum fixture_status |
| Wiring | WorkOrders dùng Shared port, không thêm reference tới AI/Survey/Citizen. Mọi thay đổi reference/package nếu phát sinh phải giải thích riêng trước khi làm; thiết kế này không dự kiến package mới |
| Vận hành / test fixture | `scripts/copy_dev_to_supabase.py` PLAN; teardown các file nêu ở mục 5.4 và fixture mới có namespace/prefix riêng |
| Tài liệu sau duyệt | `docs/contract-drift.md` PP-*, `docs/database/erd.md` bổ sung entity/quyết định, `CLAUDE.md` ràng buộc kỹ thuật đã kiểm chứng, `tracking.html` tiến độ; README/development nếu config/command thay đổi; Contract/OpenAPI chỉ cập nhật theo quyết định/version được duyệt, xuất lại spec và test policy coverage |

**Migration chỉ thêm về nghiệp vụ**, không đổi dữ liệu cũ/bảng trạng thái survey; thêm bảng/sequence/index/FK/CHECK và có thể unique target trên pole. Nếu chọn audit entity work_order + action có sẵn, không cần nới CHECK audit. Nếu chọn enum audit mới hay FK artifact registry, phạm vi migration đổi và phải review lại. Chỉ tạo/apply sau chốt, trên DB test; không chạm `luxmap_dev` trong PHA 1. Rollback xóa bảng mới sẽ mất ảnh metadata/kết quả; object MinIO không tự xóa, audit append-only vẫn còn. Production ưu tiên tắt route/rollback ứng dụng và giữ schema, không dùng Down có dữ liệu như rollback vô hại. Nếu chọn A thì cần backfill nullable tương thích và rà mọi writer evidence/audit, không dùng migration của B.

### Kiểm chứng và phá thử dự kiến

- Unit luật chung: 0/1/nhiều detection, normal/out/lớp lạ; luma ngay dưới/bằng/trên ngưỡng; confidence dưới/bằng ngưỡng, NaN/±Infinity; không sinh dim. Giữ regression Citizen và hash artifact thay khi luật/ngưỡng thay. Các test luật có sẵn để đối chiếu, chưa chạy lượt này: `tests/LuxMap.Api.Tests/CitizenReviewTests.cs:314`.
- Upload/media: magic PNG giả JPEG, JPEG hỏng/cắt, file rỗng, fake Content-Type, 16 MiB và overhead, stream không length vượt cap, pixel cap trước decode kể cả thiếu model; original hash vòng tròn bằng bytes client, thumb sạch metadata; no-store, ảnh khác parent bị 404. Storage lỗi không có row, DB lỗi sau object không row mồ côi.
- Quyền: đủ bốn role, vô danh, cùng xã khác kỹ sư, ngoài xã, giao lại phiếu/thu hồi xã; parent thấy nhưng pole không thấy; pole thuộc tuyến/fault trên trang sau; đường liên xã và nhiều xã có quyền; pole ngoài tập; đủ ba task_kind theo quyết định và các wo_status. List/detail/media cùng quyền; không liệt kê ảnh mở không được.
- Replay: cùng key/nội dung lần lượt và đồng thời chỉ một row, một user audit và tối đa một analyzed audit; đổi từng trường/hash ảnh →409; đổi model không đổi replay; replay sau done vẫn 200 nếu còn quyền; người cũ sau giao lại 404. Không bắt nhầm unique khác thành replay. Ảnh mới sau done 409; UUID rỗng/thiếu 400.
- Khóa/transaction: giữ fake storage/model ở barrier rồi đổi phiếu/giao lại/pole để chứng minh không giữ khóa qua upload/inference; sau khi nhả barrier không ghi sai parent/nguồn. Phá thử bỏ recheck hoặc membership → test tương ứng phải đỏ. Với test “có khóa”, dùng `FOR NO KEY UPDATE` để FK KEY SHARE không tạo xanh giả (quy ước `CLAUDE.md:2110`).
- Audit: bỏ audit ở từng save → guard từ chối; hai event một SaveChanges → từ chối; actor/correlation/commune/phiên bản đúng; retry concurrent không nhân event. Direct SQL chèn sai FK/NaN/Infinity/null bộ AI/enum → constraint đúng tên từ chối. Nếu constraint ngoài EF, assert `pg_constraint`.
- Không tác dụng phụ: chụp snapshot current status/luminance/baseline/fault/inspection outcome/trạng thái phiếu trước và sau upload normal/out/unknown/model lỗi; tất cả giữ nguyên. Pole-photo không thỏa `after` bắt buộc của repair.
- Mất model/sai SHA/inference exception: upload vẫn 201 có row/URL, AI null; cancel sau commit + retry không tạo ảnh mới; cancel trước commit không có row nửa chừng; audit/save thứ hai lỗi thì toàn transaction rollback. Replay đua không được thấy snapshot AI null tạm trước khi request gốc hoàn tất. Đo latency riêng, không khẳng định độ chính xác model bằng ảnh tổng hợp.
- Test policy coverage/OpenAPI snake_case/nullability/201/200/413/415; build và test phù hợp trên DB cô lập; test copy PLAN/teardown FK và migration apply/rollback/reapply trên DB test trống. Thử HTTP qua host/proxy cho giới hạn body trước khi xác nhận 413 thực tế.

### Drift cần đăng ký sau Mỹ chốt (chưa ghi vào drift trong PHA 1)

| Mã đề xuất | Nội dung |
|---|---|
| PP-1 | Route POST/read/media, schema response/status/error; field `photo`, ID prefix mới |
| PP-2 | Hint ON/OFF, ánh xạ normal/out/unknown/null, không dim, không đổi trạng thái/sinh fault; khác nghĩa fixture classification |
| PP-3 | Task kind/status/capability, membership WO-12 và phạm vi liên xã; không thay ảnh after |
| PP-4 | Bảng/nguồn dữ liệu/FK/index; bucket evidence, original/thumbnail, proxy/retention |
| PP-5 | Audit submitted/analyzed, actor và một event mỗi SaveChanges, version luật/model |
| PP-6 | Request hash/replay, UUID bắt buộc, offline upload trước complete; không op_type JSON mới |
| PP-7 | AI đồng bộ ngoài transaction sau ghi object, commit chung row/kết quả/audit; model failure, cancellation/recovery, không infer trong GET/replay |
| PP-8 | Giới hạn file/body/pixel, timestamp, bbox và endpoint lịch sử theo pole nếu được chọn |

Mỗi mục drift cần Decision / Decision maker / Date / Scope và ghi nền SELF-SIGNED nếu chưa FW, theo `docs/contract-drift.md:15`. Không hợp thức hóa đề xuất bằng cách sửa Contract cho khớp code.

**Cần báo:** WP6 nhận form/response, chọn đúng pole từ WO-12, thông báo ON ≠ đủ sáng, UUID + file giữ nguyên trong hàng chờ, upload trước complete, điều kiện ảnh after; WP5 nhận list/media/quyền theo phiếu, cách hiển thị hint và lỗi unavailable; WP4 xác nhận tái dùng luật/model/version và bộ ảnh thực để đo. Mỹ chuyển các quyết định đã chốt tới FW. **Chưa gửi thông báo cho ai trong PHA 1.**

## 10. Bằng chứng lệnh và giới hạn xác minh

Lệnh chỉ đọc Git đầu lượt:

```console
$ git status --short
?? .ai/tasks/POLE-PHOTO-AI.md
$ ls -l AGENTS.md
lrwxr-xr-x@ 1 nhm809  staff  9 Oct 15 22:13 AGENTS.md -> CLAUDE.md
$ git branch --show-current
docs/POLE-PHOTO-AI-p1
$ git rev-parse --short HEAD
9278eae
$ git diff --name-only
```

Output chọn lọc của các lệnh kiểm chứng, chép nguyên văn:

```console
$ rg -n 'MaxUploadBytes|RequestSizeLimit' src/LuxMap.Modules.AI/Controllers/AiController.cs
27:    public const long MaxUploadBytes = 16 * 1024 * 1024;
36:    [RequestSizeLimit(MaxUploadBytes)]
37:    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
$ rg -n 'ConfidenceThreshold|IouThreshold|MaxConcurrency|MaxPixels' src/LuxMap.Modules.AI/AiOptions.cs
17:    public float ConfidenceThreshold { get; init; } = 0.25f;
19:    public float IouThreshold { get; init; } = 0.45f;
22:    public int MaxConcurrency { get; init; } = 2;
25:    public long MaxPixels { get; init; } = 40_000_000;
29:        if (!float.IsFinite(ConfidenceThreshold) || ConfidenceThreshold is <= 0 or >= 1
30:            || !float.IsFinite(IouThreshold) || IouThreshold is <= 0 or >= 1
31:            || MaxConcurrency is < 1 or > 16 || MaxPixels <= 0 || string.IsNullOrWhiteSpace(ModelPath))
$ rg -n 'earlier.WorkOrderId|earlier.FaultId|earlier.Kind|UploadedBy == actorId' src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs
213:            .FirstOrDefaultAsync(x => x.UploadedBy == actorId && x.ClientOpId == operation, ct);
215:        if (earlier.WorkOrderId != target.WorkOrderId || earlier.FaultId != target.FaultId || earlier.Kind != kind)
```

Kiểm Git sau khi ghi báo cáo (output nguyên văn; diff của tracked files rỗng):

```console
$ git status --short
?? .ai/results/POLE-PHOTO-AI-p1.md
?? .ai/tasks/POLE-PHOTO-AI.md
$ git diff --name-only
$ git diff --check
```

Đã dùng Python chỉ đọc để kiểm các tham chiếu đường dẫn/số dòng trong báo cáo, cặp code fence và khoảng trắng cuối dòng; không chạy mã sản phẩm. Output nguyên văn:

```text
References checked: 106
Reference paths and line numbers: OK
Markdown fences: 6 (must be even)
Trailing whitespace: 0
```

Con số 6 là số fence tại thời điểm kiểm trước khi chèn chính các khối bằng chứng kiểm tra này; không phải kết quả build/test.

✅ **Đã xác minh bằng đọc code/docs:** các đường lưu ảnh gần giống, model/luật hint, quyền/membership, quyền công bố survey, audit/idempotency/sync và giới hạn hiện hữu; mọi nhận định code có dẫn file:dòng. Các testcase hiện có chỉ được đọc, không được báo là đã chạy/pass.

⚠️ **Chưa xác minh bằng chạy:** build/test, API/DB/MinIO thực, inference latency/accuracy, reverse proxy và cạnh tranh của thiết kế mới. Không có code mới để nghiệm thu hành vi; tất cả schema/response/error/test dự kiến là thiết kế chờ duyệt.

📌 **Giả định còn lại:** tính năng ảnh đơn bổ sung vào quá trình thực hiện phiếu, không trở thành sweep, không chứng nhận dim/độ sáng, không thay evidence after. Mỹ cần chốt phạm vi và nghĩa nhãn trước Pha 2.

## 11. D-item — Mỹ chốt trước Pha 2

| Mã | Phương án | Khuyến nghị và lý do |
|---|---|---|
| D-1 | A mở rộng evidence; A' evidence + bảng con; B bảng pole-photo riêng | **B nếu nhiều loại phiếu; A nếu chỉ inspection.** B giữ nguyên EV-1 và nghĩa after, A tiết kiệm endpoint/media khi phạm vi thật hẹp. Chưa chọn thay Mỹ. |
| D-2 | `classified_as normal/out/unknown` với scope hint; hoặc enum ON/OFF riêng/null khi ON | **Mapping mục 2 + scope và dim eligibility false** nếu giữ tên theo yêu cầu. UI bắt buộc “đang bật”, không tuyên bố đủ sáng; chọn enum ON/OFF nếu Mỹ muốn loại bỏ hoàn toàn nghĩa nhập nhằng. |
| D-3 | Tái dùng trực tiếp Citizen; tách port/adapter chung; viết luật mới | **Tách phần trung lập**, Citizen ủy quyền; một nguồn daylight/config, giữ key và hash cũ khi output không đổi; không vòng dependency, không luật song song. |
| D-4 | Chỉ inspection; inspection+repair; cả survey/inspection/repair; chỉ in_progress hay mở assigned/done | **Cả ba, in_progress** cho phương án B; survey chỉ ảnh bổ sung, repair không thay after. Mở assigned/done cần quyết định offline/quyền riêng. |
| D-5 | Chỉ gợi ý; cập nhật current/history; tự fault | **Chỉ gợi ý lưu lại**, giữ độc quyền công bố sweep và không trộn bằng chứng ảnh đơn vào dữ liệu nghiên cứu. |
| D-6 | ExecuteWorkOrders/ReadWorkOrders; capability mới | **Dùng capability sẵn**, POST chỉ assignee, đọc qua parent hiện tại; không mở ReviewSurveys cho kỹ sư. |
| D-7 | Pole cùng xã phiếu; hoặc đúng tập WO-12 liên xã; FK pole-commune bằng EF alternate key hay DB-only | **Đúng WO-12 liên xã**, commune ảnh từ pole; parent FK đơn, pole-commune FK ghép. Ưu tiên DB-only unique/FK nếu cần tránh EF key mới; kiểm catalog và tác động đổi xã. |
| D-8 | Không proximity; chỉ cảnh báo xa; từ chối xa | **Chưa hard-reject**, chưa thêm distance/cờ nếu chưa cần; nếu chọn cảnh báo phải chốt ngưỡng và UX. GPS không chứng minh ảnh đúng bóng. |
| D-9 | Bảng/cột/ID mới như mục 5; hoặc nullable mở rộng evidence | **Nếu B: một bảng hẹp, PPH 6 chữ số tối thiểu do DB sinh, data_source snapshot từ pole**, ICommuneScoped/IAudited và constraints hữu hạn; prefix/tên vẫn cần duyệt. |
| D-10 | Chuỗi phiên bản như Citizen; hay FK registry artifact_version ngay | **Chuỗi model_version + artifact_version như Citizen đợt đầu**, không registry thứ hai; nếu cần FK Q8 thì duyệt port/migration riêng, không reference Survey ngược. |
| D-11 | Original nguyên byte; hay sanitize như ảnh công dân; bucket mới | **Nguyên byte + thumbnail sạch, bucket evidence hiện có**; phù hợp quy ước ảnh kỹ sư, không tự mở ngoại lệ sanitize. |
| D-12 | Audit entity work_order sẵn; entity pole_photo mới; một hay hai save | **WorkOrder + submitted(User), analyzed(Cv), hai save ngắn** và đúng một event/save; commune theo ảnh, state đủ pole/photo/phiếu, không đổi marker evidence legacy. |
| D-13 | Có bbox/lịch sử theo pole toàn hệ thống; hay chỉ list/detail/media dưới phiếu | **Chỉ các route dưới phiếu mục 8, filter pole_id trong list; chưa bbox**. Thêm overlay/history khi WP5/WP6 cần và chốt quyền theo từng parent. |
| D-14 | Đồng bộ trước commit row; commit ảnh trước rồi AI; bắt AI thành công mới nhận ảnh; nền bền vững | **Infer ngoài transaction sau ghi object; commit chung row/kết quả/audit, lỗi model vẫn nhận ảnh AI null; chưa retry GET/replay**, chưa worker. Tránh replay thấy kết quả nửa chừng; nếu phục hồi AI là bắt buộc, duyệt explicit retry/job và status trước triển khai. Latency chưa đo. |
| D-15 | Replay chỉ parent/kind như evidence; hay hash đầy đủ | **UUID bắt buộc + unique user/key + hash metadata và bytes**, replay 200 snapshot cũ, khác nội dung 409; quyền hiện tại vẫn áp. |
| D-16 | Thêm op_type JSON; hay hàng chờ multipart riêng | **Hàng chờ multipart riêng, start → photos → complete**; không base64, không op_type mới; after vẫn thuộc evidence. |
| D-17 | 16 MiB cả request như AI; hay file 16 MiB + overhead; timestamp chỉ Z hay Z/offset | **File 16 MiB/request 17 MiB, cap 40 MP trước decode; ISO đầy đủ có Z/offset quy UTC, từ chối thiếu timezone cho route mới**. Phải ghi rõ khác parser evidence đang cho thiếu timezone; test 400/413/415 qua HTTP/proxy. |

**Dừng cứng PHA 1. Không tạo migration, không triển khai endpoint và không tự chốt các D-item trên.**
