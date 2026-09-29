namespace WinAppInspector.Core.Models;

/// <summary>A directory associated with an application (§14.3). Size is filled in by a second-stage background task (§32).</summary>
public sealed record AppDirectory
{
    public required string Path { get; init; }
    public ScanRoot Root { get; init; } = ScanRoot.Custom;
    public DirectoryRole Role { get; init; } = DirectoryRole.Unknown;
    public DateTimeOffset? LastWriteTime { get; init; }
    public DateTimeOffset? CreationTime { get; init; }
    /// <summary>Total size in bytes; <c>null</c> until computed.</summary>
    public long? SizeBytes { get; init; }
    public int? FileCount { get; init; }
    public IReadOnlyList<string> ExecutablePaths { get; init; } = [];
    public bool ContainsExecutables => ExecutablePaths.Count > 0;
}

/// <summary>A running process (§7.5).</summary>
public sealed record ProcessRecord
{
    public required int ProcessId { get; init; }
    public required string Name { get; init; }
    public string? ExecutablePath { get; init; }
    public string? CommandLine { get; init; }
    public int? ParentProcessId { get; init; }
    public string? CompanyName { get; init; }
    public string? ProductName { get; init; }
}

/// <summary>A Windows service (§7.6).</summary>
public sealed record ServiceRecord
{
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? State { get; init; }
    public string? StartMode { get; init; }
    /// <summary>Raw <c>PathName</c> including arguments; see <see cref="ExecutablePath"/> for the parsed image path.</summary>
    public string? PathName { get; init; }
    public string? ExecutablePath { get; init; }
    public string? StartName { get; init; }
    public string? Description { get; init; }
}

/// <summary>A startup registration (§7.7).</summary>
public sealed record StartupItemRecord
{
    public required string Name { get; init; }
    public required StartupItemKind Kind { get; init; }
    /// <summary>Registry key path or startup folder path.</summary>
    public required string Location { get; init; }
    public bool IsMachineWide { get; init; }
    public string? Command { get; init; }
    public string? ExecutablePath { get; init; }
    /// <summary>From Explorer's StartupApproved state; <c>null</c> when unknown.</summary>
    public bool? IsDisabled { get; init; }
}

/// <summary>A scheduled task (§7.8).</summary>
public sealed record ScheduledTaskRecord
{
    public required string TaskName { get; init; }
    public required string TaskPath { get; init; }
    public string? Execute { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? Trigger { get; init; }
    public string? State { get; init; }
    public string? Author { get; init; }
}

/// <summary>An entry under one of the three Uninstall registry keys (§7.1).</summary>
public sealed record RegistryUninstallEntry
{
    public required RegistryScope Scope { get; init; }
    /// <summary>Sub-key name, e.g. a product GUID or "Google Chrome".</summary>
    public required string KeyName { get; init; }
    /// <summary>Full key path for display and "why" output.</summary>
    public required string KeyPath { get; init; }
    public string? DisplayName { get; init; }
    public string? DisplayVersion { get; init; }
    public string? Publisher { get; init; }
    public string? InstallLocation { get; init; }
    /// <summary>Raw InstallDate, usually <c>yyyyMMdd</c>.</summary>
    public string? InstallDate { get; init; }
    /// <summary>EstimatedSize as stored by Windows, in KB.</summary>
    public long? EstimatedSizeKb { get; init; }
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public string? DisplayIcon { get; init; }
    public bool WindowsInstaller { get; init; }
    public bool SystemComponent { get; init; }
    public string? ReleaseType { get; init; }
    public string? ParentKeyName { get; init; }
}

/// <summary>An AppX / MSIX package (§7.2).</summary>
public sealed record AppxPackageRecord
{
    public required string Name { get; init; }
    public required string PackageFullName { get; init; }
    public required string PackageFamilyName { get; init; }
    public string? DisplayName { get; init; }
    public string? Publisher { get; init; }
    public string? PublisherDisplayName { get; init; }
    public string? Version { get; init; }
    public string? InstallLocation { get; init; }
    public string? Architecture { get; init; }
    public bool IsFramework { get; init; }
    /// <summary>Only known when read through PowerShell; the WinRT API does not expose it.</summary>
    public bool? NonRemovable { get; init; }
    public bool IsBundle { get; init; }
    public string? SignatureKind { get; init; }
}

/// <summary>Version resource of an executable (§7.3).</summary>
public sealed record ExecutableMetadata
{
    public required string Path { get; init; }
    public string? FileDescription { get; init; }
    public string? ProductName { get; init; }
    public string? ProductVersion { get; init; }
    public string? FileVersion { get; init; }
    public string? CompanyName { get; init; }
    public string? OriginalFilename { get; init; }
    public string? InternalName { get; init; }
    public string? Copyright { get; init; }
    public long? FileSizeBytes { get; init; }
    public DateTimeOffset? LastWriteTime { get; init; }
}

/// <summary>Authenticode signature information (§7.4). A valid signature is an attribution hint, not a safety verdict.</summary>
public sealed record SignatureInfo
{
    public required SignatureStatus Status { get; init; }
    public string? SubjectName { get; init; }
    public string? IssuerName { get; init; }
    /// <summary>Publisher name extracted from the certificate subject (CN / O).</summary>
    public string? Publisher { get; init; }
    public string? Thumbprint { get; init; }
    public DateTimeOffset? SigningTime { get; init; }
    public DateTimeOffset? NotBefore { get; init; }
    public DateTimeOffset? NotAfter { get; init; }
    /// <summary>Specific reason when <see cref="Status"/> is <see cref="SignatureStatus.ReadFailed"/> or <see cref="SignatureStatus.Invalid"/> (§34).</summary>
    public string? Error { get; init; }

    public static SignatureInfo NotChecked { get; } = new() { Status = SignatureStatus.NotChecked };
    public static SignatureInfo NotSigned { get; } = new() { Status = SignatureStatus.NotSigned };
    public bool IsValid => Status == SignatureStatus.Valid;
}
