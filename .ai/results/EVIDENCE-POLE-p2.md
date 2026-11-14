# EVIDENCE-POLE — Kết quả PHA 2

Ngày 14/11/2026 · Codex · theo quyết định Mỹ D-1…D-8, **D-3 chọn A (cùng xã)**.

✅ Đã hiện thực code, migration, test, OpenAPI, drift, ERD và dashboard; build toàn solution thành công; EF không còn thay đổi model; lint OpenAPI hợp lệ.  
⚠️ **Chưa nghiệm thu runtime:** VSTest bị sandbox chặn mở socket; Docker socket cũng bị từ chối. **Không có test nào được báo pass trong lượt này.** Chưa apply migration / chạy rollback / phá thử trên PostGIS.  
📌 Giữ nguyên working tree: **không commit, không push, không tạo nhánh**, không sửa Contract văn bản. EV-4 SELF-SIGNED, nền tạm tới FW; WP5/WP6 chưa báo.

## 1. Đã hiện thực

- `pole_id` tuỳ chọn ở form POST evidence, nullable ở `RepairEvidence` và `EvidenceItem`. Không gửi ⇒ NULL; không backfill, không suy từ GPS. Mapping chung cho POST / replay / GET / ảnh fault.
- Quyền phiếu kiểm trước: giữ `WORK_ORDER_NOT_FOUND`. Cột đọc qua query filter: không tồn tại/ngoài scope ⇒ 404 `POLE_NOT_FOUND`. Cột thấy được nhưng khác xã phiếu hoặc ngoài **toàn bộ** `WorkOrderPoleService.Targets(id)` ⇒ 409 `POLE_NOT_IN_WORK_ORDER`. Gọi trực tiếp `Targets`, không chép predicate / không lấy trang đầu / không chặn geom NULL.
- Kiểm trước storage; sau storage mở transaction, đọc cha qua filter trước khi chờ khoá, khoá phiếu, đọc lại quyền/replay/trạng thái; đọc cột qua filter, khoá cột, đọc lại bằng LINQ và kiểm tập mục tiêu/xã. Khoá **phiếu → cột**, dùng `ExecuteSql` với `SELECT 1 … FOR UPDATE`; khoá cột có cả predicate commune. Không giữ khoá qua upload/decode. Giữ object trước row, lỗi sau storage có thể để orphan theo cơ chế sẵn có.
- `Target` giữ nullable PoleId; **cả ba** đường replay (sớm, sau storage, catch unique violation sau rollback) dùng cùng `Replay`, so hai cha + kind + pole chính xác, kể cả NULL. Khác ⇒ 409 `IDEMPOTENCY_CONFLICT`. Replay chạy trước kiểm điều kiện dành cho ảnh mới.
- GET evidence nhận `?pole_id=`, lọc trước count / phân trang; giữ thứ tự `captured_at, uploaded_at, length(evidence_id), evidence_id`; không khớp ⇒ trang rỗng, không lookup cột.
- Ảnh fault không có input pole_id và row luôn NULL; CHECK mới ngăn fault-photo mang liên kết cột trực tiếp. Không đổi `/pole-photos`, luật nhãn EV-3, luật ảnh after trước hoàn thành, sync bundle/push, media pipeline hay capability.
- `AddEvidencePole`: `pole_id text NULL`, index đầy đủ `ix_repair_evidence_pole_id`, CHECK `ck_repair_evidence_fault_no_pole`; FK ghép DB-only `fk_repair_evidence_pole_same_commune` `(pole_id, commune_id) → pole(pole_id, commune_id)` Restrict, tái dùng unique key `ak_pole_photo_target`, không thêm alternate key EF vào Pole.CommuneId. Down từ chối nếu còn liên kết rồi mới tháo các đối tượng vừa thêm; không đụng key đích dùng chung.
- Thêm EV-4 ngay sau EV-3, cập nhật hai dòng BE-24, thêm quan hệ/cột và ngoại lệ Q7 vào ERD; card dashboard mới ngay dưới h1.

## 2. File thay đổi

| File | Nội dung |
|---|---|
| `src/LuxMap.Modules.WorkOrders/Entities/RepairEvidence.cs` | PoleId nullable |
| `src/LuxMap.Modules.WorkOrders/Configurations/WorkOrderConfigurations.cs` | Index pole + CHECK fault không có pole |
| `src/LuxMap.Modules.WorkOrders/WorkOrderEvidence.cs` | DTO, kiểm cột, khoá, replay, filter/mapping |
| `src/LuxMap.Modules.WorkOrders/WorkOrdersController.cs` | Form POST / query GET pole_id |
| `src/LuxMap.Persistence/Migrations/20261114044112_AddEvidencePole.cs` | Up/Down, FK ghép DB-only, chốt rollback |
| `src/LuxMap.Persistence/Migrations/20261114044112_AddEvidencePole.Designer.cs` | EF sinh model migration |
| `src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs` | Cột/index/CHECK mới; giữ nguyên dòng CHECK khác vùng (khác escape CRLF khi EF sinh lại) |
| `tests/LuxMap.Api.Tests/WorkOrderTests.cs` | Helper poleId, ca app cũ trả null |
| `tests/LuxMap.Api.Tests/WorkOrderEvidencePoleTests.cs` | Partial cùng WorkOrderTests: mục tiêu/quyền/xã/geometry/race/replay/filter/FK/lock |
| `tests/LuxMap.Api.Tests/WorkOrderEvidenceMigrationTests.cs` | Partial cùng WorkOrderTests: DB test riêng, Up/Down/reapply, giữ hàng cũ, từ chối mất liên kết |
| `tests/LuxMap.Api.Tests/FaultReportTests.cs` | Fault có/không cột đều trả pole_id null; CHECK mới |
| `tests/LuxMap.Api.Tests/OpenApiSpecTests.cs` | Form/filter optional, DTO nullable, fault không có field pole |
| `docs/openapi/luxmap-v1.json` | Xuất lại từ DLL bằng Swagger CLI |
| `docs/openapi/tools/gen_consolidated_spec.py` | Summary POST/GET evidence, không schema viết tay |
| `docs/openapi/luxmap-v1.5.json` | Sinh lại từ generator |
| `docs/contract-drift.md` | EV-4 + bảng BE-24 |
| `docs/database/erd.md` | Cột/quan hệ/cùng xã, FK, rollback |
| `tracking.html` | Card EVIDENCE-POLE, kết quả và phần còn lại |
| `.ai/results/EVIDENCE-POLE-p2.md` | Báo cáo này |

Không cần sửa `scripts/copy_dev_to_supabase.py`: không có bảng mới; `pole` đã đứng trước `repair_evidence`, toàn bộ cột thuộc các xã được giữ đều được chép. D-3 A buộc cột cùng xã ảnh nên không cần thêm lọc pole. Script đọc danh sách cột động rồi COPY theo danh sách; hai DB vẫn phải cùng migration. Chưa chạy chép DB thực tế.

## 3. Build — output thật, exit code 0

Môi trường có .NET SDK 10.0.302. Các lần thử build mặc định kẹt ở restore/MSBuild nên đã yêu cầu huỷ; chạy một worker (`-m:1`) và dùng cache NuGet cục bộ giải quyết được. Không sửa dependency/config trong repo. Bước restore offline dùng `-p:NuGetAudit=false` nên không phải bằng chứng đã audit NuGet online.

Lệnh restore + build solution đã thành công (0 warning / 0 error):

```bash
DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false DOTNET_USE_POLLING_FILE_WATCHER=1 \
NUGET_SCRATCH=/tmp/evidence-pole-nuget-single DOTNET_CLI_HOME=/tmp/evidence-pole-dotnet \
dotnet build --disable-build-servers -m:1 \
  -p:RestoreSources=/Users/nhm809/.nuget/packages -p:NuGetAudit=false
```

Sau sửa cuối, build lại toàn solution bằng đúng lệnh dưới (log đầy đủ, exit 0):

```bash
DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false DOTNET_USE_POLLING_FILE_WATCHER=1 \
DOTNET_CLI_HOME=/tmp/evidence-pole-dotnet \
dotnet build --disable-build-servers --no-restore -m:1
```

```text
LuxMap.Shared -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.IOT -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.IOT/bin/Debug/net10.0/LuxMap.Modules.IOT.dll
  LuxMap.Modules.Sync -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Infrastructure.Storage -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Modules.Statistics -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Api -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.IotBridge -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.IotBridge/bin/Debug/net10.0/LuxMap.IotBridge.dll
  LuxMap.Api.Tests -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Persistence.Tests -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:06.59
```
## 4. Migration — EF sinh, đã đọc Up/Down và SQL, chưa apply

Lệnh sinh đã chạy với `--no-build` sau build API (không có bước build/test ngầm thiếu cờ):

```bash
DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false DOTNET_USE_POLLING_FILE_WATCHER=1 \
DOTNET_CLI_HOME=/tmp/evidence-pole-dotnet MSBUILDDISABLENODEREUSE=1 \
Cors__AllowedOrigins__0=https://localhost:3000 \
dotnet ef migrations add AddEvidencePole --no-build \
  -p src/LuxMap.Persistence -s src/LuxMap.Api -o Migrations --verbose
```

Đuôi output thật (exit 0):

```text
Finding design-time services referenced by assembly 'LuxMap.Persistence'...
Using design-time services from assembly 'Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite'.
Finding design-time services for provider 'Npgsql.EntityFrameworkCore.PostgreSQL'...
Using design-time services from provider 'Npgsql.EntityFrameworkCore.PostgreSQL'.
Finding IDesignTimeServices implementations in assembly 'LuxMap.Api'...
No design-time services were found.
Writing migration to '/private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Persistence/Migrations/20261114044112_AddEvidencePole.cs'.
Writing model snapshot to '/private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs'.
Done. To undo this action, use 'ef migrations remove'
```

Sau khi sinh đã bổ sung raw SQL FK + guard Down theo quyết định. Nội dung migration cuối:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuxMap.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidencePole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pole_id",
                table: "repair_evidence",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_repair_evidence_pole_id",
                table: "repair_evidence",
                column: "pole_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_repair_evidence_fault_no_pole",
                table: "repair_evidence",
                sql: "fault_id IS NULL OR pole_id IS NULL");

            // DB-only: reuse the photo target key without making Pole.CommuneId immutable in EF.
            migrationBuilder.Sql("ALTER TABLE repair_evidence ADD CONSTRAINT fk_repair_evidence_pole_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse to lose an explicit pole link. Roll back the app while keeping this additive schema instead.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM repair_evidence WHERE pole_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'AddEvidencePole cannot be rolled back: pole links exist (repair_evidence.pole_id)';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("ALTER TABLE repair_evidence DROP CONSTRAINT fk_repair_evidence_pole_same_commune;");

            migrationBuilder.DropIndex(
                name: "ix_repair_evidence_pole_id",
                table: "repair_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_repair_evidence_fault_no_pole",
                table: "repair_evidence");

            migrationBuilder.DropColumn(
                name: "pole_id",
                table: "repair_evidence");
        }
    }
}
```
Đã rà: Up chỉ AddColumn/CreateIndex/AddCheckConstraint/ADD FK; không DropIndex/DropColumn/AlterColumn ngoài yêu cầu, không xmin, không backfill. Down kiểm trước rồi đảo đúng các đối tượng mới; không drop `ak_pole_photo_target`.

Với cùng các biến môi trường ở trên, đã chạy cả hai lệnh sau, **exit 0**; chỉ sinh script, không kết nối/apply DB:

```bash
dotnet ef migrations script 20261113100141_CommuneBoundary 20261114044112_AddEvidencePole --no-build -p src/LuxMap.Persistence -s src/LuxMap.Api -o /tmp/evidence-pole-up.sql
dotnet ef migrations script 20261114044112_AddEvidencePole 20261113100141_CommuneBoundary --no-build -p src/LuxMap.Persistence -s src/LuxMap.Api -o /tmp/evidence-pole-down.sql
```

Output `cat /tmp/evidence-pole-up.sql /tmp/evidence-pole-down.sql`:

```sql
START TRANSACTION;
ALTER TABLE repair_evidence ADD pole_id text;

CREATE INDEX ix_repair_evidence_pole_id ON repair_evidence (pole_id);

ALTER TABLE repair_evidence ADD CONSTRAINT ck_repair_evidence_fault_no_pole CHECK (fault_id IS NULL OR pole_id IS NULL);

ALTER TABLE repair_evidence ADD CONSTRAINT fk_repair_evidence_pole_same_commune FOREIGN KEY (pole_id, commune_id) REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT;

INSERT INTO __ef_migrations_history (migration_id, product_version)
VALUES ('20261114044112_AddEvidencePole', '10.0.11');

COMMIT;

START TRANSACTION;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM repair_evidence WHERE pole_id IS NOT NULL) THEN
        RAISE EXCEPTION 'AddEvidencePole cannot be rolled back: pole links exist (repair_evidence.pole_id)';
    END IF;
END $$;

ALTER TABLE repair_evidence DROP CONSTRAINT fk_repair_evidence_pole_same_commune;

DROP INDEX ix_repair_evidence_pole_id;

ALTER TABLE repair_evidence DROP CONSTRAINT ck_repair_evidence_fault_no_pole;

ALTER TABLE repair_evidence DROP COLUMN pole_id;

DELETE FROM __ef_migrations_history
WHERE migration_id = '20261114044112_AddEvidencePole';

COMMIT;
```
Lệnh kiểm model (cùng env), exit 0:

```bash
dotnet ef migrations has-pending-model-changes --no-build -p src/LuxMap.Persistence -s src/LuxMap.Api
```

```text
No changes have been made to the model since the last migration.
```
## 5. OpenAPI và kiểm tra tĩnh — output thật

Không viết tay JSON/schema; xuất từ DLL rồi chạy generator:

```bash
DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false DOTNET_USE_POLLING_FILE_WATCHER=1 \
Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 \
dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py
```

```text
Swagger JSON/YAML successfully written to /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/docs/openapi/luxmap-v1.json
wrote docs/openapi/luxmap-v1.5.json: paths=146 implemented_ops=189 not_implemented_ops=0 schemas=328
```
`npx --no-install @redocly/cli …` bị kẹt và đã huỷ; dùng trực tiếp Redocly **2.78.0** đã có trong npm cache (không cài dependency):

```bash
node /Users/nhm809/.npm/_npx/1738445911c9ab26/node_modules/@redocly/cli/bin/cli.js lint docs/openapi/luxmap-v1.5.json
```

Đuôi output thật, **exit 0**:

```text

docs/openapi/luxmap-v1.5.json: validated in 84ms

Woohoo! Your API description is valid. 🎉
You have 10 warnings.
```
Đối chiếu bản HEAD bằng `git show HEAD:docs/openapi/luxmap-v1.5.json > /tmp/evidence-pole-baseline-spec.json`, rồi cùng CLI lint file đó: exit 0, cũng **10 warnings**. Các warning hiện có: thiếu license, localhost servers, `$ref` sibling description, unused components; không sửa ngoài ticket.

Kiểm tra tĩnh riêng (Python đọc hai JSON so HEAD, import PLAN không gọi main, đọc nguồn migration):

```text
docs/openapi/luxmap-v1.json: OK - optional form/filter, nullable DTO; only evidence operations and EvidenceItem changed; fault/pole-photos/sync unchanged.
docs/openapi/luxmap-v1.5.json: OK - optional form/filter, nullable DTO; only evidence operations and EvidenceItem changed; fault/pole-photos/sync unchanged.
Copy PLAN: OK - pole precedes repair_evidence and all poles of kept communes are copied; D-3 same-commune FK needs no new filter/table.
Migration source: OK - additive Up, DB-only composite RESTRICT FK; Down refuses links before dropping only the new objects; no xmin/backfill.
```
Script kiểm tĩnh đã chạy là `python3 /tmp/evidence-pole-static-check.py` (exit 0). Nội dung để review/reproduce:

```python
import json, runpy, subprocess
from pathlib import Path
for file in ('docs/openapi/luxmap-v1.json', 'docs/openapi/luxmap-v1.5.json'):
    now = json.loads(Path(file).read_text())
    old = json.loads(subprocess.check_output(['git','show', 'HEAD:' + file], text=True))
    root = '/api/v1/work-orders/{id}/evidence'
    form = now['paths'][root]['post']['requestBody']['content']['multipart/form-data']['schema']
    assert set(form['required']) == {'file', 'kind', 'captured_at', 'lat', 'lng'}
    assert form['properties']['pole_id']['type'] == 'string'
    assert now['components']['schemas']['EvidenceItem']['properties']['pole_id'] == {'type':'string', 'nullable': True}
    assert any(p['name'] == 'pole_id' and p['in'] == 'query' and not p.get('required') for p in now['paths'][root]['get']['parameters'])
    fault = '/api/v1/faults/{id}/photos'
    assert now['paths'][fault] == old['paths'][fault]
    assert all(now['paths'][p] == old['paths'][p] for p in old['paths'] if p != root)
    changed_schemas = [name for name in old['components']['schemas'] if old['components']['schemas'][name] != now['components']['schemas'][name]]
    assert changed_schemas == ['EvidenceItem'], changed_schemas
    print(file + ': OK - optional form/filter, nullable DTO; only evidence operations and EvidenceItem changed; fault/pole-photos/sync unchanged.')
copy = runpy.run_path('scripts/copy_dev_to_supabase.py', run_name='evidence_pole_check')
plan = {table:(i, where) for i,(table,where,_) in enumerate(copy['PLAN'])}
assert plan['pole'][0] < plan['repair_evidence'][0]
assert plan['pole'][1] == copy['IN_COMMUNES']
assert plan['repair_evidence'][1].startswith(copy['IN_COMMUNES'])
print('Copy PLAN: OK - pole precedes repair_evidence and all poles of kept communes are copied; D-3 same-commune FK needs no new filter/table.')
snapshot = Path('src/LuxMap.Persistence/Migrations/LuxMapDbContextModelSnapshot.cs').read_text()
assert 'b.Property<string>("PoleId")' in snapshot
migration = Path('src/LuxMap.Persistence/Migrations/20261114044112_AddEvidencePole.cs').read_text()
up, down = migration.split('protected override void Down')
assert not any(s in up for s in ('DropIndex(', 'DropColumn(', 'AlterColumn(', 'xmin'))
assert 'REFERENCES pole (pole_id, commune_id) ON DELETE RESTRICT;' in up
assert down.index('RAISE EXCEPTION') < down.index('DROP CONSTRAINT') < down.index('DropColumn')
print('Migration source: OK - additive Up, DB-only composite RESTRICT FK; Down refuses links before dropping only the new objects; no xmin/backfill.')
```
`git diff --check`: exit 0, không output. Kiểm tĩnh không thay thế test hành vi / DB.

## 6. Test — đã biên dịch, không chạy được vì sandbox

Các lệnh test đã thử (**exit code 1** cả ba):

```bash
# Cùng env DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false,
# DOTNET_USE_POLLING_FILE_WATCHER=1, DOTNET_CLI_HOME=/tmp/evidence-pole-dotnet
dotnet test tests/LuxMap.Api.Tests --disable-build-servers --no-restore -m:1 --filter FullyQualifiedName~Evidence_filter_counts_before_pagination
dotnet test tests/LuxMap.Shared.Tests --disable-build-servers --no-build --no-restore -m:1
dotnet test tests/LuxMap.Infrastructure.Storage.Tests --disable-build-servers --no-build --no-restore -m:1
```

Đuôi output API test thật (build xong rồi runner abort, chưa vào test):

```text
Test run for /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.
```

Output Shared test thật (Storage có cùng lỗi SocketException và `Test Run Aborted.`):

```text
Test run for /private/tmp/claude-501/-Users-nhm809--claude-spares-spare-dyHA33/a4c63c13-d46d-4cb5-be49-060059cf7027/scratchpad/ep-wt/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
System.Net.Sockets.SocketException (13): Permission denied
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at System.Net.Sockets.TcpListener.Start(Int32 backlog)
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start(String endPoint) in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/SocketServer.cs:line 65
   at Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.TestRequestSender.InitializeCommunication() in /_/src/vstest/src/Microsoft.TestPlatform.CommunicationUtilities/TestRequestSender.cs:line 168
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyOperationManager.SetupChannel(IEnumerable`1 sources, String runSettings) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyOperationManager.cs:line 207
   at Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Client.ProxyExecutionManager.InitializeTestRun(TestRunCriteria testRunCriteria, IInternalTestRunEventsHandler eventHandler) in /_/src/vstest/src/Microsoft.TestPlatform.CrossPlatEngine/Client/ProxyExecutionManager.cs:line 184

Test Run Aborted.
```

Kiểm Docker bằng `docker info --format '{{.ServerVersion}}'`, exit 1:

```text
permission denied while trying to connect to the docker API at unix:///Users/nhm809/.docker/run/docker.sock
```
Không đổi sang DB dev hay bỏ guard để né hạn chế. Không thực hiện phá thử khi runner chưa chạy được.

### Các ca test đã thêm / mở rộng, chờ chạy

- `Evidence_accepts_every_work_order_target`: inspection/repair theo tuyến; survey có 203 cột, chọn cột nằm ngoài trang 200 đầu; GPS chỉ nhận target được nêu, không nhận cột khác cùng đường.
- `Evidence_accepts_a_fault_target_without_a_road_or_geometry_and_does_not_publish`: cột từ fault, không tuyến/geom vẫn nhận; không publish status hay sinh thêm fault.
- `Evidence_rejects_missing_out_of_scope_and_unrelated_poles_before_storage`: missing/ngoài scope 404; ngoài tập 409; quyền cha kiểm trước; chưa ghi object.
- `Evidence_requires_the_orders_commune_even_when_the_caller_sees_both`: scope giả lập hai xã chỉ trong host test (không tạo tài khoản nhiều xã trái ACCT-1), cột cùng đường khác xã 409; raw UPDATE chứng minh FK ghép chặn và kiểm đúng constraint.
- `Evidence_rechecks_targets_after_storage`: DuringWrite đổi tuyến/xoá cột/rút mục tiêu tuyến khỏi phiếu ⇒ đúng 404/409, không evidence row. Giữ nguyên hồi quy reassign/cancel lúc upload.
- `Evidence_replay_compares_nullable_pole_on_every_path`: 3 đường × 3 chiều (khác cột, NULL→cột, cột→NULL). Unique branch dùng interceptor cho hai lượt replay đầu chưa thấy hàng, DB thật phát 23505; lượt sau rollback đọc hàng cũ và phải 409, assert 3 lượt đọc.
- `Evidence_replays_the_same_pole_after_completion_and_target_changes`: replay cùng cột sau done và sau rời tập vẫn 200 cùng snapshot. Giữ nguyên replay không cột và overlapping retry sau complete hiện có.
- `Evidence_filter_counts_before_pagination_and_keeps_capture_order`: trộn hai cột + NULL, total và trang 2 đúng theo filter, filter không khớp rỗng; app cũ vẫn 201/null. Ca media hiện có thêm assert null khi thiếu pole.
- `Evidence_fk_prevents_pole_deletion_with_a_clean_asset_conflict`: chỉ ảnh evidence giữ cột, DELETE tài sản phải 409 `ASSET_IN_USE`, cột/ảnh còn nguyên.
- `Evidence_holds_order_then_pole_locks_without_holding_them_during_storage`: chặn cột bằng `FOR NO KEY UPDATE`; đợi tới lệnh khoá cột, probe độc lập chứng minh phiếu đã khoá; storage chạy khi chưa có khoá; cuối cùng 201. Không dùng FOR UPDATE làm khoá probe gây xanh giả qua FK.
- `Evidence_migration_preserves_old_rows_and_refuses_rollback_with_pole_links`: container PostGIS riêng; ghi SQL schema cũ (không EF AppUser); Up giữ hàng NULL/index cũ; assert FK/CHECK/index/model/key; Down có liên kết phải từ chối và giữ hàng/history; bỏ liên kết chỉ trong DB test, Down/reapply sạch.
- `FaultReportTests`: fault có/không pole đều trả pole_id NULL; CHECK chặn ảnh fault có pole; giữ one-parent/observation.
- `Evidence_has_an_optional_nullable_pole_and_a_query_filter_but_fault_upload_does_not`: kiểm wire schema form/DTO/filter/fault. Multipart pole là field string không required; nullable được mô tả ở DTO.

Cả hai file test mới là **partial của WorkOrderTests**, giữ `AssetDatabaseCollection`/fixture và teardown có sẵn (evidence trước pole). Không ghim ID thực; ca missing dùng ID không hợp lệ cố ý. Test migration không chạm DB dev.

## 7. Chưa làm / bàn giao Claude

1. Chạy thực các test mới và hồi quy, kiểm API/media/DB trên PostGIS test; hiện chỉ xác nhận biên dịch. Có thể chạy trước:

   ```bash
   dotnet test tests/LuxMap.Api.Tests --disable-build-servers --filter 'FullyQualifiedName~WorkOrderTests|FullyQualifiedName~FaultReportTests|FullyQualifiedName~OpenApiSpecTests'
   ```

2. Chạy test migration Up/Down/reapply; đối chiếu `pg_constraint`/index thực. Script SQL sinh ra **không phải** bằng chứng đã apply thành công.
3. Phá thử theo kế hoạch: bỏ membership/cùng xã, bỏ kiểm sau storage, bỏ khoá cột; bỏ so pole ở từng đường replay; bỏ FK/CHECK mới; đẩy filter sau count/phân trang. Mỗi lần phải test đỏ, khôi phục bằng cp/touch và build/test lại với `--disable-build-servers`. Bỏ ca scope cột độc lập của phương án liên xã vì Mỹ chọn D-3 A.
4. Claude review rồi commit theo yêu cầu Mỹ; lượt này không stage/commit/push/tạo nhánh, không mở PR, không apply DB dev hay chạy copy DB.
5. Mỹ/WP5/WP6 xác nhận EV-4 ở FW tiếp theo; chưa gửi thông báo cho ai.

Không có D-item kiến trúc mới. Các phần chưa kiểm chứng được ghi ở card EVIDENCE-POLE trong tracking.html.


## 7. Nghiệm thu runtime — Claude, 14/11/2026

Chạy ngoài sandbox, trên cây làm việc này (PostGIS Testcontainers riêng mỗi lượt).

- **Sửa trước khi chạy:** `WorkOrderEvidenceMigrationTests` truyền `users["a"]` (tài khoản dùng chung của lớp) vào `OldSchemaUser.InsertAsync`,
  hàm này **ghi đè `UserId`** bằng id trong container riêng ⇒ teardown `WorkOrderTests.DisposeAsync` xoá nhầm id và vỡ ở
  `fk_app_user_commune_administrative_unit_commune_id`. Thân test đã đạt; sửa bằng tài khoản mới riêng như `WorkOrderCancellationMigrationTests`.
- `dotnet test --filter "FullyQualifiedName~WorkOrderTests.Evidence_"` ⇒ `Passed!  - Failed: 0, Passed: 24, Skipped: 0, Total: 24`, mã thoát 0.
- Toàn bộ `dotnet test --no-build --disable-build-servers` ⇒ mã thoát 0:
  `LuxMap.Shared.Tests 427/427`, `LuxMap.Persistence.Tests 45/45`, `LuxMap.Infrastructure.Storage.Tests 83/83`,
  `LuxMap.Api.Tests Passed: 1747, Skipped: 7, Total: 1754`.
- Phá thử (mỗi lần một chốt trong `WorkOrderEvidence.cs`, khôi phục bằng `cp` + `touch`, chạy `WorkOrderTests.Evidence_`):

| Chốt bị gỡ | Kết quả | Test đỏ |
|---|---|---|
| thuộc `Targets` | 4 fail / 24 | `Evidence_rechecks_targets_after_storage` (move, withdraw), `Evidence_accepts_every_work_order_target(PoleGpsUpdate)`, `Evidence_rejects_missing_out_of_scope_and_unrelated_poles_before_storage` |
| cùng xã phiếu | 1 fail / 24 | `Evidence_requires_the_orders_commune_even_when_the_caller_sees_both` |
| kiểm lại sau storage | 4 fail / 24 | `Evidence_rechecks_targets_after_storage` (move, withdraw, delete), `Evidence_holds_order_then_pole_locks_without_holding_them_during_storage` |
| so `pole_id` khi replay | 9 fail / 24 | `Evidence_replay_compares_nullable_pole_on_every_path` (3 đường × 3 chiều) |
| khoá cột | 1 fail / 24 | `Evidence_holds_order_then_pole_locks_without_holding_them_during_storage` |

- OpenAPI: xuất lại `luxmap-v1.json` + chạy generator ⇒ trùng byte với file trong cây (`cmp` không khác).
