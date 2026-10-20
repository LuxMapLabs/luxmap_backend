# Test tích hợp

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi viết / sửa test, fixture, teardown, sequence, test host. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Quy ước: MỌI test ghi tài sản nằm trong `AssetDatabaseCollection`, và KHÔNG test nào ghim ID bằng literal

Hai quy tắc, một nguyên nhân: **`pole_id_seq` là tài nguyên TOÀN CỤC dùng chung**, còn DB phát triển
thì dùng chung giữa mọi lượt chạy.

**1. Một collection duy nhất cho 16 class ghi tài sản.** xUnit chạy song song các class **khác
collection**, tuần tự các class **cùng collection**. `PrefixedIdOverflowTests` phải `setval` ghim
sequence tới ngưỡng độ rộng đệm rồi chèn — việc đó **không chịu được người ghi thứ hai**: lượt lùi
sequence trao cho hàng xóm một ID đã có, còn `nextval` của hàng xóm cướp mất ID vừa ghim. Bên thua
chết vì `pk_pole`, thông điệp không hề nhắc tới race.

Đo thật, 5 lượt mỗi bên, cùng máy cùng DB: tách hai collection → **4,1–4,3 s, 4/5 lượt đỏ**; gộp một
collection → **5,2–5,4 s, 5/5 xanh**. **Một giây đổi lấy bộ test không còn đỏ ngẫu nhiên.** Đừng tách
ra lại vì lý do tốc độ.

**2. Không literal ID nào trong test — chọn dải từ bảng LIVE.**

> 🔴 `AssetSchemaFixture` chèn 2500 cột bắt đầu từ **chỗ `pole_id_seq` đang đứng**, KHÔNG phải từ 1.

`PrefixedIdOverflowTests` từng giành literal `3000` / `30000`, canh bằng
`Assert.True(SyntheticPoleCount < 3000)` — câu đó **mã hoá một tiền đề sai**. Sau khi BE-39 seed bộ
mock FO-26, sequence đứng ở **854**, khối 2500 cột rơi vào **855..3354**, trùm lên 3000. Hậu quả
không dừng ở một test: lượt `setval(2999)` **làm nhiễm độc sequence** cho khoảng 355 lượt chèn kế
tiếp, nên một lượt chạy đỏ 36 test, lượt sau đỏ 76 — trông y hệt flaky, thực ra tất định.

Đường đúng: hỏi bảng xem số nào còn trống rồi mới ghim (`FreeFourDigitDecadeAsync`), và nêu tiền đề
thành assert có thông điệp (`RequireFreeAsync`) thay vì để nó lộ ra dưới dạng `23505` trần từ trong
`SaveChanges`.

⚠️ Ngưỡng **9999 → 10000** là ngoại lệ: nó do chính độ rộng đệm quy định nên **không dời đi đâu
được**. Vì vậy thập niên `1000` bị loại khỏi danh sách ứng viên — bội mười của nó đúng là 10000.

**BE-36 (11/10/2026) chỉ xoá NỬA giữa các lượt của lớp lỗi này** — mỗi lượt một DB sạch, nên cặn và
sequence trôi từ lượt trước không còn. **Nửa trong một lượt vẫn còn nguyên:** mọi class vẫn chung một DB
và chung `pole_id_seq`, nên hai quy tắc trên vẫn bắt buộc và **đừng tách collection**.

## Hai bẫy test chốt ở REG-v1.2 — culture và tài khoản seed

**SQL có số thực phải độc lập culture.** Ưu tiên truyền giá trị bằng SQL parameters. Khi test cần
sinh chuỗi SQL literal, dùng `string.Create(CultureInfo.InvariantCulture, $"…")` như
`MapEndpointTests.PlantPolesAsync`. Culture `en_VN` từng biến `108.5` thành `108,5`, khiến
`ST_MakePoint` nhận thêm đối số và PostGIS báo `22023: Geometry has Z dimension`. Đổi locale của
máy chỉ che lỗi; kiểm tra phải chạy được với locale dấu phẩy.

**Test đổi trạng thái tài khoản phải tự tạo tài khoản riêng và dọn trong `finally`.** Các tài khoản
seed được nhiều collection dùng để đăng nhập. Khoá, đổi role, password hoặc scope của chúng sẽ
ảnh hưởng test khác dù có khôi phục ở teardown. `AuthEndpointTests` từng khoá `crew` trong vài ms,
khiến `RoleCapabilityMatrixTests` ngẫu nhiên nhận 403 ngay ở login. Dùng mẫu
`CreateThrowawayAccountAsync` / `DeleteAccountAsync` trong `AuthEndpointTests`; chỉ sửa tài khoản
thuộc test đó, giữ tài khoản seed dùng chung ổn định.

## BE-36 — test chạy trên container PostGIS của chính lượt chạy (11/10/2026)

**Mọi factory trong `LuxMap.Api.Tests` kế thừa `LuxMapTestHost`, không bao giờ kế thừa thẳng
`WebApplicationFactory<Program>`.** `LuxMapTestHost.CreateHost` chờ `TestDatabase` (một container
`imresamu/postgis:17-3.5` mỗi lượt, migrate → seed → SQL của `seed_mock_set.py`) rồi kiểm data source của
host trỏ đúng container. Factory kế thừa thẳng thì đọc DB từ `.env` — **`luxmap_dev`** — và test vẫn xanh.
Canh bằng `TestHostCoverageTests` (phá thử: `DeviceProbeFactory` kế thừa thẳng → đỏ nêu đúng tên).
Ngoại lệ có tên: **`NoDatabaseTestHost`** cho test không chạm DB (CORS, correlation id, phân trang, lỗi khởi động) — chạy
không cần Docker. Nó **không** được đổi `ConnectionStrings__LuxMap` (biến toàn tiến trình, host container đọc song song) mà
thay chính `NpgsqlDataSource` bằng địa chỉ không ai nghe: test lỡ chạm DB thì đỏ "connection refused", không lặng lẽ dùng
`.env` (`NoDatabaseHostTests`).

**Chỉ đặt `ConnectionStrings__LuxMap` (chuỗi nguyên khối), không đặt `POSTGRES_*`.** `Program` nạp `.env` bằng
DotNetEnv với mặc định **ghi đè** biến đã có, ở **mỗi lần** dựng host — các biến thành phần sẽ bị giá trị dev
đè. Vì vậy `.env` **không được** chứa `ConnectionStrings__LuxMap`: `TestDatabase.LoadEnvironment` từ chối chạy.
⚠️ Kiểm "host trỏ đúng container" chạy **sau** khi host đã dựng và khởi động — nó bắt được lỗi, nhưng thứ
**ngăn** ghi nhầm là lượt kiểm `.env` phía trước. Đừng gỡ lượt kiểm đó vì tưởng `AssertTargets` đã đủ.

**Không có chế độ "chạy test trên DB có sẵn"** (D-5): biến `ConnectionStrings__LuxMap` đặt trong terminal bị
container ghi đè. Thêm chế độ đó phải kèm một guard riêng cấm DB dev — guard hiện tại chỉ biết container.

**Nạp mock bằng SQL stdout của script, không bằng `--apply`.** `--apply` mặc định `luxmap_dev` và xoá toàn
bảng; SQL chạy qua kết nối của `TestDatabase` thì đích không thể lạc.

**Mật khẩu seed đọc từ biến môi trường** (`AuthTestExtensions.SeedPassword` gọi `TestDatabase.LoadEnvironment`),
cùng nguồn với `SeedCredentials` — trước đây hai bên đọc hai nơi (file và env).

⚠️ **Khôi phục file sau phá thử bằng `cp` hoặc `touch`, KHÔNG bằng `mv` bản sao lưu.** `mv` giữ thời điểm sửa CŨ của bản
sao, cũ hơn DLL vừa build từ bản đã phá ⇒ MSBuild coi module là chưa đổi và giữ DLL bản phá; test đỏ ở lượt chạy toàn bộ trong khi
mã nguồn đã đúng (gặp thật ở CITIZEN-QR P2a). `git checkout` cũng không cứu được file chưa được track.

⚠️ **`dotnet test --no-build` sau khi đổi branch chạy bin CŨ.** Gặp thật ở P1: bin build trước khi pull
TOPO-INFER làm hàng loạt test đỏ `23514 ck_pole_feeder_source_matches_feeder` — trông như lỗi DB, thực ra
là thiếu code. Build lại trước khi kết luận.
