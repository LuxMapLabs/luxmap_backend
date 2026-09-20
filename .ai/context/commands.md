# Lệnh hay dùng

Bản rút gọn để agent chạy được ngay. **`README.md` là bản đầy đủ và thắng khi lệch** — nó giải thích
vì sao từng lệnh như vậy. Lệnh ở đây chạy sai thì sửa theo README rồi cập nhật file này.

## Build / test

```bash
dotnet build
dotnet test
```

Benchmark bị loại khỏi lượt mặc định (`luxmap.runsettings` lọc `Category!=Benchmark`). Chạy riêng:

```bash
dotnet test --settings luxmap.benchmark.runsettings
```

⚠️ `--filter "Category=Benchmark"` **không dùng được** — VSTest lấy giao với filter trong runsettings,
ra `(Category!=Benchmark)&(Category=Benchmark)`, khớp 0 test. Phải thay bằng file settings thứ hai.

Chạy một test:

```bash
dotnet test --filter "FullyQualifiedName~<TênTest>"
```

## Hạ tầng dev

Cần Docker Desktop. Lần đầu: `cp .env.example .env`.

```bash
docker compose up -d
docker compose ps
```

`postgres`, `redis`, `minio` phải `healthy`. `luxmap_minio_mc` **không** hiện ở đây — nó tạo 2 bucket
rồi thoát; `docker compose ps -a` thấy `Exited (0)` là **thành công**, không phải crash.

| Service | Cổng host | Trong container |
|---|---|---|
| PostgreSQL + PostGIS | **5433** | 5432 |
| Redis | **6380** | 6379 |
| MinIO S3 / console | 9000 / 9001 | 9000 / 9001 |

```
Host=localhost;Port=5433;Database=luxmap_dev;Username=luxmap;Password=luxmap_local_dev
```

```bash
docker compose exec postgres psql -U luxmap -d luxmap_dev
docker compose logs -f postgres
docker compose down
```

⚠️ `docker compose down -v` **xoá sạch dữ liệu dev** (volume `luxmap_postgres_data`). Chỉ dùng khi cố
ý dựng lại DB từ đầu — init script trong `docker/postgres/init/` chỉ chạy khi volume còn rỗng.

## Migration

```bash
dotnet ef migrations add <Tên> -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
```

⚠️ **Không tạo migration khi chưa được duyệt.** Nếu ticket đòi đổi schema, ghi thành D-item trước.

## Chạy API / seed

```bash
dotnet run --project src/LuxMap.Api
dotnet run --project src/LuxMap.Api -- --seed          # 1 xã + 4 tài khoản, idempotent
python3 scripts/seed_mock_set.py --apply               # bộ mock FO-26; bỏ --apply thì chỉ in SQL
```

Seed từ chối chạy khi còn migration chưa apply. `seed_mock_set.py` **từ chối chạy nếu `lux_reading`
có dữ liệu** — đó là ground truth RQ1.

## Xuất OpenAPI (chạy lại mỗi khi thêm/sửa endpoint, rồi commit)

```bash
dotnet tool restore
dotnet build src/LuxMap.Api && Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 \
  dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py && npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
```

⚠️ `Cors__AllowedOrigins__0` là **bắt buộc** dù không liên quan CORS: CLI dựng host ở Production, mà
ngoài Development thì `CorsSetup` dừng khởi động khi danh sách rỗng. Thiếu nó → lệnh chết và file cũ
**vẫn nằm nguyên**, rất dễ tưởng là spec không đổi.

⚠️ **README dòng 294 đang ghi `luxmap-v1.4.json`, file đó không còn tồn tại** (bản hợp nhất nay là
`luxmap-v1.5.json`). Lệnh trên đã dùng tên thật. Kiểm tra `ls docs/openapi/` trước khi chạy, và nếu
version lại nhảy thì sửa cả README lẫn file này.

## Kiểm tra nhanh trước khi báo xong

```bash
dotnet build && dotnet test && git status --short && git diff --stat
```
