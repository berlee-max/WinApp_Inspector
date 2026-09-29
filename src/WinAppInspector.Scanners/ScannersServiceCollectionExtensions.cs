using Microsoft.Extensions.DependencyInjection;

namespace WinAppInspector.Scanners;

public static class ScannersServiceCollectionExtensions
{
    /// <summary>Registers the Windows data-source scanners (§7). Filled in during phase 2.</summary>
    public static IServiceCollection AddWinAppInspectorScanners(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
