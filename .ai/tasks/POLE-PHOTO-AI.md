---
ticket: POLE-PHOTO-AI
title: Kỹ sư chụp ảnh một cột trong phiếu → AI phân loại, LƯU kết quả gắn pole_id + work_order_id
status: ready
phase: 1
owner: codex
branch: docs/POLE-PHOTO-AI-p1
depends_on:
  - "AI-1 (docs/contract-drift.md mục AI-1): POST /ai/detect, model YOLO normal/out — không ghi gì"
  - "CITIZEN-QR P2c (CQ-5, CLAUDE.md mục P2c): gợi ý AI cho ảnh công dân — đã có luật ảnh ban ngày, artifact_version, audit 'analyzed'"
---

## Bối cảnh

Mỹ đề xuất một endpoint mới (nguyên văn yêu cầu):

```
POST /api/v1/work-orders/{work_order_id}/pole-photos
multipart/form-data:
  pole_id        (bắt buộc)
  photo          (JPEG, giống giới hạn AiController.MaxUploadBytes = 16MB)
  captured_at    (ISO 8601 UTC)
  lat, lng       (EPSG:4326)
  client_op_id   (UUID, chống trùng — đúng pattern idempotency đã dùng khắp API)
Trả về: kết quả AI phân loại cột đó (classified_as), có lưu lại (không như /ai/detect hiện tại),
gắn đúng pole_id/work_order_id
```

Ticket chạm **API, schema, phân quyền và audit** ⇒ theo `.ai/README.md` bắt buộc hai pha. **Đây là PHA 1: khảo sát
và đề xuất, KHÔNG sửa code — dừng cứng ở cuối.**

## Đọc trước

- `AGENTS.md` (= `CLAUDE.md`): mục **AI-1**, **CITIZEN-QR P2c** (gợi ý AI, `YoloCitizenPhotoHinter`, `artifact_version`,
  audit `analyzed`, không inject `YoloModel` vào service trên đường request), **BE-11** (lưu ảnh: proxy, JPEG magic bytes,
  ghi object trước row sau, thumbnail bỏ EXIF), **BE-23a / BE-19** (audit: đúng một event mỗi `SaveChanges`),
  **BE-43** (sync offline: `client_op_id`, `sync_operation`, op_type mới phải gọi service gốc), **BE-15 P2a** (không giữ
  khoá qua stream upload), **BE-42** (lux ≠ luminance_history), **Quy tắc dễ sai âm thầm** (`data_source`, `unknown` ≠ `out`,
  ngưỡng theo baseline của chính cột, mọi phát hiện tự động ghi phiên bản model).
- Code: `src/LuxMap.Modules.AI/**` (`AiController`, `YoloModel`, `YoloOnOffDetector`), `src/LuxMap.Modules.WorkOrders/**`
  (`WorkOrderEvidenceService`, `FaultPhotosController`, `GET /work-orders/{id}/poles` WO-12, loại phiếu, quyền người được giao),
  `src/LuxMap.Modules.Citizen/**` (gợi ý AI cho ảnh), `src/LuxMap.Modules.Survey/**` (`pole_observation`, `survey_frame`,
  `classified_as`, ai sở hữu quyền ghi `pole_current_status` / `luminance_history`), `src/LuxMap.Modules.Sync/**`.
- Docs: `docs/api-contract-v1.1.md` (§2 ma trận vai trò, §3 work order, evidence), `docs/contract-drift.md` (AI-1, EV-1/EV-2,
  WO-12, CQ-*, I-18), `docs/database/erd.md`, `docs/registration/FA26SE222_v1.4.md` (CV quyết ON/OFF; `dim` đo bằng BH1750).

## Pha 1 — phải trả lời (ghi vào `.ai/results/POLE-PHOTO-AI-p1.md`, tiếng Việt)

1. **Đã có gì gần giống?** So endpoint đề xuất với: `POST /work-orders/{id}/evidence` (kind `observation` ở phiếu kiểm tra),
   `POST /faults/{id}/photos`, gợi ý AI của ảnh công dân, `pole_observation` của pipeline khảo sát. Có nên **mở rộng** một
   đường sẵn có (ví dụ evidence + gợi ý AI) thay vì endpoint/bảng mới không? Nêu ưu nhược, trích file:dòng.
2. **`classified_as` trả gì được thật?** Model chỉ có `normal | out`. Enum `fixture_status` có `normal | dim | out | unknown`.
   Một ảnh **không** cho ra `dim` (v1.4: dim đo bằng BH1750 so baseline). Ảnh ban ngày / không thấy bóng / nhiều bóng ⇒ gì?
   Đề xuất ánh xạ + ngưỡng confidence, tái dùng luật của gợi ý công dân nếu hợp lý — **đừng** viết luật thứ hai.
3. **Kết quả này có được làm đổi trạng thái cột không?** (`pole_current_status`, `luminance_history`, bản đồ, fault tự sinh).
   Theo `CLAUDE.md` quyền ghi các bảng đó thuộc pipeline khảo sát. Đề xuất mặc định: **chỉ là gợi ý lưu lại, không đổi trạng
   thái, không tự tạo sự cố** — xác nhận hoặc phản biện có căn cứ.
4. **Phạm vi phiếu và người gọi:** loại phiếu nào (khảo sát / kiểm tra / sửa chữa), trạng thái phiếu nào, chỉ người được giao?
   Capability nào (có sẵn hay mới)? `pole_id` phải thuộc tập cột của phiếu (cùng luật WO-12)? Có kiểm `lat/lng` cách cột bao xa?
5. **Lưu trữ:** bảng mới (đề xuất tên, cột, FK, CHECK, index, `commune_id` + `ICommuneScoped`, `data_source` lấy từ cột) hay tái
   dùng `repair_evidence`? Bucket/key MinIO; ảnh gốc nguyên byte hay làm sạch như ảnh công dân? Thumbnail + endpoint xem ảnh.
   `IAudited` + audit action nào. Teardown/`copy_dev_to_supabase.py` `PLAN`. Mọi cột `double` theo quy ước NaN/Infinity.
6. **AI chạy đồng bộ trong request hay nền?** Thời gian suy luận đo được (nếu đo được không cần sửa code thì đo), khoá/transaction
   (không giữ khoá qua lúc chạy model), khi model lỗi / thiếu file thì upload vẫn thành công?
7. **Idempotency + offline:** `client_op_id` lưu ở đâu, gửi lại trả gì (200 cùng kết quả), cùng khoá khác nội dung trả gì. Có cần
   `op_type` mới cho `POST /sync/push` không (ảnh multipart không đi qua sync JSON — mobile làm thế nào khi offline)?
8. **Response shape** đề xuất (snake_case, kèm `photo_id`, `model_version`/`artifact_version`, `confidence`, `classified_as`,
   bbox?, `thumbnail_url`), mã lỗi (400/403/404/409/413/415), và danh sách endpoint đọc lại (liệt kê ảnh theo phiếu / theo cột?).
9. **Kế hoạch Pha 2:** file sẽ tạo/sửa, migration (chỉ thêm?), test sẽ viết (gồm phá thử), drift mới cần ghi (mã đề xuất `PP-1…`),
   ai phải báo (WP5/WP6).

Cuối file: danh sách **D-item** (`D-1`, `D-2`, …), mỗi cái có phương án, khuyến nghị, và lý do — Mỹ chốt trước Pha 2.
Mọi khẳng định về code phải kèm `đường/dẫn:dòng`; output lệnh dán nguyên văn.

## KHÔNG ĐƯỢC làm

- **Không sửa bất kỳ file nào ngoài `.ai/results/POLE-PHOTO-AI-p1.md`.** Không sửa `src/`, `tests/`, migration, Contract, drift,
  `CLAUDE.md`, `tracking.html`.
- Không chạy `dotnet ef database update`, không chạm DB `luxmap_dev`, không đọc `.env`, không gọi API thật.
- Không chạy toàn bộ `dotnet test` (DB/container dùng chung với lượt khác). Build (`dotnet build`) được.
- Không commit, không push, không `git add -A`.
- Không tự chọn phương án kiến trúc: chỗ mơ hồ ⇒ D-item.
