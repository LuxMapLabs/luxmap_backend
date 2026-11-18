namespace LuxMap.Modules.Assets.Import;

/// <summary>WB-12: sheet names and CSV column vocabulary, shared by import and export.</summary>
public static class WorkbookSheets
{
    public sealed record Layout(ImportKind Kind, string Name, string[] Columns, string[] Required);
    public static IReadOnlyList<Layout> All { get; } = [
        new(ImportKind.Segments, "Tuyến đường", ["external_ref", "segment_name", "road_class", "length_m", "geom_wkt"], ["external_ref", "segment_name", "road_class"]),
        new(ImportKind.Cabinets, "Tủ điện", ["external_ref", "cabinet_name", "geom_wkt"], ["external_ref", "cabinet_name", "geom_wkt"]),
        new(ImportKind.Feeders, "Mạch điện", ["external_ref", "feeder_name", "cabinet_external_ref", "cabinet_source", "geom_wkt"], ["external_ref", "feeder_name"]),
        new(ImportKind.Poles, "Cột điện", ["external_ref", "segment_external_ref", "feeder_external_ref", "feeder_source", "geom_wkt", "near_sensitive_poi", "note"], ["external_ref"]),
        new(ImportKind.Fixtures, "Bóng đèn", ["pole_external_ref", "fixture_type", "power_source", "lamp_watt", "install_date", "lamp_count", "removed_date", "warranty_expiry", "note"], ["pole_external_ref", "fixture_type", "power_source", "lamp_watt", "install_date"]),
    ];
    public static string Name(ImportKind kind) => All.Single(s => s.Kind == kind).Name;
}
