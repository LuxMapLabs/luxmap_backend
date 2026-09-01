using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// The foreign key from any <c>commune_id</c> column to <see cref="AdministrativeUnit"/>.
/// <para>
/// Use it in the <c>IEntityTypeConfiguration</c> of every entity carrying <c>commune_id</c>:
/// <code>
/// builder.HasCommuneReference(pole => pole.CommuneId);
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// <b>Why the constraint is not fussiness.</b> An orphaned <c>commune_id</c> raises no error: the
/// section 7 filter lives in the <c>WHERE</c> clause, so the row simply becomes invisible to every
/// user at once. No exception, no log line, just data that is gone — the exact class of silent drift
/// BE-08 and BE-09 went to some trouble to rule out elsewhere. One typo in a BE-12 CSV import is
/// enough.
/// <para>
/// <b>Deliberately no navigation property.</b> A <c>Commune</c> navigation would let
/// <c>pole.Commune.Name</c> spread through the modules, and coupling between modules is meant to stay
/// at the level of an ID string. The database gets full integrity; the object model gets none of the
/// entanglement.
/// </para>
/// <para>
/// <c>Restrict</c>, never cascade: deleting an administrative unit must never take assets, faults or
/// work orders with it.
/// </para>
/// </remarks>
public static class CommuneReferenceBuilderExtensions
{
    /// <summary>The table name, in one place, so the guard and the configurations cannot disagree.</summary>
    public const string CommuneTable = "administrative_unit";

    /// <summary>The column every scoped entity carries, spelled as Contract section 5.1 requires.</summary>
    public const string CommuneColumn = "commune_id";

    public static EntityTypeBuilder<TEntity> HasCommuneReference<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        System.Linq.Expressions.Expression<Func<TEntity, string>> communeId)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(communeId);

        entity.Property(communeId).HasColumnType("text").IsRequired();

        // HasForeignKey's expression overload wants Expression<Func<T, object>>; passing the property
        // NAME avoids boxing the string in a Convert node just to satisfy the signature.
        entity.HasOne<AdministrativeUnit>()
            .WithMany()
            .HasForeignKey(PropertyName(communeId))
            .OnDelete(DeleteBehavior.Restrict);

        return entity;
    }

    private static string PropertyName<TEntity>(
        System.Linq.Expressions.Expression<Func<TEntity, string>> property)
        => property.Body is System.Linq.Expressions.MemberExpression member
            ? member.Member.Name
            : throw new ArgumentException(
                "HasCommuneReference expects a plain property access, for example x => x.CommuneId.",
                nameof(property));

    /// <summary>
    /// Fails startup if ANY entity has a <c>commune_id</c> column without a foreign key to
    /// <see cref="AdministrativeUnit"/>.
    /// </summary>
    /// <remarks>
    /// This scans COLUMNS, not interfaces, and that is the whole point. The
    /// <c>ICommuneScoped</c> guard can only see entities that already implement the interface, a
    /// limit its own documentation admits: an entity that carries <c>commune_id</c> but forgets to
    /// implement it slips through, and only code review catches that. Checking for the column closes
    /// exactly that hole — the same reasoning that made the query-filter guard check for an
    /// annotation rather than trust a convention.
    /// </remarks>
    internal static void ValidateCommuneReferences(this ModelBuilder modelBuilder)
    {
        var offenders = new List<string>();

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.ClrType == typeof(AdministrativeUnit))
            {
                // The anchor itself: its commune_id IS the primary key, so it has nothing to point at.
                continue;
            }

            var column = entity.GetProperties()
                .FirstOrDefault(property => property.GetColumnName() == CommuneColumn);

            if (column is null)
            {
                continue;
            }

            var referenced = entity.GetForeignKeys().Any(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(AdministrativeUnit)
                && foreignKey.Properties.Contains(column));

            if (!referenced)
            {
                offenders.Add(entity.ClrType.Name);
            }
        }

        if (offenders.Count > 0)
        {
            throw new InvalidOperationException(
                $"Entities have a '{CommuneColumn}' column with no foreign key to '{CommuneTable}': "
                + $"{string.Join(", ", offenders.Order(StringComparer.Ordinal))}. "
                + "An orphaned commune_id does not fail — the Contract section 7 query filter just "
                + "hides that row from every user, with nothing to point at the cause. Add "
                + "builder.HasCommuneReference(x => x.CommuneId) to that entity's configuration.");
        }
    }
}
