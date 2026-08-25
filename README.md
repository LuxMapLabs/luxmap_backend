# LuxMap — Backend (WP2)

Modular monolith ASP.NET Core phục vụ Web SPA (WP5), Android native (WP6) và engine CV (WP4).

Nguồn sự thật: [`docs/api-contract-v1.1.md`](docs/api-contract-v1.1.md) → [`docs/tasks-backend.csv`](docs/tasks-backend.csv) → [`CLAUDE.md`](CLAUDE.md).

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

Lần đầu sẽ kéo image (~150 MB) và chạy `initdb`, mất khoảng 30–60 giây. Kiểm tra:

```bash
docker compose ps
```

Cả hai service phải ở trạng thái `healthy`. Nếu `postgres` còn `starting`, chờ thêm — healthcheck có `start_period` 30 giây.

### Cổng

| Service | Cổng host (mặc định) | Trong container |
|---|---|---|
| PostgreSQL + PostGIS | **5433** | 5432 |
| Redis | **6380** | 6379 |

Mặc định KHÔNG phải 5432/6379: máy dev thường đã có PostgreSQL hoặc Redis cài native chiếm sẵn. Cả hai chỉ bind vào `127.0.0.1`, không phơi ra LAN.

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

## Cấu trúc

| Project | Vai trò |
|---|---|
| `src/LuxMap.Api` | Host, liệt kê module, áp quy ước JSON |
| `src/LuxMap.Shared` | Quy ước contract dùng chung: enum, JSON, lỗi, phân trang, seam module |
| `src/LuxMap.Modules.Identity` | AppUser, AdministrativeUnit, JWT, phân quyền (BE-06..BE-08) |
| `src/LuxMap.Modules.Assets` | Pole, Fixture, RoadSegment, Feeder, bbox (BE-09..BE-14) |
| `src/LuxMap.Modules.Survey` | SurveySweep, SurveyFrame, Detection, LuxReading (BE-15..BE-17, BE-42) |
| `src/LuxMap.Modules.Faults` | Fault, FaultHistory, luồng trạng thái (BE-18..BE-20, BE-41) |
| `src/LuxMap.Modules.WorkOrders` | WorkOrder, ExternalUnit, RepairEvidence (BE-21..BE-24) |
| `src/LuxMap.Modules.Telemetry` | IotNode, TelemetryReading |
| `src/LuxMap.Modules.Admin` | Danh mục, ngưỡng, model version, dashboard (BE-28..BE-35) |
| `tests/LuxMap.Shared.Tests` | Khoá lại quy ước contract |

## Thêm một module

1. `dotnet new classlib -o src/LuxMap.Modules.<Tên>` và thêm `FrameworkReference` tới `Microsoft.AspNetCore.App`.
2. Hiện thực `ILuxMapModule` — module tự đăng ký service của mình.
3. Thêm một dòng vào mảng `modules` trong [`Program.cs`](src/LuxMap.Api/Program.cs).

## Quy ước JSON

Mọi thứ đi qua `LuxMapJsonOptions.Configure`. Host gọi `AddLuxMapJsonConventions()` — hàm này
cấu hình **cả** minimal API lẫn MVC controller, vì hai đường đọc hai `JsonOptions` khác nhau.
