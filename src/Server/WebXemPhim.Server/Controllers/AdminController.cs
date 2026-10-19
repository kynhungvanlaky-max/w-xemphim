using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Security;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[NonController]
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/[controller]")]
public sealed class AdminController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IHttpClientFactory? httpClientFactory = null,
    IWebHostEnvironment? env = null) : ControllerBase
{
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
    private Guid? CallerUserId
    {
        get
        {
            var principal = ControllerContext?.HttpContext?.User;
            if (principal is null) return null;
            var claimValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claimValue, out var id) ? id : null;
        }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AdminDashboardDto>>> GetStats()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var now = UtcNow;

        var totalMovies = await context.Movies.CountAsync(m => m.Type == MovieType.Movie);
        var totalSeries = await context.Movies.CountAsync(m => m.Type == MovieType.Series);
        var totalAnime = await context.Movies.CountAsync(m => m.Type == MovieType.Anime);
        var totalEpisodes = await context.Episodes.CountAsync();

        var totalUsers = await context.Users.CountAsync();
        var activeVipUsers = await context.Users.CountAsync(u => u.Membership != MembershipTier.Free && u.VipExpiresAtUtc > now);
        var freeUsers = totalUsers - activeVipUsers;

        var totalRevenue = await context.Payments
            .Where(p => p.Status == PaymentStatus.Completed)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var totalTransactions = await context.Payments.CountAsync();

        var totalReviews = await context.Reviews.CountAsync();
        var averageRating = totalReviews > 0
            ? await context.Reviews.AverageAsync(r => (double)r.Rating)
            : 0.0;

        var recentUsers = await context.Users
            .OrderByDescending(u => u.CreatedAtUtc)
            .Take(5)
            .Select(u => new UserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                MembershipTier = u.Membership,
                SubscriptionExpiresAt = u.VipExpiresAtUtc.HasValue ? new DateTimeOffset(u.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
                CreatedAt = new DateTimeOffset(u.CreatedAtUtc, TimeSpan.Zero),
                Initials = GetInitials(u.FullName)
            })
            .ToListAsync();

        var recentPayments = await context.Payments
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(5)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                UserId = p.UserId,
                PlanId = p.PlanId,
                PlanName = p.PlanName,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                CreatedAt = new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero),
                PaidAt = p.PaidAtUtc.HasValue ? new DateTimeOffset(p.PaidAtUtc.Value, TimeSpan.Zero) : null,
                VipExpiresAt = p.VipExpiresAtUtc.HasValue ? new DateTimeOffset(p.VipExpiresAtUtc.Value, TimeSpan.Zero) : null
            })
            .ToListAsync();

        var recentReviews = await context.Reviews
            .Include(r => r.Movie)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(5)
            .Select(r => new AdminReviewDto
            {
                Id = r.Id,
                MovieId = r.MovieId,
                MovieTitle = r.Movie.Title,
                MoviePosterUrl = r.Movie.PosterUrl,
                UserId = r.UserId,
                UserFullName = r.User.FullName,
                UserEmail = r.User.Email,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                AdminReply = r.AdminReply,
                AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                IsReported = r.IsReported
            })
            .ToListAsync();

        var genreDistribution = await context.MovieGenres
            .Include(mg => mg.Genre)
            .GroupBy(mg => mg.Genre.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Name, g => g.Count);

        var dto = new AdminDashboardDto
        {
            TotalMovies = totalMovies,
            TotalSeries = totalSeries,
            TotalAnime = totalAnime,
            TotalEpisodes = totalEpisodes,
            TotalUsers = totalUsers,
            ActiveVipUsers = activeVipUsers,
            FreeUsers = freeUsers,
            TotalRevenue = totalRevenue,
            TotalTransactions = totalTransactions,
            TotalReviews = totalReviews,
            AverageRating = Math.Round(averageRating, 1),
            RecentUsers = recentUsers,
            RecentPayments = recentPayments,
            RecentReviews = recentReviews,
            GenreDistribution = genreDistribution
        };

        return Ok(ApiResponse<AdminDashboardDto>.Ok(dto));
    }

    [HttpGet("movies")]
    public async Task<ActionResult<ApiResponse<List<MovieDetailDto>>>> GetMovies(
        [FromQuery] string? q = null,
        [FromQuery] MovieType? type = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim().ToLower();
            query = query.Where(m => m.Title.ToLower().Contains(search)
                                  || m.OriginalTitle.ToLower().Contains(search)
                                  || m.Director.ToLower().Contains(search));
        }

        if (type.HasValue)
        {
            query = query.Where(m => m.Type == type.Value);
        }

        var movies = await query
            .OrderByDescending(m => m.Id)
            .ToListAsync();

        var result = movies.Select(MapMovieDetail).ToList();
        return Ok(ApiResponse<List<MovieDetailDto>>.Ok(result));
    }

    [HttpPost("movies")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> CreateMovie([FromBody] UpsertMovieRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(ApiResponse<MovieDetailDto>.Fail("Tiêu đề phim không được để trống."));
        }

        await using var context = await contextFactory.CreateDbContextAsync();

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
            PosterUrl = string.IsNullOrWhiteSpace(request.PosterUrl) ? "/images/movies/movie-1-poster.jpg" : request.PosterUrl.Trim(),
            BackdropUrl = string.IsNullOrWhiteSpace(request.BackdropUrl) ? "/images/movies/movie-1-backdrop.jpg" : request.BackdropUrl.Trim(),
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

        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        if (request.GenreIds.Count > 0)
        {
            foreach (var gid in request.GenreIds.Distinct())
            {
                if (await context.Genres.AnyAsync(g => g.Id == gid))
                {
                    context.MovieGenres.Add(new MovieGenreEntity { MovieId = movie.Id, GenreId = gid });
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
                    context.CastMembers.Add(new CastMemberEntity
                    {
                        MovieId = movie.Id,
                        Position = i + 1,
                        Name = name
                    });
                }
            }
        }

        // Tạo tập 1 mặc định cho phim lẻ
        var defaultEp = new EpisodeEntity
        {
            MovieId = movie.Id,
            EpisodeNumber = 1,
            Title = movie.Type == MovieType.Movie ? "Bản Đầy Đủ" : "Tập 1",
            DurationMinutes = movie.DurationMinutes,
            ThumbnailUrl = movie.BackdropUrl,
            Overview = movie.Overview
        };
        context.Episodes.Add(defaultEp);
        await context.SaveChangesAsync();

        context.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = defaultEp.Id,
            Position = 1,
            ServerName = "HLS Main VIP",
            ServerType = StreamServerType.VipFastServer,
            Quality = movie.Quality,
            StreamUrl = "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
        });

        await context.SaveChangesAsync();

        var created = await context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstAsync(m => m.Id == movie.Id);

        return Ok(ApiResponse<MovieDetailDto>.Ok(MapMovieDetail(created), "Thêm phim mới thành công."));
    }

    [HttpPut("movies/{id:int}")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> UpdateMovie(int id, [FromBody] UpsertMovieRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
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

        // Cập nhật Thể loại
        context.MovieGenres.RemoveRange(movie.MovieGenres);
        foreach (var gid in request.GenreIds.Distinct())
        {
            if (await context.Genres.AnyAsync(g => g.Id == gid))
            {
                context.MovieGenres.Add(new MovieGenreEntity { MovieId = movie.Id, GenreId = gid });
            }
        }

        // Cập nhật Diễn viên nếu có truyền lên
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

        var updated = await context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstAsync(m => m.Id == movie.Id);

        return Ok(ApiResponse<MovieDetailDto>.Ok(MapMovieDetail(updated), "Cập nhật phim thành công."));
    }

    [HttpDelete("movies/{id:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteMovie(int id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null)
        {
            return NotFound(ApiResponse<bool>.Fail($"Không tìm thấy phim với ID: {id}"));
        }

        context.Movies.Remove(movie);
        await context.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa phim thành công."));
    }

    [HttpGet("movies/{id:int}/episodes")]
    public async Task<ActionResult<ApiResponse<List<EpisodeDto>>>> GetEpisodes(int id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var episodes = await context.Episodes
            .Where(e => e.MovieId == id)
            .OrderBy(e => e.EpisodeNumber)
            .Select(e => new EpisodeDto
            {
                Id = e.Id,
                MovieId = e.MovieId,
                EpisodeNumber = e.EpisodeNumber,
                Title = e.Title,
                DurationMinutes = e.DurationMinutes,
                ThumbnailUrl = e.ThumbnailUrl,
                Overview = e.Overview
            })
            .ToListAsync();

        return Ok(ApiResponse<List<EpisodeDto>>.Ok(episodes));
    }

    [HttpPost("movies/{id:int}/episodes")]
    public async Task<ActionResult<ApiResponse<EpisodeDto>>> AddEpisode(int id, [FromBody] UpsertEpisodeRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null)
        {
            return NotFound(ApiResponse<EpisodeDto>.Fail($"Không tìm thấy phim với ID: {id}"));
        }

        var episodeNumber = request.EpisodeNumber;
        if (await context.Episodes.AnyAsync(e => e.MovieId == id && e.EpisodeNumber == episodeNumber))
        {
            var maxEp = await context.Episodes.Where(e => e.MovieId == id).MaxAsync(e => (int?)e.EpisodeNumber) ?? 0;
            episodeNumber = maxEp + 1;
        }

        var episode = new EpisodeEntity
        {
            MovieId = id,
            EpisodeNumber = episodeNumber,
            Title = string.IsNullOrWhiteSpace(request.Title) ? $"Tập {episodeNumber}" : request.Title.Trim(),
            DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 45,
            ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl) ? movie.BackdropUrl : request.ThumbnailUrl.Trim(),
            Overview = string.IsNullOrWhiteSpace(request.Overview) ? $"Nội dung tập {episodeNumber}." : request.Overview.Trim()
        };

        context.Episodes.Add(episode);
        await context.SaveChangesAsync();

        var streamUrl = string.IsNullOrWhiteSpace(request.StreamUrl)
            ? "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4"
            : request.StreamUrl.Trim();

        context.StreamSources.Add(new StreamSourceEntity
        {
            EpisodeId = episode.Id,
            Position = 1,
            ServerName = string.IsNullOrWhiteSpace(request.ServerName) ? "VIP Server" : request.ServerName.Trim(),
            ServerType = request.ServerType,
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "1080p" : request.Quality.Trim(),
            StreamUrl = streamUrl
        });

        if (episodeNumber > movie.CurrentEpisode) movie.CurrentEpisode = episodeNumber;
        if (episodeNumber > movie.TotalEpisodes) movie.TotalEpisodes = episodeNumber;

        await context.SaveChangesAsync();

        return Ok(ApiResponse<EpisodeDto>.Ok(new EpisodeDto
        {
            Id = episode.Id,
            MovieId = episode.MovieId,
            EpisodeNumber = episode.EpisodeNumber,
            Title = episode.Title,
            DurationMinutes = episode.DurationMinutes,
            ThumbnailUrl = episode.ThumbnailUrl,
            Overview = episode.Overview
        }, "Thêm tập phim thành công."));
    }

    [HttpDelete("episodes/{episodeId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteEpisode(int episodeId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var episode = await context.Episodes.FindAsync(episodeId);
        if (episode is null)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy tập phim."));
        }

        var movieId = episode.MovieId;
        context.Episodes.Remove(episode);
        await context.SaveChangesAsync();

        var movie = await context.Movies.FindAsync(movieId);
        if (movie is not null)
        {
            var remaining = await context.Episodes.Where(e => e.MovieId == movieId).ToListAsync();
            movie.CurrentEpisode = remaining.Count > 0 ? remaining.Max(e => e.EpisodeNumber) : 0;
            await context.SaveChangesAsync();
        }

        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa tập phim."));
    }

    [HttpGet("users")]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetUsers(
        [FromQuery] string? q = null,
        [FromQuery] MembershipTier? tier = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(s) || u.FullName.ToLower().Contains(s));
        }

        if (tier.HasValue)
        {
            query = query.Where(u => u.Membership == tier.Value);
        }

        var rawUsers = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync();

        var userIds = rawUsers.Select(u => u.Id).ToList();
        var securities = await context.Set<UserSecurityEntity>()
            .Where(s => userIds.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId);

        var utcNow = DateTime.UtcNow;
        var users = rawUsers.Select(u =>
        {
            securities.TryGetValue(u.Id, out var sec);
            var isLocked = sec?.LockedUntilUtc.HasValue == true && sec.LockedUntilUtc.Value > utcNow;
            return new UserDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                MembershipTier = u.Membership,
                SubscriptionExpiresAt = u.VipExpiresAtUtc.HasValue ? new DateTimeOffset(u.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
                CreatedAt = new DateTimeOffset(u.CreatedAtUtc, TimeSpan.Zero),
                Initials = GetInitials(u.FullName),
                IsAdmin = sec?.IsAdmin ?? false,
                IsLocked = isLocked,
                LockedUntil = sec?.LockedUntilUtc.HasValue == true ? new DateTimeOffset(sec.LockedUntilUtc.Value, TimeSpan.Zero) : null
            };
        }).ToList();

        return Ok(ApiResponse<List<UserDto>>.Ok(users));
    }

    [HttpPut("users/{id:guid}/membership")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUserMembership(
        Guid id,
        [FromBody] AdminUpdateUserRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound(ApiResponse<UserDto>.Fail("Không tìm thấy người dùng."));
        }

        user.Membership = request.MembershipTier;
        if (request.MembershipTier == MembershipTier.Free)
        {
            user.VipExpiresAtUtc = null;
        }
        else if (request.ExtendDays > 0)
        {
            var currentExpiry = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > UtcNow
                ? user.VipExpiresAtUtc.Value
                : UtcNow;
            user.VipExpiresAtUtc = currentExpiry.AddDays(request.ExtendDays);
        }

        await context.SaveChangesAsync();

        return Ok(ApiResponse<UserDto>.Ok(new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            MembershipTier = user.Membership,
            SubscriptionExpiresAt = user.VipExpiresAtUtc.HasValue ? new DateTimeOffset(user.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            CreatedAt = new DateTimeOffset(user.CreatedAtUtc, TimeSpan.Zero),
            Initials = GetInitials(user.FullName)
        }, "Cập nhật hạng thành viên thành công."));
    }

    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<List<PaymentDto>>>> GetPayments(
        [FromQuery] string? q = null,
        [FromQuery] PaymentStatus? status = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.Payments.Include(p => p.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(p => p.OrderCode.ToLower().Contains(s)
                                  || p.PlanName.ToLower().Contains(s)
                                  || p.User.Email.ToLower().Contains(s));
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var payments = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                UserId = p.UserId,
                UserEmail = p.User != null ? p.User.Email : null,
                UserFullName = p.User != null ? p.User.FullName : null,
                PlanId = p.PlanId,
                PlanName = p.PlanName,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                CreatedAt = new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero),
                PaidAt = p.PaidAtUtc.HasValue ? new DateTimeOffset(p.PaidAtUtc.Value, TimeSpan.Zero) : null,
                VipExpiresAt = p.VipExpiresAtUtc.HasValue ? new DateTimeOffset(p.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
                TransactionReference = p.TransactionReference,
                Note = p.Note
            })
            .ToListAsync();

        return Ok(ApiResponse<List<PaymentDto>>.Ok(payments));
    }

    [HttpGet("reviews")]
    public async Task<ActionResult<ApiResponse<List<AdminReviewDto>>>> GetReviews([FromQuery] string? q = null)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.Reviews
            .Include(r => r.Movie)
            .Include(r => r.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(r => r.Comment.ToLower().Contains(s)
                                  || r.Movie.Title.ToLower().Contains(s)
                                  || r.User.Email.ToLower().Contains(s));
        }

        var reviews = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new AdminReviewDto
            {
                Id = r.Id,
                MovieId = r.MovieId,
                MovieTitle = r.Movie.Title,
                MoviePosterUrl = r.Movie.PosterUrl,
                UserId = r.UserId,
                UserFullName = r.User.FullName,
                UserEmail = r.User.Email,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                AdminReply = r.AdminReply,
                AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                IsReported = r.IsReported
            })
            .ToListAsync();

        return Ok(ApiResponse<List<AdminReviewDto>>.Ok(reviews));
    }

    [HttpDelete("reviews/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteReview(Guid id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var review = await context.Reviews.FindAsync(id);
        if (review is null)
        {
            return NotFound(ApiResponse<bool>.Fail("Không tìm thấy đánh giá."));
        }

        var movieId = review.MovieId;
        context.Reviews.Remove(review);
        await context.SaveChangesAsync();

        var movieReviews = await context.Reviews
            .Where(r => r.MovieId == movieId)
            .ToListAsync();
        var movie = await context.Movies.FindAsync(movieId);
        if (movie is not null)
        {
            movie.VoteCount = movieReviews.Count;
            movie.Rating = movieReviews.Count > 0
                ? Math.Round(movieReviews.Average(r => (double)r.Rating), 1)
                : 0;
            await context.SaveChangesAsync();
        }

        await LogAuditAsync(context, "DELETE_REVIEW", "Review", id.ToString(), $"Đã xóa đánh giá của người dùng {review.UserId}.");
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa đánh giá thành công."));
    }

    private async Task LogAuditAsync(AppDbContext context, string action, string entityType, string entityId, string summary, string? detailsJson = null)
    {
        try
        {
            var adminEmail = User?.Identity?.Name ?? "admin@cinemaflow.local";
            context.AuditLogs.Add(new AuditLogEntity
            {
                TimestampUtc = UtcNow,
                AdminEmail = adminEmail,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary,
                DetailsJson = detailsJson
            });
            await context.SaveChangesAsync();
        }
        catch { }
    }

    [HttpGet("system/health")]
    public async Task<ActionResult<ApiResponse<SystemHealthDto>>> GetSystemHealth()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var totalMovies = await context.Movies.CountAsync();
        var totalEpisodes = await context.Episodes.CountAsync();
        var totalUsers = await context.Users.CountAsync();
        var activeStreamsCount = await context.StreamSources.CountAsync();

        long dbSizeBytes = 0;
        try
        {
            var dbPath = "webxemphim.db";
            if (System.IO.File.Exists(dbPath))
            {
                dbSizeBytes = new System.IO.FileInfo(dbPath).Length;
            }
        }
        catch { }

        var memMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2);
        string uptimeStr = "0h 0m";
        try
        {
            var startTime = System.Diagnostics.Process.GetCurrentProcess().StartTime;
            var diff = DateTime.Now - startTime;
            uptimeStr = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        }
        catch { }

        var dto = new SystemHealthDto
        {
            DbSizeBytes = dbSizeBytes,
            DbSizeFormatted = dbSizeBytes > 0 ? $"{Math.Round(dbSizeBytes / (1024.0 * 1024.0), 2)} MB" : "0.4 MB (SQLite)",
            ServerUptime = uptimeStr,
            TotalMovies = totalMovies,
            TotalEpisodes = totalEpisodes,
            TotalUsers = totalUsers,
            ActiveStreamsCount = activeStreamsCount,
            MemoryUsageMb = memMb,
            Environment = env?.EnvironmentName ?? "Development",
            ServerTimeUtc = UtcNow
        };

        return Ok(ApiResponse<SystemHealthDto>.Ok(dto));
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<ApiResponse<List<AuditLogDto>>>> GetAuditLogs(
        [FromQuery] string? q = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int take = 50)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var query = context.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(a => a.Summary.ToLower().Contains(s) || a.AdminEmail.ToLower().Contains(s) || a.EntityId.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType.ToLower() == entityType.Trim().ToLower());
        }

        var logs = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Min(take, 200))
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                Timestamp = new DateTimeOffset(a.TimestampUtc, TimeSpan.Zero),
                AdminEmail = a.AdminEmail,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Summary = a.Summary,
                DetailsJson = a.DetailsJson
            })
            .ToListAsync();

        return Ok(ApiResponse<List<AuditLogDto>>.Ok(logs));
    }

    [HttpPost("movies/bulk-action")]
    public async Task<ActionResult<ApiResponse<int>>> BulkMovieAction([FromBody] BulkMovieActionRequest request)
    {
        if (request.MovieIds.Count == 0)
        {
            return BadRequest(ApiResponse<int>.Fail("Vui lòng chọn ít nhất một bộ phim."));
        }

        await using var context = await contextFactory.CreateDbContextAsync();
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

        return Ok(ApiResponse<int>.Ok(count, $"Đã hoàn tất thao tác '{request.Action}' trên {count} phim."));
    }

    [HttpPost("movies/{id:int}/toggle-trending")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleTrending(int id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy phim."));

        movie.IsTrending = !movie.IsTrending;
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "TOGGLE_TRENDING", "Movie", id.ToString(), $"Chuyển Thịnh hành thành {movie.IsTrending} cho phim '{movie.Title}'.");
        return Ok(ApiResponse<bool>.Ok(movie.IsTrending, $"Đã đổi trạng thái Thịnh hành thành: {(movie.IsTrending ? "Bật" : "Tắt")}."));
    }

    [HttpPost("movies/{id:int}/toggle-featured")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleFeatured(int id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(id);
        if (movie is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy phim."));

        movie.IsFeatured = !movie.IsFeatured;
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "TOGGLE_FEATURED", "Movie", id.ToString(), $"Chuyển Nổi bật thành {movie.IsFeatured} cho phim '{movie.Title}'.");
        return Ok(ApiResponse<bool>.Ok(movie.IsFeatured, $"Đã đổi trạng thái Nổi bật thành: {(movie.IsFeatured ? "Bật" : "Tắt")}."));
    }

    [HttpPost("movies/{id:int}/clone")]
    public async Task<ActionResult<ApiResponse<MovieDetailDto>>> CloneMovie(int id, [FromBody] CloneMovieRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
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

        var created = await context.Movies
            .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
            .Include(m => m.CastMembers)
            .Include(m => m.Episodes)
            .FirstAsync(m => m.Id == cloned.Id);

        return Ok(ApiResponse<MovieDetailDto>.Ok(MapMovieDetail(created), "Nhân bản phim thành công."));
    }

    [HttpGet("episodes/{episodeId:int}/streams")]
    public async Task<ActionResult<ApiResponse<List<StreamSourceDto>>>> GetStreamsForEpisode(int episodeId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var streams = await context.StreamSources
            .Where(s => s.EpisodeId == episodeId)
            .OrderBy(s => s.Position)
            .Select(s => new StreamSourceDto
            {
                Id = s.Position,
                EpisodeId = s.EpisodeId,
                Position = s.Position,
                ServerName = s.ServerName,
                ServerType = s.ServerType,
                Quality = s.Quality,
                StreamUrl = s.StreamUrl
            })
            .ToListAsync();

        return Ok(ApiResponse<List<StreamSourceDto>>.Ok(streams));
    }

    [HttpPost("episodes/{episodeId:int}/streams")]
    public async Task<ActionResult<ApiResponse<StreamSourceDto>>> AddStreamSource(int episodeId, [FromBody] UpsertStreamSourceRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var ep = await context.Episodes.FindAsync(episodeId);
        if (ep is null) return NotFound(ApiResponse<StreamSourceDto>.Fail("Không tìm thấy tập phim."));

        var existing = await context.StreamSources.Where(s => s.EpisodeId == episodeId).ToListAsync();
        var nextPos = existing.Count > 0 ? existing.Max(s => s.Position) + 1 : 1;

        var source = new StreamSourceEntity
        {
            EpisodeId = episodeId,
            Position = nextPos,
            ServerName = string.IsNullOrWhiteSpace(request.ServerName) ? $"Server {nextPos}" : request.ServerName.Trim(),
            ServerType = request.ServerType,
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "1080p" : request.Quality.Trim(),
            StreamUrl = request.StreamUrl.Trim()
        };

        context.StreamSources.Add(source);
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "ADD_STREAM", "StreamSource", $"{episodeId}-{nextPos}", $"Thêm nguồn stream '{source.ServerName}' ({source.Quality}) cho tập {episodeId}.");

        return Ok(ApiResponse<StreamSourceDto>.Ok(new StreamSourceDto
        {
            Id = source.Position,
            EpisodeId = source.EpisodeId,
            Position = source.Position,
            ServerName = source.ServerName,
            ServerType = source.ServerType,
            Quality = source.Quality,
            StreamUrl = source.StreamUrl
        }, "Đã thêm nguồn stream thành công."));
    }

    [HttpPut("episodes/{episodeId:int}/streams/{position:int}")]
    public async Task<ActionResult<ApiResponse<StreamSourceDto>>> UpdateStreamSource(int episodeId, int position, [FromBody] UpsertStreamSourceRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var stream = await context.StreamSources.FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == position);
        if (stream is null) return NotFound(ApiResponse<StreamSourceDto>.Fail("Không tìm thấy nguồn stream cần sửa."));

        stream.ServerName = request.ServerName.Trim();
        stream.ServerType = request.ServerType;
        stream.Quality = request.Quality.Trim();
        stream.StreamUrl = request.StreamUrl.Trim();

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "UPDATE_STREAM", "StreamSource", $"{episodeId}-{position}", $"Cập nhật nguồn stream vị trí {position} cho tập {episodeId}.");

        return Ok(ApiResponse<StreamSourceDto>.Ok(new StreamSourceDto
        {
            Id = stream.Position,
            EpisodeId = stream.EpisodeId,
            Position = stream.Position,
            ServerName = stream.ServerName,
            ServerType = stream.ServerType,
            Quality = stream.Quality,
            StreamUrl = stream.StreamUrl
        }, "Cập nhật nguồn stream thành công."));
    }

    [HttpDelete("episodes/{episodeId:int}/streams/{position:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteStreamSource(int episodeId, int position)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var stream = await context.StreamSources.FirstOrDefaultAsync(s => s.EpisodeId == episodeId && s.Position == position);
        if (stream is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy nguồn stream để xóa."));

        context.StreamSources.Remove(stream);
        await context.SaveChangesAsync();
        await LogAuditAsync(context, "DELETE_STREAM", "StreamSource", $"{episodeId}-{position}", $"Xóa nguồn stream vị trí {position} của tập {episodeId}.");
        return Ok(ApiResponse<bool>.Ok(true, "Đã xóa nguồn stream."));
    }

    [HttpPost("streams/probe")]
    public async Task<ActionResult<ApiResponse<ProbeStreamResultDto>>> ProbeStream([FromBody] ProbeStreamRequest request)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (isSafe, errorMessage, validatedUri) = await SafeStreamUrlValidator.ValidateUrlAsync(request.StreamUrl);
            if (!isSafe || validatedUri is null)
            {
                sw.Stop();
                return Ok(ApiResponse<ProbeStreamResultDto>.Ok(new ProbeStreamResultDto
                {
                    IsSuccess = false,
                    StatusCode = 400,
                    LatencyMs = sw.ElapsedMilliseconds,
                    ErrorMessage = errorMessage ?? "URL không hợp lệ hoặc bị chặn vì lý do an toàn mạng nội bộ."
                }));
            }

            using var client = httpClientFactory?.CreateClient() ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            client.Timeout = TimeSpan.FromSeconds(5);
            using var req = new HttpRequestMessage(HttpMethod.Head, validatedUri);
            req.Headers.UserAgent.ParseAdd("CinemaFlow-Probe/2026.1");

            var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (res.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, validatedUri);
                getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1024);
                res = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
            }
            sw.Stop();

            var result = new ProbeStreamResultDto
            {
                IsSuccess = res.IsSuccessStatusCode,
                StatusCode = (int)res.StatusCode,
                LatencyMs = sw.ElapsedMilliseconds,
                ContentType = res.Content.Headers.ContentType?.MediaType ?? "video/mp4"
            };

            return Ok(ApiResponse<ProbeStreamResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            sw.Stop();
            var result = new ProbeStreamResultDto
            {
                IsSuccess = false,
                StatusCode = 0,
                LatencyMs = sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
            return Ok(ApiResponse<ProbeStreamResultDto>.Ok(result));
        }
    }

    [HttpPost("episodes/batch-generate")]
    public async Task<ActionResult<ApiResponse<int>>> BatchGenerateEpisodes([FromBody] BatchEpisodeGenerateRequest request)
    {
        if (request.StartEpisode > request.EndEpisode)
        {
            return BadRequest(ApiResponse<int>.Fail("Tập bắt đầu không thể lớn hơn tập kết thúc."));
        }

        await using var context = await contextFactory.CreateDbContextAsync();
        var movie = await context.Movies.FindAsync(request.MovieId);
        if (movie is null) return NotFound(ApiResponse<int>.Fail("Không tìm thấy phim."));

        var existingEpisodes = (await context.Episodes
            .Where(e => e.MovieId == request.MovieId && e.EpisodeNumber >= request.StartEpisode && e.EpisodeNumber <= request.EndEpisode)
            .Select(e => e.EpisodeNumber)
            .ToListAsync())
            .ToHashSet();

        var createdCount = 0;
        for (var epNum = request.StartEpisode; epNum <= request.EndEpisode; epNum++)
        {
            if (existingEpisodes.Contains(epNum))
            {
                continue;
            }

            var title = request.TitlePattern.Replace("{n}", epNum.ToString());
            var streamUrl = request.StreamUrlPattern.Replace("{n}", epNum.ToString());

            var episode = new EpisodeEntity
            {
                MovieId = request.MovieId,
                EpisodeNumber = epNum,
                Title = title,
                DurationMinutes = request.DurationMinutes,
                ThumbnailUrl = movie.BackdropUrl,
                Overview = $"Diễn biến kịch tính của {title}."
            };

            episode.Streams.Add(new StreamSourceEntity
            {
                Position = 1,
                ServerName = request.ServerName,
                ServerType = request.ServerType,
                Quality = request.Quality,
                StreamUrl = streamUrl
            });

            context.Episodes.Add(episode);
            createdCount++;
        }

        if (request.EndEpisode > movie.TotalEpisodes) movie.TotalEpisodes = request.EndEpisode;
        if (request.EndEpisode > movie.CurrentEpisode) movie.CurrentEpisode = request.EndEpisode;

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "BATCH_GENERATE_EPISODES", "Movie", request.MovieId.ToString(), $"Tạo tự động {createdCount} tập cho phim '{movie.Title}'.");
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

        return Ok(ApiResponse<int>.Ok(createdCount, $"Đã tạo tự động thành công {createdCount} tập phim mới."));
    }

    [HttpGet("users/{id:guid}/details")]
    public async Task<ActionResult<ApiResponse<AdminUserDetailDto>>> GetUserDetail(Guid id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<AdminUserDetailDto>.Fail("Không tìm thấy người dùng."));

        var security = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        var watchlistCount = await context.UserLibrary.CountAsync(ul => ul.UserId == id);
        var reviewsCount = await context.Reviews.CountAsync(r => r.UserId == id);

        var recentPayments = await context.Payments
            .Where(p => p.UserId == id)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(10)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                UserId = p.UserId,
                PlanId = p.PlanId,
                PlanName = p.PlanName,
                Amount = p.Amount,
                Method = p.Method,
                Status = p.Status,
                CreatedAt = new DateTimeOffset(p.CreatedAtUtc, TimeSpan.Zero),
                PaidAt = p.PaidAtUtc.HasValue ? new DateTimeOffset(p.PaidAtUtc.Value, TimeSpan.Zero) : null,
                VipExpiresAt = p.VipExpiresAtUtc.HasValue ? new DateTimeOffset(p.VipExpiresAtUtc.Value, TimeSpan.Zero) : null
            })
            .ToListAsync();

        var recentReviews = await context.Reviews
            .Where(r => r.UserId == id)
            .Include(r => r.Movie)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(5)
            .Select(r => new AdminReviewDto
            {
                Id = r.Id,
                MovieId = r.MovieId,
                MovieTitle = r.Movie.Title,
                MoviePosterUrl = r.Movie.PosterUrl,
                UserId = r.UserId,
                UserFullName = user.FullName,
                UserEmail = user.Email,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = new DateTimeOffset(r.CreatedAtUtc, TimeSpan.Zero),
                AdminReply = r.AdminReply,
                AdminRepliedAt = r.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(r.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
                IsReported = r.IsReported
            })
            .ToListAsync();

        var isLocked = security?.LockedUntilUtc.HasValue == true && security.LockedUntilUtc.Value > UtcNow;

        var dto = new AdminUserDetailDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            MembershipTier = user.Membership,
            SubscriptionExpiresAt = user.VipExpiresAtUtc.HasValue ? new DateTimeOffset(user.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            CreatedAt = new DateTimeOffset(user.CreatedAtUtc, TimeSpan.Zero),
            Initials = GetInitials(user.FullName),
            IsAdmin = security?.IsAdmin ?? false,
            IsLocked = isLocked,
            LockedUntil = security?.LockedUntilUtc.HasValue == true ? new DateTimeOffset(security.LockedUntilUtc.Value, TimeSpan.Zero) : null,
            FailedLoginAttempts = security?.FailedLoginAttempts ?? 0,
            WatchlistCount = watchlistCount,
            ReviewsCount = reviewsCount,
            RecentPayments = recentPayments,
            RecentReviews = recentReviews
        };

        return Ok(ApiResponse<AdminUserDetailDto>.Ok(dto));
    }

    [HttpPut("users/{id:guid}/admin-role")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUserAdminRole(Guid id, [FromBody] AdminUpdateUserRoleRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy người dùng."));

        if (CallerUserId.HasValue && CallerUserId.Value == id && !request.IsAdmin)
        {
            return BadRequest(ApiResponse<bool>.Fail("Bạn không thể tự hạ quyền quản trị của chính mình."));
        }

        var security = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        if (security is null)
        {
            security = new UserSecurityEntity
            {
                UserId = id,
                IsAdmin = request.IsAdmin,
                PasswordIterations = 100000,
                FailedLoginAttempts = 0
            };
            context.Set<UserSecurityEntity>().Add(security);
        }
        else
        {
            if (security.IsAdmin && !request.IsAdmin)
            {
                var activeAdminCount = await context.Set<UserSecurityEntity>()
                    .CountAsync(s => s.IsAdmin && (s.LockedUntilUtc == null || s.LockedUntilUtc <= UtcNow));
                if (activeAdminCount <= 1)
                {
                    return BadRequest(ApiResponse<bool>.Fail("Không thể hạ quyền quản trị viên duy nhất còn lại trong hệ thống."));
                }

                // Revoke all active sessions upon administrative demotion
                var tokens = await context.AccessTokens.Where(t => t.UserId == id).ToListAsync();
                context.AccessTokens.RemoveRange(tokens);
                var mfaSessions = await context.MfaVerifiedSessions.Where(t => t.UserId == id).ToListAsync();
                context.MfaVerifiedSessions.RemoveRange(mfaSessions);
            }
            security.IsAdmin = request.IsAdmin;
        }

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "UPDATE_ADMIN_ROLE", "User", id.ToString(), $"Đổi quyền Quản trị của '{user.Email}' thành: {request.IsAdmin}.");

        return Ok(ApiResponse<bool>.Ok(security.IsAdmin, $"Đã cập nhật quyền Quản trị: {(security.IsAdmin ? "Bật" : "Tắt")}."));
    }

    [HttpPut("users/{id:guid}/lock-status")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateUserLockStatus(Guid id, [FromBody] AdminUpdateUserLockRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user is null) return NotFound(ApiResponse<bool>.Fail("Không tìm thấy người dùng."));

        if (CallerUserId.HasValue && CallerUserId.Value == id && request.IsLocked)
        {
            return BadRequest(ApiResponse<bool>.Fail("Bạn không thể tự khóa tài khoản quản trị của chính mình."));
        }

        var security = await context.Set<UserSecurityEntity>().FirstOrDefaultAsync(s => s.UserId == id);
        if (security is null)
        {
            security = new UserSecurityEntity
            {
                UserId = id,
                IsAdmin = false,
                PasswordIterations = 100000,
                FailedLoginAttempts = 0
            };
            context.Set<UserSecurityEntity>().Add(security);
        }

        if (request.IsLocked)
        {
            if (security.IsAdmin)
            {
                var otherActiveAdminCount = await context.Set<UserSecurityEntity>()
                    .CountAsync(s => s.IsAdmin && s.UserId != id && (s.LockedUntilUtc == null || s.LockedUntilUtc <= UtcNow));
                if (otherActiveAdminCount == 0)
                {
                    return BadRequest(ApiResponse<bool>.Fail("Không thể khóa quản trị viên duy nhất còn lại trong hệ thống."));
                }
            }

            security.LockedUntilUtc = UtcNow.AddMinutes(Math.Max(15, request.LockMinutes));

            // Immediately revoke all active sessions for locked user
            var tokens = await context.AccessTokens.Where(t => t.UserId == id).ToListAsync();
            context.AccessTokens.RemoveRange(tokens);
            var mfaSessions = await context.MfaVerifiedSessions.Where(t => t.UserId == id).ToListAsync();
            context.MfaVerifiedSessions.RemoveRange(mfaSessions);
        }
        else
        {
            security.LockedUntilUtc = null;
            security.FailedLoginAttempts = 0;
        }

        await context.SaveChangesAsync();
        var isLockedNow = security.LockedUntilUtc.HasValue && security.LockedUntilUtc.Value > UtcNow;
        await LogAuditAsync(context, "UPDATE_LOCK_STATUS", "User", id.ToString(), $"Cập nhật trạng thái khóa của '{user.Email}': {(isLockedNow ? "Khóa" : "Mở khóa")}.");

        return Ok(ApiResponse<bool>.Ok(isLockedNow, $"Đã cập nhật trạng thái tài khoản: {(isLockedNow ? "Đã khóa" : "Hoạt động")}."));
    }

    [HttpPost("reviews/{id:guid}/reply")]
    public async Task<ActionResult<ApiResponse<AdminReviewDto>>> ReplyToReview(Guid id, [FromBody] AdminReviewModerationRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var review = await context.Reviews
            .Include(r => r.Movie)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review is null) return NotFound(ApiResponse<AdminReviewDto>.Fail("Không tìm thấy đánh giá."));

        if (request.AdminReply != null)
        {
            review.AdminReply = request.AdminReply.Trim();
            review.AdminRepliedAtUtc = UtcNow;
        }

        if (request.IsReported.HasValue)
        {
            review.IsReported = request.IsReported.Value;
        }

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "MODERATE_REVIEW", "Review", id.ToString(), $"Kiểm duyệt đánh giá của '{review.User.Email}' trên phim '{review.Movie.Title}'.");

        var dto = new AdminReviewDto
        {
            Id = review.Id,
            MovieId = review.MovieId,
            MovieTitle = review.Movie.Title,
            MoviePosterUrl = review.Movie.PosterUrl,
            UserId = review.UserId,
            UserFullName = review.User.FullName,
            UserEmail = review.User.Email,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = new DateTimeOffset(review.CreatedAtUtc, TimeSpan.Zero),
            AdminReply = review.AdminReply,
            AdminRepliedAt = review.AdminRepliedAtUtc.HasValue ? new DateTimeOffset(review.AdminRepliedAtUtc.Value, TimeSpan.Zero) : null,
            IsReported = review.IsReported
        };

        return Ok(ApiResponse<AdminReviewDto>.Ok(dto, "Cập nhật kiểm duyệt đánh giá thành công."));
    }


    [HttpPost("payments/{id:guid}/mark-paid")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> MarkPaymentPaid(Guid id, [FromBody] ManualPaymentApprovalRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var payment = await context.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound(ApiResponse<PaymentDto>.Fail("Không tìm thấy đơn thanh toán."));

        if (payment.Status == PaymentStatus.Completed)
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã được thanh toán trước đó."));
        }

        payment.Status = PaymentStatus.Completed;
        payment.PaidAtUtc = UtcNow;
        payment.TransactionReference = request.TransactionReference.Trim();
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            payment.Note = request.Note.Trim();
        }

        var plan = await context.VipPlans.FindAsync(payment.PlanId);
        var extendDays = plan?.DurationDays ?? (payment.PlanId switch
        {
            "vip-365" => 365,
            "vip-180" => 180,
            "vip-90" => 90,
            _ => 30
        });

        var user = payment.User;
        if (user is not null)
        {
            user.Membership = MembershipTier.Vip;
            var currentExpiry = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > UtcNow
                ? user.VipExpiresAtUtc.Value
                : UtcNow;
            user.VipExpiresAtUtc = currentExpiry.AddDays(extendDays);
            payment.VipExpiresAtUtc = user.VipExpiresAtUtc;
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (context.Database.IsRelational())
        {
            tx = await context.Database.BeginTransactionAsync();
        }
        try
        {
            await context.SaveChangesAsync();
            await LogAuditAsync(context, "APPROVE_PAYMENT", "Payment", payment.OrderCode, $"Duyệt thanh toán thủ công cho đơn '{payment.OrderCode}', mã GD: {request.TransactionReference}.");
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync();
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã được xử lý hoặc thanh toán trước đó."));
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

        var dto = new PaymentDto
        {
            Id = payment.Id,
            OrderCode = payment.OrderCode,
            UserId = payment.UserId,
            UserEmail = payment.User?.Email,
            UserFullName = payment.User?.FullName,
            PlanId = payment.PlanId,
            PlanName = payment.PlanName,
            Amount = payment.Amount,
            Method = payment.Method,
            Status = payment.Status,
            CreatedAt = new DateTimeOffset(payment.CreatedAtUtc, TimeSpan.Zero),
            PaidAt = payment.PaidAtUtc.HasValue ? new DateTimeOffset(payment.PaidAtUtc.Value, TimeSpan.Zero) : null,
            VipExpiresAt = payment.VipExpiresAtUtc.HasValue ? new DateTimeOffset(payment.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            TransactionReference = payment.TransactionReference,
            Note = payment.Note
        };

        return Ok(ApiResponse<PaymentDto>.Ok(dto, "Đã xác nhận thanh toán và gia hạn VIP cho người dùng thành công."));
    }

    [HttpPost("payments/{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<PaymentDto>>> RejectPayment(Guid id, [FromBody] PaymentRejectionRequest request)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var payment = await context.Payments.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == id);
        if (payment is null) return NotFound(ApiResponse<PaymentDto>.Fail("Không tìm thấy đơn thanh toán."));

        if (payment.Status == PaymentStatus.Completed)
        {
            return BadRequest(ApiResponse<PaymentDto>.Fail("Đơn hàng này đã hoàn tất, không thể từ chối."));
        }

        payment.Status = PaymentStatus.Failed;
        payment.Note = string.IsNullOrWhiteSpace(payment.Note)
            ? $"Từ chối duyệt: {request.Reason.Trim()}"
            : $"{payment.Note} | Từ chối: {request.Reason.Trim()}";

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "REJECT_PAYMENT", "Payment", payment.OrderCode, $"Từ chối duyệt đơn '{payment.OrderCode}', lý do: {request.Reason}.");

        var dto = new PaymentDto
        {
            Id = payment.Id,
            OrderCode = payment.OrderCode,
            UserId = payment.UserId,
            UserEmail = payment.User?.Email,
            UserFullName = payment.User?.FullName,
            PlanId = payment.PlanId,
            PlanName = payment.PlanName,
            Amount = payment.Amount,
            Method = payment.Method,
            Status = payment.Status,
            CreatedAt = new DateTimeOffset(payment.CreatedAtUtc, TimeSpan.Zero),
            PaidAt = payment.PaidAtUtc.HasValue ? new DateTimeOffset(payment.PaidAtUtc.Value, TimeSpan.Zero) : null,
            VipExpiresAt = payment.VipExpiresAtUtc.HasValue ? new DateTimeOffset(payment.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            TransactionReference = payment.TransactionReference,
            Note = payment.Note
        };

        return Ok(ApiResponse<PaymentDto>.Ok(dto, "Đã từ chối đơn thanh toán thành công."));
    }

    [HttpGet("vip-plans")]
    public async Task<ActionResult<ApiResponse<List<VipPlanDto>>>> GetVipPlans([FromServices] IPaymentService paymentSvc)
    {
        var plans = await paymentSvc.GetAdminPlansAsync();
        return Ok(ApiResponse<List<VipPlanDto>>.Ok(plans));
    }

    [HttpPut("vip-plans/{id}")]
    public async Task<ActionResult<ApiResponse<VipPlanDto>>> UpsertVipPlan(string id, [FromBody] UpsertVipPlanRequest request, [FromServices] IPaymentService paymentSvc)
    {
        request.Id = id;
        var updated = await paymentSvc.UpsertPlanAsync(request);
        if (updated is null) return BadRequest(ApiResponse<VipPlanDto>.Fail("Không thể cập nhật gói VIP."));

        await using var context = await contextFactory.CreateDbContextAsync();
        await LogAuditAsync(context, "UPDATE_VIP_PLAN", "VipPlan", id, $"Cập nhật cấu hình gói VIP '{updated.Name}' ({updated.Price:N0}đ, {updated.DurationDays} ngày).");

        return Ok(ApiResponse<VipPlanDto>.Ok(updated, $"Đã lưu cấu hình gói '{updated.Name}' thành công."));
    }

    [HttpPost("users/grant-vip")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GrantVipToUser([FromBody] ManualVipGrantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserEmailOrUsername))
        {
            return BadRequest(ApiResponse<UserDto>.Fail("Vui lòng nhập Email hoặc Họ tên người dùng."));
        }

        await using var context = await contextFactory.CreateDbContextAsync();
        var rawTerm = request.UserEmailOrUsername.Trim();
        var termLower = rawTerm.ToLower();
        var termUpper = rawTerm.ToUpper();

        UserEntity? user = null;
        if (Guid.TryParse(rawTerm, out var parsedGuid))
        {
            user = await context.Users.FirstOrDefaultAsync(u => u.Id == parsedGuid);
        }

        if (user is null)
        {
            user = await context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == termUpper || u.Email.ToLower() == termLower);
        }

        if (user is null)
        {
            var matchingUsers = await context.Users.Where(u => u.FullName.ToLower() == termLower).Take(2).ToListAsync();
            if (matchingUsers.Count > 1)
            {
                return BadRequest(ApiResponse<UserDto>.Fail($"Có nhiều người dùng cùng tên '{rawTerm}'. Vui lòng nhập chính xác địa chỉ Email để cấp quyền VIP."));
            }
            user = matchingUsers.FirstOrDefault();
        }

        if (user is null)
        {
            return NotFound(ApiResponse<UserDto>.Fail($"Không tìm thấy người dùng có thông tin '{rawTerm}'."));
        }

        var now = UtcNow;
        var currentExpiry = user.VipExpiresAtUtc.HasValue && user.VipExpiresAtUtc.Value > now
            ? user.VipExpiresAtUtc.Value
            : now;

        user.Membership = MembershipTier.Vip;
        user.VipExpiresAtUtc = currentExpiry.AddDays(request.Days);

        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderCode = $"GRANT{Guid.NewGuid():N}"[..26],
            UserId = user.Id,
            PlanId = $"manual-{request.Days}d",
            PlanName = $"Đặc cách VIP ({request.Days} ngày)",
            Amount = 0,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Completed,
            CreatedAtUtc = now,
            PaidAtUtc = now,
            VipExpiresAtUtc = user.VipExpiresAtUtc,
            TransactionReference = "ADMIN-MANUAL-GRANT",
            Note = string.IsNullOrWhiteSpace(request.Reason) ? "Đặc cách cấp VIP trực tiếp từ quản trị viên" : $"Đặc cách: {request.Reason.Trim()}"
        };
        context.Payments.Add(payment);
        context.Notifications.Add(new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Cấp quyền VIP thành công",
            Copy = $"Tài khoản của bạn đã được quản trị viên cấp {request.Days} ngày VIP.",
            Icon = "Crown",
            ActionUrl = "/vip",
            CreatedAtUtc = now
        });

        await context.SaveChangesAsync();
        await LogAuditAsync(context, "GRANT_VIP", "User", user.Id.ToString(), $"Cấp {request.Days} ngày VIP cho '{user.Email}'. Lý do: {request.Reason}.");

        var userDto = new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            MembershipTier = user.Membership,
            SubscriptionExpiresAt = user.VipExpiresAtUtc.HasValue ? new DateTimeOffset(user.VipExpiresAtUtc.Value, TimeSpan.Zero) : null,
            CreatedAt = new DateTimeOffset(user.CreatedAtUtc, TimeSpan.Zero),
            Initials = GetInitials(user.FullName)
        };

        return Ok(ApiResponse<UserDto>.Ok(userDto, $"Đã cấp thành công {request.Days} ngày VIP cho thành viên '{user.FullName}' ({user.Email})."));
    }

    private static MovieDetailDto MapMovieDetail(MovieEntity m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        OriginalTitle = m.OriginalTitle,
        Slug = m.Slug,
        Overview = m.Overview,
        PosterUrl = m.PosterUrl,
        BackdropUrl = m.BackdropUrl,
        TrailerUrl = m.TrailerUrl,
        Rating = m.Rating,
        VoteCount = m.VoteCount,
        ReleaseYear = m.ReleaseYear,
        DurationMinutes = m.DurationMinutes,
        AgeRating = m.AgeRating,
        Quality = m.Quality,
        Type = m.Type,
        Genres = m.MovieGenres.Select(mg => mg.Genre.Name).ToList(),
        Country = m.Country,
        ViewCount = m.ViewCount,
        IsTrending = m.IsTrending,
        IsFeatured = m.IsFeatured,
        TotalEpisodes = m.TotalEpisodes,
        CurrentEpisode = m.CurrentEpisode,
        Director = m.Director,
        Cast = m.CastMembers.OrderBy(c => c.Position).Select(c => c.Name).ToList(),
        Episodes = m.Episodes.OrderBy(e => e.EpisodeNumber).Select(e => new EpisodeDto
        {
            Id = e.Id,
            MovieId = e.MovieId,
            EpisodeNumber = e.EpisodeNumber,
            Title = e.Title,
            DurationMinutes = e.DurationMinutes,
            ThumbnailUrl = e.ThumbnailUrl,
            Overview = e.Overview
        }).ToList()
    };

    private static string GetInitials(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "AD";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }

    private static string GenerateSlug(string text)
    {
        var slug = text.ToLowerInvariant().Trim();
        var builder = new System.Text.StringBuilder();
        foreach (var c in slug)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (c == ' ' || c == '-' || c == '_') builder.Append('-');
        }
        var res = builder.ToString();
        while (res.Contains("--")) res = res.Replace("--", "-");
        return res.Trim('-');
    }
}
