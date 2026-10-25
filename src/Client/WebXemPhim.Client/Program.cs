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

var builder = WebApplication.CreateBuilder(args);
WebSecurityExtensions.ApplyDevelopmentDefaults(builder.Configuration, builder.Environment, client: true);
WebSecurityExtensions.ValidateProduction(builder.Configuration, builder.Environment, client: true);
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 64 * 1024; });
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSiteDataProtection(builder.Configuration, "WebXemPhim.Client");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICircuitClientIpProvider, CircuitClientIpProvider>();
builder.Services.AddTransient<ForwardedClientIpHandler>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Blazor Server chuyển tệp người dùng chọn qua SignalR. Mặc định mỗi tin nhắn chỉ được
// 32 KB nên ảnh nền tập (tối đa 2 MB) sẽ bị chặn giữa chừng. Nới riêng giới hạn này —
// các endpoint HTTP của web client vẫn giữ mức 64 KB.
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 4 * 1024 * 1024);

// Configure HttpClient Resilience & Fallback Defaults
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        // The API needs a moment to apply its migrations and seed the catalogue on the very
        // first start, so the total budget has to cover that window.
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);

        options.Retry.MaxRetryAttempts = 6;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.MaxDelay = TimeSpan.FromSeconds(4);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        // A refused connection is not a transient HTTP status, so the default handler never
        // retries it. Right after the stack starts the API is not listening yet and every
        // page would otherwise render an empty state until the visitor reloads the page.
        options.Retry.ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>(exception => exception.StatusCode is null)
            .HandleResult(response => response.StatusCode is HttpStatusCode.RequestTimeout
                || response.StatusCode == HttpStatusCode.TooManyRequests
                || (int)response.StatusCode >= 500);
    });
});

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
builder.Services.AddHttpClient<IMovieApiClient, MovieApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<ForwardedClientIpHandler>();
builder.Services.AddHttpClient("AccountApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<ForwardedClientIpHandler>();
builder.Services.AddHttpClient<IAdminApiClient, AdminApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<ForwardedClientIpHandler>();

// Register reactive state
builder.Services.AddScoped<IAccountApiClient>(services => new AccountApiClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("AccountApi"),
    services.GetRequiredService<ILogger<AccountApiClient>>()));
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

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
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
app.UseForwardedHeaders(forwardedHeadersOptions);
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("WebXemPhim.Client application is stopping gracefully.");
});

app.Run();
