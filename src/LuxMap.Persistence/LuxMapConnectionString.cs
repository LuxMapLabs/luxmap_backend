using Npgsql;

namespace LuxMap.Persistence;

/// <summary>
/// Builds the connection string from exactly the environment variables declared in the BE-02
/// <c>.env</c>, so the port and password live in ONE place shared by Docker Compose and the app.
/// </summary>
public static class LuxMapConnectionString
{
    public const string DefaultHost = "localhost";

    /// <summary>5433 rather than 5432 — see the BE-02 docker-compose.yml.</summary>
    public const int DefaultPort = 5433;

    public const string DefaultDatabase = "luxmap_dev";
    public const string DefaultUser = "luxmap";

    public static string FromEnvironment()
    {
        // A whole-string override (CI, staging) wins before we assemble from parts.
        var whole = Environment.GetEnvironmentVariable("ConnectionStrings__LuxMap");
        if (!string.IsNullOrWhiteSpace(whole))
        {
            return whole;
        }

        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "POSTGRES_PASSWORD is not set. Run `cp .env.example .env` at the repository root, "
                + "or set the ConnectionStrings__LuxMap environment variable.");
        }

        return Build(
            host: Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? DefaultHost,
            port: ReadPort(),
            database: Environment.GetEnvironmentVariable("POSTGRES_DB") ?? DefaultDatabase,
            user: Environment.GetEnvironmentVariable("POSTGRES_USER") ?? DefaultUser,
            password: password);
    }

    public static string Build(string host, int port, string database, string user, string password)
        => new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = user,
            Password = password,
        }.ConnectionString;

    private static int ReadPort()
    {
        var raw = Environment.GetEnvironmentVariable("POSTGRES_PORT");
        return int.TryParse(raw, out var port) ? port : DefaultPort;
    }
}
