using Microsoft.EntityFrameworkCore;

namespace WebXemPhim.Server.Data;

public static class DatabaseSchemaHelper
{
    public static async Task EnsureTablesCreatedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var isSqlite = db.Database.IsSqlite();
        var userSecuritySql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS user_security (UserId TEXT NOT NULL PRIMARY KEY, IsAdmin INTEGER NOT NULL DEFAULT 0, PasswordIterations INTEGER NOT NULL DEFAULT 100000, FailedLoginAttempts INTEGER NOT NULL DEFAULT 0, LockedUntilUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 0);"
            : "CREATE TABLE IF NOT EXISTS user_security (UserId char(36) NOT NULL PRIMARY KEY, IsAdmin tinyint(1) NOT NULL DEFAULT 0, PasswordIterations int NOT NULL DEFAULT 100000, FailedLoginAttempts int NOT NULL DEFAULT 0, LockedUntilUtc datetime(6) NULL, Version bigint NOT NULL DEFAULT 0);";
        await db.Database.ExecuteSqlRawAsync(userSecuritySql, cancellationToken);
        var mfaCredentialsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS mfa_credentials (UserId TEXT NOT NULL PRIMARY KEY, ProtectedSecret TEXT NOT NULL, RecoveryCodeHashes TEXT NOT NULL, Enabled INTEGER NOT NULL DEFAULT 0, EnabledAtUtc TEXT NULL);"
            : "CREATE TABLE IF NOT EXISTS mfa_credentials (UserId char(36) NOT NULL PRIMARY KEY, ProtectedSecret varchar(1000) NOT NULL, RecoveryCodeHashes varchar(2000) NOT NULL, Enabled tinyint(1) NOT NULL DEFAULT 0, EnabledAtUtc datetime(6) NULL);";
        var mfaChallengesSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS mfa_challenges (TokenHash TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, ExpiresAtUtc TEXT NOT NULL, FailedAttempts INTEGER NOT NULL DEFAULT 0);"
            : "CREATE TABLE IF NOT EXISTS mfa_challenges (TokenHash varchar(64) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, ExpiresAtUtc datetime(6) NOT NULL, FailedAttempts int NOT NULL DEFAULT 0);";
        var mfaVerifiedSessionsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS mfa_verified_sessions (TokenHash TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, VerifiedAtUtc TEXT NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS mfa_verified_sessions (TokenHash varchar(64) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, VerifiedAtUtc datetime(6) NOT NULL, INDEX IX_mfa_verified_sessions_UserId (UserId));";
        var securityAuditSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS security_audit_events (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NULL, EventType TEXT NOT NULL, Success INTEGER NOT NULL, Detail TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS security_audit_events (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NULL, EventType varchar(80) NOT NULL, Success tinyint(1) NOT NULL, Detail varchar(240) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_security_audit_events_CreatedAtUtc (CreatedAtUtc));";
        await db.Database.ExecuteSqlRawAsync(mfaCredentialsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(mfaChallengesSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(mfaVerifiedSessionsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(securityAuditSql, cancellationToken);
        var playbackIssuesSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS playback_issues (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, EpisodeId INTEGER NOT NULL, Description TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS playback_issues (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NOT NULL, Description varchar(800) NOT NULL, CreatedAtUtc datetime(6) NOT NULL);";
        await db.Database.ExecuteSqlRawAsync(playbackIssuesSql, cancellationToken);

        var userLibrarySql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS user_library (UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, AddedAt TEXT NOT NULL, PRIMARY KEY (UserId, MovieId));"
            : "CREATE TABLE IF NOT EXISTS user_library (UserId char(36) NOT NULL, MovieId int NOT NULL, AddedAt datetime(6) NOT NULL, PRIMARY KEY (UserId, MovieId));";

        var userSettingsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS user_settings (UserId TEXT NOT NULL PRIMARY KEY, DefaultQuality TEXT NOT NULL, AutoplayNext INTEGER NOT NULL, NotifyNewEpisodes INTEGER NOT NULL, UpdatedAtUtc TEXT NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS user_settings (UserId char(36) NOT NULL PRIMARY KEY, DefaultQuality varchar(32) NOT NULL, AutoplayNext tinyint(1) NOT NULL, NotifyNewEpisodes tinyint(1) NOT NULL, UpdatedAtUtc datetime(6) NOT NULL);";

        var notificationsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS notifications (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, Title TEXT NOT NULL, Copy TEXT NOT NULL, Icon TEXT NOT NULL, ActionUrl TEXT, \"Read\" INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS notifications (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, Title varchar(200) NOT NULL, Copy varchar(1000) NOT NULL, Icon varchar(32) NOT NULL, ActionUrl varchar(500) NULL, `Read` tinyint(1) NOT NULL, CreatedAtUtc datetime(6) NOT NULL);";

        var watchProgressSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS watch_progress (UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, EpisodeId INTEGER NULL, EpisodeNumber INTEGER NOT NULL, ProgressSeconds INTEGER NOT NULL, DurationSeconds INTEGER NOT NULL, LastWatchedAtUtc TEXT NOT NULL, PRIMARY KEY (UserId, MovieId));"
            : "CREATE TABLE IF NOT EXISTS watch_progress (UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NULL, EpisodeNumber int NOT NULL, ProgressSeconds int NOT NULL, DurationSeconds int NOT NULL, LastWatchedAtUtc datetime(6) NOT NULL, PRIMARY KEY (UserId, MovieId));";

        var auditLogsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS audit_logs (Id TEXT NOT NULL PRIMARY KEY, TimestampUtc TEXT NOT NULL, AdminEmail TEXT NOT NULL, Action TEXT NOT NULL, EntityType TEXT NOT NULL, EntityId TEXT NOT NULL, Summary TEXT NOT NULL, DetailsJson TEXT NULL);"
            : "CREATE TABLE IF NOT EXISTS audit_logs (Id char(36) NOT NULL PRIMARY KEY, TimestampUtc datetime(6) NOT NULL, AdminEmail varchar(200) NOT NULL, Action varchar(80) NOT NULL, EntityType varchar(80) NOT NULL, EntityId varchar(120) NOT NULL, Summary varchar(500) NOT NULL, DetailsJson text NULL);";

        await db.Database.ExecuteSqlRawAsync(userLibrarySql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(userSettingsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(notificationsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(watchProgressSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(auditLogsSql, cancellationToken);

        // Safe column migrations for reviews moderation
        try
        {
            await db.Database.ExecuteSqlRawAsync(isSqlite ? "ALTER TABLE reviews ADD COLUMN AdminReply TEXT NULL;" : "ALTER TABLE reviews ADD COLUMN AdminReply text NULL;", cancellationToken);
        }
        catch { }
        try
        {
            await db.Database.ExecuteSqlRawAsync(isSqlite ? "ALTER TABLE reviews ADD COLUMN AdminRepliedAtUtc TEXT NULL;" : "ALTER TABLE reviews ADD COLUMN AdminRepliedAtUtc datetime(6) NULL;", cancellationToken);
        }
        catch { }
        try
        {
            await db.Database.ExecuteSqlRawAsync(isSqlite ? "ALTER TABLE reviews ADD COLUMN IsReported INTEGER NOT NULL DEFAULT 0;" : "ALTER TABLE reviews ADD COLUMN IsReported tinyint(1) NOT NULL DEFAULT 0;", cancellationToken);
        }
        catch { }

        // Safe column migrations for payments
        try
        {
            await db.Database.ExecuteSqlRawAsync(isSqlite ? "ALTER TABLE payments ADD COLUMN TransactionReference TEXT NULL;" : "ALTER TABLE payments ADD COLUMN TransactionReference varchar(120) NULL;", cancellationToken);
        }
        catch { }
        try
        {
            await db.Database.ExecuteSqlRawAsync(isSqlite ? "ALTER TABLE payments ADD COLUMN Note TEXT NULL;" : "ALTER TABLE payments ADD COLUMN Note varchar(500) NULL;", cancellationToken);
        }
        catch { }
    }
}
