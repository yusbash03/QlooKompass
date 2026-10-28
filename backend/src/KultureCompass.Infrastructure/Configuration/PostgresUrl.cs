namespace KultureCompass.Infrastructure.Configuration;

/// <summary>Converts a URL-style connection string (<c>postgres://user:pass@host:5432/db</c>, as Render, Heroku and
/// Neon provide in DATABASE_URL) into the key/value form Npgsql expects.</summary>
public static class PostgresUrl
{
    public static string ToConnectionString(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme is not ("postgres" or "postgresql"))
            throw new ArgumentException($"DATABASE_URL must use the postgres:// or postgresql:// scheme, not {uri.Scheme}://.", nameof(url));

        var userInfo = uri.UserInfo.Split(':', 2);
        var parts = new List<string>
        {
            $"Host={uri.Host}",
            $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
            $"Database={Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))}",
            $"Username={Uri.UnescapeDataString(userInfo[0])}",
        };
        if (userInfo.Length == 2)
            parts.Add($"Password={Uri.UnescapeDataString(userInfo[1])}");

        // Render's internal URLs don't need TLS; its external ones require it. Prefer covers both.
        var sslMode = System.Web.HttpUtility.ParseQueryString(uri.Query)["sslmode"];
        parts.Add($"SSL Mode={(string.IsNullOrWhiteSpace(sslMode) ? "Prefer" : sslMode)}");
        return string.Join(';', parts);
    }
}
