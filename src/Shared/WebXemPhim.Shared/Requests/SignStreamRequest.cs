namespace WebXemPhim.Shared.Requests;

public sealed class SignStreamRequest
{
    public int EpisodeId { get; set; }
    public int Position { get; set; } = 1;
}
