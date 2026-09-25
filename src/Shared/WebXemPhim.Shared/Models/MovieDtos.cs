using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Shared.Models;

public class MovieDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public string PosterUrl { get; set; } = string.Empty;
    public string BackdropUrl { get; set; } = string.Empty;
    public string? TrailerUrl { get; set; }
    public double Rating { get; set; }
    public int VoteCount { get; set; }
    public int ReleaseYear { get; set; }
    public int DurationMinutes { get; set; }
    public string AgeRating { get; set; } = "13+";
    public string Quality { get; set; } = "4K UHD";
    public MovieType Type { get; set; } = MovieType.Movie;
    public List<string> Genres { get; set; } = [];
    public string Country { get; set; } = "Âu Mỹ";
    public long ViewCount { get; set; }
    public bool IsTrending { get; set; }
    public bool IsFeatured { get; set; }
    public int TotalEpisodes { get; set; } = 1;
    public int CurrentEpisode { get; set; } = 1;
}

public class MovieDetailDto : MovieDto
{
    public string Director { get; set; } = string.Empty;
    public List<string> Cast { get; set; } = [];
    public List<EpisodeDto> Episodes { get; set; } = [];
    public List<MovieDto> SimilarMovies { get; set; } = [];
}

public class EpisodeDto
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public List<StreamSourceDto> Streams { get; set; } = [];
    public List<StreamSourceDto> StreamSources { get => Streams; set => Streams = value; }
}

public class StreamSourceDto
{
    public int Id { get; set; }
    public int EpisodeId { get; set; }
    public int Position { get; set; }
    public string ServerName { get; set; } = "VIP FastServer";
    public StreamServerType ServerType { get; set; } = StreamServerType.VipFastServer;
    public string Quality { get; set; } = "1080p 60fps";
    public string StreamUrl { get; set; } = string.Empty;
}

public class GenreDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int MovieCount { get; set; }
}

public class WatchlistItemDto
{
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = string.Empty;
    public string PosterUrl { get; set; } = string.Empty;
    public string Quality { get; set; } = "4K";
    public double Rating { get; set; }
    public int ReleaseYear { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public int ProgressPercent { get; set; }
    public string? LastWatchedEpisode { get; set; }
}
