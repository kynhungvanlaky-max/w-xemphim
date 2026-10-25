using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WebXemPhim.Server.Security;

public static class TotpSecurity
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] CreateSecret() => RandomNumberGenerator.GetBytes(20);

    public static bool Verify(byte[] secret, string? code, DateTime utcNow, out long matchedStep)
    {
        matchedStep = 0;
        var normalized = NormalizeCode(code);
        if (normalized.Length != 6 || !normalized.All(char.IsDigit)) return false;
        var counter = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            var step = counter + offset;
            if (FixedEquals(normalized, Generate(secret, step)))
            {
                matchedStep = step;
                return true;
            }
        }
        return false;
    }

    public static bool Verify(byte[] secret, string? code, DateTime utcNow) =>
        Verify(secret, code, utcNow, out _);

    public static string ToBase32(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    public static string NormalizeCode(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string GenerateCode(byte[] secret, DateTime utcNow) =>
        Generate(secret, new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds() / 30);

    public static string Generate(byte[] secret, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        var hash = HMACSHA1.HashData(secret, counterBytes);
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
