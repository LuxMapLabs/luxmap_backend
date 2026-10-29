# Phạm vi xã — chi tiết

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi sửa entity mang `commune_id`, query filter, `CommuneWriteGuard`, FK cascade, backdoor. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

**ACCT-1 (29/10/2026):** mỗi tài khoản thường chỉ có một xã; admin dùng `*`, không gán xã. Giữ `CommuneScope.CommuneIds` dạng danh sách cho guard/filter và token cũ; không rút về giá trị đơn.

## Hai quy tắc chốt ở BE-09 — entity và quyền ghi

**1. Khi nào denormalize `commune_id` và implement `ICommuneScoped`.**

> Entity nào **có thể là gốc của một truy vấn** thì mang `commune_id` và implement `ICommuneScoped`.
> Entity nào **bao giờ cũng đi tới qua một gốc đã scope** thì không.

`Pole` có `commune_id` sẵn. `Fixture` và `PoleCurrentStatus` được **denormalize thêm** — không phải
vì tiện, mà vì cả hai rất dễ trở thành gốc truy vấn: một dashboard viết
`context.Set<PoleCurrentStatus>().GroupBy(...)` là rò dữ liệu ngay. Có `ICommuneScoped` thì chốt chặn
lúc dựng model bắt được; dựa vào "nhớ join" thì chỉ code review bắt được.

`SurveyFrame` và `TelemetryReading` thì **không** — chúng luôn đi qua sweep hoặc node.

⚠️ Chốt chặn chỉ thấy entity **đã** implement interface. Entity có `commune_id` mà quên implement sẽ
lọt — đó là giới hạn thật.

**1c. `HasQueryFilter` chỉ áp lên ĐỌC. Ghi được canh bởi guard riêng ở `SaveChanges`.**

Đây là chỗ BE-08 từng thủng, và nó thủng **âm thầm**. `HasQueryFilter` chèn `commune_id` vào `WHERE`
của truy vấn — nó không tham gia `Add()`, `Update()`, `Remove()`. Trước hotfix này, một kỹ sư chỉ có
`COM-001` gửi `commune_id: "COM-002"` sẽ **ghi thẳng vào DB**. Khoá ngoại không cứu: nó chứng minh xã
đó **tồn tại**, không nói gì về việc ai được ghi. Rồi chính query filter làm hàng vừa tạo **vô hình với
người tạo ra nó** — không exception, không log, dữ liệu biến mất.

**Hai lớp, hai việc khác nhau — cần cả hai:**

| Lớp | Ở đâu | Cho cái gì |
|---|---|---|
| `CommuneFilter.Narrow` | Entry point của controller, gọi tường minh | 403 với thông điệp tử tế, nêu đúng `commune_id` bị từ chối. Người dùng hiểu vì sao. |
| `CommuneWriteGuard` ở `SaveChanges` | `LuxMapDbContext`, tự động | **Backstop không thể quên.** Phủ entity `ICommuneScoped` — **trừ một ca, xem ngay dưới**. |

> Ngoại lệ `Feeder.CommuneId` là key property: xem `docs/agents/traps/assets.md`, mục O-7 (EF từ chối trước guard; test `Changing_a_tracked_feeders_own_commune_is_refused_by_EF_before_the_guard_sees_it`).

Cùng nguyên lý `ValidateCommuneReferences()`: biến quy ước-phải-nhớ thành ràng buộc-không-thể-quên.
Không dùng `ActionFilter` — attribute phải gắn, mà cái bị quên chính là cái rò.

Guard kiểm `Added`, `Modified` (**cả `OriginalValues` LẪN `CurrentValues`** — đổi commune ra ngoài là
cho đi tài sản, đổi từ ngoài vào trong là chiếm tài sản, cùng một lỗi) và `Deleted` (trên
`OriginalValues`).

> 🔴 **ĐIỀU KIỆN PHẢI KIỂM LẠI — không phải ghi chú.**
>
> **Guard `SaveChanges` không thấy cascade do DB thực hiện.** `ChangeTracker` chỉ biết những gì EF
> theo dõi; `ON DELETE CASCADE` chạy trong Postgres, sau khi guard đã xong.
>
> **Cascade của dữ liệu theo xã** (`fixture`, `pole_current_status` từ `pole`) an toàn vì cùng xã với cha.
> `account_token` và `sync_operation` là ngoại lệ không thuộc xã; không dùng chúng để suy rộng cho dữ liệu theo xã.
>
> **Bất kỳ FK cascade MỚI nào giữa hai bảng có thể khác commune đều phá vỡ giả định này.** Thêm một
> cascade như vậy mà không kèm cơ chế canh riêng nghĩa là mở lại đúng lỗ hổng hotfix này vừa bịt —
> lần này qua đường xoá, và không có test nào hiện tại bắt được. Ai thêm `OnDelete(Cascade)` vào một
> quan hệ liên-commune phải xử lý việc đó **trong cùng migration**, không để lại sau.

**Cửa sau: `EnterUnscopedSystemWriteBackdoor()`.**

Tên dài và xấu có chủ đích. Hiện chỉ dùng ở fixture test (`AssetSchemaFixture.WriteAsSystemAsync`,
`AssetImportFixture`, `PoleWriteTests`…); `IdentitySeeder` chưa cần vì nó chỉ ghi entity không
`ICommuneScoped`. **Không bao giờ nới nó.**

Đường tắt hấp dẫn nhất là cho **scope rỗng** đi qua, vì seeder và fixture đều có scope rỗng. Nhưng
scope rỗng cũng chính là scope của **caller chưa đăng nhập** và của **token không có `commune_ids`** —
coi nó là quyền tức là mở guard cho đúng những người nó sinh ra để chặn. Muốn bỏ qua thì phải **làm
một hành động**, nhìn thấy được ở call site và trong diff.

Guard ném `LuxMapException` → **403 `COMMUNE_FORBIDDEN`**, ném **TRƯỚC** `base.SaveChanges`: ném từ
trong pipeline của EF sẽ bị bọc thành `DbUpdateException` và middleware BE-04 trả 500 thay vì 403.

> Chi phí guard: xem mục cuối file; không lấy số đo cũ làm lý do nới kiểm phạm vi.

**1b. `AdministrativeUnit` nằm ở `LuxMap.Persistence`, và KHÔNG implement `ICommuneScoped`.**

Nó không phải khái niệm của Identity — nó là **mốc neo phạm vi** cho entity theo xã. Đặt cạnh chính cơ chế thực thi nó (`ICommuneScoped`, `HasCommuneScope()`, `HasCommuneReference()`, chốt chặn khởi động) thì mọi module khai được FK thật qua tham chiếu `Persistence` vốn đã có, không phải phụ thuộc Identity.

**Không lọc chính bảng neo** — sẽ thành vòng lặp ngữ nghĩa: dòng định nghĩa một xã bị giấu bởi chính phạm vi suy ra từ nó. Endpoint liệt kê commune truy vấn tường minh theo `commune_ids` trong JWT. **Đừng "sửa cho nhất quán".**

Mọi cột `commune_id` khác **bắt buộc** `HasCommuneReference()`. Chốt chặn quét **theo cột**, không theo interface — nên nó bắt được cả entity mang `commune_id` mà quên implement `ICommuneScoped`, đúng lỗ hổng mà XML doc của interface tự thừa nhận. FK khai **không có navigation property**: coupling giữa module giữ ở mức chuỗi ID, không để `pole.Commune.Name` rải khắp nơi. `Restrict`, không bao giờ cascade một đơn vị hành chính.

Vì sao FK này không phải cầu toàn: `commune_id` mồ côi **không gây lỗi** — query filter nằm trong `WHERE` nên dòng đó vô hình với tất cả mọi người. Không exception, không log, dữ liệu biến mất.

**2. Tạo schema và quyền ghi là hai chuyện khác nhau.**

`pole_current_status` do **BE-09 tạo bảng** (BE-14 chạy trước BE-15 và cần 4 trường đó), nhưng
**BE-15/BE-17 sở hữu quyền ghi**. BE-12 (CRUD tài sản + import CSV) **không được đụng vào bảng này**.
Đó là lý do nó là bảng riêng chứ không phải 4 cột trên `pole` — ranh giới nằm trong lược đồ, không
chỉ trong quy ước.

`pole_current_status.last_sweep_id` đã có FK `Restrict` tới `SurveySweep` (`SurveyIngestConfigurations`).

## Chi phí `CommuneWriteGuard`

Không trích số đo cũ làm chi phí guard hoặc lý do nới guard: phép đo A/B chưa tách được chi phí biên khỏi nhiễu ghi DB.
`Entries<T>()` gọi `DetectChanges`, mà `SaveChanges` cũng gọi; không cộng các phép đo thành phần thành chi phí biên.
Muốn đo lại phải loại nhiễu có chu kỳ của phép ghi trước, không chỉ tăng số lượt. Benchmark tham chiếu:
`CommuneWriteGuardCostTests`, `The_parts_of_the_guard_measured_separately`.
Tối ưu vòng nạp bằng `AutoDetectChangesEnabled` rồi `DetectChanges()` một lần trước `SaveChanges`; **giữ nguyên kiểm phạm vi**.
