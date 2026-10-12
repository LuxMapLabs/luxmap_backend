using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.Citizen.Entities;

/// <summary>
/// The QR code stuck on a pole (CITIZEN-QR, CQ-1 / CQ-10): a random code the server draws, which the public report page
/// resolves to the pole. At most one is in use per pole; a revoked code is kept for history and never comes back.
/// </summary>
/// <remarks>
/// <para>
/// Two identifiers on purpose. <see cref="QrId"/> (<c>QR-0001</c>) is what Managers act on — routes, logs, audit rows.
/// <see cref="Code"/> is the value printed on the pole: whoever holds it can file a report, so it never goes into a URL
/// path, a log line or an audit row (D-16). Only the issuing Manager's response and the list carry it.
/// </para>
/// <para>
/// <see cref="CommuneId"/> is copied from the pole by the server, never read from a client — the same rule as
/// <c>fixture</c>. A pole never changes commune (BE-12a rule 1), so the copy cannot drift.
/// </para>
/// </remarks>
public sealed class QrCode : ICommuneScoped, IAudited
{
    public string QrId { get; set; } = null!;

    /// <summary>16 characters of base62 from a CSPRNG (~95 bits, D-3).</summary>
    public required string Code { get; set; }

    public required string PoleId { get; set; }

    public required string CommuneId { get; set; }

    public required string CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set together with <see cref="RevokedAt"/>; both null while the code is in use.</summary>
    public string? RevokedBy { get; set; }

    public DateTime? RevokedAt { get; set; }
}
