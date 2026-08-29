# LuxMap Backend — Báo cáo hiện trạng để review

**Repo:** `LuxMapLabs/luxmap_backend` · **WP2** · nhánh tích hợp `dev`
**Phạm vi báo cáo:** BE-00 → BE-07 (nền tảng W1 + nhóm Identity)
**Ngày:** 30/08/2026 · **Quy mô:** ~5.760 dòng C# trong 78 file, 13 project (10 src + 3 test), 174 test

> Tài liệu này viết cho người review code. Mọi khẳng định đều đối chiếu code thật trong repo,
> không phải kế hoạch. Phần **§8 Điểm cần soi kỹ** và **§9 Chưa làm** là phần đáng đọc nhất.

---

## 1. Bối cảnh — vì sao một số quyết định trông lạ

Nền tảng GIS + IoT + Computer Vision quản lý chiếu sáng đường nông thôn. Backend phục vụ **đúng
ba consumer**: Web SPA (WP5), Android native (WP6), engine CV (WP4). Không có API công khai cho
người dân, và **không có vai trò Người dân** trong hệ thống.

Ba ràng buộc chi phối gần như mọi quyết định kỹ thuật:

1. **`docs/api-contract-v1.1.md` là hợp đồng đã publish.** FE và mobile đã hardcode enum và tên
   trường. Lệch một ký tự là hỏng phía client. Khi code lệch Contract, quy ước là *theo Contract,
   ghi lại chỗ lệch*, không tự sửa Contract.
2. **Ba nguồn dữ liệu phải tách bạch từ lúc ingest** (ảnh đêm công khai / bộ hiệu chuẩn / IoT mô
   phỏng). Gộp số liệu giữa dữ liệu có ground truth thật và dữ liệu gán nhãn cảm quan làm kết quả
   nghiên cứu vô nghĩa.
3. **Mobile phải chạy trọn ca offline.** Ảnh hưởng trực tiếp tới thiết kế vòng đời token (§7.4).

---

## 2. Đã hoàn thành

| Task | Nội dung | Commit |
|---|---|---|
| BE-01 | Solution modular monolith, 7 module domain + Shared | `7c1fa7e` |
| BE-00 | Quy ước Contract: JSON snake_case, 12 enum, ISO 8601 UTC, hình dạng lỗi/phân trang | `8d2520d` |
| BE-02 | Docker Compose: PostgreSQL 17.6 + PostGIS 3.5.3 + Redis 8.10.1 | `b61d274` |
| BE-03 | EF Core + Npgsql + NetTopologySuite, snake_case, SRID 4326 | `f7ea653` |
| BE-04 | Middleware lỗi, correlation id, phân trang, versioning `/api/v1` | `d60bd06` |
| BE-05 | Serilog + Swagger + security scheme JWT + export OpenAPI | `a3ff311` |
| BE-06 | Entity Identity, initial migration, seed idempotent | `251c0c1` |
| BE-07 | 3 endpoint auth, JWT HS256, xoay vòng refresh token | `3428377` |

**Chưa merge:** BE-07 đang ở nhánh `feat/BE-07-auth-endpoints`.

---

## 3. Cấu trúc project

```
LuxMap.slnx
├── src/LuxMap.Api                 host: pipeline, Serilog, Swagger, seed command
├── src/LuxMap.Shared              quy ước Contract — KHÔNG phụ thuộc EF Core
│   ├── Contracts/Enums            12 enum Contract mục 1 + UserRole
│   ├── Contracts/Errors           ApiErrorResponse, ErrorCodes, KnownErrors
│   ├── Contracts/Paging           PagedResult<T>, PageRequest
│   ├── Contracts/PrefixedId.cs    bảng 16 prefix ID của Contract mục 0.2
│   ├── Http                       PageQuery + model binder, LuxMapException
│   ├── Modularity                 ILuxMapModule + seam đăng ký
│   └── Serialization              LuxMapJsonOptions, UtcDateTimeConverter
├── src/LuxMap.Persistence         LuxMapDbContext, convention EF, migration
├── src/LuxMap.Modules.Identity    ✅ entity + auth (module DUY NHẤT có nội dung)
├── src/LuxMap.Modules.{Assets, Survey, Faults, WorkOrders, Telemetry, Admin}
│                                  ⬜ khung rỗng, chờ BE-09 trở đi
└── tests/{Shared, Persistence, Api}.Tests
```

**Chiều phụ thuộc:** `Modules.* → Persistence → Shared`, và `Api → tất cả`.
Persistence **không** tham chiếu ngược module nào — nó nhận danh sách assembly từ host rồi quét
`IEntityTypeConfiguration`. Nhờ vậy thêm module không tạo vòng lặp reference.

### Seam module

`ILuxMapModule` (`src/LuxMap.Shared/Modularity/ILuxMapModule.cs`) có `RegisterServices` và
`MapEndpoints` (default no-op). Host liệt kê module **tường minh** trong `Program.cs` thay vì quét
assembly — thứ tự đăng ký nhìn thấy được, thêm module sửa đúng một dòng.

`ModuleRegistrationExtensions.AddLuxMapModules` gọi `AddControllers().AddApplicationPart(...)` cho
từng module. **Đây là chi tiết dễ bỏ sót:** controller nằm trong assembly module chứ không phải
assembly host, không nạp ApplicationPart thì MVC không thấy route và trả 404.

---

## 4. Stack và package

| Thành phần | Version | Ghi chú |
|---|---|---|
| .NET | 10.0 | |
| PostgreSQL | 17.6 | image `imresamu/postgis:17-3.5` |
| PostGIS | 3.5.3 | GEOS 3.11.1, PROJ 9.1.1 |
| Redis | 8.10.1 | cache thuần, chưa có code nào dùng |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | + `.NetTopologySuite` 10.0.3 |
| Microsoft.EntityFrameworkCore | 10.0.11 | pin để tránh xung đột với `.Design` |
| EFCore.NamingConventions | 10.0.1 | snake_case |
| Serilog.AspNetCore | 10.0.0 | + Console 6.1.1, File 7.0.0 |
| Swashbuckle.AspNetCore | 10.2.3 | + CLI cùng version (local tool) |
| Asp.Versioning.Mvc | 10.2.1 | + `.ApiExplorer` |
| Microsoft.IdentityModel.JsonWebTokens | 8.15.0 | phát JWT |
| DotNetEnv | 3.2.0 | nạp `.env` cho app |
| xunit | 2.9.3 | + Mvc.Testing 10.0.11, TimeProvider.Testing 10.0.0 |

> **`imresamu/postgis` thay cho `postgis/postgis`:** repo chính thức **không publish bản arm64**
> cho bất kỳ tag PostgreSQL 17 nào — `docker pull` báo lỗi thẳng trên Mac Apple Silicon.
> `imresamu` là maintainer chính của `docker-postgis`, cùng lineage Dockerfile, build đa kiến trúc.
> **BE-36 Testcontainers phải dùng đúng image này**, nếu không test sẽ xanh trên CI Linux và đỏ
> trên mọi máy Mac.

---

## 5. Pipeline HTTP

```csharp
DotNetEnv.Env.TraversePath().Load();      // .env → biến môi trường
SerilogSetup.CreateBootstrapLogger();     // logger tối thiểu cho lỗi khởi động
try {
    builder.UseLuxMapSerilog();
    builder.Services.AddLuxMapJsonConventions();   // BE-00
    builder.Services.AddLuxMapApiConventions();    // BE-04: versioning + validation
    builder.Services.AddLuxMapSwagger();           // BE-05
    builder.Services.AddLuxMapPersistence(...);    // BE-03
    builder.Services.AddLuxMapModules(...);        // mỗi module tự đăng ký

    var app = builder.Build();
    if (SeedCommand.IsRequested(args)) return await SeedCommand.RunAsync(app);

    app.UseLuxMapCorrelationId();     // 1. đẩy CorrelationId vào LogContext
    app.UseLuxMapRequestLogging();    // 2. chạy TRONG scope đó nên log mang id
    app.UseLuxMapErrorHandling();     // 3. lỗi → response, Serilog thấy status thật
    app.UseLuxMapSwagger();
    app.UseHttpsRedirection();
    app.MapControllers();
    app.MapLuxMapModules(modules);
    app.Run();
} catch (Exception ex) when (ex is not HostAbortedException) {
    Log.Fatal(ex, "..."); throw;
} finally { Log.CloseAndFlush(); }
```

**Thứ tự 1→2→3 là bắt buộc.** Đảo lại thì hoặc log mất correlation id, hoặc Serilog tự bắt
exception và log trùng với `ExceptionHandlingMiddleware`.

---

## 6. Các cơ chế then chốt — công nghệ đã dùng

### 6.1 JSON snake_case và enum chuỗi
`src/LuxMap.Shared/Serialization/LuxMapJsonOptions.cs`

`JsonNamingPolicy.SnakeCaseLower` cho property lẫn dictionary key.
`JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)` cho enum → `lamp_out`, `field_report`.

`AddLuxMapJsonConventions()` cấu hình **cả hai** đường: `Http.JsonOptions` (minimal API) **và**
`Mvc.JsonOptions` (controller). Hai đường đọc hai options khác nhau — chỉ cấu hình một bên thì
endpoint kiểu còn lại âm thầm trả camelCase.

### 6.2 Thời gian UTC
`src/LuxMap.Shared/Serialization/UtcDateTimeConverter.cs`

Chuẩn hoá kind **ở biên serialize**, không vá tại chỗ gọi. `Local → ToUniversalTime()`,
`Unspecified → SpecifyKind(Utc)`.

Format `"yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"` — `F` viết hoa bỏ số 0 thừa **và bỏ luôn dấu chấm**
khi phần thập phân bằng 0. Giây tròn in ra `2026-08-20T04:00:00Z` khớp bộ mock FO-26, mà vẫn
không cắt precision dưới giây (quan trọng vì telemetry idempotent theo `(node_id, reading_time)`).

`DateOnly` không cần converter — System.Text.Json đã ra `YYYY-MM-DD`.

### 6.3 Sinh ID có prefix
`src/LuxMap.Shared/Contracts/PrefixedId.cs` + `src/LuxMap.Persistence/Conventions/PrefixedIdBuilderExtensions.cs`

Contract mục 0.4 yêu cầu sequence PostgreSQL, format ngay ở tầng DB. Hiện thực một lần cho cả 16
entity:

```csharp
builder.Property(p => p.PoleId).HasPrefixedId(PrefixedIds.Pole);
// → DEFAULT 'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')
```

**Sequence tự tạo:** extension gắn annotation `LuxMap:PrefixedIdSequence` lên property;
`LuxMapDbContext.OnModelCreating` quét model sau khi áp hết cấu hình rồi `HasSequence<long>` cho
từng cái. Không ai phải nhớ khai sequence, và migration chỉ tạo sequence **thực sự dùng** —
initial migration chỉ có `commune_id_seq` và `user_id_seq`, không thừa 14 cái.

### 6.4 Enum lưu xuống DB
`src/LuxMap.Persistence/Conventions/ContractEnum.cs`

Lưu `text` mang **đúng chuỗi mà API trả ra**, kèm CHECK constraint:

```sql
CHECK (role = ANY (ARRAY['management_agency','maintenance_engineer','field_crew','administrator']))
```

Dùng lại chính `JsonNamingPolicy.SnakeCaseLower` của tầng JSON nên giá trị DB và giá trị trên dây
**không thể lệch nhau** — có test khoá lại từng giá trị của cả 12 enum.

> **Không dùng `HasConversion<string>()` mặc định của EF** — nó lưu tên C# (`LampOut`) chứ không
> phải chuỗi Contract (`lamp_out`).

Lý do chọn `text` thay vì native PG enum hay int: CV-11, CV-18, IOT-16 đều phải truy vấn tay theo
`data_source`; `fault_type = 2` thì không ai đọc được, và chèn thêm member vào giữa enum C# sẽ làm
sai toàn bộ dữ liệu cũ mà không báo lỗi.

Có overload cho enum nullable (CHECK cho phép NULL) — dùng ở `refresh_token.revoked_reason`.

### 6.5 Hình dạng lỗi và correlation id
`src/LuxMap.Api/Http/{CorrelationIdMiddleware,ExceptionHandlingMiddleware,ApiConventionsSetup}.cs`

Mọi lỗi ra đúng một hình dạng `{ error: { code, message, details } }`, kể cả:
- ngoại lệ chưa xử lý → `INTERNAL_ERROR` 500
- `LuxMapException` → giữ nguyên mã và status của Contract
- **lỗi validation** → `VALIDATION_FAILED` 400
- status trần (404/405/415) qua `UseStatusCodePages`

**Bẫy ProblemDetails:** `[ApiController]` mặc định trả RFC 7807 cho lỗi validation, không khớp
Contract. Đã thay `ApiBehaviorOptions.InvalidModelStateResponseFactory`. Tên field trong `details`
đổi sang snake_case cho khớp body client gửi lên. Test khẳng định không còn khoá
`type`/`title`/`status`/`traceId` nào lọt ra.

> Chọn `InvalidModelStateResponseFactory` thay vì `SuppressModelStateInvalidFilter = true`:
> suppress sẽ để request hỏng đi thẳng vào action, phải tự kiểm ModelState ở mọi action.

Correlation id: đọc header `X-Correlation-Id` nếu client gửi, không thì sinh GUID. Trả lại trên
**mọi** response và đưa vào `error.details.correlation_id`. Giá trị client gửi bị sanitize
(≤128 ký tự, chỉ `[A-Za-z0-9-_.:]`) — chặn dùng header làm vector chèn rác vào log.

### 6.6 Phân trang
`src/LuxMap.Shared/Contracts/Paging/` + `src/LuxMap.Shared/Http/PageQuery.cs`

`{page, page_size, total, items[]}`, `page_size` vượt 200 **bị kẹp im lặng** về 200.

**`PageQueryModelBinder` là bắt buộc, đừng thay bằng `[FromQuery]` thường.** Nếu tham số action
tên là `page`, MVC lấy chính tên đó làm prefix (vì query có key `page`) rồi đi tìm
`page.page_size` — `page_size=500` âm thầm rơi về mặc định 50 thay vì bị kẹp về 200. Không lỗi,
không cảnh báo, chỉ sai số. Đây là bug đã gặp thật, test bắt được.

### 6.7 Persistence và không gian
`src/LuxMap.Persistence/PersistenceServiceCollectionExtensions.cs`

`NpgsqlDataSourceBuilder.UseNetTopologySuite()` ở tầng DataSource + `UseSnakeCaseNamingConvention()`.
Đã verify round-trip `Point` và `LineString` qua DB thật với SRID 4326 (entity tạm, đã xoá sau khi
verify để BE-09 có initial migration sạch).

`SpatialConstants.Srid = 4326` cho toàn bộ geometry. `SridVn2000 = 3405` khai sẵn nhưng **chưa có
code chuyển đổi** — Contract cấm trả 3405 ra API.

Bảng lịch sử migration đổi thành `__ef_migrations_history`: mặc định của EF là
`"__EFMigrationsHistory"`, mixed case buộc Postgres quote ở mọi nơi, trái Contract mục 5.1.

### 6.8 OpenAPI
`src/LuxMap.Api/OpenApi/`

`ContractEnumDocumentFilter` bơm **cả 12 enum** vào `components/schemas` kể cả khi chưa DTO nào
tham chiếu — OpenAPI chỉ sinh schema cho kiểu được dùng, mà giai đoạn này chưa có endpoint domain.
Không có filter này thì spec rỗng và FM-04 (WP6 sinh DTO Kotlin) không có gì để sinh.

Ba điểm WP6 phụ thuộc, đều có test khoá:
- enum ra spec dạng **chuỗi** với đúng giá trị Contract, không phải integer
- `DateTime` → `format: date-time`, `DateOnly` → `format: date` (sai chỗ này thì Kotlin sinh
  `Instant` thay cho `LocalDate`)
- tên property snake_case

`Asp.Versioning.Mvc.ApiExplorer` với `SubstituteApiVersionInUrl = true` — thiếu nó thì spec ghi
`/api/v{version}/...` thay vì `/api/v1/...`.

Export: `dotnet swagger tofile` (local tool manifest), kết quả commit ở `docs/openapi/luxmap-v1.json`.

### 6.9 Log
`src/LuxMap.Api/Observability/`

Console đọc được + file JSON có cấu trúc (`CompactJsonFormatter`), xoay theo ngày, giữ 14 file.
Mỗi request một dòng tổng kết: method, path, status, thời gian. Information cho 2xx, Warning cho
4xx, Error cho 5xx.

`SensitivePropertyScrubber` (`ILogEventEnricher`) che cứng property có tên chứa `authorization`,
`token`, `password`, `secret`, `apikey`, `connectionstring`, `cookie` — chặn ở **tầng ghi log**,
không trông chờ mỗi lời gọi tự nhớ. Kèm override mức log EF Core xuống Warning để SQL và tham số
không rơi vào log.

> Dùng `CreateLogger` chứ **không** `CreateBootstrapLogger`: bootstrap logger là loại reloadable
> và `UseSerilog` gọi `Freeze()` lên nó mỗi lần dựng host, nên dựng host lần thứ hai trong cùng
> process — đúng điều `WebApplicationFactory` làm khi chạy test — sẽ ném
> `The logger is already frozen`.

---

## 7. Xác thực (BE-07) — phần phức tạp nhất

### 7.1 Endpoint

```
POST /api/v1/auth/login     { username, password }
POST /api/v1/auth/refresh   { refresh_token }
POST /api/v1/auth/logout    { refresh_token }
```

Cả ba **không cần** access token. **Không có endpoint đăng ký** — cả 4 vai trò là cán bộ nội bộ,
tạo tài khoản chuyển sang BE-33.

Response login/refresh đúng **bốn** trường: `access_token`, `refresh_token`, `token_type`,
`expires_in` (giây, lifetime của **access** token).

### 7.2 Mật khẩu
`PasswordHasher<AppUser>` của ASP.NET Core (PBKDF2-HMAC-SHA512, 210k vòng, `pbkdf2-aspnetcore-v3`).
Salt **nhúng sẵn** trong chuỗi mã hoá nên **không có cột salt riêng**. Cột `password_algorithm` cho
phép đổi thuật toán về sau mà không đụng schema.

### 7.3 JWT
HS256, khoá từ `JWT_SIGNING_KEY` trong `.env`. **Thiếu hoặc ngắn hơn 32 byte thì app dừng ngay lúc
khởi động** (`JwtOptions.Validate()`, có test khẳng định).

Claim — **BE-08 sẽ so chuỗi chính xác:**

| Claim | Kiểu | Ví dụ |
|---|---|---|
| `sub` | chuỗi | `USR-001` |
| `role` | **chuỗi đơn** | `maintenance_engineer` |
| `commune_ids` | **luôn là mảng** | `["COM-001"]` · Quản trị `["*"]` |
| `iss` / `aud` | chuỗi | `luxmap-api` / `luxmap-clients` |

> **Bẫy đã xử lý:** dùng `SecurityTokenDescriptor.Claims` với giá trị **mảng** thay vì nhiều
> `Claim` trùng tên. Handler gộp claim trùng tên thành mảng **chỉ khi có từ hai giá trị** — một xã
> sẽ ra chuỗi thay vì mảng và làm hỏng bên đọc.

### 7.4 Vòng đời token

- Access **60 phút**
- Refresh **trượt 30 ngày** mỗi lần xoay vòng
- Trần tuyệt đối **90 ngày** kể từ lần đăng nhập đầu của chuỗi

```
chain_absolute_expiry = thời điểm đăng nhập đầu + 90 ngày   (CỐ ĐỊNH)
expires_at(token mới) = min(now + 30 ngày, chain_absolute_expiry)
```

Xoay vòng **không bao giờ** đẩy `chain_absolute_expiry` ra xa; mọi token trong chuỗi kế thừa đúng
giá trị đó.

Lý do 30 ngày: FM-20 yêu cầu app chạy trọn ca offline. Tổ khảo sát đi đêm mất mạng nhiều giờ —
access token hết hạn nhưng lúc offline không có lời gọi server nào; có sóng lại thì refresh rồi
mới push (BE-43).

### 7.5 Refresh token
32 byte từ `RandomNumberGenerator`, **chỉ lưu SHA-256**, unique index trên cột hash.

> Không dùng PBKDF2 cho refresh token: token đã là chuỗi ngẫu nhiên entropy cao nên băm chậm không
> thêm an toàn, chỉ làm mỗi lần refresh chậm đi. Quan trọng hơn: salt ngẫu nhiên sẽ làm unique
> index vô dụng vì phải quét và verify từng hàng.

### 7.6 Xoay vòng và chống đồng thời ⚠️ *phần cần soi kỹ nhất*
`src/LuxMap.Modules.Identity/Auth/AuthService.cs` — `RotateAsync`

Toàn bộ trong **một transaction**:

```csharp
var claimed = await dbContext.Set<RefreshToken>()
    .Where(token => token.Id == current.Id && token.RevokedAt == null)
    .ExecuteUpdateAsync(s => s
        .SetProperty(t => t.RevokedAt, now)
        .SetProperty(t => t.RevokedReason, RefreshTokenRevocationReason.Rotation), ct);

if (claimed == 0) { await transaction.RollbackAsync(ct); return Fail(InvalidRefreshToken); }
```

**Cơ chế:** UPDATE có điều kiện rồi đếm số dòng. Ở READ COMMITTED, request thứ hai bị **chặn ở row
lock** tới khi request đầu commit, rồi đánh giá lại `WHERE` trên bản ghi **mới** và khớp **0 dòng**
→ rollback, trả 401. Không cần cột version, không lock thủ công.

**Điểm mấu chốt:** request thua **không đụng gì** tới token mà request thắng vừa phát. Đã verify
bằng test query thẳng DB: sau hai request đồng thời, chuỗi còn **đúng 1 token sống** và đó là
token của request thắng.

### 7.7 Phát hiện dùng lại token

| Tình huống | Hành vi |
|---|---|
| Token thu hồi do `rotation`, **trong 30 giây** | 401, **không thu hồi gì** — retry lành tính |
| Token thu hồi do `rotation`, **quá 30 giây** | 401 + **thu hồi cả chuỗi** (`reuse_detected`) |
| Token thu hồi do `logout` | 401, **không bao giờ** thu hồi chuỗi, bất kể bao lâu |
| Token thu hồi do `reuse_detected` | 401, không làm gì thêm (chuỗi đã chết) |

**Vì sao cần cửa sổ ân hạn:** request thua ở §7.6 thấy token cũ đã `revoked` với lý do `rotation`.
Nếu vội kết luận "bị đánh cắp" rồi thu hồi cả chuỗi thì **giết luôn session hợp lệ của request
thắng** — người dùng bấm hai lần hoặc mạng retry là mất phiên. Cùng tinh thần với `client_op_id`
trong Contract: retry ở vùng sóng yếu là hoạt động bình thường.

**Thu hồi chuỗi chỉ đụng token còn sống** (`WHERE chain_id = @x AND revoked_at IS NULL`). Token đã
xoay vòng giữ nguyên `reason = rotation` để không mất dấu vết kiểm toán. Mỗi lần đăng nhập mở một
`chain_id` riêng nên phiên trên thiết bị khác **không bị ảnh hưởng** — đã verify bằng test.

### 7.8 Rò rỉ thông tin
Sai tài khoản và sai mật khẩu trả body **giống hệt nhau** (`INVALID_CREDENTIALS`), `details` không
trỏ vào field cụ thể. Kiểm khoá tài khoản **sau** khi xác minh mật khẩu — trả 403 trước đó sẽ tiết
lộ tài khoản tồn tại. Tài khoản khoá bị chặn ở **cả login lẫn refresh**; logout vẫn cho phép.

---

## 8. Điểm cần soi kỹ khi review

1. **`RotateAsync` — giả định về mức cô lập transaction.** Cơ chế chống đồng thời dựa vào hành vi
   của READ COMMITTED trong PostgreSQL (UPDATE bị chặn rồi đánh giá lại điều kiện). Nếu ai đó đổi
   isolation level hoặc chuyển sang DB khác, bảo đảm này mất mà **không có test nào đỏ ngay** —
   test đồng thời hiện tại chạy trên PostgreSQL thật.

2. **`HandleRevokedTokenAsync` chạy NGOÀI transaction.** Việc thu hồi chuỗi khi phát hiện reuse
   không nằm trong transaction nào. Hai request reuse đồng thời có thể cùng chạy UPDATE thu hồi
   chuỗi — kết quả cuối vẫn đúng (idempotent) nhưng đáng xác nhận lại.

3. **`commune_ids` lấy từ bảng nối mỗi lần phát token.** Sửa phân công xã của user **không** làm
   access token đang lưu hành mất hiệu lực — nó vẫn mang claim cũ tới 60 phút. BE-08 cần biết điều
   này.

4. **`AuthService` gọi `SaveChangesAsync` nhiều lần** trong `RotateAsync` và `IdentitySeeder`.
   Trong transaction thì an toàn, nhưng đáng soi xem có chỗ nào ngoài transaction không —
   PostgreSQL abort cả transaction khi một statement lỗi.

5. **Test tích hợp chạy trên database dev thật**, không có isolation giữa các test. Chúng tạo
   `refresh_token` thật và không dọn. BE-36 (Testcontainers) sẽ xử lý; hiện tại chạy test nhiều
   lần sẽ tích luỹ rác trong bảng.

6. **`ExceptionHandlingMiddleware` log 401 kèm stack trace.** `LogWarning(exception, ...)` nên mỗi
   401 auth ghi cả stack trace vào file log. Không rò credential nhưng rất ồn khi FM-05 test.

7. **Timing attack — đã nhận diện, chưa xử lý.** Username sai thì trả 401 ngay; username đúng thì
   chạy PBKDF2 rồi mới trả 401. Chênh lệch thời gian để lộ tài khoản nào tồn tại. Cách chuẩn là
   luôn verify với một hash giả.

8. **`Program.cs` gọi `LuxMapConnectionString.FromEnvironment()` ở thời điểm đăng ký DI**, nên
   thiếu `POSTGRES_PASSWORD` là app không khởi động được — kể cả khi chỉ muốn export OpenAPI.

---

## 9. Chưa làm / còn nợ

### 9.1 Lệch với Contract — cần nêu ở FW-00

| Chỗ lệch | Chi tiết |
|---|---|
| Nhóm `/auth` chưa có trong Contract | 3 endpoint, hình dạng response, 3 claim |
| 5 mã lỗi chưa có trong Contract | `VALIDATION_FAILED`, `INTERNAL_ERROR`, `INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `INVALID_REFRESH_TOKEN` |
| 4 giá trị `UserRole` chưa có trong Contract | `management_agency`, `maintenance_engineer`, `field_crew`, `administrator` — sẽ nằm trong claim JWT nên FE/mobile sẽ hardcode |
| Correlation id | Nằm ở cả header lẫn `error.details`, khác quyết định ban đầu (chỉ header) |
| Bỏ endpoint đăng ký | `tasks-backend.csv` dòng BE-07 vẫn ghi "API đăng ký" |

### 9.2 Mock FO-26 lệch bảng prefix Contract mục 0.2 — sẽ chặn BE-39

| Trong mock | Contract quy định |
|---|---|
| `USR-khang` | `USR-001` (3 chữ số) — có ở `assigned_to` của `mock-work-orders.json` |
| `CLU-0001` | `CLS-001` |
| `NODE-0020` | `NODE-001` (3 chữ số) |
| `FRM-88213` | `FRM-000001` (6 chữ số) |
| `SWEEP-2026` | `SWP-001` |
| `SUP-004` | không có trong bảng prefix |

### 9.3 Chức năng chưa có

- **Không có `AddAuthentication`/`AddJwtBearer`** — BE-07 chỉ *phát* token. Hiện **chưa endpoint
  nào yêu cầu đăng nhập**. Kiểm token và lọc theo địa bàn là BE-08.
- **Redis chạy nhưng chưa có code nào dùng.**
- **6/7 module domain là khung rỗng** — không entity, không endpoint. Từ BE-09.
- **Chưa có CI.** Không có gì tự động phát hiện `docs/openapi/luxmap-v1.json` đã cũ khi thêm
  endpoint mà quên chạy lại lệnh export.
- **Chưa có key rotation cho JWT.**
- **`AdministrativeUnit` chưa có cột ranh giới polygon** — quyết định có chủ ý: phân quyền dùng
  claim `commune_ids` khớp cột `commune_id` chứ không dùng phép chứa không gian, và Nhánh C không
  có nguồn ranh giới thật.

### 9.4 Mốc sắp tới dễ trượt

**BE-42 `LuxReading` hạn W4** — FO-14 đo lux ở W5, FM-14 và CV-12 phụ thuộc. Nằm ngoài dãy số
BE-08 → BE-09 nên rất dễ bị bỏ quên khi làm tuần tự.

---

## 10. Database

| Bảng | Cột | Index |
|---|---|---|
| `administrative_unit` | `commune_id` (PK, `COM-xxx`), `name`, `created_at`, `updated_at` | PK + unique `name` |
| `app_user` | `user_id` (PK, `USR-xxx`), `username`, `email`, `full_name`, `password_hash`, `password_algorithm`, `role`, `is_locked`, `has_system_wide_scope`, `created_at`, `updated_at` | PK + unique `username` + unique `email` + CHECK `role` |
| `app_user_commune` | `user_id`, `commune_id`, `assigned_at` | PK ghép + index `commune_id` |
| `refresh_token` | `id` (bigint identity), `user_id`, `chain_id`, `token_hash`, `expires_at`, `chain_absolute_expiry`, `revoked_at`, `revoked_reason`, `replaced_by_token_id`, `created_at` | PK + unique `token_hash` + index `expires_at`, `chain_id`, `user_id`, `replaced_by_token_id` + CHECK `revoked_reason` |

**Migration:** `InitialIdentity` → `AddRefreshTokenChainTracking`.
Toàn bộ tên bảng và cột snake_case chữ thường, mọi timestamp là `timestamptz`.

**Seed** (`dotnet run --project src/LuxMap.Api -- --seed`): idempotent, nhận diện bằng khoá tự
nhiên (tên xã, username) chứ không cấy ID cứng nên ID vẫn do sequence sinh. Tạo 1 xã + 4 tài khoản
(mỗi vai trò một cái). Mật khẩu đọc từ `.env`; thiếu biến thì **dừng hẳn** kèm thông báo.

---

## 11. Test

| Project | Số test | Phủ gì |
|---|---|---|
| `LuxMap.Shared.Tests` | 100 | Từng giá trị của 12 enum, snake_case, UTC/ISO 8601, hình dạng lỗi và phân trang, 16 prefix ID, `UserRole`, seam module |
| `LuxMap.Persistence.Tests` | 15 | Giá trị enum trong DB **khớp tuyệt đối** giá trị JSON |
| `LuxMap.Api.Tests` | 59 | Pipeline thật qua `WebApplicationFactory`: hình dạng lỗi, correlation id, phân trang, spec OpenAPI, che dữ liệu nhạy cảm, **toàn bộ luồng auth** |
| **Tổng** | **174** | tất cả xanh, build 0 warning |

Test auth dùng `FakeTimeProvider` để tua qua cửa sổ ân hạn 30 giây và mốc 90 ngày mà không phải
chờ thật, và **query thẳng database** để kiểm trạng thái bản ghi (thu hồi lúc nào, lý do, token
thay thế, các token khác trong chuỗi) thay vì chỉ tin mã trả về của service.

---

## 12. Chạy thử

```bash
cp .env.example .env          # rồi đặt JWT_SIGNING_KEY và các SEED_*_PASSWORD
docker compose up -d
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
dotnet run --project src/LuxMap.Api -- --seed
dotnet run --project src/LuxMap.Api
```

Swagger ở `/swagger` (chỉ bật khi `Swagger:Enabled = true`, mặc định chỉ Development).
Cổng mặc định: PostgreSQL **5433**, Redis **6380** (tránh đụng bản cài native trên máy dev), cả hai
chỉ bind `127.0.0.1`.

Xem thêm `README.md` cho chi tiết từng phần.
