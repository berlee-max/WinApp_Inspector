using WinAppInspector.Core.Evidence;

namespace WinAppInspector.Core.Models;

/// <summary>
/// The unified application entity (§8, §38). Everything the user sees is an aggregation of
/// registry entries, directories, packages, processes, services, startup items and scheduled tasks
/// into one of these. Immutable; use <c>with</c> to derive updated copies.
/// </summary>
/// <remarks>
/// Collection properties are compared by reference in the generated equality; two entities with equal
/// scalar data but different list instances are not equal. Compare by <see cref="Id"/> where identity matters.
/// </remarks>
public sealed record ApplicationEntity
{
    /// <summary>Stable identifier: registry key path, package family name, or a normalized directory path.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Publisher { get; init; }
    public AppType AppType { get; init; } = AppType.Undetermined;

    public string? InstallLocation { get; init; }
    public string? MainExecutable { get; init; }
    public string? IconPath { get; init; }

    public string? UninstallCommand { get; init; }
    public string? QuietUninstallCommand { get; init; }
    /// <summary>MSI product code (GUID) when the entry was installed by Windows Installer.</summary>
    public string? MsiProductCode { get; init; }
    public string? AppxPackageFullName { get; init; }
    public UninstallMethod PreferredUninstallMethod { get; init; } = UninstallMethod.None;

    public SignatureInfo Signature { get; init; } = SignatureInfo.NotChecked;

    /// <summary>True when the publisher / signature identifies Microsoft.</summary>
    public bool IsMicrosoft { get; init; }
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Unknown;
    public ConfidenceLevel DetectionConfidence { get; init; } = ConfidenceLevel.Unknown;
    public DiscoverySource Sources { get; init; } = DiscoverySource.None;

    public DateOnly? InstallDate { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    /// <summary>Registry EstimatedSize converted to bytes, when present.</summary>
    public long? EstimatedSizeBytes { get; init; }
    /// <summary>Sum of directory sizes once the background size pass has run (§32).</summary>
    public long? DiskUsageBytes { get; init; }

    public IReadOnlyList<AppDirectory> Directories { get; init; } = [];
    public IReadOnlyList<ProcessRecord> Processes { get; init; } = [];
    public IReadOnlyList<ServiceRecord> Services { get; init; } = [];
    public IReadOnlyList<StartupItemRecord> StartupItems { get; init; } = [];
    public IReadOnlyList<ScheduledTaskRecord> ScheduledTasks { get; init; } = [];
    public IReadOnlyList<RegistryUninstallEntry> RegistryEntries { get; init; } = [];
    public IReadOnlyList<AppxPackageRecord> Packages { get; init; } = [];
    public IReadOnlyList<ExecutableMetadata> Executables { get; init; } = [];

    /// <summary>Attribution evidence (§10.2) that links the directories to this application.</summary>
    public IReadOnlyList<EvidenceItem> Evidence { get; init; } = [];

    /// <summary>Reasons behind <see cref="AppType"/> and <see cref="RiskLevel"/>, shown under "why" (§14.6, §42).</summary>
    public IReadOnlyList<Reason> Reasons { get; init; } = [];

    /// <summary>§38 <c>IsRunning</c>: derived from the attached processes so it cannot drift.</summary>
    public bool IsRunning => Processes.Count > 0;

    /// <summary>Whether any official uninstall route exists (§5.2). Only when this is false does manual cleanup apply.</summary>
    public bool HasOfficialUninstaller => PreferredUninstallMethod != UninstallMethod.None;
}
