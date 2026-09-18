namespace LuxMap.Shared.Contracts;

public static class ApiHeaders
{
    /// <summary>
    /// Sent on EVERY response (2xx and errors alike). On an error response the same value is ALSO
    /// written as <c>error.details.correlation_id</c> — <c>details</c> is the published free-form bag,
    /// so the <c>{ error: { code, message, details } }</c> shape grows no new key (drift 4, folded
    /// into the Contract at BE-REVIEW-02). Attaching the header is BE-04 middleware's job.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}
