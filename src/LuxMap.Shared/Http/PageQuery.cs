using LuxMap.Shared.Contracts.Paging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxMap.Shared.Http;

/// <summary>
/// Binds the Contract's <c>?page=1&amp;page_size=50</c> query (section 0).
/// </summary>
/// <remarks>
/// Uses a dedicated binder rather than plain <c>[FromQuery]</c>: when the action parameter is named
/// <c>page</c>, MVC adopts that name as a prefix (because the query contains a <c>page</c> key) and
/// then looks for <c>page.page_size</c>, so <c>page_size</c> silently falls back to the default.
/// This binder reads the query directly and therefore does not depend on the parameter name.
/// </remarks>
[ModelBinder(BinderType = typeof(PageQueryModelBinder))]
public sealed class PageQuery
{
    public const string PageKey = "page";
    public const string PageSizeKey = "page_size";

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Out-of-range values are clamped silently — see <see cref="PageRequest.Create"/>.</summary>
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
    /// Non-numeric values are ignored and fall back to the default rather than becoming a validation
    /// error — the Contract defines no error code for malformed pagination parameters.
    /// </summary>
    private static int? ReadInt(IQueryCollection query, string key)
        => query.TryGetValue(key, out var values) && int.TryParse(values.ToString(), out var parsed)
            ? parsed
            : null;
}
