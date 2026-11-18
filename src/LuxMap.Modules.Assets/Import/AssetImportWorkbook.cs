using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Import;

public sealed partial class AssetImportService
{
    public async Task<WorkbookImportResult> ImportWorkbookAsync(Stream stream, CancellationToken cancellationToken)
    {
        var document = WorkbookDocument.Read(stream);
        var errors = document.Errors;
        var warnings = new List<ImportRowError>();
        var counts = new Dictionary<ImportKind, WorkbookKindCounts>();
        WorkbookImportResult Result(bool committed)
        {
            var ordered = errors.Distinct().OrderBy(e => e.Row).ThenBy(e => e.Column, StringComparer.Ordinal).ToArray();
            var reportedWarnings = committed
                ? warnings.OrderBy(e => e.Row).ThenBy(e => e.Column, StringComparer.Ordinal).ToArray() : [];
            WorkbookKindCounts Count(ImportKind kind) => committed ? counts[kind] : new(0, 0, 0);
            return new(committed, Count(ImportKind.Segments), Count(ImportKind.Cabinets), Count(ImportKind.Feeders),
                Count(ImportKind.Poles), Count(ImportKind.Fixtures), ordered.Select(e => e.Row).Distinct().Count(),
                ordered.Length, ordered.Length > ImportResult.MaxReportedErrors, [.. ordered.Take(ImportResult.MaxReportedErrors)],
                reportedWarnings.Length, [.. reportedWarnings.Take(ImportResult.MaxReportedErrors)]);
        }
        if (errors.Any(e => e.Row == 1)) return Result(false);

        var failed = new HashSet<(ImportKind Kind, string? Commune, string? Ref)>();
        var projections = ProjectWorkbook(document, failed);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var kind in ImportKindExtensions.LoadOrder) await LockKindAsync(kind, cancellationToken);
        foreach (var kind in ImportKindExtensions.LoadOrder)
        {
            var readers = projections[kind].Select(r => new ImportRowReader(r, errors)).ToList();
            var kindWarnings = new List<ImportRowError>();
            var plan = await PlanAsync(kind, readers, kindWarnings, cancellationToken, matchFixtures: true);
            warnings.AddRange(kindWarnings.Select(w => w with { Column = "pole_note" }));
            counts[kind] = new(plan.Inserted, plan.Updated, plan.Unchanged);
            foreach (var reader in readers.Where(r => !r.IsValid))
                failed.Add((kind, reader.Optional("commune_id"), reader.Optional("external_ref")));
            // IDs must exist before the next planner builds its reference index. All writes remain provisional.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (errors.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Result(false);
        }
        await transaction.CommitAsync(cancellationToken);
        return Result(true);
    }

    private static Dictionary<ImportKind, List<WorkbookImportRow>> ProjectWorkbook(WorkbookDocument document,
        HashSet<(ImportKind Kind, string? Commune, string? Ref)> failed)
    {
        var result = ImportKindExtensions.LoadOrder.ToDictionary(k => k, _ => new List<WorkbookImportRow>());
        var groups = new Dictionary<(ImportKind, string?, string), WorkbookDocument.SheetRow>();
        string[] fixtureColumns = ["fixture_type", "power_source", "lamp_watt", "lamp_count", "install_date", "warranty_expiry", "fixture_note"];
        foreach (var row in document.Rows)
        {
            foreach (var kind in ImportKindExtensions.LoadOrder)
            {
                string[] columns = kind switch
                {
                    ImportKind.Segments => ["segment_ref", "segment_name", "road_class", "segment_length_m", "data_source"],
                    ImportKind.Cabinets => ["cabinet_ref", "cabinet_name", "cabinet_lat", "cabinet_lng", "data_source"],
                    ImportKind.Feeders => ["feeder_ref", "feeder_name", "cabinet_ref", "cabinet_source"],
                    _ => ["pole_ref"],
                };
                var reference = row.Cells.GetValueOrDefault(columns[0]);
                if (kind == ImportKind.Fixtures && !fixtureColumns.Any(c => row.Cells.GetValueOrDefault(c) is not null)) continue;
                if (kind is ImportKind.Segments or ImportKind.Cabinets or ImportKind.Feeders && reference is null) continue;
                var projection = new WorkbookImportRow(row, kind, failed)
                {
                    HasErrors = document.Errors.Any(e => e.Row == row.Number),
                };
                if (kind != ImportKind.Fixtures && reference is not null)
                {
                    var key = (kind, row.Cells.GetValueOrDefault("commune_id"), reference);
                    if (groups.TryGetValue(key, out var first))
                    {
                        if (kind == ImportKind.Poles)
                            document.Errors.Add(new(row.Number, "pole_ref", $"Duplicate pole_ref; first defined on row {first.Number}."));
                        else foreach (var column in columns.Skip(1))
                        {
                            var value = row.Cells.GetValueOrDefault(column);
                            var previous = first.Cells.GetValueOrDefault(column);
                            if (value is not null && !Equal(column, value, previous))
                                document.Errors.Add(new(row.Number, column, $"Conflicts with the value defined on row {first.Number}."));
                        }
                        continue;
                    }
                    groups[key] = row;
                }
                result[kind].Add(projection);
            }
        }
        return result;
    }

    private static bool Equal(string column, string value, string? previous)
        => column is "segment_length_m" or "cabinet_lat" or "cabinet_lng"
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(previous, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
                ? a == b : string.Equals(value, previous, StringComparison.Ordinal);
}
