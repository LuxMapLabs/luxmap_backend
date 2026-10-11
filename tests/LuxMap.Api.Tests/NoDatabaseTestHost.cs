using LuxMap.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-36 — the host for the few tests that never reach the database (CORS, correlation id, the paging envelope, startup
/// failures). It does not wait for the test container, so those tests still run without Docker.
/// </summary>
/// <remarks>
/// It cannot point the process-wide <c>ConnectionStrings__LuxMap</c> anywhere — the container hosts read that same variable
/// in parallel. So it swaps the data source itself for one on an address nothing listens on: a test here that DOES touch
/// the database fails with "connection refused" instead of quietly using the database in <c>.env</c>
/// (<see cref="NoDatabaseHostTests"/>).
/// </remarks>
public abstract class NoDatabaseTestHost : WebApplicationFactory<Program>
{
    public const string Unreachable = "Host=127.0.0.1;Port=1;Database=no_database;Username=none;Password=none;Timeout=2";

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ConfigureHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(_ => PersistenceServiceCollectionExtensions.BuildDataSource(Unreachable));
        });
    }

    protected abstract void ConfigureHost(IWebHostBuilder builder);
}

/// <summary>The <see cref="LuxMapApiFactory"/> setup, without a database.</summary>
public sealed class NoDatabaseApiFactory : NoDatabaseTestHost
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseTestCorsOrigin();
        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly));
    }
}

public sealed class NoDatabaseHostTests
{
    [Fact]
    public async Task A_host_without_a_database_reaches_none_not_the_one_in_env()
    {
        await using var host = new NoDatabaseApiFactory();
        await using var scope = host.Services.CreateAsyncScope();

        Assert.False(await scope.ServiceProvider.GetRequiredService<LuxMapDbContext>().Database.CanConnectAsync());
        Assert.Equal("no_database", new NpgsqlConnectionStringBuilder(
            scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>().ConnectionString).Database);
    }
}
