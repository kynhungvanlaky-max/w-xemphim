using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
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
    IMemoryCache? cache = null) : ControllerBase
{
    [HttpPost("sign")]
    public async Task<ActionResult<ApiResponse<SignedStreamDto>>> SignStream([FromBody] SignStreamRequest request)
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(ApiResponse<SignedStreamDto>.Fail("Yêu cầu đăng nhập để tạo liên kết phát video."));
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var isVip = await IsCallerVipAsync(db);
        if (!isVip)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<SignedStreamDto>.Fail("Yêu cầu gói thành viên VIP để tạo link phát video."));
        }

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

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var baseUrl = Request?.Host.HasValue == true
            ? $"{Request.Scheme}://{Request.Host}"
            : "http://localhost";

        var signed = urlSigner.Sign(baseUrl, request.EpisodeId, stream.Position, clientIp);
        return Ok(ApiResponse<SignedStreamDto>.Ok(signed));
    }

    [HttpGet("play")]
    public async Task<IActionResult> PlayStream(
        [FromQuery] int episodeId,
        [FromQuery] int pos,
        [FromQuery] long exp,
        [FromQuery] string? ip,
        [FromQuery] string? sig)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var validation = urlSigner.Validate(episodeId, pos, exp, ip ?? string.Empty, sig ?? string.Empty, clientIp);

        if (!validation.IsValid)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<string>.Fail(validation.ErrorMessage ?? "Chữ ký bảo vệ luồng không hợp lệ."));
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

        if (isVipStream && User?.Identity?.IsAuthenticated == true)
        {
            var isVip = await IsCallerVipAsync(db);
            if (!isVip)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<string>.Fail("Yêu cầu gói thành viên VIP để xem nguồn phát này."));
            }
        }

        return Redirect(stream.StreamUrl);
    }

    private async Task<bool> IsCallerVipAsync(AppDbContext db)
    {
        if (User?.Identity?.IsAuthenticated != true) return false;
        if (User.IsInRole("Admin")) return true;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
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
        else if (User.FindFirst("membership")?.Value is { } m && !m.Equals("Free", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
