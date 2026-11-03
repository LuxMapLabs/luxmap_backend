# POLE-SEGMENT-NULL — kết quả PR 2c · 03/11/2026

**Nguồn gốc file này:** Codex làm xong code, test, OpenAPI và tài liệu rồi dừng vì báo hết hạn mức (`You've hit your usage limit … try again at 4:03 PM`)
TRƯỚC khi viết file kết quả. Claude viết file này từ những gì đã tự kiểm; không có output nào ở đây do Codex sinh ra.

## 1. Phạm vi đã làm (D-3, D-6, D-9, D-10, D-14)

| Việc | Nơi đổi | Trạng thái |
|---|---|---|
| D-6 `segment_id` / `segment_name` null ở bề mặt đọc | `PoleListItem`, `PoleDetail`, `TopologyPole`, `PoleProperties`, `PoleMapDetail`, `WarrantyItem`, `PublicQrInfo`, `CitizenReportDetail`; `AssetCrudService`, `PoleDetailService`, `MapQueryService`, `PublicReportService`, `CitizenReviewService` | Xong; không còn `SegmentId!` do 2a thêm ở các file này |
| D-3 sơ đồ nhánh cabinet | `CabinetTopologyService`: kiểm cột chưa tuyến ở đầu, 409 `POLE_SEGMENT_REQUIRED`, `details{operation, pole_ids[≤100], pole_count}`; `ErrorCodes.PoleSegmentRequired`; `[ProducesResponseType(409)]` | Xong |
| D-9 thống kê | `FixtureStatusRow.unassigned_segment_pole_count`; nhóm `segment_id: null` đứng cuối; warranty `segment_id` nullable (CSV ô trống) | Xong |
| D-10 lighting | `affected_segment_ids` không còn null; `unassigned_segment_pole_count` (`int?`) ở preview + kết quả tạo mới; KHÔNG lưu bền, đọc lại / replay là `null` | Xong |
| OpenAPI | Sinh lại bằng lệnh chuẩn; override `PoleProperties.segment_id` ở `gen_consolidated_spec.py` thành `allOf:[$ref SegmentId] + nullable` | Xong |
| Tài liệu | drift (mục POLE-SEGMENT-NULL), traps `spatial-map` / `iot-lighting` / `citizen` | Xong |

`SyncPoleProperties.segment_id` nullable chảy theo lớp cha `PoleProperties`; logic `SyncBundleService` KHÔNG đổi (2d).
Các `SegmentId!` còn lại trong `src` đều ngoài phạm vi 2c: `AssetCrudService` dòng 301 / 483 (POST/PUT, nút bật 2e), `WorkOrderPoles.cs:108` (2b, đã xử lý ở #199), `SurveyProcessor.cs:92` (không liên quan).

## 2. Bằng chứng Claude chạy

- Build `MSBUILDDISABLENODEREUSE=1 dotnet build --disable-build-servers`: 0 Warning, 0 Error.
- `dotnet test` toàn bộ: Shared 386, Persistence 45, Storage 54, Api **1241 pass / 7 skip / 0 fail** (7 skip có sẵn: ACCT-1, MQTT broker).
- OpenAPI sinh lại bằng `dotnet swagger tofile` + `gen_consolidated_spec.py`: **giống từng byte** bản Codex để lại.
- Phá thử (mỗi lần đọc thông báo lỗi, khôi phục bằng `cp`, xác nhận bằng `cmp`):

| # | Đột biến | Test đỏ | Lý do |
|---|---|---|---|
| S1 | Tắt guard cabinet (`if (false && …)`) | `Cabinet_topology_refuses_with_unassigned_poles_and_lists_them` | `Expected: Conflict` |
| S2 | `segment_name ?? ""` ở chi tiết bản đồ | `Unassigned_pole_remains_in_inventory_bbox_detail_and_feeder_lists` | `Expected: Null` |
| S3 | Số cột chưa tuyến của lighting luôn 0 | `Lighting_handles_unassigned_poles_without_null_affected_ids` | `Expected: 2` |
| S4 | Bỏ `ThenBy(SegmentId == null)` ở thống kê | `Statistics_count_unassigned_separately_without_losing_unknown` | `Expected: Null` (nhóm null không còn cuối) |
| S5b | `segment_name ?? ""` ở QR lookup | `Qr_lookup_and_citizen_review_work_for_unassigned_pole` | `Expected: Null` |
| S6 | `segment_name ?? ""` ở inventory | `Unassigned_pole_remains_in_inventory_bbox_detail_and_feeder_lists` | `Expected: Null` |

- **S5 (đột biến tương đương, không phải lỗ hổng):** đổi `.FirstOrDefault()` về `.First()` ở `PublicReportService` KHÔNG làm test đỏ vì trong subquery EF cả hai dịch ra cùng SQL (`NULL` khi không có dòng). Chốt thật của QR là giá trị `null`, đã phá ở S5b.

## 3. D-item

- **D-18 — lưu bền `unassigned_segment_pole_count` của lighting?** Snapshot lệnh (`AffectedSegmentIds`, `UncontrollablePoleCount`) nằm trong DB; lưu thêm cần migration. Hiện không lưu. Cần Mỹ quyết nếu muốn đọc lại được số này cho lệnh cũ.
- **D-19 — hành vi đổi ở ca có tuyến:** `segment_name` của cột nằm trên tuyến liên xã bị ẩn bởi phạm vi xã trước đây là `""`, nay là `null` (cùng D-6). Client nào đang kiểm `== ""` phải đổi. Báo WP5 khi thông báo POLE-SEGMENT-NULL.
