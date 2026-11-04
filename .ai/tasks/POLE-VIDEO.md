---
ticket: POLE-VIDEO
title: Kỹ sư quay một clip ngắn chứa MỘT đèn trong phiếu → AI phân loại ON/OFF, LƯU kết quả gắn pole_id + work_order_id
status: ready
phase: 1
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
