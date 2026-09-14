namespace LuxMap.Modules.Identity.Auth.Web;

/// <summary>
/// The operation answers with <c>Set-Cookie</c> for <see cref="RefreshTokenCookie"/>.
/// </summary>
/// <remarks>
/// Documentation metadata only: the OpenAPI filter reads it so the generated spec shows the cookie,
/// which is otherwise invisible to it. Nothing at runtime reads this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SetsRefreshTokenCookieAttribute : Attribute;

/// <summary>
/// The operation is authenticated by <see cref="RefreshTokenCookie"/> rather than by an access token.
/// </summary>
/// <remarks>Documentation metadata only, like <see cref="SetsRefreshTokenCookieAttribute"/>.</remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReadsRefreshTokenCookieAttribute : Attribute;
