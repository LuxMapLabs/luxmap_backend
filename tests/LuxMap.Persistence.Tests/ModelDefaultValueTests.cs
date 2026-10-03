using LuxMap.Modules.Assets;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Identity;
using LuxMap.Modules.Survey;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LuxMap.Persistence.Tests;

/// <summary>
/// EF omits a value-type column from INSERT when its value equals the property's sentinel (the CLR
/// default unless configured), and the database default then fills it in. A DB default that differs from
/// the CLR default therefore silently rewrites a legitimate value: found in BE-15 P2b-2, where
/// <c>classified_as</c> defaulted to <c>unknown</c> and <c>FixtureStatus.Normal</c> is enum value 0, so every
/// normal lamp was stored as unknown — no exception, no log.
/// </summary>
public sealed class ModelDefaultValueTests
{
    private sealed class NoScope : ICommuneScopeAccessor
    {
        public CommuneScope Scope => CommuneScope.Empty;
    }

    [Fact]
    public void Value_columns_never_carry_a_database_default_EF_could_mistake_for_unset()
    {
        using var db = new LuxMapDbContext(new DbContextOptionsBuilder<LuxMapDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=unused", x => x.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention().Options,
            new ModuleAssemblyCatalog([typeof(SurveyModule).Assembly, typeof(AssetsModule).Assembly,
                typeof(IdentityModule).Assembly, typeof(WorkOrdersModule).Assembly, typeof(FaultsModule).Assembly,
                typeof(TelemetryModule).Assembly]), new NoScope());
        var model = db.GetService<IDesignTimeModel>().Model;

        var traps = model.GetEntityTypes().SelectMany(entity => entity.GetProperties()
            .Where(p => p.ClrType.IsValueType && Nullable.GetUnderlyingType(p.ClrType) is null)
            .Where(p => p.GetDefaultValue() is { } value
                && !Equals(value, Activator.CreateInstance(p.ClrType))
                && Equals(p.Sentinel, Activator.CreateInstance(p.ClrType)))
            .Select(p => $"{entity.GetTableName()}.{p.GetColumnName()} defaults to {p.GetDefaultValue()}"))
            .ToArray();

        Assert.True(traps.Length == 0, "A non-default DB default on a value column: " + string.Join("; ", traps));
    }
}
