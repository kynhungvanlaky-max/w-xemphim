using Microsoft.EntityFrameworkCore;

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
            var connectionString = configuration.GetConnectionString("Default")
                ?? configuration.GetConnectionString("PostgreSql");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'Default' is required for the PostgreSQL provider.");
            }

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
}
