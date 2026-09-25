using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Client.Services;

public sealed class AuthState(IAccountApiClient apiClient, IAuthSessionStore? sessionStore = null)
{
    private Task? initialization;
    public bool IsInitialized { get; private set; }
    public UserDto? User { get; private set; }
    public string? AccessToken { get; private set; }
    public bool IsAuthenticated => User is not null && !string.IsNullOrWhiteSpace(AccessToken);
    public bool IsAdmin => User?.IsAdmin == true && (User.MfaEnabled == false || User.MfaSessionVerified);
    public event Action? Changed;
    public event Action? OnChange { add => Changed += value; remove => Changed -= value; }

    public async Task<ApiResponse<AuthDto>?> RegisterAsync(RegisterRequest request) =>
        await ApplySessionAsync(await apiClient.RegisterAsync(request));

    public async Task<ApiResponse<AuthDto>?> LoginAsync(LoginRequest request) =>
        await ApplySessionAsync(await apiClient.LoginAsync(request));

    public async Task<ApiResponse<AuthDto>?> VerifyMfaLoginAsync(VerifyMfaLoginRequest request) =>
        await ApplySessionAsync(await apiClient.VerifyMfaLoginAsync(request));

    public Task InitializeAsync() => initialization ??= RestoreAsync();

    private async Task RestoreAsync()
    {
        try
        {
            AccessToken = sessionStore is null ? null : await sessionStore.ReadAsync();
            apiClient.SetAccessToken(AccessToken);
            if (!string.IsNullOrWhiteSpace(AccessToken))
            {
                User = await apiClient.GetMeAsync();
                if (User is null) await LogoutAsync();
            }
        }
        finally { IsInitialized = true; Changed?.Invoke(); }
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(AccessToken)) return;
        User = await apiClient.GetMeAsync();
        if (User is null) await LogoutAsync();
        else Changed?.Invoke();
    }

    public void Logout()
    {
        AccessToken = null;
        User = null;
        apiClient.SetAccessToken(null);
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        try { if (!string.IsNullOrWhiteSpace(AccessToken)) await apiClient.LogoutAsync(); }
        finally { Logout(); if (sessionStore is not null) await sessionStore.ClearAsync(); }
    }

    private async Task<ApiResponse<AuthDto>?> ApplySessionAsync(ApiResponse<AuthDto>? response)
    {
        if (response?.Success == true && response.Data is { RequiresMfa: false } data && !string.IsNullOrWhiteSpace(data.AccessToken))
        {
            AccessToken = data.AccessToken;
            User = data.User;
            apiClient.SetAccessToken(AccessToken);
            IsInitialized = true;
            initialization = Task.CompletedTask;
            if (sessionStore is not null) await sessionStore.WriteAsync(AccessToken);
            Changed?.Invoke();
        }
        return response;
    }
}
