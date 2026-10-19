namespace WebXemPhim.Server.Security;

/// <summary>
/// Service providing field-level encryption for customer PII (e.g. Email, FullName)
/// and deterministic lookup hash generation for collision-free authentication.
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>
    /// Encrypts plaintext string using ASP.NET Core Data Protection
    /// </summary>
    string? Encrypt(string? plaintext);

    /// <summary>
    /// Decrypts ciphertext string. If ciphertext cannot be unprotected (e.g. legacy unencrypted), returns original string.
    /// </summary>
    string? Decrypt(string? ciphertext);

    /// <summary>
    /// Computes deterministic SHA-256 lookup hash from normalized value (lowercase, trimmed)
    /// </summary>
    string? ComputeLookupHash(string? value);
}
