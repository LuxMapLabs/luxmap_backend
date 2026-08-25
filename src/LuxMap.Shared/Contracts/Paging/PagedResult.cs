namespace LuxMap.Shared.Contracts.Paging;

/// <summary>
/// Contract v1.1 mục 0 — hình dạng phân trang đã chốt:
/// <c>{ page, page_size, total, items[] }</c>.
/// </summary>
/// <param name="Total">Tổng số bản ghi khớp bộ lọc, KHÔNG phải số phần tử trong <paramref name="Items"/>.</param>
public sealed record PagedResult<T>(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<T> Items)
{
    public static PagedResult<T> From(PageRequest request, int total, IReadOnlyList<T> items)
        => new(request.Page, request.PageSize, total, items);

    public static PagedResult<T> Empty(PageRequest request)
        => new(request.Page, request.PageSize, 0, []);
}
