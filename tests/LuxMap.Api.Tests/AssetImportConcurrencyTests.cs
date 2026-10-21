using System.Data.Common;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetImportConcurrencyTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Concurrent_imports_of_the_same_file_insert_each_reference_once_and_both_answer_200()
    {
        var gate = new ImportLookupGate();
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(gate))));
        using var manager = await fixture.ManagerClientAsync();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = manager.DefaultRequestHeaders.Authorization;

        var tag = $"RACE-{Guid.NewGuid():N}";
        string[] references = [$"{tag}-1", $"{tag}-2"];
        var file = "external_ref,feeder_name,commune_id"
            + $"\n{references[0]},First circuit,{fixture.CommuneId}"
            + $"\n{references[1]},Second circuit,{fixture.CommuneId}";

        Task<JsonElement> Import() => AssetImportTests.ImportAsync(client, "feeders", "feeders.csv", file);

        async Task<JsonElement> OverlappingImport()
        {
            try
            {
                // The first SELECT has returned an empty result from PostgreSQL, but its planner cannot proceed yet.
                await gate.FirstLookup.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var second = Import();
                try
                {
                    // With the lock, this is the second lock attempt. Without it, wait until the second SELECT
                    // has ALSO returned empty. Releasing both then deterministically reproduces the unique violation.
                    await gate.SecondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                finally
                {
                    gate.Release();
                }

                return await second;
            }
            finally
            {
                gate.Release();
            }
        }

        // ImportAsync asserts HTTP 200 for BOTH responses, including the losing writer if the lock is removed.
        var results = await Task.WhenAll(Import(), OverlappingImport());
        Assert.All(results, result => Assert.Equal(0, result.GetProperty("failed").GetInt32()));
        Assert.Equal(references.Length, results.Sum(result => result.GetProperty("inserted").GetInt32()));
        Assert.Equal(references.Length, results.Sum(result => result.GetProperty("unchanged").GetInt32()));
        Assert.All(results, result => Assert.Equal(0, result.GetProperty("updated").GetInt32()));

        var rows = await fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
            .Where(feeder => feeder.CommuneId == fixture.CommuneId && references.Contains(feeder.ExternalRef!))
            .ToListAsync());
        Assert.Equal(references.Length, rows.Count);
        Assert.All(references, reference => Assert.Single(rows, row => row.ExternalRef == reference));

        // The unchanged request returned without committing: a fresh request must still acquire the same lock.
        var replay = await Import().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, replay.GetProperty("inserted").GetInt32());
        Assert.Equal(0, replay.GetProperty("updated").GetInt32());
        Assert.Equal(0, replay.GetProperty("failed").GetInt32());
        Assert.Equal(references.Length, replay.GetProperty("unchanged").GetInt32());
    }

    /// <summary>Only this derived test host pauses SQL; production has no timing hook.</summary>
    private sealed class ImportLookupGate : DbCommandInterceptor
    {
        public TaskCompletionSource FirstLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int locks;
        private int lookups;

        public void Release() => released.TrySetResult();

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)
                && Interlocked.Increment(ref locks) == 2)
            {
                SecondAttempt.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM feeder AS", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref lookups) == 1)
                {
                    Assert.False(result.HasRows);
                    FirstLookup.TrySetResult();
                }
                else
                {
                    SecondAttempt.TrySetResult();
                }

                await released.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }
    }
}
