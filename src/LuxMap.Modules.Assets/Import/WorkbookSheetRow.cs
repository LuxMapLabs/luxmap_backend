using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets.Import;

/// <summary>Supplies upload context to the existing planners without renaming CSV columns.</summary>
internal sealed class WorkbookSheetRow(
    WorkbookDocument.SheetRow source, string commune, string dataSource,
    HashSet<(ImportKind Kind, string? Commune, string? Ref)> failed) : IImportRow
{
    public int Row => source.Number;
    public bool HasErrors { get; set; }
    public string? ReferenceCommune => commune;
    public string? this[string column] => column switch
    {
        "commune_id" => commune, "data_source" => dataSource, _ => source.Cells.GetValueOrDefault(column),
    };
    public bool IsNonScalar(string column) => false;
    public bool KeepEmptyGeometry => true;
    public bool RequireLocationNote => true;
    public bool GeometryIsEmpty => this["geom_wkt"] is null;
    public bool TryGeometry(out Geometry? geometry, out string? error)
        => AssetGeometry.TryReadWkt(this["geom_wkt"], out geometry, out error);
    public bool ReferenceFailed(string column)
    {
        var parent = column switch
        {
            "segment_external_ref" => ImportKind.Segments, "cabinet_external_ref" => ImportKind.Cabinets,
            "feeder_external_ref" => ImportKind.Feeders, _ => ImportKind.Poles,
        };
        return failed.Contains((parent, commune, this[column]));
    }
}
