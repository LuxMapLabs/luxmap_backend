using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// Kiểm tham số <c>commune_id</c> mà client truyền lên.
/// </summary>
/// <remarks>
/// Global query filter KHÔNG làm được việc này: nó chỉ lọc mất bản ghi, cho ra danh sách rỗng và
/// HTTP 200. Contract mục 7 đòi <b>403 <c>COMMUNE_FORBIDDEN</c></b> khi client hỏi một xã ngoài
/// phạm vi, nên phần này bắt buộc phải kiểm tường minh ở tầng vào.
/// <para>
/// Query param <c>commune_id</c> là bộ lọc THU HẸP trong phạm vi được phép, không phải cách mở
/// rộng phạm vi.
/// </para>
/// </remarks>
public static class CommuneFilter
{
    /// <summary>
    /// Trả về tập xã cần lọc thêm, hoặc <c>null</c> nghĩa là "không thu hẹp" (để query filter lo).
    /// </summary>
    /// <exception cref="LuxMapException">
    /// 403 <c>COMMUNE_FORBIDDEN</c> nếu có bất kỳ xã nào nằm ngoài phạm vi — kể cả khi các xã
    /// còn lại đều hợp lệ.
    /// </exception>
    public static IReadOnlyList<string>? Narrow(CommuneScope scope, IEnumerable<string>? requested)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var wanted = (requested ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return null;
        }

        var forbidden = wanted.Where(id => !scope.Allows(id)).ToArray();
        if (forbidden.Length > 0)
        {
            throw new LuxMapException(
                ErrorCodes.CommuneForbidden,
                System.Net.HttpStatusCode.Forbidden,
                "Yêu cầu địa bàn ngoài phạm vi được phép.",
                // Nêu đúng xã bị từ chối là an toàn: client đã tự nói ra giá trị đó, không lộ
                // thêm thông tin nào về dữ liệu bên trong.
                new Dictionary<string, object?> { ["commune_id"] = forbidden });
        }

        return wanted;
    }
}
