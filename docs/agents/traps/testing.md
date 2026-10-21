# Test tích hợp

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi viết / sửa test, fixture, teardown, sequence, test host. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## Quy ước: MỌI test ghi tài sản nằm trong `AssetDatabaseCollection`, và KHÔNG test nào ghim ID bằng literal

`pole_id_seq` là tài nguyên dùng chung **trong một lượt chạy**; mỗi lượt có container PostGIS riêng (BE-36).

**1. Một collection cho mọi class ghi tài sản.** xUnit tuần tự các class trong `AssetDatabaseCollection`.
`PrefixedIdOverflowTests` lùi sequence để chạm ngưỡng độ rộng; người ghi song song có thể cướp ID hoặc nhận ID đã có,
ra `23505 pk_pole`. Không tách collection để tăng tốc.

**2. Chọn dải từ bảng LIVE, không ghim literal ID.** `AssetSchemaFixture` chèn từ chỗ sequence đang đứng, không từ 1.
Dùng `FreeFourDigitDecadeAsync`, assert tiền đề bằng `RequireFreeAsync`; chọn dải trống rồi **trả sequence về giá trị cũ
trong `try/finally`**. Dải đang ghi trống không chứng minh cả đoạn sequence sẽ đi qua đều trống.
Ngưỡng **9999 → 10000** là ngoại lệ cố định bởi độ rộng đệm; loại thập niên `1000` khỏi ứng viên.
Không `setval` trong fixture để nâng sàn theo ID lớn nhất: nó có thể đẩy khối dữ liệu vào đúng dải ngưỡng mà test giữ.

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
