---
ticket: BE-15
phase: 2a
status: done
author: codex (hiện thực) · claude (review, sửa, kiểm)
branch: feat/BE-15-survey-ingest
date: 2026-10-02
---

# BE-15 P2a — nhận phiên khảo sát

Codex hiện thực theo `.ai/tasks/BE-15-p2a.md` nhưng bị dừng vì quá giới hạn thời gian trước khi viết file
này; Claude review, sửa và viết lại kết quả.

## Đã làm

- **Migration `AddSurveyIngest`**: `survey_sweep` (ID `SWP` qua `luxmap_format_id`, `status` tách khỏi
  `processing_status`, `xmin` concurrency, `ICommuneScoped` + `IAudited`), `survey_video_clip`,
  `survey_raw_file` (bảng riêng, một dòng/loại — giữ hash, byte, key của file thô nguyên byte),
  `survey_gps_sample`, `survey_lux_sample`, `work_order_segment` (tuyến có thứ tự, FK ghép
  `(work_order_id, commune_id)`); `task_kind` nhận `survey` + CHECK `ck_work_order_survey_target`;
  FK `pole_current_status.last_sweep_id` (nợ BE-09); CHECK audit nhận `survey_sweep` / `submitted`.
- **Capability**: `ReadSurveys` (4 vai trò), `SubmitSurveys` (field_engineer), `ReviewSurveys` (manager, khai
  sẵn). Test ma trận literal ở cả `CapabilityMatrixTests` và `RoleCapabilityMatrixTests`.
- **Lưu trữ**: `IObjectStore.StoreStreamAsync` + `MultipartUpload` (part 5 MiB, hash và đếm byte khi stream,
  kiểm `ftyp`, abort khi lỗi/huỷ); bucket `luxmap-video` do sidecar tạo.
- **Endpoint** (SELF-SIGNED, nền tạm tới FW; payload ở `docs/survey-ingest-p2a.md`):
  `POST /work-orders` với `task_kind = survey`; `POST /sweeps`; `PUT /sweeps/{id}/clips/{n}`;
  `PUT /sweeps/{id}/raw/{kind}`; `POST /sweeps/{id}/submit`; `GET /sweeps`; `GET /sweeps/{id}`.

## Migration — đã đọc

`Up()`: 6 `CreateTable`, 14 `CreateIndex`, 1 `CreateSequence`, 1 `AddForeignKey`, 4 `AddCheckConstraint`,
3 `DropCheckConstraint` (dựng lại `ck_work_order_task_kind`, `ck_audit_event_action`,
`ck_audit_event_entity_type` với giá trị mới). **Không** `DropIndex` / `DropColumn` / `AlterColumn`, không
cột `xmin` vật lý; `ix_survey_sweep_commune_id` và `ix_work_order_segment_commune_id` có mặt. `Down()` đối
xứng. ⚠️ Như `AddFaultReview`: `Down()` gãy khi đã có audit `entity_type = 'survey_sweep'` (bảng audit không
cho xoá) — rollback chỉ chạy trên DB chưa có phiên nào.

## Review của Claude — đã sửa

1. **Khoá hàng bằng `FromSql SELECT * … FOR UPDATE`** → `42703 xmin` → 500 ở mọi lượt ghi (5 test tích hợp
   đỏ). Đổi sang khuôn `FaultLocks`.
2. **Upload clip giữ khoá `work_order` suốt lúc stream** → Quản lý không sửa/giao lại/huỷ được phiếu trong
   vài phút. Nay kiểm dưới khoá, nhả, stream, khoá lại để ghi. Test mới; phá thử (giữ khoá) → đỏ.
3. **`GET /sweeps` N+1** (3 truy vấn/phiên, tới 600/trang) → 3 truy vấn/trang.
4. **Abort multipart lỗi đè lỗi gốc** (413 thành 500) → log cảnh báo, giữ lỗi gốc. Test mới; phá thử → đỏ.
5. **`SurveyMigrationContextFactory`** liệt kê module bằng tay → gỡ, khôi phục README. Kiểm
   `has-pending-model-changes` qua host thật: không đổi.
6. Nhỏ: bỏ một truy vấn đọc lại phiếu thừa; tách cấu hình FK `pole_current_status` ra class riêng.

## Kiểm

- `dotnet build -warnaserror`: 0 warning.
- `luxmap_test` (đã áp migration): Api 536/536, Persistence 36/36, Storage 25/25. Shared 173/173 (chạy không
  nạp `.env` — biến `JWT_SIGNING_KEY` trong shell làm một test cố ý-thiếu-key đỏ, không liên quan).
- OpenAPI xuất lại + `gen_consolidated_spec.py` (thêm summary 6 op, xoá stub `GET /sweeps` cùng
  `SweepItem`/`SweepPagedResult`/`PAGE`): 56 op đã hiện thực, lint 3 cảnh báo như `dev`.
- `luxmap_dev` **chưa** migrate.

## Để lại

- **P2b**: worker theo lease, ffmpeg cắt frame tại mọi lần ngang cột dự kiến, detector giả, ghép cột,
  phân loại, `coverage_pct` / `frame_count`.
- **P2c**: duyệt phiên (`ReviewSurveys`), công bố sau khi Quản lý chấp nhận.
- Thực địa: D-06 (mẫu thiết bị WP6), D-07 (JSON Roboflow WP4).
- Chưa báo WP5/WP6 (drift mục "BE-15 P2a").
- Survey WO đi được `start`; luồng `complete`/`verify` cho phiếu survey chưa có test riêng — xét ở P2c.
