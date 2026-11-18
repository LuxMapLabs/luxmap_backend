using System.Globalization;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets.Import;

/// <summary>Projects sheet names into the existing planner vocabulary without copying its validation.</summary>
internal sealed class WorkbookImportRow(
    WorkbookDocument.SheetRow source, ImportKind kind,
    HashSet<(ImportKind Kind, string? Commune, string? Ref)> failedReferences) : IImportRow
{
    public int Row => source.Number;
    public bool HasErrors { get; set; }
    public string? ReferenceCommune => this["commune_id"];
    public string? this[string column] => ColumnName(column) is { } name ? source.Cells.GetValueOrDefault(name) : null;
    public bool IsNonScalar(string column) => false;
    public string? ColumnName(string? column) => column switch
    {
        "external_ref" => kind switch
        {
            ImportKind.Segments => "segment_ref", ImportKind.Cabinets => "cabinet_ref",
            ImportKind.Feeders => "feeder_ref", _ => "pole_ref",
        },
        "segment_external_ref" => "segment_ref",
        "cabinet_external_ref" => "cabinet_ref",
        "feeder_external_ref" => "feeder_ref",
        "pole_external_ref" => "pole_ref",
        "length_m" => "segment_length_m",
        "note" => kind == ImportKind.Fixtures ? "fixture_note" : "pole_note",
        // The sheet carries no route for segments or feeders: their geom_wkt must read as blank ("keep"), never as the pole's latitude.
        "geom_wkt" => kind switch { ImportKind.Cabinets => "cabinet_lat", ImportKind.Poles => "pole_lat", _ => null },
        _ => column,
    };

    public bool ReferenceFailed(string column)
    {
        var parent = column switch
        {
            "segment_external_ref" => ImportKind.Segments, "cabinet_external_ref" => ImportKind.Cabinets,
            "feeder_external_ref" => ImportKind.Feeders, _ => ImportKind.Poles,
        };
        return failedReferences.Contains((parent, this["commune_id"], this[column]));
    }

    public bool GeometryIsEmpty => kind is ImportKind.Segments or ImportKind.Feeders
        || (source.Cells.GetValueOrDefault(Prefix + "_lat") is null && source.Cells.GetValueOrDefault(Prefix + "_lng") is null);
    private string Prefix => kind == ImportKind.Cabinets ? "cabinet" : "pole";

    public bool TryGeometry(out Geometry? geometry, out string? error)
    {
        geometry = null;
        error = "Both latitude and longitude must be finite numbers in [-90, 90] and [-180, 180].";
        if (!double.TryParse(source.Cells.GetValueOrDefault(Prefix + "_lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(source.Cells.GetValueOrDefault(Prefix + "_lng"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
            || !double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180)
            return false;
        geometry = new Point(lng, lat) { SRID = 4326 };
        error = null;
        return true;
    }
}
