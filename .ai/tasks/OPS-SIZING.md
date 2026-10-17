---
ticket: OPS-SIZING
title: Đo đỉnh RAM / CPU / đĩa của stack production để chọn cấu hình VPS
status: ready
phase: 1
owner: codex
branch: docs/OPS-SIZING
depends_on:
  - "OPS-DEPLOY-VPS (docs/deploy/vps.md, deploy/compose.yml) — stack cần đo"
---

## Bối cảnh

Runbook `docs/deploy/vps.md` đề xuất **4 vCPU / 8 GB / ≥100 GB** nhưng con số đó là ước tính. Claude đã đo sơ bộ trên macOS,
.NET chạy trần (KHÔNG trong container Linux, KHÔNG có video): Postgres ~92 MB, MinIO ~194 MB, API idle 191 MB, API đỉnh **575 MB**
khi 6 ảnh 12 MP vào `POST /ai/detect` cùng lúc. **Chưa ai đo một lượt xử lý video khảo sát** (ffmpeg + YOLO trong worker).
Mỹ cần chọn giữa VPS 2 GB / 4 GB / 8 GB và dung lượng đĩa — hãy đo bằng số thật, trên đúng stack production.

## Việc phải làm

1. **Dựng đúng stack production** từ `deploy/compose.yml` (project `luxmap-prod`, image build bằng `Dockerfile` của repo) trên Docker
   Desktop của máy này. Tạo `deploy/.env` từ `deploy/.env.example` với giá trị **thử nghiệm** (mật khẩu ngẫu nhiên, tên miền
   `localhost`, SMTP trỏ tới host không tồn tại — không gửi mail thật). Chạy migrate (service `migrate`, profile `tools`) + seed tối thiểu
   (tài khoản seed, bộ mock FO-26 bằng SQL stdout của `scripts/seed_mock_set.py` như `TestDatabase` làm) — đọc `docs/deploy/vps.md`.
2. **Lấy mẫu `docker stats` mỗi giây** cho mọi container của project `luxmap-prod` (RAM, CPU%) trong suốt các kịch bản dưới, ghi CSV.
   Ghi thêm RSS của tiến trình `ffmpeg` / `ffprobe` nếu đo được tách riêng (chúng chạy trong container `api`, nên đã nằm trong số của api).
3. **Kịch bản** (mỗi cái ghi đỉnh từng container + đỉnh TỔNG cùng thời điểm):
   - **A. Nhàn** sau khởi động 2 phút.
   - **B. Ảnh:** 10 lượt `POST /work-orders/{id}/pole-photos` đồng thời (ảnh JPEG thật 12 MP trong `/Users/nhm809/Documents/LuxMap/luxmap_backend/img/`, phiếu `in_progress` của `crew`,
     cột thuộc phiếu) + 5 `POST /ai/detect` đồng thời.
   - **C. Khảo sát video (quan trọng nhất):** tạo phiếu survey, `crew` start, upload **một sweep** đủ điều kiện `submit` (clip + `gps_track`,
     `lux_log`, `capture_config`, manifest có hash — học cách dựng từ `tests/LuxMap.Api.Tests/Survey*Tests.cs` / `SurveyIngestService`;
     GPS/lux có thể tổng hợp quanh tuyến mock), rồi để worker xử lý tới `awaiting_review` hoặc `failed`. Dùng **clip thật** trong `/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/`
     cỡ ~100–260 MB (ví dụ `IMG_0458.MOV`, `IMG_0460.MOV`) — đó là cỡ clip thực địa. Nếu `failed` vì dữ liệu GPS/lux tổng hợp nhưng
     ffmpeg + YOLO **đã chạy** trên frame thì số đo vẫn dùng được — ghi rõ bước nào đã chạy (log worker).
   - **D. Chồng tải:** C đang xử lý + B cùng lúc.
4. **Kiểm dưới giới hạn RAM** (mô phỏng VPS nhỏ): chạy lại C (và D nếu kịp) với file override compose đặt `mem_limit` cho `api`
   (thử **1 GB** rồi **1.5 GB**) và tổng hợp lý cho postgres/minio; ghi có bị OOM-kill (`docker inspect … OOMKilled`) hay lỗi không.
5. **Đĩa:** sau C, đo dung lượng volume (postgres, minio) và thư mục tạm frame; suy ra **byte trên mỗi phút video** lưu lại.
6. **Kết luận** bằng bảng: đỉnh từng container, đỉnh tổng, cộng **ước tính** OS + Docker (ghi rõ là ước tính), → khuyến nghị
   **RAM tối thiểu / khuyến nghị**, **vCPU** (thời gian xử lý một clip theo số CPU nếu đo được), **đĩa** cho ~4 tháng đồ án
   (giả định số đêm khảo sát ghi rõ). Ghi thời gian xử lý clip C.

Ghi toàn bộ vào `.ai/results/OPS-SIZING.md` (tiếng Việt), dán output lệnh thật (trích ngắn, CSV để riêng cạnh file kết quả
`.ai/results/OPS-SIZING-stats/*.csv`). Script đo nếu viết thì để trong `.ai/results/OPS-SIZING-stats/` luôn.

## KHÔNG ĐƯỢC làm

- 🔴 **Không đụng stack DEV:** container `luxmap_postgres`, `luxmap_minio` (đang chạy), volume `luxmap_postgres_data`,
  `luxmap_minio_data`, `luxmap_emqx_data`, DB `luxmap_dev`. Không `docker compose` ở thư mục gốc repo (đó là compose dev, project `luxmap`).
  Mọi lệnh compose phải là `-f deploy/compose.yml` (project `luxmap-prod`).
- Cuối cùng **dọn stack đo**: `docker compose -f deploy/compose.yml … down -v` — **trước khi chạy, liệt kê volume sẽ bị xoá và
  chắc chắn tất cả có tiền tố `luxmap-prod`**. Không `docker system prune`, không xoá image/volume nào khác.
- Không sửa `src/`, `tests/`, `deploy/compose.yml`, `Dockerfile`, Caddyfile (override đặt ở file tạm ngoài repo hoặc trong
  `.ai/results/OPS-SIZING-stats/`). Không sửa `CLAUDE.md`, drift, `tracking.html`, `docs/deploy/vps.md` — Claude cập nhật sau khi review.
- `/Users/nhm809/Documents/LuxMap/luxmap_backend/Videos/` và `…/img/` (nằm ở working tree chính, không có trong worktree này) là dữ liệu thực địa của Mỹ: **chỉ đọc**, chép vào request, không sửa/xoá/di chuyển.
- Không in mật khẩu / `.env` ra kết quả. Không gửi mail, không gọi dịch vụ ngoài (trừ kéo image đã pin nếu cần).
- Không commit, không push. Nếu Docker Desktop không đủ tài nguyên hoặc bước nào không làm được: dừng bước đó, ghi rõ lý do — đừng đoán số.
