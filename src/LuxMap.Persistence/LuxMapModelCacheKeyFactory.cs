using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LuxMap.Persistence;

/// <summary>
/// Khoá cache model của EF mặc định chỉ gồm KIỂU DbContext. Nhưng model của LuxMap còn phụ thuộc
/// <see cref="ModuleAssemblyCatalog"/> — danh sách module quyết định entity nào có mặt.
/// </summary>
/// <remarks>
/// Không có lớp này thì hai host trong cùng một process với danh sách module khác nhau sẽ dùng
/// chung model của host dựng trước, và host còn lại truy vấn entity của mình sẽ ném lỗi. Trong
/// ứng dụng thật chỉ có một host nên không đổi gì; nhưng khoá cache phải phản ánh đúng thứ model
/// thực sự phụ thuộc vào, nếu không đây là quả bom hẹn giờ cho mọi kịch bản nhiều host.
/// </remarks>
public sealed class LuxMapModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        var catalogSignature = context is LuxMapDbContext luxMap
            ? luxMap.CatalogSignature
            : string.Empty;

        return (context.GetType(), catalogSignature, designTime);
    }
}
