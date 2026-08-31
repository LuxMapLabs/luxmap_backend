using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LuxMap.Api.Tests;

/// <summary>
/// A real host for the BE-08 tests. It adds the test assembly to <see cref="ModuleAssemblyCatalog"/>
/// so <see cref="ScopeProbe"/> joins the model — reusing the existing BE-03 seam without touching the
/// application. The table is created and dropped with raw SQL, so NO migration is left in the project.
/// </summary>
public sealed class ScopeTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string InScopeCommune = "COM-001";

    public string SecondCommune { get; private set; } = null!;
    public string OutOfScopeCommune { get; private set; } = null!;

    public long InScopeProbeId { get; private set; }
    public long SecondProbeId { get; private set; }
    public long OutOfScopeProbeId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ScopeTestController).Assembly);

            var catalog = services.Single(d => d.ServiceType == typeof(ModuleAssemblyCatalog));
            var existing = (ModuleAssemblyCatalog)catalog.ImplementationInstance!;
            services.Replace(ServiceDescriptor.Singleton(
                new ModuleAssemblyCatalog([.. existing.Assemblies, typeof(ScopeProbe).Assembly])));
        });
    }

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        await db.Database.ExecuteSqlRawAsync(ScopeProbeSchema.DropSql);
        await db.Database.ExecuteSqlRawAsync(ScopeProbeSchema.CreateSql);

        // Two extra communes created per run so BE-06's seeded data is never disturbed.
        SecondCommune = await EnsureCommuneAsync(db, $"Scope test commune B {Guid.NewGuid():N}"[..34]);
        OutOfScopeCommune = await EnsureCommuneAsync(db, $"Scope test commune C {Guid.NewGuid():N}"[..34]);

        InScopeProbeId = await InsertProbeAsync(db, "in scope", InScopeCommune);
        SecondProbeId = await InsertProbeAsync(db, "second commune", SecondCommune);
        OutOfScopeProbeId = await InsertProbeAsync(db, "out of scope", OutOfScopeCommune);
    }

    public new async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
            await db.Database.ExecuteSqlRawAsync(ScopeProbeSchema.DropSql);
        }

        await base.DisposeAsync();
    }

    /// <summary>Queries the database directly — inspects the REAL row state instead of trusting the API.</summary>
    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }

    /// <summary>Assigns a commune to a seeded user so the commune_ids claim holds several values.</summary>
    public async Task AssignCommuneAsync(string username, string communeId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var userId = await db.Set<AppUser>().Where(u => u.Username == username)
            .Select(u => u.UserId).SingleAsync();

        if (!await db.Set<AppUserCommune>().AnyAsync(a => a.UserId == userId && a.CommuneId == communeId))
        {
            db.Set<AppUserCommune>().Add(new AppUserCommune { UserId = userId, CommuneId = communeId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveAllCommunesAsync(string username)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var userId = await db.Set<AppUser>().Where(u => u.Username == username)
            .Select(u => u.UserId).SingleAsync();
        await db.Set<AppUserCommune>().Where(a => a.UserId == userId).ExecuteDeleteAsync();
    }

    public async Task SetSystemWideAsync(string username, bool value)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await db.Set<AppUser>().Where(u => u.Username == username)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.HasSystemWideScope, value));
    }

    private static async Task<string> EnsureCommuneAsync(LuxMapDbContext db, string name)
    {
        var existing = await db.Set<AdministrativeUnit>().FirstOrDefaultAsync(u => u.Name == name);
        if (existing is not null)
        {
            return existing.CommuneId;
        }

        var unit = new AdministrativeUnit { Name = name };
        db.Set<AdministrativeUnit>().Add(unit);
        await db.SaveChangesAsync();
        return unit.CommuneId;
    }

    private static async Task<long> InsertProbeAsync(LuxMapDbContext db, string label, string communeId)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {ScopeProbeSchema.TableName} (label, commune_id) VALUES (@l, @c) RETURNING id;";
        var l = command.CreateParameter(); l.ParameterName = "l"; l.Value = label; command.Parameters.Add(l);
        var c = command.CreateParameter(); c.ParameterName = "c"; c.Value = communeId; command.Parameters.Add(c);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}

[CollectionDefinition(nameof(ScopeCollection))]
public sealed class ScopeCollection : ICollectionFixture<ScopeTestFixture>;
