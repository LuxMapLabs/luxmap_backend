using System.Text.Json;
using LuxMap.Modules.Assets.Crud;
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
/// unresolvable reference, a commune outside scope. A write-time failure aborts the whole batch and
/// surfaces as 500 rather than being blamed on a row.
/// <para>
/// <b>Concurrent imports are queued by file kind.</b> The transaction takes a PostgreSQL advisory
/// transaction lock BEFORE planning, so the next import of the same kind reads the previous one's
/// committed rows. Read and write remain separate statements, but imports of that kind run serially.
/// Raw <c>ON CONFLICT</c> would bypass the change tracker, <c>CommuneWriteGuard</c> and
/// <c>AssetStamp.Touch</c>; all row writes still go through <c>SaveChanges</c>. Commit, rollback and
/// disposal all release the lock, including an unchanged or invalid batch with nothing to write.
/// Other asset write paths do not take this import lock.
/// </para>
/// <para>
/// <b>One kind of file per request.</b> That is what makes references safe: a pole's segment was
/// committed by an earlier request, so it already has its database-generated <c>SEG-001</c>. Loading
/// all four kinds in a single transaction would mean resolving references to rows whose IDs do not
/// exist yet, and the load order in <c>docs/templates/README.md</c> would have to become code.
/// Ordering is instead enforced by the reference check itself: importing poles before segments fails
/// every row that FILLS <c>segment_external_ref</c> with a clear message naming it (a blank cell is a pole on no
/// segment yet — POLE-SEGMENT-NULL D-2).
/// </para>
/// </remarks>
public sealed partial class AssetImportService(
    LuxMapDbContext dbContext, ICommuneScopeAccessor scopeAccessor, ICurrentActorAccessor actor, ICabinetDeviceLookup devices)
{
    public Task<ImportResult> ImportCsvAsync(ImportKind kind, string text, CancellationToken cancellationToken)
    {
        var document = CsvDocument.Parse(text);

        var missing = document.MissingColumns(RequiredColumns(kind));
        if (missing.Count > 0)
        {
            // A header problem is not N row problems: reporting it once, against line 1, is the whole
            // truth. Most often it is a file saved with the wrong delimiter or a stray BOM.
            return Task.FromResult(ImportResult.From(0, 0, 0, document.Rows.Count,
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
            return ImportResult.From(0, 0, 0, 0,
                [new ImportRowError(0, "features", "Not a GeoJSON FeatureCollection.")]);
        }

        var rows = new List<IImportRow>();
        var index = 0;
        foreach (var feature in features.EnumerateArray())
        {
            index++;
            var properties = feature.TryGetProperty("properties", out var p) ? p : default;
            // Undefined when the key is absent; the row decides what a non-object value means.
            var geometry = feature.TryGetProperty("geometry", out var g) ? g : default;

            rows.Add(new GeoJsonImportRow(index, properties, geometry));
        }

        return await ImportAsync(kind, rows, cancellationToken);
    }

    private static IReadOnlyList<string> RequiredColumns(ImportKind kind) => kind switch
    {
        // geom_wkt is optional for segments since ROAD-SEGMENT-GEOM-NULL: a route known only from the books has no line yet.
        // length_m too (ASSET-SEGMENT-LENGTH-NULL): it is only required of a row that has a route, which the row check below decides.
        ImportKind.Segments => ["external_ref", "segment_name", "road_class", "commune_id", "data_source"],
        ImportKind.Cabinets => ["external_ref", "cabinet_name", "commune_id", "geom_wkt", "data_source"],
        ImportKind.Feeders => ["external_ref", "feeder_name", "commune_id"],
        // geom_wkt is optional for poles since POLE-GEOM-NULL: a pole known only from the books has no position yet.
        ImportKind.Poles => ["external_ref", "commune_id", "data_source"],
        ImportKind.Fixtures => ["pole_external_ref", "fixture_type", "power_source", "lamp_watt", "install_date", "data_source"],
        _ => [],
    };

    private async Task<ImportResult> ImportAsync(
        ImportKind kind, IReadOnlyList<IImportRow> rows, CancellationToken cancellationToken)
    {
        var errors = new List<ImportRowError>();
        var warnings = new List<ImportRowError>();
        var readers = rows.Select(row => new ImportRowReader(row, errors)).ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockKindAsync(kind, cancellationToken);
        var plan = await PlanAsync(kind, readers, warnings, cancellationToken);

        var failed = readers.Count(reader => !reader.IsValid);

        if (plan.Inserted + plan.Updated == 0)
        {
            // No row changes to commit. Disposal rolls back and releases the lock. Detach anything a planner
            // attached before it discovered the row was bad, so a later request in the same scope starts clean.
            dbContext.ChangeTracker.Clear();
            return ImportResult.From(0, 0, plan.Unchanged, failed, errors, warnings);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ImportResult.From(plan.Inserted, plan.Updated, plan.Unchanged, failed, errors, warnings);
    }

    private Task<int> LockKindAsync(ImportKind kind, CancellationToken cancellationToken)
    {
        // Stable keys shared by CSV, GeoJSON and workbook; LoadOrder is ascending lock order.
        var lockKey = kind switch
        {
            ImportKind.Segments => 0x4C4D4901L,
            ImportKind.Cabinets => 0x4C4D4902L,
            ImportKind.Feeders => 0x4C4D4903L,
            ImportKind.Poles => 0x4C4D4904L,
            ImportKind.Fixtures => 0x4C4D4905L,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
    }

    private Task<WritePlan> PlanAsync(ImportKind kind, List<ImportRowReader> readers,
        List<ImportRowError> warnings, CancellationToken cancellationToken, bool matchFixtures = false)
        => kind switch
        {
            ImportKind.Segments => PlanSegmentsAsync(readers, cancellationToken),
            ImportKind.Cabinets => PlanCabinetsAsync(readers, cancellationToken),
            ImportKind.Feeders => PlanFeedersAsync(readers, cancellationToken),
            ImportKind.Poles => PlanPolesAsync(readers, warnings, cancellationToken),
            ImportKind.Fixtures => PlanFixturesAsync(readers, cancellationToken, matchFixtures),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private sealed record WritePlan(int Inserted, int Updated, int Unchanged);

    private async Task<WritePlan> PlanSegmentsAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<RoadSegment>(readers, segment => segment.ExternalRef, cancellationToken);
        int inserted = 0, updated = 0, unchanged = 0;

        foreach (var reader in readers)
        {
            var externalRef = reader.Required("external_ref");
            var communeId = await CommuneAsync(reader, cancellationToken);
            var name = reader.Required("segment_name");
            var roadClass = reader.RequiredEnum<RoadClass>("road_class");
            // Empty = unknown (ASSET-SEGMENT-LENGTH-NULL); a present one must still be a whole number ≥ 1.
            var length = reader.OptionalInt("length_m", 1);
            var dataSource = reader.RequiredEnum<DataSource>("data_source");
            // Empty = no route yet (RG-2); a present but malformed one still fails the row.
            var geometry = reader.OptionalGeometry<LineString>(CsvImportRow.GeometryColumn);

            existing.TryGetValue((communeId!, externalRef!), out var segment);

            // A length is measured off a route, so a row that leaves the segment WITH one must carry a length — its own, or the one the
            // segment already has (an empty cell keeps what is stored, exactly as an empty geom_wkt cell keeps the route).
            var drawn = geometry is not null || segment?.Geom is not null;
            if (reader.IsValid && drawn && length is null && segment?.LengthM is null)
            {
                reader.Fail("length_m", "Required when the segment has a route (geom_wkt), but the cell is empty.");
            }

            if (!reader.IsValid)
            {
                continue;
            }

            if (segment is not null)
            {
                segment.SegmentName = name!;
                segment.RoadClass = roadClass;
                // An empty cell KEEPS the length a segment already has, like the route below.
                if (length is not null) segment.LengthM = length;
                // An empty cell KEEPS the route a segment already has: a re-import of a sheet without lines must not wipe what was drawn.
                if (geometry is not null) segment.Geom = geometry;
                segment.DataSource = dataSource;
                Count(segment, ref updated, ref unchanged);
                continue;
            }

            var created = new RoadSegment
            {
                ExternalRef = externalRef,
                SegmentName = name!,
                RoadClass = roadClass,
                LengthM = length,
                Geom = geometry,
                CommuneId = communeId!,
                DataSource = dataSource,
            };

            dbContext.Set<RoadSegment>().Add(created);
            Touch(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated, unchanged);
    }

    /// <summary>Cabinets (CAB-7) — upserted on <c>(commune_id, external_ref)</c> like segments.</summary>
    private async Task<WritePlan> PlanCabinetsAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<ElectricalCabinet>(readers, cabinet => cabinet.ExternalRef, cancellationToken);
        int inserted = 0, updated = 0, unchanged = 0;

        foreach (var reader in readers)
        {
            var externalRef = reader.Required("external_ref");
            var communeId = await CommuneAsync(reader, cancellationToken);
            var name = reader.Required("cabinet_name");
            var dataSource = reader.RequiredEnum<DataSource>("data_source");
            var geometry = reader.Geometry<Point>(CsvImportRow.GeometryColumn);

            ElectricalCabinet? cabinet = null;
            if (communeId is not null && externalRef is not null)
            {
                existing.TryGetValue((communeId, externalRef), out cabinet);
            }

            if (!reader.IsValid)
            {
                continue;
            }

            if (cabinet is not null)
            {
                cabinet.CabinetName = name!;
                cabinet.Geom = geometry!;
                cabinet.DataSource = dataSource;
                Count(cabinet, ref updated, ref unchanged);
                continue;
            }

            var created = new ElectricalCabinet
            {
                ExternalRef = externalRef,
                CabinetName = name!,
                CommuneId = communeId!,
                Geom = geometry!,
                DataSource = dataSource,
            };

            dbContext.Set<ElectricalCabinet>().Add(created);
            Touch(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated, unchanged);
    }

    /// <remarks>
    /// <c>cabinet_external_ref</c> is optional, and a blank cell or a missing column KEEPS the feeder's cabinet —
    /// the import never detaches one, the same rule as a pole's note and as <c>PUT</c> (CAB-6). Moving a feeder a
    /// device switches is a row error (CAB-4).
    /// <para>
    /// ⚠️ <b>The import lock does not serialize relay wiring:</b> the relay lookup is inside the transaction,
    /// but another writer can wire a relay between that read and the write. The database then refuses the write —
    /// the batch rolls back whole and answers 500, nothing is corrupted. Accepted: the other writer is a Manager wiring a
    /// relay (<c>PUT /assets/iot-nodes/{id}/relays/{n}</c>, LIGHT-CTRL 2a), as rare as an import (Codex review 07/10/2026,
    /// P2); coordinating those writers requires row locks, not another pre-check.
    /// </para>
    /// </remarks>
    private async Task<WritePlan> PlanFeedersAsync(List<ImportRowReader> readers, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<Feeder>(readers, feeder => feeder.ExternalRef, cancellationToken);
        var cabinets = await ReferenceIndexAsync<ElectricalCabinet>(readers, "cabinet_external_ref", cancellationToken);
        var switched = await devices.ControllersAsync([.. existing.Values.Select(feeder => feeder.FeederId)], cancellationToken);
        int inserted = 0, updated = 0, unchanged = 0;

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

            var cabinet = Resolve(reader, cabinets, "cabinet_external_ref", required: false);

            // In the feeder's own commune — the composite key would otherwise fail the whole batch at the write.
            if (cabinet is not null && communeId is not null
                && !string.Equals(cabinet.CommuneId, communeId, StringComparison.Ordinal))
            {
                reader.Fail(
                    "cabinet_external_ref",
                    $"'{reader.Optional("cabinet_external_ref")}' belongs to commune '{cabinet.CommuneId}', not to the feeder's commune '{communeId}'.");
            }

            Feeder? feeder = null;
            if (communeId is not null && externalRef is not null)
            {
                existing.TryGetValue((communeId, externalRef), out feeder);
            }

            if (feeder is not null && cabinet is not null
                && !string.Equals(cabinet.Id, feeder.CabinetId, StringComparison.Ordinal)
                && switched.TryGetValue(feeder.FeederId, out var nodeId))
            {
                reader.Fail(
                    "cabinet_external_ref",
                    $"Device '{nodeId}' in the feeder's cabinet switches it, so the feeder cannot move to another cabinet.");
            }

            // TOPO-INFER D-10: the label follows TopologyLink's rule; a blank cabinet cell keeps the cabinet (CAB-6).
            var newCabinetId = cabinet?.Id ?? feeder?.CabinetId;
            var (cabinetSource, labelError) = TopologyLink.Resolve(
                feeder?.CabinetId, feeder?.CabinetSource, newCabinetId, reader.Label("cabinet_source"), "cabinet_source", "cabinet_id");
            if (labelError is not null && !reader.ReferenceSuppressed)
            {
                reader.Fail("cabinet_source", labelError);
            }

            if (!reader.IsValid)
            {
                continue;
            }

            if (feeder is not null)
            {
                feeder.FeederName = name!;
                if (!reader.Row.KeepEmptyGeometry || !reader.Row.GeometryIsEmpty)
                {
                    feeder.Geom = geometry;
                }

                TopologyLink.ApplyFeederCabinet(dbContext, feeder, newCabinetId, cabinetSource);
                Count(feeder, ref updated, ref unchanged);
                continue;
            }

            var created = new Feeder
            {
                ExternalRef = externalRef,
                FeederName = name!,
                CommuneId = communeId!,
                Geom = geometry,
                CabinetId = newCabinetId,
                CabinetSource = cabinetSource,
            };

            dbContext.Set<Feeder>().Add(created);
            Touch(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated, unchanged);
    }

    private async Task<WritePlan> PlanPolesAsync(
        List<ImportRowReader> readers, List<ImportRowError> warnings, CancellationToken cancellationToken)
    {
        var existing = await ExistingByRefAsync<Pole>(readers, pole => pole.ExternalRef, cancellationToken);
        var segments = await ReferenceIndexAsync<RoadSegment>(readers, "segment_external_ref", cancellationToken);
        var feeders = await ReferenceIndexAsync<Feeder>(readers, "feeder_external_ref", cancellationToken);

        int inserted = 0, updated = 0, unchanged = 0;

        foreach (var reader in readers)
        {
            var externalRef = reader.Required("external_ref");
            var communeId = await CommuneAsync(reader, cancellationToken);
            var dataSource = reader.RequiredEnum<DataSource>("data_source");
            var nearPoi = reader.Flag("near_sensitive_poi", fallback: false);

            // POLE-GEOM-NULL GN-2: a blank or missing cell leaves a NEW pole without a position and KEEPS an existing pole's one (a
            // pole never goes back to "no position"); a malformed value is still a row error — never read as "empty".
            var geometry = reader.OptionalGeometry<Point>(CsvImportRow.GeometryColumn);

            var note = OptionalNote(reader);

            // POLE-SEGMENT-NULL D-2: the cell is optional. Blank or a missing column leaves a NEW pole on no segment and KEEPS an
            // existing pole's segment (only PUT null takes a pole off its segment). A reference that matches nothing is still a row
            // error — Resolve reports it — so a typo can never turn into "no segment".
            var segment = Resolve(reader, segments, "segment_external_ref", required: false);

            // Nullable on purpose: a solar_all_in_one pole is connected to no circuit at all.
            var feeder = Resolve(reader, feeders, "feeder_external_ref", required: false);

            // The feeder must sit in the pole's own commune (BE-REVIEW-02, F-01). CommuneWriteGuard
            // cannot see this — it checks the commune of the row being written, which is in scope —
            // and the CRUD paths already refuse it through RequireFeederInCommuneAsync. This is the
            // third write path, and it had the hole open. The SEGMENT is deliberately NOT checked:
            // road_class = inter_commune means a road running BETWEEN communes, so a pole in another
            // commune than its segment's owner is legitimate (docs/agents/traps/assets.md, BE-12a rule 6).
            if (feeder is not null && communeId is not null
                && !string.Equals(feeder.CommuneId, communeId, StringComparison.Ordinal))
            {
                reader.Fail(
                    "feeder_external_ref",
                    $"'{reader.Optional("feeder_external_ref")}' belongs to commune '{feeder.CommuneId}', not to the pole's commune '{communeId}'.");
            }

            Pole? pole = null;
            if (communeId is not null && externalRef is not null)
            {
                existing.TryGetValue((communeId, externalRef), out pole);
            }

            if (pole is null && reader.Row.RequireLocationNote && reader.Row.GeometryIsEmpty && note is null)
                reader.Fail("note", "A pole without geom_wkt needs a note describing where it is");

            // TOPO-INFER TI-2: the label follows TopologyLink's rule. An empty feeder cell still clears the circuit here
            // (full replacement, as before), and with it the label.
            var (feederSource, labelError) = TopologyLink.Resolve(
                pole?.FeederId, pole?.FeederSource, feeder?.Id, reader.Label("feeder_source"), "feeder_source", "feeder_id");
            if (labelError is not null && !reader.ReferenceSuppressed)
            {
                reader.Fail("feeder_source", labelError);
            }

            if (!reader.IsValid)
            {
                continue;
            }

            if (pole is not null)
            {
                if (segment is not null)
                {
                    pole.SegmentId = segment.Id;
                }

                TopologyLink.ApplyPoleFeeder(dbContext, pole, feeder?.Id, feederSource);
                if (geometry is not null)
                {
                    pole.Geom = geometry;
                }

                pole.NearSensitivePoi = nearPoi;
                pole.DataSource = dataSource;
                ImportNote(reader, pole, note, warnings);
                Count(pole, ref updated, ref unchanged);
                continue;
            }

            var created = new Pole
            {
                ExternalRef = externalRef,
                SegmentId = segment?.Id,
                FeederId = feeder?.Id,
                FeederSource = feederSource,
                CommuneId = communeId!,
                Geom = geometry,
                NearSensitivePoi = nearPoi,
                DataSource = dataSource,
                Note = note,
            };

            dbContext.Set<Pole>().Add(created);
            Touch(created);
            existing[(communeId!, externalRef!)] = created;
            inserted++;
        }

        return new WritePlan(inserted, updated, unchanged);
    }

    /// <summary>
    /// A note in the file overwrites the pole's note; a blank cell or a missing column keeps it (drift POLE-NOTE N-4).
    /// </summary>
    /// <remarks>
    /// Replacing a DIFFERENT note is reported as a warning that carries the old text: there is no note
    /// history, and a stale inventory file re-imported over an engineer's edit is otherwise lost without a
    /// trace. The import never clears a note — that is a decision for the pole itself, not a gap in a file.
    /// </remarks>
    /// <summary>
    /// The optional <c>note</c> column of a poles or fixtures file: never required in the header, already trimmed,
    /// blank = no note, longer than <see cref="PoleNoteInput.MaxLength"/> fails the row.
    /// </summary>
    private static string? OptionalNote(ImportRowReader reader)
    {
        var note = reader.Optional("note");
        if (note is { Length: > PoleNoteInput.MaxLength })
        {
            reader.Fail("note", $"A note is at most {PoleNoteInput.MaxLength} characters; this one has {note.Length}.");
        }

        return note;
    }

    private static void ImportNote(ImportRowReader reader, Pole pole, string? note, List<ImportRowError> warnings)
    {
        if (note is null || note == pole.Note)
        {
            return;
        }

        if (pole.Note is not null)
        {
            warnings.Add(new ImportRowError(reader.Row.Row, "note", $"Replaced the pole's previous note: \"{pole.Note}\""));
        }

        pole.Note = note;
    }

    /// <summary>An existing row the file matched: <c>updated</c> only if a value really changed.</summary>
    private void Count(IUpdateStamped asset, ref int updated, ref int unchanged)
    {
        if (Touch(asset))
        {
            updated++;
        }
        else
        {
            unchanged++;
        }
    }

    private bool Touch(IUpdateStamped asset) => AssetStamp.Touch(dbContext, asset, actor);

    /// <summary>
    /// CSV / GeoJSON fixtures are insert-only; workbook replay matches immutable installations by (pole, install_date).
    /// </summary>
    /// <remarks>
    /// A pole carries several lamps over its life and the history is the point. Re-running
    /// a CSV / GeoJSON fixtures file reports one error per ACTIVE row beyond a pole's one fitting rather than silently
    /// doubling the equipment history (ONE-FIXTURE: one fitting in service per pole). Replacing a fitting goes
    /// through CRUD, which can say which row it means.
    /// </remarks>
    private async Task<WritePlan> PlanFixturesAsync(List<ImportRowReader> readers, CancellationToken cancellationToken, bool matchExisting = false)
    {
        var poles = await ReferenceIndexAsync<Pole>(readers, "pole_external_ref", cancellationToken);

        var poleIds = poles.Values.SelectMany(assets => assets.Select(asset => asset.Id)).ToArray();
        var slotsInUse = await SlotsInUseAsync(poleIds, cancellationToken);
        var fittings = matchExisting
            ? (await dbContext.Set<Fixture>().AsNoTracking().Where(f => poleIds.Contains(f.PoleId)).ToListAsync(cancellationToken))
                .GroupBy(f => (f.PoleId, f.InstallDate)).ToDictionary(g => g.Key, g => g.ToList())
            : new Dictionary<(string, DateOnly), List<Fixture>>();
        int inserted = 0, unchanged = 0;

        foreach (var reader in readers)
        {
            var pole = Resolve(reader, poles, "pole_external_ref", required: true);
            var poleId = pole?.Id;
            var fixtureType = reader.RequiredEnum<FixtureType>("fixture_type");
            var powerSource = reader.RequiredEnum<PowerSource>("power_source");
            var watt = reader.RequiredInt("lamp_watt");
            var lampCount = reader.OptionalInt("lamp_count", 1, 1, 4);
            var installDate = reader.Date("install_date", required: true);
            var removedDate = reader.Date("removed_date", required: false);
            var warranty = reader.Date("warranty_expiry", required: false);
            var dataSource = reader.RequiredEnum<DataSource>("data_source");
            var note = OptionalNote(reader);

            // One fitting in service per pole (ONE-FIXTURE). A row with removed_date is history and neither takes
            // a slot nor is refused by a fitting already there; only an ACTIVE row can be refused, whether the
            // fitting in service is in the database or earlier in this file.
            if (installDate is not null && removedDate is not null && removedDate < installDate)
            {
                reader.Fail("removed_date", $"'{removedDate:yyyy-MM-dd}' is before install_date '{installDate:yyyy-MM-dd}'.");
            }

            if (matchExisting && poleId is not null && installDate is not null
                && fittings.TryGetValue((poleId, installDate.Value), out var matches))
            {
                if (matches.Count > 1)
                {
                    reader.Fail("install_date", "The fitting match (pole, install_date) is ambiguous.");
                    continue;
                }
                var current = matches[0];
                void Match(string column, bool equal)
                {
                    if (!equal)
                        reader.Fail(column, $"Different {column}; change a fitting through the asset form");
                }
                Match("fixture_type", current.FixtureType == fixtureType);
                Match("power_source", current.PowerSource == powerSource);
                Match("lamp_watt", current.LampWatt == watt);
                Match("lamp_count", current.LampCount == lampCount);
                Match("removed_date", current.RemovedDate == removedDate);
                if (reader.Optional("warranty_expiry") is not null) Match("warranty_expiry", current.WarrantyExpiry == warranty);
                if (note is not null) Match("note", current.Note == note);
                if (reader.IsValid) unchanged++;
                continue;
            }

            short slot = FixtureSlots.First;
            if (poleId is not null && removedDate is null)
            {
                if (!slotsInUse.TryGetValue(poleId, out var taken))
                {
                    slotsInUse[poleId] = taken = [];
                }

                if (FixtureSlots.FirstFree(taken) is { } free)
                {
                    slot = free;
                    taken.Add(free);
                }
                else
                {
                    reader.Fail(
                        "pole_external_ref",
                        "That pole already has a fitting in service. Import creates equipment records, it never "
                        + "replaces them — retire the fitting (PUT /assets/fixtures/{id}/removal) before recording "
                        + "its replacement.");
                }
            }

            if (!reader.IsValid)
            {
                continue;
            }

            // Copied from the pole, never read from the file. A fixture is always in the commune of
            // the pole carrying it, and letting the file say otherwise would let the two drift with
            // nothing to detect it. The reference index already carries the commune (BE-REVIEW-02,
            // F-03): this used to be one extra query per row.
            var fixture = new Fixture
            {
                PoleId = poleId!,
                CommuneId = pole!.CommuneId,
                FixtureType = fixtureType,
                PowerSource = powerSource,
                LampWatt = watt,
                LampCount = lampCount,
                Slot = slot,
                InstallDate = installDate!.Value,
                RemovedDate = removedDate,
                WarrantyExpiry = warranty,
                DataSource = dataSource,
                Note = note,
            };
            dbContext.Set<Fixture>().Add(fixture);
            Touch(fixture);

            if (matchExisting) fittings[(poleId!, installDate.Value)] = [fixture];
            inserted++;
        }

        return new WritePlan(inserted, 0, unchanged);
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

    /// <summary>A row another file refers to by <c>external_ref</c>: its id and the commune it sits in.</summary>
    private sealed record ReferencedAsset(string Id, string CommuneId);

    /// <summary>
    /// Maps each referenced <c>external_ref</c> to the rows that carry it, across everything the
    /// caller can see — id AND commune, because a reference across communes is a row error.
    /// </summary>
    /// <remarks>
    /// A list rather than a single id because <c>external_ref</c> is unique per COMMUNE, not globally.
    /// A caller scoped to several communes can legitimately see two segments coded <c>TUYEN-A</c>, and
    /// guessing between them would attach poles to the wrong road; <see cref="Resolve"/> reports the
    /// ambiguity instead.
    /// </remarks>
    private async Task<Dictionary<string, List<ReferencedAsset>>> ReferenceIndexAsync<TEntity>(
        List<ImportRowReader> readers,
        string column,
        CancellationToken cancellationToken)
        where TEntity : class, ICommuneScoped, IExternallyReferenced
    {
        var wanted = readers.Select(reader => reader.Row[column])
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return [];
        }

        // Three columns, not the whole row: a pole's geometry is the widest thing on the table and
        // nothing here reads it. The primary key is reached by name because the three asset types
        // share no base type (same reasoning as AssetCrudService.ListAsync).
        var key = dbContext.Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties.Single().Name;

        var rows = await dbContext.Set<TEntity>().AsNoTracking()
            .Where(entity => entity.ExternalRef != null && wanted.Contains(entity.ExternalRef))
            .Select(entity => new
            {
                Ref = entity.ExternalRef!,
                Id = EF.Property<string>(entity, key),
                entity.CommuneId,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Ref, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new ReferencedAsset(row.Id, row.CommuneId)).ToList(),
                StringComparer.Ordinal);
    }

    private static ReferencedAsset? Resolve(
        ImportRowReader reader, Dictionary<string, List<ReferencedAsset>> index, string column, bool required)
    {
        // The indexer reads a GeoJSON object / array as "empty". For an optional reference an empty cell MEANS something (a new pole
        // is left on no segment, an existing one keeps it), so a malformed value must fail here instead of being taken for that decision.
        if (reader.Row.IsNonScalar(column))
        {
            reader.Fail(column, "Expected a text reference (an external_ref); got an object or an array.");
            return null;
        }

        var value = required ? reader.Required(column) : reader.Optional(column);
        if (value is null)
        {
            return null;
        }

        if (reader.Row.ReferenceFailed(column))
        {
            reader.SuppressReference(); // The defining row already explains why this reference cannot resolve.
            return null;
        }

        index.TryGetValue(value, out var matches);
        // Workbook refs identify a definition in this row's commune; CSV/GeoJSON retain their existing lookup semantics.
        if (reader.Row.ReferenceCommune is { } commune && matches is not null)
            matches = matches.Where(match => match.CommuneId == commune).ToList();
        if (matches is null || matches.Count == 0)
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

    private async Task<Dictionary<string, HashSet<short>>> SlotsInUseAsync(string[] poleIds, CancellationToken cancellationToken)
    {
        if (poleIds.Length == 0)
        {
            return new Dictionary<string, HashSet<short>>(StringComparer.Ordinal);
        }

        // Only lamps still in service hold a slot; a retired one leaves room for its replacement.
        var rows = await dbContext.Set<Fixture>().AsNoTracking()
            .Where(fixture => poleIds.Contains(fixture.PoleId) && fixture.RemovedDate == null)
            .Select(fixture => new { fixture.PoleId, fixture.Slot })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(row => row.PoleId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Slot).ToHashSet(), StringComparer.Ordinal);
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
