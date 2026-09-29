using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Classification;

/// <summary>Verdict for an unowned directory without executables (§9.5 vs §9.10).</summary>
public sealed record ResidueVerdict(AppType Type, IReadOnlyList<Reason> Reasons);

/// <summary>
/// Decides whether a directory with no registration, no executables and no runtime links is leftover from an
/// uninstalled program (§9.5, §40) or simply cannot be judged yet (§9.10). Never says "safe to delete" by itself.
/// </summary>
public sealed class ResidueDetector
{
    /// <summary>Directories untouched for this long count as stale.</summary>
    public static readonly TimeSpan StaleAge = TimeSpan.FromDays(90);

    public ResidueVerdict Evaluate(EntityDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var reasons = new List<Reason>();

        var hasRegistration = draft.RegistryEntries.Count > 0 || draft.Packages.Count > 0;
        var hasExecutable = draft.Directories.Any(d => d.ContainsExecutables);
        var hasRuntimeLink = draft.Processes.Count > 0 || draft.Services.Count > 0 || draft.StartupItems.Count > 0 || draft.ScheduledTasks.Count > 0;

        reasons.Add(new Reason(hasRegistration ? ReasonKind.UninstallEntryFound : ReasonKind.UninstallEntryMissing));
        reasons.Add(new Reason(hasExecutable ? ReasonKind.MainExecutableFound : ReasonKind.MainExecutableMissing));
        reasons.Add(new Reason(draft.Processes.Count > 0 ? ReasonKind.RunningProcessFound : ReasonKind.NoRunningProcess));
        reasons.Add(new Reason(draft.Services.Count > 0 ? ReasonKind.ServiceFound : ReasonKind.NoService));
        reasons.Add(new Reason(draft.StartupItems.Count > 0 ? ReasonKind.StartupItemFound : ReasonKind.NoStartupItem));
        reasons.Add(new Reason(draft.ScheduledTasks.Count > 0 ? ReasonKind.ScheduledTaskFound : ReasonKind.NoScheduledTask));

        if (hasRegistration || hasExecutable || hasRuntimeLink)
        {
            return new ResidueVerdict(AppType.Undetermined, reasons);
        }

        var lastWrite = draft.Directories.Select(d => d.LastWriteTime).Where(t => t is not null).Max();
        var isStale = lastWrite is not null && now - lastWrite.Value >= StaleAge;
        if (lastWrite is not null)
        {
            reasons.Add(new Reason(ReasonKind.LongUnmodified, lastWrite.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }

        var onlyCacheContent = draft.Directories.Count > 0 && draft.Directories.All(IsCacheOrLogOnly);
        if (onlyCacheContent)
        {
            reasons.Add(new Reason(ReasonKind.OnlyCacheOrLogContent));
        }

        var cacheLikeName = draft.Directories.Any(d => CacheDirectoryNames.IsCacheOrLogPath(d.Path));
        if (cacheLikeName)
        {
            reasons.Add(new Reason(ReasonKind.CacheLikeFolderName, WindowsPath.GetFileName(draft.Directories[0].Path)));
        }

        // Stale, or clearly nothing but cache / logs: residue. Recently touched data with unknown owner: undetermined.
        var type = isStale || onlyCacheContent ? AppType.SuspectedResidue : AppType.Undetermined;
        if (!isStale && lastWrite is null && onlyCacheContent is false)
        {
            type = AppType.Undetermined;
        }

        return new ResidueVerdict(type, reasons);
    }

    private static bool IsCacheOrLogOnly(AppDirectory directory)
    {
        if (directory.ChildDirectoryNames.Count == 0)
        {
            return false;
        }

        return directory.ChildDirectoryNames.All(CacheDirectoryNames.IsCacheOrLogName) && (directory.TopLevelFileCount ?? 0) <= 3;
    }
}
