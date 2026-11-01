$file = "src/Server/WebXemPhim.Server/Data/DatabaseSchemaHelper.cs"
$lines = [System.Collections.Generic.List[string]]([System.IO.File]::ReadAllLines($file))

# Check lines 176..195
if ($lines[176].Contains("usersTableExists")) {
    $newLines = @(
        '        var usersTableExists = await CheckTableExistsAsync(db, isPostgres, "users", cancellationToken);'
        '        if (usersTableExists)'
        '        {'
        '            await SafeCreateIndexAsync('
        '                db,'
        '                isPostgres'
        '                    ? "CREATE INDEX IF NOT EXISTS \"IX_users_EmailLookupHash\" ON users (\"EmailLookupHash\");"'
        '                    : "CREATE INDEX IF NOT EXISTS IX_users_EmailLookupHash ON users (EmailLookupHash);",'
        '                cancellationToken);'
        '        }'
        ''
        '        var paymentsTableExists = await CheckTableExistsAsync(db, isPostgres, "payments", cancellationToken);'
        '        if (paymentsTableExists)'
        '        {'
        '            await SafeCreateIndexAsync('
        '                db,'
        '                isPostgres'
        '                    ? "CREATE INDEX IF NOT EXISTS \"IX_payments_TransactionReference\" ON payments (\"TransactionReference\");"'
        '                    : "CREATE INDEX IF NOT EXISTS IX_payments_TransactionReference ON payments (TransactionReference);",'
        '                cancellationToken);'
        '        }'
    )

    $lines.RemoveRange(176, 20)
    $lines.InsertRange(176, [string[]]$newLines)

    # Also update SafeCreateIndexAsync catch clause
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like "*ex.Message.Contains(`"Duplicate`"*") {
            $lines[$i] = '                                   ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||'
            $lines.Insert($i + 1, '                                   ex.Message.Contains("syntax", StringComparison.OrdinalIgnoreCase))')
            break
        }
    }

    [System.IO.File]::WriteAllLines($file, $lines.ToArray(), [System.Text.Encoding]::UTF8)
    Write-Host "UPDATE_USERS_PAYMENTS_INDEX_SUCCESS"
} else {
    Write-Host "LINE_176_MISMATCH: $($lines[176])"
}
