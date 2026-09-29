using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Actions.Residue;

/// <summary>Finds what an application left behind (§21) or what a manual removal would touch (§22).</summary>
public interface IResidueScanner
{
    Task<ResidueReport> ScanAsync(ApplicationEntity application, CancellationToken cancellationToken);
}

/// <summary>
/// Targeted rescan after an uninstall: which of the application's known directories still exist (with sizes),
/// which registry entries survive, and which startup items / services / scheduled tasks still point into its folders.
/// Also probes the conventional per-vendor configuration keys under HKCU\Software and HKLM\Software.
/// </summary>
public sealed class ResidueScanner : IResidueScanner
{
    private readonly IRegistryKeyProbe _registry;
    private readonly IDirectorySizeCalculator _sizes;
    private readonly IScanner<StartupItemRecord> _startup;
    private readonly IScanner<ServiceRecord> _services;
    private readonly IScanner<ScheduledTaskRecord> _tasks;
    private readonly ILogger<ResidueScanner> _logger;

    public ResidueScanner(
        IRegistryKeyProbe registry,
        IDirectorySizeCalculator sizes,
        IScanner<StartupItemRecord> startup,
        IScanner<ServiceRecord> services,
        IScanner<ScheduledTaskRecord> tasks,
        ILogger<ResidueScanner> logger)
    {
        _registry = registry;
        _sizes = sizes;
        _startup = startup;
        _services = services;
        _tasks = tasks;
        _logger = logger;
    }

    public async Task<ResidueReport> ScanAsync(ApplicationEntity application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var roots = application.Directories.Where(d => d.Role != DirectoryRole.SharedParent).Select(d => d.Path).ToList();
        if (application.InstallLocation is not null && !roots.Any(r => WindowsPath.IsSameOrUnder(application.InstallLocation, r)))
        {
            roots.Add(application.InstallLocation);
        }

        // Directories that still exist, measured now (§21 shows sizes).
        var directories = new List<AppDirectory>();
        foreach (var directory in application.Directories.Where(d => d.Role != DirectoryRole.SharedParent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory.Path))
            {
                continue;
            }

            var size = await _sizes.ComputeAsync(directory.Path, cancellationToken).ConfigureAwait(false);
            directories.Add(directory with { SizeBytes = size.Bytes, FileCount = size.FileCount });
        }

        if (application.InstallLocation is not null && Directory.Exists(application.InstallLocation)
            && !directories.Any(d => WindowsPath.IsSameOrUnder(application.InstallLocation, d.Path)))
        {
            var size = await _sizes.ComputeAsync(application.InstallLocation, cancellationToken).ConfigureAwait(false);
            directories.Add(new AppDirectory { Path = application.InstallLocation, Role = DirectoryRole.Program, SizeBytes = size.Bytes, FileCount = size.FileCount });
        }

        var registryEntries = application.RegistryEntries.Where(e => _registry.Exists(e.KeyPath)).ToList();
        var configKeys = ProbeConfigurationKeys(application);

        var startupTask = _startup.ScanAsync(null, cancellationToken);
        var servicesTask = _services.ScanAsync(null, cancellationToken);
        var tasksTask = _tasks.ScanAsync(null, cancellationToken);
        await Task.WhenAll(startupTask, servicesTask, tasksTask).ConfigureAwait(false);

        var knownStartup = application.StartupItems.Select(s => s.Location + "::" + s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var startup = startupTask.Result.Items
            .Where(s => knownStartup.Contains(s.Location + "::" + s.Name) || PointsInto(s.ExecutablePath, roots))
            .ToList();

        var knownServices = application.Services.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var services = servicesTask.Result.Items
            .Where(s => knownServices.Contains(s.Name) || PointsInto(s.ExecutablePath, roots))
            .ToList();

        var knownTasks = application.ScheduledTasks.Select(t => t.TaskPath + "|" + t.TaskName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tasks = tasksTask.Result.Items
            .Where(t => knownTasks.Contains(t.TaskPath + "|" + t.TaskName) || PointsInto(t.Execute, roots))
            .ToList();

        _logger.LogInformation("Residue scan for {App}: {Dirs} directories, {Reg} registry entries, {Cfg} config keys, {Startup} startup, {Svc} services, {Tasks} tasks",
            application.Name, directories.Count, registryEntries.Count, configKeys.Count, startup.Count, services.Count, tasks.Count);

        return new ResidueReport(directories, registryEntries, configKeys, startup, services, tasks);
    }

    /// <summary>HKCU\Software\&lt;Publisher&gt;\&lt;Name&gt; and friends. Only exact, existing keys are reported; vendor-level keys are skipped when the vendor has other products.</summary>
    private List<string> ProbeConfigurationKeys(ApplicationEntity application)
    {
        var keys = new List<string>();
        var names = new[] { application.Name }.Concat(application.Executables.Select(e => e.ProductName)).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var publishers = new[] { application.Publisher }.Concat(application.Executables.Select(e => e.CompanyName)).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var hive in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
        {
            foreach (var publisher in publishers)
            {
                foreach (var name in names)
                {
                    Probe($@"{hive}\Software\{publisher}\{name}");
                }
            }

            foreach (var name in names)
            {
                Probe($@"{hive}\Software\{name}");
            }
        }

        return keys;

        void Probe(string key)
        {
            if (key.Contains('/', StringComparison.Ordinal) || keys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            if (_registry.Exists(key))
            {
                keys.Add(key);
            }
        }
    }

    private static bool PointsInto(string? path, List<string> roots) =>
        path is not null && roots.Any(root => WindowsPath.IsSameOrUnder(path, root));
}
