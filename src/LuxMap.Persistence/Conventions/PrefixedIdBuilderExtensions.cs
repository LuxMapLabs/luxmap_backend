using LuxMap.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// Hiện thực quy ước ID của Contract mục 0.4 một lần cho toàn bộ 16 entity có ID hiển thị.
/// <para>
/// Dùng ở <c>IEntityTypeConfiguration</c> của bất kỳ entity nào:
/// <code>
/// builder.Property(p => p.PoleId).HasPrefixedId(PrefixedIds.Pole);
/// </code>
/// Sequence tương ứng KHÔNG cần khai riêng — <see cref="LuxMapDbContext"/> tự quét model và
/// tạo sequence cho mọi cột đã đánh dấu, nên không ai quên và migration luôn đủ.
/// </para>
/// </summary>
public static class PrefixedIdBuilderExtensions
{
    /// <summary>Đánh dấu để DbContext biết cột này cần sequence nào.</summary>
    internal const string SequenceAnnotation = "LuxMap:PrefixedIdSequence";

    public static PropertyBuilder<string> HasPrefixedId(
        this PropertyBuilder<string> property,
        PrefixedIdSpec spec)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(spec);

        return property
            .HasColumnType("text")
            // DB sinh ID, client không bao giờ tự đặt (Contract mục 0.4).
            .HasDefaultValueSql(spec.DefaultValueSql)
            .ValueGeneratedOnAdd()
            .HasAnnotation(SequenceAnnotation, spec.SequenceName);
    }

    /// <summary>
    /// Tạo sequence cho mọi cột đã gắn <see cref="HasPrefixedId"/>. Gọi ở cuối
    /// <c>OnModelCreating</c>, sau khi đã áp hết cấu hình của các module.
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
            // bigint: Contract mục 0.3 nói ID dài ra tự nhiên khi vượt ngưỡng chữ số,
            // không có chuyện tràn số.
            modelBuilder.HasSequence<long>(name!).StartsAt(1).IncrementsBy(1);
        }
    }
}
