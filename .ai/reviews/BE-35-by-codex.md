# BE-35 — review của Codex (22/10/2026) — DỞ DANG

Codex CLI 0.162.0 (bản đi kèm ChatGPT), `exec --sandbox read-only`, prompt khoanh vùng (job cảnh báo đĩa, job đối chiếu, trạng thái
job, migration, test) trên `git diff origin/dev...HEAD` của `feat/BE-35-system-status`. **Hết quota sau ~62k token, trước khi viết báo
cáo** ("try again at 3:07 PM"). Chỉ có một nhận xét giữa chừng trong log; mục 2–5 của prompt **chưa được review**.

## Phát hiện

| # | Mức | Phát hiện | Claude kiểm | Xử lý |
|---|---|---|---|---|
| C-1 | P1 (Claude xếp) | `StorageCapacityJob`: mỗi instance chụp `now` riêng. Lượt bắt đầu **trước** khi lượt khác mở đợt vẫn tới bước nhận thư / đóng đợt và ghi `notified_at` / `closed_at` = `now` của nó, sớm hơn `opened_at` ⇒ `23514 ck_storage_alert_order`, job lỗi. Test đồng thời cho mọi lượt cùng một `now` nên không bắt được | **Đúng.** Tái hiện bằng test mới (lượt sau mở đợt rồi gửi lỗi, lượt trước nhận thư và đóng đợt) → đỏ đúng `ck_storage_alert_order` | `GREATEST($1, opened_at)` ở cả hai `UPDATE`; test `A_run_that_started_before_the_spell_opened_can_still_mail_and_close_it` xanh |

## Còn lại

Chạy lại lượt review sau khi quota có lại, chỉ phần chưa xem: `StorageReconciliationJob` / `StorageUsage`, `SystemStatusService`,
`Persistence/Jobs` + migration, `SystemMonitoringTests`.
