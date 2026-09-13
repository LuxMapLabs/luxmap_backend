namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Lifetimes of the two web session kinds (Contract section 2.10.4). The mobile lifetimes, and the
/// 90-day ceiling <c>web_persistent</c> shares with them, stay in <see cref="JwtOptions"/>.
/// </summary>
/// <remarks>
/// Only lifetimes live here. The cookie's name and attributes are constants in code on purpose:
/// they are part of the Contract, not something a deployment may vary.
/// </remarks>
public sealed record WebAuthOptions
{
    public const string SectionName = "WebAuth";

    /// <summary>
    /// <c>web_persistent</c>: each rotation grants this many days from that moment, never past the
    /// chain's <see cref="JwtOptions.RefreshAbsoluteDays"/> ceiling.
    /// </summary>
    public int PersistentSlidingDays { get; init; } = 14;

    /// <summary><c>web_session</c>: absolute lifetime measured from sign-in. Rotation never extends it.</summary>
    public int SessionHours { get; init; } = 12;

    public TimeSpan SessionLifetime => TimeSpan.FromHours(SessionHours);

    /// <summary>A non-positive lifetime would issue tokens that are already expired, so startup STOPS.</summary>
    public void Validate()
    {
        if (PersistentSlidingDays <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:PersistentSlidingDays must be greater than 0; got {PersistentSlidingDays}.");
        }

        if (SessionHours <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:SessionHours must be greater than 0; got {SessionHours}.");
        }
    }
}
