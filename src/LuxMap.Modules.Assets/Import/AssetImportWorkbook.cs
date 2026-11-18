using System.Net;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Import;

public sealed partial class AssetImportService
{
    public async Task<WorkbookImportResult> ImportWorkbookAsync(Stream stream, string? communeId, string? dataSource,
        CancellationToken cancellationToken)
    {
        void Invalid(string field, string message) => throw new LuxMapException(ErrorCodes.ValidationFailed,
            HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });
        if (!Enum.GetValues<DataSource>().Any(v => WireEnum.Name(v) == dataSource))
            Invalid("data_source", "Select a valid data_source.");
        if (string.IsNullOrWhiteSpace(communeId))
        {
            if (scopeAccessor.Scope.IsSystemWide || scopeAccessor.Scope.CommuneIds.Count != 1)
                Invalid("commune_id", "Select one commune.");
            communeId = scopeAccessor.Scope.CommuneIds.Single();
        }
        communeId = communeId.Trim();
        CommuneFilter.Narrow(scopeAccessor.Scope, [communeId]);
        var document = WorkbookDocument.Read(stream);
        var errors = document.Errors;
        var warnings = new List<WorkbookRowError>();
        var counts = new Dictionary<ImportKind, WorkbookKindCounts>();
        int Order(string? sheet) => WorkbookSheets.All.ToList().FindIndex(s => s.Name == sheet);
        WorkbookImportResult Result(bool committed)
        {
            var ordered = errors.Distinct().OrderBy(e => Order(e.Sheet)).ThenBy(e => e.Row).ThenBy(e => e.Column, StringComparer.Ordinal).ToArray();
            var reportedWarnings = committed
                ? warnings.OrderBy(e => Order(e.Sheet)).ThenBy(e => e.Row).ThenBy(e => e.Column, StringComparer.Ordinal).ToArray() : [];
            WorkbookKindCounts Count(ImportKind kind) => committed ? counts[kind] : new(0, 0, 0);
            return new(committed, Count(ImportKind.Segments), Count(ImportKind.Cabinets), Count(ImportKind.Feeders),
                Count(ImportKind.Poles), Count(ImportKind.Fixtures), ordered.Select(e => (e.Sheet, e.Row)).Distinct().Count(),
                ordered.Length, ordered.Length > ImportResult.MaxReportedErrors, [.. ordered.Take(ImportResult.MaxReportedErrors)],
                reportedWarnings.Length, [.. reportedWarnings.Take(ImportResult.MaxReportedErrors)]);
        }
        if (errors.Any(e => e.Row == 1 || e.Sheet == "IoT")) return Result(false);
        var failed = new HashSet<(ImportKind Kind, string? Commune, string? Ref)>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var kind in ImportKindExtensions.LoadOrder) await LockKindAsync(kind, cancellationToken);
        foreach (var kind in ImportKindExtensions.LoadOrder)
        {
            var sheet = WorkbookSheets.Name(kind);
            var kindErrors = new List<ImportRowError>();
            var kindWarnings = new List<ImportRowError>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var readers = new List<ImportRowReader>();
            foreach (var source in document.Sheets.GetValueOrDefault(kind) ?? [])
            {
                var row = new WorkbookSheetRow(source, communeId, dataSource!, failed)
                {
                    HasErrors = errors.Any(e => e.Sheet == sheet && e.Row == source.Number),
                };
                var reader = new ImportRowReader(row, kindErrors);
                if (kind != ImportKind.Fixtures && row["external_ref"] is { } reference)
                {
                    if (seen.TryGetValue(reference, out var first))
                    {
                        errors.Add(new(sheet, row.Row, "external_ref", $"Duplicate external_ref; first defined on row {first}."));
                        reader.Invalidate();
                    }
                    else seen[reference] = row.Row;
                }
                readers.Add(reader);
            }
            var plan = await PlanAsync(kind, readers, kindWarnings, cancellationToken, matchFixtures: true);
            errors.AddRange(kindErrors.Select(e => new WorkbookRowError(sheet, e.Row, e.Column, e.Message)));
            warnings.AddRange(kindWarnings.Select(e => new WorkbookRowError(sheet, e.Row, e.Column, e.Message)));
            counts[kind] = new(plan.Inserted, plan.Updated, plan.Unchanged);
            foreach (var reader in readers.Where(r => !r.IsValid))
                failed.Add((kind, communeId, reader.Optional("external_ref")));
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
}
