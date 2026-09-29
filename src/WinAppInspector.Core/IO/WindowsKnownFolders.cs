using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.IO;

/// <summary>
/// The well-known folders the scanners and safety rules depend on (§6, §11).
/// Constructed explicitly in tests; from the environment at runtime on Windows.
/// </summary>
public sealed record WindowsKnownFolders
{
    public required string SystemDrive { get; init; }
    public required string SystemRoot { get; init; }
    public required string ProgramFiles { get; init; }
    public required string ProgramFilesX86 { get; init; }
    public required string ProgramData { get; init; }
    public required string UsersRoot { get; init; }
    public required string UserProfile { get; init; }
    public required string LocalAppData { get; init; }
    public required string RoamingAppData { get; init; }
    public required string LocalLowAppData { get; init; }

    public string WindowsApps => WindowsPath.Combine(ProgramFiles, "WindowsApps");
    public string System32 => WindowsPath.Combine(SystemRoot, "System32");
    public string SysWow64 => WindowsPath.Combine(SystemRoot, "SysWOW64");
    public string WinSxS => WindowsPath.Combine(SystemRoot, "WinSxS");
    public string WindowsInstaller => WindowsPath.Combine(SystemRoot, "Installer");

    /// <summary>A conventional layout on drive C: for a user named <paramref name="userName"/>. Used by tests and as a fallback.</summary>
    public static WindowsKnownFolders CreateDefault(string userName = "User")
    {
        var profile = @"C:\Users\" + userName;
        return new WindowsKnownFolders
        {
            SystemDrive = @"C:\",
            SystemRoot = @"C:\Windows",
            ProgramFiles = @"C:\Program Files",
            ProgramFilesX86 = @"C:\Program Files (x86)",
            ProgramData = @"C:\ProgramData",
            UsersRoot = @"C:\Users",
            UserProfile = profile,
            LocalAppData = profile + @"\AppData\Local",
            RoamingAppData = profile + @"\AppData\Roaming",
            LocalLowAppData = profile + @"\AppData\LocalLow",
        };
    }

    /// <summary>Reads the real folders from the current Windows session.</summary>
    /// <exception cref="PlatformNotSupportedException">Thrown on non-Windows hosts.</exception>
    public static WindowsKnownFolders FromEnvironment()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Known folders can only be resolved on Windows. Use CreateDefault() for tests.");
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemDrive = WindowsPath.Normalize(Environment.GetEnvironmentVariable("SystemDrive") ?? systemRoot[..2]);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
                           ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(programFilesX86))
        {
            programFilesX86 = WindowsPath.Combine(systemDrive, "Program Files (x86)");
        }

        return new WindowsKnownFolders
        {
            SystemDrive = systemDrive,
            SystemRoot = WindowsPath.Normalize(systemRoot),
            ProgramFiles = WindowsPath.Normalize(programFiles),
            ProgramFilesX86 = WindowsPath.Normalize(programFilesX86),
            ProgramData = WindowsPath.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
            UsersRoot = WindowsPath.GetDirectoryName(profile) ?? WindowsPath.Combine(systemDrive, "Users"),
            UserProfile = WindowsPath.Normalize(profile),
            LocalAppData = WindowsPath.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            RoamingAppData = WindowsPath.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            LocalLowAppData = WindowsPath.Combine(profile, "AppData", "LocalLow"),
        };
    }

    /// <summary>The path of a scan root (§6.1–6.2).</summary>
    public string PathOf(ScanRoot root) => root switch
    {
        ScanRoot.ProgramFiles => ProgramFiles,
        ScanRoot.ProgramFilesX86 => ProgramFilesX86,
        ScanRoot.ProgramData => ProgramData,
        ScanRoot.LocalAppData => LocalAppData,
        ScanRoot.RoamingAppData => RoamingAppData,
        ScanRoot.LocalLowAppData => LocalLowAppData,
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Custom roots have no fixed path."),
    };

    /// <summary>Which scan root contains <paramref name="path"/>, or <see cref="ScanRoot.Custom"/> when none does.</summary>
    public ScanRoot RootOf(string? path)
    {
        // More specific roots first: LocalLow is not under Local, but check the AppData roots before the profile-wide ones anyway.
        foreach (var root in new[]
                 {
                     ScanRoot.LocalLowAppData, ScanRoot.LocalAppData, ScanRoot.RoamingAppData,
                     ScanRoot.ProgramFilesX86, ScanRoot.ProgramFiles, ScanRoot.ProgramData,
                 })
        {
            if (WindowsPath.IsSameOrUnder(path, PathOf(root)))
            {
                return root;
            }
        }

        return ScanRoot.Custom;
    }

    /// <summary>True when the path lies inside the current user's profile.</summary>
    public bool IsInUserProfile(string? path) => WindowsPath.IsSameOrUnder(path, UserProfile);

    /// <summary>The environment variables that commonly appear in registry commands, derived from these folders.</summary>
    public IReadOnlyDictionary<string, string> ToEnvironment() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemDrive"] = SystemDrive.TrimEnd(WindowsPath.Separator),
        ["SystemRoot"] = SystemRoot,
        ["windir"] = SystemRoot,
        ["ProgramFiles"] = ProgramFiles,
        ["ProgramW6432"] = ProgramFiles,
        ["ProgramFiles(x86)"] = ProgramFilesX86,
        ["ProgramData"] = ProgramData,
        ["ALLUSERSPROFILE"] = ProgramData,
        ["UserProfile"] = UserProfile,
        ["LocalAppData"] = LocalAppData,
        ["AppData"] = RoamingAppData,
        ["HomeDrive"] = SystemDrive.TrimEnd(WindowsPath.Separator),
        ["Public"] = WindowsPath.Combine(UsersRoot, "Public"),
    };
}
