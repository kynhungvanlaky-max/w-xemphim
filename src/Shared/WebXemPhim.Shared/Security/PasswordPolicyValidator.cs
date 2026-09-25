namespace WebXemPhim.Shared.Security;

/// <summary>
/// Enforces OWASP ASVS 2.1.7: Checks passwords submitted during registration,
/// recovery, and change against common breached, dictionary, and predictable passwords.
/// </summary>
public static class PasswordPolicyValidator
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "password1",
        "password12",
        "password123",
        "password1234",
        "password12345",
        "admin123",
        "admin1234",
        "admin12345",
        "welcome123",
        "welcome1234",
        "qwerty123",
        "qwerty1234",
        "qwerty12345",
        "12345678",
        "123456789",
        "12345678aa",
        "123456789aa",
        "abcdef123",
        "abcdef1234",
        "abcdef12345",
        "cinemaflow123",
        "cinemaflow1234",
        "cinemapassword123",
        "letmein123",
        "iloveyou123",
        "monkey123",
        "dragon123",
        "master123",
        "shadow123",
        "football123",
        "superman123"
    };

    private static readonly string[] WeakRoots =
    [
        "password",
        "admin",
        "welcome",
        "qwerty",
        "123456",
        "cinemaflow",
        "abcdef"
    ];

    /// <summary>
    /// Returns true if the password is known to be common, trivial, or easily guessable.
    /// </summary>
    public static bool IsCommonOrWeak(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return true;
        }

        var trimmed = password.Trim();

        // 1. Direct match in common password dictionary
        if (CommonPasswords.Contains(trimmed))
        {
            return true;
        }

        // 2. Trivial root + digits pattern (e.g. "password" + "999", "admin" + "007")
        var lower = trimmed.ToLowerInvariant();
        foreach (var root in WeakRoots)
        {
            if (lower.StartsWith(root, StringComparison.Ordinal))
            {
                var suffix = lower[root.Length..];
                // If suffix is purely digits or empty or a single symbol + digits
                if (suffix.Length <= 4 && suffix.All(char.IsDigit))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
