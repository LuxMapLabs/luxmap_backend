# Lưu trữ ảnh và object store

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi đọc / ghi ảnh, thumbnail, MinIO, ImageSharp, video bytes. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Sáu quy tắc chốt ở BE-11 — lưu trữ ảnh

**1. PROXY qua API, KHÔNG BAO GIỜ presigned URL.**

Mọi byte ảnh đi qua endpoint .NET. MinIO bind `127.0.0.1`, không bao giờ phơi ra ngoài.

Lý do không phải hiệu năng mà là **phân quyền**: chuỗi bảo vệ của BE-08 có bốn lớp — claim
`commune_ids` trong JWT có chữ ký → `CommuneScope` (không nhận input client) → `CommuneFilter.Narrow`
(403 khi vượt phạm vi) → `HasQueryFilter` (đưa `commune_id` vào `WHERE`). **Cả bốn đều bám vào một
truy vấn EF.** Presigned URL là chữ ký HMAC do MinIO cấp; MinIO không biết `commune_id` là gì, không
đọc JWT, không có bảng `administrative_unit`. Byte rời MinIO là **không lớp nào chạy**.

**Thu hồi quyền theo claim JWT:** đổi xã/vai trò có hiệu lực khi cấp lại access token (tối đa 60 phút, Contract §4).
API kiểm claim ở mỗi request; presigned URL vẫn sống tới hạn riêng, kể cả sau khi token được cấp lại hoặc link đã rò qua log/`Referer`.

Khớp luôn Contract mục 2.7 (`GET /api/v1/frames/{frame_id}/thumbnail` → JPEG) và
`mock-pole-detail.json` (`"thumbnail_url": "/api/v1/frames/FRM-088213/thumbnail"` — đường dẫn tương
đối, không host, không chữ ký). FE đã dựng theo hình dạng đó.

**2. Bốn bucket, key phân tầng, KHÔNG nhúng `commune_id`.**

```
luxmap-survey     original/{frame_id}.jpg      thumb/{frame_id}.jpg
luxmap-evidence   original/{evidence_id}.jpg   thumb/{evidence_id}.jpg
luxmap-citizen    ảnh đã sanitize + thumbnail
luxmap-video      clip và JSON/JSONL đầu vào khảo sát (key theo ingest)
```

Tên bucket/key lấy từ `StorageKeys` và ingest; Citizen sanitize trước lưu, video/raw input đi qua `StoreStreamAsync`.

`commune_id` nằm ngoài key **có chủ đích**: phân quyền có đúng một nguồn sự thật là cột `commune_id`
với khoá ngoại thật tới `administrative_unit`. Một bản sao trong key là **câu trả lời thứ hai không
ràng buộc** cho cùng câu hỏi, và không có gì phát hiện hai bản lệch nhau — đúng loại drift âm thầm mà
BE-09 đã bỏ công loại trừ. Nó chỉ có lợi khi dùng policy theo prefix ở tầng MinIO, tức chỉ khi chọn
presigned — mà quy tắc 1 đã loại.

⚠️ **KHÔNG BAO GIỜ sắp xếp theo object key.** Trong key là ID có prefix, độ rộng là TỐI THIỂU chứ
không cố định, nên `FRM-100000` đứng trước `FRM-999999` khi so chuỗi. Cùng cái bẫy đã ghi cho
`ORDER BY pole_id` ở `CLAUDE.md`, mục Quy ước API. Object store không phải index.

**3. Ghi OBJECT trước, commit ROW sau.**

Cả hai chiều đều có thể hỏng giữa chừng. Chiều này hỏng thành **object mồ côi** — tốn byte, BE-35 đối
chiếu ra được. Chiều ngược lại hỏng thành **row mồ côi**, tức `thumbnail_url` trả 404 ngay trước mặt
người dùng. Chọn chiều để lỗi rơi vào chỗ máy dọn được, không phải chỗ người nhìn thấy.

Đối chiếu object mồ côi và dung lượng thuộc BE-35; tiến độ xem `tracking.html`. Mỗi lượt ghi trả **byte thật**, không tin `Content-Length`.

**4. Ảnh gốc khảo sát / evidence / ảnh cột giữ NGUYÊN BYTE; thumbnail là object RIÊNG.**

Không re-encode, strip EXIF hay xoay original. **Ảnh công dân là ngoại lệ**: sanitize trước lưu
(`docs/agents/traps/citizen.md`, mục P2b). Capture config và chất lượng GPS track kiểm ở cấp phiên, không đòi metadata
ISO/shutter/aperture/GPS/heading trên từng JPEG (D-R24; `SurveyRawParser`). Thumbnail bỏ metadata tường minh — xem cuối file.

**5. Chỉ nhận JPEG, quyết bằng MAGIC BYTES.**

`FF D8 FF`. Không tin `Content-Type`, không tin đuôi file — PNG đổi tên `.jpg` bị chặn, JPEG khai sai
header vẫn qua. Lớp phòng thủ thứ hai: ImageSharp chạy trên một `Configuration` **chỉ đăng ký
JpegConfigurationModule**, không phải `Configuration.Default` — PNG dựng sẵn để tấn công không chỉ bị
từ chối bởi chính sách, mà **không có code path nào phân tích được nó**.

**Video/raw input là luồng riêng**: D-R28 chốt proxy streaming qua API (BE-15), MP4 H.264/H.265, clip tối đa 300 MiB;
quy tắc JPEG chỉ áp lên luồng ảnh. Không presigned; xem `docs/agents/traps/survey.md`, mục BE-15 P2a.

**6. Thumbnail sinh ĐỒNG BỘ, 320px cạnh dài, JPEG q80 — con số TẠM.**

Contract chỉ nói "ảnh JPEG", **không quy định kích thước**. 320/q80 là tôi chọn — **phải chốt với FE ở
FW-00**, nếu không hai bên tự quyết khác nhau.

Thumbnail sinh đồng bộ trên đường ghi (`ImagePipeline`); scheduler hiện tại là DB lease (`docs/agents/traps/faults-workorders.md`, mục BE-26).
Mỗi upload giữ ảnh gốc + thumbnail trong RAM; batching và giới hạn đồng thời phải giữ tải RAM có chặn khi nhận cả sweep.

**Vị trí code:** `IObjectStore` ở `LuxMap.Shared` (không kèm package nào); adapter + magic bytes +
thumbnail ở `LuxMap.Infrastructure.Storage`; test ở `LuxMap.Infrastructure.Storage.Tests`, **không cần
MinIO và không cần DB**. Không đặt vào `LuxMap.Persistence` (tên đó chỉ về EF/Postgres) và không đặt
package vào `Shared` (sẽ kéo S3 SDK lẫn image codec vào cả hai assembly test đang sạch hạ tầng).

**Bucket do sidecar `minio-mc` tạo**, không phải ứng dụng. .NET chỉ fail-fast trên **cấu hình** thiếu,
theo khuôn `LuxMapConnectionString` và `JwtOptions.Validate`; storage không provision bucket lúc startup.
Worker job có chạm DB lúc startup, trong vòng retry (BE-26). Sidecar bắt buộc `restart: "no"`: mặc định compose sẽ khởi
động lại nó **vô hạn** sau mỗi lần chạy thành công.

**Image MinIO kéo từ `quay.io`, không phải Docker Hub — pin bằng INDEX digest.**

⚠️ **Digest phải là digest của MANIFEST LIST, không phải per-platform.** Pin nhầm digest arm64 sẽ làm
**mọi máy Windows/x86 của nhóm không kéo nổi image**, và lỗi chỉ lộ trên máy người khác chứ không bao
giờ trên máy vừa sửa. Lấy bằng `docker buildx imagetools inspect <ref>`, đọc dòng `Digest:` cấp cao
nhất — `docker manifest inspect` **không** in số đó.

Tình trạng pull image và test S3 thật theo `tracking.html`; không suy trạng thái registry từ cache máy.

**Hai ràng buộc về ImageSharp — kiểm chứng bằng test, không phải giả định:**

- **ImageSharp GIỮ EXIF qua resize** (trái với giả định thông thường). Việc bỏ metadata khỏi
  thumbnail phải **TƯỜNG MINH**: `ExifProfile` / `XmpProfile` / `IptcProfile` = `null`. Không dựa vào
  hành vi mặc định. Lý do là **bảo mật, không phải dung lượng**: thumbnail là object phục vụ rộng
  nhất, GPS chụp là trường nhạy cảm nhất — để nguyên là đặt dữ liệu nhạy nhất vào chỗ ít bảo vệ nhất,
  trong một hệ thống phân quyền theo địa bàn. Canh bằng
  `The_thumbnail_carries_no_gps_so_a_widely_served_object_cannot_leak_capture_locations`; test đó
  **không phải chuyện dọn dẹp metadata**, đừng xoá khi refactor.

- 🔴 **Năm advisory của ImageSharp 3.1.12 đang được TẮT theo mã (08/10/2026, `Directory.Build.props`)** — bản vá chỉ có ở
  4.1.2, mà 4.x cần license key (gạch dưới). Chấp nhận được **CHỈ VÌ** mọi lần decode đi qua cấu hình chỉ-JPEG
  (`ThumbnailFactory.JpegOnly`, sau kiểm `FF D8 FF`): TIFF / histogram / ICC không có đường tới. `Image.Load(stream)` không kèm
  `DecoderOptions` dùng `Configuration.Default` (mọi codec) và **phá đúng tiền đề đó** — `JpegOnlyDecodeTests` đỏ. Lên 4.1.2 thì
  gỡ khối suppress.
- **Ghim ImageSharp ở 3.x.** Từ 4.x, task validate lúc build đòi `SixLaborsLicenseKey`, và
  `ContinueOnError="$(Configuration.StartsWith('Debug'))"` nghĩa là Debug chỉ cảnh báo còn
  **Release/CI/deploy GÃY**. Điều khoản Split License không đổi; chỉ khác cái cổng kiểm key. Muốn lên
  4.x phải **xin key TRƯỚC**. Đã đưa vào FW-00.

**Giới hạn kiểm chứng:** `MissingObjectTests` và `SurveyIngestTests` dùng `FakeS3`, không canh đường MinIO thật.
Tiến độ test S3 thật theo `tracking.html` (BE-36 D-7); một lần kiểm tay không thay regression test.
