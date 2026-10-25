using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using WebXemPhim.Server.Services;

namespace WebXemPhim.Server.Controllers;

/// <summary>
/// Sinh ảnh nền riêng cho từng tập phim.
/// <para>
/// Trước đây mọi tập của một phim dùng chung một ảnh bìa nên danh sách tập nhìn như bị
/// lặp. Ở đây mỗi tập nhận một màu chuyển sắc riêng (tính theo phim + số tập) nên hai tập
/// liền nhau luôn khác nhau, và không phải tạo hàng nghìn file ảnh trong repo.
/// </para>
/// <para>
/// Nếu quản trị viên đã tải ảnh riêng cho tập thì ảnh đó được phục vụ tại
/// <c>/api/episodes/{episodeId}/media</c> và ghi đè ảnh sinh sẵn.
/// </para>
/// </summary>
[ApiController]
[Route("api/episodes")]
public sealed class EpisodeThumbnailController : ControllerBase
{
    private readonly EpisodeThumbnailStorage? _storage;

    public EpisodeThumbnailController(EpisodeThumbnailStorage? storage = null)
    {
        _storage = storage;
    }

    /// <summary>
    /// Trả về ảnh nền 16:9 dạng SVG cho một tập. Ảnh được sinh theo thứ tự tập nên
    /// tập này và tập kế tiếp không bao giờ giống nhau.
    /// </summary>
    [HttpGet("{movieId:int}/{episodeNumber:int}/thumbnail")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult GetThumbnail(int movieId, int episodeNumber)
    {
        if (movieId <= 0 || episodeNumber <= 0) return NotFound();

        // Sắc màu neo theo phim, dịch chuyển theo số tập: mỗi tập một màu riêng.
        var baseHue = (movieId * 47) % 360;
        var hue = (baseHue + episodeNumber * 23) % 360;
        var hue2 = (hue + 34) % 360;

        var title = string.Create(CultureInfo.InvariantCulture, $"Tập {episodeNumber:00}");
        var svg = BuildSvg(hue, hue2, title, movieId, episodeNumber);
        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }

    /// <summary>
    /// Trả về ảnh nền do quản trị viên tải lên cho tập. Nếu tập chưa có ảnh riêng thì
    /// trả 404 để trang xem phim dùng ảnh sinh sẵn.
    /// </summary>
    [HttpGet("{episodeId:int}/media")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetCustomThumbnail(int episodeId)
    {
        if (episodeId <= 0) return NotFound();
        if (_storage is null || !_storage.TryResolve(episodeId, out var filePath, out var contentType)) return NotFound();

        return PhysicalFile(filePath, contentType, enableRangeProcessing: false);
    }

    private static string BuildSvg(int hue, int hue2, string title, int movieId, int episodeNumber)
    {
        var seed = movieId * 31 + episodeNumber * 17;
        var tilt = (seed % 11) - 5;

        var sb = new StringBuilder(2048);
        sb.Append("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 360" width="640" height="360" role="img" """);
        sb.Append("aria-label=\"");
        sb.Append(System.Security.SecurityElement.Escape(title));
        sb.Append("\">");

        sb.Append($"""<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">""");
        sb.Append($"""<stop offset="0%" stop-color="hsl({hue} 46% 16%)"/>""");
        sb.Append($"""<stop offset="52%" stop-color="hsl({hue2} 40% 11%)"/>""");
        sb.Append($"""<stop offset="100%" stop-color="hsl({hue} 52% 7%)"/>""");
        sb.Append("</linearGradient>");
        sb.Append($"""<radialGradient id="v" cx="50%" cy="42%" r="72%"><stop offset="55%" stop-color="rgba(0,0,0,0)"/><stop offset="100%" stop-color="rgba(0,0,0,0.62)"/></radialGradient></defs>""");

        sb.Append("""<rect width="640" height="360" fill="url(#g)"/>""");

        // Các vệt sáng xiên tạo chiều sâu, mỗi tập một góc khác nhau.
        sb.Append($"""<g transform="rotate({tilt} 320 180)" opacity="0.16">""");
        for (var i = 0; i < 5; i++)
        {
            var x = 40 + i * 130 + (seed % 37);
            sb.Append($"""<rect x="{x}" y="-120" width="46" height="620" fill="hsl({(hue + i * 24) % 360} 70% 62%)"/>""");
        }
        sb.Append("</g>");

        sb.Append("""<rect width="640" height="360" fill="url(#v)"/>""");

        // Số tập lớn ở góc phải.
        sb.Append($"""<text x="600" y="300" text-anchor="end" font-family="'Be Vietnam Pro',system-ui,sans-serif" font-size="104" font-weight="800" fill="rgba(255,255,255,0.13)" letter-spacing="-2">{episodeNumber:00}</text>""");
        sb.Append($"""<text x="34" y="318" font-family="'Be Vietnam Pro',system-ui,sans-serif" font-size="26" font-weight="700" fill="rgba(255,255,255,0.82)">{title}</text>""");
        sb.Append($"""<text x="34" y="342" font-family="'Be Vietnam Pro',system-ui,sans-serif" font-size="13" font-weight="600" fill="rgba(255,255,255,0.42)" letter-spacing="2.6">CINEMAFLOW</text>""");

        sb.Append("</svg>");
        return sb.ToString();
    }
}
