using Microsoft.Net.Http.Headers;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// An HTTP <c>Range</c> request for a clip, resolved against its size (RFC 9110 §14). One byte range is honoured: <c>a-b</c>, <c>a-</c>
/// (to the end) and <c>-n</c> (the last n bytes). A header that is absent, names another unit, holds several ranges or is malformed is
/// IGNORED and the whole clip is sent — the standard fallback. A range that starts past the end, or asks for the last 0 bytes, cannot
/// be satisfied: 416.
/// </summary>
public readonly record struct PoleVideoRange(bool Partial, bool Unsatisfiable, long From, long To)
{
    public static PoleVideoRange Resolve(RangeHeaderValue? header, long total)
    {
        var whole = new PoleVideoRange(false, false, 0, total - 1);
        if (header is null || !string.Equals(header.Unit.ToString(), "bytes", StringComparison.OrdinalIgnoreCase) || header.Ranges.Count != 1)
            return whole;
        var (first, last) = (header.Ranges.First().From, header.Ranges.First().To);
        if (first is null && last is null) return whole;
        if (first is not { } start)
            return last is { } suffix && suffix > 0 ? new(true, false, Math.Max(0, total - suffix), total - 1) : Unsatisfied;
        if (last is { } end && end < start) return whole;     // "5-3" is not a range at all
        if (start >= total) return Unsatisfied;
        return new(true, false, start, Math.Min(last ?? total - 1, total - 1));
    }

    private static PoleVideoRange Unsatisfied => new(true, true, 0, 0);
}
