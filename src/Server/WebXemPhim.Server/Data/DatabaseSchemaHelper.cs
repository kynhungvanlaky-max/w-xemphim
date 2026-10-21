using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Shared.Utils;

namespace WebXemPhim.Server.Data;

public static class DatabaseSchemaHelper
{
    public static async Task EnsureTablesCreatedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var isSqlite = db.Database.IsSqlite();

        var userSecuritySql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS user_security (UserId TEXT NOT NULL PRIMARY KEY, IsAdmin INTEGER NOT NULL DEFAULT 0, PasswordIterations INTEGER NOT NULL DEFAULT 100000, FailedLoginAttempts INTEGER NOT NULL DEFAULT 0, LockedUntilUtc TEXT NULL, Version INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS user_security (UserId char(36) NOT NULL PRIMARY KEY, IsAdmin tinyint(1) NOT NULL DEFAULT 0, PasswordIterations int NOT NULL DEFAULT 100000, FailedLoginAttempts int NOT NULL DEFAULT 0, LockedUntilUtc datetime(6) NULL, Version bigint NOT NULL DEFAULT 0, CONSTRAINT FK_user_security_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var mfaCredentialsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS mfa_credentials (UserId TEXT NOT NULL PRIMARY KEY, ProtectedSecret TEXT NOT NULL, RecoveryCodeHashes TEXT NOT NULL, Enabled INTEGER NOT NULL DEFAULT 0, EnabledAtUtc TEXT NULL, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS mfa_credentials (UserId char(36) NOT NULL PRIMARY KEY, ProtectedSecret varchar(1000) NOT NULL, RecoveryCodeHashes varchar(2000) NOT NULL, Enabled tinyint(1) NOT NULL DEFAULT 0, EnabledAtUtc datetime(6) NULL, CONSTRAINT FK_mfa_credentials_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var mfaChallengesSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS mfa_challenges (TokenHash TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, ExpiresAtUtc TEXT NOT NULL, FailedAttempts INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS mfa_challenges (TokenHash varchar(64) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, ExpiresAtUtc datetime(6) NOT NULL, FailedAttempts int NOT NULL DEFAULT 0, INDEX IX_mfa_challenges_UserId (UserId), CONSTRAINT FK_mfa_challenges_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var mfaVerifiedSessionsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS mfa_verified_sessions (TokenHash TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, VerifiedAtUtc TEXT NOT NULL, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS mfa_verified_sessions (TokenHash varchar(64) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, VerifiedAtUtc datetime(6) NOT NULL, INDEX IX_mfa_verified_sessions_UserId (UserId), CONSTRAINT FK_mfa_verified_sessions_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var securityAuditSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS security_audit_events (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NULL, EventType TEXT NOT NULL, Success INTEGER NOT NULL, Detail TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE SET NULL);"
            : "CREATE TABLE IF NOT EXISTS security_audit_events (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NULL, EventType varchar(80) NOT NULL, Success tinyint(1) NOT NULL, Detail varchar(240) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_security_audit_events_CreatedAtUtc (CreatedAtUtc), INDEX IX_security_audit_events_UserId (UserId), CONSTRAINT FK_security_audit_events_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE SET NULL);";

        var playbackIssuesSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS playback_issues (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, EpisodeId INTEGER NOT NULL, Description TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE, FOREIGN KEY (EpisodeId) REFERENCES episodes(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS playback_issues (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NOT NULL, Description varchar(800) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_playback_issues_UserId_MovieId (UserId, MovieId), CONSTRAINT FK_playback_issues_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_playback_issues_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE, CONSTRAINT FK_playback_issues_episodes_EpisodeId FOREIGN KEY (EpisodeId) REFERENCES episodes(Id) ON DELETE CASCADE);";

        var userLibrarySql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS user_library (UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, AddedAt TEXT NOT NULL, PRIMARY KEY (UserId, MovieId), FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS user_library (UserId char(36) NOT NULL, MovieId int NOT NULL, AddedAt datetime(6) NOT NULL, PRIMARY KEY (UserId, MovieId), INDEX IX_user_library_MovieId (MovieId), CONSTRAINT FK_user_library_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_user_library_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE);";

        var userSettingsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS user_settings (UserId TEXT NOT NULL PRIMARY KEY, DefaultQuality TEXT NOT NULL, AutoplayNext INTEGER NOT NULL, NotifyNewEpisodes INTEGER NOT NULL, Language TEXT NOT NULL DEFAULT 'vi', UpdatedAtUtc TEXT NOT NULL, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS user_settings (UserId char(36) NOT NULL PRIMARY KEY, DefaultQuality varchar(32) NOT NULL, AutoplayNext tinyint(1) NOT NULL, NotifyNewEpisodes tinyint(1) NOT NULL, Language varchar(10) NOT NULL DEFAULT 'vi', UpdatedAtUtc datetime(6) NOT NULL, CONSTRAINT FK_user_settings_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var notificationsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS notifications (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, Title TEXT NOT NULL, Copy TEXT NOT NULL, Icon TEXT NOT NULL, ActionUrl TEXT, \"Read\" INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS notifications (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, Title varchar(200) NOT NULL, Copy varchar(1000) NOT NULL, Icon varchar(32) NOT NULL, ActionUrl varchar(500) NULL, `Read` tinyint(1) NOT NULL, CreatedAtUtc datetime(6) NOT NULL, INDEX IX_notifications_UserId_Read (UserId, `Read`), CONSTRAINT FK_notifications_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE);";

        var watchProgressSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS watch_progress (UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, EpisodeId INTEGER NULL, EpisodeNumber INTEGER NOT NULL, ProgressSeconds INTEGER NOT NULL, DurationSeconds INTEGER NOT NULL, LastWatchedAtUtc TEXT NOT NULL, PRIMARY KEY (UserId, MovieId), FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE);"
            : "CREATE TABLE IF NOT EXISTS watch_progress (UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NULL, EpisodeNumber int NOT NULL, ProgressSeconds int NOT NULL, DurationSeconds int NOT NULL, LastWatchedAtUtc datetime(6) NOT NULL, PRIMARY KEY (UserId, MovieId), INDEX IX_watch_progress_MovieId (MovieId), CONSTRAINT FK_watch_progress_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_watch_progress_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE);";

        var auditLogsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS audit_logs (Id TEXT NOT NULL PRIMARY KEY, TimestampUtc TEXT NOT NULL, AdminEmail TEXT NOT NULL, Action TEXT NOT NULL, EntityType TEXT NOT NULL, EntityId TEXT NOT NULL, Summary TEXT NOT NULL, DetailsJson TEXT NULL);"
            : "CREATE TABLE IF NOT EXISTS audit_logs (Id char(36) NOT NULL PRIMARY KEY, TimestampUtc datetime(6) NOT NULL, AdminEmail varchar(200) NOT NULL, Action varchar(80) NOT NULL, EntityType varchar(80) NOT NULL, EntityId varchar(120) NOT NULL, Summary varchar(500) NOT NULL, DetailsJson text NULL, INDEX IX_audit_logs_TimestampUtc (TimestampUtc), INDEX IX_audit_logs_AdminEmail (AdminEmail), INDEX IX_audit_logs_EntityType_EntityId (EntityType, EntityId));";

        var vipPlansSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS vip_plans (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL, PriceVnd NUMERIC NOT NULL, DurationDays INTEGER NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1, DisplayOrder INTEGER NOT NULL DEFAULT 0);"
            : "CREATE TABLE IF NOT EXISTS vip_plans (Id varchar(32) NOT NULL PRIMARY KEY, Name varchar(80) NOT NULL, Description varchar(500) NOT NULL, PriceVnd decimal(12,2) NOT NULL, DurationDays int NOT NULL, IsActive tinyint(1) NOT NULL DEFAULT 1, DisplayOrder int NOT NULL DEFAULT 0);";

        var couponsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS coupons (Code TEXT NOT NULL PRIMARY KEY, DiscountType TEXT NOT NULL, DiscountValue NUMERIC NOT NULL, ValidUntilUtc TEXT NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1);"
            : "CREATE TABLE IF NOT EXISTS coupons (Code varchar(32) NOT NULL PRIMARY KEY, DiscountType varchar(32) NOT NULL, DiscountValue decimal(12,2) NOT NULL, ValidUntilUtc datetime(6) NOT NULL, IsActive tinyint(1) NOT NULL DEFAULT 1);";

        var activeStreamsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS active_streams (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, MovieId INTEGER NOT NULL, EpisodeId INTEGER NULL, SessionId TEXT NOT NULL, ConnectionId TEXT NULL, IpAddress TEXT NULL, DeviceInfo TEXT NULL, StartedAtUtc TEXT NOT NULL, LastHeartbeatUtc TEXT NOT NULL, ExpiresAtUtc TEXT NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1, FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE, FOREIGN KEY (EpisodeId) REFERENCES episodes(Id) ON DELETE SET NULL);"
            : "CREATE TABLE IF NOT EXISTS active_streams (Id char(36) NOT NULL PRIMARY KEY, UserId char(36) NOT NULL, MovieId int NOT NULL, EpisodeId int NULL, SessionId varchar(64) NOT NULL, ConnectionId varchar(64) NULL, IpAddress varchar(45) NULL, DeviceInfo varchar(256) NULL, StartedAtUtc datetime(6) NOT NULL, LastHeartbeatUtc datetime(6) NOT NULL, ExpiresAtUtc datetime(6) NOT NULL, IsActive tinyint(1) NOT NULL DEFAULT 1, UNIQUE INDEX IX_active_streams_SessionId (SessionId), INDEX IX_active_streams_UserId (UserId), INDEX IX_active_streams_ExpiresAtUtc (ExpiresAtUtc), INDEX IX_active_streams_User_Active_Exp (UserId, IsActive, ExpiresAtUtc), CONSTRAINT FK_active_streams_users_UserId FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE, CONSTRAINT FK_active_streams_movies_MovieId FOREIGN KEY (MovieId) REFERENCES movies(Id) ON DELETE CASCADE, CONSTRAINT FK_active_streams_episodes_EpisodeId FOREIGN KEY (EpisodeId) REFERENCES episodes(Id) ON DELETE SET NULL);";

        var maintenanceFlagsSql = isSqlite
            ? "CREATE TABLE IF NOT EXISTS app_maintenance_flags (Flag TEXT NOT NULL PRIMARY KEY, CompletedAtUtc TEXT NOT NULL);"
            : "CREATE TABLE IF NOT EXISTS app_maintenance_flags (Flag varchar(120) NOT NULL PRIMARY KEY, CompletedAtUtc datetime(6) NOT NULL);";

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
        await db.Database.ExecuteSqlRawAsync(auditLogsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(vipPlansSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(couponsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(activeStreamsSql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(maintenanceFlagsSql, cancellationToken);

        // Ensure secondary indexes in SQLite
        if (isSqlite)
        {
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_mfa_challenges_UserId ON mfa_challenges (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_mfa_verified_sessions_UserId ON mfa_verified_sessions (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_security_audit_events_CreatedAtUtc ON security_audit_events (CreatedAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_security_audit_events_UserId ON security_audit_events (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_playback_issues_UserId_MovieId ON playback_issues (UserId, MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_user_library_MovieId ON user_library (MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_notifications_UserId_Read ON notifications (UserId, \"Read\");", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_watch_progress_MovieId ON watch_progress (MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_audit_logs_TimestampUtc ON audit_logs (TimestampUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_audit_logs_AdminEmail ON audit_logs (AdminEmail);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_audit_logs_EntityType_EntityId ON audit_logs (EntityType, EntityId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_active_streams_SessionId ON active_streams (SessionId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_active_streams_UserId ON active_streams (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_active_streams_ExpiresAtUtc ON active_streams (ExpiresAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_active_streams_UserId_IsActive ON active_streams (UserId, IsActive, ExpiresAtUtc);", cancellationToken);

            // SQLite FTS5 Virtual Table for fast full text search with diacritic normalization
            await db.Database.ExecuteSqlRawAsync(@"
CREATE VIRTUAL TABLE IF NOT EXISTS movies_fts USING fts5(
    movie_id UNINDEXED,
    title,
    original_title,
    director,
    cast_members,
    normalized_content,
    tokenize='unicode61 remove_diacritics 2'
);", cancellationToken);

            var moviesTableExists = await CheckTableExistsAsync(db, isSqlite, "movies", cancellationToken);
            var castTableExists = await CheckTableExistsAsync(db, isSqlite, "cast_members", cancellationToken);
            if (moviesTableExists && castTableExists)
            {
                // Triggers are recreated (DROP + CREATE) so schema upgrades always pick up
                // the latest definition, including cast member synchronization.
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_movies_fts_delete;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_movies_fts_insert;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_movies_fts_update;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_movies_fts_search_update;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_cast_movies_fts_insert;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_cast_movies_fts_update;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_cast_movies_fts_delete;", cancellationToken);

                // The normalized_content column stores the pre-normalized SearchText.
                // SQLite's built-in tokenizer cannot fold the Vietnamese "Đ/đ" letter, so the
                // normalized column is the only place where "Hành Động" becomes "hanh dong".
                const string normalizedFallback = @"
        COALESCE(NULLIF(new.SearchText, ''),
            new.Title || ' ' || COALESCE(new.OriginalTitle, '') || ' ' || COALESCE(new.Director, '') || ' ' ||
            COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.Id), '') || ' ' || COALESCE(new.Overview, ''))";

                await db.Database.ExecuteSqlRawAsync(@"
CREATE TRIGGER trg_movies_fts_delete AFTER DELETE ON movies
BEGIN
    DELETE FROM movies_fts WHERE movie_id = old.Id;
END;", cancellationToken);

                await db.Database.ExecuteSqlRawAsync($@"
CREATE TRIGGER trg_movies_fts_insert AFTER INSERT ON movies
BEGIN
    INSERT INTO movies_fts(movie_id, title, original_title, director, cast_members, normalized_content)
    VALUES (
        new.Id,
        new.Title,
        new.OriginalTitle,
        new.Director,
        COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.Id), ''),
        {normalizedFallback}
    );
END;", cancellationToken);

                await db.Database.ExecuteSqlRawAsync($@"
CREATE TRIGGER trg_movies_fts_update AFTER UPDATE ON movies
BEGIN
    UPDATE movies_fts
    SET title = new.Title,
        original_title = new.OriginalTitle,
        director = new.Director,
        cast_members = COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.Id), ''),
        normalized_content = {normalizedFallback}
    WHERE movie_id = old.Id;
END;", cancellationToken);

                // Keeps the FTS row aligned when SearchText is (re)computed after the insert.
                await db.Database.ExecuteSqlRawAsync($@"
CREATE TRIGGER trg_movies_fts_search_update AFTER UPDATE OF SearchText ON movies
WHEN old.SearchText IS NOT new.SearchText
BEGIN
    UPDATE movies_fts
    SET normalized_content = {normalizedFallback}
    WHERE movie_id = new.Id;
END;", cancellationToken);

                // Keep the FTS index in sync when the cast list changes independently of the movie row.
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TRIGGER trg_cast_movies_fts_insert AFTER INSERT ON cast_members
BEGIN
    UPDATE movies_fts
    SET cast_members = COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.MovieId), ''),
        normalized_content = COALESCE(NULLIF((SELECT SearchText FROM movies WHERE Id = new.MovieId), ''),
            COALESCE((SELECT Title || ' ' || COALESCE(OriginalTitle, '') || ' ' || COALESCE(Director, '') || ' ' || COALESCE(Overview, '') FROM movies WHERE Id = new.MovieId), '') || ' ' ||
            COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.MovieId), ''))
    WHERE movie_id = new.MovieId;
END;", cancellationToken);

                await db.Database.ExecuteSqlRawAsync(@"
CREATE TRIGGER trg_cast_movies_fts_update AFTER UPDATE ON cast_members
BEGIN
    UPDATE movies_fts
    SET cast_members = COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.MovieId), ''),
        normalized_content = COALESCE(NULLIF((SELECT SearchText FROM movies WHERE Id = new.MovieId), ''),
            COALESCE((SELECT Title || ' ' || COALESCE(OriginalTitle, '') || ' ' || COALESCE(Director, '') || ' ' || COALESCE(Overview, '') FROM movies WHERE Id = new.MovieId), '') || ' ' ||
            COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = new.MovieId), ''))
    WHERE movie_id = new.MovieId;
END;", cancellationToken);

                await db.Database.ExecuteSqlRawAsync(@"
CREATE TRIGGER trg_cast_movies_fts_delete AFTER DELETE ON cast_members
BEGIN
    UPDATE movies_fts
    SET cast_members = COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = old.MovieId), ''),
        normalized_content = COALESCE(NULLIF((SELECT SearchText FROM movies WHERE Id = old.MovieId), ''),
            COALESCE((SELECT Title || ' ' || COALESCE(OriginalTitle, '') || ' ' || COALESCE(Director, '') || ' ' || COALESCE(Overview, '') FROM movies WHERE Id = old.MovieId), '') || ' ' ||
            COALESCE((SELECT GROUP_CONCAT(Name, ', ') FROM cast_members WHERE MovieId = old.MovieId), ''))
    WHERE movie_id = old.MovieId;
END;", cancellationToken);
            }
        }

        // Safe column migrations via metadata inspection
        await EnsureColumnExistsAsync(db, isSqlite, "reviews", "AdminReply", "TEXT NULL", "text NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isSqlite, "reviews", "AdminRepliedAtUtc", "TEXT NULL", "datetime(6) NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isSqlite, "reviews", "IsReported", "INTEGER NOT NULL DEFAULT 0", "tinyint(1) NOT NULL DEFAULT 0", cancellationToken);

        await EnsureColumnExistsAsync(db, isSqlite, "payments", "TransactionReference", "TEXT NULL", "varchar(120) NULL", cancellationToken);
        await EnsureColumnExistsAsync(db, isSqlite, "payments", "Note", "TEXT NULL", "varchar(500) NULL", cancellationToken);

        await EnsureColumnExistsAsync(db, isSqlite, "users", "EmailLookupHash", "TEXT NULL", "varchar(64) NULL", cancellationToken);

        await EnsureColumnExistsAsync(db, isSqlite, "user_settings", "Language", "TEXT NOT NULL DEFAULT 'vi'", "varchar(10) NOT NULL DEFAULT 'vi'", cancellationToken);

        await EnsureColumnExistsAsync(db, isSqlite, "movies", "SearchText", "TEXT NULL", "longtext NULL", cancellationToken);
        await EnsureSearchTextIndexAsync(db, isSqlite, cancellationToken);

        if (isSqlite)
        {
            var usersTableExists = await CheckTableExistsAsync(db, isSqlite, "users", cancellationToken);
            if (usersTableExists)
            {
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_users_EmailLookupHash ON users (EmailLookupHash);", cancellationToken);
            }

            var paymentsTableExists = await CheckTableExistsAsync(db, isSqlite, "payments", cancellationToken);
            if (paymentsTableExists)
            {
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_payments_TransactionReference ON payments (TransactionReference);", cancellationToken);
            }
        }

        if (isSqlite)
        {
            try
            {
                var moviesTableExists = await CheckTableExistsAsync(db, isSqlite, "movies", cancellationToken);
                if (moviesTableExists)
                {
                    var conn = db.Database.GetDbConnection();
                    var wasOpen = conn.State == System.Data.ConnectionState.Open;
                    if (!wasOpen) await conn.OpenAsync(cancellationToken);
                    try
                    {
                        await using var countCmd = conn.CreateCommand();
                        countCmd.CommandText = "SELECT COUNT(1) FROM movies_fts;";
                        var ftsCountObj = await countCmd.ExecuteScalarAsync(cancellationToken);
                        var ftsCount = ftsCountObj != null ? Convert.ToInt32(ftsCountObj) : 0;
                        var moviesCount = await db.Movies.CountAsync(cancellationToken);
                        if (ftsCount == 0 && moviesCount > 0)
                        {
                            await SyncMoviesFtsAsync(db, cancellationToken);
                        }
                    }
                    finally
                    {
                        if (!wasOpen) await conn.CloseAsync();
                    }
                }
            }
            catch
            {
                // Non-critical startup check
            }
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

    private static async Task EnsureSearchTextIndexAsync(AppDbContext db, bool isSqlite, CancellationToken cancellationToken)
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
                if (isSqlite)
                {
                    checkCmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='index' AND name='IX_movies_SearchText';";
                }
                else
                {
                    checkCmd.CommandText = "SELECT COUNT(1) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='movies' AND INDEX_NAME='IX_movies_SearchText';";
                }

                var existing = await checkCmd.ExecuteScalarAsync(cancellationToken);
                if (existing is not null && Convert.ToInt32(existing) > 0) return;

                await db.Database.ExecuteSqlRawAsync(
                    isSqlite
                        ? "CREATE INDEX IF NOT EXISTS IX_movies_SearchText ON movies (SearchText);"
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
        bool isSqlite,
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
            if (isSqlite)
            {
                checkTableCmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@tableName;";
                var param = checkTableCmd.CreateParameter();
                param.ParameterName = "@tableName";
                param.Value = tableName;
                checkTableCmd.Parameters.Add(param);
            }
            else
            {
                checkTableCmd.CommandText = "SELECT COUNT(1) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;";
                var param = checkTableCmd.CreateParameter();
                param.ParameterName = "@tableName";
                param.Value = tableName;
                checkTableCmd.Parameters.Add(param);
            }

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
        bool isSqlite,
        string tableName,
        string columnName,
        string sqliteDefinition,
        string mysqlDefinition,
        CancellationToken cancellationToken)
    {
        var tableExists = await CheckTableExistsAsync(db, isSqlite, tableName, cancellationToken);
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
            if (isSqlite)
            {
                await using var checkColCmd = connection.CreateCommand();
                checkColCmd.CommandText = $"PRAGMA table_info({tableName});";
                await using var reader = await checkColCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var colName = reader["name"]?.ToString();
                    if (string.Equals(colName, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }
            else
            {
                await using var checkColCmd = connection.CreateCommand();
                checkColCmd.CommandText = "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName AND COLUMN_NAME = @colName;";
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
                var def = isSqlite ? sqliteDefinition : mysqlDefinition;
#pragma warning disable EF1002
                var alterSql = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {def};";
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

    public static async Task SyncMoviesFtsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite()) return;

        var movies = await db.Movies
            .Include(m => m.CastMembers)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var connection = db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            shouldClose = true;
        }

        try
        {
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);

            await using (var clearCmd = connection.CreateCommand())
            {
                clearCmd.Transaction = tx;
                clearCmd.CommandText = "DELETE FROM movies_fts;";
                await clearCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insertCmd = connection.CreateCommand())
            {
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO movies_fts (movie_id, title, original_title, director, cast_members, normalized_content) VALUES (@id, @title, @originalTitle, @director, @cast, @normalized);";

                var pId = insertCmd.CreateParameter(); pId.ParameterName = "@id"; insertCmd.Parameters.Add(pId);
                var pTitle = insertCmd.CreateParameter(); pTitle.ParameterName = "@title"; insertCmd.Parameters.Add(pTitle);
                var pOriginalTitle = insertCmd.CreateParameter(); pOriginalTitle.ParameterName = "@originalTitle"; insertCmd.Parameters.Add(pOriginalTitle);
                var pDirector = insertCmd.CreateParameter(); pDirector.ParameterName = "@director"; insertCmd.Parameters.Add(pDirector);
                var pCast = insertCmd.CreateParameter(); pCast.ParameterName = "@cast"; insertCmd.Parameters.Add(pCast);
                var pNorm = insertCmd.CreateParameter(); pNorm.ParameterName = "@normalized"; insertCmd.Parameters.Add(pNorm);

                foreach (var movie in movies)
                {
                    var castText = string.Join(", ", movie.CastMembers.Select(c => c.Name));
                    var fullContent = $"{movie.Title} {movie.OriginalTitle} {movie.Director} {castText} {movie.Overview}";
                    var normalized = string.IsNullOrWhiteSpace(movie.SearchText)
                        ? VietnameseTextNormalizer.NormalizeForSearch(fullContent)
                        : movie.SearchText;

                    pId.Value = movie.Id;
                    pTitle.Value = movie.Title ?? string.Empty;
                    pOriginalTitle.Value = movie.OriginalTitle ?? string.Empty;
                    pDirector.Value = movie.Director ?? string.Empty;
                    pCast.Value = castText;
                    pNorm.Value = normalized;

                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static async Task IndexSingleMovieAsync(AppDbContext db, int movieId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite()) return;

        var movie = await db.Movies
            .Include(m => m.CastMembers)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == movieId, cancellationToken);

        var connection = db.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            shouldClose = true;
        }

        try
        {
            await using (var delCmd = connection.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM movies_fts WHERE movie_id = @id;";
                var pDelId = delCmd.CreateParameter();
                pDelId.ParameterName = "@id";
                pDelId.Value = movieId;
                delCmd.Parameters.Add(pDelId);
                await delCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (movie != null)
            {
                var castText = string.Join(", ", movie.CastMembers.Select(c => c.Name));
                var fullContent = $"{movie.Title} {movie.OriginalTitle} {movie.Director} {castText} {movie.Overview}";
                var normalized = string.IsNullOrWhiteSpace(movie.SearchText)
                    ? VietnameseTextNormalizer.NormalizeForSearch(fullContent)
                    : movie.SearchText;

                await using var insCmd = connection.CreateCommand();
                insCmd.CommandText = "INSERT INTO movies_fts (movie_id, title, original_title, director, cast_members, normalized_content) VALUES (@id, @title, @originalTitle, @director, @cast, @normalized);";

                var pId = insCmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = movie.Id; insCmd.Parameters.Add(pId);
                var pTitle = insCmd.CreateParameter(); pTitle.ParameterName = "@title"; pTitle.Value = movie.Title ?? string.Empty; insCmd.Parameters.Add(pTitle);
                var pOrig = insCmd.CreateParameter(); pOrig.ParameterName = "@originalTitle"; pOrig.Value = movie.OriginalTitle ?? string.Empty; insCmd.Parameters.Add(pOrig);
                var pDir = insCmd.CreateParameter(); pDir.ParameterName = "@director"; pDir.Value = movie.Director ?? string.Empty; insCmd.Parameters.Add(pDir);
                var pCast = insCmd.CreateParameter(); pCast.ParameterName = "@cast"; pCast.Value = castText; insCmd.Parameters.Add(pCast);
                var pNorm = insCmd.CreateParameter(); pNorm.ParameterName = "@normalized"; pNorm.Value = normalized; insCmd.Parameters.Add(pNorm);

                await insCmd.ExecuteNonQueryAsync(cancellationToken);
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
}
