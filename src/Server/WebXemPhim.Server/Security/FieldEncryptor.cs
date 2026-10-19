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
            // Fallback for legacy plaintext values in database
            return ciphertext;
        }
    }

    public string? ComputeLookupHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
