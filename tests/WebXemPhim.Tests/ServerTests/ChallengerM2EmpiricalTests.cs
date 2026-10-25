using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebXemPhim.Server.Data;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class ChallengerM2EmpiricalTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private async Task SetUserMembershipAsync(string email, MembershipTier tier, DateTime? expiresAtUtc, bool isAdmin = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.Membership = tier;
        user.VipExpiresAtUtc = expiresAtUtc;
        if (isAdmin)
        {
            var sec = await db.UserSecurity.SingleOrDefaultAsync(s => s.UserId == user.Id);
            if (sec is null)
            {
                sec = new UserSecurityEntity { UserId = user.Id, IsAdmin = true };
                db.UserSecurity.Add(sec);
            }
            else
            {
                sec.IsAdmin = true;
            }
        }
        await db.SaveChangesAsync();
    }

    private async Task<int> CreateIsolatedMovieAsync(string title, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var movie = new MovieEntity
        {
            Title = title,
            OriginalTitle = title,
            Slug = slug,
            Overview = "Isolated movie for testing rating recalculation",
            PosterUrl = "https://example.com/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg",
            Rating = 0,
            VoteCount = 0,
            ReleaseYear = 2026,
            DurationMinutes = 120,
            Type = MovieType.Movie,
            AgeRating = "16+",
            Quality = "4K UHD"
        };
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie.Id;
    }

    [Fact]
    public async Task Empirical_RatingAndVoteCount_FullLifecycleRecalculation()
    {
        // Setup isolated movie
        var movieSlug = $"empirical-rating-movie-{Guid.NewGuid():N}";
        var movieId = await CreateIsolatedMovieAsync("Phim Test Danh Gia M2", movieSlug);

        // Setup 3 users + 1 admin
        using var client1 = factory.CreateClient();
        using var client2 = factory.CreateClient();
        using var client3 = factory.CreateClient();
        using var adminClient = factory.CreateClient();

        var email1 = $"rev-user1-{Guid.NewGuid():N}@test.vn";
        var email2 = $"rev-user2-{Guid.NewGuid():N}@test.vn";
        var email3 = $"rev-user3-{Guid.NewGuid():N}@test.vn";
        var adminEmail = $"rev-admin-{Guid.NewGuid():N}@test.vn";

        await RegisterUserAsync(client1, email1, "Khán Giả 1");
        await RegisterUserAsync(client2, email2, "Khán Giả 2");
        await RegisterUserAsync(client3, email3, "Khán Giả 3");
        await RegisterUserAsync(adminClient, adminEmail, "Admin He Thong");
        await SetUserMembershipAsync(adminEmail, MembershipTier.Free, null, isAdmin: true);

        // Step 0: Check initial movie stats (0 votes, 0 rating)
        var initMovieRes = await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}");
        Assert.NotNull(initMovieRes?.Data);
        Assert.Equal(0, initMovieRes.Data.VoteCount);
        Assert.Equal(0.0, initMovieRes.Data.Rating);

        // Step 1: User 1 rates 9
        var r1Res = await client1.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest
        {
            Rating = 9,
            Comment = "Phim rat xuat sac!"
        });
        Assert.Equal(HttpStatusCode.OK, r1Res.StatusCode);

        var movieAfterR1 = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(1, movieAfterR1.VoteCount);
        Assert.Equal(9.0, movieAfterR1.Rating);

        var reviewsDto1 = (await client1.GetFromJsonAsync<ApiResponse<MovieReviewsDto>>($"/api/reviews/movie/{movieId}"))!.Data!;
        Assert.Equal(1, reviewsDto1.TotalReviews);
        Assert.Equal(9.0, reviewsDto1.AverageRating);

        // Step 2: User 2 rates 10
        var r2Res = await client2.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest
        {
            Rating = 10,
            Comment = "Dinh cao dien anh!"
        });
        Assert.Equal(HttpStatusCode.OK, r2Res.StatusCode);

        var movieAfterR2 = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(2, movieAfterR2.VoteCount);
        Assert.Equal(9.5, movieAfterR2.Rating); // (9 + 10) / 2 = 9.5

        // Step 3: User 3 rates 7
        var r3Res = await client3.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest
        {
            Rating = 7,
            Comment = "Xem on ap"
        });
        Assert.Equal(HttpStatusCode.OK, r3Res.StatusCode);

        var movieAfterR3 = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(3, movieAfterR3.VoteCount);
        Assert.Equal(8.7, movieAfterR3.Rating); // (9 + 10 + 7) / 3 = 26 / 3 = 8.666... -> 8.7

        // Step 4: User 1 updates rating from 9 to 4
        var r1UpdateRes = await client1.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest
        {
            Rating = 4,
            Comment = "Xem lai do qua"
        });
        Assert.Equal(HttpStatusCode.OK, r1UpdateRes.StatusCode);

        var movieAfterR1Update = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(3, movieAfterR1Update.VoteCount);
        Assert.Equal(7.0, movieAfterR1Update.Rating); // (4 + 10 + 7) / 3 = 21 / 3 = 7.0

        // Step 5: User 2 deletes review
        var del2Res = await client2.DeleteAsync($"/api/reviews/movie/{movieId}");
        Assert.Equal(HttpStatusCode.OK, del2Res.StatusCode);

        var movieAfterDel2 = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(2, movieAfterDel2.VoteCount);
        Assert.Equal(5.5, movieAfterDel2.Rating); // (4 + 7) / 2 = 5.5

        // Step 6: Admin deletes User 3's review via /api/admin/reviews/{id}
        var allReviews = (await client1.GetFromJsonAsync<ApiResponse<MovieReviewsDto>>($"/api/reviews/movie/{movieId}"))!.Data!;
        var user3Review = allReviews.Reviews.Single(r => r.Rating == 7);

        var adminDelRes = await adminClient.DeleteAsync($"/api/admin/reviews/{user3Review.Id}");
        Assert.Equal(HttpStatusCode.OK, adminDelRes.StatusCode);

        var movieAfterAdminDel = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(1, movieAfterAdminDel.VoteCount);
        Assert.Equal(4.0, movieAfterAdminDel.Rating); // only User 1 (rating 4) remains

        // Step 7: User 1 deletes their review -> 0 reviews remain
        var del1Res = await client1.DeleteAsync($"/api/reviews/movie/{movieId}");
        Assert.Equal(HttpStatusCode.OK, del1Res.StatusCode);

        var movieAfterDelAll = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        Assert.Equal(0, movieAfterDelAll.VoteCount);
        Assert.Equal(0.0, movieAfterDelAll.Rating);

        var reviewsDtoFinal = (await client1.GetFromJsonAsync<ApiResponse<MovieReviewsDto>>($"/api/reviews/movie/{movieId}"))!.Data!;
        Assert.Equal(0, reviewsDtoFinal.TotalReviews);
        Assert.Equal(0.0, reviewsDtoFinal.AverageRating);
    }

    [Fact]
    public async Task Empirical_Rating_InputValidation_And_Rejection()
    {
        var movieId = await CreateIsolatedMovieAsync("Phim Validation Test", $"val-movie-{Guid.NewGuid():N}");

        using var client = factory.CreateClient();
        var email = $"val-user-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);

        // Rating < 1 is rejected (0, -1)
        var zeroRes = await client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest { Rating = 0, Comment = "Bad" });
        Assert.Equal(HttpStatusCode.BadRequest, zeroRes.StatusCode);

        var negRes = await client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest { Rating = -1, Comment = "Negative" });
        Assert.Equal(HttpStatusCode.BadRequest, negRes.StatusCode);

        // Rating > 10 is rejected (11)
        var elevenRes = await client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest { Rating = 11, Comment = "Over" });
        Assert.Equal(HttpStatusCode.BadRequest, elevenRes.StatusCode);

        // Boundaries 1 and 10 are accepted
        var oneRes = await client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest { Rating = 1, Comment = "Lowest valid" });
        Assert.Equal(HttpStatusCode.OK, oneRes.StatusCode);

        var tenRes = await client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest { Rating = 10, Comment = "Highest valid" });
        Assert.Equal(HttpStatusCode.OK, tenRes.StatusCode);

        // Non-existent movie returns 404
        var nonExistentRes = await client.PutAsJsonAsync("/api/reviews/movie/999999", new UpsertReviewRequest { Rating = 8, Comment = "Missing" });
        Assert.Equal(HttpStatusCode.NotFound, nonExistentRes.StatusCode);

        // Deleting non-existent review returns 404
        using var client2 = factory.CreateClient();
        await RegisterUserAsync(client2, $"norev-{Guid.NewGuid():N}@test.vn");
        var delRes = await client2.DeleteAsync($"/api/reviews/movie/{movieId}");
        Assert.Equal(HttpStatusCode.NotFound, delRes.StatusCode);
    }

    [Fact]
    public async Task Empirical_Rating_MultiMovieIsolation()
    {
        var movieAId = await CreateIsolatedMovieAsync("Movie A", $"movie-a-{Guid.NewGuid():N}");
        var movieBId = await CreateIsolatedMovieAsync("Movie B", $"movie-b-{Guid.NewGuid():N}");

        using var client1 = factory.CreateClient();
        using var client2 = factory.CreateClient();

        await RegisterUserAsync(client1, $"user-iso1-{Guid.NewGuid():N}@test.vn");
        await RegisterUserAsync(client2, $"user-iso2-{Guid.NewGuid():N}@test.vn");

        // User 1 reviews Movie A with 10
        await client1.PutAsJsonAsync($"/api/reviews/movie/{movieAId}", new UpsertReviewRequest { Rating = 10, Comment = "Movie A top" });

        // User 2 reviews Movie B with 4
        await client2.PutAsJsonAsync($"/api/reviews/movie/{movieBId}", new UpsertReviewRequest { Rating = 4, Comment = "Movie B mid" });

        var a = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieAId}"))!.Data!;
        var b = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieBId}"))!.Data!;

        Assert.Equal(1, a.VoteCount);
        Assert.Equal(10.0, a.Rating);
        Assert.Equal(1, b.VoteCount);
        Assert.Equal(4.0, b.Rating);

        // Delete review on Movie A -> Movie B must not be altered
        await client1.DeleteAsync($"/api/reviews/movie/{movieAId}");

        var aAfter = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieAId}"))!.Data!;
        var bAfter = (await client1.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieBId}"))!.Data!;

        Assert.Equal(0, aAfter.VoteCount);
        Assert.Equal(0.0, aAfter.Rating);
        Assert.Equal(1, bAfter.VoteCount);
        Assert.Equal(4.0, bAfter.Rating);
    }

    [Fact]
    public async Task Empirical_StreamGating_ComprehensiveCoverageAcrossAllEndpoints()
    {
        // Movie 1 in catalog has episodes with both VipFastServer and 4K streams as well as standard streams
        // Episode 1 of Movie 1 has ID 1

        // 1. Setup Clients
        using var guestClient = factory.CreateClient();

        using var freeClient = factory.CreateClient();
        var freeEmail = $"gate-free-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(freeClient, freeEmail);
        await SetUserMembershipAsync(freeEmail, MembershipTier.Free, null);

        using var expiredVipClient = factory.CreateClient();
        var expiredEmail = $"gate-expired-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(expiredVipClient, expiredEmail);
        await SetUserMembershipAsync(expiredEmail, MembershipTier.Vip, DateTime.UtcNow.AddHours(-1)); // expired 1 hr ago

        using var activeVipClient = factory.CreateClient();
        var vipEmail = $"gate-active-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(activeVipClient, vipEmail);
        await SetUserMembershipAsync(vipEmail, MembershipTier.Vip, DateTime.UtcNow.AddDays(30)); // active

        using var adminClient = factory.CreateClient();
        var adminEmail = $"gate-admin-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(adminClient, adminEmail);
        await SetUserMembershipAsync(adminEmail, MembershipTier.Free, null, isAdmin: true);

        // Endpoints to test:
        // A. /api/movies/1
        // B. /api/movies/slug/dune-part-three
        // C. /api/episodes/movie/1
        // D. /api/episodes/1

        var restrictedClients = new[] { ("Guest", guestClient), ("Free", freeClient), ("ExpiredVip", expiredVipClient) };
        var permittedClients = new[] { ("ActiveVip", activeVipClient), ("Admin", adminClient) };

        // Test Endpoint A: /api/movies/1
        foreach (var (label, client) in restrictedClients)
        {
            var res = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
            var streams = res.Episodes.SelectMany(e => e.Streams).ToList();

            var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.True(string.IsNullOrEmpty(s.StreamUrl), $"{label} should have empty stream url for VIP/4K on /api/movies/1"));

            var standard = streams.Where(s => s.ServerType != StreamServerType.VipFastServer && !s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.NotEmpty(standard);
            Assert.All(standard, s => Assert.False(string.IsNullOrEmpty(s.StreamUrl), $"{label} should have playable url for standard stream"));
        }

        foreach (var (label, client) in permittedClients)
        {
            var res = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
            var streams = res.Episodes.SelectMany(e => e.Streams).ToList();

            var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.Equal(string.Empty, s.StreamUrl));
        }

        // Test Endpoint B: /api/movies/slug/dune-part-three
        foreach (var (label, client) in restrictedClients)
        {
            var res = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/slug/dune-part-three"))!.Data!;
            var streams = res.Episodes.SelectMany(e => e.Streams).ToList();

            var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.True(string.IsNullOrEmpty(s.StreamUrl), $"{label} on slug route"));
        }

        foreach (var (label, client) in permittedClients)
        {
            var res = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/slug/dune-part-three"))!.Data!;
            var streams = res.Episodes.SelectMany(e => e.Streams).ToList();

            var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.Equal(string.Empty, s.StreamUrl));
        }

        // Test Endpoint C & D: /api/episodes/movie/1 and /api/episodes/{firstEpId}
        var epList = (await guestClient.GetFromJsonAsync<ApiResponse<List<EpisodeDto>>>("/api/episodes/movie/1"))!.Data!;
        var firstEpId = epList.First().Id;

        foreach (var (label, client) in restrictedClients)
        {
            var episodes = (await client.GetFromJsonAsync<ApiResponse<List<EpisodeDto>>>("/api/episodes/movie/1"))!.Data!;
            var streams = episodes.SelectMany(e => e.Streams).ToList();

            var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.True(string.IsNullOrEmpty(s.StreamUrl), $"{label} on /api/episodes/movie/1"));
        }

        foreach (var (label, client) in permittedClients)
        {
            var episodes = (await client.GetFromJsonAsync<ApiResponse<List<EpisodeDto>>>("/api/episodes/movie/1"))!.Data!;
            var streams = episodes.SelectMany(e => e.Streams).ToList();

            var vipAnd4k = streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.Equal(string.Empty, s.StreamUrl));
        }

        // Test Endpoint D: /api/episodes/{firstEpId}
        foreach (var (label, client) in restrictedClients)
        {
            var ep = (await client.GetFromJsonAsync<ApiResponse<EpisodeDto>>($"/api/episodes/{firstEpId}"))!.Data!;
            var vipAnd4k = ep.Streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.True(string.IsNullOrEmpty(s.StreamUrl), $"{label} on /api/episodes/{firstEpId}"));
        }

        foreach (var (label, client) in permittedClients)
        {
            var ep = (await client.GetFromJsonAsync<ApiResponse<EpisodeDto>>($"/api/episodes/{firstEpId}"))!.Data!;
            var vipAnd4k = ep.Streams.Where(s => s.ServerType == StreamServerType.VipFastServer || (!string.IsNullOrEmpty(s.Quality) && s.Quality.Contains("4K", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(vipAnd4k);
            Assert.All(vipAnd4k, s => Assert.Equal(string.Empty, s.StreamUrl));
        }
    }

    [Fact]
    public async Task Empirical_StreamGating_DoesNotMutateDatabaseOrLeakAcrossRequests()
    {
        using var guestClient = factory.CreateClient();
        using var vipClient = factory.CreateClient();
        var vipEmail = $"leak-vip-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(vipClient, vipEmail);
        await SetUserMembershipAsync(vipEmail, MembershipTier.Vip, DateTime.UtcNow.AddDays(7));

        // 1. Guest calls first -> VIP masked
        var guest1 = (await guestClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        var guestStreams1 = guest1.Episodes.SelectMany(e => e.Streams).Where(s => s.ServerType == StreamServerType.VipFastServer).ToList();
        Assert.All(guestStreams1, s => Assert.Equal(string.Empty, s.StreamUrl));

        // 2. VIP calls second -> VIP unmasked
        var vip = (await vipClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        var vipStreams = vip.Episodes.SelectMany(e => e.Streams).Where(s => s.ServerType == StreamServerType.VipFastServer).ToList();
        Assert.All(vipStreams, s => Assert.Equal(string.Empty, s.StreamUrl));

        // 3. Guest calls third -> VIP still masked (no cache pollution)
        var guest2 = (await guestClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>("/api/movies/1"))!.Data!;
        var guestStreams2 = guest2.Episodes.SelectMany(e => e.Streams).Where(s => s.ServerType == StreamServerType.VipFastServer).ToList();
        Assert.All(guestStreams2, s => Assert.Equal(string.Empty, s.StreamUrl));

        // 4. Verify DB entity still has genuine stream URLs
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var dbStreams = await db.StreamSources
            .Where(s => s.ServerType == StreamServerType.VipFastServer)
            .ToListAsync();
        Assert.NotEmpty(dbStreams);
        Assert.All(dbStreams, s => Assert.False(string.IsNullOrEmpty(s.StreamUrl), "Database StreamUrl must never be wiped"));
    }

    [Fact]
    public async Task Empirical_Rating_RapidUpsertBySameUser_DoesNotDuplicateVoteCount()
    {
        var movieId = await CreateIsolatedMovieAsync("Phim Single User Repeats", $"repeat-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        var email = $"repeat-user-{Guid.NewGuid():N}@test.vn";
        await RegisterUserAsync(client, email);

        // User updates rating 5 times
        for (int rating = 2; rating <= 10; rating += 2)
        {
            var res = await client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest
            {
                Rating = rating,
                Comment = $"Lan sua thu {rating}"
            });
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var movie = (await client.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
            Assert.Equal(1, movie.VoteCount); // VoteCount MUST remain 1
            Assert.Equal((double)rating, movie.Rating);
        }
    }

    [Fact]
    public async Task Empirical_Rating_ConcurrentReviews_RecalculationUnderConcurrency()
    {
        var movieId = await CreateIsolatedMovieAsync("Phim Concurrency Test", $"concur-{Guid.NewGuid():N}");

        // Create 10 distinct clients
        var clients = new List<(HttpClient Client, int Rating)>();
        for (int i = 1; i <= 10; i++)
        {
            var c = factory.CreateClient();
            var email = $"concur-user-{i}-{Guid.NewGuid():N}@test.vn";
            await RegisterUserAsync(c, email);
            clients.Add((c, i));
        }

        // Fire all 10 reviews concurrently
        var tasks = clients.Select(pair => pair.Client.PutAsJsonAsync($"/api/reviews/movie/{movieId}", new UpsertReviewRequest
        {
            Rating = pair.Rating,
            Comment = $"Concurrent review {pair.Rating}"
        })).ToArray();

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // After all concurrent writes settle, verify final recalculated stats
        using var verifyClient = factory.CreateClient();
        var movie = (await verifyClient.GetFromJsonAsync<ApiResponse<MovieDetailDto>>($"/api/movies/{movieId}"))!.Data!;
        var reviewsDto = (await verifyClient.GetFromJsonAsync<ApiResponse<MovieReviewsDto>>($"/api/reviews/movie/{movieId}"))!.Data!;

        // Total votes must be 10
        Assert.Equal(10, movie.VoteCount);
        Assert.Equal(10, reviewsDto.TotalReviews);

        // Average of 1..10 is 55 / 10 = 5.5
        Assert.Equal(5.5, movie.Rating);
        Assert.Equal(5.5, reviewsDto.AverageRating);
    }
}
