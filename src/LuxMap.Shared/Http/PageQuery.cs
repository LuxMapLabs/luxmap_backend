using LuxMap.Shared.Contracts.Paging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxMap.Shared.Http;

/// <summary>
/// Ràng buộc query <c>?page=1&amp;page_size=50</c> của Contract mục 0.
/// </summary>
/// <remarks>
/// Dùng binder riêng thay vì <c>[FromQuery]</c> mặc định: khi tham số action đặt tên
/// <c>page</c>, MVC lấy chính tên đó làm prefix (vì query có key <c>page</c>) rồi đi tìm
/// <c>page.page_size</c>, nên <c>page_size</c> âm thầm rơi về mặc định. Binder này đọc thẳng
/// từ query nên không phụ thuộc tên tham số.
/// </remarks>
[ModelBinder(BinderType = typeof(PageQueryModelBinder))]
public sealed class PageQuery
{
    public const string PageKey = "page";
    public const string PageSizeKey = "page_size";

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Giá trị ngoài khoảng bị kẹp im lặng — xem <see cref="PageRequest.Create"/>.</summary>
    public PageRequest ToPageRequest() => PageRequest.Create(Page, PageSize);
}

public sealed class PageQueryModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var query = bindingContext.HttpContext.Request.Query;

        bindingContext.Result = ModelBindingResult.Success(new PageQuery
        {
            Page = ReadInt(query, PageQuery.PageKey),
            PageSize = ReadInt(query, PageQuery.PageSizeKey),
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Giá trị không phải số bị bỏ qua và rơi về mặc định, không dựng thành lỗi validation —
    /// Contract không định nghĩa mã lỗi nào cho tham số phân trang sai định dạng.
    /// </summary>
    private static int? ReadInt(IQueryCollection query, string key)
        => query.TryGetValue(key, out var values) && int.TryParse(values.ToString(), out var parsed)
            ? parsed
            : null;
}
