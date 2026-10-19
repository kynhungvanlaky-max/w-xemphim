using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Client.Services;

public interface IAdminApiClient
{
    Task<AdminDashboardDto?> GetStatsAsync();
    Task<List<MovieDetailDto>> GetMoviesAsync(string? query = null, MovieType? type = null);
    Task<PagedResult<MovieDetailDto>> GetMoviesPagedAsync(int page = 1, int pageSize = 20, string? query = null, MovieType? type = null, string? genre = null, string? quality = null, string? status = null, string? sortBy = null);
    Task<ApiResponse<MovieDetailDto>?> CreateMovieAsync(UpsertMovieRequest request);
    Task<ApiResponse<MovieDetailDto>?> UpdateMovieAsync(int id, UpsertMovieRequest request);
    Task<bool> DeleteMovieAsync(int id);
    Task<List<EpisodeDto>> GetEpisodesAsync(int movieId);
    Task<ApiResponse<EpisodeDto>?> AddEpisodeAsync(int movieId, UpsertEpisodeRequest request);
    Task<bool> DeleteEpisodeAsync(int episodeId);
    Task<List<UserDto>> GetUsersAsync(string? query = null, MembershipTier? tier = null);
    Task<PagedResult<UserDto>> GetUsersPagedAsync(int page = 1, int pageSize = 20, string? query = null, MembershipTier? tier = null);
    Task<ApiResponse<UserDto>?> UpdateUserMembershipAsync(Guid userId, AdminUpdateUserRequest request);
    Task<List<PaymentDto>> GetPaymentsAsync(string? query = null, PaymentStatus? status = null);
    Task<PagedResult<PaymentDto>> GetPaymentsPagedAsync(int page = 1, int pageSize = 20, string? query = null, PaymentStatus? status = null);
    Task<List<AdminReviewDto>> GetReviewsAsync(string? query = null);
    Task<PagedResult<AdminReviewDto>> GetReviewsPagedAsync(int page = 1, int pageSize = 20, string? query = null);
    Task<bool> DeleteReviewAsync(Guid reviewId);
    Task<SystemHealthDto?> GetSystemHealthAsync();
    Task<List<AuditLogDto>> GetAuditLogsAsync(string? query = null, string? entityType = null, int take = 50);
    Task<PagedResult<AuditLogDto>> GetAuditLogsPagedAsync(string? query = null, string? entityType = null, int page = 1, int pageSize = 50);
    Task<ApiResponse<int>?> BulkMovieActionAsync(BulkMovieActionRequest request);
    Task<ApiResponse<bool>?> ToggleMovieTrendingAsync(int id);
    Task<ApiResponse<bool>?> ToggleMovieFeaturedAsync(int id);
    Task<ApiResponse<MovieDetailDto>?> CloneMovieAsync(int id, CloneMovieRequest request);
    Task<List<StreamSourceDto>> GetEpisodeStreamsAsync(int episodeId);
    Task<ApiResponse<StreamSourceDto>?> AddStreamSourceAsync(int episodeId, UpsertStreamSourceRequest request);
    Task<ApiResponse<StreamSourceDto>?> UpdateStreamSourceAsync(int episodeId, int position, UpsertStreamSourceRequest request);
    Task<ApiResponse<bool>?> DeleteStreamSourceAsync(int episodeId, int position);
    Task<ApiResponse<ProbeStreamResultDto>?> ProbeStreamAsync(ProbeStreamRequest request);
    Task<ApiResponse<int>?> BatchGenerateEpisodesAsync(BatchEpisodeGenerateRequest request);
    Task<AdminUserDetailDto?> GetUserDetailAsync(Guid userId);
    Task<ApiResponse<bool>?> UpdateUserAdminRoleAsync(Guid userId, AdminUpdateUserRoleRequest request);
    Task<ApiResponse<bool>?> UpdateUserLockStatusAsync(Guid userId, AdminUpdateUserLockRequest request);
    Task<ApiResponse<AdminReviewDto>?> ReplyToReviewAsync(Guid reviewId, AdminReviewModerationRequest request);
    Task<ApiResponse<PaymentDto>?> MarkPaymentPaidAsync(Guid paymentId, ManualPaymentApprovalRequest request);
    Task<ApiResponse<PaymentDto>?> RejectPaymentAsync(Guid paymentId, PaymentRejectionRequest request);
    Task<List<VipPlanDto>> GetVipPlansAsync();
    Task<ApiResponse<VipPlanDto>?> UpsertVipPlanAsync(string id, UpsertVipPlanRequest request);
    Task<ApiResponse<UserDto>?> GrantVipToUserAsync(ManualVipGrantRequest request);
}

public sealed class AdminApiClient(
    HttpClient httpClient,
    AuthState authState,
    ILogger<AdminApiClient> logger) : IAdminApiClient
{
    public async Task<AdminDashboardDto?> GetStatsAsync() =>
        (await SendAsync<AdminDashboardDto>(HttpMethod.Get, "api/admin/stats"))?.Data;

    public async Task<SystemHealthDto?> GetSystemHealthAsync() =>
        (await SendAsync<SystemHealthDto>(HttpMethod.Get, "api/admin/system/health"))?.Data;

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsPagedAsync(string? query = null, string? entityType = null, int page = 1, int pageSize = 50)
    {
        var url = $"api/admin/audit-logs?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(entityType)) url += $"&entityType={Uri.EscapeDataString(entityType)}";

        return (await SendAsync<PagedResult<AuditLogDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<AuditLogDto>();
    }

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(string? query = null, string? entityType = null, int take = 50)
    {
        var paged = await GetAuditLogsPagedAsync(query, entityType, 1, take > 0 ? take : 50);
        return paged.Items;
    }

    public Task<ApiResponse<int>?> BulkMovieActionAsync(BulkMovieActionRequest request) =>
        SendAsync<int>(HttpMethod.Post, "api/admin/movies/bulk-action", request);

    public Task<ApiResponse<bool>?> ToggleMovieTrendingAsync(int id) =>
        SendAsync<bool>(HttpMethod.Post, $"api/admin/movies/{id}/toggle-trending");

    public Task<ApiResponse<bool>?> ToggleMovieFeaturedAsync(int id) =>
        SendAsync<bool>(HttpMethod.Post, $"api/admin/movies/{id}/toggle-featured");

    public Task<ApiResponse<MovieDetailDto>?> CloneMovieAsync(int id, CloneMovieRequest request) =>
        SendAsync<MovieDetailDto>(HttpMethod.Post, $"api/admin/movies/{id}/clone", request);

    public async Task<List<StreamSourceDto>> GetEpisodeStreamsAsync(int episodeId) =>
        (await SendAsync<List<StreamSourceDto>>(HttpMethod.Get, $"api/admin/episodes/{episodeId}/streams"))?.Data ?? [];

    public Task<ApiResponse<StreamSourceDto>?> AddStreamSourceAsync(int episodeId, UpsertStreamSourceRequest request) =>
        SendAsync<StreamSourceDto>(HttpMethod.Post, $"api/admin/episodes/{episodeId}/streams", request);

    public Task<ApiResponse<StreamSourceDto>?> UpdateStreamSourceAsync(int episodeId, int position, UpsertStreamSourceRequest request) =>
        SendAsync<StreamSourceDto>(HttpMethod.Put, $"api/admin/episodes/{episodeId}/streams/{position}", request);

    public Task<ApiResponse<bool>?> DeleteStreamSourceAsync(int episodeId, int position) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/admin/episodes/{episodeId}/streams/{position}");

    public Task<ApiResponse<ProbeStreamResultDto>?> ProbeStreamAsync(ProbeStreamRequest request) =>
        SendAsync<ProbeStreamResultDto>(HttpMethod.Post, "api/admin/streams/probe", request);

    public Task<ApiResponse<int>?> BatchGenerateEpisodesAsync(BatchEpisodeGenerateRequest request) =>
        SendAsync<int>(HttpMethod.Post, "api/admin/episodes/batch-generate", request);

    public async Task<AdminUserDetailDto?> GetUserDetailAsync(Guid userId) =>
        (await SendAsync<AdminUserDetailDto>(HttpMethod.Get, $"api/admin/users/{userId}/details"))?.Data;

    public Task<ApiResponse<bool>?> UpdateUserAdminRoleAsync(Guid userId, AdminUpdateUserRoleRequest request) =>
        SendAsync<bool>(HttpMethod.Put, $"api/admin/users/{userId}/admin-role", request);

    public Task<ApiResponse<bool>?> UpdateUserLockStatusAsync(Guid userId, AdminUpdateUserLockRequest request) =>
        SendAsync<bool>(HttpMethod.Put, $"api/admin/users/{userId}/lock-status", request);

    public Task<ApiResponse<AdminReviewDto>?> ReplyToReviewAsync(Guid reviewId, AdminReviewModerationRequest request) =>
        SendAsync<AdminReviewDto>(HttpMethod.Post, $"api/admin/reviews/{reviewId}/reply", request);

    public Task<ApiResponse<PaymentDto>?> MarkPaymentPaidAsync(Guid paymentId, ManualPaymentApprovalRequest request) =>
        SendAsync<PaymentDto>(HttpMethod.Post, $"api/admin/payments/{paymentId}/mark-paid", request);

    public Task<ApiResponse<PaymentDto>?> RejectPaymentAsync(Guid paymentId, PaymentRejectionRequest request) =>
        SendAsync<PaymentDto>(HttpMethod.Post, $"api/admin/payments/{paymentId}/reject", request);

    public async Task<List<VipPlanDto>> GetVipPlansAsync() =>
        (await SendAsync<List<VipPlanDto>>(HttpMethod.Get, "api/admin/vip-plans"))?.Data ?? [];

    public Task<ApiResponse<VipPlanDto>?> UpsertVipPlanAsync(string id, UpsertVipPlanRequest request) =>
        SendAsync<VipPlanDto>(HttpMethod.Put, $"api/admin/vip-plans/{id}", request);

    public Task<ApiResponse<UserDto>?> GrantVipToUserAsync(ManualVipGrantRequest request) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/admin/users/grant-vip", request);

    public async Task<PagedResult<MovieDetailDto>> GetMoviesPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        MovieType? type = null,
        string? genre = null,
        string? quality = null,
        string? status = null,
        string? sortBy = null)
    {
        var url = $"api/admin/movies?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (type.HasValue) queryParams.Add($"type={(int)type.Value}");
        if (!string.IsNullOrWhiteSpace(genre)) queryParams.Add($"genre={Uri.EscapeDataString(genre)}");
        if (!string.IsNullOrWhiteSpace(quality)) queryParams.Add($"quality={Uri.EscapeDataString(quality)}");
        if (!string.IsNullOrWhiteSpace(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (queryParams.Count > 0) url += "&" + string.Join("&", queryParams);

        return (await SendAsync<PagedResult<MovieDetailDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<MovieDetailDto>();
    }

    public async Task<List<MovieDetailDto>> GetMoviesAsync(string? query = null, MovieType? type = null)
    {
        var paged = await GetMoviesPagedAsync(page: 1, pageSize: 1000, query: query, type: type);
        return paged.Items;
    }

    public Task<ApiResponse<MovieDetailDto>?> CreateMovieAsync(UpsertMovieRequest request) =>
        SendAsync<MovieDetailDto>(HttpMethod.Post, "api/admin/movies", request);

    public Task<ApiResponse<MovieDetailDto>?> UpdateMovieAsync(int id, UpsertMovieRequest request) =>
        SendAsync<MovieDetailDto>(HttpMethod.Put, $"api/admin/movies/{id}", request);

    public async Task<bool> DeleteMovieAsync(int id) =>
        (await SendAsync<bool>(HttpMethod.Delete, $"api/admin/movies/{id}"))?.Data ?? false;

    public async Task<List<EpisodeDto>> GetEpisodesAsync(int movieId) =>
        (await SendAsync<List<EpisodeDto>>(HttpMethod.Get, $"api/admin/movies/{movieId}/episodes"))?.Data ?? [];

    public Task<ApiResponse<EpisodeDto>?> AddEpisodeAsync(int movieId, UpsertEpisodeRequest request) =>
        SendAsync<EpisodeDto>(HttpMethod.Post, $"api/admin/movies/{movieId}/episodes", request);

    public async Task<bool> DeleteEpisodeAsync(int episodeId) =>
        (await SendAsync<bool>(HttpMethod.Delete, $"api/admin/episodes/{episodeId}"))?.Data ?? false;

    public async Task<PagedResult<UserDto>> GetUsersPagedAsync(int page = 1, int pageSize = 20, string? query = null, MembershipTier? tier = null)
    {
        var url = $"api/admin/users?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (tier.HasValue) queryParams.Add($"tier={(int)tier.Value}");
        if (queryParams.Count > 0) url += "&" + string.Join("&", queryParams);

        return (await SendAsync<PagedResult<UserDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<UserDto>();
    }

    public async Task<List<UserDto>> GetUsersAsync(string? query = null, MembershipTier? tier = null)
    {
        var paged = await GetUsersPagedAsync(page: 1, pageSize: 1000, query: query, tier: tier);
        return paged.Items;
    }

    public Task<ApiResponse<UserDto>?> UpdateUserMembershipAsync(Guid userId, AdminUpdateUserRequest request) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/admin/users/{userId}/membership", request);

    public async Task<PagedResult<PaymentDto>> GetPaymentsPagedAsync(int page = 1, int pageSize = 20, string? query = null, PaymentStatus? status = null)
    {
        var url = $"api/admin/payments?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (status.HasValue) queryParams.Add($"status={(int)status.Value}");
        if (queryParams.Count > 0) url += "&" + string.Join("&", queryParams);

        return (await SendAsync<PagedResult<PaymentDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<PaymentDto>();
    }

    public async Task<List<PaymentDto>> GetPaymentsAsync(string? query = null, PaymentStatus? status = null)
    {
        var paged = await GetPaymentsPagedAsync(page: 1, pageSize: 1000, query: query, status: status);
        return paged.Items;
    }

    public async Task<PagedResult<AdminReviewDto>> GetReviewsPagedAsync(int page = 1, int pageSize = 20, string? query = null)
    {
        var url = $"api/admin/reviews?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&q={Uri.EscapeDataString(query)}";

        return (await SendAsync<PagedResult<AdminReviewDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<AdminReviewDto>();
    }

    public async Task<List<AdminReviewDto>> GetReviewsAsync(string? query = null)
    {
        var paged = await GetReviewsPagedAsync(page: 1, pageSize: 1000, query: query);
        return paged.Items;
    }

    public async Task<bool> DeleteReviewAsync(Guid reviewId) =>
        (await SendAsync<bool>(HttpMethod.Delete, $"api/admin/reviews/{reviewId}"))?.Data ?? false;

    private async Task<ApiResponse<T>?> SendAsync<T>(HttpMethod method, string uri, object? body = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (!string.IsNullOrWhiteSpace(authState.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

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
                    // Ignore deserialization failure on empty body
                }

                return new ApiResponse<T>
                {
                    Success = false,
                    Message = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized => "Vui lòng đăng nhập với quyền quản trị viên.",
                        System.Net.HttpStatusCode.Forbidden => "Bạn không có quyền truy cập trang quản trị.",
                        _ => $"Yêu cầu không thành công (Mã lỗi: {(int)response.StatusCode})."
                    }
                };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Không thể gọi admin API {Method} {Uri}.", method, uri);
            return null;
        }
    }
}
