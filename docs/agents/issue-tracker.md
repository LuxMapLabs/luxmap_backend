# Issue tracker: giao thức `.ai/`

Repo này **không** dùng GitHub Issues. Việc được theo dõi bằng giao thức `.ai/` (quy ước đầy đủ: `.ai/README.md`).

## Quy ước

- Một ticket = một file `.ai/tasks/<mã>.md`, dựng từ `.ai/tasks/_TEMPLATE.md` (front matter + Bối cảnh, Phải đọc trước,
  Yêu cầu, **KHÔNG ĐƯỢC làm**, Phase 1 dừng ở đâu).
- Mã lấy từ `docs/tasks-backend.csv` (`BE-xx`). Việc ngoài CSV dùng mã mô tả viết hoa (`POLE-NOTE`, `OPS-SCHEMA`).
- Trạng thái nằm ở `status:` trong front matter; ai giữ ticket ở `owner:`. Ánh xạ vai trò triage: `triage-labels.md`.
- Kết quả: `.ai/results/<mã>-p1.md` (khảo sát, không sửa code) → `<mã>-p2.md` (hiện thực). Review: `.ai/reviews/<mã>-by-<agent>.md`.
- `.ai/tasks/current.md` là con trỏ phiên, **không vào git** — không coi nó là ticket.

## Khi skill nói "publish to the issue tracker"

Tạo `.ai/tasks/<mã>.md` từ template với `status: draft`. Nếu là việc mới ngoài CSV, ghi thêm một dòng vào
`tracking.html` (mục việc còn lại) — tiến độ chỉ sống ở đó.

## Khi skill nói "fetch the relevant ticket"

Đọc `.ai/tasks/<mã>.md`, rồi các file `.ai/results/<mã>*.md` và `.ai/reviews/<mã>*.md` nếu có.

## Bình luận / trao đổi

Nối vào cuối file ticket dưới heading `## Ghi chú triage`. Kết quả khảo sát và bằng chứng chạy thật thì vào `results/`, không vào đây.

## `.ai/` KHÔNG nhận bốn loại phát hiện

Deviation → `docs/contract-drift.md` · luật nhiều ticket → Contract (sau khi duyệt) · ràng buộc kỹ thuật → `CLAUDE.md` ·
tiến độ → `tracking.html`. Ghi vào `.ai/` là WP5/WP6 không bao giờ đọc được.

## Wayfinding

- **Map**: `.ai/tasks/<effort>-map.md`.
- **Ticket con**: `.ai/tasks/<effort>-NN-<slug>.md`, đánh số từ `01`; dòng `Type:` (`research`/`prototype`/`grilling`/`task`).
- **Chặn**: dòng `Blocked by: NN, NN`; claim = `status: in_progress` + `owner:`; xong = `status: done` + trả lời dưới
  `## Answer`, rồi thêm con trỏ vào mục Decisions-so-far của map.
