namespace WebXemPhim.Shared.Models;

public sealed class SignedStreamDto
{
    public string StreamUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public int ExpiresInSeconds { get; set; }
}
