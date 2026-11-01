$file = "src/Server/WebXemPhim.Server/Data/DatabaseSchemaHelper.cs"
$lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($file))

# Check lines 139..160
if ($lines[139].Trim() -eq "else" -and $lines[140].Trim() -eq "{") {
    $newLines = @(
        "        else"
        "        {"
        "            if (canCreateDependentTables)"
        "            {"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges (UserId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_mfa_verified_sessions_UserId ON mfa_verified_sessions (UserId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_security_audit_events_CreatedAtUtc ON security_audit_events (CreatedAtUtc);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_security_audit_events_UserId ON security_audit_events (UserId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_playback_issues_UserId_MovieId ON playback_issues (UserId, MovieId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_playback_telemetry_CreatedAtUtc ON playback_telemetry (CreatedAtUtc);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_playback_telemetry_EventType_CreatedAtUtc ON playback_telemetry (EventType, CreatedAtUtc);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_playback_telemetry_EpisodeId ON playback_telemetry (EpisodeId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_user_library_MovieId ON user_library (MovieId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_notifications_UserId_Read ON notifications (UserId, ``Read``);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_watch_progress_MovieId ON watch_progress (MovieId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_audit_logs_TimestampUtc ON audit_logs (TimestampUtc);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_audit_logs_AdminEmail ON audit_logs (AdminEmail);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_audit_logs_EntityType_EntityId ON audit_logs (EntityType, EntityId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE UNIQUE INDEX IX_active_streams_SessionId ON active_streams (SessionId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_active_streams_UserId ON active_streams (UserId);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_active_streams_ExpiresAtUtc ON active_streams (ExpiresAtUtc);"", cancellationToken);"
        "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_active_streams_UserId_IsActive ON active_streams (UserId, IsActive, ExpiresAtUtc);"", cancellationToken);"
        "            }"
        "        }"
    )

    # Remove 22 lines starting at 139
    $lines.RemoveRange(139, 22)
    # Insert new lines
    $lines.InsertRange(139, $newLines)

    # Now find CheckTableExistsAsync
    $idx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like "*Task<bool> CheckTableExistsAsync*") {
            $idx = $i
            break
        }
    }

    if ($idx -gt 0) {
        $helperLines = @(
            "    private static async Task SafeCreateIndexAsync(AppDbContext db, string sql, CancellationToken cancellationToken)"
            "    {"
            "        try"
            "        {"
            "            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);"
            "        }"
            "        catch (Exception ex) when (ex.Message.Contains(""Duplicate key"", StringComparison.OrdinalIgnoreCase) ||"
            "                                   ex.Message.Contains(""already exists"", StringComparison.OrdinalIgnoreCase) ||"
            "                                   ex.Message.Contains(""Duplicate"", StringComparison.OrdinalIgnoreCase))"
            "        {"
            "            // Index already exists, safe to ignore."
            "        }"
            "    }"
            ""
        )
        $lines.InsertRange($idx, $helperLines)
        [System.IO.File]::WriteAllLines($file, $lines)
        Write-Host "PATCH_APPLIED_SUCCESSFULLY"
    } else {
        Write-Host "CHECK_TABLE_NOT_FOUND"
    }
} else {
    Write-Host "ELSE_BLOCK_NOT_FOUND: $($lines[139])"
}
