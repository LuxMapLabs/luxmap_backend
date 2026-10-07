# Bỏ feeder / trụ điện tổng khỏi scope — tác động · 07/10/2026

Câu hỏi của Mỹ: không có dữ liệu thật từ EVN / xã nên **không đoán, không gán bừa** được trụ nào cấp cột nào — vậy bỏ
feeder (và trụ điện tổng) thì tác động thế nào?

Khảo sát chia đôi: **Codex** (gpt-6.1-sol, chỉ đọc, 151k token) — `luxmap-web` (`origin/dev` `03227aa`,
`FW-04` `d20b5c0`, `FW-05` `538bd50`), `mobile` (`origin/dev` `6148fe4`, `origin/main` `71c9b99`), Phiếu v1.4,
`tasks-backend.csv`. **Claude** — backend `origin/dev` `9f2c54d`, DB `luxmap_dev` / `luxmap_test`. Claude đã kiểm lại
bằng `git show` hai phát hiện FE nặng nhất. Nguyên văn Codex: `.ai/reviews/FEEDER-SCOPE-by-codex.md`.

> ✅ **Mỹ chốt 07/10/2026: phương án A.** Giữ lược đồ; cột thực địa để `feeder_id` null; topology chỉ trên mô hình testbed.
> Đã gửi WP5 danh sách việc FE (mục "Đề xuất", điểm 3). Dọn dữ liệu đoán (điểm 4) còn chờ xác minh trên DB của FE.

## Kết luận

**Vấn đề là DỮ LIỆU, không phải lược đồ.** Lược đồ hiện tại đã cho `pole.feeder_id` / `feeder.cabinet_id` là null, và null
nghĩa đúng là "chưa biết". Xoá bảng không làm dữ liệu đáng tin hơn — nó chỉ bỏ khả năng ghi lại khi có sơ đồ thật, và làm gãy
phần IoT / điều khiển mà Phiếu cam kết **không điều kiện**.

**Chỗ đang "gán bừa" thật sự nằm ở FE, không ở backend:**

| Ở đâu | Làm gì | Bằng chứng |
|---|---|---|
| `luxmap-web` `src/hooks/gis-map/useElectricalCascade.ts` (`origin/dev`) | **Chia đều cột của mỗi tuyến cho các tủ** của tuyến đó (`chunkSize = ceil(cột / số tủ)`), gán `cabinet_id` / `feeder_id`, và khi tủ "ngắt" thì **đặt `fixture_status = 'out'`** + `power_loss_reason` cho các cột đó | Claude kiểm bằng `git show` — dòng ~101–160 |
| `luxmap-web` `src/pages/assets/components/poles/AddPoleModal.tsx` (`origin/dev`) | **Bắt buộc chọn tủ** mới cho tạo cột: *"Vui lòng chọn Tủ điện điều khiển trực tiếp quản lý các cột đèn này!"* | `:96`, `:165` (Claude kiểm) |
| Server FE đang dùng (không truy cập được từ đây) | `FDR-005 "Tủ A - Huỳnh Văn Cọ"`, `pole_count: 13`, sửa bởi `USR-003` — nhiều khả năng hệ quả của form trên | Response FE dán vào phiên này; **chưa xác minh** trên DB đó |
| `luxmap-web` `FW-05` `AssetManagementPage` / `AddCabinetModal` | Tự chia cột cho tủ, **tự sinh `CAB-…` / `FDR-…` dự phòng** | Codex `:72–77` |

`luxmap_dev` (Claude, `psql`): **173 cột `field`, 0 cột có `feeder_id`**; 103 cột có feeder đều là mock `public_imagery`
trên 3 "Mạch tạm (demo, chờ O-6)"; `FDR-004…006` (Phước Thiện) 0 cột.

## Ba phương án

| | **A — giữ lược đồ, thực địa null, topology chỉ trên mô hình** ✅ đề xuất | B — bỏ feeder, giữ trụ + IoT | C — bỏ cả feeder lẫn trụ |
|---|---|---|---|
| Backend | **0 thay đổi** (tuỳ chọn: chặn cột `field` gắn feeder của trụ không-`field`, ~½ ngày) | Bỏ `feeder`, `feeder_control`, `pole.feeder_id`, BE-13 topology, import feeder, `feeder_ids` / `controller_node_ids` trên bản đồ (**BREAKING**), ràng buộc CAB-4; migration xoá bảng | B + bỏ `/assets/cabinets`, `/map/cabinets` (vừa làm ở #111), **trả `geom` lại cho `iot_node`** |
| Điều khiển ON/OFF/AUTO (Phiếu `:101`, `:264` — **không điều kiện**) | ✅ theo rơ-le → feeder | ⚠️ rơ-le phải trỏ thẳng tới cột / tuyến → **vẫn là topology**, chỉ đổi tên | ⚠️ như B, và thiết bị mất chỗ đứng |
| Demo đèn lẻ / chẵn trên testbed (I-13) | ✅ | ❌ trừ khi dựng lại "nhóm rơ-le" | ❌ |
| Highlight cột của một trụ (Phiếu `:175`, I-15) | ✅ trên mô hình; thực địa trống — đúng sự thật | ❌ | ❌ |
| Câu hỏi phụ RQ (`:273`, `:289`) | ✅ trả lời trên mô hình, thực địa báo tỉ lệ phủ | ❌ phải sửa Phiếu | ❌ |
| Sửa Phiếu | **Không** — `:175`, `:215`, `:217`, `:305`, `:343`, `:349` có "where available" / "missing feeder information should not prevent…" | Bắt buộc `:197, 209, 231, 239, 247, 273, 289, 313, 315` | B + `:197, 239`, đồng bộ `:215, 349` |
| FE | Bỏ chia đều + bỏ ép `out`; bỏ "bắt buộc chọn tủ" | Bỏ toàn bộ tab tủ/mạch, lớp `feeder-lines`, cascade, type `Feeder*` | B + marker / drawer / legend / tìm kiếm tủ |
| Mobile | Không đổi | Sửa DTO: `controller_node_id` là `String` **không nullable** (`RoadSegmentFeatureDto.kt:9…`) | Như B |

**Phương án B/C không gỡ được vấn đề gốc:** điều khiển và "trụ cấp những cột nào" vẫn cần một quan hệ thiết bị → cột. Bỏ feeder
chỉ dời quan hệ đó sang chỗ khác — vẫn phải có dữ liệu, vẫn không đoán được ngoài thực địa.

## Đề xuất (phương án A)

1. **Thực địa:** `feeder_id` / `cabinet_id` để **null**, trừ khi xã giao sơ đồ. Thống kê / báo cáo ghi đúng tỉ lệ phủ (hiện 0/173).
   Đúng chữ Phiếu `:175` / `:343`.
2. **Mô hình (testbed):** trụ + thiết bị + 2 feeder đèn lẻ / chẵn, `data_source = calibration_rig` (mạch thật) hoặc `simulated`
   — topology **thật vì nhóm tự đi dây**. Đây là chỗ demo điều khiển và trả lời RQ `:289`.
3. **FE — việc phải làm, không phải tuỳ chọn:**
   - Gỡ `useElectricalCascade` chia đều cột cho tủ; **không bao giờ** tự đặt `fixture_status = 'out'` theo tủ — đó là số liệu
     giả hiện lên như kết quả khảo sát, đúng loại lỗi `data_source` sinh ra để chặn.
   - Bỏ "bắt buộc chọn tủ" ở `AddPoleModal` (Phiếu `:175`: *missing feeder information should not prevent an asset from being
     managed*). Lấy tủ từ `cabinet` của feeder (CAB-6) thay vì tự suy.
   - Highlight / vẽ quan hệ trụ → cột **chỉ** từ dữ liệu backend (`feeder_ids` → `/assets/feeders/{id}/poles`), ghi chú giải
     "sơ đồ logic, không phải tuyến cáp".
4. **Dọn dữ liệu đoán** trên server FE đang dùng (ví dụ `FDR-005`, 13 cột): đặt lại `feeder_id = null` cho cột thực địa —
   **cần xác minh trên DB đó trước**, và cần Mỹ duyệt.
5. Tuỳ chọn backend: chặn ở service + import việc gắn cột `field` vào feeder có trụ không-`field` (và ngược lại).

## Chưa xác minh

- DB mà FE đang gọi (`FDR-005`, 13 cột) — không truy cập được từ phiên này.
- CV-15, IOT-* và ticket điều khiển đèn không có trong `tasks-backend.csv` (chỉ BE-00…BE-43); tuần / trạng thái của chúng chưa tra.
- Phân tích FE / mobile là tĩnh (grep + `git show`), không chạy app.
