using Microsoft.Extensions.DependencyInjection;
using WinAppInspector.Analysis.Classification;
using WinAppInspector.Analysis.Matching;
using WinAppInspector.Analysis.Resolution;

namespace WinAppInspector.Analysis;

public static class AnalysisServiceCollectionExtensions
{
    /// <summary>Registers the attribution engine: matchers, catalog, classifier, residue detector, risk assessor and resolver.</summary>
    public static IServiceCollection AddWinAppInspectorAnalysis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<PublisherMatcher>();
        services.AddSingleton<DirectoryMatcher>();
        services.AddSingleton<ExecutableMatcher>();
        services.AddSingleton<KnownComponentCatalog>();
        services.AddSingleton<ResidueDetector>();
        services.AddSingleton<AppClassifier>();
        services.AddSingleton<RiskAssessor>();
        services.AddSingleton<IApplicationResolver, ApplicationResolver>();
        return services;
    }
}
