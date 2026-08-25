# LuxMap — Backend (WP2)

Modular monolith ASP.NET Core phục vụ Web SPA (WP5), Android native (WP6) và engine CV (WP4).

Nguồn sự thật: [`docs/api-contract-v1.1.md`](docs/api-contract-v1.1.md) → [`docs/tasks-backend.csv`](docs/tasks-backend.csv) → [`CLAUDE.md`](CLAUDE.md).

## Yêu cầu

- .NET SDK 10.0

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
