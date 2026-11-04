using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebXemPhim.Server.Data;

/// <summary>
/// Design-time factory so that <c>dotnet ef</c> commands always target PostgreSQL,
/// independently of the environment the tooling runs in.
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var raw =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim";

        var connectionString = DatabaseRegistration.NormalizePostgresConnectionString(raw);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
