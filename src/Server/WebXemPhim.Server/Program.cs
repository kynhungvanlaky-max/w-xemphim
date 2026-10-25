using System.IO.Compression;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Hubs;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Security;

var builder = WebApplication.CreateBuilder(args);
WebSecurityExtensions.ApplyDevelopmentDefaults(builder.Configuration, builder.Environment, client: false);
WebSecurityExtensions.ValidateProduction(builder.Configuration, builder.Environment, client: false);
// Giới hạn mặc định 64 KB cho mọi API. Riêng endpoint tải ảnh nền tập dùng giới hạn
// riêng (RequestSizeLimit trên action) nên Kestrel phải cho phép mức cao hơn — giới hạn
// của action vẫn được áp dụng, các endpoint khác không bị nới ra.
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 6 * 1024 * 1024; });
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSiteDataProtection(builder.Configuration, "WebXemPhim.Server");

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(64 * 1024)));
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddApiRateLimits(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json", "application/problem+json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("MoviesCatalogCache", b => b.Expire(TimeSpan.FromSeconds(60)).Tag("movies"));
    options.AddPolicy("GenresCatalogCache", b => b.Expire(TimeSpan.FromSeconds(120)).Tag("genres"));
    options.AddPolicy("FilterOptionsCache", b => b.Expire(TimeSpan.FromSeconds(120)).Tag("movies").Tag("genres"));
});
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddApplicationDatabase(builder.Configuration);
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();

// Proxy video: máy chủ nguồn có thể chậm, nên thời gian chờ phải rộng và không được chặn
// việc đọc nốt phần dữ liệu còn lại khi người xem đóng tab.
// HLS có thể tạo nhiều request segment song song, nên cần mở rộng connection pool
// và bật HTTP/2 để tránh bottleneck ở plafon 2 kết nối/máy chủ (mặc định HTTP/1.1).
builder.Services.AddHttpClient("StreamProxy", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CinemaFlow/1.0 (+stream-proxy)");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Redirect được xử lý thủ công trong StreamController để kiểm tra lại URL từng bước.
    AllowAutoRedirect = false,
    AutomaticDecompression = System.Net.DecompressionMethods.All,
    ConnectTimeout = TimeSpan.FromSeconds(15),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer = 64,
    EnableMultipleHttp2Connections = true,
    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
    MaxResponseHeadersLength = 8 * 1024,
    MaxResponseDrainSize = 8 * 1024,
    // Chặn DNS rebinding: địa chỉ IP thật sự được kết nối phải là IP công khai.
    ConnectCallback = SafeStreamUrlValidator.ConnectSafeAsync
});

builder.Services.AddHttpClient("StreamProbe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CinemaFlow-Probe/2026.1");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    ConnectTimeout = TimeSpan.FromSeconds(5),
    ConnectCallback = SafeStreamUrlValidator.ConnectSafeAsync
});

// Register application services
builder.Services.AddScoped<IMovieService, DatabaseMovieService>();
builder.Services.AddScoped<IGenreService, DatabaseGenreService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordResetEmailSender, PasswordResetEmailSender>();
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddSingleton<IStreamUrlSigner, StreamUrlSigner>();
builder.Services.AddSingleton<PublicUrlResolver>();
// Ảnh nền tập do quản trị viên tải lên nằm trong volume server_data (/app/data) nên
// không mất khi build lại container.
builder.Services.AddSingleton<EpisodeThumbnailStorage>();
builder.Services.AddSingleton<IPaymentWebhookVerifier, PaymentWebhookVerifier>();
builder.Services.AddSingleton<IFieldEncryptor, FieldEncryptor>();
builder.Services.AddScoped<IStreamSessionService, StreamSessionService>();
builder.Services.AddHostedService<StreamSessionCleanupWorker>();
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, OpaqueBearerHandler>("Bearer", null);
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin")));

// Enable CORS for Blazor Client
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientOrigins", policy =>
    {
        var origins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                  .WithMethods("GET", "POST", "PUT", "DELETE")
                  .WithHeaders("Content-Type", "Authorization", "x-requested-with", "x-signalr-user-agent", "Range")
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// The customer-field encryptor must share the application's Data Protection key ring.
// Without this the static accessor falls back to a key folder in the process temp path,
// which is wiped whenever the container is rebuilt — every already encrypted user name and
// e-mail would then be unreadable and the cockpit would render raw ciphertext.
FieldEncryptor.Current = new FieldEncryptor(app.Services.GetRequiredService<IDataProtectionProvider>());

if (!app.Environment.IsEnvironment("Testing"))
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    var schemaStartedAt = DateTime.UtcNow;
    if (db.Database.IsNpgsql())
    {
        // PostgreSQL validates foreign keys when a table is created, so the schema is applied
        // through the migrations (which know the dependency order) instead of EnsureCreated.
        await DatabaseSchemaHelper.BaselineMigrationsAsync(db);
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
    await DatabaseSchemaHelper.EnsureTablesCreatedAsync(db);

    // Revoke historical plaintext/predictable tokens; never migrate them into valid sessions.
    // This is a one-shot cleanup: the flag prevents a full table scan on every restart.
    const string legacyTokenCleanupFlag = "legacy-plaintext-tokens-revoked";
    var legacyCleanupDone = await db.AppMaintenanceFlags.AnyAsync(item => item.Flag == legacyTokenCleanupFlag);
    if (!legacyCleanupDone)
    {
        var legacyAccess = await db.AccessTokens.Where(item => !item.Token.StartsWith("sha256:")).ToListAsync();
        var legacyResets = await db.PasswordResetTokens.Where(item => !item.Token.StartsWith("sha256:")).ToListAsync();
        db.AccessTokens.RemoveRange(legacyAccess);
        db.PasswordResetTokens.RemoveRange(legacyResets);
        await db.SaveChangesAsync();
        db.AppMaintenanceFlags.Add(new AppMaintenanceFlagEntity { Flag = legacyTokenCleanupFlag, CompletedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        app.Logger.LogInformation(
            "Revoked {AccessCount} legacy access tokens and {ResetCount} legacy password reset tokens.",
            legacyAccess.Count,
            legacyResets.Count);
    }

    app.Logger.LogInformation(
        "Database schema verified in {ElapsedMs} ms.",
        (DateTime.UtcNow - schemaStartedAt).TotalMilliseconds);

    // Deployment sanity checks: a mis-configured reverse proxy silently breaks the client IP
    // resolution (and therefore the rate limiting), so make it visible at startup.
    if (app.Environment.IsProduction())
    {
        var allowedHosts = app.Configuration["AllowedHosts"] ?? string.Empty;
        if (allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(host => host is "localhost" or "127.0.0.1" or "[::1]"))
        {
            app.Logger.LogWarning(
                "AllowedHosts still contains only local addresses ({AllowedHosts}). Every request from a real host will be rejected. Set AllowedHosts to the public host names before going live.",
                allowedHosts);
        }

        var origins = app.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0)
        {
            app.Logger.LogWarning(
                "Security:AllowedOrigins is empty. Browser calls to the REST API from another origin will be blocked by CORS. Configure the public client origins before going live.");
        }

        if (app.Configuration.GetSection("Security:KnownProxies").Get<string[]>() is not { Length: > 0 } &&
            app.Configuration.GetSection("Security:KnownProxyNetworks").Get<string[]>() is not { Length: > 0 })
        {
            app.Logger.LogWarning(
                "No reverse proxy is explicitly trusted. Only loopback is trusted for X-Forwarded-For, so the client IP (and the rate limit bucket) may resolve to the proxy when it runs on a private network. Configure Security:KnownProxies or Security:KnownProxyNetworks.");
        }
    }

    // Move legacy seeded artwork URLs to the WebP variants shipped with the client.
    const string legacyImageUrlFlag = "movie-image-urls-migrated-to-webp";
    var imageMigrationDone = await db.AppMaintenanceFlags.AnyAsync(item => item.Flag == legacyImageUrlFlag);
    if (!imageMigrationDone)
    {
        var migrated = await DatabaseSchemaHelper.MigrateLegacyMovieImageUrlsAsync(db);
        db.AppMaintenanceFlags.Add(new AppMaintenanceFlagEntity { Flag = legacyImageUrlFlag, CompletedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (migrated > 0)
        {
            app.Logger.LogInformation("Migrated {Count} movie artwork references to WebP.", migrated);
        }
    }

    // Repair text that was stored with the wrong code page (mojibake) by older seeds.
    const string mojibakeRepairFlag = "mojibake-text-repaired-v2";
    var mojibakeRepairDone = await db.AppMaintenanceFlags.AnyAsync(item => item.Flag == mojibakeRepairFlag);
    if (!mojibakeRepairDone)
    {
        var repaired = await DatabaseSchemaHelper.RepairMojibakeTextAsync(db);
        db.AppMaintenanceFlags.Add(new AppMaintenanceFlagEntity { Flag = mojibakeRepairFlag, CompletedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (repaired > 0)
        {
            app.Logger.LogInformation("Repaired {Count} text records that were stored with the wrong code page.", repaired);
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Không tin toàn bộ dải mạng riêng: bất kỳ máy nào trong cùng VPC/LAN/Docker network đều
// có thể gửi X-Forwarded-For giả để vượt rate limit hoặc làm sai lệch IP dùng để ký stream.
// Chỉ loopback và proxy được cấu hình tường minh mới được tin.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownProxies.Add(System.Net.IPAddress.Loopback);
forwardedHeadersOptions.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);

var configuredProxies = app.Configuration.GetSection("Security:KnownProxies").Get<string[]>();
if (configuredProxies is { Length: > 0 })
{
    foreach (var proxy in configuredProxies)
    {
        if (System.Net.IPAddress.TryParse(proxy, out var ip))
        {
            forwardedHeadersOptions.KnownProxies.Add(ip);
            ApiRateLimits.AddTrustedProxy(proxy);
        }
    }
}

// Additional trusted proxy networks (CIDR) for deployments where the reverse proxy does not
// live in the default private ranges, e.g. "Security:KnownProxyNetworks": ["10.20.0.0/16"].
var configuredProxyNetworks = app.Configuration.GetSection("Security:KnownProxyNetworks").Get<string[]>();
if (configuredProxyNetworks is { Length: > 0 })
{
    foreach (var network in configuredProxyNetworks)
    {
        var parts = network.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            System.Net.IPAddress.TryParse(parts[0], out var address) &&
            int.TryParse(parts[1], out var prefixLength))
        {
            forwardedHeadersOptions.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefixLength));
            ApiRateLimits.AddTrustedProxyNetwork(network);
        }
        else
        {
            app.Logger.LogWarning("Ignoring invalid Security:KnownProxyNetworks entry '{Network}'.", network);
        }
    }
}
app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseResponseCompression();

app.UseSiteSecurity(client: false, app.Environment.IsDevelopment(), app.Configuration);
app.UseExceptionHandler();
if (app.Environment.IsProduction()) { app.UseHsts(); app.UseHttpsRedirection(); app.RequireHttpsTransport(); }
app.UseRouting();
app.UseCors("ClientOrigins");
app.UseOutputCache();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<CinemaHub>("/hubs/cinema");

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:SeedDemoData"))
{
    try
    {
        var factory = app.Services.GetService<IDbContextFactory<AppDbContext>>();
        if (factory is not null)
        {
            await using var context = await factory.CreateDbContextAsync();
            var timeProvider = app.Services.GetRequiredService<TimeProvider>();
            await DatabaseSeeder.SeedAsync(context, timeProvider);
            if (context.Database.IsNpgsql())
            {
                await DatabaseSchemaHelper.SyncMovieSearchIndexAsync(context);
                await DatabaseSchemaHelper.SyncIdentitySequencesAsync(context);
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not automatically seed database. Ensure database server is running or run seed scripts manually.");
    }
}

app.Run();
