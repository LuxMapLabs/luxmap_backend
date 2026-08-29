namespace LuxMap.Shared.Authorization;

/// <summary>
/// Phạm vi địa bàn của request hiện tại, đã rút ra từ JWT đã ký. KHÔNG bao giờ lấy từ input
/// của client.
/// </summary>
/// <param name="IsSystemWide">Contract mục 7: Quản trị mang <c>["*"]</c>, thấy toàn hệ thống.</param>
/// <param name="CommuneIds">Rỗng khi chưa xác thực hoặc claim thiếu — nghĩa là KHÔNG thấy gì.</param>
public sealed record CommuneScope(bool IsSystemWide, IReadOnlyList<string> CommuneIds)
{
    /// <summary>
    /// Mặc định an toàn: không phạm vi nào cả. Dùng khi chưa xác thực, khi claim
    /// <c>commune_ids</c> vắng mặt, hoặc khi claim rỗng — cả ba đều KHÔNG được hiểu là
    /// "không có ràng buộc".
    /// </summary>
    public static readonly CommuneScope Empty = new(false, []);

    public static CommuneScope SystemWide { get; } = new(true, []);

    public static CommuneScope ForCommunes(IEnumerable<string> communeIds)
        => new(false, [.. communeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)]);

    /// <summary>Quản trị thấy tất cả; ngoài ra chỉ thấy đúng các xã trong claim.</summary>
    public bool Allows(string communeId)
        => IsSystemWide || CommuneIds.Contains(communeId, StringComparer.Ordinal);
}

/// <summary>
/// Lấy phạm vi địa bàn của request hiện tại. Tầng nghiệp vụ và tầng persistence dùng qua đây
/// thay vì đọc <c>HttpContext</c> — cùng mẫu với <c>ICorrelationIdAccessor</c> ở BE-04.
/// </summary>
public interface ICommuneScopeAccessor
{
    CommuneScope Scope { get; }
}
