using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Hold.Data;

/// <summary>
/// Works out which Postgres to talk to, and puts the answer in the form Npgsql expects.
///
/// Hosts hand out a URL — <c>postgresql://user:pass@host/dbname?sslmode=require</c> — and Npgsql
/// accepts only key-value form, <c>Host=…;Username=…</c>. It does not fail with anything helpful
/// when handed a URL, so the translation lives here, in one place, where it can be tested
/// without a database.
/// </summary>
public static class PostgresConnection
{
    /// <summary>
    /// DATABASE_URL first, because that is the name Render and Neon use and what an operator
    /// will reach for. The Hold connection string second, which is how local development is
    /// configured. Nothing after that — there is deliberately no built-in fallback, so a
    /// misconfigured deployment stops at startup rather than quietly running against the wrong
    /// database.
    /// </summary>
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

    /// <summary>
    /// Passes key-value connection strings through untouched and converts URLs.
    /// </summary>
    public static string Normalise(string value)
    {
        var trimmed = value.Trim();

        return IsUrl(trimmed) ? FromUrl(trimmed) : trimmed;
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Built through NpgsqlConnectionStringBuilder rather than by joining strings, so a password
    /// containing a semicolon or a quote is escaped by the library that has to parse it back.
    /// </summary>
    private static string FromUrl(string url)
    {
        var uri = new Uri(url);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = uri.AbsolutePath.Trim('/'),
        };

        // A URL without an explicit port reports 5432 anyway, so only a real one is worth
        // setting. Uri.Port is -1 when the scheme is unknown to it.
        if (uri.Port > 0 && !uri.IsDefaultPort)
        {
            builder.Port = uri.Port;
        }

        // Percent-decoded: a generated password can legitimately contain %, @ or :, and it
        // arrives encoded.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var credentials = uri.UserInfo.Split(':', 2);

            builder.Username = Uri.UnescapeDataString(credentials[0]);

            if (credentials.Length is 2)
            {
                builder.Password = Uri.UnescapeDataString(credentials[1]);
            }
        }

        // Only sslmode is translated. Other parameters a host may append — channel_binding,
        // options, application_name — are left alone rather than mapped onto guessed property
        // names, which would turn a working URL into a startup crash.
        builder.SslMode = SslModeFrom(QueryValue(uri.Query, "sslmode"), uri.Host);

        // Neon's free tier sleeps when idle and allows far fewer connections than Npgsql's
        // default ceiling of 100. A Blazor Server circuit holds no connection between renders,
        // so a small pool is enough, and staying well under the limit matters more.
        builder.MaxPoolSize = 20;

        return builder.ConnectionString;
    }

    /// <summary>
    /// One parameter out of a query string. Hand-rolled rather than pulled from System.Web or
    /// ASP.NET, so this file stays usable anywhere the entities are.
    /// </summary>
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

    /// <summary>
    /// libpq spells these with hyphens; Npgsql spells them in Pascal case. Anything unrecognised
    /// falls through to Require, because a managed Postgres reached over the internet should not
    /// end up unencrypted through a typo.
    /// </summary>
    private static SslMode SslModeFrom(string? sslmode, string host) =>
        sslmode?.Trim().ToLowerInvariant() switch
        {
            "disable" => SslMode.Disable,
            "allow" => SslMode.Allow,
            "prefer" => SslMode.Prefer,
            "require" => SslMode.Require,
            "verify-ca" => SslMode.VerifyCA,
            "verify-full" => SslMode.VerifyFull,

            // Unspecified: a local container has no certificate, everything else is remote.
            null or "" => IsLocal(host) ? SslMode.Disable : SslMode.Require,
            _ => SslMode.Require,
        };

    private static bool IsLocal(string host) =>
        host is "localhost" or "127.0.0.1" or "::1" or "postgres";
}
