using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WinAppInspector.Actions.Cleanup;
using WinAppInspector.Actions.Logging;
using WinAppInspector.Actions.Residue;
using WinAppInspector.Actions.Platform;
using WinAppInspector.Actions.Uninstall;
using WinAppInspector.Core.Actions;
using WinAppInspector.Core.Logging;

namespace WinAppInspector.Actions;

public static class ActionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers UninstallManager, CleanupManager, ResidueScanner, ProcessController, RestorePointManager and the file-based
    /// operation log (§25) written to <paramref name="dataDirectory"/>. Requires Core and Scanners registrations.
    /// </summary>
    public static IServiceCollection AddWinAppInspectorActions(this IServiceCollection services, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        services.TryAddSingleton<IOperationLog>(sp => new FileOperationLog(dataDirectory, sp.GetRequiredService<ILogger<FileOperationLog>>()));
        services.AddSingleton<IRegistryKeyProbe, RegistryKeyProbe>();
        services.AddSingleton<IUninstallManager, UninstallManager>();
        services.AddSingleton<ICleanupManager, CleanupManager>();
        services.AddSingleton<IResidueScanner, ResidueScanner>();
        services.AddSingleton<IProcessController, ProcessController>();
        services.AddSingleton<IRestorePointManager, RestorePointManager>();
        return services;
    }
}
