using WinAppInspector.Analysis.Resolution;
using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Analysis.Classification;

/// <summary>Derives the internal <see cref="RiskLevel"/> (§23) from type, confidence and runtime state. Not a user-facing score (§29).</summary>
public sealed class RiskAssessor
{
    public RiskLevel Assess(EntityDraft draft, AppType type, ConfidenceLevel confidence, List<Reason> reasons)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(reasons);

        if (type.IsProtectedByDefault())
        {
            return RiskLevel.Protected;
        }

        if (type == AppType.Undetermined)
        {
            return RiskLevel.High;
        }

        if (confidence is ConfidenceLevel.Unknown or ConfidenceLevel.Low)
        {
            reasons.Add(new Reason(ReasonKind.LowAttributionConfidence, confidence.ToString()));
            return RiskLevel.High;
        }

        if (draft.Processes.Count > 0 || draft.Services.Any(s => string.Equals(s.State, "Running", StringComparison.OrdinalIgnoreCase)))
        {
            return RiskLevel.Medium;
        }

        return type switch
        {
            AppType.ThirdPartyInstalled or AppType.StoreApp => draft.HasOfficialUninstaller ? RiskLevel.Low : RiskLevel.Medium,
            AppType.UserLevel => draft.HasOfficialUninstaller ? RiskLevel.Low : RiskLevel.Medium,
            AppType.Portable => RiskLevel.Medium,
            AppType.SuspectedResidue or AppType.AppCache => confidence == ConfidenceLevel.High ? RiskLevel.Low : RiskLevel.Medium,
            _ => RiskLevel.Medium,
        };
    }
}
