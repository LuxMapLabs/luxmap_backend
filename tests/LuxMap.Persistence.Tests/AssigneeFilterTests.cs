using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Persistence.Tests;

public class AssigneeFilterTests
{
    [Fact]
    public void Assignee_scope_without_commune_scope_refuses_model_startup()
    {
        using var db = new InvalidContext();
        var error = Assert.Throws<InvalidOperationException>(() => db.Model);
        Assert.Contains("IAssigneeScoped requires ICommuneScoped", error.Message);
    }

    private sealed class AssigneeOnly : IAssigneeScoped
    {
        public int Id { get; set; }
        public string? AssignedTo { get; set; }
    }

    private sealed class ScopeAccessor : ICommuneScopeAccessor
    {
        public CommuneScope Scope => CommuneScope.Empty;
    }

    private sealed class InvalidContext() : LuxMapDbContext(
        new DbContextOptionsBuilder<LuxMapDbContext>().UseNpgsql("Host=localhost;Database=unused", x => x.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention().Options, new ModuleAssemblyCatalog([]), new ScopeAccessor())
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<AssigneeOnly>().HasKey(x => x.Id);
            base.OnModelCreating(builder);
        }
    }
}
