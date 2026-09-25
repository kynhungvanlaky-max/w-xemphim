using Microsoft.AspNetCore.Mvc;
using Moq;
using WebXemPhim.Server.Controllers;
using WebXemPhim.Server.Data;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Tests.ServerTests;

public class GenreServiceTests
{
    private readonly GenreService _service = new();

    [Fact]
    public async Task GetAllGenresAsync_ReturnsEveryConfiguredGenreInDatabaseOrder()
    {
        var result = await _service.GetAllGenresAsync();

        Assert.Same(MockMovieDatabase.Genres, result);
        Assert.NotEmpty(result);
        Assert.Equal(result.Count, result.Select(genre => genre.Id).Distinct().Count());
        Assert.Equal(result.Count, result.Select(genre => genre.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result, genre =>
        {
            Assert.True(genre.Id > 0);
            Assert.NotEmpty(genre.Name);
            Assert.NotEmpty(genre.Slug);
            Assert.True(genre.MovieCount >= 0);
        });
    }
}

public class GenresControllerTests
{
    [Fact]
    public async Task GetAll_ReturnsServiceDataInSuccessEnvelope()
    {
        var genres = new List<GenreDto> { new() { Id = 1, Name = "Action" } };
        var service = new Mock<IGenreService>(MockBehavior.Strict);
        service.Setup(instance => instance.GetAllGenresAsync()).ReturnsAsync(genres);
        var controller = new GenresController(service.Object);

        var actionResult = await controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApiResponse<List<GenreDto>>>(ok.Value);
        Assert.True(response.Success);
        Assert.Same(genres, response.Data);
        service.VerifyAll();
    }
}
