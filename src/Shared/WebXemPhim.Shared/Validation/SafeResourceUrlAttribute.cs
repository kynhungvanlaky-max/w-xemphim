using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Validation;

public sealed class SafeResourceUrlAttribute(bool allowLocalPath = false) : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null || value is string { Length: 0 }) return true;
        if (value is not string text || text.Any(char.IsControl) || text.Contains('\\')) return false;
        if (allowLocalPath && text.StartsWith('/') && !text.StartsWith("//") && !text.Contains("..")) return true;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is "local" or "r2" or "s3")
        {
            return !text.Contains("..") && (!string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')));
        }

        if (allowLocalPath && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && !text.Contains(".."))
        {
            return true;
        }

        return false;
    }
}
