using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Server.Data;

public sealed class DevelopmentDataSeeder(
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<DevelopmentDataSeeder> logger)
{
    private static readonly IReadOnlyDictionary<int, (string Poster, string Backdrop)> SharpArtwork =
        Enumerable.Range(1, 40).ToDictionary(
            id => id,
            id => ($"/images/movies/movie-{id}-poster.jpg", $"/images/movies/movie-{id}-backdrop.jpg"));

    private static readonly string[] DemoNames =
    [
        "Nguyễn Minh An", "Trần Hải Yến", "Lê Quốc Bảo", "Phạm Thùy Linh",
        "Võ Hoàng Nam", "Đặng Nhật Vy", "Bùi Gia Huy", "Đỗ Khánh Ngân",
        "Huỳnh Anh Tú", "Ngô Mai Phương", "Dương Đức Anh", "Lý Thanh Hà",
        "Trương Minh Khang", "Phan Bảo Trâm", "Hồ Quang Vinh", "Vũ Ngọc Anh",
        "Tạ Tuấn Kiệt", "Mai Hà My"
    ];

    private static readonly string[] ReviewComments =
    [
        "Hình ảnh đẹp, nhịp phim chắc và phần âm thanh rất tốt.",
        "Câu chuyện cuốn hút, diễn xuất tự nhiên và có nhiều đoạn đáng nhớ.",
        "Mở đầu hơi chậm nhưng nửa sau thực sự bùng nổ.",
        "Một tác phẩm đáng xem lại trên màn hình lớn.",
        "Phần hình ảnh nổi bật, cách kể chuyện gọn và dễ theo dõi.",
        "Âm nhạc tạo đúng cảm xúc, đoạn kết được xử lý thuyết phục.",
        "Dàn nhân vật có chiều sâu và tương tác rất tự nhiên.",
        "Kịch bản còn vài điểm dễ đoán nhưng tổng thể vẫn hấp dẫn.",
        "Chất lượng sản xuất tốt, nhiều khung hình rất điện ảnh.",
        "Xem liền mạch không bị hụt nhịp, phù hợp để xem cuối tuần.",
        "Ý tưởng quen thuộc nhưng cách triển khai có nét riêng.",
        "Một trải nghiệm giải trí trọn vẹn và vừa đủ cảm xúc."
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await DatabaseSchemaHelper.EnsureTablesCreatedAsync(context, cancellationToken);
        await SeedCatalogAsync(context, cancellationToken);
        await SynchronizeSharpArtworkAsync(context, cancellationToken);
        await SynchronizeStreamsAndTrailersAsync(context, cancellationToken);

        await SeedAccountsAsync(context, cancellationToken);

        logger.LogInformation(
            "Development database ready with {MovieCount} movies, {GenreCount} genres, {UserCount} users, {ReviewCount} reviews and {PaymentCount} payments.",
            await context.Movies.CountAsync(cancellationToken),
            await context.Genres.CountAsync(cancellationToken),
            await context.Users.CountAsync(cancellationToken),
            await context.Reviews.CountAsync(cancellationToken),
            await context.Payments.CountAsync(cancellationToken));
    }

    private static async Task SynchronizeSharpArtworkAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var movieIds = SharpArtwork.Keys.ToArray();
        var movies = await context.Movies
            .Include(movie => movie.Episodes)
            .Where(movie => movieIds.Contains(movie.Id))
            .ToListAsync(cancellationToken);

        foreach (var movie in movies)
        {
            var artwork = SharpArtwork[movie.Id];
            movie.PosterUrl = artwork.Poster;
            movie.BackdropUrl = artwork.Backdrop;

            foreach (var episode in movie.Episodes)
            {
                episode.ThumbnailUrl = artwork.Backdrop;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SynchronizeStreamsAndTrailersAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var allMovies = await context.Movies.ToListAsync(cancellationToken);
        var mockMoviesById = MockMovieDatabase.Movies.ToDictionary(movie => movie.Id);

        foreach (var movie in allMovies)
        {
            if (mockMoviesById.TryGetValue(movie.Id, out var mockMovie) &&
                !string.IsNullOrWhiteSpace(mockMovie.TrailerUrl) &&
                movie.TrailerUrl != mockMovie.TrailerUrl)
            {
                movie.TrailerUrl = mockMovie.TrailerUrl;
            }
        }

        var deadStreams = await context.StreamSources
            .Where(stream => stream.StreamUrl.Contains("commondatastorage.googleapis.com"))
            .ToListAsync(cancellationToken);

        var mockEpisodesById = MockMovieDatabase.Movies
            .SelectMany(movie => movie.Episodes)
            .ToDictionary(episode => episode.Id);

        foreach (var stream in deadStreams)
        {
            if (mockEpisodesById.TryGetValue(stream.EpisodeId, out var mockEpisode))
            {
                var matchingStream = mockEpisode.Streams.ElementAtOrDefault(stream.Position - 1)
                                     ?? mockEpisode.Streams.FirstOrDefault(s => s.Quality == stream.Quality)
                                     ?? mockEpisode.Streams.FirstOrDefault();

                if (matchingStream != null && !matchingStream.StreamUrl.Contains("commondatastorage.googleapis.com"))
                {
                    stream.StreamUrl = matchingStream.StreamUrl;
                    continue;
                }
            }

            stream.StreamUrl = stream.StreamUrl.Contains("ElephantsDream")
                ? "https://vjs.zencdn.net/v/oceans.mp4"
                : stream.StreamUrl.Contains("TearsOfSteel")
                    ? "https://media.w3.org/2010/05/sintel/trailer.mp4"
                    : stream.StreamUrl.Contains("ForBiggerBlazes")
                        ? "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-720p.mp4"
                        : "https://cdn.plyr.io/static/demo/View_From_A_Blue_Moon_Trailer-1080p.mp4";
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedCatalogAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        var genreEntities = await context.Genres.ToListAsync(cancellationToken);
        var genresByName = genreEntities.ToDictionary(genre => genre.Name, StringComparer.OrdinalIgnoreCase);
        var usedGenreIds = genreEntities.Select(genre => genre.Id).ToHashSet();
        var nextGenreId = usedGenreIds.Count == 0 ? 1 : usedGenreIds.Max() + 1;

        foreach (var sourceGenre in MockMovieDatabase.Genres)
        {
            if (genresByName.ContainsKey(sourceGenre.Name)) continue;

            var genreId = usedGenreIds.Add(sourceGenre.Id) ? sourceGenre.Id : nextGenreId++;
            var genre = new GenreEntity { Id = genreId, Name = sourceGenre.Name, Slug = sourceGenre.Slug };
            genreEntities.Add(genre);
            genresByName.Add(genre.Name, genre);
            context.Genres.Add(genre);
        }

        foreach (var genreName in MockMovieDatabase.Movies.SelectMany(movie => movie.Genres).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (genresByName.ContainsKey(genreName)) continue;

            while (!usedGenreIds.Add(nextGenreId)) nextGenreId++;
            var genre = new GenreEntity { Id = nextGenreId, Name = genreName, Slug = $"genre-{nextGenreId}" };
            nextGenreId++;
            genreEntities.Add(genre);
            genresByName.Add(genreName, genre);
            context.Genres.Add(genre);
        }

        var existingMovieIds = await context.Movies
            .Select(movie => movie.Id)
            .ToHashSetAsync(cancellationToken);

        foreach (var source in MockMovieDatabase.Movies)
        {
            if (existingMovieIds.Contains(source.Id)) continue;

            var movie = new MovieEntity
            {
                Id = source.Id,
                Title = source.Title,
                OriginalTitle = source.OriginalTitle,
                Slug = source.Slug,
                Overview = source.Overview,
                PosterUrl = source.PosterUrl,
                BackdropUrl = source.BackdropUrl,
                TrailerUrl = source.TrailerUrl,
                Rating = source.Rating,
                VoteCount = source.VoteCount,
                ReleaseYear = source.ReleaseYear,
                DurationMinutes = source.DurationMinutes,
                AgeRating = source.AgeRating,
                Quality = source.Quality,
                Type = source.Type,
                Country = source.Country,
                ViewCount = source.ViewCount,
                IsTrending = source.IsTrending,
                IsFeatured = source.IsFeatured,
                Director = source.Director,
                TotalEpisodes = source.TotalEpisodes,
                CurrentEpisode = source.CurrentEpisode,
                MovieGenres = source.Genres.Select(name => new MovieGenreEntity { Genre = genresByName[name] }).ToList(),
                CastMembers = source.Cast.Select((name, index) => new CastMemberEntity { Position = index + 1, Name = name }).ToList(),
                Episodes = source.Episodes.Select(episode => new EpisodeEntity
                {
                    Id = episode.Id,
                    EpisodeNumber = episode.EpisodeNumber,
                    Title = episode.Title,
                    DurationMinutes = episode.DurationMinutes,
                    ThumbnailUrl = episode.ThumbnailUrl,
                    Overview = episode.Overview,
                    Streams = episode.Streams.Select((stream, index) => new StreamSourceEntity
                    {
                        Position = index + 1,
                        ServerName = stream.ServerName,
                        ServerType = stream.ServerType,
                        Quality = stream.Quality,
                        StreamUrl = stream.StreamUrl
                    }).ToList()
                }).ToList()
            };
            context.Movies.Add(movie);
        }

        await context.SaveChangesAsync(cancellationToken);

        // Migrate / upgrade stream sources to multi-server if existing database has fewer than 4 servers
        var episodesToUpdate = await context.Episodes.Include(e => e.Streams)
            .Where(e => e.Streams.Select(s => s.ServerName).Distinct().Count() < 4)
            .ToListAsync(cancellationToken);
        if (episodesToUpdate.Count > 0)
        {
            foreach (var ep in episodesToUpdate)
            {
                context.StreamSources.RemoveRange(ep.Streams);
                var newStreams = MockMovieDatabase.GenerateMultiServerStreams(ep.Id);
                foreach (var s in newStreams)
                {
                    context.StreamSources.Add(new StreamSourceEntity
                    {
                        EpisodeId = ep.Id,
                        Position = s.Position,
                        ServerName = s.ServerName,
                        ServerType = s.ServerType,
                        Quality = s.Quality,
                        StreamUrl = s.StreamUrl
                    });
                }
            }
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedAccountsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        const string seedDomain = "@cinemaflow.demo";
        var now = DateTime.UtcNow;
        var existingUsers = await context.Users.ToListAsync(cancellationToken);
        var usersByEmail = existingUsers.ToDictionary(user => user.NormalizedEmail, StringComparer.OrdinalIgnoreCase);
        var usedIds = existingUsers.Select(user => user.Id).ToHashSet();
        var users = new List<UserEntity>();
        // Development only: never reset existing account passwords on startup.
        const string adminEmail = "admin@cinemaflow.vn";
        var adminSalt = RandomNumberGenerator.GetBytes(16);
        var adminHash = HashPassword("Password123!", adminSalt);
        var createdAdmin = !usersByEmail.ContainsKey(adminEmail);
        if (!usersByEmail.TryGetValue(adminEmail, out var adminUser))
        {
            var adminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            adminUser = new UserEntity
            {
                Id = usedIds.Add(adminId) ? adminId : Guid.NewGuid(),
                FullName = "Quản Trị Viên",
                Email = adminEmail,
                NormalizedEmail = adminEmail,
                PasswordSalt = adminSalt,
                PasswordHash = adminHash,
                Membership = MembershipTier.Vip,
                VipExpiresAtUtc = now.AddYears(10),
                CreatedAtUtc = now.AddMonths(-12)
            };
            users.Add(adminUser);
            context.Users.Add(adminUser);
            usersByEmail[adminEmail] = adminUser;
        }
        else
        {
            users.Add(adminUser);
        }
        if ((createdAdmin || adminUser.Id == Guid.Parse("11111111-1111-1111-1111-111111111111"))
            && !await context.UserSecurity.AnyAsync(item => item.UserId == adminUser.Id, cancellationToken))
            context.UserSecurity.Add(new UserSecurityEntity { UserId = adminUser.Id, IsAdmin = true });

        for (var index = 0; index < DemoNames.Length; index++)
        {
            var email = index == 0 ? "demo@cinemaflow.vn" : $"viewer{index + 1:00}{seedDomain}";
            if (usersByEmail.TryGetValue(email, out var existing))
            {
                users.Add(existing);
                continue;
            }

            var password = index == 0 ? "Password123!" : "Cinema123";
            var salt = RandomNumberGenerator.GetBytes(16);
            var isVip = index < 6;
            var deterministicId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}");
            var user = new UserEntity
            {
                Id = usedIds.Add(deterministicId) ? deterministicId : Guid.NewGuid(),
                FullName = DemoNames[index],
                Email = email,
                NormalizedEmail = email.ToLowerInvariant(),
                PasswordSalt = salt,
                PasswordHash = HashPassword(password, salt),
                Membership = isVip ? MembershipTier.Vip : MembershipTier.Free,
                VipExpiresAtUtc = isVip ? now.AddDays(30 + index * 15) : null,
                CreatedAtUtc = now.AddDays(-(120 + index * 7))
            };
            users.Add(user);
            context.Users.Add(user);
        }
        await context.SaveChangesAsync(cancellationToken);

        var movies = await context.Movies.OrderBy(movie => movie.Id).Select(movie => movie.Id).ToListAsync(cancellationToken);
        var seededUserIds = users.Select(user => user.Id).ToList();
        var existingReviewKeys = (await context.Reviews
                .Where(review => seededUserIds.Contains(review.UserId))
                .Select(review => new { review.UserId, review.MovieId })
                .ToListAsync(cancellationToken))
            .Select(item => $"{item.UserId:N}:{item.MovieId}")
            .ToHashSet(StringComparer.Ordinal);
        var reviews = new List<ReviewEntity>();
        for (var userIndex = 0; userIndex < users.Count; userIndex++)
        {
            for (var offset = 0; offset < 12 && offset < movies.Count; offset++)
            {
                var movieId = movies[(userIndex * 5 + offset * 3) % movies.Count];
                if (!existingReviewKeys.Add($"{users[userIndex].Id:N}:{movieId}")) continue;
                var createdAt = now.AddDays(-(userIndex * 2 + offset + 1)).AddMinutes(-(userIndex * 11));
                reviews.Add(new ReviewEntity
                {
                    Id = Guid.NewGuid(),
                    MovieId = movieId,
                    UserId = users[userIndex].Id,
                    Rating = 7 + (userIndex + offset) % 4,
                    Comment = ReviewComments[(userIndex + offset) % ReviewComments.Length],
                    CreatedAtUtc = createdAt,
                    UpdatedAtUtc = createdAt.AddHours(offset % 3)
                });
            }
        }
        context.Reviews.AddRange(reviews);

        var existingOrderCodes = await context.Payments.Select(payment => payment.OrderCode).ToHashSetAsync(cancellationToken);
        for (var index = 0; index < 6; index++)
        {
            var orderCode = $"SEED{index + 1:00000000000000000000}";
            if (existingOrderCodes.Contains(orderCode)) continue;
            var paidAt = now.AddDays(-(24 + index * 9));
            context.Payments.Add(new PaymentEntity
            {
                Id = Guid.NewGuid(),
                OrderCode = orderCode,
                UserId = users[index].Id,
                PlanId = index % 2 == 0 ? "vip-90" : "vip-30",
                PlanName = index % 2 == 0 ? "VIP 90 ngày" : "VIP 30 ngày",
                Amount = index % 2 == 0 ? 149000 : 59000,
                Method = (PaymentMethod)(index % 3 + 1),
                Status = PaymentStatus.Completed,
                CreatedAtUtc = paidAt.AddMinutes(-3),
                PaidAtUtc = paidAt,
                VipExpiresAtUtc = users[index].VipExpiresAtUtc
            });
        }

        if (!await context.Notifications.AnyAsync(cancellationToken))
        {
            var sampleUsers = users.Take(3).ToList();
            foreach (var u in sampleUsers)
            {
                context.Notifications.AddRange(
                    new NotificationEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = u.Id,
                        Title = "Tập mới đã lên sóng",
                        Copy = "Xứ Cát: Phần Ba vừa cập nhật bản chiếu rạp đầy đủ 4K HDR.",
                        Icon = "Play",
                        ActionUrl = "/watch/1",
                        Read = false,
                        CreatedAtUtc = now.AddMinutes(-12)
                    },
                    new NotificationEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = u.Id,
                        Title = "Phim trong tủ sắp rời thư viện",
                        Copy = "Bạn còn 3 ngày để xem lại các tác phẩm đã lưu trong tủ phim.",
                        Icon = "Bookmark",
                        ActionUrl = "/watchlist",
                        Read = false,
                        CreatedAtUtc = now.AddHours(-2)
                    },
                    new NotificationEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = u.Id,
                        Title = "CinemaFlow VIP",
                        Copy = "Máy chủ VIP 4K mới đã sẵn sàng cho tài khoản của bạn.",
                        Icon = "Crown",
                        ActionUrl = "/vip",
                        Read = true,
                        CreatedAtUtc = now.AddDays(-1)
                    }
                );
            }
        }

        if (!await context.WatchProgress.AnyAsync(cancellationToken) && movies.Count > 0)
        {
            var sampleUsers = users.Take(2).ToList();
            foreach (var u in sampleUsers)
            {
                var m1 = movies.Contains(1) ? 1 : movies[0];
                var m2 = movies.Contains(2) ? 2 : (movies.Count > 1 ? movies[1] : movies[0]);
                var m3 = movies.Contains(3) ? 3 : (movies.Count > 2 ? movies[2] : movies[0]);

                context.WatchProgress.AddRange(
                    new WatchProgressEntity
                    {
                        UserId = u.Id,
                        MovieId = m1,
                        EpisodeNumber = 8,
                        ProgressSeconds = 2400,
                        DurationSeconds = 3600,
                        LastWatchedAtUtc = now.AddHours(-1)
                    },
                    new WatchProgressEntity
                    {
                        UserId = u.Id,
                        MovieId = m2,
                        EpisodeNumber = 1,
                        ProgressSeconds = 3100,
                        DurationSeconds = 7200,
                        LastWatchedAtUtc = now.AddHours(-5)
                    },
                    new WatchProgressEntity
                    {
                        UserId = u.Id,
                        MovieId = m3,
                        EpisodeNumber = 1,
                        ProgressSeconds = 4200,
                        DurationSeconds = 5400,
                        LastWatchedAtUtc = now.AddDays(-1)
                    }
                );
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
}
