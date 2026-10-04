# Supabase — đưa database LuxMap lên cloud

Chỉ **database** lên Supabase. API và MinIO vẫn chạy ở máy dev. Không dùng Supabase Auth, Storage hay
Data API: backend tự lo đăng nhập và phân quyền theo xã (Contract §2/§7), mọi đường khác đều đi vòng qua.

> ⚠️ **Ảnh và video KHÔNG nằm trong database.** MinIO giữ byte, các bảng chỉ giữ khoá (`object_key`, `thumbnail_key`) —
> ảnh khảo sát (`luxmap-survey`), ảnh phiếu (`luxmap-evidence`), clip và file thô (`luxmap-video`). Script dưới đây chỉ chép
> **hàng**. Chừng nào API vẫn chạy trên máy có MinIO cũ thì không cần đổi gì. Nếu API chạy ở **máy khác** (hay cả nhóm cùng
> trỏ vào Supabase), máy đó phải đọc được **cùng** kho file: một MinIO dùng chung, chép ba bucket bằng `mc mirror` **giữ
> nguyên khoá**. Thiếu file thì API trả `503 STORAGE_OBJECT_MISSING` cho ảnh, không hỏng dữ liệu. Máy chạy API cũng cần
> `ffmpeg` (worker cắt frame chạy trong tiến trình API).

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

Kỳ vọng: **đúng bằng lịch sử migration của `luxmap_dev`** — script so khớp từng dòng và dừng nếu lệch. Ngày 04/10/2026
là 29 migration, mới nhất `20261004134151_AddRepairEvidence`. Lệnh `CREATE EXTENSION IF NOT EXISTS
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
liệu, có bảng chưa nằm trong kế hoạch, hoặc thứ tự khoá ngoại sai. Trước COMMIT, script so **md5 nội dung** từng bảng
với nguồn; lệch một ký tự là huỷ cả transaction, chưa sequence nào bị đổi. Hai phiên luôn chạy UTF8 / ISO / UTC
bất kể shell đặt `PGCLIENTENCODING` hay `PGDATESTYLE` gì. Đích không phải localhost **bắt buộc** `sslmode=verify-full` kèm
`sslrootcert` trỏ tới file CA có thật; mọi biến `PG*` kế thừa từ shell bị bỏ (trừ `PGPASSWORD`). Lỗi PostgreSQL
in dạng rút gọn, không kèm `DETAIL` — dòng bị từ chối không bị in ra, nên hash mật khẩu không lọt vào terminal. Kế hoạch đo ngày 01/10/2026:

```text
administrative_unit 3 / bỏ 14 · app_user 4 / bỏ 14 · app_user_commune 9 / bỏ 21
road_segment 20 · feeder 3 · pole 217 · fixture 217 · iot_node 3 · feeder_control 3
fault_cluster 1 · fault 28 · work_order 3 · work_order_fault 11 · audit_event 0 · lux_reading 0
bỏ qua: refresh_token, __ef_migrations_history, spatial_ref_sys
```

**Bảng khảo sát và ảnh (thêm 04/10/2026, 16 bảng):** `work_order_segment`, `artifact_version` (sổ phiên bản thuật toán/model,
không theo xã — chép nguyên), `survey_sweep` và mọi bảng con (`survey_raw_file`, `survey_video_clip`, `survey_gps_sample`,
`survey_lux_sample`, `survey_processing_run`, `survey_frame`, `detection`, `survey_pass`, `pole_observation`), `luminance_baseline`,
`baseline_member`, `luminance_history`, `repair_evidence`. Bảng không có `commune_id` đi theo cha đã giữ (phiên, run, baseline);
phiên của xã test bị bỏ cùng toàn bộ con của nó. `pole_current_status` và `fault` nay **đứng sau** chuỗi khảo sát vì trỏ vào nó.

🔴 **Vòng khoá ngoại `survey_sweep` ↔ `survey_processing_run`** (`accepted_run_id` một chiều, `sweep_id` chiều kia) và
`ck_survey_sweep_review` (phiên `accepted` ⇔ có `accepted_run_id`) làm **không có thứ tự chép nào hợp lệ**. Script chép
phiên đã duyệt ở dạng *chưa duyệt* (`awaiting_review`, các cột duyệt `NULL`), chép run, rồi **trả lại** các cột duyệt từ
nguồn — cùng transaction, **trước** kiểm md5, nên trạng thái cuối trùng nguồn hoặc huỷ hết (`TWO_PASS` trong script).
Bảng mới có vòng tương tự phải khai vào `TWO_PASS`, không gỡ ràng buộc.

**Bảng mới luôn phải được thêm vào `PLAN` có chủ đích** — script dừng khi gặp bảng lạ. Ticket nào tạo bảng thì sửa script
trong cùng PR.

## 5. Nghiệm thu trước khi cả nhóm dùng

- **Data API bị chặn — đạt khi đủ CẢ BA, thiếu một là "chưa kết luận", không phải "đạt":**
  1. **Key đúng của project:** `GET https://<ref>.supabase.co/auth/v1/settings` kèm `apikey: <anon/publishable key>`
     trả **200**. Không phải 200 thì key sai / project sai — mọi lời từ chối ở bước 2 đều vô nghĩa.
  2. `GET https://<ref>.supabase.co/rest/v1/pole?select=pole_id&limit=1` với **cùng key** **không** trả 2xx. Ghi lại
     status và body. **`200 []` là KHÔNG đạt** (bảng vẫn mở, chỉ là RLS lọc hết).
  3. Ảnh chụp trang cài đặt Data API của project cho thấy **đã tắt**.

  Key chỉ dùng cho phép thử này, không đưa vào app.
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

## Diễn tập 04/10/2026 — 16 bảng khảo sát và ảnh

`luxmap_dev` chưa có dòng khảo sát nào, nên diễn tập từ **bản sao** `luxmap_copysrc` (pg_dump của `luxmap_dev`) có thêm
một chuỗi khảo sát đầy đủ ở COM-001 — phiên `accepted`, run, lượt, frame, detection, quan sát, baseline + member, lịch sử
(một điểm `not_observed`), trạng thái cột, sự cố CV trỏ vào quan sát, ảnh phiếu — và một phiên ở xã test. Cờ
`--source-db` chỉ nhận khi đích là localhost (thử với đích ở xa → dừng, exit 2).

- Đích `luxmap_copytgt`, chủ là role `rehearse` **không phải superuser**, PostGIS ở `extensions`: **29/29 migration**, kể cả
  trigger bất biến của các bảng khảo sát.
- `--apply`: **32 bảng** đúng số dòng và **trùng md5** với nguồn, **23 sequence** khớp; phiên `SWP-001` lên đích vẫn
  `accepted` trỏ đúng run; phiên và file thô của xã test bị bỏ lại. `--apply` lần hai → dừng, exit 2.
- Phá thử: bỏ lượt trả cột duyệt → `nội dung bảng survey_sweep lệch nguồn`, exit 2, đích `luxmap_copytgt2` còn 0 dòng,
  `pole_id_seq` vẫn `1|f`.

## Diễn tập 01/10/2026

DB `luxmap_copytest` trên container local, chủ là role `deploytest` **không phải superuser**, PostGIS cài
sẵn ở schema `extensions`, `search_path` của role như Supabase:

- `dotnet ef database update` với `Search Path=public,extensions`: **23/23 migration**, 18 bảng thuộc
  `deploytest`, `luxmap_format_id` và trigger audit append-only tạo được không cần superuser.
- `copy_dev_to_supabase.py --apply`: 16 bảng đúng số dòng, 13 sequence khớp, `POLE-0047` và `POLE-0104` có
  cùng toạ độ với nguồn; chạy `--apply` lần hai → dừng, exit 2, "đích đã có 3 dòng".
- API trỏ vào bản sao: đăng nhập `engineer`/`agency` được (hash mật khẩu giữ nguyên), bbox thực địa 114 cột,
  bbox mock 103 cột, `GET /faults` total 28.

Sau review của Codex (P1): đích có trigger cố ý sửa `segment_name` → script dừng ở md5 `road_segment`, exit 2,
đích còn 0 dòng, `pole_id_seq` vẫn `1|f`. Shell đặt `PGCLIENTENCODING=LATIN1`, `PGDATESTYLE='SQL, DMY'`: bản
cũ chép thành `PhÆ°á»ng Long PhÆ°á»c` mà vẫn báo ✅ exit 0; bản mới chép đúng, md5 16 bảng trùng nguồn.

Sau sửa P2-3/P2-4: URL máy chủ thật thiếu `sslmode`, dùng `require`, hay `verify-full` mà thiếu / sai đường dẫn CA
→ dừng trước khi kết nối. Shell có `PGHOSTADDR` lạ vẫn kết nối đúng đích. Đích gài CHECK làm hỏng dòng
`app_user`: bản cũ in `Failing row contains` kèm hash (2 lần), bản mới chỉ in tên ràng buộc.

Chưa diễn tập được: TLS `VerifyFull` qua pooler thật, quota kết nối, và việc tắt Data API — phải kiểm trên
project thật ở bước 5.
