using Serilog.Core;
using Serilog.Events;

namespace LuxMap.Api.Observability;

/// <summary>
/// Chặn cứng các trường nhạy cảm ở tầng ghi log, thay vì trông chờ mọi lời gọi log
/// đều nhớ không truyền chúng vào. Bất kỳ property nào có tên khớp danh sách dưới đây
/// đều bị thay bằng <c>***</c> trước khi sink nhìn thấy.
/// </summary>
public sealed class SensitivePropertyScrubber : ILogEventEnricher
{
    private const string Mask = "***";

    /// <summary>So khớp theo chuỗi con, không phân biệt hoa thường — bắt được cả
    /// <c>Authorization</c>, <c>RequestHeaders.Authorization</c>, <c>DbPassword</c>...</summary>
    private static readonly string[] SensitiveFragments =
    [
        "authorization",
        "token",
        "password",
        "pwd",
        "secret",
        "apikey",
        "api_key",
        "connectionstring",
        "connection_string",
        "cookie",
        "clientsecret",
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Chụp danh sách tên trước khi sửa, tránh sửa collection đang duyệt.
        var offenders = logEvent.Properties.Keys.Where(IsSensitive).ToArray();

        foreach (var name in offenders)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Mask)));
        }
    }

    public static bool IsSensitive(string propertyName)
        => SensitiveFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
