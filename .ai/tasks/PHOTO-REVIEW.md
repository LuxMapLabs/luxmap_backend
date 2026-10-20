---
ticket: PHOTO-REVIEW
title: Kỹ sư sửa nhãn AI của ảnh cột (final_cv_state) trước khi Quản lý quyết định công bố
status: review
phase: 2
owner: codex
branch: feat/PHOTO-REVIEW
depends_on:
  - "POLE-PHOTO-AI (drift PP-1…PP-10): upload ảnh cột, cv_state, POST …/decision của Quản lý, PoleStatusPublication"
---

## Bối cảnh

Đề xuất của WP5 (20/10/2026): bỏ `decision`, `decided_by`, `decided_at`, `decision_note`; thêm `final_cv_state`, `review_note`,
`reviewed_by`, `reviewed_at`; endpoint mới `PATCH /api/v1/work-orders/{id}/pole-photos/{photo_id}/review`
(`ExecuteWorkOrders`, body `{final_cv_state: on|off, review_note}`; 400 khi khác `cv_state` mà thiếu note; 409 khi phiếu không
`in_progress`; 404 ngoài phiếu/xã). Review Claude + Codex 20/10: **xoá luồng quyết định sẽ mất công bố trạng thái, mất tạo
`lamp_out`, mất reject, và cho kỹ sư tự công bố** — bác phần xoá.

## Mỹ đã chốt (20/10/2026)

- **R-1** **Quản lý vẫn là người công bố.** Giữ nguyên `POST …/decision` (`ReviewSurveys`) và bốn trường `decision*` — thay đổi
  là **thêm**, không xoá (không breaking).
- **R-2** Thêm `final_cv_state`, `review_note`, `reviewed_by`, `reviewed_at` vào `PolePhotoItem` (mọi response dùng nó) và
  endpoint `PATCH …/review` cho **người được giao**, phiếu `in_progress`, ảnh **chưa có quyết định**.
- **R-3** Quản lý `confirm` thì công bố **nhãn đã review** (`final_cv_state` nếu có, không thì `cv_state`), và phải ghi được
  rằng nhãn do người sửa.

## KHÔNG ĐƯỢC làm

- Không xoá/đổi tên trường hay endpoint hiện có; không đổi policy của `/decision`.
- Không ghi `confidence` / `model_version` của AI như thể là của nhãn người sửa — giữ nguyên bộ AI, nguồn của nhãn tách riêng.
- Không viết đường công bố thứ hai: đi qua `IPolePhotoPublication` → `PoleStatusPublication.StageAsync`.
- Không phá thứ tự khoá `work_order → pole → photo`, không bỏ luật "mỗi `SaveChanges` đúng một audit" (`WorkOrderPolePhoto` là `IAudited`).
- Không dùng positional record cho body JSON (CLAUDE.md POLE-PHOTO-AI: `sealed class` + `init`).
- Không đụng branch `feat/WO-SURVEY-CHAIN` (Claude đang giữ).

## Pha 1 — phải trả lời (`.ai/results/PHOTO-REVIEW-p1.md`)

1. Ngữ nghĩa `final_cv_state` khi chưa review: trả `cv_state` (kể cả `inconclusive`/null) hay null? Lưu hay tính lúc đọc?
2. `cv_state` null/inconclusive: kỹ sư gán on/off được không, `review_note` bắt buộc? Sau đó Quản lý confirm được không
   (hiện 409 `POLE_PHOTO_INCONCLUSIVE`)? Confidence/model/artifact truyền gì cho publication và cho fault `lamp_out`
   (hiện publisher đòi đủ bộ AI)? `source_channel` của fault từ nhãn người sửa?
3. Review lại nhiều lần (PATCH lặp), review về đúng `cv_state` (xoá note?), đua với `/decision` (khoá nào, thứ tự nào).
4. Schema: cột, FK `reviewed_by → app_user` (Restrict), CHECK (cặp by/at, giá trị on/off, note khi khác cv_state, độ dài),
   migration, `copy_dev_to_supabase.py` PLAN, audit action nào.
5. Test (literal, sabotage, khoá), drift PP-11+, OpenAPI sinh lại + `gen_consolidated_spec.py`.
6. D-item còn mở.

## Mỹ chốt D-item Phase 1 (20/10/2026) — Phase 2 làm theo đây

Nền là `.ai/results/PHOTO-REVIEW-p1.md` (thiết kế Codex). Chốt:

- **D-1** Chưa review: bốn trường review đều `null`; fallback `final_cv_state ?? cv_state` chỉ ở lúc công bố. Không backfill.
- **D-2** Kỹ sư gán `on/off` cả khi AI null/inconclusive, note bắt buộc khi khác `cv_state` (kể cả null). Sửa nhiều lần trước
  decision; PATCH giống hệt là no-op (không audit, không đổi `reviewed_at`). Không có reset-null.
- **D-3** Nhãn do người đổi (khác AI / AI không kết luận): `status_confidence = null`. Nới `ck_pole_current_status_confidence_matches_status`
  thành `fixture_status <> 'unknown' OR status_confidence IS NULL`; giữ CHECK range. Sửa `PoleStatusPublication.StageAsync` tương ứng.
  Không nới CHECK history/observation.
- **D-4** Fault từ nhãn người đổi: `source_channel = field_report`, `reported_by = reviewed_by`, `status_confidence = null`,
  `detection_model_version = null`; nhãn đúng AI giữ `cv` như hiện nay. Sửa `ck_fault_pole_photo_source`.
- **D-5** KHÔNG làm `If-Match`. Ghi rõ trong drift: `/decision` công bố review mới nhất sau khoá.

## Phase 2 — phạm vi

Implement đúng P1 + D-1…D-5 trên branch `feat/PHOTO-REVIEW` (worktree này). Bắt buộc: migration `AddPolePhotoReviews` (đọc `Up()`/`Down()`
trước khi apply — không `DropIndex`/`DropColumn` lạ, không cột `xmin`, giữ hai constraint DB-only); test theo bảng P1 mục 5 (ưu tiên các test
quyền, note, dùng nhãn review khi confirm, không mượn confidence AI, field_report + hai actor, khoá/đua review–decision, CHECK DB);
drift **PP-11…PP-15** ở `docs/contract-drift.md` (SELF-SIGNED 20/10/2026, chạm API → nền tạm); một đoạn ngắn ràng buộc kỹ thuật vào
`CLAUDE.md` mục POLE-PHOTO-AI nếu có bẫy mới; `scripts/copy_dev_to_supabase.py` (`reviewed_by`); export OpenAPI → `gen_consolidated_spec.py`
→ lint. Ghi `.ai/results/PHOTO-REVIEW-p2.md` với output terminal THẬT.

KHÔNG: commit/push (Claude commit), sửa `tracking.html`, đụng branch/thư mục khác, chạy `--apply` script copy.
