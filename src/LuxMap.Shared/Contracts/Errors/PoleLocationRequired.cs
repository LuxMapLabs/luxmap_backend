using System.Net;
using LuxMap.Shared.Http;

namespace LuxMap.Shared.Contracts.Errors;

/// <summary>
/// The one 409 for "this pole has no position yet" (POLE-GEOM-NULL G-4, GN-1, GN-3), so the fault report, the citizen
/// report and the pole photo answer the same code and the same <c>details</c>.
/// </summary>
public static class PoleLocationRequired
{
    public const string FaultReport = "fault_report";
    public const string CitizenReport = "citizen_report";
    public const string PolePhoto = "pole_photo";

    public static LuxMapException For(string poleId, string operation)
        => new(ErrorCodes.PoleLocationRequired, HttpStatusCode.Conflict,
            "This pole has no position yet. Set its position first, then try again.",
            new Dictionary<string, object?> { ["pole_id"] = poleId, ["operation"] = operation });
}
