using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinAppInspector.Analysis.Matching;

/// <summary>
/// Normalises application, product and folder names so "Google Chrome", "GoogleChrome" and "chrome_x64 v128" compare well.
/// Never used as the sole basis for a decision (§5.3); it produces the <c>FolderNameSimilar</c> evidence (§10.2 low weight).
/// </summary>
public static partial class NameNormalizer
{
    /// <summary>Threshold above which two names are considered "similar".</summary>
    public const double SimilarityThreshold = 0.6;

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "x64", "x86", "amd64", "arm64", "win64", "win32", "64bit", "32bit", "64", "32", "bit",
        "setup", "install", "installer", "portable", "edition", "version", "release", "stable", "beta", "preview",
        "the", "app", "application", "program", "software", "for", "and", "of", "a", "an",
    };

    [GeneratedRegex(@"^v?\d+([._]\d+)*[a-z]?$")]
    private static partial Regex VersionToken();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonAlphanumeric();

    /// <summary>Lower-cased, punctuation-free, version- and noise-free token string ("google chrome").</summary>
    public static string Normalize(string? name) => string.Join(' ', Tokens(name));

    /// <summary>Normalized tokens, in order, without duplicates.</summary>
    public static IReadOnlyList<string> Tokens(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var lowered = RemoveDiacritics(name).ToLowerInvariant();
        // Split camel case boundaries ("GoogleChrome" -> "google chrome") before stripping punctuation.
        lowered = SplitCamelCase(name).ToLowerInvariant();

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in NonAlphanumeric().Split(lowered))
        {
            if (raw.Length == 0 || NoiseTokens.Contains(raw))
            {
                continue;
            }

            // "23.01", "v2", "2022" and trailing build numbers are versions; a leading short number ("7-Zip") is part of the name.
            var looksLikeVersion = VersionToken().IsMatch(raw) && (tokens.Count > 0 || raw.Length >= 4 || raw.Contains('.') || raw.Contains('_') || raw[0] == 'v');
            if (looksLikeVersion)
            {
                continue;
            }

            if (seen.Add(raw))
            {
                tokens.Add(raw);
            }
        }

        return tokens;
    }

    /// <summary>Tokens joined without spaces ("googlechrome"), for comparing folder names that omit spaces.</summary>
    public static string Compact(string? name) => string.Concat(Tokens(name));

    /// <summary>A similarity score in [0, 1]. 1 = same normalized name.</summary>
    public static double Similarity(string? a, string? b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        var ca = string.Concat(ta);
        var cb = string.Concat(tb);
        if (ca == cb)
        {
            return 1;
        }

        var setA = new HashSet<string>(ta, StringComparer.Ordinal);
        var setB = new HashSet<string>(tb, StringComparer.Ordinal);
        var intersection = setA.Intersect(setB, StringComparer.Ordinal).Count();
        var union = setA.Union(setB, StringComparer.Ordinal).Count();
        var jaccard = union == 0 ? 0 : (double)intersection / union;
        var containment = (double)intersection / Math.Min(setA.Count, setB.Count);

        // One compact name containing the other ("vlc" in "vlcmediaplayer") is a strong hint when the shorter side is meaningful.
        var shorter = ca.Length <= cb.Length ? ca : cb;
        var longer = ca.Length <= cb.Length ? cb : ca;
        var compactContainment = shorter.Length >= 3 && longer.Contains(shorter, StringComparison.Ordinal) ? 0.85 : 0;

        return Math.Max(jaccard, Math.Max(containment * 0.85, compactContainment));
    }

    public static bool IsSimilar(string? a, string? b) => Similarity(a, b) >= SimilarityThreshold;

    private static string SplitCamelCase(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(value[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(c);
        }

        return RemoveDiacritics(sb.ToString());
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
