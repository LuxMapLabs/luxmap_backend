using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LuxMap.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLuxMapPersistence(
        this IServiceCollection services,
        string connectionString,
        IEnumerable<Assembly> moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(moduleAssemblies);

        services.AddSingleton(new ModuleAssemblyCatalog(moduleAssemblies));
        services.AddSingleton(_ => BuildDataSource(connectionString));
        services.AddDbContext<LuxMapDbContext>((provider, options) =>
            Configure(options, provider.GetRequiredService<NpgsqlDataSource>()));

        return services;
    }

    /// <summary>
    /// NetTopologySuite has to be enabled at the data source level before Npgsql can read and write
    /// <c>geometry</c> as <c>Point</c> / <c>LineString</c>.
    /// </summary>
    public static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseNetTopologySuite();
        return builder.Build();
    }

    /// <summary>
    /// The single place the DbContext is configured — the host and the migration tooling must both
    /// go through it, otherwise the snake_case convention applies to only one of them and the
    /// migrations drift from the runtime model.
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
        => options
            .UseNpgsql(dataSource, npgsql => npgsql
                .UseNetTopologySuite()
                // EF names the history table "__EFMigrationsHistory" by default — mixed case, so
                // Postgres has to quote it everywhere, which violates Contract section 5.1. Change
                // it before BE-09; changing it after the initial migration has run is painful.
                .MigrationsHistoryTable("__ef_migrations_history"))
            // Contract section 5.1: table and column names are lowercase snake_case, unquoted.
            .UseSnakeCaseNamingConvention()
            // The model depends on the module list, so the model cache key must include it.
            .ReplaceService<IModelCacheKeyFactory, LuxMapModelCacheKeyFactory>();
}
