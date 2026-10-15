---
ticket: POLE-PHOTO-AI
title: Kỹ sư chụp ảnh một cột trong phiếu → AI phân loại, LƯU kết quả gắn pole_id + work_order_id
status: review
phase: 2
owner: codex
branch: feat/POLE-PHOTO-AI
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

---

## Quyết định của Mỹ — 15/10/2026 (đáp D-item của `.ai/results/POLE-PHOTO-AI-p1.md`)

Nền **SELF-SIGNED**, chạm bề mặt API ⇒ chưa ổn định cho tới FW kế tiếp; mọi mục drift ghi rõ "nền tạm".

| D | Chốt |
|---|---|
| D-1, D-4 | **Phương án B**: bảng mới hẹp `work_order_pole_photo`, endpoint dưới phiếu. Cả **ba loại phiếu** (survey / inspection / repair), **chỉ `in_progress`**, chỉ người được giao. Ảnh này **không** thay ảnh `after` của evidence. |
| D-2 | Nhãn là **enum riêng `on \| off \| inconclusive`** (tên trường đề xuất `cv_state`/`ai_label` — Codex chọn một tên, ghi drift), **KHÔNG** mượn `fixture_status`, **không** có `dim`. `null` = AI chưa chạy được (`ai_status = unavailable`). |
| D-5 | **ĐỔI so với khuyến nghị: kết quả ẢNH ĐƯỢC cập nhật trạng thái cột — nhưng CHỈ sau khi Quản lý xác nhận ảnh.** Chi tiết bên dưới (D-5a…D-5e). |
| D-3, D-6…D-17 | **Theo đúng khuyến nghị của Codex ở mục 11** (port AI trung lập trong Shared, Citizen ủy quyền và giữ hash cũ; `ExecuteWorkOrders` / `ReadWorkOrders`; tập cột WO-12 liên xã, xã của ảnh lấy từ cột; chưa kiểm khoảng cách GPS; ID tiền tố mới do DB sinh; chuỗi phiên bản như Citizen; ảnh gốc nguyên byte + thumbnail sạch, bucket `luxmap-evidence`; audit entity `work_order`; chỉ route dưới phiếu, chưa bbox; AI đồng bộ ngoài transaction, model lỗi vẫn nhận ảnh; `client_op_id` bắt buộc + băm nội dung + 409 khi khác; không `op_type` sync mới; file ≤ 16 MiB / request ≤ 17 MiB / 40 MP trước decode; `captured_at` bắt buộc có Z/offset). |

### D-5 — Quản lý xác nhận ảnh thì trạng thái cột mới đổi

- **D-5a Luồng:** kỹ sư upload → AI gán `on/off/inconclusive` (lưu, như trên) → **Quản lý** quyết định trên ảnh: **xác nhận** hoặc **bác**. Chỉ **xác nhận** mới chạm trạng thái cột. Đề xuất route
  `POST /api/v1/work-orders/{id}/pole-photos/{photo_id}/decision` body `{decision: confirm|reject, note?}` (Codex có thể đề xuất hình dạng khác, ghi drift). Capability: dùng capability sẵn của Quản lý cho việc duyệt (`ReviewSurveys` hoặc capability duyệt sự cố hiện có — chọn cái hợp nghĩa nhất, ghi lý do; **không** mở cho kỹ sư). Quyết định một lần (đã quyết → 409), khoá hàng đúng thứ tự, audit `confirmed` / `rejected` (actor User).
- **D-5b Ánh xạ khi XÁC NHẬN** (không ghi `luminance_history`, không đụng baseline):
  - `off` → `fixture_status = out`.
  - `on` → `normal` **chỉ khi** trạng thái hiện tại là `out` hoặc `unknown` (hoặc chưa có dòng). Đang `dim` hoặc `normal` → **giữ nguyên** (ảnh "on" không bác bỏ được dim).
  - `inconclusive` / AI null → **không xác nhận được** (409 hoặc 400, ghi drift) — không có gì để áp.
- **D-5c Không ghi đè dữ liệu mới hơn:** chỉ áp khi `captured_at` của ảnh **mới hơn** mốc quan sát hiện tại của cột (`pole_current_status.last_seen_at` hoặc tương đương). Ảnh cũ hơn kết quả sweep đã công bố → xác nhận vẫn được ghi (audit) nhưng **không đổi trạng thái**, response nói rõ `status_applied = false` + lý do.
- **D-5d Sự cố:** xác nhận `off` → tạo **một** fault `lamp_out`, `source_channel = cv`, `data_source` lấy từ cột, xã/tuyến từ cột, `confirmed` theo cùng luật của sự cố do sweep công bố, có audit, **nếu cột chưa có fault `lamp_out` đang mở** (`FaultStatusSets.Open`, đừng viết lại tập). Ảnh "on" **không** tự đóng sự cố nào. Fault phải truy vết được về ảnh (cột/trường nguồn gốc, khuôn `origin_observation_id` / `origin_node_id`).
- **D-5e Quyền ghi `pole_current_status`:** hiện thuộc pipeline khảo sát (BE-15/17). Đường ghi mới **phải đi qua cùng một chỗ / cùng luật** với lượt công bố sweep (`SurveyReviewService.Publish` hoặc phần dùng chung tách ra) — tôn trọng CHECK `ck_pole_current_status_confidence_matches_status` / `confidence_range`, `data_source`, `status_confidence` hữu hạn 0..1. Không viết đường ghi thứ hai lệch luật. Nếu cần port để WorkOrders không tham chiếu Survey (vòng phụ thuộc), khai port như `IActiveWorkOrderLookup`. `last_sweep_id` của ảnh: Codex quyết hợp lý (ví dụ giữ nguyên) và ghi drift + CLAUDE.md.

## Pha 2 — làm

1. Hiện thực đúng các quyết định trên, theo kế hoạch mục 9 của P1 (file, migration chỉ thêm, đọc Up()/Down() trước khi apply, port AI, endpoint POST / list / detail / thumbnail / original / decision).
2. Test tích hợp (HTTP + DB) và **phá thử** cho từng luật quan trọng: quyền 4 vai trò, người được giao, tập cột WO-12, replay/409, model lỗi vẫn 201, pixel cap trước decode, D-5b từng nhánh (dim giữ, out→normal, off→out), D-5c ảnh cũ không ghi đè, D-5d không tạo fault trùng, quyết định hai lần → 409, audit đúng một event mỗi `SaveChanges`. Ghi rõ phá thử nào đỏ.
3. Chạy **toàn bộ** `dotnet test LuxMap.slnx` (BE-36: container riêng mỗi lượt — được phép) và đọc **mã thoát**; build `-warnaserror`; xuất lại OpenAPI + `gen_consolidated_spec.py` + redocly lint (lệnh trong `.ai/context/commands.md` / CI). Thêm bảng mới vào `scripts/copy_dev_to_supabase.py` `PLAN`.
4. Ghi drift **PP-1…PP-n** vào `docs/contract-drift.md` (Decision maker: Mỹ, 15/10/2026, SELF-SIGNED, nền tạm, Consumer WP5/WP6 chưa báo), ràng buộc kỹ thuật vào `CLAUDE.md` (mục mới cuối file, khuôn các mục khác), thẻ tiến độ đầu `tracking.html`.
5. Kết quả + output lệnh thật vào `.ai/results/POLE-PHOTO-AI-p2.md`.

## KHÔNG ĐƯỢC làm (Pha 2)

- Không chạm DB `luxmap_dev` (không `database update` trên nó, không `psql` vào nó). `.env` có sẵn cho tiến trình test đọc — **không in, không sửa, không chép nội dung** của nó. Không gọi API/MinIO thật ngoài test.
- Không commit, không push, không `git add -A`. Claude review diff rồi commit.
- Không thêm package NuGet. Không đổi enum `fixture_status` hay enum đã khoá ở Contract mục 1. Không đổi hành vi `POST /ai/detect` hay gợi ý công dân (hash `artifact_version` của Citizen phải giữ nguyên khi output không đổi — có test chứng minh).
- Không sửa Contract `docs/api-contract-v1.1.md` (chỉ drift).
- Không `ExecuteUpdate/ExecuteDelete`, không backdoor ngoài fixture test, không `ORDER BY` thẳng theo ID hay enum text.
- Gặp chỗ mơ hồ mới ⇒ chọn phương án an toàn nhất, ghi thành D-item ở cuối `results/…-p2.md`, đừng âm thầm mở rộng phạm vi.
