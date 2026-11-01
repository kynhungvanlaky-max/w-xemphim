$file = "src/Server/WebXemPhim.Server/Data/DatabaseSchemaHelper.cs"
$lines = [System.IO.File]::ReadAllLines($file)

# 1. Update lines 131..148
$start = -1
$end = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\s+else\s*$' -and $lines[$i+1] -match '^\s+\{\s*$' -and $lines[$i+2] -match 'IX_mfa_challenges_UserId') {
        $start = $i
        break
    }
}

if ($start -ge 0) {
    for ($i = $start + 2; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s+\}\s*$') {
            $end = $i
            break
        }
    }
}

Write-Host "Found else block at $start to $end"

$newBlock = @(
    "        else"
    "        {"
    "            if (canCreateDependentTables)"
    "            {"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_mfa_challenges_UserId ON mfa_challenges (UserId);"", cancellationToken);"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_mfa_verified_sessions_UserId ON mfa_verified_sessions (UserId);"", cancellationToken);"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_security_audit_events_CreatedAtUtc ON security_audit_events (CreatedAtUtc);"", cancellationToken);"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_security_audit_events_UserId ON security_audit_events (UserId);"", cancellationToken);"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_playback_issues_UserId_MovieId ON playback_issues (UserId, MovieId);"", cancellationToken);"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_user_library_MovieId ON user_library (MovieId);"", cancellationToken);"
    "                await SafeCreateIndexAsync(db, ""CREATE INDEX IX_notifications_UserId_Read ON notifications (UserId, ``Read``);"", cancellationToken).Replace("``", "`");"
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

$list = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $start; $i++) {
    $list.Add($lines[$i])
}
foreach ($l in $newBlock) {
    $list.Add($l)
}
for ($i = $end + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -like "*Task<bool> CheckTableExistsAsync*") {
        $list.Add("    private static async Task SafeCreateIndexAsync(AppDbContext db, string sql, CancellationToken cancellationToken)")
        $list.Add("    {")
        $list.Add("        try")
        $list.Add("        {")
        $list.Add("            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);")
        $list.Add("        }")
        $list.Add("        catch (Exception ex) when (ex.Message.Contains(""Duplicate key"", StringComparison.OrdinalIgnoreCase) ||")
        $list.Add("                                   ex.Message.Contains(""already exists"", StringComparison.OrdinalIgnoreCase) ||")
        $list.Add("                                   ex.Message.Contains(""Duplicate"", StringComparison.OrdinalIgnoreCase))")
        $list.Add("        {")
        $list.Add("            // Index already exists, safe to ignore.")
        $list.Add("        }")
        $list.Add("    }")
        $list.Add("")
    }
    $list.Add($lines[$i])
}

[System.IO.File]::WriteAllLines($file, $list.ToArray(), [System.Text.Encoding]::UTF8)
Write-Host "DONE"
