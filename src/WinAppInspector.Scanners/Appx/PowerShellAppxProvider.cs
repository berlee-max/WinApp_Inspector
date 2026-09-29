using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Scanners.Appx;

/// <summary>Fallback AppX source: Windows PowerShell <c>Get-AppxPackage</c> serialised to JSON.</summary>
public sealed class PowerShellAppxProvider : IAppxPackageProvider
{
    // Enum and Version members are stringified so the JSON is stable across PowerShell versions.
    private const string Script =
        "Get-AppxPackage | Select-Object Name, PackageFullName, PackageFamilyName, Publisher, InstallLocation, IsFramework, NonRemovable, IsBundle, " +
        "@{n='Version';e={$_.Version.ToString()}}, @{n='Architecture';e={$_.Architecture.ToString()}}, @{n='SignatureKind';e={$_.SignatureKind.ToString()}} " +
        "| ConvertTo-Json -Compress -Depth 2";

    private readonly ILogger<PowerShellAppxProvider> _logger;

    public PowerShellAppxProvider(ILogger<PowerShellAppxProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "Get-AppxPackage";

    public async Task<IReadOnlyList<AppxPackageRecord>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-OutputFormat");
        startInfo.ArgumentList.Add("Text");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + Script);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("powershell.exe could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Get-AppxPackage exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogDebug("Get-AppxPackage wrote to stderr: {Stderr}", stderr.Trim());
        }

        return AppxPackageJsonParser.Parse(stdout);
    }
}
