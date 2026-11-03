using System.Buffers;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
    IMemoryCache? cache = null,
    IStreamSegmentTokenProtector? segmentTokens = null,
    StreamOriginCircuitBreaker? circuitBreaker = null,
    IConfiguration? configuration = null) : ControllerBase
{
    /// <summary>Tên của HttpClient dùng để proxy video từ máy chủ nguồn.</summary>
    private const string StreamProxyClientName = "StreamProxy";
    private const string PlaybackScope = "play";
    private const string PlaybackCookiePath = "/api/stream";
    private const int FallbackPlaybackTtlSeconds = 600;
    private const int MaxUpstreamRedirects = 5;
    private const int CopyBufferSize = 64 * 1024;
    private const int MaxManifestChars = 4 * 1024 * 1024;
    private const string ManifestContentType = "application/vnd.apple.mpegurl; charset=utf-8";

    private readonly IStreamSegmentTokenProtector _segmentTokens =
        segmentTokens ?? new StreamSegmentTokenProtector();

    private readonly StreamOriginCircuitBreaker _circuitBreaker =
        circuitBreaker ?? new StreamOriginCircuitBreaker();

    private readonly TimeSpan _headerTimeout = TimeSpan.FromSeconds(
        Math.Clamp(configuration?.GetValue("Security:StreamProxyHeaderTimeoutSeconds", 15) ?? 15, 1, 120));

    private readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(
        Math.Clamp(configuration?.GetValue("Security:StreamProxyIdleTimeoutSeconds", 30) ?? 30, 5, 300));

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

            if (!string.IsNullOrWhiteSpace(lease.IpAddress) && lease.IpAddress != "127.0.0.1" && lease.IpAddress != "::1")
            {
                clientIp = lease.IpAddress;
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
            scope: PlaybackScope,
            isHls: HlsManifestRewriter.LooksLikeHlsUrl(stream.StreamUrl));

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
            scope: PlaybackScope,
            isHls: HlsManifestRewriter.LooksLikeHlsUrl(stream.StreamUrl));

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
        else if (!validation.IsValid
            && validation.Status == StreamValidationStatus.IpMismatch
            && !string.IsNullOrWhiteSpace(sid)
            && string.Equals(scope, PlaybackScope, StringComparison.Ordinal))
        {
            var lease = await streamSessionService.GetActiveLeaseAsync(sid, cancellationToken);
            if (lease != null && lease.EpisodeId == episodeId)
            {
                // Kiểm tra xem chữ ký có hợp lệ với:
                // 1. IP đã lưu trong phiên lease (được gán lúc client tạo lease)
                // 2. Hoặc Loopback 127.0.0.1 / ::1
                var leaseIp = lease.IpAddress ?? string.Empty;
                var leaseIpValid = !string.IsNullOrWhiteSpace(leaseIp) && urlSigner.Validate(
                    episodeId,
                    pos,
                    exp,
                    ip ?? string.Empty,
                    sig ?? string.Empty,
                    leaseIp,
                    sessionId: sid,
                    scope: scope,
                    keyId: kid).IsValid;

                var loopbackV4Valid = leaseIpValid || urlSigner.Validate(
                    episodeId,
                    pos,
                    exp,
                    ip ?? string.Empty,
                    sig ?? string.Empty,
                    "127.0.0.1",
                    sessionId: sid,
                    scope: scope,
                    keyId: kid).IsValid;

                var loopbackV6Valid = loopbackV4Valid || urlSigner.Validate(
                    episodeId,
                    pos,
                    exp,
                    ip ?? string.Empty,
                    sig ?? string.Empty,
                    "::1",
                    sessionId: sid,
                    scope: scope,
                    keyId: kid).IsValid;

                if (leaseIpValid || loopbackV4Valid || loopbackV6Valid)
                {
                    validation = StreamValidationResult.Success();
                }
            }
        }

        if (!validation.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail(validation.ErrorMessage ?? "Chữ ký bảo vệ luồng không hợp lệ."));
        }

        // HEAD request: xác thực + trả metadata thật từ nguồn (content-length, content-type,
        // accept-ranges, ETag, Last-Modified) mà không tải nội dung.
        if (HttpMethods.IsHead(Request.Method))
        {
            await using var headDb = await contextFactory.CreateDbContextAsync(cancellationToken);
            var headStream = await headDb.StreamSources
                .FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == pos, cancellationToken);

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

                var headLease = await streamSessionService.GetActiveLeaseAsync(sid, cancellationToken);
                if (headLease is null || headLease.EpisodeId != episodeId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden,
                        ApiResponse<string>.Fail("Phiên phát đã hết hạn hoặc đã bị thu hồi."));
                }
            }

            if (TryResolveLocalMedia(headStream.StreamUrl, out var headLocalPath))
            {
                var fileInfo = new FileInfo(headLocalPath);
                if (fileInfo.Exists)
                {
                    Response.StatusCode = StatusCodes.Status200OK;
                    Response.Headers.ContentLength = fileInfo.Length;
                    Response.Headers.ContentType = GetMediaContentType(headLocalPath);
                    Response.Headers.AcceptRanges = "bytes";
                    Response.Headers.LastModified = fileInfo.LastWriteTimeUtc.ToString("R");
                    Response.Headers.CacheControl = "no-store, private, max-age=0";
                    Response.Headers["Pragma"] = "no-cache";
                    Response.Headers["Referrer-Policy"] = "no-referrer";
                    Response.Headers["X-Content-Type-Options"] = "nosniff";
                    return new EmptyResult();
                }
            }

            if (!Uri.TryCreate(headStream.StreamUrl, UriKind.Absolute, out var headUpstreamUri)
                || (headUpstreamUri.Scheme != Uri.UriSchemeHttp && headUpstreamUri.Scheme != Uri.UriSchemeHttps))
            {
                return StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Nguồn phát không hợp lệ."));
            }

            var headClient = httpClientFactory.CreateClient(StreamProxyClientName);
            var probeResult = await ProbeUpstreamAsync(headClient, headUpstreamUri, cancellationToken);
            if (probeResult.Error is not null)
            {
                return probeResult.Error;
            }

            var probe = probeResult.Probe!;
            Response.StatusCode = probe.StatusCode;
            ApplyProbeHeaders(probe);
            Response.Headers.CacheControl = "no-store, private, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return new EmptyResult();
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var stream = await db.StreamSources
            .FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == pos, cancellationToken);

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

        if (TryResolveLocalMedia(stream.StreamUrl, out var localPath))
        {
            if (System.IO.File.Exists(localPath))
            {
                var contentType = GetMediaContentType(localPath);
                return PhysicalFile(localPath, contentType, enableRangeProcessing: true);
            }
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

        var fetch = await FetchUpstreamAsync(httpClient, upstreamUri, rangeHeader, cancellationToken);
        if (fetch.Error is not null)
        {
            return fetch.Error;
        }

        using (var upstreamResponse = fetch.Response!)
        {
            var upstreamContentType = upstreamResponse.Content.Headers.ContentType?.ToString();
            var isHls = HlsManifestRewriter.LooksLikeHlsUrl(stream.StreamUrl)
                || IsTruthy(Request.Query["hls"].FirstOrDefault())
                || HlsManifestRewriter.IsHlsContentType(upstreamContentType);

            if (upstreamResponse.IsSuccessStatusCode && isHls)
            {
                var manifest = await ReadManifestTextAsync(upstreamResponse, cancellationToken);
                if (manifest is null || !manifest.TrimStart().StartsWith("#EXTM3U", StringComparison.Ordinal))
                {
                    return StatusCode(StatusCodes.Status502BadGateway,
                        ApiResponse<string>.Fail("Nguồn phát không trả về playlist HLS hợp lệ."));
                }

                var rewritten = HlsManifestRewriter.Rewrite(
                    manifest,
                    fetch.FinalUri!,
                    (uri, isManifest) => BuildHlsResourceUrl(uri, isManifest, episodeId, pos, sid, clientIp));

                await WriteManifestAsync(rewritten, cancellationToken);
                return new EmptyResult();
            }

            Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, includeContentLength: true);

            // URL đã ký chỉ sống 10 phút và gắn với IP — không được cache lại ở bất kỳ tầng nào.
            Response.Headers.CacheControl = "no-store, private, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            // URL CDN gốc không bao giờ được trả về cho trình duyệt.
            Response.Headers.Remove("X-Upstream");

            // Không buffer toàn bộ phản hồi vào bộ nhớ server: truyền thẳng bytes từ
            // nguồn lên client, tránh OOM khi nhiều người xem cùng lúc.
            var bodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bodyFeature?.DisableBuffering();
            await using var body = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await CopyBodyWithIdleTimeoutAsync(body, Response.Body, cancellationToken);
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Proxy một segment/key/init-map HLS. Token AES-GCM chứa URL gốc đã mã hoá nên trình
    /// duyệt không bao giờ thấy origin; mỗi request vẫn kiểm tra IP, lease và SSRF.
    /// </summary>
    [HttpGet("segment/{token}")]
    [HttpHead("segment/{token}")]
    [EnableRateLimiting("StreamPlayback")]
    public Task<IActionResult> ProxySegment(
        string token,
        [FromQuery] string? sid,
        CancellationToken cancellationToken) =>
        ProxyHlsResourceAsync(token, StreamSegmentKind.Segment, sid, cancellationToken);

    /// <summary>Proxy playlist HLS con và viết lại URI bằng token mới.</summary>
    [HttpGet("manifest/{token}")]
    [HttpHead("manifest/{token}")]
    [EnableRateLimiting("StreamPlayback")]
    public Task<IActionResult> ProxyManifest(
        string token,
        [FromQuery] string? sid,
        CancellationToken cancellationToken) =>
        ProxyHlsResourceAsync(token, StreamSegmentKind.Manifest, sid, cancellationToken);

    /// <summary>
    /// Endpoint nội bộ cho Nginx <c>auth_request</c>. Edge gọi endpoint này trước khi trả
    /// segment/manifest đã cache; endpoint chỉ trả 204/401, không trả nội dung.
    /// </summary>
    [HttpGet("edge/authorize")]
    [HttpHead("edge/authorize")]
    [EnableRateLimiting("StreamPlayback")]
    public async Task<IActionResult> EdgeAuthorize(CancellationToken cancellationToken)
    {
        var original = Request.Headers["X-Original-URI"].FirstOrDefault();
        // Nginx gửi $request_uri (bao gồm cả query). Tách path và query trước khi lấy
        // token ở cuối path, nếu không token sẽ bị dính "?sid=..." và luôn không hợp lệ.
        Uri? originalUri = null;
        if (original is not null)
        {
            Uri.TryCreate("http://edge" + original, UriKind.Absolute, out originalUri);
        }

        var originalPath = originalUri?.AbsolutePath
            ?? (original is null ? Request.Path.Value ?? string.Empty : string.Empty);
        var originalQuery = originalUri?.Query ?? Request.QueryString.Value ?? string.Empty;
        var query = QueryHelpers.ParseQuery(originalQuery);
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

        if (originalPath.StartsWith("/api/stream/segment/", StringComparison.OrdinalIgnoreCase)
            || originalPath.StartsWith("/api/stream/manifest/", StringComparison.OrdinalIgnoreCase))
        {
            var kind = originalPath.Contains("/manifest/", StringComparison.OrdinalIgnoreCase)
                ? StreamSegmentKind.Manifest
                : StreamSegmentKind.Segment;
            var token = originalPath[(originalPath.LastIndexOf('/') + 1)..];
            var read = _segmentTokens.Read(token, kind);
            var sidQuery = query.TryGetValue("sid", out var sidValues) ? sidValues.FirstOrDefault() : null;
            var error = await ValidateHlsTokenAsync(read, sidQuery, clientIp, cancellationToken);
            return error ?? NoContent();
        }

        var episodeId = ParseInt(query.TryGetValue("episodeId", out var episodeValues) ? episodeValues.FirstOrDefault() : null);
        var position = ParseInt(query.TryGetValue("pos", out var posValues) ? posValues.FirstOrDefault() : null);
        if (episodeId <= 0 || position < 0)
        {
            return Unauthorized();
        }

        var sid = query.TryGetValue("sid", out var sidQueryValues) ? sidQueryValues.FirstOrDefault() : null;
        var scope = query.TryGetValue("scope", out var scopeValues) ? scopeValues.FirstOrDefault() : null;
        var validation = urlSigner.Validate(
            episodeId,
            position,
            ParseLong(query.TryGetValue("exp", out var expValues) ? expValues.FirstOrDefault() : null),
            (query.TryGetValue("ip", out var ipValues) ? ipValues.FirstOrDefault() : null) ?? string.Empty,
            (query.TryGetValue("sig", out var sigValues) ? sigValues.FirstOrDefault() : null) ?? string.Empty,
            clientIp,
            sessionId: sid,
            scope: scope,
            keyId: query.TryGetValue("kid", out var kidValues) ? kidValues.FirstOrDefault() : null);

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

        if (!validation.IsValid) return Unauthorized();

        if (!string.IsNullOrWhiteSpace(sid))
        {
            var lease = await streamSessionService.GetActiveLeaseAsync(sid, cancellationToken);
            if (lease is null || lease.EpisodeId != episodeId) return Unauthorized();
        }

        return NoContent();
    }

    private async Task<IActionResult> ProxyHlsResourceAsync(
        string token,
        StreamSegmentKind kind,
        string? sidQuery,
        CancellationToken cancellationToken)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var read = _segmentTokens.Read(token, kind);
        var error = await ValidateHlsTokenAsync(read, sidQuery, clientIp, cancellationToken);
        if (error is not null) return error;

        var payload = read.Payload!;
        if (payload.SessionId is not null)
        {
            SetPlaybackCookie(payload.EpisodeId, payload.SessionId, clientIp);
        }

        if (!Uri.TryCreate(payload.UpstreamUrl, UriKind.Absolute, out var upstreamUri)
            || (upstreamUri.Scheme != Uri.UriSchemeHttp && upstreamUri.Scheme != Uri.UriSchemeHttps))
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse<string>.Fail("Nguồn phát không hợp lệ."));
        }

        var httpClient = httpClientFactory.CreateClient(StreamProxyClientName);

        if (HttpMethods.IsHead(Request.Method))
        {
            var probeResult = await ProbeUpstreamAsync(httpClient, upstreamUri, cancellationToken);
            if (probeResult.Error is not null) return probeResult.Error;

            var probe = probeResult.Probe!;
            Response.StatusCode = probe.StatusCode;
            ApplyProbeHeaders(probe);
            if (kind == StreamSegmentKind.Manifest)
            {
                Response.ContentType = ManifestContentType;
            }
            Response.Headers.CacheControl = "no-store, private, max-age=0";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return new EmptyResult();
        }

        var rangeHeader = Request.Headers.TryGetValue("Range", out var range) && !string.IsNullOrWhiteSpace(range)
            ? range.ToString()
            : null;

        var fetch = await FetchUpstreamAsync(httpClient, upstreamUri, rangeHeader, cancellationToken);
        if (fetch.Error is not null) return fetch.Error;

        using (var upstreamResponse = fetch.Response!)
        {
            if (!upstreamResponse.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)upstreamResponse.StatusCode is >= 500 ? StatusCodes.Status502BadGateway : (int)upstreamResponse.StatusCode,
                    ApiResponse<string>.Fail("Không tải được tài nguyên phát từ máy chủ nguồn."));
            }

            if (kind == StreamSegmentKind.Manifest)
            {
                var manifest = await ReadManifestTextAsync(upstreamResponse, cancellationToken);
                if (manifest is null || !manifest.TrimStart().StartsWith("#EXTM3U", StringComparison.Ordinal))
                {
                    return StatusCode(StatusCodes.Status502BadGateway,
                        ApiResponse<string>.Fail("Nguồn phát không trả về playlist HLS hợp lệ."));
                }

                var rewritten = HlsManifestRewriter.Rewrite(
                    manifest,
                    fetch.FinalUri!,
                    (uri, isManifest) => BuildHlsResourceUrl(
                        uri,
                        isManifest,
                        payload.EpisodeId,
                        payload.Position,
                        payload.SessionId,
                        clientIp));

                await WriteManifestAsync(rewritten, cancellationToken);
                return new EmptyResult();
            }

            Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, includeContentLength: true);
            Response.Headers.CacheControl = "no-store, private, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            var bodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
            bodyFeature?.DisableBuffering();
            await using var body = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
            await CopyBodyWithIdleTimeoutAsync(body, Response.Body, cancellationToken);
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Kiểm tra token HLS: hết hạn nhưng cookie phiên còn sống thì vẫn cho qua (giống
    /// <c>/play</c>), sau đó xác minh IP, lease và tập phim.
    /// </summary>
    private async Task<IActionResult?> ValidateHlsTokenAsync(
        StreamSegmentTokenResult read,
        string? sidQuery,
        string clientIp,
        CancellationToken cancellationToken)
    {
        var payload = read.Payload;
        if (payload is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Liên kết tài nguyên HLS không hợp lệ hoặc đã hết hạn."));
        }

        var allowedByCookie = false;
        if (read.Status == StreamSegmentTokenStatus.Expired)
        {
            if (payload.SessionId is null
                || !StreamPlaybackCookie.Validate(
                    urlSigner,
                    Request.Cookies[StreamPlaybackCookie.Name],
                    payload.SessionId,
                    payload.EpisodeId,
                    clientIp))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Liên kết tài nguyên HLS đã hết hạn. Vui lòng tải lại trang."));
            }

            allowedByCookie = true;
        }
        else if (!read.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Liên kết tài nguyên HLS không hợp lệ."));
        }

        var expectedIpHash = urlSigner.ComputeIpHash(clientIp);
        if (!FixedTimeEquals(payload.IpHash, expectedIpHash))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Địa chỉ IP truy cập không khớp với thiết bị được cấp quyền."));
        }

        if (!string.IsNullOrWhiteSpace(sidQuery)
            && !string.Equals(sidQuery, payload.SessionId, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail("Phiên phát không khớp với tài nguyên được yêu cầu."));
        }

        if (!string.IsNullOrWhiteSpace(payload.SessionId))
        {
            var lease = await streamSessionService.GetActiveLeaseAsync(payload.SessionId, cancellationToken);
            if (lease is null || lease.EpisodeId != payload.EpisodeId)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Phiên phát đã hết hạn hoặc đã bị thu hồi."));
            }
        }
        else if (!allowedByCookie)
        {
            // URL không có sid (ví dụ URL ký trước khi có lease) vẫn hợp lệ trong TTL ngắn
            // của token; không có lease để kiểm tra thêm.
        }

        return null;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left.ToLowerInvariant());
        var rightBytes = Encoding.UTF8.GetBytes(right.ToLowerInvariant());
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private string BuildHlsResourceUrl(
        Uri upstreamUri,
        bool isManifest,
        int episodeId,
        int position,
        string? sessionId,
        string clientIp)
    {
        var token = _segmentTokens.Create(
            episodeId,
            position,
            sessionId,
            urlSigner.ComputeIpHash(clientIp),
            upstreamUri.ToString(),
            isManifest ? StreamSegmentKind.Manifest : StreamSegmentKind.Segment);

        var path = isManifest ? "manifest" : "segment";
        var sidPart = string.IsNullOrWhiteSpace(sessionId)
            ? string.Empty
            : $"?sid={Uri.EscapeDataString(sessionId)}";

        return $"/api/stream/{path}/{token}{sidPart}";
    }

    private static async Task<string?> ReadManifestTextAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        var builder = new StringBuilder();
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            builder.Append(buffer, 0, read);
            if (builder.Length > MaxManifestChars) return null;
        }

        return builder.ToString();
    }

    private async Task WriteManifestAsync(string manifest, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(manifest);
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = ManifestContentType;
        Response.ContentLength = bytes.Length;
        Response.Headers.CacheControl = "no-store, private, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        var bodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bodyFeature?.DisableBuffering();
        await Response.Body.WriteAsync(bytes, cancellationToken);
    }

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string? raw) =>
        int.TryParse(raw, out var value) ? value : 0;

    private static long ParseLong(string? raw) =>
        long.TryParse(raw, out var value) ? value : 0;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private IActionResult CircuitOpenError()
    {
        Response.Headers.RetryAfter = "30";
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            ApiResponse<string>.Fail("Nguồn phát đang tạm gián đoạn. Vui lòng chuyển sang nguồn khác."));
    }

    /// <summary>
    /// Tải nguồn phát qua tối đa 5 lần chuyển hướng, kiểm tra lại URL ở MỖI bước. HttpClient
    /// được cấu hình <c>AllowAutoRedirect = false</c> để không thể bị lợi dụng redirect trỏ
    /// vào mạng nội bộ. Range header được giữ nguyên để tua video vẫn hoạt động.
    /// Header timeout ngắn để CDN treo không giữ request vô hạn; body có idle timeout riêng.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, Uri? FinalUri, IActionResult? Error)> FetchUpstreamAsync(
        HttpClient httpClient,
        Uri initialUri,
        string? rangeHeader,
        CancellationToken cancellationToken)
    {
        var originKey = StreamOriginCircuitBreaker.GetOriginKey(initialUri);
        if (_circuitBreaker.IsOpen(originKey))
        {
            return (null, null, CircuitOpenError());
        }

        var currentUri = initialUri;
        for (var hop = 0; hop <= MaxUpstreamRedirects; hop++)
        {
            var (isSafe, errorMessage, validatedUri) =
                await SafeStreamUrlValidator.ValidateUrlAsync(currentUri.ToString(), cancellationToken);
            if (!isSafe || validatedUri is null)
            {
                return (null, null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail(errorMessage ?? "Nguồn phát trỏ tới địa chỉ không an toàn.")));
            }

            HttpResponseMessage upstreamResponse;
            try
            {
                using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                headerCts.CancelAfter(_headerTimeout);

                using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, validatedUri);
                if (!string.IsNullOrWhiteSpace(rangeHeader))
                {
                    upstreamRequest.Headers.TryAddWithoutValidation("Range", rangeHeader);
                }

                ForwardRequestHeader(upstreamRequest, "If-Range");
                ForwardRequestHeader(upstreamRequest, "If-None-Match");
                ForwardRequestHeader(upstreamRequest, "If-Modified-Since");

                upstreamResponse = await httpClient.SendAsync(
                    upstreamRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    headerCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _circuitBreaker.RecordFailure(originKey);
                logger.LogWarning("Het thoi gian cho header tu nguon phat {EpisodeOrigin}.", originKey);
                return (null, null, StatusCode(StatusCodes.Status504GatewayTimeout,
                    ApiResponse<string>.Fail("Máy chủ phát video phản hồi quá chậm.")));
            }
            catch (HttpRequestException ex)
            {
                _circuitBreaker.RecordFailure(originKey);
                logger.LogWarning(ex, "Khong the ket noi toi nguon phat {EpisodeOrigin}.", originKey);
                return (null, null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Không kết nối được tới máy chủ phát video.")));
            }

            if (!IsRedirect(upstreamResponse.StatusCode))
            {
                if ((int)upstreamResponse.StatusCode >= 500)
                {
                    _circuitBreaker.RecordFailure(originKey);
                }
                else
                {
                    _circuitBreaker.RecordSuccess(originKey);
                }

                return (upstreamResponse, validatedUri, null);
            }

            var location = upstreamResponse.Headers.Location;
            upstreamResponse.Dispose();

            if (location is null)
            {
                return (null, null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Nguồn phát trả về chuyển hướng không hợp lệ.")));
            }

            if (hop == MaxUpstreamRedirects)
            {
                return (null, null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Nguồn phát chuyển hướng quá nhiều lần.")));
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(validatedUri, location);
        }

        return (null, null, StatusCode(StatusCodes.Status502BadGateway,
            ApiResponse<string>.Fail("Nguồn phát chuyển hướng quá nhiều lần.")));
    }

    private void ForwardRequestHeader(HttpRequestMessage request, string name)
    {
        if (Request.Headers.TryGetValue(name, out var values) && !string.IsNullOrWhiteSpace(values))
        {
            request.Headers.TryAddWithoutValidation(name, values.ToString());
        }
    }

    private sealed record StreamProbeResult(
        int StatusCode,
        string? ContentType,
        long? ContentLength,
        string? ContentRange,
        string? AcceptRanges,
        string? ETag,
        DateTimeOffset? LastModified);

    /// <summary>
    /// HEAD thật tới nguồn để lấy metadata. Một số CDN không hỗ trợ HEAD nên fallback
    /// GET <c>Range: bytes=0-0</c>; body không được đọc vào bộ nhớ.
    /// </summary>
    private async Task<(StreamProbeResult? Probe, IActionResult? Error)> ProbeUpstreamAsync(
        HttpClient httpClient,
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var originKey = StreamOriginCircuitBreaker.GetOriginKey(initialUri);
        if (_circuitBreaker.IsOpen(originKey))
        {
            return (null, CircuitOpenError());
        }

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

            HttpResponseMessage? response = null;
            try
            {
                using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                headerCts.CancelAfter(_headerTimeout);

                using (var headRequest = new HttpRequestMessage(HttpMethod.Head, validatedUri))
                {
                    response = await httpClient.SendAsync(
                        headRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        headerCts.Token);
                }

                if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                {
                    response.Dispose();
                    using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, validatedUri);
                    fallbackRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-0");
                    response = await httpClient.SendAsync(
                        fallbackRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        headerCts.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _circuitBreaker.RecordFailure(originKey);
                return (null, StatusCode(StatusCodes.Status504GatewayTimeout,
                    ApiResponse<string>.Fail("Máy chủ phát video phản hồi quá chậm.")));
            }
            catch (HttpRequestException ex)
            {
                _circuitBreaker.RecordFailure(originKey);
                logger.LogWarning(ex, "Khong the probe nguon phat {EpisodeOrigin}.", originKey);
                return (null, StatusCode(StatusCodes.Status502BadGateway,
                    ApiResponse<string>.Fail("Không kết nối được tới máy chủ phát video.")));
            }

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                response.Dispose();
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
                continue;
            }

            if ((int)response.StatusCode >= 500)
            {
                _circuitBreaker.RecordFailure(originKey);
            }
            else
            {
                _circuitBreaker.RecordSuccess(originKey);
            }

            var probe = new StreamProbeResult(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                response.Content.Headers.ContentLength,
                response.Content.Headers.ContentRange?.ToString(),
                response.Headers.TryGetValues("Accept-Ranges", out var acceptRanges) ? acceptRanges.FirstOrDefault() : null,
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified);

            response.Dispose();
            return (probe, null);
        }

        return (null, StatusCode(StatusCodes.Status502BadGateway,
            ApiResponse<string>.Fail("Nguồn phát chuyển hướng quá nhiều lần.")));
    }

    private void ApplyProbeHeaders(StreamProbeResult probe)
    {
        if (!string.IsNullOrWhiteSpace(probe.ContentType))
        {
            Response.ContentType = probe.ContentType;
        }

        if (probe.ContentLength is >= 0)
        {
            Response.ContentLength = probe.ContentLength;
        }

        if (!string.IsNullOrWhiteSpace(probe.ContentRange))
        {
            Response.Headers["Content-Range"] = probe.ContentRange;
        }

        Response.Headers["Accept-Ranges"] = probe.AcceptRanges ?? "bytes";

        if (!string.IsNullOrWhiteSpace(probe.ETag))
        {
            Response.Headers["ETag"] = probe.ETag;
        }

        if (probe.LastModified.HasValue)
        {
            Response.Headers["Last-Modified"] = probe.LastModified.Value.ToString("R");
        }
    }

    private async Task CopyBodyWithIdleTimeoutAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            while (true)
            {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                idleCts.CancelAfter(_idleTimeout);

                int read;
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), idleCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Nguon phat khong gui du lieu trong {IdleSeconds}s, dong ket noi.", _idleTimeout.TotalSeconds);
                    break;
                }

                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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

    private void CopyResponseHeaders(HttpResponseMessage upstream, bool includeContentLength)
    {
        CopyUpstreamHeader(upstream, "Content-Type");
        if (includeContentLength)
        {
            CopyUpstreamHeader(upstream, "Content-Length");
        }
        CopyUpstreamHeader(upstream, "Content-Range");
        CopyUpstreamHeader(upstream, "Accept-Ranges");
        CopyUpstreamHeader(upstream, "ETag");
        CopyUpstreamHeader(upstream, "Last-Modified");
        CopyUpstreamHeader(upstream, "Content-Disposition");
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

    private bool TryResolveLocalMedia(string? streamUrl, out string localPath)
    {
        localPath = string.Empty;
        if (string.IsNullOrWhiteSpace(streamUrl)) return false;

        string fileName;
        if (streamUrl.StartsWith("local://", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileName(streamUrl[8..]);
        }
        else if (streamUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileName(streamUrl[7..]);
        }
        else if (streamUrl.StartsWith("/root/data/movies/", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileName(streamUrl);
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
        {
            return false;
        }

        var configuredDir = configuration?.GetValue<string>("Storage:MediaDirectory");
        string[] searchDirs = [
            configuredDir ?? string.Empty,
            "/root/data/movies",
            Path.Combine(AppContext.BaseDirectory, "movies"),
            Path.Combine(Directory.GetCurrentDirectory(), "movies")
        ];

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var fullPath = Path.Combine(dir, fileName);
            if (System.IO.File.Exists(fullPath))
            {
                localPath = fullPath;
                return true;
            }
        }

        return false;
    }

    private static string GetMediaContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".m3u8" => "application/vnd.apple.mpegurl",
            _ => "video/mp4"
        };
    }
}
