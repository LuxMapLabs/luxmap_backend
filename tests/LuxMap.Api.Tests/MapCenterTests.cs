using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// MAP-CENTER — <c>GET /auth/me</c> opens a user's map on their commune's poles rather than on the middle of its frame,
/// which for a frame drawn around a whole ward can be kilometres from any lamp. The frame itself is unchanged.
/// </summary>
/// <remarks>
/// In <see cref="AssetDatabaseCollection"/> because it writes poles. Each test makes its own commune and manager (an account
/// holds one commune), so the fixture's communes — full of other tests' poles — never enter the average.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class MapCenterTests(AssetImportFixture fixture)
{
    private const string Password = "a-long-enough-map-center-password";

    [Fact]
    public async Task The_map_opens_on_the_mean_of_the_poles_and_ignores_poles_without_a_position()
    {
        await using var probe = await CommuneAsync(
            new Point(107.21, 10.41), new Point(107.23, 10.43), new Point(107.22, 10.42), null);

        var me = await MeAsync(probe);

        var commune = Assert.Single(me.GetProperty("communes").EnumerateArray());
        AssertCenter(10.42, 107.22, commune.GetProperty("center"));
        AssertCenter(10.42, 107.22, me.GetProperty("map_bounds").GetProperty("center"));
        // The frame is what holds the map; it is not moved.
        Assert.Equal(107.40, me.GetProperty("map_bounds").GetProperty("bbox").GetProperty("max_lng").GetDouble());
        Assert.Equal(10.40, me.GetProperty("map_bounds").GetProperty("bbox").GetProperty("min_lat").GetDouble());
    }

    [Fact]
    public async Task Poles_outside_the_frame_open_the_map_on_its_nearest_edge()
    {
        await using var probe = await CommuneAsync(new Point(107.30, 10.70));

        var me = await MeAsync(probe);

        AssertCenter(10.60, 107.30, me.GetProperty("map_bounds").GetProperty("center"));
    }

    [Fact]
    public async Task A_commune_without_a_positioned_pole_opens_on_its_frame_midpoint()
    {
        await using var probe = await CommuneAsync((Point?)null);

        var me = await MeAsync(probe);

        AssertCenter(10.50, 107.30, me.GetProperty("map_bounds").GetProperty("center"));
    }

    private static void AssertCenter(double lat, double lng, JsonElement center)
    {
        Assert.Equal(lat, center.GetProperty("lat").GetDouble(), 9);
        Assert.Equal(lng, center.GetProperty("lng").GetDouble(), 9);
    }

    private async Task<JsonElement> MeAsync(Probe probe)
    {
        var client = fixture.CreateClient();
        var tokens = await (await client.PostLoginAsync(probe.Username, Password)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        return JsonDocument.Parse(await client.GetStringAsync("/api/v1/auth/me")).RootElement;
    }

    /// <summary>A commune framed 107.20–107.40 / 10.40–10.60, a manager of it, and one pole per position given.</summary>
    private Task<Probe> CommuneAsync(params Point?[] positions)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var commune = new AdministrativeUnit
                {
                    Name = $"map-center {Guid.NewGuid():N}"[..40],
                    BboxMinLng = 107.20, BboxMinLat = 10.40, BboxMaxLng = 107.40, BboxMaxLat = 10.60,
                };
                db.Set<AdministrativeUnit>().Add(commune);
                await db.SaveChangesAsync();

                var username = $"mapc-{Guid.NewGuid():N}"[..20];
                var user = new AppUser
                {
                    Username = username,
                    Email = $"{username}@luxmap.local",
                    FullName = "MAP-CENTER manager",
                    Role = UserRole.Manager,
                    PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
                    PasswordSetAt = DateTime.UtcNow,
                };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Password);
                db.Set<AppUser>().Add(user);
                await db.SaveChangesAsync();

                db.Set<AppUserCommune>().Add(new AppUserCommune { UserId = user.UserId, CommuneId = commune.CommuneId });
                foreach (var position in positions)
                {
                    if (position is not null)
                    {
                        position.SRID = 4326;
                    }

                    db.Set<Pole>().Add(new Pole { CommuneId = commune.CommuneId, Geom = position, DataSource = DataSource.PublicImagery });
                }

                await db.SaveChangesAsync();
                return new Probe(fixture, commune.CommuneId, user.UserId, username);
            }
        });

    private sealed record Probe(AssetImportFixture Fixture, string CommuneId, string UserId, string Username) : IAsyncDisposable
    {
        /// <summary>Children before parents: poles, then the account's tokens and commune link, the account, the commune.</summary>
        public async ValueTask DisposeAsync()
            => await Fixture.QueryAsync(async db =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE commune_id = {CommuneId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM refresh_token WHERE user_id = {UserId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = {UserId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = {UserId}");
                return await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM administrative_unit WHERE commune_id = {CommuneId}");
            });
    }
}
