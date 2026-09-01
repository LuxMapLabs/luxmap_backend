using System.Net;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Shared.Http;

/// <summary>
/// A known business failure carrying the Contract's error code and HTTP status.
/// Throw this instead of building an error response inline — the middleware then produces the
/// <see cref="ApiErrorResponse"/> shape in exactly one place.
/// </summary>
public class LuxMapException(
    string code,
    HttpStatusCode statusCode,
    string message,
    IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? ApiError.NoDetails;
}
