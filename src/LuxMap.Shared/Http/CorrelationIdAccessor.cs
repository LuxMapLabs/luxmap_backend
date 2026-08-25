namespace LuxMap.Shared.Http;

/// <summary>
/// Correlation id của request hiện tại. Tầng nghiệp vụ lấy qua đây thay vì đọc
/// <c>HttpContext</c>, để module không phụ thuộc vào tầng web.
/// </summary>
public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}
