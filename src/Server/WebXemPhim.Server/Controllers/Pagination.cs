namespace WebXemPhim.Server.Controllers;

/// <summary>
/// Centralised pagination bounds. Every list endpoint must normalise page/pageSize before
/// calculating <c>Skip((page - 1) * pageSize)</c>; otherwise a crafted page number can
/// overflow <see cref="int"/> and either throw or produce a negative SQL OFFSET.
/// </summary>
public static class Pagination
{
    public const int MaxPage = 1_000_000;
    public const int MaxPageSize = 1_000;

    public static (int Page, int PageSize, int Skip) Normalize(
        int page,
        int pageSize,
        int defaultPageSize = 20,
        int maxPageSize = MaxPageSize)
    {
        page = Math.Clamp(page <= 0 ? 1 : page, 1, MaxPage);
        var effectiveMax = Math.Clamp(maxPageSize, 1, MaxPageSize);
        pageSize = Math.Clamp(pageSize <= 0 ? defaultPageSize : pageSize, 1, effectiveMax);
        var skip = (long)(page - 1) * pageSize;
        return (page, pageSize, (int)skip);
    }
}
