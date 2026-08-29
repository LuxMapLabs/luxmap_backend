namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Cấu hình phát JWT. Giá trị không bí mật nằm ở <c>appsettings.json</c> mục <c>Jwt</c>;
/// khoá ký KHÔNG nằm ở đó — xem <see cref="SigningKey"/>.
/// </summary>
public sealed record JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Biến môi trường chứa khoá ký, nạp từ <c>.env</c> như mật khẩu DB ở BE-03.</summary>
    public const string SigningKeyEnvironmentVariable = "JWT_SIGNING_KEY";

    /// <summary>HS256 cần khoá tối thiểu 256 bit.</summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>BE-08 so chuỗi CHÍNH XÁC. Lệch một ký tự là mọi request bị từ chối.</summary>
    public string Issuer { get; init; } = "luxmap-api";

    public string Audience { get; init; } = "luxmap-clients";

    /// <summary>Access token sống 60 phút — giá trị này đi thẳng vào <c>expires_in</c>.</summary>
    public int AccessTokenMinutes { get; init; } = 60;

    /// <summary>Refresh token trượt: mỗi lần xoay vòng lại được 30 ngày kể từ lúc đó.</summary>
    public int RefreshSlidingDays { get; init; } = 30;

    /// <summary>Trần tuyệt đối kể từ lần đăng nhập đầu. Xoay vòng không đẩy mốc này ra xa.</summary>
    public int RefreshAbsoluteDays { get; init; } = 90;

    /// <summary>
    /// Dùng lại token vừa xoay vòng trong bấy nhiêu giây được coi là retry lành tính.
    /// Vùng sóng yếu retry là hoạt động bình thường, cùng tinh thần với <c>client_op_id</c>.
    /// </summary>
    public int ReuseGraceSeconds { get; init; } = 30;

    /// <summary>Khoá ký HS256. Không hard-code, không commit, không có giá trị mặc định.</summary>
    public required string SigningKey { get; init; }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan ReuseGraceWindow => TimeSpan.FromSeconds(ReuseGraceSeconds);

    /// <summary>Thiếu khoá hoặc khoá quá ngắn thì DỪNG ngay lúc khởi động, không chạy tiếp.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            throw new InvalidOperationException(
                $"Thiếu {SigningKeyEnvironmentVariable}. Chạy `cp .env.example .env` ở thư mục gốc repo "
                + "rồi đặt một khoá ngẫu nhiên đủ dài.");
        }

        var bytes = System.Text.Encoding.UTF8.GetByteCount(SigningKey);
        if (bytes < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"{SigningKeyEnvironmentVariable} chỉ dài {bytes} byte, HS256 cần tối thiểu "
                + $"{MinimumSigningKeyBytes} byte.");
        }
    }
}
