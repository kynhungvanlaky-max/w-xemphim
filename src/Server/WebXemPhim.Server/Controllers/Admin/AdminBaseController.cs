using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Models;

namespace WebXemPhim.Server.Controllers.Admin;

[ApiController]
[Authorize(Policy = "Admin")]
public abstract class AdminBaseController(
    IDbContextFactory<AppDbContext> contextFactory,
    TimeProvider timeProvider,
    IWebHostEnvironment? env = null,
    IHttpClientFactory? httpClientFactory = null) : ControllerBase
{
    protected readonly IDbContextFactory<AppDbContext> ContextFactory = contextFactory;
    protected readonly TimeProvider TimeProvider = timeProvider;
    protected readonly IWebHostEnvironment? Env = env;
    protected readonly IHttpClientFactory? HttpClientFactory = httpClientFactory;

    protected DateTime UtcNow => TimeProvider.GetUtcNow().UtcDateTime;

    protected Guid? CallerUserId
    {
        get
        {
            var principal = ControllerContext?.HttpContext?.User;
            if (principal is null) return null;
            var claimValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claimValue, out var id) ? id : null;
        }
    }

    protected string CallerAdminEmail => User?.Identity?.Name ?? "admin@cinemaflow.local";

    protected async Task LogAuditAsync(AppDbContext context, string action, string entityType, string entityId, string summary, string? detailsJson = null)
    {
        try
        {
            context.AuditLogs.Add(new AuditLogEntity
            {
                TimestampUtc = UtcNow,
                AdminEmail = CallerAdminEmail,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary,
                DetailsJson = detailsJson
            });
            await context.SaveChangesAsync();
        }
        catch
        {
            // Logging audit failures should not fail the primary business operation
        }
    }

    protected static string GetInitials(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "AD";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpper();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }

    protected static string GenerateSlug(string text)
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

    protected static MovieDetailDto MapMovieDetail(MovieEntity m) => new()
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
        Genres = m.MovieGenres.Select(mg => mg.Genre?.Name ?? string.Empty).Where(name => !string.IsNullOrEmpty(name)).ToList(),
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
}
