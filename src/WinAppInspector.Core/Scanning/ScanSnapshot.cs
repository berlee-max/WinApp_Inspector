using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Scanning;

/// <summary>
/// Everything one scan run collected (§7), handed to the attribution engine. Immutable and OS-neutral,
/// so resolution can be replayed in tests from fake data.
/// </summary>
public sealed record ScanSnapshot
{
    public required DateTimeOffset ScanTime { get; init; }
    public required WindowsKnownFolders Folders { get; init; }
    public IReadOnlyList<RegistryUninstallEntry> RegistryEntries { get; init; } = [];
    public IReadOnlyList<AppxPackageRecord> Packages { get; init; } = [];
    public IReadOnlyList<AppDirectory> Directories { get; init; } = [];
    public IReadOnlyList<ProcessRecord> Processes { get; init; } = [];
    public IReadOnlyList<ServiceRecord> Services { get; init; } = [];
    public IReadOnlyList<StartupItemRecord> StartupItems { get; init; } = [];
    public IReadOnlyList<ScheduledTaskRecord> ScheduledTasks { get; init; } = [];
    public IReadOnlyList<ShortcutRecord> Shortcuts { get; init; } = [];
    /// <summary>Version resources keyed by normalized executable path (case-insensitive).</summary>
    public IReadOnlyDictionary<string, ExecutableMetadata> Executables { get; init; } = new Dictionary<string, ExecutableMetadata>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Signatures keyed by normalized executable path (case-insensitive).</summary>
    public IReadOnlyDictionary<string, SignatureInfo> Signatures { get; init; } = new Dictionary<string, SignatureInfo>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ScanError> Errors { get; init; } = [];

    public ExecutableMetadata? MetadataOf(string? path) =>
        path is not null && Executables.TryGetValue(WindowsPath.Normalize(path), out var m) ? m : null;

    public SignatureInfo? SignatureOf(string? path) =>
        path is not null && Signatures.TryGetValue(WindowsPath.Normalize(path), out var s) ? s : null;
}
