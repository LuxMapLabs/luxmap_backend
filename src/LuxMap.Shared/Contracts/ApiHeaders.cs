namespace LuxMap.Shared.Contracts;

public static class ApiHeaders
{
    /// <summary>
    /// Sent on EVERY response (2xx and errors alike), never placed in the body — the
    /// <c>{ error: { code, message, details } }</c> shape is published and must not grow new keys.
    /// Attaching the header is BE-04 middleware's job.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}
