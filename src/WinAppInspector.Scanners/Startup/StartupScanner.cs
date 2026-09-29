using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Startup;

/// <summary>Reads the Run / RunOnce keys and the two Startup folders (§7.7), plus Explorer's enabled / disabled state.</summary>
public sealed class StartupScanner : IScanner<StartupItemRecord>
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunWow6432Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string StartupApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private readonly WindowsKnownFolders _folders;
    private readonly IShortcutResolver _shortcuts;
    private readonly ILogger<StartupScanner> _logger;

    public StartupScanner(WindowsKnownFolders folders, IShortcutResolver shortcuts, ILogger<StartupScanner> logger)
    {
        _folders = folders;
        _shortcuts = shortcuts;
        _logger = logger;
    }

    public string Name => nameof(StartupScanner);

    public Task<ScanResult<StartupItemRecord>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult<StartupItemRecord> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress(ScanStages.Startup));
        var items = new List<StartupItemRecord>();
        var errors = new List<ScanError>();

        var userApproved = ReadApprovedStates(RegistryHive.CurrentUser, StartupApprovedRun, errors);
        var machineApproved = ReadApprovedStates(RegistryHive.LocalMachine, StartupApprovedRun, errors);
        var machineApproved32 = ReadApprovedStates(RegistryHive.LocalMachine, StartupApprovedRun32, errors);
        var userFolderApproved = ReadApprovedStates(RegistryHive.CurrentUser, StartupApprovedFolder, errors);

        ReadRunKey(RegistryHive.CurrentUser, RunPath, StartupItemKind.RegistryRun, false, userApproved, items, errors, cancellationToken);
        ReadRunKey(RegistryHive.CurrentUser, RunOncePath, StartupItemKind.RegistryRunOnce, false, null, items, errors, cancellationToken);
        ReadRunKey(RegistryHive.LocalMachine, RunPath, StartupItemKind.RegistryRun, true, machineApproved, items, errors, cancellationToken);
        ReadRunKey(RegistryHive.LocalMachine, RunOncePath, StartupItemKind.RegistryRunOnce, true, null, items, errors, cancellationToken);
        ReadRunKey(RegistryHive.LocalMachine, RunWow6432Path, StartupItemKind.RegistryRun, true, machineApproved32, items, errors, cancellationToken);

        ReadStartupFolder(WindowsPath.Combine(_folders.RoamingAppData, @"Microsoft\Windows\Start Menu\Programs\Startup"), false, userFolderApproved, items, errors, cancellationToken);
        ReadStartupFolder(WindowsPath.Combine(_folders.ProgramData, @"Microsoft\Windows\Start Menu\Programs\StartUp"), true, null, items, errors, cancellationToken);

        _logger.LogInformation("Startup scan found {Count} items with {Errors} errors", items.Count, errors.Count);
        return new ScanResult<StartupItemRecord>(items, errors);
    }

    private void ReadRunKey(
        RegistryHive hive,
        string path,
        StartupItemKind kind,
        bool machineWide,
        Dictionary<string, bool>? approved,
        List<StartupItemRecord> items,
        List<ScanError> errors,
        CancellationToken cancellationToken)
    {
        var location = (hive == RegistryHive.CurrentUser ? "HKEY_CURRENT_USER\\" : "HKEY_LOCAL_MACHINE\\") + path;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var command = key.GetValue(valueName) as string;
                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                items.Add(new StartupItemRecord
                {
                    Name = valueName.Length == 0 ? "(Default)" : valueName,
                    Kind = kind,
                    Location = location,
                    IsMachineWide = machineWide,
                    Command = command,
                    ExecutablePath = CommandLine.ResolveCommandExecutable(command, _folders),
                    IsDisabled = approved is not null && approved.TryGetValue(valueName, out var disabled) ? disabled : null,
                });
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            errors.Add(new ScanError(Name, location, ex.Message, ex));
        }
    }

    private void ReadStartupFolder(
        string folder,
        bool machineWide,
        Dictionary<string, bool>? approved,
        List<StartupItemRecord> items,
        List<ScanError> errors,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? target = null;
                string? command = file;
                if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var resolved = _shortcuts.Resolve(file);
                    if (resolved is not null)
                    {
                        target = WindowsPath.Normalize(resolved.TargetPath);
                        command = resolved.Arguments is null ? $"\"{resolved.TargetPath}\"" : $"\"{resolved.TargetPath}\" {resolved.Arguments}";
                    }
                }
                else
                {
                    target = WindowsPath.Normalize(file);
                }

                items.Add(new StartupItemRecord
                {
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    Kind = StartupItemKind.StartupFolder,
                    Location = folder,
                    IsMachineWide = machineWide,
                    Command = command,
                    ExecutablePath = target,
                    IsDisabled = approved is not null && approved.TryGetValue(fileName, out var disabled) ? disabled : null,
                });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            errors.Add(new ScanError(Name, folder, ex.Message, ex));
        }
    }

    /// <summary>StartupApproved values are 12-byte blobs; the first byte is 0x02 (enabled) or 0x03 (disabled).</summary>
    private Dictionary<string, bool>? ReadApprovedStates(RegistryHive hive, string path, List<ScanError> errors)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null)
            {
                return null;
            }

            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is byte[] { Length: > 0 } blob)
                {
                    result[valueName] = blob[0] is 0x03 or 0x01;
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            errors.Add(new ScanError(Name, path, ex.Message, ex));
            return null;
        }
    }
}
