using System.Text.Json.Nodes;
using LuxMap.Modules.Identity.Accounts;
using LuxMap.Modules.Identity.Auth;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>Personal-details wire values, including response fields and explicit PATCH nulls.</summary>
public sealed class UserPersonalDetailsSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!typeof(UserPersonalDetailsRequest).IsAssignableFrom(context.Type) &&
            context.Type != typeof(UserAccountItem) && context.Type != typeof(CurrentUserResponse)) return;

        void Describe(string name, string description, params string[] values)
        {
            if (schema.Properties is not { } properties || !properties.TryGetValue(name, out var field) ||
                field is not OpenApiSchema property) return;
            property.Description = description;
            if (values.Length > 0)
                property.Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).Append(null!).ToList();
        }
        Describe("date_of_birth", "Nullable YYYY-MM-DD; future dates rejected against the server UTC date. PATCH omission preserves; null clears.");
        Describe("gender", "Nullable gender. PATCH omission preserves; null clears.", "male", "female", "other");
        Describe("address", "Personal address, max 500 characters; does not change commune scope.");
        Describe("phone_number", "7–15 digits with optional leading +. Spaces, dots, hyphens and parentheses removed; not unique.");
        Describe("identity_type", "Must be set or cleared together with identity_number.", "citizen_id", "identity_card", "passport", "other");
        Describe("identity_number", "Max 64 characters, trimmed and uppercased; unique per identity_type. Masked in admin list, full in admin detail and own profile.");
    }
}
