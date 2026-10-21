using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;

namespace WebXemPhim.Client.Services;

public interface IAuthSessionStore
{
    Task<string?> ReadAsync();
    Task WriteAsync(string token);
    Task ClearAsync();
}

// Only an encrypted token is kept in this tab; identity is always verified by /me.
public sealed class AuthSessionStore(ProtectedSessionStorage storage, ILogger<AuthSessionStore>? logger = null) : IAuthSessionStore
{
    private const string Key = "cinemaflow.auth.v1";
    private readonly ILogger<AuthSessionStore>? _logger = logger;

    public async Task<string?> ReadAsync()
    {
        try { var value = await storage.GetAsync<string>(Key); return value.Success ? value.Value : null; }
        catch (InvalidOperationException ex)
        {
            // Browser storage is not available yet (prerender or JS disabled).
            _logger?.LogDebug(ex, "Session storage is not available while reading the auth token.");
            return null;
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            _logger?.LogWarning(ex, "Stored auth token could not be decrypted and was discarded.");
            await ClearAsync();
            return null;
        }
    }

    public async Task WriteAsync(string token)
    {
        try { await storage.SetAsync(Key, token); }
        catch (InvalidOperationException ex)
        {
            _logger?.LogDebug(ex, "Session storage is not available while writing the auth token.");
        }
    }

    public async Task ClearAsync()
    {
        try { await storage.DeleteAsync(Key); }
        catch (InvalidOperationException ex)
        {
            _logger?.LogDebug(ex, "Session storage is not available while clearing the auth token.");
        }
    }
}
