using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Client.Services;

public sealed class WatchApiClient(IHttpClientFactory factory, AuthState auth)
{
    public async Task<MovieDetailDto?> GetMovieAsync(int id)
    {
        using var client = factory.CreateClient("AccountApi");
        using var response = await client.GetAsync($"api/movies/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<MovieDetailDto>>();
        if (result?.Success != true || result.Data is null) throw new HttpRequestException("Không thể tải phim.");
        return result.Data;
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
}
