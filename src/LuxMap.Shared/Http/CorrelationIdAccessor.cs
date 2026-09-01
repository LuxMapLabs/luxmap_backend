namespace LuxMap.Shared.Http;

/// <summary>
/// Correlation id of the current request. Business code reads it through this interface instead of
/// touching <c>HttpContext</c>, so modules stay independent of the web layer.
/// </summary>
public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}
