using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Resolution;

/// <summary>Where an entity originated; determines which classification path applies.</summary>
public enum SeedKind
{
    Registry = 0,
    Package = 1,
    Directory = 2,
    /// <summary>A running program whose executable lies outside every discovered directory (a portable tool started from elsewhere).</summary>
    Process = 3,
}

/// <summary>
/// Mutable work-in-progress form of an <see cref="ApplicationEntity"/> used while the resolver merges sources.
/// Converted to the immutable record at the end of resolution.
/// </summary>
public sealed class EntityDraft
{
    public required string Id { get; init; }
    public required SeedKind Seed { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? InstallLocation { get; set; }
    public string? MainExecutable { get; set; }
    public string? IconPath { get; set; }
    public string? UninstallCommand { get; set; }
    public string? QuietUninstallCommand { get; set; }
    public string? MsiProductCode { get; set; }
    public string? AppxPackageFullName { get; set; }
    public UninstallMethod PreferredUninstallMethod { get; set; }
    public DateOnly? InstallDate { get; set; }
    public long? EstimatedSizeBytes { get; set; }
    public SignatureInfo Signature { get; set; } = SignatureInfo.NotChecked;
    public DiscoverySource Sources { get; set; }

    /// <summary>Registry entries that Apps &amp; Features would hide (SystemComponent, updates, children).</summary>
    public bool IsHiddenRegistryEntry { get; set; }

    public List<RegistryUninstallEntry> RegistryEntries { get; } = [];
    public List<AppxPackageRecord> Packages { get; } = [];
    public List<AppDirectory> Directories { get; } = [];
    public List<ProcessRecord> Processes { get; } = [];
    public List<ServiceRecord> Services { get; } = [];
    public List<StartupItemRecord> StartupItems { get; } = [];
    public List<ScheduledTaskRecord> ScheduledTasks { get; } = [];
    public List<ExecutableMetadata> Executables { get; } = [];
    public List<ShortcutRecord> Shortcuts { get; } = [];

    /// <summary>
    /// Folders a registered application is known to run from even though no InstallLocation was recorded (the folder of
    /// its DisplayIcon executable). Used only to attribute processes and links; never offered for deletion.
    /// </summary>
    public List<string> OwnershipHints { get; } = [];
    public List<EvidenceItem> Evidence { get; } = [];
    public List<Reason> Reasons { get; } = [];

    /// <summary>Directories that hold executables (as opposed to data / cache / vendor folders).</summary>
    public IEnumerable<AppDirectory> ProgramDirectories => Directories.Where(d => d.ContainsExecutables && d.Role != DirectoryRole.SharedParent);

    public bool HasOfficialUninstaller => PreferredUninstallMethod != UninstallMethod.None;

    public void AddEvidence(EvidenceKind kind, string detail)
    {
        if (!Evidence.Any(e => e.Kind == kind && string.Equals(e.Detail, detail, StringComparison.OrdinalIgnoreCase)))
        {
            Evidence.Add(new EvidenceItem(kind, detail));
        }
    }

    public void AddReason(ReasonKind kind, string? detail = null)
    {
        if (!Reasons.Any(r => r.Kind == kind && string.Equals(r.Detail, detail, StringComparison.OrdinalIgnoreCase)))
        {
            Reasons.Add(new Reason(kind, detail));
        }
    }

    public void AddDirectory(AppDirectory directory)
    {
        var index = Directories.FindIndex(d => string.Equals(d.Path, directory.Path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Directories.Add(directory);
        }
        else if (Directories[index].Role == DirectoryRole.SharedParent && directory.Role != DirectoryRole.SharedParent)
        {
            Directories[index] = directory;
        }
    }
}
