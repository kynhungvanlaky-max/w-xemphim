using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Server.Data;

public sealed class MovieEntity
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
    public MovieType Type { get; set; }
    public string Country { get; set; } = string.Empty;
    public long ViewCount { get; set; }
    public bool IsTrending { get; set; }
    public bool IsFeatured { get; set; }
    public string Director { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public int TotalEpisodes { get; set; }
    public int CurrentEpisode { get; set; }
    public List<MovieGenreEntity> MovieGenres { get; set; } = [];
    public List<CastMemberEntity> CastMembers { get; set; } = [];
    public List<EpisodeEntity> Episodes { get; set; } = [];
    public List<ReviewEntity> Reviews { get; set; } = [];
}

public sealed class GenreEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public List<MovieGenreEntity> MovieGenres { get; set; } = [];
}

public sealed class MovieGenreEntity
{
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;
    public int GenreId { get; set; }
    public GenreEntity Genre { get; set; } = null!;
}

public sealed class CastMemberEntity
{
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;
    public int Position { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class EpisodeEntity
{
    public int Id { get; set; }
    public int MovieId { get; set; }
    public MovieEntity Movie { get; set; } = null!;
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public List<StreamSourceEntity> Streams { get; set; } = [];
}

public sealed class StreamSourceEntity
{
    public int EpisodeId { get; set; }
    public EpisodeEntity Episode { get; set; } = null!;
    public int Position { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public StreamServerType ServerType { get; set; }
    public string Quality { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
}
