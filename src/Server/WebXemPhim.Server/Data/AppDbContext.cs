using Microsoft.EntityFrameworkCore;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserSecurityEntity> UserSecurity => Set<UserSecurityEntity>();
    public DbSet<MfaCredentialEntity> MfaCredentials => Set<MfaCredentialEntity>();
    public DbSet<MfaChallengeEntity> MfaChallenges => Set<MfaChallengeEntity>();
    public DbSet<MfaVerifiedSessionEntity> MfaVerifiedSessions => Set<MfaVerifiedSessionEntity>();
    public DbSet<SecurityAuditEventEntity> SecurityAuditEvents => Set<SecurityAuditEventEntity>();
    public DbSet<PlaybackIssueEntity> PlaybackIssues => Set<PlaybackIssueEntity>();
    public DbSet<UserLibraryEntity> UserLibrary => Set<UserLibraryEntity>();
    public DbSet<AccessTokenEntity> AccessTokens => Set<AccessTokenEntity>();
    public DbSet<PasswordResetTokenEntity> PasswordResetTokens => Set<PasswordResetTokenEntity>();
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();
    public DbSet<ReviewEntity> Reviews => Set<ReviewEntity>();
    public DbSet<MovieEntity> Movies => Set<MovieEntity>();
    public DbSet<GenreEntity> Genres => Set<GenreEntity>();
    public DbSet<MovieGenreEntity> MovieGenres => Set<MovieGenreEntity>();
    public DbSet<CastMemberEntity> CastMembers => Set<CastMemberEntity>();
    public DbSet<EpisodeEntity> Episodes => Set<EpisodeEntity>();
    public DbSet<StreamSourceEntity> StreamSources => Set<StreamSourceEntity>();
    public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<WatchProgressEntity> WatchProgress => Set<WatchProgressEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserSecurityEntity>(entity =>
        {
            entity.ToTable("user_security");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne(item => item.User).WithOne().HasForeignKey<UserSecurityEntity>(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MfaCredentialEntity>(entity =>
        {
            entity.ToTable("mfa_credentials");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.ProtectedSecret).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.RecoveryCodeHashes).HasMaxLength(2000).IsRequired();
            entity.HasOne(item => item.User).WithOne().HasForeignKey<MfaCredentialEntity>(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MfaChallengeEntity>(entity =>
        {
            entity.ToTable("mfa_challenges");
            entity.HasKey(item => item.TokenHash);
            entity.Property(item => item.TokenHash).HasMaxLength(64);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MfaVerifiedSessionEntity>(entity =>
        {
            entity.ToTable("mfa_verified_sessions");
            entity.HasKey(item => item.TokenHash);
            entity.Property(item => item.TokenHash).HasMaxLength(64);
            entity.HasIndex(item => item.UserId);
        });
        modelBuilder.Entity<SecurityAuditEventEntity>(entity =>
        {
            entity.ToTable("security_audit_events");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.CreatedAtUtc);
            entity.Property(item => item.EventType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Detail).HasMaxLength(240).IsRequired();
        });
        modelBuilder.Entity<PlaybackIssueEntity>(entity =>
        {
            entity.ToTable("playback_issues");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Description).HasMaxLength(800).IsRequired();
        });
        modelBuilder.Entity<UserLibraryEntity>(entity =>
        {
            entity.ToTable("user_library");
            entity.HasKey(item => new { item.UserId, item.MovieId });
        });
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.NormalizedEmail).IsUnique();
            entity.Property(item => item.FullName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Email).HasMaxLength(160).IsRequired();
            entity.Property(item => item.NormalizedEmail).HasMaxLength(160).IsRequired();
            entity.Property(item => item.PasswordHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.PasswordSalt).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Membership).HasConversion<int>();
        });
        modelBuilder.Entity<AccessTokenEntity>(entity =>
        {
            entity.ToTable("access_tokens");
            entity.HasKey(item => item.Token);
            entity.Property(item => item.Token).HasMaxLength(64);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PasswordResetTokenEntity>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(item => item.Token);
            entity.Property(item => item.Token).HasMaxLength(64);
            entity.Property(item => item.UsedAtUtc).IsConcurrencyToken();
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.OrderCode).IsUnique();
            entity.Property(item => item.OrderCode).HasMaxLength(32).IsRequired();
            entity.Property(item => item.PlanId).HasMaxLength(32).IsRequired();
            entity.Property(item => item.PlanName).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(12, 2);
            entity.Property(item => item.Method).HasConversion<int>();
            entity.Property(item => item.Status).HasConversion<int>();
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReviewEntity>(entity =>
        {
            entity.ToTable("reviews");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.MovieId, item.UserId }).IsUnique();
            entity.Property(item => item.Comment).HasMaxLength(800);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Movie).WithMany(movie => movie.Reviews).HasForeignKey(item => item.MovieId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MovieEntity>(entity =>
        {
            entity.ToTable("movies");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Slug).IsUnique();
            entity.HasIndex(item => new { item.Type, item.ReleaseYear });
            entity.HasIndex(item => new { item.IsTrending, item.Rating });
            entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
            entity.Property(item => item.OriginalTitle).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Slug).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Overview).HasMaxLength(2000).IsRequired();
            entity.Property(item => item.PosterUrl).HasMaxLength(600).IsRequired();
            entity.Property(item => item.BackdropUrl).HasMaxLength(600).IsRequired();
            entity.Property(item => item.TrailerUrl).HasMaxLength(600);
            entity.Property(item => item.AgeRating).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Quality).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Country).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Director).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Type).HasConversion<int>();
        });
        modelBuilder.Entity<GenreEntity>(entity =>
        {
            entity.ToTable("genres");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Slug).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Slug).HasMaxLength(80).IsRequired();
        });
        modelBuilder.Entity<MovieGenreEntity>(entity =>
        {
            entity.ToTable("movie_genres");
            entity.HasKey(item => new { item.MovieId, item.GenreId });
            entity.HasOne(item => item.Movie).WithMany(movie => movie.MovieGenres).HasForeignKey(item => item.MovieId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Genre).WithMany(genre => genre.MovieGenres).HasForeignKey(item => item.GenreId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CastMemberEntity>(entity =>
        {
            entity.ToTable("cast_members");
            entity.HasKey(item => new { item.MovieId, item.Position });
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.HasOne(item => item.Movie).WithMany(movie => movie.CastMembers).HasForeignKey(item => item.MovieId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EpisodeEntity>(entity =>
        {
            entity.ToTable("episodes");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.MovieId, item.EpisodeNumber }).IsUnique();
            entity.Property(item => item.Title).HasMaxLength(240).IsRequired();
            entity.Property(item => item.ThumbnailUrl).HasMaxLength(600).IsRequired();
            entity.Property(item => item.Overview).HasMaxLength(2000).IsRequired();
            entity.HasOne(item => item.Movie).WithMany(movie => movie.Episodes).HasForeignKey(item => item.MovieId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<StreamSourceEntity>(entity =>
        {
            entity.ToTable("stream_sources");
            entity.HasKey(item => new { item.EpisodeId, item.Position });
            entity.Property(item => item.ServerName).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Quality).HasMaxLength(32).IsRequired();
            entity.Property(item => item.StreamUrl).HasMaxLength(600).IsRequired();
            entity.Property(item => item.ServerType).HasConversion<int>();
            entity.HasOne(item => item.Episode).WithMany(episode => episode.Streams).HasForeignKey(item => item.EpisodeId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserSettingsEntity>(entity =>
        {
            entity.ToTable("user_settings");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.DefaultQuality).HasMaxLength(32).IsRequired();
            entity.HasOne(item => item.User).WithOne().HasForeignKey<UserSettingsEntity>(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Copy).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Icon).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ActionUrl).HasMaxLength(500);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WatchProgressEntity>(entity =>
        {
            entity.ToTable("watch_progress");
            entity.HasKey(item => new { item.UserId, item.MovieId });
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Movie).WithMany().HasForeignKey(item => item.MovieId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.TimestampUtc);
            entity.HasIndex(item => item.AdminEmail);
            entity.Property(item => item.AdminEmail).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Action).HasMaxLength(80).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.EntityId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Summary).HasMaxLength(500).IsRequired();
        });
    }
}

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordHash { get; set; } = [];
    public MembershipTier Membership { get; set; } = MembershipTier.Free;
    public DateTime? VipExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AccessTokenEntity
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class PasswordResetTokenEntity
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

public sealed class MfaCredentialEntity
{
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string ProtectedSecret { get; set; } = string.Empty;
    public string RecoveryCodeHashes { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTime? EnabledAtUtc { get; set; }
}

public sealed class MfaChallengeEntity
{
    public string TokenHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
}

public sealed class SecurityAuditEventEntity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class MfaVerifiedSessionEntity
{
    public string TokenHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTime VerifiedAtUtc { get; set; }
}

public sealed class PaymentEntity
{
    public Guid Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string PlanId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? VipExpiresAtUtc { get; set; }
    public string? TransactionReference { get; set; }
    public string? Note { get; set; }
}

public sealed class ReviewEntity
{
    public Guid Id { get; set; }
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? AdminReply { get; set; }
    public DateTime? AdminRepliedAtUtc { get; set; }
    public bool IsReported { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class UserSettingsEntity
{
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string DefaultQuality { get; set; } = "4K khi có thể";
    public bool AutoplayNext { get; set; } = true;
    public bool NotifyNewEpisodes { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class NotificationEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Copy { get; set; } = string.Empty;
    public string Icon { get; set; } = "Bell";
    public string? ActionUrl { get; set; }
    public bool Read { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class WatchProgressEntity
{
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;
    public int? EpisodeId { get; set; }
    public int EpisodeNumber { get; set; } = 1;
    public int ProgressSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime LastWatchedAtUtc { get; set; }
}
