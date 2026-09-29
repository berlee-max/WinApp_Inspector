using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Parsing;

namespace WinAppInspector.Scanners.Appx;

/// <summary>Primary AppX source: the WinRT deployment API, current user only (no elevation, §28).</summary>
public sealed class PackageManagerAppxProvider : IAppxPackageProvider
{
    private readonly ILogger<PackageManagerAppxProvider> _logger;

    public PackageManagerAppxProvider(ILogger<PackageManagerAppxProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "PackageManager";

    public Task<IReadOnlyList<AppxPackageRecord>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<AppxPackageRecord>>(() =>
        {
            var manager = new PackageManager();
            var result = new List<AppxPackageRecord>();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = TryConvert(package);
                if (record is not null)
                {
                    result.Add(record);
                }
            }

            return result;
        }, cancellationToken);
    }

    private AppxPackageRecord? TryConvert(Package package)
    {
        try
        {
            var id = package.Id;
            var publisher = id.Publisher;
            var version = id.Version;
            return new AppxPackageRecord
            {
                Name = id.Name,
                PackageFullName = id.FullName,
                PackageFamilyName = id.FamilyName,
                DisplayName = TryGet(() => package.DisplayName),
                Publisher = publisher,
                PublisherDisplayName = TryGet(() => package.PublisherDisplayName) ?? DistinguishedName.GetCommonName(publisher),
                Version = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                InstallLocation = TryGet(() => WindowsPath.Normalize(package.InstalledLocation.Path)),
                Architecture = id.Architecture.ToString(),
                IsFramework = package.IsFramework,
                NonRemovable = null,
                IsBundle = package.IsBundle,
                SignatureKind = TryGet(() => package.SignatureKind.ToString()),
                // Already on a pool thread (Task.Run above), so the async projection is waited synchronously.
                AppUserModelId = TryGet(() => FirstAppUserModelId(package)),
            };
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // Staged / broken packages can throw on almost any property; skip them (§34: the error is logged with its cause).
            _logger.LogDebug(ex, "Skipping a package that could not be read");
            return null;
        }
    }

    private static string? FirstAppUserModelId(Package package)
    {
        var entries = package.GetAppListEntriesAsync().AsTask().GetAwaiter().GetResult();
        return entries.Count > 0 ? entries[0].AppUserModelId : null;
    }

    private static string? TryGet(Func<string?> getter)
    {
        try
        {
            var value = getter();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }
}
