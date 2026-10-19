using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;

namespace WebXemPhim.Tests.ServerTests;

public sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(options));
}
