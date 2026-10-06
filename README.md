# LuxMap — Backend (WP2)

LuxMap là nền tảng GIS + IoT + thị giác máy tính để quản lý tài sản và sự cố chiếu sáng đường giao thông nông thôn
(đồ án FA26SE222). Repo này là backend: một modular monolith ASP.NET Core (.NET 10) trên PostgreSQL + PostGIS, phục vụ
Web SPA (WP5), app Android (WP6) và engine CV (WP4) qua REST API `/api/v1`.

- Đặc tả API: [`docs/api-contract-v1.1.md`](docs/api-contract-v1.1.md) — nguồn sự thật, thắng mọi tài liệu khác
- Đọc code từ đâu: [`docs/code-walkthrough.md`](docs/code-walkthrough.md)
- Quy ước và các bẫy đã biết: [`CLAUDE.md`](CLAUDE.md)
- Lệnh và cấu hình chi tiết: [`docs/development.md`](docs/development.md)

## Chạy lần đầu sau khi clone

### 1. Cài sẵn

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — đang chạy
- Python 3 (chỉ để nạp dữ liệu mẫu, không cần thư viện nào thêm)
- Công cụ migration, đúng phiên bản EF Core của repo: `dotnet tool install --global dotnet-ef --version 10.0.11`

### 2. Cấu hình

```bash
cp .env.example .env
```

`.env.example` đã có sẵn giá trị chạy được trên máy dev (database, MinIO, mật khẩu tài khoản demo, hộp thư Mailpit).
Nên thay `JWT_SIGNING_KEY` bằng một khoá thật: `openssl rand -base64 48`. **Không commit `.env`.**

### 3. Nạp image MinIO (chỉ máy mới)

Image MinIO không còn tải ẩn danh được từ Docker Hub hay quay.io. Xin BE1 file `luxmap-minio-images.tar` rồi:

```bash
docker load -i luxmap-minio-images.tar
```

SHA-256 của file phải là `6832673c39f69e84cb1411fe52e462912ffe2affcb5923bfe572cad75f11c03c`. Máy đã từng chạy dự án thì
bỏ qua bước này. Chưa có file thì vẫn chạy được API và test bằng `docker compose up -d postgres mailpit` — chỉ phần lưu
ảnh / video không hoạt động.

### 4. Bật hạ tầng

```bash
docker compose up -d
docker compose ps
```

Chờ `postgres`, `redis`, `minio`, `mailpit` ở trạng thái `healthy` (lần đầu khoảng 30–60 giây). `luxmap_minio_mc` thoát với
`Exited (0)` là bình thường: nó chỉ tạo bucket.

| Dịch vụ | Địa chỉ |
|---|---|
| PostgreSQL + PostGIS | `localhost:5433` — database `luxmap_dev` |
| Redis | `localhost:6380` |
| MinIO console | http://localhost:9001 |
| Mailpit (thư mời / đặt lại mật khẩu) | http://localhost:8025 |

Trùng cổng thì đổi trong `.env` (`POSTGRES_PORT`, `REDIS_PORT`…), không sửa `docker-compose.yml`.

### 5. Tạo database và dữ liệu mẫu

```bash
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
dotnet run --project src/LuxMap.Api -- --seed
python3 scripts/seed_mock_set.py --apply
```

- `--seed` tạo một xã và bốn tài khoản demo; mật khẩu lấy từ `SEED_*_PASSWORD` trong `.env`.
- `seed_mock_set.py` nạp bộ dữ liệu mẫu của FE (3 tuyến, 103 cột, 28 sự cố) — tuỳ chọn. ⚠️ Nó **xoá trọn các bảng tài
  sản** rồi ghi lại, nên chỉ chạy trên database của chính bạn.

| Username | Vai trò |
|---|---|
| `admin` | Quản trị hệ thống |
| `agency` | Cấp giám sát (chỉ đọc) |
| `engineer` | Quản lý |
| `crew` | Kỹ sư hiện trường |

### 6. Chạy API

```bash
dotnet run --project src/LuxMap.Api
```

Mở **http://localhost:5141/swagger**. Gọi `POST /api/v1/auth/login` với một tài khoản ở trên, copy `access_token`, bấm
**Authorize** và dán token (không kèm chữ `Bearer`).

### 7. Chạy test

Test tích hợp chạy trên PostGIS thật và **ghi vào database**; chúng cần database đã migrate, đã seed tài khoản và đã nạp
bộ mẫu (đúng như CI). Đừng chạy trên `luxmap_dev` — tạo một database riêng một lần:

```bash
docker compose exec postgres psql -U luxmap -d luxmap_dev -c "CREATE DATABASE luxmap_test"
docker compose exec postgres psql -U luxmap -d luxmap_test -c "CREATE EXTENSION postgis"
export ConnectionStrings__LuxMap="Host=localhost;Port=5433;Database=luxmap_test;Username=luxmap;Password=<POSTGRES_PASSWORD trong .env>"
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
dotnet run --project src/LuxMap.Api -- --seed
python3 scripts/seed_mock_set.py --apply --database luxmap_test
dotnet test
```

Biến `ConnectionStrings__LuxMap` chỉ sống trong cửa sổ terminal đó (PowerShell: `$env:ConnectionStrings__LuxMap="…"`).
Mở terminal mới thì lệnh `dotnet run` lại trỏ về `luxmap_dev` theo `.env`.

## Khi gặp lỗi

| Hiện tượng | Cách xử lý |
|---|---|
| API dừng ngay lúc khởi động, báo thiếu cấu hình | Thiếu `.env` hoặc thiếu biến — so lại với `.env.example` |
| `--seed` từ chối chạy | Còn migration chưa áp — chạy lại `dotnet ef database update …` |
| `docker compose up` không tải được image MinIO | Làm bước 3 |
| Muốn xoá sạch database dev | `docker compose down -v && docker compose up -d` — **mất toàn bộ dữ liệu**, rồi làm lại bước 5 |

Mọi chi tiết khác (CI, xuất OpenAPI, xác thực, phân quyền, luồng khảo sát): [`docs/development.md`](docs/development.md).
