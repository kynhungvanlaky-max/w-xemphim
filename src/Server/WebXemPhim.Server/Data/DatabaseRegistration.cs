using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WebXemPhim.Server.Data;

public static class DatabaseRegistration
{
    /// <summary>
    /// Registers the application database provider. PostgreSQL is the primary engine; MySQL is
    /// kept as an optional secondary provider for deployments that already run on it.
    /// </summary>
    public static IServiceCollection AddApplicationDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "PostgreSql";

        if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            var rawConnectionString = new[]
            {
                configuration.GetConnectionString("Default"),
                configuration.GetConnectionString("PostgreSql"),
                configuration["DATABASE_URL"],
                configuration["DATABASE_CONNECTION_STRING"]
            }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'Default' or DATABASE_URL is required for the PostgreSQL provider.");
            }

            var connectionString = NormalizePostgresConnectionString(rawConnectionString);

            services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
            return services;
        }

        if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'Default' is required.");
            }

            services.AddDbContextFactory<AppDbContext>(options => options.UseMySQL(connectionString));
            return services;
        }

        throw new InvalidOperationException(
            $"Database provider '{provider}' is not supported. Supported providers: PostgreSql, MySql.");
    }

    /// <summary>
    /// Converts a postgresql:// or postgres:// URI into an Npgsql-compatible connection string.
    /// If the connection string is already in key=value format, it is returned unchanged.
    /// </summary>
    public static string NormalizePostgresConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(trimmed);
            var userInfo = uri.UserInfo.Split(':', 2);
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;

            var host = uri.Host;
            if (host.StartsWith("dpg-", StringComparison.OrdinalIgnoreCase) && !host.Contains('.'))
            {
                host = $"{host}.singapore-postgres.render.com";
            }
            else if (host.StartsWith("db.", StringComparison.OrdinalIgnoreCase) && host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
            {
                // Supabase direct host (db.xxx.supabase.co) is IPv6-only.
                // Auto-route to the IPv4/IPv6 connection pooler with tenant-scoped username.
                var projectRef = host["db.".Length..^".supabase.co".Length];
                host = "aws-0-ap-south-1.pooler.supabase.com";
                if (!username.Contains('.'))
                {
                    username = $"{username}.{projectRef}";
                }
            }

            var isSupabase = host.Contains("supabase.com", StringComparison.OrdinalIgnoreCase) ||
                             host.Contains("supabase.co", StringComparison.OrdinalIgnoreCase);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = database,
                Username = username,
                Password = password,
                SslMode = isSupabase ? SslMode.Require : SslMode.Prefer,
                SearchPath = "webxemphim,public"
            };

            if (!string.IsNullOrEmpty(uri.Query))
            {
                var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
                if (queryParams.TryGetValue("sslmode", out var sslModeValues))
                {
                    var sslMode = sslModeValues.ToString();
                    if (Enum.TryParse<SslMode>(sslMode, true, out var parsedMode))
                    {
                        builder.SslMode = parsedMode;
                    }
                }
                if (queryParams.TryGetValue("searchpath", out var searchPathValues))
                {
                    builder.SearchPath = searchPathValues.ToString();
                }
                else if (queryParams.TryGetValue("schema", out var schemaValues))
                {
                    builder.SearchPath = $"{schemaValues},public";
                }
            }

            return builder.ConnectionString;
        }

        return trimmed;
    }
}
