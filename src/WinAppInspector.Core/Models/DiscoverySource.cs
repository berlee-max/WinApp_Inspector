namespace WinAppInspector.Core.Models;

/// <summary>Data sources (§7) that contributed to an application entity. Flags so an entity can list several.</summary>
[Flags]
public enum DiscoverySource
{
    None = 0,
    Registry = 1 << 0,
    Appx = 1 << 1,
    Directory = 1 << 2,
    Executable = 1 << 3,
    Signature = 1 << 4,
    Process = 1 << 5,
    Service = 1 << 6,
    Startup = 1 << 7,
    ScheduledTask = 1 << 8,
    Shortcut = 1 << 9,
}

/// <summary>Well-known scan roots (§6). <see cref="Custom"/> is used for "analyse this folder" (§18).</summary>
public enum ScanRoot
{
    Custom = 0,
    ProgramFiles = 1,
    ProgramFilesX86 = 2,
    ProgramData = 3,
    LocalAppData = 4,
    RoamingAppData = 5,
    LocalLowAppData = 6,
}

/// <summary>What a directory appears to hold. Used by residue and cache classification (§9.5–9.6).</summary>
public enum DirectoryRole
{
    Unknown = 0,
    /// <summary>Contains the program's executables.</summary>
    Program = 1,
    /// <summary>Configuration / user data.</summary>
    Data = 2,
    /// <summary>Cache, temp or GPU cache content.</summary>
    Cache = 3,
    /// <summary>Logs or crash reports.</summary>
    Logs = 4,
}

/// <summary>Official uninstall methods in the order they must be preferred (§5.2, §20.1).</summary>
public enum UninstallMethod
{
    None = 0,
    UninstallString = 1,
    QuietUninstallString = 2,
    Msi = 3,
    Appx = 4,
    BundledUninstaller = 5,
}

/// <summary>Where a registry uninstall entry lives (§7.1).</summary>
public enum RegistryScope
{
    /// <summary>HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall</summary>
    MachineNative = 0,
    /// <summary>HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall</summary>
    MachineWow6432 = 1,
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall</summary>
    CurrentUser = 2,
}

/// <summary>Kinds of startup registration (§7.7).</summary>
public enum StartupItemKind
{
    RegistryRun = 0,
    RegistryRunOnce = 1,
    StartupFolder = 2,
}

/// <summary>Result of reading an Authenticode signature (§7.4).</summary>
public enum SignatureStatus
{
    /// <summary>The signature was not checked (e.g. the option is disabled, §26.2).</summary>
    NotChecked = 0,
    NotSigned = 1,
    Valid = 2,
    Invalid = 3,
    /// <summary>The file could not be read or verification threw; see <see cref="SignatureInfo.Error"/>.</summary>
    ReadFailed = 4,
}
