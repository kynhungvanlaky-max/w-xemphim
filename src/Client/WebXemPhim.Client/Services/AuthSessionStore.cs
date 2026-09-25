using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace WebXemPhim.Client.Services;

public interface IAuthSessionStore
{
    Task<string?> ReadAsync();
    Task WriteAsync(string token);
    Task ClearAsync();
}

// Only an encrypted token is kept in this tab; identity is always verified by /me.
public sealed class AuthSessionStore(ProtectedSessionStorage storage) : IAuthSessionStore
{
    private const string Key = "cinemaflow.auth.v1";
    public async Task<string?> ReadAsync()
    {
        try { var value = await storage.GetAsync<string>(Key); return value.Success ? value.Value : null; }
        catch (System.Security.Cryptography.CryptographicException) { await ClearAsync(); return null; }
    }
    public async Task WriteAsync(string token) => await storage.SetAsync(Key, token);
    public async Task ClearAsync() => await storage.DeleteAsync(Key);
}
