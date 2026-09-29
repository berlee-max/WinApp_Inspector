using Microsoft.Extensions.Logging;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Rules;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Directories;

/// <summary>
/// Discovers candidate application directories under the §6 roots and the user's custom directories (§26.1).
/// Discovery is shallow by design (§32): one level of children per root (plus <c>AppData\Local\Programs\*</c>), executables looked up at most
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
    private readonly IScanOptionsProvider _options;
    private readonly ILogger<DirectoryScanner> _logger;

    public DirectoryScanner(WindowsKnownFolders folders, ProtectedPathRule protectedPaths, IScanOptionsProvider options, ILogger<DirectoryScanner> logger)
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

        var roots = _options.Current.DirectoryRoots.OrderBy(r => (int)r).Select(r => (Root: r, Path: _folders.PathOf(r)))
            .Concat(_options.Current.CustomDirectories.Select(d => (Root: ScanRoot.Custom, Path: WindowsPath.Normalize(d))));

        foreach (var (root, rootPath) in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(ScanStages.Directories, rootPath));

            if (root == ScanRoot.Custom)
            {
                // A user-added folder must not be a system location, a drive root or a user profile (§11): its children would be
                // Desktop, Downloads, other accounts... which must never be offered as programs. Its own path is never an application.
                var protection = _protectedPaths.Check(rootPath);
                if (protection.Kind is not (PathProtectionKind.None or PathProtectionKind.ScanRoot))
                {
                    errors.Add(new ScanError(Name, rootPath, $"Custom scan directory skipped: protected location ({protection.Kind}).",
                        Code: ScanErrorCodes.CustomDirectoryProtected, Detail: protection.Kind.ToString()));
                    continue;
                }
            }

            if (!Directory.Exists(rootPath))
            {
                if (root == ScanRoot.Custom)
                {
                    errors.Add(new ScanError(Name, rootPath, "Custom scan directory does not exist.", Code: ScanErrorCodes.CustomDirectoryMissing));
                }
                else
                {
                    _logger.LogDebug("Scan root {Root} does not exist", rootPath);
                }

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

            if (root == ScanRoot.Custom && HasTopLevelExecutable(rootPath))
            {
                // The folder holds a program directly, so it is one application rather than a collection of them; its data
                // sub-folders must not be judged on their own. Tell the user to analyse it or add its parent instead (§18).
                errors.Add(new ScanError(Name, rootPath, "Custom scan directory contains executables itself; analyse it as one folder or add its parent.",
                    Code: ScanErrorCodes.CustomDirectoryLooksLikeProgram));
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

                // WindowsApps is covered by the AppX scanner and is off-limits anyway (§11). Under a user-added root
                // (which could be a drive root's neighbour) no protected location ever becomes an application directory.
                var childProtection = _protectedPaths.Check(dir.FullName).Kind;
                if (childProtection == PathProtectionKind.WindowsApps || (root == ScanRoot.Custom && childProtection != PathProtectionKind.None))
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
        CollectExecutables(dir, _options.Current.ExecutableSearchDepth, executables, cancellationToken);

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
        if (depth <= 0 || sink.Count >= _options.Current.MaxExecutablesPerDirectory)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var file in dir.EnumerateFiles("*.exe", ChildOptions))
        {
            if (sink.Count >= _options.Current.MaxExecutablesPerDirectory)
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
            if (sink.Count >= _options.Current.MaxExecutablesPerDirectory)
            {
                return;
            }
        }
    }

    private static bool HasTopLevelExecutable(string path)
    {
        try
        {
            return new DirectoryInfo(path).EnumerateFiles("*.exe", ChildOptions).Any();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
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
