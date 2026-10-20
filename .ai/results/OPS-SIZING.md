# OPS-SIZING — kết quả đo thực tế

Ngày 17/10/2026, Docker Desktop trên macOS, worktree `docs/OPS-SIZING`, commit `1a9ccff48e9c06f8dcce9ce9240e96c57e160119`.

**Đề xuất tạm: 4 vCPU / 4 GiB RAM** cho một worker như cấu hình đã đo. Trong ba mức 2 / 4 / 8 GiB, chọn **4 GiB**; chưa có bằng chứng cần 8 GiB. Đĩa phụ thuộc lịch khảo sát: với **16 đêm × 30 phút**, dự trù **≥160 GB nếu backup ngoài VPS**, hoặc **250–300 GB nếu giữ thêm một backup đầy đủ trên VPS**.

**Giới hạn quan trọng:** clip đúng cỡ yêu cầu 100–260 MB chưa đi qua được toàn pipeline: `VIDEO_OUTPUT_LIMIT` ở `ffprobe`, trước trích frame/inference. Số ffmpeg + YOLO dưới đây đo bổ sung bằng clip thật **92.237.921 byte**, nhỏ hơn dải yêu cầu. Vì vậy đây chưa phải chứng nhận đủ RAM cho pipeline hoàn chỉnh của clip 100–260 MB; cần xử lý blocker và đo lại trước khi chốt cấu hình cuối.

## Môi trường và cách đo

- Build bằng chính `Dockerfile`, chạy `deploy/compose.yml`, project **`luxmap-prod`**. Image `sha256:f2ca8bac247491a7be05aa6c36c55cca85c8d5a6cf8af926f7ead4745ecd9811`, Linux **arm64**, CLI báo 1,37 GB. Build/publish và EF bundle thành công; áp dụng **47 migration**.
- Docker: **10 CPU**, **8.321.515.520 byte ≈ 7,75 GiB**, engine 29.6.1. Không đổi tài nguyên Docker Desktop. Số thời gian trên ARM64 này không phải cam kết tốc độ VPS amd64.
- Tạo `deploy/.env` thử nghiệm, quyền `0600`, mật khẩu ngẫu nhiên; domain `localhost`; SMTP `smtp.ops-sizing.invalid`. Request qua **Caddy HTTPS**, xác minh bằng CA local của Caddy, không tắt SSL verification. DB / MinIO dùng cổng riêng **15432 / 19000 / 19001**.
- Seed tài khoản bằng `api --seed`; lấy SQL stdout của `scripts/seed_mock_set.py`, chạy `psql -v ON_ERROR_STOP=1` vào **DB `luxmap` của container production**. Không dùng `--apply` và không đọc/chép DB dev.
- Bật hai cấu hình đã có: `SurveyProcessing__Enabled=true`, `SurveyProcessing__Frames__Detector=yolo`. Các giá trị còn lại giữ mặc định: một worker, một clip đồng thời, 5 frame/s trong cửa sổ quanh cột, AI concurrency 2; timeout subprocess 60 s.
- Clone tuyến mock SEG-003 và 26 cột thành tuyến riêng `simulated`; tạo phiếu survey qua API, manager `engineer` giao cho `crew`, `crew` start. GPS/lux/capture config là **tổng hợp**, không dùng đánh giá độ chính xác CV. Mapping PTS lấy từ clip thật; manifest SHA-256 được endpoint xác nhận. Lượt clip ngắn đi qua 7/26 cột (coverage 26,923%).
- `docker stats` stream thực tế **~0,5 s/mẫu**, dày hơn yêu cầu 1 s; RSS subprocess, cgroup và đĩa frame tạm **1 s/mẫu**. Bốn container hoạt động được ghi đủ: API, Postgres, MinIO, Caddy. `migrate` / seed là one-shot; `minio-mc` đã thoát khi đo tải.
- Tổng RAM/CPU là tổng **bốn dòng cùng chu kỳ stats**, không cộng các đỉnh riêng khác thời điểm. RAM `docker stats` là số working set đã loại phần cache theo Docker, có làm tròn; cgroup `memory.current` / `memory.peak` và RSS giữ riêng. Không cộng RSS ffmpeg vào RAM API lần nữa.
- A: chờ API khởi động **120 s**, đo nhàn thêm 30 s. B: barrier đồng loạt **10 pole-photos + 5 ai/detect**, 10 JPEG thật **3024×4032 = 12.192.768 pixel**. Các lượt giữ thêm 10 s sau hoàn tất để bắt RAM còn giữ lại. Mỗi mức RAM khởi tạo lại API; GC/cache khác nhau giữa các lượt.

## RAM và CPU không áp giới hạn

Đơn vị RAM: **MiB**, mỗi ô container là đỉnh riêng; cột cuối là đỉnh tổng cùng thời điểm.

| Kịch bản | API | Postgres | MinIO | Caddy | Tổng cùng mẫu |
|---|---:|---:|---:|---:|---:|
| A — nhàn | 168.0 | 43.9 | 100.0 | 15.1 | **326.4** |
| B — 15 request ảnh | 853.9 | 76.0 | 121.1 | 18.7 | **1,064.1** |
| C — clip 261 MB, thất bại | 949.6 | 86.4 | 208.3 | 22.3 | **1,257.7** |
| C bổ sung — clip 92 MB | 671.3 | 98.3 | 259.8 | 24.5 | **1,042.3** |
| D bổ sung — clip 92 MB + B | 1,112.1 | 101.8 | 327.4 | 22.9 | **1,506.7** |

CPU **100% = một CPU**. Đỉnh từng container và đỉnh tổng có thể xuất hiện ở các thời điểm khác nhau.

| Kịch bản | API % | Postgres % | MinIO % | Caddy % | Tổng cùng mẫu % |
|---|---:|---:|---:|---:|---:|
| A — nhàn | 11.47 | 3.09 | 3.01 | 0.89 | 12.01 |
| B — 15 request ảnh | 983.80 | 9.59 | 12.53 | 17.77 | 983.85 |
| C — clip 261 MB, thất bại | 169.48 | 3.43 | 47.22 | 31.70 | 245.97 |
| C bổ sung — clip 92 MB | 536.63 | 4.19 | 24.52 | 14.88 | 539.83 |
| D bổ sung — clip 92 MB + B | 802.78 | 4.13 | 27.06 | 24.30 | 815.65 |

B trả **10×201 + 5×200**, hết **4,073 s**. D bổ sung cũng đủ 15 request thành công, **2,345 s**. Đỉnh tổng RAM lớn nhất của các lượt không giới hạn là **1.506,7 MiB ≈ 1,47 GiB**.

## Video: bước nào thực sự chạy

| Đầu vào | Dung lượng byte | Độ dài | Kết quả |
|---|---:|---:|---|
| IMG_0458.MOV | 261.438.540 | 147,86 s | submit 202; failed / extractor / VIDEO_OUTPUT_LIMIT; 0 frame |
| IMG_0466.MOV | 111.604.527 | 85,08 s | submit 202; cùng lỗi; 0 frame |
| IMG_0467.MOV | 176.413.504 | 82,59 s | probe kiểm tra riêng: JSON 18.365.842 byte > 16.777.216 byte; không dùng làm lượt C thành công |
| IMG_0465.MOV — bổ sung | 92.237.921 | 44,24 s | awaiting_review; **108 frame, 69 detection, 7 observation** |

Các clip đều HEVC 1920×1080. Lượt C đầu tiên mất **29.52 s** đến lỗi; con số này **không phải thời gian xử lý thành công clip lớn**. Lượt C bổ sung mất **45.85 s**, D bổ sung **46.59 s**, tính từ `run.started_at` đến `finished_at`, không gồm upload/chờ poll. Thời gian từ submit và thời gian upload riêng nằm trong `requests.jsonl` / log từng lượt.

Bằng chứng YOLO thật: `worker-runs.csv` ghi `detector=yolo`, model `yolo-d8bd77cde3e14d64`, extractor version có hash, 108 frame và 69 detection tồn tại trong DB. `overlap_start` trong `requests.jsonl` lưu output `docker top` có **ffmpeg** trước khi bắn 15 request D. Không suy ra YOLO đã chạy chỉ từ việc đăng ký artifact model.

RSS riêng của C bổ sung: ffmpeg **129.24 MiB**, ffprobe **104.27 MiB**; đã nằm trong API. Frame temp đỉnh **90.67 MiB**. Clip lớn bị chặn có temp đỉnh **249.34 MiB**, ffprobe RSS **104.71 MiB**, **không có ffmpeg extraction**.

Hai lượt thử phụ không dùng làm kết quả D hợp lệ: `D` ban đầu dừng script vì lệnh `docker top -eo comm` lỗi; chưa bắn B. `C-valid` là tên log lần thử IMG_0466 thất bại, không có nghĩa pipeline hợp lệ; danh sách PTS của lần chuẩn bị này còn đang được ghi, nhưng worker đã dừng ở output limit trước bước kiểm mapping. Script hiện có guard đủ số PTS và lệnh top có PID; các lượt C/D bổ sung và giới hạn dùng PTS hoàn chỉnh.

## Giới hạn RAM và OOM

Override chỉ nằm trong thư mục kết quả. Postgres **384 MiB**, MinIO **384 MiB**, Caddy **128 MiB**; API lần lượt **1024 / 1536 MiB**. `memswap_limit = mem_limit`, không dựa vào swap. Đây là giới hạn container trong Docker VM 7,75 GiB, không phải một VPS vật lý 2 GiB đã được kiểm chứng.

| Lượt | API đỉnh mẫu MiB | Tổng đỉnh mẫu MiB | Kết quả |
|---|---:|---:|---|
| C lớn / API 1 GiB | 308.0 | 533.9 | VIDEO_OUTPUT_LIMIT; không OOM |
| C bổ sung / API 1 GiB | 505.3 | 761.3 | 108 frame; không OOM |
| D bổ sung / API 1 GiB | 547.9 | 816.4 | **OOM; 15/15 ảnh 502**; sweep tự retry |
| C lớn / API 1,5 GiB | 312.3 | 617.5 | VIDEO_OUTPUT_LIMIT; không OOM |
| C bổ sung / API 1,5 GiB | 558.9 | 864.6 | 108 frame; không OOM |
| D bổ sung / API 1,5 GiB | 992.9 | 1,327.5 | 108 frame + 15/15 ảnh thành công; không OOM |

**API 1 GiB không chịu được D.** Lúc 14:36:09 UTC (21:36:09 giờ Việt Nam), Docker ghi:

```text
2026-10-17T14:36:09.222419+00:00,oom,luxmap-prod-api-1,
2026-10-17T14:36:09.376802+00:00,die,luxmap-prod-api-1,137
OOMKilled=false RestartCount=1 Status=running StartedAt=2026-10-17T14:36:09.391800383Z
```

Cả 15 request trả 502. Đỉnh ngắn gây OOM không được stats 0,5 s bắt đầy đủ: số 547,9 MiB ở ô API của D/1 GiB là **đỉnh quan sát được, không phải RAM tối đa thực tế**. Cgroup/counter bị reset khi container restart; `OOMKilled=false` sau restart không phủ định sự kiện `oom` đã lưu. Sweep tự thu hồi lease, hoàn tất ở **attempt 2**, nhưng D/1 GiB vẫn **không đạt** vì mất request và restart. OOM cũng để lại thư mục frame tạm của attempt cũ; phần này mất khi recreate container, không đưa vào suất lưu trữ bình thường.

Ở **1,5 GiB**, C bổ sung và D bổ sung đều tới awaiting_review ở attempt 1; D trả **10×201 + 5×200** trong **2,610 s**, inspect **OOMKilled=false, RestartCount=0**, không có event oom của lượt này. Clip 261 MB vẫn failed do output limit ở cả hai mức RAM; thêm RAM không giải quyết giới hạn phần mềm đó.

## Số CPU và thời gian

Cùng clip 44,24 s / 108 frame / 69 detection, API 1,5 GiB ở hai lượt giới hạn CPU. Ghim **cả bốn service** cùng nhóm CPU bằng cpuset; API thêm quota 2 hoặc 4 CPU. Mỗi cấu hình chạy một lượt, không phải phân phối thống kê nhiều lần.

| CPU | Thời gian worker | Frame | Kết quả |
|---|---:|---:|---|
| 10 CPU khả dụng, không ghim | 45.85 s | 108 | awaiting_review |
| 2 CPU chung: 0–1 | 60.84 s | 108 | awaiting_review |
| 4 CPU chung: 0–3 | 54.67 s | 108 | awaiting_review |

**2 vCPU là mức thấp có thể xử lý clip bổ sung đã đo; 4 vCPU là mức khuyến nghị**, có dư địa cho các burst ảnh vốn chạm gần 10 CPU ở lượt không giới hạn. Chưa đo B/D với quota 2/4 CPU; chưa đo thời gian hoàn tất clip 100–260 MB hay trên VPS amd64.

## Đĩa và dự trù bốn tháng

Snapshot trước/sau C bổ sung đầu tiên, khi chưa có orphan do OOM:

```text
                          trước C bổ sung    sau C bổ sung       đơn vị KiB (du -sk)
postgres volume                  78000             78436
minio volume                    642432            753256
/tmp/luxmap-frames                   4                 4
survey bucket                       4             20144
```

Video giữ lại **92.237.921 byte**; GPS/lux/config **67.215 byte**; 108 frame gốc **17.448.824 byte** + thumbnail **1.070.321 byte**. Tổng logical **150.292.856 byte/phút**. Tăng cấp phát MinIO thực đo **113.483.776 byte / 44,243333 s = 153.899.494 byte/phút**, gồm overhead object/filesystem. Riêng video **125.087.214 byte/phút**, frame + thumbnail **25.114.489 byte/phút**. Nguồn phép tính: `disk-rates.json`; snapshot volume sau từng lượt: `*-disk.json`.

Số này phụ thuộc bitrate và mật độ cột/cửa sổ frame. Dự trù bên dưới dùng **ước tính 180 MB/phút cho video + raw + frame + overhead**, cao hơn số đo; giữ toàn bộ bốn tháng, không xoá sau review. Cộng **15 GB OS/Docker/image/build**, **5 GB DB/WAL/log**, **1 GB ảnh riêng** — ba khoản là dự trù, không phải số đo VPS. Thêm 20% đĩa trống.

| Giả định 4 tháng | Phút video | Dữ liệu khảo sát dự trù | Đĩa nếu backup ngoài | Đĩa nếu giữ thêm 1 backup đầy đủ |
|---|---:|---:|---:|---:|
| 16 đêm × 15 phút | 240 | 43,2 GB | ≥100 GB | ≥160 GB |
| **16 đêm × 30 phút** | **480** | **86,4 GB** | **≥160 GB** | **250 GB tối thiểu tính toán; 300 GB có dư địa** |

Ví dụ dòng 30 phút: live ~107,4 GB; nếu thêm một backup ~91,4 GB thì cần ~198,8 / 0,8 = **248,5 GB** trước phần dự phòng thêm. `100 GB` không đủ dư địa cho giả định này. Temp ở bước probe của clip lớn quan sát ~249 MiB; dự trù thêm ~1 GB tạm. Không chia tổng volume cuối cho tổng phút: bộ đo còn chứa clip thất bại, lượt lặp và orphan của thử OOM.

## Kết luận về RAM và phần còn thiếu

Tổng đỉnh quan sát **1,47 GiB** + **ước tính 0,75–1 GiB OS/Docker/cache** = **2,22–2,47 GiB**, chưa cộng dư địa. **Không chọn 2 GiB.** Với API limit 1,5 GiB và các service như trên, tổng trần container là **2,375 GiB**, cộng dự trù OS thành **3,125–3,375 GiB**: **4 GiB là lựa chọn tối thiểu đề xuất và khuyến nghị cho tải đã đo**. Đây là suy luận từ phép đo, không phải số đo OS của một VPS 4 GiB. 8 GiB chưa được chứng minh là bắt buộc.

✅ Đã xác minh build, migrate, seed, ảnh 12 MP đồng thời, ffmpeg + YOLO clip bổ sung, OOM 1 GiB, D thành công 1,5 GiB, CPU 2/4 và suất đĩa.

⚠️ Chưa xác minh toàn pipeline clip 100–260 MB: bị chặn `VIDEO_OUTPUT_LIMIT`. Cần review `MediaProcess.ReadBounded` / lệnh ffprobe trong `FfmpegFrameExtractor.cs` rồi đo lại clip lớn. Theo mục cấm của ticket, **không sửa src/tests/config production để vượt blocker**, không ghi drift/dashboard; bàn giao phát hiện này cho Claude review.

📌 Giả định còn lại: một worker, video 1080p HEVC, GPS/lux/config tổng hợp, corpus mock nhỏ; OS và dung lượng bốn tháng là dự trù như công thức trên. Không suy ra độ chính xác CV hay throughput nhiều sweep từ phép đo này.

## Bằng chứng, tái lập và dọn dẹp

Mọi compose đều dùng `-p luxmap-prod -f deploy/compose.yml` (thêm override khi cần). Chuỗi bootstrap đã chạy:

```sh
docker compose -p luxmap-prod -f deploy/compose.yml build api
docker compose -p luxmap-prod -f deploy/compose.yml up -d postgres minio minio-mc
docker compose -p luxmap-prod -f deploy/compose.yml --profile tools run --rm migrate
docker compose -p luxmap-prod -f deploy/compose.yml run --rm api --seed
python3 scripts/seed_mock_set.py > .ai/results/OPS-SIZING-stats/seed-mock.sql
docker compose -p luxmap-prod -f deploy/compose.yml exec -T postgres psql -U luxmap -d luxmap -v ON_ERROR_STOP=1 < .ai/results/OPS-SIZING-stats/seed-mock.sql
```

Trong `OPS-SIZING-stats/`: `create-test-env.py` chỉ tạo .env mới khi chưa có; `seed-scenario.sql` clone dữ liệu; `scenario-data*.sql` xuất GPS; `workload.py` tạo/nộp sweep; `measure.py` lấy mẫu; `run-matrix.py` chạy giới hạn; `summarize.py` tổng hợp. Khi tái lập phải lấy lại CA local của Caddy mới, tạo lại seed/GIS và PTS hoàn chỉnh trước khi chạy workload. Các biến `OPS_SIZING_*` chỉ thuộc script đo. File snapshot/CSV/log giữ nguyên số đo; không lưu token/mật khẩu.

- `scenario-summary.csv`, `cpu-summary.csv`: đỉnh RAM/CPU từng container và tổng cùng mẫu cho mọi lượt.
- `baseline`, `1g`, `1536m`, `cpu2`, `cpu4` × `-stats.csv` / `-process.csv`: mẫu thô.
- `worker-runs.csv`, `requests.jsonl`, `*-result.json`, `*-inspect.json`, `container-events.csv`: HTTP, trạng thái và OOM.
- `probe-0467-summary.json`, `probe-0467-full.json.gz`: output ffprobe thật và tổng byte.
- `build.log`, `migrate.log`, `seed*.log`, `*-api.log`: bằng chứng build/runtime; `disk-rates.json`, `*-disk.json`: đĩa.

Đã liệt kê và kiểm tra tất cả volume sắp xoá mang prefix `luxmap-prod`:

```text
luxmap-prod_api_logs
luxmap-prod_caddy_config
luxmap-prod_caddy_data
luxmap-prod_minio_data
luxmap-prod_postgres_data
```

Sau đó chạy **`docker compose -p luxmap-prod -f deploy/compose.yml down -v`**. Kiểm lại: **0 container**, **0 volume**, **0 network** của project đo còn lại. `deploy/.env` thử nghiệm đã xoá; không prune/xoá image.

Hai container dev giữ nguyên ID và StartedAt, vẫn healthy; ba volume dev giữ nguyên tên. **14 file đầu vào đã dùng có SHA-256 và mtime khớp trước/sau.** `src/`, `tests/`, Dockerfile, compose/Caddyfile, CLAUDE, drift, dashboard và runbook không có thay đổi tracked. Không commit, không push, đúng yêu cầu ticket. Bằng chứng: `dev-before.json`, `dev-after.json`, `source-files-before.json`, `cleanup-verification.json`, `cleanup.log`.
