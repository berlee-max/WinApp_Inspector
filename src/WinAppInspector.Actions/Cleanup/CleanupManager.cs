using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Logging;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Actions.Cleanup;

/// <summary>
/// Executes a confirmed cleanup plan (§22–§25). Directories go to the recycle bin unless the plan says otherwise;
/// registry values, keys, scheduled tasks and services are removed permanently and only when the plan acknowledges it.
/// Machine-wide items that need administrator rights are handled through an elevated <c>reg.exe</c> / <c>schtasks.exe</c> /
/// <c>sc.exe</c> so the UAC prompt appears per operation (§28). Every item is re-validated against the safety rules first.
/// </summary>
public sealed class CleanupManager : ICleanupManager
{
    private readonly ProtectedPathRule _protectedPaths;
    private readonly DeletionGuard _guard;
    private readonly IOperationLog _log;
    private readonly ILogger<CleanupManager> _logger;

    public CleanupManager(ProtectedPathRule protectedPaths, DeletionGuard guard, IOperationLog log, ILogger<CleanupManager> logger)
    {
        _protectedPaths = protectedPaths;
        _guard = guard;
        _log = log;
        _logger = logger;
    }

    public async Task<CleanupResult> ExecuteAsync(CleanupPlan plan, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var results = new List<CleanupItemResult>();

        if (!plan.UserConfirmed)
        {
            // §5.1: refuse the whole plan; nothing is touched.
            return new CleanupResult(plan.Items.Select(i => new CleanupItemResult(i, false, false, "Not confirmed by the user.")).ToArray());
        }

        var permanentNeeded = plan.Items.Any(i => !i.CanRecycle) || !plan.UseRecycleBin;
        if (permanentNeeded && !plan.PermanentDeletionAcknowledged)
        {
            return new CleanupResult(plan.Items.Select(i => new CleanupItemResult(i, false, false, "Permanent deletion was not acknowledged.")).ToArray());
        }

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(item.Target);

            if (item.Blocked is not null)
            {
                results.Add(new CleanupItemResult(item, false, false, "Blocked by safety rules: " + item.Blocked));
                continue;
            }

            CleanupItemResult result;
            try
            {
                result = item.Kind switch
                {
                    CleanupItemKind.Directory => await Task.Run(() => RemoveDirectory(plan, item), cancellationToken).ConfigureAwait(false),
                    CleanupItemKind.StartupFolderItem => await Task.Run(() => RemoveFile(plan, item), cancellationToken).ConfigureAwait(false),
                    CleanupItemKind.StartupRegistryValue => await RemoveRegistryValueAsync(item, cancellationToken).ConfigureAwait(false),
                    CleanupItemKind.RegistryKey => await RemoveRegistryKeyAsync(item, cancellationToken).ConfigureAwait(false),
                    CleanupItemKind.ScheduledTask => await RemoveScheduledTaskAsync(item, cancellationToken).ConfigureAwait(false),
                    CleanupItemKind.Service => await RemoveServiceAsync(item, cancellationToken).ConfigureAwait(false),
                    _ => new CleanupItemResult(item, false, false, "Unsupported item kind."),
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                result = new CleanupItemResult(item, false, false, ex.Message);
            }

            results.Add(result);
            await _log.AppendAsync(new OperationLogEntry(DateTimeOffset.Now, KindOf(item.Kind), item.Target,
                result.Succeeded ? OperationResult.Succeeded : OperationResult.Failed, result.Error), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Cleanup {Kind} {Target}: {Outcome} {Error}", item.Kind, item.Target, result.Succeeded ? "ok" : "failed", result.Error);
        }

        return new CleanupResult(results);
    }

    private CleanupItemResult RemoveDirectory(CleanupPlan plan, CleanupCandidate item)
    {
        var path = WindowsPath.Normalize(item.Target);
        var protection = _protectedPaths.Check(path);
        if (protection.IsProtected)
        {
            return new CleanupItemResult(item, false, false, $"Protected path ({protection.Kind}).");
        }

        // The entity's process list dates from the scan; re-check which of those processes are still alive right now.
        var live = plan.Application.Processes.Where(IsStillRunning).ToArray();
        var verdict = _guard.Evaluate(new DeletionRequest
        {
            Application = plan.Application with { PreferredUninstallMethod = Core.Models.UninstallMethod.None, Processes = live },
            TargetPaths = [path],
            UserConfirmed = plan.UserConfirmed,
            ServicesHandled = true,
        });
        if (verdict.HardBlockers.Count > 0)
        {
            return new CleanupItemResult(item, false, false, "Blocked: " + string.Join(", ", verdict.HardBlockers.Select(b => b.Kind).Distinct()));
        }

        if (!Directory.Exists(path))
        {
            return new CleanupItemResult(item, true, false, null);
        }

        if (plan.UseRecycleBin)
        {
            // §24: no silent fallback to permanent deletion; the user must untick the recycle bin and acknowledge it explicitly.
            if (!RecycleBin.CanRecycle(path, out var why))
            {
                return new CleanupItemResult(item, false, false, "Cannot be moved to the recycle bin (permanent deletion required): " + why);
            }

            var error = RecycleBin.Send(path);
            return error is null
                ? new CleanupItemResult(item, true, false, null)
                : new CleanupItemResult(item, false, false, "Could not move to the recycle bin: " + error);
        }

        Directory.Delete(path, recursive: true);
        return new CleanupItemResult(item, true, true, null);
    }

    /// <summary>True when the scanned process still exists and, when readable, still runs the same image.</summary>
    private static bool IsStillRunning(Core.Models.ProcessRecord record)
    {
        try
        {
            using var process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                var image = process.MainModule?.FileName;
                return image is null || record.ExecutablePath is null || WindowsPath.AreEqual(image, record.ExecutablePath);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Access denied to the image path: the PID is alive; keep the conservative answer.
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private CleanupItemResult RemoveFile(CleanupPlan plan, CleanupCandidate item)
    {
        var path = WindowsPath.Normalize(item.Target);
        // Only entries inside a Startup folder are ever removed this way (§7.7); anything else is refused.
        if (!path.Contains(@"\Start Menu\Programs\Startup", StringComparison.OrdinalIgnoreCase) || _protectedPaths.IsProtected(path))
        {
            return new CleanupItemResult(item, false, false, "Not a Startup folder entry.");
        }

        if (!File.Exists(path))
        {
            return new CleanupItemResult(item, true, false, null);
        }

        if (plan.UseRecycleBin)
        {
            if (!RecycleBin.CanRecycle(path, out var why))
            {
                return new CleanupItemResult(item, false, false, "Cannot be moved to the recycle bin (permanent deletion required): " + why);
            }

            var error = RecycleBin.Send(path);
            return error is null ? new CleanupItemResult(item, true, false, null) : new CleanupItemResult(item, false, false, "Could not move to the recycle bin: " + error);
        }

        File.Delete(path);
        return new CleanupItemResult(item, true, true, null);
    }

    private static async Task<CleanupItemResult> RemoveRegistryValueAsync(CleanupCandidate item, CancellationToken cancellationToken)
    {
        var separator = item.Target.LastIndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            return new CleanupItemResult(item, false, false, "Malformed registry value target.");
        }

        var keyPath = item.Target[..separator];
        var valueName = item.Target[(separator + 2)..];
        var (hive, relative) = SplitHive(keyPath);

        if (hive == RegistryHive.CurrentUser)
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(relative, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            return new CleanupItemResult(item, true, true, null);
        }

        // HKLM needs elevation: reg.exe prompts via UAC (§28).
        var exit = await RunElevatedAsync("reg.exe", $"delete \"{keyPath}\" /v \"{valueName}\" /f", cancellationToken).ConfigureAwait(false);
        return exit == 0 ? new CleanupItemResult(item, true, true, null) : new CleanupItemResult(item, false, false, $"reg.exe exited with code {exit}.");
    }

    private static async Task<CleanupItemResult> RemoveRegistryKeyAsync(CleanupCandidate item, CancellationToken cancellationToken)
    {
        var (hive, relative) = SplitHive(item.Target);
        if (relative.Length == 0 || !relative.Contains('\\', StringComparison.Ordinal))
        {
            return new CleanupItemResult(item, false, false, "Refusing to delete a top-level registry key.");
        }

        if (hive == RegistryHive.CurrentUser)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(relative, throwOnMissingSubKey: false);
            return new CleanupItemResult(item, true, true, null);
        }

        var exit = await RunElevatedAsync("reg.exe", $"delete \"{item.Target}\" /f", cancellationToken).ConfigureAwait(false);
        return exit == 0 ? new CleanupItemResult(item, true, true, null) : new CleanupItemResult(item, false, false, $"reg.exe exited with code {exit}.");
    }

    private static async Task<CleanupItemResult> RemoveScheduledTaskAsync(CleanupCandidate item, CancellationToken cancellationToken)
    {
        try
        {
            using var service = new Microsoft.Win32.TaskScheduler.TaskService();
            var folderPath = WindowsPath.GetDirectoryName(item.Target);
            var folder = service.GetFolder(string.IsNullOrEmpty(folderPath) ? "\\" : folderPath);
            if (folder is null)
            {
                return new CleanupItemResult(item, true, true, null); // the folder, and therefore the task, is already gone
            }

            folder.DeleteTask(WindowsPath.GetFileName(item.Target), exceptionOnNotExists: false);
            return new CleanupItemResult(item, true, true, null);
        }
        catch (UnauthorizedAccessException)
        {
            var exit = await RunElevatedAsync("schtasks.exe", $"/Delete /TN \"{item.Target.TrimStart('\\')}\" /F", cancellationToken).ConfigureAwait(false);
            return exit == 0 ? new CleanupItemResult(item, true, true, null) : new CleanupItemResult(item, false, false, $"schtasks.exe exited with code {exit}.");
        }
    }

    private static async Task<CleanupItemResult> RemoveServiceAsync(CleanupCandidate item, CancellationToken cancellationToken)
    {
        var stop = await RunElevatedAsync("sc.exe", $"stop \"{item.Target}\"", cancellationToken).ConfigureAwait(false);
        // 1062 = service not started; that is fine.
        if (stop != 0 && stop != 1062)
        {
            _ = stop;
        }

        var exit = await RunElevatedAsync("sc.exe", $"delete \"{item.Target}\"", cancellationToken).ConfigureAwait(false);
        return exit == 0 ? new CleanupItemResult(item, true, true, null) : new CleanupItemResult(item, false, false, $"sc.exe exited with code {exit}.");
    }

    /// <summary>Runs a system tool with the <c>runas</c> verb; the UAC prompt is the per-operation elevation of §28. Returns the exit code, or -1 when refused.</summary>
    private static async Task<int> RunElevatedAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user declined the UAC prompt.
            return 1223;
        }
    }

    private static (RegistryHive Hive, string Relative) SplitHive(string keyPath)
    {
        var idx = keyPath.IndexOf('\\', StringComparison.Ordinal);
        var root = idx < 0 ? keyPath : keyPath[..idx];
        var relative = idx < 0 ? string.Empty : keyPath[(idx + 1)..];
        var hive = root.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_USERS" or "HKU" => RegistryHive.Users,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
            _ => RegistryHive.CurrentUser,
        };
        return (hive, relative);
    }

    private static OperationKind KindOf(CleanupItemKind kind) => kind switch
    {
        CleanupItemKind.Directory or CleanupItemKind.StartupFolderItem => OperationKind.DeleteDirectory,
        CleanupItemKind.StartupRegistryValue => OperationKind.DisableStartupItem,
        CleanupItemKind.ScheduledTask => OperationKind.DeleteScheduledTask,
        CleanupItemKind.Service => OperationKind.DeleteService,
        CleanupItemKind.RegistryKey => OperationKind.DeleteRegistryKey,
        _ => OperationKind.DeleteDirectory,
    };
}
