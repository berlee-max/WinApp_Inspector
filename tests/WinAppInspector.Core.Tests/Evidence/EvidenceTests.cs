using WinAppInspector.Core.Evidence;
using WinAppInspector.Core.Models;

namespace WinAppInspector.Core.Tests.Evidence;

public class EvidenceTests
{
    // §10.2 table
    [Theory]
    [InlineData(EvidenceKind.RegistryInstallLocationMatch, EvidenceWeight.High)]
    [InlineData(EvidenceKind.MainExecutableProductNameMatch, EvidenceWeight.High)]
    [InlineData(EvidenceKind.SignaturePublisherMatch, EvidenceWeight.High)]
    [InlineData(EvidenceKind.UninstallStringPointsToDirectory, EvidenceWeight.High)]
    [InlineData(EvidenceKind.ServiceExecutableInDirectory, EvidenceWeight.MediumHigh)]
    [InlineData(EvidenceKind.RunningProcessInDirectory, EvidenceWeight.MediumHigh)]
    [InlineData(EvidenceKind.StartupItemPointsToDirectory, EvidenceWeight.Medium)]
    [InlineData(EvidenceKind.ScheduledTaskPointsToDirectory, EvidenceWeight.Medium)]
    [InlineData(EvidenceKind.FolderNameSimilar, EvidenceWeight.Low)]
    [InlineData(EvidenceKind.RecentlyModified, EvidenceWeight.Auxiliary)]
    public void Weights_follow_the_requirements_table(EvidenceKind kind, EvidenceWeight expected)
    {
        EvidenceWeights.Of(kind).Should().Be(expected);
        new EvidenceItem(kind, "x").Weight.Should().Be(expected);
    }

    [Fact]
    public void Every_kind_has_a_weight()
    {
        foreach (var kind in Enum.GetValues<EvidenceKind>())
        {
            var act = () => EvidenceWeights.Of(kind);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void No_evidence_means_unknown_confidence()
    {
        ConfidenceCalculator.Score([]).Should().Be(0);
        ConfidenceCalculator.Level([]).Should().Be(ConfidenceLevel.Unknown);
    }

    [Fact]
    public void Single_high_evidence_gives_high_confidence()
    {
        var level = ConfidenceCalculator.Level([new EvidenceItem(EvidenceKind.RegistryInstallLocationMatch, @"C:\App")]);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Single_medium_evidence_gives_medium_confidence()
    {
        var level = ConfidenceCalculator.Level([new EvidenceItem(EvidenceKind.StartupItemPointsToDirectory, @"C:\App\app.exe")]);
        level.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public void Folder_name_alone_is_only_low_confidence()
    {
        var level = ConfidenceCalculator.Level([new EvidenceItem(EvidenceKind.FolderNameSimilar, "Foo")]);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Auxiliary_evidence_alone_never_exceeds_low()
    {
        var level = ConfidenceCalculator.Level([new EvidenceItem(EvidenceKind.RecentlyModified, "2026-01-01")]);
        level.Should().Be(ConfidenceLevel.Low);
    }

    [Fact]
    public void Medium_evidence_accumulates()
    {
        var level = ConfidenceCalculator.Level(
        [
            new EvidenceItem(EvidenceKind.StartupItemPointsToDirectory, "run"),
            new EvidenceItem(EvidenceKind.ScheduledTaskPointsToDirectory, "task"),
        ]);
        level.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public void Repeated_kinds_count_once()
    {
        var many = Enumerable.Range(0, 10)
            .Select(i => new EvidenceItem(EvidenceKind.RunningProcessInDirectory, $"proc {i}"))
            .ToArray();

        ConfidenceCalculator.Score(many).Should().Be((int)EvidenceWeight.MediumHigh);
        ConfidenceCalculator.Level(many).Should().Be(ConfidenceLevel.Medium);
    }
}
