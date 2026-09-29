using System.Text.RegularExpressions;
using WinAppInspector.Core.IO;

namespace WinAppInspector.Core.Parsing;

/// <summary>
/// Extracts executable paths from the command strings found in services (§7.6), startup items (§7.7),
/// scheduled tasks (§7.8) and uninstall strings (§7.1). Pure string logic; environment expansion is injected.
/// </summary>
public static partial class CommandLine
{
    [GeneratedRegex(@"%([^%\s]+)%")]
    private static partial Regex EnvironmentVariablePattern();

    [GeneratedRegex(@"^(?<path>.*?\.(?:exe|sys|dll|com|bat|cmd|msi|scr|ocx|ps1|vbs|js))(?=\s|$|,|"")", RegexOptions.IgnoreCase)]
    private static partial Regex UnquotedExecutablePattern();

    /// <summary>Replaces <c>%NAME%</c> tokens using <paramref name="environment"/> (case-insensitive). Unknown tokens are left as-is.</summary>
    public static string ExpandEnvironmentVariables(string? value, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var lookup = environment as Dictionary<string, string> ?? new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
        return EnvironmentVariablePattern().Replace(value, m =>
            lookup.TryGetValue(m.Groups[1].Value, out var replacement) ? replacement : m.Value);
    }

    /// <summary>
    /// Returns the image path from a command line such as <c>"C:\App\app.exe" --flag</c>,
    /// <c>C:\Program Files\App\app.exe /uninstall</c> or <c>rundll32.exe "C:\x\y.dll",Entry</c>.
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
            return close > 1 ? WindowsPath.Normalize(s[1..close]) : null;
        }

        var match = UnquotedExecutablePattern().Match(s);
        if (match.Success)
        {
            return WindowsPath.Normalize(match.Groups["path"].Value);
        }

        var space = s.IndexOf(' ', StringComparison.Ordinal);
        return WindowsPath.Normalize(space < 0 ? s : s[..space]);
    }

    /// <summary>The text following the executable, or <c>null</c>.</summary>
    public static string? ExtractArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var s = commandLine.Trim();
        string rest;
        if (s[0] == '"')
        {
            var close = s.IndexOf('"', 1);
            rest = close > 1 ? s[(close + 1)..] : string.Empty;
        }
        else
        {
            var match = UnquotedExecutablePattern().Match(s);
            var end = match.Success ? match.Length : (s.IndexOf(' ', StringComparison.Ordinal) is var sp && sp >= 0 ? sp : s.Length);
            rest = s[end..];
        }

        rest = rest.Trim();
        return rest.Length == 0 ? null : rest;
    }

    /// <summary>
    /// Resolves a service <c>PathName</c> (§7.6) to an absolute image path. Handles quoted paths,
    /// NT object paths (<c>\??\C:\...</c>), <c>\SystemRoot\...</c>, System32-relative driver paths and <c>%SystemRoot%</c>.
    /// Returns <c>null</c> when nothing path-like is present.
    /// </summary>
    public static string? ResolveServiceImagePath(string? pathName, WindowsKnownFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (string.IsNullOrWhiteSpace(pathName))
        {
            return null;
        }

        var expanded = ExpandEnvironmentVariables(pathName.Trim(), folders.ToEnvironment());
        var exe = ExtractExecutable(expanded);
        if (exe is null)
        {
            return null;
        }

        return MakeAbsolute(exe, folders);
    }

    /// <summary>Same as <see cref="ResolveServiceImagePath"/> but for startup commands and task actions.</summary>
    public static string? ResolveCommandExecutable(string? command, WindowsKnownFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var exe = ExtractExecutable(ExpandEnvironmentVariables(command.Trim(), folders.ToEnvironment()));
        return exe is null ? null : MakeAbsolute(exe, folders);
    }

    private static string MakeAbsolute(string path, WindowsKnownFolders folders)
    {
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            path = path[4..];
        }

        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsPath.Combine(folders.SystemRoot, path[@"\SystemRoot\".Length..]);
        }

        if (path.StartsWith(@"SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsPath.Combine(folders.SystemRoot, path[@"SystemRoot\".Length..]);
        }

        if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsPath.Combine(folders.SystemRoot, path);
        }

        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return path;
        }

        // Bare image name such as "svchost.exe" or "cmd.exe": services and Run entries resolve these from System32.
        if (!path.Contains(WindowsPath.Separator, StringComparison.Ordinal))
        {
            return WindowsPath.Combine(folders.System32, path);
        }

        return path;
    }
}
