using System.Globalization;
using System.Text;

namespace WebXemPhim.Shared.Utils;

public static class VietnameseTextNormalizer
{
    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Step 1: Replace Đ and đ explicitly as they do not decompose into D + NonSpacingMark
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D');

        // Step 2: Decompose with FormD
        var formD = normalized.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);

        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        // Step 3: Recompose with FormC
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var stripped = RemoveDiacritics(text).ToLowerInvariant().Trim();
        var sb = new StringBuilder(stripped.Length);
        var inSpace = false;

        foreach (var ch in stripped)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inSpace)
                {
                    sb.Append(' ');
                    inSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                inSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
