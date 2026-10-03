# LuxMap — Backend (WP2)

Modular monolith ASP.NET Core phục vụ Web SPA (WP5), Android native (WP6) và engine CV (WP4).

Nguồn sự thật: [`docs/api-contract-v1.1.md`](docs/api-contract-v1.1.md) (Contract **v1.7**, bản hợp nhất) → [`docs/tasks-backend.csv`](docs/tasks-backend.csv) → [`CLAUDE.md`](CLAUDE.md). Chỗ lệch mới ghi vào [`docs/contract-drift.md`](docs/contract-drift.md); log cũ ở `docs/archive/`.

📖 **Mới vào dự án?** Đọc [`docs/code-walkthrough.md`](docs/code-walkthrough.md) — hướng dẫn đọc
code theo thứ tự, giải thích từng cơ chế và vì sao nó tồn tại.

## Yêu cầu

- .NET SDK 10.0
- Docker Desktop (cho hạ tầng dev — xem [Chạy môi trường dev](#chạy-môi-trường-dev))

## Build và test

```bash
dotnet build
```

```bash
dotnet test
```

Benchmark bị **loại khỏi lượt chạy mặc định** (`luxmap.runsettings`, lọc `Category!=Benchmark`) —
chúng đo số cho người đọc, không phải test hồi quy, và một phép đo thời gian trên máy dùng chung là
chỗ sinh flake tự nhiên. Chạy có chủ đích:

```bash
dotnet test --settings luxmap.benchmark.runsettings
```

> `--filter "Category=Benchmark"` **không dùng được**: VSTest lấy giao của filter dòng lệnh với
> filter trong runsettings, ra `(Category!=Benchmark)&(Category=Benchmark)` — khớp 0 test. File
> settings thứ hai **thay thế** file mặc định thay vì giao với nó.

```bash
dotnet run --project src/LuxMap.Api
```

`/` không có route — 401 khi chưa đăng nhập, 404 khi đã đăng nhập (mặc định đóng). Endpoint đang chạy: `/api/v1/auth/*`, `/api/v1/assets/*`, `/api/v1/lux-readings*` — xem Contract.

## CI cho PR vào `dev`

[Workflow CI](.github/workflows/ci.yml) build **merge commit** của PR với `dev` bằng .NET 10,
coi warning là lỗi, xuất lại cả hai spec và so với bản commit, lint bằng Redocly rồi chạy toàn bộ
regression test (không benchmark). Mỗi job dùng service containers từ Compose trên VM riêng;
CI tạo `.env` tạm, migrate, seed tài khoản rồi nạp FO-26 trước khi test. Chỉ dựng container rỗng
hoặc chỉ truyền biến môi trường **không đủ**: fixture không tự bootstrap DB, còn
`AuthTestExtensions.SeedPassword` đọc trực tiếp file `.env`. Không dùng DB dev chung cho CI.

**Cần cấu hình trên GitHub:** đặt `Merge result / build, tests, OpenAPI` làm required check của
`dev` và bật **Require branches to be up to date before merging**, hoặc dùng merge queue
(workflow đã nhận `merge_group`). Nếu thiếu ràng buộc này, check xanh cũ vẫn có thể được dùng sau
khi PR khác đổi `dev`, tái diễn lỗi #46/#47. Khi check spec đỏ, chạy lại các lệnh ở mục
[Xuất OpenAPI spec ra file](#xuất-openapi-spec-ra-file) rồi commit cả hai file sinh; không sửa tay.

## Chạy môi trường dev

Cần Docker Desktop đang chạy.

```bash
cp .env.example .env
```

```bash
docker compose up -d
```

Lần đầu sẽ kéo image (~340 MB cho cả bốn) và chạy `initdb`, mất khoảng 30–60 giây. Kiểm tra:

```bash
docker compose ps
```

Ba service `postgres`, `redis` và `minio` phải ở trạng thái `healthy`. Nếu `postgres` còn `starting`, chờ thêm — healthcheck có `start_period` 30 giây.

Sidecar `luxmap_minio_mc` **không** hiện ở đây: nó tạo hai bucket rồi thoát. `docker compose ps -a` sẽ thấy nó ở `Exited (0)` — đó là thành công, không phải crash.

### Khi cache image còn rỗng

Image MinIO kéo từ `quay.io` chứ không phải Docker Hub — Docker Hub đã gỡ `minio/*`, và `docker login` không giúp được gì. `docker-compose.yml` đã pin sẵn registry và digest, nên `docker compose up -d` chạy đúng mà không cần cấu hình thêm.

Nếu `quay.io` cũng không kéo được, nạp từ bản tarball ngoại tuyến (amd64 + arm64, hỏi BE1 xin file):

```bash
docker load -i luxmap-minio-images.tar
```

Kiểm tra file trước khi nạp — `shasum -a 256` (macOS/Linux) hoặc `Get-FileHash` (PowerShell) phải ra:

```
6832673c39f69e84cb1411fe52e462912ffe2affcb5923bfe572cad75f11c03c
```

Nạp xong chạy lại `docker compose up -d`; compose khớp theo digest nên nó dùng luôn image vừa nạp, không kéo mạng.

### Cổng

| Service | Cổng host (mặc định) | Trong container |
|---|---|---|
| PostgreSQL + PostGIS | **5433** | 5432 |
| Redis | **6380** | 6379 |
| MinIO — S3 API | **9000** | 9000 |
| MinIO — web console | **9001** | 9001 |

Postgres và Redis cố ý KHÔNG dùng 5432/6379: máy dev thường đã có bản cài native chiếm sẵn. MinIO giữ nguyên 9000/9001 vì hiếm khi đụng thứ gì.

Cả bốn cổng chỉ bind vào `127.0.0.1`, không phơi ra LAN. Riêng MinIO đó là ràng buộc bảo mật chứ không phải thói quen: BE-11 phục vụ mọi byte ảnh **qua API** để phạm vi xã (Contract mục 7) áp cho ảnh đúng như áp cho hàng dữ liệu. MinIO không biết `commune_id` là gì, nên chạm thẳng vào nó là đi vòng qua trọn bộ lớp kiểm tra đó.

Chuỗi kết nối dev:

```
Host=localhost;Port=5433;Database=luxmap_dev;Username=luxmap;Password=luxmap_local_dev
```

### Đổi cổng khi bị trùng

Sửa `.env` (không sửa `docker-compose.yml`):

```bash
POSTGRES_PORT=15433
REDIS_PORT=16380
```

Rồi `docker compose up -d` lại. Kiểm tra cổng có đang bị chiếm:

```bash
lsof -nP -iTCP:5433 -sTCP:LISTEN
```

### Lệnh hay dùng

```bash
docker compose exec postgres psql -U luxmap -d luxmap_dev
```

```bash
docker compose logs -f postgres
```

```bash
docker compose down
```

### Tạo lại database từ đầu

Init script trong `docker/postgres/init/` chỉ chạy **một lần** lúc named volume còn rỗng. Sửa nó xong thì phải xoá volume, nếu không thay đổi sẽ không có tác dụng:

```bash
docker compose down -v && docker compose up -d
```

`-v` xoá volume `luxmap_postgres_data` — **mất toàn bộ dữ liệu dev**. Không có `-v` thì dữ liệu vẫn còn nguyên qua `down`/`up`/`restart`.

## Database và migration

Cần stack ở [Chạy môi trường dev](#chạy-môi-trường-dev) đang chạy. Connection string dựng từ
chính `.env` mà docker compose dùng, nên không phải khai cổng hay mật khẩu ở hai chỗ.
Đặt `ConnectionStrings__LuxMap` để ghi đè trọn gói (CI, staging).

```bash
dotnet tool install --global dotnet-ef
```

```bash
dotnet ef migrations add <Tên> -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations
```

```bash
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
```

Một `LuxMapDbContext` dùng chung. Entity và `IEntityTypeConfiguration` nằm trong module của
nó; `LuxMapDbContext` quét assembly từng module nên `LuxMap.Persistence` không tham chiếu
ngược lại module nào. Module nào có entity thì tự thêm reference tới `LuxMap.Persistence`.

Quy ước bắt buộc:

| Hạng mục | Quy ước |
|---|---|
| Tên bảng / cột | `snake_case` toàn chữ thường, không quote |
| Bảng lịch sử migration | `__ef_migrations_history` |
| SRID hình học | **4326** — `SpatialConstants.Srid` |
| EPSG:3405 (VN-2000) | Chỉ nội bộ, không bao giờ ra API |
| Enum | Cột `text` mang đúng chuỗi Contract, kèm `CHECK` constraint |
| ID hiển thị | Sequence PostgreSQL, format ở tầng DB — `COM-001`, `POLE-0001` |
| Thời gian | `timestamptz`, luôn `DateTimeKind.Utc` |

Map enum bằng `builder.HasContractEnum(x => x.FaultType)` — hàm này vừa đặt value converter
vừa sinh `CHECK`. Đừng dùng `HasConversion<string>()` mặc định của EF: nó lưu tên C#
(`LampOut`) chứ không phải chuỗi Contract (`lamp_out`).

### ID hiển thị

Cả 16 entity có ID hiển thị (Contract mục 0.2) dùng chung một cơ chế. Trong
`IEntityTypeConfiguration` của entity:

```bash
builder.Property(p => p.PoleId).HasPrefixedId(PrefixedIds.Pole);
```

`PrefixedIds` ở `LuxMap.Shared/Contracts/PrefixedId.cs` khai sẵn đủ 16 dòng của bảng prefix —
đừng gõ lại prefix hay số chữ số bằng tay. **Không cần khai sequence**: `LuxMapDbContext` quét
model tìm mọi cột đã đánh dấu rồi tạo sequence tương ứng, nên migration luôn đủ và không ai quên.

Client KHÔNG tự đặt ID (Contract mục 0.4). Insert bỏ trống cột, DB sinh giá trị.

## Seed dữ liệu nền

```bash
dotnet run --project src/LuxMap.Api -- --seed
```

Chạy lại bao nhiêu lần cũng được, không tạo trùng — mỗi bản ghi nhận diện bằng khoá tự nhiên
(tên xã, username) chứ không phải ID, nên ID vẫn do sequence sinh đúng quy ước.

Seed tạo một xã và bốn tài khoản demo, mỗi vai trò một tài khoản (Contract v1.7 §2):

| Username | Vai trò | Tên hiển thị | Biến mật khẩu |
|---|---|---|---|
| `admin` | `system_admin` (`*`) | Quản trị hệ thống | `SEED_ADMIN_PASSWORD` |
| `agency` | `superior` | Cấp giám sát | `SEED_AGENCY_PASSWORD` |
| `engineer` | `manager` | Quản lý | `SEED_ENGINEER_PASSWORD` |
| `crew` | `field_engineer` | Kỹ sư hiện trường | `SEED_CREW_PASSWORD` |

Username và tên biến **có trước v1.7 và được giữ** — test, `.env` của mọi người và bộ mock
(`assigned_to: USR-004`) đều trỏ vào chúng. Chạy seed lại trên DB cũ sẽ cập nhật tên hiển thị; vai trò
thì migration `RenameUserRolesToRegistrationV12` đổi. Không có tài khoản công dân — công dân báo sự cố qua
QR, không đăng nhập. Mật khẩu đọc từ `.env` (`SEED_*_PASSWORD`) — **thiếu biến nào thì seed dừng hẳn** kèm
thông báo, không lặng lẽ đặt mật khẩu mặc định.

Phải `dotnet ef database update` trước; lệnh seed từ chối chạy khi còn migration chưa apply.

### Nạp bộ mock FO-26

Sau khi seed tài khoản ở trên, nạp 3 tuyến + 103 cột + 103 bóng + 28 sự cố của `mocks/`:

```bash
python3 scripts/seed_mock_set.py --apply
```

Bỏ `--apply` thì nó chỉ in SQL ra màn hình, không đụng database. Chạy lại bao nhiêu lần cũng được:
nó xoá rồi ghi lại trong **một** transaction, và **từ chối chạy nếu `lux_reading` có dữ liệu** vì đó
là ground truth RQ1.

ID giữ đúng của mock (`POLE-0047` là `POLE-0047`), nên demo khớp với những gì FE đã dựng. Không nạp
được qua endpoint import: EF Core không giữ thứ tự dòng khi để database sinh khoá, đo thật thì 102
trên 103 cột rơi vào ID khác. Vì vậy script ghi ID tường minh rồi đẩy sequence qua vùng đã dùng.

⚠️ **Đây là bản tạm, không phải BE-39.** Chưa nạp: `pole_current_status` (quyền ghi thuộc BE-15/BE-17)
và lịch sử sweep (bảng chưa tồn tại). `feeder_id` được nạp bằng **mạch TẠM** — mỗi segment một feeder
`FDR-001..003`, `external_ref = DEMO-SEG-00n` — để 3 thiết bị IoT mock có đèn để nối; đó **không** phải
dữ liệu mạch thật, O-6 thay thế nó.

### Lập danh sách cột từ ảnh khảo sát

Chuyển một thư mục ảnh **sạch** (JPEG gốc, còn EXIF) thành bản nháp `poles.csv` cho endpoint import.
Chỉ dùng thư viện chuẩn của Python, không cần cài gì:

```bash
python3 scripts/photos_to_poles.py <thư-mục-ảnh> --out <thư-mục-kết-quả> \
    --commune-id COM-070 --segment-ref TUYEN-A --ref-prefix LP   # ba tuỳ chọn này có thể bỏ
```

Mỗi ảnh là một cột ứng viên — tool **không tự gộp** (khảo sát thật 28/09 cho thấy cùng cột chụp cách
2,4 s mà hai cột khác nhau chỉ cách 1,6 s); tọa độ và giờ đọc từ EXIF. Kết quả: `poles.csv`
(import được sau khi điền ô bắt buộc còn trống), `observations.csv` (cột `status` để **người** điền —
không import), `review.csv` (ca tool không tự quyết), `rejected.csv` (ảnh bị loại kèm lý do) và
`photos/` (ảnh chép nguyên byte). `--existing <geojson>` loại các nhóm nằm sát cột đã có. Xem ảnh trong `review.csv` xong, nếu hai nhóm là cùng một cột thì chạy lại
cùng thư mục với `--merge P002=P005` (lặp lại được): tọa độ thành trung vị ảnh của cả hai lượt.
`suggested_merges.txt` gợi ý sẵn các cặp ở hai lượt khác nhau (gần nhất trước) — vẫn phải xem ảnh, cặp
xa thường sai. `review.csv` gắn `gps_nhay` khi hai ảnh liền nhau ngụ ý vận tốc vô lý. Ảnh không phải cột (chụp nhầm) thì
`--drop P095` — đừng xoá file, vì xoá làm đổi tên mọi nhóm phía sau.
Ảnh mang toạ độ GPS dùng lại hoặc nhảy vô lý được **nội suy** theo thời gian giữa hai ảnh tốt cùng lượt
(cột `position` của `observations.csv` ghi rõ; tắt bằng `--no-interpolate`). Chi tiết và lý
do các ngưỡng: docstring đầu file. ⚠️ Chép ảnh khỏi điện thoại bằng cáp / AirDrop / tải bản gốc — gửi
qua Zalo hay Messenger làm mất EXIF.

Thư mục ảnh trải trên nhiều tuyến / nhiều xã thì gán **theo từng cột** thay cho `--segment-ref` /
`--commune-id`: `--segments segments.csv` (tuyến gần nhất trong `--review-m`) và `--boundaries
<geojson>` (ranh giới có `properties.commune_id`). Cột không khớp thì để trống và báo `khong_tuyen` /
`ngoai_ranh_gioi` trong `review.csv` — không bao giờ lấy giá trị dự phòng. `--fixture-watt` +
`--fixture-install-date` xuất thêm `fixtures.csv` với giá trị **tạm** giống nhau cho mọi cột (ảnh không
cho biết công suất hay ngày lắp).

## Quy ước lỗi và phân trang

Mọi lỗi — kể cả validation và route không khớp — trả về đúng một hình dạng:

```json
{ "error": { "code": "VALIDATION_FAILED", "message": "...", "details": { "note": ["..."], "correlation_id": "..." } } }
```

Correlation id có ở **mọi** response qua header `X-Correlation-Id`. Client gửi lên thì server
dùng lại, không gửi thì server tự sinh.

Ném `LuxMapException` cho lỗi nghiệp vụ đã biết; middleware dựng body. Mã và HTTP status của
Contract nằm trong `KnownErrors`.

Phân trang: nhận `PageQuery` trong action rồi gọi `ToPageRequest()`, trả `PagedResult<T>`.
`page_size` vượt 200 bị kẹp im lặng về 200 — client phải đọc `page_size` trong response.

## Log và OpenAPI

### Log

Serilog, hai sink: Console (dạng đọc được) và file JSON có cấu trúc tại `<thư mục chạy>/logs/luxmap-<ngày>.log`,
xoay vòng theo ngày, giữ 14 file, tối đa 50 MB mỗi file. `logs/` đã nằm trong `.gitignore`.

Mọi log entry mang `CorrelationId` — `CorrelationIdMiddleware` đẩy vào `LogContext`, không phải
nhét tay ở từng lời gọi. Mỗi request có một dòng tổng kết kèm method, path, status code và thời
gian xử lý; mức log là Information cho 2xx/3xx, Warning cho 4xx, Error cho 5xx.

`SensitivePropertyScrubber` che cứng mọi property có tên chứa `authorization`, `token`,
`password`, `secret`, `apikey`, `connectionstring`, `cookie`. Đây là chặn ở tầng ghi log, không
phải trông chờ mỗi lời gọi tự nhớ. Đừng gỡ nó ra khi thêm log mới.

Chỉnh mức log trong `appsettings.json`, mục `Serilog`.

### Swagger

Bật/tắt qua `Swagger:Enabled` — mặc định **tắt**, chỉ `appsettings.Development.json` bật.

```bash
dotnet run --project src/LuxMap.Api
```

Mở `http://localhost:<cổng>/swagger`. Nút **Authorize** nhận JWT (dán token trần, không kèm
tiền tố `Bearer`). BE-05 mới chỉ khai security scheme cho tài liệu — **chưa validate token**,
việc đó thuộc BE-07.

### Xuất OpenAPI spec ra file

WP6 sinh DTO Kotlin từ [`docs/openapi/luxmap-v1.json`](docs/openapi/luxmap-v1.json) (FM-04).
**Chạy lại lệnh này mỗi khi thêm hoặc sửa endpoint**, rồi commit file kết quả:

```bash
dotnet tool restore
```

```bash
dotnet build src/LuxMap.Api && Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
```

Lệnh này dựng host thật nên cần `.env` (hoặc `POSTGRES_PASSWORD`) như mọi lần chạy khác; không
cần database đang chạy vì chỉ đọc cấu hình chứ không kết nối.

Sau đó sinh lại bản hợp nhất khớp Contract (27 operation từ code + 15 endpoint chưa có code) và lint:

```bash
python3 docs/openapi/tools/gen_consolidated_spec.py && npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
```

`Cors__AllowedOrigins__0` là **bắt buộc** dù việc xuất spec chẳng liên quan gì tới CORS: swagger CLI
dựng host ở môi trường Production, mà ngoài Development thì `CorsSetup` dừng khởi động nếu danh sách
rỗng. Thiếu nó thì lệnh chết với `Cors:AllowedOrigins is empty` — và vì CLI ghi file **sau** khi
dựng host xong, `luxmap-v1.json` vẫn nằm nguyên bản cũ, dễ tưởng là spec không có gì thay đổi.
Giá trị nào cũng được miễn là origin https hợp lệ; nó không đi vào spec.

Trên PowerShell, đặt biến trước rồi gọi lệnh:

```bash
$env:Swagger__Enabled="true"; $env:Cors__AllowedOrigins__0="https://localhost:3000"; dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
```

## Xác thực

Bảy endpoint cấp token **không cần** access token — nhóm mobile (token trong body) và nhóm web
(`/api/v1/auth/web/*`, refresh token chỉ trong cookie `__Secure-luxmap_rt`). Endpoint thứ tám,
`GET /auth/me`, thì **cần**. Đặc tả đầy đủ: Contract mục 4.

🔴 **`POST /auth/register` DEPRECATED (Contract v1.7, D-R11), gỡ ở BE-33a.** Không có tự đăng ký: Quản
trị hệ thống tạo tài khoản, gán vai trò và gán xã. Tới khi BE-33a có `POST /api/v1/admin/users`, làm
theo [`docs/authorization-guide.md`](docs/authorization-guide.md) mục "Tạo tài khoản".

```bash
POST /api/v1/auth/login      { "username": "...", "password": "..." }
POST /api/v1/auth/register   { "username": "...", "email": "...", "full_name": "...", "password": "..." }   # DEPRECATED
POST /api/v1/auth/refresh    { "refresh_token": "..." }
POST /api/v1/auth/logout     { "refresh_token": "..." }
POST /api/v1/auth/web/login  { "username": "...", "password": "...", "remember_me": true }
POST /api/v1/auth/web/refresh   (không body — cookie)
POST /api/v1/auth/web/logout    (không body — cookie)

GET  /api/v1/auth/me         → { user_id, username, email, full_name, role, commune_ids }
```

`/auth/me` đọc từ **database**, không phải từ claim: access token sống 60 phút nên xã vừa được gán
không hiện trong claim cho tới lần đăng nhập sau, và `full_name` với `email` thì token không mang.
FE gọi nó khi vào app và sau khi quản trị đổi quyền, thay vì tự giải mã JWT.

Login và refresh trả đúng bốn trường: `access_token`, `refresh_token`, `token_type`, `expires_in`.
`expires_in` là lifetime của **access** token tính bằng giây. Logout luôn trả `204`, kể cả khi
token đã thu hồi hoặc không tồn tại.

Claim trong access token — **BE-08 so chuỗi chính xác, đừng đổi**:

| Claim | Kiểu | Ví dụ |
|---|---|---|
| `sub` | chuỗi | `USR-001` |
| `role` | **chuỗi**, không phải mảng — `superior` / `manager` / `field_engineer` / `system_admin` | `manager` |
| `commune_ids` | **luôn là mảng** | `["COM-001"]` · Quản trị hệ thống: `["*"]` |
| `iss` / `aud` | chuỗi | `luxmap-api` / `luxmap-clients` |

Vòng đời: access **60 phút**; refresh trượt **30 ngày** mỗi lần xoay vòng, nhưng không bao giờ
vượt trần **90 ngày** kể từ lần đăng nhập đầu của chuỗi đó.

Mỗi lần đăng nhập mở một **chuỗi** riêng, nên thu hồi một chuỗi không đụng phiên trên thiết bị
khác. Dùng lại token vừa xoay vòng trong **30 giây** được coi là retry lành tính (chỉ `401`);
quá 30 giây thì thu hồi cả chuỗi đó. Token đã logout thì dùng lại **không bao giờ** bị coi là
tấn công.

Khoá ký lấy từ `JWT_SIGNING_KEY` trong `.env`. **Thiếu hoặc ngắn hơn 32 byte thì app dừng ngay
lúc khởi động**, không chạy tiếp với giá trị mặc định. Sinh khoá mới:

```bash
openssl rand -base64 48
```

## Phân quyền

**Mặc định toàn ứng dụng là ĐÓNG** — endpoint mới tự động yêu cầu đăng nhập, muốn mở phải khai
`[AllowAnonymous]`. Truy vấn tự bị giới hạn trong các xã thuộc claim của người gọi; quên gắn scope
cho entity mới thì **app không khởi động được**.

**Mọi endpoint nghiệp vụ gắn một capability** của `LuxMapPolicies.Matrix` — mỗi capability liệt kê
đúng các vai trò được vào, không thứ bậc (Contract v1.7 §2). Endpoint quên gắn thì
`CapabilityPolicyCoverageTests` đỏ. Spec công bố capability và vai trò của từng operation ở
`x-luxmap-capability` / `x-luxmap-roles`.

👉 **Trước khi viết endpoint mới, đọc [`docs/authorization-guide.md`](docs/authorization-guide.md).**
Nó nói rõ bạn phải làm gì, và những chỗ dễ lách.

Tóm tắt mã lỗi:

| Tình huống | HTTP | `error.code` |
|---|---|---|
| Thiếu / sai / hết hạn token | 401 | `UNAUTHENTICATED` |
| Vai trò không nằm trong capability của endpoint | 403 | `ROLE_FORBIDDEN` |
| `commune_id` ngoài phạm vi, hoặc claim `["*"]` lệch vai trò | 403 | `COMMUNE_FORBIDDEN` |
| Tài nguyên ngoài phạm vi | 404 | `NOT_FOUND` (không phải 403 — 403 sẽ lộ ra là nó tồn tại) |

## Cấu trúc

| Project | Vai trò |
|---|---|
| `src/LuxMap.Api` | Host, liệt kê module, áp quy ước JSON |
| `src/LuxMap.Shared` | Quy ước contract dùng chung: enum, JSON, lỗi, phân trang, seam module |
| `src/LuxMap.Persistence` | EF Core, Npgsql, NetTopologySuite, `LuxMapDbContext` |
| `src/LuxMap.Modules.Identity` | AppUser, AdministrativeUnit, JWT, phân quyền (BE-06..BE-08) |
| `src/LuxMap.Modules.Assets` | Pole, Fixture, RoadSegment, Feeder, bbox (BE-09..BE-14) |
| `src/LuxMap.Modules.Survey` | SurveySweep, SurveyFrame, Detection, LuxReading (BE-15..BE-17, BE-42) |
| `src/LuxMap.Modules.Faults` | Fault, FaultHistory, luồng trạng thái (BE-18..BE-20, BE-41) |
| `src/LuxMap.Modules.WorkOrders` | WorkOrder, ExternalUnit, RepairEvidence (BE-21..BE-24) |
| `src/LuxMap.Modules.Telemetry` | IotNode, TelemetryReading |
| `src/LuxMap.Modules.Admin` | Danh mục, ngưỡng, model version, dashboard (BE-28..BE-35) |
| `tests/LuxMap.Shared.Tests` | Khoá lại quy ước contract |
| `tests/LuxMap.Persistence.Tests` | Enum lưu xuống DB đúng chuỗi Contract |
| `tests/LuxMap.Api.Tests` | Hình dạng lỗi, correlation id, phân trang qua pipeline thật |

## Thêm một module

1. `dotnet new classlib -o src/LuxMap.Modules.<Tên>` và thêm `FrameworkReference` tới `Microsoft.AspNetCore.App`.
2. Hiện thực `ILuxMapModule` — module tự đăng ký service của mình.
3. Thêm một dòng vào mảng `modules` trong [`Program.cs`](src/LuxMap.Api/Program.cs).

## Quy ước JSON

Mọi thứ đi qua `LuxMapJsonOptions.Configure`. Host gọi `AddLuxMapJsonConventions()` — hàm này
cấu hình **cả** minimal API lẫn MVC controller, vì hai đường đọc hai `JsonOptions` khác nhau.

### Work orders — BE-23

API `/api/v1/work-orders` đã có inspection/repair, giao người, lịch và các action
start/complete/verify/return/cancel. Bề mặt mới là **nền tạm tới FW kế tiếp**, xem
[drift WO-1…WO-11](docs/contract-drift.md#be-23--work-orders-28092026) và
[hướng dẫn phân quyền](docs/authorization-guide.md). POST nay bắt buộc `task_kind`; PATCH chỉ
sửa title/ngày, giao người dùng PUT `/assignee`. Không có evidence trong BE-23.

Seed mock thêm ba WO từ `mocks/mock-work-orders.json` và `mocks/mock-work-order-kinds.csv`,
tra xã bằng `seed_key=study_site`, user theo username. Script từ chối khi có audit work_order;
không xoá audit để nạp lại. Seed thay toàn bộ tài sản/fault/WO trên DB đích khi guard cho qua.
Chạy trên DB test được chỉ định:

```bash
python3 scripts/seed_mock_set.py --database luxmap_test --apply
```

WO-0001/0003 là inspection; WO-0002 repair in_progress, nên ba fault của nó cũng in_progress.
Hình dạng file mock WO giữ nguyên; priority API tính sống từ fault, không lấy số priority WO mock.

### Thiết bị IoT — BE-14b

`GET /api/v1/map/iot-nodes?bbox=…` trả thiết bị ở **tủ điện tổng** (không có IoT trên từng cột). Mỗi thiết
bị điều khiển 0..n feeder qua rơ-le (bảng `feeder_control`); `segment_ids`, `feeder_ids` và
`controller_node_ids` của `/segments` đều **tính lúc đọc**. Hình dạng là nền tạm, xem
[drift "BE-14 / IoT"](docs/contract-drift.md).

`node_status` tính từ `last_report_at` so với ngưỡng im lặng, mặc định **1 giờ**. Đổi bằng
`Iot:OfflineAfter` trong `appsettings.json` hoặc biến môi trường, rồi khởi động lại; giá trị ≤ 0 làm
app từ chối khởi động:

```bash
Iot__OfflineAfter=00:30:00 dotnet run --project src/LuxMap.Api
```

## Nhận phiên khảo sát — BE-15 P2a

API và payload tạm (SELF-SIGNED, chờ FW): [docs/survey-ingest-p2a.md](docs/survey-ingest-p2a.md).
Sidecar `minio-mc` tạo thêm bucket `luxmap-video` cho clip và file thô; cần chạy lại sidecar khi
triển khai cấu hình compose mới. Video proxy multipart qua API, cap **300 MiB/clip** riêng endpoint;
raw JSON/JSONL cap **10 MiB/file**. Không thay giới hạn JPEG, không dùng presigned URL.
P2a nhận/nộp phiên; P2b-1 thêm worker xử lý GPS/lux, mặc định **tắt**.

## Xử lý GPS/lux — BE-15 P2b-1

Sau khi môi trường được migrate `AddSurveyProcessing`, bật bằng `SurveyProcessing:Enabled=true`
(trong JSON config hoặc `SurveyProcessing__Enabled=true`). Worker polling `queued` và lease hết hạn;
`PollSeconds=5`, `LeaseSeconds=120`, `MaxAttempts=3`. Tắt worker là cách rollback ứng dụng mà giữ
nguyên dữ liệu; `Down()` của migration xoá kết quả xử lý, chỉ dùng ở DB thử nghiệm phù hợp.
Test host luôn đặt `SurveyProcessing:Enabled=false`; test tích hợp gọi `ProcessOneAsync` trực tiếp.

Tham số tạm nằm trong `SurveyProcessingOptions`, toàn bộ được chụp vào `settings_snapshot`:

| Tham số | Mặc định | Ý nghĩa |
|---|---:|---|
| `MaximumKmh` / `SpeedWindowSeconds` | 25 / 3 | Chỉ gắn `speed_excess` khi vượt 25 km/h; đi chậm không bị cảnh báo |
| `MaximumAccuracyM` / `GpsGapSeconds` | 15 / 2,5 | Bỏ vị trí GPS kém khỏi phép chiếu dọc tuyến; chỉ mất timestamp đủ lâu mới cắt lượt |
| `RouteCorridorM` / `RouteAmbiguityM` / `RouteExitSeconds` | 25 / 2 / 5 | Chọn tuyến gần nhất; chênh khoảng cách <2 m là mơ hồ; ngoài hành lang liên tục >5 s mới cắt lượt |
| `LuxGapSeconds` | 0,5 | Ngắt khoảng tìm đỉnh lux, không cắt lượt GPS |
| `ClockReceiptBatchWindowMs` / `ClockResidualMs` | 5 / 80 | Gom timestamp nhận BLE gần nhau; ngưỡng residual của anchor sau fit |
| `PeakMinimumProminenceLux` / `PeakNoiseMultiplier` | 0,5 / 6 | Prominence tối thiểu hoặc 6 lần MAD sai phân cục bộ, lấy giá trị lớn hơn |
| `MaximumGpsOffsetSeconds` / `AssociationToleranceSeconds` | 2 / 0,6 | Ước lượng độ trễ chung trước, rồi ghép trong cửa sổ hẹp |
| `MinimumOffsetAnchors` / `AmbiguousPoleDistanceM` | 3 / 3 | Số mốc tối thiểu để hiệu chỉnh; cặp cột gần nhau dùng chung thời điểm, không gán lux riêng |

GPS chỉ định vị trên tuyến gần nhất không mơ hồ; mẫu trôi ngắn vẫn giữ timestamp và gắn
`gps_degraded`, mẫu mơ hồ còn có `route_ambiguous`. Mẫu thuộc rõ tuyến khác kết thúc lượt tuyến cũ.
Nếu ngoài mọi bbox, SQL đo khoảng cách trên tập tuyến hữu hạn của phiếu để giữ chẩn đoán;
chainage ngoài hành lang không tham gia nội suy. Xe đứng yên giữ cả mốc đến và mốc rời đi.
Phép chiếu GPS, cột và hình học lưu trong `gis_snapshot` đọc cùng transaction `RepeatableRead`;
heartbeat chạy trước/sau transaction này.

Heartbeat kiểm token và gia hạn khi còn dưới nửa lease, giữa các bước xử lý; lease đã hết không được
hồi sinh. P2b-2 duy trì heartbeat trong các bước media dài. Lỗi bất ngờ và mất lease có log kèm
sweep/attempt. Kết thúc ghi run/pass/observation và (từ P2b-2) frame/detection/phân loại,
chuyển sweep sang `awaiting_review`, trả `coverage_pct` mức đi ngang và `frame_count` thật.
Chưa công bố trạng thái cột.
Các giá trị trên chưa được hiệu chỉnh bằng chuyến quay thử; `system_setting` để BE-33.


## Frame và CV mô phỏng — BE-15 P2b-2

Cần migration `AddSurveyFrames` và `ffmpeg`/`ffprobe` trong PATH của worker; CI Ubuntu cài package
`ffmpeg` trước test. Không có NuGet mới. Test media tự sinh video, thiếu executable là lỗi, không skip.
Máy đã thử: ffmpeg/ffprobe 8.1.2 trên macOS; đường dẫn, version/build và hash binary cùng
assembly bộ cắt được lưu vào `artifact_version` mỗi run. Chưa ghim bản triển khai production (chờ pilot).

Cấu hình trong `SurveyProcessing:Frames`, được chụp trong `settings_snapshot`:

| Tham số | Mặc định |
|---|---|
| `Detector` | `unconfigured` (run lỗi rõ cho tới khi cấu hình) |
| `FakeManifestPath` | null; khi `Detector=fake` phải trỏ manifest fixture |
| `FfmpegPath` / `FfprobePath` | `ffmpeg` / `ffprobe` |
| `TempRoot` | thư mục `luxmap-frames` dưới temp hệ điều hành |
| `MaximumConcurrentClips` / `TemporaryBytesPerClip` | 1 / 419430400 byte |
| `MaximumFrameBytes` | 16777216 byte |
| `TimeoutSeconds` / `DetectorTimeoutSeconds` | 60 / 30 giây |
| `BeforeSeconds` / `AfterSeconds` / `FramesPerSecond` | 3 / 0,5 / 5 |
| `MinimumConfidence` / `MinimumBoxArea` | 0,7 / 0,0001 diện tích chuẩn hoá |
| `DimThresholdRatio` | 0,80 |

Cắt theo cửa sổ theo thứ tự thời gian, khử trùng PTS ở phần chồng nhau. Mỗi lượt tối đa **64 vế**
(`MaximumSelectionTerms`); cửa sổ lớn chia thêm lượt. Seek lùi 1 giây bằng `-ss`, `-seek_timestamp 1`,
`-noaccurate_seek`, giữ `-copyts` và chọn `eq(pts,...)` theo tick/time_base gốc từ ffprobe.
`select` bỏ phần giải mã từ keyframe trước điểm seek; không suy PTS từ số frame/FPS.
Số lượng và thứ tự JPEG mỗi lượt phải khớp PTS đã chọn. JPEG dùng pixel format full-range `yuvj420p`.
Quota tổng clip + JPEG được kiểm trong lúc cắt và sau khi tiến trình kết thúc (kiểm định kỳ, không phải
hạn mức cứng của hệ điều hành). RAM/channel giữ một frame tại một thời điểm. Timeout huỷ tiến trình con;
`finally` dọn thư mục riêng của lượt cắt. Worker chết cứng có thể để file tạm: vận hành dọn thư mục temp
khi worker đã dừng; không xoá dữ liệu object store để xử lý tình huống này.

Fake chỉ hợp lệ ở `Development`/`Test` và nguồn `simulated`. Manifest là object JSON có key SHA-256 ảnh,
value `{ "Outcome": "success", "Predictions": [...] }`. Mỗi prediction có `ItemNo`, `Label` (`on/off`),
`Confidence`, `X`, `Y`, `Width`, `Height` (top-left, chuẩn hoá theo ảnh **đã xoay**).
Các outcome fixture khác: `error`, `malformed`, `timeout`; `success` với mảng rỗng là **không phát hiện**.
Xem fixture trong `SurveyFrameTests`/`SurveyFrameFixture`; không dùng pole ID làm nhãn model.

ON thiếu baseline/đỉnh riêng, lux gap, `ambiguous_association` hoặc peak dùng chung → normal, ratio null, không tham gia đánh giá dim.
OFF ghép rõ → out; CV mơ hồ/ảnh kém/video gap → unknown. Bằng chứng dự đoán dùng chung giữa hai cột khác nhau được loại trên toàn run,
kể cả khác lượt; cùng một cột được quan sát lại ở lượt khác vẫn hợp lệ. Kết quả tra theo chỉ số lượt,
cột và thời điểm để hai lượt chung điểm quay đầu không ghi đè nhau. Một detection đạt chất lượng mỗi frame cùng phía, nhãn nhất quán được ghép thành một
track dù bbox không giao nhau; nhiều detection cùng frame còn mơ hồ. Frame đại diện theo diện tích bbox × confidence, không theo lux.
Baseline lookup hiện trả null; P2c mới chọn baseline tương thích và chấp nhận/công bố. Bản đồ/status,
luminance history và fault không bị ghi ở bước này. Hợp đồng đồng hồ tạm ở
[`docs/survey-ingest-p2a.md`](docs/survey-ingest-p2a.md).

Rollback vận hành: tắt `SurveyProcessing:Enabled`, giữ dữ liệu. `Down()` mất frame/detection/CV và từ chối
nếu registry còn artifact media/classification (CHECK phiên bản trước không nhận các component mới).
Chỉ thử rollback trên DB thử nghiệm đã teardown; không tự xoá registry bất biến ở production.
