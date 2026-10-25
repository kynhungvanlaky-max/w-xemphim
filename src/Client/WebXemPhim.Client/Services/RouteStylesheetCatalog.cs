namespace WebXemPhim.Client.Services;

/// <summary>
/// Single source of truth for the route specific stylesheets.
/// <para>
/// A page declares the sheets it needs through <c>RouteStylesheet</c>. The same mapping is
/// published to the browser so the sheet of a route can be preloaded the moment the visitor
/// shows intent to open it. Without that preload the browser paints the next page before the
/// route sheet is applied, which shows up as a completely unstyled page on the first
/// navigation.
/// </para>
/// </summary>
public static class RouteStylesheetCatalog
{
    public const string HomeExpanded = "css/home-expanded.css";
    public const string DiscoveryFilters = "css/discovery-filters.css";
    public const string GenresPage = "css/genres-page.css";
    public const string AccountOverview = "css/account-overview.css";
    public const string ProfileSettings = "css/profile-settings.css";
    public const string LibraryMembership = "css/library-membership.css";
    public const string WatchWorkspace = "css/watch-workspace.css";

    public static readonly string[] Home = [HomeExpanded];
    public static readonly string[] Explore = [HomeExpanded, DiscoveryFilters];
    public static readonly string[] Genres = [GenresPage];
    public static readonly string[] Account = [AccountOverview];
    public static readonly string[] Profile = [ProfileSettings];
    public static readonly string[] Watchlist = [LibraryMembership];
    public static readonly string[] Watch = [WatchWorkspace];

    /// <summary>
    /// Route prefix (lowercase, without query string) to the stylesheets that route needs.
    /// The order matters: the longest prefix wins.
    /// </summary>
    private static readonly (string Prefix, string[] Files)[] s_routes =
    [
        ("/", Home),
        ("/explore", Explore),
        ("/genres", Genres),
        ("/account", Account),
        ("/profile", Profile),
        ("/watchlist", Watchlist),
        ("/watch", Watch)
    ];

    /// <summary>Returns the stylesheets of the route that serves the given path.</summary>
    public static string[] ForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];

        var normalized = path.Split('?')[0].TrimEnd('/').ToLowerInvariant();
        if (normalized.Length == 0) normalized = "/";

        foreach (var (prefix, files) in s_routes)
        {
            if (prefix == "/")
            {
                if (normalized == "/") return files;
                continue;
            }

            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) return files;
        }

        return [];
    }

    /// <summary>
    /// The route to stylesheet mapping as JSON, consumed by the preload script in App.razor.
    /// The resolver turns a wwwroot path into the fingerprinted asset URL, so the preloaded
    /// file is the very same cache entry the stylesheet link later points at.
    /// </summary>
    public static string ToJson(Func<string, string> resolve)
    {
        var entries = s_routes
            .GroupBy(route => route.Prefix)
            .Select(group => new
            {
                route = group.Key,
                files = group.SelectMany(route => route.Files).Distinct().Select(resolve).ToArray()
            });

        return System.Text.Json.JsonSerializer.Serialize(entries);
    }
}
