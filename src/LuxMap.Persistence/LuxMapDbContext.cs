using System.Reflection;
using LuxMap.Persistence.Conventions;
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
    ModuleAssemblyCatalog catalog)
    : DbContext(options)
{
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
    }
}

/// <summary>
/// Danh sách assembly được quét tìm cấu hình entity. Host dựng từ danh sách module đã đăng ký.
/// </summary>
public sealed class ModuleAssemblyCatalog(IEnumerable<Assembly> assemblies)
{
    public IReadOnlyList<Assembly> Assemblies { get; } = [.. assemblies.Distinct()];
}
