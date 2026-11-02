---
ticket: POLE-SEGMENT-NULL
title: Phase 2 — cột chưa thuộc tuyến (`pole.segment_id` nullable), chia nhiều PR, bật nullable ở CUỐI
status: in_progress
phase: 2
owner: codex (viết) · claude (review + commit + chạy test)
branch: feat/POLE-SEGMENT-NULL-2a-schema
contract_refs:
  - "docs/contract-drift.md — mục POLE-SEGMENT-NULL (tạo ở PR 2a)"
depends_on:
  - ".ai/results/POLE-SEGMENT-NULL-p1.md (Phase 1, đã review) — D-1…D-14 chốt 02/11/2026"
---

## Quyết định đã chốt — Mỹ, 02/11/2026 (chấp nhận nguyên khuyến nghị của Phase 1)

Bốn quyết định gốc trong `.ai/tasks/POLE-SEGMENT-NULL.md` giữ nguyên. Cách thực thi:

| Mã | Quyết định |
|---|---|
| D-1 | Phiếu inspection/repair: vẫn hiện cột mang sự cố của phiếu; cột chưa tuyến có `segment_id: null`, `position: null`, xếp nhóm cuối theo `created_at, length(pole_id), pole_id`. **Chặn `null == null`** ở `WorkOrderPoleService.Targets` (`w.SegmentId == p.SegmentId`). Cùng predicate cho danh sách và ảnh. Survey không nhận cột chưa tuyến |
| D-2 | POST: vắng hoặc `null` = chưa tuyến. PUT: vẫn bắt buộc có key `segment_id` nhưng nhận `null` rõ ràng. Import: ô trống ở cột MỚI = null; ô trống/thiếu cột ở cột ĐÃ CÓ = giữ tuyến; muốn tháo tuyến dùng PUT `null`. Header `segment_external_ref` optional. Bộ lọc `unassigned_segment=true` trên `GET /assets/poles` (không đổi nghĩa `unassigned` của feeder). `segment_external_ref` có giá trị nhưng không khớp → VẪN lỗi dòng, không rơi về null |
| D-3 | `GET /map/cabinets/{id}/topology`: có cột chưa tuyến → từ chối kèm `pole_ids` (không bịa cạnh) |
| D-4 | `GET /sync/bundle` mặc định gồm cả cột / sự cố / phiếu chưa tuyến của phiếu mở được giao; không kéo mọi cột chưa tuyến của xã; không thêm `null` vào `segment_ids[]`; metadata scope + eviction chốt với WP6 |
| D-5 | Dời cột chưa tuyến: cho phép, không đo hành lang, audit không dựng `segment_distance` giả |
| D-6 | `segment_name: null` (FE tự hiện "Chưa phân tuyến"); `PublicQrInfo` cũng null, QR không bị coi là invalid |
| D-7 | Gán / tháo tuyến chỉ đổi cột; fault, history, cluster, snapshot cũ giữ nguyên |
| D-8 | Cột chưa tuyến không thuộc target khảo sát; cluster nhận cột chưa tuyến → từ chối, không nới `fault_cluster` |
| D-9 | Thống kê: nhóm chưa tuyến riêng + số đếm riêng; không gộp vào tuyến khác hay `out`; không thêm giá trị enum |
| D-10 | Lighting: lọc `null` khỏi `affected_segment_ids`; thêm đếm cột chưa tuyến bị ảnh hưởng; `uncontrollable_pole_count` không tăng vì chưa tuyến |
| D-11 | Giữ mock cũ; thêm ví dụ/fixture chưa tuyến riêng; sửa cảnh báo "bắt buộc" của `photos_to_poles.py` |
| D-12 | `Down()`: từ chối khi còn `pole.segment_id IS NULL` (preflight + `SET NOT NULL` trong transaction); không xoá/gán/default ngầm |
| D-13 | Chưa thêm `segment_source` |
| D-14 | 400 `VALIDATION_FAILED` cho input sai; 409 với mã mới **`POLE_SEGMENT_REQUIRED`** (`details.pole_ids` + thao tác bị chặn) cho trạng thái chưa tuyến. Không trả 404 như thể cột không tồn tại |

## Lộ trình PR — tuần tự, MỖI PR vào `dev`, KHÔNG xếp chồng (đợi PR trước merge, rebase lên `dev`)

Nguyên tắc: **các lớp bảo vệ có trước, nút bật (API tạo được cột null) ở cuối**. Sau PR 2a schema cho phép null nhưng
không đường API nào tạo ra nó; test dựng cột null trực tiếp qua `DbContext`.

| PR | Nội dung |
|---|---|
| **2a (LÀM BÂY GIỜ)** | Migration nới `pole.segment_id` + entity/config + mục drift + ERD. Hành vi API KHÔNG đổi |
| 2b | Phiếu / khảo sát / sự cố / ảnh cột / citizen: D-1, D-5, D-7, D-8, D-14 (mã lỗi) |
| 2c | Đọc: assets, map, topology, thống kê, lighting: D-3, D-6, D-9, D-10 + OpenAPI sinh lại |
| 2d | Sync bundle: D-4 (cần WP6 xác nhận trước khi merge) |
| 2e | NÚT BẬT: POST/PUT/import optional + bộ lọc `unassigned_segment`, D-2, D-11, templates, README |

**Chỉ làm PR 2a trong lượt này.** Không chạm phần của 2b–2e kể cả khi thấy rõ.

## Phải đọc trước

`CLAUDE.md` (= `AGENTS.md`), `.ai/README.md`, `.ai/results/POLE-SEGMENT-NULL-p1.md` (mục 2, 6, 7 và D-12),
`docs/agents/traps/migrations.md`, `assets.md`, `commune-scope.md`, `testing.md`, `docs/contract-drift.md` (mục TOPO-INFER làm mẫu định dạng),
`docs/database/erd.md`, `tests/LuxMap.Api.Tests/PolePhotoMigrationTests.cs` (mẫu test migration có `Down()` từ chối mất dữ liệu).

## Yêu cầu — PR 2a

1. `Pole.SegmentId` thành `string?`; navigation `Pole.Segment` nullable; quan hệ optional. **Giữ** tên FK `fk_pole_road_segment_segment_id`,
   `Restrict`, `ix_pole_segment_id`, không FK ghép segment/commune (cố ý, trap assets mục 3).
2. Migration `PoleSegmentNullable`: `Up()` chỉ `ALTER COLUMN segment_id DROP NOT NULL`. `Down()` theo D-12 (preflight đếm null → dừng với
   thông điệp rõ; không `defaultValue`, không backfill). **Đọc `Up()`/`Down()` trước khi apply**: không `DropIndex` / `DropColumn` /
   `AlterColumn` nào khác; nếu EF sinh drop/re-add FK vì required→optional thì chỉnh lại cho đúng tên/Restrict và giải thích trong kết quả;
   `Down()` đối xứng. Snapshot khớp.
3. Mọi chỗ biên dịch vỡ vì kiểu nullable: sửa **tối thiểu** để hành vi cột CÓ tuyến y hệt cũ. Đường API vẫn BẮT BUỘC `segment_id`
   (POST/PUT/import không đổi). Chỗ nào hành vi cho cột null chưa được định nghĩa ở PR này thì KHÔNG tự quyết — liệt kê trong
   `results/POLE-SEGMENT-NULL-p2a.md` (file:dòng + PR nào xử lý). Không thêm `// TODO`.
4. `docs/contract-drift.md`: thêm mục **POLE-SEGMENT-NULL** theo khuôn TOPO-INFER (bốn trường Decision / Decision maker = Mỹ 02/11/2026 /
   Chạm API = Có / Nền = SELF-SIGNED tạm tới FW; bảng D-1…D-14 ở trên; ghi rõ **chưa có hiệu lực với client cho tới PR 2e**; mục
   "Phải báo" WP5 / WP6 / WP4 — **Chưa báo**).
5. `docs/database/erd.md`: cardinality `road_segment ||--o{ pole` thành optional ở phía cột (`erd.md:300`), ghi quyết định.
6. Test (Testcontainers PostGIS thật qua `LuxMapTestHost`; không `luxmap_dev`):
   - `Pole_without_segment_round_trips_and_stays_commune_scoped` — cột null ghi/đọc được qua `DbContext`; `CommuneWriteGuard` / query filter vẫn
     áp; segment non-null sai vẫn bị FK chặn.
   - `Nullable_pole_segment_schema_and_rollback_are_explicit` — theo mẫu `PolePhotoMigrationTests`: catalog (cột nullable, FK tên/`Restrict`, index còn),
     `Down()` từ chối khi có null và **giữ nguyên hàng**, `Down()` thành công khi không còn null. Nếu repo chưa có khung chạy Down phù hợp thì
     ghi D-item, KHÔNG dựng hạ tầng test lớn.
   - Test hiện có giữ nguyên kỳ vọng; chỉ sửa test nào ghim `NOT NULL` của `segment_id`.
   - **Phá thử** từng chốt: khôi phục `.IsRequired()` → test null đỏ; bỏ preflight Down → test Down đỏ; khôi phục bằng `cp`/`touch`, đọc **mã thoát** của `dotnet test`.
7. Build / test: `dotnet build --disable-build-servers` (hoặc `MSBUILDDISABLENODEREUSE=1`) rồi `dotnet test`. Nếu sandbox không chạy được Docker
   / test, **nói thẳng** trong kết quả phần nào chưa chạy — đừng báo xanh khi chưa chạy. Claude sẽ chạy lại.

Tiêu chí xong PR 2a:

- [ ] `dotnet build` 0 lỗi, không cảnh báo mới
- [ ] `dotnet test` xanh (mã thoát 0) HOẶC báo rõ phần chưa chạy được
- [ ] Hai test mới ở trên + bằng chứng phá thử (lệnh + output)
- [ ] `git diff origin/dev --stat` chỉ gồm: Pole entity/config, migration + snapshot, các sửa biên dịch tối thiểu, test, `contract-drift.md`, `erd.md`, `.ai/results/POLE-SEGMENT-NULL-p2a.md`
- [ ] `results/POLE-SEGMENT-NULL-p2a.md` có: Up/Down đã đọc (dán nguyên văn), danh sách chỗ cho cột null chưa xử lý (cho 2b–2e), output build/test thật, D-item mới nếu có

## KHÔNG ĐƯỢC làm

- Không làm bất cứ thứ gì của PR 2b–2e. Không đổi POST/PUT/import/bộ lọc/DTO/OpenAPI trong PR này.
- Không sửa `docs/api-contract-v1.1.md`, `docs/openapi/*.json`, `tracking.html` (Claude cập nhật sau review).
- Không chạy trên `luxmap_dev`; không `dotnet ef database update` lên DB thật.
- Không `DropIndex` / `DropColumn` / thay FK ngoài yêu cầu; không tuyến giả; không backfill; không default.
- Không mở lại D-1…D-14; không thêm `segment_source`.
- Không `git commit` (sandbox không ghi được `.git` của worktree — Claude commit sau review), không `git push`, không `git add -A` / `git add .`.
- Không thêm project reference ngược chiều; không `ExecuteUpdate` / `ExecuteDelete`.
- Chỗ mơ hồ → D-item ở cuối file kết quả, không tự chọn.
