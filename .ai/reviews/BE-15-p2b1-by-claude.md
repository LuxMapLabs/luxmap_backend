# Review BE-15 P2b-1 — Claude, vòng 1 (03/10/2026)

Bối cảnh: lượt Codex trước bị dừng ở giới hạn 2 giờ, trước khi viết results. Build 0 warning; test không cần DB xanh theo
log. Migration đã đọc: không `DropIndex`/`DropColumn`/`AlterColumn` ngoài ý định, không cột `xmin` vật lý, index
`commune_id` có đủ. **Test tích hợp: 5/5 đỏ ở bước dựng fixture** (chi tiết T1) — tức chưa test tích hợp nào thật sự chạy.

Mức: **P1** = sai trên dữ liệu thật hoặc làm hỏng hạ tầng test; **P2** = nên sửa trong vòng này; **P3** = ghi nợ.

## Thuật toán

**A1 — P1. `FitClocks` từ chối khi `phone_elapsed_ns` không tăng chặt.**
BLE thường gửi theo gói: nhiều mẫu đến cùng lúc, cùng hoặc gần cùng thời điểm nhận, đôi khi đảo thứ tự. Đó chính là nhiễu
mà phép fit robust sinh ra để xử lý. Chỉ `module_ms` (theo `seq`) phải tăng chặt; `phone_elapsed_ns` là quan sát nhiễu,
không được là điều kiện từ chối. Hiện một gói BLE gộp làm hỏng cả phiên (`CLOCK_NON_MONOTONIC`).

**A2 — P1. Mất một gói BLE cắt cả lượt GPS.**
`AlignLux` tách khoảng ngay khi `seq` lệch 1, và `ProcessOneAsync` chạy `SplitPasses`/`Associate` **bên trong từng khoảng
lux** — nên một mẫu lux rơi là lượt đi bị chẻ, `pass_no` sai nghĩa, cột gần chỗ hở có thể mất. Sửa: tách khoảng lux chỉ khi
**hở thời gian** > `LuxGapSeconds`; **tách lượt chỉ theo GPS** (hở thời gian, quay đầu). Lux chỉ là nguồn đỉnh: cột rơi vào
chỗ lux hở không có đỉnh và mang cờ `lux_gap`, không bị cắt khỏi lượt.

**A3 — P1. GPS kém chính xác cắt lượt.** `SplitPasses` gặp `accuracy_m > MaximumAccuracyM` là `Flush()`. Dưới tán cây
accuracy vượt 15 m vài giây là chuyện bình thường — đúng chỗ đỉnh lux phải cứu. Sửa: **bỏ điểm GPS kém, không cắt lượt**;
chỉ cắt khi hở thời gian > `GpsGapSeconds`. Cột trong đoạn GPS kém mang cờ `gps_degraded`.

**A4 — P1. Không ước lượng độ trễ GPS trước khi mở cửa sổ ghép.** Cửa sổ ±1,5 s đặt quanh thời điểm GPS **chưa hiệu chỉnh**;
ở 25 km/h hai cột cách nhau ~3 s nên trễ 0,5–1 s là đỉnh rơi sang cửa sổ cột bên cạnh → mơ hồ → mất mốc. Sửa: hai bước —
(1) ước lượng **độ lệch thời gian chung của lượt** (ví dụ trung vị của hiệu `đỉnh gần nhất − dự đoán` với các đỉnh rõ ràng,
hoặc tìm độ lệch cực đại hoá số cặp khớp), (2) ghép lại với cửa sổ hẹp hơn quanh thời điểm đã hiệu chỉnh, rồi nội suy cục bộ
như hiện tại. Lưu độ lệch ước lượng vào `quality_flags` của lượt.

**A5 — P1. `WarningKmh = 15` gắn cờ cả dải tốc độ bình thường.** 15–25 km/h là dải dự kiến; đi chậm **không** hại dữ liệu
(đã chốt với Mỹ). Chỉ giữ `speed_excess` khi vượt `MaximumKmh`; bỏ `speed_warning` theo ngưỡng 15 (nếu muốn cảnh báo sớm
thì là ngưỡng gần trần, ví dụ 22, và đặt tên rõ). Tỉ lệ thời gian vượt tốc của lượt giữ nguyên.

**A6 — P2. Ngưỡng đỉnh lux tuyệt đối 10 lux bỏ sót đèn mờ.** Đèn `dim` — lớp quan trọng nhất — có thể chỉ cho đỉnh vài lux
ở độ cao nóc xe. Dùng ngưỡng **tương đối theo nhiễu nền** (ví dụ prominence ≥ max(`PeakMinimumProminenceLux` nhỏ, k × MAD
của nền cục bộ)), mặc định thấp, ghi rõ là chờ quay thử.

**A7 — P2. Cặp cột hai bên đường (< 3 m) luôn bị coi mơ hồ và mất mốc thời gian.** Hai cột cùng chainage thì **thời điểm
ngang cột là như nhau** — dùng chung đỉnh để định thời điểm cho cả hai (cờ `paired_poles`); chỉ **lux** là không quy được cho
từng cột (để `peak_lux` null hoặc cờ `peak_shared`).

**A8 — P3.** Hằng số độ tin cậy (.2/.4/.6/.9) chỉ là thứ bậc tạm — ghi trong XML doc và results, không coi là xác suất.

## Worker và xử lý

**W1 — P2. Lỗi bất ngờ bị nuốt, không log.** `catch (Exception)` trong `ProcessOneAsync` chỉ ghi `PROCESSING_ERROR`; phải
log exception (kèm sweep/attempt) trước khi ghi run lỗi.

**W2 — P2. Hết lease thì kết quả bị bỏ im lặng.** `Complete` trả về khi lease hết hạn mà không log. Thêm log cảnh báo. Thêm
**gia hạn lease (heartbeat)** giữa các bước — P2b-2 (ffmpeg, detector) sẽ chạy lâu hơn lease 120 s.

## Test và hạ tầng

**T1 — P1. Test tích hợp không chạy được.** `ExecuteSqlRawAsync(GenerateCreateScript())` coi dấu `{}` trong script là chỗ
tham số → `FormatException`. Hơn nữa schema riêng dựng từ model **bỏ qua migration** (trigger bất biến, hàm, sequence không
được kiểm). Làm theo quy ước repo: chạy trên DB đã migrate (`luxmap_test`), collection `AssetDatabaseCollection`, tự tạo xã/
tài khoản/dữ liệu riêng và dọn trong teardown (xem `SurveyIngestTests`).

**T2 — P1. Trigger bất biến không có công tắc dọn cho test.** `luxmap_reject_processing_mutation()` từ chối tuyệt đối, nên
teardown không xoá được run/pass/observation; vì FK `Restrict`, sweep/phiếu/cột/xã của test kẹt lại trong DB dùng chung. Dùng
**đúng khuôn audit**: cho qua khi `current_setting('luxmap.audit_purge', true) = 'on'` (cùng GUC, để
`AuditGuardTests.Production_source_never_enables_the_test_purge_switch` canh luôn), chỉ teardown `SET LOCAL`.

**T3 — P1. Bộ mô phỏng quá dễ.** Kết quả 100 % đúng ở mọi vận tốc cho thấy mô phỏng chưa chạm các ca khó. Bổ sung (tất định,
seed cố định), chạy lại và báo bảng mới theo từng ca:
- GPS **trễ có hệ thống** 0,8 s; đoạn **dưới tán cây** accuracy 20–30 m trong 5–8 s;
- BLE **gửi theo gói** (nhiều mẫu cùng `phone_elapsed_ns`) và **rơi gói** rải rác;
- **2–3 đèn tắt liên tiếp**; một **đèn mờ** đỉnh chỉ 3–5 lux trên nền ~1–2 lux;
- **cặp cột hai bên đường**; đỉnh giả từ đèn xe ngược chiều.
Bảng kết quả: tỉ lệ thời điểm ngang cột trong ±0,5 s / ±1 s so với ground truth, tỉ lệ ghép đỉnh đúng, số ca mơ hồ — theo
vận tốc 15/20/25/30 km/h **và** theo từng ca khó.

**T4 — P2.** Sau khi sửa T1, các test tích hợp trong task (chainage đúng mét + `EXPLAIN` đi GIST; claim đồng thời
`SKIP LOCKED`; lease hết hạn; phạm vi xã chặn ghi; run lỗi; đúng một audit mỗi xã) phải chạy được trên DB đã migrate. Claude
sẽ chạy.

## Còn thiếu

- `.ai/results/BE-15-p2b1.md` chưa có — viết sau khi sửa, gồm bảng kết quả mới (T3) và trả lời từng mục review này.

# Vòng 3 — từ review độc lập của Codex (phiên mới), Claude đã kiểm cả bốn (03/10/2026)

Vòng 2 (Claude) đã sửa alias SQL snake_case và siết lại `AuditGuardTests` — xem results §9, **đừng đảo lại**.

**R3-1 — P2. Hai tuyến trong cùng hành lang 25 m.** `SurveyChainageQuery.Gps` chiếu mỗi mẫu GPS lên mọi tuyến trong hành lang,
rồi mỗi tuyến được xử lý độc lập với cùng các đỉnh lux ⇒ chạy một tuyến mà tuyến song song cũng "được phủ". Sửa: mỗi mẫu GPS
chỉ thuộc **tuyến gần nhất** (khoảng cách 3405 trong SQL, ví dụ `DISTINCT ON (g.sample_no) … ORDER BY` khoảng cách); hai tuyến
gần như bằng nhau (chênh dưới một ngưỡng cấu hình) ⇒ mẫu đó gắn cờ mơ hồ, không dùng để định vị trên tuyến nào. Test tích hợp:
hai tuyến song song cách ~15 m, chỉ chạy một tuyến ⇒ tuyến kia không có lượt, không coverage.

**R3-2 — P2. Xe dừng làm sai thời điểm ngang cột.** Envelope đơn điệu bỏ mọi mẫu đứng yên sau mẫu đầu, nên nội suy trải chuyển
động lên cả quãng dừng (Codex tái hiện: 10 m từ t=1 tới t=10, 20 m ở t=11 ⇒ cột 15 m ra t=6 thay vì t=10,5). Sửa: giữ **thời
điểm rời đi** của quãng đứng yên (mẫu cuối cùng trước khi chainage tăng) làm cận dưới khi nội suy. Unit test đúng ca trên; thêm
một ca vào bộ mô phỏng: dừng hẳn 10 s dưới một đèn sáng (khớp lượt 11 của kịch bản quay thử).

**R3-3 — P2. GPS trôi ngoài hành lang bị SQL loại, tạo khoảng hở giả.** Trả **mọi** mẫu GPS trong khoảng thời gian của phiên kèm
khoảng cách tới tuyến; mẫu ngoài hành lang là **mẫu kém** (giữ liên tục thời gian, không dùng chainage), không phải khoảng hở.
Chỉ khi ngoài hành lang **liên tục quá một ngưỡng thời gian cấu hình** mới coi là xe rời tuyến và cắt lượt. Test tích hợp: vài
mẫu trôi 30–40 m ngang giữa đoạn đi thẳng ⇒ một lượt, cột trong đoạn đó có `gps_degraded`.

**R3-4 — P2. Hai lần chiếu không cùng snapshot.** Đọc chiếu GPS và chiếu cột (và hình học tuyến lưu vào `gis_snapshot`) trong
**một transaction `RepeatableRead`** (một snapshot Postgres), hoặc chiếu cả hai lên cùng hình học đã chụp. Ghi rõ lựa chọn.

Sau khi sửa: build 0 warning, test không DB xanh, cập nhật bảng mô phỏng nếu đổi, và thêm mục "Vòng 3" vào results trả lời
R3-1…R3-4. Không chạy test cần DB (Claude chạy). Không commit.
