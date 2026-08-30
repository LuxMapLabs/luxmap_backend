# Đọc code LuxMap Backend

Tài liệu này để **đọc hiểu code**, không phải để tra API. Nó đi theo thứ tự nên đọc, giải thích
từng file làm gì và **vì sao nó tồn tại** — phần "vì sao" mới là thứ đọc code không tự suy ra được.

Ba tài liệu, ba mục đích khác nhau:

| File | Dùng khi |
|---|---|
| **`code-walkthrough.md`** ← đang đọc | Muốn hiểu code chạy thế nào |
| [`authorization-guide.md`](authorization-guide.md) | Sắp **viết** endpoint mới |
| [`backend-report.md`](backend-report.md) | Muốn bản tổng kết cho người ngoài review |

---

## 0. Trước khi mở file nào

Ba thứ chi phối gần như mọi dòng code. Không nắm thì nhiều đoạn trông thừa thãi hoặc lằng nhằng vô cớ:

1. **`docs/api-contract-v1.1.md` là hợp đồng đã publish.** Web (WP5) và Android (WP6) đã hardcode
   tên trường và giá trị enum. Rất nhiều chỗ code làm phức tạp hơn mức cần thiết chỉ để **không**
   lệch hợp đồng dù chỉ một ký tự.
2. **Dữ liệu nghiên cứu phải tách bạch từ lúc ingest.** Gộp số liệu giữa nguồn có ground truth thật
   và nguồn gán nhãn cảm quan làm cả đề tài vô nghĩa. Vì thế enum lưu dạng chuỗi đọc được chứ không
   phải số.
3. **Mobile phải chạy trọn ca offline.** Chi phối thiết kế vòng đời token và cơ chế chống trùng.

---

## 1. Thứ tự đọc

Đọc theo thứ tự này, mỗi bước dựa trên bước trước:

```
1. Program.cs                    ← 112 dòng, là bản đồ của toàn bộ hệ thống
2. LuxMap.Shared/                ← từ vựng chung: enum, lỗi, phân trang, ID
3. LuxMap.Persistence/           ← DbContext và các quy ước EF
4. LuxMap.Api/Http/              ← pipeline: correlation id, lỗi, validation
5. LuxMap.Api/Authorization/     ← kiểm token, phạm vi địa bàn
6. LuxMap.Modules.Identity/      ← module domain DUY NHẤT hiện có nội dung
```

Sáu module còn lại (`Assets`, `Survey`, `Faults`, `WorkOrders`, `Telemetry`, `Admin`) mỗi cái đúng
18 dòng khung rỗng. Mở một cái để thấy hình dạng, rồi bỏ qua.

---

## 2. `Program.cs` — bản đồ

Mở [`src/LuxMap.Api/Program.cs`](../src/LuxMap.Api/Program.cs). Nó ngắn có chủ ý: mọi thứ phức tạp
nằm trong các hàm `AddLuxMapXxx` / `UseLuxMapXxx`, nên file này đọc như mục lục.

```
dòng 20   DotNetEnv.Env.TraversePath().Load()   nạp .env → biến môi trường
dòng 23   CreateBootstrapLogger()               logger tạm, phòng khi chết lúc khởi động
dòng 35   ILuxMapModule[] modules = [...]       7 module, liệt kê TƯỜNG MINH
dòng 47   AddLuxMapJsonConventions()            BE-00
dòng 50   AddLuxMapApiConventions()             BE-04
dòng 53   AddLuxMapSwagger()                    BE-05
dòng 56   AddLuxMapAuthorization()              BE-08
dòng 59   AddLuxMapPersistence(...)             BE-03
dòng 64   AddLuxMapModules(...)                 mỗi module tự đăng ký service
dòng 69   if (SeedCommand.IsRequested(args))    `-- --seed` thì seed rồi thoát
```

Rồi tới pipeline — **thứ tự ở đây là logic, không phải ngẫu nhiên**:

```
dòng 79   UseLuxMapCorrelationId()    ①  đẩy CorrelationId vào LogContext
dòng 80   UseLuxMapRequestLogging()   ②  chạy TRONG scope ① nên log mang id
dòng 81   UseLuxMapErrorHandling()    ③  lỗi → response; ② thấy status thật
dòng 88   UseAuthentication()         ④  phải nằm TRONG ③, nếu không 401/403
dòng 89   UseAuthorization()          ④     ra body rỗng
```

Đảo bất kỳ cặp nào cũng hỏng một thứ. Chi tiết ở §5 và §7 dưới đây.

**Vì sao liệt kê module tường minh thay vì quét assembly:** thứ tự đăng ký nhìn thấy được, và thêm
module là sửa đúng một dòng. Quét assembly thì "vì sao module này chạy trước module kia" trở thành
câu không ai trả lời được.

---

## 3. Bản đồ project — ai phụ thuộc ai

```
Modules.{Identity,Assets,Survey,Faults,WorkOrders,Telemetry,Admin}
        │
        ▼
   Persistence  ──►  Shared
        ▲              ▲
        └──── Api ─────┘   (Api tham chiếu tất cả)
```

| Project | Dòng | Vai trò | Ràng buộc quan trọng |
|---|---|---|---|
| `Shared` | 810 | Từ vựng chung của Contract | **KHÔNG phụ thuộc EF Core.** Module nào cũng dùng được mà không kéo theo tầng dữ liệu |
| `Persistence` | 510 | `LuxMapDbContext`, quy ước EF, migration | **Không tham chiếu ngược module nào.** Nhận danh sách assembly từ host rồi quét |
| `Api` | 971 | Host: pipeline, log, Swagger, auth | Chỗ duy nhất biết về HTTP |
| `Modules.Identity` | 1052 | Module domain duy nhất có nội dung | Mẫu để BE-09 nhân bản |

**Chiều mũi tên là điều đáng nhớ nhất.** `Persistence` không biết module nào tồn tại — nó nhận
`ModuleAssemblyCatalog` rồi `ApplyConfigurationsFromAssembly`. Nhờ vậy thêm module không tạo vòng
lặp reference. Xem [`LuxMapDbContext.cs:45`](../src/LuxMap.Persistence/LuxMapDbContext.cs).

---

## 4. Đi theo một request từ đầu đến cuối

Đây là phần đáng đọc nhất. Lấy `POST /api/v1/auth/login`.

```
HTTP request
   │
   ├─ CorrelationIdMiddleware              Api/Http/CorrelationIdMiddleware.cs
   │     đọc header X-Correlation-Id, không có thì sinh GUID
   │     đẩy vào LogContext của Serilog → mọi log sau đều mang id này
   │
   ├─ Serilog request logging              Api/Observability/SerilogSetup.cs
   │     đo thời gian, cuối request ghi một dòng tổng kết
   │
   ├─ ExceptionHandlingMiddleware          Api/Http/ExceptionHandlingMiddleware.cs
   │     try/catch bọc phần còn lại
   │
   ├─ UseStatusCodePages                   Api/Http/ApiConventionsSetup.cs
   │     bọc các status "trần" (401/403/404/405/415) thành { error: {...} }
   │
   ├─ UseAuthentication / UseAuthorization
   │     login có [AllowAnonymous] nên đi thẳng qua
   │
   ├─ MVC binding + validation             Api/Http/ApiConventionsSetup.cs:44
   │     body sai → 400 VALIDATION_FAILED (KHÔNG phải ProblemDetails)
   │
   ├─ AuthController.LoginAsync            Modules.Identity/Auth/AuthController.cs
   │     └─ AuthService.LoginAsync         Modules.Identity/Auth/AuthService.cs:18
   │           ├─ tìm user (lower(username))
   │           ├─ PasswordHasher.VerifyHashedPassword
   │           ├─ kiểm IsLocked  ← SAU khi verify, xem §8
   │           ├─ mở chuỗi refresh token mới
   │           └─ BuildTokensAsync         AuthService.cs:218
   │                 └─ AccessTokenIssuer.Issue → JWT
   │
   └─ response 200 { access_token, refresh_token, token_type, expires_in }
         serialize qua LuxMapJsonOptions → snake_case
```

Nếu ở bất kỳ đâu ném `LuxMapException` thì `ExceptionHandlingMiddleware` bắt và dựng
`{ error: { code, message, details } }`. Nếu ném exception khác → 500 `INTERNAL_ERROR`, chi tiết
chỉ lộ ở Development.

---

## 5. `LuxMap.Shared` — từ vựng chung

Đọc theo thứ tự này:

### `Contracts/Enums/DomainEnums.cs`
12 enum của Contract mục 1. **Không thêm giá trị, không đổi tên** — FE đã hardcode.

Chuỗi trên dây (`lamp_out`, `field_report`…) **không** khai trong file này; nó do
`JsonNamingPolicy.SnakeCaseLower` sinh ra. Test khoá lại từng giá trị một, nên nếu ai đổi tên
member C# thì test đỏ ngay.

### `Serialization/LuxMapJsonOptions.cs`
Nguồn sự thật duy nhất cho JSON. Đọc [`Configure`](../src/LuxMap.Shared/Serialization/LuxMapJsonOptions.cs) — 4 dòng, mỗi dòng một quy ước Contract.

Chỗ dễ bỏ sót ở `JsonConventionExtensions.cs`: nó cấu hình **cả hai** loại `JsonOptions` — của
minimal API và của MVC controller. Hai đường đọc hai object khác nhau; cấu hình một bên thì endpoint
kiểu còn lại âm thầm trả camelCase.

### `Serialization/UtcDateTimeConverter.cs`
Chuẩn hoá thời gian **ở biên serialize**, không vá tại chỗ gọi.

Dòng 40 là chỗ đáng nhìn kỹ:
```csharp
public const string Iso8601Utc = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";
```
`F` viết hoa bỏ số 0 thừa **và bỏ luôn dấu chấm** khi phần thập phân bằng 0. Nên giây tròn in ra
`2026-08-20T04:00:00Z` khớp bộ mock FO-26, mà vẫn không cắt precision dưới giây khi có.

### `Contracts/PrefixedId.cs`
Bảng 16 prefix ID của Contract mục 0.2. Dòng 16 dựng SQL cho `DEFAULT` của cột:
```csharp
"'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')"
```
ID sinh **ở tầng DB**, không phải trong C#. Sequence của PostgreSQL an toàn concurrency sẵn.

### `Contracts/Errors/` và `Contracts/Paging/`
Hình dạng lỗi và phân trang. Nhỏ, đọc 5 phút.

### `Authorization/`
`ICommuneScoped` (entity nào cần lọc theo địa bàn), `CommuneScope` (phạm vi của request hiện tại),
`CommuneFilter` (kiểm tham số `commune_id`). Xem §7.

---

## 6. `LuxMap.Persistence` — tầng dữ liệu

### `LuxMapDbContext.cs` — đọc `OnModelCreating` từ dòng 37

Bốn bước, **thứ tự quan trọng**:

```csharp
dòng 41   HasPostgresExtension("postgis")
dòng 45   ApplyConfigurationsFromAssembly(assembly)   ← nạp cấu hình của từng module
dòng 50   CreatePrefixedIdSequences()                 ← PHẢI sau bước trên
dòng 53   ApplyCommuneScope(this)                     ← PHẢI sau bước trên
```

Bước 3 và 4 quét model **sau khi** đã nạp hết cấu hình, tìm những chỗ đã được đánh dấu rồi xử lý
tập trung. Đây là khuôn lặp lại hai lần trong codebase, nắm được nó là hiểu được cả hai:

> **Khuôn "đánh dấu rồi quét":** cấu hình entity chỉ *đánh dấu ý định* bằng annotation.
> `OnModelCreating` quét model tìm annotation rồi làm việc thật.
> Vì sao: `ApplyConfigurationsFromAssembly` khởi tạo cấu hình bằng constructor rỗng nên **không
> tiêm dependency vào đó được**. Đánh dấu thì không cần dependency; xử lý tập trung thì có.

Áp dụng cho:
- `HasPrefixedId()` → `CreatePrefixedIdSequences()` tạo sequence
- `HasCommuneScope()` → `ApplyCommuneScope()` gắn query filter + kiểm bất biến

### `Conventions/ContractEnum.cs`
Enum lưu xuống DB dạng `text` mang **đúng chuỗi mà API trả ra**, kèm `CHECK` constraint.

Dùng lại chính `JsonNamingPolicy.SnakeCaseLower` của tầng JSON, nên giá trị trong DB và trên dây
không thể lệch nhau.

⚠️ **Đừng dùng `HasConversion<string>()` mặc định của EF** — nó lưu tên C# (`LampOut`) chứ không
phải chuỗi Contract (`lamp_out`).

*Vì sao không dùng `int`:* `fault_type = 2` thì không ai đọc được khi truy vấn tay, mà CV-11,
CV-18, IOT-16 đều phải truy vấn tay theo `data_source`. Nguy hiểm hơn: chèn thêm một member vào
giữa enum C# là toàn bộ dữ liệu cũ sai ý nghĩa mà không báo lỗi gì.

### `LuxMapModelCacheKeyFactory.cs`
Khoá cache model của EF mặc định chỉ gồm **kiểu DbContext**. Nhưng model LuxMap còn phụ thuộc
`ModuleAssemblyCatalog` — danh sách module quyết định entity nào có mặt. File này thêm catalog vào
khoá. Không có nó, hai host trong cùng process với danh sách module khác nhau sẽ dùng chung model
của host dựng trước.

---

## 7. `LuxMap.Api` — pipeline và phân quyền

### `Http/CorrelationIdMiddleware.cs`
Nhận `X-Correlation-Id` từ client hoặc sinh mới. Có sanitize (≤128 ký tự, chỉ `[A-Za-z0-9-_.:]`) —
header của client là dữ liệu ngoài, không cho nó chèn rác vào log.

### `Http/ExceptionHandlingMiddleware.cs`
Hai nhánh `catch`:
- `LuxMapException` → giữ nguyên mã và status của Contract, log **không kèm exception** (lỗi đã
  lường trước, kèm stack trace chỉ tạo rác)
- Exception khác → 500 `INTERNAL_ERROR`, log **có** stack trace, chi tiết chỉ lộ ở Development

### `Http/ApiConventionsSetup.cs`
Hai thứ đáng đọc:

**Dòng 44** — `InvalidModelStateResponseFactory`. Đây là chỗ chặn cái bẫy lớn nhất của
`[ApiController]`: mặc định nó trả RFC 7807 ProblemDetails cho lỗi validation, **không khớp** hình
dạng lỗi của Contract. Không thay factory này thì FE nhận hai hình dạng lỗi khác nhau tuỳ tình huống.

**`HandleBareStatusCodeAsync`** — ASP.NET Core trả **body rỗng** cho 401/403/404/405/415. Hàm này
dựng lại thành `{ error: {...} }`.

### `Authorization/AuthorizationSetup.cs`
Ba khối:

**Kiểm token (dòng ~75-105).** Lấy chính `JwtOptions` mà BE-07 dùng để ký, qua DI — cùng một object
nên không có cơ hội lệch issuer/audience/khoá.

Hai dòng phải nhớ:
```csharp
dòng 80   options.MapInboundClaims = false;         // nếu không, User.FindFirst("sub") LUÔN null
dòng 99   ClockSkew = TimeSpan.FromSeconds(30),     // mặc định .NET là 5 PHÚT
```

`MapInboundClaims = true` (mặc định) đổi `sub` thành
`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`. Không tắt thì bạn sẽ đi tìm
lỗi ở phía phát token hàng giờ.

**Fail đóng (dòng 57).** `SetFallbackPolicy` khiến **toàn ứng dụng** yêu cầu đăng nhập. Endpoint mới
được bảo vệ sẵn; muốn mở phải khai `[AllowAnonymous]`.

Hệ quả: request chưa đăng nhập tới route **không tồn tại** cũng nhận 401 chứ không phải 404 — có
chủ ý, người lạ không dò được route nào có thật.

**Policy theo vai trò.** `LuxMapPolicies.MaintenanceEngineer` v.v.

### `Authorization/CommuneScopeAccessor.cs`
Rút phạm vi địa bàn từ `ClaimsPrincipal`.

⚠️ **Phải đăng ký singleton, không phải scoped.** Model của EF dựng một lần rồi cache, và biểu thức
query filter giữ tham chiếu tới instance lúc dựng model. Scoped thì mọi request sau dùng lại phạm vi
của request đầu tiên. Singleton đọc `IHttpContextAccessor` (AsyncLocal **tĩnh**) nên vẫn đúng từng
request.

Đọc `FromPrincipal` — nó **fail đóng ở mọi nhánh**: chưa xác thực, claim thiếu hẳn, claim rỗng đều
ra `CommuneScope.Empty`. Không nhánh nào hiểu là "không có ràng buộc".

### `Conventions/CommuneScopeBuilderExtensions.cs` (ở Persistence)
Trái tim của phân quyền theo địa bàn.

**Dòng 62** — chốt chặn: entity khai `ICommuneScoped` mà thiếu `HasCommuneScope()` thì **app không
khởi động được**, kèm tên entity cụ thể.

**Dòng 91-96** — bộ lọc:
```csharp
modelBuilder.Entity<TEntity>().HasQueryFilter(candidate =>
    context.CurrentCommuneScope.IsSystemWide
    || context.CurrentCommuneScope.CommuneIds.Contains(candidate.CommuneId));
```

⚠️ **Biểu thức tham chiếu `context`, không phải một singleton bắt từ ngoài.** Đây không phải chuyện
phong cách. Bản đầu tiên bắt `ICommuneScopeAccessor` từ ngoài, EF **hằng-số-hoá** `IsSystemWide` vào
query đã biên dịch, mà query cache theo hình dạng — nên **mọi người dùng sau đều dùng lại phạm vi của
người dùng đầu tiên**. Triệu chứng: admin, agency và crew đều thấy đúng một xã.

**Vì sao lọc nằm trong `WHERE` chứ không kiểm sau khi lấy:** Contract mục 7 đòi tài nguyên ngoài
phạm vi trả **404** chứ không phải 403 — 403 sẽ xác nhận tài nguyên đó tồn tại. Lọc trong `WHERE`
thì bản ghi đơn giản là không tìm thấy, 404 rơi ra tự nhiên. Lấy-rồi-kiểm sẽ tự nhiên ra 403.

---

## 8. `Modules.Identity` — module mẫu

Đây là module domain duy nhất có nội dung. **BE-09 sẽ nhân bản hình dạng này** cho `Assets`,
`Faults`…

```
Entities/          POCO thuần, không attribute EF
Configurations/    IEntityTypeConfiguration — mapping nằm ở đây, không nằm trong entity
Auth/              nghiệp vụ + controller + DTO
Seeding/           dữ liệu nền
IdentityModule.cs  đăng ký service của module
```

### `Auth/AuthService.cs` — phần khó nhất codebase

Đọc theo thứ tự:

**`LoginAsync` (dòng 18).** Một chi tiết dễ tưởng là bug: **kiểm `IsLocked` SAU khi verify mật
khẩu**. Trả 403 trước khi verify sẽ tiết lộ tài khoản đó tồn tại.

**`RefreshAsync` (dòng 59).** Chuỗi kiểm tra theo thứ tự: không tìm thấy → đã thu hồi → hết hạn →
user bị khoá → xoay vòng.

**`RotateAsync` (dòng 124).** Đọc kỹ đoạn này:
```csharp
var claimed = await dbContext.Set<RefreshToken>()
    .Where(token => token.Id == current.Id && token.RevokedAt == null)
    .ExecuteUpdateAsync(...);

if (claimed == 0) { rollback; return Fail(InvalidRefreshToken); }
```
Đây là cách chống hai request refresh đồng thời: **UPDATE có điều kiện rồi đếm số dòng**. Ở READ
COMMITTED, request thứ hai bị chặn ở row lock tới khi request đầu commit, rồi đánh giá lại `WHERE`
trên bản ghi mới và khớp **0 dòng** → trả 401 mà **không đụng gì** tới token request thắng vừa phát.

**`HandleRevokedTokenAsync` (dòng 161).** Cửa sổ ân hạn 30 giây:

| Token bị thu hồi vì | Dùng lại trong 30s | Dùng lại sau 30s |
|---|---|---|
| `rotation` | 401, **không thu hồi gì** | 401 + **thu hồi cả chuỗi** |
| `logout` | 401 | 401 — **không bao giờ** thu hồi chuỗi |

*Vì sao cần ân hạn:* request thua ở `RotateAsync` thấy token cũ đã `revoked` vì `rotation`. Nếu vội
kết luận "bị đánh cắp" rồi thu hồi cả chuỗi thì **giết luôn phiên hợp lệ của request thắng**. Người
dùng bấm hai lần hoặc mạng retry là mất phiên.

**`BuildTokensAsync` (dòng 218).** Đây là chỗ `["*"]` được sinh ra — và nó dựa trên cờ
`user.HasSystemWideScope`, **không phải** trên `role`. Không có ràng buộc DB nào buộc hai thứ đó đi
cùng nhau; đó chính là lý do BE-08 có lớp kiểm chéo.

### `Auth/AccessTokenIssuer.cs`
Một dòng đáng chú ý — dùng `SecurityTokenDescriptor.Claims` với giá trị **mảng**:
```csharp
[AuthClaims.CommuneIds] = communeIds.ToArray(),
```
Nếu dùng nhiều `Claim` trùng tên thay vì mảng, handler chỉ gộp thành mảng **khi có từ hai giá trị**
— một xã sẽ ra chuỗi thay vì mảng và làm hỏng bên đọc.

---

## 9. Những chỗ code trông lạ — và lý do

| Trông như | Thật ra là |
|---|---|
| `CreateLogger` chứ không `CreateBootstrapLogger` | Bootstrap logger là loại reloadable; `UseSerilog` gọi `Freeze()` lên nó mỗi lần dựng host, nên dựng host lần hai trong cùng process (test) sẽ ném lỗi |
| `PageQueryModelBinder` thay vì `[FromQuery]` thường | Nếu tham số action tên là `page`, MVC lấy tên đó làm prefix rồi tìm `page.page_size` — `page_size=500` âm thầm rơi về 50 |
| Bảng migration tên `__ef_migrations_history` | Mặc định EF là `"__EFMigrationsHistory"`, mixed case buộc Postgres quote ở mọi nơi, trái Contract mục 5.1 |
| `HasCommuneScope()` không nhận tham số gì | Khuôn "đánh dấu rồi quét" ở §6 |
| Không có cột `salt` cạnh `password_hash` | `PasswordHasher` nhúng salt sẵn trong chuỗi mã hoá |
| `refresh_token` không có ID dạng `RT-001` | Contract mục 0.2 cố ý bỏ qua — nó không bao giờ lộ ra FE |
| Image Postgres là `imresamu/postgis` | `postgis/postgis` chính thức không có bản arm64 cho PostgreSQL 17, `docker pull` lỗi thẳng trên Mac |

---

## 10. Muốn tìm X thì mở file nào

| Câu hỏi | File |
|---|---|
| Enum này serialize ra chuỗi gì? | `Shared/Contracts/Enums/DomainEnums.cs` + test `DomainEnumSerializationTests` |
| Vì sao JSON ra snake_case? | `Shared/Serialization/LuxMapJsonOptions.cs` |
| ID `POLE-0001` sinh ở đâu? | `Shared/Contracts/PrefixedId.cs` + `Persistence/Conventions/PrefixedIdBuilderExtensions.cs` |
| Hình dạng lỗi định nghĩa ở đâu? | `Shared/Contracts/Errors/ApiErrorResponse.cs` |
| Mã lỗi nào tồn tại? | `Shared/Contracts/Errors/ErrorCodes.cs` |
| 401/403 body dựng ở đâu? | `Api/Http/ApiConventionsSetup.cs` → `HandleBareStatusCodeAsync` |
| Token được ký thế nào? | `Modules.Identity/Auth/AccessTokenIssuer.cs` |
| Token được kiểm thế nào? | `Api/Authorization/AuthorizationSetup.cs` |
| Lọc theo xã xảy ra ở đâu? | `Persistence/Conventions/CommuneScopeBuilderExtensions.cs:91` |
| Bảng nào đang có? | `Persistence/Migrations/*_InitialIdentity.cs` |
| Chạy dự án thế nào? | [`README.md`](../README.md) |

---

## 11. Chạy thử để nhìn thấy code hoạt động

```bash
cp .env.example .env && docker compose up -d
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
dotnet run --project src/LuxMap.Api -- --seed
dotnet run --project src/LuxMap.Api
```

Rồi thử ba lệnh này theo thứ tự — nó cho thấy gần hết cơ chế trong một phút:

```bash
curl -s localhost:5294/api/v1/_khong-ton-tai
```
→ `401 UNAUTHENTICATED` — fail đóng, và body có hình dạng Contract chứ không rỗng.

```bash
curl -s -X POST localhost:5294/api/v1/auth/login -H 'Content-Type: application/json' -d '{"username":"engineer","password":"<xem .env>"}'
```
→ 4 trường snake_case. Dán `access_token` vào [jwt.io](https://jwt.io) để thấy `sub`, `role`,
`commune_ids` — chú ý `commune_ids` là **mảng** dù chỉ có một xã.

```bash
curl -s -X POST localhost:5294/api/v1/auth/login -H 'Content-Type: application/json' -d '{"username":"khong_ton_tai","password":"gi-do"}'
```
→ body **giống hệt** trường hợp sai mật khẩu. Cố ý — tách ra là nói cho kẻ tấn công biết tài khoản
nào tồn tại.

Muốn đọc test để hiểu hành vi mong đợi: `tests/LuxMap.Api.Tests/AuthRotationTests.cs` là file
nhiều thông tin nhất — nó in ra trạng thái thật của bản ghi trong database ở từng bước.
