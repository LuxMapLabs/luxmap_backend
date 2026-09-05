namespace LuxMap.Modules.Assets.Entities;

/// <summary>
/// An asset that can carry the owning authority's own inventory code.
/// </summary>
/// <remarks>
/// Implemented by <see cref="RoadSegment"/>, <see cref="Feeder"/> and <see cref="Pole"/> — the three
/// tables a CSV file addresses by name. <see cref="Fixture"/> deliberately does NOT implement it:
/// one pole carries several lamps over its life, so no external code can identify a single
/// installation, and fixture import is INSERT-ONLY rather than an upsert.
/// <para>
/// The interface exists so the column and its partial unique index are declared exactly once
/// (<c>ExternalRefColumn.HasExternalRef</c>) instead of three times that could drift apart.
/// </para>
/// </remarks>
public interface IExternallyReferenced
{
    string? ExternalRef { get; set; }
}
