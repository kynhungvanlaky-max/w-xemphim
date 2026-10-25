using Microsoft.AspNetCore.Http;

namespace WebXemPhim.Server.Security;

/// <summary>
/// Resolves the base URL the **browser** has to use to reach the API.
/// <para>
/// The playback URL is handed to the browser, so it must point at a host the browser can
/// resolve. Inside the container network the request host is the internal service name
/// (for example <c>server:8080</c>), which a browser on the host machine cannot open —
/// the player then failed with a media error. The public base URL is therefore taken from
/// configuration and only falls back to the request host when nothing is configured.
/// </para>
/// </summary>
public sealed class PublicUrlResolver(IConfiguration configuration)
{
    /// <summary>Configuration key holding the public base URL of the API.</summary>
    public const string ConfigurationKey = "Security:PublicApiBaseUrl";

    /// <summary>Environment variable alternative for <see cref="ConfigurationKey"/>.</summary>
    public const string EnvironmentKey = "PUBLIC_API_BASE_URL";

    /// <summary>
    /// The configured public base URL, or <c>null</c> when the deployment did not set one.
    /// </summary>
    public string? ConfiguredBaseUrl
    {
        get
        {
            var value = configuration[ConfigurationKey];
            if (string.IsNullOrWhiteSpace(value)) value = configuration[EnvironmentKey];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
        }
    }

    /// <summary>
    /// Returns the base URL that the browser must use. When a public base URL is configured it
    /// always wins; otherwise the host of the incoming request is used.
    /// </summary>
    public string Resolve(HttpRequest? request)
    {
        var configured = ConfiguredBaseUrl;
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        return request is not null && request.Host.HasValue
            ? $"{request.Scheme}://{request.Host}"
            : "http://localhost";
    }
}
