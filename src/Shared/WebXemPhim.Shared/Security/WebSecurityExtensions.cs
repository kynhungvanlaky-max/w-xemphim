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

    /// <summary>
    /// Applies safe local-development defaults for settings that are intentionally **not**
    /// committed to git (<c>appsettings.Development.json</c>). This keeps a fresh clone usable
    /// while making sure no secret is ever part of the repository.
    /// </summary>
    public static void ApplyDevelopmentDefaults(IConfiguration config, IWebHostEnvironment environment, bool client)
    {
        if (!environment.IsDevelopment()) return;

        if (client)
        {
            if (string.IsNullOrWhiteSpace(config["ApiBaseUrl"]))
            {
                config["ApiBaseUrl"] = "http://localhost:5085";
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
        {
            config["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=webxemphim;Username=webxemphim;Password=webxemphim";
        }

        if (string.IsNullOrWhiteSpace(config["Database:Provider"]))
        {
            config["Database:Provider"] = "PostgreSql";
        }

        if (string.IsNullOrWhiteSpace(config["Database:SeedDemoData"]))
        {
            config["Database:SeedDemoData"] = "true";
        }

        var allowedOrigins = config.GetSection("Security:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is null || allowedOrigins.Length == 0)
        {
            config["Security:AllowedOrigins:0"] = "http://localhost:5229";
            config["Security:AllowedOrigins:1"] = "http://127.0.0.1:5229";
        }
    }

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

        if (!client)
        {
            var defaultConnectionString = !string.IsNullOrWhiteSpace(config.GetConnectionString("Default"))
                ? config.GetConnectionString("Default")
                : (!string.IsNullOrWhiteSpace(config["ConnectionStrings:Default"])
                    ? config["ConnectionStrings:Default"]
                    : config["DATABASE_CONNECTION_STRING"]);
            if (string.IsNullOrWhiteSpace(defaultConnectionString))
            {
                throw new InvalidOperationException("Production database connection string (ConnectionStrings:Default) cannot be empty.");
            }

            const string defaultStreamKey = "CinemaFlow_Secure_Stream_HMAC_Secret_Key_2026_AtLeast32Bytes!";
            var rawStreamKey = !string.IsNullOrWhiteSpace(config["Security:StreamSigningKey"])
                ? config["Security:StreamSigningKey"]
                : config["STREAM_SIGNING_KEY"];
            var streamKey = rawStreamKey?.Trim();
            if (string.IsNullOrWhiteSpace(streamKey))
            {
                throw new InvalidOperationException("Production requires an explicit Security:StreamSigningKey configuration.");
            }
            if (string.Equals(streamKey, defaultStreamKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Production Security:StreamSigningKey cannot be the default placeholder key.");
            }
            if (streamKey.Length < 32)
            {
                throw new InvalidOperationException("Production Security:StreamSigningKey must be at least 32 characters long.");
            }

            const string defaultWebhookSecret = "CinemaFlow_Secure_Payment_Webhook_HMAC_SHA512_Secret_Key_2026_AtLeast32Bytes!";
            var rawWebhookSecret = !string.IsNullOrWhiteSpace(config["Payment:WebhookSecret"])
                ? config["Payment:WebhookSecret"]
                : (!string.IsNullOrWhiteSpace(config["Security:PaymentWebhookSecret"])
                    ? config["Security:PaymentWebhookSecret"]
                    : config["PAYMENT_WEBHOOK_SECRET"]);
            var webhookSecret = rawWebhookSecret?.Trim();
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                throw new InvalidOperationException("Production requires an explicit Payment:WebhookSecret configuration.");
            }
            if (string.Equals(webhookSecret, defaultWebhookSecret, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Production Payment:WebhookSecret cannot be the default placeholder key.");
            }
            if (webhookSecret.Length < 32)
            {
                throw new InvalidOperationException("Production Payment:WebhookSecret must be at least 32 characters long.");
            }

            const string defaultPayOsSecret = "CinemaFlow_PayOS_Webhook_Secret_Key_2026_AtLeast32Bytes!";
            var rawPayOsSecret = !string.IsNullOrWhiteSpace(config["Payment:Gateways:PayOS:WebhookSecret"])
                ? config["Payment:Gateways:PayOS:WebhookSecret"]
                : config["PAYOS_WEBHOOK_SECRET"];
            var payOsSecret = rawPayOsSecret?.Trim();
            if (!string.IsNullOrWhiteSpace(payOsSecret))
            {
                if (string.Equals(payOsSecret, defaultPayOsSecret, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Production PayOS webhook secret cannot be the default placeholder key.");
                }
                if (payOsSecret.Length < 32)
                {
                    throw new InvalidOperationException("Production PayOS webhook secret must be at least 32 characters long.");
                }
            }

            const string defaultVnPaySecret = "CinemaFlow_VNPay_Hash_Secret_Key_2026_AtLeast32Bytes!";
            var rawVnPaySecret = !string.IsNullOrWhiteSpace(config["Payment:Gateways:VNPay:HashSecret"])
                ? config["Payment:Gateways:VNPay:HashSecret"]
                : config["VNPAY_HASH_SECRET"];
            var vnPaySecret = rawVnPaySecret?.Trim();
            if (!string.IsNullOrWhiteSpace(vnPaySecret))
            {
                if (string.Equals(vnPaySecret, defaultVnPaySecret, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Production VNPay hash secret cannot be the default placeholder key.");
                }
                if (vnPaySecret.Length < 32)
                {
                    throw new InvalidOperationException("Production VNPay hash secret must be at least 32 characters long.");
                }
            }
        }
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
                    // media-src và img-src phải cho phép http: ở chế độ phát triển: video và ảnh
                    // bìa được nạp từ API ở cổng khác (http://localhost:5085) nên nếu chỉ cho
                    // 'self' + https: thì trình duyệt chặn và báo
                    // "Media load rejected by URL safety check" — người xem chỉ thấy màn đen.
                    // Ở production API dùng https nên đã nằm trong https: của chính sách.
                    ? $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; script-src-attr 'none'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https:{(development ? " http:" : "")}; media-src 'self' https: blob:{(development ? " http:" : "")}; connect-src 'self' https: wss:{(development ? " http: ws:" : "")}; frame-src https:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'"
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
