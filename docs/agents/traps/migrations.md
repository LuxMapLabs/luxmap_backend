# Migration và ràng buộc DB

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi tạo / sửa migration, index, CHECK, cột số thực, ghi hàng loạt. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Quy trình: ĐỌC migration sinh ra TRƯỚC khi apply

**Bắt buộc, không phải khuyến nghị.** Sau `dotnet ef migrations add`, mở file `Up()` và `Down()` ra
đọc **trước** khi `database update`. Kiểm hai câu hỏi:

1. **Có thao tác nào mình không yêu cầu không?** Đặc biệt là `DropIndex`, `DropColumn`,
   `AlterColumn`. Thêm một cột thì migration chỉ được có `AddColumn` (+ index nếu mình khai).
2. **`Down()` có đối xứng với `Up()` không?**

**Đây là một LỚP LỖI, không phải hai sự cố rời rạc.** Cả hai lỗi nặng nhất của repo tới giờ đều
thuộc lớp này — chúng **qua compile, qua toàn bộ test, và chỉ lộ ra khi có người đọc SQL**:

| Lỗi | Qua được gì | Lộ ra khi |
|---|---|---|
| **BE-06** — `LPAD` cắt bớt ID, cột thứ 10000 thành `POLE-1000` | compile ✓ test ✓ | đọc lại biểu thức `DEFAULT` |
| **BE-12a** — EF `DropIndex` ba index `commune_id` khi thêm partial unique index | compile ✓ test ✓ | đọc migration sinh ra |

Không có công cụ nào trong stack bắt được lớp này: analyzer không đọc SQL, test tích hợp chạy trên DB
đã apply nên nó *thấy* lược đồ mới là bình thường, và cả hai lỗi đều **không ném exception** — chúng
làm dữ liệu sai hoặc truy vấn chậm dần. Thứ duy nhất bắt được là mắt người, một lần, trước khi apply.

## Quy ước: PARTIAL INDEX không bao giờ thay thế được index khoá ngoại

**Khi thêm bất kỳ index nào dẫn đầu bằng `commune_id`, phải khai lại `HasIndex("CommuneId")` tường
minh trong cùng cấu hình đó.**

EF Core tạo index cho khoá ngoại bằng **convention**, và convention **bỏ qua** nếu đã có index khác
dẫn đầu bằng cùng cột. Nó **không phân biệt index đầy đủ với partial index** — đó là toàn bộ vấn đề.
Một partial index chỉ phủ tập con hàng thoả `WHERE` của nó; nó **không** phục vụ được truy vấn trên
những hàng còn lại.

Vì sao ở repo này thì nghiêm trọng: **query filter BE-08 đưa `commune_id` vào `WHERE` của MỌI truy
vấn**. Mất index `commune_id` là mất index của toàn hệ thống, không phải của một tính năng.

Gặp thật ở BE-12a: `HasIndex("CommuneId", "ExternalRef")` với filter `external_ref IS NOT NULL` làm
migration **DROP cả ba** `ix_pole_commune_id`, `ix_road_segment_commune_id`, `ix_feeder_commune_id`.
Index còn lại không phủ hàng `external_ref IS NULL` — tức **đa số cột**, những cột nạp từ ảnh công
khai. Bắt được lúc đọc migration sinh ra, **không phải** lúc chạy: không có lỗi, chỉ chậm dần.

**Ticket sắp đụng vào:** **BE-15** và **BE-17** gần như chắc chắn — bất kỳ unique index nào kiểu
`(commune_id, <cái gì đó>)`. Mẫu đúng ở `ExternalRefColumn.HasExternalRef`.

Xem quy tắc quy trình ngay dưới đây — không có `DropIndex` nào được phép xuất hiện trong một
migration mà bạn chỉ định thêm cột.

## Quy ước: `ExecuteUpdate` / `ExecuteDelete` bị CẤM ở compile-time

`CommuneWriteGuard` là override của `SaveChanges` và nó duyệt **ChangeTracker**. `ExecuteUpdate` và
`ExecuteDelete` dịch thẳng ra SQL, **không đi qua ChangeTracker**, nên chúng **vô hiệu hoá hoàn toàn
kiểm phạm vi Contract mục 7** ở đường ghi.

Đây là **lỗ hổng kiến trúc**, cùng họ với lỗ hổng BE-08 (query filter chỉ áp lên đọc): guard được
viết ra để bịt phía ghi, mà một API bulk mở lại đúng cái lỗ đó.

Bốn ký hiệu đã vào `BannedSymbols.txt`, RS0030 = error:
`ExecuteUpdate`, `ExecuteUpdateAsync`, `ExecuteDelete`, `ExecuteDeleteAsync`.

Đường đúng: **nạp hàng rồi `Remove` / sửa**, để guard nhìn thấy. Nếu thao tác thật sự nằm ngoài mọi
phạm vi thì **nói ra**: `EnterUnscopedSystemWriteBackdoor()` kèm `#pragma warning disable RS0030`
giải thích vì sao.

Hai nhóm ngoại lệ đang tồn tại, đều chính đáng và đều đã pragma tường minh:

| Chỗ | Lý do |
|---|---|
| `AuthService` (4 chỗ) | `RefreshToken` **không** `ICommuneScoped` — guard chưa bao giờ áp. Và luồng xoay token của BE-07 **phụ thuộc** vào số dòng mà `UPDATE` có điều kiện trả về để phân xử refresh đồng thời; nạp-rồi-sửa không diễn đạt được điều đó mà không tái tạo race. |
| Teardown của test (13 chỗ) | Xoá hàng loạt là cách duy nhất dọn được dưới scope rỗng. **BE-36 KHÔNG xoá nhu cầu**: cả lượt chạy dùng chung một DB, class sau vẫn thấy dữ liệu class trước. Chỉ gỡ được khi mỗi collection có DB riêng. |

⚠️ **BannedApiAnalyzers có lỗ, đã đo ở BE-10:** nó chỉ khớp khi **mọi tham số được truyền tường
minh**, mà cả hai API bulk đều có `CancellationToken` tuỳ chọn. Nên có thêm `BannedBulkWriteApiTests`
quét văn bản mã nguồn, khẳng định **mọi** lần gọi nằm trong một vùng `#pragma warning disable RS0030`
và **không file nào để vùng đó hở tới cuối file**. Đừng xoá test đó vì tưởng analyzer đã lo.

## Quy ước cho MỌI cột `double precision` đo được

**Cột số thực biểu diễn một đại lượng đo được PHẢI có CHECK loại `NaN` và `±Infinity`.**
`>= 0` là **không đủ**.

**Vì sao:** Postgres xếp `NaN` **LỚN HƠN mọi số thực** — ngược hoàn toàn với IEEE 754, nơi mọi phép
so sánh với `NaN` đều false. Nên `'NaN'::float8 >= 0` là **`true`**, và `'Infinity' >= 0` cũng vậy.
Chỉ `-Infinity` bị `>= 0` chặn.

⚠️ **Idiom `x = x` KHÔNG dùng được ở Postgres.** Trong IEEE 754 thì `NaN <> NaN`, nên `x = x` bắt
được `NaN`. **Postgres coi `NaN` BẰNG chính nó**, nên `x = x` là tautology thật và bắt được **con số
không**. Kiểm: `SELECT 'NaN'::float8 = 'NaN'::float8` → `t`.

Khuôn đúng:

```sql
CHECK (x >= 0 AND x <> 'NaN'::float8 AND x <> 'Infinity'::float8)
```

**Đây là lỗi câm.** Không exception, không log — chỉ là một hàng trông bình thường trong mọi danh
sách, rồi mọi phép trung bình, độ lệch chuẩn và tương quan tính từ nó trả về `NaN`.

**Cả JSON cũng hở, không chỉ DB.** `JsonSerializerDefaults.Web` bật `AllowReadingFromString`, và với
kiểu số thực cờ đó nhận luôn chuỗi `"NaN"` / `"Infinity"`. Đã bịt bằng `FiniteDoubleConverter` ở
`LuxMapJsonOptions` — **đừng gỡ tưởng thừa**, canh bằng `JsonNumberHandlingTests`. Không dùng
`JsonNumberHandling.Strict` vì nó chặn luôn số có nháy kép hợp lệ như `"12.4"`, đổi hình dạng wire
của mọi endpoint để sửa một vấn đề chỉ nằm ở ba literal.

**Enum JSON cũng phải chặn số lúc đọc:** giữ `allowIntegerValues: false` ở `LuxMapJsonOptions` để từ chối
cả `2` lẫn `"2"` (kể cả enum nullable), trả 400 `VALIDATION_FAILED` trước tầng ghi. Canh bằng
`JsonEnumHandlingTests` và `JsonPipelineConventionTests`; query string vẫn đi qua `WireEnum`, không qua converter JSON.

**Ticket sắp tạo cột kiểu này — phải áp quy ước ngay từ migration đầu:**

| Ticket | Cột |
|---|---|
| BE-15 / BE-17 | `normalized_luminance`, `baseline_ratio` |
| BE-33 | `dim_threshold_ratio`, `out_threshold_ratio` |

> 🔴 **`pole_current_status.status_confidence` ĐANG HỞ — chưa sửa, chờ quyết.**
> Nó nhận `NaN`, `Infinity`, **và cả giá trị ngoài `0..1`** (đã chèn thật `42.5` và nó vào). CHECK duy
> nhất trên cột đó là `ck_pole_current_status_confidence_matches_status`, chỉ ràng buộc **NULL hay
> không** so với `fixture_status`, không nói gì về giá trị. XML doc ghi *"0..1"* nhưng **không có
> ràng buộc nào thực thi**. Quyền ghi bảng đó thuộc **BE-15/BE-17** nên bản sửa thuộc về đó.

## `luxmap_format_id` — `CREATE FUNCTION` đứng TRƯỚC mọi migration dùng nó

ID có prefix sinh bằng **sequence PostgreSQL**, format ở tầng DB (biểu thức `DEFAULT` chuẩn ở `CLAUDE.md`, mục Quy ước API). EF map bằng
`.HasDefaultValueSql()` + `.ValueGeneratedOnAdd()`; sequence an toàn concurrency sẵn, không cần bảng counter; chấp nhận khoảng trống trong
dãy số khi insert lỗi.

`luxmap_format_id` được tạo trong `FixPrefixedIdOverflow`, và `DEFAULT` của **6 cột ID tồn tại lúc đó** (`segment_id`, `pole_id`,
`fixture_id`, `feeder_id`, `user_id`, `commune_id`) gọi nó; các bảng sinh sau dùng cùng hàm. Gộp / squash migration thì thứ tự này là
**ràng buộc cứng** — đảo là DB không dựng được. `Down()` drop function đúng chữ ký: `DROP FUNCTION IF EXISTS luxmap_format_id(text, bigint, int)`
(`int`, không phải `integer`). Số spec trong `PrefixedIds` (bảng prefix) lớn hơn 6 và tăng theo entity — đừng "sửa" số 6 của migration cho khớp.
