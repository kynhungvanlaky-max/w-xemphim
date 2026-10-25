using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;

namespace WebXemPhim.Server.Services;

public interface IStreamSessionService
{
    Task<StreamLeaseResultDto> AcquireLeaseAsync(
        Guid userId,
        AcquireStreamLeaseRequest request,
        string? connectionId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<StreamLeaseResultDto> AcquireLeaseAsync(
        Guid userId,
        int movieId,
        int? episodeId,
        string sessionId,
        string? deviceName = null,
        string? connectionId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> RecordHeartbeatAsync(Guid userId, string sessionId, string connectionId, CancellationToken cancellationToken = default);

    Task<bool> ReleaseLeaseAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> ReleaseLeaseAsync(Guid userId, string sessionId, CancellationToken cancellationToken = default);

    Task<int> ReleaseByConnectionIdAsync(string connectionId, CancellationToken cancellationToken = default);

    Task<int> RevokeUserStreamsAsync(Guid userId, string reason = "Tài khoản đã đăng xuất.", CancellationToken cancellationToken = default);

    Task<int> ReleaseUserLeasesAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> PruneExpiredLeasesAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task<List<ActiveStreamDto>> GetActiveLeasesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Trả về lease đang hoạt động theo mã phiên, hoặc <c>null</c> nếu đã hết hạn/thu hồi.</summary>
    Task<ActiveStreamDto?> GetActiveLeaseAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveStreamDto>> GetActiveStreamsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> GetMaxConcurrentStreamsAsync(Guid userId, CancellationToken cancellationToken = default);
}
