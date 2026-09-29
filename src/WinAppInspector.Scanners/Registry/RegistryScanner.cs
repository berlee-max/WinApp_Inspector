using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Registry;

/// <summary>Reads the three Uninstall keys (§7.1). Runs with normal user rights; HKLM is read-only.</summary>
public sealed class RegistryScanner : IScanner<RegistryUninstallEntry>
{
    private readonly ILogger<RegistryScanner> _logger;

    public RegistryScanner(ILogger<RegistryScanner> logger)
    {
        _logger = logger;
    }

    public string Name => nameof(RegistryScanner);

    public Task<ScanResult<RegistryUninstallEntry>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() => Scan(progress, cancellationToken), cancellationToken);
    }

    private ScanResult<RegistryUninstallEntry> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var items = new List<RegistryUninstallEntry>();
        var errors = new List<ScanError>();

        foreach (var (scope, _, relativePath) in RegistryUninstallKeys.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = RegistryUninstallKeys.FullPath(scope);
            progress?.Report(new ScanProgress(ScanStages.Registry, fullPath));

            RegistryKey? baseKey = null;
            RegistryKey? uninstallKey = null;
            try
            {
                baseKey = scope == RegistryScope.CurrentUser
                    ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                    : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                uninstallKey = baseKey.OpenSubKey(relativePath, writable: false);
                if (uninstallKey is null)
                {
                    // A missing WOW6432Node is normal on 32-bit Windows; anything else is worth logging.
                    _logger.LogDebug("Uninstall key {Key} does not exist", fullPath);
                    continue;
                }

                var subKeyNames = uninstallKey.GetSubKeyNames();
                for (var i = 0; i < subKeyNames.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var keyName = subKeyNames[i];
                    progress?.Report(new ScanProgress(ScanStages.Registry, keyName, i + 1, subKeyNames.Length));

                    try
                    {
                        using var subKey = uninstallKey.OpenSubKey(keyName, writable: false);
                        if (subKey is null)
                        {
                            continue;
                        }

                        items.Add(RegistryUninstallEntryParser.Parse(scope, keyName, ReadValues(subKey)));
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                    {
                        errors.Add(new ScanError(Name, fullPath + "\\" + keyName, ex.Message, ex));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                errors.Add(new ScanError(Name, fullPath, ex.Message, ex));
            }
            finally
            {
                uninstallKey?.Dispose();
                baseKey?.Dispose();
            }
        }

        _logger.LogInformation("Registry scan found {Count} uninstall entries with {Errors} errors", items.Count, errors.Count);
        return new ScanResult<RegistryUninstallEntry>(items, errors);
    }

    /// <summary>Reads every value of a key. Expandable strings are expanded by the registry API.</summary>
    internal static Dictionary<string, object?> ReadValues(RegistryKey key)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var valueName in key.GetValueNames())
        {
            try
            {
                values[valueName] = key.GetValue(valueName);
            }
            catch (IOException)
            {
                // A corrupt value (§34 注册表项损坏) must not hide the rest of the entry.
                values[valueName] = null;
            }
        }

        return values;
    }
}
