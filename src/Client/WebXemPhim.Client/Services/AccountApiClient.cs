using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Client.Services;

public interface IAccountApiClient
{
    void SetAccessToken(string? token);
    Task<ApiResponse<AuthDto>?> RegisterAsync(RegisterRequest request);
    Task<ApiResponse<AuthDto>?> LoginAsync(LoginRequest request);
    Task<ApiResponse<AuthDto>?> VerifyMfaLoginAsync(VerifyMfaLoginRequest request);
    Task<bool> LogoutAsync();
    Task<ApiResponse<PasswordResetDto?>?> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<ApiResponse<bool>?> ResetPasswordAsync(ResetPasswordRequest request);
    Task<ApiResponse<bool>?> ChangePasswordAsync(ChangePasswordRequest request);
    Task<UserDto?> GetMeAsync();
    Task<List<VipPlanDto>> GetPlansAsync();
    Task<ApiResponse<CouponValidationResultDto>?> ApplyCouponAsync(ApplyCouponRequest request);
    Task<ApiResponse<PaymentDto>?> CheckoutAsync(CreatePaymentRequest request);
    Task<List<PaymentDto>> GetPaymentHistoryAsync();
    Task<MovieReviewsDto?> GetReviewsAsync(int movieId);
    Task<ApiResponse<ReviewDto>?> SaveReviewAsync(int movieId, UpsertReviewRequest request);
    Task<bool> DeleteReviewAsync(int movieId);
    Task<UserSettingsDto?> GetSettingsAsync();
    Task<ApiResponse<bool>?> UpdateSettingsAsync(UserSettingsDto request);
    Task<MfaStatusDto?> GetMfaStatusAsync();
    Task<ApiResponse<MfaSetupDto>?> BeginMfaSetupAsync(BeginMfaSetupRequest request);
    Task<ApiResponse<MfaRecoveryCodesDto>?> EnableMfaAsync(EnableMfaRequest request);
    Task<ApiResponse<bool>?> DisableMfaAsync(DisableMfaRequest request);
    Task<List<NotificationDto>> GetNotificationsAsync();
    Task<bool> MarkNotificationReadAsync(Guid id);
    Task<bool> MarkAllNotificationsReadAsync();
    Task<List<WatchProgressDto>> GetContinueWatchingAsync();
    Task<bool> SaveWatchProgressAsync(UpdateWatchProgressRequest request);
}

public sealed class AccountApiClient(HttpClient httpClient, ILogger<AccountApiClient> logger) : IAccountApiClient
{
    private string? _accessToken;

    public void SetAccessToken(string? token) => _accessToken = token;

    public Task<ApiResponse<AuthDto>?> RegisterAsync(RegisterRequest request) =>
        SendAsync<AuthDto>(HttpMethod.Post, "api/auth/register", request);

    public Task<ApiResponse<AuthDto>?> LoginAsync(LoginRequest request) =>
        SendAsync<AuthDto>(HttpMethod.Post, "api/auth/login", request);

    public Task<ApiResponse<AuthDto>?> VerifyMfaLoginAsync(VerifyMfaLoginRequest request) =>
        SendAsync<AuthDto>(HttpMethod.Post, "api/auth/mfa/verify-login", request);

    public async Task<bool> LogoutAsync() => (await SendAsync<bool>(HttpMethod.Post, "api/auth/logout"))?.Success == true;

    public Task<ApiResponse<PasswordResetDto?>?> ForgotPasswordAsync(ForgotPasswordRequest request) =>
        SendAsync<PasswordResetDto?>(HttpMethod.Post, "api/auth/forgot-password", request);

    public Task<ApiResponse<bool>?> ResetPasswordAsync(ResetPasswordRequest request) =>
        SendAsync<bool>(HttpMethod.Post, "api/auth/reset-password", request);

    public Task<ApiResponse<bool>?> ChangePasswordAsync(ChangePasswordRequest request) =>
        SendAsync<bool>(HttpMethod.Post, "api/auth/change-password", request);

    public async Task<UserDto?> GetMeAsync() =>
        (await SendAsync<UserDto>(HttpMethod.Get, "api/auth/me"))?.Data;

    public async Task<List<VipPlanDto>> GetPlansAsync() =>
        (await SendAsync<List<VipPlanDto>>(HttpMethod.Get, "api/payments/plans"))?.Data ?? [];

    public Task<ApiResponse<CouponValidationResultDto>?> ApplyCouponAsync(ApplyCouponRequest request) =>
        SendAsync<CouponValidationResultDto>(HttpMethod.Post, "api/payments/apply-coupon", request);

    public Task<ApiResponse<PaymentDto>?> CheckoutAsync(CreatePaymentRequest request) =>
        SendAsync<PaymentDto>(HttpMethod.Post, "api/payments/checkout", request);

    public async Task<List<PaymentDto>> GetPaymentHistoryAsync() =>
        (await SendAsync<List<PaymentDto>>(HttpMethod.Get, "api/payments/history"))?.Data ?? [];

    public async Task<MovieReviewsDto?> GetReviewsAsync(int movieId) =>
        (await SendAsync<MovieReviewsDto>(HttpMethod.Get, $"api/reviews/movie/{movieId}"))?.Data;

    public Task<ApiResponse<ReviewDto>?> SaveReviewAsync(int movieId, UpsertReviewRequest request) =>
        SendAsync<ReviewDto>(HttpMethod.Put, $"api/reviews/movie/{movieId}", request);

    public async Task<bool> DeleteReviewAsync(int movieId) =>
        (await SendAsync<bool>(HttpMethod.Delete, $"api/reviews/movie/{movieId}"))?.Data ?? false;

    public async Task<UserSettingsDto?> GetSettingsAsync() =>
        (await SendAsync<UserSettingsDto>(HttpMethod.Get, "api/auth/settings"))?.Data;

    public Task<ApiResponse<bool>?> UpdateSettingsAsync(UserSettingsDto request) =>
        SendAsync<bool>(HttpMethod.Put, "api/auth/settings", request);

    public async Task<MfaStatusDto?> GetMfaStatusAsync() =>
        (await SendAsync<MfaStatusDto>(HttpMethod.Get, "api/auth/mfa"))?.Data;

    public Task<ApiResponse<MfaSetupDto>?> BeginMfaSetupAsync(BeginMfaSetupRequest request) =>
        SendAsync<MfaSetupDto>(HttpMethod.Post, "api/auth/mfa/setup", request);

    public Task<ApiResponse<MfaRecoveryCodesDto>?> EnableMfaAsync(EnableMfaRequest request) =>
        SendAsync<MfaRecoveryCodesDto>(HttpMethod.Post, "api/auth/mfa/enable", request);

    public Task<ApiResponse<bool>?> DisableMfaAsync(DisableMfaRequest request) =>
        SendAsync<bool>(HttpMethod.Post, "api/auth/mfa/disable", request);

    public async Task<List<NotificationDto>> GetNotificationsAsync() =>
        (await SendAsync<List<NotificationDto>>(HttpMethod.Get, "api/notifications"))?.Data ?? [];

    public async Task<bool> MarkNotificationReadAsync(Guid id) =>
        (await SendAsync<bool>(HttpMethod.Put, $"api/notifications/{id}/read"))?.Data ?? false;

    public async Task<bool> MarkAllNotificationsReadAsync() =>
        (await SendAsync<bool>(HttpMethod.Put, "api/notifications/read-all"))?.Data ?? false;

    public async Task<List<WatchProgressDto>> GetContinueWatchingAsync() =>
        (await SendAsync<List<WatchProgressDto>>(HttpMethod.Get, "api/library/continue-watching"))?.Data ?? [];

    public async Task<bool> SaveWatchProgressAsync(UpdateWatchProgressRequest request) =>
        (await SendAsync<bool>(HttpMethod.Post, "api/library/progress", request))?.Data ?? false;

    private async Task<ApiResponse<T>?> SendAsync<T>(HttpMethod method, string uri, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (!string.IsNullOrWhiteSpace(_accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            if (body is not null) request.Content = JsonContent.Create(body);

            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var errorResponse = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
                    if (errorResponse is not null) return errorResponse;
                }
                catch
                {
                    // Ignore deserialization failure on non-success status code
                }

                return new ApiResponse<T>
                {
                    Success = false,
                    Message = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Vui lòng đăng nhập để thực hiện thao tác này."
                        : $"Yêu cầu không thành công (Mã lỗi: {(int)response.StatusCode})."
                };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Không thể gọi account API {Method} {Uri}.", method, uri);
            return null;
        }
    }
}
