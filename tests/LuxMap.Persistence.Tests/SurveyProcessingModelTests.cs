using LuxMap.Modules.Assets;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Identity;
using LuxMap.Modules.Survey;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LuxMap.Persistence.Tests;

public sealed class SurveyProcessingModelTests
{
    private sealed class JobScopeAccessor : ICommuneScopeAccessor
    {
        public CommuneScope Scope { get; } = CommuneScope.ForCommunes(["COM-001"]);
    }
    private static LuxMapDbContext Create() => new(new DbContextOptionsBuilder<LuxMapDbContext>()
        .UseNpgsql("Host=localhost;Port=1;Database=unused", x => x.UseNetTopologySuite())
        .UseSnakeCaseNamingConvention().AddInterceptors(new Stop()).Options,
        new ModuleAssemblyCatalog([typeof(SurveyModule).Assembly, typeof(AssetsModule).Assembly,
            typeof(IdentityModule).Assembly, typeof(WorkOrdersModule).Assembly, typeof(FaultsModule).Assembly]), new JobScopeAccessor());

    [Fact]
    public void New_schema_has_restrict_fks_finite_checks_and_no_physical_xmin()
    {
        using var db = Create();
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(ArtifactVersion), typeof(SurveyProcessingRun), typeof(SurveyPass), typeof(PoleObservation), typeof(SurveyFrame), typeof(Detection) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
            Assert.DoesNotContain(entity.GetProperties(), p => p.GetColumnName() == "xmin");
            foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(double) || p.ClrType == typeof(double?)))
                Assert.Contains(entity.GetCheckConstraints(), c => c.Sql.Contains(property.GetColumnName(), StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(EntityState.Modified)] [InlineData(EntityState.Deleted)]
    public async Task Immutable_result_is_refused_before_database(EntityState state)
    {
        using var db = Create();
        db.Entry(new ArtifactVersion { VersionId = 1, Component = "clock_algorithm", Version = "v1", ArtifactHash = new('a', 64) }).State = state;
        Assert.Contains("cannot be changed", Assert.Throws<InvalidOperationException>(() => db.SaveChanges()).Message);
        Assert.Contains("cannot be changed", (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
    }

    [Fact]
    public async Task Finite_job_scope_blocks_foreign_observation_before_database()
    {
        using var db = Create();
        db.Add(new PoleObservation { PoleId = "POLE-0001", CommuneId = "COM-002", QualityFlags = "[]" });
        var ex = await Assert.ThrowsAsync<LuxMapException>(() => db.SaveChangesAsync());
        Assert.Equal("COMMUNE_FORBIDDEN", ex.Code);
    }

    private sealed class Stop : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => InterceptionResult<int>.SuppressWithResult(0);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }
}
