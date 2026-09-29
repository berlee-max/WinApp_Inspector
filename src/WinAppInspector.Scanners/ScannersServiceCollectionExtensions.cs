using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WinAppInspector.Core.Models;
using WinAppInspector.Core.Scanning;
using WinAppInspector.Scanners.Appx;
using WinAppInspector.Scanners.Directories;
using WinAppInspector.Scanners.Executables;
using WinAppInspector.Scanners.Processes;
using WinAppInspector.Scanners.Registry;
using WinAppInspector.Scanners.Services;
using WinAppInspector.Scanners.Startup;
using WinAppInspector.Scanners.Tasks;

namespace WinAppInspector.Scanners;

public static class ScannersServiceCollectionExtensions
{
    /// <summary>Registers the Windows data-source scanners (§7) and readers. Requires <c>AddWinAppInspectorCore</c>.</summary>
    public static IServiceCollection AddWinAppInspectorScanners(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(ScanOptions.Default);

        services.AddSingleton<IExecutableMetadataReader, ExeMetadataReader>();
        services.AddSingleton<ISignatureReader, SignatureReader>();
        services.AddSingleton<IShortcutResolver, ShortcutResolver>();
        services.AddSingleton<IDirectorySizeCalculator, DirectorySizeCalculator>();

        // Order matters: the first provider that succeeds wins.
        services.AddSingleton<IAppxPackageProvider, PackageManagerAppxProvider>();
        services.AddSingleton<IAppxPackageProvider, PowerShellAppxProvider>();

        services.AddSingleton<RegistryScanner>();
        services.AddSingleton<IScanner<RegistryUninstallEntry>>(sp => sp.GetRequiredService<RegistryScanner>());
        services.AddSingleton<AppxScanner>();
        services.AddSingleton<IScanner<AppxPackageRecord>>(sp => sp.GetRequiredService<AppxScanner>());
        services.AddSingleton<DirectoryScanner>();
        services.AddSingleton<IScanner<AppDirectory>>(sp => sp.GetRequiredService<DirectoryScanner>());
        services.AddSingleton<ProcessScanner>();
        services.AddSingleton<IScanner<ProcessRecord>>(sp => sp.GetRequiredService<ProcessScanner>());
        services.AddSingleton<ServiceScanner>();
        services.AddSingleton<IScanner<ServiceRecord>>(sp => sp.GetRequiredService<ServiceScanner>());
        services.AddSingleton<StartupScanner>();
        services.AddSingleton<IScanner<StartupItemRecord>>(sp => sp.GetRequiredService<StartupScanner>());
        services.AddSingleton<ShortcutScanner>();
        services.AddSingleton<IScanner<ShortcutRecord>>(sp => sp.GetRequiredService<ShortcutScanner>());
        services.AddSingleton<ScheduledTaskScanner>();
        services.AddSingleton<IScanner<ScheduledTaskRecord>>(sp => sp.GetRequiredService<ScheduledTaskScanner>());

        return services;
    }
}
