---
ticket: OPS-SUPABASE
phase: 1
agent: codex
branch: chore/supabase-deploy
date: 2026-10-01
status: done-p1-awaiting-decisions
---

# OPS-SUPABASE — khảo sát Phase 1

## Kết luận và giới hạn bằng chứng

**Có cơ sở tương thích PostgreSQL, chưa xác nhận chạy được trên project Supabase.** Những cổng cần
chốt trước Phase 2: schema PostGIS/search_path, tập dữ liệu được giữ cùng toàn bộ tham chiếu, sequence,
và cách tách cấu hình triển khai khỏi cấu hình test. Không có migration nào được chạy trong phiên này.

Đã đọc theo thứ tự yêu cầu: `AGENTS.md` → `.ai/README.md` → `.ai/context/sources.md` →
`.ai/tasks/OPS-SUPABASE.md` → `.ai/context/commands.md`; sau đó README, code cấu hình/seed,
Contract §1.2, §2 (phân quyền; task còn gọi mục 7 theo cách đánh số cũ), migration và tài liệu chính thức.
Bản nháp vận hành: [docs/deploy/supabase.md](../../docs/deploy/supabase.md).

- ✅ Kiểm chứng tĩnh từ repo tại HEAD `e299334`; nguồn web là tài liệu public, không phải project của Mỹ.
- ⚠️ Không đo được DB local: sandbox chặn Docker socket. Các số 53 MB, PostGIS 3.5.3,
  103/36/78 cột, 17 tuyến, 114 bóng và 14 xã test trong task là **thông tin đầu vào**, không phải số đo phiên này.
- ⚠️ Không xác nhận version, grants, schema, TLS, pool size hay trạng thái Data API trên Supabase.
- 📌 Tập giữ ban đầu là `COM-070`, `COM-001`, `COM-002` và username `admin/agency/engineer/crew`.
  Đây chỉ là tiêu chí kiểm kê; **ngoài tập giữ chưa đồng nghĩa là rác**, trong tập giữ cũng có thể có test.

## Bằng chứng terminal và phạm vi thay đổi

```text
$ git status --short --branch
## chore/supabase-deploy...origin/dev [ahead 1]
?? ".ai/context/FA26SE222 v1.1.docx"
?? .ai/context/tracking.html
?? .ai/results/review1/
?? LuxMap_TaskList_v2.1.xlsx
?? img/
?? img_osm/
?? img_out6/
?? img_out7/

$ git rev-parse --short HEAD
e299334

$ docker exec luxmap_postgres psql -X -U luxmap -d luxmap_dev -v ON_ERROR_STOP=1 \
    -c 'SELECT current_database(), current_user, version();'
permission denied while trying to connect to the docker API at unix:///Users/nhm809/.docker/run/docker.sock
```

Không thử đường khác để vượt sandbox. Không đọc `.env`, không gọi Docker Compose (có thể tự đọc `.env`),
không khởi động host/EF CLI/test. Không bật GUC, không gọi `nextval`/`setval`, không ghi DB.
Tám mục untracked phía trên có sẵn, giữ nguyên. Chỉ tạo hai file yêu cầu; không sửa `tracking.html`
vì task/current chỉ cấp quyền ghi hai file và Phase 1 phải dừng. Không stage, commit hay push.

Khảo sát dùng skill `research`; agent phụ chỉ đọc tài liệu public và trả dẫn nguồn, không ghi file.

## Bảng tương thích

Đường dẫn migration dưới đây đều ở `src/LuxMap.Persistence/Migrations/`.
“Có điều kiện” là suy luận từ code + tài liệu, **không** phải kết quả chạy Supabase.

| Hạng mục | Bằng chứng repo | Kết luận / điều kiện Phase 2 |
|---|---|---|
| PostgreSQL non-superuser | Quét migration không thấy tạo role, tablespace, `SECURITY DEFINER`, hay `session_replication_role` | Supabase `postgres` không phải superuser [S1]. Không có lý do mặc định cấm DDL ứng dụng; phải xác nhận ownership và grants thật. Không restore owner/role local hay các schema do Supabase quản lý. |
| Extension PostGIS | `20260829083507_InitialIdentity.cs:15–16` khai annotation `Npgsql:PostgresExtension:postgis` không schema; `LuxMapDbContext.cs:133` cũng không schema | Không có literal `CREATE EXTENSION` trong C#: provider sinh từ annotation. Bật PostGIS trước bằng dashboard, kiểm extension namespace, rồi review SQL EF sinh ở Phase 2. Không giả định role thường tự cài extension bất kỳ được. Supabase hỗ trợ PostGIS trong schema riêng [S2]. **D-2**. |
| Kiểu geometry và hàm không qualify | `20260901082917_AddAssetEntities.cs:34,51,72`, `20260928100335_AddIotNodes.cs:25`: `geometry(...,4326)`; `scripts/seed_mock_set.py:73–75`: `ST_SetSRID/ST_GeomFromGeoJSON` | `geometry`, `ST_*` cần resolve qua search_path và USAGE schema. Đề xuất `public,extensions` chỉ khi PostGIS thật ở `extensions`; nếu ở `gis` thì dùng namespace đó. Giữ `public` đầu để migration không tạo bảng app trong schema extension. Npgsql có `Search Path` [S3,S4]. Chưa kiểm NTS/3405 trên đích. |
| Hàm ID | `20260901084931_FixPrefixedIdOverflow.cs:24–33` tạo hàm SQL IMMUTABLE/STRICT; default gọi từ dòng 35 trở đi | Cần CREATE trên schema, USAGE language/type; replace cần sở hữu hàm. SQL trusted không đòi superuser [S5]. Hàm phải có trước default; không gộp/đảo migration. |
| Audit function + trigger | `20260927161055_AddAuditEvent.cs:75–96`: PL/pgSQL, 2 trigger BEFORE UPDATE/DELETE và BEFORE TRUNCATE | Role sở hữu bảng app + EXECUTE function/TRIGGER đủ theo PostgreSQL [S5,S6]; PL/pgSQL trusted. INSERT audit không bị hai trigger này chặn. Không tắt trigger để chuyển dữ liệu. |
| Custom GUC | Cùng migration dòng 79: `current_setting('luxmap.audit_purge', true)`; `tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:96` và `AssetImportFixture.cs:143` dùng SET LOCAL | Custom option hai phần không tự trở thành đặc quyền superuser; role thường có thể đặt placeholder custom GUC. SET LOCAL chỉ sống trong transaction [S7]. Đây **không** là hàng rào bảo mật trước người có SQL. Không chạy thử GUC trong Phase 1 hoặc trên DB dùng chung. |
| Connection string nguyên chuỗi | `src/LuxMap.Persistence/LuxMapConnectionString.cs:19–26` trả nguyên override; `PersistenceServiceCollectionExtensions.cs:34–38` truyền vào NpgsqlDataSourceBuilder | Nhận `SSL Mode=Require`/`VerifyFull`, `Search Path`, thông số pool mà không cần đổi code. Phải dùng định dạng Npgsql `Host=...;...`, không dán URI `postgresql://...` nguyên trạng. |
| Nhánh POSTGRES_* và TLS | `LuxMapConnectionString.cs:36–52` chỉ gán host/port/database/user/password | Không cấu hình SSL tường minh, default Npgsql là Prefer. Require mã hoá nhưng không xác thực máy chủ; đề xuất VerifyFull + CA đúng, không hạ xác minh chứng chỉ [S4,S8]. **D-3**. |
| Npgsql/EF và session mode | `LuxMap.Persistence.csproj:9–13`: EF 10.0.11, provider 10.0.3; `PersistenceServiceCollectionExtensions.cs:34–55` dùng NTS + Npgsql | Session pooler IPv4 hỗ trợ prepared statements theo hosted docs [S9]. Chưa thấy nhu cầu đổi provider/code; cần smoke test migration và truy vấn geometry thật. Direct cũng phù hợp nếu hạ tầng có IPv6, không tự đổi hướng session đã nêu trong task. |
| Prepared statements | Tìm `Prepare`/`Auto Prepare` trong src chỉ thấy pipeline ảnh; cấu hình datasource không gọi Prepare hay bật auto-prepare | Không có bằng chứng repo **đang** dùng named prepared statement; Npgsql mặc định Max Auto Prepare=0 [S4]. Chuỗi môi trường có thể bật, nhưng không đọc `.env` để đoán. Giữ session; không dùng lý do “repo chắc chắn prepare” làm bằng chứng. |
| FOR UPDATE / xmin | `src/LuxMap.Modules.Faults/FaultLocks.cs:22–36` buộc explicit transaction; `FaultConfigurations.cs:59`, `WorkOrderConfigurations.cs:34` dùng IsRowVersion; snapshot `:547` ánh xạ xmin | Session hỗ trợ primitives PostgreSQL. Transaction pooling không tự phá row lock trong cùng transaction [S10]; lý do chọn session là tương thích session/prepared/migration, không phải “FOR UPDATE không chạy”. xmin là cột hệ thống, không copy nó khi chuyển; sau cutover khởi động lại API để bỏ entity đang track; không coi xmin là ID bền vững [S11]. |
| Giới hạn pool | Datasource chưa đặt giới hạn trong code (`PersistenceServiceCollectionExtensions.cs:34–38`) | Default Npgsql Maximum Pool Size=100 [S4] chưa chắc hợp quota dự án. Session giữ backend cho mỗi client connection; số API process × pool + migration/dashboard phải nằm trong ngân sách. Chờ số thật, không bịa quota. **D-3**. |
| Seed phụ thuộc Docker | `scripts/seed_mock_set.py:257–276`: chỉ có container/database/user, gọi docker exec psql; README `:58–172,235–240` | `--database` không biến script thành remote client; không có tham số host/TLS. Không dùng nguyên `--apply` cho Supabase. Script còn DELETE toàn bảng tại `:128–130`, guard audit/lux tại `:105–124`. Cần công cụ Phase 2 đã duyệt, không viết ở đây. |
| Xung đột xã seed | `IdentitySeeder.cs:30,36,55–78`: tìm study_site hoặc INSERT lấy ID DB; `:129–137` gán scope; `seed_mock_set.py:62` tìm theo seed_key | Trên DB sạch sequence bắt đầu 1, study_site nhận COM-001 = Commune 01, đụng Long Phước. **Không phải seeder hardcode COM-001**. Existing seed users bị bỏ qua tại `:93–111`, không tự bổ sung mọi quyền xã. **D-1/D-4**. |
| Startup / cấu hình CLI | `src/LuxMap.Api/Program.cs:22,58–83`: load DotNetEnv, CORS, auth, DB, storage; `StorageOptions.cs:37–59`, `IdentityModule.cs:25–41` | Cần cấu hình MinIO và JWT, CORS hợp lệ cả khi EF CLI dựng host. Không ping MinIO khi startup; byte ảnh về sau vẫn cần MinIO local + bucket. Quét `src` không thấy đăng ký Redis/Hangfire thật, chỉ comment Hangfire; không khẳng định Redis là dependency startup hiện tại. |
| .env có thể ghi đè shell | Program `:22` dùng TraversePath().Load; package DotNetEnv 3.2.0 ở `LuxMap.Api.csproj:26` | Source đúng tag 3.2.0 đặt ClobberExistingVars=true [S12]. Không giả định export shell luôn thắng file. Đưa chuỗi cloud vào `.env` dùng chung cũng khiến test host trỏ cloud (AuthTestFactory/LuxMapApiFactory không thay datasource). Đề xuất workspace triển khai riêng; **D-6**. |
| Data API và phân quyền | Query filter/SaveChanges là cơ chế EF trong `LuxMapDbContext`; bảng snapshot không mang policy RLS Supabase | Tắt Data API trước khi tạo dữ liệu. Khả năng REST đọc được phụ thuộc grants/RLS, không phải chỉ có anon key là luôn đọc mọi bảng. Nhưng EF guard không bảo vệ đường REST/RPC; không thay bằng RLS tự thiết kế ở ticket này [S13]. Kiểm bằng key hợp lệ sau khi bảng tồn tại, không coi 200 [] là bị chặn. |
| Chép audit và rollback | `AddAuditEvent.cs:19–20` IdentityAlwaysColumn; FK actor/xã `:45–56`; `AddFaultReview.cs:90–97` Down thu hẹp CHECK | Giữ audit_id: COPY cột tường minh hoặc INSERT OVERRIDING SYSTEM VALUE; không SELECT * kèm xmin. COPY vẫn kiểm constraints/trigger [S14]. Không rollback schema trên dữ liệu sống: Down có thể mất dữ liệu hoặc fail CHECK; quay lui bằng chuyển kết nối sau khi đóng băng ghi. |

### Phủ migration

Lệnh kiểm kê đọc tất cả `src/*/Migrations/*.cs` bằng `Path.glob`, phân loại tên và liệt kê
`migrationBuilder.<operation>` (không chạy EF/SQL) cho output:

```text
files= 47 migrations= 23 designers= 23 snapshots= 1
```

Quét cả Designer/snapshot cho extension/schema/type; rà các thao tác Up/Down và khối SQL ở migration.
23 migration từ `20260829083507_InitialIdentity` tới `20260929125521_AddFaultReview`.
Ngoài extension/hàm/trigger trong bảng, các nhóm còn lại là bảng/sequence/index/GiST, FK/CHECK,
ALTER cột, COMMENT constraint và DML chuyển đổi dữ liệu. `DropSolarFixtures.cs:18` đổi dữ liệu solar;
`RenameUserRolesToRegistrationV12.cs:37–40` đổi role. Áp **đủ migration trước khi chép dữ liệu đã ở schema
mới**; không chép trước rồi chạy lại lịch sử chuyển đổi. Không coi quét C# là đã kiểm SQL provider sinh.

## Kiểm kê chỉ đọc — Mỹ chạy trên luxmap_dev

**Chưa có output SQL.** Các query dưới đã đối chiếu tên bảng/cột với migration/snapshot nhưng chưa được
PostgreSQL thực thi. Chạy khi không có test/API ghi để số các SELECT không lệch thời điểm. Cách mở psql
không đọc `.env`, không dùng startup file:

```bash
docker exec -it luxmap_postgres psql -X -U luxmap -d luxmap_dev -v ON_ERROR_STOP=1
```

### 1. Danh tính DB, migration, bảng và extension

```sql
SELECT current_database(), current_user, version(),
       pg_size_pretty(pg_database_size(current_database())) AS db_size,
       current_setting('search_path') AS search_path;
SELECT migration_id, product_version FROM public.__ef_migrations_history ORDER BY migration_id;
SELECT e.extname, e.extversion, n.nspname AS extension_schema
FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace ORDER BY e.extname;
SELECT table_name FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name;
SELECT commune_id, name, seed_key FROM public.administrative_unit
ORDER BY length(commune_id), commune_id;
SELECT user_id, username, role, is_locked, has_system_wide_scope
FROM public.app_user ORDER BY length(user_id), user_id;
SELECT u.username, c.commune_id FROM public.app_user_commune c
JOIN public.app_user u USING (user_id) ORDER BY u.username, c.commune_id;
```

Không xuất password_hash, token_hash hay JWT. Snapshot hiện có 17 bảng app; bảng thừa so với snapshot
là một phát hiện cần giải thích, không mặc định xoá hay copy.

### 2. Tổng trong/ngoài tập giữ và đếm theo từng xã

```sql
WITH inventory AS (
  SELECT 'administrative_unit' AS table_name, commune_id FROM public.administrative_unit
  UNION ALL SELECT 'pole', commune_id FROM public.pole
  UNION ALL SELECT 'road_segment', commune_id FROM public.road_segment
  UNION ALL SELECT 'fixture', commune_id FROM public.fixture
  UNION ALL SELECT 'feeder', commune_id FROM public.feeder
  UNION ALL SELECT 'pole_current_status', commune_id FROM public.pole_current_status
  UNION ALL SELECT 'fault', commune_id FROM public.fault
  UNION ALL SELECT 'fault_cluster', commune_id FROM public.fault_cluster
  UNION ALL SELECT 'lux_reading', commune_id FROM public.lux_reading
  UNION ALL SELECT 'iot_node', commune_id FROM public.iot_node
  UNION ALL SELECT 'feeder_control', commune_id FROM public.feeder_control
  UNION ALL SELECT 'work_order', commune_id FROM public.work_order
  UNION ALL SELECT 'work_order_fault', commune_id FROM public.work_order_fault
  UNION ALL SELECT 'audit_event', commune_id FROM public.audit_event
  UNION ALL SELECT 'app_user_commune', commune_id FROM public.app_user_commune
), names(table_name) AS (
  VALUES ('administrative_unit'), ('pole'), ('road_segment'), ('fixture'), ('feeder'),
         ('pole_current_status'), ('fault'), ('fault_cluster'), ('lux_reading'), ('iot_node'),
         ('feeder_control'), ('work_order'), ('work_order_fault'), ('audit_event'), ('app_user_commune')
), totals AS (
  SELECT n.table_name, count(i.table_name) AS total,
         count(i.table_name) FILTER (WHERE commune_id IN ('COM-070','COM-001','COM-002')) AS in_keep,
         count(i.table_name) FILTER (WHERE commune_id NOT IN ('COM-070','COM-001','COM-002')
                                    OR commune_id IS NULL) AS outside_keep
  FROM names n LEFT JOIN inventory i USING (table_name) GROUP BY n.table_name
)
SELECT 'total' AS report, table_name, NULL::text AS commune_id, total AS row_count,
       in_keep, outside_keep FROM totals
UNION ALL
SELECT 'by_commune', table_name, commune_id, count(*), NULL::bigint, NULL::bigint
FROM inventory GROUP BY table_name, commune_id
ORDER BY report, table_name, commune_id;

SELECT count(*) AS users_total,
       count(*) FILTER (WHERE username IN ('admin','agency','engineer','crew')) AS seed_users,
       count(*) FILTER (WHERE username NOT IN ('admin','agency','engineer','crew')) AS outside_seed
FROM public.app_user;
SELECT u.username, count(*) AS refresh_token_count
FROM public.refresh_token t JOIN public.app_user u USING (user_id)
GROUP BY u.username ORDER BY u.username;
SELECT commune_id, data_source, count(*) AS poles
FROM public.pole GROUP BY commune_id, data_source ORDER BY commune_id, data_source;
```

### 3. Phụ thuộc ngoài bốn user seed, kể cả audit trong xã được giữ

```sql
WITH refs AS (
  SELECT 'audit_event.actor_user_id' AS ref, commune_id, actor_user_id AS user_id FROM public.audit_event
  UNION ALL SELECT 'fault.reported_by', commune_id, reported_by FROM public.fault
  UNION ALL SELECT 'fault.confirmed_by', commune_id, confirmed_by FROM public.fault
  UNION ALL SELECT 'fault.resolved_by', commune_id, resolved_by FROM public.fault
  UNION ALL SELECT 'work_order.created_by', commune_id, created_by FROM public.work_order
  UNION ALL SELECT 'work_order.assigned_to', commune_id, assigned_to FROM public.work_order
  UNION ALL SELECT 'lux_reading.measured_by', commune_id, measured_by FROM public.lux_reading
)
SELECT r.ref, r.commune_id, r.user_id, u.username, count(*) AS referenced_rows
FROM refs r LEFT JOIN public.app_user u USING (user_id)
WHERE r.user_id IS NOT NULL
  AND (u.username IS NULL OR u.username NOT IN ('admin','agency','engineer','crew'))
GROUP BY r.ref, r.commune_id, r.user_id, u.username ORDER BY r.ref, r.commune_id, r.user_id;

SELECT commune_id, actor_kind,
       CASE WHEN actor_user_id IS NULL THEN 'engine'
            WHEN EXISTS (SELECT 1 FROM public.app_user u WHERE u.user_id = a.actor_user_id
                         AND u.username IN ('admin','agency','engineer','crew')) THEN 'seed_user'
            ELSE 'outside_seed' END AS actor_group,
       count(*) AS audit_rows
FROM public.audit_event a GROUP BY commune_id, actor_kind, actor_group
ORDER BY commune_id, actor_kind, actor_group;

SELECT p.commune_id AS pole_commune, s.commune_id AS segment_commune, count(*) AS poles
FROM public.pole p JOIN public.road_segment s USING (segment_id)
WHERE p.commune_id IN ('COM-070','COM-001','COM-002')
  AND s.commune_id NOT IN ('COM-070','COM-001','COM-002')
GROUP BY p.commune_id, s.commune_id;

SELECT c.conrelid::regclass AS child_table, c.conname,
       c.confrelid::regclass AS parent_table, pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
WHERE n.nspname = 'public' AND c.contype = 'f'
ORDER BY c.conrelid::regclass::text, c.conname;
```

Query cuối cung cấp toàn bộ đồ thị FK để Phase 2 lập manifest đóng theo tham chiếu, gồm chuỗi WO cha/gốc,
segment liên xã và actor audit. Không chỉ lọc `commune_id` độc lập trên từng bảng. Với dữ liệu test nằm
trong xã giữ, Mỹ đối chiếu ID/entity_id/external_ref/nguồn nhập và lịch sử; tên `BE-12a` chỉ là tín hiệu.
Không bỏ actor ngoài seed nếu một sự kiện thật vẫn trỏ tới họ; đưa ngoại lệ vào D-4.

### 4. Sequence — không gọi hàm làm đổi trạng thái

```sql
SELECT schemaname, sequencename, sequenceowner, start_value, min_value, max_value,
       increment_by, cycle, cache_size, last_value
FROM pg_sequences WHERE schemaname = 'public' ORDER BY sequencename;

-- Chỉ sinh văn bản SELECT để Mỹ chạy tiếp, KHÔNG tự thực thi.
-- Lấy thêm is_called; last_value của pg_sequences có thể NULL vì chưa dùng/thiếu quyền.
SELECT format('SELECT %L AS sequence_name, last_value, is_called FROM %I.%I;',
              schemaname || '.' || sequencename, schemaname, sequencename) AS read_only_query
FROM pg_sequences WHERE schemaname = 'public' ORDER BY sequencename;

SELECT 'commune_id_seq' AS sequence_name, max(substring(commune_id from '[0-9]+$')::bigint) AS max_kept_id
FROM public.administrative_unit WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'user_id_seq', max(substring(user_id from '[0-9]+$')::bigint)
FROM public.app_user WHERE username IN ('admin','agency','engineer','crew')
UNION ALL SELECT 'pole_id_seq', max(substring(pole_id from '[0-9]+$')::bigint)
FROM public.pole WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'segment_id_seq', max(substring(segment_id from '[0-9]+$')::bigint)
FROM public.road_segment WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'fixture_id_seq', max(substring(fixture_id from '[0-9]+$')::bigint)
FROM public.fixture WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'feeder_id_seq', max(substring(feeder_id from '[0-9]+$')::bigint)
FROM public.feeder WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'fault_id_seq', max(substring(fault_id from '[0-9]+$')::bigint)
FROM public.fault WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'cluster_id_seq', max(substring(cluster_id from '[0-9]+$')::bigint)
FROM public.fault_cluster WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'lux_id_seq', max(substring(lux_id from '[0-9]+$')::bigint)
FROM public.lux_reading WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'node_id_seq', max(substring(node_id from '[0-9]+$')::bigint)
FROM public.iot_node WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT 'work_order_id_seq', max(substring(work_order_id from '[0-9]+$')::bigint)
FROM public.work_order WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT pg_get_serial_sequence('public.audit_event','audit_id'), max(audit_id)
FROM public.audit_event WHERE commune_id IN ('COM-070','COM-001','COM-002')
UNION ALL SELECT pg_get_serial_sequence('public.refresh_token','id'), max(t.id)
FROM public.refresh_token t JOIN public.app_user u USING (user_id)
WHERE u.username IN ('admin','agency','engineer','crew');
```

Các max này **chưa** là giá trị setval đã duyệt: manifest cuối có thể thêm/bớt hàng. Snapshot có 11
sequence ID hiển thị và hai cột identity audit/token; query catalog tránh bỏ sót sequence tự sinh.

## Hai phương án chuyển dữ liệu — chưa chọn

| | A — migration + chép dữ liệu đã lọc | B — DB mới, seed lại rồi nạp thực địa |
|---|---|---|
| Bước | Đóng băng nguồn; backup; manifest ID/hàng/FK được Mỹ ký; migrate đích rỗng; chép dữ liệu theo FK; audit cuối; chỉnh sequence trên đích sau duyệt; đối chiếu trước mở ghi | Dựng DB rỗng; migrate; giải quyết COM-001/COM-070 trước seed; seed 4 user; tạo lại mock bằng công cụ được duyệt; nạp thực địa với bảng ánh xạ; đối chiếu |
| Giữ gì | Có thể giữ nguyên ID, tên, scope, password hash, timestamp, fault/WO/lux/audit và data_source; không giữ xmin | Tái tạo mock và tài khoản; dữ liệu thực địa chỉ giữ được thuộc tính/ID nếu nguồn nhập và quy trình bảo toàn có đủ |
| Rủi ro | Test lẫn trong xã giữ; actor ngoài seed; FK liên xã/WO tự tham chiếu; identity ALWAYS audit; copy nhầm token; schema nguồn khác đích | Seed sạch mặc định lấy COM-001 của Long Phước; script mock hiện tại xoá toàn bảng và chỉ Docker; import API không bảo toàn ID; seed không khôi phục lịch sử, scope thật hay mật khẩu cũ |
| Mất gì | Nếu manifest đầy đủ: không mất dữ liệu nghiệp vụ; refresh token có thể chủ động bỏ theo D-5; mọi bỏ dữ liệu cần danh sách rõ | Fault/WO đã thay đổi, audit, lux và lịch sử thực địa không được seeder dựng lại. Nếu không copy bổ sung thì **không đạt yêu cầu giữ dữ liệu**; B có copy bổ sung thực chất gần A |
| Kiểm | Số hàng theo bảng/xã, tập ID và giá trị, data_source, FK closure, audit JSON/timestamp/ID, sequence, POLE-0047, đăng nhập/scope, Data API bị chặn | Những kiểm của A cộng đối chiếu nguồn thực địa và mapping mọi ID; danh sách lịch sử mất được duyệt trước, không lấy “sạch” làm bằng chứng đúng |

**Đề xuất A để Mỹ cân nhắc, chưa chốt.** B nguyên trạng không đáp ứng yêu cầu giữ sự cố/phiếu/audit.
Không dùng full dump restore vào Supabase hoặc “copy hết rồi xoá rác”. Không viết script chuyển ở Phase 1.

## D-item — tất cả CHỜ Mỹ chốt

| Mã | Quyết định cần chốt | Đề xuất / lựa chọn và hệ quả |
|---|---|---|
| **D-1** | A hay B, tiêu chí không mất lịch sử | Đề xuất A theo bảng trên. B chỉ hợp lệ khi có nguồn tái tạo đủ và Mỹ quyết rõ lịch sử nào giữ bằng copy bổ sung; không tự bỏ dữ liệu đã yêu cầu giữ. |
| **D-2** | Namespace PostGIS và search_path của CLI/API | Đề xuất bật PostGIS trong `extensions` nếu project cho phép; kiểm namespace thực tế rồi dùng `public,<namespace>`. Không đổi migration cũ, không chuyển extension đang có. Nếu thiếu quyền/SQL provider không tương thích thì dừng và mở thay đổi riêng. |
| **D-3** | Role migrate/runtime, TLS và ngân sách kết nối | Dùng session pooler đúng Connect panel; migrate bằng postgres có quyền sở hữu app. Đề xuất runtime role riêng đủ DML/sequence/USAGE, không DDL; cần grant script Phase 2 duyệt riêng. VerifyFull; chốt quota/pool khi biết plan và số process. Không coi role thường là chống được audit_purge. |
| **D-4** | Manifest giữ/lọc và ánh xạ study_site | Chờ SQL live. Giữ COM-001 Long Phước, COM-002 Long Bình, COM-070 mock + seed_key study_site đúng hàng; giữ user/scope thực tế và closure FK. Quyết từng actor ngoài seed/hàng test trong xã giữ; không tự gắn lại data_source. Nếu B, bootstrap ID xã trước seed và sửa công cụ seed trong phạm vi được duyệt riêng. |
| **D-5** | Sequence, refresh token và phiên cutover | (a) floor theo max ID giữ, hay (b) không lùi high-water nguồn để tránh tái dùng ID đã phát. Đề xuất (b) khi chưa biết cache/offline nào giữ ID cũ; cả hai phải lớn hơn max đích, gồm commune/user/lux/audit/token. Đề xuất không chuyển refresh token, yêu cầu đăng nhập lại; JWT còn hạn không tự mất hiệu lực chỉ vì bỏ refresh token — cần chốt giữ/đổi signing key và thời điểm cutover. |
| **D-6** | Tách cấu hình triển khai và test | Đề xuất workspace/thư mục vận hành riêng không dùng chạy test; Mỹ đặt secret tại đó, không cloud override trong workspace phát triển. Không chỉ unset shell vì DotNetEnv có thể nạp lại file. Nếu muốn guard tự động/NoClobber thì là thay đổi code riêng, chưa làm. |
| **D-7** | Mốc freeze, nghiệm thu, quay lui sau khi đã có ghi mới | Mỹ chốt cửa sổ dừng ghi cả API/test/engine và người giữ backup. Trước ghi mới có thể chuyển về DB local đã freeze; sau ghi mới phải backup/reconcile phần chênh trước, không chuyển ngược mù gây mất dữ liệu. |

Không xác lập deviation API mới trong phiên này. Khác biệt diễn giải trong checklist task (prepare,
row lock, “anon luôn mở”) được đính chính bằng nguồn trong báo cáo, không sửa Contract cho khớp.
Nếu Phase 2 phát hiện code/mock lệch Contract, Mỹ chuyển vào `docs/contract-drift.md` trước xử lý;
ràng buộc kỹ thuật đã chốt mới đưa vào `CLAUDE.md`, không biến đề xuất hiện tại thành luật.

## Nguồn chính thức (tra 01/10/2026)

- [S1 — Supabase postgres không superuser](https://supabase.com/docs/guides/database/postgres/roles-superuser).
- [S2 — Supabase PostGIS, schema và quyền extension](https://supabase.com/docs/guides/database/extensions/postgis).
- [S3 — PostgreSQL schemas/search_path](https://www.postgresql.org/docs/16/ddl-schemas.html).
- [S4 — Npgsql connection parameters](https://www.npgsql.org/doc/connection-string-parameters.html).
- [S5 — CREATE FUNCTION](https://www.postgresql.org/docs/current/sql-createfunction.html), [trusted PL/pgSQL](https://www.postgresql.org/docs/16/plpgsql-overview.html).
- [S6 — CREATE TRIGGER](https://www.postgresql.org/docs/current/sql-createtrigger.html).
- [S7 — Custom options](https://www.postgresql.org/docs/current/runtime-config-custom.html), [SET LOCAL](https://www.postgresql.org/docs/current/sql-set.html).
- [S8 — Npgsql SSL/TLS](https://www.npgsql.org/doc/security.html).
- [S9 — Hosted connections](https://supabase.com/docs/guides/database/connecting-to-postgres), [prepared statements](https://supabase.com/docs/guides/troubleshooting/disabling-prepared-statements-qL8lEL).
- [S10 — Supavisor pool modes](https://supabase.github.io/supavisor/configuration/pool_modes/), [PostgreSQL row locks](https://www.postgresql.org/docs/16/explicit-locking.html).
- [S11 — Npgsql xmin concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html).
- [S12 — DotNetEnv 3.2.0 LoadOptions](https://github.com/tonerdo/dotnet-env/blob/v3.2.0/src/DotNetEnv/LoadOptions.cs), [Env.Load](https://github.com/tonerdo/dotnet-env/blob/v3.2.0/src/DotNetEnv/Env.cs).
- [S13 — Disable Data API](https://supabase.com/docs/guides/api/securing-your-api), [API keys/anon](https://supabase.com/docs/guides/getting-started/api-keys).
- [S14 — PostgreSQL COPY](https://www.postgresql.org/docs/current/sql-copy.html), [sequence/setval semantics](https://www.postgresql.org/docs/current/functions-sequence.html).

## Nghiệm thu Phase 1

- ✅ Bảng tương thích có dẫn file/dòng và nguồn chính thức; SQL fallback đủ nhóm kiểm kê, không bịa số.
- ✅ Hai phương án và D-1…D-7 giữ trạng thái chờ; bản nháp A/C có điểm dừng tương ứng.
- ⚠️ SQL chưa chạy, migration chưa apply, API/TLS/pool/Data API chưa smoke test; không chạy build/test
  vì đây là tài liệu và test bị cấm. Không dùng kiểm tĩnh để tuyên bố triển khai thành công.
- ✅ Rà tĩnh hai file: liên kết tương đối tồn tại, code fence cân bằng, không whitespace cuối dòng;
  `git diff --check` sạch và `git diff --name-only` rỗng (không sửa file tracked). Status chỉ thêm
  hai file yêu cầu so với baseline untracked đã ghi ở trên.
- **Dừng hẳn tại Phase 1.** Phase 2 do Mỹ/Claude thực hiện sau khi Mỹ chốt, không phải Codex tự tiếp tục.

## Bổ sung — số đo thật (Claude chạy, 01/10/2026)

Codex không chạy được Docker trong sandbox. Claude chạy các query mục 2–4 ở trên, **nguyên văn**, trong
`BEGIN READ ONLY; … ROLLBACK;` trên `luxmap_dev`. Tóm tắt output (FK 42 dòng lược bớt):

```text
 total | administrative_unit | 17 rows | in_keep 3   | outside_keep 14   (COM-1603 … COM-1650)
 total | app_user_commune    | 30 rows | in_keep 9   | outside_keep 21
 total | pole / fixture      | 217     | in_keep 217 | outside_keep 0
 total | road_segment        | 20      | in_keep 20  | outside_keep 0
 total | fault 28 · fault_cluster 1 · feeder 3 · feeder_control 3 · iot_node 3 · work_order 3 · work_order_fault 11 — outside_keep 0 cho tất cả
 total | audit_event 0 · lux_reading 0 · pole_current_status 0
 users_total 18 | seed_users 4 | outside_seed 14
 refresh_token: engineer 10065 · admin 2503 · agency 1301 · crew 901 · be12a-*/be12b-* 8–78 mỗi tài khoản
 pole by commune/data_source: COM-001 field 36 · COM-002 field 78 · COM-070 public_imagery 103
 tham chiếu tới user ngoài seed: 0 rows · audit theo actor: 0 rows · cột trong xã giữ trên tuyến xã khác: 0 rows
 sequence last_value: commune 1874 · user 2084 · pole 217 · fixture 217 · segment 20 · fault 28 · feeder 3
                      node 3 · cluster 1 · lux 2357 · refresh_token 24445 · audit (chưa dùng)
```

**Hệ quả cho D-1/D-4:** rác test chỉ nằm ở `administrative_unit` (14 xã), `app_user` (14 tài khoản),
`app_user_commune` (21 dòng) và `refresh_token`. Mọi bảng nghiệp vụ sạch, audit trống, không có tham
chiếu chéo ra ngoài tập giữ — phương án A không cần lọc audit hay xử lý actor ngoài seed.

## Chốt D-item (Mỹ, 01/10/2026) và Phase 2

Mỹ chốt **cả D-1…D-7 theo đề xuất** trình bày trong phiên (bảng "Đã chốt" ở `docs/deploy/supabase.md`):
A (migrate + chép dữ liệu lọc); PostGIS ở `extensions`; session pooler + VerifyFull + pool 10, role `postgres`;
giữ 3 xã + 4 tài khoản seed; sequence = high-water nguồn, bỏ refresh token; workspace triển khai riêng;
đóng ghi lúc chép.

Phase 2 (Claude): `scripts/copy_dev_to_supabase.py` và runbook `docs/deploy/supabase.md` viết lại theo quyết
định. Diễn tập trên DB local mô phỏng Supabase (role `deploytest` không superuser, PostGIS ở `extensions`):

```text
dotnet ef database update (Search Path=public,extensions)  -> Done; 23 migration; 18 bảng owner deploytest;
                                                             luxmap_format_id, luxmap_audit_event_append_only owner deploytest;
                                                             audit_event_append_only_rows / _truncate có mặt
copy_dev_to_supabase.py              -> superuser f; migration 23 khớp; PostGIS schema extensions; kế hoạch như runbook
copy_dev_to_supabase.py --apply      -> 16 bảng ✅, 13 sequence khớp, mốc POLE-0047,POLE-0104; exit 0
copy_dev_to_supabase.py --apply (2)  -> exit 2 "administrative_unit ở đích đã có 3 dòng"
API trên bản sao                     -> engineer/agency login ok; bbox thực địa 114, bbox mock 103; faults total 28
POLE-0047 / POLE-0104 geom           -> trùng nguồn (POINT(106.492025 10.965989) / POINT(106.8484139 10.8415694))
```

Phát hiện khi diễn tập: phiên SQL không có `extensions` trong `search_path` không thấy hàm PostGIS — đã ghi vào
runbook mục 5. `GET /poles/{id}` trả 404 vì endpoint chưa có (BE-20), không phải lỗi chép.

Chưa kiểm được (cần project thật): TLS VerifyFull qua pooler, quota kết nối, Data API đã tắt.
