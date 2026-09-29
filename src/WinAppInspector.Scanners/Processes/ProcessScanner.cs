using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Processes;

/// <summary>
/// Lists running processes (§7.5). WMI <c>Win32_Process</c> gives image path, command line and parent in one query
/// without touching each process; the <see cref="Process"/> API is the fallback. CompanyName / ProductName come from the version resource.
/// </summary>
public sealed class ProcessScanner : IScanner<ProcessRecord>
{
    private readonly IExecutableMetadataReader _metadataReader;
    private readonly ILogger<ProcessScanner> _logger;

    public ProcessScanner(IExecutableMetadataReader metadataReader, ILogger<ProcessScanner> logger)
    {
        _metadataReader = metadataReader;
        _logger = logger;
    }

    public string Name => nameof(ProcessScanner);

    public Task<ScanResult<ProcessRecord>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult<ProcessRecord> Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress(ScanStages.Processes));
        var errors = new List<ScanError>();
        List<ProcessRecord> processes;

        try
        {
            processes = ScanWithWmi(cancellationToken);
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TypeInitializationException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "WMI process enumeration failed; falling back to the Process API");
            errors.Add(new ScanError(Name, "Win32_Process", ex.Message, ex));
            processes = ScanWithProcessApi(cancellationToken);
        }

        var metadataCache = new Dictionary<string, ExecutableMetadata?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < processes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = processes[i];
            if (p.ExecutablePath is null)
            {
                continue;
            }

            if (!metadataCache.TryGetValue(p.ExecutablePath, out var metadata))
            {
                metadata = TryReadMetadata(p.ExecutablePath);
                metadataCache[p.ExecutablePath] = metadata;
            }

            if (metadata is not null)
            {
                processes[i] = p with { CompanyName = metadata.CompanyName, ProductName = metadata.ProductName };
            }
        }

        _logger.LogInformation("Process scan found {Count} processes", processes.Count);
        return new ScanResult<ProcessRecord>(processes, errors);
    }

    private static List<ProcessRecord> ScanWithWmi(CancellationToken cancellationToken)
    {
        var result = new List<ProcessRecord>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
        using var collection = searcher.Get();
        foreach (var item in collection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (item)
            {
                var pid = Convert.ToInt32(item["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                var parent = item["ParentProcessId"] is { } pp ? Convert.ToInt32(pp, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
                var path = item["ExecutablePath"] as string;
                result.Add(new ProcessRecord
                {
                    ProcessId = pid,
                    Name = item[nameof(ProcessRecord.Name)] as string ?? pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ExecutablePath = string.IsNullOrWhiteSpace(path) ? null : WindowsPath.Normalize(path),
                    CommandLine = item["CommandLine"] as string,
                    ParentProcessId = parent,
                });
            }
        }

        return result;
    }

    private static List<ProcessRecord> ScanWithProcessApi(CancellationToken cancellationToken)
    {
        var result = new List<ProcessRecord>();
        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Protected or exited process: image path stays unknown.
                }

                result.Add(new ProcessRecord
                {
                    ProcessId = process.Id,
                    Name = process.ProcessName + ".exe",
                    ExecutablePath = path is null ? null : WindowsPath.Normalize(path),
                });
            }
        }

        return result;
    }

    private ExecutableMetadata? TryReadMetadata(string path)
    {
        try
        {
            return _metadataReader.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogDebug(ex, "Version resource of {Path} could not be read", path);
            return null;
        }
    }
}
