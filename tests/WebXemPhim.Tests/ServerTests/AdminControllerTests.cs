using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class AdminControllerTests
{
    private sealed class TestContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppDbContext(options));
    }

    private static (AdminController controller, TestContextFactory factory, DbContextOptions<AppDbContext> options) CreateController()
    {
        var dbName = $"webxemphim-admin-tests-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestContextFactory(options);
        var controller = new AdminController(factory, TimeProvider.System);
        return (controller, factory, options);
    }

    private static async Task SeedSampleDataAsync(DbContextOptions<AppDbContext> options)
    {
        await using var context = new AppDbContext(options);
        context.Genres.AddRange(
            new GenreEntity { Id = 1, Name = "Hành Động", Slug = "hanh-dong" },
            new GenreEntity { Id = 2, Name = "Kịch Tính", Slug = "kich-tinh" });

        var movie = new MovieEntity
        {
            Id = 1,
            Title = "Thử Nghiệm",
            OriginalTitle = "Test Movie",
            Slug = "thu-nghiem",
            Overview = "Mô tả thử nghiệm.",
            PosterUrl = "https://example.com/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg",
            Rating = 8.5,
            VoteCount = 100,
            ReleaseYear = 2026,
            DurationMinutes = 120,
            AgeRating = "16+",
            Quality = "4K UHD",
            Type = MovieType.Movie,
            Country = "Việt Nam",
            Director = "Đạo Diễn A",
            IsTrending = true,
            IsFeatured = true,
            TotalEpisodes = 1,
            CurrentEpisode = 1
        };
        context.Movies.Add(movie);

        context.MovieGenres.Add(new MovieGenreEntity { MovieId = 1, GenreId = 1 });

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            FullName = "Khán Giả Mẫu",
            Email = "viewer@example.com",
            NormalizedEmail = "viewer@example.com",
            PasswordSalt = [1, 2, 3],
            PasswordHash = [4, 5, 6],
            Membership = MembershipTier.Vip,
            VipExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            CreatedAtUtc = DateTime.UtcNow.AddMonths(-1)
        };
        context.Users.Add(user);

        context.Payments.Add(new PaymentEntity
        {
            Id = Guid.NewGuid(),
            OrderCode = "ORD-001",
            UserId = user.Id,
            PlanId = "vip-30",
            PlanName = "VIP 1 Tháng",
            Amount = 59000m,
            Method = PaymentMethod.Momo,
            Status = PaymentStatus.Completed,
            CreatedAtUtc = DateTime.UtcNow
        });

        context.Reviews.Add(new ReviewEntity
        {
            Id = Guid.NewGuid(),
            MovieId = 1,
            UserId = user.Id,
            Rating = 9,
            Comment = "Phim rất hay và ấn tượng!",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetStats_ReturnsValidDashboardMetrics()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var actionResult = await controller.GetStats();
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<AdminDashboardDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(1, envelope.Data.TotalMovies);
        Assert.Equal(1, envelope.Data.TotalUsers);
        Assert.Equal(1, envelope.Data.ActiveVipUsers);
        Assert.Equal(59000m, envelope.Data.TotalRevenue);
        Assert.Equal(1, envelope.Data.TotalReviews);
        Assert.Equal(9.0, envelope.Data.AverageRating);
    }

    [Fact]
    public async Task GetMovies_WithFilter_ReturnsMatchingMovies()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var actionResult = await controller.GetMovies(q: "Thử Nghiệm", type: MovieType.Movie);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<List<MovieDetailDto>>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Single(envelope.Data);
        Assert.Equal("Thử Nghiệm", envelope.Data[0].Title);
    }

    [Fact]
    public async Task CreateMovie_AddsMovieAndDefaultEpisode()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var request = new UpsertMovieRequest
        {
            Title = "Phim Mới 2026",
            OriginalTitle = "New Movie 2026",
            Slug = "phim-moi-2026",
            Overview = "Tóm tắt phim mới tạo.",
            PosterUrl = "https://example.com/new.jpg",
            BackdropUrl = "https://example.com/new-bg.jpg",
            Rating = 9.2,
            ReleaseYear = 2026,
            DurationMinutes = 115,
            Type = MovieType.Movie,
            GenreIds = [1],
            CastNames = ["Diễn Viên 1", "Diễn Viên 2"]
        };

        var actionResult = await controller.CreateMovie(request);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("Phim Mới 2026", envelope.Data.Title);
        Assert.Single(envelope.Data.Genres);
        Assert.Equal(2, envelope.Data.Cast.Count);

        await using var context = new AppDbContext(options);
        var saved = await context.Movies.Include(m => m.Episodes).FirstOrDefaultAsync(m => m.Title == "Phim Mới 2026");
        Assert.NotNull(saved);
        Assert.Single(saved.Episodes);
    }

    [Fact]
    public async Task UpdateMovie_ModifiesExistingFields()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var request = new UpsertMovieRequest
        {
            Title = "Thử Nghiệm Đã Đổi Tên",
            OriginalTitle = "Test Movie Updated",
            Overview = "Mô tả mới.",
            PosterUrl = "https://example.com/p.jpg",
            BackdropUrl = "https://example.com/b.jpg",
            Rating = 9.0,
            ReleaseYear = 2026,
            DurationMinutes = 130,
            Type = MovieType.Movie,
            GenreIds = [2]
        };

        var actionResult = await controller.UpdateMovie(1, request);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.Equal("Thử Nghiệm Đã Đổi Tên", envelope.Data!.Title);

        await using var context = new AppDbContext(options);
        var updated = await context.Movies.Include(m => m.MovieGenres).FirstAsync(m => m.Id == 1);
        Assert.Equal("Thử Nghiệm Đã Đổi Tên", updated.Title);
        Assert.Equal(2, updated.MovieGenres.First().GenreId);
    }

    [Fact]
    public async Task DeleteMovie_RemovesMovieFromDatabase()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var actionResult = await controller.DeleteMovie(1);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<bool>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.True(envelope.Data);

        await using var context = new AppDbContext(options);
        Assert.Null(await context.Movies.FindAsync(1));
    }

    [Fact]
    public async Task AddEpisode_IncreasesEpisodesAndUpdatesTotal()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var request = new UpsertEpisodeRequest
        {
            EpisodeNumber = 2,
            Title = "Tập 2 Đặc Sắc",
            DurationMinutes = 50,
            StreamUrl = "https://example.com/stream2.m3u8"
        };

        var actionResult = await controller.AddEpisode(1, request);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<EpisodeDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.Equal("Tập 2 Đặc Sắc", envelope.Data!.Title);

        await using var context = new AppDbContext(options);
        var movie = await context.Movies.FindAsync(1);
        Assert.Equal(2, movie!.CurrentEpisode);
    }

    [Fact]
    public async Task UpdateUserMembership_ModifiesTierAndExtendsVip()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using var initContext = new AppDbContext(options);
        var user = await initContext.Users.FirstAsync();

        var request = new AdminUpdateUserRequest
        {
            MembershipTier = MembershipTier.Premium,
            ExtendDays = 90
        };

        var actionResult = await controller.UpdateUserMembership(user.Id, request);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<UserDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.Equal(MembershipTier.Premium, envelope.Data!.MembershipTier);
        Assert.True(envelope.Data.IsVip);
    }

    [Fact]
    public async Task DeleteReview_RemovesReviewFromDatabase()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using var initContext = new AppDbContext(options);
        var review = await initContext.Reviews.FirstAsync();

        var actionResult = await controller.DeleteReview(review.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<bool>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.True(envelope.Data);

        await using var verifyContext = new AppDbContext(options);
        Assert.Empty(await verifyContext.Reviews.ToListAsync());
    }

    [Fact]
    public async Task GetSystemHealth_ReturnsAccurateMetrics()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var actionResult = await controller.GetSystemHealth();
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<SystemHealthDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(1, envelope.Data.TotalMovies);
        Assert.Equal(1, envelope.Data.TotalUsers);
        Assert.True(envelope.Data.MemoryUsageMb > 0);
    }

    [Fact]
    public async Task GetAuditLogs_WithFilters_ReturnsMatchingLogs()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using (var context = new AppDbContext(options))
        {
            context.AuditLogs.AddRange(
                new AuditLogEntity
                {
                    Id = Guid.NewGuid(),
                    TimestampUtc = DateTime.UtcNow,
                    AdminEmail = "admin@cinemaflow.vn",
                    Action = "CREATE_MOVIE",
                    EntityType = "Movie",
                    EntityId = "1",
                    Summary = "Tạo phim thử nghiệm"
                },
                new AuditLogEntity
                {
                    Id = Guid.NewGuid(),
                    TimestampUtc = DateTime.UtcNow,
                    AdminEmail = "admin@cinemaflow.vn",
                    Action = "APPROVE_PAYMENT",
                    EntityType = "Payment",
                    EntityId = "ORD-001",
                    Summary = "Duyệt thanh toán"
                }
            );
            await context.SaveChangesAsync();
        }

        var result = await controller.GetAuditLogs(entityType: "Movie");
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<List<AuditLogDto>>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.Single(envelope.Data!);
        Assert.Equal("Movie", envelope.Data![0].EntityType);
        Assert.Equal("CREATE_MOVIE", envelope.Data[0].Action);
    }

    [Fact]
    public async Task BulkMovieAction_SetTrending_And_Delete_Works()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        // Add second movie
        await using (var context = new AppDbContext(options))
        {
            context.Movies.Add(new MovieEntity
            {
                Id = 2,
                Title = "Phim 2",
                OriginalTitle = "Movie 2",
                Slug = "phim-2",
                Overview = "Mo ta",
                PosterUrl = "https://example.com/p2.jpg",
                BackdropUrl = "https://example.com/b2.jpg",
                Rating = 8.0,
                VoteCount = 10,
                ReleaseYear = 2026,
                DurationMinutes = 100,
                AgeRating = "13+",
                Quality = "HD",
                Type = MovieType.Movie,
                Country = "VN",
                Director = "Director B",
                IsTrending = false,
                IsFeatured = false,
                TotalEpisodes = 1,
                CurrentEpisode = 1
            });
            await context.SaveChangesAsync();
        }

        // Test Bulk Trending
        var bulkTrending = new BulkMovieActionRequest
        {
            Action = "set_trending",
            MovieIds = [1, 2]
        };
        var actionResult = await controller.BulkMovieAction(bulkTrending);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<int>>(okResult.Value);
        Assert.True(envelope.Success);
        Assert.Equal(2, envelope.Data);

        // Verify in DB
        await using (var context = new AppDbContext(options))
        {
            var m2 = await context.Movies.FindAsync(2);
            Assert.True(m2!.IsTrending);
        }

        // Test Bulk Delete
        var bulkDelete = new BulkMovieActionRequest
        {
            Action = "delete",
            MovieIds = [2]
        };
        var delResult = await controller.BulkMovieAction(bulkDelete);
        var delOkResult = Assert.IsType<OkObjectResult>(delResult.Result);
        var delEnvelope = Assert.IsType<ApiResponse<int>>(delOkResult.Value);
        Assert.True(delEnvelope.Success);
        Assert.Equal(1, delEnvelope.Data);

        await using (var context = new AppDbContext(options))
        {
            Assert.Null(await context.Movies.FindAsync(2));
        }
    }

    [Fact]
    public async Task BulkMovieAction_EmptyMovieIds_ReturnsBadRequest()
    {
        var (controller, _, options) = CreateController();
        var request = new BulkMovieActionRequest { Action = "delete", MovieIds = [] };
        var result = await controller.BulkMovieAction(request);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ToggleTrending_And_ToggleFeatured_TogglesFlags()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        // Toggle trending from true to false
        var trendingResult = await controller.ToggleTrending(1);
        var okTrending = Assert.IsType<OkObjectResult>(trendingResult.Result);
        var trendingEnv = Assert.IsType<ApiResponse<bool>>(okTrending.Value);
        Assert.True(trendingEnv.Success);
        Assert.False(trendingEnv.Data);

        // Toggle featured from true to false
        var featuredResult = await controller.ToggleFeatured(1);
        var okFeatured = Assert.IsType<OkObjectResult>(featuredResult.Result);
        var featuredEnv = Assert.IsType<ApiResponse<bool>>(okFeatured.Value);
        Assert.True(featuredEnv.Success);
        Assert.False(featuredEnv.Data);
    }

    [Fact]
    public async Task CloneMovie_ReplicatesMovieAndAssignsUniqueSlug()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        // Add episode and stream to source movie
        await using (var context = new AppDbContext(options))
        {
            var ep = new EpisodeEntity
            {
                Id = 10,
                MovieId = 1,
                EpisodeNumber = 1,
                Title = "Tập 1",
                DurationMinutes = 45,
                ThumbnailUrl = "https://example.com/thumb.jpg",
                Overview = "Tập đầu"
            };
            context.Episodes.Add(ep);
            context.StreamSources.Add(new StreamSourceEntity
            {
                EpisodeId = 10,
                Position = 1,
                ServerName = "VIP Server 1",
                ServerType = StreamServerType.VipFastServer,
                Quality = "1080p",
                StreamUrl = "https://example.com/stream.m3u8"
            });
            await context.SaveChangesAsync();
        }

        var cloneRequest = new CloneMovieRequest
        {
            NewTitle = "Thử Nghiệm Phần 2",
            NewSlug = "thu-nghiem", // Same slug to test auto uniqueness
            ReleaseYear = 2027,
            CopyEpisodes = true
        };

        var actionResult = await controller.CloneMovie(1, cloneRequest);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var envelope = Assert.IsType<ApiResponse<MovieDetailDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("Thử Nghiệm Phần 2", envelope.Data.Title);
        Assert.Equal("thu-nghiem-1", envelope.Data.Slug);
        Assert.Equal(2027, envelope.Data.ReleaseYear);

        // Verify episodes copied
        await using (var context = new AppDbContext(options))
        {
            var cloned = await context.Movies.Include(m => m.Episodes).ThenInclude(e => e.Streams).FirstOrDefaultAsync(m => m.Slug == "thu-nghiem-1");
            Assert.NotNull(cloned);
            Assert.Single(cloned.Episodes);
            Assert.Single(cloned.Episodes.First().Streams);
        }
    }

    [Fact]
    public async Task StreamSources_Crud_Lifecycle()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        // Setup episode
        await using (var context = new AppDbContext(options))
        {
            context.Episodes.Add(new EpisodeEntity
            {
                Id = 20,
                MovieId = 1,
                EpisodeNumber = 1,
                Title = "Tập 1",
                DurationMinutes = 45
            });
            await context.SaveChangesAsync();
        }

        // 1. Add Stream Source
        var addRequest = new UpsertStreamSourceRequest
        {
            ServerName = "Hydra CDN 1",
            ServerType = StreamServerType.HydraCdn,
            Quality = "4K UHD",
            StreamUrl = "https://cdn.example.com/movie.m3u8"
        };
        var addResult = await controller.AddStreamSource(20, addRequest);
        var addOk = Assert.IsType<OkObjectResult>(addResult.Result);
        var addEnv = Assert.IsType<ApiResponse<StreamSourceDto>>(addOk.Value);
        Assert.True(addEnv.Success);
        Assert.Equal(1, addEnv.Data!.Position);
        Assert.Equal("Hydra CDN 1", addEnv.Data.ServerName);

        // 2. Get Streams
        var getResult = await controller.GetStreamsForEpisode(20);
        var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
        var getEnv = Assert.IsType<ApiResponse<List<StreamSourceDto>>>(getOk.Value);
        Assert.Single(getEnv.Data!);

        // 3. Update Stream
        var updateRequest = new UpsertStreamSourceRequest
        {
            ServerName = "Hydra CDN Backup",
            ServerType = StreamServerType.BackupStream,
            Quality = "1080p",
            StreamUrl = "https://backup.example.com/movie.m3u8"
        };
        var updateResult = await controller.UpdateStreamSource(20, 1, updateRequest);
        var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updateEnv = Assert.IsType<ApiResponse<StreamSourceDto>>(updateOk.Value);
        Assert.True(updateEnv.Success);
        Assert.Equal("Hydra CDN Backup", updateEnv.Data!.ServerName);

        // 4. Delete Stream
        var deleteResult = await controller.DeleteStreamSource(20, 1);
        var delOk = Assert.IsType<OkObjectResult>(deleteResult.Result);
        var delEnv = Assert.IsType<ApiResponse<bool>>(delOk.Value);
        Assert.True(delEnv.Success);

        await using (var context = new AppDbContext(options))
        {
            Assert.Empty(await context.StreamSources.Where(s => s.EpisodeId == 20).ToListAsync());
        }
    }

    [Fact]
    public async Task ProbeStream_InvalidUrl_ReturnsSafeFailureResult()
    {
        var (controller, _, _) = CreateController();
        var request = new ProbeStreamRequest { StreamUrl = "http://invalid-non-existing-domain-test.local/video.m3u8" };

        var result = await controller.ProbeStream(request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<ProbeStreamResultDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.False(envelope.Data.IsSuccess);
    }

    [Fact]
    public async Task BatchGenerateEpisodes_CreatesEpisodesWithSequentialData()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        var request = new BatchEpisodeGenerateRequest
        {
            MovieId = 1,
            StartEpisode = 1,
            EndEpisode = 5,
            TitlePattern = "Tập {n} Thuyết Minh",
            StreamUrlPattern = "https://cdn.example.com/ep_{n}.m3u8",
            ServerName = "CDN Fast VIP",
            ServerType = StreamServerType.VipFastServer,
            Quality = "1080p",
            DurationMinutes = 45
        };

        var result = await controller.BatchGenerateEpisodes(request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<int>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.Equal(5, envelope.Data);

        await using var context = new AppDbContext(options);
        var episodes = await context.Episodes.Where(e => e.MovieId == 1).OrderBy(e => e.EpisodeNumber).ToListAsync();
        Assert.Equal(5, episodes.Count);
        Assert.Equal("Tập 1 Thuyết Minh", episodes[0].Title);
        Assert.Equal("Tập 5 Thuyết Minh", episodes[4].Title);

        var movie = await context.Movies.FindAsync(1);
        Assert.Equal(5, movie!.TotalEpisodes);
    }

    [Fact]
    public async Task GetUserDetail_ReturnsFull360Overview()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using var initContext = new AppDbContext(options);
        var user = await initContext.Users.FirstAsync();

        var result = await controller.GetUserDetail(user.Id);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AdminUserDetailDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("viewer@example.com", envelope.Data.Email);
        Assert.Single(envelope.Data.RecentPayments);
        Assert.Single(envelope.Data.RecentReviews);
        Assert.False(envelope.Data.IsAdmin);
        Assert.False(envelope.Data.IsLocked);
    }

    [Fact]
    public async Task UpdateUserAdminRole_TogglesSecurityAdminRole()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using var initContext = new AppDbContext(options);
        var user = await initContext.Users.FirstAsync();

        var request = new AdminUpdateUserRoleRequest { IsAdmin = true };
        var result = await controller.UpdateUserAdminRole(user.Id, request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<bool>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.True(envelope.Data);

        await using var verifyContext = new AppDbContext(options);
        var security = await verifyContext.UserSecurity.FirstOrDefaultAsync(s => s.UserId == user.Id);
        Assert.NotNull(security);
        Assert.True(security.IsAdmin);
    }

    [Fact]
    public async Task UpdateUserLockStatus_LocksAndUnlocksUser()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using var initContext = new AppDbContext(options);
        var user = await initContext.Users.FirstAsync();

        // 1. Lock user for 60 minutes
        var lockRequest = new AdminUpdateUserLockRequest { IsLocked = true, LockMinutes = 60 };
        var lockResult = await controller.UpdateUserLockStatus(user.Id, lockRequest);
        var lockOk = Assert.IsType<OkObjectResult>(lockResult.Result);
        var lockEnv = Assert.IsType<ApiResponse<bool>>(lockOk.Value);
        Assert.True(lockEnv.Success);
        Assert.True(lockEnv.Data);

        // 2. Unlock user
        var unlockRequest = new AdminUpdateUserLockRequest { IsLocked = false };
        var unlockResult = await controller.UpdateUserLockStatus(user.Id, unlockRequest);
        var unlockOk = Assert.IsType<OkObjectResult>(unlockResult.Result);
        var unlockEnv = Assert.IsType<ApiResponse<bool>>(unlockOk.Value);
        Assert.True(unlockEnv.Success);
        Assert.False(unlockEnv.Data);
    }

    [Fact]
    public async Task ReplyToReview_SetsAdminReplyAndReportStatus()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        await using var initContext = new AppDbContext(options);
        var review = await initContext.Reviews.FirstAsync();

        var request = new AdminReviewModerationRequest
        {
            AdminReply = "Cảm ơn bạn đã phản hồi! CinemaFlow chúc bạn xem phim vui vẻ.",
            IsReported = true
        };

        var result = await controller.ReplyToReview(review.Id, request);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<AdminReviewDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("Cảm ơn bạn đã phản hồi! CinemaFlow chúc bạn xem phim vui vẻ.", envelope.Data.AdminReply);
        Assert.True(envelope.Data.IsReported);
        Assert.NotNull(envelope.Data.AdminRepliedAt);
    }

    [Fact]
    public async Task MarkPaymentPaid_ApprovesPaymentAndExtendsVipTier()
    {
        var (controller, _, options) = CreateController();
        await SeedSampleDataAsync(options);

        Guid paymentId;
        Guid userId;
        await using (var context = new AppDbContext(options))
        {
            var user = await context.Users.FirstAsync();
            userId = user.Id;
            user.Membership = MembershipTier.Free;
            user.VipExpiresAtUtc = null;

            var pendingPayment = new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = "ORD-MANUAL-999",
                UserId = user.Id,
                PlanId = "vip-90",
                PlanName = "VIP 3 Tháng",
                Amount = 149000m,
                Method = PaymentMethod.BankTransfer,
                Status = PaymentStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Payments.Add(pendingPayment);
            await context.SaveChangesAsync();
            paymentId = pendingPayment.Id;
        }

        var approvalRequest = new ManualPaymentApprovalRequest
        {
            TransactionReference = "VCB_123456789",
            Note = "Duyệt chuyển khoản thủ công VCB"
        };

        var result = await controller.MarkPaymentPaid(paymentId, approvalRequest);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var envelope = Assert.IsType<ApiResponse<PaymentDto>>(okResult.Value);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(PaymentStatus.Completed, envelope.Data.Status);
        Assert.NotNull(envelope.Data.PaidAt);

        // Verify user became VIP
        await using var verifyContext = new AppDbContext(options);
        var verifiedUser = await verifyContext.Users.FindAsync(userId);
        Assert.Equal(MembershipTier.Vip, verifiedUser!.Membership);
        Assert.NotNull(verifiedUser.VipExpiresAtUtc);
    }
}
