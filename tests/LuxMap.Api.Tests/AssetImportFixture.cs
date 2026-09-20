using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// A real host and database for the BE-12a import and CRUD tests.
/// </summary>
/// <remarks>
/// It creates TWO communes and an administrator scoped to only the first. That combination is the
/// point: <c>CommuneScopeConsistencyHandler</c> rejects a wildcard claim on a non-administrator, but
/// the reverse — an administrator scoped to named communes — is a supported configuration, and it is
/// the only way to exercise territorial rules against a role that is allowed to write at all. The
/// seeded <c>admin</c> account is system-wide, so the write guard never applies to it.
/// </remarks>
public sealed class AssetImportFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The commune the test administrator may write to.</summary>
    public string CommuneId { get; private set; } = null!;

    /// <summary>A commune that EXISTS but is outside the test administrator's scope.</summary>
    public string ForeignCommuneId { get; private set; } = null!;

    public string AdminUsername { get; private set; } = null!;

    /// <summary>
    /// An administrator whose scope covers BOTH communes.
    /// </summary>
    /// <remarks>
    /// Exists for exactly one question: when the query filter admits rows from two communes at once,
    /// is the upsert key the composite <c>(commune_id, external_ref)</c> or the code alone? With the
    /// single-commune account the filter hides the other row before the key is ever consulted, so
    /// that account cannot tell the two implementations apart.
    /// </remarks>
    public string BothCommunesUsername { get; private set; } = null!;

    /// <summary>From <c>.env</c>, exactly as BE-06 reads it. Never a literal in the test source.</summary>
    public string AdminPassword { get; } = AuthTestExtensions.SeedPassword("SEED_ADMIN_PASSWORD");

    private string userId = null!;

    private string bothCommunesUserId = null!;

    /// <summary>
    /// Hashes of the refresh tokens this fixture obtained for the SEEDED accounts, so teardown can
    /// delete exactly those rows (BE-REVIEW-02, N-5 / F-06).
    /// </summary>
    /// <remarks>
    /// The two accounts this fixture creates take their tokens with them (<c>refresh_token.user_id</c>
    /// cascades), but the seeded <c>engineer</c>, <c>agency</c> and <c>crew</c> outlive every run, and
    /// each <see cref="SeededClientAsync"/> sign-in left a row behind: 6,168 of them on the shared
    /// development database when this was measured. Deleting by hash touches only what this
    /// fixture issued, so a class running beside it keeps its own sessions. BE-36 makes all of this
    /// unnecessary by giving each run a fresh database.
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentBag<string> issuedTokenHashes = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Production").UseTestCorsOrigin();

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        // Building fixture data is acting as the system, not as a user, and the backdoor is spelled
        // the way it is so that shows up in the diff.
        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var mine = new AdministrativeUnit { Name = $"BE-12a home {Guid.NewGuid():N}"[..40] };
            var theirs = new AdministrativeUnit { Name = $"BE-12a foreign {Guid.NewGuid():N}"[..40] };
            db.Set<AdministrativeUnit>().AddRange(mine, theirs);
            await db.SaveChangesAsync();

            CommuneId = mine.CommuneId;
            ForeignCommuneId = theirs.CommuneId;

            AdminUsername = $"be12a-{Guid.NewGuid():N}"[..20];
            var user = new AppUser
            {
                Username = AdminUsername,
                Email = $"{AdminUsername}@luxmap.local",
                FullName = "BE-12a commune-scoped administrator",
                Role = UserRole.Administrator,

                // The whole reason this account exists: administrator rights WITHOUT system-wide
                // scope, so the territorial rules still bite.
                HasSystemWideScope = false,
                PasswordHash = string.Empty,
                PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
            };

            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, AdminPassword);
            db.Set<AppUser>().Add(user);
            await db.SaveChangesAsync();

            userId = user.UserId;
            db.Set<AppUserCommune>().Add(new AppUserCommune { UserId = userId, CommuneId = CommuneId });
            await db.SaveChangesAsync();

            BothCommunesUsername = $"be12b-{Guid.NewGuid():N}"[..20];
            var wide = new AppUser
            {
                Username = BothCommunesUsername,
                Email = $"{BothCommunesUsername}@luxmap.local",
                FullName = "BE-12a administrator over two communes",
                Role = UserRole.Administrator,
                HasSystemWideScope = false,
                PasswordHash = string.Empty,
                PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
            };

            wide.PasswordHash = new PasswordHasher<AppUser>().HashPassword(wide, AdminPassword);
            db.Set<AppUser>().Add(wide);
            await db.SaveChangesAsync();

            bothCommunesUserId = wide.UserId;
            db.Set<AppUserCommune>().AddRange(
                new AppUserCommune { UserId = bothCommunesUserId, CommuneId = CommuneId },
                new AppUserCommune { UserId = bothCommunesUserId, CommuneId = ForeignCommuneId });
            await db.SaveChangesAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

            // Raw SQL, in dependency order. Every table here has a Restrict or Cascade edge, so the
            // order is the schema's, not a preference.
            foreach (var sql in new[]
            {
                "DELETE FROM fixture WHERE commune_id = @c OR commune_id = @f;",
                "DELETE FROM pole WHERE commune_id = @c OR commune_id = @f;",
                "DELETE FROM feeder WHERE commune_id = @c OR commune_id = @f;",
                "DELETE FROM road_segment WHERE commune_id = @c OR commune_id = @f;",
                "DELETE FROM refresh_token WHERE user_id = @u OR user_id = @w OR token_hash = ANY(@h);",
                "DELETE FROM app_user_commune WHERE user_id = @u OR user_id = @w;",
                "DELETE FROM app_user WHERE user_id = @u OR user_id = @w;",
                "DELETE FROM administrative_unit WHERE commune_id = @c OR commune_id = @f;",
            })
            {
                await ExecuteAsync(db, sql);
            }
        }

        await base.DisposeAsync();
    }

    /// <summary>An <see cref="HttpClient"/> already carrying the commune-scoped administrator's token.</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        var client = CreateClient();
        var tokens = await (await client.PostLoginAsync(AdminUsername, AdminPassword)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return client;
    }

    /// <summary>An <see cref="HttpClient"/> for the administrator scoped to BOTH communes.</summary>
    public async Task<HttpClient> BothCommunesClientAsync()
    {
        var client = CreateClient();
        var tokens = await (await client.PostLoginAsync(BothCommunesUsername, AdminPassword)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return client;
    }

    /// <summary>A client for one of the four accounts BE-06 seeds, by role.</summary>
    public async Task<HttpClient> SeededClientAsync(string username, string passwordVariable)
    {
        var client = CreateClient();
        var tokens = await client.LoginAsync(username, passwordVariable);
        issuedTokenHashes.Add(Modules.Identity.Auth.RefreshTokenGenerator.Hash(tokens.RefreshToken));
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return client;
    }

    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }

    private async Task ExecuteAsync(LuxMapDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in new (string, object)[]
                 {
                     ("c", CommuneId), ("f", ForeignCommuneId), ("u", userId), ("w", bothCommunesUserId),
                     ("h", issuedTokenHashes.ToArray()),
                 })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }
}
