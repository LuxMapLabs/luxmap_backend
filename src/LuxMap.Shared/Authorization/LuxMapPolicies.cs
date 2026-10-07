using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// The capability matrix: every authorization policy of the API and the EXACT roles it admits.
/// This file is the only source <c>AuthorizationSetup</c> registers policies from.
/// </summary>
/// <remarks>
/// Declared in <c>LuxMap.Shared</c> rather than beside the registration in <c>LuxMap.Api</c>, because
/// the two ends live in different assemblies and the dependency only runs one way: the host
/// references the modules, so a module controller writing
/// <c>[Authorize(Policy = LuxMapPolicies.ManageAssets)]</c> could not see a name defined in the host.
/// The names are a CONTRACT between the two, which is exactly what Shared is for.
/// <para>
/// ⚠️ <b>A policy is a LIST of exact roles, never a rank</b> (Contract v1.7 section 2, replacing
/// D-14's "one exact role"). There is no "manager and above": a role is admitted because it is
/// named in <see cref="Matrix"/>, and a role that is not named is refused. Registration form v1.2
/// needs this — the Superior reads but never writes, and some work is split between the Manager and
/// the Field Engineer — and a single-role policy could only express it by leaving endpoints bare.
/// </para>
/// <para>
/// Every business endpoint names one of these policies; none relies on the fallback policy
/// (<c>CapabilityPolicyCoverageTests</c>). A capability without an endpoint yet is declared here so
/// its roles are decided once, before the ticket that builds it: <see cref="ControlLighting"/>
/// (D-R7) and <see cref="ManageUsers"/> (BE-33). Other capabilities — survey review, work orders,
/// fault decisions — are added WITH their ticket, not in advance.
/// </para>
/// </remarks>
public static class LuxMapPolicies
{
    /// <summary>Read the lighting network: map layers, asset inventory, topology.</summary>
    public const string ReadNetwork = "cap:read_network";

    /// <summary>Read lux readings.</summary>
    public const string ReadLuxReadings = "cap:read_lux_readings";

    /// <summary>Create, replace, delete and import poles, fixtures, segments and feeders.</summary>
    public const string ManageAssets = "cap:manage_assets";

    /// <summary>Record a relative light reading taken in the field.</summary>
    public const string RecordLuxReading = "cap:record_lux_reading";

    /// <summary>
    /// Force ON / OFF / AUTO on supported lighting devices (testbed demo). No endpoint yet (D-R7).
    /// </summary>
    public const string ControlLighting = "cap:control_lighting";

    /// <summary>Create accounts and assign roles and communes (BE-33a, <c>/admin/users</c>).</summary>
    public const string ManageUsers = "cap:manage_users";

    /// <summary>
    /// Write the free-text note on a pole (POLE-NOTE). Field engineers write it on site, managers too —
    /// without either gaining any other asset write, which stays <see cref="ManageAssets"/>.
    /// </summary>
    public const string EditPoleNotes = "cap:edit_pole_notes";

    /// <summary>Read the fault list (BE-40). Kept apart from <see cref="ReadNetwork"/> so fault access can change without touching the map.</summary>
    /// <summary>BE-27 — read and mark one's OWN notifications. Every role, so no endpoint leans on the fallback.</summary>
    public const string ReadNotifications = "cap:read_notifications";

    public const string ReadFaults = "cap:read_faults";

    /// <summary>Review a fault: confirm, reject, reclassify, set severity, write a review note (BE-19).</summary>
    public const string ReviewFaults = "cap:review_faults";

    /// <summary>
    /// Report a fault seen on site and attach photos to it (BE-41, Contract 5.4). Field engineers only: a report
    /// is an observation from the road, and every report starts <c>detected</c> for a Manager to review.
    /// </summary>
    public const string ReportFaults = "cap:report_faults";

    public const string ReadSurveys = "cap:read_surveys";
    public const string SubmitSurveys = "cap:submit_surveys";
    public const string ReviewSurveys = "cap:review_surveys";

    public const string ReadWorkOrders = "cap:read_work_orders";
    public const string ManageWorkOrders = "cap:manage_work_orders";
    public const string ExecuteWorkOrders = "cap:execute_work_orders";

    /// <summary>
    /// BE-43 — download the offline bundle and push the offline queue. Field engineers only: the five queued
    /// operations are theirs. Every queued operation is ALSO checked against its own capability.
    /// </summary>
    public const string SyncOffline = "cap:sync_offline";

    /// <summary>
    /// BE-28 — dashboard statistics. Not the Field Engineer: work orders are filtered to the assignee for that role,
    /// so the same figure would come out different from everyone else's (drift ST-8).
    /// </summary>
    public const string ReadStatistics = "cap:read_statistics";

    /// <summary>Policy name → the roles it admits. Exhaustive: a policy missing here does not exist.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<UserRole>> Matrix { get; } =
        new Dictionary<string, IReadOnlyList<UserRole>>(StringComparer.Ordinal)
        {
            [ReadSurveys] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [SubmitSurveys] = [UserRole.FieldEngineer],
            [ReviewSurveys] = [UserRole.Manager],
            [ReadNetwork] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ReadLuxReadings] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ManageAssets] = [UserRole.Manager],
            [EditPoleNotes] = [UserRole.Manager, UserRole.FieldEngineer],
            [RecordLuxReading] = [UserRole.FieldEngineer],
            [ControlLighting] = [UserRole.Manager],
            [ManageUsers] = [UserRole.SystemAdmin],
            [ReadFaults] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ReadNotifications] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ReviewFaults] = [UserRole.Manager],
            [ReportFaults] = [UserRole.FieldEngineer],
            [ReadWorkOrders] = [UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin],
            [ManageWorkOrders] = [UserRole.Manager],
            [ExecuteWorkOrders] = [UserRole.FieldEngineer],
            [SyncOffline] = [UserRole.FieldEngineer],
            [ReadStatistics] = [UserRole.Superior, UserRole.Manager, UserRole.SystemAdmin],
        };
}
