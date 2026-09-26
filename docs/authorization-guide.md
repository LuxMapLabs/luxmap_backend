# Phân quyền và phạm vi địa bàn — hướng dẫn cho người viết endpoint

Đọc file này trước khi thêm bất kỳ endpoint nào từ BE-09 trở đi.
Đặc tả gốc: [`api-contract-v1.1.md`](api-contract-v1.1.md) **§2** (v1.7 — vai trò, ma trận capability, phạm vi địa bàn).

---

## Mặc định là ĐÓNG

Toàn ứng dụng yêu cầu đăng nhập. Endpoint mới **không cần khai gì** để được bảo vệ — nó đã được
bảo vệ sẵn. Muốn mở phải khai tường minh `[AllowAnonymous]`.

Hệ quả cần biết: request chưa đăng nhập tới **route không tồn tại** cũng nhận `401`, không phải
`404`. Đây là chủ ý — người lạ không dò được route nào có thật.

---

## Thêm endpoint mới: bạn phải làm gì

### Trường hợp 1 — entity có cột `commune_id` (Pole, Fault, Fixture, RoadSegment…)

**Một lần cho mỗi entity**, lúc tạo entity ở BE-09:

```csharp
public class Pole : ICommuneScoped        // 1. khai interface
{
    public required string CommuneId { get; set; }
}

public sealed class PoleConfiguration : IEntityTypeConfiguration<Pole>
{
    public void Configure(EntityTypeBuilder<Pole> builder)
    {
        builder.HasCommuneScope();        // 2. đánh dấu
    }
}
```

Quên bước 2 thì **app không khởi động được**, kèm thông báo chỉ đúng tên entity. Không có cách nào
để nó âm thầm chạy tiếp.

**Ở endpoint: không phải làm gì cả.** Truy vấn tự bị giới hạn:

```csharp
var poles = await dbContext.Set<Pole>().Where(p => p.FixtureStatus == FixtureStatus.Dim).ToListAsync();
// → chỉ ra cột trong các xã thuộc claim của người gọi
```

Lookup theo ID cũng vậy, và **tự nhiên ra 404 đúng Contract**:

```csharp
var pole = await dbContext.Set<Pole>().FirstOrDefaultAsync(p => p.PoleId == id);
return pole is null ? NotFound() : Ok(pole);
// → cột ngoài phạm vi trả 404, KHÔNG phải 403, nên không lộ ra là nó tồn tại
```

### Trường hợp 2 — endpoint nhận query param `commune_id`

Đây là **bước tường minh duy nhất** bạn phải nhớ. Query filter không làm được — nó chỉ lọc mất bản
ghi rồi trả `200` với danh sách rỗng, trong khi Contract đòi `403`.

```csharp
public async Task<IActionResult> GetPolesAsync(
    [FromQuery(Name = "commune_id")] string[]? communeId, ...)
{
    var query = dbContext.Set<Pole>().AsNoTracking();

    var narrowed = CommuneFilter.Narrow(scopeAccessor.Scope, communeId);   // ném 403 nếu ngoài phạm vi
    if (narrowed is not null)
    {
        query = query.Where(p => narrowed.Contains(p.CommuneId));
    }
    ...
}
```

`commune_id` là bộ lọc **thu hẹp trong phạm vi được phép**, không phải cách mở rộng phạm vi.
Truyền nhiều giá trị mà chỉ một cái ngoài phạm vi → vẫn `403`.

### Trường hợp 3 — entity suy commune qua nhiều bậc (SurveyFrame, TelemetryReading)

**KHÔNG** khai `ICommuneScoped`. Chốt chặn không đòi hỏi chúng, nghĩa là **bạn không được cơ chế
nào bảo vệ** — phải tự viết join có scope:

```csharp
var frames = dbContext.Set<SurveyFrame>()
    .Where(f => dbContext.Set<Pole>().Any(p => p.PoleId == f.PoleId));
    // Pole đã bị filter → phép Any này chính là ràng buộc phạm vi
```

⚠️ Đây là chỗ dễ rò nhất trong toàn hệ thống. Review kỹ mọi truy vấn loại này.

### Giới hạn theo vai trò — BẮT BUỘC một capability trên mọi endpoint nghiệp vụ

```csharp
[HttpGet("poles")]
[Authorize(Policy = LuxMapPolicies.ReadNetwork)]

[HttpPost("poles")]
[Authorize(Policy = LuxMapPolicies.ManageAssets)]
```

Từ Contract v1.7, mỗi policy là một **capability** liệt kê **tường minh** các vai trò được vào —
**không thứ bậc**. Ma trận ở `LuxMapPolicies.Matrix` (nguồn duy nhất, `AuthorizationSetup` đăng ký
từ đó):

| Capability | Vai trò |
|---|---|
| `ReadNetwork` | superior, manager, field_engineer, system_admin |
| `ReadLuxReadings` | superior, manager, field_engineer, system_admin |
| `ManageAssets` | manager |
| `RecordLuxReading` | field_engineer |
| `ControlLighting` | manager (chưa có endpoint) |
| `ManageUsers` | system_admin (chưa có endpoint) |

Viết endpoint mới:

1. **Đừng để endpoint trần.** Fallback chỉ đòi "đã đăng nhập", tức cho **cả bốn** vai trò — kể cả Cấp
   giám sát chỉ-đọc trên một endpoint ghi. `CapabilityPolicyCoverageTests` đỏ nếu bạn quên.
2. Capability có sẵn khớp thì dùng lại. Chưa có thì **thêm vào `Matrix`** cùng ticket, cập nhật bảng
   Contract §2, **và** hai bảng literal trong `RoleCapabilityMatrixTests` / `CapabilityMatrixTests`
   — cùng một diff.
3. **Không bao giờ để một capability rỗng.** `RequireClaim(role)` không có giá trị nào **nhận mọi vai
   trò**; host từ chối khởi động nếu gặp.
4. Dùng hằng số, đừng gõ chuỗi.

---

## Những chỗ dễ lách — đọc kỹ

| Cách lách | Hậu quả | Phòng bằng gì |
|---|---|---|
| `IgnoreQueryFilters()` | Bỏ qua toàn bộ phạm vi địa bàn | Không có gì chặn. **Cấm dùng** trừ khi có lý do viết rõ ra và được review |
| Quên khai `ICommuneScoped` trên entity mới | Rò toàn bộ, im lặng | Chốt chặn KHÔNG bắt được. Chỉ review mới bắt |
| SQL thô / `FromSqlRaw` | Query filter không áp | Tự viết điều kiện phạm vi |
| Entity nhiều bậc quan hệ | Không có filter | Trường hợp 3 ở trên |
| `Find()` trên entity đang được theo dõi | Trả về từ change tracker, bỏ qua filter | Dùng `FirstOrDefaultAsync` thay vì `Find` |

---

## Hai bẫy kỹ thuật đã xử lý, đừng làm hỏng

**1. Filter phải tham chiếu `LuxMapDbContext`, không được bắt singleton từ ngoài.**
Model của EF dựng một lần rồi cache. Nếu biểu thức filter bắt một `ICommuneScopeAccessor` từ bên
ngoài, EF hằng-số-hoá `IsSystemWide` vào query đã biên dịch, và **mọi người dùng sau đều dùng lại
phạm vi của người dùng đầu tiên**. Đây là lỗi thật đã gặp khi làm BE-08, test bắt được.
Xem `CommuneScopeBuilderExtensions.ApplyFilter`.

**2. `ICommuneScopeAccessor` phải đăng ký singleton, không phải scoped.** Cùng lý do — model giữ
tham chiếu tới instance lúc dựng model. Singleton đọc `IHttpContextAccessor` (AsyncLocal tĩnh) nên
vẫn đúng từng request.

---

## Kiểm tra chéo `["*"]` với vai trò

Claim `commune_ids` mang `"*"` mà vai trò không phải `system_admin` → **403 + log Error**.

Đây **không phải** chống client giả mạo — claim nằm trong JWT đã ký. Đây là lớp chặn **lỗi ở phía
phát token**: BE-06 không có ràng buộc DB nào buộc `has_system_wide_scope` đi cùng
`role = 'system_admin'`, nên một câu `UPDATE` tay hoặc một bug ở BE-33 là đủ để BE-07 phát `["*"]`
cho tài khoản thường. Log ở mức **Error** vì đó là dấu hiệu bug, không phải dấu hiệu bị tấn công.

---

## Mã lỗi

| Tình huống | HTTP | `error.code` |
|---|---|---|
| Thiếu / sai / hết hạn token, sai `iss`, sai `aud` | 401 | `UNAUTHENTICATED` |
| Vai trò không nằm trong capability | 403 | `ROLE_FORBIDDEN` (BE-REVIEW-02 D-4; trước đó là `COMMUNE_FORBIDDEN`) |
| `commune_id` ngoài phạm vi | 403 | `COMMUNE_FORBIDDEN` |
| `["*"]` lệch vai trò | 403 | `COMMUNE_FORBIDDEN` |
| Tài nguyên ngoài phạm vi | 404 | `NOT_FOUND` |

⚠️ `UNAUTHENTICATED` **chưa có trong Contract v1.1**, cần bổ sung ở FW-00.

---

## Tạo tài khoản

**Mô hình chính thức (Contract v1.7, D-R11): chỉ Quản trị hệ thống tạo tài khoản**, gán vai trò và gán
xã. Không có tự đăng ký. API cho việc này là `POST /api/v1/admin/users` (capability `ManageUsers`) ở
**BE-33a** — chưa có.

**Giai đoạn chuyển tiếp, tới BE-33a.** `POST /api/v1/auth/register` còn chạy nhưng **DEPRECATED** — chỉ
dùng như công cụ tạo tài khoản của Quản trị hệ thống, không phải màn tự đăng ký cho người dùng (WP6 bỏ
màn đó). Tài khoản vừa tạo nhận `field_engineer` và **không có xã nào**, nên **đăng nhập được nhưng
không thấy bản ghi nào**. Quản trị hệ thống gán vai trò và xã bằng SQL:

```bash
docker compose exec postgres psql -U luxmap -d luxmap_dev
```

Xem ai đang chờ được gán:

```sql
SELECT u.user_id, u.username, u.role, count(c.commune_id) AS communes
FROM app_user u LEFT JOIN app_user_commune c ON c.user_id = u.user_id
GROUP BY u.user_id, u.username, u.role HAVING count(c.commune_id) = 0 ORDER BY u.user_id;
```

Gán địa bàn:

```sql
INSERT INTO app_user_commune (user_id, commune_id) VALUES ('USR-005', 'COM-001');
```

Đổi vai trò nếu cần:

```sql
UPDATE app_user SET role = 'manager' WHERE user_id = 'USR-005';
-- superior | manager | field_engineer | system_admin (CHECK ck_app_user_role từ chối giá trị khác)
```

Cấp giám sát xem nhiều xã thì **gán nhiều dòng `app_user_commune`** — không có cấp huyện (D-R3).

⚠️ **Đừng bật `has_system_wide_scope` cho tài khoản không phải `system_admin`.** Không có ràng buộc DB nào
chặn, nhưng BE-08 sẽ từ chối mọi request của tài khoản đó và ghi log mức Error.

📌 Người dùng phải **đăng nhập lại** để thấy thay đổi: access token mang claim cũ tới 60 phút.

## Còn nợ

**BE-14 phải đo `EXPLAIN`.** Filter sinh ra `WHERE (@isSystemWide OR commune_id = ANY(@ids))` đi
kèm `ST_Intersects`. Giả thuyết là nó không phá GIST index vì index chính cho `bbox` nằm trên cột
geometry, còn mệnh đề commune chỉ là filter phụ — **nhưng chưa được chứng minh**, vì chưa có bảng
`pole` để đo. Nếu không đạt ngân sách 500ms/2000 cột thì cho nhánh nóng đi qua repository tường
minh thay vì query filter.
