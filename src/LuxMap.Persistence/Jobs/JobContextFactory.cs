using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Persistence.Jobs;

/// <summary>
/// Contexts for a background job (BE-26), which has no principal: one with an EMPTY scope for finding candidates across communes
/// (<c>IgnoreQueryFilters</c>, read only — the write guard refuses every write there), and one scoped to exactly ONE commune for every
/// write, where query filters, <c>CommuneWriteGuard</c> and the audit guard apply as usual. The <c>SurveyProcessor.JobScope</c> shape;
/// never the backdoor, never <c>*</c>.
/// </summary>
public sealed class JobContextFactory(NpgsqlDataSource dataSource, ModuleAssemblyCatalog catalog)
{
    public LuxMapDbContext Open(string communeId) => Context(CommuneScope.ForCommunes([communeId]));

    public LuxMapDbContext Unscoped() => Context(CommuneScope.Empty);

    public static IAuditTrail AuditFor(LuxMapDbContext db, string correlationId) => new AuditTrail(db, new Correlation(correlationId));

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

    private sealed class Correlation(string id) : ICorrelationIdAccessor
    {
        public string CorrelationId { get; } = id;
    }
}
