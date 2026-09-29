using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.Logging;

namespace WinAppInspector.Actions.Platform;

/// <summary>Registry existence check used by residue detection.</summary>
public sealed class RegistryKeyProbe : IRegistryKeyProbe
{
    public bool Exists(string fullKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullKeyPath);
        var idx = fullKeyPath.IndexOf('\\', StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        var hive = fullKeyPath[..idx].ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_USERS" or "HKU" => RegistryHive.Users,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
            _ => (RegistryHive?)null,
        };
        if (hive is null)
        {
            return false;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive.Value, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(fullKeyPath[(idx + 1)..], writable: false);
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary>Terminates a process after the user confirmed (§20.1 step 1, §25 结束进程).</summary>
public sealed class ProcessController : IProcessController
{
    private readonly IOperationLog _log;
    private readonly ILogger<ProcessController> _logger;

    public ProcessController(IOperationLog log, ILogger<ProcessController> logger)
    {
        _log = log;
        _logger = logger;
    }

    public async Task<(bool Succeeded, string? Error)> TerminateAsync(int processId, string name, bool userConfirmed, CancellationToken cancellationToken)
    {
        if (!userConfirmed)
        {
            return (false, "Not confirmed by the user.");
        }

        string? error = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            error = ex.Message;
            _logger.LogWarning(ex, "Process {Pid} could not be terminated", processId);
        }

        await _log.AppendAsync(new OperationLogEntry(DateTimeOffset.Now, OperationKind.TerminateProcess, $"{name} (PID {processId})",
            error is null ? OperationResult.Succeeded : OperationResult.Failed, error), cancellationToken).ConfigureAwait(false);
        return (error is null, error);
    }
}

/// <summary>Creates a restore point via WMI <c>SystemRestore.CreateRestorePoint</c> (§27). Requires administrator rights and System Protection enabled.</summary>
public sealed class RestorePointManager : IRestorePointManager
{
    private readonly IOperationLog _log;
    private readonly ILogger<RestorePointManager> _logger;

    public RestorePointManager(IOperationLog log, ILogger<RestorePointManager> logger)
    {
        _log = log;
        _logger = logger;
    }

    public async Task<(bool Succeeded, string? Error)> CreateAsync(string description, CancellationToken cancellationToken)
    {
        // The WMI call needs administrator rights; the app runs as a normal user (§28), so try it directly first (works when elevated)
        // and otherwise go through an elevated PowerShell so the UAC prompt appears for this one operation.
        var result = await Task.Run(() => Create(description), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && result.Error is not null && (result.Error.Contains("Access denied", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("拒绝", StringComparison.Ordinal)))
        {
            result = await CreateElevatedAsync(description, cancellationToken).ConfigureAwait(false);
        }

        await _log.AppendAsync(new OperationLogEntry(DateTimeOffset.Now, OperationKind.CreateRestorePoint, description,
            result.Succeeded ? OperationResult.Succeeded : OperationResult.Failed, result.Error), cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<(bool Succeeded, string? Error)> CreateElevatedAsync(string description, CancellationToken cancellationToken)
    {
        var safeDescription = description.Replace("'", "''", StringComparison.Ordinal);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '{safeDescription}' -RestorePointType APPLICATION_INSTALL -ErrorAction Stop\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, "PowerShell could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? (true, null)
                : (false, $"Checkpoint-Computer exited with code {process.ExitCode}. Windows creates at most one restore point per 24 hours unless SystemRestorePointCreationFrequency is changed, and System Protection must be enabled (§27).");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "The elevation prompt was declined.");
        }
    }

    private (bool Succeeded, string? Error) Create(string description)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            scope.Connect();
            using var systemRestore = new ManagementClass(scope, new ManagementPath("SystemRestore"), new ObjectGetOptions());
            using var parameters = systemRestore.GetMethodParameters("CreateRestorePoint");
            parameters["Description"] = description;
            parameters["RestorePointType"] = 0u;  // APPLICATION_INSTALL
            parameters["EventType"] = 100u;       // BEGIN_SYSTEM_CHANGE
            using var outcome = systemRestore.InvokeMethod("CreateRestorePoint", parameters, null);
            var code = Convert.ToUInt32(outcome["ReturnValue"], System.Globalization.CultureInfo.InvariantCulture);
            return code switch
            {
                0 => (true, null),
                1058 => (false, "System Protection is disabled for the system drive (§27: enable it in System Properties)."),
                5 => (false, "Access denied: creating a restore point requires administrator rights."),
                _ => (false, $"SystemRestore.CreateRestorePoint returned {code}."),
            };
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Restore point could not be created");
            return (false, ex.Message);
        }
    }
}
