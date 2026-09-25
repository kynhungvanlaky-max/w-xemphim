using System.ComponentModel.DataAnnotations;
using WebXemPhim.Shared.Enums;

namespace WebXemPhim.Shared.Requests;

public class MovieFilterRequest : IValidatableObject
{
    [MaxLength(30)] public List<string> Genres { get; set; } = [];
    [MaxLength(30)] public List<string> Countries { get; set; } = [];
    [MaxLength(100)] public List<int> Years { get; set; } = [];
    [MaxLength(3)] public List<MovieType> Types { get; set; } = [];
    [StringLength(200)] public string? Keyword { get; set; }
    [StringLength(80)] public string? Genre { get; set; }
    [StringLength(80)] public string? Country { get; set; }
    public int? Year { get; set; }
    [EnumDataType(typeof(MovieType))]
    public MovieType? Type { get; set; }
    [StringLength(32)] public string SortBy { get; set; } = "trending"; // trending, rating, latest, views
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 18;

    // Values within a group are OR-ed; the four groups are AND-ed.
    // Legacy single-value links remain compatible with the new filters.
    public string[] ResolveGenres() => Normalize(Genres, Genre);
    public string[] ResolveCountries() => Normalize(Countries, Country);
    public int[] ResolveYears() => Years.Concat(Year.HasValue ? [Year.Value] : []).Where(x => x > 0).Distinct().ToArray();
    public MovieType[] ResolveTypes() => Types.Concat(Type.HasValue ? [Type.Value] : []).Distinct().ToArray();

    private static string[] Normalize(IEnumerable<string> values, string? legacy) => values.Append(legacy ?? "")
        .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.Trim().ToLowerInvariant()).Distinct().ToArray();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Genres.Concat(Countries).Any(x => x is null || x.Length > 80))
            yield return new ValidationResult("Mỗi lựa chọn tối đa 80 ký tự.", [nameof(Genres), nameof(Countries)]);
        if (Types.Any(x => !Enum.IsDefined(x)))
            yield return new ValidationResult("Định dạng phim không hợp lệ.", [nameof(Types)]);
        if (Years.Any(x => x < 1888 || x > 2100))
            yield return new ValidationResult("Năm phát hành không hợp lệ.", [nameof(Years)]);
    }
}
