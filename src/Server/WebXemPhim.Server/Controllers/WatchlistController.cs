using Microsoft.AspNetCore.Mvc;
using WebXemPhim.Server.Services;
using WebXemPhim.Shared.Models;
using WebXemPhim.Shared.Responses;

namespace WebXemPhim.Server.Controllers;

[ApiController]
[NonController] // Historical in-memory implementation; HTTP routes now use the user-scoped LibraryController.
[Route("api/[controller]")]
public class WatchlistController(IWatchlistService watchlistService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<WatchlistItemDto>>>> GetWatchlist()
    {
        var items = await watchlistService.GetWatchlistAsync();
        return Ok(ApiResponse<List<WatchlistItemDto>>.Ok(items));
    }

    [HttpPost("{movieId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> AddToWatchlist(int movieId)
    {
        var result = await watchlistService.AddToWatchlistAsync(movieId);
        if (!result)
            return BadRequest(ApiResponse<bool>.Fail("Không tìm thấy phim để thêm vào tủ phim."));

        return Ok(ApiResponse<bool>.Ok(true, "Đã thêm phim vào tủ phim thành công."));
    }

    [HttpDelete("{movieId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveFromWatchlist(int movieId)
    {
        var result = await watchlistService.RemoveFromWatchlistAsync(movieId);
        return Ok(ApiResponse<bool>.Ok(result, result ? "Đã xóa phim khỏi tủ phim." : "Phim không tồn tại trong tủ phim."));
    }

    [HttpGet("check/{movieId:int}")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckInWatchlist(int movieId)
    {
        var exists = await watchlistService.IsInWatchlistAsync(movieId);
        return Ok(ApiResponse<bool>.Ok(exists));
    }
}
