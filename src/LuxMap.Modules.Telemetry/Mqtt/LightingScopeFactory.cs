using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Lighting;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>
/// The MQTT channel has no HTTP request, hence no principal: each message gets its own context whose scope is the device's ONE
/// commune (LC-12 M-14, the <c>SurveyProcessor.JobScope</c> shape). Query filters, <c>CommuneWriteGuard</c> and the audit guard all
/// run exactly as for the device's own HTTP requests.
/// </summary>
public sealed class LightingScopeFactory(
    NpgsqlDataSource dataSource, ModuleAssemblyCatalog catalog, TimeProvider clock, LightingOptions options)
{
    /// <summary>A context + service scoped to <paramref name="communeId"/>. Dispose it.</summary>
    public LightingScope Open(string communeId, string correlationId)
    {
        var db = Context(CommuneScope.ForCommunes([communeId]));
        var service = new LightingCommandService(db, new AuditTrail(db, new FixedCorrelation(correlationId)), NoActor.Instance, clock, options);
        return new LightingScope(db, service);
    }

    /// <summary>
    /// The commune of a device — read UNFILTERED, only to establish whose scope a message runs in (M-14). Null for an unknown id.
    /// </summary>
    public async Task<string?> CommuneOfAsync(string nodeId, CancellationToken ct)
    {
        await using var db = Context(CommuneScope.Empty);
        return await db.Set<IotNode>().IgnoreQueryFilters().AsNoTracking()
            .Where(node => node.NodeId == nodeId).Select(node => node.CommuneId).FirstOrDefaultAsync(ct);
    }

    /// <summary>Devices that still have an open command, with their commune — read unfiltered, to know which scopes to open.</summary>
    public async Task<IReadOnlyList<(string NodeId, string CommuneId)>> NodesWithOpenCommandsAsync(CancellationToken ct)
    {
        await using var db = Context(CommuneScope.Empty);
        var rows = await db.Set<LightingCommand>().IgnoreQueryFilters().AsNoTracking()
            .Where(command => command.Status == LightingCommandStatus.Pending || command.Status == LightingCommandStatus.Delivered)
            .Select(command => new { command.NodeId, command.CommuneId })
            .Distinct()
            .ToListAsync(ct);
        return [.. rows.Select(row => (row.NodeId, row.CommuneId))];
    }

    private LuxMapDbContext Context(CommuneScope scope)
    {
        var builder = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, dataSource);
        return new LuxMapDbContext(builder.Options, catalog, new FixedScope(scope));
    }

    private sealed class FixedScope(CommuneScope scope) : ICommuneScopeAccessor
    {
        public CommuneScope Scope { get; } = scope;
    }

    private sealed class FixedCorrelation(string id) : ICorrelationIdAccessor
    {
        public string CorrelationId { get; } = id;
    }

    /// <summary>A device message has no user: actor fields stay null, as for every <c>iot</c> / <c>system</c> audit event.</summary>
    private sealed class NoActor : ICurrentActorAccessor
    {
        public static readonly NoActor Instance = new();

        public string? UserId => null;

        public UserRole? Role => null;
    }
}

public sealed class LightingScope(LuxMapDbContext db, LightingCommandService service) : IAsyncDisposable
{
    public LightingCommandService Service { get; } = service;

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
