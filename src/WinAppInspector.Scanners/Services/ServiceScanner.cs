using System.Management;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Services;

/// <summary>Lists Windows services via WMI <c>Win32_Service</c> (§7.6) and resolves each image path.</summary>
public sealed class ServiceScanner : IScanner<ServiceRecord>
{
    private readonly WindowsKnownFolders _folders;
    private readonly ILogger<ServiceScanner> _logger;

    public ServiceScanner(WindowsKnownFolders folders, ILogger<ServiceScanner> logger)
    {
        _folders = folders;
        _logger = logger;
    }

    public string Name => nameof(ServiceScanner);

    public Task<ScanResult<ServiceRecord>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult<ServiceRecord> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress(ScanStages.Services));
        var items = new List<ServiceRecord>();
        var errors = new List<ScanError>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, State, StartMode, PathName, StartName, Description FROM Win32_Service");
            using var collection = searcher.Get();
            foreach (var item in collection)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (item)
                {
                    var name = item[nameof(ServiceRecord.Name)] as string;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var pathName = item["PathName"] as string;
                    items.Add(new ServiceRecord
                    {
                        Name = name,
                        DisplayName = item["DisplayName"] as string,
                        State = item["State"] as string,
                        StartMode = item["StartMode"] as string,
                        PathName = pathName,
                        ExecutablePath = CommandLine.ResolveServiceImagePath(pathName, _folders),
                        StartName = item["StartName"] as string,
                        Description = item["Description"] as string,
                    });
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TypeInitializationException)
        {
            _logger.LogWarning(ex, "Service enumeration failed");
            errors.Add(new ScanError(Name, "Win32_Service", ex.Message, ex));
        }

        _logger.LogInformation("Service scan found {Count} services", items.Count);
        return new ScanResult<ServiceRecord>(items, errors);
    }
}
