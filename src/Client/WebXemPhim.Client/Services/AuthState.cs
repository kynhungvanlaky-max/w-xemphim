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

    /// <summary>
    /// True khi đã có token trong phiên, kể cả khi hồ sơ người dùng chưa tải được vì API
    /// chưa sẵn sàng. Dùng để biết trước rằng người dùng có thể vào khu vực quản trị.
    /// </summary>
    public bool HasSession => !string.IsNullOrWhiteSpace(AccessToken);
    public bool IsAdmin => User?.IsAdmin == true && (User.MfaEnabled == false || User.MfaSessionVerified);

    /// <summary>
    /// True khi API chưa phản hồi được (máy chủ chưa sẵn sàng lúc mở trang lần đầu).
    /// Trong trạng thái này phiên đăng nhập được giữ nguyên và giao diện tự thử lại,
    /// thay vì đá người dùng ra trang đăng nhập.
    /// </summary>
    public bool ApiUnavailable { get; private set; }
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
                await RestoreUserAsync();
            }
        }
        catch (Exception)
        {
            // The session storage or the API misbehaved. The token is kept so the next
            // attempt can still restore the session instead of forcing a new login.
            ApiUnavailable = true;
        }
        finally { IsInitialized = true; Changed?.Invoke(); }
    }

    /// <summary>
    /// Lấy lại hồ sơ người dùng từ API. Chỉ xoá phiên khi API **đã trả lời** và báo token
    /// không hợp lệ; nếu request thất bại (API chưa bật) thì phiên được giữ lại.
    /// </summary>
    private async Task RestoreUserAsync()
    {
        var response = await apiClient.GetMeResultAsync();
        if (response is null)
        {
            ApiUnavailable = true;
            return;
        }

        ApiUnavailable = false;
        if (!response.Success || response.Data is null)
        {
            await LogoutAsync();
            return;
        }

        User = response.Data;
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(AccessToken)) return;
        await RestoreUserAsync();
        Changed?.Invoke();
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
