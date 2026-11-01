namespace WebXemPhim.Shared.Models;

public sealed class SignedStreamDto
{
    public string StreamUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int ExpiresInSeconds { get; set; }

    /// <summary>Mã phiên phát được gắn vào URL, nếu có.</summary>
    public string? SessionId { get; set; }

    /// <summary>Nguồn là HLS (manifest được server viết lại và proxy qua segment token).</summary>
    public bool IsHls { get; set; }
}
