namespace WebXemPhim.Shared.Models;

public class NotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Copy { get; set; } = string.Empty;
    public string Message => Copy;
    public DateTimeOffset CreatedAt { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Icon { get; set; } = "Bell";
    public bool Read { get; set; }
    public bool IsRead => Read;
    public string? ActionUrl { get; set; }
}
