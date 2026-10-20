# Phạm vi xã — chi tiết

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi sửa entity mang `commune_id`, query filter, `CommuneWriteGuard`, FK cascade, backdoor. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Hai quy tắc chốt ở BE-09 — áp cho 14 entity còn lại

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

Vì sao lọt lâu: tiêu chí nghiệm thu của BE-08 trong `tasks-backend.csv` ghi *"Kỹ sư bảo trì chỉ
**THẤY** tài sản thuộc địa bàn mình"*. **"Thấy" là đọc.** Cả 8 test scope đều là `GET`,
`ScopeTestController` chỉ có `HttpGet`. Ghi chép lại đây để không ai tưởng BE-08 vốn đã đủ.

**Hai lớp, hai việc khác nhau — cần cả hai:**

| Lớp | Ở đâu | Cho cái gì |
|---|---|---|
| `CommuneFilter.Narrow` | Entry point của controller, gọi tường minh | 403 với thông điệp tử tế, nêu đúng `commune_id` bị từ chối. Người dùng hiểu vì sao. |
| `CommuneWriteGuard` ở `SaveChanges` | `LuxMapDbContext`, tự động | **Backstop không thể quên.** Phủ mọi entity `ICommuneScoped`, kể cả của BE-15/BE-18/BE-21 trước khi chúng được viết — **trừ một ca, xem ngay dưới**. |

> ⚠️ **Một ngoại lệ đã biết, phát sinh từ O-7: sửa `commune_id` của chính `Feeder` đã được track.**
> `HasAlternateKey` biến `Feeder.CommuneId` thành **key property**, mà EF Core cấm sửa key trên entity
> đang track — nó ném `InvalidOperationException` ngay trong `DetectChanges`, **trước khi guard chạy**,
> nên ra **500** chứ không phải 403 `COMMUNE_FORBIDDEN`.
>
> **Dữ liệu không hề gặp rủi ro** — cả hai lớp đều TỪ CHỐI, chỉ khác lớp nào từ chối và từ chối có dễ
> đọc không. Ghi lại vì câu "phủ mọi entity" ở trên là câu mà ticket sau sẽ dựa vào. Ghim bằng
> `Changing_a_tracked_feeders_own_commune_is_refused_by_EF_before_the_guard_sees_it`.

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
> **Hiện an toàn**, vì mọi bảng cascade — `fixture`, `pole_current_status` — đều **cùng commune với
> `pole` cha**. Xoá một pole hợp lệ chỉ kéo theo dữ liệu của chính commune đó.
>
> **Bất kỳ FK cascade MỚI nào giữa hai bảng có thể khác commune đều phá vỡ giả định này.** Thêm một
> cascade như vậy mà không kèm cơ chế canh riêng nghĩa là mở lại đúng lỗ hổng hotfix này vừa bịt —
> lần này qua đường xoá, và không có test nào hiện tại bắt được. Ai thêm `OnDelete(Cascade)` vào một
> quan hệ liên-commune phải xử lý việc đó **trong cùng migration**, không để lại sau.

**Cửa sau: `EnterUnscopedSystemWriteBackdoor()`.**

Tên dài và xấu có chủ đích. Hiện chỉ dùng ở fixture test (`AssetSchemaFixture.WriteAsSystemAsync`,
`AssetImportFixture`, `PoleWriteTests`…); `IdentitySeeder` chưa cần vì nó chỉ ghi entity không
`ICommuneScoped`. BE-39 seed tài sản sẽ là caller thật đầu tiên trong `src/`. **Không bao giờ nới nó.**

Đường tắt hấp dẫn nhất là cho **scope rỗng** đi qua, vì seeder và fixture đều có scope rỗng. Nhưng
scope rỗng cũng chính là scope của **caller chưa đăng nhập** và của **token không có `commune_ids`** —
coi nó là quyền tức là mở guard cho đúng những người nó sinh ra để chặn. Muốn bỏ qua thì phải **làm
một hành động**, nhìn thấy được ở call site và trong diff.

Guard ném `LuxMapException` → **403 `COMMUNE_FORBIDDEN`**, ném **TRƯỚC** `base.SaveChanges`: ném từ
trong pipeline của EF sẽ bị bọc thành `DbUpdateException` và middleware BE-04 trả 500 thay vì 403.

> ⚠️ **Con số ~3,7 µs/entity ghi ở đây trước kia ĐO NHẦM ĐỐI TƯỢNG.** Nó là lượt
> `ChangeTracker.Entries<T>()` **ấm** — không phải việc kiểm phạm vi (0,21 µs), cũng không phải chi
> phí thật của guard. Phép đo **A/B** ở BE-12a cho kết luận là **không đo nổi**: chi phí guard nhỏ
> hơn nhiễu của chính phép ghi. Xem mục "Chi phí `CommuneWriteGuard`". **Đừng trích lại 3,7 µs — và
> cũng đừng trích con số nào khác** như thể nó là giá của guard.

**1b. `AdministrativeUnit` nằm ở `LuxMap.Persistence`, và KHÔNG implement `ICommuneScoped`.**

Nó không phải khái niệm của Identity — nó là **mốc neo phạm vi** cho 15/16 entity. Đặt cạnh chính cơ chế thực thi nó (`ICommuneScoped`, `HasCommuneScope()`, `HasCommuneReference()`, chốt chặn khởi động) thì mọi module khai được FK thật qua tham chiếu `Persistence` vốn đã có, không phải phụ thuộc Identity.

**Không lọc chính bảng neo** — sẽ thành vòng lặp ngữ nghĩa: dòng định nghĩa một xã bị giấu bởi chính phạm vi suy ra từ nó. Endpoint liệt kê commune truy vấn tường minh theo `commune_ids` trong JWT. **Đừng "sửa cho nhất quán".**

Mọi cột `commune_id` khác **bắt buộc** `HasCommuneReference()`. Chốt chặn quét **theo cột**, không theo interface — nên nó bắt được cả entity mang `commune_id` mà quên implement `ICommuneScoped`, đúng lỗ hổng mà XML doc của interface tự thừa nhận. FK khai **không có navigation property**: coupling giữa module giữ ở mức chuỗi ID, không để `pole.Commune.Name` rải khắp nơi. `Restrict`, không bao giờ cascade một đơn vị hành chính.

Vì sao FK này không phải cầu toàn: `commune_id` mồ côi **không gây lỗi** — query filter nằm trong `WHERE` nên dòng đó vô hình với tất cả mọi người. Không exception, không log, dữ liệu biến mất.

**2. Tạo schema và quyền ghi là hai chuyện khác nhau.**

`pole_current_status` do **BE-09 tạo bảng** (BE-14 chạy trước BE-15 và cần 4 trường đó), nhưng
**BE-15/BE-17 sở hữu quyền ghi**. BE-12 (CRUD tài sản + import CSV) **không được đụng vào bảng này**.
Đó là lý do nó là bảng riêng chứ không phải 4 cột trên `pole` — ranh giới nằm trong lược đồ, không
chỉ trong quy ước.

Nợ FK duy nhất của BE-09: `pole_current_status.last_sweep_id` là `text` chưa có FK; BE-15 thêm ràng
buộc trong migration của nó.

## Chi phí `CommuneWriteGuard` — đo A/B bắt cặp, KHÔNG kết luận được con số

Con số **~3,7 µs/entity** ghi ở mục BE-09 **đo nhầm đối tượng**: nó là lượt `Entries<T>()` ấm, không
phải việc kiểm phạm vi. Nhưng cộng các phần lại cũng sai nốt, theo chiều ngược: `Entries<T>()` gọi
`DetectChanges`, mà `SaveChanges` **đằng nào cũng gọi** — nên guard có thể chỉ **dời** lượt quét đó
sớm lên chứ không thêm.

**Chi phí biên của guard nằm dưới ngưỡng phân giải của phép đo.** Đo A/B bắt cặp trên 1000 entity
(`SaveChanges` có guard vs không, D1 = `EnterUnscopedSystemWriteBackdoor`), 30 cặp sau 8 cặp
warm-up: **trung vị delta ~8 µs/entity**, nhưng **IQR (14,3 ms) rộng hơn trung vị (8,5 ms)** và chỉ
**20/30 cặp dương**, không đạt ngưỡng 27/30. **Kết luận dùng được: chi phí guard nhỏ hơn nhiễu của
chính phép ghi nó bảo vệ** (~8 ms trên nền ~55 ms cho 1000 dòng). **Không có con số điểm nào đáng
trích.**

Ba số 9,69 / 3,69 / 0,21 là **phân rã bên trong guard đo trong bộ nhớ**, KHÔNG phải chi phí biên —
**đừng cộng, đừng trích** khi nói guard tốn bao nhiêu.

> **Giả thuyết "guard chỉ dời `DetectChanges` sang sớm hơn" chưa bị bác cũng chưa được xác nhận ở độ
> phân giải này.** 20/30 cặp dương và trung vị dương thì nghiêng về việc guard **thêm** chi phí thật,
> nhưng không qua cổng nên chưa kết luận được. Đó là câu hỏi còn mở cho ai muốn đo lại.

⚠️ **Đừng đo lại bằng chính cách này.** Ba lượt chạy đã cho ba kết quả trượt cổng, và lượt cuối lộ ra
một **nhiễu có chu kỳ**: 10 cặp delta âm rơi vào **đúng các cặp 1, 4, 7, 10, … — chu kỳ 3, không sót
cặp nào**, và nhánh B ở đúng những cặp đó vọt lên ~60–70 ms. Đó là **hiện tượng hệ thống**, không
phải nhiễu ngẫu nhiên, nên tăng số lượt sẽ không làm nó biến mất. Muốn đo lại thì phải tìm ra chu kỳ
3 ấy là gì trước (checkpoint của PostgreSQL? xoay vòng connection pool?), hoặc bỏ hẳn phép ghi thật.

Bốn lượt đã chạy, để không ai lặp lại:

| Lượt | Cấu hình | Kết quả | Cổng |
|---|---|---|---|
| 1–4 | 5 cặp, 1 cặp warm-up, trung-vị-A trừ trung-vị-B | 5,92 – 14,84 µs/entity | không có cổng; **cách trừ đã sai** |
| 5 | 30 cặp, 2 cặp warm-up, delta trong cặp | 26/30 dương · trung vị 6,45 · IQR 9,56 | **trượt cả hai** |
| 6 | 30 cặp, **8 cặp** warm-up, delta trong cặp | 20/30 dương · trung vị 8,46 · IQR 14,33 | **trượt cả hai, tệ hơn** |

Ba số đo trực tiếp, ghi được vì chúng đo đúng thứ chúng nói (`The_parts_of_the_guard_measured_separately`):

| Đo cái gì | µs/entity |
|---|---|
| `Entries<T>()` kèm `DetectChanges` | 8,6 – 9,7 |
| `Entries<T>()` lượt ấm — **con số 3,7 cũ** | 3,7 – 8,5, **không tái lập ổn định** |
| Vòng kiểm phạm vi — **công việc thật của guard** | **0,21 – 0,23** |

⚠️ **Đừng cộng ba số này lại thành "chi phí guard".** Đó chính là lỗi quy-sai đã sinh ra con số 3,7,
chỉ lệch chiều ngược. Chúng chỉ để hiểu chi phí **nằm ở đâu** bên trong guard — phép đo A/B, thứ duy
nhất trả lời được guard tốn thêm bao nhiêu, **không kết luận được**.

> **Nếu có người lấy cớ hiệu năng đòi nới guard:** việc kiểm phạm vi tốn **0,21 µs** — nới nó ra
> không mua được gì. Đường tối ưu đúng là tắt `AutoDetectChangesEnabled` quanh vòng nạp rồi gọi
> `DetectChanges()` **một lần** trước `SaveChanges`: bỏ lượt quét thừa, **giữ nguyên** phần kiểm.
