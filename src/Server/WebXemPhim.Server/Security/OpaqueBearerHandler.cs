using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using WebXemPhim.Server.Services;

namespace WebXemPhim.Server.Security;

public sealed class OpaqueBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthService authService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var authorization = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return AuthenticateResult.Fail("Authorization header must use the Bearer scheme.");
            token = authorization["Bearer ".Length..].Trim();
        }
        else if (Request.Path.StartsWithSegments("/hubs") && Request.Query.TryGetValue("access_token", out var queryToken) && !string.IsNullOrWhiteSpace(queryToken))
        {
            token = queryToken.ToString().Trim();
        }

        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.NoResult();
        if (!CredentialSecurity.IsToken(token)) return AuthenticateResult.Fail("Bearer token is malformed.");

        var user = await authService.ValidateAccessTokenAsync(token);
        if (user is null) return AuthenticateResult.Fail("Bearer token is invalid or expired.");

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("membership", user.Membership.ToString())
        };
        if (user.IsAdmin && (!user.MfaEnabled || user.MfaSessionVerified)) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
