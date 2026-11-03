# POLE-SEGMENT-NULL — kết quả PR 2d · 03/11/2026

Người viết: Claude (Codex `gpt-6.1-sol` bị chặn bởi lỗi hạn mức). Review độc lập: Codex `-p river` (DeepSeek-V4.1-Flash, chỉ đọc, ~220k token) → **APPROVE WITH NITS**, không có P1/P2.

## 1. Vấn đề và thay đổi

Trước PR này, một phiếu sửa chữa / kiểm tra mà sự cố nằm ở cột chưa tuyến (`fault.segment_id = null`) KHÔNG vào gói offline của kỹ sư: danh sách tuyến suy ra từ phiếu không chứa nó,
nên cột, sự cố và chính phiếu đều thiếu. PR này:

```text
default bundle (no segment_id)
  unassigned = poles with segment_id IS NULL
               that carry a fault linked (released_at IS NULL) to one of MY open orders (assigned | in_progress)
  poles       += unassigned
  open_faults += open faults of unassigned poles
  work_orders += my open orders holding a fault on an unassigned pole
named bundle (segment_id=…)   unchanged: exactly those segments
segment_ids[]                 never contains null; may be [] when the only work is unassigned
```

Một nơi tính (`MyUnassignedPolesAsync`), truyền vào ba nơi đọc; không viết lại điều kiện.

## 2. Bằng chứng

- `dotnet build --disable-build-servers`: 0 Warning, 0 Error.
- `dotnet test` toàn bộ: Shared 386, Persistence 45, Storage 54, Api **1256 pass / 7 skip / 0 fail** (1249 của 2c + 7 mới; 7 skip có sẵn: ACCT-1, MQTT broker).
- `luxmap-v1.json` sinh lại bằng lệnh chuẩn: KHÔNG đổi; `luxmap-v1.5.json` chỉ đổi chuỗi tóm tắt `/sync/bundle`.
- 7 test mới trong `SyncTests`: gói mặc định kèm cột chưa tuyến của phiếu mình (không kèm cột không liên quan / của kỹ sư khác, `segment_id: null`, `note`, thứ tự `created_at`→id);
  phiếu chỉ có việc ở cột chưa tuyến vẫn ra gói (`segment_ids: []`); gói nêu tuyến không kèm; phiếu huỷ; phiếu `done` (liên kết vẫn giữ); liên kết đã giải phóng khi phiếu còn mở;
  `fault_report` cho cột chưa tuyến qua `/sync/push` + replay.
- Phá thử (đọc tên test đỏ, khôi phục bằng `cp`, xác nhận `cmp`):

| # | Đột biến | Test đỏ |
|---|---|---|
| M1 | Gói mặc định không bao giờ kèm cột chưa tuyến | `Without_segments_the_bundle_also_packs…`, `An_order_whose_only_work…`, `A_cancelled_order…` |
| M2 | Kéo MỌI cột chưa tuyến của xã | `Without_segments_the_bundle_also_packs…`, `A_cancelled_order…` |
| M3 | Gói nêu tuyến cũng kèm cột chưa tuyến | `Named_segments_never_pack_unassigned_poles…` |
| M4 | Bỏ nhánh phiếu giữ sự cố trên cột chưa tuyến | `Without_segments_the_bundle_also_packs…`, `An_order_whose_only_work…` |
| M6 | Bỏ sự cố mở của cột chưa tuyến | `Without_segments_the_bundle_also_packs…`, `An_order_whose_only_work…` |
| M7 | Bỏ lọc trạng thái phiếu (`OpenForEngineer`) | `A_finished_order_stops_packing…`, `Without_segments_the_bundle_packs_the_roads…` |
| M8 | Liên kết đã giải phóng vẫn tính | `A_released_fault_link_stops_packing…` |

## 3. Review của Codex (river) và xử lý

| Mục | Mức | Xử lý |
|---|---|---|
| Lõi D-4 (rò rỉ kỹ sư khác / phiếu đóng / xã khác), REPEATABLE READ, `segment_ids: []` | không có finding | — |
| Không test nào chứng minh bộ lọc trạng thái phiếu (huỷ cũng giải phóng liên kết nên che lỗi; phiếu `done` giữ liên kết) | P3, đúng | Thêm test `done` + phá thử M7 đỏ |
| Không test chứng minh `released_at IS NULL` khi phiếu còn mở | P3, đúng | Thêm test liên kết giải phóng + phá thử M8 đỏ |
| Test sắp cả hai vế bằng `IdOrder` nên không kiểm thứ tự thật | P3, đúng | Vế phải giữ thứ tự thực tế: `[IdOrder(pole1,pole2), held]` |
| `open_faults` đóng gói MỌI sự cố mở trên cột chưa tuyến, kể cả do phiếu người khác giữ | P3 | Giữ: đúng cách gói đang làm với cột trên tuyến (`active_work_order_id` cho biết ai); sửa câu chữ drift cho khớp |
| Drift: "đã có test" cho push, mục 2d đứng trước 2c, trap nói quá | P3 | Sửa: "đã THÊM test", đúng thứ tự 2c→2d, trap nêu ba điều kiện + test tương ứng |
| Test push ngoài phạm vi D-4 | P3 | Giữ (khoá hành vi D-4 nói về push: không đổi); ghi rõ trong drift |

## 4. D-item / điểm còn mở

- **D-20 — WP6 xác nhận quy tắc thay cache:** cột `segment_id: null` trong gói được thay cùng phần còn lại; không có metadata mới. Chờ WP6 trước khi coi D-4 xong với client.
