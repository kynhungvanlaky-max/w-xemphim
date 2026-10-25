using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StreamController(
    IDbContextFactory<AppDbContext> contextFactory,
    IStreamUrlSigner urlSigner,
    PublicUrlResolver publicUrlResolver,
    IHttpClientFactory httpClientFactory,
    ILogger<StreamController> logger,
    IStreamSessionService streamSessionService,
    IMemoryCache? cache = null) : ControllerBase
{
    /// <summary>Tên của HttpClient dùng để proxy video từ máy chủ nguồn.</summary>
    private const string StreamProxyClientName = "StreamProxy";
    private const string PlaybackScope = "play";
    private const string PlaybackCookiePath = "/api/stream";
    private const int FallbackPlaybackTtlSeconds = 600;

    [HttpPost("sign")]
    [EnableRateLimiting("StreamSign")]
    public async Task<ActionResult<ApiResponse<SignedStreamDto>>> SignStream([FromBody] SignStreamRequest request)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(ApiResponse<SignedStreamDto>.Fail("Yêu cầu đăng nhập để tạo liên kết phát video."));
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var stream = await db.StreamSources
            .FirstOrDefaultAsync(s => s.EpisodeId == request.EpisodeId && s.Position == request.Position);

        if (stream is null)
        {
            stream = await db.StreamSources
                .Where(s => s.EpisodeId == request.EpisodeId)
                .OrderBy(s => s.Position)
                .FirstOrDefaultAsync();

            if (stream is null)
            {
                return NotFound(ApiResponse<SignedStreamDto>.Fail($"Không tìm thấy nguồn phát cho tập: {request.EpisodeId}"));
            }
        }

        var isVip = await IsCallerVipAsync(db);
        var isVipStream = stream.ServerType == StreamServerType.VipFastServer ||
                          (!string.IsNullOrEmpty(stream.Quality) && stream.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));
        if (isVipStream && !isVip)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SignedStreamDto>.Fail("Yêu cầu gói thành viên VIP để tạo link phát video VIP."));
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(ApiResponse<SignedStreamDto>.Fail("Không thể xác thực danh tính người dùng."));
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var baseUrl = publicUrlResolver.Resolve(Request);

        // Nếu client đã có lease (do CinemaHub cấp), xác minh lease thuộc đúng người dùng
        // và đúng tập phim trước khi ký. Nếu chưa có, tự tạo một lease ngắn hạn để URL
        // không thể bị dùng lại ngoài phiên hiện tại.
        var sessionId = request.SessionId?.Trim();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var lease = await streamSessionService.GetActiveLeaseAsync(sessionId);
            if (lease is null || lease.UserId != userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<SignedStreamDto>.Fail("Phiên phát không tồn tại, đã hết hạn hoặc không thuộc tài khoản này."));
            }

            if (lease.EpisodeId != request.EpisodeId)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<SignedStreamDto>.Fail("Phiên phát không khớp với tập phim được yêu cầu."));
            }
        }
        else
        {
            var episode = await db.Episodes
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == request.EpisodeId);

            if (episode is null)
            {
                return NotFound(ApiResponse<SignedStreamDto>.Fail($"Không tìm thấy tập phim: {request.EpisodeId}"));
            }

            var leaseResult = await streamSessionService.AcquireLeaseAsync(
                userId,
                episode.MovieId,
                episode.Id,
                Guid.NewGuid().ToString("N"),
                deviceName: "Yêu cầu ký luồng phát",
                ipAddress: clientIp);

            if (!leaseResult.Success || string.IsNullOrWhiteSpace(leaseResult.SessionId))
            {
                return StatusCode(StatusCodes.Status409Conflict,
                    ApiResponse<SignedStreamDto>.Fail(leaseResult.ErrorMessage ?? "Không thể tạo phiên phát cho tài khoản này."));
            }

            sessionId = leaseResult.SessionId;
        }

        var signed = urlSigner.Sign(
            baseUrl,
            request.EpisodeId,
            stream.Position,
            clientIp,
            sessionId: sessionId,
            scope: PlaybackScope);

        return Ok(ApiResponse<SignedStreamDto>.Ok(signed));
    }

    /// <summary>
    /// Cấp lại URL phát cho một phiên đang hoạt động. Endpoint chỉ trả URL mới;
    /// cookie phiên do chính request media của trình duyệt gia hạn (xem <see cref="PlayStream"/>).
    /// </summary>
    [HttpPost("sessions/{sessionId}/refresh")]
    [EnableRateLimiting("StreamSign")]
    public async Task<ActionResult<ApiResponse<SignedStreamDto>>> RefreshSession(
        string sessionId,
        [FromBody] RefreshStreamRequest request)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(ApiResponse<SignedStreamDto>.Fail("Yêu cầu đăng nhập để gia hạn phiên phát."));
        }

        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            return Unauthorized(ApiResponse<SignedStreamDto>.Fail("Không thể xác thực danh tính người dùng."));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(ApiResponse<SignedStreamDto>.Fail("Mã phiên phát không hợp lệ."));
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var stream = await FindStreamSourceAsync(db, request.EpisodeId, request.Position);
        if (stream is null)
        {
            return NotFound(ApiResponse<SignedStreamDto>.Fail($"Không tìm thấy nguồn phát cho tập: {request.EpisodeId}"));
        }

        var isVip = await IsCallerVipAsync(db);
        var isVipStream = stream.ServerType == StreamServerType.VipFastServer ||
                          (!string.IsNullOrEmpty(stream.Quality) && stream.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));
        if (isVipStream && !isVip)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SignedStreamDto>.Fail("Yêu cầu gói thành viên VIP để gia hạn phiên phát VIP."));
        }

        var lease = await streamSessionService.GetActiveLeaseAsync(sessionId);
        if (lease is null || lease.UserId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SignedStreamDto>.Fail("Phiên phát không tồn tại, đã hết hạn hoặc không thuộc tài khoản này."));
        }

        if (lease.EpisodeId != request.EpisodeId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SignedStreamDto>.Fail("Phiên phát không khớp với tập phim được yêu cầu."));
        }


        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var baseUrl = publicUrlResolver.Resolve(Request);
        var signed = urlSigner.Sign(
            baseUrl,
            request.EpisodeId,
            stream.Position,
            clientIp,
            sessionId: sessionId,
            scope: PlaybackScope);

        return Ok(ApiResponse<SignedStreamDto>.Ok(signed));
    }

    /// <summary>Thu hồi ngay phiên phát của chính người dùng đang đăng nhập.</summary>
    [HttpPost("sessions/{sessionId}/revoke")]
    [EnableRateLimiting("StreamSign")]
    public async Task<ActionResult<ApiResponse<bool>>> RevokeSession(string sessionId)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(ApiResponse<bool>.Fail("Yêu cầu đăng nhập để kết thúc phiên phát."));
        }

        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            return Unauthorized(ApiResponse<bool>.Fail("Không thể xác thực danh tính người dùng."));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(ApiResponse<bool>.Fail("Mã phiên phát không hợp lệ."));
        }

        var lease = await streamSessionService.GetActiveLeaseAsync(sessionId);
        if (lease is null)
        {
            ClearPlaybackCookie();
            return Ok(ApiResponse<bool>.Ok(true, "Phiên phát đã kết thúc."));
        }

        if (lease.UserId != userId)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<bool>.Fail("Phiên phát không thuộc tài khoản này."));
        }

        await streamSessionService.ReleaseLeaseAsync(userId, sessionId);
        ClearPlaybackCookie();
        return Ok(ApiResponse<bool>.Ok(true, "Đã kết thúc phiên phát."));
    }

    [HttpGet("play")]
    [HttpHead("play")]
    [EnableRateLimiting("StreamPlayback")]
    public async Task<IActionResult> PlayStream(
        [FromQuery] int episodeId,
        [FromQuery] int pos,
        [FromQuery] long exp,
        [FromQuery] string? ip,
        [FromQuery] string? sig,
        [FromQuery] string? sid,
        [FromQuery] string? scope,
        [FromQuery] string? kid,
        CancellationToken cancellationToken)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var validation = urlSigner.Validate(
            episodeId,
            pos,
            exp,
            ip ?? string.Empty,
            sig ?? string.Empty,
            clientIp,
            sessionId: sid,
            scope: scope,
            keyId: kid);

        // URL ngắn hạn có thể hết hạn trong khi video vẫn đang phát hoặc người dùng
        // tạm dừng lâu. Cookie phiên (HttpOnly, gắn sid/tập/IP) cho phép các request
        // Range tiếp theo đi qua mà không phải đổi src của thẻ video.
        if (!validation.IsValid
            && validation.Status == StreamValidationStatus.Expired
            && !string.IsNullOrWhiteSpace(sid)
            && string.Equals(scope, PlaybackScope, StringComparison.Ordinal)
            && StreamPlaybackCookie.Validate(
                urlSigner,
                Request.Cookies[StreamPlaybackCookie.Name],
                sid,
                episodeId,
                clientIp))
        {
            validation = StreamValidationResult.Success();
        }

        if (!validation.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail(validation.ErrorMessage ?? "Chữ ký bảo vệ luồng không hợp lệ."));
        }

        // HEAD request: chỉ xác thực + trả metadata (content-length, content-type, accept-ranges)
        // mà không tải nội dung, dùng để kiểm tra nguồn trước khi phát.
        if (HttpContext.Request.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await using var headDb = await contextFactory.CreateDbContextAsync();
            var headStream = await headDb.StreamSources
                .FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == pos);

            if (headStream is null)
            {
                return NotFound(ApiResponse<string>.Fail("Không tìm thấy nguồn phát video được chỉ định."));
            }

            var isHeadVipStream = headStream.ServerType == StreamServerType.VipFastServer ||
                                  (!string.IsNullOrEmpty(headStream.Quality) && headStream.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));
            if (isHeadVipStream && string.IsNullOrWhiteSpace(sid))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Luồng phát VIP yêu cầu phiên phát (session) hợp lệ."));
            }

            if (!string.IsNullOrWhiteSpace(sid))
            {
                if (!string.Equals(scope, PlaybackScope, StringComparison.Ordinal))
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponse<string>.Fail("Liên kết phát video không đúng phạm vi sử dụng."));
                }

                var headLease = await streamSessionService.GetActiveLeaseAsync(sid);
                if (headLease is null || headLease.EpisodeId != episodeId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponse<string>.Fail("Phiên phát đã hết hạn hoặc đã bị thu hồi."));
                }
            }

            // Thiết lập metadata headers mà không tải body.
            if (headStream.StreamUrl != null)
            {
                Response.ContentType = "application/octet-stream";
                Response.Headers["Accept-Ranges"] = "bytes";
                Response.Headers.CacheControl = "no-store, private, max-age=0";
                Response.Headers["Referrer-Policy"] = "no-referrer";
            }
            return new EmptyResult();
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var stream = await db.StreamSources
            .FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == pos);

        if (stream is null)
        {
            return NotFound(ApiResponse<string>.Fail("Không tìm thấy nguồn phát video được chỉ định."));
        }

        var isVipStream = stream.ServerType == StreamServerType.VipFastServer ||
                          (!string.IsNullOrEmpty(stream.Quality) && stream.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase));

        if (isVipStream && string.IsNullOrWhiteSpace(sid))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Luồng phát VIP yêu cầu phiên phát (session) hợp lệ."));
        }

        // URL kiểu mới mang theo mã phiên. Lease phải còn hoạt động và khớp tập phim;
        // nhờ vậy khi người dùng bị evict/logout/ban, URL cũ lập tức mất hiệu lực dù
        // chữ ký vẫn còn trong hạn.
        if (!string.IsNullOrWhiteSpace(sid))
        {
            if (!string.Equals(scope, PlaybackScope, StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Liên kết phát video không đúng phạm vi sử dụng."));
            }

            var lease = await streamSessionService.GetActiveLeaseAsync(sid, cancellationToken);
            if (lease is null)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Phiên phát đã hết hạn hoặc đã bị thu hồi. Vui lòng tải lại trang."));
            }

            if (lease.EpisodeId != episodeId)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Phiên phát không khớp với tập phim được yêu cầu."));
            }

            if (isVipStream)
            {
                var isVip = await IsUserVipAsync(db, lease.UserId);
                if (!isVip)
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponse<string>.Fail("Yêu cầu gói thành viên VIP để xem nguồn phát này."));
                }
            }

            // Gia hạn cookie phiên mỗi khi trình duyệt thực sự gửi request media.
            // Cookie là HttpOnly nên JavaScript không đọc được; URL cũ vẫn dùng được
            // sau khi exp nhờ cookie còn sống, miễn lease chưa bị thu hồi.
            SetPlaybackCookie(episodeId, sid, clientIp);
        }

        // Video được PROXY qua server thay vì chuyển hướng (Redirect). Nếu chuyển hướng, URL
        // CDN gốc lộ ra trong header Location và dùng được vô thời hạn, khiến toàn bộ cơ chế
        // ký + giới hạn thời hạn + ràng buộc IP mất tác dụng.
        // URL lưu trong DB cũng phải được kiểm tra lại tại thời điểm phát: dữ liệu cũ hoặc do
        // quản trị viên nhập có thể trỏ vào mạng nội bộ (SSRF), kể cả khi lúc lưu đã hợp lệ.
        if (!Uri.TryCreate(stream.StreamUrl, UriKind.Absolute, out var upstreamUri)
            || (upstreamUri.Scheme != Uri.UriSchemeHttp && upstreamUri.Scheme != Uri.UriSchemeHttps))
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse<string>.Fail("Nguồn phát không hợp lệ."));
        }

        var httpClient = httpClientFactory.CreateClient(StreamProxyClientName);
        var rangeHeader = Request.Headers.TryGetValue("Range", out var range) && !string.IsNullOrWhiteSpace(range)
            ? range.ToString()
            : null;

        HttpResponseMessage upstreamResponse;
        try
        {
            var fetch = await FetchUpstreamAsync(httpClient, upstreamUri, rangeHeader, cancellationToken);
            if (fetch.Error is not null)
            {
                return fetch.Error;
            }

            upstreamResponse = fetch.Response!;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Khong the proxy duoc nguon phat {EpisodeId}/{Position}.", episodeId, pos);
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse<string>.Fail("Không kết nối được tới máy chủ phát video."));
        }

        using (upstreamResponse)
        {
            Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyUpstreamHeader(upstreamResponse, "Content-Type");
            CopyUpstreamHeader(upstreamResponse, "Content-Length");
            CopyUpstreamHeader(upstreamResponse, "Content-Range");
            CopyUpstreamHeader(upstreamResponse, "Accept-Ranges");

            // URL đã ký chỉ sống 10 phút và gắn với IP — không được cache lại ở bất kỳ tầng nào.
            Response.Headers.CacheControl = "no-store, private, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            // URL CDN gốc không bao giờ được trả về cho trình duyệt.
            Response.Headers.Remove("X-Upstream");

            // Không buffer toàn bộ phản hồi vào bộ nhớ server: truyền thẳng bytes từ
            // nguồn lên client, tránh OOM khi nhiều người xem cùng lúc.
            var bodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bodyFeature?.DisableBuffering();
            await upstreamResponse.Content.CopyToAsync(Response.Body, cancellationToken);
        }

        return new EmptyResult();
    }

    private const int MaxUpstreamRedirects = 5;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Tải nguồn phát qua tối đa 5 lần chuyển hướng, kiểm tra lại URL ở MỖI bước. HttpClient
    /// được cấu hình <c>AllowAutoRedirect = false</c> để không thể bị lợi dụng redirect trỏ
    /// vào mạng nội bộ. Range header được giữ nguyên để tua video vẫn hoạt động.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, IActionResult? Error)> FetchUpstreamAsync(
        HttpClient httpClient,
        Uri initialUri,
        string? rangeHeader,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var hop = 0; hop <= MaxUpstreamRedirects; hop++)
        {
            var (isSafe, errorMessage, validatedUri) =
                await SafeStreamUrlValidator.ValidateUrlAsync(currentUri.ToString(), cancellationToken);
            if (!isSafe || validatedUri is null)
            {
                return (null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail(errorMessage ?? "Nguồn phát trỏ tới địa chỉ không an toàn.")));
            }

            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, validatedUri);
            if (!string.IsNullOrWhiteSpace(rangeHeader))
            {
                upstreamRequest.Headers.TryAddWithoutValidation("Range", rangeHeader);
            }

            var upstreamResponse = await httpClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!IsRedirect(upstreamResponse.StatusCode))
            {
                return (upstreamResponse, null);
            }

            var location = upstreamResponse.Headers.Location;
            upstreamResponse.Dispose();

            if (location is null)
            {
                return (null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Nguồn phát trả về chuyển hướng không hợp lệ.")));
            }

            if (hop == MaxUpstreamRedirects)
            {
                return (null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Nguồn phát chuyển hướng quá nhiều lần.")));
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(validatedUri, location);
        }

        return (null, StatusCode(StatusCodes.Status502BadGateway,
            ApiResponse<string>.Fail("Nguồn phát chuyển hướng quá nhiều lần.")));
    }

    /// <summary>
    /// Tìm nguồn phát theo vị trí; nếu vị trí không tồn tại thì fallback về nguồn
    /// đầu tiên của tập (giữ tương thích với hành vi cũ của <c>/sign</c>).
    /// </summary>
    private static async Task<StreamSourceEntity?> FindStreamSourceAsync(AppDbContext db, int episodeId, int position)
    {
        var stream = await db.StreamSources
            .FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == position);

        if (stream is not null)
        {
            return stream;
        }

        return await db.StreamSources
            .Where(s => s.EpisodeId == episodeId)
            .OrderBy(s => s.Position)
            .FirstOrDefaultAsync();
    }

    private void SetPlaybackCookie(int episodeId, string sessionId, string clientIp)
    {
        var ttlSeconds = urlSigner.TokenTtlSeconds > 0
            ? urlSigner.TokenTtlSeconds
            : FallbackPlaybackTtlSeconds;

        var value = StreamPlaybackCookie.Create(
            urlSigner,
            sessionId,
            episodeId,
            clientIp,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(ttlSeconds));

        Response.Cookies.Append(StreamPlaybackCookie.Name, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = PlaybackCookiePath,
            MaxAge = TimeSpan.FromSeconds(ttlSeconds),
            IsEssential = true
        });
    }

    private void ClearPlaybackCookie()
    {
        Response.Cookies.Delete(StreamPlaybackCookie.Name, new CookieOptions
        {
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = PlaybackCookiePath
        });
    }

    /// <summary>
    /// Chép một header từ response của máy chủ nguồn sang response hiện tại.
    /// <c>Content-Type</c> phải đặt qua <see cref="ControllerBase.Response"/> (thuộc tính riêng),
    /// nếu đặt trong dictionary thì trình duyệt không nhận ra kiểu nội dung và báo
    /// "no supported source was found".
    /// </summary>
    private void CopyUpstreamHeader(HttpResponseMessage upstream, string name)
    {
        // Content-Type / Content-Length / Content-Range / Accept-Ranges nằm ở Content.Headers,
        // không phải Headers (Headers chỉ chứa header của giao thức).
        if (!upstream.Content.Headers.TryGetValues(name, out var values)
            && !upstream.Headers.TryGetValues(name, out values))
        {
            return;
        }

        var value = values.ToArray();
        if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            Response.ContentType = value.FirstOrDefault();
            return;
        }
        Response.Headers[name] = value;
    }

    private async Task<bool> IsUserVipAsync(AppDbContext db, Guid userId)
    {
        var cacheKey = $"vip_status_{userId}";
        if (cache is not null && cache.TryGetValue(cacheKey, out bool cachedVip))
        {
            return cachedVip;
        }

        var user = await db.Users.FindAsync(userId);
        var isVip = user is not null && user.Membership != MembershipTier.Free && (!user.VipExpiresAtUtc.HasValue || user.VipExpiresAtUtc.Value > DateTime.UtcNow);

        if (cache is not null)
        {
            cache.Set(cacheKey, isVip, TimeSpan.FromMinutes(5));
        }
        return isVip;
    }

    private async Task<bool> IsCallerVipAsync(AppDbContext db)
    {
        if (User?.Identity?.IsAuthenticated != true) return false;
        if (User.IsInRole("Admin")) return true;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return await IsUserVipAsync(db, userId);
        }
        else if (User.FindFirst("membership")?.Value is { } m && !m.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
