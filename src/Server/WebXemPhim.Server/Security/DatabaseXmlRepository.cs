using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WebXemPhim.Server.Security;

/// <summary>
/// Persists ASP.NET Core Data Protection key ring XML elements in PostgreSQL
/// (<c>data_protection_keys</c>) so stateless cloud containers (e.g. Render free tier) never lose
/// their encryption keys across sleeps, restarts, or redeploys.
/// When a master secret is supplied, XML key material is encrypted at rest with AES-256-GCM.
/// Falls back transparently to the local filesystem repository if PostgreSQL is unreachable.
/// </summary>
public sealed class DatabaseXmlRepository : IXmlRepository
{
    private const string EncryptedPrefix = "v1:aesgcm:";
    private readonly string _connectionString;
    private readonly byte[]? _aesKey;
    private readonly IXmlRepository _fallbackRepository;
    private readonly ILogger<DatabaseXmlRepository>? _logger;
    private volatile bool _tableEnsured;

    public DatabaseXmlRepository(
        string connectionString,
        string? masterSecret,
        DirectoryInfo fallbackDirectory,
        ILogger<DatabaseXmlRepository>? logger = null)
    {
        _connectionString = connectionString;
        _fallbackRepository = new FileSystemXmlRepository(fallbackDirectory, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(masterSecret))
        {
            _aesKey = SHA256.HashData(Encoding.UTF8.GetBytes($"CinemaFlow.DataProtectionKeyRing.v1:{masterSecret.Trim()}"));
        }
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            EnsureTableExists(conn);

            using var cmd = new NpgsqlCommand("SELECT \"Xml\" FROM data_protection_keys ORDER BY \"Id\" ASC;", conn);
            using var reader = cmd.ExecuteReader();
            var list = new List<XElement>();
            while (reader.Read())
            {
                var raw = reader.GetString(0);
                var xmlText = UnprotectXml(raw);
                if (!string.IsNullOrWhiteSpace(xmlText))
                {
                    list.Add(XElement.Parse(xmlText));
                }
            }
            return list.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not read DataProtection keys from PostgreSQL; falling back to local filesystem key ring.");
            return _fallbackRepository.GetAllElements();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        var xmlText = element.ToString(SaveOptions.DisableFormatting);
        var payload = ProtectXml(xmlText);

        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            EnsureTableExists(conn);

            using var cmd = new NpgsqlCommand(
                "INSERT INTO data_protection_keys (\"FriendlyName\", \"Xml\", \"CreatedAtUtc\") VALUES (@name, @xml, NOW());",
                conn);
            cmd.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(friendlyName) ? $"key-{Guid.NewGuid():N}" : friendlyName[..Math.Min(200, friendlyName.Length)]);
            cmd.Parameters.AddWithValue("xml", payload);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not persist DataProtection key '{FriendlyName}' to PostgreSQL; writing to fallback filesystem.", friendlyName);
            _fallbackRepository.StoreElement(element, friendlyName);
        }
    }

    private void EnsureTableExists(NpgsqlConnection conn)
    {
        if (_tableEnsured) return;
        using var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS data_protection_keys (
                "Id" serial PRIMARY KEY,
                "FriendlyName" varchar(200) NOT NULL,
                "Xml" text NOT NULL,
                "CreatedAtUtc" timestamptz NOT NULL DEFAULT NOW()
            );
            ALTER TABLE data_protection_keys ENABLE ROW LEVEL SECURITY;
            """,
            conn);
        cmd.ExecuteNonQuery();
        _tableEnsured = true;
    }

    public string ProtectXml(string xml)
    {
        if (_aesKey is null) return xml;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(xml);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_aesKey, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return $"{EncryptedPrefix}{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
    }

    public string? UnprotectXml(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
        {
            return stored;
        }

        if (_aesKey is null)
        {
            _logger?.LogError("Encrypted DataProtection key found in PostgreSQL, but no master secret is configured.");
            return null;
        }

        try
        {
            var parts = stored[EncryptedPrefix.Length..].Split(':');
            if (parts.Length != 3) return null;
            var nonce = Convert.FromBase64String(parts[0]);
            var tag = Convert.FromBase64String(parts[1]);
            var ciphertext = Convert.FromBase64String(parts[2]);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_aesKey, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            _logger?.LogWarning(ex, "Failed to decrypt a DataProtection key row from PostgreSQL; skipping unreadable key.");
            return null;
        }
    }
}
