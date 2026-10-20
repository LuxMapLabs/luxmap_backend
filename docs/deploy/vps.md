# VPS — triển khai LuxMap lên một máy chủ

Toàn bộ hệ thống chạy trên **một VPS** bằng Docker Compose: Caddy (HTTPS) → API → PostGIS + MinIO. Web SPA và API dùng
**chung một origin**: `https://<tên miền>/` là web, `https://<tên miền>/api/v1` là API. File nằm ở [`deploy/`](../../deploy).

```
internet ──443──▶ caddy ──┬─ /api/*  ──▶ api:8080 ──┬─▶ postgres (PostGIS)
           80 → 443       └─ còn lại ─▶ /srv/web    └─▶ minio
                          (mạng riêng 172.30.0.0/24; postgres/minio chỉ mở 127.0.0.1, đi vào bằng SSH)
```

## Đã chốt (Mỹ, 15/10/2026)

| | |
|---|---|
| V-1 | **Một VPS chạy tất cả**, Docker Compose. Không PaaS, không máy nhà + tunnel (tunnel cắt request ở 100 MB, clip tới 300 MiB) |
| V-2 | Đặt ở **Singapore** |
| V-3 | **PostGIS trên chính VPS** — **thay việc đặt DB trên Supabase** ([supabase.md](supabase.md), OPS-SUPABASE). DB cạnh API (NFR bbox < 500 ms), không bị tạm dừng vì gói free, không pooler. Cách chép của D-1/D-4/D-5 **giữ nguyên**: `copy_dev_to_supabase.py` chạy qua SSH tunnel (bước 6) |
| V-4 | **Tên miền riêng `luxmap.com.vn`** (Mỹ thuê 19/10/2026), **web chạy cùng server, cùng origin với API.** Cookie refresh của web (`__Secure-luxmap_rt`, `SameSite=Lax`) luôn là first-party, trình duyệt không gửi preflight CORS |
| V-5 | **Không triển khai EMQX.** Thiết bị testbed dùng kênh HTTP (`Lighting__Channel=http`, `/api/v1/device/…` qua HTTPS). MQTT trên internet cần listener TLS riêng — làm khi thật sự cần |
| V-6 | Migration chạy bằng **EF bundle của cùng bản build**, **sau** một lần backup — `deploy.sh` giữ thứ tự đó |

Cấu hình máy đề xuất: **4 vCPU / 8 GB RAM / ≥ 100 GB SSD**, Ubuntu 24.04. RAM vì worker khảo sát chạy ffmpeg + YOLO trong
tiến trình API; đĩa vì một đêm khảo sát là vài GB video (100–150 MB/phút).

## 1. Thuê máy, trỏ tên miền (Mỹ làm)

1. Tạo VPS Ubuntu 24.04 ở Singapore. Đăng nhập bằng **SSH key**, tắt đăng nhập mật khẩu.
2. Ở nhà cung cấp tên miền (DNS đang do nhà đăng ký quản lý, NS `hoian` / `danang.vclouddns.com`): bản ghi **A**
   `luxmap.com.vn` → IP của VPS (thêm AAAA nếu VPS có IPv6). Kiểm: `dig +short luxmap.com.vn` ra đúng IP trước khi chạy bước 4.
   ⚠️ Nếu DNS ở Cloudflare thì để **DNS only** (mây xám): proxy của Cloudflare cắt request ở 100 MB.
3. Tường lửa: chỉ mở **22, 80, 443** (cả 443/udp cho HTTP/3).
   ```bash
   sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443 && sudo ufw enable
   ```
   ⚠️ Docker tự mở cổng nó publish, **vượt qua ufw**. Vì vậy `compose.yml` chỉ publish 80/443; postgres và minio bind
   `127.0.0.1`. Đừng sửa thành `"5432:5432"`.

## 2. Cài Docker, lấy mã nguồn

```bash
curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker $USER   # đăng nhập lại sau lệnh này
sudo git clone https://github.com/LuxMapLabs/luxmap_backend.git /opt/luxmap && sudo chown -R $USER /opt/luxmap
cd /opt/luxmap && git checkout dev
```

Image MinIO không còn kéo ẩn danh được (quay.io trả 401): chép file `luxmap-minio-images.tar` (README bước 3) lên máy rồi
`docker load -i luxmap-minio-images.tar`.

## 3. Cấu hình

```bash
cd /opt/luxmap/deploy && cp .env.example .env && chmod 600 .env
```

Điền **mọi** `CHANGE_ME` (`deploy.sh` từ chối chạy khi còn sót). Mỗi secret sinh riêng:
`openssl rand -base64 48 | tr -d '/+=' | cut -c1-40`. Ba chỗ mang tên miền (`LUXMAP_DOMAIN`, `Cors__AllowedOrigins__0`,
`WEB_APP_BASE_URL`) phải cùng một giá trị. Gmail SMTP cần **app password** (bật xác minh 2 bước trước) — Gmail chạy được từ
Linux, chỉ macOS mới vướng kiểm tra thu hồi chứng chỉ (BE-33a).

Worker khảo sát để **tắt** (`SurveyProcessing__Enabled=false`): sweep đã nộp nằm `queued` và được xử lý khi bật. Bật lúc
cổng `VIDEO_DEVICE_MAPPING_PENDING` (D-07) còn giữ thì mọi sweep `field` **thất bại ngay** ở bước `video_clock`, và không
có API nào xếp lại hàng cho sweep đã thất bại. Chỉ bật sớm khi demo bằng sweep `simulated`; detector đã đặt sẵn `yolo`.

## 4. Triển khai

Thư mục backup phải có **trước** lần deploy đầu: `deploy.sh` backup trước khi migrate, chạy bằng user thường, và dừng nếu
không ghi được vào đó.

```bash
sudo mkdir -p /var/backups/luxmap && sudo chown $USER /var/backups/luxmap
cd /opt/luxmap && deploy/deploy.sh
```

`deploy.sh` làm theo đúng thứ tự: build image API → bật postgres + minio → tạo bucket → **backup** → **migration** → bật
API + Caddy. Lỗi ở bước nào thì dừng ở bước đó; API đang chạy không bị đụng tới. Lần đầu Caddy xin chứng chỉ Let's Encrypt
(cần DNS đã trỏ đúng và cổng 80 mở).

Kiểm: `curl -I https://<tên miền>/` (200) và `curl https://<tên miền>/api/v1/auth/me` (401 dạng `{"error":…}`).

## 5. Web SPA (WP5)

WP5 build với địa chỉ API **tương đối** `/api/v1` (cùng origin), rồi chép kết quả build vào `deploy/web/` (thư mục này
git-ignore). Thiếu `VITE_API_URL` thì bản build gọi `http://localhost:5141`:

```bash
VITE_API_URL=/api/v1 npm run build
rsync -a --delete dist/ <user>@<vps>:/opt/luxmap/deploy/web/
```

Không cần khởi động lại gì: Caddy đọc file trực tiếp, đường dẫn phía client (`/map`, `/work-orders/WO-0001`) trả `index.html`.
App Android (WP6) đặt `API_BASE_URL=https://<tên miền>/` — **không** kèm `api/v1`: mỗi đường dẫn trong app đã bắt đầu bằng
`api/v1/…`, kèm thêm là thành `/api/v1/api/v1/…` (404). Bản release cũng đọc giá trị này; để trống là trỏ emulator `10.0.2.2`.

## 6. Dữ liệu — chọn MỘT trong hai

**(a) Bắt đầu trống:** `cd deploy && docker compose run --rm api --seed` — tạo một xã và bốn tài khoản demo với mật khẩu
`SEED_*` trong `.env`.

**(b) Chép từ `luxmap_dev`** (3 xã, 4 tài khoản, mọi dữ liệu nghiệp vụ, giữ nguyên ID — D-4/D-5 của supabase.md). Chạy
**từ máy dev**, DB đích phải đã migrate và còn trống (bỏ qua 6a):

```bash
ssh -N -L 15432:127.0.0.1:5432 -L 19000:127.0.0.1:9000 <user>@<vps> &     # tunnel tới postgres + minio của VPS
ssh <user>@<vps> 'cd /opt/luxmap/deploy && docker compose stop api'          # không job nào ghi trong lúc chép

export LUXMAP_TARGET_URL='postgresql://luxmap@localhost:15432/luxmap'
read -rs PGPASSWORD && export PGPASSWORD                                       # POSTGRES_PASSWORD của VPS
python3 scripts/copy_dev_to_supabase.py            # kiểm + in kế hoạch, chưa ghi gì
python3 scripts/copy_dev_to_supabase.py --apply    # chép trong MỘT transaction, so md5 từng bảng trước commit
```

Ảnh và video nằm trong MinIO, chép riêng, **giữ nguyên khoá** (`SRC_*` là cặp khoá trong `.env` dev, `DST_*` của VPS):

```bash
docker run --rm -e SRC_KEY -e SRC_SECRET -e DST_KEY -e DST_SECRET --entrypoint /bin/sh \
  quay.io/minio/mc:RELEASE.2025-08-13T08-35-41Z -c '
  mc alias set src http://host.docker.internal:9000 "$SRC_KEY" "$SRC_SECRET"
  mc alias set dst http://host.docker.internal:19000 "$DST_KEY" "$DST_SECRET"
  for b in luxmap-survey luxmap-evidence luxmap-video luxmap-citizen; do mc mirror "src/$b" "dst/$b"; done'
```

Rồi làm **mục 7 bước 1 khi API còn tắt**, sau đó mới bật lại:

```bash
ssh <user>@<vps> 'cd /opt/luxmap/deploy && docker compose start api'
```

## 7. 🔴 Bỏ mật khẩu dev của bốn tài khoản demo (sau 6b)

Đường (b) chép **mật khẩu dev** (`change_this_password_0x` trong `.env.example`, ai trong nhóm cũng biết) lên một máy chủ
công khai. Xoá chúng **trước khi API bật lại**, nên không có lúc nào đăng nhập bằng mật khẩu dev được:

1. Vẫn qua tunnel của 6b, API còn tắt: đặt email thật và đưa cả bốn về trạng thái "đã mời, chưa đặt mật khẩu"
   (`password_hash` và `password_set_at` cùng NULL — `ck_app_user_password_set_together`; đăng nhập trả 401 như sai mật khẩu):
   ```bash
   psql "$LUXMAP_TARGET_URL" -v ON_ERROR_STOP=1 <<'SQL'
   BEGIN;
   UPDATE app_user SET email = '<email thật của admin>'    WHERE username = 'admin';
   UPDATE app_user SET email = '<email thật của agency>'   WHERE username = 'agency';
   UPDATE app_user SET email = '<email thật của engineer>' WHERE username = 'engineer';
   UPDATE app_user SET email = '<email thật của crew>'     WHERE username = 'crew';
   UPDATE app_user SET password_hash = NULL, password_set_at = NULL
    WHERE username IN ('admin', 'agency', 'engineer', 'crew');
   COMMIT;
   SQL
   ```
2. Bật API (lệnh cuối của 6b). Với từng email: `POST /api/v1/auth/password/forgot` `{"email": "…"}` → tài khoản chưa có mật
   khẩu nhận **thư mời** mang link `…/set-password?token=…` → đặt mật khẩu bằng `POST /api/v1/auth/password/set`
   (`{"token": "…", "new_password": "…"}`, tối thiểu 12 ký tự). Web chưa có trang `/set-password`, nên link trong thư
   ra 404 tới khi WP5 làm.

Seed (`--seed`) **bỏ qua tài khoản đã có**, nên đổi `SEED_*` trong `.env` không đổi được mật khẩu của chúng.

## 8. Sao lưu

`deploy/backup.sh`: dump DB (định dạng custom) + `mc mirror` bốn bucket vào `/var/backups/luxmap`, giữ dump 14 ngày.
`deploy.sh` gọi nó trước mỗi migration (thư mục đã tạo ở mục 4). Hằng đêm lúc **03:15 giờ Việt Nam**:

```bash
( crontab -l 2>/dev/null; echo '15 20 * * * cd /opt/luxmap/deploy && ./backup.sh >> /var/backups/luxmap/backup.log 2>&1' ) | crontab -
```

⚠️ Cron chạy theo **múi giờ của máy chủ**, mà image Ubuntu của VPS để **UTC** (`timedatectl show -p Timezone`): `20:15 UTC`
= `03:15` UTC+7. Viết `15 3 * * *` thì backup chạy lúc 10:15 sáng giờ Việt Nam, giữa giờ dùng. Kiểm một lần sau khi cài, trong
môi trường tối giản như cron (`PATH` chỉ `/usr/bin:/bin`), rồi xem `backup.log`:

```bash
env -i HOME=$HOME PATH=/usr/bin:/bin /bin/sh -c 'cd /opt/luxmap/deploy && ./backup.sh >> /var/backups/luxmap/backup.log 2>&1'
tail -1 /var/backups/luxmap/backup.log      # backup <stamp>: … database, … objects
```

⚠️ Bản sao lưu nằm **trên cùng đĩa** với dữ liệu — chép thêm ra ngoài định kỳ (`rsync` về máy nhóm, hoặc snapshot đĩa của
nhà cung cấp). Khôi phục DB:

```bash
docker compose stop api
docker compose exec -T postgres sh -c 'dropdb -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
docker compose exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --exit-on-error' < /var/backups/luxmap/db/luxmap-<stamp>.dump
docker compose start api
```

## 9. Cập nhật phiên bản

```bash
cd /opt/luxmap && git pull && deploy/deploy.sh
```

Quay lui mã: `git checkout <commit cũ> && deploy/deploy.sh`. Migration **không** tự lùi; một migration đã áp mà cần bỏ thì
khôi phục từ dump `deploy.sh` vừa tạo (bước 8) — đó là lý do backup đứng trước migration.

## Vận hành

| Việc | Lệnh (trong `deploy/`) |
|---|---|
| Trạng thái | `docker compose ps` |
| Log API | `docker compose logs -f api` (file JSON trong volume `api_logs`, xoay vòng 14 ngày) |
| Console MinIO | `ssh -L 9001:127.0.0.1:9001 <vps>` rồi mở http://localhost:9001 |
| psql | `docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" "$POSTGRES_DB"'` |
| Sửa `caddy/Caddyfile` | `docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile` |

Cảnh báo `Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' that may not be persisted` lúc API khởi động là
**vô hại**: không code nào dùng DataProtection (JWT ký bằng `JWT_SIGNING_KEY`, cookie refresh là token thô).

## KHÔNG BAO GIỜ

- Publish cổng postgres / minio ra ngoài `127.0.0.1`. Ảnh đi qua API là để phân quyền theo xã áp lên cả byte (BE-11 quy tắc 1).
- Để trống `ForwardedHeaders__KnownProxies__0` hay đổi IP của Caddy mà không sửa nó: danh sách **rỗng = tin mọi peer**, còn
  thiếu cấu hình thì cả internet chung **một** xô giới hạn tần suất (CITIZEN-QR P2b).
- Đổi `name: luxmap-prod` thành `luxmap` — trùng project và volume của stack dev.
- Commit hay gửi `deploy/.env` qua chat.

## Diễn tập 15/10/2026 — trên máy dev, `LUXMAP_DOMAIN=localhost`

Chạy `deploy/deploy.sh` y như trên server (image build trên arm64; VPS amd64 tự build bản của nó), rồi đi qua Caddy HTTPS:

| Kiểm | Kết quả |
|---|---|
| Migration bundle trên DB trống | 44/44 migration, API khởi động `Production` |
| Chép `luxmap_dev` (6b) | 41 bảng trùng md5, 32 sequence khớp; 28 object ảnh mirror |
| Web: `/`, đường SPA `/work-orders/WO-0010`, `http://` | 200 `index.html`; 200 `index.html`; 308 → https |
| `/api/v1/internal/*` | 404 (Caddy chặn) |
| Đăng nhập mobile, danh sách phiếu, thumbnail, upload ảnh | 200 / 17 phiếu / `image/jpeg` / 201 |
| Đăng nhập web + refresh bằng cookie | 200, `__Secure-luxmap_rt` `Path=/api/v1/auth/web` `Secure`; refresh 200 |
| Clip **290 MB** thật qua Caddy | 200, SHA-256 khớp, 2 s |
| Body khai 320 MB | 413 `UPLOAD_TOO_LARGE` do Caddy trả (trước khi sửa: **502** — xem dưới) |
| API thấy `https` và IP thật | `RequestScheme=https` ở 42/42 request; client thứ hai có xô giới hạn riêng (202 khi máy kia đã 429). **Phá thử**: bỏ `KnownProxies` → client thứ hai 429 |
| `X-Forwarded-For` giả từ client | Không thoát được xô (429) |
| `backup.sh` + khôi phục vào DB nháp | Dump 47 bảng, khôi phục đủ 276 cột / 20 phiếu / 17 ảnh / 44 migration |

Ba lỗi bắt được nhờ diễn tập, đã sửa trong file:

1. **Clip vượt 300 MiB ra 502.** API trả 413 ngay từ `Content-Length` rồi đóng kết nối trong lúc Caddy còn đang gửi body →
   `broken pipe`. Caddy nay tự trả 413 đúng dạng lỗi Contract cho body khai lớn hơn 300 MiB.
   POLE-PHOTO-AI (PP-8, 15/10/2026) thêm guard cùng cơ chế cho **POST `/api/v1/work-orders/*/pole-photos`**,
   Content-Length > **17 MiB**: giữ 413 thay vì 502 khi API chặn sớm; file bên trong vẫn ≤16 MiB.
2. **Gắn Caddyfile theo FILE làm `caddy reload` đọc file đã xoá.** Trình soạn thảo và `git pull` thay file bằng inode mới;
   container vẫn giữ inode cũ, reload báo lỗi và **cấu hình cũ chạy tiếp**. Nay gắn cả thư mục `deploy/caddy/`.
3. **`respond @matcher` đứng sau mọi `handle`** trong thứ tự của Caddy, nên chặn `/api/v1/internal/*` bằng `respond` không
   bao giờ chạy. Nay là một `handle` có đường dẫn dài hơn.
