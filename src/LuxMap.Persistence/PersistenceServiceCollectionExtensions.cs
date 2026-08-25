using System.Reflection;
using Microsoft.EntityFrameworkCore;
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
    /// NetTopologySuite phải bật ở tầng DataSource thì Npgsql mới đọc/ghi được
    /// <c>geometry</c> thành <c>Point</c> / <c>LineString</c>.
    /// </summary>
    public static NpgsqlDataSource BuildDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseNetTopologySuite();
        return builder.Build();
    }

    /// <summary>
    /// Một chỗ duy nhất cấu hình DbContext — host và công cụ migration phải đi qua đây,
    /// nếu không quy ước snake_case sẽ chỉ áp cho một bên và migration lệch với runtime.
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
        => options
            .UseNpgsql(dataSource, npgsql => npgsql
                .UseNetTopologySuite()
                // EF mặc định đặt bảng lịch sử là "__EFMigrationsHistory" — mixed case nên
                // Postgres buộc phải quote ở mọi nơi, trái Contract mục 5.1. Đổi trước BE-09,
                // sau khi initial migration đã chạy thì đổi rất phiền.
                .MigrationsHistoryTable("__ef_migrations_history"))
            // Contract mục 5.1: tên bảng/cột snake_case toàn chữ thường, không quote.
            .UseSnakeCaseNamingConvention();
}
