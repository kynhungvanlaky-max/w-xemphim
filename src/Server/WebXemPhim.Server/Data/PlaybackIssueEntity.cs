namespace WebXemPhim.Server.Data;

public sealed class PlaybackIssueEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int MovieId { get; set; }
    public int EpisodeId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
