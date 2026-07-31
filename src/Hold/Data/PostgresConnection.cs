using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Hold.Data;

public static class PostgresConnection
{
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Hold");

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "No database configured. Set the DATABASE_URL environment variable (the form "
                + "Neon and Render give you) or the ConnectionStrings:Hold setting.");
        }

        return Normalise(configured);
    }

    public static string Normalise(string value)
    {
        var trimmed = value.Trim();

        return IsUrl(trimmed) ? FromUrl(trimmed) : trimmed;
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    private static string FromUrl(string url)
    {
        var uri = new Uri(url);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = uri.AbsolutePath.Trim('/'),
        };

        if (uri.Port > 0 && !uri.IsDefaultPort)
        {
            builder.Port = uri.Port;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var credentials = uri.UserInfo.Split(':', 2);

            builder.Username = Uri.UnescapeDataString(credentials[0]);

            if (credentials.Length is 2)
            {
                builder.Password = Uri.UnescapeDataString(credentials[1]);
            }
        }

        builder.SslMode = SslModeFrom(QueryValue(uri.Query, "sslmode"), uri.Host);

        builder.MaxPoolSize = 20;

        return builder.ConnectionString;
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');

            if (separator > 0
                && pair.AsSpan(0, separator).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }

    private static SslMode SslModeFrom(string? sslmode, string host) =>
        sslmode?.Trim().ToLowerInvariant() switch
        {
            "disable" => SslMode.Disable,
            "allow" => SslMode.Allow,
            "prefer" => SslMode.Prefer,
            "require" => SslMode.Require,
            "verify-ca" => SslMode.VerifyCA,
            "verify-full" => SslMode.VerifyFull,

            null or "" => IsLocal(host) ? SslMode.Disable : SslMode.Require,
            _ => SslMode.Require,
        };

    private static bool IsLocal(string host) =>
        host is "localhost" or "127.0.0.1" or "::1" or "postgres";
}
