using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Shared.Utils;

namespace WebXemPhim.Server.Data;

public static class DatabaseSchemaHelper
{
    /// <summary>
    /// Creates the tables/indexes that the EF model does not cover, keeps the runtime search
    /// index in sync and applies the safe column additions that older databases are missing.
    /// </summary>
    public static async Task EnsureTablesCreatedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var isPostgres = db.Database.IsNpgsql();

        var userSecuritySql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS user_security (\"UserId\" uuid NOT NULL PRIMARY KEY, \"IsAdmin\" boolean NOT NULL DEFAULT false, \"PasswordIterations\" integer NOT NULL DEFAULT 100000, \"FailedLoginAttempts\" integer NOT NULL DEFAULT 0, \"LockedUntilUtc\" timestamptz NULL, \"Version\" bigint NOT NULL DEFAULT 0, CONSTRAINT FK_user_security_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS user_security (UserId char(36) NOT NULL PRIMARY KEY, IsAdmin tinyint(1) NOT NULL DEFAULT 0, PasswordIterations int NOT NULL DEFAULT 100000, FailedLoginAttempts int NOT NULL DEFAULT 0, LockedUntilUtc datetime(6) NULL, Version bigint NOT NULL DEFAULT 0, CONSTRAINT FK_user_security_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var mfaCredentialsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS mfa_credentials (\"UserId\" uuid NOT NULL PRIMARY KEY, \"ProtectedSecret\" varchar(1000) NOT NULL, \"RecoveryCodeHashes\" varchar(2000) NOT NULL, \"Enabled\" boolean NOT NULL DEFAULT false, \"EnabledAtUtc\" timestamptz NULL, \"LastUsedTimeStep\" bigint NULL, \"Version\" bigint NOT NULL DEFAULT 0, CONSTRAINT FK_mfa_credentials_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS mfa_credentials (UserId char(36) NOT NULL PRIMARY KEY, ProtectedSecret varchar(1000) NOT NULL, RecoveryCodeHashes varchar(2000) NOT NULL, Enabled tinyint(1) NOT NULL DEFAULT 0, EnabledAtUtc datetime(6) NULL, LastUsedTimeStep bigint NULL, Version bigint NOT NULL DEFAULT 0, CONSTRAINT FK_mfa_credentials_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var mfaChallengesSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS mfa_challenges (\"TokenHash\" varchar(64) NOT NULL PRIMARY KEY, \"UserId\" uuid NOT NULL, \"ExpiresAtUtc\" timestamptz NOT NULL, \"FailedAttempts\" integer NOT NULL DEFAULT 0, \"Version\" bigint NOT NULL DEFAULT 0, CONSTRAINT FK_mfa_challenges_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS mfa_challenges (TokenHash varchar(64) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, ExpiresAtUtc datetime(6) NOT NULL, FailedAttempts int NOT NULL DEFAULT 0, Version bigint NOT NULL DEFAULT 0, INDEX IX_mfa_challenges_UserId (UserId), CONSTRAINT FK_mfa_challenges_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var mfaVerifiedSessionsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS mfa_verified_sessions (\"TokenHash\" varchar(64) NOT NULL PRIMARY KEY, \"UserId\" uuid NOT NULL, \"VerifiedAtUtc\" timestamptz NOT NULL, CONSTRAINT FK_mfa_verified_sessions_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS mfa_verified_sessions (TokenHash varchar(64) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, VerifiedAtUtc datetime(6) NOT NULL, INDEX IX_mfa_verified_sessions_UserId (UserId), CONSTRAINT FK_mfa_verified_sessions_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var securityAuditSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS security_audit_events (\"Id\" uuid NOT NULL PRIMARY KEY, \"UserId\" uuid NULL, \"EventType\" varchar(80) NOT NULL, \"Success\" boolean NOT NULL, \"Detail\" varchar(240) NOT NULL, \"CreatedAtUtc\" timestamptz NOT NULL, CONSTRAINT FK_security_audit_events_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE SET NULL);"
            : "CREATE TABLE IF NOT EXISTS security_audit_events (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NULL, EventType varchar(80) NOT NULL, Success tinyint(1) NOT NULL, Detail varchar(240) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_security_audit_events_CreatedAtUtc (CreatedAtUtc), INDEX IX_security_audit_events_UserId (UserId), CONSTRAINT FK_security_audit_events_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE SET NULL);";

        var playbackIssuesSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS playback_issues (\"Id\" uuid NOT NULL PRIMARY KEY, \"UserId\" uuid NOT NULL, \"MovieId\" integer NOT NULL, \"EpisodeId\" integer NOT NULL, \"Description\" varchar(800) NOT NULL, \"CreatedAtUtc\" timestamptz NOT NULL, CONSTRAINT FK_playback_issues_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE, CONSTRAINT FK_playback_issues_movies_MovieId FOREIGN KEY (\"MovieId\") REFERENCES movies(\"Id\") ON DELETE CASCADE, CONSTRAINT FK_playback_issues_episodes_EpisodeId FOREIGN KEY (\"EpisodeId\") REFERENCES episodes(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS playback_issues (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NOT NULL, Description varchar(800) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_playback_issues_UserId_MovieId (UserId, MovieId), CONSTRAINT FK_playback_issues_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_playback_issues_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE, CONSTRAINT FK_playback_issues_episodes_EpisodeId FOREIGN KEY (EpisodeId) REFERENCES episodes(Id) ON DELETE CASCADE);";

        var userLibrarySql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS user_library (\"UserId\" uuid NOT NULL, \"MovieId\" integer NOT NULL, \"AddedAt\" timestamptz NOT NULL, PRIMARY KEY (\"UserId\", \"MovieId\"), CONSTRAINT FK_user_library_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE, CONSTRAINT FK_user_library_movies_MovieId FOREIGN KEY (\"MovieId\") REFERENCES movies(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS user_library (UserId char(36) NOT NULL, MovieId int NOT NULL, AddedAt datetime(6) NOT NULL, PRIMARY KEY (UserId, MovieId), INDEX IX_user_library_MovieId (MovieId), CONSTRAINT FK_user_library_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_user_library_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE);";

        var userSettingsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS user_settings (\"UserId\" uuid NOT NULL PRIMARY KEY, \"DefaultQuality\" varchar(32) NOT NULL, \"AutoplayNext\" boolean NOT NULL, \"NotifyNewEpisodes\" boolean NOT NULL, \"Language\" varchar(10) NOT NULL DEFAULT 'vi', \"UpdatedAtUtc\" timestamptz NOT NULL, CONSTRAINT FK_user_settings_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS user_settings (UserId char(36) NOT NULL PRIMARY KEY, DefaultQuality varchar(32) NOT NULL, AutoplayNext tinyint(1) NOT NULL, NotifyNewEpisodes tinyint(1) NOT NULL, Language varchar(10) NOT NULL DEFAULT 'vi', UpdatedAtUtc datetime(6) NOT NULL, CONSTRAINT FK_user_settings_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var notificationsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS notifications (\"Id\" uuid NOT NULL PRIMARY KEY, \"UserId\" uuid NOT NULL, \"Title\" varchar(200) NOT NULL, \"Copy\" varchar(1000) NOT NULL, \"Icon\" varchar(32) NOT NULL, \"ActionUrl\" varchar(500) NULL, \"Read\" boolean NOT NULL, \"CreatedAtUtc\" timestamptz NOT NULL, CONSTRAINT FK_notifications_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS notifications (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, Title varchar(200) NOT NULL, Copy varchar(1000) NOT NULL, Icon varchar(32) NOT NULL, ActionUrl varchar(500) NULL, `Read` tinyint(1) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_notifications_UserId_Read (UserId, `Read`), CONSTRAINT FK_notifications_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var watchProgressSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS watch_progress (\"UserId\" uuid NOT NULL, \"MovieId\" integer NOT NULL, \"EpisodeId\" integer NULL, \"EpisodeNumber\" integer NOT NULL, \"ProgressSeconds\" integer NOT NULL, \"DurationSeconds\" integer NOT NULL, \"LastWatchedAtUtc\" timestamptz NOT NULL, PRIMARY KEY (\"UserId\", \"MovieId\"), CONSTRAINT FK_watch_progress_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE, CONSTRAINT FK_watch_progress_movies_MovieId FOREIGN KEY (\"MovieId\") REFERENCES movies(\"Id\") ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS watch_progress (UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NULL, EpisodeNumber int NOT NULL, ProgressSeconds int NOT NULL, DurationSeconds int NOT NULL, LastWatchedAtUtc datetime(6) NOT NULL, PRIMARY KEY (UserId, MovieId), INDEX IX_watch_progress_MovieId (MovieId), CONSTRAINT FK_watch_progress_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_watch_progress_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE);";

        var auditLogsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS audit_logs (\"Id\" uuid NOT NULL PRIMARY KEY, \"TimestampUtc\" timestamptz NOT NULL, \"AdminEmail\" varchar(200) NOT NULL, \"Action\" varchar(80) NOT NULL, \"EntityType\" varchar(80) NOT NULL, \"EntityId\" varchar(120) NOT NULL, \"Summary\" varchar(500) NOT NULL, \"DetailsJson\" text NULL);"
            : "CREATE TABLE IF NOT EXISTS audit_logs (Id char(36) NOT NULL PRIMARY KEY, TimestampUtc datetime(6) NOT NULL, AdminEmail varchar(200) NOT NULL, Action varchar(80) NOT NULL, EntityType varchar(80) NOT NULL, EntityId varchar(120) NOT NULL, Summary varchar(500) NOT NULL, DetailsJson text NULL, INDEX IX_audit_logs_TimestampUtc (TimestampUtc), INDEX IX_audit_logs_AdminEmail (AdminEmail), INDEX IX_audit_logs_EntityType_EntityId (EntityType, EntityId));";

        var vipPlansSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS vip_plans (\"Id\" varchar(32) NOT NULL PRIMARY KEY, \"Name\" varchar(80) NOT NULL, \"Description\" varchar(500) NOT NULL, \"PriceVnd\" numeric(12,2) NOT NULL, \"DurationDays\" integer NOT NULL, \"IsActive\" boolean NOT NULL DEFAULT true, \"DisplayOrder\" integer NOT NULL DEFAULT 0);"
            : "CREATE TABLE IF NOT EXISTS vip_plans (Id varchar(32) NOT NULL PRIMARY KEY, Name varchar(80) NOT NULL, Description varchar(500) NOT NULL, PriceVnd decimal(12,2) NOT NULL, DurationDays int NOT NULL, IsActive tinyint(1) NOT NULL DEFAULT 1, DisplayOrder int NOT NULL DEFAULT 0);";

        var couponsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS coupons (\"Code\" varchar(32) NOT NULL PRIMARY KEY, \"DiscountType\" varchar(32) NOT NULL, \"DiscountValue\" numeric(12,2) NOT NULL, \"ValidUntilUtc\" timestamptz NOT NULL, \"IsActive\" boolean NOT NULL DEFAULT true);"
            : "CREATE TABLE IF NOT EXISTS coupons (Code varchar(32) NOT NULL PRIMARY KEY, DiscountType varchar(32) NOT NULL, DiscountValue decimal(12,2) NOT NULL, ValidUntilUtc datetime(6) NOT NULL, IsActive tinyint(1) NOT NULL DEFAULT 1);";

        var activeStreamsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS active_streams (\"Id\" uuid NOT NULL PRIMARY KEY, \"UserId\" uuid NOT NULL, \"MovieId\" integer NOT NULL, \"EpisodeId\" integer NULL, \"SessionId\" varchar(64) NOT NULL, \"ConnectionId\" varchar(64) NULL, \"IpAddress\" varchar(45) NULL, \"DeviceInfo\" varchar(256) NULL, \"StartedAtUtc\" timestamptz NOT NULL, \"LastHeartbeatUtc\" timestamptz NOT NULL, \"ExpiresAtUtc\" timestamptz NOT NULL, \"IsActive\" boolean NOT NULL DEFAULT true, CONSTRAINT FK_active_streams_users_UserId FOREIGN KEY (\"UserId\") REFERENCES users(\"Id\") ON DELETE CASCADE, CONSTRAINT FK_active_streams_movies_MovieId FOREIGN KEY (\"MovieId\") REFERENCES movies(\"Id\") ON DELETE CASCADE, CONSTRAINT FK_active_streams_episodes_EpisodeId FOREIGN KEY (\"EpisodeId\") REFERENCES episodes(\"Id\") ON DELETE SET NULL);"
            : "CREATE TABLE IF NOT EXISTS active_streams (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NULL, SessionId varchar(64) NOT NULL, ConnectionId varchar(64) NULL, IpAddress varchar(45) NULL, DeviceInfo varchar(256) NULL, StartedAtUtc datetime(6) NOT NULL, LastHeartbeatUtc datetime(6) NOT NULL, ExpiresAtUtc datetime(6) NOT NULL, IsActive tinyint(1) NOT NULL DEFAULT 1, UNIQUE INDEX IX_active_streams_SessionId (SessionId), INDEX IX_active_streams_UserId (UserId), INDEX IX_active_streams_ExpiresAtUtc (ExpiresAtUtc), INDEX IX_active_streams_User_Active_Exp (UserId, IsActive, ExpiresAtUtc), CONSTRAINT FK_active_streams_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_active_streams_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE, CONSTRAINT FK_active_streams_episodes_EpisodeId FOREIGN KEY (EpisodeId) REFERENCES episodes(Id) ON DELETE SET NULL);";

        var maintenanceFlagsSql = isPostgres
            ? "CREATE TABLE IF NOT EXISTS app_maintenance_flags (\"Flag\" varchar(120) NOT NULL PRIMARY KEY, \"CompletedAtUtc\" timestamptz NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS app_maintenance_flags (Flag varchar(120) NOT NULL PRIMARY KEY, CompletedAtUtc datetime(6) NOT NULL);";

        // PostgreSQL validates a foreign key when the referencing table is created, so the
        // tables that the helper DDL points at must already exist. When they do not (an
        // uninitialized database) the dependent tables are skipped and left to the migrations.
        var canCreateDependentTables = true;
        if (isPostgres)
        {
            canCreateDependentTables =
                await CheckTableExistsAsync(db, isPostgres, "users", cancellationToken) &&
                await CheckTableExistsAsync(db, isPostgres, "movies", cancellationToken) &&
                await CheckTableExistsAsync(db, isPostgres, "episodes", cancellationToken);
        }

        if (canCreateDependentTables)
        {
            await db.Database.ExecuteSqlRawAsync(userSecuritySql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(mfaCredentialsSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(mfaChallengesSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(mfaVerifiedSessionsSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(securityAuditSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(playbackIssuesSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(userLibrarySql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(userSettingsSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(notificationsSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(watchProgressSql, cancellationToken);
            await db.Database.ExecuteSqlRawAsync(activeStreamsSql, cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(auditLogsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(vipPlansSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(couponsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(maintenanceFlagsSql, cancellationToken);
        if (isPostgres)
        {
            if (canCreateDependentTables)
            {
                // PostgreSQL cannot declare secondary indexes inline in CREATE TABLE, so they are
                // created separately. IF NOT EXISTS keeps the helper idempotent across restarts.
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_mfa_challenges_UserId\" ON mfa_challenges (\"UserId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_mfa_verified_sessions_UserId\" ON mfa_verified_sessions (\"UserId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_security_audit_events_CreatedAtUtc\" ON security_audit_events (\"CreatedAtUtc\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_security_audit_events_UserId\" ON security_audit_events (\"UserId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_playback_issues_UserId_MovieId\" ON playback_issues (\"UserId\", \"MovieId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_user_library_MovieId\" ON user_library (\"MovieId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_notifications_UserId_Read\" ON notifications (\"UserId\", \"Read\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_watch_progress_MovieId\" ON watch_progress (\"MovieId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_audit_logs_TimestampUtc\" ON audit_logs (\"TimestampUtc\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_audit_logs_AdminEmail\" ON audit_logs (\"AdminEmail\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_audit_logs_EntityType_EntityId\" ON audit_logs (\"EntityType\", \"EntityId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_active_streams_SessionId\" ON active_streams (\"SessionId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_active_streams_UserId\" ON active_streams (\"UserId\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_active_streams_ExpiresAtUtc\" ON active_streams (\"ExpiresAtUtc\");", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_active_streams_UserId_IsActive\" ON active_streams (\"UserId\", \"IsActive\", \"ExpiresAtUtc\");", cancellationToken);
            }
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_mfa_verified_sessions_UserId ON mfa_verified_sessions (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_security_audit_events_CreatedAtUtc ON security_audit_events (CreatedAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_security_audit_events_UserId ON security_audit_events (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_playback_issues_UserId_MovieId ON playback_issues (UserId, MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_user_library_MovieId ON user_library (MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_notifications_UserId_Read ON notifications (UserId, `Read`);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_watch_progress_MovieId ON watch_progress (MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_audit_logs_TimestampUtc ON audit_logs (TimestampUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_audit_logs_AdminEmail ON audit_logs (AdminEmail);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_audit_logs_EntityType_EntityId ON audit_logs (EntityType, EntityId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_active_streams_SessionId ON active_streams (SessionId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_active_streams_UserId ON active_streams (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_active_streams_ExpiresAtUtc ON active_streams (ExpiresAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_active_streams_UserId_IsActive ON active_streams (UserId, IsActive, ExpiresAtUtc);", cancellationToken);
        }

        // Safe column migrations via metadata inspection
        await EnsureColumnExistsAsync(db, isPostgres, "reviews", "AdminReply", "text NULL", "text NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isPostgres, "reviews", "AdminRepliedAtUtc", "timestamptz NULL", "datetime(6) NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isPostgres, "reviews", "IsReported", "boolean NOT NULL DEFAULT false", "tinyint(1) NOT NULL DEFAULT 0", cancellationToken);

        await EnsureColumnExistsAsync(db, isPostgres, "payments", "TransactionReference", "varchar(120) NULL", "varchar(120) NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isPostgres, "payments", "Note", "text NULL", "varchar(500) NULL", cancellationToken);

        await EnsureColumnExistsAsync(db, isPostgres, "users", "EmailLookupHash", "varchar(64) NULL", "varchar(64) NULL", cancellationToken);

        await EnsureColumnExistsAsync(db, isPostgres, "user_settings", "Language", "varchar(10) NOT NULL DEFAULT 'vi'", "varchar(10) NOT NULL DEFAULT 'vi'", cancellationToken);

        await EnsureColumnExistsAsync(db, isPostgres, "mfa_credentials", "LastUsedTimeStep", "bigint NULL", "bigint NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isPostgres, "mfa_credentials", "Version", "bigint NOT NULL DEFAULT 0", "bigint NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnExistsAsync(db, isPostgres, "mfa_challenges", "Version", "bigint NOT NULL DEFAULT 0", "bigint NOT NULL DEFAULT 0", cancellationToken);

        await EnsureColumnExistsAsync(db, isPostgres, "movies", "SearchText", "text NULL", "longtext NULL", cancellationToken);
        await EnsureSearchTextIndexAsync(db, isPostgres, cancellationToken);

        if (isPostgres)
        {
            await EnsurePostgresSearchIndexAsync(db, cancellationToken);
        }

        var usersTableExists = await CheckTableExistsAsync(db, isPostgres, "users", cancellationToken);
        if (usersTableExists)
        {
            await db.Database.ExecuteSqlRawAsync(
                isPostgres
                    ? "CREATE INDEX IF NOT EXISTS \"IX_users_EmailLookupHash\" ON users (\"EmailLookupHash\");"
                    : "CREATE INDEX IF NOT EXISTS \"IX_users_EmailLookupHash\" ON users (EmailLookupHash);",
                cancellationToken);
        }

        var paymentsTableExists = await CheckTableExistsAsync(db, isPostgres, "payments", cancellationToken);
        if (paymentsTableExists)
        {
            await db.Database.ExecuteSqlRawAsync(
                isPostgres
                    ? "CREATE INDEX IF NOT EXISTS \"IX_payments_TransactionReference\" ON payments (\"TransactionReference\");"
                    : "CREATE INDEX IF NOT EXISTS \"IX_payments_TransactionReference\" ON payments (TransactionReference);",
                cancellationToken);
        }

        try
        {
            var moviesNeedingSearchText = await db.Movies
                .Include(m => m.CastMembers)
                .Where(m => m.SearchText == null || m.SearchText == string.Empty)
                .ToListAsync(cancellationToken);
            if (moviesNeedingSearchText.Count > 0)
            {
                foreach (var movie in moviesNeedingSearchText)
                {
                    movie.SearchText = WebXemPhim.Shared.Utils.MovieSearchTextBuilder.Build(
                        movie.Title,
                        movie.OriginalTitle,
                        movie.Director,
                        movie.CastMembers.Select(c => c.Name),
                        movie.Overview);
                }
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Non-critical startup backfill.
        }

        await SyncIdentitySequencesAsync(db, cancellationToken);
    }

    /// <summary>
    /// Marks the pending migrations as applied when the schema was created outside of EF Core
    /// (for example with the SQL script in <c>database/postgresql</c>). Without this the
    /// migrator would try to recreate tables that already exist.
    /// </summary>
    public static async Task BaselineMigrationsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return;

        var tableCount = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(1) AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name <> '__EFMigrationsHistory'")
            .SingleAsync(cancellationToken);

        if (tableCount == 0)
        {
            // An empty database is created by the migrator itself.
            return;
        }

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0) return;

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" character varying(150) NOT NULL, \"ProductVersion\" character varying(32) NOT NULL, CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY (\"MigrationId\"));",
            cancellationToken);

        var productVersion = GetEfProductVersion();
        foreach (var migrationId in pending)
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1}) ON CONFLICT DO NOTHING;",
                new object[] { migrationId, productVersion },
                cancellationToken);
        }
    }

    private static string GetEfProductVersion()
    {
        var assembly = typeof(DbContext).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "10.0.0";
    }

    /// <summary>
    /// The catalog is seeded with explicit primary keys. PostgreSQL identity sequences are not
    /// advanced by an explicit insert, so the sequences are re-synced with the current maximum
    /// value; otherwise a later insert that relies on the database to pick the id would collide
    /// with a seeded row.
    /// </summary>
    public static async Task SyncIdentitySequencesAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return;

        await db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE
  r RECORD;
  v_max bigint;
  v_seq text;
BEGIN
  FOR r IN
    SELECT c.table_name, c.column_name
      FROM information_schema.columns c
     WHERE c.table_schema = current_schema()
       AND c.is_identity = 'YES'
  LOOP
    v_seq := pg_get_serial_sequence(format('%I', r.table_name), r.column_name);
    IF v_seq IS NULL THEN
      CONTINUE;
    END IF;
    EXECUTE format('SELECT COALESCE(MAX(%I), 0) FROM %I', r.column_name, r.table_name) INTO v_max;
    IF v_max > 0 THEN
      EXECUTE format('SELECT setval(%L, %L, true)', v_seq, v_max);
    END IF;
  END LOOP;
END $$;", cancellationToken);
    }

    /// <summary>
    /// Installs the PostgreSQL full-text search index: a <c>tsvector</c> column on
    /// <c>movies</c>, a normalization function, the triggers that keep the vector in sync and
    /// the GIN index that makes the lookup fast.
    /// </summary>
    public static async Task EnsurePostgresSearchIndexAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return;

        var moviesTableExists = await CheckTableExistsAsync(db, true, "movies", cancellationToken);
        if (!moviesTableExists) return;

        // The unaccent extension folds diacritics; it is a trusted extension since PostgreSQL 13,
        // but a managed instance may still refuse to install it. The normalization function
        // therefore checks for the extension at runtime and degrades gracefully.
        try
        {
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS unaccent;", cancellationToken);
        }
        catch
        {
            // The fallback normalization keeps the index working without the extension.
        }

        await db.Database.ExecuteSqlRawAsync(@"CREATE OR REPLACE FUNCTION webxemphim_normalize(p_text text)
RETURNS text LANGUAGE plpgsql STABLE PARALLEL SAFE AS $$
DECLARE
  v_text text;
BEGIN
  v_text := coalesce(p_text, '');
  IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'unaccent') THEN
    v_text := public.unaccent(v_text);
  END IF;
  RETURN lower(regexp_replace(
           replace(replace(v_text, U&'\0110', 'D'), U&'\0111', 'd'),
           '\s+', ' ', 'g'));
END;
$$;", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(@"CREATE OR REPLACE FUNCTION webxemphim_movie_search_vector(p_movie_id integer)
RETURNS tsvector LANGUAGE plpgsql STABLE AS $$
DECLARE
  v_title text;
  v_original_title text;
  v_director text;
  v_overview text;
  v_cast text;
BEGIN
  SELECT m.""Title"", m.""OriginalTitle"", m.""Director"", m.""Overview""
    INTO v_title, v_original_title, v_director, v_overview
    FROM movies m
   WHERE m.""Id"" = p_movie_id;

  IF NOT FOUND THEN
    RETURN NULL;
  END IF;

  SELECT string_agg(c.""Name"", ' ' ORDER BY c.""Position"") INTO v_cast
    FROM cast_members c
   WHERE c.""MovieId"" = p_movie_id;

  RETURN setweight(to_tsvector('simple', webxemphim_normalize(v_title)), 'A')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_original_title)), 'B')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_director)), 'C')
      || setweight(to_tsvector('simple', webxemphim_normalize(coalesce(v_cast, ''))), 'D')
      || setweight(to_tsvector('simple', webxemphim_normalize(v_overview)), 'D');
END;
$$;", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(@"CREATE OR REPLACE FUNCTION webxemphim_refresh_movie_search_vector()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  UPDATE movies m
     SET ""search_vector"" = webxemphim_movie_search_vector(m.""Id"")
   WHERE m.""Id"" = NEW.""Id"";
  RETURN NULL;
END;
$$;", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(@"CREATE OR REPLACE FUNCTION webxemphim_refresh_movie_search_vector_for_cast()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
  v_movie_id integer;
BEGIN
  v_movie_id := COALESCE(NEW.""MovieId"", OLD.""MovieId"");
  UPDATE movies m
     SET ""search_vector"" = webxemphim_movie_search_vector(m.""Id"")
   WHERE m.""Id"" = v_movie_id;
  RETURN NULL;
END;
$$;", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE movies ADD COLUMN IF NOT EXISTS \"search_vector\" tsvector NULL;", cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE OR REPLACE TRIGGER trg_movies_search_vector AFTER INSERT OR UPDATE OF \"Title\", \"OriginalTitle\", \"Director\", \"Overview\" ON movies FOR EACH ROW EXECUTE FUNCTION webxemphim_refresh_movie_search_vector();",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE OR REPLACE TRIGGER trg_cast_movies_search_vector AFTER INSERT OR UPDATE OR DELETE ON cast_members FOR EACH ROW EXECUTE FUNCTION webxemphim_refresh_movie_search_vector_for_cast();",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_movies_SearchVector\" ON movies USING GIN (\"search_vector\");",
            cancellationToken);

        await SyncMovieSearchIndexAsync(db, cancellationToken);
    }

    /// <summary>
    /// Repairs text that was stored with the wrong code page (UTF-8 bytes decoded as
    /// Windows-1252). Some seeded/demo content was written that way, which made titles such as
    /// "Mắt tháng" render as "Má»™t thÃ¡ng" on every page.
    /// </summary>
    public static async Task<int> RepairMojibakeTextAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var changed = 0;

        var plans = await db.VipPlans.ToListAsync(cancellationToken);
        foreach (var plan in plans)
        {
            var name = MojibakeRepair.Repair(plan.Name);
            var description = MojibakeRepair.Repair(plan.Description);
            if (!string.Equals(name, plan.Name, StringComparison.Ordinal)) { plan.Name = name ?? plan.Name; changed++; }
            if (!string.Equals(description, plan.Description, StringComparison.Ordinal)) { plan.Description = description ?? plan.Description; changed++; }
        }

        var genres = await db.Genres.ToListAsync(cancellationToken);
        foreach (var genre in genres)
        {
            var name = MojibakeRepair.Repair(genre.Name);
            if (!string.Equals(name, genre.Name, StringComparison.Ordinal)) { genre.Name = name ?? genre.Name; changed++; }
        }

        var notifications = await db.Notifications.ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            var title = MojibakeRepair.Repair(notification.Title);
            var copy = MojibakeRepair.Repair(notification.Copy);
            if (!string.Equals(title, notification.Title, StringComparison.Ordinal)) { notification.Title = title ?? notification.Title; changed++; }
            if (!string.Equals(copy, notification.Copy, StringComparison.Ordinal)) { notification.Copy = copy ?? notification.Copy; changed++; }
        }

        var reviews = await db.Reviews.ToListAsync(cancellationToken);
        foreach (var review in reviews)
        {
            var comment = MojibakeRepair.Repair(review.Comment);
            var reply = MojibakeRepair.Repair(review.AdminReply);
            if (!string.Equals(comment, review.Comment, StringComparison.Ordinal)) { review.Comment = comment ?? review.Comment; changed++; }
            if (!string.Equals(reply, review.AdminReply, StringComparison.Ordinal)) { review.AdminReply = reply; changed++; }
        }

        var movies = await db.Movies.Include(movie => movie.CastMembers).ToListAsync(cancellationToken);
        foreach (var movie in movies)
        {
            var title = MojibakeRepair.Repair(movie.Title);
            var originalTitle = MojibakeRepair.Repair(movie.OriginalTitle);
            var overview = MojibakeRepair.Repair(movie.Overview);
            var director = MojibakeRepair.Repair(movie.Director);
            var country = MojibakeRepair.Repair(movie.Country);
            var movieChanged = false;

            if (!string.Equals(title, movie.Title, StringComparison.Ordinal)) { movie.Title = title ?? movie.Title; movieChanged = true; }
            if (!string.Equals(originalTitle, movie.OriginalTitle, StringComparison.Ordinal)) { movie.OriginalTitle = originalTitle ?? movie.OriginalTitle; movieChanged = true; }
            if (!string.Equals(overview, movie.Overview, StringComparison.Ordinal)) { movie.Overview = overview ?? movie.Overview; movieChanged = true; }
            if (!string.Equals(director, movie.Director, StringComparison.Ordinal)) { movie.Director = director ?? movie.Director; movieChanged = true; }
            if (!string.Equals(country, movie.Country, StringComparison.Ordinal)) { movie.Country = country ?? movie.Country; movieChanged = true; }

            foreach (var member in movie.CastMembers)
            {
                var name = MojibakeRepair.Repair(member.Name);
                if (!string.Equals(name, member.Name, StringComparison.Ordinal)) { member.Name = name ?? member.Name; movieChanged = true; }
            }

            if (movieChanged)
            {
                changed++;
                // The normalized search text is derived from the (now repaired) movie fields.
                movie.SearchText = MovieSearchTextBuilder.Build(
                    movie.Title,
                    movie.OriginalTitle,
                    movie.Director,
                    movie.CastMembers.Select(c => c.Name),
                    movie.Overview);
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    /// <summary>
    /// Rewrites legacy seeded movie artwork URLs (<c>/images/movies/movie-N-poster.jpg</c>) to the
    /// WebP variants that are shipped with the client. Only the deterministic seeded pattern is
    /// touched, so custom artwork uploaded by an administrator is never modified.
    /// </summary>
    public static async Task<int> MigrateLegacyMovieImageUrlsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            @"^(/images/movies/movie-\d+-(?:poster|backdrop))\.jpg$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        var changed = 0;

        var movies = await db.Movies
            .Where(item => item.PosterUrl.EndsWith(".jpg") || item.BackdropUrl.EndsWith(".jpg"))
            .ToListAsync(cancellationToken);
        foreach (var movie in movies)
        {
            var poster = pattern.Replace(movie.PosterUrl, "$1.webp");
            var backdrop = pattern.Replace(movie.BackdropUrl, "$1.webp");
            if (!string.Equals(poster, movie.PosterUrl, StringComparison.Ordinal) ||
                !string.Equals(backdrop, movie.BackdropUrl, StringComparison.Ordinal))
            {
                movie.PosterUrl = poster;
                movie.BackdropUrl = backdrop;
                changed++;
            }
        }

        var episodes = await db.Episodes
            .Where(item => item.ThumbnailUrl != null && item.ThumbnailUrl.EndsWith(".jpg"))
            .ToListAsync(cancellationToken);
        foreach (var episode in episodes)
        {
            var thumbnail = pattern.Replace(episode.ThumbnailUrl ?? string.Empty, "$1.webp");
            if (!string.Equals(thumbnail, episode.ThumbnailUrl, StringComparison.Ordinal))
            {
                episode.ThumbnailUrl = thumbnail;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    private static async Task EnsureSearchTextIndexAsync(AppDbContext db, bool isPostgres, CancellationToken cancellationToken)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var shouldClose = false;
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                shouldClose = true;
            }

            try
            {
                await using var checkCmd = connection.CreateCommand();
                if (isPostgres)
                {
                    checkCmd.CommandText = "SELECT COUNT(1) FROM pg_indexes WHERE schemaname = current_schema() AND tablename = 'movies' AND indexname = 'IX_movies_SearchText'";
                }
                else
                {
                    checkCmd.CommandText = "SELECT COUNT(1) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='movies' AND INDEX_NAME='IX_movies_SearchText'";
                }

                var existing = await checkCmd.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && Convert.ToInt32(existing) > 0) return;

                await db.Database.ExecuteSqlRawAsync(
                    isPostgres
                        ? "CREATE INDEX IF NOT EXISTS \"IX_movies_SearchText\" ON movies (\"SearchText\");"
                        : "CREATE INDEX IX_movies_SearchText ON movies (SearchText(191));",
                    cancellationToken);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
        }
        catch
        {
            // Index creation is an optimization; search still works without it.
        }
    }

    private static async Task<bool> CheckTableExistsAsync(
        AppDbContext db,
        bool isPostgres,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            shouldClose = true;
        }

        try
        {
            await using var checkTableCmd = connection.CreateCommand();
            if (isPostgres)
            {
                checkTableCmd.CommandText = "SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @tableName";
            }
            else
            {
                checkTableCmd.CommandText = "SELECT COUNT(1) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName";
            }

            var param = checkTableCmd.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = tableName;
            checkTableCmd.Parameters.Add(param);

            var countResult = await checkTableCmd.ExecuteScalarAsync(cancellationToken);
            return countResult is not null && Convert.ToInt32(countResult) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task EnsureColumnExistsAsync(
        AppDbContext db,
        bool isPostgres,
        string tableName,
        string columnName,
        string postgresDefinition,
        string mysqlDefinition,
        CancellationToken cancellationToken)
    {
        var tableExists = await CheckTableExistsAsync(db, isPostgres, tableName, cancellationToken);
        if (!tableExists)
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            shouldClose = true;
        }

        try
        {
            // Check if column exists
            var columnExists = false;
            if (isPostgres)
            {
                await using var checkColCmd = connection.CreateCommand();
                checkColCmd.CommandText = "SELECT COUNT(1) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND (column_name = @colName OR column_name = LOWER(@colName))";
                var pTable = checkColCmd.CreateParameter();
                pTable.ParameterName = "@tableName";
                pTable.Value = tableName;
                checkColCmd.Parameters.Add(pTable);

                var pCol = checkColCmd.CreateParameter();
                pCol.ParameterName = "@colName";
                pCol.Value = columnName;
                checkColCmd.Parameters.Add(pCol);

                var countResult = await checkColCmd.ExecuteScalarAsync(cancellationToken);
                columnExists = countResult is not null && Convert.ToInt32(countResult) > 0;
            }
            else
            {
                await using var checkColCmd = connection.CreateCommand();
                checkColCmd.CommandText = "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = @colName";
                var pTable = checkColCmd.CreateParameter();
                pTable.ParameterName = "@tableName";
                pTable.Value = tableName;
                checkColCmd.Parameters.Add(pTable);

                var pCol = checkColCmd.CreateParameter();
                pCol.ParameterName = "@colName";
                pCol.Value = columnName;
                checkColCmd.Parameters.Add(pCol);

                var countResult = await checkColCmd.ExecuteScalarAsync(cancellationToken);
                columnExists = countResult is not null && Convert.ToInt32(countResult) > 0;
            }

            if (!columnExists)
            {
                var def = isPostgres ? postgresDefinition : mysqlDefinition;
#pragma warning disable EF1002
                var alterSql = isPostgres
                    ? $"ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS \"{columnName}\" {def};"
                    : $"ALTER TABLE {tableName} ADD COLUMN {columnName} {def};";
                await db.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
#pragma warning restore EF1002
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Rebuilds the PostgreSQL search vectors for every movie. The triggers keep them in sync
    /// afterwards; this is the recovery path for rows written before the index existed.
    /// </summary>
    public static async Task SyncMovieSearchIndexAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return;

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE movies m SET \"search_vector\" = webxemphim_movie_search_vector(m.\"Id\");",
            cancellationToken);
    }

    /// <summary>
    /// Recomputes the search vector of a single movie.
    /// </summary>
    public static async Task RefreshMovieSearchIndexAsync(AppDbContext db, int movieId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql()) return;

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE movies m SET \"search_vector\" = webxemphim_movie_search_vector(m.\"Id\") WHERE m.\"Id\" = {0};",
            new object[] { movieId },
            cancellationToken);
    }
}