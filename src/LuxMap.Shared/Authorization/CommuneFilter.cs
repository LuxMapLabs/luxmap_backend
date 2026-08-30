using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// Validates the <c>commune_id</c> query parameter supplied by the client.
/// </summary>
/// <remarks>
/// A global query filter cannot do this job: it merely drops rows, producing an empty list with
/// HTTP 200. Contract section 7 requires <b>403 <c>COMMUNE_FORBIDDEN</c></b> when the client asks
/// for a commune outside its scope, so this check must be explicit at the entry point.
/// <para>
/// <c>commune_id</c> NARROWS within the permitted scope; it never widens it.
/// </para>
/// </remarks>
public static class CommuneFilter
{
    /// <summary>
    /// Returns the communes to narrow to, or <c>null</c> meaning "no narrowing" (leave it to the
    /// query filter).
    /// </summary>
    /// <exception cref="LuxMapException">
    /// 403 <c>COMMUNE_FORBIDDEN</c> if ANY requested commune falls outside the scope — even when
    /// the remaining ones are valid.
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
                "Requested commune is outside the permitted scope.",
                // Echoing the rejected commune is safe: the client supplied that value itself, so
                // it reveals nothing about the data behind the API.
                new Dictionary<string, object?> { ["commune_id"] = forbidden });
        }

        return wanted;
    }
}
