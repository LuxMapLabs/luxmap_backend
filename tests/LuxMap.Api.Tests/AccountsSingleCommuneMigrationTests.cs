using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AccountsSingleCommuneMigrationTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Migration_keeps_earliest_assignment_breaks_ties_ordinally_and_cannot_restore_deleted_rows()
    {
        const string previous = "20261026100552_AddWorkOrderCancellationRequests";
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("single_commune_migration").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        var communes = new[] { new AdministrativeUnit { Name = "First" }, new AdministrativeUnit { Name = "Second" }, new AdministrativeUnit { Name = "Third" } };
        var users = new[] { User("earliest"), User("tied") };
        db.AddRange(communes); db.AddRange(users); await db.SaveChangesAsync();
        var ordered = communes.Select(c => c.CommuneId).Order(StringComparer.Ordinal).ToArray();
        // Reverse insertion order; timestamps, then ordinal IDs, must decide the retained rows.
        foreach (var user in users)
        foreach (var commune in ordered.Reverse())
        {
            var time = user == users[0] && commune == ordered[2]
                ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.Database.ExecuteSqlAsync($"INSERT INTO app_user_commune (user_id, commune_id, assigned_at) VALUES ({user.UserId}, {commune}, {time})");
        }
        await db.Database.OpenConnectionAsync();
        var notices = new List<string>();
        ((NpgsqlConnection)db.Database.GetDbConnection()).Notice += (_, args) => notices.Add(args.Notice.MessageText);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var assignments = await db.Set<AppUserCommune>().AsNoTracking().ToArrayAsync();
        Assert.Equal(2, assignments.Length);
        Assert.Equal(ordered[2], Assert.Single(assignments, x => x.UserId == users[0].UserId).CommuneId);
        Assert.Equal(ordered[0], Assert.Single(assignments, x => x.UserId == users[1].UserId).CommuneId);
        foreach (var user in users)
        foreach (var removed in ordered.Where(c => c != assignments.Single(x => x.UserId == user.UserId).CommuneId))
            Assert.Contains(notices, n => n.Contains($"user_id={user.UserId}, commune_id={removed}", StringComparison.Ordinal));
        foreach (var notice in notices) output.WriteLine(notice);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO app_user_commune (user_id, commune_id) VALUES ({users[0].UserId}, {ordered[0]})"));
        Assert.Equal("23505", error.SqlState);
        Assert.Equal("ux_app_user_commune_user_id", error.ConstraintName);
        await migrator.MigrateAsync(previous);
        Assert.Equal(2, await db.Set<AppUserCommune>().CountAsync());
        await db.Database.ExecuteSqlAsync($"INSERT INTO app_user_commune (user_id, commune_id) VALUES ({users[0].UserId}, {ordered[0]})");
        await migrator.MigrateAsync();
        Assert.Equal(2, await db.Set<AppUserCommune>().CountAsync());
        Assert.Contains("ix_app_user_commune_commune_id", await db.Database.SqlQuery<string>(
            $"SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'app_user_commune'").ToArrayAsync());
    }

    private static AppUser User(string name) => new()
    {
        Username = name, Email = name + "@example.invalid", FullName = name,
        Role = UserRole.Superior, PasswordAlgorithm = "pbkdf2-aspnetcore-v3",
    };
}
