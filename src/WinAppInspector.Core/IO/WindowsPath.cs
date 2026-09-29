namespace WinAppInspector.Core.IO;

/// <summary>
/// Windows path semantics implemented without <see cref="System.IO.Path"/>, so that rules and tests
/// behave identically on macOS / Linux where the host path separator differs.
/// </summary>
public static class WindowsPath
{
    public const char Separator = '\\';
    private const string ExtendedPrefix = @"\\?\";
    private const string UncExtendedPrefix = @"\\?\UNC\";

    /// <summary>
    /// Normalizes a Windows path: trims whitespace and quotes, converts forward slashes, collapses repeated separators,
    /// strips the <c>\\?\</c> prefix and removes trailing separators (a drive root keeps its single backslash).
    /// Returns an empty string for null / whitespace input. Does not touch the file system.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var p = path.Trim().Trim('"').Trim();
        p = p.Replace('/', Separator);

        if (p.StartsWith(UncExtendedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            p = @"\\" + p[UncExtendedPrefix.Length..];
        }
        else if (p.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
        {
            p = p[ExtendedPrefix.Length..];
        }

        var isUnc = p.StartsWith(@"\\", StringComparison.Ordinal);
        p = CollapseSeparators(p);
        if (isUnc)
        {
            p = Separator + p;
        }

        // Trim trailing separators, but keep "C:\" intact.
        while (p.Length > 1 && p[^1] == Separator && !IsDriveRoot(p))
        {
            p = p[..^1];
        }

        // "C:" -> "C:\"
        if (p.Length == 2 && IsDriveLetter(p[0]) && p[1] == ':')
        {
            p += Separator;
        }

        return p;
    }

    /// <summary>True for <c>X:\</c> (after normalization).</summary>
    public static bool IsDriveRoot(string path) =>
        path.Length == 3 && IsDriveLetter(path[0]) && path[1] == ':' && path[2] == Separator;

    /// <summary>True when <paramref name="path"/> equals <paramref name="root"/> or is located anywhere beneath it. Case-insensitive.</summary>
    public static bool IsSameOrUnder(string? path, string? root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (p.Length == 0 || r.Length == 0)
        {
            return false;
        }

        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = r[^1] == Separator ? r : r + Separator;
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="path"/> is strictly beneath <paramref name="root"/>.</summary>
    public static bool IsUnder(string? path, string? root) =>
        IsSameOrUnder(path, root) && !AreEqual(path, root);

    public static bool AreEqual(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Joins segments with a single backslash.</summary>
    public static string Combine(string first, params string[] rest)
    {
        var result = Normalize(first);
        foreach (var segment in rest)
        {
            var s = Normalize(segment).TrimStart(Separator);
            if (s.Length == 0)
            {
                continue;
            }

            result = result.Length == 0 || result[^1] == Separator ? result + s : result + Separator + s;
        }

        return result;
    }

    /// <summary>Last path segment, or the whole string when there is no separator.</summary>
    public static string GetFileName(string? path)
    {
        var p = Normalize(path);
        if (p.Length == 0 || IsDriveRoot(p))
        {
            return string.Empty;
        }

        var idx = p.LastIndexOf(Separator);
        return idx < 0 ? p : p[(idx + 1)..];
    }

    /// <summary>Parent directory, or <c>null</c> for a drive root / single segment.</summary>
    public static string? GetDirectoryName(string? path)
    {
        var p = Normalize(path);
        if (p.Length == 0 || IsDriveRoot(p))
        {
            return null;
        }

        var idx = p.LastIndexOf(Separator);
        if (idx < 0)
        {
            return null;
        }

        var parent = p[..idx];
        return parent.Length == 2 && parent[1] == ':' ? parent + Separator : parent;
    }

    /// <summary>
    /// Extracts the executable path from a command line such as
    /// <c>"C:\Program Files\App\unins000.exe" /SILENT</c> or <c>C:\App\app.exe --flag</c>.
    /// Returns <c>null</c> when nothing path-like is found.
    /// </summary>
    public static string? ExtractExecutable(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var s = commandLine.Trim();
        if (s[0] == '"')
        {
            var close = s.IndexOf('"', 1);
            return close > 1 ? Normalize(s[1..close]) : null;
        }

        // Unquoted: the executable ends at ".exe" (case-insensitive) if present, otherwise at the first space.
        var exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx >= 0)
        {
            return Normalize(s[..(exeIdx + 4)]);
        }

        var space = s.IndexOf(' ', StringComparison.Ordinal);
        return Normalize(space < 0 ? s : s[..space]);
    }

    private static bool IsDriveLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static string CollapseSeparators(string p)
    {
        var chars = new char[p.Length];
        var n = 0;
        var prevSep = false;
        foreach (var c in p)
        {
            var isSep = c == Separator;
            if (isSep && prevSep)
            {
                continue;
            }

            chars[n++] = c;
            prevSep = isSep;
        }

        return new string(chars, 0, n);
    }
}
