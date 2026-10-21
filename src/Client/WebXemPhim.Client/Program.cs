using WebXemPhim.Client.Components;
using WebXemPhim.Client.Services;
using Microsoft.AspNetCore.DataProtection;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using WebXemPhim.Shared.Security;

var builder = WebApplication.CreateBuilder(args);
WebSecurityExtensions.ApplyDevelopmentDefaults(builder.Configuration, builder.Environment, client: true);
WebSecurityExtensions.ValidateProduction(builder.Configuration, builder.Environment, client: true);
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 64 * 1024; });
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSiteDataProtection(builder.Configuration, "WebXemPhim.Client");
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ForwardedClientIpHandler>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure HttpClient Resilience & Fallback Defaults
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
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

var app = builder.Build();
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
