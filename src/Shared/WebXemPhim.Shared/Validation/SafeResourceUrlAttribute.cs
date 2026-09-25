using System.ComponentModel.DataAnnotations;

namespace WebXemPhim.Shared.Validation;

public sealed class SafeResourceUrlAttribute(bool allowLocalPath = false) : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null || value is string { Length: 0 }) return true;
        if (value is not string text || text.Any(char.IsControl) || text.Contains('\\')) return false;
        if (allowLocalPath && text.StartsWith('/') && !text.StartsWith("//")) return true;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
    }
}
