# POLE-SEGMENT-NULL PR 2a — review của Claude · 02/11/2026

Người viết: Codex (`.ai/results/POLE-SEGMENT-NULL-p2a.md`). Codex không chạy được test (sandbox chặn socket / Docker, thiếu `.env` trong
worktree). Claude chạy lại mọi thứ ở cây này và commit.

## Kết luận: ĐẠT — không có yêu cầu sửa

| Hạng mục | Kết quả |
|---|---|
| `Up()` | Một `AlterColumn` `segment_id` → `nullable: true`. Không `DropIndex` / `DropColumn` / drop FK |
| `Down()` | Preflight `RAISE EXCEPTION` khi còn `segment_id IS NULL`, sau đó `ALTER … SET NOT NULL`. Không default, không backfill. Đúng D-12 |
| Snapshot | Chỉ bỏ hai `.IsRequired()` (thuộc tính + quan hệ `Segment`); tên FK `fk_pole_road_segment_segment_id`, `Restrict`, `ix_pole_segment_id` giữ nguyên |
| Hành vi API | Không đổi: POST/PUT/import vẫn bắt buộc tuyến. 10 file `src` chỉ thêm `!` để biên dịch |
| Drift | Mục POLE-SEGMENT-NULL đủ bốn trường, bảng D-1…D-14, ghi "chưa có hiệu lực với client cho tới PR 2e", "Chưa báo" |

## Bằng chứng Claude chạy (worktree `wt-pole-segment-null-2a`, `MSBUILDDISABLENODEREUSE=1`)

- `dotnet build --disable-build-servers` → 0 Warning, 0 Error.
- Hai test mới + `PolePhotoMigrationTests`: `Passed! Failed: 0, Passed: 3`.
- `dotnet test` toàn bộ: Persistence 45, Storage 54, Shared 386, Api **1235 pass / 7 skip / 0 fail** (7 skip có sẵn: ACCT-1 "awaiting My review" và
  môi trường MQTT broker — không do PR này).
- **Phá thử 1** — khôi phục `.IsRequired()` ở `AssetConfigurations.cs:198`: cả hai test mới đỏ với `PendingModelChangesWarning`
  (model lệch snapshot). Đỏ ở lúc dựng host nên chứng minh được tính nhất quán model ↔ migration; KHÔNG chứng minh riêng assert null round-trip.
- **Phá thử 2** — bỏ khối preflight trong `Down()`: `Nullable_pole_segment_schema_and_rollback_are_explicit` đỏ
  (`Expected: "P0001"`). Khôi phục bằng `cp`, `cmp` xác nhận hai file giống bản của Codex.

## Ghi chú (không chặn merge)

- D-15 (chưa có bằng chứng runtime) → đã đóng bằng các lệnh trên. D-16 (ba file `.ai/` nằm sẵn trong commit nền) → chủ ý: PR 2a mang theo
  task + kết quả Phase 1 vì không có PR riêng cho Phase 1.
- `Nullable_pole_segment_schema_and_rollback_are_explicit` ghim `previous = "20261029090000_AccountsSingleCommune"`. Nếu migration khác lên `dev`
  trước PR này thì phải rebase và đưa `PoleSegmentNullable` về đúng vị trí; test sẽ đỏ nếu quên.
- Các `!` ở `AssetCrudService`, `CitizenReviewService`, `MapQueryService`, `PoleDetailService`, `CabinetTopologyService`, `LightingCommandService`,
  `WorkOrderPoles` chỉ để biên dịch. Cột null đi vào chúng sẽ ra `null` trong DTO non-null cho tới PR 2b–2e. An toàn vì chưa đường API nào tạo
  cột null; danh sách đầy đủ ở mục 4 của file kết quả.
- Phá thử 1 bắt ở cấp host. Test round-trip riêng (null → ghi → đọc) sẽ có giá trị phá thử độc lập hơn nếu sau này có cách tắt kiểm tra
  pending-model; để 2b xem lại.
