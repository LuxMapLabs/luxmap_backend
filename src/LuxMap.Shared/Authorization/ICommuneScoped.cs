namespace LuxMap.Shared.Authorization;

/// <summary>
/// Entity mang <c>commune_id</c> trực tiếp và PHẢI bị giới hạn theo phạm vi địa bàn của
/// người dùng (Contract mục 7).
/// </summary>
/// <remarks>
/// Khai interface này là một lời cam kết có chốt chặn: <c>LuxMapDbContext</c> kiểm lúc dựng model,
/// entity nào implement mà chưa gọi <c>HasCommuneScope()</c> thì <b>app không khởi động được</b>.
/// Quên scope trở thành lỗi ồn ào ngay lập tức thay vì lỗ rò im lặng.
/// <para>
/// ⚠️ Chốt chặn chỉ thấy được entity ĐÃ khai interface này. Entity có <c>commune_id</c> mà quên
/// khai thì không cơ chế nào bắt được — đó là giới hạn thật, phải chặn ở khâu review.
/// </para>
/// <para>
/// Entity phải suy ra commune qua nhiều bậc quan hệ (<c>SurveyFrame</c>, <c>TelemetryReading</c>)
/// KHÔNG khai interface này; chúng đi qua truy vấn tường minh có scope.
/// </para>
/// </remarks>
public interface ICommuneScoped
{
    string CommuneId { get; }
}
