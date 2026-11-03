# POLE-SEGMENT-NULL PR 2b — review của Claude · 03/11/2026

Người viết: Codex (`.ai/results/POLE-SEGMENT-NULL-p2b.md`). Codex không chạy được test / Docker trong sandbox. Claude chạy lại mọi thứ, phá thử và commit.

## Kết luận: ĐẠT — không có yêu cầu sửa

| Hạng mục | Kết quả |
|---|---|
| `Targets` | Hai nhánh theo tuyến được bọc trong `p.SegmentId != null && (…)`; nhánh theo sự cố giữ nguyên. Một predicate dùng chung cho danh sách cột và upload ảnh (kể cả lượt kiểm lại dưới khoá) |
| `GET /work-orders/{id}/poles` | `segment_id`, `position` nullable; nhóm chưa tuyến xếp cuối theo `created_at, length(pole_id), pole_id`; đánh số cột có tuyến trước phân trang; hành vi cột có tuyến không đổi |
| Dời cột | `pole.SegmentId is null` → không truy vấn tuyến, không `POLE_OFF_SEGMENT`; audit `segment_id` và `segment_distance_m` cùng `null` (không dựng số 0 giả) |
| Phạm vi | Chỉ 2 file `src` (+ test, drift, trap, OpenAPI, kết quả). Không migration, POST/PUT/import vẫn bắt buộc tuyến, không mã lỗi `POLE_SEGMENT_REQUIRED` (chưa có chỗ dùng → 2c) |
| `fault_cluster` | Xác nhận không có writer trong `src/`; Codex không dựng, ghi thành mục "để CV-15" + bẫy trong `traps/faults-workorders.md` |

## Bằng chứng Claude chạy (worktree `wt-pole-segment-null-2b`, `MSBUILDDISABLENODEREUSE=1`)

- `dotnet build --disable-build-servers` → 0 Warning, 0 Error.
- `dotnet test` toàn bộ: Shared 386, Persistence 45, Storage 54, Api **1243 pass / 7 skip / 0 fail** (1235 của 2a + 8 mới; 7 skip có sẵn: ACCT-1, MQTT broker).
- **OpenAPI:** sinh lại bằng lệnh chuẩn của `.ai/context/commands.md` (`dotnet swagger tofile` + `gen_consolidated_spec.py`) → `luxmap-v1.json` và `luxmap-v1.5.json` **giống từng byte** bản Codex đã tạo bằng exporter dự phòng. Diff chỉ là `nullable: true` ở `WorkOrderPole.segment_id` và `position`.
- **Phá thử** (mỗi lần đọc thông báo lỗi, khôi phục bằng `cp`, xác nhận bằng `cmp`):

| # | Đột biến | Kết quả |
|---|---|---|
| M1 | Bỏ `p.SegmentId != null &&` trong `Targets` | 3 test đỏ: danh sách cột `Expected: 4 / Actual: 5`; upload ảnh cột không liên quan `Expected: 409 / Actual: 201`; phiếu khảo sát chứa cột chưa tuyến (`Assert.Single` thấy 2 mục). Chứng minh `null == null` là lỗi THẬT và test bắt được |
| M2 | Khôi phục bản `AssetCrudService` gốc (đo hành lang theo tuyến null) | `Relocating_unassigned_pole_follows_explicit_policy` đỏ: `Expected: OK / Actual: InternalServerError` (đúng dự đoán 500) |
| M3 | `position = 0` thay `null` cho cột chưa tuyến | `Work_order_poles_excludes_unrelated…` đỏ: `Expected: Null / Actual: Number` |

## Chưa phá thử riêng (ghi trung thực)

- `Citizen_accept_on_unassigned_pole_…`, `Survey_publication_after_unassigning_a_pole_…` và assert trong `OpenApiSpecTests` chạy xanh nhưng tôi chưa bẻ chốt riêng cho chúng. Hai test đầu khẳng định hành vi mà production code hiện đã đúng sẵn (không cần sửa code), nên chúng là test khoá hồi quy, không phải chốt mới.
- `Survey_never_targets_unassigned_poles` đỏ dưới M1 vì nó gọi `GET /work-orders/{id}/poles`; nó KHÔNG kiểm trực tiếp `SurveyChainageQuery.Poles` (JOIN `p.segment_id = r.segment_id`). Nếu muốn khoá riêng truy vấn đó, thêm ở 2c hoặc PR sau.

## Ghi chú (không chặn merge)

- Dòng thông báo lỗi `POLE_OFF_SEGMENT` vẫn nội suy `offSegment` (kiểu `double?`) thay vì `distance`; chạy đúng vì chỉ vào nhánh non-null, nhưng gọn hơn nếu dùng `distance`. Để nguyên, không đáng một vòng sửa.
- `Along` của nhóm chưa tuyến là 0 và `CreatedAt` của nhóm có tuyến là `default` (không ảnh hưởng thứ tự); đúng, nhưng dễ gây thắc mắc cho người đọc sau.
