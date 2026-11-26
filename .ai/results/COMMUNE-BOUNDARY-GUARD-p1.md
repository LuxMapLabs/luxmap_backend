# COMMUNE-BOUNDARY-GUARD — Phase 1 (khảo sát)

- Ticket: `.ai/tasks/COMMUNE-BOUNDARY-GUARD.md` — phase 1, `status: ready`
- Nhánh: `feat/COMMUNE-BOUNDARY-guard`, dựng từ `origin/dev`; HEAD lúc khảo sát `50be400`
- Ngày: 26/11/2026
- **Chưa chạy `dotnet test`** — sandbox không có Docker nên không dựng được PostGIS/Testcontainers. Mọi kết luận dưới đây là đọc mã và đối chiếu, không phải kết quả chạy test.
- Giữ nguyên quyết định đã chốt: D1 dung sai 0 m + `ST_Covers`; D2 xã không ranh giới → từ chối; D3 ranh giới chỉ nạp qua `PUT /admin/communes/{id}/boundary`; D4 chỉ kiểm khi toạ độ hoặc `commune_id` đổi (tạo mới luôn kiểm); phạm vi chỉ `pole` + `electrical_cabinet`.
- Không dùng `bbox` để kiểm (CB-1 vẫn đúng), không suy `commune_id` từ ranh giới, không kiểm tuyến/feeder/sự cố/IoT, không đổi hành vi endpoint ranh giới, không tạo migration.

## Kết luận nhanh

1. Chốt chặn đặt được **trong service**, gọi sau các kiểm phạm vi/tồn tại hiện có và trước `SaveChanges`; **không** đặt trong `CommuneWriteGuard` (guard chỉ thấy ChangeTracker, không tạo được lỗi theo dòng của import và không phân biệt được "toạ độ không đổi").
2. Npgsql 10.0.3 **dịch được** `Geometry.Covers` → `ST_Covers` (đã kiểm trong DLL, xem §2.4) nên không cần `HasDbFunction` mới; nhưng đường import vẫn nên đọc **một truy vấn** rồi `Covers` in-memory để có lỗi theo từng dòng.
3. Rủi ro lớn nhất không nằm ở code mà ở **fixture**: `AssetImportFixture` tạo 2 xã **không ranh giới** và là collection fixture dùng chung cho 86 file test; `AssetWorkbookTestContext` tạo thêm 2 xã cũng không ranh giới.
4. Dữ liệu mock/`COM-070` không có nguồn polygon: `scripts/load_commune_boundaries.py` chỉ có COM-001/COM-002 (OSM). Seeder ghi thẳng SQL nên vẫn ghi được, nhưng sẽ để lại cột "ngoài ranh giới" — cần một D-item riêng.

---

## 1. Mọi đường ghi làm đổi `Pole.Geom` / `Pole.CommuneId` / `ElectricalCabinet.Geom` / `ElectricalCabinet.CommuneId`

Quét toàn `src/` (trừ `Migrations/`) cho `.Geom =` chỉ ra đúng 8 chỗ; 5 chỗ liên quan tài sản dạng điểm, 3 chỗ là tuyến/feeder (ngoài phạm vi):

```text
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:271   cabinet.Geom = Read<Point>(request.GeomWkt);
src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:636   if (request.GeomWkt is not null) pole.Geom = Read<Point>(request.GeomWkt);
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:257  cabinet.Geom = geometry!;
src/LuxMap.Modules.Assets/Import/AssetImportService.cs:466  pole.Geom = geometry;
src/LuxMap.Modules.Assets/Crud/PoleRelocation.cs:59      pole.Geom = measured.Target;
```

(Ba chỗ còn lại: `AssetCrudService.cs:491` segment, `:582` feeder, `AssetImportService.cs:203` segment và `:359` feeder — tuyến/feeder không kiểm.)

| # | Đường ghi | Điểm vào | Qua service chung? | Đổi `commune_id`? | Kiểm ranh giới hiện tại |
|---|---|---|---|---|---|
| 1 | `POST /assets/poles` | `AssetCrudService.CreatePoleAsync` `:341-380` | Có (`CheckedCommuneAsync`, `RequireFeederInCommuneAsync`) | Có — **đặt lúc tạo** | Không |
| 2 | `PUT /assets/poles/{id}` | `AssetCrudService.UpdatePoleAsync` `:611-657` | Có | **Không** — `UpdatePoleRequest` không có `commune_id` | Không |
| 3 | `POST /assets/cabinets` | `AssetCrudService.CreateCabinetAsync` `:242-259` | Có (`CheckedCommuneAsync`) | Có — đặt lúc tạo | Không |
| 4 | `PUT /assets/cabinets/{id}` | `AssetCrudService.UpdateCabinetAsync` `:263-276` | Có | **Không** | Không |
| 5 | `PUT /assets/poles/{id}/location` | `AssetCrudService.SetPoleLocationAsync` `:897-936` → `PoleRelocation` | Có (`PoleRelocation`) | Không | Không (chỉ hành lang tuyến) |
| 6 | `POST /work-orders/{id}/act` action `verify`, `task_kind = pole_gps_update` | `WorkOrderService.Act` `:607` → `MovePolesAsync` `:783-812` → `PoleRelocation` | Có (cùng `PoleRelocation`) | Không | Không |
| 7 | Import CSV / GeoJSON (poles, cabinets) | `AssetImportService.ImportCsvAsync` `:46`, `ImportGeoJsonAsync` `:66`, `ImportAsync` `:108-131`; planner `PlanCabinetsAsync` `:230-283`, `PlanPolesAsync` `:386-507` | Có (`CommuneAsync` + planner) | Chỉ **tạo** (upsert khoá `(commune_id, external_ref)`) | Không |
| 8 | Import workbook nhiều sheet | `AssetImportWorkbook.ImportWorkbookAsync` `:11-89`, dùng lại `PlanAsync` `:75` | Có (cùng planner) | Chỉ tạo | Không |
| 9 | `POST /sync/push` | `SyncPushService` `:199-206`, `:387-390` | Có — `WorkOrderComplete` gọi `workOrders.Act(...)` | Không trực tiếp | Theo đường 6 |
| 10 | Seeder / mock / demo | `scripts/seed_mock_set.py` `:66`, `:158-160`, `:222-224` | **Không** — SQL thô, không qua service/guard | Có (chèn thẳng) | Không |

Không có job nền nào ghi `pole.geom` / `cabinet.geom`: ngoài 8 dòng `.Geom =` ở trên không còn chỗ nào; các job trong `src/LuxMap.Modules.IOT/Jobs/` chỉ ghi sự cố/telemetry.

### 1.1 CRUD cột / tủ điện

Tạo cột: `commune_id` lấy từ body, chỉ kiểm **phạm vi JWT + tồn tại**, `geom` được phép `null` (POLE-GEOM-NULL):

`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:341-370`
```csharp
    public async Task<string> CreatePoleAsync(CreatePoleRequest request, CancellationToken ct)
    {
        var segmentId = request.ReadSegmentId();

        var communeId = await CheckedCommuneAsync(request.CommuneId!, ct);
        await RejectDuplicateRefAsync<Pole>(communeId, request.ExternalRef, ct);
        ...
        await RequireFeederInCommuneAsync(request.FeederId, communeId, ct);
        ...
        var pole = new Pole
        {
            ...
            CommuneId = communeId,
            // Absent / null = a pole from the books with no position yet (POLE-GEOM-NULL); blank or malformed is still a 400.
            Geom = request.GeomWkt is null ? null : Read<Point>(request.GeomWkt),
```

`CheckedCommuneAsync` chỉ làm hai việc — không có ranh giới:

`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:1397-1413`
```csharp
    private async Task<string> CheckedCommuneAsync(string communeId, CancellationToken ct)
    {
        // Narrow is the entry-point check Contract section 7 asks for: it answers 403 naming the
        // rejected commune, where the query filter would only ever produce a silent empty result.
        CommuneFilter.Narrow(scopeAccessor.Scope, [communeId]);

        var exists = await dbContext.Set<AdministrativeUnit>().AsNoTracking()
            .AnyAsync(unit => unit.CommuneId == communeId, ct);

        return exists
            ? communeId
            : throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                "That commune does not exist.",
                new Dictionary<string, object?> { ["commune_id"] = communeId });
    }
```

Sửa cột: không có `commune_id`; `geom` chỉ ghi khi client gửi WKT (và bắt buộc nếu cột đã có vị trí):

`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:611-642`
```csharp
    public async Task UpdatePoleAsync(string poleId, UpdatePoleRequest request, CancellationToken ct)
    {
        ...
        var pole = await RequireAsync<Pole>(candidate => candidate.PoleId == poleId, "pole", ct);
        ...
        await RequireFeederInCommuneAsync(request.FeederId, pole.CommuneId, ct);
        ...
        // GN-2: a pole that has a position keeps one — absent / null is a 400; a pole without one stays without unless a WKT arrives.
        if (request.GeomWkt is not null) pole.Geom = Read<Point>(request.GeomWkt);
        else if (pole.Geom is not null)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "geom_wkt is required for a pole that already has a position.",
                new Dictionary<string, object?> { ["field"] = "geom_wkt" });
        }
```

Tủ điện: tạo bắt buộc `geom`; sửa **luôn ghi đè** `geom` (đây là chỗ D4 phải so sánh toạ độ, không phải "có gửi hay không"):

`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:242-252`
```csharp
    public async Task<string> CreateCabinetAsync(CreateCabinetRequest request, CancellationToken ct)
    {
        var communeId = await CheckedCommuneAsync(request.CommuneId!, ct);
        await RejectDuplicateRefAsync<ElectricalCabinet>(communeId, request.ExternalRef, ct);

        var cabinet = new ElectricalCabinet
        {
            ...
            CommuneId = communeId,
            Geom = Read<Point>(request.GeomWkt),
```

`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:263-271`
```csharp
    public async Task UpdateCabinetAsync(string cabinetId, UpdateCabinetRequest request, CancellationToken ct)
    {
        var cabinet = await RequireAsync<ElectricalCabinet>(candidate => candidate.CabinetId == cabinetId, "cabinet", ct);
        ...
        cabinet.Geom = Read<Point>(request.GeomWkt);
```

### 1.2 Dời cột — một luật, hai caller

`PoleRelocation.MeasureAsync` dựng điểm đích và **chỉ** đo khoảng cách/hành lang; `Stage` mới ghi `pole.Geom`:

`src/LuxMap.Modules.Assets/Crud/PoleRelocation.cs:26-42`
```csharp
    public async Task<Measured> MeasureAsync(Pole pole, double lat, double lng, CancellationToken ct)
    {
        var target = new Point(lng, lat) { SRID = 4326 };
        ...
        double? offSegment = pole.SegmentId is null ? null : await dbContext.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => segment.SegmentId == pole.SegmentId && segment.Geom != null)
            .Select(segment => (double?)SpatialFunctions.DistanceMeters(segment.Geom!, target)).SingleOrDefaultAsync(ct);
        return new Measured(pole, target, moved, offSegment);
    }
```

`src/LuxMap.Modules.Assets/Crud/PoleRelocation.cs:54-59`
```csharp
    public bool Stage(Measured measured, string? workOrderId)
    {
        if (IsOffSegment(measured)) throw new InvalidOperationException("Check IsOffSegment before staging a relocation.");
        var pole = measured.Pole;
        var before = pole.Geom is null ? null : new { lat = pole.Geom.Y, lng = pole.Geom.X };
        pole.Geom = measured.Target;
```

`Measured.Unchanged` đã có sẵn đúng ngữ nghĩa "không đổi toạ độ" cho D4:

`src/LuxMap.Modules.Assets/Crud/PoleRelocation.cs:78`
```csharp
        public bool Unchanged => Pole.Geom is { } current && current.X == Target.X && current.Y == Target.Y;
```

Caller 1 — endpoint dời cột (đọc ngoài phạm vi → 404 trước, rồi `FOR UPDATE`, rồi mới đo):

`src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:911-935`
```csharp
        if (!await dbContext.Set<Pole>().AnyAsync(candidate => candidate.PoleId == poleId, ct))
        {
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
                "That pole does not exist, or it is outside your permitted commune scope.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await dbContext.Database.ExecuteSqlRawAsync("SELECT 1 FROM pole WHERE pole_id = {0} FOR UPDATE", [poleId], ct);
        var pole = await RequireAsync<Pole>(candidate => candidate.PoleId == poleId, "pole", ct);
        var measured = await relocation.MeasureAsync(pole, lat, lng, ct);
        if (relocation.IsOffSegment(measured)) { throw ... ErrorCodes.PoleOffSegment ... 409 ... }

        var moved = relocation.Stage(measured, workOrderId: null) ? measured.MovedM : 0;
```

Caller 2 — verify phiếu GPS cột, khoá theo thứ tự `work_order` → từng `pole`, rồi `Stage` cả lô:

`src/LuxMap.Modules.WorkOrders/WorkOrderService.cs:783-811`
```csharp
    private async Task MovePolesAsync(WorkOrder wo, List<WorkOrderPoleTarget> targets, CancellationToken ct)
    {
        var reported = targets.Where(x => x.ReportedLat is not null).ToList();
        var poleIds = reported.Select(x => x.PoleId).OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).ToArray();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {wo.WorkOrderId} FOR UPDATE", ct);
        foreach (var poleId in poleIds)
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} FOR UPDATE", ct);
        var poles = await db.Set<Pole>().Where(x => poleIds.Contains(x.PoleId)).ToDictionaryAsync(x => x.PoleId, StringComparer.Ordinal, ct);
        ...
        var off = moves.Where(relocation.IsOffSegment).ToArray();
        if (off.Length != 0)
            throw Error("POLE_OFF_SEGMENT", HttpStatusCode.Conflict, ("work_order_id", wo.WorkOrderId),
                ("poles", off.Select(x => new
                {
                    pole_id = x.Pole.PoleId, segment_id = x.Pole.SegmentId,
                    distance_m = Math.Round(x.SegmentDistanceM!.Value, 1), corridor_m = relocation.CorridorM,
                }).ToArray()));
        foreach (var move in moves) relocation.Stage(move, wo.WorkOrderId);
        await db.SaveChangesAsync(ct);
    }
```

### 1.3 Import CSV / GeoJSON

Một transaction + advisory lock **theo loại tài sản**; planner trả lỗi theo dòng, không ném:

`src/LuxMap.Modules.Assets/Import/AssetImportService.cs:108-131`
```csharp
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockKindAsync(kind, cancellationToken);
        var plan = await PlanAsync(kind, readers, warnings, cancellationToken);
        var failed = readers.Count(reader => !reader.IsValid);
        ...
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ImportResult.From(plan.Inserted, plan.Updated, plan.Unchanged, failed, errors, warnings);
```

`src/LuxMap.Modules.Assets/Import/AssetImportService.cs:135-147`
```csharp
    private Task<int> LockKindAsync(ImportKind kind, CancellationToken cancellationToken)
    {
        var lockKey = kind switch
        {
            ImportKind.Segments => 0x4C4D4901L,
            ImportKind.Cabinets => 0x4C4D4902L,
            ...
            ImportKind.Poles => 0x4C4D4904L,
            ...
        };
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
    }
```

`CommuneAsync` chỉ kiểm phạm vi + tồn tại (đúng như ticket mô tả), và trả `null` để planner bỏ dòng:

`src/LuxMap.Modules.Assets/Import/AssetImportService.cs:724-744`
```csharp
    private async Task<string?> CommuneAsync(ImportRowReader reader, CancellationToken cancellationToken)
    {
        var communeId = reader.Required("commune_id");
        if (communeId is null) return null;
        if (!scopeAccessor.Scope.Allows(communeId))
        {
            reader.Fail("commune_id", $"'{communeId}' is outside your permitted commune scope.");
            return null;
        }
        var exists = await dbContext.Set<AdministrativeUnit>().AsNoTracking()
            .AnyAsync(unit => unit.CommuneId == communeId, cancellationToken);
        if (!exists) { reader.Fail("commune_id", $"'{communeId}' is not a known commune."); return null; }
        return communeId;
    }
```

Tủ điện — dòng mới và dòng cũ đều ghi `geom` (dòng cũ ghi đè vô điều kiện, nên D4 phải so sánh):

`src/LuxMap.Modules.Assets/Import/AssetImportService.cs:257-270`
```csharp
                cabinet.CabinetName = name!;
                cabinet.Geom = geometry!;
                cabinet.DataSource = dataSource;
                ...
            var created = new ElectricalCabinet
            {
                ...
                CommuneId = communeId!,
                Geom = geometry!,
```

Cột — dòng cũ **giữ** vị trí nếu file không có `geom_wkt`; dòng mới nhận `geom` (có thể `null`):

`src/LuxMap.Modules.Assets/Import/AssetImportService.cs:455-489`
```csharp
                if (geometry is not null)
                {
                    pole.Geom = geometry;
                }
                ...
            var created = new Pole
            {
                ...
                CommuneId = communeId!,
                Geom = geometry,
```

### 1.4 Import workbook nhiều sheet

Một xã cho **cả file**, lấy từ form (hoặc từ scope nếu scope đúng một xã); mọi loại dùng chung `PlanAsync`; có lỗi thì rollback toàn bộ:

`src/LuxMap.Modules.Assets/Import/AssetImportWorkbook.cs:18-30`
```csharp
        if (string.IsNullOrWhiteSpace(communeId))
        {
            if (scopeAccessor.Scope.IsSystemWide || scopeAccessor.Scope.CommuneIds.Count != 1)
                Invalid("commune_id", "Select one commune.");
            communeId = scopeAccessor.Scope.CommuneIds.Single();
        }
        communeId = communeId.Trim();
        CommuneFilter.Narrow(scopeAccessor.Scope, [communeId]);
```

`src/LuxMap.Modules.Assets/Import/AssetImportWorkbook.cs:75-85`
```csharp
            var plan = await PlanAsync(kind, readers, kindWarnings, cancellationToken, matchFixtures: true);
            ...
        if (errors.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Result(false);
        }
```

### 1.5 `POST /sync/push`

Không có thao tác sync nào ghi thẳng `pole.geom` / `commune_id`. Đường duy nhất chạm vị trí là `WorkOrderComplete` mang `pole_locations`, và nó đi qua đúng service của endpoint HTTP:

`src/LuxMap.Modules.Sync/SyncPushService.cs:199-206`
```csharp
    private static string PolicyFor(SyncOpType type) => type switch
    {
        SyncOpType.FaultReport => LuxMapPolicies.ReportFaults,
        SyncOpType.LuxReading => LuxMapPolicies.RecordLuxReading,
        SyncOpType.PoleNote => LuxMapPolicies.EditPoleNotes,
        SyncOpType.WorkOrderStart or SyncOpType.WorkOrderComplete => LuxMapPolicies.ExecuteWorkOrders,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
```

`src/LuxMap.Modules.Sync/SyncPushService.cs:387-390`
```csharp
            case CompletePayload complete:
                await workOrders.Act(parsed.EntityId!, "complete", complete.ReportNote, complete.FaultOutcomes, ct,
                    complete.MaterialsUsed, complete.PerformedAt, complete.PoleLocations);
                break;
```

Lỗi 404 / 409 (trừ `IDEMPOTENCY_CONFLICT`) thành `conflicts[]`, còn lại `rejected[]` — quyết định hình dạng lỗi ở §3:

`src/LuxMap.Modules.Sync/SyncPushService.cs:417-419`
```csharp
    private static bool IsConflict(LuxMapException error)
        => error.StatusCode == HttpStatusCode.NotFound
           || (error.StatusCode == HttpStatusCode.Conflict && error.Code != "IDEMPOTENCY_CONFLICT");
```

### 1.6 Seeder / mock / demo

Xã được tra bằng `seed_key = 'study_site'` (trên dev là `COM-070` "Commune 01"), và cột/tủ được chèn bằng **SQL thô** — không qua service, không qua `CommuneWriteGuard`, không qua kiểm ranh giới:

`scripts/seed_mock_set.py:63-66`
```python
# Every seeded row belongs to the commune BE-06 seeds. Resolved in SQL rather than here, ...
COMMUNE = "(SELECT commune_id FROM administrative_unit WHERE seed_key = 'study_site')"
```

`scripts/seed_mock_set.py:158-160`
```python
            "INSERT INTO pole (pole_id, segment_id, feeder_id, feeder_source, commune_id, geom, "
            "near_sensitive_poi, data_source, external_ref) VALUES ("
            f"{quote(p['pole_id'])}, {quote(p['segment_id'])}, {quote(demo_feeder[p['segment_id']])}, 'inferred', {COMMUNE}, "
```

`scripts/seed_mock_set.py:222-224`
```python
            "INSERT INTO electrical_cabinet (cabinet_id, cabinet_name, external_ref, commune_id, geom, data_source) VALUES ("
            f"{quote(cabinet_id)}, {quote('Tủ ' + p['node_id'])}, {quote('DEMO-' + cabinet_id)}, {COMMUNE}, "
            f"{geometry(node['geometry'])}, 'simulated');")
```

---

## 2. Một chỗ định nghĩa duy nhất + cách gọi từ từng đường

### 2.1 Nơi đặt

Đặt **`LuxMap.Persistence`**, cạnh `AdministrativeUnit` và `SpatialFunctions`:

- `administrative_unit.boundary` thuộc Persistence (`src/LuxMap.Persistence/AdministrativeUnit.cs:72`), và mọi module liên quan đều đã tham chiếu Persistence: Assets, Admin, Sync (`*.csproj` dòng 5), WorkOrders (dòng 5), Faults (dòng 5).
- WorkOrders **không** tham chiếu trực tiếp Assets nhưng đi qua Faults (`src/LuxMap.Modules.Faults/LuxMap.Modules.Faults.csproj:9`), và đã dùng `PoleRelocation` của Assets (`WorkOrderService.cs:3`, `:25`) — nên đặt luật trong Persistence là tầng thấp nhất dùng chung được, **không cần port mới**.
- Có tiền lệ đúng khuôn: `SpatialFunctions.DistanceMeters` là marker chỉ dùng trong LINQ, map sang PostGIS bằng `HasDbFunction` + `HasTranslation`, không cần migration (`src/LuxMap.Persistence/SpatialFunctions.cs:44-102`).

Đề xuất API tối thiểu (một luật, hai cách nạp):

```csharp
// Persistence — luật duy nhất của D1/D2
public enum BoundaryVerdict { Inside, Outside, Missing }

public static class CommuneBoundary
{
    // Hàm thuần: mọi caller đều rơi vào đây
    public static BoundaryVerdict Verdict(MultiPolygon? boundary, Point point)
        => boundary is null ? BoundaryVerdict.Missing
           : boundary.Covers(point) ? BoundaryVerdict.Inside
           : BoundaryVerdict.Outside;
}
```

### 2.2 Cách gọi từng đường (vị trí tương đối với kiểm sẵn có)

| Đường | Gọi ở đâu | Trước / sau kiểm sẵn có | Ghi chú |
|---|---|---|---|
| `CreatePoleAsync` | sau `RequireFeederInCommuneAsync` (`:357`), trước `new Pole` (`:362`) | **sau** segment/feeder | Giữ nguyên 409 `CROSS_COMMUNE_REFERENCE` của test hiện có |
| `UpdatePoleAsync` | sau `RequireFeederInCommuneAsync` (`:626`), ngay trước/sau khi đọc `Read<Point>` (`:636`) | sau | Chỉ khi geom **đổi** (D4) |
| `CreateCabinetAsync` | sau `CheckedCommuneAsync` (`:244`) và `Read<Point>` | sau | Cabinet luôn có điểm |
| `UpdateCabinetAsync` | sau `RequireAsync` (`:265`), **trước** `cabinet.Geom = ...` (`:271`) | sau | Bắt buộc so sánh cũ/mới vì dòng 271 luôn ghi |
| `SetPoleLocationAsync` | trong `PoleRelocation.MeasureAsync` (sau khi có `target`, `:28`) | sau `FOR UPDATE` + `RequireAsync` | Dùng lại cho caller 2, không lặp luật |
| `MovePolesAsync` (GPS verify) | tự động qua `MeasureAsync` | sau khoá `work_order` → `pole` | Không sửa gì thêm |
| Import CSV/GeoJSON | trong `PlanCabinetsAsync` sau `:241`; `PlanPolesAsync` sau `:404` | sau `CommuneAsync`, trước `if (!reader.IsValid) continue` | Lỗi theo dòng, không ném |
| Import workbook | không thêm code — dùng lại planner | — | Lỗi dòng tự đổi thành `WorkbookRowError` ở `AssetImportWorkbook.cs:75` |
| Sync | không thêm code — đi qua `MovePolesAsync` | — | Hình dạng lỗi xem §3 |

### 2.3 Vì sao KHÔNG đặt trong `CommuneWriteGuard`

`CommuneWriteGuard` chạy trong `SaveChanges` và chỉ có `ChangeTracker` (`src/LuxMap.Persistence/CommuneWriteGuard.cs:40-78`). Ba lý do không dùng được:

1. Nó chỉ ném **một** lỗi 403 `COMMUNE_FORBIDDEN`; import cần lỗi **theo từng dòng** (`ImportRowReader.Fail`) để trả `failed`/`rows[]`.
2. Nó không biết "toạ độ có đổi không": `UpdateCabinetAsync` luôn gán `Geom` (D4 sẽ chặn nhầm cột cũ đang lệch nếu không so sánh), và `UpdatePoleAsync` không có `commune_id` để so.
3. Nó là **lưới an toàn phạm vi**, không phải nơi đặt luật nghiệp vụ có thông điệp riêng; trộn vào sẽ làm 403 và 400 lẫn nhau.

Giữ nguyên vai trò: guard vẫn là backstop 403; kiểm ranh giới là một lớp **trước** `SaveChanges`, trong service.

### 2.4 EF có dịch `ST_Covers` không? — CÓ (đã kiểm trong DLL)

`Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` 10.0.3 có map `Covers` → `ST_Covers` (cùng `Contains`/`ST_Contains`, `Intersects`/`ST_Intersects`, `CoveredBy`, `Within`, …):

```text
$ perl -0777 -ne '...' Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite.dll \
    | grep -oE '(Covers|Contains|Intersects|ST_Covers|ST_Contains|ST_Intersects)' | sort -u
Contains
Covers
Intersects
... ST_CoversST_Contains ... ST_Intersects ...
```

Ý nghĩa cho ticket:

- CRUD một dòng có thể viết `unit.Boundary!.Covers(point)` trong LINQ và EF sinh `ST_Covers(boundary, @point)` — **không cần** thêm `HasDbFunction` như `DistanceMeters`.
- Nhưng `boundary` nullable: đừng viết `Where(u => u.Boundary.Covers(point))` (NULL → row bị loại, không phân biệt được "ngoài" với "chưa có ranh giới"). Hoặc đọc `Boundary` rồi gọi `Covers` in-memory (1 truy vấn, ba trạng thái rõ), hoặc chiếu `bool?`.
- `Covers` là quan hệ **topological**, không phải metric: cả hai hình 4326 (độ) là đúng, **không** reproject sang 3405. Khác hẳn `DistanceMeters`.

### 2.5 Gom N dòng của import — một truy vấn

Planner đã có sẵn danh sách reader; làm một lượt trước vòng lặp:

```csharp
var ids = readers.Select(r => r.Optional("commune_id")).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
var boundaries = await dbContext.Set<AdministrativeUnit>().AsNoTracking()
    .Where(u => ids.Contains(u.CommuneId))
    .Select(u => new { u.CommuneId, u.Boundary })
    .ToDictionaryAsync(x => x.CommuneId, x => x.Boundary, StringComparer.Ordinal, ct);
```

Rồi trong vòng lặp, sau khi có `geometry`:

```csharp
var verdict = CommuneBoundary.Verdict(boundaries.GetValueOrDefault(communeId!), geometry);
if (verdict == BoundaryVerdict.Missing)
    reader.Fail("commune_id", $"Commune '{communeId}' has no boundary; set it before importing assets there.");
else if (verdict == BoundaryVerdict.Outside)
    reader.Fail("geom_wkt", "That position is outside the commune's boundary.");
```

Không N truy vấn; không phụ thuộc dịch `CASE` của EF; cùng một helper dùng cho cả CSV, GeoJSON và workbook.

---

## 3. Hình dạng lỗi

Nguyên tắc hiện có: `ErrorCodes` chỉ chứa mã **Contract đã đặc tả**; mã mới phải vào Contract trước (`src/LuxMap.Shared/Contracts/Errors/ErrorCodes.cs:1-6`). Vì vậy đề xuất **tái dùng** `VALIDATION_FAILED` (400) + `details` thay vì thêm mã, trừ khi Mỹ muốn FE phân biệt bằng mã.

Hai lý do phải phân biệt được bằng máy:

- `OUTSIDE_COMMUNE_BOUNDARY` — xã **có** ranh giới, điểm nằm ngoài.
- `COMMUNE_BOUNDARY_MISSING` — xã **chưa có** ranh giới (D2).

| Đường | Đề xuất | Ghi chú |
|---|---|---|
| CRUD tạo/sửa cột, tủ | `400 VALIDATION_FAILED`, `details { field: "geom_wkt", reason: "OUTSIDE_COMMUNE_BOUNDARY" \| "COMMUNE_BOUNDARY_MISSING", commune_id }` | Đúng khuôn hiện có: `AssetCrudService.cs:639-641` dùng `["field"] = "geom_wkt"`; test `AssetCoordinateRangeTests` đang chờ `400` + chuỗi `VALIDATION_FAILED` |
| Dời cột / GPS verify | `400 VALIDATION_FAILED`, `field: "location"` / `field: "pole_locations"`, `reason` như trên | **D-item**: 400 hay 409? Xem D-3 |
| Import CSV/GeoJSON | HTTP **200** với `failed++` và `rows[]`: lỗi `geom_wkt` (ngoài) / `commune_id` (thiếu ranh giới) | Giữ nguyên hình dạng `ImportResult` (`ImportRowError(row, column, message)`) |
| Import workbook | Như trên; nếu file chỉ một xã và xã **thiếu ranh giới** → có thể chặn sớm `400` ở `Invalid("commune_id", …)` như `AssetImportWorkbook.cs:22`, thay vì lặp N dòng | **D-item**: chặn sớm hay lỗi từng dòng? |
| Sync push | Nếu chọn 400 → `rejected[]` (deterministic, client sửa được); nếu 409 → `conflicts[]` + server state | **D-item** D-4; hiện `IsConflict` chỉ nhận 404/409 (`SyncPushService.cs:417-419`) |

Thông điệp nên nêu **tên xã + lý do**, không nêu toạ độ của ranh giới (không lộ dữ liệu ngoài phạm vi — cùng lý do `CommuneFilter.Narrow` echo id bị từ chối, xem `CommuneWriteGuard.cs:75-77`).

---

## 4. Tác động lên test

### 4.1 Bán kính nổ

- **86 file** test tham chiếu `AssetImportFixture`, **87 class** — và đây là **collection fixture dùng chung**, không phải mỗi class một bản:
  `tests/LuxMap.Api.Tests/AssetDatabaseCollection.cs:34-36`
  ```csharp
  [CollectionDefinition(nameof(AssetDatabaseCollection))]
  public sealed class AssetDatabaseCollection
      : ICollectionFixture<AssetSchemaFixture>, ICollectionFixture<AssetImportFixture>;
  ```
- Fixture tạo **hai xã không ranh giới**:
  `tests/LuxMap.Api.Tests/AssetImportFixture.cs:77-78`
  ```csharp
              var mine = new AdministrativeUnit { Name = $"BE-12a home {Guid.NewGuid():N}"[..40] };
              var theirs = new AdministrativeUnit { Name = $"BE-12a foreign {Guid.NewGuid():N}"[..40] };
  ```
- `AssetWorkbookTestContext` tạo thêm **hai xã không ranh giới** cho mọi test workbook:
  `tests/LuxMap.Api.Tests/AssetWorkbookTestContext.cs:24-25`
  ```csharp
              var a = new AdministrativeUnit { Name = $"wb-multi-a-{Guid.NewGuid():N}" };
              var b = new AdministrativeUnit { Name = $"wb-multi-b-{Guid.NewGuid():N}" };
  ```
- `AssetSchemaFixture` (2500 cột) và `ScopeTestFixture` cũng tạo xã **không ranh giới**, nhưng chúng ghi bằng SQL/DbContext nên **không đi qua chốt** (`tests/LuxMap.Api.Tests/AssetSchemaFixture.cs:64`, `ScopeTestFixture.cs:132`).

### 4.2 Sẽ đỏ nếu chỉ thêm chốt mà không sửa fixture

**Tạo cột/tủ có toạ độ qua API (D2 → 400 vì xã chưa ranh giới):**

- `AssetUpdatedByTests.cs:33-38` — `POST /assets/poles` với `POINT(Lng Lat)`.
- `PoleNoteTests.cs:163-174` — `POST /assets/poles`, body có `geom_wkt`.
- `PoleWriteTests.cs:344,363` — `POST` cột với `POINT(106.49 10.97)`.
- `TopologyInferenceTests.cs:542-549` — helper `CreatePoleAsync` luôn gắn `geom_wkt`.
- `PoleWithoutLocationWriteTests.cs:25-59,90-92` — tạo cột **có** và **không** geom; nhánh có geom đỏ, nhánh `wkt: null` chỉ đỏ nếu D-1 chọn "chặn cả cột không toạ độ".
- `CabinetTests.cs:570-575` — `CreateCabinetAsync` gọi `POST /assets/cabinets` với `POINT(Lng Lat)`; nhiều test trong class dùng helper này.
- `AssetCoordinateRangeTests.cs:39-51` — cả nhánh `Create_refuses_...` (400 vì lý do khác) lẫn nhánh `Create_accepts_the_edges...` sẽ đổi hành vi; xem 4.4.

**Đổi vị trí (geom đổi → kiểm):**

- `PoleLocationTests.cs` — plant cột ở `107.72 / 11.72` rồi `PUT /location` sang vị trí mới (`:38,68,82,103-104,117,162,185,192,208,219`).
- `PoleWithoutLocationWriteTests.cs:192,196` — `PUT /location` ở `109.5005 / 13.5+`.
- `SegmentGeomNullableTests.cs:104-105` — `PUT /location` ở `110.31 / 14.31+` (cột plant bằng DbContext, nên chỉ lỗi ở bước dời).
- `WorkOrderPoleGpsTests.cs:211` (partial `WorkOrderTests`) — verify GPS ghi qua `MovePolesAsync`; toạ độ `108.002 / 16.002`.

**Import (cột/tủ có `geom_wkt`):** `AssetImportTests`, `AssetCoordinateRangeTests`, `AssetImportInitialStatusTests`, `AssetImportMockSetTests`, `AssetImportConcurrencyTests`, `AssetUpdatedByTests`, `PoleNoteTests` (nhánh `ImportPolesAsync`), `TopologyInferenceTests`, `CabinetTests`, `PoleWithoutLocationWriteTests`, `PoleWithoutSegmentWriteTests`, `SegmentWithoutGeometryWriteTests` — tất cả dùng `fixture.CommuneId` và import qua planner.

**Workbook:** mọi class kế thừa `AssetWorkbookTestContext` (`AssetImportWorkbookTests`, `AssetExportWorkbookTests`, …) — xã không ranh giới nên **mọi** lần upload sẽ hỏng, kể cả khi sheet không có toạ độ, nếu D-1 chọn "chặn theo xã".

### 4.3 KHÔNG đỏ (nhờ D4 hoặc nhờ ghi thẳng DB)

- `AssetReadShapeTests.cs:315-325` — `PUT` gửi lại **đúng** `geom_wkt` vừa đọc → D4 "không đổi" → bỏ qua kiểm.
- `AssetReplaceAndDeleteTests.cs:316-325` — cột plant `POINT(106.49 10.97)` (`:524-535`) và `PUT` đúng toạ độ đó → bỏ qua.
- `PoleNoteTests.ReplaceAsync` (`:277-290`) — lấy `geom_wkt` hiện tại rồi gửi lại → bỏ qua.
- `CommuneWriteScopeTests.AddPoleInScopeAsync` (`:276-283`) — `db.Set<Pole>().Add` trong `WriteAsSystemAsync` → không qua service.
- `CommuneWriteGuardCostTests`, `AssetPermissionTests` (phần plant), `MapEndpointTests`, `PoleDetailEndpointTests`, `TopologyInferenceTests` (phần plant), `AssetPhotoTests.cs:441-449` — ghi bằng DbContext/backdoor.

### 4.4 Cách sửa ít đụng nhất

1. Thêm helper vào fixture, gọi trong `InitializeAsync` cho **cả hai xã**:
   ```csharp
   async Task SetBoundaryAsync(string communeId, params (double MinLng, double MinLat, double MaxLng, double MaxLat)[] boxes)
   ```
   `Boundary` là `MultiPolygon`, nên một hình chữ nhật mỗi cụm toạ độ là hợp lệ và rẻ. Các cụm đang dùng: `106.49/10.97`, `107.72/11.72`, `108.002/16.002`, `109.5/13.5`, `110.31/14.31`; cộng thêm workbook/initial-status (`106.4901-106.4903 / 10.9700-10.9701`).
2. Làm y hệt trong `AssetWorkbookTestContext` cho `Commune` và `OtherCommune`.
3. **Bẫy collection fixture:** `AssetImportFixture` sống suốt collection. Test muốn chứng minh D2 ("xã chưa ranh giới → từ chối") phải **tự tạo xã riêng**, không được `UPDATE boundary = NULL` trên `fixture.CommuneId` (sẽ làm đỏ các class chạy sau trong cùng collection). Ghi bẫy này vào `docs/agents/traps/testing.md` ở phase 2.
4. `AssetCoordinateRangeTests.Create_accepts_the_edges_and_an_ordinary_position` (`:43-50`) đang khẳng định `POINT(180 90)` / `POINT(-180 -90)` tạo được. Không ranh giới xã thật nào chứa hai điểm này → **phải viết lại**: giữ `POINT(106.49 10.97)` ở test endpoint, chuyển hai điểm biên sang test đơn vị của `AssetGeometry.TryReadWkt` (hoặc một test khẳng định endpoint trả `OUTSIDE_COMMUNE_BOUNDARY` — đó chính là hành vi mới).
5. Không cần sửa test nào đang `PUT` lại đúng toạ độ — D4 tự miễn.

---

## 5. Dữ liệu: seeder / mock / `COM-070`

- Seeder chỉ tạo **một** xã nghiên cứu, không ranh giới:
  `src/LuxMap.Modules.Identity/Seeding/IdentitySeeder.cs:36`
  ```csharp
          var commune = await EnsureCommuneAsync(SeedKeys.StudySite, DefaultCommuneName, cancellationToken);
  ```
  `SeedKeys.CalibrationSite` **chỉ là hằng số**, không có chỗ nào seed nó (`rg` toàn `src/`, `scripts/` chỉ ra `AdministrativeUnit.cs:105`) → hiện không có xã testbed nào cần ranh giới; nếu sau này tạo, nó cũng phải có ranh giới.
- Nguồn polygon duy nhất trong repo là `scripts/data/commune-boundaries.geojson`, và script chỉ nạp **COM-001 / COM-002**:
  `scripts/load_commune_boundaries.py:9-20` (docstring: Long Phước relation 7342308, Long Bình relation 7342307).
  `COM-070` ("Commune 01", vùng mock Củ Chi) **không có** feature tương ứng và không có relation OSM trong repo.
- Mock poles nằm trong `mocks/mock-poles.geojson` quanh `106.49-106.504 / 10.966-10.976`; seeder chèn thẳng nên **vẫn ghi được** sau D2, nhưng sẽ tạo dữ liệu "ngoài ranh giới" (nếu sau này có ranh giới) hoặc dữ liệu ở xã "chưa có ranh giới".
- Hệ quả cần chốt (D-8):
  - Nếu chọn "mock phải tuân luật": thêm một polygon mock (chữ nhật/vài chữ nhật) cho `study_site`, nạp bằng script hoặc `--mock`, và để `seed_mock_set.py` gọi API/`PUT boundary` trước khi chèn.
  - Nếu chấp nhận "dữ liệu cũ/mock được miễn": ghi rõ trong drift; nhưng khi WP5 sửa một cột mock qua `PUT` **với toạ độ khác**, nó sẽ bị 400 cho tới khi xã có ranh giới. Sửa cùng toạ độ thì D4 cho qua — đây là lý do D4 phải so sánh toạ độ chứ không chỉ "có gửi `geom_wkt`".

---

## 6. Đồng thời

Hiện trạng:

- Import: một transaction + `pg_advisory_xact_lock` **theo loại tài sản** (`AssetImportService.cs:115-116`, `:135-147`). Không có khoá trên `administrative_unit`.
- Endpoint ranh giới: `FindAsync` (không khoá) rồi `SaveChanges` — mỗi lần lưu là transaction riêng (`CommuneAdminService.cs:94-103`), không khoá hàng.
- Dời cột: khoá `work_order` → `pole` (`WorkOrderService.cs:789-792`), hoặc `pole` (`AssetCrudService.cs:919`). Không khoá xã.
- Repo **chưa dùng `FOR SHARE` ở đâu** (`rg "FOR SHARE"` toàn `src/` = 0), chỉ `FOR UPDATE` / `FOR NO KEY UPDATE`.

Race: ở READ COMMITTED, ranh giới có thể bị đổi/xoá **sau** lúc đọc và **trước** lúc commit → ghi được cột nằm ngoài ranh giới mới, hoặc ghi vào xã vừa bị xoá ranh giới.

Đề xuất (D-5):

1. Trong import: sau advisory lock, lấy khoá chia sẻ trên đúng các xã sẽ ghi, **theo thứ tự id**:
   `SELECT 1 FROM administrative_unit WHERE commune_id = ANY({ids}) ORDER BY commune_id FOR SHARE`.
2. Trong `CommuneAdminService.UpdateBoundaryAsync`: `SELECT 1 FROM administrative_unit WHERE commune_id = {id} FOR UPDATE` trước khi gán `Boundary`.
3. Trong dời cột: lấy `FOR SHARE` trên xã **sau** các khoá `work_order`/`pole` hiện có (giữ thứ tự cố định, tránh vòng khoá). Vì endpoint ranh giới chỉ chạm `administrative_unit`, nó không tạo chu trình với `work_order → pole`.
4. Phương án nhẹ hơn: **chấp nhận** race và ghi vào drift "ranh giới đổi đồng thời có thể lọt một lần ghi"; rẻ hơn nhưng phải nói rõ.

---

## 7. Tài liệu / OpenAPI phải sửa ở phase 2 (chỉ liệt kê)

| Nơi | Sửa gì |
|---|---|
| `docs/contract-drift.md:1989-1998` (CBD-1) | Đổi "chỉ để vẽ" → ranh giới là chốt chặn khi ghi tài sản điểm; thêm entry mới (sau CBD-6) ghi D1–D4, hình dạng lỗi, phạm vi `pole`/`cabinet`, và "không đổi `PUT boundary`" |
| `docs/contract-drift.md` mục "Lược đồ" của COMMUNE-BOUNDARY | Câu "Không index (chỉ đọc ra để vẽ)" phải viết lại cho đúng: **vẫn không cần GIST** (xem D-9), nhưng lý do đổi |
| `docs/agents/traps/assets.md:419-433` | Bỏ "Ranh giới cũng chỉ để VẼ"; thêm luật kiểm + thứ tự với `POLE_OFF_SEGMENT`; dòng `:402` (COMMUNE-BBOX) giữ nguyên nhưng bỏ câu "ranh giới thật ở mục dưới" nếu ngữ nghĩa đổi |
| `docs/agents/traps/commune-scope.md` | Ghi rõ kiểm ranh giới **không** nằm trong `CommuneWriteGuard`/query filter; là lớp service |
| `docs/agents/traps/testing.md` | Bẫy collection fixture: xã dùng chung phải có ranh giới; test D2 phải tạo xã riêng |
| `docs/database/erd.md:40` (Q17) | "mới dùng để vẽ" → dùng để kiểm khi ghi; suy xã (phần còn lại của Q17) **vẫn chưa làm** |
| XML doc trong code | `AdministrativeUnit.cs:28-33,68-71`; `CommuneAdminService.cs:81-84`; `AdminCommunesController.cs:55-58` — bỏ "Drawing only / Neither decides anything" |
| `docs/openapi/tools/gen_consolidated_spec.py` | Cập nhật `SUMMARY`: dòng `:49` (COMMUNE-BOUNDARY), `:222-223` (import), `:232,238` (POST/PUT poles), `:244` (location), `:286-287` (cabinets), và dòng release-note đầu file. **Không** có schema viết tay cho `CreatePoleRequest`/`CreateCabinetRequest`/`Update*` (đã `rg`, 0 match) → chỉ sửa mô tả rồi regenerate + lint |
| `docs/api-contract-v1.1.md` | Chỉ cần nếu chốt **mã lỗi mới** hoặc `details.reason` là bề mặt Contract; nếu tái dùng `VALIDATION_FAILED` + `details` thì thêm một dòng mô tả và tăng version theo quy trình |
| `tracking.html` | Cập nhật sau khi hiện thực (AGENTS §6) |
| Thông báo consumer | WP5 (form tài sản + màn import), WP6 (nếu có ghi vị trí) — theo cột "Consumer" của drift |

---

## D-item còn mở (kèm khuyến nghị)

**D-1 — Cột không có toạ độ (`geom = null`) khi tạo thì sao?** (quan trọng nhất)
D2 nói "xã chưa ranh giới → từ chối ghi cột", nhưng `Pole.Geom` nullable có chủ đích (POLE-GEOM-NULL): cột "trong sổ" chưa có vị trí. Không có điểm thì không thể `Covers`.
- (a) Chỉ kiểm khi lần ghi **mang theo một điểm** (tạo cột có `geom_wkt`, `PUT` có WKT, `/location`, import có `geom`); cột `geom = null` được tạo, lần đặt vị trí đầu tiên mới kiểm.
- (b) Từ chối cả cột không toạ độ nếu xã chưa có ranh giới (đọc D2 theo nghĩa chặt).
- **Khuyến nghị (a)**: đúng bản chất "vị trí phải nằm trong xã"; (b) sẽ chặn toàn bộ luồng kiểm kê sổ sách và làm đỏ thêm nhánh `PoleWithoutLocationWriteTests` không có geom. Nếu chọn (a), phải ghi rõ trong drift: "D2 áp cho lần ghi có toạ độ".

**D-2 — Mã lỗi: tái dùng `VALIDATION_FAILED` + `details` hay mã mới?**
- (a) `400 VALIDATION_FAILED`, `details {field, reason}` — không đổi Contract/FE, đúng khuôn `geom_wkt` hiện có.
- (b) Mã mới `POLE_OUTSIDE_COMMUNE` / `COMMUNE_BOUNDARY_MISSING` — FE phân biệt dễ, nhưng phải vào Contract trước + `ErrorCodes.cs` + generator + test ma trận lỗi.
- **Khuyến nghị (a)**; giữ `reason` là chuỗi ổn định để FE nâng cấp sau mà không phá.

**D-3 — Dời cột: 400 hay 409, và kiểm trước hay sau `POLE_OFF_SEGMENT`?**
- Hiện `/location` trả `409 POLE_OFF_SEGMENT` khi ngoài hành lang; GPS verify cũng 409.
- (a) Kiểm hành lang **trước**, ranh giới **sau** → giữ nguyên 409 cho điểm vừa ngoài tuyến vừa ngoài xã; ranh giới trả 400.
- (b) Ranh giới trước → điểm ngoài xã luôn 400, kể cả khi cũng ngoài tuyến.
- **Khuyến nghị (a)**: ít đổi hành vi/test nhất, và "sai tuyến" là lỗi cụ thể hơn. Status: 400 `VALIDATION_FAILED` (không phải 409) vì đây là dữ liệu sai, không phải xung đột trạng thái.

**D-4 — Sync: `rejected[]` hay `conflicts[]`?**
- 400 → `rejected` (client sửa gửi lại được); 409 → `conflicts` + `ServerStateAsync` (server coi như trạng thái đã đổi).
- **Khuyến nghị 400/rejected**: toạ độ ngoài xã là dữ liệu sai, gửi lại y nguyên vẫn sai; `conflicts` dành cho "server đã khác".

**D-5 — Khoá hàng / race ranh giới.**
- (a) `FOR SHARE` xã trong import + dời cột, `FOR UPDATE` trong `PUT boundary` (mục 6).
- (b) Chấp nhận race, ghi drift.
- **Khuyến nghị (a)** cho import (một truy vấn, rẻ); dời cột có thể (b) nếu muốn tránh thêm khoá. `FOR SHARE` là khuôn mới trong repo → phải thêm trap + test khoá (dùng `FOR NO KEY UPDATE`/`FOR SHARE` đúng như `testing.md`).

**D-6 — Helper đặt ở đâu, SQL hay in-memory?**
- (a) `LuxMap.Persistence.CommuneBoundary` (thuần) + đọc `Boundary` rồi `Covers` in-memory cho cả CRUD và import (1 truy vấn, ba trạng thái).
- (b) LINQ `boundary.Covers(point)` cho CRUD (EF dịch `ST_Covers`, đã xác nhận) + in-memory cho import.
- **Khuyến nghị (b)**: nhanh nhất cho một dòng, nhưng **vẫn** có hàm thuần dùng chung để import/workbook không lệch luật. Không thêm `HasDbFunction` mới.

**D-7 — Chiến lược fixture + test biên 180/90.**
- Thêm `SetBoundaryAsync` vào `AssetImportFixture`/`AssetWorkbookTestContext`, đặt MultiPolygon gồm các chữ nhật quanh 5 cụm toạ độ test; test D2 tự tạo xã riêng (không sửa xã dùng chung); viết lại `AssetCoordinateRangeTests` nhánh 180/90.
- **Khuyến nghị**: làm đúng như trên, và thêm một test "xã không ranh giới → 400 reason `COMMUNE_BOUNDARY_MISSING`" + một test "điểm trên biên → được nhận" (`Covers`, không `Contains`).

**D-8 — Ranh giới cho `COM-070` / mock / testbed.**
- (a) Thêm polygon mock cho `study_site` (chữ nhật quanh `106.49-106.504 / 10.966-10.976`), nạp bằng `load_commune_boundaries.py` hoặc script seed trước khi chèn.
- (b) Để mock/seed được miễn, ghi rõ trong drift; chấp nhận cột mock không sửa được vị trí cho tới khi có ranh giới.
- **Khuyến nghị (a)**: nếu không, ngay sau khi triển khai, Quản lý mở form sửa cột mock sẽ nhận 400 mà không hiểu vì sao. `CalibrationSite` hiện không được seed nên chưa cấp bách, nhưng cùng luật.

**D-9 — Có cần GIST index trên `administrative_unit.boundary`?**
- Truy vấn của ticket luôn lọc **theo khoá chính** `commune_id` rồi mới `ST_Covers` trên **một** hàng (hoặc một tập id của file import) → GIST không giúp gì.
- **Khuyến nghị: KHÔNG thêm index/migration**; chỉ sửa comment `AdministrativeUnit.cs:134` ("never searched spatially") thành lý do đúng: "predicate chỉ chạy trên hàng đã lấy theo PK, không quét không gian".

**D-10 — D4 so sánh "đổi toạ độ" bằng gì?**
- `Measured.Unchanged` đã so `current.X == Target.X && current.Y == Target.Y` (`PoleRelocation.cs:78`). NTS `Point.Equals`/`EqualsExact` có thể khác nhau về SRID/thứ tự/`-0.0`.
- **Khuyến nghị**: dùng đúng khuôn X/Y như `Unchanged` cho cả `UpdatePoleAsync`/`UpdateCabinetAsync`; `NaN`/`Infinity` đã bị chặn ở tầng WKT/CHECK nên không cần xử lý thêm. Ghi rõ "đổi = X hoặc Y khác", không dùng `Equals`.

**D-11 — Nhánh "`commune_id` đổi" của D4 gần như không với tới được.**
- Không đường API nào sửa `commune_id` của cột/tủ (create đặt, update không có trường). Chỉ SQL thô/seed mới đổi.
- **Khuyến nghị**: vẫn viết kiểm phòng thủ (nếu `entity.CommuneId` khác giá trị gốc thì kiểm), nhưng ghi trong drift là "hiện chỉ create/raw SQL chạm tới"; đừng thiết kế UI/flow cho trường hợp này.

**D-12 — Thứ tự kiểm so với các 409 hiện có (ngoài D-3).**
- `CreatePoleAsync` đang trả `409 CROSS_COMMUNE_REFERENCE` khi feeder khác xã (`PoleWriteTests.cs:338-349`). Nếu kiểm ranh giới đặt **trước** kiểm feeder, test này đổi từ 409 sang 400.
- **Khuyến nghị**: đặt kiểm ranh giới **sau** `RequireFeederInCommuneAsync` ở mọi đường create/update; thêm một test khoá thứ tự (feeder sai + toạ độ ngoài → vẫn 409).
