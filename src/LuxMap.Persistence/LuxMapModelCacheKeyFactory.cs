using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LuxMap.Persistence;

/// <summary>
/// EF Core's default model cache key is just the DbContext TYPE. The LuxMap model also depends on
/// <see cref="ModuleAssemblyCatalog"/> — the module list decides which entities exist.
/// </summary>
/// <remarks>
/// Without this, two hosts in the same process with different module lists share whichever model was
/// built first, and the other one throws when it queries its own entities. The real application only
/// ever runs one host, so nothing changes there — but the cache key must reflect what the model
/// actually depends on, otherwise this is a time bomb for every multi-host scenario.
/// </remarks>
public sealed class LuxMapModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        var catalogSignature = context is LuxMapDbContext luxMap
            ? luxMap.CatalogSignature
            : string.Empty;

        return (context.GetType(), catalogSignature, designTime);
    }
}
