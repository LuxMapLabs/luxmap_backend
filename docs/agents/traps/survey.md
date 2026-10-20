# Khảo sát, lux, AI và ảnh cột

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi pipeline khảo sát (ingest, ffprobe, publication), `LuxReading`, model YOLO, ảnh cột trong phiếu. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Bốn quy tắc chốt ở BE-42 — số đo lux

> 🔴 **Nền đã đổi ở Phiếu v1.4 (27/09/2026) — bốn quy tắc dưới đây mô tả code HIỆN CÓ, chưa phải hướng
> đi.** Quy tắc 1 dựa trên tiền đề *lux là ground truth chấm CV*; ở v1.4 đỉnh lux theo cột **chính là**
> tín hiệu `dim`, nên chuỗi luminance sẽ lấy từ lux (D-R22, D-R23). `lux_value` "cảm biến điện thoại"
> (D-R15) thành module BH1750 trên xe, lấy mẫu theo sweep — D-R21 **chốt hướng (B)** (SELF-SIGNED): chuỗi mẫu thô theo sweep + đỉnh theo cột vào chuỗi luminance; `LuxReading` giữ cho đo **thủ công**. Đặc tả BE-15 chưa viết; upload video (D-R28) còn chặn.
> Quy tắc 2 (`commune_id` server tra) và 4 (`Restrict`) **giữ nguyên** dù chọn hướng nào. XML doc của
> `LuxReading.cs` còn ghi *"ABSOLUTE `lux_value`"* — lệch cả D-R15, sửa ở Phase 2.

**1. `LuxReading` KHÔNG phải `luminance_history`. Lẫn hai cái là hỏng nghiên cứu.**

| | `LuxReading` (BE-42) | `luminance_history` (BE-15/BE-17 + CV) |
|---|---|---|
| Ai sinh | **Người** đo bằng **điện thoại** | **CV** xử lý ảnh sweep |
| Thời gian | `measured_at` | `observed_at` |
| Giá trị | `lux_value` — **số đọc TƯƠNG ĐỐI** của cảm biến ánh sáng điện thoại | `baseline_ratio` — **tỉ lệ, không đơn vị** |
| Truy vết | `meter_model` — **model điện thoại** | `sweep_id` |
| ID hiển thị | `LUX-0001` | **không có** |

⚠️ **`lux_value` là TƯƠNG ĐỐI (D-R15, Contract §5.7).** Đo theo quy trình cố định — cùng máy, cùng app,
cùng tư thế — nên sai số hệ thống của cảm biến là chung cho mọi lần đo và triệt tiêu khi so tương đối.
Chỉ dùng để so giữa các cột và giữa các đêm; **không bao giờ** là lux tuyệt đối, **không** dùng để đánh
giá đạt/không đạt chuẩn chiếu sáng. Ngưỡng cảnh báo 200 là kiểm tra hợp lệ dữ liệu thô, không mang nghĩa
trắc quang. Tên cột **giữ nguyên**.

Contract §5.7 gọi lux là **một trong các nguồn ground truth cho RQ1**: CV-12 dùng nó để **chấm** phân loại của CV.
Ghi lux vào chuỗi luminance là để CV tự chấm chính mình, và làm lệch luôn biểu đồ của BE-20.
Chúng gặp nhau **đúng một chỗ**: trường `nearest_luminance` của `GET /lux-readings`, ghép theo
**THỜI GIAN** ±48 giờ (không phải không gian).

**2. `commune_id` do SERVER tra từ `pole_id`, client gửi là 400.**

Guard `SaveChanges` kiểm một commune **có trong scope không** — nó **không** kiểm commune đó có
**khớp với pole** không. Nếu client gửi được `commune_id`, họ có thể gửi commune của chính mình kèm
pole của xã khác: cả hai lớp đều pass, bản ghi vào nhầm xã. Đọc pole trước cũng cho 404 đúng Contract
mục 7 — pole ngoài phạm vi thì query filter làm nó **không tồn tại**, không phải 403.

Lý lẽ denormalize của `Fixture` (*"không bao giờ là resource riêng"*) **KHÔNG áp dụng** ở đây —
`LuxReading` có endpoint riêng. Cột vẫn denormalize (bắt buộc, nếu không thì lọt cả query filter lẫn
guard), nhưng **không emit ra response**.

**3. `nearest_luminance` LUÔN `null` cho tới BE-17 — khác với "không có điểm trong ±48h".**

Bảng `luminance_history` chưa tồn tại. **Khoá vẫn được emit**, không bị bỏ khỏi JSON, để CV-12 bind
theo hình dạng cuối ngay bây giờ. **Nợ có chủ: BE-15/BE-17** nối nguồn thật và xoá mục drift số 17.

**4. FK `lux_reading → pole` là `Restrict`, KHÔNG `Cascade`.**

Lux là sự kiện đã xảy ra và là ground truth RQ1 — xoá pole không được âm thầm xoá dữ liệu nghiên cứu.
Cũng để **không tạo bảng cascade thứ ba**: guard `SaveChanges` không thấy cascade do DB thực hiện
(xem mục 1c). `measured_by` cũng `Restrict` tới `app_user` — xoá được người đo là mất dấu vết.

**Phân quyền (Contract v1.7):** `POST` = `RecordLuxReading` (**chỉ Kỹ sư hiện trường**), `GET` =
`ReadLuxReadings` (cả bốn vai trò). Tới v1.6 `POST` **không gắn policy nào**, nên mọi vai trò đã đăng nhập
đều ghi được — kể cả Cấp giám sát chỉ-đọc. Phạm vi địa bàn **vẫn được canh riêng** — qua lượt đọc pole
và qua guard.

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
ffprobe giải mã video: HEVC 148 s vượt timeout 60 s dù stdout đã nhỏ. Packet đến theo thứ tự giải mã,
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

- **Một model, hai lối vào:** `YoloModel` (singleton, nạp lúc dùng lần đầu) phục vụ cả `YoloOnOffDetector` (pipeline khảo sát,
  keyed `IOnOffDetector` khoá `"yolo"` — Survey **không** tham chiếu AI) lẫn `POST /ai/detect`. Đừng viết lối decode / tiền xử lý thứ hai.
- 🔴 **Decode chỉ qua `JpegMagicBytes` + `ThumbnailFactory.JpegOnly`** — tiền đề để tắt advisory ImageSharp. Bản #118 decode bằng
  cấu hình mặc định và tin `Content-Type`; `JpegOnlyDecodeTests` giờ đỏ nếu ai viết lại như vậy.
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
  lượt chạy toàn bộ (request đầu tiên đồng thời); chạy riêng test thì xanh 10/10. Khuôn đúng là các request ở `WorkOrderRequests.cs`.
- **DB-only** `ak_pole_photo_target` / `fk_pole_photo_same_commune` không ở EF snapshot: bảo toàn khi sinh migration sau, canh catalog/FK.
  Dọn fault **trước ảnh**, ảnh trước work_order/pole/user, audit trước actor/xã. PLAN copy phải giữ parent/users của ảnh và fault có nguồn ảnh.
  Rollback migration mất metadata/kết quả/quyết định và origin ảnh của fault; object/audit không tự mất. Trên môi trường có dữ liệu ưu tiên
  rollback ứng dụng, giữ schema; apply/down/reapply chỉ kiểm trên container test riêng trống.
