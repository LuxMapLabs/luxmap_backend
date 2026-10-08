using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace LuxMap.Shared.Authorization;

/// <summary>
/// The device authentication scheme (LIGHT-CTRL LC-2, SELF-SIGNED): how an IoT device — not a person — proves who it is.
/// </summary>
/// <remarks>
/// <para>
/// Header <c>Authorization: Device &lt;node_id&gt;.&lt;secret&gt;</c>. The scheme is NOT the default (Bearer stays the
/// default), so no user endpoint ever authenticates with it; only endpoints naming <see cref="Policy"/> do.
/// </para>
/// <para>
/// The principal it issues carries <see cref="NodeIdClaim"/> and <c>commune_ids</c> = the device's ONE commune (never
/// <c>*</c>), and NO role claim — so the standard commune scope, query filter and <c>CommuneWriteGuard</c> apply, and no
/// capability in the matrix ever admits a device.
/// </para>
/// </remarks>
public static class DeviceAuth
{
    public const string Scheme = "Device";

    /// <summary>The only policy a device endpoint names: <see cref="Scheme"/> + authenticated + <see cref="NodeIdClaim"/>.</summary>
    public const string Policy = "DeviceOnly";

    public const string NodeIdClaim = "device_node_id";

    /// <summary>Longest header value accepted after <c>Device </c> — anything longer is refused before any lookup.</summary>
    public const int MaxCredentialLength = 128;
}

/// <summary>
/// A device's secret (LC-2): 32 random bytes, base64url (43 characters). Only its SHA-256 (UTF-8, hex) is stored — the same
/// shape as a refresh token, which a fast hash suits: the input cannot be brute-forced.
/// </summary>
public static class DeviceSecret
{
    public const int SecretBytes = 32;

    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>Compares the secret's digest with the stored hex digest in constant time. A malformed stored value never matches.</summary>
    public static bool Matches(string secret, string? storedHash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        byte[] stored;
        try
        {
            stored = storedHash is { Length: 64 } ? Convert.FromHexString(storedHash) : new byte[digest.Length];
        }
        catch (FormatException)
        {
            stored = new byte[digest.Length];
        }

        // Always one fixed-time compare, so a missing or malformed hash costs the same as a wrong secret.
        return CryptographicOperations.FixedTimeEquals(digest, stored) && storedHash is { Length: 64 };
    }
}
