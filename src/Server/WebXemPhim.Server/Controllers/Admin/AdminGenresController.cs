using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin/genres")]
public sealed class AdminGenresController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null,
    ILogger<AdminGenresController>? logger = null,
    IOutputCacheStore? outputCacheStore = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    private async Task InvalidateOutputCacheAsync()
    {
        if (outputCacheStore is not null)
        {
            await outputCacheStore.EvictByTagAsync("genres", default);
            await outputCacheStore.EvictByTagAsync("movies", default);
        }
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<GenreDto>>>> GetGenres()
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var genres = await context.Genres
            .AsNoTracking()
            .OrderBy(g => g.Id)
            .Select(g => new GenreDto
            {
                Id = g.Id,
                Name = g.Name,
                Slug = g.Slug,
                MovieCount = g.MovieGenres.Count
            })
            .ToListAsync();

        return Ok(ApiResponse<List<GenreDto>>.Ok(genres));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<GenreDto>>> CreateGenre([FromBody] UpsertGenreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<GenreDto>.Fail("Tên thể loại không được để trống."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var baseSlug = GenerateSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
        var uniqueSlug = baseSlug;
        var suffix = 1;
        while (await context.Genres.AnyAsync(g => g.Slug == uniqueSlug))
        {
            uniqueSlug = $"{baseSlug}-{suffix++}";
        }

        var genre = new GenreEntity
        {
            Name = request.Name.Trim(),
            Slug = uniqueSlug
        };

        context.Genres.Add(genre);
        await context.SaveChangesAsync();

        await LogAuditAsync(context, "CREATE_GENRE", "Genre", genre.Id.ToString(), $"Thêm thể loại '{genre.Name}'.");
        logger?.LogInformation("Thêm thể loại mới {GenreId} - {GenreName}", genre.Id, genre.Name);
        await InvalidateOutputCacheAsync();

        var dto = new GenreDto
        {
            Id = genre.Id,
            Name = genre.Name,
            Slug = genre.Slug,
            MovieCount = 0
        };

        return Ok(ApiResponse<GenreDto>.Ok(dto, "Thêm thể loại thành công."));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<GenreDto>>> UpdateGenre(int id, [FromBody] UpsertGenreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(ApiResponse<GenreDto>.Fail("Tên thể loại không được để trống."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var genre = await context.Genres.Include(g => g.MovieGenres).FirstOrDefaultAsync(g => g.Id == id);
        if (genre is null)
        {
            return NotFound(ApiResponse<GenreDto>.Fail($"Không tìm thấy thể loại với ID: {id}"));
        }

        genre.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var targetSlug = GenerateSlug(request.Slug);
            var slugExists = await context.Genres.AnyAsync(g => g.Id != id && g.Slug == targetSlug);
            if (slugExists)
            {
                return Conflict(ApiResponse<GenreDto>.Fail("Đường dẫn tĩnh (slug) này đã được sử dụng bởi thể loại khác."));
            }
            genre.Slug = targetSlug;
        }

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "UPDATE_GENRE", "Genre", id.ToString(), $"Cập nhật thể loại '{genre.Name}'.");
        logger?.LogInformation("Cập nhật thể loại {GenreId} - {GenreName}", genre.Id, genre.Name);
        await InvalidateOutputCacheAsync();

        var dto = new GenreDto
        {
            Id = genre.Id,
            Name = genre.Name,
            Slug = genre.Slug,
            MovieCount = genre.MovieGenres.Count
        };

        return Ok(ApiResponse<GenreDto>.Ok(dto, "Cập nhật thể loại thành công."));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteGenre(int id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var genre = await context.Genres.FindAsync(id);
        if (genre is null)
        {
            return NotFound(ApiResponse<bool>.Fail($"Không tìm thấy thể loại với ID: {id}"));
        }

        context.Genres.Remove(genre);
        await context.SaveChangesAsync();

        await LogAuditAsync(context, "DELETE_GENRE", "Genre", id.ToString(), $"Xóa thể loại '{genre.Name}'.");
        logger?.LogInformation("Xóa thể loại {GenreId} - {GenreName}", genre.Id, genre.Name);
        await InvalidateOutputCacheAsync();

        return Ok(ApiResponse<bool>.Ok(true, "Xóa thể loại thành công."));
    }
}
