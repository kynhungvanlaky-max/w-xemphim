$file = "src/Server/WebXemPhim.Server/Data/DatabaseSchemaHelper.cs"
$content = [System.IO.File]::ReadAllText($file)
$content = $content -replace "CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges \(UserId\);", "await SafeCreateIndexAsync(db, ""CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges (UserId);"", cancellationToken);"

# Replace the whole block:
$oldBlock = @"
        else
        {
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_mfa_verified_sessions_UserId ON mfa_verified_sessions (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_security_audit_events_CreatedAtUtc ON security_audit_events (CreatedAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_security_audit_events_UserId ON security_audit_events (UserId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_playback_issues_UserId_MovieId ON playback_issues (UserId, MovieId);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_playback_telemetry_CreatedAtUtc ON playback_telemetry (CreatedAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_playback_telemetry_EventType_CreatedAtUtc ON playback_telemetry (EventType, CreatedAtUtc);", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IX_playback_telemetry_EpisodeId ON playback_telemetry (EpisodeId);", cancellationToken);
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
"@

$newBlock = @"
        else
        {
            if (canCreateDependentTables)
            {
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges (UserId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_mfa_verified_sessions_UserId ON mfa_verified_sessions (UserId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_security_audit_events_CreatedAtUtc ON security_audit_events (CreatedAtUtc);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_security_audit_events_UserId ON security_audit_events (UserId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_playback_issues_UserId_MovieId ON playback_issues (UserId, MovieId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_playback_telemetry_CreatedAtUtc ON playback_telemetry (CreatedAtUtc);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_playback_telemetry_EventType_CreatedAtUtc ON playback_telemetry (EventType, CreatedAtUtc);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_playback_telemetry_EpisodeId ON playback_telemetry (EpisodeId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_user_library_MovieId ON user_library (MovieId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_notifications_UserId_Read ON notifications (UserId, ``Read``);", cancellationToken).Replace("``", "`");
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_watch_progress_MovieId ON watch_progress (MovieId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_audit_logs_TimestampUtc ON audit_logs (TimestampUtc);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_audit_logs_AdminEmail ON audit_logs (AdminEmail);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_audit_logs_EntityType_EntityId ON audit_logs (EntityType, EntityId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE UNIQUE INDEX IX_active_streams_SessionId ON active_streams (SessionId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_active_streams_UserId ON active_streams (UserId);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_active_streams_ExpiresAtUtc ON active_streams (ExpiresAtUtc);", cancellationToken);
                await SafeCreateIndexAsync(db, "CREATE INDEX IX_active_streams_UserId_IsActive ON active_streams (UserId, IsActive, ExpiresAtUtc);", cancellationToken);
            }
        }
"@

$helper = @"
    private static async Task SafeCreateIndexAsync(AppDbContext db, string sql, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("Duplicate key", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            // Index already exists, safe to ignore.
        }
    }

    private static async Task<bool> CheckTableExistsAsync(
"@

$cNorm = $content.Replace("`r`n", "`n")
$oldNorm = $oldBlock.Replace("`r`n", "`n")
$newNorm = $newBlock.Replace("`r`n", "`n")
$helperNorm = $helper.Replace("`r`n", "`n")

if ($cNorm.Contains($oldNorm)) {
    $cNorm = $cNorm.Replace($oldNorm, $newNorm)
    $cNorm = $cNorm.Replace("    private static async Task<bool> CheckTableExistsAsync(", $helperNorm)
    [System.IO.File]::WriteAllText($file, $cNorm.Replace("`n", "`r`n"), [System.Text.Encoding]::UTF8)
    Write-Host "PATCH_SUCCESS"
} else {
    Write-Host "OLD_BLOCK_NOT_FOUND"
}
