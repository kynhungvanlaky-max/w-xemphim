namespace WebXemPhim.Shared.Models;

public sealed class MovieFilterOptionsDto
{
    public List<string> Genres { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public List<int> Years { get; set; } = [];
}
