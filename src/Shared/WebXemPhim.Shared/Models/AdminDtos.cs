using WebXemPhim.Shared.Enums;
using System.ComponentModel.DataAnnotations;
using WebXemPhim.Shared.Validation;

namespace WebXemPhim.Shared.Models;

public class AdminDashboardDto
{
    public int TotalMovies { get; set; }
    public int TotalSeries { get; set; }
    public int TotalAnime { get; set; }
    public int TotalEpisodes { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveVipUsers { get; set; }
    public int FreeUsers { get; set; }
    /// <summary>Số tài khoản có hạng VIP, tính cả tài khoản đã hết hạn.</summary>
    public int VipMembers { get; set; }
    /// <summary>Số tài khoản đang có quyền quản trị viên.</summary>
    public int AdminUsers { get; set; }
    /// <summary>Số tài khoản đang bị khoá.</summary>
    public int LockedUsers { get; set; }
    /// <summary>Số tài khoản VIP sắp hết hạn trong vòng 7 ngày.</summary>
    public int ExpiringSoonVipUsers { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalTransactions { get; set; }
    public int TotalReviews { get; set; }
    public double AverageRating { get; set; }
    public List<UserDto> RecentUsers { get; set; } = [];
    public List<PaymentDto> RecentPayments { get; set; } = [];
    public List<AdminReviewDto> RecentReviews { get; set; } = [];
    public Dictionary<string, int> GenreDistribution { get; set; } = [];
}

public class UpsertMovieRequest : IValidatableObject
{
    public int Id { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(200)]
    public string OriginalTitle { get; set; } = string.Empty;
    [StringLength(160)]
    public string Slug { get; set; } = string.Empty;
    [StringLength(2000)]
    public string Overview { get; set; } = string.Empty;
    [StringLength(600), SafeResourceUrl(true)]
    public string PosterUrl { get; set; } = string.Empty;
    [StringLength(600), SafeResourceUrl(true)]
    public string BackdropUrl { get; set; } = string.Empty;
    [StringLength(600), SafeResourceUrl]
    public string? TrailerUrl { get; set; }
    [Range(0, 10)]
    public double Rating { get; set; } = 8.0;
    [Range(0, int.MaxValue)]
    public int VoteCount { get; set; } = 100;
    [Range(1900, 2200)]
    public int ReleaseYear { get; set; } = DateTime.UtcNow.Year;
    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 120;
    [Required, StringLength(16)]
    public string AgeRating { get; set; } = "16+";
    [Required, StringLength(32)]
    public string Quality { get; set; } = "4K UHD";
    [EnumDataType(typeof(MovieType))]
    public MovieType Type { get; set; } = MovieType.Movie;
    [Required, StringLength(80)]
    public string Country { get; set; } = "Việt Nam";
    [StringLength(120)]
    public string Director { get; set; } = string.Empty;
    public bool IsTrending { get; set; }
    public bool IsFeatured { get; set; }
    [Range(1, 10000)]
    public int TotalEpisodes { get; set; } = 1;
    [Range(1, 10000)]
    public int CurrentEpisode { get; set; } = 1;
    [Required, MaxLength(50)]
    public List<int> GenreIds { get; set; } = [];
    [Required, MaxLength(100)]
    public List<string> CastNames { get; set; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CastNames?.Any(name => name is null || name.Length > 120) == true)
            yield return new ValidationResult("Tên diễn viên không được vượt quá 120 ký tự hoặc là null.", [nameof(CastNames)]);
        if (GenreIds?.Any(id => id <= 0) == true)
            yield return new ValidationResult("ID thể loại phải là số dương.", [nameof(GenreIds)]);
        if (CurrentEpisode > TotalEpisodes)
            yield return new ValidationResult("Tập hiện tại không được vượt tổng số tập.", [nameof(CurrentEpisode)]);
    }
}

public class UpsertEpisodeRequest
{
    [Range(1, 10000)]
    public int EpisodeNumber { get; set; } = 1;
    [Required, StringLength(240)]
    public string Title { get; set; } = string.Empty;
    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 45;
    [StringLength(600), SafeResourceUrl(true)]
    public string ThumbnailUrl { get; set; } = string.Empty;
    [StringLength(2000)]
    public string Overview { get; set; } = string.Empty;
    [StringLength(600), SafeResourceUrl]
    public string StreamUrl { get; set; } = string.Empty;
    [Required, StringLength(120)]
    public string ServerName { get; set; } = "VIP CDN";
    [Required, StringLength(32)]
    public string Quality { get; set; } = "1080p";
    [EnumDataType(typeof(StreamServerType))]
    public StreamServerType ServerType { get; set; } = StreamServerType.VipFastServer;
}

public class UpdateEpisodeRequest
{
    [Range(1, 10000)]
    public int EpisodeNumber { get; set; } = 1;
    [Required, StringLength(240)]
    public string Title { get; set; } = string.Empty;
    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 45;
    [StringLength(2000)]
    public string Overview { get; set; } = string.Empty;
}

public class AdminUpdateUserRequest
{
    [EnumDataType(typeof(MembershipTier))]
    public MembershipTier MembershipTier { get; set; }
    [Range(0, 3650)]
    public int ExtendDays { get; set; }
}

public class AdminReviewDto
{
    public Guid Id { get; set; }
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string MoviePosterUrl { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? AdminReply { get; set; }
    public DateTimeOffset? AdminRepliedAt { get; set; }
    public bool IsReported { get; set; }
}

public class UpsertStreamSourceRequest
{
    public int Id { get; set; }
    public int EpisodeId { get; set; }
    public int Position { get; set; } = 1;
    [Required, StringLength(120)]
    public string ServerName { get; set; } = "VIP CDN";
    [EnumDataType(typeof(StreamServerType))]
    public StreamServerType ServerType { get; set; } = StreamServerType.VipFastServer;
    [Required, StringLength(32)]
    public string Quality { get; set; } = "1080p";
    [Required, StringLength(600), SafeResourceUrl]
    public string StreamUrl { get; set; } = string.Empty;
}

public class ProbeStreamRequest
{
    [Required, StringLength(600), SafeResourceUrl]
    public string StreamUrl { get; set; } = string.Empty;
}

public class ProbeStreamResultDto
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public long LatencyMs { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class BulkMovieActionRequest
{
    [Required, MinLength(1)]
    public List<int> MovieIds { get; set; } = [];
    [Required]
    public string Action { get; set; } = string.Empty; // "delete", "toggle_trending", "toggle_featured", "change_genre"
    public int? TargetGenreId { get; set; }
}

public class CloneMovieRequest
{
    [Required, StringLength(200)]
    public string NewTitle { get; set; } = string.Empty;
    [StringLength(160)]
    public string? NewSlug { get; set; }
    [Range(1900, 2200)]
    public int ReleaseYear { get; set; } = DateTime.UtcNow.Year;
    public bool CopyEpisodes { get; set; } = true;
}

public class SystemHealthDto
{
    public long DbSizeBytes { get; set; }
    public string DbSizeFormatted { get; set; } = "0 MB";
    public string ServerUptime { get; set; } = "0h 0m";
    public int TotalMovies { get; set; }
    public int TotalEpisodes { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveStreamsCount { get; set; }
    public double MemoryUsageMb { get; set; }
    public string Environment { get; set; } = "Development";
    public DateTime ServerTimeUtc { get; set; }
}

public class AdminUserDetailDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public MembershipTier MembershipTier { get; set; }
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Initials { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsLocked { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public int FailedLoginAttempts { get; set; }
    public int WatchlistCount { get; set; }
    public int ReviewsCount { get; set; }
    public List<PaymentDto> RecentPayments { get; set; } = [];
    public List<AdminReviewDto> RecentReviews { get; set; } = [];
}

public class AdminUpdateUserRoleRequest
{
    public bool IsAdmin { get; set; }
}

public class AdminUpdateUserLockRequest
{
    public bool IsLocked { get; set; }
    [Range(0, 43200)]
    public int LockMinutes { get; set; } = 1440; // Default 1 day
}

public class AuditLogDto
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string AdminEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
}

public class AdminReviewModerationRequest
{
    [StringLength(1000)]
    public string? AdminReply { get; set; }
    public bool? IsReported { get; set; }
}

public class ManualPaymentApprovalRequest
{
    [Required, StringLength(120)]
    public string TransactionReference { get; set; } = string.Empty;
    [StringLength(500)]
    public string? Note { get; set; }
}

public class ManualVipGrantRequest
{
    [Required, StringLength(120)]
    public string UserEmailOrUsername { get; set; } = string.Empty;

    [Range(1, 3650)]
    public int Days { get; set; } = 30;

    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class PaymentRejectionRequest
{
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class UpsertVipPlanRequest
{
    [Required, StringLength(64)]
    public string Id { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 3650)]
    public int DurationDays { get; set; } = 30;

    [Range(0, 100000000)]
    public decimal Price { get; set; }

    [Range(0, 100000000)]
    public decimal? OriginalPrice { get; set; }

    [StringLength(60)]
    public string? DiscountBadge { get; set; }

    [StringLength(300)]
    public string Description { get; set; } = string.Empty;

    public List<string> Benefits { get; set; } = [];

    public bool IsRecommended { get; set; }

    public bool IsActive { get; set; } = true;
}

public class BatchEpisodeGenerateRequest
{
    public int MovieId { get; set; }
    [Range(1, 1000)]
    public int StartEpisode { get; set; } = 1;
    [Range(1, 1000)]
    public int EndEpisode { get; set; } = 12;
    [Range(1, 1440)]
    public int DurationMinutes { get; set; } = 45;
    [StringLength(240)]
    public string TitlePattern { get; set; } = "Tập {n}";
    [StringLength(600)]
    public string StreamUrlPattern { get; set; } = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4";
    [StringLength(120)]
    public string ServerName { get; set; } = "VIP FastCDN 1";
    [EnumDataType(typeof(StreamServerType))]
    public StreamServerType ServerType { get; set; } = StreamServerType.VipFastServer;
    [StringLength(32)]
    public string Quality { get; set; } = "1080p FHD";
}

