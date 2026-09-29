using WinAppInspector.Core.Models;

namespace WinAppInspector.Scanners.Registry;

/// <summary>The three Uninstall keys from §7.1.</summary>
public static class RegistryUninstallKeys
{
    public const string RelativeNative = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string RelativeWow6432 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<(RegistryScope Scope, string HiveName, string RelativePath)> All { get; } =
    [
        (RegistryScope.MachineNative, "HKEY_LOCAL_MACHINE", RelativeNative),
        (RegistryScope.MachineWow6432, "HKEY_LOCAL_MACHINE", RelativeWow6432),
        (RegistryScope.CurrentUser, "HKEY_CURRENT_USER", RelativeNative),
    ];

    public static string FullPath(RegistryScope scope)
    {
        var (_, hive, relative) = All.First(k => k.Scope == scope);
        return hive + "\\" + relative;
    }
}
