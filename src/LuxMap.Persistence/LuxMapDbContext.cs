using System.Reflection;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        foreach (var assembly in catalog.Assemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        // Must run AFTER every configuration is applied: scans the model for columns marked by
        // HasPrefixedId and creates the matching sequence, so nobody has to remember to declare one.
        modelBuilder.CreatePrefixedIdSequences();

        // Contract section 7. Forgetting the scope means the app WILL NOT START, instead of leaking
        // data silently.
        modelBuilder.ApplyCommuneScope(this);
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
