using WinAppInspector.Core.Actions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Analysis.Cleanup;

/// <summary>
/// Turns a <see cref="ResidueReport"/> into the checklist the user sees before manual removal (§21–§24).
/// Every candidate is pre-checked with <see cref="DeletionGuard"/>; blocked ones stay visible but unselectable,
/// so the user can see what was refused and why. Pure logic.
/// </summary>
public sealed class CleanupPlanner
{
    private readonly DeletionGuard _guard;
    private readonly ProtectedPathRule _protectedPaths;

    public CleanupPlanner(DeletionGuard guard, ProtectedPathRule protectedPaths)
    {
        _guard = guard;
        _protectedPaths = protectedPaths;
    }

    /// <summary>Builds candidates for <paramref name="app"/>. When <paramref name="afterUninstall"/> is true the official
    /// uninstaller has already run, so the "official uninstaller available" rule no longer applies.</summary>
    public IReadOnlyList<CleanupCandidate> Build(ApplicationEntity app, ResidueReport report, bool afterUninstall)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(report);

        var candidates = new List<CleanupCandidate>();
        var evaluated = afterUninstall ? app with { PreferredUninstallMethod = UninstallMethod.None, Processes = [] } : app;

        foreach (var directory in report.Directories)
        {
            string? blocked = null;
            if (directory.Role == DirectoryRole.SharedParent)
            {
                blocked = DeletionBlockerKind.SharedDirectory.ToString();
            }
            else
            {
                var verdict = _guard.Evaluate(new DeletionRequest
                {
                    Application = evaluated,
                    TargetPaths = [directory.Path],
                    UserConfirmed = true,
                    ServicesHandled = true,
                });
                blocked = verdict.HardBlockers.Count == 0 ? null : string.Join(", ", verdict.HardBlockers.Select(b => b.Kind.ToString()).Distinct());
            }

            candidates.Add(new CleanupCandidate
            {
                Kind = CleanupItemKind.Directory,
                Target = directory.Path,
                Detail = directory.Role.ToString(),
                SizeBytes = directory.SizeBytes,
                CanRecycle = true,
                RequiresElevation = !IsUserWritable(directory.Path, app),
                Blocked = blocked,
            });
        }

        foreach (var item in report.StartupItems)
        {
            var isFolder = item.Kind == StartupItemKind.StartupFolder;
            candidates.Add(new CleanupCandidate
            {
                Kind = isFolder ? CleanupItemKind.StartupFolderItem : CleanupItemKind.StartupRegistryValue,
                Target = isFolder ? item.Command ?? item.Name : $"{item.Location}::{item.Name}",
                Detail = item.Command,
                CanRecycle = isFolder,
                RequiresElevation = item.IsMachineWide,
            });
        }

        foreach (var task in report.ScheduledTasks)
        {
            var path = task.TaskPath.EndsWith('\\') ? task.TaskPath + task.TaskName : task.TaskPath + "\\" + task.TaskName;
            if (!candidates.Any(c => c.Kind == CleanupItemKind.ScheduledTask && string.Equals(c.Target, path, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(new CleanupCandidate
                {
                    Kind = CleanupItemKind.ScheduledTask,
                    Target = path,
                    Detail = task.Execute,
                    CanRecycle = false,
                    RequiresElevation = true,
                });
            }
        }

        foreach (var service in report.Services)
        {
            candidates.Add(new CleanupCandidate
            {
                Kind = CleanupItemKind.Service,
                Target = service.Name,
                Detail = service.ExecutablePath ?? service.PathName,
                CanRecycle = false,
                RequiresElevation = true,
                Blocked = string.Equals(service.State, "Running", StringComparison.OrdinalIgnoreCase) ? DeletionBlockerKind.ReferencedByService.ToString() : null,
            });
        }

        foreach (var entry in report.RegistryEntries)
        {
            candidates.Add(new CleanupCandidate
            {
                Kind = CleanupItemKind.RegistryKey,
                Target = entry.KeyPath,
                Detail = entry.DisplayName,
                CanRecycle = false,
                RequiresElevation = entry.Scope != RegistryScope.CurrentUser,
            });
        }

        foreach (var key in report.ConfigurationRegistryKeys)
        {
            candidates.Add(new CleanupCandidate
            {
                Kind = CleanupItemKind.RegistryKey,
                Target = key,
                CanRecycle = false,
                RequiresElevation = !key.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase),
            });
        }

        return candidates;
    }

    /// <summary>Validates a plan the user assembled: no blocked items, confirmation given, permanent deletion acknowledged when needed.</summary>
    public IReadOnlyList<string> Validate(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var problems = new List<string>();

        if (!plan.UserConfirmed)
        {
            problems.Add(DeletionBlockerKind.UserConfirmationRequired.ToString());
        }

        foreach (var item in plan.Items)
        {
            if (item.Blocked is not null)
            {
                problems.Add($"{item.Target}: {item.Blocked}");
            }

            if (item.Kind == CleanupItemKind.Directory && _protectedPaths.IsProtected(item.Target))
            {
                problems.Add($"{item.Target}: {DeletionBlockerKind.ProtectedPath}");
            }
        }

        var permanent = plan.Items.Any(i => !i.CanRecycle) || !plan.UseRecycleBin;
        if (permanent && !plan.PermanentDeletionAcknowledged)
        {
            problems.Add("PermanentDeletionNotAcknowledged");
        }

        return problems.Distinct().ToArray();
    }

    private static bool IsUserWritable(string path, ApplicationEntity app)
    {
        // Under the user profile no elevation is needed; Program Files / ProgramData generally require it (§28).
        var root = app.Directories.FirstOrDefault(d => WindowsPath.AreEqual(d.Path, path))?.Root;
        return root is ScanRoot.LocalAppData or ScanRoot.RoamingAppData or ScanRoot.LocalLowAppData
               || path.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase);
    }
}
