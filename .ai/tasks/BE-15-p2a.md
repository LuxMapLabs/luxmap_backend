---
ticket: BE-15
title: Phase 2a — nhận dữ liệu phiên khảo sát (schema, upload video streaming, file thô, nộp phiên, GET /sweeps)
status: done
phase: 2
owner: codex
branch: feat/BE-15-survey-ingest
contract_refs:
  - "mục 2 — capability (O-2): ReadSurveys / SubmitSurveys / ReviewSurveys"
  - "mục 5.5 — work order task_kind survey"
  - "mục 5.6 — GET /sweeps"
depends_on:
  - "Phase 1 đã chốt: .ai/results/BE-15-p1.md §11 (D-01…D-15)"
---

## Bối cảnh

Phase 1 đã chốt (`.ai/results/BE-15-p1.md`, đặc biệt **§2, §3, §4 và §11**; ERD `docs/database/erd.md` Q1–Q9).
Phase 2 chia ba lát, mỗi lát một PR: **P2a nhận dữ liệu (ticket này)** → P2b xử lý (worker, ffmpeg, detector giả,
ghép cột, phân loại) → P2c duyệt và công bố. Không làm việc của P2b/P2c ở đây.

Mục chạm API là **SELF-SIGNED, nền tạm tới FW** — ghi rõ trong XML doc và results.

## Phải đọc trước

- `AGENTS.md` toàn bộ — đặc biệt: quy ước ID (`luxmap_format_id`, sequence, `ORDER BY created_at, length(id), id`),
  `ICommuneScoped` + `HasCommuneReference` + guard `SaveChanges`, `IAssigneeScoped`, audit (BE-23a, BE-19 — `Fault`
  là `IAudited`), quy ước `double precision` (CHECK NaN/±Infinity), **bẫy partial index**, **đọc migration trước khi
  apply**, `ExecuteUpdate/Delete` bị cấm, `UtcMicrosecondClock`, BE-11 (proxy, không presigned, `IObjectStore`),
  `WireEnum`, ma trận capability (test kỳ vọng LITERAL).
- `.ai/results/BE-15-p1.md` §2.1–2.3, §2.6–2.7, §3 (bảng endpoint), §4 (file thô, đồng hồ), §11 (chốt).
- `docs/database/erd.md` (Q1–Q9).
- Code: `src/LuxMap.Modules.Survey/*`, `src/LuxMap.Modules.WorkOrders/*`, `src/LuxMap.Infrastructure.Storage/*`,
  `src/LuxMap.Shared/Authorization/LuxMapPolicies.cs`, `src/LuxMap.Persistence/PrefixedId*.cs`, `docker-compose*.yml`.

## Yêu cầu

1. **Schema (một migration, tên rõ nghĩa):**
   - `work_order.task_kind` nhận thêm `survey`; thêm `anchor_commune_id`? — **không**: phiếu đã có `commune_id` làm xã neo
     (D-02 gọi là anchor). Phiếu survey: `segment_id` = NULL, `fault_ids` rỗng.
   - `work_order_segment(work_order_id, position, segment_id)` — PK (work_order_id, position), FK `Restrict`, unique
     `(work_order_id, segment_id)` (không lặp tuyến trong một phiếu), `commune_id` + `ICommuneScoped` theo khuôn
     `work_order_fault`.
   - `survey_sweep` — ID `SWP` qua `luxmap_format_id` + `sweep_id_seq` (prefix đã giữ chỗ). `work_order_id` FK,
     `commune_id` (= xã của phiếu), `captured_by`, `client_op_id` (unique theo `(captured_by, client_op_id)`),
     `boot_session_id`, `elapsed_anchor_ns` bigint, `utc_anchor` timestamptz, `utc_uncertainty_ms`, `status`
     (`uploading|queued|processing|awaiting_review|accepted|returned|failed`), `processing_status`
     (`not_started|queued|processing|succeeded|failed`), `data_source`, `submitted_at`, timestamps, `xmin` concurrency.
     `IAudited` (tạo, nộp). `ICommuneScoped`.
   - `survey_video_clip` — PK nội bộ bigint identity; `(sweep_id, clip_no)` unique; `object_key`, `sha256`, `byte_count`,
     `content_type`, `stored_at`. Không `commune_id` (luôn đi qua sweep — mọi đường đọc/ghi lookup sweep đã scope trước).
   - `survey_gps_sample` — PK `(sweep_id, sample_no)`; `phone_elapsed_ns` bigint, `geom geometry(Point,4326)`,
     `accuracy_m`, `heading_deg?`, `speed_mps?`, `provider`. CHECK hữu hạn + miền (accuracy ≥ 0, heading [0,360)).
   - `survey_lux_sample` — PK `(sweep_id, sample_no)`; `module_epoch`, `seq`, `module_ms` bigint, `phone_elapsed_ns`
     bigint, `lux` (CHECK ≥ 0 và hữu hạn).
   - `survey_raw_file` (hoặc cột trên sweep — chọn và ghi lý do): kind `gps_track|lux_log|capture_config`, sha256,
     byte_count, object_key, schema_version — giữ **nguyên byte** file thô trong object store.
   - FK `pole_current_status.last_sweep_id → survey_sweep` (`Restrict`) — nợ BE-09.
   - Mở rộng CHECK entity/action của `audit_event` cho `survey_sweep`.
   - **Đọc migration sinh ra, liệt kê mọi `DropIndex`/`DropColumn`/`AlterColumn` trong results** (phải không có cái nào
     ngoài ý định). Không thêm cột vật lý `xmin`.
2. **Capability** `ReadSurveys` (4 vai trò), `SubmitSurveys` (field_engineer), `ReviewSurveys` (manager — khai sẵn, chưa
   có endpoint ở P2a). Cập nhật `CapabilityMatrixTests` + `RoleCapabilityMatrixTests` bằng **literal**.
3. **Lưu trữ:** thêm cổng ghi **stream** vào `IObjectStore` (S3 multipart, không đọc cả clip vào RAM, hash SHA-256 và đếm
   byte trong lúc stream, huỷ thì abort multipart). Bucket mới `luxmap-video` (sidecar `minio-mc` tạo). Giữ nguyên
   `StoreImageAsync` JPEG.
4. **Endpoint** (theo bảng §3 của p1, rút gọn cho P2a):
   - `POST /work-orders` nhận `task_kind = survey` + `segment_ids[]` có thứ tự; detail trả danh sách tuyến.
   - `POST /sweeps` (SubmitSurveys, người được giao phiếu) — idempotent `client_op_id`: cùng body → 200 cùng ID, khác
     body → 409 `IDEMPOTENCY_CONFLICT`.
   - `PUT /sweeps/{id}/clips/{clip_no}` — body nhị phân MP4, header `X-Content-SHA256` + `Content-Length`; giới hạn riêng
     **300 MiB** (không nâng giới hạn toàn cục); cùng hash → 200, khác hash → 409; 413 quá cỡ; 415 không phải MP4
     (kiểm `ftyp` magic).
   - `PUT /sweeps/{id}/raw/{kind}` — JSONL theo §4.1 (GPS, lux: bigint dạng chuỗi chữ số), JSON cấu hình §4.2.
     **Parse toàn file, validate xong mới ghi một lượt**; lỗi trả 400 kèm dòng/trường. NaN/Infinity → 400.
   - `POST /sweeps/{id}/submit` — đủ ít nhất 1 clip + 3 file thô, manifest hash khớp → 202, `status = queued`;
     thiếu → 409 `UPLOAD_INCOMPLETE` kèm danh sách. Retry → 200 trạng thái hiện tại.
   - `GET /sweeps` (Contract §5.6 + lọc `work_order_id`, `processing_status`, `data_source`; phân trang, max 200) và
     `GET /sweeps/{id}`. `coverage_pct`, `frame_count` = null/0 cho tới P2b.
   - Mọi lookup ngoài phạm vi / không phải người được giao → **404**, không lộ tồn tại.
5. **Test tích hợp** (PostGIS thật, collection tài sản theo quy ước): idempotent tạo sweep (cả đồng thời), clip đúng/sai
   hash/quá cỡ/không phải MP4, parse GPS/lux (NaN, Infinity, bigint lớn, dòng hỏng cuối file → không ghi gì), submit
   thiếu/đủ, phạm vi xã + người được giao (404), ma trận capability literal, thứ tự `GET /sweeps` theo khuôn ID.
   Test lưu trữ stream **không cần MinIO** (fake), như `LuxMap.Infrastructure.Storage.Tests`.

Tiêu chí xong (Codex tự kiểm được trong sandbox):

- [ ] `dotnet build` 0 warning.
- [ ] Test **không cần DB** chạy xanh (Shared, Storage). Test tích hợp viết xong — **Claude chạy** trên `luxmap_test`.
- [ ] Migration đã sinh + đã đọc; results liệt kê thao tác của `Up()`/`Down()`.
- [ ] `.ai/results/BE-15-p2a.md`: đã làm gì, file nào, quyết định nhỏ tự chọn (ghi lý do), việc để lại cho P2b/P2c.

## KHÔNG ĐƯỢC làm

- Không làm P2b/P2c: không worker, không ffmpeg, không detector, không ghép cột, không bảng run/pass/frame/detection/
  observation/luminance/baseline, không duyệt/công bố.
- **Không đọc `.env`.** Không kết nối DB/Docker/MinIO/Supabase/Roboflow. Lệnh EF cần connection string thì đặt biến
  môi trường **giả** `ConnectionStrings__LuxMap=Host=localhost;Database=none;Username=x;Password=x` (`migrations add` không
  kết nối). **Không** `dotnet ef database update`.
- Không chạy test tích hợp cần DB (Claude chạy). Không xuất lại OpenAPI (Claude chạy, cần cấu hình host).
- Không sửa `docs/api-contract-v1.1.md`, mock, `docs/openapi/*`.
- Không thêm NuGet package. Không `ExecuteUpdate/ExecuteDelete`. Không presigned URL.
- Không commit, không push, không `git add -A`.

## Dừng ở đâu

Xong tiêu chí trên thì dừng, ghi results. Claude review, chạy test + OpenAPI, sửa và commit.
