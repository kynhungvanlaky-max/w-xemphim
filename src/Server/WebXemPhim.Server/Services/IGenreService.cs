using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Services;

public interface IGenreService
{
    Task<List<GenreDto>> GetAllGenresAsync();
}
