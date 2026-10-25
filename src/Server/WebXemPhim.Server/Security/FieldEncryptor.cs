using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace WebXemPhim.Server.Security;

public sealed class FieldEncryptor : IFieldEncryptor
{
    private const string Purpose = "WebXemPhim.CustomerData.v1";
    private readonly IDataProtector _protector;

    private static IFieldEncryptor? _current;
    public static IFieldEncryptor Current
    {
        get => _current ??= new FieldEncryptor();
        set => _current = value;
    }

    public FieldEncryptor(IDataProtectionProvider? provider = null)
    {
        var resolvedProvider = provider ?? CreateDefaultProvider();
        _protector = resolvedProvider.CreateProtector(Purpose);
    }

    private static IDataProtectionProvider CreateDefaultProvider()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), "WebXemPhim.FieldEncryption.Keys");
        return DataProtectionProvider.Create(new DirectoryInfo(keyPath), builder =>
        {
            builder.SetApplicationName("WebXemPhim");
        });
    }

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return plaintext;
        return _protector.Protect(plaintext.Trim());
    }

    public string? Decrypt(string? ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext)) return ciphertext;
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            // A value that still carries the Data Protection header was written by the
            // encryptor but cannot be read with the current key ring (the ring was rotated
            // or reset). Returning it verbatim would push raw ciphertext into the UI, so the
            // value is dropped and the caller shows an empty state instead.
            if (LooksEncrypted(ciphertext)) return null;

            // Fallback for legacy plaintext values in database
            return ciphertext;
        }
    }

    /// <summary>
    /// True when the value carries the ASP.NET Core Data Protection header, i.e. it was
    /// produced by the encryptor and cannot be read with the current key ring.
    /// </summary>
    public static bool LooksEncrypted(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("CfDJ8", StringComparison.Ordinal);

    public string? ComputeLookupHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
