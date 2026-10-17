---
ticket: BE-15-ffprobe-show-frames
title: Clip khảo sát thật > ~1 phút fail VIDEO_OUTPUT_LIMIT vì ffprobe in mọi trường của mọi frame
status: review
phase: 2
owner: codex
branch: fix/BE-15-ffprobe-show-frames
depends_on:
  - "OPS-SIZING (nhánh local docs/OPS-SIZING, .ai/results/OPS-SIZING.md mục 'Video: bước nào thực sự chạy')"
---

## Bối cảnh — đã kiểm chứng

`src/LuxMap.Modules.Survey/Processing/Frames/FfmpegFrameExtractor.cs:87` chạy:

```
ffprobe -v error -select_streams v:0 -show_entries stream=width,height,time_base:stream_side_data=rotation:frame=pts \
        -show_frames -of json <input>
```

`-show_frames` làm ffprobe in **mọi trường của mọi frame** (media_type, pkt_pos, crop_*, …), không chỉ `pts`. stdout được đọc qua
`ReadBounded` với trần **16 MiB** (`FfmpegFrameExtractor.cs` ~dòng 226, ném `VIDEO_OUTPUT_LIMIT`). Đo trên clip thật của Mỹ (HEVC 1080p iPhone):

| Clip | Frame | Có `-show_frames` | Bỏ `-show_frames` |
|---|---:|---:|---:|
| IMG_0466.MOV (77 s, 111 MB) | 2306 | 18.976.469 B → **fail** | 3.237.541 B |
| IMG_0458.MOV (148 s, 261 MB) | 4433 | 35.971.346 B → **fail** | 6.061.373 B |

Bỏ `-show_frames` vẫn ra `frames[].pts` (kèm `side_data_list` mỗi frame) và `streams[0]` có `width,height,time_base,side_data_list`.
Hệ quả: mọi clip thực địa dài hơn ~1 phút **không xử lý được** — sweep `failed` ở bước `extractor`.

## Yêu cầu

1. Sửa lệnh ffprobe để output chỉ mang thứ code đọc (đọc kỹ code parse phía sau: `pts` từng frame, `time_base`, `width/height`, rotation —
   **rotation lấy từ đâu**, stream side data hay frame side data? đừng làm mất nó). Cân nhắc thu gọn thêm (vd. bỏ `side_data_list` của frame
   nếu code không dùng) để clip dài hơn nữa vẫn dưới trần. **Giữ nguyên trần 16 MiB** trừ khi có lý do đo được — nếu đổi, ghi lý do.
   Ghi rõ trong kết quả: với lệnh mới, clip dài tối đa bao nhiêu phút (ở 30 fps / 60 fps) thì vừa trần.
2. Phiên bản extractor: nếu `artifact_version` / version của extractor băm lệnh hoặc tham số (BE-34 — "mọi phát hiện tự động ghi phiên bản"),
   kiểm xem đổi lệnh probe có cần đổi version không, và làm cho đúng luật hiện có (đọc `SurveyFrameOptions`, chỗ tạo version của extractor).
3. **Test hồi quy** (theo khuôn test sẵn có: `tests/LuxMap.Shared.Tests/SurveyFrameTests.cs`,
   `tests/LuxMap.Infrastructure.Storage.Tests/SurveyFrameMediaTests.cs`, `tests/LuxMap.Api.Tests/SurveyFrameFixture.cs`; CI có cài ffmpeg):
   một clip **sinh bằng ffmpeg trong test** (vd. testsrc, đủ dài/đủ frame để lệnh CŨ vượt 16 MiB — tính toán số frame cần) đi qua probe thành
   công với lệnh mới. **Phá thử:** trả `-show_frames` về → test đỏ. Không commit file video lớn vào repo. Nếu sinh clip quá chậm cho CI,
   giảm kích thước khung (vd. 64×64) — lượng JSON phụ thuộc số frame, không phụ thuộc độ phân giải; kiểm điều đó bằng đo.
4. Chạy **toàn bộ** `dotnet test LuxMap.slnx` (BE-36: container riêng mỗi lượt) + build `-warnaserror`; đọc **mã thoát**. Đổi OpenAPI thì
   không (thay đổi nội bộ) — xác nhận spec không đổi.
5. **Đo lại clip lớn trên stack production** (khuôn OPS-SIZING): dựng `deploy/compose.yml` project `luxmap-prod` từ **branch này**, chạy kịch bản
   C với `/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/IMG_0458.MOV` (148 s, 261 MB) tới `awaiting_review` (GPS/lux tổng hợp được),
   lấy mẫu `docker stats` ~1 s: đỉnh RAM API + tổng, thời gian xử lý, số frame. Có thể tái dùng script ở
   `/Users/nhm809/Documents/LuxMap/wt-sizing/.ai/results/OPS-SIZING-stats/` (chỉ đọc/chép, không sửa thư mục đó). Dọn `luxmap-prod` khi xong.
6. Ghi drift nếu có hành vi nhìn thấy được thay đổi (đọc `docs/contract-drift.md` mục BE-15 — có lẽ không cần, đây là sửa lỗi nội bộ);
   ràng buộc kỹ thuật (bẫy `-show_frames` + `-show_entries`) vào `CLAUDE.md` mục BE-15 thích hợp; thẻ tiến độ đầu `tracking.html`.
7. Kết quả + output lệnh thật vào `.ai/results/BE-15-ffprobe-show-frames.md` (tiếng Việt).

## KHÔNG ĐƯỢC làm

- 🔴 Không đụng stack DEV: container `luxmap_postgres`, `luxmap_minio`, volume `luxmap_*_data`, DB `luxmap_dev`. Compose chỉ `-f deploy/compose.yml`
  (project `luxmap-prod`); trước `down -v` liệt kê volume và chắc chắn đều tiền tố `luxmap-prod`. Không `docker system prune`.
- `Videos/`, `img/` ở working tree chính: chỉ đọc. Không commit video/ảnh vào repo.
- `.env` trong worktree chỉ cho tiến trình test đọc: không in, không sửa, không chép nội dung.
- Không commit, không push, không `git add -A`. Không thêm package. Không đổi Contract.
- Không nới các giới hạn an toàn khác (timeout, `TemporaryBytesPerClip`, `MaximumFrameBytes`) để "cho qua". Chỗ mơ hồ → D-item cuối file kết quả.

---

## Quyết định D-1 — Mỹ, 17/10/2026: lấy PTS theo PACKET, không giải mã

Claude đo trên 4 clip thật (ffprobe macOS, `-of json=compact=1`): tập PTS theo packet **trùng khít** tập theo frame, nhanh hơn ~300 lần.

| Clip | Frame | packet = frame (sau sort) | `packet=pts` | `frame=pts` |
|---|---:|---|---:|---:|
| IMG_0458.MOV (148 s) | 4433 | True | 0,2 s | 59,9 s |
| IMG_0466.MOV (77 s) | 2306 | True | 0,1 s | 29,3 s |
| IMG_0465.MOV (44 s) | 1316 | True | 0,1 s | 20,7 s |
| export.MOV (39 s) | 1170 | True | 0,0 s | 3,2 s |

### Pha 2b — làm tiếp trên cùng nhánh

1. Đổi probe sang `-show_entries stream=…:stream_side_data=rotation:packet=pts` (bỏ `frame=pts`), đọc `packets[].pts`, **sắp tăng dần** để
   ra thứ tự hiển thị (packet đến theo thứ tự giải mã khi có B-frame). Xử lý rõ ràng packet **thiếu `pts`** và **pts trùng** (đọc code
   hiện tại xử lý frame thiếu/trùng thế nào — giữ cùng ngữ nghĩa, fail rõ ràng thay vì đoán). Giữ rotation, width/height, time_base như cũ.
2. Test: (a) test hồi quy sẵn có vẫn xanh; (b) **clip sinh có B-frame** (vd. libx264 `-bf 2`) — chứng minh danh sách PTS sau sort **bằng đúng**
   danh sách PTS theo frame (chạy cả hai lệnh ffprobe trong test để so) và frame trích ra đúng vị trí; (c) **phá thử**: bỏ bước sort → test (b)
   đỏ; đổi về `frame=pts` → test thời gian/timeout không cần, nhưng ghi số giây đo được trong kết quả.
3. Toàn bộ `dotnet test LuxMap.slnx` + build `-warnaserror`, đọc mã thoát.
4. **Đo lại production (tiêu chí 5)** với `IMG_0458.MOV` tới `awaiting_review`: đỉnh RAM API + tổng, thời gian xử lý, số frame; cả lượt chồng tải
   D (clip + 15 request ảnh) nếu kịp. Dọn `luxmap-prod`.
5. Cập nhật `CLAUDE.md` (bẫy: `frame=` buộc ffprobe giải mã toàn clip; dùng `packet=` + sort), `tracking.html`, và phần kết quả
   `.ai/results/BE-15-ffprobe-show-frames.md` (thêm mục Pha 2b, đóng D-1).

Mục KHÔNG ĐƯỢC làm ở trên vẫn áp nguyên. Không tăng timeout.

---

## Pha 2c — sửa phát hiện P2 của review (Codex, chỉ đọc, 17/10/2026)

**P2 (đã tái hiện):** `packet=pts` + `Order()` đếm cả packet mang cờ **discard** của MOV/MP4 có **edit list** — packet chỉ để giải mã tham chiếu,
không bao giờ thành frame. Ví dụ x264 12 frame, time base 1/1000, edit list `media_time=200`, `duration=360`: packet sau sort
`[-120,-80,-40,0,40,…,320]` (3 packet đầu discard), frame thật `[0,40,…,320]` → clock đúng bị `CLOCK_VIDEO_MAPPING`; edit list cắt **đuôi** cũng
làm điểm cuối lệch. Câu "giữ ngữ nghĩa parser frame cũ" ở `CLAUDE.md` (~dòng 1669) đang khẳng định quá rộng.

1. Lấy thêm cờ packet (`packet=pts,flags`) và **loại packet có cờ discard (`D`)**. **Kiểm thực nghiệm** cả edit list cắt **đầu** lẫn cắt **đuôi**:
   sau khi lọc, tập PTS packet có **bằng đúng** tập PTS frame (`frame=pts`) không. Nếu cắt đuôi KHÔNG được đánh cờ discard, tìm cách đúng mà vẫn
   không giải mã toàn clip (đọc mov demuxer / thông tin stream); nếu không có cách chắc chắn → **D-item**, đừng đoán và đừng quay về giải mã cả clip.
2. Test (khuôn `SurveyFrameMediaTests`): clip sinh có edit list cắt đầu **và** clip cắt đuôi, so danh sách PTS của extractor với `frame=pts` thật,
   trích frame đầu/cuối đúng. Phá thử: bỏ lọc discard → test đỏ. Giữ test B-frame và test 30.000 frame.
3. Kiểm lại 4 clip thật (`IMG_0458`, `IMG_0466`, `IMG_0465`, `export.MOV` ở working tree chính, chỉ đọc): tập PTS sau lọc vẫn bằng tập frame.
4. Toàn bộ `dotnet test LuxMap.slnx` + build `-warnaserror`, đọc mã thoát. **Không cần** đo lại production (thời gian/RAM không đổi đáng kể) —
   trừ khi cách sửa có giải mã thêm.
5. Sửa câu khẳng định trong `CLAUDE.md`, cập nhật mục kết quả (thêm "Pha 2c"), commit **không**: để working tree cho Claude review.

Mục KHÔNG ĐƯỢC làm vẫn áp nguyên.
