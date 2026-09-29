using System.IO;
using Microsoft.Extensions.Logging;
using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;
using WinAppInspector.Scanners.Directories;

namespace WinAppInspector.UI.Services;

/// <summary>Result of one full scan: the raw snapshot and the resolved applications.</summary>
public sealed record ScanOutcome(ScanSnapshot Snapshot, ResolutionResult Resolution);

/// <summary>Callback for the background size pass (§32).</summary>
public delegate void DirectorySizeReported(string applicationId, string directoryPath, DirectorySize size);

public interface IScanService
{
    /// <summary>Runs every enabled scanner, reads executable metadata / signatures, and resolves applications (§16).</summary>
    Task<ScanOutcome> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Second stage: computes directory sizes for the given applications in the background (§32).</summary>
    Task ComputeSizesAsync(IReadOnlyList<ApplicationEntity> applications, DirectorySizeReported onSize, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);

    /// <summary>§18: analyses one directory in the context of the last scan.</summary>
    Task<ApplicationEntity?> AnalyzeDirectoryAsync(string path, ScanSnapshot? context, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates the scanners (§7) into a <see cref="ScanSnapshot"/> and hands it to the attribution engine.
/// Scanners run concurrently; executable metadata and signatures are read with bounded parallelism.
/// Errors from individual items are collected, never swallowed (§34).
/// </summary>
public sealed class ScanService : IScanService
{
    private readonly IScanner<RegistryUninstallEntry> _registry;
    private readonly IScanner<AppxPackageRecord> _appx;
    private readonly DirectoryScanner _directories;
    private readonly IScanner<ProcessRecord> _processes;
    private readonly IScanner<ServiceRecord> _services;
    private readonly IScanner<StartupItemRecord> _startup;
    private readonly IScanner<ScheduledTaskRecord> _tasks;
    private readonly IScanner<ShortcutRecord> _shortcuts;
    private readonly IExecutableMetadataReader _metadata;
    private readonly ISignatureReader _signatures;
    private readonly IDirectorySizeCalculator _sizes;
    private readonly IApplicationResolver _resolver;
    private readonly IScanOptionsProvider _options;
    private readonly WindowsKnownFolders _folders;
    private readonly ILogger<ScanService> _logger;

    public ScanService(
        IScanner<RegistryUninstallEntry> registry,
        IScanner<AppxPackageRecord> appx,
        DirectoryScanner directories,
        IScanner<ProcessRecord> processes,
        IScanner<ServiceRecord> services,
        IScanner<StartupItemRecord> startup,
        IScanner<ScheduledTaskRecord> tasks,
        IScanner<ShortcutRecord> shortcuts,
        IExecutableMetadataReader metadata,
        ISignatureReader signatures,
        IDirectorySizeCalculator sizes,
        IApplicationResolver resolver,
        IScanOptionsProvider options,
        WindowsKnownFolders folders,
        ILogger<ScanService> logger)
    {
        _registry = registry;
        _appx = appx;
        _directories = directories;
        _processes = processes;
        _services = services;
        _startup = startup;
        _tasks = tasks;
        _shortcuts = shortcuts;
        _metadata = metadata;
        _signatures = signatures;
        _sizes = sizes;
        _resolver = resolver;
        _options = options;
        _folders = folders;
        _logger = logger;
    }

    public async Task<ScanOutcome> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.Now;
        var errors = new List<ScanError>();

        var registryTask = _registry.ScanAsync(progress, cancellationToken);
        var appxTask = _appx.ScanAsync(progress, cancellationToken);
        var directoriesTask = _directories.ScanAsync(progress, cancellationToken);
        var processesTask = _options.Current.ScanProcesses ? _processes.ScanAsync(progress, cancellationToken) : Task.FromResult(ScanResult.Empty<ProcessRecord>());
        var servicesTask = _options.Current.ScanServices ? _services.ScanAsync(progress, cancellationToken) : Task.FromResult(ScanResult.Empty<ServiceRecord>());
        var startupTask = _options.Current.ScanStartupItems ? _startup.ScanAsync(progress, cancellationToken) : Task.FromResult(ScanResult.Empty<StartupItemRecord>());
        var tasksTask = _options.Current.ScanScheduledTasks ? _tasks.ScanAsync(progress, cancellationToken) : Task.FromResult(ScanResult.Empty<ScheduledTaskRecord>());
        var shortcutsTask = _shortcuts.ScanAsync(progress, cancellationToken);

        await Task.WhenAll(registryTask, appxTask, directoriesTask, processesTask, servicesTask, startupTask, tasksTask, shortcutsTask).ConfigureAwait(false);

        var registry = Collect(registryTask.Result, errors);
        var appx = Collect(appxTask.Result, errors);
        var directories = Collect(directoriesTask.Result, errors);
        var processes = Collect(processesTask.Result, errors);
        var services = Collect(servicesTask.Result, errors);
        var startup = Collect(startupTask.Result, errors);
        var tasks = Collect(tasksTask.Result, errors);
        var shortcuts = Collect(shortcutsTask.Result, errors);

        var executablePaths = CollectExecutablePaths(registry, directories, processes, services, startup, tasks, shortcuts);
        var (metadata, signatures) = await ReadExecutablesAsync(executablePaths, errors, progress, cancellationToken).ConfigureAwait(false);

        var snapshot = new ScanSnapshot
        {
            ScanTime = started,
            Folders = _folders,
            RegistryEntries = registry,
            Packages = appx,
            Directories = directories,
            Processes = processes,
            Services = services,
            StartupItems = startup,
            ScheduledTasks = tasks,
            Shortcuts = shortcuts,
            Executables = metadata,
            Signatures = signatures,
            Errors = errors,
        };

        progress?.Report(new ScanProgress(ScanStages.Resolving));
        var resolution = await Task.Run(() => _resolver.Resolve(snapshot), cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Scan finished in {Elapsed:F1}s with {Apps} applications and {Errors} errors",
            (DateTimeOffset.Now - started).TotalSeconds, resolution.Applications.Count, errors.Count);

        return new ScanOutcome(snapshot, resolution);
    }

    public async Task ComputeSizesAsync(IReadOnlyList<ApplicationEntity> applications, DirectorySizeReported onSize, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(onSize);

        var work = applications
            .SelectMany(a => a.Directories.Where(d => d.Role != DirectoryRole.SharedParent).Select(d => (a.Id, d.Path)))
            .ToList();

        for (var i = 0; i < work.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (id, path) = work[i];
            progress?.Report(new ScanProgress(ScanStages.Sizes, path, i + 1, work.Count));
            var size = await _sizes.ComputeAsync(path, cancellationToken).ConfigureAwait(false);
            onSize(id, path, size);
        }
    }

    public async Task<ApplicationEntity?> AnalyzeDirectoryAsync(string path, ScanSnapshot? context, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = WindowsPath.Normalize(path);

        var directory = await Task.Run(() => _directories.Inspect(normalized, cancellationToken), cancellationToken).ConfigureAwait(false);
        var errors = new List<ScanError>();
        var (metadata, signatures) = await ReadExecutablesAsync(directory.ExecutablePaths, errors, null, cancellationToken).ConfigureAwait(false);

        var baseSnapshot = context ?? new ScanSnapshot { ScanTime = DateTimeOffset.Now, Folders = _folders };
        var mergedMetadata = new Dictionary<string, ExecutableMetadata>(baseSnapshot.Executables, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in metadata) mergedMetadata[k] = v;
        var mergedSignatures = new Dictionary<string, SignatureInfo>(baseSnapshot.Signatures, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in signatures) mergedSignatures[k] = v;

        var snapshot = baseSnapshot with
        {
            Directories = baseSnapshot.Directories.Where(d => !WindowsPath.AreEqual(d.Path, normalized)).Append(directory).ToArray(),
            Executables = mergedMetadata,
            Signatures = mergedSignatures,
        };

        var resolution = await Task.Run(() => _resolver.Resolve(snapshot), cancellationToken).ConfigureAwait(false);
        return resolution.Applications.FirstOrDefault(a => a.Directories.Any(d => WindowsPath.AreEqual(d.Path, normalized)));
    }

    private static IReadOnlyList<T> Collect<T>(ScanResult<T> result, List<ScanError> errors)
    {
        errors.AddRange(result.Errors);
        return result.Items;
    }

    private static HashSet<string> CollectExecutablePaths(
        IReadOnlyList<RegistryUninstallEntry> registry,
        IReadOnlyList<AppDirectory> directories,
        IReadOnlyList<ProcessRecord> processes,
        IReadOnlyList<ServiceRecord> services,
        IReadOnlyList<StartupItemRecord> startup,
        IReadOnlyList<ScheduledTaskRecord> tasks,
        IReadOnlyList<ShortcutRecord> shortcuts)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in directories) paths.UnionWith(d.ExecutablePaths);
        foreach (var p in processes) Add(paths, p.ExecutablePath);
        foreach (var s in services) Add(paths, s.ExecutablePath);
        foreach (var s in startup) Add(paths, s.ExecutablePath);
        foreach (var t in tasks) Add(paths, t.Execute);
        foreach (var s in shortcuts) Add(paths, s.TargetPath);
        foreach (var r in registry)
        {
            Add(paths, CommandLine.ExtractExecutable(r.DisplayIcon?.Split(',')[0]));
            Add(paths, CommandLine.ExtractExecutable(r.UninstallString));
        }

        return paths;

        static void Add(HashSet<string> set, string? path)
        {
            if (path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && path.Length > 3 && path[1] == ':')
            {
                set.Add(WindowsPath.Normalize(path));
            }
        }
    }

    private async Task<(Dictionary<string, ExecutableMetadata>, Dictionary<string, SignatureInfo>)> ReadExecutablesAsync(
        IEnumerable<string> paths, List<ScanError> errors, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var list = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var metadata = new Dictionary<string, ExecutableMetadata>(StringComparer.OrdinalIgnoreCase);
        var signatures = new Dictionary<string, SignatureInfo>(StringComparer.OrdinalIgnoreCase);
        var errorSink = new System.Collections.Concurrent.ConcurrentBag<ScanError>();
        var done = 0;

        await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, (path, ct) =>
        {
            var count = Interlocked.Increment(ref done);
            if ((count & 0x1F) == 0 || count == list.Count)
            {
                progress?.Report(new ScanProgress(_options.Current.ReadSignatures ? ScanStages.Signatures : ScanStages.Executables, path, count, list.Count));
            }

            if (!File.Exists(path))
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                var m = _metadata.Read(path);
                lock (metadata)
                {
                    metadata[path] = m;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errorSink.Add(new ScanError("ExeMetadataReader", path, ex.Message, ex));
            }

            if (_options.Current.ReadSignatures)
            {
                var s = _signatures.Read(path);
                lock (signatures)
                {
                    signatures[path] = s;
                }
            }

            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        errors.AddRange(errorSink);
        return (metadata, signatures);
    }
}
