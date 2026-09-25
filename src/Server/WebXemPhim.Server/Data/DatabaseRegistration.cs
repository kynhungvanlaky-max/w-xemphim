using Microsoft.EntityFrameworkCore;

namespace WebXemPhim.Server.Data;

public static class DatabaseRegistration
{
    public static IServiceCollection AddApplicationDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("Sqlite")
                ?? configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("Server="))
            {
                connectionString = "Data Source=webxemphim.db";
            }

            services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
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
            $"Database provider '{provider}' is not supported. Supported providers: Sqlite, MySql.");
    }
}
