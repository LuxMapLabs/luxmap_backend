# POLE-VIDEO — Pha 1: khảo sát và đề xuất (04/11/2026)

Người làm: Claude. **Chỉ đọc code + đo ffmpeg trên một clip có sẵn; không sửa `src/`, `tests/`, migration, Contract, drift.**
Nền: `origin/dev` @ `c0f4a03`. R-1…R-4 là quyết định của Mỹ (04/11/2026) ở `.ai/tasks/POLE-VIDEO.md`.

## 0. Tóm tắt

- **Làm biến thể của ảnh cột (PP-1…PP-15), không mở rộng sweep.** Một lượt upload clip tạo **một dòng `work_order_pole_photo`** (frame đại diện + nhãn
  on/off/inconclusive) **cộng một dòng con `work_order_pole_video`** (clip gốc + số liệu frame). Nhờ vậy `/review`, `/decision`, công bố trạng thái cột,
  sự cố `origin_pole_photo_id` và audit chạy **nguyên vẹn**, không viết lại.
- **Không dùng được endpoint ảnh làm nền cho phần upload:** nó là multipart và **đệm cả body vào RAM** (17 MiB). Clip phải đi như `PUT /sweeps/{id}/clips/{n}`:
  body thô, stream thẳng sang S3.
- **Hai chỗ code hiện tại chặn, phải mở bằng port/hàm mới:** `WorkOrders` **không tham chiếu được** `Survey` (ngược chiều phụ thuộc), và
  `FfmpegFrameExtractor` **đòi đồng hồ clip** mà clip một đèn không có.
- **Đã đo:** trích 4 frame từ clip HEVC 1080p 4,2 s mất **1,9 s** (Mac, 1 luồng); mỗi frame ~230–280 KB. **Chưa đo suy luận** (không dựng harness ở Pha 1, như POLE-PHOTO-AI).
- 12 D-item cần Mỹ chốt ở cuối.

## 1. Đã có gì gần giống — tái dùng được gì

| Thành phần | Dùng lại? | Bằng chứng |
|---|---|---|
| Quyền, người được giao, `in_progress`, tập cột WO-12 | **Có, nguyên** | `WorkOrderPolePhotoService.cs:33-54` (`Order`, `Target`, `Writable`); `WorkOrderPoles.cs:79-83` (`Targets`) |
| Idempotency `(uploaded_by, client_op_id)` | **Có** | unique `ux_pole_photo_user_operation` ở `WorkOrderPolePhotoConfiguration.cs:45`; replay `WorkOrderPolePhotoService.cs:89-98` |
| Review của kỹ sư (`PATCH …/review`) và quyết định Quản lý (`POST …/decision`) | **Có, nguyên** — chúng chỉ đọc `cv_state/final_cv_state` của dòng ảnh | `WorkOrderPolePhotoService.cs:186-214`, `216-248`; controller `WorkOrderPolePhotosController.cs:75-90` |
| Công bố trạng thái cột + tạo `lamp_out` | **Có, nguyên** | `ReviewedPolePhotoPublisher.cs:20-85`, đăng ký `SurveyModule.cs:39`. `fault.origin_pole_photo_id` là FK ghép tới **dòng ảnh** (`WorkOrderPolePhotoConfiguration.cs:54-57`, CHECK `ck_fault_pole_photo_source`) — nên dòng ảnh **phải tồn tại**, đây là lý do chọn D-1 |
| Gợi ý AI một đèn | **Có, gọi theo từng frame** | `ISingleLampPhotoHinter.HintAsync(photo, thumbnail)` (`SingleLampPhotoHints.cs:7-10`); luật: đúng 1 detection, ảnh không quá sáng ⇒ on/off + confidence, ngoài ra `inconclusive` (`YoloSingleLampPhotoHinter.cs:44-58`) |
| Đọc JPEG + sinh thumbnail | **Có** | `images.ReadAsync(buffer, MaxUploadBytes, MaxPixels)` (`WorkOrderPolePhotoService.cs:102`) cho frame đại diện |
| Upload thân thô stream sang S3, kiểm hash/`ftyp` | **Có (khuôn)** | `SweepsController.cs:31-40`; `SurveyIngestService.cs:108-130`; `MultipartUpload.cs:42` (`ftyp` ở byte 4–8) |
| Trích frame bằng ffmpeg | **Không dùng nguyên** | xem dưới |
| Phần multipart + `PolePhotoBodyLimit` | **Không** | `PolePhotoBodyLimitAttribute.cs:17-29` copy cả body vào `MemoryStream` (cap 17 MiB) — với clip 50–100 MB là sai theo D-R28 ("không buffer vào RAM, không qua `IFormFile`") |

**`FfmpegFrameExtractor` không dùng nguyên được** (`Processing/Frames/FfmpegFrameExtractor.cs`):
- `ExtractAsync` nhận `ClipClock` và **ném `CLOCK_VIDEO_MAPPING`** nếu PTS file không khớp PTS điện thoại khai (dòng 95, 115-117); clip một đèn không có đồng hồ.
- Chọn frame theo `FrameWindow` quanh thời điểm đi ngang cột (`SelectFrames`, dòng 185-197); clip một đèn cần lấy mẫu **đều** trên cả clip.
- Phần tái dùng được: tải clip về thư mục tạm có hash/quota (60-85), `ffprobe` PTS (89-110), chặn xoay/kích thước (98-105), chạy ffmpeg có timeout (143-152), xoá thư mục tạm (178).
  Đề xuất: tách phần chung thành hàm nội bộ, thêm một lối vào `SampleAsync(objectKey, bytes, sha, fps, maxFrames, consume)`.
- **Ranh giới module:** `LuxMap.Modules.Survey.csproj:4` tham chiếu `WorkOrders`; ngược lại không được. Vì vậy khai **port trung lập ở `LuxMap.Shared`** cạnh `ISingleLampPhotoHinter`
  (đề xuất `IClipFrameSampler`), Survey hiện thực — đúng quy tắc "hỏi qua port khai ở module cần, module sở hữu hiện thực" (`CLAUDE.md`).
- **Semaphore dùng chung:** `slots = new SemaphoreSlim(options.MaximumConcurrentClips)` mặc định **1** (`FfmpegFrameExtractor.cs:25`, `SurveyFrameOptions.cs`). Nếu dùng chung, request upload sẽ xếp hàng sau worker khảo sát (xem D-3).

## 2. Kết luận cho cả clip — luật tổng hợp

Mỗi frame đi qua hinter ⇒ `on | off | inconclusive`. Đề xuất (con số tạm, đặt ở `appsettings` section `PoleVideo`, **không** ở `system_setting` — chỉ ngưỡng `dim`/offline ở đó; tái dùng `Citizen:DaylightMeanLuma` vì hinter đã đọc nó):

1. Lấy mẫu **1 frame/giây**, tối đa **12 frame** (bỏ 0,5 s đầu để máy lấy nét/phơi sáng ổn định).
2. `decided` = frame có nhãn on/off. **`decided < 3` ⇒ `inconclusive`** (đèn không vào khung / ban ngày / nhiều bóng).
3. Nhãn đa số trong `decided`; nếu **tỉ lệ đồng thuận < 0,7 ⇒ `inconclusive`** (đèn nhấp nháy hoặc detector lẫn).
4. `confidence` = **trung vị** confidence của các frame đồng thuận (không lấy max — một frame tốt không đủ để tin cả clip).
5. `inconclusive` ⇒ `confidence = null` (khớp `ck_pole_photo_ai`, `YoloSingleLampPhotoHinter.cs:47`).
6. `artifact_version` = `<artifact hinter>;pv-<hash 12 ký tự của bộ tham số trên>`, theo cách `cq-` đã làm (`YoloSingleLampPhotoHinter.cs:82`), để đổi tham số là đổi phiên bản và tái lập được (BE-34).

Không viết luật on/off thứ hai: nhãn từng frame **chính là** luật ảnh của Citizen/PP.

## 3. Lưu gì, giữ gì (liên quan câu "mỗi video giữ vài ảnh")

- **Clip gốc**: bucket `luxmap-video`, nguyên byte, key `pole-videos/{photo_id}/{sha256}.mp4` (hash trong key như sweep, `SurveyIngestService.cs:119`). Giữ theo D-R16.
- **Frame đại diện**: **một** frame — frame confidence cao nhất trong nhóm đồng thuận (nếu `inconclusive`: frame giữa clip) — lưu **làm ảnh của dòng `work_order_pole_photo`**
  (original nguyên byte + thumbnail sạch, `luxmap-evidence`), nên `thumbnail_url`/`original_url` hiện có vẫn đúng.
- **Các frame khác chỉ dùng để suy luận rồi bỏ**; không có bảng `survey_frame` cho luồng này. Nếu sau cần thêm ảnh: trích lại từ clip đã giữ. 1–3 ảnh là D-5.
- **Dung lượng** (đo trên `Videos/IMG_0457.MOV`: HEVC 1080p30, 126 frame, 4,198 s, 7 958 840 B ⇒ **14,5 Mbps ≈ 1,9 MB/s**):

  | Độ dài clip | Dung lượng ước (cùng bitrate) |
  |---|---|
  | 10 s | ~19 MB |
  | 30 s | ~57 MB |
  | 60 s | ~114 MB (khớp 100–150 MB/phút ở D-R28) |

  Ổ VPS là 40 GB NVMe (OVH VPS-1 2/4/40, ghi chú VPS 20/10) dùng chung DB + OS + MinIO; 100 clip 30 s ≈ 5,7 GB. **Cap đề xuất 100 MiB và ≤ 30 s** (thấp hơn 300 MiB của sweep). `IObjectStore` **không có delete**
  (`docs/agents/traps/media-storage.md`, BE-35 chỉ báo cáo) ⇒ clip bị từ chối sau khi đã ghi là object mồ côi không tự dọn được (D-12).
- `StoredObjectColumn`: bảng con có cột `object_key` ⇒ phải khai `StoredObjectColumn.Original(StorageBucket.Video, "work_order_pole_video")` ở `WorkOrdersModule.cs` (cạnh dòng 35-38);
  `StoredObjectColumnTests` đỏ nếu thiếu (`ObjectInventory.cs:23`). Bảng con **không** có thumbnail (thumbnail ở dòng ảnh).

## 4. Phạm vi phiếu và người gọi

Giữ nguyên PP D-1/D-4: cả ba loại phiếu, **chỉ `in_progress`**, **chỉ người được giao**, cột thuộc tập WO-12. **Không capability mới**: upload và review dùng `ExecuteWorkOrders`, đọc dùng
`ReadWorkOrders`, quyết định dùng `ReviewSurveys` — y như `WorkOrderPolePhotosController.cs:19,46,76,88`. Thêm capability sẽ phải sửa `LuxMapPolicies.Matrix` + hai test literal
(`RoleCapabilityMatrixTests`, `CapabilityMatrixTests`) mà không đem lại gì. Không kiểm khoảng cách GPS (PP đã chốt chưa kiểm).

## 5. `data_source` (R-4)

Dòng ảnh lấy `DataSource` **từ cột**, không từ client (`WorkOrderPolePhotoService.cs:130`, `fresh.DataSource`). Giữ như vậy: cột `field` ⇒ clip mang `field` đúng R-4; cột testbed ⇒ `calibration_rig` **tự động**,
không có đường nào gắn `field` lên dữ liệu testbed. **Không hard-code `field`** (sẽ phá luật "dữ liệu testbed không bao giờ mang `field`"). Có cần chặn upload clip lên cột testbed không? Khuyến nghị **không chặn**
(demo testbed cần), nhưng là D-7 để Mỹ xác nhận. Test: cột `calibration_rig` ⇒ dòng ảnh `calibration_rig`.

## 6. Hệ quả của việc bỏ cấu hình quay (R-3)

- D-R24 là luật của **phiên** khảo sát (từ chối phiên sai cấu hình). Clip một đèn không phải phiên ⇒ không đụng luật đó, **nhưng phải ghi drift** để không ai hiểu nhầm clip này là dữ liệu có kiểm soát.
- Rủi ro kỹ thuật: auto-exposure/EIS/HDR có thể làm ON/OFF sai (đèn cháy sáng hoặc tối đen). Được giảm bởi: (a) nhãn chỉ là **gợi ý**, trạng thái cột chỉ đổi sau khi Quản lý xác nhận
  (`DecideAsync`, `WorkOrderPolePhotoService.cs:216-248`); (b) luật `decided ≥ 3` và đồng thuận ≥ 0,7 ở mục 2; (c) kỹ sư có thể sửa nhãn kèm ghi chú (PP-12).
- Chỉ ON/OFF, không `dim` (R-2): `ReviewedPolePhotoPublisher.cs:50-53` đã giữ `dim`/`normal` khi ảnh nói `on` — luật y nguyên cho clip.
- Thống kê: `Statistics` **không đọc** `work_order_pole_photo` (grep tên bảng/entity trong `src/LuxMap.Modules.Statistics` không ra kết quả) ⇒ chưa có gì để loại. Ghi vào drift: precision/recall chính thức **không** lấy dòng có `video`.
- Snapshot nguồn gốc của nhãn (`LabelSnapshot`, `IPolePhotoPublication.cs:12-17`) thêm `media = "video"`, `capture_config = "not_verified"`.

## 7. Đồng bộ hay nền

- **Đo (lệnh thật, máy Mac của Mỹ, 1 luồng, KHÔNG phải VPS 2 vCore):**

  ```
  $ ffprobe … -show_entries stream=codec_name,width,height,r_frame_rate,nb_frames,duration,bit_rate Videos/IMG_0457.MOV
  codec_name=hevc  width=1920  height=1080  r_frame_rate=30/1  duration=4.198333  bit_rate=14499670  nb_frames=126
  $ ffmpeg -nostdin -loglevel error -threads 1 -i IMG_0457.MOV -vf fps=1 -q:v 2 -pix_fmt yuvj420p f_%03d.jpg
  real 1.90 / user 2.14          → 4 frame: 278518, 227021, 232032, 258704 byte
  $ ffmpeg … -vf fps=2 …         real 1.90 / user 2.19  → 8 frame
  $ ffprobe -show_entries packet=pts,flags …                real 0.02
  ```
  Giải mã HEVC chiếm gần hết thời gian (1 fps và 2 fps cùng 1,9 s) ⇒ chi phí theo **độ dài clip**, ít phụ thuộc số frame. Clip 30 s ≈ 14 s ffmpeg trên máy này (ngoại suy tuyến tính — **chưa đo**).
- **Suy luận: chưa đo** (cấm dựng harness ở Pha 1; `AiOptions.cs:21` nói "vài trăm ms" nhưng đó là comment, không phải số đo — cùng kết luận với POLE-PHOTO-AI-p1 §6). Model `best.onnx` có trong `src/LuxMap.Modules.AI/Models/`; semaphore mặc định 2 (`AiOptions.cs:22`).
- Thời gian upload mới là chi phí chủ đạo: 20 MB qua 4G 5–20 Mbps ≈ 8–32 s.
- Nguyên tắc giữ từ PP-7 (`WorkOrderPolePhotoService.cs:117-126`): **không giữ khoá qua stream/ffmpeg/suy luận**; khoá phiếu → cột → ảnh trong transaction ngắn sau cùng; model lỗi/thiếu ⇒ vẫn nhận clip, `ai_status = unavailable`
  (`hint is null`, dòng 111-115 và `Item` dòng 250). `OperationCanceledException` phải truyền đi.
- Khuyến nghị D-3: **đồng bộ** như PP D-14 (một request cho mobile có kết quả ngay), kèm cap 30 s/12 frame, **semaphore riêng** cho đường này, và **cổng đo ở Pha 2**: nếu suy luận + ffmpeg > ngưỡng Mỹ chọn thì chuyển nền.
  Nền cần `pending/processing/failed` + lease trên `scheduled_job` (không có sẵn cho bảng này) — đắt, nên chỉ làm khi số đo buộc.

## 8. Idempotency + offline

- Route `PUT` mang `client_op_id` ở đường dẫn ⇒ tự idempotent theo HTTP; thân thô nên metadata (`pole_id`, `captured_at`, `lat`, `lng`) đi bằng **query**, hash clip bằng **`X-Content-SHA256`** (như clip sweep).
- `request_hash` = SHA-256 của `{work_order, pole, captured_at (µs, UTC), lat, lng, clip_sha256}` — **không cần đệm clip** vì hash do client khai và `StoreStreamAsync` kiểm lại khi stream (`SurveyIngestService.cs:120`, `StreamUpload.ExpectedSha256`).
- Gửi lại cùng khoá, cùng hash ⇒ **200** snapshot cũ, **không** đọc body, không ghi, không audit; khác hash ⇒ **409 `IDEMPOTENCY_CONFLICT`**; kiểm replay **trước** khi stream (khuôn `ExistingClip`, `SurveyIngestService.cs:115,134-146`).
  Hai request đua nhau: unique `ux_pole_photo_user_operation` bắt, thua thì rollback rồi trả bản thắng (`WorkOrderPolePhotoService.cs:151-158`); object thua có hash trong key nên chỉ là orphan có thể dọn.
- Offline: clip **không** đi qua `POST /sync/push` (drift S-4, `contract-drift.md:1042`). Mobile xếp một **hàng chờ riêng, một PUT mỗi clip** (đơn giản hơn PP-6 vì không còn bước start/complete).

## 9. Xác nhận và xem clip

`/review` và `/decision` tái dùng nguyên (mục 1). Người duyệt phải **xem được clip**: thêm `GET …/{photo_id}/video` proxy qua API (BE-11 quy tắc 1), `Cache-Control: no-store`
(khuôn `Media`, `WorkOrderPolePhotosController.cs:67-72`). **Vướng:** `IObjectStore.OpenAsync` trả luồng không tua được (`ObjectStorage.cs:154-156`); trình phát di động/web cần **Range** để tua
MP4 có `moov` ở cuối. Hoặc trả full stream (không tua, tải trọn clip), hoặc thêm `OpenRangeAsync` vào `IObjectStore` (D-9).

## 10. Bề mặt API đề xuất

```
PUT  /api/v1/work-orders/{id}/pole-videos/{client_op_id}?pole_id=&captured_at=&lat=&lng=
     Authorization: Bearer … · Content-Length (bắt buộc) · X-Content-SHA256: <64 hex thường> · Content-Type: video/mp4
     → 201 (mới) / 200 (replay)  body: PolePhotoItem + "video": {duration_ms, byte_count, sha256, sampled_frames, decided_frames, on_frames, off_frames, video_url}
GET  /api/v1/work-orders/{id}/pole-photos[…]      → như cũ; item có "video" khi dòng có clip (null với ảnh)
GET  /api/v1/work-orders/{id}/pole-photos/{photo_id}/video   → proxy MP4, no-store
```
- `snake_case`, thêm `video` nullable vào `PolePhotoItem` (`WorkOrderPolePhotoContracts.cs:7-12`) là thay đổi **thêm** nhưng chạm bề mặt đã công bố ⇒ ghi drift, báo WP5/WP6.
- Lỗi: 400 `VALIDATION_FAILED` (metadata/hash/Content-Length thiếu, clip quá dài `VIDEO_TOO_LONG`, ffprobe không đọc được `VIDEO_INVALID`); 403; 404 `WORK_ORDER_NOT_FOUND`/`POLE_NOT_FOUND`;
  409 `WORK_ORDER_NOT_IN_PROGRESS`, `POLE_NOT_IN_WORK_ORDER`, `POLE_PHOTO_TARGET_CHANGED`, `IDEMPOTENCY_CONFLICT`; 413 `UPLOAD_TOO_LARGE`; 415 `UNSUPPORTED_VIDEO_FORMAT` (không có `ftyp`); 503 theo storage hiện có.
- **Caddy:** thêm guard `Content-Length > cap` **riêng route** này trả 413 đúng hình dạng (bài học PP-8: nếu thiếu, API trả 413 sớm trong lúc Caddy còn đang gửi thân ⇒ 502). Mẫu ở `deploy/caddy/Caddyfile:32-39` (ảnh) và `41-44` (300 MiB chung).
  Docker image đã có ffmpeg (`Dockerfile:33-35`), CI đã cài (`.github/workflows/ci.yml:110`).
- Phải xuất lại OpenAPI + `gen_consolidated_spec.py` + lint (lệnh ở `.ai/context/commands.md`).

## 11. Kế hoạch Pha 2

**Chia PR (không xếp chồng; chờ cha merge):**
1. **PV-a (migration + entity):** bảng `work_order_pole_video` — PK = FK `photo_id` → `work_order_pole_photo` (`Restrict`), `object_key`, `byte_count`, `sha256`, `duration_ms`,
   `sampled_frames/decided_frames/on_frames/off_frames`, `extractor_version_id` (registry BE-34 ⇒ khai `VersionedResultColumn`). CHECK: key không trắng, sha hex 64, `byte_count BETWEEN 1 AND <cap>`,
   `duration_ms BETWEEN 1 AND 30000`, `0 ≤ on+off ≤ decided ≤ sampled ≤ 12`. **Không có cột `double`** (không cần CHECK NaN). Không `commune_id` (luôn đi tới qua dòng ảnh đã scope). **Chỉ thêm**; `Down()` đối xứng; đọc `Up()/Down()` trước khi áp. Thêm bảng vào `PLAN` của `scripts/copy_dev_to_supabase.py`; khai `StoredObjectColumn`.
2. **PV-b (port + sampler):** `IClipFrameSampler` ở `LuxMap.Shared`; hiện thực ở Survey (tách phần chung của `FfmpegFrameExtractor`; semaphore riêng); test với `IMediaProcessRunner` giả như test hiện có.
3. **PV-c (endpoint + service + tổng hợp nhãn + Caddy + OpenAPI + docs):** `WorkOrderPoleVideoService` (hoặc mở rộng service ảnh), controller PUT/GET, luật mục 2.
4. **PV-d (drift + CLAUDE/traps + tracking):** `contract-drift.md` mã `PV-1…PV-n`; `docs/agents/traps/survey.md` hoặc `media-storage.md` (clip một đèn không phải sweep, không đệm RAM, orphan); báo WP5/WP6.

**Test (HTTP + DB thật, `LuxMapTestHost`):** quyền 4 vai trò; người được giao; tập cột WO-12; replay 200 / khác hash 409 / đua hai request; clip quá cap 413 (kể cả không `Content-Length`); sai `ftyp` 415; ffprobe hỏng 400; quá dài 400; model lỗi ⇒ 201 `unavailable`;
luật mục 2 từng nhánh (`decided<3`, đồng thuận 0,69/0,7, trung vị, tất cả OFF/ON); cột testbed ⇒ `calibration_rig`; `/review` + `/decision` trên dòng có clip (dim giữ, ảnh cũ không ghi đè); audit **đúng một** event mỗi `SaveChanges`;
không giữ khoá qua stream. Sampler thật cần ffmpeg (CI có); dùng clip sinh bằng `ffmpeg -f lavfi` lúc chạy test, không commit file nhị phân; detector giả (`ManifestOnOffDetector` chỉ cho Development/Test, `SurveyFrameOptions.ValidateEnvironment`).
**Phá thử** mỗi chốt: bỏ kiểm replay ⇒ đỏ; bỏ `Writable` ⇒ đỏ; đổi đồng thuận 0,7 ⇒ đỏ; bỏ guard `Content-Length`; ghi `data_source` cứng `field` ⇒ test testbed đỏ; khôi phục bằng `cp`/`touch`.
Chạy **toàn bộ** `dotnet test` đọc mã thoát, build `-warnaserror`, `--disable-build-servers`.

**Drift đề xuất:** PV-1 (nền: biến thể ảnh cột, hai bảng), PV-2 (PUT thân thô, metadata ở query, mã lỗi), PV-3 (luật tổng hợp + tham số + `artifact_version`), PV-4 (D-R24 không áp, `capture_config = not_verified`, loại khỏi precision/recall), PV-5 (cap 100 MiB/30 s, Caddy), PV-6 (`video` nullable trong `PolePhotoItem`), PV-7 (GET video/Range).
**Phải báo:** WP6 (quay, upload PUT, hàng chờ riêng, H.264/H.265 MP4), WP5 (xem clip ở màn duyệt ảnh cột, trường `video`). WP4: không việc mới — cùng model; chỉ biết phiên bản `pv-…`.

## D-item — Mỹ chốt trước Pha 2

| # | Vấn đề | Phương án | Khuyến nghị |
|---|---|---|---|
| **D-1** | Mô hình dữ liệu | **A:** dòng `work_order_pole_photo` (frame đại diện) + bảng con `work_order_pole_video`. **B:** bảng `work_order_pole_video` đầy đủ, độc lập | **A.** `fault.origin_pole_photo_id`, `/review`, `/decision`, publisher đều bám `photo_id`; B phải nhân đôi cột review/decision, FK nguồn sự cố và publisher |
| **D-2** | Hình dạng upload | **A:** `PUT` thân thô, `client_op_id` ở path, metadata ở query. **B:** `POST` multipart như ảnh | **A.** B buộc đệm cả clip vào RAM (`PolePhotoBodyLimitAttribute.cs:17-29`) |
| **D-3** | Đồng bộ hay nền | **A:** đồng bộ trong request, cap 30 s/12 frame, semaphore riêng. **B:** nền (`pending/processing/failed` + lease) | **A**, cổng đo ở Pha 2 (suy luận chưa đo); chuyển B nếu số đo vượt ngưỡng Mỹ đặt |
| **D-4** | Luật nhãn clip | Mục 2: 1 fps, ≤12 frame, `decided ≥ 3`, đồng thuận ≥ 0,7, confidence = trung vị | Chấp nhận làm con số tạm ở `appsettings` `PoleVideo`; chỉnh sau khi có clip thật |
| **D-5** | Giữ ảnh bằng chứng | **A:** 1 frame đại diện (là ảnh của dòng photo). **B:** 1–3 frame (cần bảng `…_frame` hoặc nhiều dòng photo) | **A.** Clip gốc đã giữ nên không mất gì; B thêm bảng và làm hỏng ý "một clip = một quyết định" |
| **D-6** | Cap | 100 MiB, ≤ 30 s, ≤ 3840×2160 (đã có ở extractor `:104`), ≤ 12 frame | Chấp nhận. 30 s ở 14,5 Mbps ≈ 57 MB; thấp hơn 300 MiB của sweep để bảo vệ ổ 40 GB |
| **D-7** | `data_source` (R-4) | **A:** lấy từ cột (field ⇒ `field`, testbed ⇒ `calibration_rig`), cho phép cả hai. **B:** hard-code `field` + từ chối cột testbed | **A.** B phá luật "testbed không bao giờ `field`" hoặc chặn demo testbed |
| **D-8** | Bỏ D-R24 (R-3) | Ghi `capture_config = not_verified` vào snapshot nguồn gốc; loại khỏi precision/recall chính thức; drift PV-4 | Chấp nhận |
| **D-9** | Xem clip | **A:** full stream, không tua. **B:** thêm `OpenRangeAsync` vào `IObjectStore` để hỗ trợ Range | **B** nếu WP5/WP6 cần tua (chưa hỏi); **A** là điểm khởi đầu rẻ. Cần hỏi WP5 |
| **D-10** | Capability | Tái dùng `ExecuteWorkOrders`/`ReadWorkOrders`/`ReviewSurveys` | Chấp nhận (không thêm capability) |
| **D-11** | Port sampler | Port `IClipFrameSampler` ở `LuxMap.Shared`, Survey hiện thực (WorkOrders không tham chiếu được Survey: `Survey.csproj:4`) | Chấp nhận |
| **D-12** | Clip bị từ chối sau khi đã ghi (ffprobe lỗi / quá dài) | **A:** chấp nhận object mồ côi (không có delete, BE-35 chỉ báo cáo). **B:** thêm `IObjectStore.DeleteAsync` + chữ ký riêng (ticket riêng) | **A** cho pilot; ghi rủi ro: người dùng đã xác thực, trần 100 MiB mỗi lần. Hỏi lại khi quy mô tăng |

## Chưa kiểm chứng

- Thời gian suy luận YOLO trên frame 1080p và tổng thời gian request trên VPS 2 vCore (cấm dựng harness ở Pha 1).
- Hành vi ffmpeg/ffprobe với MP4 H.264 từ máy Android quay thật (chỉ đo MOV HEVC của iPhone; `ftyp` của file đó là `qt  ` — vẫn qua kiểm `ftyp` nhưng khai `Content-Type: video/mp4`; WP6 là Android).
- Chưa mở `docs/api-contract-v1.1.md` để đối chiếu từng chữ; mọi tham chiếu bề mặt lấy từ code và drift.
