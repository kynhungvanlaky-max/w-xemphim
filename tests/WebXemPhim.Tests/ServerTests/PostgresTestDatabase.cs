using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using WebXemPhim.Server.Data;

namespace WebXemPhim.Tests.ServerTests;

/// <summary>
/// Boots a real PostgreSQL instance for the relational test suites.
/// <para>
/// A single container is shared by the whole test assembly (the suite runs serially), but every
/// test class gets its own database so that <c>EnsureCreated</c> always has a clean schema to
/// work with and parallel state never leaks between classes.
/// </para>
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private const string ContainerImage = "postgres:17-alpine";
    private const string ContainerUser = "webxemphim";
    private const string ContainerPassword = "webxemphim_test_pw";
    private const string ContainerDatabase = "webxemphim";

    private static readonly SemaphoreSlim s_containerGate = new(1, 1);
    private static PostgreSqlContainer? s_container;
    private static string? s_containerConnectionString;

    private readonly string _databaseName;

    private PostgresTestDatabase(string databaseName, string connectionString)
    {
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    /// <summary>The Npgsql connection string of the isolated test database.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates an empty database on the shared container.</summary>
    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var containerConnectionString = await EnsureContainerAsync();
        var databaseName = $"webxemphim_test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(containerConnectionString))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", admin);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(containerConnectionString)
        {
            Database = databaseName,
            // The concurrency stress suites (100/200 simultaneous writers) need far more than
            // the default pool of 100 connections, otherwise Npgsql starves the callers.
            MaxPoolSize = 300,
            // Give a saturated pool enough time to hand a connection to a waiting writer.
            Timeout = 60
        }.ConnectionString;

        return new PostgresTestDatabase(databaseName, connectionString);
    }

    /// <summary>Creates the database and applies the full schema (tables, indexes, search index).</summary>
    public static async Task<PostgresTestDatabase> CreateInitializedAsync()
    {
        var database = await CreateAsync();
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        return database;
    }

    public DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

    public AppDbContext CreateContext() => new(CreateOptions());

    public IDbContextFactory<AppDbContext> CreateFactory() => new TestDbContextFactory(CreateOptions());

    /// <summary>
    /// Empties the tables and returns an open connection, a context and a context factory that
    /// all share the same physical connection (mirrors the previous SQLite in-memory helper).
    /// </summary>
    public async Task<(NpgsqlConnection Connection, AppDbContext Context, IDbContextFactory<AppDbContext> Factory)> CreateOpenContextAsync()
    {
        await ResetAsync();
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;
        return (connection, new AppDbContext(options), new TestDbContextFactory(options));
    }

    /// <summary>Returns the names of the tables of the current schema.</summary>
    public async Task<List<string>> GetTableNamesAsync()
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync();
        try
        {
            var names = new List<string>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = current_schema() ORDER BY table_name;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    /// <summary>Returns the names of the indexes of the current schema (excluding the implicit ones).</summary>
    public async Task<List<string>> GetIndexNamesAsync()
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync();
        try
        {
            var names = new List<string>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND indexname NOT LIKE 'movies_pkey%' AND indexname NOT LIKE '%_pkey' ORDER BY indexname;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    /// <summary>Returns the column names of a table in the current schema.</summary>
    public async Task<List<string>> GetColumnNamesAsync(string tableName)
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync();
        try
        {
            var names = new List<string>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName ORDER BY ordinal_position;";
            var p = cmd.CreateParameter();
            p.ParameterName = "@tableName";
            p.Value = tableName;
            cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
            return names;
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    /// <summary>Empties every table so a test class can reuse the same database.</summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE
  r RECORD;
BEGIN
  FOR r IN
    SELECT tablename FROM pg_tables
     WHERE schemaname = current_schema()
       AND tablename <> '__EFMigrationsHistory'
  LOOP
    EXECUTE 'TRUNCATE TABLE ""' || r.tablename || '"" RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (s_container is null || s_containerConnectionString is null) return;

            var builder = new NpgsqlConnectionStringBuilder(s_containerConnectionString)
            {
                Database = ContainerDatabase,
                Pooling = false
            };

            await using var admin = new NpgsqlConnection(builder.ConnectionString);
            await admin.OpenAsync();
            await using var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid()", admin);
            terminate.Parameters.AddWithValue("db", _databaseName);
            await terminate.ExecuteNonQueryAsync();

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\";", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort cleanup; a leftover database in a throwaway container is harmless.
        }
    }

    private static async Task<string> EnsureContainerAsync()
    {
        if (s_containerConnectionString is not null) return s_containerConnectionString;

        await s_containerGate.WaitAsync();
        try
        {
            if (s_containerConnectionString is not null) return s_containerConnectionString;

            var container = new PostgreSqlBuilder(ContainerImage)
                .WithDatabase(ContainerDatabase)
                .WithUsername(ContainerUser)
                .WithPassword(ContainerPassword)
                .WithCleanUp(true)
                // PostgreSQL ships with max_connections = 100, which is not enough for the
                // atomic view-counter stress suites that open hundreds of simultaneous writers.
                // The image entrypoint prepends "postgres" when the command starts with "-c".
                .WithCommand("-c", "max_connections=500")
                .Build();

            await container.StartAsync();
            s_container = container;
            s_containerConnectionString = container.GetConnectionString();
            return s_containerConnectionString;
        }
        finally
        {
            s_containerGate.Release();
        }
    }
}

/// <summary>
/// Class fixture that owns one isolated PostgreSQL database for a whole test class.
/// The schema is created once; every test truncates the tables before seeding.
/// </summary>
public sealed class PostgresTestDatabaseFixture : IAsyncLifetime
{
    public PostgresTestDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Database = await PostgresTestDatabase.CreateInitializedAsync();
    }

    public async Task DisposeAsync()
    {
        if (Database is not null)
        {
            await Database.DisposeAsync();
        }
    }
}