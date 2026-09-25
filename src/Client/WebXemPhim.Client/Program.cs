using WebXemPhim.Client.Components;
using WebXemPhim.Client.Services;
using Microsoft.AspNetCore.DataProtection;
using WebXemPhim.Shared.Security;

var builder = WebApplication.CreateBuilder(args);
WebSecurityExtensions.ValidateProduction(builder.Configuration, builder.Environment, client: true);
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 64 * 1024; });
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSiteDataProtection(builder.Configuration, "WebXemPhim.Client");
builder.Services.AddHttpContextAccessor();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure HttpClient for Server REST API
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5085";
builder.Services.AddHttpClient<IMovieApiClient, MovieApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("AccountApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<IAdminApiClient, AdminApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

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

var app = builder.Build();
app.UseSiteSecurity(client: true, app.Environment.IsDevelopment(), app.Configuration);

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
    app.Logger.LogInformation("WebXemPhim.Client application is stopping. Trace: {StackTrace}", Environment.StackTrace);
});

app.Run();
