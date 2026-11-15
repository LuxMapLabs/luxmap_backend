namespace LuxMap.Modules.Assets;

/// <summary>
/// The QR codes on a pole, asked from the Assets side when the pole is deleted (POLE-DELETE-QR).
/// </summary>
/// <remarks>
/// <para>
/// A port because Assets cannot reference Citizen — the reference already runs the other way (<c>qr_code</c> points at
/// <c>pole</c>). Citizen implements it.
/// </para>
/// <para>
/// A code is not research data: it is a sticker the team printed for a pole. Once the pole row goes, a code naming it
/// means nothing, so deleting the pole takes its codes with it. What a citizen SENT through a code is different — a
/// <c>citizen_submission</c> holds the code with RESTRICT, so a pole somebody reported through is still refused, by the
/// database, like a pole with a fault.
/// </para>
/// </remarks>
public interface IPoleQrCodes
{
    /// <summary>
    /// Deletes every code of the pole, in use or revoked, one <c>SaveChanges</c> and one audit event each.
    /// </summary>
    /// <remarks>
    /// Runs inside the caller's transaction, on the caller's context, AFTER the caller has locked the pole row — the lock
    /// issuing and revoking take (pole, then code), so no code can be issued between this and the pole's own delete.
    /// A refusal surfaces as the <c>DbUpdateException</c> of the save that hit it; the caller rolls back.
    /// </remarks>
    Task RemoveAllAsync(string poleId, CancellationToken ct);
}
