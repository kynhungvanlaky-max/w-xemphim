namespace WebXemPhim.Server.Data;

public sealed class UserLibraryEntity
{
    public Guid UserId { get; set; }
    public int MovieId { get; set; }
    public DateTime AddedAt { get; set; }
}
