using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM3AdminStressTests
{
    private static DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"webxemphim-challenger-m3-admin-{Guid.NewGuid():N}")
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

    private static readonly Guid AdminUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MemberUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Payment1Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Review1Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

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
        var s1 = new UserSecurityEntity
        {
            UserId = AdminUserId,
            IsAdmin = true,
            PasswordIterations = 100000
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
        var s2 = new UserSecurityEntity
        {
            UserId = MemberUserId,
            IsAdmin = false,
            PasswordIterations = 100000
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
            OrderCode = "ORD-CHALLENGER-001",
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
        context.UserSecurity.AddRange(s1, s2);
        context.VipPlans.Add(plan);
        context.Payments.Add(pay1);
        context.Reviews.Add(r1);

        await context.SaveChangesAsync();
    }

    // =========================================================================
    // SECTION 1: PAGINATION EDGE CASES ACROSS ALL 4 ADMIN CONTROLLERS
    // =========================================================================

    [Theory]
    [InlineData(0, 20, 1, 20)]         // page <= 0 falls back to page 1
    [InlineData(-5, 20, 1, 20)]        // negative page falls back to page 1
    [InlineData(1, 0, 1, 20)]          // pageSize <= 0 falls back to pageSize 20
    [InlineData(1, -10, 1, 20)]        // negative pageSize falls back to 20
    [InlineData(1, 500, 1, 500)]       // huge pageSize returns all items cleanly
    [InlineData(99999, 20, 99999, 20)] // out-of-bound page returns empty items safely
    public async Task AdminMoviesController_PaginationEdgeCases_HandledGracefully(
        int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var actionResult = await controller.GetMovies(page: inputPage, pageSize: inputPageSize);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<MovieDetailDto>>>(okResult.Value);

        Assert.True(resp.Success);
        Assert.NotNull(resp.Data);
        Assert.Equal(expectedPage, resp.Data.Page);
        Assert.Equal(expectedPageSize, resp.Data.PageSize);
        Assert.Equal(2, resp.Data.TotalItems);

        if (inputPage == 99999)
        {
            Assert.Empty(resp.Data.Items);
            Assert.False(resp.Data.HasNextPage);
            Assert.True(resp.Data.HasPreviousPage);
        }
        else
        {
            Assert.Equal(2, resp.Data.Items.Count);
        }
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-10, 20, 1, 20)]
    [InlineData(1, 0, 1, 20)]
    [InlineData(1, -50, 1, 20)]
    [InlineData(1, 1000, 1, 1000)]
    [InlineData(99999, 20, 99999, 20)]
    public async Task AdminUsersController_PaginationEdgeCases_HandledGracefully(
        int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminUsersController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var actionResult = await controller.GetUsers(page: inputPage, pageSize: inputPageSize);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<UserDto>>>(okResult.Value);

        Assert.True(resp.Success);
        Assert.NotNull(resp.Data);
        Assert.Equal(expectedPage, resp.Data.Page);
        Assert.Equal(expectedPageSize, resp.Data.PageSize);
        Assert.Equal(2, resp.Data.TotalItems);

        if (inputPage == 99999)
        {
            Assert.Empty(resp.Data.Items);
        }
        else
        {
            Assert.Equal(2, resp.Data.Items.Count);
        }
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-99, 20, 1, 20)]
    [InlineData(1, 0, 1, 20)]
    [InlineData(1, -25, 1, 20)]
    [InlineData(1, 500, 1, 500)]
    [InlineData(99999, 20, 99999, 20)]
    public async Task AdminPaymentsController_PaginationEdgeCases_HandledGracefully(
        int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminPaymentsController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var actionResult = await controller.GetPayments(page: inputPage, pageSize: inputPageSize);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<PaymentDto>>>(okResult.Value);

        Assert.True(resp.Success);
        Assert.NotNull(resp.Data);
        Assert.Equal(expectedPage, resp.Data.Page);
        Assert.Equal(expectedPageSize, resp.Data.PageSize);
        Assert.Equal(1, resp.Data.TotalItems);

        if (inputPage == 99999)
        {
            Assert.Empty(resp.Data.Items);
        }
        else
        {
            Assert.Single(resp.Data.Items);
        }
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-1, 20, 1, 20)]
    [InlineData(1, 0, 1, 20)]
    [InlineData(1, -100, 1, 20)]
    [InlineData(1, 500, 1, 500)]
    [InlineData(99999, 20, 99999, 20)]
    public async Task AdminReviewsController_PaginationEdgeCases_HandledGracefully(
        int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminReviewsController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var actionResult = await controller.GetReviews(page: inputPage, pageSize: inputPageSize);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var resp = Assert.IsType<ApiResponse<PagedResult<AdminReviewDto>>>(okResult.Value);

        Assert.True(resp.Success);
        Assert.NotNull(resp.Data);
        Assert.Equal(expectedPage, resp.Data.Page);
        Assert.Equal(expectedPageSize, resp.Data.PageSize);
        Assert.Equal(1, resp.Data.TotalItems);

        if (inputPage == 99999)
        {
            Assert.Empty(resp.Data.Items);
        }
        else
        {
            Assert.Single(resp.Data.Items);
        }
    }

    // =========================================================================
    // SECTION 2: SLUG UNIQUENESS & CONFLICT HANDLING
    // =========================================================================

    [Fact]
    public async Task AdminMoviesController_UpdateMovie_CollisionWithOtherMovieSlug_Returns409Conflict()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // Movie 2 attempts to claim Movie 1's slug "dune-hanh-tinh-cat"
        var request = new UpsertMovieRequest
        {
            Title = "Blade Runner Collision",
            OriginalTitle = "Blade Runner 2049",
            Slug = "dune-hanh-tinh-cat",
            Overview = "Conflict attempt",
            ReleaseYear = 2024,
            Quality = "HD",
            Type = MovieType.Movie
        };

        var result = await controller.UpdateMovie(2, request);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<MovieDetailDto>>(conflict.Value);

        Assert.False(resp.Success);
        Assert.Contains("dune-hanh-tinh-cat", resp.Message);
        Assert.Contains("đã tồn tại", resp.Message);

        // Verify in DB: Movie 2 slug must NOT be changed
        await using var verifyCtx = new AppDbContext(options);
        var movie2 = await verifyCtx.Movies.FindAsync(2);
        Assert.NotNull(movie2);
        Assert.Equal("blade-runner-2049", movie2.Slug);
    }

    [Theory]
    [InlineData("  DUNE-HANH-TINH-CAT  ")]
    [InlineData("Dune-Hanh-Tinh-Cat")]
    [InlineData("dune_hanh_tinh_cat")]
    public async Task AdminMoviesController_UpdateMovie_NormalizedSlugCollision_Returns409Conflict(string dirtySlug)
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var request = new UpsertMovieRequest
        {
            Title = "Blade Runner Dirty Slug",
            OriginalTitle = "Blade Runner 2049",
            Slug = dirtySlug,
            Overview = "Normalized dirty conflict attempt",
            ReleaseYear = 2024,
            Quality = "HD",
            Type = MovieType.Movie
        };

        var result = await controller.UpdateMovie(2, request);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<MovieDetailDto>>(conflict.Value);

        Assert.False(resp.Success);
        Assert.Contains("dune-hanh-tinh-cat", resp.Message);
    }

    [Fact]
    public async Task AdminMoviesController_UpdateMovie_InvalidSlugCharactersOnly_Returns400BadRequest()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        var request = new UpsertMovieRequest
        {
            Title = "Invalid Slug Characters",
            OriginalTitle = "Invalid",
            Slug = "---___---", // Characters will all be stripped by GenerateSlug, resulting in empty string
            Overview = "Invalid slug test",
            ReleaseYear = 2024,
            Quality = "HD",
            Type = MovieType.Movie
        };

        var result = await controller.UpdateMovie(2, request);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<MovieDetailDto>>(badRequest.Value);

        Assert.False(resp.Success);
        Assert.Contains("không hợp lệ", resp.Message);
    }

    [Fact]
    public async Task AdminMoviesController_UpdateMovie_SelfSlugAndNewUniqueSlug_Succeeds()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminMoviesController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // 1. Updating Movie 1 keeping its own slug succeeds
        var selfRequest = new UpsertMovieRequest
        {
            Title = "Dune Hành Tinh Cát (Bản cập nhật)",
            OriginalTitle = "Dune Part One",
            Slug = "dune-hanh-tinh-cat",
            Overview = "Mô tả mới",
            ReleaseYear = 2021,
            Quality = "4K UHD",
            Type = MovieType.Movie
        };
        var selfResult = await controller.UpdateMovie(1, selfRequest);
        var okSelf = Assert.IsType<OkObjectResult>(selfResult.Result);
        var respSelf = Assert.IsType<ApiResponse<MovieDetailDto>>(okSelf.Value);
        Assert.True(respSelf.Success);
        Assert.Equal("dune-hanh-tinh-cat", respSelf.Data!.Slug);

        // 2. Updating Movie 1 to a completely new unique slug succeeds
        var newSlugRequest = new UpsertMovieRequest
        {
            Title = "Dune Hành Tinh Cát (IMAX)",
            OriginalTitle = "Dune Part One",
            Slug = "dune-hanh-tinh-cat-imax",
            Overview = "Bản IMAX",
            ReleaseYear = 2021,
            Quality = "4K UHD",
            Type = MovieType.Movie
        };
        var newSlugResult = await controller.UpdateMovie(1, newSlugRequest);
        var okNew = Assert.IsType<OkObjectResult>(newSlugResult.Result);
        var respNew = Assert.IsType<ApiResponse<MovieDetailDto>>(okNew.Value);
        Assert.True(respNew.Success);
        Assert.Equal("dune-hanh-tinh-cat-imax", respNew.Data!.Slug);
    }

    // =========================================================================
    // SECTION 3: PAYMENT CONCURRENCY & RACE CONDITION STRESS TESTING
    // =========================================================================

    [Fact]
    public async Task AdminPaymentsController_MarkPaymentPaid_ConcurrentApprovals_ExactlyOneSucceeds()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        const int concurrentAttempts = 10;

        // We instantiate separate controller instances simulating 10 concurrent requests from administrators
        var tasks = Enumerable.Range(1, concurrentAttempts).Select(async i =>
        {
            var controller = new AdminPaymentsController(factory, TimeProvider.System);
            SetAdminUser(controller, AdminUserId, $"admin{i}@cinema.test");

            var approvalReq = new ManualPaymentApprovalRequest
            {
                TransactionReference = $"CONCUR-TX-{i:D3}",
                Note = $"Concurrent attempt #{i}"
            };

            return await controller.MarkPaymentPaid(Payment1Id, approvalReq);
        }).ToArray();

        var settledResponses = await Task.WhenAll(tasks);

        var successCount = 0;
        var badRequestCount = 0;

        foreach (var actionResult in settledResponses)
        {
            if (actionResult.Result is OkObjectResult okObj)
            {
                var resp = Assert.IsType<ApiResponse<PaymentDto>>(okObj.Value);
                if (resp.Success) successCount++;
            }
            else if (actionResult.Result is BadRequestObjectResult badObj)
            {
                var resp = Assert.IsType<ApiResponse<PaymentDto>>(badObj.Value);
                if (!resp.Success) badRequestCount++;
            }
            else
            {
                Assert.Fail($"Unexpected result type: {actionResult.Result?.GetType().Name}");
            }
        }

        // CRITICAL INVARIANT: Exactly 1 approval must succeed, exactly 9 must fail with BadRequest
        Assert.Equal(1, successCount);
        Assert.Equal(concurrentAttempts - 1, badRequestCount);

        // Verify in database: User was extended by exactly 30 days, NOT 300 days (10x race multiplier)
        await using var verifyCtx = new AppDbContext(options);
        var user = await verifyCtx.Users.FindAsync(MemberUserId);
        Assert.NotNull(user);
        Assert.Equal(MembershipTier.Vip, user.Membership);
        Assert.NotNull(user.VipExpiresAtUtc);

        // Approx 30 days from now (within 2 minutes tolerance)
        var expectedExpiry = DateTime.UtcNow.AddDays(30);
        var diff = Math.Abs((user.VipExpiresAtUtc.Value - expectedExpiry).TotalMinutes);
        Assert.True(diff < 2.0, $"Expected expiry close to 30 days, but got: {user.VipExpiresAtUtc.Value}");

        var payment = await verifyCtx.Payments.FindAsync(Payment1Id);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
    }

    [Fact]
    public async Task AdminPaymentsController_RejectPayment_AfterCompleted_Returns400BadRequest()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminPaymentsController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // 1. Mark paid
        var approveResult = await controller.MarkPaymentPaid(Payment1Id, new ManualPaymentApprovalRequest
        {
            TransactionReference = "TX-OK-01"
        });
        Assert.IsType<OkObjectResult>(approveResult.Result);

        // 2. Attempt reject completed payment -> returns 400 Bad Request
        var rejectResult = await controller.RejectPayment(Payment1Id, new PaymentRejectionRequest
        {
            Reason = "Lỗi sau thanh toán"
        });
        var badRequest = Assert.IsType<BadRequestObjectResult>(rejectResult.Result);
        var resp = Assert.IsType<ApiResponse<PaymentDto>>(badRequest.Value);

        Assert.False(resp.Success);
        Assert.Contains("đã hoàn tất, không thể từ chối", resp.Message);
    }

    [Fact]
    public async Task AdminPaymentsController_MarkPaymentPaid_AfterRejected_ReconsidersCleanly()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminPaymentsController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId);

        // 1. Reject payment
        var rejectResult = await controller.RejectPayment(Payment1Id, new PaymentRejectionRequest
        {
            Reason = "Ủy nhiệm chi không hợp lệ"
        });
        Assert.IsType<OkObjectResult>(rejectResult.Result);

        await using (var checkCtx = new AppDbContext(options))
        {
            var p = await checkCtx.Payments.FindAsync(Payment1Id);
            Assert.NotNull(p);
            Assert.Equal(PaymentStatus.Failed, p.Status);
        }

        // 2. Marking paid on rejected payment updates status to Completed and credits VIP
        var approveResult = await controller.MarkPaymentPaid(Payment1Id, new ManualPaymentApprovalRequest
        {
            TransactionReference = "TX-RECONSIDER-01",
            Note = "Đã xác minh lại hóa đơn"
        });
        var okResult = Assert.IsType<OkObjectResult>(approveResult.Result);
        var resp = Assert.IsType<ApiResponse<PaymentDto>>(okResult.Value);
        Assert.True(resp.Success);
        Assert.Equal(PaymentStatus.Completed, resp.Data!.Status);
    }

    // =========================================================================
    // SECTION 4: ADMIN SAFETY (SELF-DEMOTION & SELF-LOCK PROTECTION)
    // =========================================================================

    [Fact]
    public async Task AdminUsersController_CannotDemoteOwnAdminRole()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminUsersController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId); // Current caller is AdminUserId

        // Admin attempts to demote self
        var result = await controller.UpdateUserAdminRole(AdminUserId, new AdminUpdateUserRoleRequest { IsAdmin = false });
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<bool>>(badRequest.Value);

        Assert.False(resp.Success);
        Assert.Contains("không thể tự hạ quyền quản trị", resp.Message);
    }

    [Fact]
    public async Task AdminUsersController_CannotLockOwnAdminAccount()
    {
        var (factory, options) = CreateContextFactory();
        await using (var ctx = new AppDbContext(options)) await SeedSampleDataAsync(ctx);

        var controller = new AdminUsersController(factory, TimeProvider.System);
        SetAdminUser(controller, AdminUserId); // Current caller is AdminUserId

        // Admin attempts to lock self
        var result = await controller.UpdateUserLockStatus(AdminUserId, new AdminUpdateUserLockRequest { IsLocked = true, LockMinutes = 60 });
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var resp = Assert.IsType<ApiResponse<bool>>(badRequest.Value);

        Assert.False(resp.Success);
        Assert.Contains("không thể tự khóa tài khoản", resp.Message);
    }
}

public sealed class ChallengerM3AdminHttpTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static async Task<string> RegisterUserAsync(HttpClient client, string email, string name = "Tester")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = name,
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        var token = payload!.Data!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private async Task SetUserAdminAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var sec = await db.UserSecurity.SingleOrDefaultAsync(s => s.UserId == user.Id);
        if (sec is null)
        {
            sec = new UserSecurityEntity { UserId = user.Id, IsAdmin = true, PasswordIterations = 100000 };
            db.UserSecurity.Add(sec);
        }
        else
        {
            sec.IsAdmin = true;
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdminEndpoints_UnauthenticatedAccess_Returns401Unauthorized()
    {
        using var client = factory.CreateClient();

        var resMovies = await client.GetAsync("/api/admin/movies");
        Assert.Equal(HttpStatusCode.Unauthorized, resMovies.StatusCode);

        var resUsers = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, resUsers.StatusCode);

        var resPayments = await client.GetAsync("/api/admin/payments");
        Assert.Equal(HttpStatusCode.Unauthorized, resPayments.StatusCode);

        var resReviews = await client.GetAsync("/api/admin/reviews");
        Assert.Equal(HttpStatusCode.Unauthorized, resReviews.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_NormalMemberAccess_Returns403Forbidden()
    {
        using var client = factory.CreateClient();
        var email = $"nonadmin-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);

        var resMovies = await client.GetAsync("/api/admin/movies");
        Assert.Equal(HttpStatusCode.Forbidden, resMovies.StatusCode);

        var resUsers = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, resUsers.StatusCode);

        var resPayments = await client.GetAsync("/api/admin/payments");
        Assert.Equal(HttpStatusCode.Forbidden, resPayments.StatusCode);

        var resReviews = await client.GetAsync("/api/admin/reviews");
        Assert.Equal(HttpStatusCode.Forbidden, resReviews.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_AdminAccess_PaginationAndConflictOverHttp()
    {
        using var client = factory.CreateClient();
        var email = $"admin-e2e-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);
        await SetUserAdminAsync(email);

        // 1. Pagination edge case over HTTP: page=0, pageSize=0 -> 200 OK with Page=1, PageSize=20
        var resPaged = await client.GetFromJsonAsync<ApiResponse<PagedResult<MovieDetailDto>>>("/api/admin/movies?page=0&pageSize=0");
        Assert.NotNull(resPaged);
        Assert.True(resPaged.Success);
        Assert.Equal(1, resPaged.Data!.Page);
        Assert.Equal(20, resPaged.Data.PageSize);

        // 2. Slug conflict over HTTP: Movie 2 updating to Movie 1's slug ("dune-part-three")
        var resConflict = await client.PutAsJsonAsync("/api/admin/movies/2", new UpsertMovieRequest
        {
            Title = "Oppenheimer Collision Attempt",
            OriginalTitle = "Oppenheimer",
            Slug = "dune-part-three",
            Overview = "Conflict test over HTTP",
            Quality = "4K IMAX",
            AgeRating = "18+",
            DurationMinutes = 180,
            ReleaseYear = 2023,
            Type = MovieType.Movie
        });
        Assert.Equal(HttpStatusCode.Conflict, resConflict.StatusCode);
        var conflictPayload = await resConflict.Content.ReadFromJsonAsync<ApiResponse<MovieDetailDto>>();
        Assert.NotNull(conflictPayload);
        Assert.False(conflictPayload.Success);
        Assert.Contains("dune-part-three", conflictPayload.Message);
    }
}

