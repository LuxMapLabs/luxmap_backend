using System.Reflection;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace LuxMap.Persistence;

/// <summary>
/// One DbContext shared by the whole monolith. Every module contributes its own
/// <see cref="IEntityTypeConfiguration{TEntity}"/>; the context scans each module's assembly, so
/// Persistence never has to reference a module back.
/// <para>
/// A single shared context rather than one per module because the Contract requires cross-module
/// joins: <c>GET /poles/{id}</c> must return <c>open_faults[]</c> in ONE request,
/// <c>GET /segments</c> needs <c>has_active_segment_fault</c>, and <c>GET /sync/bundle</c> bundles
/// poles, segments, faults and work orders together.
/// </para>
/// </summary>
public class LuxMapDbContext(
    DbContextOptions<LuxMapDbContext> options,
    ModuleAssemblyCatalog catalog,
    ICommuneScopeAccessor scopeAccessor)
    : DbContext(options)
{
    /// <summary>
    /// Commune scope of the current request. Query filters read it through HERE rather than
    /// capturing an outside accessor — see the note on <c>CommuneScopeBuilderExtensions.ApplyFilter</c>.
    /// </summary>
    public CommuneScope CurrentCommuneScope => scopeAccessor.Scope;

    /// <summary>
    /// Identifies the set of loaded modules. The model depends on it, so the model cache key must
    /// include it — see <see cref="LuxMapModelCacheKeyFactory"/>.
    /// </summary>
    internal string CatalogSignature => catalog.Signature;

    /// <summary>Nesting depth of <see cref="EnterUnscopedSystemWriteBackdoor"/>.</summary>
    private int systemWriteDepth;

    /// <summary>
    /// ⚠️ <b>A BACKDOOR.</b> Turns OFF the Contract section 7 check on writes until the returned
    /// handle is disposed.
    /// </summary>
    /// <remarks>
    /// Named to be impossible to reach for by accident. It exists for the two callers that legitimately
    /// write outside any request — <c>IdentitySeeder</c>, which creates the communes themselves before
    /// any scope could name them, and the integration-test fixtures, which build data with no HTTP
    /// context at all.
    /// <para>
    /// <b>It is deliberately not implicit.</b> The obvious shortcut would be to let an EMPTY scope
    /// through, since seeder and fixtures both have one — but an empty scope is also what an
    /// unauthenticated caller and a token carrying no <c>commune_ids</c> produce. Treating it as
    /// permission would open the guard precisely to the callers it was written to stop. Opting out
    /// has to be an act, visible at the call site and in a diff.
    /// </para>
    /// <para>
    /// Never widen this to make a test pass. A test that needs it is telling you it writes as the
    /// system, and it should say so.
    /// </para>
    /// </remarks>
    public IDisposable EnterUnscopedSystemWriteBackdoor()
    {
        systemWriteDepth++;
        return new SystemWriteBackdoor(this);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Both public overloads of <c>SaveChanges</c> funnel through this one, so guarding here covers
    /// the parameterless call as well.
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceCommuneWriteScope();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceCommuneWriteScope();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Contract section 7 for writes — see <see cref="CommuneWriteGuard"/>.
    /// </summary>
    /// <remarks>
    /// Runs BEFORE the base call on purpose: throwing from inside EF's save pipeline would come back
    /// wrapped in a <c>DbUpdateException</c>, and the BE-04 middleware would then answer 500 instead
    /// of the 403 this actually is.
    /// </remarks>
    private void EnforceCommuneWriteScope()
    {
        if (systemWriteDepth > 0)
        {
            return;
        }

        CommuneWriteGuard.Enforce(ChangeTracker, CurrentCommuneScope);
    }

    private sealed class SystemWriteBackdoor(LuxMapDbContext context) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            context.systemWriteDepth--;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        // Persistence's own assembly first: it owns AdministrativeUnit, the anchor every
        // commune_id foreign key points at.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LuxMapDbContext).Assembly);

        foreach (var assembly in catalog.Assemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        // Must run AFTER every configuration is applied: scans the model for columns marked by
        // HasPrefixedId and creates the matching sequence, so nobody has to remember to declare one.
        modelBuilder.CreatePrefixedIdSequences();

        // BE-10 — SpatialFunctions.DistanceMeters, translated to PostGIS rather than backed by a
        // database function, so the whole feature carries no migration.
        modelBuilder.HasLuxMapSpatialFunctions(this.GetService<IRelationalTypeMappingSource>());

        // Contract section 7. Forgetting the scope means the app WILL NOT START, instead of leaking
        // data silently.
        modelBuilder.ApplyCommuneScope(this);

        // The second half of the same guarantee: ApplyCommuneScope can only see entities that
        // implement ICommuneScoped, so this one checks for the COLUMN instead and catches the
        // entity that carries commune_id but never declared itself scoped.
        modelBuilder.ValidateCommuneReferences();
    }
}

/// <summary>
/// The assemblies scanned for entity configurations. The host builds it from the registered module list.
/// </summary>
public sealed class ModuleAssemblyCatalog(IEnumerable<Assembly> assemblies)
{
    public IReadOnlyList<Assembly> Assemblies { get; } = [.. assemblies.Distinct()];

    /// <summary>A stable string identifying the assembly set, used as part of the model cache key.</summary>
    public string Signature => field ??= string.Join('|', Assemblies.Select(a => a.FullName).Order(StringComparer.Ordinal));
}
