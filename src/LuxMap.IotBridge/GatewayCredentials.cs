using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;

sealed class GatewayCredentials(IHostEnvironment environment, IDataProtectionProvider protection)
{
    private readonly string path = Path.Combine(environment.ContentRootPath, "data", "gateway-credentials.json");
    private readonly IDataProtector protector = protection.CreateProtector("LuxMap.GatewaySecret.v1");
    private readonly object gate = new();

    public string? Issue(string gatewayId)
    {
        lock (gate)
        {
            var records = Read();
            if (records.ContainsKey(gatewayId)) return null;
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            records.Add(gatewayId, new(Convert.ToHexString(Hash(secret)), protector.Protect(secret)));
            Save(records);
            return secret;
        }
    }

    public (bool Issued, string? Secret) Retrieve(string gatewayId)
    {
        lock (gate)
        {
            if (!Read().TryGetValue(gatewayId, out var record)) return (false, null);
            return (true, record.ProtectedSecret is { } encrypted ? protector.Unprotect(encrypted) : null);
        }
    }

    public bool Revoke(string gatewayId)
    {
        lock (gate)
        {
            var records = Read();
            if (!records.Remove(gatewayId)) return false;
            Save(records);
            return true;
        }
    }

    internal string? Fingerprint(string gatewayId)
    {
        lock (gate) return Read().GetValueOrDefault(gatewayId)?.Hash;
    }

    public bool IsIssued(string gatewayId)
    {
        lock (gate) return Read().ContainsKey(gatewayId);
    }

    public bool Validate(string gatewayId, string? username, string? password)
    {
        if (username != gatewayId || password is not { Length: 64 }) return false;
        lock (gate)
            return Read().TryGetValue(gatewayId, out var record) &&
                CryptographicOperations.FixedTimeEquals(Convert.FromHexString(record.Hash), Hash(password));
    }

    public static bool MatchesAdminKey(string? configured, string supplied) =>
        configured is { Length: >= 32 } && supplied.Length == configured.Length &&
        CryptographicOperations.FixedTimeEquals(Hash(configured), Hash(supplied));

    private Dictionary<string, StoredCredential> Read()
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String
                ? new StoredCredential(property.Value.GetString()!, null)
                : property.Value.Deserialize<StoredCredential>() ?? throw new InvalidDataException("Invalid gateway credentials."),
            StringComparer.Ordinal);
    }

    private void Save(Dictionary<string, StoredCredential> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = File.Create(path + ".tmp"))
        {
            JsonSerializer.Serialize(file, records);
            file.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private sealed record StoredCredential(string Hash, string? ProtectedSecret);
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
