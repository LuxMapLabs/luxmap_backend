using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MQTTnet.Certificates;

// Serves the MQTT TLS certificate from PEM files that another process renews in place (Caddy on the VPS).
// MQTTnet asks for the certificate on every connection, so a renewed pair is picked up by the next gateway
// without a restart; files are re-read only when their timestamps change.
sealed class PemCertificateProvider(string certificatePath, string keyPath, ILogger logger) : ICertificateProvider
{
    private readonly object gate = new();
    private (DateTime CertificateWrite, DateTime KeyWrite, X509Certificate2 Certificate)? current;

    public X509Certificate2 GetCertificate()
    {
        lock (gate)
        {
            var certificateWrite = File.GetLastWriteTimeUtc(certificatePath);
            var keyWrite = File.GetLastWriteTimeUtc(keyPath);
            if (current is { } loaded && loaded.CertificateWrite == certificateWrite && loaded.KeyWrite == keyWrite)
                return loaded.Certificate;
            try
            {
                var certificate = Load();
                current = (certificateWrite, keyWrite, certificate);
                logger.LogInformation("MQTT TLS certificate loaded: {Subject}, valid until {NotAfter:o}",
                    certificate.Subject, certificate.NotAfter.ToUniversalTime());
                return certificate;
            }
            // A renewal caught between writing the certificate and the key leaves a mismatched pair for a moment:
            // keep serving the previous pair and retry on the next connection.
            catch (CryptographicException ex) when (current is { } previous)
            {
                logger.LogWarning(ex, "Cannot load the renewed MQTT TLS certificate; serving the previous one");
                return previous.Certificate;
            }
        }
    }

    private X509Certificate2 Load()
    {
        var bundle = new X509Certificate2Collection();
        bundle.ImportFromPemFile(certificatePath);
        if (bundle.Count == 0) throw new CryptographicException($"No certificate in {certificatePath}.");
        // The leaf comes first. Linux SslStream sends intermediates only when it can find them in a store, and a
        // gateway trusting just the root cannot finish the chain without them.
        using (var store = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadWrite);
            foreach (var intermediate in bundle.Skip(1)) store.Add(intermediate);
        }
        using var pem = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
        // An ephemeral key cannot be used by SslStream on every platform; a PKCS#12 round trip gives a persisted one.
        return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
    }
}
