using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Evidence;

/// <summary>One piece of attribution evidence with a machine-readable kind and a human-readable detail (e.g. the matching path).</summary>
public sealed record EvidenceItem(EvidenceKind Kind, string Detail)
{
    public EvidenceWeight Weight => EvidenceWeights.Of(Kind);
    public int Score => (int)Weight;
}

/// <summary>The §10.2 weight table.</summary>
public static class EvidenceWeights
{
    public static EvidenceWeight Of(EvidenceKind kind) => kind switch
    {
        EvidenceKind.RegistryInstallLocationMatch => EvidenceWeight.High,
        EvidenceKind.MainExecutableProductNameMatch => EvidenceWeight.High,
        EvidenceKind.SignaturePublisherMatch => EvidenceWeight.High,
        EvidenceKind.UninstallStringPointsToDirectory => EvidenceWeight.High,
        EvidenceKind.ServiceExecutableInDirectory => EvidenceWeight.MediumHigh,
        EvidenceKind.RunningProcessInDirectory => EvidenceWeight.MediumHigh,
        EvidenceKind.StartupItemPointsToDirectory => EvidenceWeight.Medium,
        EvidenceKind.ScheduledTaskPointsToDirectory => EvidenceWeight.Medium,
        EvidenceKind.ShortcutPointsToDirectory => EvidenceWeight.Medium,
        EvidenceKind.ExecutableCompanyNameMatch => EvidenceWeight.Medium,
        EvidenceKind.FolderNameSimilar => EvidenceWeight.Low,
        EvidenceKind.RecentlyModified => EvidenceWeight.Auxiliary,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown evidence kind."),
    };
}

/// <summary>
/// Turns a set of evidence into a <see cref="ConfidenceLevel"/>. The score is internal (§10.2: "not necessarily shown as a percentage").
/// </summary>
public static class ConfidenceCalculator
{
    /// <summary>A single High evidence item is enough for high confidence.</summary>
    public const int HighThreshold = (int)EvidenceWeight.High;

    /// <summary>A single Medium evidence item yields medium confidence.</summary>
    public const int MediumThreshold = (int)EvidenceWeight.Medium;

    public static int Score(IEnumerable<EvidenceItem> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        // Each kind counts once: ten running processes in a directory are not ten times the evidence of one.
        return evidence.GroupBy(e => e.Kind).Sum(g => g.First().Score);
    }

    public static ConfidenceLevel Level(int score) => score switch
    {
        <= 0 => ConfidenceLevel.Unknown,
        >= HighThreshold => ConfidenceLevel.High,
        >= MediumThreshold => ConfidenceLevel.Medium,
        _ => ConfidenceLevel.Low,
    };

    public static ConfidenceLevel Level(IEnumerable<EvidenceItem> evidence) => Level(Score(evidence));
}
