namespace WebXemPhim.Client.Services;

public static class AuthNavigation
{
    public static string ReturnPath(string? path, string fallback = "/watchlist") =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && !path.StartsWith("//")
        && !path.Contains('\\') && !path.Any(char.IsControl)
            ? path : fallback;
}
