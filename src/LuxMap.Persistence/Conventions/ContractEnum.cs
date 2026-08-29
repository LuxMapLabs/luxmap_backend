using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// Enum lưu xuống DB dưới dạng <c>text</c> mang ĐÚNG chuỗi mà API trả ra
/// (<c>lamp_out</c>, <c>field_report</c>, ...), kèm CHECK constraint giới hạn tập giá trị.
/// <para>
/// Cùng một <see cref="JsonNamingPolicy.SnakeCaseLower"/> mà BE-00 dùng cho tầng JSON, nên
/// giá trị trong DB và giá trị trên dây không thể lệch nhau. <c>ContractEnumStorageTests</c>
/// khoá lại điều đó cho từng giá trị của cả 12 enum.
/// </para>
/// <para>
/// Không dùng <c>HasConversion&lt;string&gt;()</c> mặc định của EF Core: nó lưu TÊN C#
/// (<c>LampOut</c>), lệch với chuỗi trên API.
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

    /// <summary>Bản cho cột enum cho phép NULL — NULL đi thẳng qua, không convert.</summary>
    public static ValueConverter<TEnum?, string?> NullableConverter<TEnum>()
        where TEnum : struct, Enum
        => new(
            value => value.HasValue ? ToDbValue(value.Value) : null,
            text => text == null ? null : Parse<TEnum>(text));

    /// <summary>So sánh enum đã convert bằng giá trị, không phải bằng tham chiếu chuỗi.</summary>
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
            $"Giá trị '{text}' trong cột {typeof(TEnum).Name} không thuộc tập enum của Contract mục 1.");
    }
}

public static class ContractEnumBuilderExtensions
{
    /// <summary>
    /// Map một property enum thành cột <c>text</c> mang chuỗi của Contract, kèm CHECK constraint.
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
    /// Bản cho property enum nullable. CHECK constraint cho phép NULL, vì NULL ở đây mang
    /// nghĩa nghiệp vụ riêng (ví dụ: chưa thu hồi) chứ không phải giá trị sai.
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
    /// Cùng quy tắc snake_case với EFCore.NamingConventions, dùng để dựng tên cột trong
    /// CHECK constraint — thời điểm cấu hình model thì tên cột cuối cùng chưa được chốt.
    /// </summary>
    internal static string ToSnakeCaseLower(this string name)
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
