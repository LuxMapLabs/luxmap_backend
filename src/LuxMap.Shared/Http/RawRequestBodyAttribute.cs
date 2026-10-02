namespace LuxMap.Shared.Http;

/// <summary>
/// The action reads its body straight from the request stream, so the API explorer sees no body
/// parameter. Declares the body for the OpenAPI document only; binding is untouched.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RawRequestBodyAttribute(params string[] contentTypes) : Attribute
{
    public IReadOnlyList<string> ContentTypes { get; } = contentTypes;
}
