$file = "src/Server/WebXemPhim.Server/Data/DatabaseSchemaHelper.cs"
$content = [System.IO.File]::ReadAllText($file)

$content = $content.Replace(
'        var usersTableExists = await CheckTableExistsAsync(db, isPostgres, "users", cancellationToken);
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
        }',
'        var usersTableExists = await CheckTableExistsAsync(db, isPostgres, "users", cancellationToken);
        if (usersTableExists)
        {
            await SafeCreateIndexAsync(
                db,
                isPostgres
                    ? "CREATE INDEX IF NOT EXISTS \"IX_users_EmailLookupHash\" ON users (\"EmailLookupHash\");"
                    : "CREATE INDEX IF NOT EXISTS IX_users_EmailLookupHash ON users (EmailLookupHash);",
                cancellationToken);
        }

        var paymentsTableExists = await CheckTableExistsAsync(db, isPostgres, "payments", cancellationToken);
        if (paymentsTableExists)
        {
            await SafeCreateIndexAsync(
                db,
                isPostgres
                    ? "CREATE INDEX IF NOT EXISTS \"IX_payments_TransactionReference\" ON payments (\"TransactionReference\");"
                    : "CREATE INDEX IF NOT EXISTS IX_payments_TransactionReference ON payments (TransactionReference);",
                cancellationToken);
        }'
)

$content = $content.Replace(
'        catch (Exception ex) when (ex.Message.Contains("Duplicate key", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))',
'        catch (Exception ex) when (ex.Message.Contains("Duplicate key", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("syntax", StringComparison.OrdinalIgnoreCase))'
)

[System.IO.File]::WriteAllText($file, $content, [System.Text.Encoding]::UTF8)
Write-Host "UPDATE_COMPLETE"
