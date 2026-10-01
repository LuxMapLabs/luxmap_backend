# Supabase — bản nháp triển khai database LuxMap

> **NHÁP 01/10/2026 — chưa thực hiện triển khai.** Chỉ database chuyển lên Supabase;
> API và MinIO vẫn ở máy dev. Không dùng Supabase Auth/Storage/Data API thay backend.
> Mọi thao tác dưới đây dành cho **Mỹ/Claude ở Phase 2 sau phê duyệt**; Codex dừng ở Phase 1.
> Quyết định và bằng chứng: [OPS-SUPABASE-p1](../../.ai/results/OPS-SUPABASE-p1.md).
> `[CHỜ D-x]` là cổng dừng thật, không phải giá trị mặc định đã chọn.

## Trước khi bắt đầu

1. Mỹ chốt D-1…D-7 trong báo cáo. Chưa chốt phương án/manifest thì không chạy migration/chuyển dữ liệu.
2. Đọc nguồn dữ liệu thật bằng bộ SELECT trong báo cáo. Phiên khảo sát không truy cập được Docker;
   **chưa có số đo rác test**, không sử dụng số trong task làm biên bản nghiệm thu.
3. Chốt commit dùng để triển khai (bản khảo sát: `e299334`), version PostgreSQL/PostGIS nguồn và đích,
   danh sách migration, người chạy, thời điểm freeze, chỗ giữ backup ngoài Git. `[CHỜ D-7]`
4. Chuẩn bị .NET SDK 10, dotnet-ef phù hợp EF 10, psql/pg_dump phù hợp version server, client TLS tin cậy.
   Không chạy test trong môi trường mang credential Supabase.

## Phần A — Mỹ làm trên Dashboard

### A1. Project và kết nối

- Tạo project ở **Southeast Asia (Singapore)**; lưu mật khẩu database vào password manager.
  Ghi project ref/region/version để đối chiếu nội bộ, không ghi mật khẩu vào biên bản.
- Chọn **Session pooler** trong Connect panel, IPv4, port **5432** theo thông tin project thực tế.
  Không tự ghép host từ tên vùng; username pooler thường có project ref, phải lấy đúng panel.
  Direct mặc định IPv6; transaction pooler port 6543 không thuộc phương án lần này.
  Session hỗ trợ prepared statements; row lock trong explicit transaction tự nó không bắt buộc session.
  [Nguồn Supabase](https://supabase.com/docs/guides/database/connecting-to-postgres).
- `[CHỜ D-3]` Tài khoản migration có quyền tạo/sở hữu object ứng dụng; runtime role và giới hạn pool
  phải được chốt theo quota thật. Role `postgres` của Supabase không có superuser đầy đủ.

### A2. Đóng đường đi vòng qua backend trước khi có dữ liệu

- Dashboard → Integrations → Data API (hoặc trang Data API tương ứng giao diện hiện hành), tắt
  **Enable Data API**. Khuyến nghị tắt toàn bộ vì LuxMap chỉ dùng PostgreSQL qua .NET.
  Nếu chọn chỉ bỏ schema `public` khỏi exposed schemas, phải chứng minh không schema/RPC nào khác
  cho truy cập dữ liệu LuxMap; không coi đây là tương đương tự động với tắt toàn bộ.
- Ghi bằng chứng cấu hình đã tắt; kiểm lại sau migration khi bảng `pole` thực sự tồn tại (C5).
  Grants/RLS ảnh hưởng quyền REST, nhưng query filter/SaveChanges của EF không chạy ở đường này.
  [Hướng dẫn chính thức](https://supabase.com/docs/guides/api/securing-your-api).
- Không tạo policy RLS mới để thay Contract hay đưa DB key cho FE/mobile. Không dùng service_role key
  để thử “quyền người dùng”.

### A3. PostGIS và schema

- Database → Extensions → bật **postgis**. `[CHỜ D-2]` Chọn schema riêng đã duyệt, đề xuất
  `extensions` nếu project cho phép; `gis` cũng hợp lệ nhưng cần search_path tương ứng.
- Xác nhận namespace thật bằng SELECT ở C1. Không mặc định `public`, không drop/recreate hoặc đổi schema
  extension đang có. Không đưa schema extension lên đầu search_path của EF: migration không khai schema
  nên bảng ứng dụng phải được tạo trong `public`.
- Hướng dẫn Supabase dùng schema riêng; `spatial_ref_sys` có ownership do nền tảng quản lý.
  [Nguồn PostGIS](https://supabase.com/docs/guides/database/extensions/postgis).

### A4. Secret và môi trường thực thi

`[CHỜ D-6]` Đề xuất **workspace triển khai riêng** tại commit đã chốt, không dùng chạy test.
Mỹ quản lý `.env` riêng tại đó hoặc môi trường không có `.env` trong cây thư mục cha.
`Program.cs:22` tự tìm `.env` lên thư mục cha và DotNetEnv 3.2.0 mặc định ghi đè biến shell cùng tên;
chỉ `export` hay `unset` không bảo đảm đúng đích. Không đặt cloud override vào `.env` workspace phát triển.
[Source DotNetEnv](https://github.com/tonerdo/dotnet-env/blob/v3.2.0/src/DotNetEnv/LoadOptions.cs).

Tên biến ứng dụng có thật: `ConnectionStrings__LuxMap`. Giá trị dùng cú pháp Npgsql, **không phải URI**:

```text
Host=<session-pooler-host>;Port=5432;Database=<database-from-panel>;Username=<user-from-panel>;Password=<secret>;SSL Mode=VerifyFull;Search Path=public,<postgis-schema>
```

Đây là mẫu, chưa phải chuỗi để chạy. Mật khẩu có ký tự đặc biệt phải encode/quote đúng Npgsql và định dạng
file môi trường; không dán vào shell history, issue hay output. Nếu CA chưa được trust, cài CA xác thực
được hoặc dùng tham số Npgsql `Root Certificate` trỏ tới CA đúng. **Không dùng Trust Server Certificate=true**.
Nhánh POSTGRES_* hiện không ép TLS; `SSL Mode=Require` được code nhận nhưng chỉ mã hoá,
không xác minh máy chủ. `[CHỜ D-3]` Chốt VerifyFull và pool size trước kết nối.
[Npgsql TLS](https://www.npgsql.org/doc/security.html).

Để host/EF CLI dựng được: cung cấp `JWT_SIGNING_KEY`, `MINIO_ENDPOINT`, `MINIO_ACCESS_KEY`,
`MINIO_SECRET_KEY`; nếu ngoài Development, cần `Cors__AllowedOrigins__0` là HTTPS origin thật hợp lệ.
Giữ MinIO local và bucket hiện có. Startup kiểm cấu hình MinIO, không ping dịch vụ; ảnh về sau cần
MinIO hoạt động. Repo chưa đăng ký Redis/Hangfire làm dependency startup thực tế.
Chỉ nhánh B mới cần bộ `SEED_*_PASSWORD`; không chạy `--seed` theo thói quen ở nhánh A.

## Phần C — chỉ sau khi Mỹ chốt, Mỹ/Claude thực hiện

### C1. Preflight chỉ đọc, xác nhận đúng đích

Tạo kết nối psql bằng host/port/user/database vừa lấy trong panel; đưa mật khẩu qua password manager
hoặc passfile có quyền riêng, không tham số dòng lệnh. psql dùng cú pháp libpq (`sslmode=verify-full`,
`sslrootcert` nếu cần), không nhận nguyên chuỗi Npgsql. Không nhúng credential vào tài liệu.

Trên đích, kiểm bằng SELECT và lưu output đã che thông tin nhận diện nhạy cảm:

```sql
SELECT current_database(), current_user, version(),
       current_setting('search_path') AS search_path, current_schemas(false);
SELECT ssl, version, cipher FROM pg_stat_ssl WHERE pid = pg_backend_pid();
SELECT rolname, rolsuper, rolcreatedb FROM pg_roles WHERE rolname = current_user;
SELECT extname, extversion, n.nspname AS extension_schema
FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace WHERE extname = 'postgis';
SELECT to_regtype('geometry') AS geometry_type,
       to_regprocedure('st_intersects(geometry,geometry)') AS intersects_function;
SELECT nspname, has_schema_privilege(current_user, oid, 'USAGE') AS can_use,
       has_schema_privilege(current_user, oid, 'CREATE') AS can_create
FROM pg_namespace WHERE nspname IN ('public','extensions','gis');
SELECT lanname, has_language_privilege(current_user, oid, 'USAGE') AS can_use
FROM pg_language WHERE lanname IN ('sql','plpgsql');
SELECT to_regclass('public.pole') AS pole_table,
       to_regclass('public.__ef_migrations_history') AS migration_table;
```

Kết quả pg_stat_ssl mô tả kết nối backend thấy ở PostgreSQL; qua pooler nó không tự chứng minh
TLS client → pooler. Việc đó phải dựa vào client VerifyFull thành công và cấu hình TLS đã chốt.
Nếu chưa resolve geometry thì dừng trước câu cast/hàm phụ thuộc geometry. Sau khi resolve:

```sql
SELECT postgis_full_version();
SELECT ST_SRID(ST_SetSRID(ST_MakePoint(106,10),4326)) AS source_srid,
       ST_SRID(ST_Transform(ST_SetSRID(ST_MakePoint(106,10),4326),3405)) AS internal_srid;
```

- Namespace/USAGE sai → quay lại D-2, không tự sửa migration.
- Role migrate thiếu CREATE public hoặc language USAGE → dừng D-3.
- SSL không đạt/host không đúng → dừng, không hạ chế độ TLS.
- Đích đã có bảng LuxMap/dữ liệu không dự kiến → dừng, không seed đè.
- SELECT này không thay thế kiểm chứng quyền CREATE FUNCTION/TRIGGER thực tế khi migration chạy.

### C2. Freeze nguồn và chốt manifest `[CHỜ D-1, D-4, D-5, D-7]`

1. Dừng mọi writer vào local (API/engine/job/test), giữ nguyên nguồn, không dọn rác trực tiếp.
2. Chụp backup nguồn bằng quy trình đã duyệt; kiểm đọc/restore ở môi trường phục hồi **riêng**, không
   ghi vào `luxmap_dev` để kiểm. Backup chứa dữ liệu nhạy cảm phải ngoài Git, quyền truy cập hạn chế.
3. Chạy bộ SELECT kiểm kê trong báo cáo; ký manifest gồm bảng, tập PK, số hàng theo xã/nguồn,
   fingerprint giá trị, tập FK, sequence state và các hàng loại trừ kèm lý do.
4. Giữ tên/ID `COM-001` Long Phước, `COM-002` Long Bình, `COM-070` mock và `study_site` đúng hàng.
   Giữ `POLE-0047` cùng dữ liệu của nó; không renumber theo thứ tự INSERT.
5. Đi hết closure FK: actor ngoài seed, tuyến liên xã, parent/root WO, bảng nối, lux, feeder_control,
   status, audit. Không dùng bộ lọc ba xã để bỏ cha còn được hàng giữ tham chiếu.
6. Test nằm trong xã giữ phải phân loại theo chứng cứ. Không dùng prefix tên hoặc “ngoài bốn username”
   làm quyết định xoá lịch sử thật. Mọi ngoại lệ cần Mỹ ký trước xuất dữ liệu.

### C3. Migration trên đích rỗng `[CHỜ D-2, D-3, D-6]`

Chỉ chạy trong workspace triển khai đã cấu hình ở A4. Các lệnh này **chưa chạy ở Phase 1**:

```bash
dotnet ef migrations script 0 -p src/LuxMap.Persistence -s src/LuxMap.Api -o /tmp/luxmap-supabase-migrations.sql
```

Đọc SQL trước apply: extension không cố tạo sai namespace, bảng/history thuộc public,
function ID trước default, hai trigger audit còn nguyên, không restore Supabase-owned object.
Không tạo migration mới, không chạy database update tới version cũ.

```bash
dotnet ef database update -p src/LuxMap.Persistence -s src/LuxMap.Api
```

Đối chiếu toàn bộ `public.__ef_migrations_history` với danh sách migration của commit, không chỉ dòng cuối
(kỳ vọng tại commit khảo sát: 23 migration, cuối `20260929125521_AddFaultReview`). Khi provider báo thiếu
quyền hoặc extension/schema lỗi: dừng, lưu lỗi đã che secret, mở D-item bổ sung; không cấp superuser,
không bỏ trigger hay SSL để chạy tiếp.

### C4. Chuyển dữ liệu — rẽ nhánh đúng quyết định `[CHỜ D-1]`

#### A. Migration + chép dữ liệu đã lọc (đề xuất, chưa chốt)

- Xuất **chỉ dữ liệu ứng dụng trong manifest**, cột tường minh; không dump/restore toàn cluster,
  ownership, grants, schema `auth/storage/extensions`, bảng lịch sử EF hoặc `spatial_ref_sys`.
  Không xuất xmin. Không dùng `--disable-triggers` hay `session_replication_role`.
- Công cụ xuất/nạp sẽ được viết/review ở Phase 2, chưa tồn tại ở đây. Không dùng nguyên
  `scripts/seed_mock_set.py --apply`: nó trỏ Docker và xoá toàn bảng.
- Thứ tự nạp gợi ý từ FK hiện tại (phải kiểm catalog nguồn/đích trước):
  1. administrative_unit, app_user, app_user_commune.
  2. road_segment, feeder, iot_node; rồi pole, fixture, pole_current_status, feeder_control.
  3. fault_cluster; rồi fault và lux_reading (actor và tài sản đã có).
  4. work_order theo cha/gốc trước con; rồi work_order_fault.
  5. audit_event sau khi đã có actor/xã/entity; refresh_token chỉ nếu D-5 cho giữ,
     xử lý self-FK replaced_by_token_id theo đồ thị, không đoán thứ tự ID là đủ.
- Dùng transaction nạp với fail-fast; constraint vẫn bật. Kiểm manifest trước commit dữ liệu.
  Audit lọc **trước** nạp: không copy hết rồi DELETE. Giữ audit_id bằng COPY cột tường minh
  (hỗ trợ identity ALWAYS), hoặc INSERT với OVERRIDING SYSTEM VALUE khi công cụ chọn INSERT.
  [PostgreSQL COPY](https://www.postgresql.org/docs/current/sql-copy.html).
- Bảo toàn geometry/SRID, NULL, UTC/timestamp, enum, data_source, password hash và thuật toán,
  scope người dùng, audit before/after/correlation_id; không in hash/token vào báo cáo.
- `[CHỜ D-5]` Sau nạp thành công và vẫn đóng ghi, đặt **sequence đích** qua vùng ID đã dùng:
  11 sequence ID hiển thị cùng identity audit/token; tra tên identity bằng pg_get_serial_sequence.
  Chọn floor theo quyết định: max manifest hoặc giữ high-water nguồn lớn hơn. Với bảng rỗng phân biệt
  `is_called=false` để lần cấp đầu đúng start_value; không dùng MAX text cho ID có prefix.
  Không cung cấp lệnh setval chạy mù ở bản nháp; script chính thức phải liệt kê từng sequence/giá trị
  để Mỹ review. setval không rollback cùng transaction — nếu nạp lỗi, đối chiếu lại toàn bộ trước retry.
  [PostgreSQL sequence](https://www.postgresql.org/docs/current/functions-sequence.html).
- Không chạy `--seed` sau copy để “sửa quyền”: seeder không bổ sung scope cho user đã tồn tại.

#### B. Dựng sạch + seed + nạp lại thực địa (chưa đáp ứng giữ lịch sử nếu dùng nguyên trạng)

- Chỉ được chọn khi Mỹ đã duyệt cách bảo toàn lịch sử và nguồn tái tạo. `--seed` tạo study_site bằng
  sequence, nên trên DB mới sẽ chiếm COM-001. `[CHỜ D-4]` Bootstrap các xã/seed_key/sequence đã duyệt
  trước seed để COM-070 là study_site, giữ COM-001/COM-002 cho dữ liệu thực địa.
- Sau bootstrap đã kiểm, lệnh seed có thật (chỉ Phase 2, nhánh B):

  ```bash
  dotnet run --project src/LuxMap.Api -- --seed
  ```

- Tạo mock bằng phiên bản công cụ đã duyệt phù hợp remote, rồi nạp thực địa với mapping ID tường minh.
  Script hiện có chưa dùng được: ngoài Docker còn DELETE không WHERE, guard lux/audit, mock ID có thể
  đụng dữ liệu thực địa. Không chạy script cũ sau khi đã nạp thực địa/lịch sử.
- Nạp lại bằng API import thông thường không giữ được ID; cần đối chiếu từng ID cũ/mới. Nếu yêu cầu giữ
  ID/lịch sử không đạt thì dừng, không đổi yêu cầu thành “migrate thành công”. Copy bổ sung fault/WO/lux/
  audit phải chịu toàn bộ quy tắc nhánh A. Mật khẩu/scope/refresh session không tự khôi phục bằng seed.

### C5. Nghiệm thu trước mở ghi

**Database:** chạy lại bộ SELECT kiểm kê trên đích, thay kỳ vọng bằng manifest được ký. Đối chiếu từng bảng,
xã, data_source, tập ID, giá trị hàng và FK (đếm bằng nhau vẫn có thể là sai hàng). Kiểm riêng study_site,
bốn user/scope, POLE-0047, audit_id/actor/before/after/timestamp, chuỗi WO. Kiểm cả constraint/index/trigger:

```sql
SELECT migration_id, product_version FROM public.__ef_migrations_history ORDER BY migration_id;
SELECT n.nspname, c.relname, t.tgname, t.tgenabled, pg_get_triggerdef(t.oid)
FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relname = 'audit_event' AND NOT t.tgisinternal;
SELECT conrelid::regclass AS table_name, conname, convalidated, pg_get_constraintdef(oid)
FROM pg_constraint WHERE connamespace = 'public'::regnamespace
ORDER BY conrelid::regclass::text, conname;
SELECT tablename, indexname, indexdef FROM pg_indexes
WHERE schemaname = 'public' ORDER BY tablename, indexname;
SELECT luxmap_format_id('POLE',10000,4) AS overflow_id;
SELECT sequencename, last_value, increment_by, cache_size FROM pg_sequences
WHERE schemaname = 'public' ORDER BY sequencename;
```

Kỳ vọng overflow là POLE-10000 (hàm thuần, không tăng sequence). Đối chiếu last_value/is_called và max ID
bằng query báo cáo, **không gọi nextval để “xem”**. Kiểm quyền runtime/DDL theo D-3; kiểm chức năng ghi,
lock cạnh tranh/xmin nếu cần phải có kịch bản Phase 2 riêng được duyệt, không lấy suite test dev chạy cloud.

**Data API phải bị chặn:** sau khi bảng pole đã tồn tại, Mỹ dùng HTTP client gửi hai GET sau trên đúng project:

```http
GET https://<project-ref>.supabase.co/rest/v1/pole?select=pole_id&limit=1
```

Lần 1 không key. Lần 2 kèm header `apikey: <publishable-or-legacy-anon-key-hợp-lệ>` lấy từ project,
không kèm user JWT. Key chỉ dùng cho **phép kiểm âm tính này**, không tích hợp vào ứng dụng.
Lưu status/body đã che key. Cả hai phải bị từ chối do đường Data API đã tắt/không expose bảng;
`200 []` không đạt, lỗi mạng/DNS hay key sai cũng không chứng minh cấu hình đúng. Không cố định mã HTTP
trước khi đo vì gateway có thể khác; đối chiếu với dashboard và bảo đảm đang thử đúng key/project.
Không thử RPC ghi hoặc endpoint có tác dụng phụ. [API keys](https://supabase.com/docs/guides/getting-started/api-keys).

**API LuxMap:** chạy API local bằng workspace vận hành, đúng DB remote; dùng flow đăng nhập hiện có
(có thể ghi refresh token, vì vậy chỉ làm ở Phase 2 được duyệt). Không đưa token/mật khẩu vào log.

- `GET /api/v1/auth/me`: role/commune_ids khớp manifest.
- `GET /api/v1/poles?bbox=<minLng,minLat,maxLng,maxLat>&commune_id=<xã-trong-scope>`:
  bbox lấy từ dữ liệu giữ, đủ nhỏ, trả GeoJSON, tọa độ 4326 `[lng,lat]`; đối chiếu tập ID chứ không
  chỉ 200. Kiểm `GET /api/v1/poles/POLE-0047` bằng tài khoản có scope COM-070.
- Không token → 401. Tài khoản hạn chế phải không thấy xã ngoài scope; filter commune ngoài quyền
  trả 403 theo Contract. Nếu chưa có cặp user/xã để kiểm âm tính, ghi thiếu coverage, không sửa scope
  hoặc tạo test user trên shared DB tuỳ tiện.
- Không kỳ vọng mock status/luminance chưa được seed thành dữ liệu thật: đối chiếu output local cùng
  commit/manifest; `pole_current_status` không do script mock ghi.
- Kiểm luồng ảnh **qua API** nếu có dữ liệu/endpoint đã triển khai; giữ MinIO loopback, không công khai URL.

Chỉ mở ghi sau khi Mỹ ký kết quả DB + API + Data API và phương án phiên đăng nhập D-5.
Nếu đổi JWT signing key thì token cũ bị vô hiệu; nếu chỉ bỏ refresh_token thì access JWT còn hạn vẫn có thể dùng.

### C6. Quay lui `[CHỜ D-7]`

- **Chưa có ghi mới trên Supabase:** dừng API, chuyển workspace vận hành về cấu hình local đã xác nhận,
  khởi động lại để bỏ connection pool cũ; kiểm /auth/me và bbox. Nguồn local/MinIO đã giữ nguyên trong freeze.
  Đồng bộ quyết định JWT key/session; không tự xoay key để xử lý lỗi kết nối.
- **Đã có ghi mới:** dừng writer cả hai phía, backup đích, lập chênh lệch gồm audit và sequence,
  reconcile bằng kế hoạch được duyệt rồi mới chuyển. Không quay về bản local cũ và bỏ các cập nhật mới.
- Nếu migration/nạp thất bại khi chưa cutover: giữ API ở local; ghi trạng thái đích và lỗi đã che secret.
  Không retry seed/destructive cleanup vào đích có audit. Cách dựng lại đích cần phê duyệt riêng.
- Không `database update 0`/Down trên dữ liệu sống. `DropSolarFixtures.Down` không khôi phục dữ liệu;
  `AddFaultReview.Down` có thể fail vì audit fault; `AddAuditEvent.Down` xoá audit. Quay lui kết nối khác
  với quay lui schema. Không xoá project/backup nguồn trong ticket này.

## KHÔNG BAO GIỜ

- Chạy **dotnet test vào Supabase** hoặc suite fixture với credential cloud: test có setval, purge audit
  và dữ liệu rác. Tách cả file môi trường lẫn process; unset shell chưa đủ khi host đọc lại `.env`.
- Dùng anon/publishable/service_role key làm đường truy cập dữ liệu LuxMap cho FE/mobile; ngoại lệ duy nhất
  ở tài liệu này là phép kiểm GET âm tính do Mỹ làm tại C5.
- Mở Data API để “test cho tiện”, bỏ auth/SSL verification, hoặc coi audit_purge là quyền chỉ superuser có.
- Mở MinIO/ảnh bằng URL công khai hoặc presigned URL để thay proxy API theo phạm vi xã.
- Copy toàn DB rồi xoá rác, tắt trigger/FK, chạy seed_mock_set.py nguyên trạng trên DB dùng chung,
  hay chỉnh sequence nguồn để “đẹp ID”.
- Commit `.env`, passfile, backup, hash mật khẩu/token, connection string thật hoặc key.

## Trạng thái bàn giao

Phase 1 chỉ tạo báo cáo khảo sát và bản nháp này. **Chưa thực hiện A/C, chưa triển khai thành công.**
Mỹ chốt D-item và bổ sung output kiểm kê; Mỹ/Claude mới tiến hành Phase 2 theo các cổng dừng trên.
