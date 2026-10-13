using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Citizen;

/// <summary>
/// The public form has no principal, so the request's own context has an EMPTY scope: it sees no row and may write none
/// (review P1 #1). The code is looked up unfiltered — only to learn which pole and commune it names — and everything after runs in
/// a context scoped to that ONE commune (the <c>LightingScopeFactory</c> shape), where query filters, <c>CommuneWriteGuard</c>
/// and the audit guard all apply as usual. Never a backdoor, never <c>*</c>.
/// </summary>
public sealed class CitizenScopeFactory(NpgsqlDataSource dataSource, ModuleAssemblyCatalog catalog)
{
    /// <summary>A context whose scope is exactly <paramref name="communeId"/>. Dispose it.</summary>
    public LuxMapDbContext Open(string communeId) => Context(CommuneScope.ForCommunes([communeId]));

    /// <summary>For the unfiltered lookups only: the code → pole → commune step and the replay check. Never writes.</summary>
    public LuxMapDbContext Unscoped() => Context(CommuneScope.Empty);

    public static IAuditTrail AuditFor(LuxMapDbContext db, ICorrelationIdAccessor correlation) => new AuditTrail(db, correlation);

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
}
