# Deploy, CI và chép dữ liệu

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi Docker / VPS / Caddy, CI, script chép DB. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

## CI: cấu hình host chỉ đặt ở bước cần dựng host

`Cors__AllowedOrigins__0` dùng cho export OpenAPI và migrate/seed phải ở `env` của từng step. Đặt ở cấp job làm `CorsTests.Startup_stops_on_a_missing_or_malformed_allowlist(null)` khởi động thành công vì allowlist từ môi trường, dù test đang kiểm cấu hình thiếu. Giữ môi trường regression test độc lập với cấu hình của CLI (PR #52, 27/09/2026).

## Chép DB lên Supabase — bảng mới phải vào `PLAN` (04/10/2026)

`scripts/copy_dev_to_supabase.py` **dừng** khi gặp bảng không có trong `PLAN` — cố ý, để không bảng nào bị bỏ sót âm
thầm. **Ticket tạo bảng thì thêm bảng vào `PLAN` trong cùng PR**: bộ lọc giữ xã (bảng không có `commune_id` lọc theo cha), và
đúng thứ tự khoá ngoại (script tự kiểm). Bảng nằm trong **vòng** khoá ngoại thì khai vào `TWO_PASS` như `survey_sweep` ↔
`survey_processing_run`, không gỡ ràng buộc. Diễn tập theo `docs/deploy/supabase.md` (cờ `--source-db`, chỉ cho đích localhost).

## OPS-DEPLOY-VPS — Docker, Caddy, một VPS (15/10/2026, `docs/deploy/vps.md`, drift DEP-1…DEP-4)

- **Image build NGAY TRÊN SERVER** (`deploy/deploy.sh` → `docker compose build`), nên không có `-r` / RID nào: image luôn đúng kiến trúc
  của máy chạy nó. Migration là **EF bundle trong cùng image** (`/app/efbundle`), service `migrate` profile `tools`, chạy **sau** backup.
- `dotnet ef migrations bundle` **dựng host** để tìm DbContext, mà `Program` dừng khi thiếu cấu hình ⇒ Dockerfile đặt giá trị giả
  `build-only` **chỉ cho lệnh đó**. Thêm biến bắt buộc lúc khởi động thì thêm vào dòng đó, nếu không image không build được.
  ⚠️ `-c` của `dotnet ef` là **`--context`**, không phải cấu hình build (`No DbContext named 'Release'`) — dùng `--configuration`.
- 🔴 **`deploy/compose.yml` là project `luxmap-prod`, KHÔNG BAO GIỜ `luxmap`.** Compose dev mang `name: luxmap` và đặt tên volume tường minh
  `luxmap_postgres_data`; cùng tên thì stack production gắn vào **DB dev**, và `--remove-orphans` xoá container dev.
- 🔴 **Caddyfile gắn theo THƯ MỤC (`./caddy:/etc/caddy`), không theo file.** Trình soạn thảo và `git pull` thay file bằng inode mới; bind mount
  một file giữ inode cũ đã xoá ⇒ `caddy reload` lỗi và **cấu hình cũ chạy tiếp** (gặp thật lúc diễn tập).
- **Trong Caddyfile, mọi `handle` chạy trước `respond` trần** ⇒ chặn một đường dẫn con phải là `handle <đường dài hơn>`, không phải
  `respond @matcher` (đã thử: `/api/v1/internal/*` vẫn tới API).
- 🔴 **Body khai lớn hơn mức API nhận ra 502, không phải 413**, nếu không có gì chặn trước: API trả 413 ngay từ `Content-Length` rồi đóng kết nối
  trong lúc Caddy còn gửi body (`broken pipe`). Caddy tự trả 413 cho `Content-Length > 314572800` = `SurveyIngestService.ClipLimit`.
  **Đổi `ClipLimit` (hay thêm endpoint nhận body lớn hơn) thì sửa guard trong `deploy/caddy/Caddyfile` cùng diff.** Evidence multipart
  đọc body rồi mới trả lỗi; không suy rộng sang mọi multipart: pole-photo từ chối trước binding, có guard Content-Length 17 MiB riêng
  (`PolePhotoBodyLimit`, PP-8). Thiếu guard này vẫn có thể ra 502.
- **`ForwardedHeaders__KnownProxies__0` = IP cố định của Caddy (`172.30.0.10`)**, đặt trong `compose.yml`. Đổi IP hay subnet của Caddy thì sửa cả
  hai cùng lúc; giá trị rỗng làm API **dừng lúc khởi động** (không phải IP), còn thiếu hẳn thì mọi client chung một xô giới hạn (phá thử đã đo).
- Cảnh báo DataProtection (`keys … may not be persisted`) lúc khởi động là vô hại: không code nào dùng DataProtection. Nếu sau này dùng
  (cookie auth, antiforgery) thì phải gắn volume cho thư mục khoá, nếu không mỗi lần deploy làm mất hiệu lực dữ liệu đã bảo vệ.
- Script chép dữ liệu lên server vẫn là `scripts/copy_dev_to_supabase.py` (đích = PostGIS của VPS qua SSH tunnel, `localhost` nên không đòi TLS);
  luật "bảng mới phải vào `PLAN`" vẫn áp.
