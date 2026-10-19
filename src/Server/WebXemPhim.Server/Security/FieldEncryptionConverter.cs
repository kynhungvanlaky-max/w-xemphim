using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WebXemPhim.Server.Security;

/// <summary>
/// EF Core ValueConverter that transparently encrypts strings on write
/// and decrypts on read using FieldEncryptor.Current.
/// </summary>
public sealed class FieldEncryptionConverter : ValueConverter<string, string>
{
    public static readonly FieldEncryptionConverter Instance = new();

    public FieldEncryptionConverter() : base(
        v => FieldEncryptor.Current.Encrypt(v) ?? string.Empty,
        v => FieldEncryptor.Current.Decrypt(v) ?? string.Empty)
    {
    }
}
