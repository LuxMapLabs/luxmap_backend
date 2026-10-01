# Supabase — đưa database LuxMap lên cloud

Chỉ **database** lên Supabase. API và MinIO vẫn chạy ở máy dev. Không dùng Supabase Auth, Storage hay
Data API: backend tự lo đăng nhập và phân quyền theo xã (Contract §2/§7), mọi đường khác đều đi vòng qua.

Quyết định D-1…D-7 chốt 01/10/2026, khảo sát và số đo ở
[OPS-SUPABASE-p1](../../.ai/results/OPS-SUPABASE-p1.md). Toàn bộ quy trình dưới đây đã **diễn tập trên một
DB local mô phỏng Supabase** (role không phải superuser, PostGIS ở schema `extensions`) — xem mục cuối.

## Đã chốt

| | |
|---|---|
| D-1 | Chạy migration trên Supabase rồi **chép dữ liệu đã lọc** từ `luxmap_dev` — giữ nguyên mọi ID (`POLE-0047`, `COM-001`…) |
| D-2 | PostGIS ở schema **`extensions`**; connection string có `Search Path=public,extensions` |
| D-3 | **Session pooler**, `SSL Mode=VerifyFull`, `Maximum Pool Size=10`; role `postgres` cho cả migration và runtime (tách role riêng là việc sau) |
| D-4 | Giữ 3 xã `COM-070` (mock), `COM-001` Long Phước, `COM-002` Long Bình; 4 tài khoản seed kèm mật khẩu cũ; mọi bảng nghiệp vụ. Bỏ 14 xã + 14 tài khoản test |
| D-5 | Sequence ở đích = giá trị hiện tại của nguồn. **Không** chép refresh token — mọi người đăng nhập lại |
| D-6 | Connection string Supabase chỉ nằm trong **thư mục triển khai riêng**, không bao giờ ở `.env` của repo dev |
| D-7 | Lúc chép: tắt API local, không chạy test. DB 53 MB, vài phút |

## 1. Dashboard Supabase (Mỹ làm)

1. Tạo project vùng **Southeast Asia (Singapore)**. Mật khẩu DB lưu trong password manager.
2. 🔴 **Tắt Data API** (Integrations → Data API, hoặc trang Data API của giao diện hiện hành). Không tắt thì
   PostgREST mở các bảng `public` qua key của project — đường đọc/ghi đi vòng qua toàn bộ phân quyền theo xã
   và qua cả bảng audit. Làm **trước** khi có dữ liệu.
3. Database → Extensions → bật **postgis**, schema **`extensions`**.
4. Connect → **Session pooler**: ghi lại host, port (5432), database, user (dạng `postgres.<ref>`).
   Không dùng kết nối trực tiếp (mặc định chỉ IPv6) hay Transaction pooler (6543).
5. Tải **chứng chỉ CA** của project (trang cài đặt Database, mục SSL) về một chỗ ngoài repo.

## 2. Thư mục triển khai riêng (D-6)

`Program.cs` nạp `.env` bằng DotNetEnv, và nó **ghi đè** biến môi trường cùng tên, tìm từ thư mục hiện tại
lên các thư mục cha. Chuỗi Supabase nằm trong `.env` của repo dev thì lần `dotnet test` kế tiếp sẽ ghi thẳng
lên cloud. Vì vậy dùng một worktree **nằm cạnh**, không nằm trong, repo:

```bash
git worktree add --detach ../luxmap_deploy <commit-đã-chốt>
cp .env ../luxmap_deploy/.env        # giữ JWT, MinIO, CORS, SEED_* như dev
```

Trong `../luxmap_deploy/.env`, thêm **một** dòng (cú pháp Npgsql, không phải URI):

```text
ConnectionStrings__LuxMap=Host=<pooler-host>;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<mật-khẩu>;SSL Mode=VerifyFull;Root Certificate=<đường-dẫn-CA>;Search Path=public,extensions;Maximum Pool Size=10
```

**Không chạy `dotnet test` trong thư mục này.** Không bao giờ dùng `Trust Server Certificate=true`.

## 3. Migration

```bash
cd ../luxmap_deploy
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
```

Kỳ vọng: 23 migration, mới nhất `20260929125521_AddFaultReview`. Lệnh `CREATE EXTENSION IF NOT EXISTS
postgis` trong migration không làm gì vì PostGIS đã bật ở bước 1. Lỗi quyền hay extension → **dừng**, không
cấp thêm quyền, không gỡ trigger hay SSL để chạy tiếp.

## 4. Chép dữ liệu

Tắt API local và mọi lượt test. Chạy từ bất kỳ thư mục nào trên máy có container `luxmap_postgres`:

```bash
export LUXMAP_TARGET_URL='postgresql://postgres.<ref>@<pooler-host>:5432/postgres?sslmode=verify-full&sslrootcert=<đường-dẫn-CA>'
read -s PGPASSWORD && export PGPASSWORD     # gõ mật khẩu, không để lại trong history
python3 scripts/copy_dev_to_supabase.py           # chỉ kiểm, in kế hoạch, không ghi
python3 scripts/copy_dev_to_supabase.py --apply   # chép trong MỘT transaction rồi tự đối chiếu
```

Script **từ chối** nếu: nguồn không phải `luxmap_dev`, lịch sử migration hai bên khác nhau, đích đã có dữ
liệu, có bảng chưa nằm trong kế hoạch, hoặc thứ tự khoá ngoại sai. Kế hoạch đo ngày 01/10/2026:

```text
administrative_unit 3 / bỏ 14 · app_user 4 / bỏ 14 · app_user_commune 9 / bỏ 21
road_segment 20 · feeder 3 · pole 217 · fixture 217 · iot_node 3 · feeder_control 3
fault_cluster 1 · fault 28 · work_order 3 · work_order_fault 11 · audit_event 0 · lux_reading 0
bỏ qua: refresh_token, __ef_migrations_history, spatial_ref_sys
```

## 5. Nghiệm thu trước khi cả nhóm dùng

- **Data API bị chặn:** `GET https://<ref>.supabase.co/rest/v1/pole?select=pole_id&limit=1`, một lần không key,
  một lần kèm header `apikey: <anon/publishable key>`. Cả hai phải bị từ chối; **`200 []` là KHÔNG đạt**.
  Key chỉ dùng cho phép thử này.
- **API:** `cd ../luxmap_deploy && dotnet run --project src/LuxMap.Api`, đăng nhập `engineer` / `agency`, rồi
  `GET /api/v1/poles?bbox=106.82,10.81,106.86,10.86` → 114 cột, `bbox=106.48,10.96,106.51,10.98` → 103 cột,
  `GET /api/v1/faults` → `total` 28. (`GET /poles/{id}` chưa có — BE-20.)
- **Công cụ SQL:** phiên nào không có `extensions` trong `search_path` sẽ báo `function st_astext(...) does not
  exist` — đặt `SET search_path = public, extensions;` trước.

## 6. Quay lui

- **Chưa ai ghi lên Supabase:** chỉ cần quay về chạy API từ repo dev — `luxmap_dev` không bị đụng tới.
- **Đã có ghi mới:** dừng ghi cả hai phía, sao lưu Supabase, đối chiếu chênh lệch rồi mới chuyển. Không quay
  về bản local cũ mà bỏ cập nhật mới.
- Không bao giờ `database update 0` hay `Down()` trên dữ liệu thật: `DropSolarFixtures.Down` không khôi phục
  dữ liệu, `AddAuditEvent.Down` xoá audit.

## KHÔNG BAO GIỜ

- Chạy `dotnet test` với connection string Supabase — test đẩy sequence, để lại rác, bật GUC xoá audit.
- Đưa anon / service_role key cho FE hay mobile, hoặc bật lại Data API "cho tiện".
- Mở ảnh MinIO bằng URL công khai hay presigned (BE-11 quy tắc 1).
- Chạy `scripts/seed_mock_set.py --apply` lên DB dùng chung — nó `DELETE` toàn bảng.
- Commit `.env`, chứng chỉ, file backup hay connection string thật.

## Diễn tập 01/10/2026

DB `luxmap_copytest` trên container local, chủ là role `deploytest` **không phải superuser**, PostGIS cài
sẵn ở schema `extensions`, `search_path` của role như Supabase:

- `dotnet ef database update` với `Search Path=public,extensions`: **23/23 migration**, 18 bảng thuộc
  `deploytest`, `luxmap_format_id` và trigger audit append-only tạo được không cần superuser.
- `copy_dev_to_supabase.py --apply`: 16 bảng đúng số dòng, 13 sequence khớp, `POLE-0047` và `POLE-0104` có
  cùng toạ độ với nguồn; chạy `--apply` lần hai → dừng, exit 2, "đích đã có 3 dòng".
- API trỏ vào bản sao: đăng nhập `engineer`/`agency` được (hash mật khẩu giữ nguyên), bbox thực địa 114 cột,
  bbox mock 103 cột, `GET /faults` total 28.

Chưa diễn tập được: TLS `VerifyFull` qua pooler thật, quota kết nối, và việc tắt Data API — phải kiểm trên
project thật ở bước 5.
