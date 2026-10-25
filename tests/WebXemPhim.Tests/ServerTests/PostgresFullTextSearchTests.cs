using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Requests;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class PostgresFullTextSearchTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public PostgresFullTextSearchTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresTestDatabase Database => _fixture.Database;

    private async Task SeedSampleMoviesAsync(AppDbContext context)
    {
        var gHanhDong = new GenreEntity { Id = 1, Name = "Hành Động", Slug = "hanh-dong" };
        var gSciFi = new GenreEntity { Id = 2, Name = "Khoa Học Viễn Tưởng", Slug = "khoa-hoc-vien-tuong" };
        var gTamLy = new GenreEntity { Id = 3, Name = "Tâm Lý", Slug = "tam-ly" };
        context.Genres.AddRange(gHanhDong, gSciFi, gTamLy);

        var m1 = new MovieEntity
        {
            Title = "Bố Già",
            OriginalTitle = "The Old Father",
            Slug = "bo-gia",
            Overview = "Phim điện ảnh tâm lý hài hước gia đình Việt Nam.",
            PosterUrl = "/images/bo-gia.jpg",
            BackdropUrl = "/images/bo-gia-bg.jpg",
            Director = "Trấn Thành",
            Country = "Việt Nam",
            ReleaseYear = 2021,
            Rating = 8.5,
            Type = MovieType.Movie,
            CastMembers =
            [
                new CastMemberEntity { Name = "Tuấn Trần", Position = 1 },
                new CastMemberEntity { Name = "Lê Giang", Position = 2 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 3 }]
        };

        var m2 = new MovieEntity
        {
            Title = "Mắt Biếc",
            OriginalTitle = "Dreamy Eyes",
            Slug = "mat-biec",
            Overview = "Mối tình si của thầy giáo Ngạn.",
            PosterUrl = "/images/mat-biec.jpg",
            BackdropUrl = "/images/mat-biec-bg.jpg",
            Director = "Victor Vũ",
            Country = "Việt Nam",
            ReleaseYear = 2019,
            Rating = 8.7,
            Type = MovieType.Movie,
            CastMembers =
            [
                new CastMemberEntity { Name = "Trần Nghĩa", Position = 1 },
                new CastMemberEntity { Name = "Trúc Anh", Position = 2 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 3 }]
        };

        var m3 = new MovieEntity
        {
            Title = "Lật Mặt: 48h",
            OriginalTitle = "Face Off 48h",
            Slug = "lat-mat-48h",
            Overview = "Cuộc rượt đuổi nghẹt thở trong 48 giờ.",
            PosterUrl = "/images/lat-mat.jpg",
            BackdropUrl = "/images/lat-mat-bg.jpg",
            Director = "Lý Hải",
            Country = "Việt Nam",
            ReleaseYear = 2021,
            Rating = 7.9,
            Type = MovieType.Movie,
            CastMembers =
            [
                new CastMemberEntity { Name = "Ốc Thanh Vân", Position = 1 },
                new CastMemberEntity { Name = "Mạc Văn Khoa", Position = 2 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 1 }]
        };

        var m4 = new MovieEntity
        {
            Title = "Xứ Cát: Phần Hai",
            OriginalTitle = "Dune: Part Two",
            Slug = "xu-cat-phan-hai",
            Overview = "Hành trình báo thù của Paul Atreides - Đấng Tiên Tri của tộc Fremen.",
            PosterUrl = "/images/dune2.jpg",
            BackdropUrl = "/images/dune2-bg.jpg",
            Director = "Denis Villeneuve",
            Country = "Mỹ",
            ReleaseYear = 2024,
            Rating = 8.8,
            Type = MovieType.Movie,
            CastMembers =
            [
                new CastMemberEntity { Name = "Timothée Chalamet", Position = 1 },
                new CastMemberEntity { Name = "Zendaya", Position = 2 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 1 }, new MovieGenreEntity { GenreId = 2 }]
        };

        var m5 = new MovieEntity
        {
            Title = "Kỵ Sĩ Bóng Đêm",
            OriginalTitle = "The Dark Knight",
            Slug = "ky-si-bong-dem",
            Overview = "Hiệp sĩ bóng đêm bảo vệ Gotham trước Joker.",
            PosterUrl = "/images/dark-knight.jpg",
            BackdropUrl = "/images/dark-knight-bg.jpg",
            Director = "Christopher Nolan",
            Country = "Mỹ",
            ReleaseYear = 2008,
            Rating = 9.0,
            Type = MovieType.Movie,
            CastMembers =
            [
                new CastMemberEntity { Name = "Christian Bale", Position = 1 },
                new CastMemberEntity { Name = "Heath Ledger", Position = 2 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 1 }]
        };

        var m6 = new MovieEntity
        {
            Title = "Tôi Thăng Cấp Một Mình",
            OriginalTitle = "Solo Leveling",
            Slug = "solo-leveling",
            Overview = "Thợ săn yếu nhất nhân loại thức tỉnh sức mạnh vô song.",
            PosterUrl = "/images/solo-leveling.jpg",
            BackdropUrl = "/images/solo-leveling-bg.jpg",
            Director = "Shunsuke Nakashige",
            Country = "Nhật Bản",
            ReleaseYear = 2024,
            Rating = 8.6,
            Type = MovieType.Anime,
            CastMembers =
            [
                new CastMemberEntity { Name = "Taito Ban", Position = 1 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 1 }, new MovieGenreEntity { GenreId = 2 }]
        };

        var m7 = new MovieEntity
        {
            Title = "Oppenheimer",
            OriginalTitle = "Oppenheimer",
            Slug = "oppenheimer",
            Overview = "Câu chuyện về cha đẻ của bom nguyên tử.",
            PosterUrl = "/images/oppenheimer.jpg",
            BackdropUrl = "/images/oppenheimer-bg.jpg",
            Director = "Christopher Nolan",
            Country = "Mỹ",
            ReleaseYear = 2023,
            Rating = 8.9,
            Type = MovieType.Movie,
            CastMembers =
            [
                new CastMemberEntity { Name = "Cillian Murphy", Position = 1 }
            ],
            MovieGenres = [new MovieGenreEntity { GenreId = 3 }]
        };

        context.Movies.AddRange(m1, m2, m3, m4, m5, m6, m7);
        await context.SaveChangesAsync();
        await DatabaseSchemaHelper.SyncMovieSearchIndexAsync(context);
    }

    [Fact]
    public async Task SearchVectorColumn_AndTriggers_CreatedSuccessfully()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);

        var indexed = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(1) AS \"Value\" FROM movies WHERE \"search_vector\" IS NOT NULL").SingleAsync();
        Assert.Equal(7, indexed);

        var triggers = await context.Database.SqlQueryRaw<string>(
            "SELECT trigger_name AS \"Value\" FROM information_schema.triggers WHERE trigger_schema = current_schema()").ToListAsync();
        Assert.Contains("trg_movies_search_vector", triggers);
        Assert.Contains("trg_cast_movies_search_vector", triggers);
    }

    [Fact]
    public async Task GinIndex_ExistsOnSearchVector()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();

        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync();
        string? indexDefinition;
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE schemaname = current_schema() AND tablename = 'movies' AND indexname = 'IX_movies_SearchVector';";
            indexDefinition = (string?)await cmd.ExecuteScalarAsync();
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }

        Assert.NotNull(indexDefinition);
        Assert.Contains("GIN", indexDefinition, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Xứ Cát", "Xứ Cát: Phần Hai")]
    [InlineData("Đấng Tiên Tri", "Xứ Cát: Phần Hai")]
    [InlineData("Kỵ Sĩ Bóng Đêm", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("Bố Già", "Bố Già")]
    [InlineData("Mắt Biếc", "Mắt Biếc")]
    public async Task Search_WithVietnameseDiacritics_ReturnsCorrectMovie(string keyword, string expectedTitle)
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        var results = await service.SearchMoviesAsync(keyword, 10);
        Assert.NotEmpty(results);
        Assert.Contains(results, m => m.Title == expectedTitle);
    }

    [Theory]
    [InlineData("xu cat", "Xứ Cát: Phần Hai")]
    [InlineData("dang tien tri", "Xứ Cát: Phần Hai")]
    [InlineData("ky si bong dem", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("toi thang cap mot minh", "Tôi Thăng Cấp Một Mình")]
    [InlineData("bo gia", "Bố Già")]
    [InlineData("mat biec", "Mắt Biếc")]
    public async Task Search_WithoutDiacritics_MatchesAccentInsensitive(string keyword, string expectedTitle)
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        var results = await service.SearchMoviesAsync(keyword, 10);
        Assert.NotEmpty(results);
        Assert.Contains(results, m => m.Title == expectedTitle);
    }

    [Theory]
    [InlineData("Timothée Chalamet", "Xứ Cát: Phần Hai")]
    [InlineData("chalamet", "Xứ Cát: Phần Hai")]
    [InlineData("Zendaya", "Xứ Cát: Phần Hai")]
    [InlineData("Heath Ledger", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("Tuấn Trần", "Bố Già")]
    [InlineData("tuan tran", "Bố Già")]
    public async Task Search_CastMember_ReturnsExpectedMovie(string keyword, string expectedTitle)
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        var results = await service.SearchMoviesAsync(keyword, 10);
        Assert.NotEmpty(results);
        Assert.Contains(results, m => m.Title == expectedTitle);
    }

    [Theory]
    [InlineData("Christopher Nolan", "Kỵ Sĩ Bóng Đêm")]
    [InlineData("Denis Villeneuve", "Xứ Cát: Phần Hai")]
    [InlineData("Trấn Thành", "Bố Già")]
    [InlineData("tran thanh", "Bố Già")]
    [InlineData("Victor Vu", "Mắt Biếc")]
    public async Task Search_Director_ReturnsExpectedMovie(string keyword, string expectedTitle)
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        var results = await service.SearchMoviesAsync(keyword, 10);
        Assert.NotEmpty(results);
        Assert.Contains(results, m => m.Title == expectedTitle);
    }

    [Theory]
    [InlineData("Xứ Cát: Phần Ba")]
    [InlineData("Spider-Man: No Way Home")]
    [InlineData("Movie (2025)")]
    [InlineData("title:")]
    [InlineData("::: --- !!!")]
    [InlineData("AND OR NOT")]
    [InlineData("\"quoted string\"")]
    [InlineData("'; DROP TABLE movies; --")]
    public async Task Search_SpecialCharactersAndColons_DoesNotThrowSyntaxError(string keyword)
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        // Must execute cleanly without throwing PostgreSQL syntax exceptions
        var results = await service.SearchMoviesAsync(keyword, 10);
        Assert.NotNull(results);

        // The movies table must still be intact after the hostile input.
        Assert.Equal(7, await context.Movies.CountAsync());
    }

    [Fact]
    public async Task Search_CombinedWithFilterOptions_RespectsGenresAndYears()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        // Search for "Nolan" with Genre "Hành Động" and Year 2008 -> should match "Kỵ Sĩ Bóng Đêm", not "Oppenheimer"
        var request = new MovieFilterRequest
        {
            Keyword = "Nolan",
            Genres = ["hanh-dong"],
            Years = [2008],
            Page = 1,
            PageSize = 10
        };

        var paged = await service.FilterMoviesAsync(request);
        Assert.Single(paged.Items);
        Assert.Equal("Kỵ Sĩ Bóng Đêm", paged.Items[0].Title);
    }

    [Fact]
    public async Task FilterMoviesAsync_WithRelevanceSorting_ReturnsSearchRankOrder()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        var request = new MovieFilterRequest
        {
            Keyword = "Biếc",
            SortBy = "relevance",
            Page = 1,
            PageSize = 10
        };

        var paged = await service.FilterMoviesAsync(request);
        Assert.Single(paged.Items);
        Assert.Equal("Mắt Biếc", paged.Items[0].Title);
        Assert.Equal(1, paged.TotalItems);
    }

    [Fact]
    public async Task Triggers_AndIndexer_SyncMovieOnCreateUpdateDelete()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        // Test 1: Insert new movie and re-index
        var newMovie = new MovieEntity
        {
            Title = "Đất Rừng Phương Nam",
            OriginalTitle = "Song of the South",
            Slug = "dat-rung-phuong-nam",
            Overview = "Chuyến phiêu lưu của bé An.",
            PosterUrl = "/images/dat-rung.jpg",
            BackdropUrl = "/images/dat-rung-bg.jpg",
            Director = "Nguyễn Quang Dũng",
            Country = "Việt Nam",
            ReleaseYear = 2023,
            Rating = 7.5,
            Type = MovieType.Movie,
            CastMembers = [new CastMemberEntity { Name = "Hồng Ánh", Position = 1 }]
        };
        context.Movies.Add(newMovie);
        await context.SaveChangesAsync();
        await DatabaseSchemaHelper.RefreshMovieSearchIndexAsync(context, newMovie.Id);

        var searchNew = await service.SearchMoviesAsync("dat rung", 5);
        Assert.Contains(searchNew, m => m.Title == "Đất Rừng Phương Nam");

        // Test 2: Update existing movie
        newMovie.Title = "Đất Rừng Phương Nam: Bản Điện Ảnh";
        await context.SaveChangesAsync();
        await DatabaseSchemaHelper.RefreshMovieSearchIndexAsync(context, newMovie.Id);

        var searchUpdated = await service.SearchMoviesAsync("Ban Dien Anh", 5);
        Assert.Contains(searchUpdated, m => m.Title == "Đất Rừng Phương Nam: Bản Điện Ảnh");

        // Test 3: Delete movie
        context.Movies.Remove(newMovie);
        await context.SaveChangesAsync();

        var searchDeleted = await service.SearchMoviesAsync("dat rung", 5);
        Assert.DoesNotContain(searchDeleted, m => m.Title.Contains("Đất Rừng"));
    }

    [Fact]
    public async Task CastMemberChange_RefreshesSearchVectorWithoutManualReindex()
    {
        var database = Database;
        await database.ResetAsync();
        await using var context = database.CreateContext();
        await SeedSampleMoviesAsync(context);
        var service = new DatabaseMovieService(database.CreateFactory());

        var movie = await context.Movies.Include(m => m.CastMembers).FirstAsync(m => m.Slug == "bo-gia");
        movie.CastMembers.Add(new CastMemberEntity { Name = "Ngô Kiến Huy", Position = 9 });
        await context.SaveChangesAsync();

        // The cast trigger must have refreshed the vector without any explicit reindex call.
        var results = await service.SearchMoviesAsync("ngô kiến huy", 5);
        Assert.Contains(results, m => m.Title == "Bố Già");
    }

    [Fact]
    public async Task FallbackSearch_OnInMemoryDatabase_WithoutFullTextIndex_WorksCorrectly()
    {
        var dbName = $"InMemorySearchTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var factory = new TestDbContextFactory(options);
        await using var context = factory.CreateDbContext();

        var movie = new MovieEntity
        {
            Title = "Người Phán Xử",
            OriginalTitle = "The Arbitrator",
            Slug = "nguoi-phan-xu",
            Overview = "Phim truyền hình tâm lý hình sự.",
            PosterUrl = "/images/npx.jpg",
            BackdropUrl = "/images/npx-bg.jpg",
            Director = "Khải Anh",
            Country = "Việt Nam",
            ReleaseYear = 2017,
            Rating = 8.8,
            Type = MovieType.Series,
            CastMembers =
            [
                new CastMemberEntity { Name = "Hoàng Dũng", Position = 1 },
                new CastMemberEntity { Name = "Việt Anh", Position = 2 }
            ]
        };
        context.Movies.Add(movie);
        await context.SaveChangesAsync();

        var service = new DatabaseMovieService(factory);

        // Search with unaccented keyword on InMemory database
        var unaccentedResults = await service.SearchMoviesAsync("nguoi phan xu", 10);
        Assert.Single(unaccentedResults);
        Assert.Equal("Người Phán Xử", unaccentedResults[0].Title);

        // Search with unaccented cast member on InMemory database
        var castResults = await service.SearchMoviesAsync("hoang dung", 10);
        Assert.Single(castResults);
        Assert.Equal("Người Phán Xử", castResults[0].Title);
    }
}