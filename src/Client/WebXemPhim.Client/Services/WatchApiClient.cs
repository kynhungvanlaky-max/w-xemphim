using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Client.Services;

public sealed class WatchApiClient(
    IHttpClientFactory factory,
    AuthState auth,
    ILogger<WatchApiClient>? logger = null)
{
    public async Task<MovieDetailDto?> GetMovieAsync(int id)
    {
        using var client = factory.CreateClient("AccountApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/movies/{id}");
        if (auth.IsAuthenticated && !string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        }
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<MovieDetailDto>>();
        if (result?.Success != true || result.Data is null) throw new HttpRequestException("Không thể tải phim.");
        return result.Data;
    }

    public async Task<SignedStreamDto?> SignStreamAsync(
        int episodeId,
        int position,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken)) return null;

        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/stream/sign")
            {
                Content = JsonContent.Create(new SignStreamRequest
                {
                    EpisodeId = episodeId,
                    Position = position,
                    SessionId = sessionId
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>(cancellationToken);
            return result?.Success == true ? result.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger?.LogWarning(ex, "Failed to sign stream for episode {EpisodeId} position {Position}", episodeId, position);
            return null;
        }
    }

    public async Task<SignedStreamDto?> RefreshStreamAsync(
        int episodeId,
        int position,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken) || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/stream/sessions/{Uri.EscapeDataString(sessionId)}/refresh")
            {
                Content = JsonContent.Create(new RefreshStreamRequest
                {
                    EpisodeId = episodeId,
                    Position = position
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>(cancellationToken);
            return result?.Success == true ? result.Data : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger?.LogWarning(ex, "Failed to refresh stream session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<bool> RevokeStreamAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken) || string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"api/stream/sessions/{Uri.EscapeDataString(sessionId)}/revoke");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger?.LogWarning(ex, "Failed to revoke stream session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<WatchProgressDto?> GetProgressAsync(int movieId)
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken)) return null;
        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/library/progress/{movieId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<WatchProgressDto>>();
            return result?.Data;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get watch progress for movieId: {MovieId}", movieId);
            return null;
        }
    }

    public async Task<bool> SaveProgressAsync(UpdateWatchProgressRequest requestDto)
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken)) return false;
        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/library/progress")
            {
                Content = JsonContent.Create(requestDto)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            using var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to save watch progress for movieId: {MovieId}", requestDto.MovieId);
            return false;
        }
    }

    public async Task<bool> IncrementViewCountAsync(int movieId)
    {
        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var response = await client.PostAsync($"api/movies/{movieId}/view", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to increment view count for movieId: {MovieId}", movieId);
            return false;
        }
    }

    public async Task<UserSettingsDto?> GetSettingsAsync()
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken)) return null;
        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/account/settings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<UserSettingsDto>>();
            return result?.Data;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get settings for user");
            return null;
        }
    }

    public async Task<ApiResponse<Guid>?> ReportAsync(PlaybackIssueRequest issue)
    {
        using var client = factory.CreateClient("AccountApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/playback-issues")
        { Content = JsonContent.Create(issue) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var response = await client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return ApiResponse<Guid>.Fail("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        if (!response.IsSuccessStatusCode) return ApiResponse<Guid>.Fail("Chưa gửi được báo lỗi. Vui lòng thử lại.");
        return await response.Content.ReadFromJsonAsync<ApiResponse<Guid>>();
    }

    public async Task<ApiResponse<SignedStreamDto>?> SignStreamAsync(int episodeId, int position = 1)
    {
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.AccessToken))
            return ApiResponse<SignedStreamDto>.Fail("Yêu cầu đăng nhập.");

        try
        {
            using var client = factory.CreateClient("AccountApi");
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/stream/sign")
            {
                Content = JsonContent.Create(new SignStreamRequest { EpisodeId = episodeId, Position = position })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
                return err ?? ApiResponse<SignedStreamDto>.Fail("Không thể tạo liên kết phát video bảo mật.");
            }
            return await response.Content.ReadFromJsonAsync<ApiResponse<SignedStreamDto>>();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to sign stream for episodeId: {EpisodeId}", episodeId);
            return ApiResponse<SignedStreamDto>.Fail(ex.Message);
        }
    }
}
