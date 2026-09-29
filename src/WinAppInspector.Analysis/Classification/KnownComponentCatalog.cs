using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Classification;

/// <summary>
/// Static knowledge about shared runtimes (§9.8) and Windows-owned folders (§9.9). These are hints that feed the
/// classifier; each hit is recorded as a <see cref="Core.Evidence.Reason"/> so the user can see why.
/// </summary>
public sealed class KnownComponentCatalog
{
    private static readonly string[] SharedRuntimePatterns =
    [
        "visual c++", "vc++", "vcredist", "redistributable", "microsoft visual c", "universal crt", "msvc",
        ".net framework", ".net runtime", ".net core", ".net sdk", "microsoft .net", "windows desktop runtime", "asp.net core", "dotnet",
        "webview2", "edgewebview", "edge webview",
        "directx", "xna framework",
        "java runtime", "java(tm)", "java se", "java 8", "java update", "openjdk", "jre", "jdk", "adoptium", "temurin",
        "python launcher", "python 3", "python 2",
        "microsoft xml", "msxml", "windows installer", "microsoft help viewer", "microsoft sql server compact", "sql server native client",
        "microsoft ole db driver", "microsoft odbc driver", "microsoft access database engine", "visual studio tools for office runtime",
        "windows app runtime", "windowsappruntime", "microsoft.ui.xaml", "vclibs", "microsoft.net.native",
        "adobe air", "microsoft silverlight", "bonjour", "nvidia physx",
    ];

    /// <summary>Program Files children that belong to Windows or to a shared Microsoft component rather than an application.</summary>
    private static readonly HashSet<string> ProgramFilesSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "Internet Explorer", "Windows Defender", "Windows Defender Advanced Threat Protection", "Windows Mail",
        "Windows Media Player", "Windows Multimedia Platform", "Windows NT", "Windows Photo Viewer", "Windows Portable Devices",
        "Windows Security", "Windows Sidebar", "WindowsPowerShell", "PowerShell", "WindowsApps", "ModifiableWindowsApps", "MSBuild",
        "Reference Assemblies", "Uninstall Information", "Microsoft Update Health Tools", "Microsoft.NET", "PackageManagement",
        "Hyper-V", "Windows Kits", "Microsoft", "Microsoft Office 15", "Application Verifier", "Windows Defender Application Guard",
        "Microsoft OneDrive", "Microsoft Analysis Services", "Microsoft Help Viewer", "MSECache", "IIS", "IIS Express",
        "Microsoft SQL Server", "Microsoft SDKs", "Microsoft Visual Studio", "dotnet", "Java", "Common Files (x86)",
    };

    private static readonly HashSet<string> ProgramDataSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Package Cache", "USOShared", "USOPrivate", "regid.1991-06.com.microsoft", "SoftwareDistribution", "ssh",
        "Packages", "Microsoft OneDrive", "WindowsHolographicDevices", "Comms", "Windows", "Windows App Certification Kit",
        "Application Data", "Desktop", "Documents", "Start Menu", "Templates", "Favorites", "Intel", "NVIDIA", "NVIDIA Corporation",
        "AMD", "Dell", "HP", "Lenovo", "Realtek", "Microsoft Help", "Microsoft Visual Studio", "Oracle", "MiniTool",
    };

    private static readonly HashSet<string> LocalAppDataSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Packages", "Temp", "Comms", "ConnectedDevicesPlatform", "D3DSCache", "PeerDistRepub", "PlaceholderTileLogoFolder",
        "Publishers", "CrashDumps", "VirtualStore", "History", "Microsoft Help", "Application Data", "Temporary Internet Files",
        "PenWorkspace", "OneDrive", "SquirrelTemp", "Programs", "Packages", "PowerShell", "NuGet", "pip", "npm-cache", "Yarn", "ms-playwright",
        "ElevatedDiagnostics", "IsolatedStorage", "AMD", "NVIDIA", "NVIDIA Corporation", "Intel", "Realtek", "Dell", "HP", "Lenovo",
    };

    private static readonly HashSet<string> RoamingAppDataSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Adobe", "Macromedia", "Identities", "Sun", "Oracle", "NVIDIA", "Intel", "AMD", "Dell", "HP", "Lenovo", "Realtek",
    };

    private static readonly HashSet<string> LocalLowSystemFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Adobe", "Sun", "Oracle", "Intel", "NVIDIA", "AMD",
    };

    /// <summary>True when the name reads like a shared runtime / redistributable (§9.8).</summary>
    public bool IsSharedRuntimeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var pattern in SharedRuntimePatterns)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a top-level folder under a scan root is owned by Windows / a vendor platform rather than an application (§9.9).</summary>
    public bool IsSystemFolder(ScanRoot root, string? path)
    {
        var name = WindowsPath.GetFileName(path);
        if (name.Length == 0)
        {
            return false;
        }

        return root switch
        {
            ScanRoot.ProgramFiles or ScanRoot.ProgramFilesX86 => ProgramFilesSystemFolders.Contains(name),
            ScanRoot.ProgramData => ProgramDataSystemFolders.Contains(name),
            ScanRoot.LocalAppData => LocalAppDataSystemFolders.Contains(name),
            ScanRoot.RoamingAppData => RoamingAppDataSystemFolders.Contains(name),
            ScanRoot.LocalLowAppData => LocalLowSystemFolders.Contains(name),
            _ => false,
        };
    }
}
