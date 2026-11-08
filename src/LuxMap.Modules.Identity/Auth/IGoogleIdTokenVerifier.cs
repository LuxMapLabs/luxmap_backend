namespace LuxMap.Modules.Identity.Auth;

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified);

public interface IGoogleIdTokenVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct);
}
