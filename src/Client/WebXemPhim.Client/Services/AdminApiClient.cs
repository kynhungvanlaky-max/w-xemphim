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
    Task<PagedResult<MovieDetailDto>> GetMoviesPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        MovieType? type = null,
        string? genre = null,
        string? quality = null,
        string? status = null,
        string? sortBy = null,
        int? yearFrom = null,
        int? yearTo = null,
        string? country = null,
        double? minRating = null,
        double? maxRating = null,
        bool summary = false);
    Task<ApiResponse<MovieDetailDto>?> CreateMovieAsync(UpsertMovieRequest request);
    Task<ApiResponse<MovieDetailDto>?> UpdateMovieAsync(int id, UpsertMovieRequest request);
    Task<bool> DeleteMovieAsync(int id);
    Task<List<EpisodeDto>> GetEpisodesAsync(int movieId);
    Task<ApiResponse<EpisodeDto>?> AddEpisodeAsync(int movieId, UpsertEpisodeRequest request);
    Task<ApiResponse<EpisodeDto>?> UpdateEpisodeAsync(int episodeId, UpdateEpisodeRequest request);
    Task<bool> DeleteEpisodeAsync(int episodeId);
    Task<ApiResponse<EpisodeDto>?> UploadEpisodeThumbnailAsync(int episodeId, Stream stream, string fileName);
    Task<ApiResponse<bool>?> DeleteEpisodeThumbnailAsync(int episodeId);
    Task<List<UserDto>> GetUsersAsync(string? query = null, MembershipTier? tier = null);
    Task<PagedResult<UserDto>> GetUsersPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        MembershipTier? tier = null,
        string? role = null,
        string? lockStatus = null,
        string? vipStatus = null,
        string? registeredPeriod = null,
        string? sortBy = null);
    Task<ApiResponse<UserDto>?> UpdateUserMembershipAsync(Guid userId, AdminUpdateUserRequest request);
    Task<ApiResponse<int>?> BulkUserActionAsync(AdminBulkUserActionRequest request);
    Task<List<PaymentDto>> GetPaymentsAsync(string? query = null, PaymentStatus? status = null);
    Task<AdminPaymentSummaryDto?> GetPaymentSummaryAsync(string? query = null, string? method = null);
    Task<string?> ExportPaymentsCsvAsync(string? query = null, string? method = null, PaymentStatus? status = null);
    Task<PagedResult<PaymentDto>> GetPaymentsPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        PaymentStatus? status = null,
        string? method = null,
        string? sortBy = null);
    Task<List<AdminReviewDto>> GetReviewsAsync(string? query = null);
    Task<PagedResult<AdminReviewDto>> GetReviewsPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        string? status = null,
        int? minRating = null,
        int? maxRating = null,
        string? sortBy = null);
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
    Task<ApiResponse<VideoIngestResultDto>?> UploadEpisodeVideoAsync(
        int episodeId,
        Stream stream,
        string fileName,
        string masterMaxQuality = "1080p FHD",
        bool replaceExisting = true,
        string? targetServers = null,
        IProgress<double>? progress = null,
        long? totalBytes = null,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<List<StreamSourceDto>>?> AutoDistributeStreamsAsync(int episodeId, AutoProvisionStreamsRequest request);
    Task<ApiResponse<ProbeStreamResultDto>?> ProbeStreamAsync(ProbeStreamRequest request);
    Task<ApiResponse<List<StreamServerPolicyDto>>?> GetStreamServerPoliciesAsync();
    Task<ApiResponse<StreamServerPolicyDto>?> UpdateStreamServerPolicyAsync(string serverKey, UpdateStreamServerPolicyRequest request);
    Task<ApiResponse<List<StreamServerPolicyDto>>?> ResetStreamServerPoliciesAsync();
    Task<ApiResponse<int>?> BatchGenerateEpisodesAsync(BatchEpisodeGenerateRequest request);
    Task<AdminUserDetailDto?> GetUserDetailAsync(Guid userId);
    Task<ApiResponse<bool>?> UpdateUserAdminRoleAsync(Guid userId, AdminUpdateUserRoleRequest request);
    Task<ApiResponse<bool>?> UpdateUserLockStatusAsync(Guid userId, AdminUpdateUserLockRequest request);
    Task<ApiResponse<AdminReviewDto>?> ReplyToReviewAsync(Guid reviewId, AdminReviewModerationRequest request);
    Task<ApiResponse<PaymentDto>?> MarkPaymentPaidAsync(Guid paymentId, ManualPaymentApprovalRequest request);
    Task<ApiResponse<PaymentDto>?> RejectPaymentAsync(Guid paymentId, PaymentRejectionRequest request);
    Task<ApiResponse<CloudStorageStatusDto>?> GetCloudStorageStatusAsync();
    Task<ApiResponse<CloudStoragePresignedUploadDto>?> GetPresignedUploadUrlAsync(string fileName, string? contentType = null, string folder = "episodes");
    Task<ApiResponse<CloudStorageUploadResultDto>?> UploadToR2Async(
        Stream stream,
        string fileName,
        string folder = "episodes",
        long? totalBytes = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
    Task<List<VipPlanDto>> GetVipPlansAsync();
    Task<ApiResponse<VipPlanDto>?> UpsertVipPlanAsync(string id, UpsertVipPlanRequest request);
    Task<ApiResponse<UserDto>?> GrantVipToUserAsync(ManualVipGrantRequest request);
    Task<AdminTelemetrySummaryDto?> GetTelemetrySummaryAsync(int hours = 24);
    Task<PagedResult<AdminTelemetryEventDto>> GetTelemetryEventsAsync(
        string? eventType = null,
        int? episodeId = null,
        string? sessionId = null,
        int hours = 24,
        int page = 1,
        int pageSize = 50);
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

    public async Task<ApiResponse<VideoIngestResultDto>?> UploadEpisodeVideoAsync(
        int episodeId,
        Stream stream,
        string fileName,
        string masterMaxQuality = "1080p FHD",
        bool replaceExisting = true,
        string? targetServers = null,
        IProgress<double>? progress = null,
        long? totalBytes = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            Stream streamToSend = stream;
            if (progress is not null && totalBytes.HasValue && totalBytes.Value > 0)
            {
                streamToSend = new ProgressReportingStream(stream, totalBytes.Value, progress);
            }

            var streamContent = new StreamContent(streamToSend);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = extension switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                _ => "application/octet-stream"
            };
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);
            content.Add(new StringContent(masterMaxQuality), "masterMaxQuality");
            content.Add(new StringContent(replaceExisting.ToString().ToLowerInvariant()), "replaceExisting");
            if (!string.IsNullOrWhiteSpace(targetServers))
            {
                content.Add(new StringContent(targetServers), "targetServers");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/admin/episodes/{episodeId}/video")
            {
                Content = content
            };
            if (!string.IsNullOrWhiteSpace(authState.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            progress?.Report(100.0);
            return await ReadApiResponseAsync<VideoIngestResultDto>(response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể tải lên video cho tập {EpisodeId}.", episodeId);
            return new ApiResponse<VideoIngestResultDto> { Success = false, Message = ex.Message };
        }
    }

    public Task<ApiResponse<List<StreamSourceDto>>?> AutoDistributeStreamsAsync(int episodeId, AutoProvisionStreamsRequest request) =>
        SendAsync<List<StreamSourceDto>>(HttpMethod.Post, $"api/admin/episodes/{episodeId}/streams/auto-distribute", request);

    public Task<ApiResponse<CloudStorageStatusDto>?> GetCloudStorageStatusAsync() =>
        SendAsync<CloudStorageStatusDto>(HttpMethod.Get, "api/admin/storage/status");

    public Task<ApiResponse<CloudStoragePresignedUploadDto>?> GetPresignedUploadUrlAsync(
        string fileName,
        string? contentType = null,
        string folder = "episodes")
    {
        var url = $"api/admin/storage/presigned-upload-url?fileName={Uri.EscapeDataString(fileName)}&folder={Uri.EscapeDataString(folder)}";
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            url += $"&contentType={Uri.EscapeDataString(contentType)}";
        }
        return SendAsync<CloudStoragePresignedUploadDto>(HttpMethod.Get, url);
    }

    public async Task<ApiResponse<CloudStorageUploadResultDto>?> UploadToR2Async(
        Stream stream,
        string fileName,
        string folder = "episodes",
        long? totalBytes = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            Stream streamToSend = stream;
            if (progress is not null && totalBytes.HasValue && totalBytes.Value > 0)
            {
                streamToSend = new ProgressReportingStream(stream, totalBytes.Value, progress);
            }

            var streamContent = new StreamContent(streamToSend);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = extension switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);
            content.Add(new StringContent(folder), "folder");

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/storage/upload-r2")
            {
                Content = content
            };
            if (!string.IsNullOrWhiteSpace(authState.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            progress?.Report(100.0);
            return await ReadApiResponseAsync<CloudStorageUploadResultDto>(response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi khi tải lên Cloudflare R2: {FileName}", fileName);
            return new ApiResponse<CloudStorageUploadResultDto> { Success = false, Message = ex.Message };
        }
    }

    public Task<ApiResponse<ProbeStreamResultDto>?> ProbeStreamAsync(ProbeStreamRequest request) =>
        SendAsync<ProbeStreamResultDto>(HttpMethod.Post, "api/admin/streams/probe", request);

    public Task<ApiResponse<List<StreamServerPolicyDto>>?> GetStreamServerPoliciesAsync() =>
        SendAsync<List<StreamServerPolicyDto>>(HttpMethod.Get, "api/admin/streams/policies");

    public Task<ApiResponse<StreamServerPolicyDto>?> UpdateStreamServerPolicyAsync(string serverKey, UpdateStreamServerPolicyRequest request) =>
        SendAsync<StreamServerPolicyDto>(HttpMethod.Put, $"api/admin/streams/policies/{Uri.EscapeDataString(serverKey)}", request);

    public Task<ApiResponse<List<StreamServerPolicyDto>>?> ResetStreamServerPoliciesAsync() =>
        SendAsync<List<StreamServerPolicyDto>>(HttpMethod.Post, "api/admin/streams/policies/reset");

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

    public async Task<AdminTelemetrySummaryDto?> GetTelemetrySummaryAsync(int hours = 24) =>
        (await SendAsync<AdminTelemetrySummaryDto>(
            HttpMethod.Get,
            $"api/admin/telemetry/summary?hours={Math.Clamp(hours, 1, 168)}"))?.Data;

    public async Task<PagedResult<AdminTelemetryEventDto>> GetTelemetryEventsAsync(
        string? eventType = null,
        int? episodeId = null,
        string? sessionId = null,
        int hours = 24,
        int page = 1,
        int pageSize = 50)
    {
        var url = $"api/admin/telemetry/events?hours={Math.Clamp(hours, 1, 168)}&page={Math.Max(page, 1)}&pageSize={Math.Clamp(pageSize, 1, 200)}";
        if (!string.IsNullOrWhiteSpace(eventType)) url += $"&eventType={Uri.EscapeDataString(eventType.Trim())}";
        if (episodeId is > 0) url += $"&episodeId={episodeId.Value}";
        if (!string.IsNullOrWhiteSpace(sessionId)) url += $"&sessionId={Uri.EscapeDataString(sessionId.Trim())}";

        return (await SendAsync<PagedResult<AdminTelemetryEventDto>>(HttpMethod.Get, url))?.Data
               ?? new PagedResult<AdminTelemetryEventDto>();
    }

    public async Task<PagedResult<MovieDetailDto>> GetMoviesPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        MovieType? type = null,
        string? genre = null,
        string? quality = null,
        string? status = null,
        string? sortBy = null,
        int? yearFrom = null,
        int? yearTo = null,
        string? country = null,
        double? minRating = null,
        double? maxRating = null,
        bool summary = false)
    {
        var url = $"api/admin/movies?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (summary) queryParams.Add("summary=true");
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (type.HasValue) queryParams.Add($"type={(int)type.Value}");
        if (!string.IsNullOrWhiteSpace(genre)) queryParams.Add($"genre={Uri.EscapeDataString(genre)}");
        if (!string.IsNullOrWhiteSpace(quality)) queryParams.Add($"quality={Uri.EscapeDataString(quality)}");
        if (!string.IsNullOrWhiteSpace(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (yearFrom.HasValue) queryParams.Add($"yearFrom={yearFrom.Value}");
        if (yearTo.HasValue) queryParams.Add($"yearTo={yearTo.Value}");
        if (!string.IsNullOrWhiteSpace(country)) queryParams.Add($"country={Uri.EscapeDataString(country)}");
        if (minRating.HasValue) queryParams.Add($"minRating={minRating.Value}");
        if (maxRating.HasValue) queryParams.Add($"maxRating={maxRating.Value}");
        if (queryParams.Count > 0) url += "&" + string.Join("&", queryParams);

        return (await SendAsync<PagedResult<MovieDetailDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<MovieDetailDto>();
    }

    public async Task<List<MovieDetailDto>> GetMoviesAsync(string? query = null, MovieType? type = null)
    {
        var paged = await GetMoviesPagedAsync(page: 1, pageSize: 1000, query: query, type: type, summary: true);
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

    public Task<ApiResponse<EpisodeDto>?> UpdateEpisodeAsync(int episodeId, UpdateEpisodeRequest request) =>
        SendAsync<EpisodeDto>(HttpMethod.Put, $"api/admin/episodes/{episodeId}", request);

    public async Task<bool> DeleteEpisodeAsync(int episodeId) =>
        (await SendAsync<bool>(HttpMethod.Delete, $"api/admin/episodes/{episodeId}"))?.Data ?? false;

    public async Task<ApiResponse<EpisodeDto>?> UploadEpisodeThumbnailAsync(int episodeId, Stream stream, string fileName)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(stream);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".avif" => "image/avif",
                _ => "application/octet-stream"
            };
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/admin/episodes/{episodeId}/thumbnail")
            {
                Content = content
            };
            if (!string.IsNullOrWhiteSpace(authState.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            }

            using var response = await httpClient.SendAsync(request);
            return await ReadApiResponseAsync<EpisodeDto>(response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể tải lên ảnh nền cho tập {EpisodeId}.", episodeId);
            return new ApiResponse<EpisodeDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<bool>?> DeleteEpisodeThumbnailAsync(int episodeId) =>
        await SendAsync<bool>(HttpMethod.Delete, $"api/admin/episodes/{episodeId}/thumbnail");

    public async Task<PagedResult<UserDto>> GetUsersPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        MembershipTier? tier = null,
        string? role = null,
        string? lockStatus = null,
        string? vipStatus = null,
        string? registeredPeriod = null,
        string? sortBy = null)
    {
        var url = $"api/admin/users?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (tier.HasValue) queryParams.Add($"tier={(int)tier.Value}");
        if (!string.IsNullOrWhiteSpace(role)) queryParams.Add($"role={Uri.EscapeDataString(role)}");
        if (!string.IsNullOrWhiteSpace(lockStatus)) queryParams.Add($"lockStatus={Uri.EscapeDataString(lockStatus)}");
        if (!string.IsNullOrWhiteSpace(vipStatus)) queryParams.Add($"vipStatus={Uri.EscapeDataString(vipStatus)}");
        if (!string.IsNullOrWhiteSpace(registeredPeriod)) queryParams.Add($"registeredPeriod={Uri.EscapeDataString(registeredPeriod)}");
        if (!string.IsNullOrWhiteSpace(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
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

    public Task<ApiResponse<int>?> BulkUserActionAsync(AdminBulkUserActionRequest request) =>
        SendAsync<int>(HttpMethod.Post, "api/admin/users/bulk-action", request);

    public async Task<PagedResult<PaymentDto>> GetPaymentsPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        PaymentStatus? status = null,
        string? method = null,
        string? sortBy = null)
    {
        var url = $"api/admin/payments?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (status.HasValue) queryParams.Add($"status={(int)status.Value}");
        if (!string.IsNullOrWhiteSpace(method)) queryParams.Add($"method={Uri.EscapeDataString(method)}");
        if (!string.IsNullOrWhiteSpace(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (queryParams.Count > 0) url += "&" + string.Join("&", queryParams);

        return (await SendAsync<PagedResult<PaymentDto>>(HttpMethod.Get, url))?.Data ?? new PagedResult<PaymentDto>();
    }

    public async Task<List<PaymentDto>> GetPaymentsAsync(string? query = null, PaymentStatus? status = null)
    {
        var paged = await GetPaymentsPagedAsync(page: 1, pageSize: 1000, query: query, status: status);
        return paged.Items;
    }

    public async Task<AdminPaymentSummaryDto?> GetPaymentSummaryAsync(string? query = null, string? method = null)
    {
        var url = "api/admin/payments/summary";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(method)) queryParams.Add($"method={Uri.EscapeDataString(method)}");
        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

        return (await SendAsync<AdminPaymentSummaryDto>(HttpMethod.Get, url))?.Data;
    }

    public async Task<string?> ExportPaymentsCsvAsync(string? query = null, string? method = null, PaymentStatus? status = null)
    {
        try
        {
            var url = "api/admin/payments/export-csv";
            var queryParams = new List<string>();
            if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrWhiteSpace(method)) queryParams.Add($"method={Uri.EscapeDataString(method)}");
            if (status.HasValue) queryParams.Add($"status={(int)status.Value}");
            if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(authState.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authState.AccessToken);
            }

            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Xuất CSV thanh toán thất bại: HTTP {Status}.", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể xuất CSV thanh toán.");
            return null;
        }
    }

    public async Task<PagedResult<AdminReviewDto>> GetReviewsPagedAsync(
        int page = 1,
        int pageSize = 20,
        string? query = null,
        string? status = null,
        int? minRating = null,
        int? maxRating = null,
        string? sortBy = null)
    {
        var url = $"api/admin/reviews?page={page}&pageSize={pageSize}";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) queryParams.Add($"q={Uri.EscapeDataString(query)}");
        if (!string.IsNullOrWhiteSpace(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
        if (minRating.HasValue) queryParams.Add($"minRating={minRating.Value}");
        if (maxRating.HasValue) queryParams.Add($"maxRating={maxRating.Value}");
        if (!string.IsNullOrWhiteSpace(sortBy)) queryParams.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        if (queryParams.Count > 0) url += "&" + string.Join("&", queryParams);

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
            return await ReadApiResponseAsync<T>(response);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Không thể gọi admin API {Method} {Uri}.", method, uri);
            return null;
        }
    }

    private static async Task<ApiResponse<T>?> ReadApiResponseAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        }

        string? extractedMessage = null;
        string? extractedErrorCode = null;

        try
        {
            var rawJson = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (TryGetStringProperty(root, "message", out var msg) && !string.IsNullOrWhiteSpace(msg))
                    {
                        extractedMessage = msg;
                    }
                    if (TryGetStringProperty(root, "errorCode", out var code) && !string.IsNullOrWhiteSpace(code))
                    {
                        extractedErrorCode = code;
                    }

                    // Extract ASP.NET Core ValidationProblemDetails / ProblemDetails if message wasn't set
                    if (string.IsNullOrWhiteSpace(extractedMessage))
                    {
                        if (TryGetPropertyIgnoreCase(root, "errors", out var errorsElement) &&
                            errorsElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            var messages = new List<string>();
                            foreach (var prop in errorsElement.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var item in prop.Value.EnumerateArray())
                                    {
                                        if (item.ValueKind == System.Text.Json.JsonValueKind.String &&
                                            !string.IsNullOrWhiteSpace(item.GetString()))
                                        {
                                            messages.Add(item.GetString()!);
                                        }
                                    }
                                }
                            }
                            if (messages.Count > 0)
                            {
                                extractedMessage = string.Join(" ", messages);
                            }
                        }

                        if (string.IsNullOrWhiteSpace(extractedMessage) &&
                            TryGetStringProperty(root, "detail", out var detail) &&
                            !string.IsNullOrWhiteSpace(detail))
                        {
                            extractedMessage = detail;
                        }

                        if (string.IsNullOrWhiteSpace(extractedMessage) &&
                            TryGetStringProperty(root, "title", out var title) &&
                            !string.IsNullOrWhiteSpace(title))
                        {
                            extractedMessage = title;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore non-JSON error bodies
        }

        return new ApiResponse<T>
        {
            Success = false,
            ErrorCode = extractedErrorCode,
            Message = !string.IsNullOrWhiteSpace(extractedMessage)
                ? extractedMessage
                : response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Vui lòng đăng nhập với quyền quản trị viên.",
                    System.Net.HttpStatusCode.Forbidden => "Bạn không có quyền truy cập trang quản trị.",
                    _ => $"Yêu cầu không thành công (Mã lỗi: {(int)response.StatusCode})."
                }
        };
    }

    private static bool TryGetPropertyIgnoreCase(
        System.Text.Json.JsonElement element,
        string propertyName,
        out System.Text.Json.JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryGetStringProperty(
        System.Text.Json.JsonElement element,
        string propertyName,
        out string? value)
    {
        if (TryGetPropertyIgnoreCase(element, propertyName, out var prop) &&
            prop.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }
        value = null;
        return false;
    }
}

public sealed class ProgressReportingStream(Stream innerStream, long totalBytes, IProgress<double>? progress) : Stream
{
    private long _bytesRead;
    private int _lastReportedPercent = -1;

    public override bool CanRead => innerStream.CanRead;
    public override bool CanSeek => innerStream.CanSeek;
    public override bool CanWrite => innerStream.CanWrite;
    public override long Length => innerStream.Length;
    public override long Position
    {
        get => innerStream.Position;
        set => innerStream.Position = value;
    }

    public override void Flush() => innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => innerStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = innerStream.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await innerStream.ReadAsync(buffer, cancellationToken);
        Report(read);
        return read;
    }

    private void Report(int read)
    {
        if (read > 0 && totalBytes > 0 && progress is not null)
        {
            _bytesRead += read;
            var pct = Math.Min(99.0, (double)_bytesRead / totalBytes * 100.0);
            // Chỉ báo khi phần trăm nguyên thay đổi (~100 callback) thay vì mỗi lần đọc buffer.
            var rounded = (int)pct;
            if (rounded != _lastReportedPercent)
            {
                _lastReportedPercent = rounded;
                progress.Report(pct);
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
    public override void SetLength(long value) => innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => innerStream.Write(buffer, offset, count);
}

