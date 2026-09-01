using System.Reflection;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// Commune scoping at the query level (Contract section 7).
/// <para>
/// Use it in the <c>IEntityTypeConfiguration</c> of any entity implementing <see cref="ICommuneScoped"/>:
/// <code>
/// builder.HasCommuneScope();
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// Same shape as <c>HasPrefixedId</c>: this call only MARKS the intent; the actual filter is applied
/// centrally by <see cref="LuxMapDbContext"/> once every module configuration has been loaded —
/// <c>ApplyConfigurationsFromAssembly</c> instantiates configurations through their parameterless
/// constructor, so an <see cref="ICommuneScopeAccessor"/> cannot be injected here.
/// <para>
/// The filter lives in the <c>WHERE</c> clause rather than in a post-fetch check — that is what makes
/// an out-of-scope <c>GET /poles/{id}</c> return <b>404</b>: the row simply is not found.
/// Fetch-then-check naturally produces 403 and reveals that the resource exists.
/// </para>
/// </remarks>
public static class CommuneScopeBuilderExtensions
{
    /// <summary>
    /// An annotation WE define. The guard compares against this rather than probing EF's internal
    /// query-filter metadata, so it does not depend on the EF version.
    /// </summary>
    internal const string ScopeAnnotation = "LuxMap:CommuneScopeApplied";

    public static EntityTypeBuilder<TEntity> HasCommuneScope<TEntity>(this EntityTypeBuilder<TEntity> entity)
        where TEntity : class, ICommuneScoped
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.HasAnnotation(ScopeAnnotation, true);
    }

    /// <summary>
    /// Applies the filter to every marked entity AND enforces the invariant: an entity implementing
    /// <see cref="ICommuneScoped"/> without the marker throws — the application will not start.
    /// </summary>
    internal static void ApplyCommuneScope(this ModelBuilder modelBuilder, LuxMapDbContext context)
    {
        var scoped = modelBuilder.Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType.IsAssignableTo(typeof(ICommuneScoped)))
            .ToArray();

        var missing = scoped
            .Where(entity => entity.FindAnnotation(ScopeAnnotation) is null)
            .Select(entity => entity.ClrType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Entities implement ICommuneScoped but never call HasCommuneScope(): {string.Join(", ", missing)}. "
                + "Contract section 7 requires commune scoping on the server, ALWAYS — leaving it out leaks "
                + "data across communes. Add builder.HasCommuneScope() to that entity's IEntityTypeConfiguration.");
        }

        foreach (var entity in scoped)
        {
            ApplyFilterMethod
                .MakeGenericMethod(entity.ClrType)
                .Invoke(null, [modelBuilder, context]);
        }
    }

    private static readonly MethodInfo ApplyFilterMethod =
        typeof(CommuneScopeBuilderExtensions).GetMethod(
            nameof(ApplyFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// The expression references the DbContext ITSELF, not an accessor captured from outside.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is not a style choice. Capturing <c>ICommuneScopeAccessor</c> from outside lets EF treat
    /// <c>scopeAccessor.Scope.IsSystemWide</c> as an evaluatable subtree and FOLD IT INTO A CONSTANT
    /// inside the compiled query. That query is cached by shape, so EVERY later user reuses the FIRST
    /// user's scope — a silent cross-commune leak.
    /// Going through the DbContext is the officially supported path: the value is re-read per context
    /// instance, which means per request.
    /// </remarks>
    private static void ApplyFilter<TEntity>(ModelBuilder modelBuilder, LuxMapDbContext context)
        where TEntity : class, ICommuneScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(candidate =>
            context.CurrentCommuneScope.IsSystemWide
            || context.CurrentCommuneScope.CommuneIds.Contains(candidate.CommuneId));
    }
}
