using System;
using System.Collections.Generic;
using System.Linq;

namespace WebXemPhim.Shared.Utils;

public static class MovieSearchTextBuilder
{
    public static string Build(string? title, string? originalTitle, string? director, IEnumerable<string>? castNames, string? overview)
    {
        var parts = new[]
        {
            title ?? string.Empty,
            originalTitle ?? string.Empty,
            director ?? string.Empty,
            string.Join(" ", castNames ?? Array.Empty<string>()),
            overview ?? string.Empty
        };

        return VietnameseTextNormalizer.NormalizeForSearch(string.Join(" ", parts));
    }
}
