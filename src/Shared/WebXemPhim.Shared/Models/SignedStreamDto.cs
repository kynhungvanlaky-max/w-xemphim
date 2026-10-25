namespace WebXemPhim.Shared.Models;

public sealed class SignedStreamDto
{
    public string StreamUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int ExpiresInSeconds { get; set; }

    /// <summary>Mã phiên phát được gắn vào URL, nếu có.</summary>
    public string? SessionId { get; set; }
}
