namespace WebXemPhim.Shared.Models;

public class HomeAggregateDto
{
    public MovieDto? Featured { get; set; }
    public List<MovieDto> Trending { get; set; } = [];
    public List<MovieDto> Latest { get; set; } = [];
    public List<MovieDto> TopSeries { get; set; } = [];
    public List<MovieDto> HeroMovies { get; set; } = [];
}
