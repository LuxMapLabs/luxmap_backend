namespace LuxMap.Shared.Contracts.Errors;

/// <summary>
/// Contract v1.1 section 0 — the frozen error shape:
/// <c>{ "error": { "code", "message", "details" } }</c>.
/// The correlation id travels in the <see cref="ApiHeaders.CorrelationId"/> header and inside
/// <c>details</c>; the object itself keeps exactly the three published keys.
/// </summary>
public sealed record ApiErrorResponse(ApiError Error)
{
    public static ApiErrorResponse Create(
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
        => new(new ApiError(code, message, details ?? ApiError.NoDetails));
}

/// <param name="Code">Stable machine-readable code, e.g. <c>BBOX_TOO_LARGE</c>. See <see cref="ErrorCodes"/>.</param>
/// <param name="Message">Human-readable sentence. Clients must not parse it.</param>
/// <param name="Details">Context bag; always present, rendered as <c>{}</c> when empty.</param>
public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Details)
{
    public static readonly IReadOnlyDictionary<string, object?> NoDetails =
        new Dictionary<string, object?>();
}
