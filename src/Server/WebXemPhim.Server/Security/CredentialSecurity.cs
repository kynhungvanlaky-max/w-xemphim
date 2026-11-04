using System.Security.Cryptography;
using System.Text;

namespace WebXemPhim.Server.Security;

public static class CredentialSecurity
{
    public const int LegacyIterations = 100_000;
    public const int PasswordIterations = 600_000;
    public static string CreateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static bool IsToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);
    public static string TokenHash(string token) => "sha256:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static byte[] PasswordHash(string password, byte[] salt, int iterations = PasswordIterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
    public static bool Verify(string password, byte[] salt, byte[] hash, int iterations) =>
        CryptographicOperations.FixedTimeEquals(PasswordHash(password, salt, iterations), hash);

    /// <summary>
    /// Creates a high-entropy recovery code. 10 random bytes = 80 bits, enough that offline
    /// guessing is infeasible even if the credential database leaks.
    /// </summary>
    public static string CreateRecoveryCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(10));

    /// <summary>
    /// Hashes a recovery code with a per-code random salt. The stored format is
    /// <c>v2:&lt;saltBase64&gt;:&lt;hashBase64&gt;</c>; legacy unsalted <c>sha256:</c>
    /// values remain verifiable until the user regenerates their codes.
    /// </summary>
    public static string HashRecoveryCode(string code)
    {
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));
        return $"v2:{salt}:{hash}";
    }

    public static bool VerifyRecoveryCode(string code, string stored)
    {
        if (stored.StartsWith("v2:", StringComparison.Ordinal))
        {
            var parts = stored.Split(':');
            if (parts.Length != 3) return false;
            try
            {
                var expected = Convert.FromBase64String(parts[2]);
                var actual = SHA256.HashData(Encoding.UTF8.GetBytes($"{parts[1]}:{code}"));
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Backward compatibility with recovery codes generated before v2.
        var legacy = TokenHash(code);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(legacy));
    }
}
