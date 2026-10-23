# Khảo sát, lux, AI và ảnh cột

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi pipeline khảo sát (ingest, ffprobe, publication), `LuxReading`, model YOLO, ảnh cột trong phiếu. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Bốn quy tắc chốt ở BE-42 — số đo lux

**BH1750FVI gắn ở đầu xe máy khảo sát**; vị trí/cách gắn theo `docs/field/BE-15-pilot-drive.md`, không tự đổi bố trí giữa các lượt.

Nền BH1750/v1.4: xem `CLAUDE.md`, phần mở đầu; D-R22/D-R23 chốt 07/10, D-R21/D-R24 là nền SELF-SIGNED.

**1. `LuxReading` thủ công KHÁC lux theo sweep trong `luminance_history`.**

| | `LuxReading` (BE-42) | `luminance_history` (BE-15/BE-17) |
|---|---|---|
| Ai sinh | Người đo thủ công | Server ghép CV ON/OFF + đỉnh lux BH1750 theo cột |
| Thời gian | `measured_at` | `observed_at` |
| Giá trị | `lux_value` tương đối | `PeakLux`, `CvState`, `BaselineRatio`, `classified_as` |
| Truy vết | `meter_model` của dụng cụ | sweep / run / observation / baseline |
| ID hiển thị | `LUX-0001` | Không có |

Lux thủ công dùng so tương đối giữa cột/đêm theo quy trình cố định, không chấm đạt chuẩn chiếu sáng;
phân loại `dim` chỉ dùng baseline **của chính cột** (`CLAUDE.md`, mục Quy tắc dễ sai âm thầm).
Giữ cùng dụng cụ, app và tư thế giữa các lượt đo: sai số hệ thống chung mới cho phép so tương đối có nghĩa.
Ngưỡng cảnh báo 200 chỉ kiểm hợp lệ dữ liệu thô, không mang nghĩa trắc quang; giữ tên cột.
Lux BH1750 theo sweep là **đầu vào** phân loại, không dùng chính nó làm ground truth của `dim`.
D-R23 chọn tấm lọc ND (B) trước, lux kế tay (A) sau (`docs/contract-drift.md`, Quyết định khảo sát 07/10).
`nearest_luminance` dự kiến ghép theo **thời gian ±48 giờ**, không theo không gian.

**2. `commune_id` do SERVER tra từ `pole_id`, client gửi là 400.**

Guard `SaveChanges` kiểm một commune **có trong scope không** — nó **không** kiểm commune đó có
**khớp với pole** không. Nếu client gửi được `commune_id`, họ có thể gửi commune của chính mình kèm
pole của xã khác: cả hai lớp đều pass, bản ghi vào nhầm xã. Đọc pole trước cũng cho 404 đúng Contract
mục 7 — pole ngoài phạm vi thì query filter làm nó **không tồn tại**, không phải 403.

Lý lẽ denormalize của `Fixture` (*"không bao giờ là resource riêng"*) **KHÔNG áp dụng** ở đây —
`LuxReading` có endpoint riêng. Cột vẫn denormalize (bắt buộc, nếu không thì lọt cả query filter lẫn
guard), nhưng **không emit ra response**.

**3. `nearest_luminance` hiện luôn `null`, nhưng history đã có.**

Giữ khoá trong JSON; đó là nợ nối nguồn thật thuộc giai đoạn A của D-R23 (`LuxReadingService.SearchAsync`),
không phải kết luận "không có điểm trong ±48 giờ". Quyết định/nợ: `docs/contract-drift.md`, D-R23.

**4. FK `lux_reading → pole` là `Restrict`, KHÔNG `Cascade`.**

Lux là sự kiện đã xảy ra và là ground truth RQ1 — xoá pole không được âm thầm xoá dữ liệu nghiên cứu.
Guard không thấy cascade của DB: xem `docs/agents/traps/commune-scope.md`, mục 1c. `measured_by` cũng `Restrict` tới `app_user` — xoá được người đo là mất dấu vết.

**Phân quyền (Contract v1.7):** `POST` = `RecordLuxReading` (**chỉ Kỹ sư hiện trường**), `GET` =
`ReadLuxReadings` (cả bốn vai trò). Phạm vi xã vẫn canh riêng qua đọc pole và guard; lịch sử D-R5 ở `docs/contract-drift.md`.

## BE-15 P2a — khoá hàng, upload dài và EF design-time (02/10/2026)

**Không khoá hàng bằng `FromSql("SELECT * … FOR UPDATE")` trên entity có row version `xmin`.**
`SELECT *` không trả cột hệ thống `xmin`, nên EF ném `42703: column xmin does not exist` → 500 ở
mọi lượt ghi. Khuôn đúng là `FaultLocks`: `ExecuteSql("SELECT 1 … FOR UPDATE")` rồi mới đọc bằng
LINQ, để đọc vẫn đi qua query filter xã và người được giao.

**Không giữ transaction hay khoá hàng qua một lượt stream upload.** Clip 100–300 MB qua 4G mất vài
phút; giữ `FOR UPDATE` trên `work_order` suốt lúc đó làm mọi thao tác của Quản lý trên phiếu treo.
Khuôn ở `SurveyIngestService.Clip`: kiểm dưới khoá → nhả → stream object (key mang hash) → khoá lại,
kiểm lại, ghi dòng. Object mồ côi khi thua race là chấp nhận được (BE-11 quy tắc 3). Canh bằng
`A_clip_in_flight_does_not_hold_the_work_order_lock`.

**Không thêm `IDesignTimeDbContextFactory`.** EF dùng host thật để dựng model lúc design-time; một
factory tự liệt kê assembly module sẽ âm thầm thiếu module mới, và `migrations add` sinh `DropTable`
cho bảng của module đó. Sinh migration trong môi trường không có `.env` thì đặt
`ConnectionStrings__LuxMap` giả cho lệnh đó, đừng đổi đường dựng model.

## BE-15 — ffprobe: lấy packet PTS, sắp thứ tự và giữ rotation (17/10/2026)

**Dùng `packet=pts,flags`, loại cờ discard `D` rồi sort tăng dần; không dùng `frame=pts` để probe toàn clip.** `frame=` buộc
ffprobe giải mã video, có thể vượt timeout dù stdout nhỏ. Packet đến theo thứ tự giải mã,
**không phải thứ tự hiển thị khi có B-frame**; phải sort PTS trước khi kiểm clock/chọn frame/seek.
**MOV/MP4 có edit list có thể giữ packet tham chiếu ngoài đoạn hiển thị**, cả đầu lẫn đuôi clip.
Packet mang `D` không thành frame: lọc trước khi đọc PTS, kiểm clock và chọn ảnh; chỉ sort là chưa đủ.
Không suy từ FPS, không khử PTS trùng: với packet được hiển thị, thiếu PTS → `VIDEO_INVALID`,
trùng → `CLOCK_VIDEO_MAPPING`. Không khẳng định packet/frame tương đương cho mọi codec/container.
Rotation vẫn ở **`stream.side_data_list`**:
`stream=width,height,time_base:stream_side_data=rotation:packet=pts,flags -of json=compact=1`.
Không thêm `-show_frames` (bật mọi trường frame, vượt stdout 16 MiB) hay `-show_packets` (bật lại
mọi trường packet). Giữ trần stdout, timeout và quota hiện có. Dung lượng JSON còn phụ thuộc metadata
và độ dài PTS, không suy giới hạn số phút chung chỉ từ FPS. `DescribeAsync` băm assembly extractor
nên sửa lệnh tự đổi `artifact_version`, không cần tăng số version thủ công.
Canh bằng `Large_frame_probe_stays_bounded_and_extracts_the_last_frame` (video tự sinh, lệnh cũ vượt trần),
`B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images` (đối chiếu packet/frame PTS
và mã màu ảnh; bỏ sort phải đỏ), `Edit_list_discards_head_or_tail_packets_and_extracts_only_displayed_frames`
(edit list cắt đầu/đuôi, PTS và mã màu ảnh đầu/cuối; bỏ lọc discard phải đỏ), test thiếu/trùng PTS và VFR/rotation sẵn có.

## BE-15 P2c — FK ghép không giữ được hàng có cột null (04/10/2026)

**FK ghép `MATCH SIMPLE` bỏ qua hẳn hàng có BẤT KỲ cột nào null — kể cả các cột còn lại không null.**
`luminance_history` trỏ tới `pole_observation` và `luminance_baseline` bằng FK ghép có `pole_id`; hàng
`not_observed` có `observation_id` và `baseline_id` đều null nên **không FK nào kiểm `pole_id`**, và cột xoá được
dù lịch sử bất biến vẫn trỏ vào nó (review Codex sau merge #80). Đã thêm FK đơn `fk_luminance_history_pole_pole_id`
(`Restrict`). Bảng mới có FK ghép trên cột nullable phải tự hỏi: khi phần nullable là null, cái gì còn giữ các cột kia?
Test xoá phải đọc dependent bằng `AsNoTracking` — entity đang track làm EF tự từ chối ở client, test xanh mà DB không
hề được kiểm (`A_pole_with_only_not_observed_history_cannot_be_deleted`).

## AI-1 — model YOLO trong backend (08/10/2026)

- **Một model, ba caller:** `YoloModel` (singleton, nạp lúc dùng lần đầu) phục vụ `YoloOnOffDetector` (pipeline khảo sát,
  keyed `IOnOffDetector` khoá `"yolo"` — Survey **không** tham chiếu AI), `POST /ai/detect` và `YoloSingleLampPhotoHinter`
  (Citizen + ảnh cột). Dùng chung decode / tiền xử lý.
- Decode JPEG theo `docs/agents/traps/media-storage.md`, mục Hai ràng buộc về ImageSharp (`JpegOnlyDecodeTests`).
- **Model được KIỂM lúc nạp:** input `[1,3,640,640]`, output `[1,6,N]`, metadata `names` đúng `normal, out` theo thứ tự, SHA-256
  khớp `Ai:ModelSha256`. Đổi model = đổi mã ghim trong cùng PR. Tiền xử lý theo Ultralytics: xoay EXIF, letterbox xám 114,
  resize **bilinear** (`KnownResamplers.Triangle` — mặc định của ImageSharp là bicubic, lệch với `cv2.INTER_LINEAR`).
- **`artifact_version` của detector băm cả ngưỡng + tiền xử lý + ánh xạ lớp**, không chỉ file model: đổi ngưỡng mà giữ phiên bản
  là làm mất truy vết BE-34. Test canh: `As_the_survey_detector_its_output_passes_the_pipeline_validation`.
- Không có ảnh đèn thật trong test ⇒ test **không** khẳng định model phát hiện được gì; chúng canh hợp đồng (nhãn, bbox, phiên bản,
  validation) và hậu xử lý trên tensor dựng tay. Độ chính xác là việc của WP4.

## POLE-PHOTO-AI — ảnh một cột, duyệt rồi công bố (15/10/2026, drift PP-1…PP-10, nền tạm)

- Khi sửa AI ảnh đơn, dùng port Shared `ISingleLampPhotoHinter` và luật `YoloSingleLampPhotoHinter`; Citizen adapter chỉ đổi tên nhãn.
  Giữ khoá `Citizen:DaylightMeanLuma`, chuỗi băm `citizen-hint:single-lamp;daylight-mean-luma<=…` và prefix **cq** để không đổi phiên bản
  Citizen khi output giữ nguyên. Model resolve lazy; lỗi model trả null, cancellation truyền tiếp.
- Khi kiểm cột thuộc phiếu, dùng `WorkOrderPoleService.Targets` (cùng WO-12), không trang đầu. Xã/nguồn ảnh theo cột, có thể khác xã phiếu;
  mọi read ảnh đi qua phiếu cha trước. Hàng ảnh scoped theo xã chưa đủ chặn kỹ sư khác trong cùng xã.
- Khi ghi current-status, đi qua `Survey/Review/PoleStatusPublication.StageAsync` (sweep và ảnh dùng chung), caller khoá **hàng pole** cả
  khi chưa có current row. Thứ tự **work_order → pole → photo**; storage/model ở ngoài transaction, rồi khoá và kiểm lại quyền/target.
- **ON giữ dim/normal và confidence cũ nhưng tiến clock quan sát**, chặn ảnh OFF cũ ghi đè. Chỉ capture mới hơn
  `LastEvaluatedAt ?? LastSeenAt` mới áp; ảnh cũ/bằng vẫn confirm được nhưng không đổi row/sinh fault. Ảnh áp xong
  **LastSweepId/LastRunId=null**, không gán ảnh cho sweep; không ghi history/baseline. `classification_preserved` vẫn StatusApplied=true.
- Fault từ ảnh do WorkOrders cấu hình FK ghép ảnh/pole/xã; source CV, lamp_out, không trộn origin khác. D-5d chốt **confirmed**
  (sweep hiện vẫn detected); severity dùng cấu hình survey, chống trùng theo effective type + `FaultStatusSets.Open` dưới khoá cột.
  Source/xã của cột đổi sau upload →409 khi confirm. ON không đóng fault; ảnh không thay evidence after.
- Mỗi save **một** audit, transaction bao trọn upload/decision: submitted(User), analyzed(Cv), publication/applied(User),
  fault/created(User), decision/confirmed hoặc rejected(User). Save phân tích lỗi phải rollback cả ảnh và submitted.
  Replay/GET không infer hay audit lại; quyết định lần hai →409.
- Khi sửa giới hạn ảnh, giữ cap 40 MP **trước decode** ở `IOriginalJpegReader` kể cả thiếu model. Body 17 MiB được chặn trước binding
  bởi `PolePhotoBodyLimit`; Caddy phải giữ cùng guard Content-Length riêng route để tránh 502 từ early 413 (xem PP-8).
- 🔴 **Body JSON khai bằng `sealed class` + property `init`, KHÔNG positional record có attribute kiểm tra trên tham số.**
  `PolePhotoDecisionRequest` dạng record làm MVC ném `No property found that maps to constructor parameter 'Note'` → **500**, chỉ ở
  request đầu tiên đồng thời; chạy riêng có thể che lỗi. Khuôn đúng là các request ở `WorkOrderRequests.cs`.
- **DB-only** `ak_pole_photo_target` / `fk_pole_photo_same_commune` không ở EF snapshot: bảo toàn khi sinh migration sau, canh catalog/FK.
  Dọn fault **trước ảnh**, ảnh trước work_order/pole/user, audit trước actor/xã. PLAN copy phải giữ parent/users của ảnh và fault có nguồn ảnh.
  Rollback migration mất metadata/kết quả/quyết định và origin ảnh của fault; object/audit không tự mất. Trên môi trường có dữ liệu ưu tiên
  rollback ứng dụng, giữ schema; apply/down/reapply chỉ kiểm trên container test riêng trống.

- **PHOTO-REVIEW (20/10, PP-11…PP-15):** fallback `FinalCvState ?? CvState` chỉ lúc công bố. Nhãn người đổi đi qua
  `IPolePhotoPublication` với provenance riêng; score/model AI gốc chỉ là snapshot, không phải score của kết luận người sửa.
  Current-status known có thể không score; history/observation vẫn giữ invariant cũ. Review phải đọc lại sau khóa đủ ba hàng;
  chỉ chờ UPDATE ảnh không chứng minh explicit photo lock (test canh cả thứ tự command). Down `AddPolePhotoReviews` có preflight
  chống mất review và không ép dữ liệu mới về CHECK cũ; trên DB có review thì rollback ứng dụng, giữ schema.
  SQL trim dùng `chr(11)` cho vertical tab: PostgreSQL `E'\v'` là chữ `v`, vừa lọt whitespace vừa từ chối note hợp lệ `v`.

## BE-34 — registry phiên bản và nguồn gốc phiên (23/10/2026)

- **`ArtifactVersion` ở `LuxMap.Persistence.Versions`**, không còn ở Survey. Viết dòng firmware qua `ArtifactRegistry.EnsureFirmwareAsync`
  (ON CONFLICT DO NOTHING); dòng của pipeline vẫn qua `SurveyProcessor.Artifact` / `SurveyFramePipeline.RegisterArtifact`.
- **CHECK có NULL:** `x ~ 'regex'` với x NULL cho NULL, và CHECK NULL là **qua**. Mọi nhánh "hoặc" có so khớp phải kèm `x IS NOT NULL`
  (`ck_artifact_version_hash` từng để `cv_model` không hash lọt).
- **Nguồn gốc phiên nằm trên `survey_raw_file`**, không trên `survey_sweep`: upload không chạm sweep (`IAudited`). `capture_config` mang
  `module_firmware_version_id`, `profile_id`, `app_version`, `phone_model`, `camera_id`; `lux_log` chỉ id firmware module. Không kiểm với
  registry — app chạy offline, không biết id của server. Ý nghĩa hai id số chưa chốt với WP6.
- **`AlterColumn` sang NOT NULL trên bảng bất biến** sinh `UPDATE … WHERE … IS NULL` trước — trigger statement-level chặn kể cả 0 dòng. Dùng
  `ALTER TABLE … SET NOT NULL` trần sau khi đã chứng minh không còn NULL (`AddVersionTraceability.Down`).
