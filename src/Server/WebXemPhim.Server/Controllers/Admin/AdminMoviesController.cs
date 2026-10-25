using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Utils;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers.Admin;

[Route("api/admin/movies")]
public sealed class AdminMoviesController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null,
    ILogger<AdminMoviesController>? logger = null,
    IOutputCacheStore? outputCacheStore = null)
    : AdminBaseController(contextFactory, timeProvider, env, httpClientFactory)
{
    private async Task InvalidateOutputCacheAsync()
    {
        if (outputCacheStore is not null)
        {
            await outputCacheStore.EvictByTagAsync("movies", default);
            await outputCacheStore.EvictByTagAsync("genres", default);
        }
    }
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<MovieDetailDto>>>> GetMovies(
        [FromQuery] string? q = null,
        [FromQuery] MovieType? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? genre = null,
        [FromQuery] string? quality = null,
        [FromQuery] string? status = null,
        [FromQuery] int? year = null,
        [FromQuery] int? yearFrom = null,
        [FromQuery] int? yearTo = null,
        [FromQuery] string? country = null,
        [FromQuery] double? minRating = null,
        [FromQuery] double? maxRating = null,
        [FromQuery] string? sortBy = null)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var query = context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(m => m.Title.ToLower().Contains(s)
                || m.OriginalTitle.ToLower().Contains(s)
                || m.Slug.ToLower().Contains(s)
                || (m.Director != null && m.Director.ToLower().Contains(s))
                || m.CastMembers.Any(c => c.Name.ToLower().Contains(s))
                || m.MovieGenres.Any(mg => mg.Genre.Name.ToLower().Contains(s)));
        }

        if (type.HasValue)
        {
            query = query.Where(m => m.Type == type.Value);
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            var g = genre.Trim().ToLower();
            query = query.Where(m => m.MovieGenres.Any(mg => mg.Genre.Name.ToLower() == g || mg.Genre.Slug.ToLower() == g));
        }

        if (!string.IsNullOrWhiteSpace(quality))
        {
            var qu = quality.Trim().ToLower();
            query = query.Where(m => m.Quality.ToLower() == qu);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLower();
            if (st == "trending") query = query.Where(m => m.IsTrending);
            else if (st == "featured") query = query.Where(m => m.IsFeatured);
            else if (st == "standard") query = query.Where(m => !m.IsTrending && !m.IsFeatured);
        }

        if (year.HasValue && year.Value > 0)
        {
            query = query.Where(m => m.ReleaseYear == year.Value);
        }
        else if (yearFrom.HasValue && yearFrom.Value > 0)
        {
            var from = yearFrom.Value;
            query = query.Where(m => m.ReleaseYear >= from);
            if (yearTo.HasValue && yearTo.Value > 0)
            {
                var to = yearTo.Value;
                query = query.Where(m => m.ReleaseYear <= to);
            }
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            var c = country.Trim().ToLower();
            query = query.Where(m => m.Country.ToLower().Contains(c));
        }

        if (minRating.HasValue && minRating.Value > 0)
        {
            var r = minRating.Value;
            query = query.Where(m => m.Rating >= r);
        }

        if (maxRating.HasValue && maxRating.Value > 0)
        {
            var r = maxRating.Value;
            query = query.Where(m => m.Rating <= r);
        }

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "rating" or "rating_desc" => query.OrderByDescending(m => m.Rating),
            "releaseyear" or "date_desc" or "year_desc" => query.OrderByDescending(m => m.ReleaseYear),
            "viewcount" or "views_desc" => query.OrderByDescending(m => m.ViewCount),
            "title" or "title_asc" => query.OrderBy(m => m.Title),
            "title_desc" => query.OrderByDescending(m => m.Title),
            _ => query.OrderByDescending(m => m.Id)
        };

        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 1000) pageSize = 1000;
        var totalItems = await query.CountAsync();
        var movies = await query
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var pagedResult = new PagedResult<MovieDetailDto>
        {
            Items = movies.Select(MapMovieDetail).ToList(),
            TotalItems = totalItems,
            Page = Math.Max(page, 1),
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<MovieDetailDto>>.Ok(pagedResult));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> CreateMovie([FromBody] UpsertMovieRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(ApiResponse<MovieDetailDto>.Fail("Tiêu đề phim không được để trống."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();

        var slug = GenerateSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Title : request.Slug);
        var uniqueSlug = slug;
        var suffix = 1;
        while (await context.Movies.AnyAsync(m => m.Slug == uniqueSlug))
        {
            uniqueSlug = $"{slug}-{suffix++}";
        }

        var movie = new MovieEntity
        {
            Title = request.Title.Trim(),
            OriginalTitle = string.IsNullOrWhiteSpace(request.OriginalTitle) ? request.Title.Trim() : request.OriginalTitle.Trim(),
            Slug = uniqueSlug,
            Overview = string.IsNullOrWhiteSpace(request.Overview) ? "Chưa có nội dung tóm tắt." : request.Overview.Trim(),
            PosterUrl = string.IsNullOrWhiteSpace(request.PosterUrl) ? "/images/movies/movie-1-poster.webp" : request.PosterUrl.Trim(),
            BackdropUrl = string.IsNullOrWhiteSpace(request.BackdropUrl) ? "/images/movies/movie-1-backdrop.webp" : request.BackdropUrl.Trim(),
            TrailerUrl = request.TrailerUrl,
            Rating = request.Rating > 0 ? request.Rating : 8.0,
            VoteCount = request.VoteCount > 0 ? request.VoteCount : 50,
            ReleaseYear = request.ReleaseYear > 1900 ? request.ReleaseYear : DateTime.UtcNow.Year,
            DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 120,
            AgeRating = string.IsNullOrWhiteSpace(request.AgeRating) ? "16+" : request.AgeRating.Trim(),
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "4K UHD" : request.Quality.Trim(),
            Type = request.Type,
            Country = string.IsNullOrWhiteSpace(request.Country) ? "Việt Nam" : request.Country.Trim(),
            Director = string.IsNullOrWhiteSpace(request.Director) ? "Đang cập nhật" : request.Director.Trim(),
            IsTrending = request.IsTrending,
            IsFeatured = request.IsFeatured,
            TotalEpisodes = request.TotalEpisodes > 0 ? request.TotalEpisodes : 1,
            CurrentEpisode = request.CurrentEpisode > 0 ? request.CurrentEpisode : 1
        };

        if (request.GenreIds.Count > 0)
        {
            foreach (var gid in request.GenreIds.Distinct())
            {
                if (await context.Genres.AnyAsync(g => g.Id == gid))
                {
                    movie.MovieGenres.Add(new MovieGenreEntity { GenreId = gid });
                }
            }
        }

        if (request.CastNames.Count > 0)
        {
            for (var i = 0; i < request.CastNames.Count; i++)
            {
                var name = request.CastNames[i].Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    movie.CastMembers.Add(new CastMemberEntity
                    {
                        Position = i + 1,
                        Name = name
                    });
                }
            }
        }

        movie.SearchText = MovieSearchTextBuilder.Build(movie.Title, movie.OriginalTitle, movie.Director, movie.CastMembers.Select(c => c.Name), movie.Overview);

        var defaultEp = new EpisodeEntity
        {
            EpisodeNumber = 1,
            Title = movie.Type == MovieType.Movie ? "Bản Đầy Đủ" : "Tập 1",
            DurationMinutes = movie.DurationMinutes,
            ThumbnailUrl = movie.BackdropUrl,
            Overview = movie.Overview
        };
        defaultEp.Streams.Add(new StreamSourceEntity
        {
            Position = 1,
            ServerName = "HLS Main VIP",
            ServerType = StreamServerType.VipFastServer,
            Quality = movie.Quality,
            StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
        });
        movie.Episodes.Add(defaultEp);

        context.Movies.Add(movie);

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "CREATE_MOVIE", "Movie", movie.Id.ToString(), $"Thêm mới phim '{movie.Title}'.");
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        if (context.Database.IsNpgsql())
        {
            try { await DatabaseSchemaHelper.RefreshMovieSearchIndexAsync(context, movie.Id); }
            catch (Exception ex) { logger?.LogWarning(ex, "Lỗi tái tạo chỉ mục tìm kiếm cho phim {MovieId}.", movie.Id); }
        }

        var created = await context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstAsync(m => m.Id == movie.Id);

        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<MovieDetailDto>.Ok(MapMovieDetail(created), "Thêm phim mới thành công."));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> UpdateMovie(int id, [FromBody] UpsertMovieRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies
            .Include(m => m.MovieGenres)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movie is null)
        {
            return NotFound(ApiResponse<MovieDetailDto>.Fail($"Không tìm thấy phim với ID: {id}"));
        }

        movie.Title = request.Title.Trim();
        movie.OriginalTitle = string.IsNullOrWhiteSpace(request.OriginalTitle) ? request.Title.Trim() : request.OriginalTitle.Trim();

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var targetSlug = GenerateSlug(request.Slug);
            if (string.IsNullOrWhiteSpace(targetSlug))
            {
                return BadRequest(ApiResponse<MovieDetailDto>.Fail("Đường dẫn tĩnh (slug) không hợp lệ."));
            }
            var slugExists = await context.Movies.AnyAsync(m => m.Id != id && m.Slug == targetSlug);
            if (slugExists)
            {
                return Conflict(ApiResponse<MovieDetailDto>.Fail($"Đường dẫn tĩnh (slug) '{targetSlug}' đã tồn tại ở một phim khác."));
            }
            movie.Slug = targetSlug;
        }

        movie.Overview = request.Overview.Trim();
        movie.PosterUrl = request.PosterUrl.Trim();
        movie.BackdropUrl = request.BackdropUrl.Trim();
        movie.TrailerUrl = request.TrailerUrl;
        movie.Rating = request.Rating;
        movie.VoteCount = request.VoteCount;
        movie.ReleaseYear = request.ReleaseYear;
        movie.DurationMinutes = request.DurationMinutes;
        movie.AgeRating = request.AgeRating;
        movie.Quality = request.Quality;
        movie.Type = request.Type;
        movie.Country = request.Country;
        movie.Director = request.Director;
        movie.IsTrending = request.IsTrending;
        movie.IsFeatured = request.IsFeatured;
        movie.TotalEpisodes = request.TotalEpisodes;
        movie.CurrentEpisode = request.CurrentEpisode;

        movie.SearchText = MovieSearchTextBuilder.Build(movie.Title, movie.OriginalTitle, movie.Director, request.CastNames.Count > 0 ? request.CastNames : movie.CastMembers.Select(c => c.Name), movie.Overview);

        context.MovieGenres.RemoveRange(movie.MovieGenres);
        foreach (var gid in request.GenreIds.Distinct())
        {
            if (await context.Genres.AnyAsync(g => g.Id == gid))
            {
                context.MovieGenres.Add(new MovieGenreEntity { MovieId = movie.Id, GenreId = gid });
            }
        }

        if (request.CastNames.Count > 0)
        {
            context.CastMembers.RemoveRange(movie.CastMembers);
            for (var i = 0; i < request.CastNames.Count; i++)
            {
                var name = request.CastNames[i].Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    context.CastMembers.Add(new CastMemberEntity
                    {
                        MovieId = movie.Id,
                        Position = i + 1,
                        Name = name
                    });
                }
            }
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "UPDATE_MOVIE", "Movie", movie.Id.ToString(), $"Cập nhật thông tin phim '{movie.Title}'.");
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            if (tx != null) await tx.RollbackAsync();
            if (await context.Movies.AnyAsync(m => m.Id != id && m.Slug == movie.Slug))
            {
                return Conflict(ApiResponse<MovieDetailDto>.Fail($"Đường dẫn tĩnh (slug) '{movie.Slug}' đã tồn tại ở một phim khác."));
            }
            throw;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        if (context.Database.IsNpgsql())
        {
            try { await DatabaseSchemaHelper.RefreshMovieSearchIndexAsync(context, movie.Id); }
            catch (Exception ex) { logger?.LogWarning(ex, "Lỗi tái tạo chỉ mục tìm kiếm cho phim {MovieId}.", movie.Id); }
        }

        var updated = await context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstAsync(m => m.Id == movie.Id);

        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<MovieDetailDto>.Ok(MapMovieDetail(updated), "Cập nhật phim thành công."));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMovie(int id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null)
        {
            return NotFound(ApiResponse<bool>.Fail($"Không tìm thấy phim với ID: {id}"));
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            context.Movies.Remove(movie);
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "DELETE_MOVIE", "Movie", id.ToString(), $"Xóa phim '{movie.Title}'.");
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa phim thành công."));
    }

    [HttpPost("bulk-action")]
    public async Task<ActionResult<ApiResponse<int>>> BulkMovieAction([FromBody] BulkMovieActionRequest request)
    {
        if (request.MovieIds.Count == 0)
        {
            return BadRequest(ApiResponse<int>.Fail("Vui lòng chọn ít nhất một bộ phim."));
        }

        await using var context = await ContextFactory.CreateDbContextAsync();
        var movies = await context.Movies.Where(m => request.MovieIds.Contains(m.Id)).ToListAsync();
        var count = 0;

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }

        try
        {
            switch (request.Action.ToLowerInvariant())
            {
                case "delete":
                    context.Movies.RemoveRange(movies);
                    count = movies.Count;
                    await context.SaveChangesAsync();
                    await LogAuditAsync(context, "BULK_DELETE", "Movie", string.Join(",", request.MovieIds), $"Đã xóa {count} phim hàng loạt.");
                    break;

                case "set_trending":
                    foreach (var m in movies) m.IsTrending = true;
                    count = movies.Count;
                    await context.SaveChangesAsync();
                    await LogAuditAsync(context, "BULK_SET_TRENDING", "Movie", string.Join(",", request.MovieIds), $"Bật Thịnh hành cho {count} phim.");
                    break;

                case "unset_trending":
                    foreach (var m in movies) m.IsTrending = false;
                    count = movies.Count;
                    await context.SaveChangesAsync();
                    await LogAuditAsync(context, "BULK_UNSET_TRENDING", "Movie", string.Join(",", request.MovieIds), $"Tắt Thịnh hành cho {count} phim.");
                    break;

                case "set_featured":
                    foreach (var m in movies) m.IsFeatured = true;
                    count = movies.Count;
                    await context.SaveChangesAsync();
                    await LogAuditAsync(context, "BULK_SET_FEATURED", "Movie", string.Join(",", request.MovieIds), $"Bật Nổi bật cho {count} phim.");
                    break;

                case "unset_featured":
                    foreach (var m in movies) m.IsFeatured = false;
                    count = movies.Count;
                    await context.SaveChangesAsync();
                    await LogAuditAsync(context, "BULK_UNSET_FEATURED", "Movie", string.Join(",", request.MovieIds), $"Tắt Nổi bật cho {count} phim.");
                    break;

                case "change_genre":
                    if (request.TargetGenreId.HasValue && await context.Genres.AnyAsync(g => g.Id == request.TargetGenreId.Value))
                    {
                        var gid = request.TargetGenreId.Value;
                        var movieIds = movies.Select(m => m.Id).ToList();
                        var existingGenreMovieIds = (await context.MovieGenres
                            .Where(mg => movieIds.Contains(mg.MovieId) && mg.GenreId == gid)
                            .Select(mg => mg.MovieId)
                            .ToListAsync())
                            .ToHashSet();

                        foreach (var m in movies)
                        {
                            if (!existingGenreMovieIds.Contains(m.Id))
                            {
                                context.MovieGenres.Add(new MovieGenreEntity { MovieId = m.Id, GenreId = gid });
                            }
                        }
                        count = movies.Count;
                        await context.SaveChangesAsync();
                        await LogAuditAsync(context, "BULK_ADD_GENRE", "Movie", string.Join(",", request.MovieIds), $"Thêm thể loại ID {gid} cho {count} phim.");
                    }
                    break;

                default:
                    return BadRequest(ApiResponse<int>.Fail($"Hành động không hợp lệ: {request.Action}"));
            }

            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<int>.Ok(count, $"Đã hoàn tất thao tác '{request.Action}' trên {count} phim."));
    }

    [HttpPost("{id:int}/toggle-trending")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleTrending(int id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy phim."));

        movie.IsTrending = !movie.IsTrending;
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "TOGGLE_TRENDING", "Movie", id.ToString(), $"Chuyển Thịnh hành thành {movie.IsTrending} cho phim '{movie.Title}'.");
        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<bool>.Ok(movie.IsTrending, $"Đã đổi trạng thái Thịnh hành thành: {(movie.IsTrending ? "Bật" : "Tắt")}."));
    }

    [HttpPost("{id:int}/toggle-featured")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleFeatured(int id)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy phim."));

        movie.IsFeatured = !movie.IsFeatured;
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "TOGGLE_FEATURED", "Movie", id.ToString(), $"Chuyển Nổi bật thành {movie.IsFeatured} cho phim '{movie.Title}'.");
        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<bool>.Ok(movie.IsFeatured, $"Đã đổi trạng thái Nổi bật thành: {(movie.IsFeatured ? "Bật" : "Tắt")}."));
    }

    [HttpPost("{id:int}/clone")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> CloneMovie(int id, [FromBody] CloneMovieRequest request)
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        var source = await context.Movies
            .Include(m => m.MovieGenres)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes).ThenInclude(e => e.Streams)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (source is null) return NotFound(ApiResponse<MovieDetailDto>.Fail("Không tìm thấy phim nguồn để nhân bản."));

        var baseSlug = GenerateSlug(string.IsNullOrWhiteSpace(request.NewSlug) ? request.NewTitle : request.NewSlug);
        var uniqueSlug = baseSlug;
        var suffix = 1;
        while (await context.Movies.AnyAsync(m => m.Slug == uniqueSlug))
        {
            uniqueSlug = $"{baseSlug}-{suffix++}";
        }

        var cloned = new MovieEntity
        {
            Title = request.NewTitle.Trim(),
            OriginalTitle = request.NewTitle.Trim(),
            Slug = uniqueSlug,
            Overview = source.Overview,
            PosterUrl = source.PosterUrl,
            BackdropUrl = source.BackdropUrl,
            TrailerUrl = source.TrailerUrl,
            Rating = source.Rating,
            VoteCount = 10,
            ReleaseYear = request.ReleaseYear,
            DurationMinutes = source.DurationMinutes,
            AgeRating = source.AgeRating,
            Quality = source.Quality,
            Type = source.Type,
            Country = source.Country,
            Director = source.Director,
            IsTrending = false,
            IsFeatured = false,
            TotalEpisodes = source.TotalEpisodes,
            CurrentEpisode = source.CurrentEpisode
        };

        foreach (var mg in source.MovieGenres)
        {
            cloned.MovieGenres.Add(new MovieGenreEntity { GenreId = mg.GenreId });
        }

        foreach (var cm in source.CastMembers)
        {
            cloned.CastMembers.Add(new CastMemberEntity { Position = cm.Position, Name = cm.Name });
        }

        cloned.SearchText = MovieSearchTextBuilder.Build(cloned.Title, cloned.OriginalTitle, cloned.Director, cloned.CastMembers.Select(c => c.Name), cloned.Overview);

        if (request.CopyEpisodes)
        {
            foreach (var ep in source.Episodes)
            {
                var newEp = new EpisodeEntity
                {
                    EpisodeNumber = ep.EpisodeNumber,
                    Title = ep.Title,
                    DurationMinutes = ep.DurationMinutes,
                    ThumbnailUrl = ep.ThumbnailUrl,
                    Overview = ep.Overview
                };

                foreach (var str in ep.Streams)
                {
                    newEp.Streams.Add(new StreamSourceEntity
                    {
                        Position = str.Position,
                        ServerName = str.ServerName,
                        ServerType = str.ServerType,
                        Quality = str.Quality,
                        StreamUrl = str.StreamUrl
                    });
                }
                cloned.Episodes.Add(newEp);
            }
        }

        context.Movies.Add(cloned);

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "CLONE_MOVIE", "Movie", cloned.Id.ToString(), $"Nhân bản phim '{source.Title}' thành '{cloned.Title}'.");
            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        if (context.Database.IsNpgsql())
        {
            try { await DatabaseSchemaHelper.RefreshMovieSearchIndexAsync(context, cloned.Id); }
            catch (Exception ex) { logger?.LogWarning(ex, "Lỗi tái tạo chỉ mục tìm kiếm cho phim đã nhân bản {MovieId}.", cloned.Id); }
        }

        var created = await context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstAsync(m => m.Id == cloned.Id);

        await InvalidateOutputCacheAsync();
        return Ok(ApiResponse<MovieDetailDto>.Ok(MapMovieDetail(created), "Nhân bản phim thành công."));
    }
}
