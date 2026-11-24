using WebXemPhim.Client.Components;
using WebXemPhim.Client.Services;
using Microsoft.AspNetCore.DataProtection;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using WebXemPhim.Shared.Security;
using Microsoft.AspNetCore.Http.Features;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Auto-derive AllowedHosts for production environments (Render, Cloud containers)
// if not provided or if wildcard was injected by default container runners
var rawClientHosts = builder.Configuration["AllowedHosts"];
if (string.IsNullOrWhiteSpace(rawClientHosts) || rawClientHosts.Trim() == "*")
{
    var renderHost = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_HOSTNAME");
    builder.Configuration["AllowedHosts"] = !string.IsNullOrWhiteSpace(renderHost)
        ? $"{renderHost};w-phim-client.onrender.com;localhost;127.0.0.1;[::1]"
        : "w-phim-client.onrender.com;localhost;127.0.0.1;[::1]";
}

WebSecurityExtensions.ApplyDevelopmentDefaults(builder.Configuration, builder.Environment, client: true);
WebSecurityExtensions.ValidateProduction(builder.Configuration, builder.Environment, client: true);
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 10L * 1024 * 1024; });
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSiteDataProtection(builder.Configuration, "WebXemPhim.Client");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICircuitClientIpProvider, CircuitClientIpProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, ClientIpCircuitHandler>();
builder.Services.AddTransient<ForwardedClientIpHandler>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DisconnectedCircuitMaxRetained = 30;
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(30);
    });

// Giới hạn kích thước frame SignalR ở mức 128 KB (đủ cho phân đoạn 32 KB của Blazor InputFile)
// để ngăn tấn công cạn kiệt bộ nhớ qua kết nối WebSocket chưa xác thực.
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 128 * 1024);

// Standard resilience handler for public consumer APIs with cold-start tolerance for Render
Action<HttpStandardResilienceOptions> standardResilience = options =>
{
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);

    options.Retry.MaxRetryAttempts = 6;
    options.Retry.Delay = TimeSpan.FromSeconds(2);
    options.Retry.MaxDelay = TimeSpan.FromSeconds(8);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;
    options.Retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
        .Handle<HttpRequestException>()
        .Handle<TaskCanceledException>()
        .Handle<TimeoutException>()
        .HandleResult(response => response.StatusCode is HttpStatusCode.RequestTimeout
            || response.StatusCode == HttpStatusCode.BadGateway
            || response.StatusCode == HttpStatusCode.ServiceUnavailable
            || response.StatusCode == HttpStatusCode.GatewayTimeout
            || (int)response.StatusCode >= 500);
};

// Compress HTML/CSS/JS/JSON responses (Brotli first, Gzip fallback).
// The Blazor client serves the whole design system, so this is the single
// biggest transfer-size win for first page load.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes;
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

// Configure HttpClient for Server REST API
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5085";
if (!apiBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
    !apiBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    apiBaseUrl = apiBaseUrl.Contains(".onrender.com", StringComparison.OrdinalIgnoreCase)
        ? $"https://{apiBaseUrl}"
        : $"http://{apiBaseUrl}";
    builder.Configuration["ApiBaseUrl"] = apiBaseUrl;
}
builder.Services.AddHttpClient<IMovieApiClient, MovieApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
}).AddHttpMessageHandler<ForwardedClientIpHandler>()
  .AddStandardResilienceHandler(standardResilience);

builder.Services.AddHttpClient("AccountApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
}).AddHttpMessageHandler<ForwardedClientIpHandler>()
  .AddStandardResilienceHandler(standardResilience);

builder.Services.AddHttpClient("BackendWarmup", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<BackendWarmupService>();
builder.Services.AddSingleton<IBackendWarmupService>(sp => sp.GetRequiredService<BackendWarmupService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackendWarmupService>());

builder.Services.AddHttpClient<IAdminApiClient, AdminApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromHours(2);
}).AddHttpMessageHandler<ForwardedClientIpHandler>();

// Configure YARP Reverse Proxy to transparently forward /api, /hubs, /openapi to Server REST API
builder.Services.AddReverseProxy()
    .LoadFromMemory(
        routes:
        [
            new RouteConfig
            {
                RouteId = "api-route",
                ClusterId = "server-cluster",
                Match = new RouteMatch { Path = "/api/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "hubs-route",
                ClusterId = "server-cluster",
                Match = new RouteMatch { Path = "/hubs/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "openapi-route",
                ClusterId = "server-cluster",
                Match = new RouteMatch { Path = "/openapi/{**catch-all}" }
            }
        ],
        clusters:
        [
            new ClusterConfig
            {
                ClusterId = "server-cluster",
                Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    ["server-destination"] = new DestinationConfig { Address = apiBaseUrl }
                },
                HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig
                {
                    ActivityTimeout = TimeSpan.FromHours(2)
                }
            }
        ])
    .AddTransforms(builderContext =>
    {
        // Tắt default forwarders của YARP để không gửi X-Forwarded-Host của w-phim-client
        // sang w-phim-api (không nằm trong AllowedHosts của API làm hủy xử lý ForwardedHeaders)
        // và đảm bảo header IP/HMAC đi qua YARP khớp 100% với HttpClient của Blazor.
        builderContext.UseDefaultForwarders = false;
        var proxySecret = SignedClientIpHeader.ResolveSecret(builder.Configuration);
        builderContext.AddRequestTransform(transformContext =>
        {
            var proxyHeaders = transformContext.ProxyRequest.Headers;
            foreach (var headerName in proxyHeaders.Select(h => h.Key).ToList())
            {
                if (headerName.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                    || headerName.StartsWith("CF-", StringComparison.OrdinalIgnoreCase)
                    || headerName.StartsWith("X-Render-", StringComparison.OrdinalIgnoreCase)
                    || headerName.StartsWith("Render-", StringComparison.OrdinalIgnoreCase)
                    || headerName.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase)
                    || headerName.Equals("True-Client-IP", StringComparison.OrdinalIgnoreCase)
                    || headerName.Equals("CDN-Loop", StringComparison.OrdinalIgnoreCase)
                    || headerName.Equals("Forwarded", StringComparison.OrdinalIgnoreCase))
                {
                    proxyHeaders.Remove(headerName);
                }
            }

            var remoteIp = transformContext.HttpContext.Connection.RemoteIpAddress;
            if (remoteIp is not null)
            {
                var clientIp = SignedClientIpHeader.NormalizeIp(remoteIp);
                SignedClientIpHeader.AttachSignedHeaders(
                    proxyHeaders,
                    clientIp,
                    proxySecret,
                    DateTimeOffset.UtcNow);
            }
            return ValueTask.CompletedTask;
        });
    });

// Register reactive state
builder.Services.AddScoped<IAccountApiClient>(services => new AccountApiClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("AccountApi"),
    services.GetRequiredService<ILogger<AccountApiClient>>(),
    services.GetRequiredService<ICircuitClientIpProvider>()));
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedSessionStorage>();
builder.Services.AddScoped<IAuthSessionStore, AuthSessionStore>();
builder.Services.AddScoped<WatchlistState>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddScoped<WatchApiClient>();
builder.Services.AddScoped<CinemaHubClient>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<IToastService>(sp => sp.GetRequiredService<ToastService>());

var app = builder.Build();

var trustCloudflare = TrustedEdgeProxies.ShouldTrustCloudflareEdge(app.Configuration);
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    ForwardLimit = TrustedEdgeProxies.ResolveForwardLimit(app.Configuration, trustCloudflare)
};
var clientAllowedHosts = (app.Configuration["AllowedHosts"] ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(h => !h.Contains('*'))
    .ToList();
if (clientAllowedHosts.Count > 0)
{
    forwardedHeadersOptions.AllowedHosts = clientAllowedHosts;
}
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownProxies.Add(IPAddress.Loopback);
forwardedHeadersOptions.KnownProxies.Add(IPAddress.IPv6Loopback);

var clientProxies = app.Configuration.GetSection("Security:KnownProxies").Get<string[]>();
if (clientProxies is { Length: > 0 })
{
    foreach (var proxy in clientProxies)
    {
        if (IPAddress.TryParse(proxy, out var ip))
        {
            forwardedHeadersOptions.KnownProxies.Add(ip);
        }
    }
}
var clientNetworks = app.Configuration.GetSection("Security:KnownProxyNetworks").Get<string[]>();
if (clientNetworks is { Length: > 0 })
{
    foreach (var network in clientNetworks)
    {
        var parts = network.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var address) && int.TryParse(parts[1], out var prefixLength))
        {
            forwardedHeadersOptions.KnownIPNetworks.Add(new IPNetwork(address, prefixLength));
        }
    }
}
if (trustCloudflare)
{
    TrustedEdgeProxies.AddCloudflareNetworks(forwardedHeadersOptions);
}

var behindProxy = app.Configuration.GetValue<bool>("Security:BehindReverseProxy")
    || string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

// The client follows the same trust model as the API: only loopback and explicitly
// configured proxies/networks may supply forwarding headers. Container mode alone is not
// enough to trust an arbitrary peer.
if (behindProxy && clientProxies is not { Length: > 0 } && clientNetworks is not { Length: > 0 })
{
    app.Logger.LogWarning(
        "Security:BehindReverseProxy is enabled but no Security:KnownProxies/KnownProxyNetworks are configured. Forwarded headers will be ignored and the client IP will resolve to the proxy address.");
}

app.UseForwardedHeaders(forwardedHeadersOptions);
app.Use(async (context, next) =>
{
    // UseForwardedHeaders above has already replaced RemoteIpAddress with the real client
    // address when (and only when) the direct peer is trusted. Never parse X-Forwarded-For
    // here directly: the header may contain attacker-controlled entries.
    var clientIp = context.Connection.RemoteIpAddress;
    if (clientIp is not null && clientIp.IsIPv4MappedToIPv6)
    {
        clientIp = clientIp.MapToIPv4();
    }

    if (clientIp is not null)
    {
        var ipProvider = context.RequestServices.GetService<ICircuitClientIpProvider>();
        if (ipProvider != null)
        {
            ipProvider.ClientIp = clientIp.ToString();
        }
    }

    // Chỉ nới trần kích thước body lên 4 GB cho 2 endpoint upload video quản trị có Bearer token
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var reqPath = context.Request.Path.Value ?? string.Empty;
        if (reqPath.Equals("/api/admin/storage/upload-r2", StringComparison.OrdinalIgnoreCase)
            || (reqPath.StartsWith("/api/admin/episodes/", StringComparison.OrdinalIgnoreCase)
                && reqPath.EndsWith("/video", StringComparison.OrdinalIgnoreCase)))
        {
            var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySizeFeature is { IsReadOnly: false })
            {
                bodySizeFeature.MaxRequestBodySize = 4L * 1024 * 1024 * 1024;
            }
        }
    }

    // Trigger backend warmup immediately on incoming visitor traffic if not yet warm
    var warmupService = context.RequestServices.GetService<IBackendWarmupService>();
    if (warmupService != null && !warmupService.IsReady)
    {
        warmupService.TriggerWarmup();
    }

    await next(context);
});
app.UseSiteSecurity(client: true, app.Environment.IsDevelopment(), app.Configuration);
app.UseResponseCompression();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (app.Environment.IsProduction()) { app.UseHsts(); app.UseHttpsRedirection(); app.RequireHttpsTransport(); }
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CinemaFlow.Client", timestamp = DateTimeOffset.UtcNow }));
// /api/health is deliberately omitted here so that YARP reverse proxy forwards /api/health
// directly to w-phim-api, ensuring any health ping to Client wakes up the API server as well.
app.MapReverseProxy();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("WebXemPhim.Client started. Triggering background warmup for backend REST API...");
    var warmup = app.Services.GetRequiredService<IBackendWarmupService>();
    warmup.TriggerWarmup();
});
lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("WebXemPhim.Client application is stopping gracefully.");
});

app.Run();
