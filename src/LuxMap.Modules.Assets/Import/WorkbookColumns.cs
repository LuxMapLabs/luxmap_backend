namespace LuxMap.Modules.Assets.Import;

/// <summary>The ordered WB-2 header shared by workbook import and export.</summary>
public static class WorkbookColumns
{
    public sealed record Column(string Name, bool Required = false);

    public static IReadOnlyList<Column> All { get; } = Array.AsReadOnly<Column>([
        new("commune_id", true), new("data_source", true),
        new("segment_ref"), new("segment_name"), new("road_class"), new("segment_length_m"),
        new("cabinet_ref"), new("cabinet_name"), new("cabinet_lat"), new("cabinet_lng"),
        new("feeder_ref"), new("feeder_name"), new("cabinet_source"),
        new("pole_ref", true), new("pole_lat"), new("pole_lng"), new("near_sensitive_poi"), new("pole_note"), new("feeder_source"),
        new("fixture_type"), new("power_source"), new("lamp_watt"), new("lamp_count"),
        new("install_date"), new("warranty_expiry"), new("fixture_note"),
    ]);
}
