# BE-15-ffprobe-show-frames — kết quả thực thi

Ngày: 17/10/2026 · Agent: Codex · Branch: `fix/BE-15-ffprobe-show-frames`.

**Pha 2b hoàn thành — D-1 đã đóng:** probe `packet=pts` + sort, giữ nguyên giới hạn.
Build exit 0; toàn suite **1.550 pass, 2 MQTT skip**, exit 0. Production C/D đều
`awaiting_review`, mỗi lượt **270 frame**; D có đủ **15 request ảnh thành công** chồng lúc ffmpeg chạy.
Đã dọn `luxmap-prod`, dev và video nguồn nguyên trạng. Chưa stage/commit/push.

**Mục 1–6 bên dưới là bằng chứng lịch sử của pha trước** (khi còn `frame=pts` và timeout).
Kết quả hiện tại, output/mã thoát và nghiệm thu production nằm ở **mục 7 — Pha 2b**.

## 1. Thay đổi và phạm vi

- `FfmpegFrameExtractor.cs`: bỏ `-show_frames`, đổi writer sang `json=compact=1`. Không đổi parser,
  chọn frame, PTS, clock mapping hay phân loại. Giữ stdout 16 MiB, timeout 60 s,
  `TemporaryBytesPerClip=419430400`, `MaximumFrameBytes=16777216`, concurrency 1.
- `SurveyFrameMediaTests.cs`: thêm clip tự sinh 30.000 frame 64×64; chứng minh lệnh cũ vượt trần qua
  **MediaProcess thật**, sau đó gọi **extractor thật** và kiểm JPEG/PTS của frame cuối cùng, kích thước,
  phone elapsed và dọn temp. Sửa fake runner nhận probe qua `-show_entries` thay vì cờ vừa bỏ.
- `CLAUDE.md`: ghi bẫy ở mục BE-15; `docs/development.md`: cập nhật lệnh probe.
- `tracking.html`: thẻ đầu ghi rõ code/test đã qua, nghiệm thu production còn D-1.
- Không đổi package, migration, Contract, OpenAPI, endpoint/DTO. Không commit, push hay stage.

Lệnh mới trong extractor:

```sh
ffprobe -v error -select_streams v:0 \
  -show_entries stream=width,height,time_base:stream_side_data=rotation:frame=pts \
  -of json=compact=1 <input>
```

Parser đọc `frames[].pts`, `streams[0].width/height/time_base` và
**`streams[0].side_data_list[].rotation`**. Rotation không lấy từ frame.
`frame=pts` tự bật xuất frame; `-show_frames` phía sau bật lại mọi trường frame.
Các scalar không dùng đã được lọc; ffprobe vẫn tự xuất các wrapper rỗng của frame side data
(HDR/Dolby Vision: `side_data_list`, `components`, `pieces`). Đã đo thêm `frame_side_data=` và
`frame_side_data_list=`: cùng 3.948.609 byte, không giảm thêm. Vì vậy không thêm các selector vô hiệu.
Giữ stream rotation; test VFR/rotation sẵn có đều qua, gồm ảnh xoay và kích thước thực tế.

### Version extractor

Đã đọc `SurveyFrameOptions`, `DescribeAsync` và `SurveyFramePipeline.RegisterArtifact`:
`DescribeAsync` băm binary ffmpeg, ffprobe, **assembly chứa extractor**, cùng metadata đường dẫn/version.
Sửa lệnh làm assembly đổi, do đó digest/version tự đổi theo luật hiện có; không có số version thủ công
cần tăng và không cần sửa options. Pipeline đăng ký component `frame_extractor`, lưu FK vào run/frame.
DB production thực tế đã ghi version mới:

```
6daa6564987236d8361fa19a648798969004b17c89bb2ee5df1124dda5e2cef6
```

## 2. Đo stdout và thời lượng vừa trần

Lệnh sinh clip (chạy thật, exit 0; cùng khuôn trong test):

```sh
ffmpeg -v error -f lavfi -i color=size=64x64:rate=30 -frames:v 30000 \
  -c:v libx264 -preset ultrafast -bf 0 -video_track_timescale 30000 -y generated.mp4
```

File sinh chỉ **423.965 byte**; đã xoá sau đo, không giữ/commit media.
Lượng JSON lớn vẫn xảy ra ở 64×64, nên không cần video 1080p hay fixture lớn để bắt lỗi.
30.000 frame tương đương 1.000 s ở 30 fps; sinh và probe chạy nhanh hơn thời gian thực.

| ffprobe 8.1.2 macOS, cùng 30.000 frame | stdout byte | exit |
|---|---:|---:|
| Lệnh cũ, `-show_frames -of json` | 27.816.309 | 0 |
| Bỏ `-show_frames`, writer JSON thường | 1.459.183 | 0 |
| Lệnh mới, JSON compact | 859.123 | 0 |

Đo cũ trung bình khoảng 927 byte/frame: 30.000 frame tạo khoảng 27,8 MB, lớn hơn
16.777.216 byte với biên rõ ràng. Trên ffprobe **6.1.1-3ubuntu5** của chính image production,
clip 64×64/30.000 frame cho lệnh cũ **31.885.168 byte**, exit 0
(`linux-old-probe.log`); hồi quy không dựa vào riêng ffprobe macOS.
CLI xuất hết stdout có exit 0; khi qua **bounded runner**, cùng lệnh cũ phải ném `VIDEO_OUTPUT_LIMIT`.

Clip thật được đọc nguyên bản:
`/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/IMG_0458.MOV`, **261.438.540 byte**, 4.433 frame,
stream 1920×1080, `time_base=1/600`, rotation **−90°**.
Output lệnh mới đo trực tiếp:

```json
{
  "command": [
    "ffprobe",
    "-v",
    "error",
    "-select_streams",
    "v:0",
    "-show_entries",
    "stream=width,height,time_base:stream_side_data=rotation:frame=pts",
    "-of",
    "json=compact=1",
    "/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/IMG_0458.MOV"
  ],
  "exit_code": 0,
  "seconds": 64.809,
  "bytes": 3948609,
  "frames": 4433,
  "stream": [
    {
      "width": 1920,
      "height": 1080,
      "time_base": "1/600",
      "side_data_list": [
        {},
        {
          "rotation": -90
        },
        {}
      ]
    }
  ],
  "frame_max_with_int64_pts_and_separator": 932,
  "reserve_stream_bytes": 4096,
  "capacity_frames_same_metadata_bound": 17996,
  "capacity_minutes_30fps_bound": 9.997777777777777,
  "capacity_minutes_60fps_bound": 4.998888888888889,
  "average_based_minutes_30fps": 10.46407854963046,
  "average_based_minutes_60fps": 5.23203927481523
}
```

**Giới hạn stdout có điều kiện, không phải giới hạn thời lượng chung:**

- Với mật độ JSON trung bình đo trên clip này: khoảng **10,464 phút ở 30 fps / 5,232 phút ở 60 fps**.
- Chặn bảo thủ cho **cùng các dạng metadata đã thấy trong clip**, dự phòng PTS không âm tới 19 chữ số,
  separator/indentation và 4.096 byte overhead stream: tối đa 932 byte/frame;
  `floor((16777216-4096)/932) = 17996` frame, tương đương
  **9,9978 phút ở 30 fps / 4,9989 phút ở 60 fps**.
- Clip khác có metadata khác thì dung lượng/frame khác; không có một số phút tối đa đúng cho mọi clip.
  `ReadBounded` đếm ký tự; JSON được đo ở đây là ASCII nên số byte và số ký tự bằng nhau.
- Các con số trên **chỉ xét stdout**. Lệnh CLI macOS mất **64,809 s**, không phải bằng chứng extractor
  với timeout 60 s xử lý được clip này. Không thay timeout của ứng dụng; xem D-1.

Bằng chứng chi tiết: `generated-probe-measurements.json`, `real-probe-measurement.json` trong thư mục
[BE-15-ffprobe-show-frames-evidence](BE-15-ffprobe-show-frames-evidence/).

## 3. Test hồi quy, phá thử, build và toàn suite

### Đỏ trước sửa

```sh
dotnet test tests/LuxMap.Infrastructure.Storage.Tests --filter FullyQualifiedName~Large_frame_probe_stays_bounded_and_extracts_the_last_frame
```

Output thật (trích `regression-red.log`):

```text
[xUnit.net 00:00:01.29]     LuxMap.Infrastructure.Storage.Tests.SurveyFrameMediaTests.Large_frame_probe_stays_bounded_and_extracts_the_last_frame [FAIL]
  Error Message:
   LuxMap.Modules.Survey.Processing.ProcessingFailure : VIDEO_OUTPUT_LIMIT
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
DOTNET_TEST_EXIT=1
```

### Xanh và phá thử sau sửa

```sh
dotnet test tests/LuxMap.Infrastructure.Storage.Tests --filter FullyQualifiedName~SurveyFrameMediaTests
```

```text
Passed!  - Failed:     0, Passed:    17, Skipped:     0, Total:    17, Duration: 3 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
DOTNET_TEST_EXIT=0
```

Sau đó **chỉ thêm lại `-show_frames` vào lệnh extractor mới**, giữ compact và mọi thứ khác,
rồi chạy lại đúng test hồi quy:

```text
[xUnit.net 00:00:01.31]     LuxMap.Infrastructure.Storage.Tests.SurveyFrameMediaTests.Large_frame_probe_stays_bounded_and_extracts_the_last_frame [FAIL]
   LuxMap.Modules.Survey.Processing.ProcessingFailure : VIDEO_OUTPUT_LIMIT
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 1 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
DOTNET_TEST_EXIT=1
```

Đã khôi phục bản sửa trước build/full suite; không để mutation trong source.

### Build

```sh
dotnet build LuxMap.slnx -warnaserror
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:08.05

DOTNET_BUILD_EXIT=0
```

### Toàn bộ dotnet test — output thật đầy đủ

```sh
dotnet test LuxMap.slnx
```

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll (.NETCoreApp,Version=v10.0)
Test run for /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 1 s - LuxMap.Persistence.Tests.dll (net10.0)
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll

Passed!  - Failed:     0, Passed:    47, Skipped:     0, Total:    47, Duration: 5 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   384, Skipped:     0, Total:   384, Duration: 313 ms - LuxMap.Shared.Tests.dll (net10.0)
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:01:44.35]     LuxMap.Api.Tests.MqttEndToEndTests.A_device_receives_executes_and_acknowledges_over_the_real_broker [SKIP]
[xUnit.net 00:01:44.35]     LuxMap.Api.Tests.MqttEndToEndTests.The_broker_enforces_identity_and_each_devices_own_topics [SKIP]
  Skipped LuxMap.Api.Tests.MqttEndToEndTests.A_device_receives_executes_and_acknowledges_over_the_real_broker [1 ms]
  Skipped LuxMap.Api.Tests.MqttEndToEndTests.The_broker_enforces_identity_and_each_devices_own_topics [1 ms]

Passed!  - Failed:     0, Passed:  1070, Skipped:     2, Total:  1072, Duration: 2 m 15 s - LuxMap.Api.Tests.dll (net10.0)

DOTNET_TEST_EXIT=0
```

**1.546 pass, 2 skip MQTT, 0 fail**, exit **0**, 145,968 s.
Hai MQTT test cần broker thật, được suite chủ động skip; không dựng/đụng stack dev để ép chạy chúng.
API suite tự dựng container PostGIS riêng của lượt chạy (BE-36), không dùng `luxmap_dev`.
Các file `build-exit.json`, `test-exit.json` lưu command, thời gian và mã thoát từ `subprocess.run`.

## 4. Production — tiêu chí 5 CHƯA ĐẠT

Dùng chính `deploy/compose.yml`, project **luxmap-prod**, build từ worktree này, image
`sha256:12aaf1f25cc41e9b579715c0008f8d274ee2ad61d73c3ded182a2cbded537583`.
Không chỉnh compose hay giới hạn tài nguyên. Tạo mới `deploy/.env` từ template với credential ngẫu nhiên,
mode 0600; không đọc/chép/in/sửa `.env` gốc. Cấu hình tạm đã xoá sau đo.
Script sizing chỉ đọc/chép từ worktree `wt-sizing`, chạy bản chép tại evidence của ticket này.

Chuỗi lệnh bootstrap đã chạy, tất cả exit 0 (log tại evidence):

```sh
docker compose -p luxmap-prod -f deploy/compose.yml build api
docker compose -p luxmap-prod -f deploy/compose.yml up -d postgres minio minio-mc
docker compose -p luxmap-prod -f deploy/compose.yml --profile tools run --rm migrate
docker compose -p luxmap-prod -f deploy/compose.yml run --rm api --seed
python3 scripts/seed_mock_set.py > .ai/results/BE-15-ffprobe-show-frames-evidence/seed-mock.sql
# SQL được nạp bằng compose exec -T postgres psql -U luxmap -d luxmap -v ON_ERROR_STOP=1
# Clone SEG-003 sang tuyến simulated bằng seed-scenario.sql; tạo GPS/lux từ tuyến clone.
docker compose -p luxmap-prod -f deploy/compose.yml up -d api caddy
docker cp luxmap-prod-caddy-1:/data/caddy/pki/authorities/local/root.crt .ai/results/BE-15-ffprobe-show-frames-evidence/caddy-root.crt
```

Áp dụng **47 migration hiện có** vào DB production thử nghiệm; không tạo migration mới.
Workload qua **HTTPS Caddy, kiểm CA local thật**, upload nguyên video, GPS/lux/config tổng hợp chung clock.
Nguồn sweep/tài sản clone là `simulated`; video thật không được coi là dữ liệu `field`/ground truth.
Detector cấu hình `yolo`, nhưng **chưa có frame tới YOLO** vì probe timeout; không nhận định độ chính xác CV.

Lượt C đầu:

```sh
python3 .ai/results/BE-15-ffprobe-show-frames-evidence/workload.py C-large
```

Script sizing gốc có exit **0 kể cả khi state=failed** vì nó là script quan sát.
Không dùng mã 0 đó làm bằng chứng pipeline thành công.
Lượt đầu trùng một phần tải full suite; chạy lại khi tests và các đo probe phụ đã xong:

```sh
python3 .ai/results/BE-15-ffprobe-show-frames-evidence/run-scenario.py C-retry
```

Wrapper mới kiểm `status == awaiting_review` và trả **exit 1**:

```json
{
  "command": [
    "/opt/homebrew/opt/python@3.14/bin/python3.14",
    "/Users/nhm809/Documents/LuxMap/wt-ffprobe/.ai/results/BE-15-ffprobe-show-frames-evidence/workload.py",
    "C-retry"
  ],
  "workload_exit_code": 0,
  "assert_awaiting_review_exit_code": 1,
  "seconds": 76.248,
  "status": "failed"
}
```

Output SQL thật (command: `docker compose -p luxmap-prod -f deploy/compose.yml exec -T postgres psql -U luxmap -d luxmap -v ON_ERROR_STOP=1 < .../worker-runs.sql`, exit 0):

```csv
run_id,sweep_id,attempt,result_state,stage,error_code,started_at,finished_at,processing_seconds,status,frame_count,detector,model_version,extractor_version,detections,observations,retained_frame_bytes
1,SWP-001,1,failed,extractor,VIDEO_TIMEOUT,2026-10-17 15:10:28.686907+00,2026-10-17 15:11:29.105583+00,60.418676,failed,0,yolo,yolo-d8bd77cde3e14d64,6daa6564987236d8361fa19a648798969004b17c89bb2ee5df1124dda5e2cef6,0,0,0
2,SWP-002,1,failed,extractor,VIDEO_TIMEOUT,2026-10-17 15:12:09.175192+00,2026-10-17 15:13:09.431368+00,60.256176,failed,0,yolo,yolo-d8bd77cde3e14d64,6daa6564987236d8361fa19a648798969004b17c89bb2ee5df1124dda5e2cef6,0,0,0

```

Mẫu RAM từ `docker stats` **khoảng 0,5 s/mẫu**, cgroup/process **1 s/mẫu**,
156 mẫu API mỗi lượt. Tổng tính từ **cùng một mẫu đủ 4 service**, không cộng các đỉnh khác thời điểm.

| Lượt | Đỉnh API (MiB) | Đỉnh tổng 4 service cùng mẫu (MiB) | Run time (s) | Frame | Kết quả |
|---|---:|---:|---:|---:|---|
| C-large | 324.2 | 566.86 | 60.418676 | 0 | failed / VIDEO_TIMEOUT |
| C-retry | 365.1 | 624.44 | 60.256176 | 0 | failed / VIDEO_TIMEOUT |

Cgroup API memory.peak lần lượt 603.770.880 và 646.238.208 byte (bao gồm page cache;
khác số Docker stats đã trừ cache). `oom=0`, `oom_kill=0`, swap=0 ở các mẫu đã ghi.
Temp tối đa 261.447.680 byte, dọn còn 4 KiB sau lỗi.
Đây là **RAM của lượt thất bại ở probe**, không phải đỉnh RAM xử lý clip thành công hay sizing YOLO.

`settings-snapshots.json` từ DB xác nhận cả hai run giữ `timeout_seconds=60`,
`maximum_frame_bytes=16777216`, `temporary_bytes_per_clip=419430400`, `maximum_concurrent_clips=1`.
CSV/stats thô, requests, result JSON, SQL và script tái lập nằm trong evidence.

## 5. Dọn dẹp, spec và an toàn stack dev

Trước `down -v` đã liệt kê volume và đối chiếu mọi mount volume của container production đều thuộc danh sách:

```text
luxmap-prod_api_logs
luxmap-prod_caddy_config
luxmap-prod_caddy_data
luxmap-prod_minio_data
luxmap-prod_postgres_data

```

```sh
docker compose -p luxmap-prod -f deploy/compose.yml down -v
```

Output thật (cuối lệnh):

```text
 Volume luxmap-prod_caddy_config Removing 
 Volume luxmap-prod_postgres_data Removing 
 Volume luxmap-prod_caddy_data Removing 
 Network luxmap-prod_backend Removing 
 Volume luxmap-prod_minio_data Removing 
 Volume luxmap-prod_api_logs Removing 
 Volume luxmap-prod_postgres_data Removed 
 Volume luxmap-prod_minio_data Removed 
 Volume luxmap-prod_caddy_data Removed 
 Volume luxmap-prod_api_logs Removed 
 Volume luxmap-prod_caddy_config Removed 
 Network luxmap-prod_backend Removed 
COMPOSE_DOWN_EXIT=0
```

Kiểm tra sau dọn:

```json
{
  "compose_down_exit": 0,
  "remaining_production_resources": {
    "container": [],
    "volume": [],
    "network": []
  },
  "dev_id_started_at_mounts_unchanged": true,
  "dev_health": {
    "/luxmap_postgres": "healthy",
    "/luxmap_minio": "healthy"
  },
  "source_video_sha256_mtime_size_unchanged": true,
  "temporary_deploy_env_removed": true
}
```

Hai container dev giữ nguyên ID, StartedAt và toàn bộ mounts, vẫn healthy; không truy cập DB `luxmap_dev`.
Nguồn video giữ nguyên SHA-256/mtime/size. Không sửa `Videos/`, `img/` hay thư mục sizing gốc.
Không prune, không xoá image, không commit/push/stage. Không có file video/ảnh trong thay đổi.

```sh
git diff --exit-code -- docs/api-contract-v1.1.md docs/openapi
```

Output rỗng, **exit 0**. Không tái xuất spec: chỉ sửa lệnh nội bộ, không đổi endpoint/DTO/schema.
Không có drift API mới cần đăng ký; ràng buộc kỹ thuật đã ghi vào CLAUDE, tiến độ ở tracking.

## 6. Đối chiếu nghiệm thu và D-item

✅ Đã kiểm: probe chỉ chọn các scalar parser dùng, giữ rotation/PTS, JSON dưới trần trên clip thật qua CLI;
version theo assembly; test hồi quy thực, mutation đỏ; build -warnaserror; full suite và mã thoát;
đo RAM production thất bại, dọn production, dev nguyên trạng, cập nhật tài liệu/tracking.

⚠️ **Chưa đạt:** mục 5 yêu cầu clip lớn tới `awaiting_review`; thực tế hai lần VIDEO_TIMEOUT,
0 frame. Vì vậy ticket chưa được đánh dấu hoàn tất toàn bộ.

📌 Giả định còn lại: ước lượng số phút chỉ có ý nghĩa với cấu trúc metadata/PTS đã nêu, không thay thế
benchmark runtime; GPS/lux/config tổng hợp không chứng minh camera clock/độ chính xác trên hiện trường.

### D-1 — Runtime probe HEVC 148 s vượt timeout hiện có (ĐÃ ĐÓNG ở Pha 2b, mục 7)

- Bằng chứng: hai run production dừng `extractor / VIDEO_TIMEOUT` ở 60,418676 s và 60,256176 s;
  CLI macOS lệnh mới hoàn thành sau 64,809 s. JSON fit không đủ bảo đảm runtime fit.
- **Cần Mỹ chốt hướng tiếp theo** để hoàn tất nghiệm thu production: ticket tối ưu runtime probe/decoder
  và đo lại trên tài nguyên triển khai, vẫn bảo toàn danh sách PTS/rotation và các giới hạn an toàn.
  Chưa thử/áp dụng thay đổi threading/decoder/kiến trúc, không tự đổi đường lấy PTS sang packet hay suy từ FPS.
- Không tự tăng timeout/quota, không thay input video hay đổi sang clip ngắn để gọi tiêu chí này là đạt.


## 7. Pha 2b — thực hiện quyết định D-1

**Nền thực hiện:** Mỹ đã chốt trong task ngày 17/10/2026: lấy PTS theo **packet**, không giải mã
trong probe. Các mục 1–6 ở trên lưu bằng chứng của pha trước; trạng thái timeout ở đó là lịch sử.
Không tăng timeout/quota, không thêm package, không đổi Contract/OpenAPI, không stage/commit/push.

### 7.1. Code, ngữ nghĩa lỗi và version

Lệnh extractor hiện tại:

```sh
ffprobe -v error -select_streams v:0 \
  -show_entries stream=width,height,time_base:stream_side_data=rotation:packet=pts \
  -of json=compact=1 <input>
```

Parser đọc `packets[].pts`, **sort tăng dần theo tick nguyên gốc** trước khi chuyển sang ns,
kiểm clock, chọn cửa sổ và seek. Giữ nguyên phép làm tròn, dung sai một tick, clock mapping,
selector ảnh và kiểm thứ tự nghiêm ngặt. Không suy PTS từ FPS; không loại packet thiếu PTS hay khử trùng:

- Thiếu khóa `pts`: `GetProperty` ném `KeyNotFoundException`, boundary hiện có đổi thành **`VIDEO_INVALID` / extractor**.
- PTS trùng sau sort (kể cả trùng bên trong khoảng, không chỉ đầu/cuối): kiểm strict tăng trả
  **`CLOCK_VIDEO_MAPPING` / video_clock** như trước. Danh sách rỗng cũng trả mã này.
- `width/height/time_base` và rotation vẫn từ **stream**, không lấy rotation từ packet/frame.

`SurveyFrameOptions` và `MediaProcess.ReadBounded` không đổi: stdout **16 MiB**, timeout **60 s**,
`TemporaryBytesPerClip=419430400`, `MaximumFrameBytes=16777216`, concurrency **1**.
`DescribeAsync` băm ffmpeg + ffprobe + assembly extractor nên sửa lệnh/parser tự đổi digest;
`SurveyFramePipeline` vẫn đăng ký `frame_extractor` và lưu FK vào run/frame. Không có version thủ công cần tăng.

### 7.2. Đo trực tiếp clip thật: packet so với frame

Lệnh đo (script chỉ đọc video; giữ stdout trong RAM, không ghi media):

```sh
python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run.py p2b-probe \
  python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-probe.py
```

Output thật của hai lệnh ffprobe và đối chiếu **toàn bộ** danh sách:

```json
{"command": ["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,time_base:stream_side_data=rotation:packet=pts", "-of", "json=compact=1", "/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/IMG_0458.MOV"], "exit_code": 0, "seconds": 0.154, "stdout_bytes": 115000, "count": 4433, "streams": [{"width": 1920, "height": 1080, "time_base": "1/600", "side_data_list": [{}, {"rotation": -90}, {}]}], "stderr": "", "packet_worst_line_bytes": 41, "reserve_bytes": 4096, "capacity_packets": 409100, "minutes_30fps": 227.27777777777777, "minutes_60fps": 113.63888888888889}
{"command": ["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,time_base:stream_side_data=rotation:frame=pts", "-of", "json=compact=1", "/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/IMG_0458.MOV"], "exit_code": 0, "seconds": 65.593, "stdout_bytes": 3948609, "count": 4433, "streams": [{"width": 1920, "height": 1080, "time_base": "1/600", "side_data_list": [{}, {"rotation": -90}, {}]}], "stderr": ""}
{"packet_sort_equals_frame": true, "packet_arrival_equals_frame": false, "unique_packet_pts": 4433}
```

Mã thoát/thời gian wrapper:

```json
{
  "command": [
    "python3",
    ".ai/results/BE-15-ffprobe-show-frames-evidence/p2b-probe.py"
  ],
  "exit_code": 0,
  "seconds": 65.79
}
```

`packet=pts`: **0,154 s / 115.000 byte**; `frame=pts`: **65,593 s / 3.948.609 byte**.
Cả hai exit **0**, đủ **4.433** PTS; `sort(packet_pts) == frame_pts`, **không** bằng khi chưa sort,
không có PTS trùng. Stream vẫn 1920×1080, `time_base=1/600`, rotation **−90°**.
Đây là đo CLI macOS, cùng máy đang build/test; không dùng làm benchmark production tách tải.
Không đổi về frame trong production hay thêm test thời gian dễ dao động; phép đo này đáp ứng yêu cầu so thời gian.

**Dung lượng vừa trần mới:** với layout packet đã đo trên clip này, thay PTS bằng Int64 dài nhất
(cả dấu âm), tính cả indentation/newline/comma: tối đa **41 byte/packet**; dành **4.096 byte** cho
wrapper/stream. `floor((16777216-4096)/41) = 409100` packet, tương đương **227,278 phút ở 30 fps**
hoặc **113,639 phút ở 60 fps** (một packet/frame như các clip đã đối chiếu).
Đây là chặn stdout có điều kiện cho cùng layout metadata, **không** phải cam kết thời lượng video
được xử lý: timeout, dung lượng upload/temp và metadata khác vẫn có thể chặn sớm hơn.

### 7.3. Test thật, phá thử, build và toàn suite

Test B-frame sinh **12 frame 64×32**, `libx264 -bf 2 -x264-params b-adapt=0:scenecut=0:keyint=30`.
Mỗi frame mã hóa số thứ tự bằng kênh đỏ; test chạy cả hai lệnh ffprobe, xác nhận thật sự có frame loại B,
packet đến sai thứ tự hiển thị, rồi assert **có thứ tự** `sort(packet_pts) == frame_pts == [0,40,…,440]`.
Sau đó gọi extractor thật, kiểm đủ PTS từng ảnh, màu pixel đúng vị trí, kích thước và phone elapsed.
Không chỉ so tập PTS. Media sinh trong temp, dọn bằng `finally`.
Ba test thiếu/trùng/rỗng PTS kiểm đúng mã lỗi, chưa consume ảnh và temp được dọn.
Hồi quy 30.000 frame, VFR, rotation, dung sai tick và các test cũ giữ xanh.

```sh
dotnet test tests/LuxMap.Infrastructure.Storage.Tests --filter FullyQualifiedName~SurveyFrameMediaTests
```

```text
Test run for /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 4 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
DOTNET_TEST_EXIT=0
```

**Phá thử:** chỉ bỏ `.Order()` khỏi parser packet, chạy đúng test B-frame:

```sh
dotnet test tests/LuxMap.Infrastructure.Storage.Tests \
  --filter FullyQualifiedName~B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images
```

```text
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
Test run for /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.38]     LuxMap.Infrastructure.Storage.Tests.SurveyFrameMediaTests.B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images [FAIL]
  Failed LuxMap.Infrastructure.Storage.Tests.SurveyFrameMediaTests.B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images [216 ms]
  Error Message:
   LuxMap.Modules.Survey.Processing.ProcessingFailure : CLOCK_VIDEO_MAPPING
  Stack Trace:
     at LuxMap.Modules.Survey.Processing.Frames.FfmpegFrameExtractor.ExtractAsync(String objectKey, Int64 expectedBytes, String expectedHash, ClipClock clock, IReadOnlyList`1 windows, Func`3 consume, CancellationToken ct) in /Users/nhm809/Documents/LuxMap/wt-ffprobe/src/LuxMap.Modules.Survey/Processing/Frames/FfmpegFrameExtractor.cs:line 115
   at LuxMap.Infrastructure.Storage.Tests.SurveyFrameMediaTests.B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images() in /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Infrastructure.Storage.Tests/SurveyFrameMediaTests.cs:line 119
--- End of stack trace from previous location ---

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 223 ms - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
```

```json
{
  "command": [
    "dotnet",
    "test",
    "tests/LuxMap.Infrastructure.Storage.Tests",
    "--filter",
    "FullyQualifiedName~B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images"
  ],
  "exit_code": 1,
  "seconds": 4.39
}
```

Đã khôi phục sort trước build, toàn suite và Docker build; không để mutation trong working tree.

```sh
dotnet build LuxMap.slnx -warnaserror
```

```text
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/wt-ffprobe/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.50
```

```json
{
  "command": [
    "dotnet",
    "build",
    "LuxMap.slnx",
    "-warnaserror"
  ],
  "exit_code": 0,
  "seconds": 3.613
}
```

```sh
dotnet test LuxMap.slnx
```

Output thật (các dòng tổng kết; log đầy đủ `p2b-test.log`):

```text
Passed!  - Failed:     0, Passed:   384, Skipped:     0, Total:   384, Duration: 548 ms - LuxMap.Shared.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    45, Skipped:     0, Total:    45, Duration: 843 ms - LuxMap.Persistence.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 4 s - LuxMap.Infrastructure.Storage.Tests.dll (net10.0)
  Skipped LuxMap.Api.Tests.MqttEndToEndTests.A_device_receives_executes_and_acknowledges_over_the_real_broker [1 ms]
  Skipped LuxMap.Api.Tests.MqttEndToEndTests.The_broker_enforces_identity_and_each_devices_own_topics [1 ms]
Passed!  - Failed:     0, Passed:  1070, Skipped:     2, Total:  1072, Duration: 2 m 13 s - LuxMap.Api.Tests.dll (net10.0)
```

```json
{
  "command": [
    "dotnet",
    "test",
    "LuxMap.slnx"
  ],
  "exit_code": 0,
  "seconds": 142.717
}
```

**1.550 pass, 2 skip MQTT, 0 fail**, exit **0**, 142,717 s.
API suite dùng container PostGIS riêng của lượt BE-36; snapshot image/id/mount ở
`p2b-suite-test-container.json`, không kết nối `luxmap_dev`. Hai test MQTT cần broker thật được suite skip.


### 7.4. Production — C và D đều đạt `awaiting_review`

Dựng **chính `deploy/compose.yml`**, project **`luxmap-prod`**, từ branch/worktree này sau khi khôi phục
mutation. Image API: `sha256:8178f6cc5596ff16b1d7900029256b6bf475b088a31db6d2354ddf4f30bdce80`.
Đo stack production **local Docker Desktop**, không thay compose, resource limit hay cấu hình an toàn.
Full suite và phép đo ffprobe CLI đã kết thúc trước workload C/D, không chồng chúng vào lượt sizing.

Bootstrap dùng lại SQL/script pha trước trong evidence của ticket (không sửa `wt-sizing`):

```sh
python3 .ai/results/BE-15-ffprobe-show-frames-evidence/create-test-env.py
docker compose -p luxmap-prod -f deploy/compose.yml build api
python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-bootstrap.py
```

Cấu hình đo `deploy/.env` tạo mới từ template với credential ngẫu nhiên, mode 0600, không ghi đè file
đang có; đã xoá sau đo. Không đọc/chép/in/sửa `.env` gốc bằng script khảo sát.
Build image exit **0**, 196,911 s. Các lệnh up/migrate/seed/nạp SQL/start trong
`p2b-bootstrap-exits.json` đều **exit 0**; bootstrap wrapper **exit 0**, 7,391 s.
Áp dụng **47 migration hiện có**, không tạo migration. Readiness qua Caddy với CA local được kiểm thật:

```text
Copy production Caddy CA exit=0
HTTPS readiness status=401, curl exit=0
```

Upload **nguyên bản IMG_0458.MOV, 261.438.540 byte**, không rút ngắn/chia clip/chuyển codec.
GPS 149 điểm, lux 8 Hz và capture config được sinh trên tuyến clone `SEG-004` (26 cột), nguồn **simulated**;
các clock dùng cùng elapsed. Detector production **yolo**, không fake. Không công bố kết quả khảo sát,
không gọi đây là ground truth hay bằng chứng chất lượng hiện trường.

Lượt C đầu đã xử lý thành công nhưng **wrapper tổng hợp exit 1** do `JSONDecodeError`: parser log đọc
cả dòng mở đầu `{` của JSON nhiều dòng do `disk.py` in ra. Đây là lỗi script bằng chứng, không phải worker.
Giữ nguyên output lỗi `p2b-C-command.log` và exit thật:

```json
{
  "command": [
    "python3",
    ".ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run-scenario.py",
    "C-p2b"
  ],
  "exit_code": 1,
  "seconds": 74.214
}
```

Đã sửa bộ đọc log chỉ nhận dòng event JSON bắt đầu `{"at":`, tổng hợp lại mẫu C đầu; chạy **C xác nhận mới**
rồi D để có cả exit workload lẫn assert trạng thái. Không sửa code ứng dụng hay giới hạn để chạy lại:

```sh
OPS_SIZING_GIS=p2b-scenario-data-partial.json python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run.py \
  p2b-C-confirm-command python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run-scenario.py C-p2b-confirm
OPS_SIZING_GIS=p2b-scenario-data-partial.json python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run.py \
  p2b-D-command python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run-scenario.py D-p2b
```

Output thật của wrapper từng lượt:

```json
{
  "command": [
    "/opt/homebrew/opt/python@3.14/bin/python3.14",
    "/Users/nhm809/Documents/LuxMap/wt-ffprobe/.ai/results/BE-15-ffprobe-show-frames-evidence/p2b-workload.py",
    "C-p2b-confirm"
  ],
  "workload_exit_code": 0,
  "assert_awaiting_review_exit_code": 0,
  "seconds": 72.354,
  "status": "awaiting_review",
  "overlap_and_images_ok": true
}
{
  "command": [
    "/opt/homebrew/opt/python@3.14/bin/python3.14",
    "/Users/nhm809/Documents/LuxMap/wt-ffprobe/.ai/results/BE-15-ffprobe-show-frames-evidence/p2b-workload.py",
    "D-p2b"
  ],
  "workload_exit_code": 0,
  "assert_awaiting_review_exit_code": 0,
  "seconds": 73.395,
  "status": "awaiting_review",
  "overlap_and_images_ok": true
}
```

Exit của hai lệnh đo ngoài cùng:

```json
{
  "command": [
    "python3",
    ".ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run-scenario.py",
    "C-p2b-confirm"
  ],
  "exit_code": 0,
  "seconds": 75.732
}
{
  "command": [
    "python3",
    ".ai/results/BE-15-ffprobe-show-frames-evidence/p2b-run-scenario.py",
    "D-p2b"
  ],
  "exit_code": 0,
  "seconds": 76.731
}
```

Thời gian `seconds` của wrapper gồm upload, polling và 10 s chờ ổn định cuối lượt;
**worker time** ở bảng dưới lấy từ `finished_at - started_at` của DB, không suy từ thời gian script.
Số **4.433 packet/frame của clip đầu vào** khác số **270 frame được chọn và lưu** để detector xử lý.

| Lượt | Sweep | Đỉnh API (MiB) | Đỉnh tổng cùng mẫu (MiB) | Worker (s) | Frame lưu | Kết quả |
|---|---|---:|---:|---:|---:|---|
| C-p2b | SWP-001 | 426.40 | 714.68 | 56.509271 | 270 | awaiting_review |
| C-p2b-confirm | SWP-002 | 444.90 | 794.72 | 56.501306 | 270 | awaiting_review |
| D-p2b | SWP-003 | 1005.00 | 1426.29 | 56.517167 | 270 | awaiting_review |


`docker stats` lấy mẫu trung vị **0,500 s**; mẫu process/cgroup **1 s**, lần lượt
**145 / 148 / 150 mẫu API**. Tổng = API + Postgres + MinIO + Caddy trong **cùng một batch đủ bốn service**,
không cộng bốn đỉnh ở các thời điểm khác nhau. Các lượt chạy nối tiếp trên cùng stack, cache/model đã ấm
ở C xác nhận/D; đây không phải ba lượt cold start độc lập. `docker stats` đã trừ cache; cgroup `memory.peak`
khác và là đỉnh tích luỹ từ lúc container khởi động (711.995.392 / 733.462.528 / 1.353.076.736 byte).
Mọi mẫu cgroup ghi được đều **swap=0, oom=0, oom_kill=0**.
Temp tối đa tương ứng **263.983.104 / 264.339.456 / 263.942.144 byte**, cuối mỗi lượt còn 4 KiB.

Lượt D khởi động 15 request ngay khi `docker top` thấy **ffmpeg** (không chỉ lúc queued/probe).
Output event thật; danh sách process trong log gốc cho thấy ffmpeg tại thời điểm overlap:

```json
{"at": "2026-10-17T15:32:22.381035Z", "event": "overlap_start", "sweep": "SWP-003", "processes": "PID                 PPID                COMMAND             RSS\n401                 377                 dotnet              458468\n7375                401                 ffmpeg              112072\n"}
{"at": "2026-10-17T15:32:24.586128Z", "event": "images_done", "label": "D-p2b", "seconds": 2.203, "results": [201, 201, 201, 201, 201, 201, 201, 201, 201, 201, 200, 200, 200, 200, 200]}
```

**10 request ảnh cột → 201; 5 `/ai/detect` → 200**, tải ảnh hoàn thành trong **2,203 s**.
Wrapper D kiểm cả `awaiting_review`, có overlap và đủ các status ảnh; không chỉ tin exit script quan sát.

SQL đối chứng sau cả ba lượt:

```sh
python3 .ai/results/BE-15-ffprobe-show-frames-evidence/p2b-collect.py
# worker-runs.sql và p2b-settings.sql được nạp qua:
# docker compose -p luxmap-prod -f deploy/compose.yml exec -T postgres psql -U luxmap -d luxmap -v ON_ERROR_STOP=1
```

Hai lệnh psql đều **exit 0**, stderr rỗng (`p2b-sql-exits.json`); collector **exit 0**.
Output `p2b-worker-runs.csv` nguyên văn:

```csv
run_id,sweep_id,attempt,result_state,stage,error_code,started_at,finished_at,processing_seconds,status,frame_count,detector,model_version,extractor_version,detections,observations,retained_frame_bytes
1,SWP-001,1,succeeded,complete,,2026-10-17 15:29:03.24905+00,2026-10-17 15:29:59.758321+00,56.509271,awaiting_review,270,yolo,yolo-d8bd77cde3e14d64,1789f375e0c7f8ba66cfb219e1f00becfc396e4329ab7fda6b711b2fe0d72b74,165,15,31442109
2,SWP-002,1,succeeded,complete,,2026-10-17 15:30:49.903634+00,2026-10-17 15:31:46.40494+00,56.501306,awaiting_review,270,yolo,yolo-d8bd77cde3e14d64,1789f375e0c7f8ba66cfb219e1f00becfc396e4329ab7fda6b711b2fe0d72b74,165,15,31442109
3,SWP-003,1,succeeded,complete,,2026-10-17 15:32:21.485646+00,2026-10-17 15:33:18.002813+00,56.517167,awaiting_review,270,yolo,yolo-d8bd77cde3e14d64,1789f375e0c7f8ba66cfb219e1f00becfc396e4329ab7fda6b711b2fe0d72b74,165,15,31442109
```

Output kiểm settings/frame version của collector:

```text
Frame version/run và quota mặc định: khớp mọi run.
```

Cả ba run có `timeout_seconds=60`, `maximum_frame_bytes=16777216`,
`temporary_bytes_per_clip=419430400`, `maximum_concurrent_clips=1`, detector `yolo`.
**810/810 frame** lưu FK extractor khớp run (`frame_extractor_version_matches_run=true` cho cả ba),
version mới **`1789f375e0c7f8ba66cfb219e1f00becfc396e4329ab7fda6b711b2fe0d72b74`**, khác digest pha trước.
Mỗi lượt có 165 detection, 15 observation; không dùng các con số này để kết luận độ chính xác CV.
Raw stats/process/requests/result, SQL, settings, disk và script đều trong evidence, tên bắt đầu `p2b-`,
`C-p2b*` hoặc `D-p2b*`; bằng chứng pha trước giữ nguyên.

### 7.5. Dọn stack, kiểm phạm vi và đóng D-1

Trước `down -v` đã liệt kê volume, xác nhận toàn bộ mang tiền tố `luxmap-prod_`, và đối chiếu
mọi volume mount của container có label project `luxmap-prod` nằm trong danh sách:

```text
luxmap-prod_api_logs
luxmap-prod_caddy_config
luxmap-prod_caddy_data
luxmap-prod_minio_data
luxmap-prod_postgres_data
```

```sh
docker compose -p luxmap-prod -f deploy/compose.yml down -v
```

Output thật (cuối log):

```text
 Container luxmap-prod-postgres-1 Removing 
 Container luxmap-prod-postgres-1 Removed 
 Network luxmap-prod_backend Removing 
 Volume luxmap-prod_api_logs Removing 
 Volume luxmap-prod_caddy_config Removing 
 Volume luxmap-prod_minio_data Removing 
 Volume luxmap-prod_caddy_data Removing 
 Volume luxmap-prod_postgres_data Removing 
 Volume luxmap-prod_caddy_config Removed 
 Volume luxmap-prod_api_logs Removed 
 Volume luxmap-prod_postgres_data Removed 
 Volume luxmap-prod_minio_data Removed 
 Volume luxmap-prod_caddy_data Removed 
 Network luxmap-prod_backend Removed
COMPOSE_DOWN_EXIT=0
```

Kiểm sau dọn, từ `p2b-cleanup-verification.json`:

```json
{
  "compose_down_exit": 0,
  "remaining_production_resources": {
    "container": [],
    "volume": [],
    "network": []
  },
  "dev_id_started_at_mounts_health_unchanged": true,
  "dev_health": {
    "/luxmap_postgres": "healthy",
    "/luxmap_minio": "healthy"
  },
  "source_video_sha256_mtime_size_unchanged": true,
  "temporary_deploy_env_removed": true
}
```

Cleanup wrapper exit **0**. Dev giữ nguyên ID, StartedAt, mounts và healthy; không truy cập DB
`luxmap_dev`. Video giữ nguyên SHA-256/mtime/size. Không sửa `Videos/`, `img/`, worktree sizing,
không prune, không xoá image. Không còn container/volume/network của project đo.

`CLAUDE.md` đã ghi bẫy `frame=` giải mã toàn clip, `packet=` cần sort và ngữ nghĩa lỗi;
`docs/development.md` cập nhật lệnh chạy; thẻ đầu `tracking.html` ghi kết quả Pha 2b.
Không phát sinh drift API: sửa lỗi lệnh nội bộ/parser, không đổi endpoint/DTO/schema, thuật toán chọn ảnh,
clock mapping hay quy tắc phân loại. Contract/OpenAPI giữ nguyên; không tái xuất spec.

Kiểm diff cuối lượt (output thật; stdout/stderr của ba lệnh đều rỗng):

```json
[
  {
    "command": [
      "git",
      "diff",
      "--check"
    ],
    "exit_code": 0,
    "stdout": "",
    "stderr": ""
  },
  {
    "command": [
      "git",
      "diff",
      "--exit-code",
      "--",
      "docs/api-contract-v1.1.md",
      "docs/openapi",
      "deploy/compose.yml",
      "Dockerfile",
      "src/LuxMap.Modules.Survey/Processing/Frames/SurveyFrameOptions.cs"
    ],
    "exit_code": 0,
    "stdout": "",
    "stderr": ""
  },
  {
    "command": [
      "git",
      "diff",
      "--cached",
      "--exit-code"
    ],
    "exit_code": 0,
    "stdout": "",
    "stderr": ""
  }
]
```

**D-1 ĐÃ ĐÓNG:** hướng packet của Mỹ đã áp dụng, packet/frame trùng chính xác trên clip thật,
B-frame được chốt bằng ảnh/PTS và mutation đỏ, C/D đi hết pipeline mà không nới giới hạn.

✅ **Đã kiểm:** build -warnaserror; toàn suite 1.550 pass/2 MQTT skip; hồi quy/mutation; probe thật;
production C và D awaiting_review/270 frame; 15 request ảnh chồng tải; RAM cùng mẫu; version và quota;
dọn production, dev/video nguyên trạng, diff/spec/phạm vi cuối lượt.

⚠️ **Chưa kiểm:** hai test MQTT cần broker thật được suite skip; không dựng broker dev để ép chạy.
Chưa đo trên VPS thuê thật; đây là stack production chạy local. Không còn tiêu chí Pha 2b chưa đạt.

📌 **Giả định/phạm vi bằng chứng:** GPS/lux/config tổng hợp, tài sản simulated, một clip iPhone này và
clip B-frame tự sinh; không thay thế kiểm clock thiết bị WP6/ground truth/độ chính xác WP4. Chặn số phút
chỉ cho layout JSON đã đo. Không còn D-item mới cần Mỹ quyết ở ticket này. **Chưa stage/commit/push**
theo mục KHÔNG ĐƯỢC làm.
