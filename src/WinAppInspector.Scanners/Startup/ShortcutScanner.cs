using Microsoft.Extensions.Logging;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Startup;

/// <summary>Resolves Start Menu and Desktop shortcuts (§7.9) to build "directory → application name" hints.</summary>
public sealed class ShortcutScanner : IScanner<ShortcutRecord>
{
    private static readonly EnumerationOptions Recursive = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MaxRecursionDepth = 6,
    };

    private readonly WindowsKnownFolders _folders;
    private readonly IShortcutResolver _resolver;
    private readonly ILogger<ShortcutScanner> _logger;

    public ShortcutScanner(WindowsKnownFolders folders, IShortcutResolver resolver, ILogger<ShortcutScanner> logger)
    {
        _folders = folders;
        _resolver = resolver;
        _logger = logger;
    }

    public string Name => nameof(ShortcutScanner);

    public Task<ScanResult<ShortcutRecord>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult<ShortcutRecord> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var items = new List<ShortcutRecord>();
        var errors = new List<ScanError>();
        var roots = new[]
        {
            WindowsPath.Combine(_folders.ProgramData, @"Microsoft\Windows\Start Menu\Programs"),
            WindowsPath.Combine(_folders.RoamingAppData, @"Microsoft\Windows\Start Menu\Programs"),
            WindowsPath.Combine(_folders.UserProfile, "Desktop"),
            WindowsPath.Combine(_folders.UsersRoot, "Public", "Desktop"),
        };

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(ScanStages.Startup, root));
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", Recursive))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = _resolver.Resolve(file);
                    if (target is null || !target.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(new ShortcutRecord
                    {
                        ShortcutPath = WindowsPath.Normalize(file),
                        Name = Path.GetFileNameWithoutExtension(file),
                        TargetPath = WindowsPath.Normalize(target.TargetPath),
                        Arguments = target.Arguments,
                        WorkingDirectory = target.WorkingDirectory,
                    });
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                errors.Add(new ScanError(Name, root, ex.Message, ex));
            }
        }

        _logger.LogInformation("Shortcut scan resolved {Count} shortcuts", items.Count);
        return new ScanResult<ShortcutRecord>(items, errors);
    }
}
