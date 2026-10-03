using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LuxMap.Persistence.Tests;

public class AuditGuardTests
{
    [Theory]
    [InlineData(EntityState.Added, 0)]
    [InlineData(EntityState.Modified, 0)]
    [InlineData(EntityState.Deleted, 0)]
    [InlineData(EntityState.Added, 2)]
    [InlineData(EntityState.Modified, 2)]
    [InlineData(EntityState.Deleted, 2)]
    public async Task An_audited_write_requires_exactly_one_event(EntityState state, int events)
    {
        using var db = Create();
        db.Entry(new Probe { Id = 1 }).State = state;
        for (var i = 0; i < events; i++) db.Add(Event());

        var sync = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("exactly one", sync.Message, StringComparison.Ordinal);
        var asyncError = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("exactly one", asyncError.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EntityState.Modified, false)]
    [InlineData(EntityState.Modified, true)]
    [InlineData(EntityState.Deleted, false)]
    [InlineData(EntityState.Deleted, true)]
    public async Task Audit_mutation_is_refused_even_in_the_system_backdoor(EntityState state, bool backdoor)
    {
        using var db = Create();
        using var bypass = backdoor ? db.EnterUnscopedSystemWriteBackdoor() : null;
        var audit = Event();
        audit.AuditId = 1;
        db.Entry(audit).State = state;

        Assert.Contains("cannot be updated or deleted",
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges()).Message, StringComparison.Ordinal);
        Assert.Contains("cannot be updated or deleted",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task One_event_reaches_the_save_pipeline(EntityState state)
    {
        using var db = Create();
        db.Entry(new Probe { Id = 1 }).State = state;
        db.Add(Event());
        Assert.Equal(0, db.SaveChanges());
        Assert.Equal(0, await db.SaveChangesAsync());
    }

    [Fact]
    public async Task Fixture_backdoor_may_create_business_data_without_an_event()
    {
        using var db = Create();
        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            db.Add(new Probe { Id = 1 });
            Assert.Equal(0, await db.SaveChangesAsync());
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Production_source_never_enables_the_test_purge_switch()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LuxMap.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        // Scan every file, regardless of extension, so new config/text formats cannot evade this guard.
        var files = Directory.GetFiles(Path.Combine(root.FullName, "src"), "*", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            // Migrations whose immutable triggers use the switch. Each is listed here by name.
            .Where(file => Path.GetFileName(file) is not ("20260927161055_AddAuditEvent.cs" or "20261002095348_AddSurveyProcessing.cs" or "20261003071658_AddSurveyFrames.cs"))
            .Concat([Path.Combine(root.FullName, "docker-compose.yml"), Path.Combine(root.FullName, ".env.example")])
            .Concat(Directory.GetFiles(Path.Combine(root.FullName, ".github", "workflows"), "*", SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(files);
        Assert.DoesNotContain(files, file => File.ReadAllText(file).Contains("luxmap.audit_purge", StringComparison.Ordinal));
    }

    private static AuditEvent Event() => new()
    {
        CommuneId = "COM-001", EntityId = "WO-0001", CorrelationId = "audit-guard-test", AfterState = "{}",
    };

    private static ProbeContext Create()
    {
        var options = new DbContextOptionsBuilder<LuxMapDbContext>()
            .UseNpgsql("Host=localhost;Database=unused", npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new StopBeforeDatabase())
            .Options;
        return new ProbeContext(options);
    }

    private sealed class ScopeAccessor : ICommuneScopeAccessor
    {
        public CommuneScope Scope => CommuneScope.SystemWide;
    }

    private sealed class ProbeContext(DbContextOptions<LuxMapDbContext> options)
        : LuxMapDbContext(options, new ModuleAssemblyCatalog([]), new ScopeAccessor())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Probe>().HasKey(probe => probe.Id);
        }
    }

    private sealed class Probe : IAudited
    {
        public int Id { get; set; }
    }

    // The test observes whether the real DbContext guard lets a write reach EF's pipeline.
    // No connection or database schema is needed for these guard-only tests.
    private sealed class StopBeforeDatabase : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => InterceptionResult<int>.SuppressWithResult(0);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }
}
