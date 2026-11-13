using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Photos;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Http;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Point = NetTopologySuite.Geometries.Point;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetPhotoTests(AssetImportFixture factory, ITestOutputHelper output) : IAsyncLifetime
{
    private WebApplicationFactory<Program> host = null!;
    private HttpClient manager = null!;
    private HttpClient field = null!;
    private HttpClient foreign = null!;
    private readonly Store store = new();
    private readonly Probe probe = new();
    private string pole = null!;
    private string cabinet = null!;
    private string otherPole = null!;
    private readonly string marker = "asset-photo-" + Guid.NewGuid().ToString("N");
    private string PathFor(bool isPole) => $"/api/v1/assets/{(isPole ? "poles/" + pole : "cabinets/" + cabinet)}/photos";

    public async Task InitializeAsync()
    {
        host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IObjectStore>(store));
            s.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(probe));
        }));
        manager = host.CreateClient(); field = host.CreateClient(); foreign = host.CreateClient();
        using var m = await factory.ManagerClientAsync(); manager.DefaultRequestHeaders.Authorization = m.DefaultRequestHeaders.Authorization;
        using var f = await factory.FieldEngineerClientAsync(); field.DefaultRequestHeaders.Authorization = f.DefaultRequestHeaders.Authorization;
        using var x = await factory.ForeignCommuneClientAsync(); foreign.DefaultRequestHeaders.Authorization = x.DefaultRequestHeaders.Authorization;
        await factory.QueryAsync(async db =>
        {
            using var write = db.EnterUnscopedSystemWriteBackdoor();
            var p = new Pole { CommuneId = factory.CommuneId, DataSource = DataSource.Simulated, ExternalRef = marker };
            var o = new Pole { CommuneId = factory.ForeignCommuneId, DataSource = DataSource.Field, ExternalRef = marker };
            var c = new ElectricalCabinet { CommuneId = factory.CommuneId, DataSource = DataSource.CalibrationRig,
                CabinetName = marker, Geom = new Point(106.22, 10.72) { SRID = 4326 } };
            db.AddRange(p, o, c); await db.SaveChangesAsync();
            pole = p.PoleId; otherPole = o.PoleId; cabinet = c.CabinetId;
            return 0;
        });
    }

    public async Task DisposeAsync()
    {
        await factory.QueryAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM audit_event WHERE
                    (entity_type = 'pole_asset_photo' AND entity_id IN (SELECT photo_id FROM pole_asset_photo WHERE pole_id = {pole} OR pole_id = {otherPole})) OR
                    (entity_type = 'cabinet_asset_photo' AND entity_id IN (SELECT photo_id FROM cabinet_asset_photo WHERE cabinet_id = {cabinet}))
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole_asset_photo WHERE pole_id = {pole} OR pole_id = {otherPole}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cabinet_asset_photo WHERE cabinet_id = {cabinet}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE external_ref LIKE {marker + "%"}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM electrical_cabinet WHERE cabinet_name LIKE {marker + "%"}");
            await tx.CommitAsync(); return 0;
        });
        manager.Dispose(); field.Dispose(); foreign.Dispose(); await host.DisposeAsync();
    }

    private static byte[] Jpeg(byte shade = 90)
    {
        using var image = new Image<Rgb24>(500, 250, new Rgb24(shade, shade, shade));
        image.Metadata.ExifProfile = new SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifProfile();
        image.Metadata.ExifProfile.SetValue(SixLabors.ImageSharp.Metadata.Profiles.Exif.ExifTag.Artist, "private metadata");
        using var buffer = new MemoryStream(); image.SaveAsJpeg(buffer); return buffer.ToArray();
    }

    private Task<HttpResponseMessage> Upload(bool isPole, Guid? op = null, HttpClient? client = null, byte[]? bytes = null,
        string? extra = null, int files = 1, string? target = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent((op ?? Guid.NewGuid()).ToString()), "client_op_id");
        for (var i = 0; i < files; i++) form.Add(new ByteArrayContent(bytes ?? Jpeg()), "photo", "untrusted.png");
        if (extra is not null) form.Add(new StringContent("forged"), extra);
        return Send(form);
        async Task<HttpResponseMessage> Send(MultipartFormDataContent content)
        {
            using (content) return await (client ?? manager).PostAsync(target ?? PathFor(isPole), content);
        }
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response, int expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private async Task<JsonElement> List(bool isPole) => await Body(await manager.GetAsync(PathFor(isPole)), 200);
    private static async Task Error(HttpResponseMessage response, int status, string code)
        => Assert.Equal(code, (await Body(response, status)).GetProperty("error").GetProperty("code").GetString());

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Upload_replay_hide_and_retention_are_the_same_for_both_asset_kinds(bool isPole)
    {
        var op = Guid.NewGuid(); var bytes = Jpeg();
        var response = await Upload(isPole, op, field, bytes);
        var item = await Body(response, 201);
        Assert.Equal(PathFor(isPole), response.Headers.Location!.ToString());
        var id = item.GetProperty("photo_id").GetString()!;
        Assert.StartsWith(isPole ? "PAP-" : "CAP-", id);
        Assert.True(item.TryGetProperty(isPole ? "pole_id" : "cabinet_id", out _));
        Assert.False(item.TryGetProperty(isPole ? "cabinet_id" : "pole_id", out _));
        Assert.False(item.TryGetProperty("object_key", out _));
        Assert.Equal(id, (await Body(await Upload(isPole, op, field, bytes), 200)).GetProperty("photo_id").GetString());
        await Error(await Upload(isPole, op, field, Jpeg(10)), 409, "IDEMPOTENCY_CONFLICT");
        using var original = await manager.GetAsync(item.GetProperty("original_url").GetString());
        Assert.Equal(bytes, await original.Content.ReadAsByteArrayAsync()); Assert.True(original.Headers.CacheControl!.NoStore);
        using var thumb = await manager.GetAsync(item.GetProperty("thumbnail_url").GetString());
        Assert.True(thumb.Headers.CacheControl!.NoStore);
        var options = new SixLabors.ImageSharp.Formats.DecoderOptions { Configuration = ThumbnailFactory.JpegOnly };
        using var decoded = Image.Load(options, await thumb.Content.ReadAsByteArrayAsync());
        Assert.Equal(320, decoded.Width); Assert.Null(decoded.Metadata.ExifProfile);
        Assert.Null(decoded.Metadata.XmpProfile); Assert.Null(decoded.Metadata.IptcProfile);
        await Error(await foreign.GetAsync(PathFor(isPole)), 404, "ASSET_NOT_FOUND");
        await Error(await field.DeleteAsync(PathFor(isPole) + "/" + id), 403, "ROLE_FORBIDDEN");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(PathFor(isPole) + "/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(PathFor(isPole) + "/" + id)).StatusCode);
        Assert.Equal(0, (await List(isPole)).GetProperty("total").GetInt32());
        await Error(await manager.GetAsync(item.GetProperty("original_url").GetString()), 404, "ASSET_PHOTO_NOT_FOUND");
        await Error(await manager.GetAsync(item.GetProperty("thumbnail_url").GetString()), 404, "ASSET_PHOTO_NOT_FOUND");
        await Error(await Upload(isPole, op, field, bytes), 404, "ASSET_PHOTO_NOT_FOUND");
        await Error(await manager.DeleteAsync(PathFor(isPole)[..^7]), 409, "ASSET_IN_USE");
        Assert.Equal(2, store.Objects.Count);
        var table = isPole ? "pole_asset_photo" : "cabinet_asset_photo";
        var registered = factory.Services.GetServices<StoredObjectColumn>().Where(c => c.Table == table);
        var usage = new LuxMap.Modules.Admin.Monitoring.StorageUsage(factory.Services.GetRequiredService<NpgsqlDataSource>(), registered);
        var total = (await usage.TotalsAsync(default))[StorageBucket.Evidence];
        Assert.Equal(2, total.Objects);
        Assert.Equal(item.GetProperty("byte_count").GetInt64() + item.GetProperty("thumbnail_bytes").GetInt64(), total.Bytes);
        Assert.Contains(StorageKeys.KeyFor(ObjectVariant.Original, id), await usage.KeysAsync(StorageBucket.Evidence, default));
        var counts = await factory.QueryAsync(async db =>
        {
            var sql = $"SELECT count(*)::int AS \"Value\" FROM audit_event WHERE entity_type = '{table}' AND entity_id = @id";
            return await db.Database.SqlQueryRaw<int>(sql, new NpgsqlParameter("id", id)).SingleAsync();
        });
        Assert.Equal(2, counts); // Upload and first hide; replay and repeated hide stage nothing.
        var entityType = isPole ? AuditEntityType.PoleAssetPhoto : AuditEntityType.CabinetAssetPhoto;
        var states = await factory.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .Where(e => e.EntityId == id && e.EntityType == entityType).OrderBy(e => e.AuditId)
            .Select(e => new { e.BeforeState, e.AfterState, e.Action }).ToListAsync());
        Assert.Equal(new[] { AuditAction.Created, AuditAction.DetailsChanged }, states.Select(e => e.Action));
        var parentField = isPole ? "pole_id" : "cabinet_id";
        foreach (var state in states)
        {
            var after = JsonDocument.Parse(state.AfterState!).RootElement;
            Assert.Equal(isPole ? pole : cabinet, after.GetProperty(parentField).GetString());
            Assert.Equal(factory.CommuneId, after.GetProperty("commune_id").GetString());
            Assert.Equal(isPole ? "simulated" : "calibration_rig", after.GetProperty("data_source").GetString());
        }
        var beforeHide = JsonDocument.Parse(states[1].BeforeState!).RootElement;
        Assert.Equal(isPole ? pole : cabinet, beforeHide.GetProperty(parentField).GetString());
        Assert.Equal(JsonValueKind.Null, beforeHide.GetProperty("deleted_at").ValueKind);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Quota_serializes_the_tenth_photo_and_replay_precedes_the_quota(bool isPole)
    {
        var op = Guid.NewGuid();
        await Body(await Upload(isPole, op), 201);
        for (var i = 0; i < 8; i++) await Body(await Upload(isPole), 201);
        var responses = await Task.WhenAll(Upload(isPole), Upload(isPole));
        Assert.Equal(new[] { 201, 409 }, responses.Select(r => (int)r.StatusCode).Order().ToArray());
        await Error(responses.Single(r => (int)r.StatusCode == 409), 409, "ASSET_PHOTO_LIMIT_REACHED");
        await Body(await Upload(isPole, op), 200);
        Assert.Equal(10, (await List(isPole)).GetProperty("total").GetInt32());
        var first = (await List(isPole)).GetProperty("items")[0].GetProperty("photo_id").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(PathFor(isPole) + "/" + first)).StatusCode);
        await Body(await Upload(isPole), 201);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Service_rejects_magic_bytes_server_fields_empty_UUID_and_multiple_files(bool isPole)
    {
        await Error(await Upload(isPole, bytes: [0x89, 0x50, 0x4e, 0x47]), 415, "UNSUPPORTED_IMAGE_FORMAT");
        await Error(await Upload(isPole, extra: "commune_id"), 400, "SERVER_OWNED_FIELD");
        await Error(await Upload(isPole, extra: "data_source"), 400, "SERVER_OWNED_FIELD");
        await Error(await Upload(isPole, Guid.Empty), 400, "VALIDATION_FAILED");
        await Error(await Upload(isPole, files: 2), 400, "VALIDATION_FAILED");
        await Error(await Upload(isPole, bytes: new byte[16 * 1024 * 1024 + 1]), 413, "UPLOAD_TOO_LARGE");
        Assert.Equal(0, (await List(isPole)).GetProperty("total").GetInt32());
        Assert.Empty(store.Objects);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Summaries_gallery_and_source_snapshot_follow_visible_photos_only(bool isPole)
    {
        var first = await Body(await Upload(isPole), 201);
        await Body(await Upload(isPole), 201);
        var parentPath = PathFor(isPole)[..^7];
        var detail = await Body(await manager.GetAsync(parentPath), 200);
        Assert.Equal(2, detail.GetProperty("asset_photos").GetArrayLength());
        var row = detail.GetProperty(isPole ? "pole" : "cabinet");
        Assert.Equal(2, row.GetProperty("photo_count").GetInt32());
        Assert.Equal(first.GetProperty("photo_id").GetString(), row.GetProperty("cover_photo").GetProperty("photo_id").GetString());
        await manager.DeleteAsync(PathFor(isPole) + "/" + first.GetProperty("photo_id").GetString());
        var latest = await Body(await manager.GetAsync(parentPath), 200);
        Assert.Equal(1, latest.GetProperty("asset_photos").GetArrayLength());
        Assert.NotEqual(first.GetProperty("photo_id").GetString(), latest.GetProperty(isPole ? "pole" : "cabinet").GetProperty("cover_photo").GetProperty("photo_id").GetString());
        await factory.QueryAsync(async db =>
        {
            using var write = db.EnterUnscopedSystemWriteBackdoor();
            if (isPole)
            {
                var parent = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
                parent.DataSource = DataSource.CalibrationRig;
                await db.SaveChangesAsync();
                Assert.All(await db.Set<PoleAssetPhoto>().IgnoreQueryFilters().Where(p => p.PoleId == pole).ToListAsync(), p => Assert.Equal(DataSource.Simulated, p.DataSource));
            }
            else
            {
                var parent = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().SingleAsync(c => c.CabinetId == cabinet);
                parent.DataSource = DataSource.Simulated;
                await db.SaveChangesAsync();
                Assert.All(await db.Set<CabinetAssetPhoto>().IgnoreQueryFilters().Where(p => p.CabinetId == cabinet).ToListAsync(), p => Assert.Equal(DataSource.CalibrationRig, p.DataSource));
            }
            return 0;
        });
        if (isPole)
        {
            var map = await Body(await manager.GetAsync("/api/v1/map/poles/" + pole), 200);
            Assert.Equal(1, map.GetProperty("asset_photos").GetArrayLength()); Assert.True(map.TryGetProperty("recent_frames", out _));
        }
        else
        {
            var map = await Body(await manager.GetAsync("/api/v1/map/cabinets?bbox=106.20,10.70,106.25,10.75&data_source=simulated"), 200);
            var c = map.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("properties").GetProperty("cabinet_id").GetString() == cabinet).GetProperty("properties");
            Assert.Equal(1, c.GetProperty("photo_count").GetInt32());
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Database_checks_and_same_commune_FK_reject_invalid_rows(bool isPole)
    {
        var id = (await Body(await Upload(isPole), 201)).GetProperty("photo_id").GetString()!;
        var table = isPole ? "pole_asset_photo" : "cabinet_asset_photo";
        (string Set, string Constraint)[] invalid =
        [
            ("byte_count = 0", "sizes"), ("byte_count = 16777217", "sizes"),
            ("thumbnail_bytes = 0", "sizes"), ("width = 0", "sizes"), ("height = 0", "sizes"),
            ("width = 40000001, height = 1", "sizes"),
            ("object_key = 'bad'", "keys"), ("thumbnail_key = 'bad'", "keys"),
            ("request_hash = 'bad'", "hash"),
            ("client_op_id = '00000000-0000-0000-0000-000000000000'", "operation"),
            ("deleted_at = now()", "deleted"), ("deleted_by = uploaded_by", "deleted"),
            ("data_source = 'fake'", "data_source"),
        ];
        foreach (var (set, constraint) in invalid)
        {
            await factory.QueryAsync(async db =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var sql = $"UPDATE {table} SET {set} WHERE photo_id = @id";
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("id", id)));
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
                Assert.Equal($"ck_{table}_{constraint}", error.ConstraintName);
                await tx.RollbackAsync(); return 0;
            });
        }
        await factory.QueryAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var sql = $"UPDATE {table} SET commune_id = @commune WHERE photo_id = @id";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("commune", factory.ForeignCommuneId), new NpgsqlParameter("id", id)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            Assert.Equal(isPole ? "fk_pole_asset_photo_target" : "fk_cabinet_asset_photo_electrical_cabinet_cabinet_id_commune_id", error.ConstraintName);
            await tx.RollbackAsync(); return 0;
        });
    }

    private sealed class PhotoScope(string commune) : ICommuneScopeAccessor
    {
        public CommuneScope Scope => CommuneScope.ForCommunes([commune]);
    }
    private LuxMapDbContext Context(string commune)
    {
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, factory.Services.GetRequiredService<NpgsqlDataSource>());
        return new(options.Options, factory.Services.GetRequiredService<ModuleAssemblyCatalog>(), new PhotoScope(commune));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Photo_rows_have_independent_read_write_and_audit_guards(bool isPole)
    {
        var id = (await Body(await Upload(isPole), 201)).GetProperty("photo_id").GetString()!;
        await using (var other = Context(factory.ForeignCommuneId))
        {
            if (isPole) Assert.False(await other.Set<PoleAssetPhoto>().AnyAsync(p => p.PhotoId == id));
            else Assert.False(await other.Set<CabinetAssetPhoto>().AnyAsync(p => p.PhotoId == id));
        }
        await using var db = Context(factory.CommuneId);
        IAssetPhoto row = isPole
            ? await db.Set<PoleAssetPhoto>().SingleAsync(p => p.PhotoId == id)
            : await db.Set<CabinetAssetPhoto>().SingleAsync(p => p.PhotoId == id);
        row.RequestHash = new string('a', 64);
        var auditError = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("exactly one new audit event", auditError.Message);
        // Context outside the photo's commune, but the one staged audit is in its own scope.
        await using var forbidden = Context(factory.ForeignCommuneId);
        forbidden.Attach(row);
        forbidden.Entry(row).Property(nameof(IAssetPhoto.RequestHash)).IsModified = true;
        forbidden.Add(new AuditEvent
        {
            OccurredAt = DateTime.UtcNow, ActorKind = AuditActorKind.User, ActorUserId = row.UploadedBy,
            ActorRole = UserRole.Manager, CommuneId = factory.ForeignCommuneId,
            EntityType = isPole ? AuditEntityType.PoleAssetPhoto : AuditEntityType.CabinetAssetPhoto,
            EntityId = row.PhotoId, Action = AuditAction.DetailsChanged, AfterState = "{}", CorrelationId = marker,
        });
        var scopeError = await Assert.ThrowsAsync<LuxMapException>(() => forbidden.SaveChangesAsync());
        Assert.Equal(HttpStatusCode.Forbidden, scopeError.StatusCode);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task List_and_gallery_order_across_the_six_digit_boundary_in_one_transaction(bool isPole)
    {
        var old = new[] { (await Body(await Upload(isPole), 201)).GetProperty("photo_id").GetString()!,
            (await Body(await Upload(isPole), 201)).GetProperty("photo_id").GetString()! };
        var spec = isPole ? PrefixedIds.PoleAssetPhoto : PrefixedIds.CabinetAssetPhoto;
        var expected = new[] { spec.Format(999999), spec.Format(1000000) };
        await using var db = Context(factory.CommuneId);
        await using var tx = await db.Database.BeginTransactionAsync();
        var table = isPole ? "pole_asset_photo" : "cabinet_asset_photo";
        for (var i = 0; i < old.Length; i++)
        {
            var sql = $"UPDATE {table} SET photo_id = @next, object_key = 'original/' || @next || '.jpg', thumbnail_key = 'thumb/' || @next || '.jpg', created_at = now() WHERE photo_id = @old";
            await db.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("next", expected[i]), new NpgsqlParameter("old", old[i]));
        }
        var reader = new AssetPhotoReader(db);
        if (isPole) Assert.Equal(expected, (await reader.PolesAsync([pole], default))[pole].Select(p => p.PhotoId));
        else Assert.Equal(expected, (await reader.CabinetsAsync([cabinet], default))[cabinet].Select(p => p.PhotoId));
        await tx.RollbackAsync();
    }

    [Fact]
    public void Each_of_the_ten_actions_has_its_literal_policy()
    {
        foreach (var type in new[] { typeof(PoleAssetPhotosController), typeof(CabinetAssetPhotosController) })
        {
            foreach (var (method, policy) in new[] { ("Upload", "cap:upload_asset_photos"), ("List", "cap:read_network"),
                         ("Original", "cap:read_network"), ("Thumbnail", "cap:read_network"), ("Hide", "cap:manage_assets") })
            {
                Assert.Equal(policy, type.GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
                Assert.Null(type.GetMethod(method)!.GetCustomAttribute<AllowAnonymousAttribute>());
            }
            Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Listing_one_or_two_hundred_assets_uses_the_same_number_of_SQL_queries(bool isPole)
    {
        await factory.QueryAsync(async db =>
        {
            using var write = db.EnterUnscopedSystemWriteBackdoor();
            if (isPole) db.AddRange(Enumerable.Range(1, 199).Select(i => new Pole { CommuneId = factory.CommuneId,
                DataSource = DataSource.Simulated, ExternalRef = marker + i }));
            else db.AddRange(Enumerable.Range(1, 199).Select(i => new ElectricalCabinet { CommuneId = factory.CommuneId,
                DataSource = DataSource.CalibrationRig, CabinetName = marker + i, Geom = new Point(106.22, 10.72) { SRID = 4326 } }));
            await db.SaveChangesAsync(); return 0;
        });
        await Body(await Upload(isPole), 201);
        var path = isPole ? "/api/v1/assets/poles" : "/api/v1/assets/cabinets";
        probe.Commands.Clear();
        await Body(await manager.GetAsync(path + "?page_size=1"), 200);
        var one = probe.Commands.ToArray(); probe.Commands.Clear();
        var many = await Body(await manager.GetAsync(path + "?page_size=200"), 200);
        Assert.Equal(200, many.GetProperty("items").GetArrayLength());
        var twoHundred = probe.Commands.ToArray();
        Assert.Equal(one.Length, twoHundred.Length);
        Assert.Single(twoHundred, q => q.Contains(isPole ? "pole_asset_photo" : "cabinet_asset_photo"));
        output.WriteLine($"SQL reads {path}: page 1 = {one.Length}; page 200 = {twoHundred.Length}; gallery batch = 1");
        foreach (var sql in twoHundred) output.WriteLine(sql);
        if (!isPole)
        {
            probe.Commands.Clear();
            var map = await Body(await manager.GetAsync("/api/v1/map/cabinets?bbox=106.20,10.70,106.25,10.75&data_source=calibration_rig"), 200);
            Assert.Equal(200, map.GetProperty("features").EnumerateArray().Count(f => f.GetProperty("properties").GetProperty("cabinet_name").GetString()!.StartsWith(marker, StringComparison.Ordinal)));
            Assert.Equal(2, probe.Commands.Count);
            Assert.Single(probe.Commands, sql => sql.Contains("cabinet_asset_photo"));
            output.WriteLine($"SQL reads /api/v1/map/cabinets: 200 cabinets = {probe.Commands.Count}; gallery batch = 1");
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Upload_waits_for_the_parent_lock_without_storage_holding_that_lock(bool isPole)
    {
        await using var db = Context(factory.CommuneId);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (isPole) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM pole WHERE pole_id = {pole} FOR NO KEY UPDATE");
        else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM electrical_cabinet WHERE cabinet_id = {cabinet} FOR NO KEY UPDATE");
        probe.LockAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploading = Upload(isPole);
        await probe.LockAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEmpty(store.Objects); // Bytes were written before the blocked lock attempt.
        Assert.NotSame(uploading, await Task.WhenAny(uploading, Task.Delay(150)));
        await tx.RollbackAsync();
        await Body(await uploading, 201);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Overlapping_identical_replays_win_before_the_full_quota_check(bool isPole)
    {
        for (var i = 0; i < 9; i++) await Body(await Upload(isPole), 201);
        var entered = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.AfterStored = async () =>
        {
            if (Interlocked.Increment(ref entered) == 2) both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var op = Guid.NewGuid();
        var results = await Task.WhenAll(Upload(isPole, op), Upload(isPole, op));
        store.AfterStored = null;
        Assert.Equal(new[] { 200, 201 }, results.Select(r => (int)r.StatusCode).Order());
        var ids = await Task.WhenAll(results.Select(async r => (await Body(r, (int)r.StatusCode)).GetProperty("photo_id").GetString()));
        Assert.Single(ids.Distinct());
        Assert.Equal(10, (await List(isPole)).GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Request_cap_is_enforced_with_and_without_Content_Length(bool isPole)
    {
        foreach (var chunked in new[] { false, true })
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(Guid.NewGuid().ToString()), "client_op_id");
            form.Add(new ByteArrayContent(Jpeg()), "photo", "photo.jpg");
            form.Add(new StringContent(new string('a', (int)AssetPhotoService.MaxRequestBytes)), "extra");
            using var body = chunked ? new UnknownLengthContent(await form.ReadAsByteArrayAsync()) : null;
            if (body is not null) body.Headers.ContentType = form.Headers.ContentType;
            await Error(await manager.PostAsync(PathFor(isPole), body is null ? form : body), 413, "UPLOAD_TOO_LARGE");
        }
        Assert.Empty(store.Objects);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Pixel_cap_is_checked_before_decoding_or_storage(bool isPole)
    {
        var bytes = Jpeg(); var found = false;
        for (var at = 0; at < bytes.Length - 9; at++)
            if (bytes[at] == 255 && bytes[at + 1] is 192 or 194)
            { bytes[at + 5] = bytes[at + 6] = bytes[at + 7] = bytes[at + 8] = 255; found = true; break; }
        Assert.True(found);
        await Error(await Upload(isPole, bytes: bytes), 400, "VALIDATION_FAILED");
        Assert.Empty(store.Objects);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    [Fact]
    public async Task UUID_reused_after_scope_narrows_is_a_conflict_without_reading_the_foreign_photo()
    {
        using var broadToken = await factory.LegacyTwoCommuneClientAsync();
        using var legacy = host.CreateClient(); legacy.DefaultRequestHeaders.Authorization = broadToken.DefaultRequestHeaders.Authorization;
        var op = Guid.NewGuid();
        var foreignPath = $"/api/v1/assets/poles/{otherPole}/photos";
        var original = await Body(await Upload(true, op, legacy, target: foreignPath), 201);
        await Error(await manager.GetAsync(original.GetProperty("original_url").GetString()), 404, "ASSET_NOT_FOUND");
        await Error(await Upload(true, op), 409, "IDEMPOTENCY_CONFLICT");
        Assert.Equal(0, (await List(true)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Replay_namespace_is_per_asset_kind_and_photo_lookup_is_bound_to_its_parent()
    {
        var op = Guid.NewGuid();
        var item = await Body(await Upload(true, op), 201); await Body(await Upload(false, op), 201);
        using var broadToken = await factory.LegacyTwoCommuneClientAsync();
        using var legacy = host.CreateClient(); legacy.DefaultRequestHeaders.Authorization = broadToken.DefaultRequestHeaders.Authorization;
        var wrongParent = $"/api/v1/assets/poles/{otherPole}/photos/" + item.GetProperty("photo_id").GetString();
        await Error(await legacy.GetAsync(wrongParent + "/original"), 404, "ASSET_PHOTO_NOT_FOUND");
        await Error(await legacy.DeleteAsync(wrongParent), 404, "ASSET_PHOTO_NOT_FOUND");
        Assert.Equal(1, (await List(true)).GetProperty("total").GetInt32());
    }

    private sealed class Probe : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public TaskCompletionSource? LockAttempt;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)) Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) LockAttempt?.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Store : IObjectStore
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public Func<Task>? AfterStored;
        public async Task<StoredImage> StoreImageAsync(StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default)
        {
            Assert.Equal(StorageBucket.Evidence, bucket);
            var image = await new OriginalJpegReader().ReadAsync(content, AssetPhotoService.MaxUploadBytes, AssetPhotoService.MaxPixels, cancellationToken);
            var original = StorageKeys.KeyFor(ObjectVariant.Original, id); var thumb = StorageKeys.KeyFor(ObjectVariant.Thumbnail, id);
            Objects[original] = image.Original; Objects[thumb] = image.Thumbnail;
            if (AfterStored is { } after) await after();
            return new(bucket, original, image.Original.Length, thumb, image.Thumbnail.Length);
        }
        public Task<Stream> OpenAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(Objects[key], false));
        public Task<bool> ExistsAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default) => Task.FromResult(Objects.ContainsKey(key));
        public Task<StoredObject> StoreStreamAsync(StorageBucket bucket, string key, Stream content, StreamUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenRangeAsync(StorageBucket bucket, string key, long from, long to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
