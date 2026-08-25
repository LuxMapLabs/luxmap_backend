using Npgsql;

namespace LuxMap.Persistence;

/// <summary>
/// Dựng connection string từ đúng những biến môi trường mà <c>.env</c> của BE-02 đã khai,
/// để cổng và mật khẩu chỉ tồn tại ở MỘT chỗ cho cả docker compose lẫn ứng dụng.
/// </summary>
public static class LuxMapConnectionString
{
    public const string DefaultHost = "localhost";

    /// <summary>5433 chứ không phải 5432 — xem docker-compose.yml của BE-02.</summary>
    public const int DefaultPort = 5433;

    public const string DefaultDatabase = "luxmap_dev";
    public const string DefaultUser = "luxmap";

    public static string FromEnvironment()
    {
        // Cho phép ghi đè trọn gói (CI, staging) trước khi ghép từ từng mảnh.
        var whole = Environment.GetEnvironmentVariable("ConnectionStrings__LuxMap");
        if (!string.IsNullOrWhiteSpace(whole))
        {
            return whole;
        }

        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Thiếu POSTGRES_PASSWORD. Chạy `cp .env.example .env` ở thư mục gốc repo, " +
                "hoặc set biến môi trường ConnectionStrings__LuxMap.");
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
