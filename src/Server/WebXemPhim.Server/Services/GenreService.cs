using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public interface IGenreService
{
    Task<List<GenreDto>> GetAllGenresAsync();
}

public class GenreService : IGenreService
{
    public Task<List<GenreDto>> GetAllGenresAsync()
    {
        return Task.FromResult(MockMovieDatabase.Genres);
    }
}
