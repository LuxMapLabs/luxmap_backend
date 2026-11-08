using Google.Apis.Auth;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>Only this adapter contacts Google; tests replace the port through DI.</summary>
public sealed class GoogleIdTokenVerifier(GoogleLoginOptions options) : IGoogleIdTokenVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (options.ClientIds.Length == 0 || string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = options.ClientIds }).WaitAsync(ct);
            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Validation or certificate retrieval failed. Never log the exception: it may contain
            // credential material. Every failed verification is indistinguishable to the caller.
            return null;
        }
    }
}
