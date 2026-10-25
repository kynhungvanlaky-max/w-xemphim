using System.Text;

namespace WebXemPhim.Server.Services;

/// <summary>
/// Đường dẫn ảnh nền của tập phim trong CSDL.
/// <para>
/// Tập không có ảnh riêng thì dùng ảnh do API sinh động
/// (<c>/api/episodes/{movieId}/{n}/thumbnail</c>); tập đã được quản trị viên tải ảnh lên
/// thì trỏ tới file thật (<c>/api/episodes/{episodeId}/media</c>).
/// </para>
/// </summary>
public static class EpisodeThumbnailUrls
{
    /// <summary>Ảnh nền do API sinh tự động theo phim + số tập.</summary>
    public static string Generated(int movieId, int episodeNumber) =>
        $"/api/episodes/{movieId}/{episodeNumber}/thumbnail?v=1";

    /// <summary>
    /// Ảnh nền tĩnh đã dựng sẵn cho từng tập trong <c>wwwroot/media/episodes</c> —
    /// mỗi tập một ảnh riêng (poster/backdrop của phim cắt riêng theo tập, có đánh số tập).
    /// </summary>
    public static string Static(int movieId, int episodeNumber) =>
        $"/media/episodes/ep-{movieId}-{episodeNumber:00}.webp";

    /// <summary>Ảnh nền do quản trị viên tải lên. <paramref name="version"/> để phá cache.</summary>
    public static string Custom(int episodeId, long version) =>
        $"/api/episodes/{episodeId}/media?v={version}";

    /// <summary>Đường dẫn lưu trong CSDL có trỏ tới ảnh tự tải lên hay không.</summary>
    public static bool IsCustom(string? thumbnailUrl) =>
        !string.IsNullOrWhiteSpace(thumbnailUrl)
        && thumbnailUrl.StartsWith("/api/episodes/", StringComparison.OrdinalIgnoreCase)
        && thumbnailUrl.Contains("/media", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lưu trữ ảnh nền do quản trị viên tải lên cho từng tập phim.
/// <para>
/// Ảnh nằm trên đĩa của API (mặc định <c>&lt;ContentRoot&gt;/data/episodes</c> — volume
/// <c>server_data</c> trong docker-compose) chứ không nhồi vào thư mục static của web, nhờ vậy
/// ảnh tồn tại qua các lần build lại container và không bị ghi đè bởi bản deploy mới.
/// </para>
/// </summary>
public sealed class EpisodeThumbnailStorage
{
    /// <summary>Kích thước tối đa cho một ảnh nền tập (5 MB).</summary>
    public const long MaxFileSizeBytes = 5L * 1024 * 1024;

    private static readonly (string Extension, string ContentType)[] AllowedTypes =
    [
        (".jpg", "image/jpeg"),
        (".jpeg", "image/jpeg"),
        (".png", "image/png"),
        (".webp", "image/webp"),
        (".gif", "image/gif"),
        (".avif", "image/avif"),
    ];

    public EpisodeThumbnailStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration["Storage:EpisodeThumbnailPath"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Path.Combine("data", "episodes");
        }

        var contentRoot = environment.ContentRootPath;
        if (string.IsNullOrWhiteSpace(contentRoot))
        {
            contentRoot = Directory.GetCurrentDirectory();
        }

        RootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRoot, configured);
    }

    /// <summary>Thư mục chứa ảnh nền tập.</summary>
    public string RootPath { get; }

    /// <summary>Các định dạng ảnh được phép tải lên.</summary>
    public static IReadOnlyList<string> AllowedExtensions =>
        AllowedTypes.Select(t => t.Extension).Distinct().ToList();

    /// <summary>Chuẩn hoá phần mở rộng từ tên file, trả về null nếu không được phép.</summary>
    public static string? ResolveExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension)) return null;

        extension = extension.ToLowerInvariant();
        return AllowedTypes.Any(t => t.Extension == extension) ? extension : null;
    }

    /// <summary>Content-Type ứng với phần mở rộng hợp lệ.</summary>
    public static string? ResolveContentType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var match = AllowedTypes.FirstOrDefault(t => t.Extension == extension.ToLowerInvariant());
        return match.Extension is null ? null : match.ContentType;
    }

    /// <summary>
    /// Kiểm tra chữ ký nhị phân của tệp để chắc chắn đây đích thực là ảnh, không phải tệp
    /// đổi tên (kiểu tấn công "upload shell disguised as image").
    /// </summary>
    public static bool HasValidSignature(byte[] header, string extension)
    {
        if (header is null || header.Length < 12) return false;

        switch (extension.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
                return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            case ".png":
                return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
            case ".gif":
                return header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F'
                    && header[3] == (byte)'8';
            case ".webp":
                return header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F'
                    && header[3] == (byte)'F' && header[8] == (byte)'W' && header[9] == (byte)'E'
                    && header[10] == (byte)'B' && header[11] == (byte)'P';
            case ".avif":
                return header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y'
                    && header[7] == (byte)'p'
                    && (MatchesAscii(header, 8, "avif") || MatchesAscii(header, 8, "avis"));
            default:
                return false;
        }
    }

    /// <summary>Ghi ảnh xuống đĩa, ghi đè ảnh cũ của tập nếu có.</summary>
    public async Task<string> SaveAsync(int episodeId, string extension, Stream source, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootPath);
        DeleteExistingFiles(episodeId);

        var path = BuildFilePath(episodeId, extension);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
        return path;
    }

    /// <summary>Tìm ảnh đã lưu của tập (nếu có).</summary>
    public bool TryResolve(int episodeId, out string filePath, out string contentType)
    {
        filePath = string.Empty;
        contentType = string.Empty;
        if (episodeId <= 0) return false;

        try
        {
            if (!Directory.Exists(RootPath)) return false;

            foreach (var path in Directory.EnumerateFiles(RootPath, $"ep-{episodeId}.*"))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var resolvedType = ResolveContentType(extension);
                if (resolvedType is null) continue;

                filePath = path;
                contentType = resolvedType;
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    /// <summary>Xoá ảnh đã lưu của tập.</summary>
    public bool Delete(int episodeId)
    {
        if (episodeId <= 0) return false;
        return DeleteExistingFiles(episodeId);
    }

    private string BuildFilePath(int episodeId, string extension) =>
        Path.Combine(RootPath, $"ep-{episodeId}{extension.ToLowerInvariant()}");

    private bool DeleteExistingFiles(int episodeId)
    {
        var removed = false;
        try
        {
            if (!Directory.Exists(RootPath)) return false;

            foreach (var path in Directory.EnumerateFiles(RootPath, $"ep-{episodeId}.*"))
            {
                File.Delete(path);
                removed = true;
            }
        }
        catch (IOException)
        {
            return removed;
        }
        catch (UnauthorizedAccessException)
        {
            return removed;
        }

        return removed;
    }

    private static bool MatchesAscii(byte[] buffer, int offset, string value)
    {
        if (buffer.Length < offset + value.Length) return false;
        for (var i = 0; i < value.Length; i++)
        {
            if (buffer[offset + i] != (byte)value[i]) return false;
        }

        return true;
    }

    /// <summary>Tạo data URI để xem trước ảnh vừa chọn ngay trong trình duyệt.</summary>
    public static string BuildPreviewDataUrl(string contentType, byte[] content)
    {
        var encoded = Convert.ToBase64String(content);
        return $"data:{contentType};base64,{encoded}";
    }

    /// <summary>Đọc phần đầu tệp để kiểm tra chữ ký nhị phân.</summary>
    public static async Task<byte[]> ReadHeaderAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[12];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await source.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (chunk <= 0) break;
            read += chunk;
        }

        if (read == buffer.Length) return buffer;

        var trimmed = new byte[read];
        Array.Copy(buffer, trimmed, read);
        return trimmed;
    }

    /// <summary>Chuẩn hoá tên tệp an toàn (chỉ dùng để gợi ý, không dùng làm đường dẫn).</summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "thumbnail";
        var name = Path.GetFileNameWithoutExtension(fileName);
        var builder = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_') builder.Append(ch);
            else if (builder.Length > 0) builder.Append('-');
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "thumbnail" : result;
    }
}
