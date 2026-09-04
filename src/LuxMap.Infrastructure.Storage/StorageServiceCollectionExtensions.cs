using Amazon.Runtime;
using Amazon.S3;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// Registers object storage. Called from <c>Program.cs</c> beside <c>AddLuxMapPersistence</c>.
/// </summary>
/// <remarks>
/// BE-11 is infrastructure shared by two modules that do not exist yet (Survey at W5, WorkOrders at
/// W11), so it is registered by the host rather than through <c>ILuxMapModule</c> — the same shape as
/// persistence, and for the same reason: it belongs to no single module.
/// </remarks>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddLuxMapObjectStorage(
        this IServiceCollection services, StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);

        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.Endpoint,

                // MinIO addresses buckets by path, not by subdomain. Left at the AWS default this
                // would try to resolve luxmap-survey.localhost and fail with a DNS error that says
                // nothing about the real cause.
                ForcePathStyle = options.ForcePathStyle,

                // A region is required by the signing algorithm even though MinIO ignores its value.
                AuthenticationRegion = "us-east-1",
            }));

        services.AddSingleton<IObjectStore, S3ObjectStore>();

        return services;
    }
}
