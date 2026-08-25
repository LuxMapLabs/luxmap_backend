namespace LuxMap.Persistence;

public static class SpatialConstants
{
    /// <summary>
    /// WGS84. Contract mục 0: API luôn trả EPSG:4326, mọi cột hình học lưu ở SRID này.
    /// </summary>
    public const int Srid = 4326;

    /// <summary>
    /// VN-2000 / UTM zone 48N. Contract mục 0: CHỈ dùng nội bộ DB và xuất báo cáo,
    /// không bao giờ trả ra API. Hằng số khai ở đây để không ai gõ số magic;
    /// việc chuyển đổi chưa thuộc phạm vi BE-03.
    /// </summary>
    public const int SridVn2000 = 3405;
}
