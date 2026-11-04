---
ticket: POLE-VIDEO
title: Kỹ sư quay một clip ngắn chứa MỘT đèn trong phiếu → AI phân loại ON/OFF, LƯU kết quả gắn pole_id + work_order_id
status: ready
phase: 2-ready
owner: —
branch: feat/POLE-VIDEO
depends_on:
  - "POLE-PHOTO-AI + PHOTO-REVIEW (docs/contract-drift.md PP-1…PP-15): ảnh một cột trong phiếu → AI → kỹ sư sửa nhãn → Quản lý xác nhận → trạng thái cột. Ticket này là BIẾN THỂ VIDEO của luồng đó"
  - "BE-15 P2a/P2b (docs/survey-ingest-p2a.md, Processing/Frames): upload clip MP4 proxy qua API, ffmpeg trích frame — chỉ để TÁI DÙNG phần media, không dùng mô hình phiên/sweep"
---

## Bối cảnh

Hỏi đáp 04/11/2026: backend chỉ có đường nhận video qua **phiên khảo sát (sweep)** — suy cột từ GPS track + lux log + đồng hồ
chung, cấu hình quay kiểm ở cấp phiên (D-R24). Một clip ngắn chỉ chứa **một** đèn không có những thứ đó và cũng không cần: **cột
đã biết trước**. Mỹ chốt (04/11/2026):

| # | Chốt |
|---|---|
| R-1 | **Gắn vào phiếu công việc**, cùng khuôn `work_order_pole_photo` (phân quyền, audit, idempotency, xác nhận của Quản lý đã có sẵn) — **không** mở rộng sweep |
| R-2 | **Chỉ ON / OFF** (+ `inconclusive` như PP D-2). Không có `dim`: clip này không có lux, không có baseline |
| R-3 | **Bỏ qua cấu hình quay** (D-R24 không áp cho luồng này). Hệ quả: kết quả chỉ là **gợi ý** cho tới khi người xác nhận, đúng như ảnh cột |
| R-4 | `data_source = field` |

Ticket chạm **API, schema, phân quyền, audit và lưu trữ video** ⇒ theo `.ai/README.md` bắt buộc hai pha. **Đây là PHA 1:
khảo sát và đề xuất, KHÔNG sửa code — dừng cứng ở cuối.**

## Đọc trước

- `AGENTS.md` (= `CLAUDE.md`): **BE-11** (proxy, ghi object trước row sau, thumbnail bỏ EXIF), **BE-15 P2a** (clip 300 MiB, stream
  S3 multipart, không giữ khoá qua upload), **BE-23a / BE-19** (đúng một `AuditEvent` mỗi `SaveChanges`), **BE-34** (phiên bản
  model / extractor), **Quy tắc dễ sai âm thầm** (`data_source`, `unknown` ≠ `out`, `dim` theo baseline).
- Code: `src/LuxMap.Modules.WorkOrders/WorkOrderPolePhotosController.cs` + service, `IPolePhotoPublication.cs`,
  `src/LuxMap.Shared/AI/SingleLampPhotoHints.cs` (`ISingleLampPhotoHinter`), `src/LuxMap.Modules.Survey/Processing/Frames/**`
  (`FfmpegFrameExtractor`, `SurveyFramePipeline`, `OnOffDetector`), `src/LuxMap.Modules.Survey/Ingest/SweepsController.cs`.
- Docs: `docs/contract-drift.md` (PP-1…PP-15, D-R16, D-R24, D-R28), `docs/survey-ingest-p2a.md`,
  `docs/agents/traps/media-storage.md`, `docs/agents/traps/survey.md`, `docs/agents/traps/deploy-ops.md` (guard body Caddy, PP-8).

## Pha 1 — phải trả lời (ghi vào `.ai/results/POLE-VIDEO-p1.md`, tiếng Việt)

1. **Tái dùng được gì?** So với `work_order_pole_photo` (mở rộng bảng thêm loại media, hay bảng riêng `work_order_pole_video`?),
   `FfmpegFrameExtractor` (hiện chọn frame theo cửa sổ `VideoClockMapping` quanh cột — clip một đèn không có mapping, trích theo
   khoảng đều trên cả clip cần đường vào mới hay đủ tham số?), `ISingleLampPhotoHinter` (nhận `Stream` ảnh — gọi theo từng frame?).
   Nêu ưu nhược, trích file:dòng.
2. **Kết luận cho cả clip.** Nhiều frame ON/OFF ⇒ một nhãn `on | off | inconclusive`. Đề xuất luật tổng hợp (đa số? ngưỡng
   confidence? bao nhiêu frame tối thiểu?), con số lấy từ `system_setting` hay `appsettings` — **không viết luật thứ hai** nếu
   luật của gợi ý ảnh dùng lại được. Clip mà đèn không xuất hiện / ban ngày ⇒ gì?
3. **Lưu gì, giữ gì.** Video gốc (bucket `luxmap-video`, nguyên byte, key, `StoredObjectColumn` cho BE-35). Chỉ giữ **1–3 ảnh
   bằng chứng** (tiêu chí chọn: sát nhãn kết luận + confidence cao nhất?), phần frame còn lại chỉ để detect rồi bỏ — ảnh bằng chứng
   đi cùng khuôn ảnh gốc nguyên byte + thumbnail sạch của PP. Ước dung lượng mỗi clip, tác động lên ổ 40 GB của VPS, cần cap
   clip bao nhiêu (đề xuất thấp hơn 300 MiB của sweep).
4. **Phạm vi phiếu và người gọi:** theo PP D-1/D-4 (cả ba loại phiếu, `in_progress`, người được giao)? Capability có sẵn
   (`ExecuteWorkOrders` / `ReadWorkOrders` / `ReviewSurveys`) hay mới? `pole_id` thuộc tập cột WO-12?
5. **`data_source = field` (R-4):** lấy từ **cột** như PP (không nhận từ client). Cột testbed mang `calibration_rig` thì
   sao — từ chối upload video lên cột testbed, hay ghi theo cột? Luật "dữ liệu testbed không bao giờ mang `field`" **không được vỡ**.
6. **D-R24 bị bỏ qua (R-3):** hệ quả cho kết quả, cho precision/recall (có phải loại clip này khỏi thống kê chính thức, báo
   riêng?), cho nhãn trong response (`config_verified = false`?). Đề xuất cách ghi vào drift.
7. **Xử lý đồng bộ hay nền?** Clip ngắn: đo thời gian trích frame + suy luận (nếu đo được không cần sửa code). Giữ nguyên nguyên tắc
   của PP-7: không giữ khoá qua upload / suy luận; model lỗi vẫn nhận clip và trả `unavailable`. Nếu nền thì dùng `scheduled_job`.
8. **Idempotency + offline:** `client_op_id` (khuôn PP-6; băm nội dung clip ra sao khi 100 MB?), gửi lại → 200 cùng kết quả,
   cùng khoá khác nội dung → 409. Clip **không** đi qua `POST /sync/push` (S-4) — mobile xếp hàng multipart riêng thế nào.
9. **Luồng xác nhận.** Tái dùng `/review` (kỹ sư sửa nhãn) và `/decision` (Quản lý) của PP-11…PP-15? Có gì khác với ảnh
   (ví dụ xem video trước khi duyệt: endpoint proxy video, `no-store`, Range request)?
10. **Bề mặt API:** route (đề xuất dưới `/work-orders/{id}/pole-videos`), response snake_case, mã lỗi (400/403/404/409/413/415),
    guard body ở Caddy (bài học PP-8), OpenAPI.
11. **Kế hoạch Pha 2:** file tạo / sửa, migration (chỉ thêm, đọc `Up()`/`Down()`), test (gồm phá thử, ffmpeg có trong CI không),
    drift mới cần ghi (mã đề xuất `PV-1…`), `copy_dev_to_supabase.py` `PLAN`, ai phải báo (WP5 màn xem video, WP6 quay + upload).

Cuối file: danh sách **D-item** (`D-1`, `D-2`, …), mỗi cái có phương án, khuyến nghị và lý do — Mỹ chốt trước Pha 2.
Mọi khẳng định về code phải kèm `đường/dẫn:dòng`; output lệnh dán nguyên văn.

## KHÔNG ĐƯỢC làm (Pha 1)

- **Không sửa bất kỳ file nào ngoài `.ai/results/POLE-VIDEO-p1.md`.** Không sửa `src/`, `tests/`, migration, Contract, drift,
  `CLAUDE.md`, `tracking.html`.
- Không chạy `dotnet ef database update`, không chạm DB `luxmap_dev`, không đọc `.env`, không gọi API / MinIO thật.
- Không chạy toàn bộ `dotnet test`. Build (`dotnet build --disable-build-servers`) được.
- Không commit, không push, không `git add -A`.
- Không mở rộng sweep / `POST /sweeps`; không thêm `dim`; không đổi enum `fixture_status`.
- Không tự chọn phương án kiến trúc: chỗ mơ hồ ⇒ D-item.

---

## Quyết định của Mỹ — 04/11/2026 (đáp D-item của `.ai/results/POLE-VIDEO-p1.md`)

Nền **SELF-SIGNED**; chạm bề mặt API ⇒ nền tạm cho tới FW kế tiếp.

| D | Chốt |
|---|---|
| **D-1** | **Phương án C**: thêm cột nullable vào chính `work_order_pole_photo` (clip: `video_object_key`, `video_sha256`, `video_bytes`, `duration_ms`, bốn bộ đếm frame, `extractor_version_id`, min/median/max confidence theo frame), một CHECK all-or-none; khai `StoredObjectColumn` cho `video_object_key`/`video_bytes`. `byte_count`/`width`/`height` của dòng **vẫn mô tả frame**, không phải clip — ghi rõ ở drift. Không bảng con |
| **D-3** | **Phương án A**: suy luận **đồng bộ** trong request. Còn phải chốt ngưỡng chuyển sang nền (đề xuất p1: tổng request > 20 s ở 2 vCore) — đo ở Pha 2, Mỹ chốt số |
| **D-13** | **Có**: Quản lý **phải xem clip** trước khi `confirm` nhãn `off` (vì xác nhận `off` tạo `lamp_out` ở trạng thái `Confirmed` ngay, `ReviewedPolePhotoPublisher.cs:62-68`). |

**Hệ quả suy ra (xin Mỹ xác nhận, chưa phải quyết định):**
- **D-9 → B**: cần phát clip có **Range** (thêm đọc theo khoảng vào `IObjectStore`); tải trọn 100 MiB qua 4G không chấp nhận được khi xem là bắt buộc.
- **Cách ép "đã xem" ở server** (Pha 2 phải đề xuất, Mỹ chốt): ghi nhận lượt xem (`video_viewed_by/at`, có audit) khi người có `ReviewSurveys` mở `GET …/video`; `confirm` nhãn hiệu lực `off` mà chưa có lượt xem ⇒ **409** (mã đề xuất `POLE_VIDEO_NOT_VIEWED`). Câu hỏi mở: có áp cho `on` không (p1 chỉ nêu `off`, vì `on` không tạo sự cố nhưng có thể đổi `out → normal`); lượt xem có cần chạm tới cuối/đủ tỉ lệ clip không.

**Chưa chốt — giữ khuyến nghị của p1 cho tới khi Mỹ nói khác:** D-2 (PUT thân thô), D-5, D-6, D-7, D-8, D-10, D-11, D-12, D-4, D-9, D-13 đã chốt ở mục dưới; **D-14** đã chốt ở mục cuối.

## Quyết định bổ sung — 04/11/2026 (D-4, D-9, D-13)

Mỹ đồng ý toàn bộ đề xuất. Nền **SELF-SIGNED**, chạm bề mặt API ⇒ nền tạm.

| D | Chốt |
|---|---|
| **D-4** | Luật nhãn clip với **số tạm** (đặt ở `appsettings` section `PoleVideo`, mọi số nằm trong `artifact_version`, chỉnh sau khi có số đo + clip một đèn thật): nhãn từng frame dùng nguyên luật ảnh một đèn; **có nhãn ≥ 60 % số frame lấy mẫu**; **đồng thuận ≥ 70 %** trong số có nhãn, không thì `inconclusive`; **frame đầu và frame cuối phải cùng nhãn**, lệch ⇒ `inconclusive`; confidence = **trung vị** các frame đồng thuận; **sàn clip 3 s** (dưới ⇒ 400 `VIDEO_TOO_SHORT`), trần 30 s; **bỏ 1 s đầu chỉ khi clip > 6 s**; frame đại diện = frame **gần trung vị confidence** (không phải max). Response trả mã lý do khi không kết luận (`NO_LAMP`, `MULTIPLE_LAMPS`, `TOO_BRIGHT`) và số frame ban ngày / độ sáng trung vị để người duyệt thấy. **Chưa chốt, chờ đo/clip thật:** fps lấy mẫu (4 fps × 30 s = 120 lần suy luận trong một request — suy luận chưa đo), ngưỡng ban ngày `Citizen:DaylightMeanLuma` 0,45 (clip thật có keyframe đêm đạt 0,47–0,49), trần confidence 0,9 và cổng không gian "tâm box" của Codex (loại nhầm đèn sát mép khung như `IMG_0464`) |
| **D-9** | **Phương án B**: phát clip **có Range** — thêm đọc theo khoảng vào `IObjectStore` (S3 `ByteRange`), `GET …/{photo_id}/video` trả `206`/`Accept-Ranges`/`Content-Range`, kiểm quyền theo xã **mỗi request**, `Cache-Control: no-store`, vẫn proxy qua API (BE-11 quy tắc 1) |
| **D-13** | Chốt chi tiết: luật xem áp cho **mọi `confirm`** (`on` lẫn `off`); `reject` **không** cần xem. "Đã xem" = người **đang quyết định** (không phải người khác) đã mở `GET …/video` **thành công ít nhất một lần**; server ghi `video_viewed_by/at` + audit; thiếu ⇒ **409 `POLE_VIDEO_NOT_VIEWED`**. Đây là rào cản, không chứng minh người đó thật sự đã xem; bản chặt (cộng dồn đủ ~80 % byte qua Range) để sau nếu cần |

**Pha 2 phải làm thêm vì các quyết định trên:** cột `video_viewed_by`, `video_viewed_at` (thuộc D-1 C, có CHECK cặp-hoặc-không); capability `ReadWorkOrders` đủ cho `GET …/video` nhưng ghi lượt xem chỉ tính khi người đó có `ReviewSurveys`; test: confirm thiếu lượt xem 409, người khác xem không thay được, reject không cần xem, Range 206 đúng đoạn và 416 khi sai đoạn, quyền xã kiểm mỗi request, audit đúng một event mỗi `SaveChanges`.

## Quyết định bổ sung — 04/11/2026 (D-14)

| D | Chốt |
|---|---|
| **D-14** | **Chấp nhận clip HDR HLG và gắn cờ `hdr`.** Không từ chối upload (cấu hình mặc định của iPhone là HDR; 12/12 clip thật trong `Videos/` là HLG 10-bit BT.2020). Pha 2: ffprobe đọc `pix_fmt` + `color_transfer` (HLG `arib-std-b67` hoặc PQ `smpte2084` ⇒ `hdr = true`), lưu cờ ở cột clip (thuộc D-1 C) và đưa vào `LabelSnapshot` cùng `capture_config = not_verified`; hiện ở `PolePhotoItem.video`. Tone-map khi trích frame **chưa làm**: `zscale` chưa kiểm trên ffmpeg của VPS — việc đo (YOLO trên cùng frame có / không tone-map) để sau. Khi có thống kê dùng clip, lọc theo `hdr` như D-8 (hiện `Statistics` không đọc bảng này) |

Ticket sẵn sàng cho **Pha 2**; kế hoạch chia PR (PV-a…PV-d), test và phá thử ở `.ai/results/POLE-VIDEO-p1.md` mục 11. Tham số D-4 còn chờ đo là fps lấy mẫu và ngưỡng ban ngày; ngưỡng chuyển sang nền (D-3) chốt sau khi đo.
