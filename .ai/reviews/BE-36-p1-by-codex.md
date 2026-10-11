# BE-36 P1 — review độc lập bởi Codex (11/10/2026)

Đọc tại `4bfd3f0`; `src/`, `tests/`, CI và script không đổi so với nền `0f45e2d`. Không chạy test/Docker; chỉ ghi file này.

## Đối chiếu

- ✅ Không có bootstrap migration/baseline trong test. Tuy nhiên fixture **có tạo dữ liệu riêng**: `AssetSchemaFixture.cs:59` trồng 2500 cột; `ScopeTestFixture.cs:52` dựng bảng probe. Câu “không fixture nào seed” nên giới hạn thành **không nạp baseline ứng dụng/FO-26**.
- ✅ Rủi ro DB dev có thật: `src/LuxMap.Api/Program.cs:26,77`, `src/LuxMap.Persistence/LuxMapConnectionString.cs:22,36` ưu tiên chuỗi nguyên khối, rồi ghép `POSTGRES_*`, mặc định cổng 5433/DB `luxmap_dev`. “Test trần ghi dev” đúng **khi có `.env` dev hợp lệ**; checkout review không có `.env`, nên không khẳng định chạy trần ở đây sẽ ghi dev.
- ✅ Đếm tĩnh Fact/Theory: API 616, Persistence 22, Shared 170, Storage 27. Bốn collection đúng; 11 factory kế thừa trực tiếp đúng. API có thêm 2 `BrokerFact` và factory gián tiếp bên dưới.
- ✅ Persistence/Shared/Storage không cần DB/Docker. Persistence còn kiểm guard trước DB và dùng interceptor chặn ghi (`tests/LuxMap.Persistence.Tests/AuditGuardTests.cs:100,129`, `SurveyProcessingModelTests.cs:22,92`), không chỉ dựng model. Storage vẫn cần `ffmpeg`/`ffprobe` cho một số test (`tests/LuxMap.Infrastructure.Storage.Tests/SurveyFrameMediaTests.cs:32,41`).
- ✅ `git grep` không thấy test ghim 5433, `luxmap_test`, `luxmap_dev`, `luxmap_postgres`; các mặc định nằm ở resolver/script/compose. MQTT có phụ thuộc compose và cổng riêng.

## Phát hiện cần bổ sung/sửa

1. **P1 — Thiếu cơ chế chờ bootstrap chung cho mọi host.** `tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:59`, `ScopeTestFixture.cs:52` truy cập `Services` ngay trong khởi tạo; `AccountAdminTests.cs:23` và `CorrelationIdTests.cs:6` dùng class fixture ngoài collection tài sản. Chỉ đặt container trong `AssetDatabaseCollection` không bảo đảm các host khác chờ nó. P2 cần một bootstrap dùng chung, khởi tạo đúng một lần, hoàn tất readiness → migrate → seed → mock trước mọi truy vấn; sống tới sau teardown cuối, kể cả lượt lọc một class và host con `WithWebHostBuilder` (`SurveyIngestTests.cs:64`).

2. **P1 — `.env` có thể ghi đè biến fixture; `UseSetting` không thay được resolver.** `src/LuxMap.Api/Program.cs:26,78` nạp env rồi đọc trực tiếp `Environment`, không đọc connection string từ `builder.Configuration`. DotNetEnv 3.2.0 mặc định clobber (`/Users/nhm809/.nuget/packages/dotnetenv/3.2.0/README.md:90,131`). Đặt `POSTGRES_*` trước host sẽ bị `.env` dev ghi đè; đặt chuỗi nguyên khối cũng bị nếu `.env` chứa cùng khóa. Chốt đường override chịu được mọi lần dựng host, và kiểm kết nối thực tế **trước ghi**.

3. **P1 — D-3 thiếu DB/user và guard trước script phá dữ liệu.** `scripts/seed_mock_set.py:277` mặc định container `luxmap_postgres`, DB `luxmap_dev`, user `luxmap`; `:291` dùng cả ba. Chỉ đổi `--container` sẽ lỗi nếu DB/user container khác mặc định, và còn thiếu `--apply`. Script có DELETE toàn bảng (`:46`), nên D-9 không được đợi tới sau seed. Truyền đủ đích đã guard; hoặc dùng stdout SQL sẵn có (`:284`) trên kết nối fixture. Không cần thêm `--emit-sql`.

4. **P2 — Seed password phụ thuộc file, không chỉ biến môi trường.** `tests/LuxMap.Api.Tests/AuthTestExtensions.cs:13` đọc `.env` trực tiếp; `AssetImportFixture.cs:47` gọi helper ngay ở property initializer, trước `InitializeAsync`. Trong khi `src/LuxMap.Modules.Identity/Seeding/SeedCredentials.cs:34` đọc env. D-4 phải bảo đảm hai nguồn khớp, hoặc thống nhất helper; thiếu file sẽ lỗi ngay dù fixture đã đặt đủ `SEED_*`. Host còn cần JWT/MinIO/SMTP config (`src/LuxMap.Api/Program.cs:84`, `src/LuxMap.Modules.Identity/IdentityModule.cs:32,52`).

5. **P2 — D-5 và D-9 chưa tương thích ở chế độ external.** `.ai/results/BE-36-p1.md:139,143`: external DB không do fixture tạo, nên không thể đồng thời thỏa guard ấy. Tên ngẫu nhiên riêng lẻ cũng chưa chứng minh đúng server/cổng. Đề nghị bỏ external khỏi đợt đầu, hoặc đặc tả guard riêng cho DB test được chỉ định, cấm DB dev; áp dụng trước migration, seed, mock và teardown.

6. **P2 — Bỏ bước CI database có thể khiến cả suite bị skip.** `.github/workflows/ci.yml:175` chỉ chạy test khi `steps.database.outcome == 'success'`; D-6 phải sửa điều kiện cùng lúc xóa step. Giữ `.env` config, `LUXMAP_MQTT_E2E=1` (`:181`) và callback/broker ports cùng khóa của EMQX (`docker-compose.yml:102`, `tests/LuxMap.Api.Tests/MqttEndToEndTests.cs:35,39`). Chuyển PostGIS không tự thay được các phụ thuộc này; tránh CORS env toàn job vì test cấu hình thiếu (`CorsTests.cs:66`).

7. **P2 — Khẳng định “S3ObjectStore không có test” sai.** `.ai/results/BE-36-p1.md:13`: `tests/LuxMap.Infrastructure.Storage.Tests/MissingObjectTests.cs:16,26` đã test S3ObjectStore bằng FakeS3, không cần hạ tầng; `tests/LuxMap.Api.Tests/SurveyIngestTests.cs:64` cũng thay store bằng S3ObjectStore dùng client giả. Nợ còn lại là **integration với S3/MinIO thật**, không phải hoàn toàn thiếu test tự động.

8. **P3 — Kiểm kê chưa đủ để chuyển mọi factory.** `tests/LuxMap.Api.Tests/AccountTestFactory.cs:74` có `TightRateLimitAccountTestFactory`, dùng ở `AccountAdminTests.cs:486`: tổng 12 kiểu factory nếu tính gián tiếp. 65 file là số chứa Fact/Theory, thêm `MqttEndToEndTests.cs:61` là 66 file có test. “Mọi WebApplicationFactory cần DB” cũng quá rộng: correlation endpoint không truy vấn DB (`CorrelationIdTests.cs:13`), startup CORS cố ý lỗi trước persistence. Không buộc các test vốn không cần Docker phải khởi động container.

## D-1…D-10

| Item | Đánh giá |
|---|---|
| D-1 | **Đồng ý A**, với bootstrap/guard ở phát hiện 1–2; chỉ thêm package vào project cần DB, giữ test thuần không phụ thuộc Docker. |
| D-2 | **Đồng ý** một container/DB mỗi lượt API testhost; giữ collection, teardown và trả sequence. DB sạch không giải đua trong lượt (`PrefixedIdOverflowTests.cs:51,69`). Giữ image PostGIS/UTC như `docker-compose.yml:12,25`. |
| D-3 | **Đồng ý gọi script, phản đối lệnh thiếu đích**; sửa theo phát hiện 3. stdout đã là chế độ mặc định. |
| D-4 | **Đồng ý seed trong tiến trình**, sau migrate; bổ sung yêu cầu đồng nhất mật khẩu/file/env và cấu hình host ở phát hiện 4. |
| D-5 | **Đồng ý fail rõ khi thiếu Docker; phản đối thêm external ngay** khi chưa chốt guard tương ứng (phát hiện 5). |
| D-6 | **Đồng ý chuyển CI**, sửa điều kiện step và giữ E2E MQTT/cấu hình cần thiết theo phát hiện 6. |
| D-7 | **Đồng ý tách integration S3 thật** để PostGIS không bị chặn; sửa mô tả độ phủ hiện có theo phát hiện 7. |
| D-8 | **Đồng ý giữ**: cùng DB/lượt vẫn chia sẻ dữ liệu; kể cả DB/collection cũng chưa đủ bỏ teardown giữa test/class cùng collection. |
| D-9 | **Đồng ý mục tiêu, phản đối chỉ kiểm tên**; guard toàn đích kết nối trước mọi thao tác ghi, giải quyết external (phát hiện 3,5). |
| D-10 | **Đồng ý việc riêng**, không đụng DB dev trong BE-36. |

⚠️ Không xác minh lại số ca chạy/pass, thời gian, số hàng/sequence sau hai lượt, khả năng pull image hay version NuGet. Đây là dữ liệu runtime được khảo sát báo cáo; review này chỉ xác nhận bằng code. 📌 P1 cần bổ sung các bẫy trên trước khi chốt phương án P2.
