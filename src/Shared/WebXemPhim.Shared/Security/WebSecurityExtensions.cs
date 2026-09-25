using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebXemPhim.Shared.Security;

public static class WebSecurityExtensions
{
    public const string NonceKey = "CinemaFlow.CspNonce";

    public static void ValidateProduction(IConfiguration config, IWebHostEnvironment environment, bool client)
    {
        if (!environment.IsProduction()) return;
        var hosts = config["AllowedHosts"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts is null || hosts.Length == 0 || hosts.Any(host => host.Contains('*')))
            throw new InvalidOperationException("Production requires an explicit AllowedHosts allowlist.");
        var keyPath = config["Security:DataProtectionKeyPath"];
        if (string.IsNullOrWhiteSpace(keyPath) || !Path.IsPathFullyQualified(keyPath))
            throw new InvalidOperationException("Production requires an absolute Security:DataProtectionKeyPath outside the web root.");
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var fullKeyPath = Path.GetFullPath(keyPath);
        if (fullKeyPath.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || fullKeyPath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Data Protection keys must not be stored in the web root.");
        if (config.GetValue<bool>("Database:SeedDemoData") || config.GetValue<bool>("Security:AllowDemoPayments"))
            throw new InvalidOperationException("Demo data and simulated payments are forbidden in Production.");
        foreach (var origin in config.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [])
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || origin != uri.GetLeftPart(UriPartial.Authority))
                throw new InvalidOperationException("Production origins must be explicit HTTPS origins without paths or wildcards.");
        if (client && (!Uri.TryCreate(config["ApiBaseUrl"], UriKind.Absolute, out var api) || api.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Production ApiBaseUrl must use HTTPS.");
    }

    public static void AddSiteDataProtection(this IServiceCollection services, IConfiguration config, string applicationName)
    {
        var keyPath = config["Security:DataProtectionKeyPath"] ?? Path.Combine(Path.GetTempPath(), "WebXemPhim.Keys");
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName(applicationName);
    }

    public static IApplicationBuilder RequireHttpsTransport(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps) { context.Response.StatusCode = StatusCodes.Status400BadRequest; await context.Response.WriteAsync("HTTPS is required."); return; }
        await next(context);
    });

    public static IApplicationBuilder UseSiteSecurity(this IApplicationBuilder app, bool client, bool development, IConfiguration config) =>
        app.Use(async (context, next) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            context.Items[NonceKey] = nonce;
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = client ? "strict-origin-when-cross-origin" : "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), fullscreen=(self), picture-in-picture=(self)";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";
                headers["X-Download-Options"] = "noopen";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Content-Security-Policy"] = client
                    ? $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; script-src-attr 'none'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; media-src 'self' https: blob:; connect-src 'self' https: wss:{(development ? " http: ws:" : "")}; frame-src https:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'"
                    : "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
                if ((client && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true) ||
                    context.Request.Headers.ContainsKey("Authorization") || context.Request.Path.StartsWithSegments("/api/auth") || context.Request.Path.StartsWithSegments("/api/admin") || context.Request.Path.StartsWithSegments("/api/payments/history"))
                    headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });

            // CORS alone doesn't protect WebSocket handshakes used by Blazor Server.
            if (client && context.Request.Path.StartsWithSegments("/_blazor") && context.Request.Headers.TryGetValue("Origin", out var origin))
            {
                var requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
                var allowed = config.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
                if (!origin.ToString().Equals(requestOrigin, StringComparison.OrdinalIgnoreCase) && !allowed.Contains(origin.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await next(context);
        });
}
