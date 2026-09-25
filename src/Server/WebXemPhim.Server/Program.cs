using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Security;

var builder = WebApplication.CreateBuilder(args);
WebSecurityExtensions.ValidateProduction(builder.Configuration, builder.Environment, client: false);
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 64 * 1024; });
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSiteDataProtection(builder.Configuration, "WebXemPhim.Server");

// Add services to the container.
builder.Services.AddControllers(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(64 * 1024)));
builder.Services.AddProblemDetails();
builder.Services.AddApiRateLimits(builder.Configuration);
builder.Services.AddOpenApi();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddApplicationDatabase(builder.Configuration);
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient();

// Register application services
builder.Services.AddScoped<IMovieService, DatabaseMovieService>();
builder.Services.AddSingleton<IWatchlistService, WatchlistService>();
builder.Services.AddScoped<IGenreService, DatabaseGenreService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordResetEmailSender, PasswordResetEmailSender>();
builder.Services.AddScoped<ISecurityAuditService, SecurityAuditService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<DevelopmentDataSeeder>();
builder.Services.AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, OpaqueBearerHandler>("Bearer", null);
builder.Services.AddAuthorization(options => options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin")));

// Enable CORS for Blazor Client
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientOrigins", policy =>
    {
        var origins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0) policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Content-Type", "Authorization");
    });
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await DatabaseSchemaHelper.EnsureTablesCreatedAsync(db);
    // Revoke historical plaintext/predictable tokens; never migrate them into valid sessions.
    var legacyAccess = await db.AccessTokens.Where(item => !item.Token.StartsWith("sha256:")).ToListAsync();
    var legacyResets = await db.PasswordResetTokens.Where(item => !item.Token.StartsWith("sha256:")).ToListAsync();
    db.AccessTokens.RemoveRange(legacyAccess);
    db.PasswordResetTokens.RemoveRange(legacyResets);
    await db.SaveChangesAsync();
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:SeedDemoData"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSiteSecurity(client: false, app.Environment.IsDevelopment(), app.Configuration);
app.UseExceptionHandler();
if (app.Environment.IsProduction()) { app.UseHsts(); app.UseHttpsRedirection(); app.RequireHttpsTransport(); }
app.UseRouting();
app.UseCors("ClientOrigins");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not automatically seed database. Ensure MySQL server is running or run database/mysql/002_seed_data.sql manually.");
    }
}

app.Run();
