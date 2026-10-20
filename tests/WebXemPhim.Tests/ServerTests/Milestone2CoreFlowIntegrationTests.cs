using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Controllers.Admin;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class Milestone2CoreFlowIntegrationTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private static async Task<(SqliteConnection Connection, AppDbContext Context, IDbContextFactory<AppDbContext> Factory)> CreateSqliteContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context);
        await DatabaseSeeder.SeedCatalogAsync(context);

        var factoryMock = new TestDbContextFactory(options);
        return (connection, context, factoryMock);
    }

    [Fact]
    public async Task WatchProgressSingleQuery_AuthenticatedUser_ReturnsProgressAndNullForUnwatched()
    {
        using var client = factory.CreateClient();
        var email = $"m2-progress-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);

        // 1. Unwatched movie returns null data
        var initialRes = await client.GetFromJsonAsync<ApiResponse<WatchProgressDto?>>("/api/library/progress/1");
        Assert.NotNull(initialRes);
        Assert.True(initialRes.Success);
        Assert.Null(initialRes.Data);

        // 2. Save progress for movie 1
        var savePayload = new UpdateWatchProgressRequest
        {
            MovieId = 1,
            EpisodeNumber = 2,
            ProgressSeconds = 750,
            DurationSeconds = 3000
        };
        var postRes = await client.PostAsJsonAsync("/api/library/progress", savePayload);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        // 3. Query progress for movie 1
        var queryRes = await client.GetFromJsonAsync<ApiResponse<WatchProgressDto?>>("/api/library/progress/1");
        Assert.NotNull(queryRes);
        Assert.True(queryRes.Success);
        Assert.NotNull(queryRes.Data);
        Assert.Equal(1, queryRes.Data.MovieId);
        Assert.Equal(2, queryRes.Data.EpisodeNumber);
        Assert.Equal(750, queryRes.Data.ProgressSeconds);
        Assert.Equal(3000, queryRes.Data.DurationSeconds);
        Assert.Equal(25, queryRes.Data.ProgressPercent); // 750/3000 = 25%

        // 4. Guest cannot query progress
        using var guestClient = factory.CreateClient();
        var guestRes = await guestClient.GetAsync("/api/library/progress/1");
        Assert.Equal(HttpStatusCode.Unauthorized, guestRes.StatusCode);
    }

    [Fact]
    public async Task MovieViewCountIncrement_IncrementsCounter_AndReturnsNotFoundForMissing()
    {
        using var client = factory.CreateClient();

        // 1. Get initial view count
        var movieBefore = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        var initialViews = movieBefore.ViewCount;

        // 2. Increment view count
        var incrementRes = await client.PostAsync("/api/movies/1/view", null);
        Assert.Equal(HttpStatusCode.OK, incrementRes.StatusCode);

        // 3. Verify view count incremented
        var movieAfter = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        Assert.Equal(initialViews + 1, movieAfter.ViewCount);

        // 4. Missing movie returns 404
        var missingRes = await client.PostAsync("/api/movies/999999/view", null);
        Assert.Equal(HttpStatusCode.NotFound, missingRes.StatusCode);
    }

    [Fact]
    public async Task ReviewsRatingRecalculation_OnUpsertAndDelete_DynamicallyRecalculatesMovieStats()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var reviewService = new ReviewService(dbFactory, TimeProvider.System);

            var movie = await context.Movies.FindAsync(1);
            Assert.NotNull(movie);
            movie.Rating = 0;
            movie.VoteCount = 0;
            await context.SaveChangesAsync();

            var user1Id = Guid.NewGuid();
            var user2Id = Guid.NewGuid();
            context.Users.AddRange(
                new UserEntity { Id = user1Id, Email = "reviewer1@cinema.vn", FullName = "Reviewer One", NormalizedEmail = "REVIEWER1@CINEMA.VN" },
                new UserEntity { Id = user2Id, Email = "reviewer2@cinema.vn", FullName = "Reviewer Two", NormalizedEmail = "REVIEWER2@CINEMA.VN" }
            );
            await context.SaveChangesAsync();

            // 1. User 1 adds 8-star review
            var r1 = await reviewService.UpsertAsync(1, user1Id, new UpsertReviewRequest { Rating = 8, Comment = "Phim rat hay!" });
            Assert.NotNull(r1);

            await using (var verifyDb = await dbFactory.CreateDbContextAsync())
            {
                var m = await verifyDb.Movies.FindAsync(1);
                Assert.Equal(1, m!.VoteCount);
                Assert.Equal(8.0, m.Rating);
            }

            // 2. User 2 adds 10-star review
            var r2 = await reviewService.UpsertAsync(1, user2Id, new UpsertReviewRequest { Rating = 10, Comment = "Tuyeet voi!" });
            Assert.NotNull(r2);

            await using (var verifyDb = await dbFactory.CreateDbContextAsync())
            {
                var m = await verifyDb.Movies.FindAsync(1);
                Assert.Equal(2, m!.VoteCount);
                Assert.Equal(9.0, m.Rating); // (8 + 10) / 2 = 9.0
            }

            // 3. User 1 updates rating to 6
            var r1Updated = await reviewService.UpsertAsync(1, user1Id, new UpsertReviewRequest { Rating = 6, Comment = "Xem lai thay binh thuong" });
            Assert.NotNull(r1Updated);

            await using (var verifyDb = await dbFactory.CreateDbContextAsync())
            {
                var m = await verifyDb.Movies.FindAsync(1);
                Assert.Equal(2, m!.VoteCount);
                Assert.Equal(8.0, m.Rating); // (6 + 10) / 2 = 8.0
            }

            // 4. User 2 deletes review
            var deleted2 = await reviewService.DeleteAsync(1, user2Id);
            Assert.True(deleted2);

            await using (var verifyDb = await dbFactory.CreateDbContextAsync())
            {
                var m = await verifyDb.Movies.FindAsync(1);
                Assert.Equal(1, m!.VoteCount);
                Assert.Equal(6.0, m.Rating);
            }

            // 5. User 1 deletes review
            var deleted1 = await reviewService.DeleteAsync(1, user1Id);
            Assert.True(deleted1);

            await using (var verifyDb = await dbFactory.CreateDbContextAsync())
            {
                var m = await verifyDb.Movies.FindAsync(1);
                Assert.Equal(0, m!.VoteCount);
                Assert.Equal(0.0, m.Rating);
            }
        }
    }

    [Fact]
    public async Task AdminMarkPaymentPaid_UsesDynamicPlanDurationDays()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            // Seed a custom VIP plan with 45 days duration
            var customPlan = new VipPlanEntity
            {
                Id = "vip-custom-45",
                Name = "Gói 45 Ngày Thử Nghiệm",
                Description = "Gói đặc biệt thời hạn 45 ngày",
                PriceVnd = 120000m,
                DurationDays = 45,
                IsActive = true,
                DisplayOrder = 99
            };
            context.VipPlans.Add(customPlan);

            var userId = Guid.NewGuid();
            var user = new UserEntity
            {
                Id = userId,
                FullName = "VIP Test User",
                Email = "viptest@cinema.vn",
                NormalizedEmail = "VIPTEST@CINEMA.VN",
                Membership = MembershipTier.Free,
                VipExpiresAtUtc = null
            };
            context.Users.Add(user);

            var paymentId = Guid.NewGuid();
            var payment = new PaymentEntity
            {
                Id = paymentId,
                OrderCode = "ORDER-CUSTOM-45",
                UserId = userId,
                PlanId = "vip-custom-45",
                PlanName = "Gói 45 Ngày",
                Amount = 120000m,
                Status = PaymentStatus.Pending,
                Method = PaymentMethod.BankTransfer,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var adminController = new AdminPaymentsController(dbFactory, TimeProvider.System);

            var approvalResult = await adminController.MarkPaymentPaid(paymentId, new ManualPaymentApprovalRequest
            {
                TransactionReference = "BANK123456",
                Note = "Duyet thu cong goi 45 ngay"
            });

            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(approvalResult.Result);

            await using (var verifyDb = await dbFactory.CreateDbContextAsync())
            {
                var verifiedUser = await verifyDb.Users.FindAsync(userId);
                Assert.NotNull(verifiedUser);
                Assert.Equal(MembershipTier.Vip, verifiedUser.Membership);
                Assert.NotNull(verifiedUser.VipExpiresAtUtc);

                var daysGranted = (verifiedUser.VipExpiresAtUtc.Value - DateTime.UtcNow).TotalDays;
                Assert.InRange(daysGranted, 44.5, 45.5);
            }
        }
    }

    [Fact]
    public async Task StreamingGating_ServerMasksVipAnd4KStreamsForGuestsAndFreeUsers()
    {
        var (connection, context, dbFactory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movieService = new DatabaseMovieService(dbFactory);
            var moviesController = new MoviesController(movieService, dbFactory);

            // Movie 1 has episodes with VIP Fast Server and 4K streams
            var actionResult = await moviesController.GetById(1);
            var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult.Result);
            var payload = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);
            var movie = payload.Data!;

            // Unauthenticated caller should have VIP and 4K streams masked to empty
            var allStreams = movie.Episodes.SelectMany(e => e.Streams).ToList();
            var vipStreams = allStreams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipStreams);
            Assert.All(vipStreams, s => Assert.Equal(string.Empty, s.StreamUrl));

            // Standard streams should retain their URL
            var standardStreams = allStreams.Where(s => s.ServerType != StreamServerType.VipFastServer && !s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(s.StreamUrl)).ToList();
            Assert.NotEmpty(standardStreams);
            Assert.All(standardStreams, s => Assert.StartsWith("http", s.StreamUrl));
        }
    }

    [Fact]
    public async Task UserSettingsLanguage_PersistsAndSurvivesRetrieval()
    {
        using var client = factory.CreateClient();
        var email = $"m2-lang-{Guid.NewGuid():N}@example.com";
        await RegisterAsync(client, email);

        // 1. Initial settings defaults to 'vi'
        var initialSettings = (await client.GetFromJsonAsync<ApiResponse<UserSettingsDto>>("/api/auth/settings"))!.Data!;
        Assert.Equal("vi", initialSettings.Language);

        // 2. Update language to 'en'
        initialSettings.Language = "en";
        var updateRes = await client.PutAsJsonAsync("/api/auth/settings", initialSettings);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // 3. Retrieve settings again to confirm 'en' persisted
        var updatedSettings = (await client.GetFromJsonAsync<ApiResponse<UserSettingsDto>>("/api/auth/settings"))!.Data!;
        Assert.Equal("en", updatedSettings.Language);
    }

    private static async Task RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            FullName = "Khán Giả Milestone 2",
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            AcceptTerms = true
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<AuthDto>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.AccessToken);
    }
}
