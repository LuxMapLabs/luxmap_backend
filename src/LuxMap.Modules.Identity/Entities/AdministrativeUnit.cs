namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// Đơn vị hành chính cấp xã. ID dạng <c>COM-001</c> (Contract mục 0.2), là giá trị mà
/// <c>commune_id</c> trên Pole, Fault... trỏ tới và là đơn vị phân quyền theo địa bàn (mục 7).
/// </summary>
/// <remarks>
/// Chưa có cột ranh giới polygon: phân quyền mục 7 dựa trên claim <c>commune_ids</c> khớp cột
/// <c>commune_id</c> chứ không dùng phép chứa không gian, và Nhánh C không có nguồn ranh giới
/// thật. Thêm cột nullable về sau chỉ là một migration nhỏ.
/// </remarks>
public class AdministrativeUnit
{
    /// <summary>Do DB sinh qua sequence — không tự đặt khi insert.</summary>
    public string CommuneId { get; set; } = null!;

    public required string Name { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<AppUserCommune> UserAssignments { get; set; } = [];
}
