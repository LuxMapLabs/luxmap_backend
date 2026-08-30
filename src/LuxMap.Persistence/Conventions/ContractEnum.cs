using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// Enums are stored as <c>text</c> holding EXACTLY the string the API returns
/// (<c>lamp_out</c>, <c>field_report</c>, ...), guarded by a CHECK constraint.
/// <para>
/// It reuses the very <see cref="JsonNamingPolicy.SnakeCaseLower"/> that BE-00 applies to JSON, so
/// the database value and the wire value cannot drift apart. <c>ContractEnumStorageTests</c> pins
/// that for every value of all 12 enums.
/// </para>
/// <para>
/// Do NOT use EF Core's default <c>HasConversion&lt;string&gt;()</c>: it stores the C# NAME
/// (<c>LampOut</c>), which differs from the API string.
/// </para>
/// </summary>
public static class ContractEnum
{
    public static string ToDbValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static IReadOnlyList<string> AllDbValues<TEnum>()
        where TEnum : struct, Enum
        => [.. Enum.GetValues<TEnum>().Select(ToDbValue)];

    public static ValueConverter<TEnum, string> Converter<TEnum>()
        where TEnum : struct, Enum
        => new(
            value => ToDbValue(value),
            text => Parse<TEnum>(text));

    /// <summary>Variant for nullable enum columns — NULL passes straight through, unconverted.</summary>
    public static ValueConverter<TEnum?, string?> NullableConverter<TEnum>()
        where TEnum : struct, Enum
        => new(
            value => value.HasValue ? ToDbValue(value.Value) : null,
            text => text == null ? null : Parse<TEnum>(text));

    /// <summary>Compares converted enums by value, not by string reference.</summary>
    public static ValueComparer<TEnum> Comparer<TEnum>()
        where TEnum : struct, Enum
        => new(
            (left, right) => left.Equals(right),
            value => value.GetHashCode());

    private static TEnum Parse<TEnum>(string text)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToDbValue(candidate), text, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Value '{text}' in column {typeof(TEnum).Name} is not part of the Contract section 1 enum set.");
    }
}

public static class ContractEnumBuilderExtensions
{
    /// <summary>
    /// Maps an enum property to a <c>text</c> column holding the Contract string, plus a CHECK constraint.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasContractEnum<TEntity, TEnum>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TEnum>> property)
        where TEntity : class
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        var builder = entity.Property(property);
        builder
            .HasColumnType("text")
            .HasConversion(ContractEnum.Converter<TEnum>(), ContractEnum.Comparer<TEnum>());

        var column = builder.Metadata.Name.ToSnakeCaseLower();
        var allowed = string.Join(", ", ContractEnum.AllDbValues<TEnum>().Select(v => $"'{v}'"));

        return entity.ToTable(table => table.HasCheckConstraint(
            $"ck_{entity.Metadata.GetTableName()?.ToSnakeCaseLower()}_{column}",
            $"\"{column}\" IN ({allowed})"));
    }

    /// <summary>
    /// Variant for nullable enum properties. The CHECK constraint permits NULL, because NULL carries
    /// its own business meaning here (for example: not revoked yet) rather than an invalid value.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasContractEnum<TEntity, TEnum>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TEnum?>> property)
        where TEntity : class
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        var builder = entity.Property(property);
        builder
            .HasColumnType("text")
            .HasConversion(ContractEnum.NullableConverter<TEnum>());

        var column = builder.Metadata.Name.ToSnakeCaseLower();
        var allowed = string.Join(", ", ContractEnum.AllDbValues<TEnum>().Select(v => $"'{v}'"));

        return entity.ToTable(table => table.HasCheckConstraint(
            $"ck_{entity.Metadata.GetTableName()?.ToSnakeCaseLower()}_{column}",
            $"\"{column}\" IS NULL OR \"{column}\" IN ({allowed})"));
    }

    /// <summary>
    /// The same snake_case rule EFCore.NamingConventions applies, used to build the column name for
    /// the CHECK constraint — at configuration time the final column name is not settled yet.
    /// </summary>
    internal static string ToSnakeCaseLower(this string name)
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
