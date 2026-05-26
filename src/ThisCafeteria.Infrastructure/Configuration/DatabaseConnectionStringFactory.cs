using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ThisCafeteria.Infrastructure.Configuration;

public static class DatabaseConnectionStringFactory
{
    public static string? Resolve(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var host = configuration["DB_HOST"];
        var database = configuration["DB_NAME"];
        var username = configuration["DB_USERNAME"];
        var password = configuration["DB_PASSWORD"];

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var port = int.TryParse(configuration["DB_PORT"], out var parsedPort)
            ? parsedPort
            : 5432;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };

        return builder.ConnectionString;
    }
}
