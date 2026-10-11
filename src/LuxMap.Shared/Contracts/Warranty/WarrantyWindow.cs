using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Contracts.Warranty;

/// <summary>
/// BE-31 — the ONE definition of <see cref="WarrantyStatus"/>, for a given "today" and expiring window (drift W-2).
/// </summary>
/// <remarks>
/// <para>
/// Calendar days, not nights: a lamp whose warranty ends today is still covered for the whole of today, so it is
/// <c>expiring</c>; it is <c>expired</c> from tomorrow. <c>expiring</c> runs from today through
/// <see cref="LastExpiringDay"/>, both ends included.
/// </para>
/// <para>
/// A list that FILTERS by status has to say the same thing in SQL, so it filters on <see cref="Today"/> and
/// <see cref="LastExpiringDay"/> — never on dates of its own — and its tests check every row it returns against
/// <see cref="Classify"/>.
/// </para>
/// </remarks>
public sealed record WarrantyWindow
{
    /// <summary>Upper bound on the expiring window: ten years is already "not soon".</summary>
    public const int MaxExpiringWithinDays = 3650;

    public WarrantyWindow(DateOnly today, int expiringWithinDays)
    {
        if (expiringWithinDays is < 0 or > MaxExpiringWithinDays)
        {
            throw new ArgumentOutOfRangeException(nameof(expiringWithinDays), expiringWithinDays,
                $"The expiring window must be 0..{MaxExpiringWithinDays} days.");
        }

        Today = today;
        ExpiringWithinDays = expiringWithinDays;
    }

    public DateOnly Today { get; }

    public int ExpiringWithinDays { get; }

    /// <summary>The last day a warranty may end on and still be <c>expiring</c> rather than <c>active</c>.</summary>
    public DateOnly LastExpiringDay => Today.AddDays(ExpiringWithinDays);

    public WarrantyStatus Classify(DateOnly? expiry) => expiry switch
    {
        null => WarrantyStatus.Unknown,
        { } end when end < Today => WarrantyStatus.Expired,
        { } end when end <= LastExpiringDay => WarrantyStatus.Expiring,
        _ => WarrantyStatus.Active,
    };

    /// <summary>Days from today to the expiry: 0 = ends today, negative = already expired; <c>null</c> when unknown.</summary>
    public int? DaysRemaining(DateOnly? expiry) => expiry?.DayNumber - Today.DayNumber;
}
