namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// An asset that records who last changed it and when (drift POLE-NOTE N-4).
/// </summary>
/// <remarks>
/// Implemented by the four tables a person edits by form or by import: <see cref="RoadSegment"/>,
/// <see cref="Feeder"/>, <see cref="Pole"/> and <see cref="Fixture"/>. Stamped through
/// <c>AssetStamp.Touch</c> only — and only when a value really changed, so re-importing an identical
/// file leaves both fields as they were.
/// <para>
/// The LATEST writer only, not a history. <see cref="UpdatedBy"/> is <c>null</c> for rows the system
/// loaded (seed, mock set) and for rows nobody has touched since the column was added.
/// </para>
/// </remarks>
public interface IUpdateStamped
{
    /// <summary>The account that last changed the row, e.g. <c>USR-004</c>.</summary>
    string? UpdatedBy { get; set; }

    DateTime UpdatedAt { get; set; }
}
