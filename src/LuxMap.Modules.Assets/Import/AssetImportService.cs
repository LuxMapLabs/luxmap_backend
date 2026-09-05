using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Csv;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets.Import;

/// <summary>
/// Bulk asset import (BE-12a) — the CSV templates in <c>docs/templates/</c> and the GeoJSON shape of
/// the FO-26 mock set.
/// </summary>
/// <remarks>
/// <b>Validate the whole file, then write the valid rows in one transaction.</b> A row is rejected
/// only for something checkable up front — a missing cell, an enum outside Contract section 1, an
/// unresolvable reference, a commune outside scope. Anything that fails at write time is by
/// definition NOT one of those (a deadlock, a dropped connection), so it aborts the whole batch and
/// surfaces as 500 rather than being blamed on a row.
/// <para>
/// <b>One kind of file per request.</b> That is what makes references safe: a pole's segment was
/// committed by an earlier request, so it already has its database-generated <c>SEG-001</c>. Loading
/// all four kinds in a single transaction would mean resolving references to rows whose IDs do not
/// exist yet, and the load order in <c>docs/templates/README.md</c> would have to become code.
/// Ordering is instead enforced by the reference check itself: importing poles before segments fails
/// every row with a clear message naming the missing <c>segment_external_ref</c>.
/// </para>
/// </remarks>
public sealed class AssetImportService(LuxMapDbContext dbContext, ICommuneScopeAccessor scopeAccessor)
{
    public Task<ImportResult> ImportCsvAsync(ImportKind kind, string text, CancellationToken cancellationToken)
    {
        var document = CsvDocument.Parse(text);

        var missing = document.MissingColumns(RequiredColumns(kind));
        if (missing.Count > 0)
        {
            // A header problem is not N row problems: reporting it once, against line 1, is the whole
            // truth. Most often it is a file saved with the wrong delimiter or a stray BOM.
            return Task.FromResult(ImportResult.From(0, 0, document.Rows.Count,
                [new ImportRowError(1, string.Join(", ", missing), "Column missing from the header row.")]));
        }

        return ImportAsync(kind, [.. document.Rows.Select(row => new CsvImportRow(row))], cancellationToken);
    }

    /// <summary>
    /// Reads a GeoJSON <c>FeatureCollection</c>. Business fields come from <c>properties</c>, exactly
    /// as the FO-26 mock set is shaped.
    /// </summary>
    public async Task<ImportResult> ImportGeoJsonAsync(ImportKind kind, string json, CancellationToken cancellationToken)
    {
        // `async`, not a Task-returning method, and that is load-bearing. A JsonDocument owns pooled
        // memory that every JsonElement points into, so returning the inner Task from a non-async
        // method would dispose the document while the import was still reading rows out of it.
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return ImportResult.From(0, 0, 0,
                [new ImportRowError(0, "features", "Not a GeoJSON FeatureCollection.")]);
        }

        var rows = new List<IImportRow>();
        var index = 0;
        foreach (var feature in features.EnumerateArray())
        {
            index++;
            var properties = feature.TryGetProperty("properties", out var p) ? p : default;
            JsonElement? geometry = feature.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object
                ? g
                : null;

            rows.Add(new GeoJsonImportRow(index, properties, geometry));
        }

        return await ImportAsync(kind, rows, cancellationToken);
    }

    private static IReadOnlyList<string> RequiredColumns(ImportKind kind) => kind switch
    {
        ImportKind.Segments => ["external_ref", "segment_name", "road_class", "length_m", "geom_wkt", "commune_id", "data_source"],
        ImportKind.Feeders => ["external_ref", "feeder_name", "commune_id"],
        ImportKind.Poles => ["external_ref", "segment_external_ref", "commune_id", "geom_wkt", "data_source"],
        ImportKind.Fixtures => ["pole_external_ref", "fixture_type", "power_source", "lamp_watt", "install_date", "data_source"],
        _ => [],
    };

    private async Task<ImportResult> ImportAsync(
        ImportKind kind, IReadOnlyList<IImportRow> rows, CancellationToken cancellationToken)
    {
        var errors = new List<ImportRowError>();
        var readers = rows.Select(row => new ImportRowReader(row, errors)).ToList();

        var plan = kind switch
        {
            ImportKind.Segments => await PlanSegmentsAsync(readers, cancellationToken),
            ImportKind.Feeders => await PlanFeedersAsync(readers, cancellationToken),
            ImportKind.Poles => await PlanPolesAsync(readers, cancellationToken),
            ImportKind.Fixtures => await PlanFixturesAsync(readers, cancellationToken),
            _ => new WritePlan(0, 0),
        };

        var failed = readers.Count(reader => !reader.IsValid);

        if (plan.Inserted + plan.Updated == 0)
        {
            // Nothing tracked, so nothing to commit. Detach anything a planner attached before it
            // discovered the row was bad, so a later request in the same scope starts clean.
            dbContext.ChangeTracker.Clear();
            return ImportResult.From(0, 0, failed, errors);
        }

        // One transaction for the whole batch. `await using` rolls back on any escape — including the
        // 403 CommuneWriteGuard throws from inside SaveChanges — so the connection is never left
        // holding an open transaction.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ImportResult.From(plan.Inserted, plan.Updated, failed, errors);
    }

    private sealed record WritePlan(int Inserted, int Updated);

    private async Task<WritePlan> PlanSegmentsAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<RoadSegment>(readers, segment => segment.ExternalRef, cancellationToken);
        int inserted = 0, updated = 0;

        foreach (var reader in readers)
        {
            var externalRef = reader.Required("external_ref");
            var communeId = await CommuneAsync(reader, cancellationToken);
            var name = reader.Required("segment_name");
            var roadClass = reader.RequiredEnum<RoadClass>("road_class");
            var length = reader.RequiredInt("length_m");
            var dataSource = reader.RequiredEnum<DataSource>("data_source");
            var geometry = reader.Geometry<LineString>(CsvImportRow.GeometryColumn);

            if (!reader.IsValid)
            {
                continue;
            }

            if (existing.TryGetValue((communeId!, externalRef!), out var segment))
            {
                segment.SegmentName = name!;
                segment.RoadClass = roadClass;
                segment.LengthM = length;
                segment.Geom = geometry!;
                segment.DataSource = dataSource;
                segment.UpdatedAt = DateTime.UtcNow;
                updated++;
                continue;
            }

            var created = new RoadSegment
            {
                ExternalRef = externalRef,
                SegmentName = name!,
                RoadClass = roadClass,
                LengthM = length,
                Geom = geometry!,
                CommuneId = communeId!,
                DataSource = dataSource,
            };

            dbContext.Set<RoadSegment>().Add(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated);
    }

    private async Task<WritePlan> PlanFeedersAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<Feeder>(readers, feeder => feeder.ExternalRef, cancellationToken);
        int inserted = 0, updated = 0;

        foreach (var reader in readers)
        {
            var externalRef = reader.Required("external_ref");
            var communeId = await CommuneAsync(reader, cancellationToken);
            var name = reader.Required("feeder_name");

            // The only nullable geometry in the module: Branch C never surveyed the cable routes, so
            // an empty cell is the honest answer rather than an invented route.
            LineString? geometry = null;
            if (reader.Optional(CsvImportRow.GeometryColumn) is not null)
            {
                geometry = reader.Geometry<LineString>(CsvImportRow.GeometryColumn);
            }

            if (!reader.IsValid)
            {
                continue;
            }

            if (existing.TryGetValue((communeId!, externalRef!), out var feeder))
            {
                feeder.FeederName = name!;
                feeder.Geom = geometry;
                feeder.UpdatedAt = DateTime.UtcNow;
                updated++;
                continue;
            }

            var created = new Feeder
            {
                ExternalRef = externalRef,
                FeederName = name!,
                CommuneId = communeId!,
                Geom = geometry,
            };

            dbContext.Set<Feeder>().Add(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated);
    }

    private async Task<WritePlan> PlanPolesAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<Pole>(readers, pole => pole.ExternalRef, cancellationToken);
        var segments = await ReferenceIndexAsync<RoadSegment>(
            readers, "segment_external_ref", segment => segment.ExternalRef, cancellationToken);
        var feeders = await ReferenceIndexAsync<Feeder>(
            readers, "feeder_external_ref", feeder => feeder.ExternalRef, cancellationToken);

        int inserted = 0, updated = 0;

        foreach (var reader in readers)
        {
            var externalRef = reader.Required("external_ref");
            var communeId = await CommuneAsync(reader, cancellationToken);
            var dataSource = reader.RequiredEnum<DataSource>("data_source");
            var nearPoi = reader.Flag("near_sensitive_poi", fallback: false);
            var geometry = reader.Geometry<Point>(CsvImportRow.GeometryColumn);

            var segment = Resolve(reader, segments, "segment_external_ref", required: true);

            // Nullable on purpose: a solar_all_in_one pole is connected to no circuit at all.
            var feeder = Resolve(reader, feeders, "feeder_external_ref", required: false);

            if (!reader.IsValid)
            {
                continue;
            }

            if (existing.TryGetValue((communeId!, externalRef!), out var pole))
            {
                pole.SegmentId = segment!;
                pole.FeederId = feeder;
                pole.Geom = geometry!;
                pole.NearSensitivePoi = nearPoi;
                pole.DataSource = dataSource;
                pole.UpdatedAt = DateTime.UtcNow;
                updated++;
                continue;
            }

            var created = new Pole
            {
                ExternalRef = externalRef,
                SegmentId = segment!,
                FeederId = feeder,
                CommuneId = communeId!,
                Geom = geometry!,
                NearSensitivePoi = nearPoi,
                DataSource = dataSource,
            };

            dbContext.Set<Pole>().Add(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated);
    }

    /// <summary>
    /// Fixtures are INSERT-ONLY — see <see cref="Fixture"/> and the note on
    /// <c>ix_fixture_pole_id_active</c>.
    /// </summary>
    /// <remarks>
    /// There is no natural key to upsert on. A pole carries several lamps over its life and the
    /// history is the point, so nothing in the file identifies a particular installation. Re-running
    /// a fixtures file therefore reports one error per row rather than silently doubling the
    /// equipment history. Replacing a lamp goes through CRUD, which can say which row it means.
    /// </remarks>
    private async Task<WritePlan> PlanFixturesAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var poles = await ReferenceIndexAsync<Pole>(
            readers, "pole_external_ref", pole => pole.ExternalRef, cancellationToken);

        var occupied = await OccupiedPolesAsync(poles.Values.SelectMany(ids => ids).ToArray(), cancellationToken);
        var inserted = 0;

        foreach (var reader in readers)
        {
            var poleId = Resolve(reader, poles, "pole_external_ref", required: true);
            var fixtureType = reader.RequiredEnum<FixtureType>("fixture_type");
            var powerSource = reader.RequiredEnum<PowerSource>("power_source");
            var watt = reader.RequiredInt("lamp_watt");
            var installDate = reader.Date("install_date", required: true);
            var removedDate = reader.Date("removed_date", required: false);
            var warranty = reader.Date("warranty_expiry", required: false);
            var dataSource = reader.RequiredEnum<DataSource>("data_source");

            if (poleId is not null && !occupied.Add(poleId))
            {
                reader.Fail(
                    "pole_external_ref",
                    "That pole already carries a fixture. Import creates equipment records, it never "
                    + "replaces them — use the fixtures endpoint to record a lamp change.");
            }

            if (!reader.IsValid)
            {
                continue;
            }

            // Copied from the pole, never read from the file. A fixture is always in the commune of
            // the pole carrying it, and letting the file say otherwise would let the two drift with
            // nothing to detect it.
            var commune = await dbContext.Set<Pole>().AsNoTracking()
                .Where(pole => pole.PoleId == poleId)
                .Select(pole => pole.CommuneId)
                .SingleAsync(cancellationToken);

            dbContext.Set<Fixture>().Add(new Fixture
            {
                PoleId = poleId!,
                CommuneId = commune,
                FixtureType = fixtureType,
                PowerSource = powerSource,
                LampWatt = watt,
                InstallDate = installDate!.Value,
                RemovedDate = removedDate,
                WarrantyExpiry = warranty,
                DataSource = dataSource,
            });

            inserted++;
        }

        return new WritePlan(inserted, 0);
    }

    /// <summary>
    /// The commune named by the row, checked to EXIST and to be inside the caller's scope.
    /// </summary>
    /// <remarks>
    /// Out of scope is a ROW error, not a 403 for the whole request: the file is a batch, and one bad
    /// line should not discard the other 499. <c>CommuneWriteGuard</c> still stands behind this as the
    /// backstop that cannot be forgotten — this check exists to produce a message a person can act on.
    /// </remarks>
    private async Task<string?> CommuneAsync(ImportRowReader reader, CancellationToken cancellationToken)
    {
        var communeId = reader.Required("commune_id");
        if (communeId is null)
        {
            return null;
        }

        if (!scopeAccessor.Scope.Allows(communeId))
        {
            reader.Fail("commune_id", $"'{communeId}' is outside your permitted commune scope.");
            return null;
        }

        var exists = await dbContext.Set<AdministrativeUnit>().AsNoTracking()
            .AnyAsync(unit => unit.CommuneId == communeId, cancellationToken);

        if (!exists)
        {
            reader.Fail("commune_id", $"'{communeId}' is not a known commune.");
            return null;
        }

        return communeId;
    }

    /// <summary>Rows already in the database for the <c>external_ref</c> values this file mentions.</summary>
    private async Task<Dictionary<(string Commune, string Ref), TEntity>> ExistingByRefAsync<TEntity>(
        List<ImportRowReader> readers,
        Func<TEntity, string?> externalRef,
        CancellationToken cancellationToken)
        where TEntity : class, ICommuneScoped, IExternallyReferenced
    {
        var wanted = readers.Select(reader => reader.Row["external_ref"])
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return new Dictionary<(string, string), TEntity>();
        }

        // Tracked, NOT AsNoTracking: an update has to go through the change tracker, because that is
        // the only thing CommuneWriteGuard can see. ExecuteUpdate would skip both.
        var rows = await dbContext.Set<TEntity>()
            .Where(entity => entity.ExternalRef != null && wanted.Contains(entity.ExternalRef))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => (row.CommuneId, externalRef(row)!), StringTupleComparer.Instance);
    }

    /// <summary>
    /// Maps each referenced <c>external_ref</c> to the ids that carry it, across everything the caller
    /// can see.
    /// </summary>
    /// <remarks>
    /// A list rather than a single id because <c>external_ref</c> is unique per COMMUNE, not globally.
    /// A caller scoped to several communes can legitimately see two segments coded <c>TUYEN-A</c>, and
    /// guessing between them would attach poles to the wrong road; <see cref="Resolve"/> reports the
    /// ambiguity instead.
    /// </remarks>
    private async Task<Dictionary<string, List<string>>> ReferenceIndexAsync<TEntity>(
        List<ImportRowReader> readers,
        string column,
        Func<TEntity, string?> externalRef,
        CancellationToken cancellationToken)
        where TEntity : class, IExternallyReferenced
    {
        var wanted = readers.Select(reader => reader.Row[column])
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return [];
        }

        var rows = await dbContext.Set<TEntity>().AsNoTracking()
            .Where(entity => entity.ExternalRef != null && wanted.Contains(entity.ExternalRef))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => externalRef(row)!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(KeyOf).ToList(),
                StringComparer.Ordinal);
    }

    private static string KeyOf<TEntity>(TEntity entity) => entity switch
    {
        RoadSegment segment => segment.SegmentId,
        Feeder feeder => feeder.FeederId,
        Pole pole => pole.PoleId,
        _ => throw new NotSupportedException($"{typeof(TEntity).Name} is not referenced by external_ref."),
    };

    private static string? Resolve(
        ImportRowReader reader, Dictionary<string, List<string>> index, string column, bool required)
    {
        var value = required ? reader.Required(column) : reader.Optional(column);
        if (value is null)
        {
            return null;
        }

        if (!index.TryGetValue(value, out var matches) || matches.Count == 0)
        {
            reader.Fail(column, $"'{value}' matches nothing. Import that file first — see docs/templates/README.md.");
            return null;
        }

        if (matches.Count > 1)
        {
            reader.Fail(column, $"'{value}' exists in more than one commune you can see; it does not identify one row.");
            return null;
        }

        return matches[0];
    }

    private async Task<HashSet<string>> OccupiedPolesAsync(string[] poleIds, CancellationToken cancellationToken)
    {
        if (poleIds.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var occupied = await dbContext.Set<Fixture>().AsNoTracking()
            .Where(fixture => poleIds.Contains(fixture.PoleId))
            .Select(fixture => fixture.PoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new HashSet<string>(occupied, StringComparer.Ordinal);
    }

    /// <summary>Ordinal comparison for the composite key, so casing is never quietly folded.</summary>
    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) left, (string, string) right)
            => string.Equals(left.Item1, right.Item1, StringComparison.Ordinal)
                && string.Equals(left.Item2, right.Item2, StringComparison.Ordinal);

        public int GetHashCode((string, string) value)
            => HashCode.Combine(value.Item1, value.Item2);
    }
}
