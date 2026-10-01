---
ticket: OPS-SUPABASE
title: Đưa database lên Supabase (PostgreSQL + PostGIS) — khảo sát tương thích, checklist, kế hoạch chuyển dữ liệu
status: blocked
phase: 1
owner: codex
branch: chore/supabase-deploy
contract_refs:
  - "mục 7 — phân quyền theo địa bàn (không được có đường đọc/ghi nào đi vòng qua API)"
  - "mục 1.2 — ID hiển thị do DB sinh; ngoại lệ hệ thống D-6"
depends_on:
  - "Phần A (Mỹ làm trên dashboard) — Phase 2 mới cần, Phase 1 KHÔNG cần"
---

## Bối cảnh

Mỹ chọn **Supabase** làm nơi đặt database dùng chung (01/10/2026). Lý do loại Firebase: Firestore là
NoSQL, không có PostGIS, không chạy được EF Core migration, function `luxmap_format_id`, trigger
append-only của audit hay khoá ngoại `Restrict`. Supabase là PostgreSQL thật nên giữ nguyên được backend.

Chỉ chuyển **database**. API và MinIO vẫn chạy ở máy dev — ngoài phạm vi ticket này.

Dữ liệu cần giữ trong `luxmap_dev` (đo 01/10/2026, DB 53 MB, PostGIS 3.5.3):
- bộ mock FO-26 ở `COM-070` (103 cột, ID tường minh `POLE-0001…`, FE hardcode `POLE-0047`);
- dữ liệu thực địa đầu tiên: `COM-001` Phường Long Phước (36 cột), `COM-002` Phường Long Bình (78 cột),
  17 tuyến, 114 bóng — xã tạo bằng SQL với ID tường minh;
- 4 tài khoản seed và quyền xã của chúng; sự cố, phiếu công việc, audit.

`luxmap_dev` còn **rác do test để lại** (ví dụ 14 xã tên `BE-12a home/foreign …`, tài khoản `be12a-*`,
sequence bị đẩy rất cao). Không được mang rác lên môi trường dùng chung.

## Phải đọc trước

- `AGENTS.md` (= `CLAUDE.md`) — đặc biệt: BE-11 quy tắc 1 (mọi byte qua API), mục 1c (guard `SaveChanges`),
  BE-23a (audit append-only, GUC `luxmap.audit_purge`, "append-only không chặn người có quyền SQL"),
  BE-REVIEW-02 ràng buộc 7/7b (sequence bị test đẩy, `seed_mock_set.py`, D-6)
- `README.md` mục hạ tầng dev, seed, nạp bộ mock; `.ai/context/commands.md`
- `src/LuxMap.Persistence/LuxMapConnectionString.cs`, `.env.example`
- Toàn bộ `src/*/Migrations/*.cs` (tìm `CREATE EXTENSION`, `CREATE FUNCTION`, `CREATE TRIGGER`, `current_setting`)
- `scripts/seed_mock_set.py` (đang chạy SQL qua `docker exec … psql`)
- `src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs` (seed xã `study_site` = "Commune 01")

## Checklist tổng (để Codex kiểm và hoàn thiện thành tài liệu)

**Phần A — Mỹ làm trên dashboard Supabase (cần tài khoản, agent KHÔNG làm):**

1. Tạo project, vùng **Southeast Asia (Singapore)**, mật khẩu DB mạnh, lưu vào password manager.
2. 🔴 **Tắt Data API cho schema `public`** (hoặc tắt hẳn Data API). Mặc định PostgREST mở mọi bảng
   `public` qua anon key — nếu không tắt, đó là đường đọc/ghi **đi vòng qua toàn bộ phân quyền theo xã**
   (Contract mục 7) và qua cả bảng audit. Kiểm: gọi `GET https://<ref>.supabase.co/rest/v1/pole` với
   anon key phải bị từ chối.
3. Bật extension **PostGIS** trong Database → Extensions.
4. Lấy connection string **Session pooler** (IPv4; kết nối trực tiếp cổng 5432 mặc định chỉ IPv6).
   **Không dùng Transaction pooler** (6543): repo dùng `SELECT … FOR UPDATE` trong transaction (BE-19)
   và prepared statement.
5. ~~Đặt chuỗi kết nối vào `.env` local~~ — **SAI, đã sửa sau Phase 1:** `Program.cs` nạp `.env` bằng
   DotNetEnv và nó **ghi đè** biến shell, nên chuỗi cloud trong `.env` dev sẽ kéo cả `dotnet test` lên
   Supabase. Đặt ở một workspace triển khai riêng (D-6 trong `results/OPS-SUPABASE-p1.md`). Không commit.

**Phần B — Codex, Phase 1 (ticket này):** xem mục Yêu cầu.

**Phần C — Phase 2, sau khi Mỹ chốt D-item:** chạy migration lên Supabase, chuyển dữ liệu theo phương án
đã chốt, kiểm đếm, kiểm bảo mật — do Mỹ hoặc Claude chạy, không phải Codex.

## Yêu cầu — Phase 1

1. **Khảo sát tương thích**, mỗi kết luận kèm bằng chứng (đường dẫn file + dòng, hoặc output lệnh):
   - Migration nào cần quyền mà role `postgres` của Supabase (không phải superuser) có thể không có:
     `CREATE EXTENSION postgis` (schema nào? Supabase hay đặt ở `extensions`), `CREATE FUNCTION`,
     `CREATE TRIGGER`, kiểu `geometry` không ghi schema có resolve được qua `search_path` không.
   - GUC tuỳ biến `luxmap.audit_purge`: role thường có `SET LOCAL` được không (chỉ test dùng).
   - `LuxMapConnectionString`: đường `ConnectionStrings__LuxMap` có nhận nguyên chuỗi kèm
     `SSL Mode=Require` không; đường ghép từ `POSTGRES_*` có thiếu SSL không.
   - Npgsql/EF với Supavisor **session mode**: có gì cần đổi (prepared statements, `xmin`, `FOR UPDATE`).
   - Script/tài liệu nào **giả định Docker local** (`docker exec … psql` trong `seed_mock_set.py`, README)
     — sẽ không chạy được với Supabase.
   - API lúc khởi động có đòi dịch vụ nào khác (MinIO, Redis) — chỉ ghi nhận, không xử lý.
2. **Đo rác test trong `luxmap_dev`** (chỉ ĐỌC): số xã / tài khoản / cột / tuyến / sự cố / audit không
   thuộc `COM-070`, `COM-001`, `COM-002` và 4 tài khoản seed; giá trị các sequence. Nếu sandbox không
   chạy được `docker exec`, ghi rõ câu SQL để Mỹ tự chạy — **không đoán số**.
3. **Viết nháp `docs/deploy/supabase.md`**: Phần A + C đầy đủ từng bước, lệnh chạy migration lên
   Supabase, cách kiểm sau khi chuyển (đếm hàng theo xã, `GET /poles` qua API, kiểm Data API bị chặn),
   cách quay lui, và mục **KHÔNG BAO GIỜ**: chạy `dotnet test` vào Supabase (test đẩy sequence, để rác),
   dùng anon key, mở MinIO/ảnh qua URL công khai.
4. **Đề xuất phương án chuyển dữ liệu thành D-item** (không tự chọn), tối thiểu so sánh:
   - (A) chạy migration trên Supabase rồi chép **dữ liệu** từ `luxmap_dev`, loại rác test — giữ nguyên
     ID (`POLE-0047`, `COM-001`/`COM-002`/`COM-070`), nhưng phải xử lý: audit append-only (trigger chặn
     DELETE, nên phải lọc **trước** khi chép), FK `Restrict`, sequence phải `setval` qua vùng đã dùng;
   - (B) DB mới tinh: migrate + `--seed` + `seed_mock_set.py` + nạp lại dữ liệu thực địa — sạch, nhưng
     seeder tạo `COM-001` = "Commune 01" cho bộ mock, **đụng** `COM-001` Phường Long Phước.
   Nêu rõ với mỗi phương án: bước, rủi ro, cái gì mất, cách kiểm.

Tiêu chí xong Phase 1:

- [ ] `.ai/results/OPS-SUPABASE-p1.md` có: bảng tương thích (mỗi dòng có bằng chứng), số đo rác test
      (hoặc câu SQL nếu không chạy được), D-item phương án chuyển dữ liệu, D-item khác nếu phát hiện
- [ ] `docs/deploy/supabase.md` bản nháp, đánh dấu chỗ phụ thuộc D-item là `[CHỜ D-x]`
- [ ] `git status` chỉ có hai file trên (cộng file khác nếu thật sự cần, có giải thích trong results)

## KHÔNG ĐƯỢC làm

- **Không kết nối, đọc hay ghi Supabase** dưới bất kỳ hình thức nào.
- **Không đọc `.env`** và không in mật khẩu / connection string thật ra output hay file — `.env` chứa
  secret. Cần biết biến nào tồn tại thì đọc `.env.example`.
- **Không ghi gì vào `luxmap_dev` hay `luxmap_test`**: chỉ `SELECT`. Không `DELETE`, `UPDATE`, `TRUNCATE`,
  không bật `luxmap.audit_purge`.
- Không sửa migration, `src/`, `docs/api-contract-v1.1.md`, `docs/openapi/*`. Phát hiện cần sửa code →
  ghi thành D-item.
- Không tự chọn phương án chuyển dữ liệu — ghi D-item.
- Không `git add -A` / `git add .`, không commit, không push.
- Không chạy `dotnet test` (DB dùng chung, sẽ va chạm sequence).
- Không ghi deviation vào `.ai/` — nếu có, ghi trong results để Mỹ chuyển vào `docs/contract-drift.md`.

## Phase 1 dừng ở đâu

Viết xong `results/OPS-SUPABASE-p1.md` và bản nháp `docs/deploy/supabase.md` thì **dừng hẳn**, chờ Mỹ
chốt D-item. Không viết script chuyển dữ liệu ở Phase 1, kể cả khi đã rõ phải làm gì.
