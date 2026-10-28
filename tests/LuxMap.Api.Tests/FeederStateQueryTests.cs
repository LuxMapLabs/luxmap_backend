using System.Data.Common;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class FeederStateQueryTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task A_page_of_200_uses_the_same_number_of_queries_as_one_feeder()
    {
        await FeederStateTests.Plant(fixture, 200, "on");
        var probe = new QueryProbe();
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(probe))));
        var client = host.CreateClient();
        var tokens = await (await client.PostLoginAsync(fixture.ManagerUsername, fixture.AccountPassword)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);

        probe.Enabled = true;
        await FeederStateTests.Json(client, "/api/v1/assets/feeders?page_size=1");
        var one = probe.Commands.ToArray();
        probe.Commands.Clear();
        await FeederStateTests.Json(client, "/api/v1/assets/feeders?page_size=200");
        var many = probe.Commands.ToArray();
        probe.Enabled = false;
        output.WriteLine($"Inventory page 1: {one.Length} SQL; page 200: {many.Length} SQL");
        Assert.Equal(one.Length, many.Length);
        Assert.Single(one, c => c.Sql.Contains("FROM feeder_control", StringComparison.Ordinal));
        Assert.Single(many, c => c.Sql.Contains("FROM feeder_control", StringComparison.Ordinal));
        Assert.Contains("JOIN", many.Single(c => c.Sql.Contains("FROM feeder_control", StringComparison.Ordinal)).Sql);
    }

    [Fact]
    public async Task The_actual_cabinet_response_query_can_use_the_4326_gist_index()
    {
        await FeederStateTests.Plant(fixture, 2, "on");
        var probe = new QueryProbe();
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(probe))));
        var client = host.CreateClient();
        var tokens = await (await client.PostLoginAsync(fixture.ManagerUsername, fixture.AccountPassword)).ReadTokensAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        probe.Enabled = true;
        await FeederStateTests.Json(client, FeederStateTests.MapUrl);
        probe.Enabled = false;
        var captured = Assert.Single(probe.Commands, c => c.Sql.Contains("ST_Intersects", StringComparison.Ordinal));
        Assert.Contains("LEFT JOIN", captured.Sql);
        Assert.DoesNotContain("ST_Transform", captured.Sql);
        var plan = await fixture.QueryAsync(async db =>
        {
            await db.Database.OpenConnectionAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "EXPLAIN " + captured.Sql;
            foreach (var parameter in captured.Parameters)
            {
                var copy = parameter.Clone();
                // On this small table the commune index is cheaper. System-wide EXPLAIN tests whether GIST is usable.
                if (copy.ParameterName.Contains("IsSystemWide", StringComparison.Ordinal)) copy.Value = true;
                command.Parameters.Add(copy);
            }
            await using var reader = await command.ExecuteReaderAsync();
            var lines = new List<string>();
            while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
            return string.Join(Environment.NewLine, lines);
        });
        output.WriteLine(captured.Sql);
        output.WriteLine(plan);
        Assert.Contains("ix_electrical_cabinet_geom", plan);
    }

    private sealed class QueryProbe : DbCommandInterceptor
    {
        public bool Enabled;
        public List<(string Sql, NpgsqlParameter[] Parameters)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled) Commands.Add((command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToArray()));
            return ValueTask.FromResult(result);
        }
    }
}
