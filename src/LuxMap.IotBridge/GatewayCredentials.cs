using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

sealed class GatewayCredentials(IHostEnvironment environment)
{
    private readonly string path = Path.Combine(environment.ContentRootPath, "data", "gateway-credentials.json");
    private readonly object gate = new();

    public string? Issue(string gatewayId)
    {
        lock (gate)
        {
            var hashes = Read();
            if (hashes.ContainsKey(gatewayId)) return null;
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            hashes.Add(gatewayId, Convert.ToHexString(Hash(secret)));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = File.Create(path + ".tmp"))
            {
                JsonSerializer.Serialize(file, hashes);
                file.Flush(flushToDisk: true);
            }
            File.Move(path + ".tmp", path, overwrite: true);
            return secret;
        }
    }

    public bool Validate(string gatewayId, string? username, string? password)
    {
        if (username != gatewayId || password is not { Length: 64 }) return false;
        lock (gate)
            return Read().TryGetValue(gatewayId, out var hash) &&
                CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Hash(password));
    }

    public static bool MatchesAdminKey(string? configured, string supplied) =>
        configured is { Length: >= 32 } && supplied.Length == configured.Length &&
        CryptographicOperations.FixedTimeEquals(Hash(configured), Hash(supplied));

    private Dictionary<string, string> Read() => File.Exists(path)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid gateway credentials.")
        : new(StringComparer.Ordinal);

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
