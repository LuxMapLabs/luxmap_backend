using System.Reflection;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LuxMap.Persistence.Conventions;

/// <summary>
/// Lọc theo địa bàn ở tầng truy vấn (Contract mục 7).
/// <para>
/// Dùng trong <c>IEntityTypeConfiguration</c> của entity khai <see cref="ICommuneScoped"/>:
/// <code>
/// builder.HasCommuneScope();
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// Cùng khuôn với <c>HasPrefixedId</c>: lệnh này chỉ ĐÁNH DẤU ý định; bộ lọc thật do
/// <see cref="LuxMapDbContext"/> áp tập trung sau khi đã nạp hết cấu hình module — vì
/// <c>ApplyConfigurationsFromAssembly</c> khởi tạo cấu hình bằng constructor rỗng nên không
/// tiêm được <see cref="ICommuneScopeAccessor"/> vào đây.
/// <para>
/// Lọc nằm trong <c>WHERE</c> chứ không phải kiểm sau khi lấy — đây là điều kiện để
/// <c>GET /poles/{id}</c> ngoài phạm vi trả <b>404</b>: bản ghi đơn giản là không tìm thấy.
/// Lấy-rồi-kiểm sẽ tự nhiên ra 403 và tiết lộ tài nguyên đó tồn tại.
/// </para>
/// </remarks>
public static class CommuneScopeBuilderExtensions
{
    /// <summary>
    /// Annotation do CHÍNH hệ thống đặt. Chốt chặn đối chiếu annotation này chứ không đi dò
    /// query filter trong nội bộ EF — nhờ vậy không phụ thuộc phiên bản EF.
    /// </summary>
    internal const string ScopeAnnotation = "LuxMap:CommuneScopeApplied";

    public static EntityTypeBuilder<TEntity> HasCommuneScope<TEntity>(this EntityTypeBuilder<TEntity> entity)
        where TEntity : class, ICommuneScoped
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.HasAnnotation(ScopeAnnotation, true);
    }

    /// <summary>
    /// Áp bộ lọc cho mọi entity đã đánh dấu, VÀ kiểm bất biến: entity khai
    /// <see cref="ICommuneScoped"/> mà thiếu đánh dấu thì ném lỗi — app không khởi động được.
    /// </summary>
    internal static void ApplyCommuneScope(this ModelBuilder modelBuilder, LuxMapDbContext context)
    {
        var scoped = modelBuilder.Model
            .GetEntityTypes()
            .Where(entity => entity.ClrType.IsAssignableTo(typeof(ICommuneScoped)))
            .ToArray();

        var missing = scoped
            .Where(entity => entity.FindAnnotation(ScopeAnnotation) is null)
            .Select(entity => entity.ClrType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Entity khai ICommuneScoped nhưng chưa gọi HasCommuneScope(): {string.Join(", ", missing)}. "
                + "Contract mục 7 yêu cầu lọc theo địa bàn ở server, LUÔN LUÔN — thiếu là rò dữ liệu xã khác. "
                + "Thêm builder.HasCommuneScope() vào IEntityTypeConfiguration của entity đó.");
        }

        foreach (var entity in scoped)
        {
            ApplyFilterMethod
                .MakeGenericMethod(entity.ClrType)
                .Invoke(null, [modelBuilder, context]);
        }
    }

    private static readonly MethodInfo ApplyFilterMethod =
        typeof(CommuneScopeBuilderExtensions).GetMethod(
            nameof(ApplyFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Biểu thức tham chiếu CHÍNH DbContext, không phải một singleton bắt từ ngoài.
    /// </summary>
    /// <remarks>
    /// ⚠️ Đây không phải chuyện phong cách. Bắt <c>ICommuneScopeAccessor</c> từ bên ngoài thì EF
    /// coi <c>scopeAccessor.Scope.IsSystemWide</c> là biểu thức con tính được và HẰNG-SỐ-HOÁ nó
    /// vào query đã biên dịch. Query đó được cache theo hình dạng, nên MỌI người dùng sau đều
    /// dùng lại phạm vi của người dùng ĐẦU TIÊN — rò dữ liệu im lặng.
    /// Tham chiếu qua DbContext là đường EF hỗ trợ chính thức: giá trị được đọc lại theo từng
    /// instance context, tức từng request.
    /// </remarks>
    private static void ApplyFilter<TEntity>(ModelBuilder modelBuilder, LuxMapDbContext context)
        where TEntity : class, ICommuneScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(candidate =>
            context.CurrentCommuneScope.IsSystemWide
            || context.CurrentCommuneScope.CommuneIds.Contains(candidate.CommuneId));
    }
}
