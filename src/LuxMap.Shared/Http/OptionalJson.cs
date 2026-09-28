using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LuxMap.Shared.Http;

/// <summary>Preserves absent, explicit null and supplied values for partial requests.</summary>
public static class OptionalJson
{
    public static bool Present(JsonElement value) => value.ValueKind != JsonValueKind.Undefined;

    public static string? Text(JsonElement value, string field, bool nullable = true)
    {
        if (!Present(value) || (nullable && value.ValueKind == JsonValueKind.Null)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        throw Invalid(field);
    }

    public static DateOnly? Date(JsonElement value, string field)
    {
        if (!Present(value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(),
            "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        throw Invalid(field);
    }

    public static LuxMapException Invalid(string field) => new("VALIDATION_FAILED", HttpStatusCode.BadRequest,
        "Invalid request field.", new Dictionary<string, object?> { ["field"] = field });
}
