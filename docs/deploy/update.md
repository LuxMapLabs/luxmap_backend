# Cập nhật phiên bản lên VPS — backend và web

Quy trình **mỗi lần** đưa bản mới lên `https://luxmap.com.vn`. Máy đã dựng xong theo [vps.md](vps.md) (bước 1–8); file này
không lặp lại phần dựng máy lần đầu.

```
máy dev ──git push──▶ GitHub (dev) ──git pull──▶ VPS /opt/luxmap ──deploy.sh──▶ API mới   (backend)
máy dev ──npm run build──▶ dist/ ──rsync──▶ VPS /opt/luxmap/deploy/web ──▶ Caddy phục vụ  (web)
```

Backend build **trên VPS** (image đúng kiến trúc của máy). Web build **trên máy dev** rồi chép lên — VPS không cần Node.

## Cần có

| | |
|---|---|
| SSH | Key của bạn nằm trong `~/.ssh/authorized_keys` của user `ubuntu` trên VPS. Thử: `ssh ubuntu@15.235.242.213 true` |
| Backend | Thay đổi đã **merge vào `dev`** và CI xanh. VPS luôn chạy `dev` |
| Web | Node 18+ trên máy dev, repo `luxmap-web`, khoá MapTiler (`VITE_MAPTILER_KEY`) |

Lệnh dưới dùng `vps` thay cho `ubuntu@15.235.242.213`. Thêm vào `~/.ssh/config` một lần:

```
Host vps
  HostName 15.235.242.213
  User ubuntu
```

## A. Cập nhật backend

```bash
ssh vps
cd /opt/luxmap
git status --short                 # phải trống — có thay đổi tại chỗ thì dừng lại hỏi nhóm, đừng xoá
git pull --ff-only                 # lấy dev mới nhất; báo lỗi nếu lịch sử lệch thay vì tự merge
nohup deploy/deploy.sh > ~/deploy-$(date +%F-%H%M).log 2>&1 &
tail -f ~/deploy-*.log             # Ctrl-C để thoát xem log; deploy vẫn chạy tiếp
```

`nohup … &` để deploy **không chết khi mất SSH** (build image mất vài phút). `deploy.sh` làm theo thứ tự và **dừng ở bước
lỗi đầu tiên**, API đang chạy không bị đụng tới:

1. kiểm `deploy/.env` không còn `CHANGE_ME`, thư mục backup ghi được
2. build image API
3. bật postgres + minio, tạo bucket
4. **backup** DB + ảnh vào `/var/backups/luxmap`
5. **migration** (EF bundle của chính bản build)
6. bật API mới + Caddy

Xong khi log in bảng `docker compose ps` với `api` ở trạng thái `running`. Kiểm:

```bash
curl -sI https://luxmap.com.vn/ | head -1                      # 200 (khi đã có web) hoặc 404 (chưa chép web)
curl -s https://luxmap.com.vn/api/v1/auth/me                   # 401 dạng {"error":{"code":"UNAUTHENTICATED",…}}
cd /opt/luxmap/deploy && docker compose logs --since 5m api | grep -iE "error|fail" | head
```

Rồi đăng nhập web / app một lần bằng tài khoản thật.

### Đổi cấu hình (không đổi code)

Sửa trên VPS, **không bao giờ** commit hay gửi file này qua chat:

```bash
ssh vps
cd /opt/luxmap/deploy && nano .env
docker compose up -d api           # compose thấy .env đổi → tạo lại container API
```

### Quay lui

- **Code:** `git checkout <commit cũ> && deploy/deploy.sh` (rồi `git checkout dev` lần sau).
- **Migration không tự lùi.** Bản mới có migration mà cần bỏ → khôi phục dump `deploy.sh` vừa tạo trước migration
  ([vps.md mục 8](vps.md#8-sao-lưu)). Một số migration còn **từ chối** chạy `Down()` khi đã có dữ liệu mới (ví dụ
  `AddPolePhotoReviews`) — trên máy có dữ liệu, quay lui **code**, giữ schema.

## B. Cập nhật web

Trên **máy dev**, trong repo `luxmap-web`:

```bash
git checkout dev && git pull --ff-only
npm ci
VITE_API_URL=/api/v1 VITE_MAPTILER_KEY=<khoá MapTiler> npm run build
grep -rlE "localhost:[0-9]+" dist/ && echo "SAI: bản build còn trỏ localhost" || echo "OK"
```

- **`VITE_API_URL=/api/v1` là bắt buộc.** Web và API cùng origin (`luxmap.com.vn`), nên địa chỉ là đường dẫn tương đối.
  Thiếu biến này, bản build gọi `https://localhost:7252` và mọi request thất bại trên máy người dùng.
- Biến `VITE_*` được **đóng vào bản build** lúc build: đổi giá trị là phải build lại. Khoá MapTiler vì thế công khai trong
  bundle — giới hạn nó theo tên miền `luxmap.com.vn` trong trang quản lý của MapTiler.
- Cảnh báo `Some chunks are larger than 500 kB` là bình thường, không chặn build.
- Chuỗi `http://localhost` **không kèm cổng** trong bundle là giá trị dự phòng của React Router / axios, vô hại; phép kiểm
  bên dưới chỉ bắt `localhost:<cổng>`, tức địa chỉ API dev bị đóng vào bản build.

Chép lên VPS — **hai lượt**, để người đang mở trang không bao giờ nhận `index.html` mới trỏ tới file chưa chép xong:

```bash
rsync -az --exclude index.html dist/ vps:/opt/luxmap/deploy/web/     # 1. file mới (tên có mã băm, không đè file cũ)
rsync -az --delete dist/ vps:/opt/luxmap/deploy/web/                 # 2. index.html mới, xoá file cũ
```

Không cần khởi động lại gì: Caddy đọc file trực tiếp, gửi `index.html` với `Cache-Control: no-cache` nên trình duyệt nhận bản
mới ngay ở lần tải sau, còn `/assets/*` (tên có mã băm) được cache một năm. Chép **vào** thư mục `deploy/web/`, đừng thay cả thư mục
(`mv`, xoá rồi tạo lại) — container gắn thư mục cũ và sẽ phục vụ bản cũ hoặc trả 404.

Kiểm: mở `https://luxmap.com.vn` (Ctrl/Cmd-Shift-R), đăng nhập, mở thẳng một đường dẫn sâu (ví dụ `/map`) — phải ra trang, không 404.

**Quay lui web:** checkout commit cũ của `luxmap-web`, build và rsync lại như trên.

> `vercel.json` trong repo web chỉ dùng cho bản xem thử. Bản chính chạy trên VPS cùng origin với API: đặt web ở tên miền
> khác thì cookie refresh (`__Secure-luxmap_rt`, `SameSite=Lax`) không gửi được và phải mở CORS.

## C. App Android (WP6)

Bản trỏ VPS đặt `API_BASE_URL=https://luxmap.com.vn/` — **không** kèm `api/v1` (đường dẫn trong app đã bắt đầu bằng `api/v1/…`).
Backend cập nhật không cần build lại app, trừ khi API đổi hình dạng (xem `docs/contract-drift.md`).

## Sự cố thường gặp

| Thấy | Nguyên nhân thường gặp | Làm |
|---|---|---|
| `deploy.sh` dừng: `CHANGE_ME values` | Bản mới thêm biến vào `.env.example` | So `deploy/.env.example` với `.env`, thêm biến còn thiếu |
| `deploy.sh` dừng ở `migrate` | Migration lỗi trên dữ liệu thật | API cũ vẫn chạy; đọc log, báo nhóm; **không** chạy lại tay từng bước |
| Trang trả **502** | Container API dừng / đang khởi động | `docker compose ps`, `docker compose logs --tail 100 api` |
| `/` trả **404** | `deploy/web/` trống | Làm mục B |
| Web lỗi mạng, DevTools thấy gọi `localhost` | Build thiếu `VITE_API_URL` | Build lại với `VITE_API_URL=/api/v1` |
| Upload lớn trả **413** | Đúng giới hạn (clip 300 MiB, ảnh cột 16 MiB) | Không phải lỗi deploy |
| Đĩa đầy | Video khảo sát + cache build Docker | `df -h`, `docker system df`; xoá cache build: `docker builder prune` |
| Sửa `deploy/caddy/Caddyfile` không ăn | Caddy chưa nạp lại | `docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile` |

## KHÔNG BAO GIỜ

- `docker compose down -v` — `-v` **xoá volume**: mất DB và toàn bộ ảnh / video.
- Chạy `scripts/copy_dev_to_supabase.py --apply` vào VPS đang có dữ liệu — script dành cho DB **trống** (vps.md mục 6b).
- Sửa code trực tiếp trên VPS. Mọi thay đổi đi qua PR vào `dev` rồi `git pull`.
- Commit hoặc gửi `deploy/.env` qua chat.
