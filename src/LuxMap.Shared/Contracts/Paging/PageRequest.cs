namespace LuxMap.Shared.Contracts.Paging;

/// <summary>
/// A normalised <c>?page=1&amp;page_size=50</c> query. Build it with <see cref="Create"/> so the
/// values are guaranteed to sit inside the allowed range.
/// </summary>
public sealed record PageRequest
{
    public const int FirstPage = 1;
    public const int DefaultPageSize = 50;

    /// <summary>Contract v1.1 section 0 — <c>page_size</c> caps at 200.</summary>
    public const int MaxPageSize = 200;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>Rows to skip — feed straight into <c>Skip()</c> / <c>OFFSET</c>.</summary>
    public int Skip => (Page - FirstPage) * PageSize;

    /// <summary>
    /// Out-of-range values are clamped SILENTLY, not rejected: <c>page_size=500</c> returns 200 rows
    /// and reports <c>page_size: 200</c> in the response. Clients must read <c>page_size</c> back.
    /// </summary>
    public static PageRequest Create(int? page = null, int? pageSize = null)
        => new(
            Math.Max(page ?? FirstPage, FirstPage),
            Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
