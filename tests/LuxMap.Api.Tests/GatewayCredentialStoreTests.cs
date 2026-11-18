using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LuxMap.Api.Tests;

public sealed class GatewayCredentialStoreTests
{
    [Fact]
    public void New_key_survives_restart_and_revoked_key_cannot_authenticate()
    {
        var root = Path.Combine(Path.GetTempPath(), "luxmap-credential-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestEnvironment(root);
            var keys = new DirectoryInfo(Path.Combine(root, "keyring"));
            var credentials = new GatewayCredentials(environment, DataProtectionProvider.Create(keys));
            var secret = credentials.Issue("GW-TEST")!;
            Assert.Equal(64, secret.Length);
            Assert.True(credentials.Validate("GW-TEST", "GW-TEST", secret));
            Assert.DoesNotContain(secret, File.ReadAllText(Path.Combine(root, "data", "gateway-credentials.json")));
            var restarted = new GatewayCredentials(environment, DataProtectionProvider.Create(keys));
            Assert.Equal(secret, restarted.Retrieve("GW-TEST").Secret);
            Assert.Null(restarted.Issue("GW-TEST"));
            Assert.True(restarted.Revoke("GW-TEST"));
            Assert.False(restarted.Revoke("GW-TEST"));
            Assert.False(restarted.Validate("GW-TEST", "GW-TEST", secret));
            Assert.False(restarted.Retrieve("GW-TEST").Issued);
            var replacement = restarted.Issue("GW-TEST")!;
            Assert.NotEqual(secret, replacement);
            Assert.False(restarted.Validate("GW-TEST", "GW-TEST", secret));
            Assert.True(restarted.Validate("GW-TEST", "GW-TEST", replacement));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Legacy_hash_remains_usable_until_explicit_revocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "luxmap-credential-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        try
        {
            var secret = new string('A', 64);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
            File.WriteAllText(Path.Combine(root, "data", "gateway-credentials.json"),
                JsonSerializer.Serialize(new Dictionary<string, string> { ["GW-OLD"] = hash }));
            var credentials = new GatewayCredentials(new TestEnvironment(root),
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keyring"))));
            Assert.True(credentials.Validate("GW-OLD", "GW-OLD", secret));
            Assert.True(credentials.Retrieve("GW-OLD").Issued);
            Assert.Null(credentials.Retrieve("GW-OLD").Secret);
            credentials.Revoke("GW-OLD");
            Assert.NotNull(credentials.Issue("GW-OLD"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "LuxMap.IotBridge";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
