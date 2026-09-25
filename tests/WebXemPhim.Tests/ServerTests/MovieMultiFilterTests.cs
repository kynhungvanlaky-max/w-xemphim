using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Requests;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public sealed class MovieMultiFilterTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task SqliteMatchesVietnameseCaseInsensitivelyAcrossMultipleGroups()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await DevelopmentDataSeeder.SeedCatalogAsync(seed);
        }
        var contexts = new Mock<IDbContextFactory<AppDbContext>>();
        contexts.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(options));
        var service = new DatabaseMovieService(contexts.Object);
        var all = await service.FilterMoviesAsync(new MovieFilterRequest { PageSize = 100 });
        var request = new MovieFilterRequest
        {
            Genres = ["hành động", "tâm lý"], Countries = ["âu mỹ", "việt nam"], PageSize = 100
        };
        var result = await service.FilterMoviesAsync(request);
        var expected = all.Items.Where(x => x.Genres.Any(g => request.ResolveGenres().Contains(g.ToLowerInvariant()))
            && request.ResolveCountries().Contains(x.Country.ToLowerInvariant())).Select(x => x.Id).Order();
        Assert.Contains(result.Items, x => x.Country == "Âu Mỹ");
        Assert.Contains(result.Items, x => x.Country == "Việt Nam");
        Assert.Equal(expected, result.Items.Select(x => x.Id).Order());
    }

    [Fact]
    public async Task MultipleGenresReturnUnionWithoutDuplicateMovies()
    {
        var action = await Read("genres=" + Uri.EscapeDataString("Hành động"));
        var drama = await Read("genres=" + Uri.EscapeDataString("Tâm lý"));
        var combined = await Read("genres=" + Uri.EscapeDataString("Hành động") + "&genres=" + Uri.EscapeDataString("Tâm lý"));
        var expected = action.Items.Concat(drama.Items).Select(x => x.Id).Distinct().Order().ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, combined.Items.Select(x => x.Id).Order());
        Assert.Equal(combined.TotalItems, combined.Items.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task GroupsIntersectAndPaginationKeepsFilteredTotal()
    {
        var all = await Read("");
        var selected = all.Items.Take(2).ToArray();
        var countries = selected.Select(x => x.Country).Distinct().ToArray();
        var years = selected.Select(x => x.ReleaseYear).Distinct().ToArray();
        var types = selected.Select(x => x.Type).Distinct().ToArray();
        var query = string.Join("&", countries.Select(x => "countries=" + Uri.EscapeDataString(x))
            .Concat(years.Select(x => "years=" + x)).Concat(types.Select(x => "types=" + (int)x)));
        var expected = all.Items.Where(x => countries.Contains(x.Country) && years.Contains(x.ReleaseYear) && types.Contains(x.Type)).ToList();
        var result = await Read(query);
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Select(x => x.Id).Order(), result.Items.Select(x => x.Id).Order());
        var page = await client.GetFromJsonAsync<ApiResponse<PagedResult<MovieDto>>>("/api/movies?" + query + "&page=2&pageSize=1");
        Assert.Equal(expected.Count, page!.Data!.TotalItems);
        Assert.Single(page.Data.Items);
        Assert.Equal(result.Items[1].Id, page.Data.Items[0].Id);
    }

    [Theory]
    [InlineData("types=99")]
    [InlineData("years=1800")]
    public async Task InvalidMultiSelectionIsRejected(string query) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/movies?" + query)).StatusCode);

    [Fact]
    public async Task FilterOptionsReflectTheCatalog()
    {
        var all = await Read("");
        var response = await client.GetFromJsonAsync<ApiResponse<MovieFilterOptionsDto>>("/api/movies/filter-options");
        Assert.NotNull(response?.Data);
        Assert.Equal(all.Items.Select(x => x.Country).Distinct().Order(), response.Data.Countries.Order());
        Assert.Equal(all.Items.Select(x => x.ReleaseYear).Distinct().OrderDescending(), response.Data.Years);
        Assert.Equal(all.Items.SelectMany(x => x.Genres).Distinct().Order(), response.Data.Genres.Order());
    }

    [Fact]
    public async Task LegacyAndMultiValueGenreLinksRemainCompatible()
    {
        var query = "genre=" + Uri.EscapeDataString("Hành động") + "&genres=" + Uri.EscapeDataString("Tâm lý");
        var api = await Read(query);
        var memory = await new MovieService().FilterMoviesAsync(new MovieFilterRequest
        {
            Genre = "Hành động", Genres = ["Tâm lý", "Hành động"], PageSize = 100
        });
        Assert.Equal(memory.Items.Select(x => x.Id).Order(), api.Items.Select(x => x.Id).Order());
    }

    private async Task<PagedResult<MovieDto>> Read(string query)
    {
        var response = await client.GetFromJsonAsync<ApiResponse<PagedResult<MovieDto>>>("/api/movies?pageSize=100&" + query);
        Assert.NotNull(response?.Data);
        return response.Data;
    }
}
