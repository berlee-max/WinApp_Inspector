using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Actions;

/// <summary>A request to run the official uninstaller (§20.1). <see cref="UserConfirmed"/> must be true (§5.1).</summary>
public sealed record UninstallRequest
{
    public required ApplicationEntity Application { get; init; }
    public bool UserConfirmed { get; init; }
    /// <summary>Prefer the quiet route when one exists; otherwise the interactive uninstaller is shown.</summary>
    public bool PreferQuiet { get; init; }
}

public enum UninstallOutcome
{
    Succeeded = 0,
    /// <summary>The uninstaller finished but asked for a restart (MSI 3010).</summary>
    SucceededRestartRequired = 1,
    CancelledByUser = 2,
    Failed = 3,
    /// <summary>No official uninstall route exists; manual cleanup is the only option (§5.2).</summary>
    NoUninstaller = 4,
    /// <summary>The request was not confirmed by the user.</summary>
    NotConfirmed = 5,
}

public sealed record UninstallResult(UninstallOutcome Outcome, UninstallMethod MethodUsed, string? Command, int? ExitCode, string? Error, TimeSpan Elapsed)
{
    public bool Succeeded => Outcome is UninstallOutcome.Succeeded or UninstallOutcome.SucceededRestartRequired;
}

/// <summary>Runs the application's own uninstaller in the §5.2 order of preference.</summary>
public interface IUninstallManager
{
    Task<UninstallResult> UninstallAsync(UninstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken);
}

/// <summary>Kinds of leftover the user may remove (§21, §22).</summary>
public enum CleanupItemKind
{
    Directory = 0,
    StartupRegistryValue = 1,
    StartupFolderItem = 2,
    ScheduledTask = 3,
    Service = 4,
    RegistryKey = 5,
}

/// <summary>One removable item, shown with a checkbox (§22). Immutable; the UI tracks the selection separately.</summary>
public sealed record CleanupCandidate
{
    public required CleanupItemKind Kind { get; init; }
    /// <summary>Path, registry value path (<c>key::value</c>), task path, service name or registry key.</summary>
    public required string Target { get; init; }
    public string? Detail { get; init; }
    public long? SizeBytes { get; init; }
    /// <summary>True when deletion can go to the recycle bin (§24). Registry, tasks and services cannot.</summary>
    public bool CanRecycle { get; init; }
    /// <summary>True when the operation needs administrator rights (§28) and will prompt for elevation.</summary>
    public bool RequiresElevation { get; init; }
    /// <summary>Set when the safety rules refuse this item; it is shown but cannot be selected.</summary>
    public string? Blocked { get; init; }
}

/// <summary>What the user confirmed to remove.</summary>
public sealed record CleanupPlan
{
    public required ApplicationEntity Application { get; init; }
    public required IReadOnlyList<CleanupCandidate> Items { get; init; }
    public bool UserConfirmed { get; init; }
    /// <summary>§24: send files to the recycle bin instead of deleting them.</summary>
    public bool UseRecycleBin { get; init; } = true;
    /// <summary>§24: the user has acknowledged that items which cannot be recycled are deleted permanently.</summary>
    public bool PermanentDeletionAcknowledged { get; init; }
}

public sealed record CleanupItemResult(CleanupCandidate Item, bool Succeeded, bool PermanentlyDeleted, string? Error);

public sealed record CleanupResult(IReadOnlyList<CleanupItemResult> Items)
{
    public bool AllSucceeded => Items.All(i => i.Succeeded);
    public int SucceededCount => Items.Count(i => i.Succeeded);
}

/// <summary>Executes a confirmed <see cref="CleanupPlan"/>. Every item is re-checked against the safety rules before it is touched.</summary>
public interface ICleanupManager
{
    Task<CleanupResult> ExecuteAsync(CleanupPlan plan, IProgress<string>? progress, CancellationToken cancellationToken);
}

/// <summary>Terminates a process before uninstall (§20.1 step 1), only after confirmation.</summary>
public interface IProcessController
{
    Task<(bool Succeeded, string? Error)> TerminateAsync(int processId, string name, bool userConfirmed, CancellationToken cancellationToken);
}

/// <summary>Creates a system restore point before high-impact operations (§27).</summary>
public interface IRestorePointManager
{
    Task<(bool Succeeded, string? Error)> CreateAsync(string description, CancellationToken cancellationToken);
}

/// <summary>Checks whether a registry key still exists, for residue detection (§21).</summary>
public interface IRegistryKeyProbe
{
    bool Exists(string fullKeyPath);
}

/// <summary>Leftovers found after an uninstall (§21) or the items of an application selected for manual removal (§22).</summary>
public sealed record ResidueReport(
    IReadOnlyList<AppDirectory> Directories,
    IReadOnlyList<RegistryUninstallEntry> RegistryEntries,
    IReadOnlyList<string> ConfigurationRegistryKeys,
    IReadOnlyList<StartupItemRecord> StartupItems,
    IReadOnlyList<ServiceRecord> Services,
    IReadOnlyList<ScheduledTaskRecord> ScheduledTasks)
{
    public bool IsEmpty => Directories.Count == 0 && RegistryEntries.Count == 0 && ConfigurationRegistryKeys.Count == 0
                           && StartupItems.Count == 0 && Services.Count == 0 && ScheduledTasks.Count == 0;
}
