using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// A real host and a real PostGIS database for the BE-09 tests.
/// <para>
/// It seeds a commune, a segment and <see cref="SyntheticPoleCount"/> synthetic poles, because the
/// FO-26 mock set cannot answer the question the task asks. 103 poles inside 1.6 km × 1.1 km is far
/// too little for PostgreSQL to prefer an index — it would pick a sequential scan because a scan
/// really is cheaper there, and the resulting EXPLAIN would prove nothing either way. The Contract's
/// bar is 2000 poles (section 5.4), so the fixture builds past it.
/// </para>
/// </summary>
public sealed class AssetSchemaFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Above the 2000 of Contract section 5.4, so the measurement clears the bar it is about.</summary>
    public const int SyntheticPoleCount = 2500;

    /// <summary>Spread over roughly 0.5° square — sparse enough that a bbox selects a small slice.</summary>
    private const string PoleSpreadSql = """
        INSERT INTO pole (segment_id, commune_id, geom, near_sensitive_poi, data_source)
        SELECT {0}, {1},
               ST_SetSRID(ST_MakePoint(106.20 + (i % 50) * 0.01, 10.70 + (i / 50) * 0.01), 4326),
               false, 'public_imagery'
        FROM generate_series(0, {2}) AS i;
        """;

    public string CommuneId { get; private set; } = null!;

    public string SegmentId { get; private set; } = null!;

    /// <summary>
    /// <c>pole_id_seq</c> as it stood before this run, so <see cref="DisposeAsync"/> can hand the
    /// values back.
    /// </summary>
    /// <remarks>
    /// Plain test hygiene, NOT a workaround for anything: the fixture burns
    /// <see cref="SyntheticPoleCount"/> sequence values per run against a SHARED development
    /// database and deletes every row it created, so keeping the values would let a handful of test
    /// runs push the sequence arbitrarily high for no reason.
    /// <para>
    /// ⚠️ It does NOT address the <c>LPAD</c> truncation defect in BE-06 — see
    /// <c>PrefixedIdOverflowTests</c>. That bug is about production reaching the 10000th pole and is
    /// still entirely live.
    /// </para>
    /// </remarks>
    private (long Value, bool IsCalled) poleSequence;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Production");

    public async Task InitializeAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        // Its own commune per run, so BE-06's seeded data and the other test collections are untouched.
        var commune = new AdministrativeUnit { Name = $"BE-09 test commune {Guid.NewGuid():N}"[..40] };
        db.Set<AdministrativeUnit>().Add(commune);
        await db.SaveChangesAsync();
        CommuneId = commune.CommuneId;

        poleSequence = await ReadPoleSequenceAsync(db);

        SegmentId = await ScalarAsync(db, """
            INSERT INTO road_segment (segment_name, road_class, length_m, geom, commune_id, data_source)
            VALUES ('BE-09 synthetic segment', 'inter_commune', 1000,
                    ST_GeomFromText('LINESTRING(106.20 10.70, 106.70 11.20)', 4326), @commune, 'public_imagery')
            RETURNING segment_id;
            """, ("commune", CommuneId));

        await ExecuteAsync(db,
            string.Format(PoleSpreadSql, "@segment", "@commune", SyntheticPoleCount - 1),
            ("segment", SegmentId), ("commune", CommuneId));

        // Without fresh statistics the planner works from defaults and its choice says nothing about
        // the index. ANALYZE is part of the measurement, not a workaround.
        await db.Database.ExecuteSqlRawAsync("ANALYZE pole;");
    }

    public new async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
            await ExecuteAsync(db, "DELETE FROM pole WHERE commune_id = @commune;", ("commune", CommuneId));
            await ExecuteAsync(db, "DELETE FROM feeder WHERE commune_id = @commune;", ("commune", CommuneId));
            await ExecuteAsync(db, "DELETE FROM road_segment WHERE commune_id = @commune;", ("commune", CommuneId));
            await ExecuteAsync(db, "DELETE FROM administrative_unit WHERE commune_id = @commune;", ("commune", CommuneId));

            // Every row this fixture created is gone by now, so nothing can collide with the values
            // being returned.
            await ExecuteAsync(
                db,
                "SELECT setval('pole_id_seq', @value, @called);",
                ("value", poleSequence.Value),
                ("called", poleSequence.IsCalled));
        }

        await base.DisposeAsync();
    }

    /// <summary>
    /// Runs <paramref name="query"/> against the real database.
    /// </summary>
    /// <remarks>
    /// There is no HTTP request here, so <c>ICommuneScopeAccessor</c> reports
    /// <see cref="LuxMap.Shared.Authorization.CommuneScope.Empty"/> and BE-08's query filters hide
    /// every row. Reading test data back therefore needs <c>IgnoreQueryFilters()</c> — and the fact
    /// that it does is itself the proof that the filters are attached.
    /// </remarks>
    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }

    /// <summary>Captures a real <c>EXPLAIN (ANALYZE)</c> plan as text, for the index assertions.</summary>
    public async Task<string> ExplainAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS) {sql}";
        AddParameters(command, parameters);

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<(long Value, bool IsCalled)> ReadPoleSequenceAsync(LuxMapDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_value, is_called FROM pole_id_seq;";

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetBoolean(1));
    }

    private static async Task<string> ScalarAsync(
        LuxMapDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(
        LuxMapDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameters(
        System.Data.Common.DbCommand command, (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}

[CollectionDefinition(nameof(AssetSchemaCollection))]
public sealed class AssetSchemaCollection : ICollectionFixture<AssetSchemaFixture>;
