using System.Diagnostics;
using System.Security.Cryptography;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-36 — one throwaway PostGIS container per test run, migrated and seeded exactly like CI used to
/// prepare its service database: migrations, the four seed accounts, then the FO-26 mock set.
/// </summary>
/// <remarks>
/// <para>
/// Before this, every host resolved its database from <c>.env</c>, so a bare <c>dotnet test</c> on a
/// developer machine wrote into <c>luxmap_dev</c>. Now the run owns its database and nothing else is
/// reachable: there is deliberately no "use an existing database" switch (BE-36 D-5).
/// </para>
/// <para>
/// The whole connection string goes into <c>ConnectionStrings__LuxMap</c>, never the <c>POSTGRES_*</c>
/// parts: <c>Program</c> loads <c>.env</c> with DotNetEnv's default clobbering on every host build, so
/// the parts would be overwritten by the dev values. The one key that could overwrite the whole string
/// is refused up front (<see cref="LoadEnvironment"/>), and every host proves its own data source
/// points here (<see cref="AssertTargets"/>).
/// </para>
/// </remarks>
public static class TestDatabase
{
    /// <summary>The same image and UTC settings as the <c>postgres</c> service in docker-compose.yml.</summary>
    public const string Image = "imresamu/postgis:17-3.5";

    private const string ConnectionVariable = "ConnectionStrings__LuxMap";

    private static readonly Lazy<Task<NpgsqlConnectionStringBuilder>> Ready =
        new(() => Task.Run(StartAsync), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> Root = new(FindRoot);

    private static readonly object EnvironmentLock = new();
    private static bool environmentLoaded;

    /// <summary>
    /// Blocks until the database is ready. Called from a synchronous framework hook
    /// (<c>CreateHost</c>), so the work runs on the thread pool to stay clear of xUnit's
    /// synchronization context.
    /// </summary>
    public static NpgsqlConnectionStringBuilder EnsureReady() => Ready.Value.GetAwaiter().GetResult();

    /// <summary>
    /// Loads the repository-root <c>.env</c> into the process once, the way <c>Program</c> does. Seed
    /// passwords, the JWT key and the SMTP settings still come from there; only the database does not.
    /// </summary>
    public static void LoadEnvironment()
    {
        lock (EnvironmentLock)
        {
            if (environmentLoaded)
            {
                return;
            }

            var path = Path.Combine(Root.Value, ".env");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{path} is missing. Run `cp .env.example .env` at the repository root before `dotnet test`.");
            }

            if (DotNetEnv.Env.NoEnvVars().Load(path).Any(pair => pair.Key == ConnectionVariable))
            {
                throw new InvalidOperationException(
                    $".env defines {ConnectionVariable}. Program reloads .env on every host build and would point the "
                    + "tests at that database instead of the test container. Remove the line to run the tests.");
            }

            DotNetEnv.Env.Load(path);
            environmentLoaded = true;
        }
    }

    /// <summary>Throws unless the host's own data source targets this run's container.</summary>
    public static void AssertTargets(IServiceProvider services) => AssertTargets(services, EnsureReady());

    private static void AssertTargets(IServiceProvider services, NpgsqlConnectionStringBuilder expected)
    {
        var actual = new NpgsqlConnectionStringBuilder(services.GetRequiredService<NpgsqlDataSource>().ConnectionString);
        if (actual.Host != expected.Host || actual.Port != expected.Port || actual.Database != expected.Database)
        {
            throw new InvalidOperationException(
                $"Test host resolved {actual.Host}:{actual.Port}/{actual.Database}, not the test container "
                + $"{expected.Host}:{expected.Port}/{expected.Database}. Refusing to run against it.");
        }
    }

    private static async Task<NpgsqlConnectionStringBuilder> StartAsync()
    {
        LoadEnvironment();

        var container = new PostgreSqlBuilder(Image)
            .WithDatabase($"luxmap_test_{RandomNumberGenerator.GetHexString(8, lowercase: true)}")
            .WithUsername("luxmap")
            .WithPassword(RandomNumberGenerator.GetHexString(32, lowercase: true))
            .WithEnvironment("TZ", "UTC")
            .WithEnvironment("PGTZ", "UTC")
            .WithCommand("-c", "timezone=UTC", "-c", "log_timezone=UTC")
            .Build();

        try
        {
            await container.StartAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Could not start the PostGIS test container. LuxMap.Api.Tests needs a running Docker daemon; "
                + "the Shared, Persistence and Storage test projects do not.", exception);
        }

        // Testcontainers' resource reaper removes the container when the test process exits; this is the polite path
        // for a normal exit. Bounded and never thrown: a Docker daemon gone by then must not turn a green run red.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (!container.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10)))
                {
                    Console.Error.WriteLine("BE-36: the test container was not removed within 10 s; the resource reaper will remove it.");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"BE-36: could not remove the test container ({exception.GetBaseException().Message}); the resource reaper will.");
            }
        };

        var target = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
        await ExecuteAsync(target, "CREATE EXTENSION IF NOT EXISTS postgis;");

        Environment.SetEnvironmentVariable(ConnectionVariable, target.ConnectionString);

        await using (var bootstrap = new BootstrapHost())
        {
            AssertTargets(bootstrap.Services, target);
            await using var scope = bootstrap.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LuxMapDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(SeedCredentials.FromEnvironment());
        }

        // The mock loader prints its SQL when run without --apply; executing it here keeps the target
        // under this class's control. Its --apply path defaults to luxmap_dev and replaces whole tables.
        await ExecuteAsync(target, await MockSetSqlAsync());

        return target;
    }

    private static async Task ExecuteAsync(NpgsqlConnectionStringBuilder target, string sql)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> MockSetSqlAsync()
    {
        var start = new ProcessStartInfo("python3")
        {
            ArgumentList = { Path.Combine(Root.Value, "scripts", "seed_mock_set.py") },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("python3 could not be started to build the FO-26 mock set.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"scripts/seed_mock_set.py exited with {process.ExitCode}: {await error}");
        }

        return await output;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LuxMap.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root (LuxMap.slnx) above the test binaries.");
    }

    /// <summary>
    /// The host that migrates and seeds. It is NOT a <see cref="LuxMapTestHost"/> — that would wait on
    /// the very database this host is preparing — and it runs no background services, which would
    /// otherwise poll tables that do not exist yet.
    /// </summary>
    private sealed class BootstrapHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseTestCorsOrigin();
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
