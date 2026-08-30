using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// Implements the Contract's ID convention (section 0.4) once for all 16 entities that carry a
/// display ID.
/// <para>
/// Use it inside any entity's <c>IEntityTypeConfiguration</c>:
/// <code>
/// builder.Property(p => p.PoleId).HasPrefixedId(PrefixedIds.Pole);
/// </code>
/// The matching sequence needs NO separate declaration — <see cref="LuxMapDbContext"/> scans the
/// model and creates a sequence for every marked column, so nobody forgets and the migration is
/// always complete.
/// </para>
/// </summary>
public static class PrefixedIdBuilderExtensions
{
    /// <summary>Marks the column so the DbContext knows which sequence it needs.</summary>
    internal const string SequenceAnnotation = "LuxMap:PrefixedIdSequence";

    public static PropertyBuilder<string> HasPrefixedId(
        this PropertyBuilder<string> property,
        PrefixedIdSpec spec)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(spec);

        return property
            .HasColumnType("text")
            // The database generates the ID; clients never assign one (Contract section 0.4).
            .HasDefaultValueSql(spec.DefaultValueSql)
            .ValueGeneratedOnAdd()
            .HasAnnotation(SequenceAnnotation, spec.SequenceName);
    }

    /// <summary>
    /// Creates a sequence for every column marked by <see cref="HasPrefixedId"/>. Call at the end of
    /// <c>OnModelCreating</c>, after every module configuration has been applied.
    /// </summary>
    internal static void CreatePrefixedIdSequences(this ModelBuilder modelBuilder)
    {
        var sequences = modelBuilder.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.FindAnnotation(SequenceAnnotation)?.Value as string)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal);

        foreach (var name in sequences)
        {
            // bigint: Contract section 0.3 says IDs simply grow past the padding width — there is
            // no overflow case to design around.
            modelBuilder.HasSequence<long>(name!).StartsAt(1).IncrementsBy(1);
        }
    }
}
