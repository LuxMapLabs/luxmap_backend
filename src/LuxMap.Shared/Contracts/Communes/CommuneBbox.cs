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

    /// <summary>The midpoint — where the map opens. Degrees are fine at commune size; no commune spans the antimeridian.</summary>
    public MapCenter Center() => new((MinLat + MaxLat) / 2, (MinLng + MaxLng) / 2);

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
public sealed record MapBounds(CommuneBbox Bbox, MapCenter Center)
{
    public static MapBounds? Of(CommuneBbox? bbox) => bbox is null ? null : new MapBounds(bbox, bbox.Center());
}

/// <summary>One commune as a signed-in user sees it: its name and its map frame (<c>null</c> until an administrator sets one).</summary>
public sealed record CommuneSummary(string CommuneId, string Name, CommuneBbox? Bbox, MapCenter? Center);
