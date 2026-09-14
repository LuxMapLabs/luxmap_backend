# LuxMap — Backend (WP2)

Modular monolith ASP.NET Core phục vụ Web SPA (WP5), Android native (WP6) và engine CV (WP4).

Nguồn sự thật: [`docs/api-contract-v1.1.md`](docs/api-contract-v1.1.md) → [`docs/tasks-backend.csv`](docs/tasks-backend.csv) → [`CLAUDE.md`](CLAUDE.md).

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

Hiện chưa có endpoint nào — BE-01/BE-00 chỉ dựng khung và quy ước, nên `/` trả 404 là đúng.

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

Seed tạo một xã và bốn tài khoản, mỗi vai trò một tài khoản: `admin`, `agency`, `engineer`,
`crew`. Mật khẩu đọc từ `.env` (`SEED_*_PASSWORD`) — **thiếu biến nào thì seed dừng hẳn** kèm
thông báo, không lặng lẽ đặt mật khẩu mặc định.

Phải `dotnet ef database update` trước; lệnh seed từ chối chạy khi còn migration chưa apply.

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
dotnet build src/LuxMap.Api && Swagger__Enabled=true dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
```

Lệnh này dựng host thật nên cần `.env` (hoặc `POSTGRES_PASSWORD`) như mọi lần chạy khác; không
cần database đang chạy vì chỉ đọc cấu hình chứ không kết nối.

Trên PowerShell, đặt biến trước rồi gọi lệnh:

```bash
$env:Swagger__Enabled="true"; dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
```

## Xác thực

Ba endpoint, đều **không cần** access token:

```bash
POST /api/v1/auth/login     { "username": "...", "password": "..." }
POST /api/v1/auth/refresh   { "refresh_token": "..." }
POST /api/v1/auth/logout    { "refresh_token": "..." }
```

Login và refresh trả đúng bốn trường: `access_token`, `refresh_token`, `token_type`, `expires_in`.
`expires_in` là lifetime của **access** token tính bằng giây. Logout luôn trả `204`, kể cả khi
token đã thu hồi hoặc không tồn tại.

Claim trong access token — **BE-08 so chuỗi chính xác, đừng đổi**:

| Claim | Kiểu | Ví dụ |
|---|---|---|
| `sub` | chuỗi | `USR-001` |
| `role` | **chuỗi**, không phải mảng | `maintenance_engineer` |
| `commune_ids` | **luôn là mảng** | `["COM-001"]` · Quản trị: `["*"]` |
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

👉 **Trước khi viết endpoint mới, đọc [`docs/authorization-guide.md`](docs/authorization-guide.md).**
Nó nói rõ bạn phải làm gì, và những chỗ dễ lách.

Tóm tắt mã lỗi:

| Tình huống | HTTP | `error.code` |
|---|---|---|
| Thiếu / sai / hết hạn token | 401 | `UNAUTHENTICATED` |
| Sai vai trò, hoặc `commune_id` ngoài phạm vi | 403 | `COMMUNE_FORBIDDEN` |
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
