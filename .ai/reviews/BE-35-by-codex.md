# BE-35 — review của Codex (22/10/2026) — DỞ DANG

Codex CLI 0.162.0 (bản đi kèm ChatGPT), `exec --sandbox read-only`, prompt khoanh vùng (job cảnh báo đĩa, job đối chiếu, trạng thái
job, migration, test) trên `git diff origin/dev...HEAD` của `feat/BE-35-system-status`. **Hết quota sau ~62k token, trước khi viết báo
cáo** ("try again at 3:07 PM"). Chỉ có một nhận xét giữa chừng trong log; mục 2–5 của prompt **chưa được review**.

## Phát hiện

| # | Mức | Phát hiện | Claude kiểm | Xử lý |
|---|---|---|---|---|
| C-1 | P1 (Claude xếp) | `StorageCapacityJob`: mỗi instance chụp `now` riêng. Lượt bắt đầu **trước** khi lượt khác mở đợt vẫn tới bước nhận thư / đóng đợt và ghi `notified_at` / `closed_at` = `now` của nó, sớm hơn `opened_at` ⇒ `23514 ck_storage_alert_order`, job lỗi. Test đồng thời cho mọi lượt cùng một `now` nên không bắt được | **Đúng.** Tái hiện bằng test mới (lượt sau mở đợt rồi gửi lỗi, lượt trước nhận thư và đóng đợt) → đỏ đúng `ck_storage_alert_order` | `GREATEST($1, opened_at)` ở cả hai `UPDATE`; test `A_run_that_started_before_the_spell_opened_can_still_mail_and_close_it` xanh |

## Tự review phần Codex chưa xem (Claude, 22/10/2026 — Mỹ chọn thay vì chờ quota)

Đọc lại `StorageReconciliationJob` / `StorageUsage`, `SystemStatusService`, `Persistence/Jobs` + migration, `SystemMonitoringTests`; kiểm thêm:
mọi lệnh ghi object (`StoreImageAsync` / `StoreStreamAsync`) đều thuộc một bảng đã khai `StoredObjectColumn` (6 chỗ ghi, 4 bucket); bucket
production tạo đủ 4 (`deploy/compose.yml`, `minio-mc`); không class test nào ngoài `AssetDatabaseCollection` ghi sweep (test đếm theo hiệu).

| # | Mức | Phát hiện | Xử lý |
|---|---|---|---|
| S-1 | P2 | `StorageCapacityJob`: một Quản trị gửi lỗi (địa chỉ bị SMTP từ chối) làm trả lại cả lượt nhận thư ⇒ lượt kế (`RetrySeconds` = 5 phút) gửi lại cho **mọi** Quản trị — địa chỉ hỏng vĩnh viễn thành thư lặp mỗi 5 phút | Chỉ trả lại khi không gửi được cho ai; gửi được cho một số thì giữ, job `failing` nêu username bị lỡ. Test `One_unreachable_admin_…` đỏ trước, xanh sau. Drift SM-7 sửa theo |
| S-2 | P3 | Không có Quản trị nào đang hoạt động ⇒ đợt bị đánh dấu đã báo mà không ai nhận | Giữ (không có ai để báo; tạo Quản trị sau đó không cần thư của đợt cũ — màn trạng thái vẫn hiện `low`); ghi log cảnh báo, ghi vào SM-7 |

Không thấy lỗi ở: thứ tự đọc dòng trước / liệt kê sau, upsert một dòng mỗi bucket, ghép SQL từ identifier đã kiểm, phân loại trạng thái
job so với `ScheduledJobRunner`, filter xã (Quản trị scope `*`; tổng byte cố ý không lọc), migration chỉ thêm và `Down()` đối xứng.

## Còn lại

Mỹ chọn tự review thay vì chờ quota (22/10). Lượt Codex cho phần trên vẫn chạy được sau trên PR nếu muốn.
