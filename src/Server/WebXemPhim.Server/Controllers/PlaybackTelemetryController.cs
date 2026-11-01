using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

/// <summary>
/// Nhận beacon QoE từ trình phát. Endpoint yêu cầu đăng nhập nhưng không tin dữ liệu
/// client gửi: mọi trường đều được giới hạn kích thước/giá trị trước khi ghi.
/// </summary>
[ApiController]
[Authorize]
[Route("api/stream/telemetry")]
public sealed class PlaybackTelemetryController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    ILogger<PlaybackTelemetryController>? logger = null) : ControllerBase
{
    private const long MaxDurationValueMs = 6 * 60 * 60 * 1000L;
    private const long MaxBitrateBps = 200_000_000L;

    [HttpPost]
    public async Task<ActionResult<ApiResponse<bool>>> Record([FromBody] PlaybackTelemetryRequest request)
    {
        if (request is null)
        {
            return BadRequest(ApiResponse<bool>.Fail("Dữ liệu telemetry không hợp lệ."));
        }

        var eventType = request.EventType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!PlaybackTelemetryEventTypes.IsValid(eventType))
        {
            return BadRequest(ApiResponse<bool>.Fail("Loại sự kiện telemetry không được hỗ trợ."));
        }

        if (request.MovieId < 0 || request.EpisodeId < 0 || request.Position < 0)
        {
            return BadRequest(ApiResponse<bool>.Fail("Mã phim/tập/vị trí không hợp lệ."));
        }

        var value = request.Value;
        if (eventType is PlaybackTelemetryEventTypes.Startup
            or PlaybackTelemetryEventTypes.Rebuffer
            or PlaybackTelemetryEventTypes.Stall
            or PlaybackTelemetryEventTypes.Seek)
        {
            if (value < 0 || value > MaxDurationValueMs)
            {
                return BadRequest(ApiResponse<bool>.Fail("Giá trị thời lượng telemetry không hợp lệ."));
            }
        }
        else if (eventType == PlaybackTelemetryEventTypes.Bitrate)
        {
            if (value < 0 || value > MaxBitrateBps)
            {
                return BadRequest(ApiResponse<bool>.Fail("Giá trị bitrate telemetry không hợp lệ."));
            }
        }
        else
        {
            value = 0;
        }

        var userId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parsed)
            ? parsed
            : (Guid?)null;

        var entity = new PlaybackTelemetryEntity
        {
            UserId = userId,
            MovieId = request.MovieId,
            EpisodeId = request.EpisodeId,
            SessionId = Truncate(request.SessionId?.Trim(), 64),
            Position = request.Position,
            ServerName = Truncate(request.ServerName?.Trim(), 120),
            Quality = Truncate(request.Quality?.Trim(), 32),
            EventType = eventType,
            Value = value,
            ErrorCode = Truncate(request.ErrorCode?.Trim(), 240),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(HttpContext.RequestAborted);
            db.PlaybackTelemetry.Add(entity);
            await db.SaveChangesAsync(HttpContext.RequestAborted);
            return Ok(ApiResponse<bool>.Ok(true));
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Telemetry không được làm hỏng trải nghiệm xem phim.
            logger?.LogWarning(ex, "Không ghi được telemetry QoE cho tập {EpisodeId}.", request.EpisodeId);
            return Ok(ApiResponse<bool>.Ok(false, "Telemetry chưa được ghi nhận."));
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}
