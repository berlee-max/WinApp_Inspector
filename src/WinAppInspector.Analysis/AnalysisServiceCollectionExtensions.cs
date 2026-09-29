using Microsoft.Extensions.DependencyInjection;

namespace WinAppInspector.Analysis;

public static class AnalysisServiceCollectionExtensions
{
    /// <summary>Registers the attribution engine (ApplicationResolver, matchers, classifier). Filled in during phase 3.</summary>
    public static IServiceCollection AddWinAppInspectorAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
