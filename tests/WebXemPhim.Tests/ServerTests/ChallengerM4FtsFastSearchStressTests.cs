using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Enums;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Utils;
using Xunit;

namespace WebXemPhim.Tests.ServerTests;

public class ChallengerM4FtsFastSearchStressTests
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

        var factory = new TestDbContextFactory(options);
        return (connection, context, factory);
    }

    #region 1. Vietnamese Text Normalization & Unicode Edge Cases

    [Fact]
    public void Normalizer_HandlesNfdVsNfcEquivalence_AcrossAllVowelsAndTones()
    {
        // Test all Vietnamese accented vowels in both precomposed (NFC) and decomposed (NFD) forms
        var nfcText = "Hành Động, Kịch Tính, Khoa Học Viễn Tưởng, Lãng Mạn, Võ Thuật, Phiêu Lưu, Hài Hước, Cổ Trang, Đất Rừng Phương Nam";
        var nfdText = nfcText.Normalize(NormalizationForm.FormD);

        // Decomposed NFD and Precomposed NFC must produce identical output
        var nfcStripped = VietnameseTextNormalizer.RemoveDiacritics(nfcText);
        var nfdStripped = VietnameseTextNormalizer.RemoveDiacritics(nfdText);
        Assert.Equal(nfcStripped, nfdStripped);

        var nfcNorm = VietnameseTextNormalizer.NormalizeForSearch(nfcText);
        var nfdNorm = VietnameseTextNormalizer.NormalizeForSearch(nfdText);
        Assert.Equal(nfcNorm, nfdNorm);
    }

    [Theory]
    [InlineData("hòa bình", "hoa binh")]
    [InlineData("hoà bình", "hoa binh")] // Old style vs new style tone placement
    [InlineData("thủy triều", "thuy trieu")]
    [InlineData("thuỷ triều", "thuy trieu")]
    public void Normalizer_HandlesNewStyleVsOldStyleTonePlacement(string input, string expected)
    {
        var result = VietnameseTextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("đẤnG TiêN TrI", "dang tien tri")]
    [InlineData("ĐỨC ĐẠT & ĐỖ QUYÊN", "duc dat & do quyen")]
    [InlineData("mẬT mÃ: 48 GIỜ", "mat ma: 48 gio")]
    [InlineData("kỴ Sĩ BÓNG ĐÊM", "ky si bong dem")]
    [InlineData("XỨ CÁT: PHẦN HAI", "xu cat: phan hai")]
    public void Normalizer_HandlesMixedCaseDiacriticsAndStrokes(string input, string expected)
    {
        var result = VietnameseTextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalizer_HandlesAllVietnameseAlphabetDiacritics()
    {
        // Full lowercase and uppercase set of Vietnamese diacritic vowels
        // a (18 forms): a à á ả ã ạ ă ằ ắ ẳ ẵ ặ â ầ ấ ẩ ẫ ậ
        // e (12 forms): e è é ẻ ẽ ẹ ê ề ế ể ễ ệ
        // i (6 forms):  i ì í ỉ ĩ ị
        // o (18 forms): o ò ó ỏ õ ọ ô ồ ố ổ ỗ ộ ơ ờ ớ ở ỡ ợ
        // u (12 forms): u ù ú ủ ũ ụ ư ừ ứ ử ữ ự
        // y (6 forms):  y ỳ ý ỷ ỹ ỵ
        // d (1 form):   đ
        var vietnameseLower = "aàáảãạăằắẳẵặâầấẩẫậeèéẻẽẹêềếểễệiìíỉĩịoòóỏõọôồốổỗộơờớởỡợuùúủũụưừứửữựyỳýỷỹỵđ";
        var expectedLower = new string('a', 18) + new string('e', 12) + new string('i', 6) +
                            new string('o', 18) + new string('u', 12) + new string('y', 6) + "d";

        var stripped = VietnameseTextNormalizer.RemoveDiacritics(vietnameseLower);
        Assert.Equal(expectedLower, stripped);

        var vietnameseUpper = "AÀÁẢÃẠĂẰẮẲẴẶÂẦẤẨẪẬEÈÉẺẼẸÊỀẾỂỄỆIÌÍỈĨỊOÒÓỎÕỌÔỒỐỔỖỘƠỜỚỞỠỢUÙÚỦŨỤƯỪỨỬỮỰYỲÝỶỸỴĐ";
        var expectedUpper = new string('A', 18) + new string('E', 12) + new string('I', 6) +
                            new string('O', 18) + new string('U', 12) + new string('Y', 6) + "D";

        var strippedUpper = VietnameseTextNormalizer.RemoveDiacritics(vietnameseUpper);
        Assert.Equal(expectedUpper, strippedUpper);
    }

    [Theory]
    [InlineData("Timothée Chalamet", "timothee chalamet")]
    [InlineData("François Ozon", "francois ozon")]
    [InlineData("Götz George", "gotz george")]
    [InlineData("Penélope Cruz", "penelope cruz")]
    [InlineData("Bong Joon-ho", "bong joon-ho")]
    public void Normalizer_HandlesEuropeanAndInternationalAccents(string input, string expected)
    {
        var result = VietnameseTextNormalizer.NormalizeForSearch(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalizer_HandlesIrregularWhitespaceAndZeroWidth()
    {
        // Non-breaking space \u00A0 and Em space \u2003
        var rawWithSpaces = "\u00A0\u00A0Bố\u2003\u2003Già\t\r\n\t";
        var normalized = VietnameseTextNormalizer.NormalizeForSearch(rawWithSpaces);
        Assert.Equal("bo gia", normalized);
    }

    #endregion

    #region 2. Adversarial FTS5 Query Construction

    [Theory]
    [InlineData("Title: Subtitle")]
    [InlineData("Spider-Man: No Way Home")]
    [InlineData("Lật Mặt: 48h")]
    [InlineData("John Wick: Chapter 4: Parabellum")]
    [InlineData("::: --- !!!")]
    [InlineData("AND OR NOT NEAR")]
    [InlineData("AND NOT (OR NEAR)")]
    [InlineData("\"unmatched quote")]
    [InlineData("\"\"double quotes\"\"")]
    [InlineData("****wildcards****")]
    [InlineData("((nested) (parentheses))")]
    [InlineData("SELECT * FROM movies; -- ' DROP TABLE")]
    [InlineData("1' OR '1'='1")]
    [InlineData("🎬🍿🎥💥🔥")]
    public void BuildFts5Query_SanitizesComplexAndAdversarialInputs_NeverThrows(string keyword)
    {
        var query = DatabaseMovieService.BuildFts5Query(keyword);
        Assert.NotNull(query);
    }

    [Fact]
    public void BuildFts5Query_MassiveKeyword_HandlesGracefullyWithoutOverflow()
    {
        var massive = string.Join(" ", Enumerable.Range(1, 300).Select(i => $"Keyword{i}"));
        var query = DatabaseMovieService.BuildFts5Query(massive);
        Assert.NotEmpty(query);
        Assert.Contains("\"Keyword1\"*", query);
        Assert.Contains("\"Keyword300\"*", query);
    }

    [Theory]
    [InlineData("Title: Subtitle")]
    [InlineData("Spider-Man: No Way Home")]
    [InlineData("AND OR NOT NEAR")]
    [InlineData("quote\"test\"")]
    [InlineData("parentheses (test)")]
    [InlineData("C++ Programming")]
    [InlineData("Top-Gun: Maverick")]
    public async Task Fts5_AdversarialQueryExecution_InSqlite_ExecutesWithoutSyntaxError(string keyword)
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var service = new DatabaseMovieService(factory);
            var results = await service.SearchMoviesAsync(keyword, 10);
            Assert.NotNull(results);
        }
    }

    #endregion

    #region 3. SQLite FTS5 Triggers & Rapid Batch CUD Integrity

    [Fact]
    public async Task Triggers_RapidBatchInsert_MaintainsExactFtsSynchronization()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var batchCount = 50;
            var movies = new List<MovieEntity>();
            for (var i = 1; i <= batchCount; i++)
            {
                movies.Add(new MovieEntity
                {
                    Title = $"Phim Hành Động Số {i}: Siêu Chiến Binh",
                    OriginalTitle = $"Action Movie #{i}: Super Warrior",
                    Slug = $"action-movie-{i}",
                    Overview = $"Mô tả tóm tắt cho bộ phim siêu anh hùng số {i}.",
                    PosterUrl = $"/images/movie-{i}.jpg",
                    BackdropUrl = $"/images/bg-{i}.jpg",
                    Director = $"Đạo Diễn {i}",
                    Country = "Việt Nam",
                    ReleaseYear = 2020 + (i % 5),
                    Rating = 7.0 + (i % 30) * 0.1,
                    Type = MovieType.Movie
                });
            }

            context.Movies.AddRange(movies);
            await context.SaveChangesAsync();

            // Verify SQLite trigger trg_movies_fts_insert populated movies_fts
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM movies_fts;";
            var ftsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(batchCount, ftsCount);

            var service = new DatabaseMovieService(factory);

            // Test search across newly inserted movies via diacritics and unaccented
            var searchAccented = await service.SearchMoviesAsync("Siêu Chiến Binh", 100);
            Assert.Equal(batchCount, searchAccented.Count);

            var searchUnaccented = await service.SearchMoviesAsync("sieu chien binh", 100);
            Assert.Equal(batchCount, searchUnaccented.Count);

            var searchSpecific = await service.SearchMoviesAsync("Số 25", 5);
            Assert.Contains(searchSpecific, m => m.Title.Contains("Số 25"));
        }
    }

    [Fact]
    public async Task Triggers_RapidBatchUpdate_ReflectsNewTitlesImmediately()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var batchCount = 30;
            var movies = new List<MovieEntity>();
            for (var i = 1; i <= batchCount; i++)
            {
                movies.Add(new MovieEntity
                {
                    Title = $"Ban Đầu {i}",
                    OriginalTitle = $"Initial {i}",
                    Slug = $"initial-{i}",
                    Overview = "Overview",
                    PosterUrl = "/p.jpg",
                    BackdropUrl = "/b.jpg",
                    Director = "Director A",
                    Country = "Việt Nam",
                    ReleaseYear = 2024,
                    Rating = 8.0,
                    Type = MovieType.Movie
                });
            }
            context.Movies.AddRange(movies);
            await context.SaveChangesAsync();

            // Update all titles in batch
            foreach (var m in movies)
            {
                m.Title = m.Title.Replace("Ban Đầu", "Cập Nhật Mới");
                m.Director = "Đạo Diễn Mới";
            }
            await context.SaveChangesAsync();

            var service = new DatabaseMovieService(factory);

            // Old title searches should return 0
            var oldSearch = await service.SearchMoviesAsync("Ban Dau", 50);
            Assert.Empty(oldSearch);

            // Trigger immediately updates title and director in movies_fts for accented searches
            var newSearchAccented = await service.SearchMoviesAsync("Cập Nhật Mới", 50);
            Assert.Equal(batchCount, newSearchAccented.Count);

            var dirSearchAccented = await service.SearchMoviesAsync("Đạo Diễn Mới", 50);
            Assert.Equal(batchCount, dirSearchAccented.Count);

            // When IndexSingleMovieAsync / SyncMoviesFtsAsync is invoked (as done by AdminMoviesController),
            // normalized_content is populated, enabling unaccented search across Latin Extended Additional diacritics
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);
            var newSearchUnaccented = await service.SearchMoviesAsync("Cap Nhat Moi", 50);
            Assert.Equal(batchCount, newSearchUnaccented.Count);
        }
    }

    [Fact]
    public async Task Triggers_RapidBatchDelete_CleansVirtualTableWithoutOrphans()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movies = new List<MovieEntity>();
            for (var i = 1; i <= 20; i++)
            {
                movies.Add(new MovieEntity
                {
                    Title = $"Phim Xóa {i}",
                    OriginalTitle = $"Delete Movie {i}",
                    Slug = $"delete-movie-{i}",
                    Overview = "Overview",
                    PosterUrl = "/p.jpg",
                    BackdropUrl = "/b.jpg",
                    Director = "Director",
                    Country = "Việt Nam",
                    ReleaseYear = 2024,
                    Rating = 7.5,
                    Type = MovieType.Movie
                });
            }
            context.Movies.AddRange(movies);
            await context.SaveChangesAsync();

            // Delete 10 movies
            var toDelete = movies.Take(10).ToList();
            context.Movies.RemoveRange(toDelete);
            await context.SaveChangesAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM movies_fts;";
            var ftsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(10, ftsCount);

            var service = new DatabaseMovieService(factory);
            var results = await service.SearchMoviesAsync("Phim Xóa", 50);
            Assert.Equal(10, results.Count);
        }
    }

    [Fact]
    public async Task IndexSingleMovieAsync_IdempotenceAndStabilityUnderRapidCalls()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movie = new MovieEntity
            {
                Title = "Thiên Đường Ẩm Thực",
                OriginalTitle = "Culinary Paradise",
                Slug = "thien-duong-am-thuc",
                Overview = "Chương trình khám phá ẩm thực bốn phương đặc sắc.",
                PosterUrl = "/p.jpg",
                BackdropUrl = "/b.jpg",
                Director = "Đạo Diễn A",
                Country = "Việt Nam",
                ReleaseYear = 2023,
                Rating = 8.0,
                Type = MovieType.Series,
                CastMembers = [new CastMemberEntity { Name = "Trường Giang", Position = 1 }]
            };
            context.Movies.Add(movie);
            await context.SaveChangesAsync();

            // Rapid repeated calls to IndexSingleMovieAsync
            for (var i = 0; i < 5; i++)
            {
                await DatabaseSchemaHelper.IndexSingleMovieAsync(context, movie.Id);
            }

            // Must have exactly 1 record in movies_fts, not 5
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM movies_fts WHERE movie_id = @id;";
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = movie.Id;
            cmd.Parameters.Add(p);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(1, count);

            var service = new DatabaseMovieService(factory);
            var results = await service.SearchMoviesAsync("Truong Giang", 5);
            Assert.Single(results);
            Assert.Equal("Thiên Đường Ẩm Thực", results[0].Title);
        }
    }

    #endregion

    #region 4. BM25 Rank Ordering Verification

    [Fact]
    public async Task Fts5_Bm25Ranking_RanksTitleMatchesAheadOfOverviewOnlyMatches()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            // Movie 1: Keyword is the title
            var m1 = new MovieEntity
            {
                Title = "Người Nhện",
                OriginalTitle = "Spider-Man",
                Slug = "nguoi-nhen",
                Overview = "Một học sinh trung học nhận được siêu năng lực sau khi bị nhện cắn.",
                PosterUrl = "/spiderman.jpg",
                BackdropUrl = "/spiderman-bg.jpg",
                Director = "Sam Raimi",
                Country = "Mỹ",
                ReleaseYear = 2002,
                Rating = 8.0,
                Type = MovieType.Movie
            };

            // Movie 2: Title does not have keyword, overview merely references it in passing
            var m2 = new MovieEntity
            {
                Title = "Vũ Trụ Hoạt Hình LEGO",
                OriginalTitle = "The LEGO Movie",
                Slug = "lego-movie",
                Overview = "Cuộc phiêu lưu hoành tráng của các khối hình đồ chơi, có sự xuất hiện chớp nhoáng của Người Nhện trong cảnh hỗn loạn.",
                PosterUrl = "/lego.jpg",
                BackdropUrl = "/lego-bg.jpg",
                Director = "Phil Lord",
                Country = "Mỹ",
                ReleaseYear = 2014,
                Rating = 8.0,
                Type = MovieType.Movie
            };

            context.Movies.AddRange(m1, m2);
            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

            var service = new DatabaseMovieService(factory);
            var request = new MovieFilterRequest
            {
                Keyword = "Người Nhện",
                SortBy = "relevance",
                Page = 1,
                PageSize = 10
            };

            var paged = await service.FilterMoviesAsync(request);
            Assert.Equal(2, paged.TotalItems);
            // Movie 1 (title match) must rank first
            Assert.Equal("Người Nhện", paged.Items[0].Title);
            Assert.Equal("Vũ Trụ Hoạt Hình LEGO", paged.Items[1].Title);
        }
    }

    [Fact]
    public async Task Fts5_RelevanceSorting_SupportsPaginationAccurately()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var movies = new List<MovieEntity>();
            for (var i = 1; i <= 6; i++)
            {
                movies.Add(new MovieEntity
                {
                    Title = $"Điệp Viên 00{i}: Điệp Vụ Tử Thần",
                    OriginalTitle = $"Agent 00{i}",
                    Slug = $"agent-00{i}",
                    Overview = "Điệp viên ngầm thực hiện nhiệm vụ bí mật.",
                    PosterUrl = "/p.jpg",
                    BackdropUrl = "/b.jpg",
                    Director = "Director",
                    Country = "Mỹ",
                    ReleaseYear = 2020 + i,
                    Rating = 7.0 + i * 0.2,
                    Type = MovieType.Movie
                });
            }
            context.Movies.AddRange(movies);
            await context.SaveChangesAsync();
            await DatabaseSchemaHelper.SyncMoviesFtsAsync(context);

            var service = new DatabaseMovieService(factory);

            // Page 1 with pageSize 2
            var page1 = await service.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "Điệp Viên",
                SortBy = "relevance",
                Page = 1,
                PageSize = 2
            });
            Assert.Equal(6, page1.TotalItems);
            Assert.Equal(2, page1.Items.Count);

            // Page 2 with pageSize 2
            var page2 = await service.FilterMoviesAsync(new MovieFilterRequest
            {
                Keyword = "Điệp Viên",
                SortBy = "relevance",
                Page = 2,
                PageSize = 2
            });
            Assert.Equal(6, page2.TotalItems);
            Assert.Equal(2, page2.Items.Count);

            // Disjoint pages
            Assert.DoesNotContain(page2.Items, m => page1.Items.Any(p1 => p1.Id == m.Id));
        }
    }

    #endregion

    #region 5. Dual-Mode Execution & Fallback Resilience

    [Fact]
    public async Task DualMode_InMemoryFallback_MatchesUnaccentedAndNormalizedTermsConsistently()
    {
        var dbName = $"InMemoryDualModeTest_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var factory = new TestDbContextFactory(options);
        await using var context = factory.CreateDbContext();

        var m1 = new MovieEntity
        {
            Title = "Gái Già Lắm Chiêu",
            OriginalTitle = "The Royal Bride",
            Slug = "gai-gia-lam-chieu",
            Overview = "Phim hài lãng mạn đậm chất quý tộc Huế.",
            PosterUrl = "/p.jpg",
            BackdropUrl = "/b.jpg",
            Director = "Bảo Nhân & Nam Cito",
            Country = "Việt Nam",
            ReleaseYear = 2020,
            Rating = 7.2,
            Type = MovieType.Movie,
            CastMembers = [new CastMemberEntity { Name = "Ninh Dương Lan Ngọc", Position = 1 }]
        };
        context.Movies.Add(m1);
        await context.SaveChangesAsync();

        var service = new DatabaseMovieService(factory);

        // Search with unaccented keyword on InMemory database
        var res1 = await service.SearchMoviesAsync("gai gia lam chieu", 5);
        Assert.Single(res1);
        Assert.Equal("Gái Già Lắm Chiêu", res1[0].Title);

        // Search with unaccented actor on InMemory database
        var res2 = await service.SearchMoviesAsync("lan ngoc", 5);
        Assert.Single(res2);
        Assert.Equal("Gái Già Lắm Chiêu", res2[0].Title);

        // Search with unaccented director on InMemory database
        var res3 = await service.SearchMoviesAsync("nam cito", 5);
        Assert.Single(res3);
        Assert.Equal("Gái Già Lắm Chiêu", res3[0].Title);
    }

    [Fact]
    public async Task SearchMoviesAsync_EmptyOrWhitespaceKeyword_ReturnsEmptyListImmediately()
    {
        var (connection, context, factory) = await CreateSqliteContextAsync();
        await using (connection)
        await using (context)
        {
            var service = new DatabaseMovieService(factory);
            Assert.Empty(await service.SearchMoviesAsync("", 10));
            Assert.Empty(await service.SearchMoviesAsync("   ", 10));
            Assert.Empty(await service.SearchMoviesAsync(null!, 10));
        }
    }

    #endregion
}
