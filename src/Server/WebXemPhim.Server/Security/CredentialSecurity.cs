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
}
