using System.Reflection;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Persistence;

/// <summary>
/// Một DbContext dùng chung cho cả monolith. Mỗi module đóng góp
/// <see cref="IEntityTypeConfiguration{TEntity}"/> của riêng mình; DbContext quét assembly
/// của từng module nên Persistence không cần tham chiếu ngược lại module nào.
/// <para>
/// Chọn một context chung thay vì mỗi module một context vì Contract bắt buộc join xuyên
/// module: <c>GET /poles/{id}</c> phải trả <c>open_faults[]</c> trong MỘT request,
/// <c>GET /segments</c> cần <c>has_active_segment_fault</c>, <c>GET /sync/bundle</c> gom
/// poles + segments + faults + work orders.
/// </para>
/// </summary>
public class LuxMapDbContext(
    DbContextOptions<LuxMapDbContext> options,
    ModuleAssemblyCatalog catalog,
    ICommuneScopeAccessor scopeAccessor)
    : DbContext(options)
{
    /// <summary>
    /// Phạm vi địa bàn của request hiện tại. Query filter đọc qua ĐÂY chứ không bắt accessor từ
    /// ngoài — xem chú thích ở <c>CommuneScopeBuilderExtensions.ApplyFilter</c>.
    /// </summary>
    public CommuneScope CurrentCommuneScope => scopeAccessor.Scope;

    /// <summary>
    /// Định danh tập module đã nạp. Model phụ thuộc vào nó nên khoá cache model phải gồm nó —
    /// xem <see cref="LuxMapModelCacheKeyFactory"/>.
    /// </summary>
    internal string CatalogSignature => catalog.Signature;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        foreach (var assembly in catalog.Assemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        // Phải chạy SAU khi áp hết cấu hình: quét model tìm cột đã gắn HasPrefixedId
        // rồi tạo sequence tương ứng, để không ai phải nhớ khai sequence bằng tay.
        modelBuilder.CreatePrefixedIdSequences();

        // Contract mục 7. Quên gắn scope là app KHÔNG khởi động được, thay vì rò dữ liệu im lặng.
        modelBuilder.ApplyCommuneScope(this);
    }
}


/// <summary>
/// Danh sách assembly được quét tìm cấu hình entity. Host dựng từ danh sách module đã đăng ký.
/// </summary>
public sealed class ModuleAssemblyCatalog(IEnumerable<Assembly> assemblies)
{
    public IReadOnlyList<Assembly> Assemblies { get; } = [.. assemblies.Distinct()];

    /// <summary>Chuỗi ổn định đại diện cho tập assembly, dùng làm một phần khoá cache model.</summary>
    public string Signature => field ??= string.Join('|', Assemblies.Select(a => a.FullName).Order(StringComparer.Ordinal));
}
