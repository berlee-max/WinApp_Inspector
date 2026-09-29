using Microsoft.Extensions.DependencyInjection;
using WinAppInspector.Core.IO;
using WinAppInspector.Core.Rules;

namespace WinAppInspector.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the platform-neutral rules. <paramref name="folders"/> defaults to the real Windows folders at runtime.</summary>
    public static IServiceCollection AddWinAppInspectorCore(this IServiceCollection services, WindowsKnownFolders? folders = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ => folders ?? (OperatingSystem.IsWindows()
            ? WindowsKnownFolders.FromEnvironment()
            : WindowsKnownFolders.CreateDefault()));
        services.AddSingleton<ProtectedPathRule>();
        services.AddSingleton<DeletionGuard>();
        return services;
    }
}
