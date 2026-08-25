using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxMap.Shared.Serialization;

/// <summary>
/// Nguồn sự thật duy nhất cho quy ước JSON của contract (mục 0 và mục 5.5).
/// Mọi host — minimal API, MVC controller, Hangfire job, test — phải đi qua <see cref="Configure"/>.
/// </summary>
public static class LuxMapJsonOptions
{
    /// <summary>Bản dùng sẵn (đã đóng băng) cho serialize thủ công ngoài pipeline HTTP.</summary>
    public static JsonSerializerOptions Default { get; } = CreateDefault();

    public static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Mục 0: định dạng JSON snake_case.
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.PropertyNameCaseInsensitive = true;

        // Mục 5.5: enum trả chuỗi thường, KHÔNG trả số — int enum của .NET sẽ làm hỏng FE.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        // Mục 0: ISO 8601 UTC hậu tố Z. DateOnly (install_date, warranty_expiry, night_of)
        // đã ra đúng YYYY-MM-DD theo mặc định của System.Text.Json — không cần converter.
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new UtcDateTimeOffsetConverter());
    }
}
