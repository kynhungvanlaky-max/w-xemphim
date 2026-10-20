using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HomeController(IMovieService movieService) : ControllerBase
{
    [HttpGet]
    [OutputCache(Duration = 60, Tags = ["movies"])]
    public async Task<ActionResult<ApiResponse<HomeAggregateDto>>> GetHome()
    {
        var aggregate = await movieService.GetHomeAggregateAsync();
        return Ok(ApiResponse<HomeAggregateDto>.Ok(aggregate));
    }
}
