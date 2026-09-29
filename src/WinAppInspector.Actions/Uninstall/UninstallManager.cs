using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Logging;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Actions.Uninstall;

/// <summary>
/// Runs the application's official uninstaller (§5.2, §20.1) in the order UninstallString → QuietUninstallString → MSI →
/// AppX → bundled uninstall.exe. Uninstallers are started through the shell so their own UAC prompt appears (§28).
/// Every run is written to the operation log (§25).
/// </summary>
public sealed class UninstallManager : IUninstallManager
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);
    private static readonly string[] BundledUninstallerNames = ["unins000.exe", "uninstall.exe", "uninst.exe", "uninstaller.exe", "Uninstall.exe", "setup.exe"];

    private readonly IOperationLog _log;
    private readonly ILogger<UninstallManager> _logger;

    public UninstallManager(IOperationLog log, ILogger<UninstallManager> logger)
    {
        _log = log;
        _logger = logger;
    }

    public async Task<UninstallResult> UninstallAsync(UninstallRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var app = request.Application;
        var watch = Stopwatch.StartNew();

        if (!request.UserConfirmed)
        {
            // §5.1: never without confirmation. This is a programming error in the caller, not a user-facing condition.
            return new UninstallResult(UninstallOutcome.NotConfirmed, UninstallMethod.None, null, null, "The uninstall was not confirmed by the user.", watch.Elapsed);
        }

        if (app.AppType.IsProtectedByDefault())
        {
            // §9.7–9.9: system components, drivers and shared runtimes are kept; no caller may run their uninstaller through this tool.
            await LogAsync(app, UninstallMethod.None, OperationResult.Skipped, $"Protected type {app.AppType}.", cancellationToken).ConfigureAwait(false);
            return new UninstallResult(UninstallOutcome.Failed, UninstallMethod.None, null, null, $"{app.AppType} is kept by default and is not uninstalled by this tool.", watch.Elapsed);
        }

        var (method, command) = ChooseRoute(app, request.PreferQuiet);
        if (method == UninstallMethod.None)
        {
            await LogAsync(app, UninstallMethod.None, OperationResult.Skipped, "No official uninstaller.", cancellationToken).ConfigureAwait(false);
            return new UninstallResult(UninstallOutcome.NoUninstaller, UninstallMethod.None, null, null, null, watch.Elapsed);
        }

        progress?.Report(command ?? method.ToString());
        _logger.LogInformation("Uninstalling {App} via {Method}: {Command}", app.Name, method, command);

        UninstallResult result;
        try
        {
            result = method == UninstallMethod.Appx
                ? await RemovePackageAsync(app, watch, cancellationToken).ConfigureAwait(false)
                : await RunCommandAsync(method, command!, watch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await LogAsync(app, method, OperationResult.Cancelled, null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // §34: the concrete reason (missing uninstaller, access denied, deployment HRESULT, ...) reaches the user.
            result = new UninstallResult(UninstallOutcome.Failed, method, command, ex.HResult, ex.Message, watch.Elapsed);
        }

        await LogAsync(app, method, result.Succeeded ? OperationResult.Succeeded : result.Outcome == UninstallOutcome.CancelledByUser ? OperationResult.Cancelled : OperationResult.Failed,
            result.Error ?? (result.ExitCode is { } code ? $"exit code {code}" : null), cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Which route and command will be used, so the UI can show it before asking for confirmation (§5.1).</summary>
    public static (UninstallMethod Method, string? Command) ChooseRoute(ApplicationEntity app, bool preferQuiet)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (preferQuiet && !string.IsNullOrWhiteSpace(app.QuietUninstallCommand))
        {
            return (UninstallMethod.QuietUninstallString, app.QuietUninstallCommand);
        }

        if (app.MsiProductCode is not null)
        {
            return (UninstallMethod.Msi, $"msiexec.exe /x {app.MsiProductCode}" + (preferQuiet ? " /qb-" : string.Empty));
        }

        if (!string.IsNullOrWhiteSpace(app.UninstallCommand))
        {
            return (UninstallMethod.UninstallString, app.UninstallCommand);
        }

        if (!string.IsNullOrWhiteSpace(app.QuietUninstallCommand))
        {
            return (UninstallMethod.QuietUninstallString, app.QuietUninstallCommand);
        }

        if (app.AppxPackageFullName is not null)
        {
            return (UninstallMethod.Appx, app.AppxPackageFullName);
        }

        var bundled = FindBundledUninstaller(app);
        return bundled is null ? (UninstallMethod.None, null) : (UninstallMethod.BundledUninstaller, $"\"{bundled}\"");
    }

    private static string? FindBundledUninstaller(ApplicationEntity app)
    {
        foreach (var directory in app.Directories.Where(d => d.Role == DirectoryRole.Program))
        {
            foreach (var name in BundledUninstallerNames)
            {
                var candidate = Path.Combine(directory.Path, name);
                if (File.Exists(candidate) && !name.Equals("setup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static async Task<UninstallResult> RunCommandAsync(UninstallMethod method, string command, Stopwatch watch, CancellationToken cancellationToken)
    {
        var exe = CommandLine.ExtractExecutable(command);
        var args = CommandLine.ExtractArguments(command);
        if (exe is null)
        {
            return new UninstallResult(UninstallOutcome.Failed, method, command, null, "The uninstall command has no executable.", watch.Elapsed);
        }

        if (exe.Contains(WindowsPath.Separator, StringComparison.Ordinal) && !File.Exists(exe))
        {
            // §34 卸载器不存在
            return new UninstallResult(UninstallOutcome.Failed, method, command, null, $"Uninstaller not found: {exe}", watch.Elapsed);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args ?? string.Empty,
            UseShellExecute = true, // lets the uninstaller request elevation itself (§28)
            WorkingDirectory = WindowsPath.GetDirectoryName(exe) is { } dir && Directory.Exists(dir) ? dir : string.Empty,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new UninstallResult(UninstallOutcome.Failed, method, command, null, "The uninstaller process could not be started.", watch.Elapsed);
        }

        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new UninstallResult(UninstallOutcome.Failed, method, command, null, "The uninstaller did not finish within 30 minutes.", watch.Elapsed);
        }

        var exitCode = process.ExitCode;
        var outcome = method == UninstallMethod.Msi ? InterpretMsiExitCode(exitCode) : exitCode == 0 ? UninstallOutcome.Succeeded : UninstallOutcome.Failed;

        // Many InnoSetup / NSIS uninstallers copy themselves to %TEMP% and exit 0 immediately; the copy keeps running.
        // The result is still reported honestly: exit code plus whatever the residue rescan finds afterwards.
        return new UninstallResult(outcome, method, command, exitCode, outcome == UninstallOutcome.Failed ? $"Uninstaller exited with code {exitCode}." : null, watch.Elapsed);
    }

    private static UninstallOutcome InterpretMsiExitCode(int exitCode) => exitCode switch
    {
        0 => UninstallOutcome.Succeeded,
        3010 or 1641 => UninstallOutcome.SucceededRestartRequired,
        1602 => UninstallOutcome.CancelledByUser,
        _ => UninstallOutcome.Failed,
    };

    private static async Task<UninstallResult> RemovePackageAsync(ApplicationEntity app, Stopwatch watch, CancellationToken cancellationToken)
    {
        var fullName = app.AppxPackageFullName!;
        var manager = new Windows.Management.Deployment.PackageManager();
        try
        {
            // A failed deployment surfaces as a faulted task (COMException with the deployment HRESULT), not as a result object.
            var result = await manager.RemovePackageAsync(fullName).AsTask(cancellationToken).ConfigureAwait(false);
            if (result.ExtendedErrorCode is null)
            {
                return new UninstallResult(UninstallOutcome.Succeeded, UninstallMethod.Appx, fullName, 0, null, watch.Elapsed);
            }

            var error = string.IsNullOrWhiteSpace(result.ErrorText) ? result.ExtendedErrorCode.Message : result.ErrorText;
            return new UninstallResult(UninstallOutcome.Failed, UninstallMethod.Appx, fullName, result.ExtendedErrorCode.HResult, error, watch.Elapsed);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            var message = ex.HResult switch
            {
                unchecked((int)0x80073CFA) => "The package is not installed for the current user.",
                unchecked((int)0x80073D19) => "The package is marked as non-removable by the system.",
                unchecked((int)0x80073CF1) => "The package was not found.",
                _ => ex.Message,
            };
            return new UninstallResult(UninstallOutcome.Failed, UninstallMethod.Appx, fullName, ex.HResult, message, watch.Elapsed);
        }
    }

    private Task LogAsync(ApplicationEntity app, UninstallMethod method, OperationResult result, string? error, CancellationToken cancellationToken) =>
        _log.AppendAsync(new OperationLogEntry(DateTimeOffset.Now, OperationKind.Uninstall, $"{app.Name} [{method}]", result, error), cancellationToken);
}
