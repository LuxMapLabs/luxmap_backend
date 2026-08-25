namespace LuxMap.Shared.Contracts.Paging;

/// <summary>
/// Query <c>?page=1&amp;page_size=50</c> đã chuẩn hoá. Dựng bằng <see cref="Create"/> để chắc chắn
/// đã kẹp về khoảng hợp lệ.
/// </summary>
public sealed record PageRequest
{
    public const int FirstPage = 1;
    public const int DefaultPageSize = 50;

    /// <summary>Contract v1.1 mục 0 — <c>page_size</c> tối đa 200.</summary>
    public const int MaxPageSize = 200;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>Số bản ghi bỏ qua — dùng cho <c>Skip()</c> / <c>OFFSET</c>.</summary>
    public int Skip => (Page - FirstPage) * PageSize;

    /// <summary>
    /// Giá trị ngoài khoảng bị KẸP im lặng, không báo lỗi: <c>page_size=500</c> trả về 200 bản ghi
    /// kèm <c>page_size: 200</c> trong response. Client đọc <c>page_size</c> trả về để biết.
    /// </summary>
    public static PageRequest Create(int? page = null, int? pageSize = null)
        => new(
            Math.Max(page ?? FirstPage, FirstPage),
            Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
