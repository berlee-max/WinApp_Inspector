using WinAppInspector.Core.IO;

namespace WinAppInspector.Core.Rules;

/// <summary>Why a path is protected from deletion.</summary>
public enum PathProtectionKind
{
    None = 0,
    /// <summary>The path is a drive root.</summary>
    DriveRoot = 1,
    /// <summary>The path is inside the Windows directory (System32, SysWOW64, WinSxS, Installer, ...). §11.</summary>
    WindowsDirectory = 2,
    /// <summary>The path is inside Program Files\WindowsApps; Store apps must go through the AppX uninstall flow. §11.</summary>
    WindowsApps = 3,
    /// <summary>The path is a scan root itself (Program Files, ProgramData, AppData\Local, ...), which may contain many applications.</summary>
    ScanRoot = 4,
    /// <summary>The path is the Users folder or the current user's profile root.</summary>
    UserProfileRoot = 5,
    /// <summary>The path is empty or not an absolute Windows path.</summary>
    NotAbsolute = 6,
}

/// <summary>Result of <see cref="ProtectedPathRule.Check"/>.</summary>
public sealed record PathProtection(bool IsProtected, PathProtectionKind Kind, string? MatchedRoot)
{
    public static PathProtection Allowed { get; } = new(false, PathProtectionKind.None, null);
}

/// <summary>
/// Requirements §11: directories that must never be deleted directly.
/// Subtree rules protect a folder and everything under it; root-only rules protect the folder itself but allow deleting its children.
/// Pure logic, no file-system access.
/// </summary>
public sealed class ProtectedPathRule
{
    private readonly WindowsKnownFolders _folders;
    private readonly IReadOnlyList<(string Path, PathProtectionKind Kind)> _subtrees;
    private readonly IReadOnlyList<(string Path, PathProtectionKind Kind)> _rootsOnly;

    public ProtectedPathRule(WindowsKnownFolders folders)
    {
        _folders = folders ?? throw new ArgumentNullException(nameof(folders));

        _subtrees =
        [
            (folders.SystemRoot, PathProtectionKind.WindowsDirectory),
            (folders.WindowsApps, PathProtectionKind.WindowsApps),
        ];

        _rootsOnly =
        [
            (folders.ProgramFiles, PathProtectionKind.ScanRoot),
            (folders.ProgramFilesX86, PathProtectionKind.ScanRoot),
            (folders.ProgramData, PathProtectionKind.ScanRoot),
            (folders.LocalAppData, PathProtectionKind.ScanRoot),
            (folders.RoamingAppData, PathProtectionKind.ScanRoot),
            (folders.LocalLowAppData, PathProtectionKind.ScanRoot),
            (WindowsPath.Combine(folders.UserProfile, "AppData"), PathProtectionKind.ScanRoot),
            (folders.UsersRoot, PathProtectionKind.UserProfileRoot),
            (folders.UserProfile, PathProtectionKind.UserProfileRoot),
        ];
    }

    /// <summary>A rule for the conventional drive-C layout.</summary>
    public static ProtectedPathRule Default { get; } = new(WindowsKnownFolders.CreateDefault());

    /// <summary>The §11 list, for display in settings / about.</summary>
    public IReadOnlyList<string> ProtectedSubtrees =>
    [
        _folders.SystemRoot,
        _folders.System32,
        _folders.SysWow64,
        _folders.WinSxS,
        _folders.WindowsInstaller,
        _folders.WindowsApps,
    ];

    public bool IsProtected(string? path) => Check(path).IsProtected;

    public PathProtection Check(string? path)
    {
        var p = WindowsPath.Normalize(path);
        if (!IsAbsoluteWindowsPath(p))
        {
            return new PathProtection(true, PathProtectionKind.NotAbsolute, null);
        }

        if (WindowsPath.IsDriveRoot(p))
        {
            return new PathProtection(true, PathProtectionKind.DriveRoot, p);
        }

        foreach (var (root, kind) in _subtrees)
        {
            if (WindowsPath.IsSameOrUnder(p, root))
            {
                return new PathProtection(true, kind, root);
            }
        }

        foreach (var (root, kind) in _rootsOnly)
        {
            if (WindowsPath.AreEqual(p, root))
            {
                return new PathProtection(true, kind, root);
            }
        }

        // Every account folder under Users (other users, Public, Default) is a profile root, not just the current one.
        if (WindowsPath.AreEqual(WindowsPath.GetDirectoryName(p), _folders.UsersRoot))
        {
            return new PathProtection(true, PathProtectionKind.UserProfileRoot, p);
        }

        return PathProtection.Allowed;
    }

    private static bool IsAbsoluteWindowsPath(string normalized)
    {
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == WindowsPath.Separator)
        {
            return true;
        }

        // UNC paths are "absolute" but we never delete over the network; treat them as not eligible.
        return false;
    }
}
