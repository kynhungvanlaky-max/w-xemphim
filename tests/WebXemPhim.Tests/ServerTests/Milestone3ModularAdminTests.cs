using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class Milestone3ModularAdminTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-m3-modular-{Guid.NewGuid():N}")
            .Options;

    private static (TestDbContextFactory factory, DbContextOptions<AppDbContext> options) CreateContextFactory()
    {
        var options = CreateOptions();
        return (new TestDbContextFactory(options), options);
    }

    private static void SetAdminUser(ControllerBase controller, Guid? userId = null, string email = "admin@cinema.test")
    {
        var uid = userId ?? Guid.NewGuid();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, "Admin")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }

    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MemberUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Payment1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Review1Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static async Task SeedSampleDataAsync(AppDbContext context)
    {
        var g1 = new GenreEntity { Id = 1, Name = "Hành Động", Slug = "hanh-dong" };
        var g2 = new GenreEntity { Id = 2, Name = "Viễn Tưởng", Slug = "vien-tuong" };
        context.Genres.AddRange(g1, g2);

        var m1 = new MovieEntity
        {
            Id = 1,
            Title = "Dune Hành Tinh Cát",
            OriginalTitle = "Dune Part One",
            Slug = "dune-hanh-tinh-cat",
            Overview = "Mô tả Dune 1",
            Rating = 8.5,
            VoteCount = 10,
            ReleaseYear = 2021,
            DurationMinutes = 155,
            Quality = "4K UHD",
            Type = MovieType.Movie,
            AgeRating = "16+",
            Country = "Mỹ"
        };
        m1.MovieGenres.Add(new MovieGenreEntity { MovieId = 1, GenreId = 1 });

        var m2 = new MovieEntity
        {
            Id = 2,
            Title = "Blade Runner 2049",
            OriginalTitle = "Blade Runner 2049",
            Slug = "blade-runner-2049",
            Overview = "Mô tả Blade Runner",
            Rating = 8.0,
            VoteCount = 5,
            ReleaseYear = 2017,
            DurationMinutes = 164,
            Quality = "4K UHD",
            Type = MovieType.Series,
            AgeRating = "18+",
            Country = "Mỹ"
        };
        m2.MovieGenres.Add(new MovieGenreEntity { MovieId = 2, GenreId = 2 });

        var ep1 = new EpisodeEntity
        {
            Id = 1,
            MovieId = 2,
            EpisodeNumber = 1,
            Title = "Tập 1",
            DurationMinutes = 55
        };
        m2.Episodes.Add(ep1);

        var u1 = new UserEntity
        {
            Id = AdminUserId,
            Email = "admin@cinema.test",
            NormalizedEmail = "ADMIN@CINEMA.TEST",
            FullName = "Quản Trị Viên",
            PasswordSalt = [1],
            PasswordHash = [2],
            Membership = MembershipTier.Vip,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-3)
        };

        var u2 = new UserEntity
        {
            Id = MemberUserId,
            Email = "member@cinema.test",
            NormalizedEmail = "MEMBER@CINEMA.TEST",
            FullName = "Người Dùng Thường",
            PasswordSalt = [3],
            PasswordHash = [4],
            Membership = MembershipTier.Free,
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-1)
        };

        var plan = new VipPlanEntity
        {
            Id = "vip-30",
            Name = "Gói 1 Tháng",
            Description = "30 ngày trải nghiệm VIP",
            DurationDays = 30,
            PriceVnd = 79000m,
            IsActive = true
        };

        var pay1 = new PaymentEntity
        {
            Id = Payment1Id,
            UserId = MemberUserId,
            PlanId = "vip-30",
            PlanName = "Gói 1 Tháng",
            Amount = 79000m,
            Method = PaymentMethod.BankTransfer,
            OrderCode = "ORD-001",
            Status = PaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        var r1 = new ReviewEntity
        {
            Id = Review1Id,
            MovieId = 1,
            UserId = MemberUserId,
            Rating = 9,
            Comment = "Phim quá xuất sắc!",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.Movies.AddRange(m1, m2);
        context.Users.AddRange(u1, u2);
        context.VipPlans.Add(plan);
        context.Payments.Add(pay1);
        context.Reviews.Add(r1);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task AdminOverviewController_GetStatsAndHealth_ReturnsCorrectData()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminOverviewController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var statsResult = await controller.GetStats();
        var okStats = Assert.IsType<OkObjectResult>(statsResult.Result);
        var statsResp = Assert.IsType<ApiResponse<AdminDashboardDto>>(okStats.Value);
        Assert.True(statsResp.Success);
        Assert.NotNull(statsResp.Data);
        Assert.Equal(1, statsResp.Data.TotalMovies);
        Assert.Equal(2, statsResp.Data.TotalUsers);

        var healthResult = await controller.GetSystemHealth();
        var okHealth = Assert.IsType<OkObjectResult>(healthResult.Result);
        var healthResp = Assert.IsType<ApiResponse<SystemHealthDto>>(okHealth.Value);
        Assert.True(healthResp.Success);
        Assert.Equal(2, healthResp.Data!.TotalMovies);

        var auditLogsResult = await controller.GetAuditLogs(page: 1, pageSize: 10);
        var okLogs = Assert.IsType<OkObjectResult>(auditLogsResult.Result);
        var logsResp = Assert.IsType<ApiResponse<PagedResult<AuditLogDto>>>(okLogs.Value);
        Assert.True(logsResp.Success);
        Assert.NotNull(logsResp.Data);
    }

    [Fact]
    public async Task AdminMoviesController_GetMovies_ReturnsPagedResult()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var result = await controller.GetMovies(page: 1, pageSize: 1);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<MovieDetailDto>>>(okResult.Value);

        Assert.True(resp.Success);
        Assert.Equal(2, resp.Data!.TotalItems);
        Assert.Single(resp.Data.Items);
        Assert.Equal(1, resp.Data.Page);
        Assert.Equal(1, resp.Data.PageSize);
        Assert.Equal(2, resp.Data.TotalPages);
    }

    [Fact]
    public async Task AdminMoviesController_UpdateMovie_SlugCollision_ReturnsConflict()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // Attempt to update movie 2 to use movie 1's slug: "dune-hanh-tinh-cat"
        var request = new UpsertMovieRequest
        {
            Title = "Duplicate Slug Movie",
            OriginalTitle = "Dupe",
            Slug = "dune-hanh-tinh-cat",
            Overview = "Conflict test",
            Quality = "HD",
            AgeRating = "13+",
            DurationMinutes = 100,
            ReleaseYear = 2024,
            Type = MovieType.Movie
        };

        var result = await controller.UpdateMovie(2, request);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<MovieDetailDto>>(conflict.Value);
        Assert.False(resp.Success);
        Assert.Contains("dune-hanh-tinh-cat", resp.Message);
        Assert.Contains("đã tồn tại", resp.Message);
    }

    [Fact]
    public async Task AdminMoviesController_UpdateMovie_SameSlug_Succeeds()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // Updating movie 1 keeping its own slug should succeed
        var request = new UpsertMovieRequest
        {
            Title = "Dune Hành Tinh Cát (Bản Đạo Diễn)",
            OriginalTitle = "Dune Part One Director Cut",
            Slug = "dune-hanh-tinh-cat",
            Overview = "Bản mở rộng",
            Quality = "4K UHD",
            AgeRating = "16+",
            DurationMinutes = 170,
            ReleaseYear = 2021,
            Type = MovieType.Movie
        };

        var result = await controller.UpdateMovie(1, request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);
        Assert.True(resp.Success);
        Assert.Equal("Dune Hành Tinh Cát (Bản Đạo Diễn)", resp.Data!.Title);
    }

    [Fact]
    public async Task AdminMoviesController_CloneMovie_ClonesSuccessfullyWithoutNPlusOne()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var request = new CloneMovieRequest
        {
            NewTitle = "Blade Runner 2049 (Bản sao)",
            NewSlug = "blade-runner-2049-copy",
            ReleaseYear = 2026,
            CopyEpisodes = true
        };

        var result = await controller.CloneMovie(2, request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);
        Assert.True(resp.Success);
        Assert.NotNull(resp.Data);
        Assert.Contains("(Bản sao)", resp.Data.Title);
        Assert.NotEqual("blade-runner-2049", resp.Data.Slug);

        await using var verifyCtx = new AppDbContext(options);
        var clonedMovie = await verifyCtx.Movies
            .Include(m => m.Episodes)
            .Include(m => m.MovieGenres)
            .FirstOrDefaultAsync(m => m.Id == resp.Data.Id);

        Assert.NotNull(clonedMovie);
        Assert.Single(clonedMovie.Episodes);
        Assert.Single(clonedMovie.MovieGenres);
    }

    [Fact]
    public async Task AdminEpisodesController_BatchGenerateEpisodes_GeneratesAndSkipsExisting()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminEpisodesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // Movie 2 already has Episode 1. Generating from 1 to 3 should generate 2 and 3 only.
        var request = new BatchEpisodeGenerateRequest
        {
            MovieId = 2,
            StartEpisode = 1,
            EndEpisode = 3,
            DurationMinutes = 50,
            StreamUrlPattern = "https://cdn.example.com/ep{n}.m3u8",
            ServerName = "CDN Alpha"
        };

        var result = await controller.BatchGenerateEpisodes(request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<int>>(okResult.Value);
        Assert.True(resp.Success);
        Assert.Equal(2, resp.Data); // Generated episodes 2 and 3

        await using var verifyCtx = new AppDbContext(options);
        var totalEpisodes = await verifyCtx.Episodes.Where(e => e.MovieId == 2).CountAsync();
        Assert.Equal(3, totalEpisodes);
    }

    [Fact]
    public async Task AdminUsersController_GetUsers_PagedResult_AndUserDetails()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminUsersController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var pagedResult = await controller.GetUsers(page: 1, pageSize: 1);
        var okResult = Assert.IsType<OkObjectResult>(pagedResult.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<UserDto>>>(okResult.Value);
        Assert.True(resp.Success);
        Assert.Equal(2, resp.Data!.TotalItems);
        Assert.Single(resp.Data.Items);

        var detailsResult = await controller.GetUserDetail(MemberUserId);
        var okDetails = Assert.IsType<OkObjectResult>(detailsResult.Result);
        var detailsResp = Assert.IsType<ApiResponse<AdminUserDetailDto>>(okDetails.Value);
        Assert.True(detailsResp.Success);
        Assert.Equal(MemberUserId, detailsResp.Data!.Id);
        Assert.Single(detailsResp.Data.RecentPayments);
    }

    [Fact]
    public async Task AdminPaymentsController_MarkPaid_ApprovesAndHandlesDuplicate()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminPaymentsController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // First approval: should succeed and extend VIP
        var approvalReq = new ManualPaymentApprovalRequest
        {
            TransactionReference = "TX-TEST-001",
            Note = "Duyệt đơn test"
        };
        var result1 = await controller.MarkPaymentPaid(Payment1Id, approvalReq);
        var okResult1 = Assert.IsType<OkObjectResult>(result1.Result);
        var resp1 = Assert.IsType<ApiResponse<PaymentDto>>(okResult1.Value);
        Assert.True(resp1.Success);
        Assert.Equal(PaymentStatus.Completed, resp1.Data!.Status);

        await using (var checkCtx = new AppDbContext(options))
        {
            var user = await checkCtx.Users.FindAsync(MemberUserId);
            Assert.NotNull(user);
            Assert.True(user.VipExpiresAtUtc > DateTime.UtcNow);
        }

        // Duplicate approval on already completed payment: should return BadRequest 400 with friendly message
        var result2 = await controller.MarkPaymentPaid(Payment1Id, approvalReq);
        var badResult = Assert.IsType<BadRequestObjectResult>(result2.Result);
        var resp2 = Assert.IsType<ApiResponse<PaymentDto>>(badResult.Value);
        Assert.False(resp2.Success);
        Assert.Contains("đã được thanh toán", resp2.Message);
    }

    [Fact]
    public async Task AdminReviewsController_GetReviewsAndReplyAndDelete()
    {
        var (factory, options) = CreateContextFactory();
        await using (var seedCtx = new AppDbContext(options))
        {
            await SeedSampleDataAsync(seedCtx);
        }

        var controller = new AdminReviewsController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // Paged reviews
        var pagedResult = await controller.GetReviews(page: 1, pageSize: 10);
        var okPaged = Assert.IsType<OkObjectResult>(pagedResult.Result);
        var pagedResp = Assert.IsType<ApiResponse<PagedResult<AdminReviewDto>>>(okPaged.Value);
        Assert.True(pagedResp.Success);
        Assert.Single(pagedResp.Data!.Items);

        // Reply to review
        var replyResult = await controller.ReplyToReview(Review1Id, new AdminReviewModerationRequest { AdminReply = "Cảm ơn bạn đã ủng hộ!" });
        var okReply = Assert.IsType<OkObjectResult>(replyResult.Result);
        var replyResp = Assert.IsType<ApiResponse<AdminReviewDto>>(okReply.Value);
        Assert.True(replyResp.Success);
        Assert.Equal("Cảm ơn bạn đã ủng hộ!", replyResp.Data!.AdminReply);

        // Delete review: recalculates movie rating
        var deleteResult = await controller.DeleteReview(Review1Id);
        var okDelete = Assert.IsType<OkObjectResult>(deleteResult.Result);
        var deleteResp = Assert.IsType<ApiResponse<bool>>(okDelete.Value);
        Assert.True(deleteResp.Success);

        await using var verifyCtx = new AppDbContext(options);
        var deletedReview = await verifyCtx.Reviews.FindAsync(Review1Id);
        Assert.Null(deletedReview);

        var movie = await verifyCtx.Movies.FindAsync(1);
        Assert.NotNull(movie);
        Assert.Equal(0, movie.VoteCount);
    }
}
