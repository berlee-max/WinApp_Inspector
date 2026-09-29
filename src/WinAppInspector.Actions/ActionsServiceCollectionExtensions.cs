using Microsoft.Extensions.DependencyInjection;

namespace WinAppInspector.Actions;

public static class ActionsServiceCollectionExtensions
{
    /// <summary>Registers UninstallManager / CleanupManager / RestorePointManager. Filled in during phases 5–7.</summary>
    public static IServiceCollection AddWinAppInspectorActions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
