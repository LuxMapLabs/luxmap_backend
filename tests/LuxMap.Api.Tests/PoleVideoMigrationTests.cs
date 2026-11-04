using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

/// <summary>POLE-VIDEO PV-a: the clip columns on <c>work_order_pole_photo</c> (D-1 option C) and the <c>viewed</c> audit action (D-13).</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleVideoMigrationTests(AssetImportFixture fixture)
{
    private const string Migration = "20261104080559_AddPoleVideoColumns";
    private const string Previous = "20261102092632_PoleSegmentNullable";

    private static readonly string[] VideoColumns =
    [
        "video_object_key", "video_sha256", "video_bytes", "video_duration_ms", "video_sampled_frames", "video_on_frames",
        "video_off_frames", "video_daylight_frames", "video_confidence_min", "video_confidence_median", "video_confidence_max",
        "video_median_luma", "video_hdr", "video_extractor_version_id", "video_reason_code", "video_viewed_by", "video_viewed_at",
    ];

    private async Task<(PostgreSqlContainer Container, NpgsqlDataSource Source, LuxMapDbContext Db)> Start(string name)
    {
        var container = new PostgreSqlBuilder(TestDatabase.Image).WithDatabase(name).WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        await db.GetService<IMigrator>().MigrateAsync();
        return (container, source, db);
    }

    private static Task<T> Scalar<T>(LuxMapDbContext db, FormattableString sql) where T : notnull
        => db.Database.SqlQuery<T>(sql).SingleAsync();

    // Every foreign key of the row is switched off (replica role) so a CHECK can be tried without building a work order, pole and
    // users around it; the CHECK constraints themselves still run. The container user is a superuser, which that setting needs.
    private static async Task Insert(NpgsqlDataSource source, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var values = new Dictionary<string, string>
        {
            ["work_order_id"] = "'WO-T'", ["pole_id"] = "'POLE-T'", ["commune_id"] = "'COM-T'", ["data_source"] = "'field'",
            ["uploaded_by"] = "'USR-T'", ["uploaded_at"] = "now()", ["captured_at"] = "now()", ["lat"] = "16", ["lng"] = "108",
            ["client_op_id"] = "gen_random_uuid()", ["request_hash"] = $"'{new string('a', 64)}'", ["object_key"] = "'original/PPH-T.jpg'",
            ["thumbnail_key"] = "'thumb/PPH-T.jpg'", ["byte_count"] = "1000", ["thumbnail_bytes"] = "100", ["width"] = "1080",
            ["height"] = "1920", ["status_applied"] = "false",
        };
        foreach (var (column, value) in overrides ?? new Dictionary<string, string>()) values[column] = value;
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var relax = new NpgsqlCommand("SET LOCAL session_replication_role = replica", connection, transaction))
            await relax.ExecuteNonQueryAsync();
        var sql = $"INSERT INTO work_order_pole_photo ({string.Join(", ", values.Keys)}) VALUES ({string.Join(", ", values.Values)})";
        await using (var insert = new NpgsqlCommand(sql, connection, transaction)) await insert.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static Dictionary<string, string> Video(params (string Column, string Value)[] changes)
    {
        var video = new Dictionary<string, string>
        {
            ["video_object_key"] = "'pole-videos/WO-T/op/clip.mp4'", ["video_sha256"] = $"'{new string('b', 64)}'", ["video_bytes"] = "1000",
            ["video_duration_ms"] = "5000", ["video_sampled_frames"] = "10", ["video_on_frames"] = "7", ["video_off_frames"] = "0",
            ["video_daylight_frames"] = "1", ["video_median_luma"] = "0.3", ["video_hdr"] = "true", ["video_extractor_version_id"] = "1",
            ["video_confidence_min"] = "0.6", ["video_confidence_median"] = "0.8", ["video_confidence_max"] = "0.9",
        };
        foreach (var (column, value) in changes) video[column] = value;
        return video;
    }

    private static async Task Violates(NpgsqlDataSource source, string constraint, Dictionary<string, string> overrides)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(source, overrides));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    [Fact]
    public async Task Clip_columns_apply_roll_back_and_reapply_and_a_lossy_down_is_refused()
    {
        var (container, source, db) = await Start("pole_video_migration_test");
        await using var ownedContainer = container; await using var ownedSource = source; await using var ownedDb = db;
        var migrator = db.GetService<IMigrator>();
        Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());

        Assert.Equal(VideoColumns.Length, await Scalar<int>(db, $"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_name = 'work_order_pole_photo' AND column_name = ANY({VideoColumns}) AND is_nullable = 'YES'
            """));
        foreach (var name in new[] { "ck_pole_photo_video_complete", "ck_pole_photo_video_values", "ck_pole_photo_video_confidence",
            "ck_pole_photo_video_reason", "ck_pole_photo_video_viewed", "fk_pole_photo_video_extractor_version", "fk_pole_photo_video_viewed_by" })
            Assert.Equal(1, await Scalar<int>(db, $"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = {name}"));
        foreach (var name in new[] { "fk_pole_photo_video_extractor_version", "fk_pole_photo_video_viewed_by" })
            Assert.Equal("r", await Scalar<string>(db, $"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conname = {name}"));
        foreach (var name in new[] { "ix_pole_photo_video_extractor_version", "ix_pole_photo_video_viewed_by" })
            Assert.Equal(1, await Scalar<int>(db, $"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = {name} AND indexdef NOT LIKE '%WHERE%'"));
        Assert.Contains("'viewed'", await Scalar<string>(db, $"SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = 'ck_audit_event_action'"));

        // Without any clip data the migration rolls back and applies again.
        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await Scalar<int>(db, $"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'work_order_pole_photo' AND column_name LIKE 'video\\_%'"));
        Assert.DoesNotContain("'viewed'", await Scalar<string>(db, $"SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = 'ck_audit_event_action'"));
        await migrator.MigrateAsync();

        // Rolling back would drop the clip's analysis for good: refused, and nothing is half-applied.
        await Insert(source, Video());
        var refused = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Contains("Cannot roll back AddPoleVideoColumns", refused.MessageText);
        Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await Scalar<int>(db, $"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'ck_pole_photo_video_values'"));
        await using (var clean = source.CreateCommand("DELETE FROM work_order_pole_photo")) await clean.ExecuteNonQueryAsync();
        await migrator.MigrateAsync(Previous);
        await migrator.MigrateAsync();
    }

    [Fact]
    public async Task Clip_columns_are_all_or_none_and_hold_their_invariants()
    {
        var (container, source, db) = await Start("pole_video_check_test");
        await using var ownedContainer = container; await using var ownedSource = source; await using var ownedDb = db;

        // Rows that must be accepted: a photo with no clip (the shape every existing row has), a clip with a verdict, a clip that
        // gave no verdict (no frame voted, so no confidence spread), and a clip that has been opened.
        await Insert(source);
        await Insert(source, Video());
        await Insert(source, Video(("video_on_frames", "0"), ("video_off_frames", "0"), ("video_confidence_min", "NULL"),
            ("video_confidence_median", "NULL"), ("video_confidence_max", "NULL"), ("video_reason_code", "'NO_LAMP'")));
        await Insert(source, Video(("video_viewed_by", "'USR-T'"), ("video_viewed_at", "now()")));

        // All-or-none: one clip column alone, or one missing.
        await Violates(source, "ck_pole_photo_video_complete", new() { ["video_object_key"] = "'k'" });
        var missing = Video(); missing.Remove("video_hdr"); missing["video_hdr"] = "NULL";
        await Violates(source, "ck_pole_photo_video_complete", missing);

        // Values: hash shape, positive size and duration, frame counts that add up, luma in range.
        await Violates(source, "ck_pole_photo_video_values", Video(("video_sha256", $"'{new string('B', 64)}'")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_sha256", "'abc'")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_object_key", "'  '")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_bytes", "0")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_duration_ms", "0")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_sampled_frames", "0")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_on_frames", "10"), ("video_daylight_frames", "1")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_off_frames", "-1")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_median_luma", "1.01")));
        await Violates(source, "ck_pole_photo_video_values", Video(("video_median_luma", "'NaN'::float8")));

        // Confidence spread: present exactly when a frame voted, ordered, in 0..1 and finite.
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_confidence_min", "NULL"), ("video_confidence_median", "NULL"), ("video_confidence_max", "NULL")));
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_on_frames", "0"), ("video_off_frames", "0")));
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_confidence_min", "NULL")));
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_confidence_min", "0.85")));
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_confidence_max", "0.7")));
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_confidence_max", "1.2")));
        await Violates(source, "ck_pole_photo_video_confidence", Video(("video_confidence_median", "'NaN'::float8")));
        await Violates(source, "ck_pole_photo_video_confidence", new() { ["video_confidence_min"] = "0.5", ["video_confidence_median"] = "0.5", ["video_confidence_max"] = "0.5" });

        // Reason code: only on a clip, never blank.
        await Violates(source, "ck_pole_photo_video_reason", new() { ["video_reason_code"] = "'NO_LAMP'" });
        await Violates(source, "ck_pole_photo_video_reason", Video(("video_reason_code", "' '")));

        // Opened: who and when together, and only a row that has a clip.
        await Violates(source, "ck_pole_photo_video_viewed", Video(("video_viewed_by", "'USR-T'")));
        await Violates(source, "ck_pole_photo_video_viewed", Video(("video_viewed_at", "now()")));
        await Violates(source, "ck_pole_photo_video_viewed", new() { ["video_viewed_by"] = "'USR-T'", ["video_viewed_at"] = "now()" });
    }
}
