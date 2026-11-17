namespace LuxMap.Shared.Contracts.Communes;

/// <summary>
/// The map frame of a commune (COMMUNE-BBOX), EPSG:4326 degrees. A VIEWPORT, not a boundary: authorization stays on
/// <c>commune_id</c> (Contract section 7) and nothing is filtered or refused for lying outside it.
/// </summary>
public sealed record CommuneBbox(double MinLng, double MinLat, double MaxLng, double MaxLat)
{
    /// <summary>Finite, within ±180 / ±90, and min strictly below max on both axes.</summary>
    public bool IsValid()
        => Within(MinLng, 180) && Within(MaxLng, 180) && Within(MinLat, 90) && Within(MaxLat, 90)
            && MinLng < MaxLng && MinLat < MaxLat;

    /// <summary>
    /// The midpoint — the admin item's <c>center</c>, and where the map opens when no pole is known (<see cref="MapFocus"/>).
    /// Degrees are fine at commune size; no commune spans the antimeridian.
    /// </summary>
    public MapCenter Center() => new((MinLat + MaxLat) / 2, (MinLng + MaxLng) / 2);

    /// <summary>The point moved, axis by axis, to the nearest place inside the frame — a map held by it can open there.</summary>
    public MapCenter Clamp(MapCenter point)
        => new(Math.Clamp(point.Lat, MinLat, MaxLat), Math.Clamp(point.Lng, MinLng, MaxLng));

    /// <summary>The smallest box holding every box given, or <c>null</c> when none is given.</summary>
    public static CommuneBbox? Union(IEnumerable<CommuneBbox> boxes)
    {
        CommuneBbox? union = null;
        foreach (var box in boxes)
        {
            union = union is null ? box : new CommuneBbox(
                Math.Min(union.MinLng, box.MinLng), Math.Min(union.MinLat, box.MinLat),
                Math.Max(union.MaxLng, box.MaxLng), Math.Max(union.MaxLat, box.MaxLat));
        }

        return union;
    }

    private static bool Within(double value, double limit) => double.IsFinite(value) && Math.Abs(value) <= limit;
}

/// <summary>A point to centre the map on, EPSG:4326.</summary>
public sealed record MapCenter(double Lat, double Lng);

/// <summary>Where a user's map is held (<c>bbox</c>, for <c>maxBounds</c>) and where it opens (<c>center</c>).</summary>
public sealed record MapBounds(CommuneBbox Bbox, MapCenter Center);

/// <summary>One commune as a signed-in user sees it: its name and its map frame (<c>null</c> until an administrator sets one).</summary>
public sealed record CommuneSummary(string CommuneId, string Name, CommuneBbox? Bbox, MapCenter? Center);

/// <summary>The mean position of a commune's poles that have one, and how many there are (MAP-CENTER).</summary>
public sealed record PoleCenter(double Lat, double Lng, int Count);

/// <summary>
/// MAP-CENTER — where a user's map OPENS: on the poles, not on the middle of the frame. A frame drawn around a whole
/// ward's boundary has its middle wherever the ward is widest, often kilometres from any lamp; the poles are what a user
/// came to look at. The frame still holds the map, so the centre is kept inside it.
/// </summary>
public static class MapFocus
{
    /// <summary>
    /// One commune: the mean of its poles, held inside its frame; the frame's midpoint when no pole has a position;
    /// <c>null</c> without a frame (there is nothing to hold the map to).
    /// </summary>
    public static MapCenter? Center(CommuneBbox? bbox, PoleCenter? poles)
        => bbox is null ? null
            : poles is { Count: > 0 } ? bbox.Clamp(new MapCenter(poles.Lat, poles.Lng))
            : bbox.Center();

    /// <summary>
    /// Several communes: the box around every frame, opening on the mean of the poles of the FRAMED communes — each weighted
    /// by its pole count, so a commune with three poles does not pull the map halfway to it — held inside that box.
    /// <c>null</c> when no commune has a frame.
    /// </summary>
    public static MapBounds? Bounds(IEnumerable<(CommuneBbox? Bbox, PoleCenter? Poles)> communes)
    {
        var framed = communes.Where(commune => commune.Bbox is not null).ToList();
        var union = CommuneBbox.Union(framed.Select(commune => commune.Bbox!));
        if (union is null)
        {
            return null;
        }

        var poles = framed.Where(commune => commune.Poles is { Count: > 0 }).Select(commune => commune.Poles!).ToList();
        long count = poles.Sum(pole => (long)pole.Count);
        var center = count == 0
            ? union.Center()
            : union.Clamp(new MapCenter(
                poles.Sum(pole => pole.Lat * pole.Count) / count, poles.Sum(pole => pole.Lng * pole.Count) / count));
        return new MapBounds(union, center);
    }
}
