using Microsoft.Extensions.Logging;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;

namespace WinAppInspector.Scanners.Appx;

/// <summary>A source of installed AppX / MSIX packages for the current user.</summary>
public interface IAppxPackageProvider
{
    string Name { get; }

    Task<IReadOnlyList<AppxPackageRecord>> GetPackagesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lists Store / UWP / MSIX packages (§7.2). Uses the WinRT <c>PackageManager</c> first and falls back to
/// PowerShell <c>Get-AppxPackage</c> when the runtime component is unavailable or fails.
/// </summary>
public sealed class AppxScanner : IScanner<AppxPackageRecord>
{
    private readonly IReadOnlyList<IAppxPackageProvider> _providers;
    private readonly ILogger<AppxScanner> _logger;

    public AppxScanner(IEnumerable<IAppxPackageProvider> providers, ILogger<AppxScanner> logger)
    {
        _providers = providers.ToArray();
        _logger = logger;
    }

    public string Name => nameof(AppxScanner);

    public async Task<ScanResult<AppxPackageRecord>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var errors = new List<ScanError>();
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgress(ScanStages.Appx, provider.Name));
            try
            {
                var packages = await provider.GetPackagesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("{Provider} returned {Count} packages", provider.Name, packages.Count);
                return new ScanResult<AppxPackageRecord>(packages, errors);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "{Provider} failed; trying the next provider", provider.Name);
                errors.Add(new ScanError(Name, provider.Name, ex.Message, ex));
            }
        }

        return new ScanResult<AppxPackageRecord>([], errors);
    }
}
