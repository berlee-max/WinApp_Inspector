using System.Globalization;
using System.Management;
using Microsoft.Extensions.Logging;

namespace WinAppInspector.Actions.Uninstall;

/// <summary>
/// Many uninstallers (NSIS <c>Au_.exe</c>, Inno Setup <c>_iuXXXX.tmp</c>, custom bootstrappers) copy themselves to
/// %TEMP%, start the copy and exit at once with code 0. Waiting for the process we started therefore returns while
/// the real uninstall is still running, and a residue scan taken then reports the whole program as residue. This
/// watcher follows the process tree: every process whose parent is the launched uninstaller (or one of its
/// descendants) and that was created after the launch counts, so the wait ends when the last of them has exited.
/// </summary>
public static class DetachedUninstallerWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Waits until no descendant of <paramref name="rootProcessId"/> is alive, or <paramref name="timeout"/> passes. Returns the number of
    /// descendants that were observed, so the caller can log that a detached copy was waited for.</summary>
    public static async Task<int> WaitForDescendantsAsync(int rootProcessId, DateTime launchedAtUtc, TimeSpan timeout, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var deadline = DateTime.UtcNow + timeout;
        var family = new HashSet<int> { rootProcessId };
        var observed = new HashSet<int>();
        var quietChecks = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<(int Pid, string Name)> alive;
            try
            {
                alive = await Task.Run(() => FindAliveDescendants(family, launchedAtUtc), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Without WMI the tree cannot be followed; the caller falls back to the exit code alone (§34: logged, not hidden).
                logger.LogWarning(ex, "Process tree of the uninstaller could not be read; not waiting for detached copies");
                return observed.Count;
            }

            if (alive.Count == 0)
            {
                // The copy may still be starting during the first moments after the launcher exits: look twice before giving up.
                if (++quietChecks >= 2)
                {
                    return observed.Count;
                }
            }
            else
            {
                quietChecks = 0;
                foreach (var (pid, name) in alive)
                {
                    if (observed.Add(pid))
                    {
                        logger.LogInformation("Uninstaller continues in detached process {Name} (PID {Pid}); waiting", name, pid);
                    }
                }
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        logger.LogWarning("Detached uninstaller processes were still running when the wait timed out");
        return observed.Count;
    }

    /// <summary>One WMI pass: adds every descendant created after the launch to <paramref name="family"/> and returns the ones alive now.</summary>
    private static List<(int Pid, string Name)> FindAliveDescendants(HashSet<int> family, DateTime launchedAtUtc)
    {
        var processes = new List<(int Pid, int Parent, string Name, DateTime CreatedUtc)>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, CreationDate FROM Win32_Process");
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                var pid = Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture);
                var parent = item["ParentProcessId"] is { } pp ? Convert.ToInt32(pp, CultureInfo.InvariantCulture) : -1;
                var name = item["Name"] as string ?? string.Empty;
                var created = item["CreationDate"] is string date ? ManagementDateTimeConverter.ToDateTime(date).ToUniversalTime() : DateTime.MinValue;
                processes.Add((pid, parent, name, created));
            }
        }

        // Parent ids survive the parent's exit, so a chain launcher → copy → helper is followed even after the launcher is gone.
        // The creation-time check guards against a recycled process id that happens to match a finished ancestor.
        var alive = new List<(int Pid, string Name)>();
        bool grew;
        do
        {
            grew = false;
            foreach (var (pid, parent, name, created) in processes)
            {
                if (!family.Contains(pid) && family.Contains(parent) && created >= launchedAtUtc.AddSeconds(-5))
                {
                    family.Add(pid);
                    grew = true;
                }
            }
        }
        while (grew);

        foreach (var (pid, _, name, created) in processes)
        {
            if (family.Contains(pid) && created >= launchedAtUtc.AddSeconds(-5))
            {
                alive.Add((pid, name));
            }
        }

        return alive;
    }
}
