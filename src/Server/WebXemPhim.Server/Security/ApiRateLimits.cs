using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WebXemPhim.Server.Security;

public static class ApiRateLimits
{
    private static string GetPartitionKey(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:ApiPerMinute", 300))));
            options.AddPolicy("Authentication", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => Window(config.GetValue("Security:RateLimits:AuthPerMinute", 15))));
            options.AddPolicy("CouponValidation", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:CouponsPerMinute", 10))));
            options.AddPolicy("Checkout", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:CheckoutPerMinute", 15))));
            options.AddPolicy("UserSubmissions", context => RateLimitPartition.GetFixedWindowLimiter(GetPartitionKey(context), _ => Window(config.GetValue("Security:RateLimits:SubmissionsPerMinute", 30))));
            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message = "Quá nhiều yêu cầu. Vui lòng thử lại sau." }, token);
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = Math.Clamp(permits, 1, 10000), Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0, AutoReplenishment = true
    };
}
