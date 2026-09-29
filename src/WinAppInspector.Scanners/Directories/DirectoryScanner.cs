using Microsoft.Extensions.Logging;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Directories;

/// <summary>
/// Discovers candidate application directories under the §6 roots. Discovery is shallow by design (§32):
/// one level of children per root (plus <c>AppData\Local\Programs\*</c>), executables looked up at most
/// <see cref="ScanOptions.ExecutableSearchDepth"/> levels deep, no size computation.
/// </summary>
public sealed class DirectoryScanner : IScanner<AppDirectory>
{
    private static readonly EnumerationOptions ChildOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    private readonly WindowsKnownFolders _folders;
    private readonly ProtectedPathRule _protectedPaths;
    private readonly ScanOptions _options;
    private readonly ILogger<DirectoryScanner> _logger;

    public DirectoryScanner(WindowsKnownFolders folders, ProtectedPathRule protectedPaths, ScanOptions options, ILogger<DirectoryScanner> logger)
    {
        _folders = folders;
        _protectedPaths = protectedPaths;
        _options = options;
        _logger = logger;
    }

    public string Name => nameof(DirectoryScanner);

    public Task<ScanResult<AppDirectory>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    /// <summary>Inspects one arbitrary directory (§18 "analyse this folder").</summary>
    public AppDirectory Inspect(string path, CancellationToken cancellationToken = default)
    {
        var normalized = WindowsPath.Normalize(path);
        return Describe(new DirectoryInfo(normalized), _folders.RootOf(normalized), cancellationToken);
    }

    private ScanResult<AppDirectory> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var items = new List<AppDirectory>();
        var errors = new List<ScanError>();

        foreach (var root in _options.DirectoryRoots.OrderBy(r => (int)r))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootPath = _folders.PathOf(root);
            progress?.Report(new ScanProgress(ScanStages.Directories, rootPath));

            if (!Directory.Exists(rootPath))
            {
                _logger.LogDebug("Scan root {Root} does not exist", rootPath);
                continue;
            }

            var candidates = new List<DirectoryInfo>();
            try
            {
                candidates.AddRange(new DirectoryInfo(rootPath).EnumerateDirectories("*", ChildOptions));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                errors.Add(new ScanError(Name, rootPath, ex.Message, ex));
                continue;
            }

            // User-level installs live one level deeper (§9.4).
            if (root == ScanRoot.LocalAppData)
            {
                var programs = candidates.FirstOrDefault(d => d.Name.Equals("Programs", StringComparison.OrdinalIgnoreCase));
                if (programs is not null)
                {
                    candidates.Remove(programs);
                    try
                    {
                        candidates.AddRange(programs.EnumerateDirectories("*", ChildOptions));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        errors.Add(new ScanError(Name, programs.FullName, ex.Message, ex));
                    }
                }
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = candidates[i];
                progress?.Report(new ScanProgress(ScanStages.Directories, dir.FullName, i + 1, candidates.Count));

                // WindowsApps is covered by the AppX scanner and is off-limits anyway (§11).
                if (_protectedPaths.Check(dir.FullName).Kind == PathProtectionKind.WindowsApps)
                {
                    continue;
                }

                try
                {
                    items.Add(Describe(dir, root, cancellationToken));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    errors.Add(new ScanError(Name, dir.FullName, ex.Message, ex));
                }
            }
        }

        _logger.LogInformation("Directory scan discovered {Count} directories with {Errors} errors", items.Count, errors.Count);
        return new ScanResult<AppDirectory>(items, errors);
    }

    private AppDirectory Describe(DirectoryInfo dir, ScanRoot root, CancellationToken cancellationToken)
    {
        var executables = new List<string>();
        CollectExecutables(dir, _options.ExecutableSearchDepth, executables, cancellationToken);

        var childNames = new List<string>();
        int? fileCount = null;
        try
        {
            childNames.AddRange(dir.EnumerateDirectories("*", ChildOptions).Select(d => d.Name));
            fileCount = dir.EnumerateFiles("*", ChildOptions).Count();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogDebug(ex, "Could not list {Directory}", dir.FullName);
        }

        return new AppDirectory
        {
            Path = WindowsPath.Normalize(dir.FullName),
            Root = root,
            Role = DirectoryRole.Unknown,
            LastWriteTime = SafeTime(() => dir.LastWriteTimeUtc),
            CreationTime = SafeTime(() => dir.CreationTimeUtc),
            ExecutablePaths = executables,
            ChildDirectoryNames = childNames,
            TopLevelFileCount = fileCount,
        };
    }

    private void CollectExecutables(DirectoryInfo dir, int depth, List<string> sink, CancellationToken cancellationToken)
    {
        if (depth <= 0 || sink.Count >= _options.MaxExecutablesPerDirectory)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var file in dir.EnumerateFiles("*.exe", ChildOptions))
        {
            if (sink.Count >= _options.MaxExecutablesPerDirectory)
            {
                return;
            }

            sink.Add(WindowsPath.Normalize(file.FullName));
        }

        if (depth == 1)
        {
            return;
        }

        foreach (var child in dir.EnumerateDirectories("*", ChildOptions))
        {
            CollectExecutables(child, depth - 1, sink, cancellationToken);
            if (sink.Count >= _options.MaxExecutablesPerDirectory)
            {
                return;
            }
        }
    }

    private static DateTimeOffset? SafeTime(Func<DateTime> getter)
    {
        try
        {
            var t = getter();
            return t.Year < 1980 ? null : new DateTimeOffset(t, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
