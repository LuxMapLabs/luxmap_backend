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

Nặng nhất: **thu hồi quyền không hồi tố.** Chuyển kỹ sư sang xã khác thì query filter chặn ngay ở
request kế tiếp; URL đã ký vẫn sống tới lúc hết hạn — dù link đã bị chia sẻ, đã vào log proxy, hay đã
rò qua header `Referer`.

Khớp luôn Contract mục 2.7 (`GET /api/v1/frames/{frame_id}/thumbnail` → JPEG) và
`mock-pole-detail.json` (`"thumbnail_url": "/api/v1/frames/FRM-088213/thumbnail"` — đường dẫn tương
đối, không host, không chữ ký). FE đã dựng theo hình dạng đó.

**2. Hai bucket, key phân tầng, KHÔNG nhúng `commune_id`.**

```
luxmap-survey     original/{frame_id}.jpg      thumb/{frame_id}.jpg
luxmap-evidence   original/{evidence_id}.jpg   thumb/{evidence_id}.jpg
```

`commune_id` nằm ngoài key **có chủ đích**: phân quyền có đúng một nguồn sự thật là cột `commune_id`
với khoá ngoại thật tới `administrative_unit`. Một bản sao trong key là **câu trả lời thứ hai không
ràng buộc** cho cùng câu hỏi, và không có gì phát hiện hai bản lệch nhau — đúng loại drift âm thầm mà
BE-09 đã bỏ công loại trừ. Nó chỉ có lợi khi dùng policy theo prefix ở tầng MinIO, tức chỉ khi chọn
presigned — mà quy tắc 1 đã loại.

⚠️ **KHÔNG BAO GIỜ sắp xếp theo object key.** Trong key là ID có prefix, độ rộng là TỐI THIỂU chứ
không cố định, nên `FRM-100000` đứng trước `FRM-999999` khi so chuỗi. Cùng cái bẫy đã ghi cho
`ORDER BY pole_id` ở mục 0. Object store không phải index.

**3. Ghi OBJECT trước, commit ROW sau.**

Cả hai chiều đều có thể hỏng giữa chừng. Chiều này hỏng thành **object mồ côi** — tốn byte, BE-35 đối
chiếu ra được. Chiều ngược lại hỏng thành **row mồ côi**, tức `thumbnail_url` trả 404 ngay trước mặt
người dùng. Chọn chiều để lỗi rơi vào chỗ máy dọn được, không phải chỗ người nhìn thấy.

**Nợ để lại:** chưa có job đối chiếu object mồ côi. Thuộc **BE-35** (W16), cùng chỗ với báo cáo dung
lượng — mỗi lần ghi đã trả về **số byte thật đã ghi**, không phải `Content-Length` client khai.

**4. Ảnh gốc NGUYÊN BYTE. Thumbnail là object RIÊNG.**

Không re-encode, không strip EXIF, không xoay theo orientation. BE-16 từ chối frame thiếu
ISO/shutter/aperture/GPS/heading, mà ảnh auto-exposure **không đo lại được sau khi chụp**.

⚠️ **ImageSharp KHÔNG tự vứt EXIF khi resize** — kiểm chứng thực nghiệm, không phải giả định: test
đòi thumbnail sạch metadata đã **fail** cho tới khi có dòng `image.Metadata.ExifProfile = null`. Nên
việc bỏ metadata khỏi thumbnail là **tường minh**, đừng xoá tưởng thừa. Quan trọng nhất là GPS:
thumbnail là object được phục vụ rộng nhất, nhét toạ độ chụp vào đó là đặt trường nhạy cảm nhất vào
chỗ ít được bảo vệ nhất.

**5. Chỉ nhận JPEG, quyết bằng MAGIC BYTES.**

`FF D8 FF`. Không tin `Content-Type`, không tin đuôi file — PNG đổi tên `.jpg` bị chặn, JPEG khai sai
header vẫn qua. Lớp phòng thủ thứ hai: ImageSharp chạy trên một `Configuration` **chỉ đăng ký
JpegConfigurationModule**, không phải `Configuration.Default` — PNG dựng sẵn để tấn công không chỉ bị
từ chối bởi chính sách, mà **không có code path nào phân tích được nó**.

⚠️ **Video là PLANNED, không phải được phép (D-R14).** Phiếu v1.2 cho Kỹ sư hiện trường quay video (khảo
sát và thiết bị), nhưng cho tới ticket video quy tắc này **giữ nguyên: chỉ JPEG**. Hướng đề xuất cho
ticket đó là upload thẳng MinIO bằng presigned URL — **ngược quy tắc 1** ở trên, nên ticket video phải tự
trả lời chuyện phân quyền theo địa bàn cho byte video, không được coi là đã giải.

**6. Thumbnail sinh ĐỒNG BỘ, 320px cạnh dài, JPEG q80 — con số TẠM.**

Contract chỉ nói "ảnh JPEG", **không quy định kích thước**. 320/q80 là tôi chọn — **phải chốt với FE ở
FW-00**, nếu không hai bên tự quyết khác nhau.

Đồng bộ vì `tasks-backend.csv` đặt "sinh thumbnail" vào tiêu chí của **BE-11**, còn Hangfire (BE-26)
mãi W12 mới có. Cái giá: mỗi upload buffer **một ảnh gốc + thumbnail trong RAM** (~5–15 MB mỗi frame).
Tuần tự thì phẳng; **hàng trăm frame một sweep đến cùng lúc thì không** — batching và giới hạn đồng
thời là việc của **BE-15**, đừng để phát hiện lúc có tải.

**Vị trí code:** `IObjectStore` ở `LuxMap.Shared` (không kèm package nào); adapter + magic bytes +
thumbnail ở `LuxMap.Infrastructure.Storage`; test ở `LuxMap.Infrastructure.Storage.Tests`, **không cần
MinIO và không cần DB**. Không đặt vào `LuxMap.Persistence` (tên đó chỉ về EF/Postgres) và không đặt
package vào `Shared` (sẽ kéo S3 SDK lẫn image codec vào cả hai assembly test đang sạch hạ tầng).

**Bucket do sidecar `minio-mc` tạo**, không phải ứng dụng. .NET chỉ fail-fast trên **cấu hình** thiếu,
theo khuôn `LuxMapConnectionString` và `JwtOptions.Validate` — repo không có tiền lệ chạm dịch vụ
ngoài lúc khởi động, kể cả PostgreSQL. Sidecar bắt buộc `restart: "no"`: mặc định compose sẽ khởi
động lại nó **vô hạn** sau mỗi lần chạy thành công.

**Image MinIO kéo từ `quay.io`, không phải Docker Hub — pin bằng INDEX digest.**

Docker Hub đã gỡ hẳn `minio/minio` và `minio/mc`: API repository trả **404** chứ không phải 401, nên
`docker login` vô ích. Registry riêng của MinIO trên quay.io còn phục vụ đúng hai tag đang pin, cùng
bytes — digest manifest list ở quay trùng khít digest của image Docker Hub còn trong cache.

⚠️ **Digest phải là digest của MANIFEST LIST, không phải per-platform.** Pin nhầm digest arm64 sẽ làm
**mọi máy Windows/x86 của nhóm không kéo nổi image**, và lỗi chỉ lộ trên máy người khác chứ không bao
giờ trên máy vừa sửa. Lấy bằng `docker buildx imagetools inspect <ref>`, đọc dòng `Digest:` cấp cao
nhất — `docker manifest inspect` **không** in số đó.

Dòng release OSS đứng yên ở `RELEASE.2025-09-07` ⇒ đây là **hoãn, không phải giải**. ⚠️ **quay.io nay
cũng trả 401** cho kéo ẩn danh (26/09, kiểm lại 11/10/2026) — chỉ máy còn image trong cache mới chạy được
MinIO. Test S3 thật (tách khỏi BE-36, quyết định D-7) phải chọn nguồn image mới trước.

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

**Nợ có tên người đòi:** `S3ObjectStore` chỉ được test trên **client S3 giả** (`MissingObjectTests`
dựng nó trên `FakeS3`; `SurveyIngestTests` cũng vậy) — **chưa có test nào với MinIO thật**. Đã kiểm
end-to-end thủ công một lần lúc làm BE-11 (8.2 KiB gốc + 1.8 KiB thumbnail vào `luxmap-survey`, SHA-256
vòng tròn khớp) nhưng **không có gì canh đường thật từ đó trở đi**. BE-36 (11/10/2026) **tách** món này
thành ticket riêng (D-7) vì chưa có nguồn image MinIO kéo được. Đừng để nợ này chỉ nằm trong báo cáo
một phiên làm việc.
